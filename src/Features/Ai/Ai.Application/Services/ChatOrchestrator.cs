using System.Runtime.CompilerServices;
using Ai.Application.Exceptions;
using Ai.Application.Interfaces;
using Ai.Application.Models;
using Ai.Domain.Models;
using Microsoft.Extensions.DependencyInjection;
using Shared.Application;

namespace Ai.Application.Services;

/// <summary>
/// Entry points for the Ai chat feature. All of them feed the same <see cref="ConversationRunner"/>:
/// <see cref="SendUserMessageAsync"/> starts a normal turn, <see cref="ResolveToolApprovalAsync"/>
/// resumes one after a gated (write/destructive) tool call was approved or denied,
/// <see cref="AnswerQuestionAsync"/> after a question was answered, and <see cref="CancelSubAgentAsync"/>
/// after the user abandoned a running sub-agent.
/// </summary>
public sealed class ChatOrchestrator(
    IConversationRepository conversations,
    IToolExecutionRepository toolExecutions,
    IUploadedImageRepository uploadedImages,
    [FromKeyedServices(AiKeys.UnitOfWork)] IUnitOfWork unitOfWork,
    IChatCompactionService compaction,
    IAiUserProvisioningService aiUserProvisioning,
    GatedToolApprovalResolver approvalResolver,
    ConversationRunner runner,
    IAiToolRegistry toolRegistry,
    IEventTracker eventTracker)
{
    public async IAsyncEnumerable<AiChatEvent> SendUserMessageAsync(
        Guid userId,
        Guid? conversationId,
        string userMessage,
        IReadOnlyList<Guid>? imageIds,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // The one point of contact: every other Ai entity FKs to AiUser, so it must exist before
        // GetOrCreateConversationAsync (or anything downstream) can insert a row that references it.
        await aiUserProvisioning.EnsureExistsAsync(userId, cancellationToken);

        var conversation = await GetOrCreateConversationAsync(userId, conversationId, userMessage, cancellationToken);
        if (conversation.Kind != ConversationKinds.Chat)
        {
            yield return new AiChatError("This conversation belongs to a sub-agent and cannot be messaged directly.");
            yield break;
        }

        // A gated tool call from a previous turn leaves a tool_use content block in live history with
        // no matching tool_result until it's resolved. Sending a new message on top of that would send
        // Anthropic an invalid message list (400: tool_use without a following tool_result) and, since
        // the new user message is about to be persisted, permanently brick the conversation - every
        // later send would hit the exact same dangling tool_use and fail the same way. Reject up front
        // instead so the client gets an actionable error and nothing bad gets committed.
        if (await toolExecutions.HasOutstandingRequestAsync(userId, conversation.Id, cancellationToken))
        {
            yield return new AiChatError("This conversation has a tool request awaiting approval, an answer, or a sub-agent to finish - resolve it before sending a new message.");
            yield break;
        }

        List<ImageContentBlock>? imageBlocks = null;
        if (imageIds is { Count: > 0 })
        {
            imageBlocks = new List<ImageContentBlock>(imageIds.Count);
            foreach (var imageId in imageIds)
            {
                var image = await uploadedImages.FindAsync(userId, imageId, cancellationToken);
                if (image is null || image.Status != UploadedImageStatus.Confirmed)
                {
                    yield return new AiChatError($"Image '{imageId}' is not a confirmed upload for this account.");
                    yield break;
                }

                imageBlocks.Add(new ImageContentBlock(image.ObjectKey, image.ContentType));
            }
        }

        var sequenceNumber = await conversations.GetNextSequenceNumberAsync(conversation.Id, cancellationToken);
        conversations.AddMessage(ConversationMessageFactory.CreateUserMessage(conversation.Id, sequenceNumber, userMessage, imageBlocks));
        conversation.LastMessageAt = DateTimeOffset.UtcNow;
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await eventTracker.TrackAsync(userId, EventTypes.AiMessageSent, null, cancellationToken);

        await compaction.CompactIfNeededAsync(conversation.Id, userId, cancellationToken);

        await foreach (var chatEvent in runner.RunAsync(userId, conversation.Id, cancellationToken))
        {
            yield return chatEvent;
        }
    }

    public async IAsyncEnumerable<AiChatEvent> ResolveToolApprovalAsync(
        Guid userId,
        Guid toolExecutionRequestId,
        bool approve,
        string? denialReason,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var request = await toolExecutions.FindAsync(userId, toolExecutionRequestId, cancellationToken);
        if (request is null || request.Status != ToolExecutionStatus.PendingApproval)
        {
            yield return new AiChatError("This request is no longer pending approval.");
            yield break;
        }

        var interaction = toolRegistry.Find(request.ToolName)?.Interaction;
        if (interaction == ToolInteractionKind.Question)
        {
            yield return new AiChatError("This request is a question - resolve it via the answer endpoint, not approve/deny.");
            yield break;
        }

        if (interaction == ToolInteractionKind.SubAgent)
        {
            yield return new AiChatError("This request is a running sub-agent - answer its questions, or cancel it via the cancel endpoint.");
            yield break;
        }

        var resolution = new ToolApprovalResolution();
        await foreach (var chatEvent in approvalResolver.ResolveAsync(userId, request, approve, denialReason, resolution, cancellationToken))
        {
            yield return chatEvent;
        }

        if (resolution.ReadyConversationId is not { } readyConversationId)
        {
            // Sibling gated calls from the same turn are still pending.
            yield return new AiChatPermissionResolved(request.ConversationId, request.Id, approve);
            yield break;
        }

        await foreach (var chatEvent in runner.RunAsync(userId, readyConversationId, cancellationToken))
        {
            yield return chatEvent;
        }
    }

    public async IAsyncEnumerable<AiChatEvent> AnswerQuestionAsync(
        Guid userId,
        Guid toolExecutionRequestId,
        IReadOnlyList<string> answers,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var request = await toolExecutions.FindAsync(userId, toolExecutionRequestId, cancellationToken);
        if (request is null || request.Status != ToolExecutionStatus.PendingApproval)
        {
            yield return new AiChatError("This request is no longer pending approval.");
            yield break;
        }

        if (toolRegistry.Find(request.ToolName)?.Interaction != ToolInteractionKind.Question)
        {
            yield return new AiChatError("This request is not a question - resolve it via approve/deny, not the answer endpoint.");
            yield break;
        }

        var resolution = new ToolApprovalResolution();
        await foreach (var chatEvent in approvalResolver.AnswerAsync(userId, request, answers, resolution, cancellationToken))
        {
            yield return chatEvent;
        }

        if (resolution.ReadyConversationId is not { } readyConversationId)
        {
            // Sibling gated calls from the same turn are still pending.
            yield break;
        }

        await foreach (var chatEvent in runner.RunAsync(userId, readyConversationId, cancellationToken))
        {
            yield return chatEvent;
        }
    }

    /// <summary>The user abandoned a running sub-agent (e.g. walked away from an interview): settles its tool call as declined so the chat is unblocked.</summary>
    public async IAsyncEnumerable<AiChatEvent> CancelSubAgentAsync(
        Guid userId,
        Guid toolExecutionRequestId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var request = await toolExecutions.FindAsync(userId, toolExecutionRequestId, cancellationToken);
        if (request is null || request.Status != ToolExecutionStatus.InProgress
            || toolRegistry.Find(request.ToolName)?.Interaction != ToolInteractionKind.SubAgent)
        {
            yield return new AiChatError("This request is not a running sub-agent.");
            yield break;
        }

        var resolution = new ToolApprovalResolution();
        await foreach (var chatEvent in approvalResolver.CancelSubAgentAsync(userId, request, resolution, cancellationToken))
        {
            yield return chatEvent;
        }

        if (resolution.ReadyConversationId is not { } readyConversationId)
        {
            yield break;
        }

        await foreach (var chatEvent in runner.RunAsync(userId, readyConversationId, cancellationToken))
        {
            yield return chatEvent;
        }
    }

    private async Task<Conversation> GetOrCreateConversationAsync(
        Guid userId, Guid? conversationId, string initialMessage, CancellationToken cancellationToken)
    {
        if (conversationId is { } id)
        {
            return await conversations.FindAsync(userId, id, cancellationToken)
                ?? throw new ConversationAccessDeniedException(id);
        }

        var conversation = new Conversation
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Title = ConversationTitleGenerator.Generate(initialMessage),
            CreatedAt = DateTimeOffset.UtcNow,
            LastMessageAt = DateTimeOffset.UtcNow,
        };

        conversations.Add(conversation);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return conversation;
    }
}
