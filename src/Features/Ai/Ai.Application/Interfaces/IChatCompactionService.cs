namespace Ai.Application.Interfaces;

/// <summary>Folds old messages into a rolling summary once a conversation crosses the configured threshold.</summary>
public interface IChatCompactionService
{
    Task CompactIfNeededAsync(Guid conversationId, Guid userId, CancellationToken cancellationToken);
}
