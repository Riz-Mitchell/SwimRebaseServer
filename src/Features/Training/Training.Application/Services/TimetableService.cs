using Microsoft.Extensions.DependencyInjection;
using Shared.Application;
using Training.Application.Interfaces;
using Training.Domain.Models;

namespace Training.Application.Services;

/// <summary>
/// Manages the versioned timetable: every edit closes the current version of a slot rather than
/// mutating it, so the timetable as it stood on any past date can always be reconstructed via
/// <see cref="GetTimetableAsOfAsync"/>.
/// </summary>
public sealed class TimetableService(
    ITimetableRepository timetable,
    ITrainingUserProvisioningService userProvisioning,
    [FromKeyedServices(TrainingKeys.UnitOfWork)] IUnitOfWork unitOfWork,
    IEventTracker eventTracker)
{
    /// <summary>
    /// Creates a new slot (<paramref name="slotId"/> null) or a new version of an existing one,
    /// closing the previous version's <c>EffectiveTo</c> the day before - never mutating it in place.
    /// </summary>
    public async Task<TimetableEntry> UpsertSlotAsync(
        Guid userId, Guid? slotId, TimetableSlotInput input, DateOnly effectiveFrom, CancellationToken cancellationToken)
    {
        ValidateSlot(input);

        await userProvisioning.EnsureExistsAsync(userId, cancellationToken);

        var version = await StageSlotAsync(userId, slotId, input, effectiveFrom, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await eventTracker.TrackAsync(userId, EventTypes.TimetableSlotUpserted, null, cancellationToken);
        return version;
    }

    /// <summary>
    /// Upserts several slots effective from the same date in one save, so either all of them land or none do.
    /// Every slot is validated before any is staged.
    /// </summary>
    public async Task<IReadOnlyList<TimetableEntry>> UpsertSlotsAsync(
        Guid userId, IReadOnlyList<TimetableSlotUpsert> slots, DateOnly effectiveFrom, CancellationToken cancellationToken)
    {
        if (slots.Count == 0)
        {
            throw new ArgumentException("At least one slot is required.", nameof(slots));
        }

        var duplicated = slots.Where(s => s.SlotId is not null).GroupBy(s => s.SlotId).FirstOrDefault(g => g.Count() > 1);
        if (duplicated is not null)
        {
            throw new ArgumentException($"Slot {duplicated.Key} appears more than once.", nameof(slots));
        }

        foreach (var slot in slots)
        {
            ValidateSlot(slot.Input);
        }

        await userProvisioning.EnsureExistsAsync(userId, cancellationToken);

        var versions = new List<TimetableEntry>(slots.Count);
        foreach (var slot in slots)
        {
            versions.Add(await StageSlotAsync(userId, slot.SlotId, slot.Input, effectiveFrom, cancellationToken));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        foreach (var _ in versions)
        {
            await eventTracker.TrackAsync(userId, EventTypes.TimetableSlotUpserted, null, cancellationToken);
        }

        return versions;
    }

    private static void ValidateSlot(TimetableSlotInput input)
    {
        if (input.SessionType != SessionType.Swim && input.ExpectedSession is not null)
        {
            throw new ArgumentException("An expected session can only be set on a Swim slot.", nameof(input));
        }
    }

    private async Task<TimetableEntry> StageSlotAsync(
        Guid userId, Guid? slotId, TimetableSlotInput input, DateOnly effectiveFrom, CancellationToken cancellationToken)
    {
        var resolvedSlotId = slotId ?? Guid.NewGuid();
        if (slotId is { } existingSlotId)
        {
            var current = await timetable.FindCurrentVersionAsync(userId, existingSlotId, cancellationToken);
            if (current is not null)
            {
                current.EffectiveTo = effectiveFrom.AddDays(-1);
            }
        }

        var version = new TimetableEntry
        {
            Id = Guid.NewGuid(),
            SlotId = resolvedSlotId,
            UserId = userId,
            DayOfWeek = input.DayOfWeek,
            StartTime = input.StartTime,
            EndTime = input.EndTime,
            SessionType = input.SessionType,
            Label = input.Label,
            EffectiveFrom = effectiveFrom,
            EffectiveTo = null,
            ExpectedSession = input.ExpectedSession is { } expected
                ? new ExpectedSession
                {
                    Summary = expected.Summary,
                    TotalDistanceMeters = expected.TotalDistanceMeters,
                    PrimaryEffortZone = expected.PrimaryEffortZone,
                }
                : null,
        };

        timetable.Add(version);
        return version;
    }

    /// <summary>Ends a slot's history as of <paramref name="effectiveTo"/> - no successor version is created.</summary>
    public async Task<bool> DeleteSlotAsync(Guid userId, Guid slotId, DateOnly effectiveTo, CancellationToken cancellationToken)
    {
        var current = await timetable.FindCurrentVersionAsync(userId, slotId, cancellationToken);
        if (current is null)
        {
            return false;
        }

        current.EffectiveTo = effectiveTo;
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await eventTracker.TrackAsync(userId, EventTypes.TimetableSlotDeleted, null, cancellationToken);
        return true;
    }

    public Task<IReadOnlyList<TimetableEntry>> GetTimetableAsOfAsync(Guid userId, DateOnly asOf, CancellationToken cancellationToken) =>
        timetable.GetTimetableAsOfAsync(userId, asOf, cancellationToken);
}
