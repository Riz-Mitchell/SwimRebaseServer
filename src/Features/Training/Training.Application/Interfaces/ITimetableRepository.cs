using Training.Domain.Models;

namespace Training.Application.Interfaces;

/// <summary>Pure query/tracking against the timetable's versioned slots - no SaveChanges (see <see cref="IUnitOfWork"/>).</summary>
public interface ITimetableRepository
{
    /// <summary>The currently-open version of a slot (<c>EffectiveTo</c> is null), if any.</summary>
    Task<TimetableEntry?> FindCurrentVersionAsync(Guid userId, Guid slotId, CancellationToken cancellationToken);

    void Add(TimetableEntry entry);

    /// <summary>Every slot version active on <paramref name="asOf"/>, with its expected sets, ordered for display.</summary>
    Task<IReadOnlyList<TimetableEntry>> GetTimetableAsOfAsync(Guid userId, DateOnly asOf, CancellationToken cancellationToken);
}
