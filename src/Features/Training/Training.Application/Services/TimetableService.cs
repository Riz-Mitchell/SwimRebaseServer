using Microsoft.Extensions.DependencyInjection;
using Shared.Infrastructure;
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
    [FromKeyedServices(TrainingKeys.UnitOfWork)] IUnitOfWork unitOfWork)
{
    /// <summary>
    /// Creates a new slot (<paramref name="slotId"/> null) or a new version of an existing one,
    /// closing the previous version's <c>EffectiveTo</c> the day before - never mutating it in place.
    /// </summary>
    public async Task<TimetableEntry> UpsertSlotAsync(
        Guid userId, Guid? slotId, TimetableSlotInput input, DateOnly effectiveFrom, CancellationToken cancellationToken)
    {
        if (input.SessionType != SessionType.Swim && input.ExpectedSession is not null)
        {
            throw new ArgumentException("An expected session can only be set on a Swim slot.", nameof(input));
        }

        await userProvisioning.EnsureExistsAsync(userId, cancellationToken);

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
        await unitOfWork.SaveChangesAsync(cancellationToken);
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
        return true;
    }

    public Task<IReadOnlyList<TimetableEntry>> GetTimetableAsOfAsync(Guid userId, DateOnly asOf, CancellationToken cancellationToken) =>
        timetable.GetTimetableAsOfAsync(userId, asOf, cancellationToken);
}
