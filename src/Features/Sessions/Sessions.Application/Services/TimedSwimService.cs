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
    Resistance? Resistance,
    IReadOnlyList<Split> Splits,
    double? StrokeRateCyclesPerMinute,
    bool IncludeInMetrics,
    DateTimeOffset CreatedAt)
{
    public static TimedSwimView Of(TimedSwim t) => new(
        t.Id, t.SessionId, t.PerformedOn, t.Course, t.Unit, t.Dist, t.Stroke, t.Mode, t.Seconds, t.Effort, t.Intensity,
        t.StartType, t.Finish,
        t.ResistanceType is null ? null : new Resistance { Type = t.ResistanceType, Load = t.ResistanceLoad },
        SessionJson.Deserialize<List<Split>>(t.SplitsJson), t.StrokeRateCyclesPerMinute, t.IncludeInMetrics, t.CreatedAt);
}

/// <summary>The timed swim failed validation. The message lists what to fix.</summary>
public sealed class InvalidTimedSwimException(IReadOnlyList<string> problems)
    : Exception($"The timed swim is not valid: {string.Join("; ", problems)}.")
{
    public IReadOnlyList<string> Problems { get; } = problems;
}

public sealed class TimedSwimNotFoundException(Guid id) : Exception($"No timed swim with id {id} exists for this user.");

/// <summary>
/// Logs and reads timed swims. Independent of <see cref="SessionService"/>'s tree/fact pipeline - a timed swim is a standalone
/// performance record, not a training-volume one, though it may optionally point at the session it was swum during.
/// </summary>
public sealed class TimedSwimService(
    ITimedSwimRepository timedSwims,
    IntensityResolver intensity,
    [FromKeyedServices(SessionsKeys.UnitOfWork)] IUnitOfWork unitOfWork,
    ILogger<TimedSwimService>? logger = null)
{
    private readonly ILogger<TimedSwimService> _logger = logger ?? NullLogger<TimedSwimService>.Instance;

    /// <exception cref="InvalidTimedSwimException">The swim is not valid.</exception>
    public async Task<TimedSwimView> LogAsync(Guid userId, LogTimedSwimRequest request, CancellationToken cancellationToken)
    {
        var splits = (request.Splits ?? []).Select(s => s.ToSplit()).ToList();
        var resistance = request.Resistance?.ToResistance();
        var problems = TimedSwimValidator.Validate(request.Dist, request.Seconds, resistance, splits);
        if (problems.Count > 0)
        {
            _logger.LogWarning("[{ClassName}] Rejected timed swim from user {UserId}: {Problems}", nameof(TimedSwimService), userId, string.Join("; ", problems));
            throw new InvalidTimedSwimException(problems);
        }

        var timedSwim = new TimedSwim
        {
            Id = Guid.NewGuid(),
            OwnerId = userId,
            SessionId = request.SessionId,
            PerformedOn = request.PerformedOn,
            Course = request.Course,
            Dist = request.Dist,
            Stroke = request.Stroke,
            Mode = request.Mode,
            Seconds = request.Seconds,
            Effort = request.Effort,
            Intensity = intensity.Resolve(request.Effort)?.Percent,
            StartType = request.StartType,
            Finish = request.Finish,
            ResistanceType = resistance?.Type,
            ResistanceLoad = resistance?.Load,
            SplitsJson = SessionJson.Serialize(splits),
            StrokeRateCyclesPerMinute = StrokeRateCalculator.Compute(splits),
            IncludeInMetrics = request.IncludeInMetrics,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        timedSwims.Add(timedSwim);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("[{ClassName}] Logged timed swim {TimedSwimId} ({Dist} {Stroke} in {Seconds}s) for user {UserId}, session {SessionId}",
            nameof(TimedSwimService), timedSwim.Id, timedSwim.Dist, timedSwim.Stroke, timedSwim.Seconds, userId, timedSwim.SessionId);
        return TimedSwimView.Of(timedSwim);
    }

    /// <exception cref="TimedSwimNotFoundException">There is no such swim for this user.</exception>
    public async Task<TimedSwimView> GetAsync(Guid userId, Guid id, CancellationToken cancellationToken)
    {
        var found = await timedSwims.FindAsync(userId, id, cancellationToken);
        if (found is null)
        {
            _logger.LogWarning("[{ClassName}] User {UserId} asked for timed swim {TimedSwimId}, which does not exist for them", nameof(TimedSwimService), userId, id);
            throw new TimedSwimNotFoundException(id);
        }

        return TimedSwimView.Of(found);
    }

    public async Task<IReadOnlyList<TimedSwimView>> ListAsync(Guid userId, TimedSwimFilter filter, CancellationToken cancellationToken)
    {
        var found = await timedSwims.ListAsync(userId, filter, cancellationToken);
        _logger.LogDebug("[{ClassName}] Listed {Count} timed swim(s) for user {UserId}", nameof(TimedSwimService), found.Count, userId);
        return found.Select(TimedSwimView.Of).ToList();
    }
}
