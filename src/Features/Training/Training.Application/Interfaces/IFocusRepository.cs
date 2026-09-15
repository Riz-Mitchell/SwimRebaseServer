using Training.Domain.Models;

namespace Training.Application.Interfaces;

/// <summary>Pure query/tracking against a user's focuses - no SaveChanges (see <see cref="Shared.Application.IUnitOfWork"/>).</summary>
public interface IFocusRepository
{
    void Add(Focus focus);

    Task<Focus?> FindActiveAsync(Guid userId, Guid focusId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Focus>> GetActiveAsync(Guid userId, CancellationToken cancellationToken);

    Task<int> CountActiveAsync(Guid userId, CancellationToken cancellationToken);

    Task<bool> HasCheckInAsync(Guid focusId, DateOnly date, CancellationToken cancellationToken);

    void AddCheckIn(FocusCheckIn checkIn);

    /// <summary>
    /// Shifts the priority of every other <see cref="FocusStatus.Active"/> focus for this user whose
    /// priority falls in [<paramref name="fromPriorityInclusive"/>, <paramref name="toPriorityInclusive"/>]
    /// by <paramref name="delta"/> - executes immediately, independent of <see cref="Shared.Application.IUnitOfWork"/>.
    /// A null <paramref name="toPriorityInclusive"/> means no upper bound.
    /// </summary>
    Task ShiftPrioritiesAsync(
        Guid userId, int fromPriorityInclusive, int? toPriorityInclusive, int delta, CancellationToken cancellationToken);
}
