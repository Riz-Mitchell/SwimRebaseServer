using System.Runtime.CompilerServices;
using Ai.Application.Exceptions;
using Ai.Application.Interfaces;
using Ai.Application.Models;
using Ai.Domain.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
    IEventTracker eventTracker,
    IConversationTitler titler,
    ITokenUsageRepository tokenUsage,
    ILogger<ChatOrchestrator>? logger = null)
{
    private readonly ILogger<ChatOrchestrator> _logger = logger ?? NullLogger<ChatOrchestrator>.Instance;

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

        var (conversation, created) = await GetOrCreateConversationAsync(userId, conversationId, userMessage, cancellationToken);
        var firstTitle = conversation.Title;
        _logger.LogInformation("[{ClassName}] User {UserId} sent a message to {Outcome} conversation {ConversationId} ({ImageCount} image(s))",
            nameof(ChatOrchestrator), userId, created ? "new" : "existing", conversation.Id, imageIds?.Count ?? 0);
        if (conversation.IsArchived)
        {
            _logger.LogWarning("[{ClassName}] Rejected message to archived conversation {ConversationId}", nameof(ChatOrchestrator), conversation.Id);
            yield return new AiChatError("This conversation is archived - unarchive it before sending new messages.");
            yield break;
        }

        if (conversation.Kind != ConversationKinds.Chat)
        {
            _logger.LogWarning("[{ClassName}] Rejected direct message to {Kind} conversation {ConversationId}", nameof(ChatOrchestrator), conversation.Kind, conversation.Id);
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
            _logger.LogWarning("[{ClassName}] Rejected message to conversation {ConversationId}: a tool request is still outstanding", nameof(ChatOrchestrator), conversation.Id);
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
                    _logger.LogWarning("[{ClassName}] User {UserId} attached image {ImageId}, which is not a confirmed upload", nameof(ChatOrchestrator), userId, imageId);
                    yield return new AiChatError($"Image '{imageId}' is not a confirmed upload for this account.");
                    yield break;
                }

                image.UsedAt = DateTimeOffset.UtcNow;
                imageBlocks.Add(new ImageContentBlock(image.ObjectKey, image.ContentType, image.Id));
            }
        }

        var sequenceNumber = await conversations.GetNextSequenceNumberAsync(conversation.Id, cancellationToken);
        conversations.AddMessage(ConversationMessageFactory.CreateUserMessage(conversation.Id, sequenceNumber, userMessage, imageBlocks));
        conversation.LastMessageAt = DateTimeOffset.UtcNow;
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await eventTracker.TrackAsync(userId, EventTypes.AiMessageSent, null, cancellationToken);
        _logger.LogInformation("[{ClassName}] Stored message {SequenceNumber} in conversation {ConversationId}; starting the turn", nameof(ChatOrchestrator), sequenceNumber, conversation.Id);

        await compaction.CompactIfNeededAsync(conversation.Id, userId, cancellationToken);

        // A new chat is named by a cheap model while the first reply streams; the title cut from the message stays until then. The call
        // does no database work, so it is safe beside the turn, and the result is saved here, between events, on this one flow.
        var titleTask = created ? StartTitle(userMessage, imageBlocks is { Count: > 0 }, cancellationToken) : null;

        await foreach (var chatEvent in runner.RunAsync(userId, conversation.Id, cancellationToken))
        {
            if (titleTask is not null && chatEvent is AiChatCompleted or AiChatError)
            {
                var titled = await ApplyTitleAsync(userId, conversation, firstTitle, titleTask, cancellationToken);
                titleTask = null;
                if (titled is not null)
                {
                    yield return titled;
                }
            }

            yield return chatEvent;
        }

        // The stream ended some other way (a turn that stopped for a question or an approval): the title still goes out, as the last event.
        if (titleTask is not null && await ApplyTitleAsync(userId, conversation, firstTitle, titleTask, cancellationToken) is { } late)
        {
            yield return late;
        }
    }

    private Task<TitleResult> StartTitle(string userMessage, bool hasImages, CancellationToken cancellationToken)
    {
        try
        {
            return titler.GenerateAsync(userMessage, hasImages, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[{ClassName}] Chat title could not be started", nameof(ChatOrchestrator));
            return Task.FromResult(TitleResult.None);
        }
    }

    /// <summary>Saves the generated title unless the chat has been renamed meanwhile; null when there is nothing to announce.</summary>
    private async Task<AiChatTitleUpdated?> ApplyTitleAsync(
        Guid userId, Conversation conversation, string? firstTitle, Task<TitleResult> titleTask, CancellationToken cancellationToken)
    {
        try
        {
            var result = await titleTask;
            if (result.Usage is { } usage)
            {
                tokenUsage.Add(ConversationMessageFactory.CreateUsageRecord(userId, conversation.Id, usage));
            }

            var announce = result.Title is { } title && conversation.Title == firstTitle && title != firstTitle;
            if (announce)
            {
                conversation.Title = result.Title;
            }

            if (announce || result.Usage is not null)
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }

            if (announce)
            {
                _logger.LogInformation("[{ClassName}] Titled conversation {ConversationId} '{Title}'", nameof(ChatOrchestrator), conversation.Id, result.Title);
            }

            return announce ? new AiChatTitleUpdated(conversation.Id, result.Title!) : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Never let naming a chat break the chat.
            _logger.LogWarning(ex, "[{ClassName}] Conversation {ConversationId} could not be given a generated title", nameof(ChatOrchestrator), conversation.Id);
            return null;
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
            _logger.LogWarning("[{ClassName}] User {UserId} tried to {Decision} tool request {ToolRequestId}, which is not pending approval (status {Status})",
                nameof(ChatOrchestrator), userId, approve ? "approve" : "deny", toolExecutionRequestId, request?.Status);
            yield return new AiChatError("This request is no longer pending approval.");
            yield break;
        }

        _logger.LogInformation("[{ClassName}] User {UserId} {Decision} tool request {ToolRequestId} ({ToolName})",
            nameof(ChatOrchestrator), userId, approve ? "approved" : "denied", toolExecutionRequestId, request.ToolName);
        var interaction = toolRegistry.Find(request.ToolName)?.Interaction;
        if (interaction == ToolInteractionKind.Question)
        {
            _logger.LogWarning("[{ClassName}] Tool request {ToolRequestId} is a question and cannot be approved or denied", nameof(ChatOrchestrator), toolExecutionRequestId);
            yield return new AiChatError("This request is a question - resolve it via the answer endpoint, not approve/deny.");
            yield break;
        }

        if (interaction == ToolInteractionKind.SubAgent)
        {
            _logger.LogWarning("[{ClassName}] Tool request {ToolRequestId} is a running sub-agent and cannot be approved or denied", nameof(ChatOrchestrator), toolExecutionRequestId);
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
            _logger.LogInformation("[{ClassName}] Tool request {ToolRequestId} resolved; sibling requests are still pending so the turn stays paused", nameof(ChatOrchestrator), toolExecutionRequestId);
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
            _logger.LogWarning("[{ClassName}] User {UserId} tried to answer tool request {ToolRequestId}, which is not pending (status {Status})",
                nameof(ChatOrchestrator), userId, toolExecutionRequestId, request?.Status);
            yield return new AiChatError("This request is no longer pending approval.");
            yield break;
        }

        if (toolRegistry.Find(request.ToolName)?.Interaction != ToolInteractionKind.Question)
        {
            _logger.LogWarning("[{ClassName}] Tool request {ToolRequestId} ({ToolName}) is not a question and cannot be answered", nameof(ChatOrchestrator), toolExecutionRequestId, request.ToolName);
            yield return new AiChatError("This request is not a question - resolve it via approve/deny, not the answer endpoint.");
            yield break;
        }

        _logger.LogInformation("[{ClassName}] User {UserId} answered tool request {ToolRequestId} ({ToolName}) with {AnswerCount} answer(s)",
            nameof(ChatOrchestrator), userId, toolExecutionRequestId, request.ToolName, answers.Count);
        var resolution = new ToolApprovalResolution();
        await foreach (var chatEvent in approvalResolver.AnswerAsync(userId, request, answers, resolution, cancellationToken))
        {
            yield return chatEvent;
        }

        if (resolution.ReadyConversationId is not { } readyConversationId)
        {
            _logger.LogInformation("[{ClassName}] Question {ToolRequestId} answered; sibling requests are still pending so the turn stays paused", nameof(ChatOrchestrator), toolExecutionRequestId);
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
            _logger.LogWarning("[{ClassName}] User {UserId} tried to cancel tool request {ToolRequestId}, which is not a running sub-agent (status {Status})",
                nameof(ChatOrchestrator), userId, toolExecutionRequestId, request?.Status);
            yield return new AiChatError("This request is not a running sub-agent.");
            yield break;
        }

        _logger.LogInformation("[{ClassName}] User {UserId} cancelled sub-agent tool request {ToolRequestId} ({ToolName})", nameof(ChatOrchestrator), userId, toolExecutionRequestId, request.ToolName);

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

    private async Task<(Conversation Conversation, bool Created)> GetOrCreateConversationAsync(
        Guid userId, Guid? conversationId, string initialMessage, CancellationToken cancellationToken)
    {
        if (conversationId is { } id)
        {
            var existing = await conversations.FindAsync(userId, id, cancellationToken);
            if (existing is null)
            {
                _logger.LogWarning("[{ClassName}] User {UserId} tried to use conversation {ConversationId}, which does not exist for them", nameof(ChatOrchestrator), userId, id);
                throw new ConversationAccessDeniedException(id);
            }

            return (existing, false);
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
        _logger.LogInformation("[{ClassName}] Created conversation {ConversationId} for user {UserId}", nameof(ChatOrchestrator), conversation.Id, userId);
        return (conversation, true);
    }
}
