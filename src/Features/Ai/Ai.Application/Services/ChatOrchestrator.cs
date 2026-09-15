using System.Runtime.CompilerServices;
using Ai.Application.Exceptions;
using Ai.Application.Interfaces;
using Ai.Application.Models;
using Ai.Domain.Models;
using Microsoft.Extensions.DependencyInjection;
using Shared.Application;

namespace Ai.Application.Services;

/// <summary>
/// Entry points for the Ai chat feature. Both feed the same <see cref="ChatTurnLoop"/>:
/// <see cref="SendUserMessageAsync"/> starts a normal turn, <see cref="ResolveToolApprovalAsync"/>
/// resumes one after a gated (write/destructive) tool call was approved or denied.
/// </summary>
public sealed class ChatOrchestrator(
    IConversationRepository conversations,
    IToolExecutionRepository toolExecutions,
    IUploadedImageRepository uploadedImages,
    [FromKeyedServices(AiKeys.UnitOfWork)] IUnitOfWork unitOfWork,
    IChatCompactionService compaction,
    IAiUserProvisioningService aiUserProvisioning,
    GatedToolApprovalResolver approvalResolver,
    ChatTurnLoop turnLoop)
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

        // A gated tool call from a previous turn leaves a tool_use content block in live history with
        // no matching tool_result until it's resolved. Sending a new message on top of that would send
        // Anthropic an invalid message list (400: tool_use without a following tool_result) and, since
        // the new user message is about to be persisted, permanently brick the conversation - every
        // later send would hit the exact same dangling tool_use and fail the same way. Reject up front
        // instead so the client gets an actionable error and nothing bad gets committed.
        var pending = await toolExecutions.GetPendingForConversationAsync(userId, conversation.Id, cancellationToken);
        if (pending.Count > 0)
        {
            yield return new AiChatError("This conversation has a tool request awaiting approval - resolve it before sending a new message.");
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

        await compaction.CompactIfNeededAsync(conversation.Id, userId, cancellationToken);

        await foreach (var chatEvent in turnLoop.RunAsync(userId, conversation.Id, cancellationToken))
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

        var readyConversationId = await approvalResolver.ResolveAsync(userId, request, approve, denialReason, cancellationToken);
        if (readyConversationId is null)
        {
            // Sibling gated calls from the same turn are still pending.
            yield return new AiChatPermissionResolved(request.ConversationId, request.Id, approve);
            yield break;
        }

        await foreach (var chatEvent in turnLoop.RunAsync(userId, readyConversationId.Value, cancellationToken))
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
