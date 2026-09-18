using Microsoft.Extensions.DependencyInjection;
using Shared.Application;
using Training.Application.Interfaces;
using Training.Domain.Models;

namespace Training.Application.Services;

/// <summary>
/// Manages a user's focuses, including the dense 1..N priority rank across their
/// <see cref="FocusStatus.Active"/> focuses - every mutation here keeps that rank contiguous and
/// unique, so "no two active focuses share a priority" never needs checking elsewhere.
/// </summary>
public sealed class FocusService(
    IFocusRepository focuses,
    ITrainingUserProvisioningService userProvisioning,
    [FromKeyedServices(TrainingKeys.UnitOfWork)] IUnitOfWork unitOfWork,
    IEventTracker eventTracker)
{
    public async Task<Focus> CreateAsync(
        Guid userId, FocusInput input, FocusOrigin origin, string? originalUserRequest, CancellationToken cancellationToken)
    {
        await userProvisioning.EnsureExistsAsync(userId, cancellationToken);

        var activeCount = await focuses.CountActiveAsync(userId, cancellationToken);
        var priority = Math.Clamp(input.Priority ?? activeCount + 1, 1, activeCount + 1);

        // Make room at `priority` by pushing everyone already there (and below) down by one.
        await focuses.ShiftPrioritiesAsync(userId, priority, activeCount, +1, cancellationToken);

        var focus = new Focus
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TagLine = input.TagLine,
            Prompt = input.Prompt,
            OriginalUserRequest = originalUserRequest,
            TotalDays = input.TotalDays,
            DaysCompleted = 0,
            Priority = priority,
            Status = FocusStatus.Active,
            Origin = origin,
            StartedAt = DateOnly.FromDateTime(DateTime.UtcNow),
            ComparisonPoints = BuildComparisonPoints(input.PositivePoints, input.NegativePoints),
        };

        focuses.Add(focus);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await eventTracker.TrackAsync(userId, EventTypes.FocusCreated, null, cancellationToken);
        return focus;
    }

    /// <summary>
    /// Records today (or <paramref name="date"/>) as honored. A second call for the same date is a
    /// no-op - a focus completes only once per calendar day even with multiple sessions that day.
    /// Completing the focus (<see cref="Focus.DaysCompleted"/> reaching <see cref="Focus.TotalDays"/>)
    /// closes the gap it leaves in the priority order.
    /// </summary>
    public async Task<bool> LogCheckInAsync(Guid userId, Guid focusId, DateOnly date, CancellationToken cancellationToken)
    {
        var focus = await focuses.FindActiveAsync(userId, focusId, cancellationToken);
        if (focus is null)
        {
            return false;
        }

        if (await focuses.HasCheckInAsync(focusId, date, cancellationToken))
        {
            return true;
        }

        focuses.AddCheckIn(new FocusCheckIn { Id = Guid.NewGuid(), FocusId = focusId, Date = date, CheckedInAt = DateTimeOffset.UtcNow });
        focus.DaysCompleted++;

        if (focus.DaysCompleted >= focus.TotalDays)
        {
            focus.Status = FocusStatus.Completed;
            focus.EndedAt = date;
            await focuses.ShiftPrioritiesAsync(userId, focus.Priority + 1, null, -1, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Re-ranks a focus, shifting only the focuses strictly between its old and new position.</summary>
    public async Task<bool> MoveToPriorityAsync(Guid userId, Guid focusId, int newPriority, CancellationToken cancellationToken)
    {
        var focus = await focuses.FindActiveAsync(userId, focusId, cancellationToken);
        if (focus is null)
        {
            return false;
        }

        var activeCount = await focuses.CountActiveAsync(userId, cancellationToken);
        newPriority = Math.Clamp(newPriority, 1, activeCount);

        if (newPriority < focus.Priority)
        {
            await focuses.ShiftPrioritiesAsync(userId, newPriority, focus.Priority - 1, +1, cancellationToken);
        }
        else if (newPriority > focus.Priority)
        {
            await focuses.ShiftPrioritiesAsync(userId, focus.Priority + 1, newPriority, -1, cancellationToken);
        }

        focus.Priority = newPriority;
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> AbandonAsync(Guid userId, Guid focusId, CancellationToken cancellationToken)
    {
        var focus = await focuses.FindActiveAsync(userId, focusId, cancellationToken);
        if (focus is null)
        {
            return false;
        }

        focus.Status = FocusStatus.Abandoned;
        focus.EndedAt = DateOnly.FromDateTime(DateTime.UtcNow);
        await CloseActivePriorityGapAsync(userId, focus.Priority, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// Replaces a focus's tagLine/prompt/totalDays and its full comparison-point list. Priority is
    /// untouched - use <see cref="MoveToPriorityAsync"/> for that. Active-only, like every other
    /// mutator here.
    /// </summary>
    public async Task<Focus?> EditAsync(Guid userId, Guid focusId, FocusEditInput input, CancellationToken cancellationToken)
    {
        var focus = await focuses.FindActiveAsync(userId, focusId, cancellationToken);
        if (focus is null)
        {
            return null;
        }

        focus.TagLine = input.TagLine;
        focus.Prompt = input.Prompt;
        focus.TotalDays = input.TotalDays;

        focus.ComparisonPoints.Clear();
        focus.ComparisonPoints.AddRange(BuildComparisonPoints(input.PositivePoints, input.NegativePoints));

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await eventTracker.TrackAsync(userId, EventTypes.FocusEdited, null, cancellationToken);
        return focus;
    }

    /// <summary>Permanently deletes a focus of any status. If it was active, closes the priority gap it leaves behind.</summary>
    public async Task<bool> DeleteAsync(Guid userId, Guid focusId, CancellationToken cancellationToken)
    {
        var focus = await focuses.FindAsync(userId, focusId, cancellationToken);
        if (focus is null)
        {
            return false;
        }

        if (focus.Status == FocusStatus.Active)
        {
            await CloseActivePriorityGapAsync(userId, focus.Priority, cancellationToken);
        }

        focuses.Remove(focus);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await eventTracker.TrackAsync(userId, EventTypes.FocusDeleted, null, cancellationToken);
        return true;
    }

    public Task<IReadOnlyList<Focus>> GetActiveAsync(Guid userId, CancellationToken cancellationToken) =>
        focuses.GetActiveAsync(userId, cancellationToken);

    /// <summary>Closes the priority gap an active focus leaves behind by shifting everything above it down by one.</summary>
    private Task CloseActivePriorityGapAsync(Guid userId, int priority, CancellationToken cancellationToken) =>
        focuses.ShiftPrioritiesAsync(userId, priority + 1, null, -1, cancellationToken);

    private static List<ComparisonPoint> BuildComparisonPoints(
        IReadOnlyList<ComparisonPointInput> positivePoints, IReadOnlyList<ComparisonPointInput> negativePoints) =>
        ToPoints(positivePoints, ComparisonPointPolarity.Positive)
            .Concat(ToPoints(negativePoints, ComparisonPointPolarity.Negative))
            .ToList();

    private static IEnumerable<ComparisonPoint> ToPoints(IReadOnlyList<ComparisonPointInput> points, ComparisonPointPolarity polarity) =>
        points.Select((p, index) => new ComparisonPoint
        {
            Id = Guid.NewGuid(),
            Polarity = polarity,
            Order = index + 1,
            TagLine = p.TagLine,
            Description = p.Description,
        });
}
