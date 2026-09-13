using Ai.Domain.Models;

namespace Ai.Application.Interfaces;

/// <summary>Pure query/tracking - no SaveChanges (see <see cref="IUnitOfWork"/>).</summary>
public interface IToolExecutionRepository
{
    void Add(ToolExecutionRequest request);

    /// <summary>Scoped to <paramref name="userId"/> - returns null if the request belongs to someone else.</summary>
    Task<ToolExecutionRequest?> FindAsync(Guid userId, Guid id, CancellationToken cancellationToken);

    /// <summary>All gated calls raised by one assistant turn - used to detect when every call in the turn is resolved.</summary>
    Task<IReadOnlyList<ToolExecutionRequest>> GetByAssistantMessageIdAsync(Guid assistantMessageId, CancellationToken cancellationToken);
}
