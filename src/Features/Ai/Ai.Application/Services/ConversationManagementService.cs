using Ai.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shared.Application;

namespace Ai.Application.Services;

/// <summary>Rename/archive/unarchive for a chat - multi-step, transactional-in-spirit operations kept out of the controller.</summary>
public sealed class ConversationManagementService(
    IConversationRepository conversations,
    IToolExecutionRepository toolExecutions,
    [FromKeyedServices(AiKeys.UnitOfWork)] IUnitOfWork unitOfWork,
    ILogger<ConversationManagementService> logger)
{
    public async Task<ConversationManagementResult> RenameAsync(
        Guid userId, Guid conversationId, string title, CancellationToken cancellationToken)
    {
        var conversation = await conversations.FindAsync(userId, conversationId, cancellationToken);
        if (conversation is null)
        {
            logger.LogWarning("[{ClassName}] User {UserId} tried to rename conversation {ConversationId}, which does not exist for them", nameof(ConversationManagementService), userId, conversationId);
            return ConversationManagementResult.NotFound;
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            logger.LogWarning("[{ClassName}] User {UserId} tried to rename conversation {ConversationId} to an empty title", nameof(ConversationManagementService), userId, conversationId);
            return ConversationManagementResult.InvalidTitle;
        }

        conversation.Title = title;
        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation("[{ClassName}] User {UserId} renamed conversation {ConversationId}", nameof(ConversationManagementService), userId, conversationId);
        return ConversationManagementResult.Ok;
    }

    /// <summary>Archives the conversation and its whole sub-agent tree, and auto-denies any still-pending tool requests anywhere in it.</summary>
    public async Task<ConversationManagementResult> ArchiveAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken)
    {
        var conversation = await conversations.FindAsync(userId, conversationId, cancellationToken);
        if (conversation is null)
        {
            logger.LogWarning("[{ClassName}] User {UserId} tried to archive conversation {ConversationId}, which does not exist for them", nameof(ConversationManagementService), userId, conversationId);
            return ConversationManagementResult.NotFound;
        }

        var treeIds = await conversations.GetConversationTreeIdsAsync(conversationId, cancellationToken);
        await conversations.ArchiveAsync(treeIds, DateTimeOffset.UtcNow, cancellationToken);
        await toolExecutions.DenyOutstandingForConversationsAsync(treeIds, "Conversation archived", cancellationToken);
        logger.LogInformation("[{ClassName}] User {UserId} archived conversation {ConversationId} and {TreeSize} conversation(s) in its tree", nameof(ConversationManagementService), userId, conversationId, treeIds.Count);
        return ConversationManagementResult.Ok;
    }

    /// <summary>Unarchives just the given conversation - does not cascade, and does not undo any auto-denials from when it was archived.</summary>
    public async Task<ConversationManagementResult> UnarchiveAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken)
    {
        var conversation = await conversations.FindAsync(userId, conversationId, cancellationToken);
        if (conversation is null)
        {
            logger.LogWarning("[{ClassName}] User {UserId} tried to unarchive conversation {ConversationId}, which does not exist for them", nameof(ConversationManagementService), userId, conversationId);
            return ConversationManagementResult.NotFound;
        }

        await conversations.UnarchiveAsync(conversationId, cancellationToken);
        logger.LogInformation("[{ClassName}] User {UserId} unarchived conversation {ConversationId}", nameof(ConversationManagementService), userId, conversationId);
        return ConversationManagementResult.Ok;
    }
}

public enum ConversationManagementResult
{
    Ok,
    NotFound,
    InvalidTitle,
}
