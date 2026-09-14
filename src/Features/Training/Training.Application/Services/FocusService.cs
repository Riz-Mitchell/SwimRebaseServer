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
    IUnitOfWork unitOfWork)
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
            ComparisonPoints = BuildComparisonPoints(input),
        };

        focuses.Add(focus);
        await unitOfWork.SaveChangesAsync(cancellationToken);
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
        await focuses.ShiftPrioritiesAsync(userId, focus.Priority + 1, null, -1, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }

    public Task<IReadOnlyList<Focus>> GetActiveAsync(Guid userId, CancellationToken cancellationToken) =>
        focuses.GetActiveAsync(userId, cancellationToken);

    private static List<ComparisonPoint> BuildComparisonPoints(FocusInput input) =>
        ToPoints(input.PositivePoints, ComparisonPointPolarity.Positive)
            .Concat(ToPoints(input.NegativePoints, ComparisonPointPolarity.Negative))
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
