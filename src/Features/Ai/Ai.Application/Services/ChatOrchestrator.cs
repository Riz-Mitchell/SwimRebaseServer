using System.Runtime.CompilerServices;
using Ai.Application.Exceptions;
using Ai.Application.Interfaces;
using Ai.Application.Models;
using Ai.Domain.Models;

namespace Ai.Application.Services;

/// <summary>
/// Entry points for the Ai chat feature. Both feed the same <see cref="ChatTurnLoop"/>:
/// <see cref="SendUserMessageAsync"/> starts a normal turn, <see cref="ResolveToolApprovalAsync"/>
/// resumes one after a gated (write/destructive) tool call was approved or denied.
/// </summary>
public sealed class ChatOrchestrator(
    IConversationRepository conversations,
    IToolExecutionRepository toolExecutions,
    IUnitOfWork unitOfWork,
    IChatCompactionService compaction,
    GatedToolApprovalResolver approvalResolver,
    ChatTurnLoop turnLoop)
{
    public async IAsyncEnumerable<AiChatEvent> SendUserMessageAsync(
        Guid userId, Guid? conversationId, string userMessage, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var conversation = await GetOrCreateConversationAsync(userId, conversationId, cancellationToken);

        var sequenceNumber = await conversations.GetNextSequenceNumberAsync(conversation.Id, cancellationToken);
        conversations.AddMessage(ConversationMessageFactory.CreateUserMessage(conversation.Id, sequenceNumber, userMessage));
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
        Guid userId, Guid? conversationId, CancellationToken cancellationToken)
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
            CreatedAt = DateTimeOffset.UtcNow,
            LastMessageAt = DateTimeOffset.UtcNow,
        };

        conversations.Add(conversation);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return conversation;
    }
}
