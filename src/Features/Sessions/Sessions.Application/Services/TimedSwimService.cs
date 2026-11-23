using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Sessions.Application.Interfaces;
using Sessions.Domain.Calculations;
using Sessions.Domain.Models;
using Shared.Application;

namespace Sessions.Application.Services;

/// <summary>A logged timed swim with its splits parsed.</summary>
public sealed record TimedSwimView(
    Guid Id,
    Guid? SessionId,
    DateOnly PerformedOn,
    PoolCourse Course,
    DistanceUnit Unit,
    decimal Dist,
    Stroke Stroke,
    SwimMode Mode,
    double Seconds,
    string? Effort,
    int? Intensity,
    StartType? StartType,
    FinishReference? Finish,
    IReadOnlyList<ResistanceItem> Resistances,
    IReadOnlyList<Split> Splits,
    double? StrokeRateCyclesPerMinute,
    DateTimeOffset CreatedAt)
{
    public static TimedSwimView Of(TimedSwim t) => new(
        t.Id, t.SessionId, t.PerformedOn, t.Course, t.Unit, t.Dist, t.Stroke, t.Mode, t.Seconds, t.Effort, t.Intensity,
        t.StartType, t.Finish,
        t.Resistances.Select(ResistanceItem.Of).ToList(),
        SessionJson.Deserialize<List<Split>>(t.SplitsJson), t.StrokeRateCyclesPerMinute, t.CreatedAt);
}

/// <summary>The timed swim failed validation. The message lists what to fix.</summary>
public sealed class InvalidTimedSwimException(IReadOnlyList<string> problems)
    : Exception($"The timed swim is not valid: {string.Join("; ", problems)}.")
{
    public IReadOnlyList<string> Problems { get; } = problems;
}

public sealed class TimedSwimNotFoundException(Guid id) : Exception($"No timed swim with id {id} exists for this user.");

/// <summary>
/// Logs, edits, deletes and reads timed swims. Independent of <see cref="SessionService"/>'s tree/fact pipeline - a timed swim is a
/// standalone performance record, not a training-volume one, though it may optionally point at the session it was swum during.
/// </summary>
public sealed class TimedSwimService(
    ITimedSwimRepository timedSwims,
    IntensityResolver intensity,
    IPersonalIntensityProvider personal,
    [FromKeyedServices(SessionsKeys.UnitOfWork)] IUnitOfWork unitOfWork,
    ILogger<TimedSwimService>? logger = null)
{
    private readonly ILogger<TimedSwimService> _logger = logger ?? NullLogger<TimedSwimService>.Instance;

    /// <exception cref="InvalidTimedSwimException">The swim is not valid.</exception>
    public async Task<TimedSwimView> LogAsync(Guid userId, LogTimedSwimRequest request, CancellationToken cancellationToken)
    {
        var (splits, resistances) = Check(userId, request);
        var timedSwim = new TimedSwim { Id = Guid.NewGuid(), OwnerId = userId, CreatedAt = DateTimeOffset.UtcNow };
        await ApplyAsync(timedSwim, request, splits, cancellationToken);
        timedSwim.Resistances = [.. resistances.Select(r => TimedSwimResistance.From(timedSwim.Id, r))];

        timedSwims.Add(timedSwim);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("[{ClassName}] Logged timed swim {TimedSwimId} ({Dist} {Stroke} in {Seconds}s) for user {UserId}, session {SessionId}",
            nameof(TimedSwimService), timedSwim.Id, timedSwim.Dist, timedSwim.Stroke, timedSwim.Seconds, userId, timedSwim.SessionId);
        return TimedSwimView.Of(timedSwim);
    }

    /// <summary>Replaces everything about a swim except its id, owner and creation time; the resistances are swapped as a whole.</summary>
    /// <exception cref="InvalidTimedSwimException">The swim is not valid.</exception>
    /// <exception cref="TimedSwimNotFoundException">There is no such swim for this user.</exception>
    public async Task<TimedSwimView> UpdateAsync(Guid userId, Guid id, LogTimedSwimRequest request, CancellationToken cancellationToken)
    {
        var (splits, resistances) = Check(userId, request);
        var timedSwim = await timedSwims.FindForUpdateAsync(userId, id, cancellationToken) ?? throw NotFound(userId, id);

        await ApplyAsync(timedSwim, request, splits, cancellationToken);
        timedSwims.ReplaceResistances(timedSwim, [.. resistances.Select(r => TimedSwimResistance.From(timedSwim.Id, r))]);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("[{ClassName}] Updated timed swim {TimedSwimId} for user {UserId}", nameof(TimedSwimService), id, userId);
        return TimedSwimView.Of(timedSwim);
    }

    /// <exception cref="TimedSwimNotFoundException">There is no such swim for this user.</exception>
    public async Task DeleteAsync(Guid userId, Guid id, CancellationToken cancellationToken)
    {
        var timedSwim = await timedSwims.FindForUpdateAsync(userId, id, cancellationToken) ?? throw NotFound(userId, id);
        timedSwims.Remove(timedSwim);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("[{ClassName}] Deleted timed swim {TimedSwimId} for user {UserId}", nameof(TimedSwimService), id, userId);
    }

    private (List<Split> Splits, List<ResistanceItem> Resistances) Check(Guid userId, LogTimedSwimRequest request)
    {
        var splits = (request.Splits ?? []).Select(s => s.ToSplit()).ToList();
        var resistances = request.Resistances ?? [];
        var problems = TimedSwimValidator.Validate(request.Dist, request.Seconds, resistances, splits);
        if (problems.Count > 0)
        {
            _logger.LogWarning("[{ClassName}] Rejected timed swim from user {UserId}: {Problems}", nameof(TimedSwimService), userId, string.Join("; ", problems));
            throw new InvalidTimedSwimException(problems);
        }

        return (splits, resistances);
    }

    /// <summary>Sets every request-driven field. Intensity resolves with the swimmer's own effort terms first, as for sessions.</summary>
    private async Task ApplyAsync(TimedSwim timedSwim, LogTimedSwimRequest request, List<Split> splits, CancellationToken cancellationToken)
    {
        timedSwim.SessionId = request.SessionId;
        timedSwim.PerformedOn = request.PerformedOn;
        timedSwim.Course = request.Course;
        timedSwim.Dist = request.Dist;
        timedSwim.Stroke = request.Stroke;
        timedSwim.Mode = request.Mode;
        timedSwim.Seconds = request.Seconds;
        timedSwim.Effort = request.Effort;
        timedSwim.Intensity = intensity.Resolve(request.Effort, await personal.MapperAsync(timedSwim.OwnerId, cancellationToken))?.Percent;
        timedSwim.StartType = request.StartType;
        timedSwim.Finish = request.Finish;
        timedSwim.SplitsJson = SessionJson.Serialize(splits);
        timedSwim.StrokeRateCyclesPerMinute = StrokeRateCalculator.Compute(splits);
    }

    private TimedSwimNotFoundException NotFound(Guid userId, Guid id)
    {
        _logger.LogWarning("[{ClassName}] User {UserId} asked for timed swim {TimedSwimId}, which does not exist for them", nameof(TimedSwimService), userId, id);
        return new TimedSwimNotFoundException(id);
    }

    /// <exception cref="TimedSwimNotFoundException">There is no such swim for this user.</exception>
    public async Task<TimedSwimView> GetAsync(Guid userId, Guid id, CancellationToken cancellationToken)
    {
        var found = await timedSwims.FindAsync(userId, id, cancellationToken) ?? throw NotFound(userId, id);
        return TimedSwimView.Of(found);
    }

    public async Task<IReadOnlyList<TimedSwimView>> ListAsync(Guid userId, TimedSwimFilter filter, CancellationToken cancellationToken)
    {
        var found = await timedSwims.ListAsync(userId, filter, cancellationToken);
        _logger.LogDebug("[{ClassName}] Listed {Count} timed swim(s) for user {UserId}", nameof(TimedSwimService), found.Count, userId);
        return found.Select(TimedSwimView.Of).ToList();
    }
}
