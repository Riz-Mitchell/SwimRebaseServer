using Sessions.Domain.Models;

namespace Sessions.Application.Interfaces;

/// <summary>Pure query/tracking - no SaveChanges (see <see cref="Shared.Application.IUnitOfWork"/>).</summary>
public interface IUserEffortTermRepository
{
    Task<IReadOnlyList<UserEffortTerm>> ListAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Tracked, so it can be edited. <paramref name="phrase"/> is already normalised.</summary>
    Task<UserEffortTerm?> FindAsync(Guid userId, string phrase, CancellationToken cancellationToken);

    void Add(UserEffortTerm term);
}
