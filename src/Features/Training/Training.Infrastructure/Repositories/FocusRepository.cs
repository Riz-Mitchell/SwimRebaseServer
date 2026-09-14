using Microsoft.EntityFrameworkCore;
using Training.Application.Interfaces;
using Training.Domain.Models;

namespace Training.Infrastructure.Repositories;

/// <summary>Pure query/tracking against <see cref="TrainingDbContext"/> - no SaveChanges.</summary>
public sealed class FocusRepository(TrainingDbContext db) : IFocusRepository
{
    public void Add(Focus focus) => db.Focuses.Add(focus);

    public Task<Focus?> FindActiveAsync(Guid userId, Guid focusId, CancellationToken cancellationToken) =>
        db.Focuses
            .Include(f => f.ComparisonPoints)
            .SingleOrDefaultAsync(f => f.UserId == userId && f.Id == focusId && f.Status == FocusStatus.Active, cancellationToken);

    public async Task<IReadOnlyList<Focus>> GetActiveAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.Focuses
            .Include(f => f.ComparisonPoints)
            .Where(f => f.UserId == userId && f.Status == FocusStatus.Active)
            .OrderBy(f => f.Priority)
            .ToListAsync(cancellationToken);

    public Task<int> CountActiveAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Focuses.CountAsync(f => f.UserId == userId && f.Status == FocusStatus.Active, cancellationToken);

    public Task<bool> HasCheckInAsync(Guid focusId, DateOnly date, CancellationToken cancellationToken) =>
        db.FocusCheckIns.AnyAsync(c => c.FocusId == focusId && c.Date == date, cancellationToken);

    public void AddCheckIn(FocusCheckIn checkIn) => db.FocusCheckIns.Add(checkIn);

    public Task ShiftPrioritiesAsync(
        Guid userId, int fromPriorityInclusive, int? toPriorityInclusive, int delta, CancellationToken cancellationToken) =>
        db.Focuses
            .Where(f => f.UserId == userId && f.Status == FocusStatus.Active && f.Priority >= fromPriorityInclusive
                && (toPriorityInclusive == null || f.Priority <= toPriorityInclusive))
            .ExecuteUpdateAsync(setters => setters.SetProperty(f => f.Priority, f => f.Priority + delta), cancellationToken);
}
