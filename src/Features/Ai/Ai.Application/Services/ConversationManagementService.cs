using Ai.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Shared.Application;

namespace Ai.Application.Services;

/// <summary>Rename/archive/unarchive for a chat - multi-step, transactional-in-spirit operations kept out of the controller.</summary>
public sealed class ConversationManagementService(
    IConversationRepository conversations,
    IToolExecutionRepository toolExecutions,
    [FromKeyedServices(AiKeys.UnitOfWork)] IUnitOfWork unitOfWork)
{
    public async Task<ConversationManagementResult> RenameAsync(
        Guid userId, Guid conversationId, string title, CancellationToken cancellationToken)
    {
        var conversation = await conversations.FindAsync(userId, conversationId, cancellationToken);
        if (conversation is null) return ConversationManagementResult.NotFound;
        if (string.IsNullOrWhiteSpace(title)) return ConversationManagementResult.InvalidTitle;

        conversation.Title = title;
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ConversationManagementResult.Ok;
    }

    /// <summary>Archives the conversation and its whole sub-agent tree, and auto-denies any still-pending tool requests anywhere in it.</summary>
    public async Task<ConversationManagementResult> ArchiveAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken)
    {
        var conversation = await conversations.FindAsync(userId, conversationId, cancellationToken);
        if (conversation is null) return ConversationManagementResult.NotFound;

        var treeIds = await conversations.GetConversationTreeIdsAsync(conversationId, cancellationToken);
        await conversations.ArchiveAsync(treeIds, DateTimeOffset.UtcNow, cancellationToken);
        await toolExecutions.DenyOutstandingForConversationsAsync(treeIds, "Conversation archived", cancellationToken);
        return ConversationManagementResult.Ok;
    }

    /// <summary>Unarchives just the given conversation - does not cascade, and does not undo any auto-denials from when it was archived.</summary>
    public async Task<ConversationManagementResult> UnarchiveAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken)
    {
        var conversation = await conversations.FindAsync(userId, conversationId, cancellationToken);
        if (conversation is null) return ConversationManagementResult.NotFound;

        await conversations.UnarchiveAsync(conversationId, cancellationToken);
        return ConversationManagementResult.Ok;
    }
}

public enum ConversationManagementResult
{
    Ok,
    NotFound,
    InvalidTitle,
}
