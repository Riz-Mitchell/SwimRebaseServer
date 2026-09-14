using Microsoft.EntityFrameworkCore;
using Training.Application.Interfaces;
using Training.Domain.Models;

namespace Training.Infrastructure.Repositories;

/// <summary>Pure query/tracking against <see cref="TrainingDbContext"/> - no SaveChanges.</summary>
public sealed class TimetableRepository(TrainingDbContext db) : ITimetableRepository
{
    public Task<TimetableEntry?> FindCurrentVersionAsync(Guid userId, Guid slotId, CancellationToken cancellationToken) =>
        db.TimetableEntries
            .Include(t => t.ExpectedSession)
            .SingleOrDefaultAsync(t => t.UserId == userId && t.SlotId == slotId && t.EffectiveTo == null, cancellationToken);

    public void Add(TimetableEntry entry) => db.TimetableEntries.Add(entry);

    public async Task<IReadOnlyList<TimetableEntry>> GetTimetableAsOfAsync(
        Guid userId, DateOnly asOf, CancellationToken cancellationToken) =>
        await db.TimetableEntries
            .Include(t => t.ExpectedSession)
            .Where(t => t.UserId == userId && t.EffectiveFrom <= asOf && (t.EffectiveTo == null || t.EffectiveTo >= asOf))
            .OrderBy(t => t.DayOfWeek).ThenBy(t => t.StartTime)
            .ToListAsync(cancellationToken);
}
