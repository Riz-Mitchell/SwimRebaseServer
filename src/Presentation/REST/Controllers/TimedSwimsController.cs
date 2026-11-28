using Auth.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sessions.Application.Metrics;
using Sessions.Application.Services;
using Sessions.Domain.Calculations;
using Sessions.Domain.Models;

namespace REST.Controllers;

/// <summary>
/// Timed swims: how a swim went, logged like a gym set and kept apart from training-volume sessions - usually during a practice
/// (<see cref="LogTimedSwimRequest.SessionId"/> set, as a plain reference), sometimes standalone (e.g. a race result). They have
/// their own metrics and never count toward <c>/api/sessions/metrics</c>.
/// </summary>
[ApiController]
[Authorize]
[Route("api/timed-swims")]
public sealed class TimedSwimsController(TimedSwimService timedSwims, TimedSwimMetricsService metrics, ICurrentUserAccessor currentUser) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Log([FromBody] LogTimedSwimRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(TimedSwimResponse.FromView(await timedSwims.LogAsync(currentUser.UserId, request, cancellationToken)));
        }
        catch (InvalidTimedSwimException ex)
        {
            return BadRequest(new { error = ex.Message, problems = ex.Problems });
        }
    }

    /// <summary>Filterable, fastest-first. E.g. <c>?strokes=free&amp;distances=50</c>.</summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] Guid? sessionId,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] decimal[]? distances,
        [FromQuery] Stroke[]? strokes,
        [FromQuery] int? minPercent,
        [FromQuery] int? maxPercent,
        [FromQuery] int? minIntensity,
        [FromQuery] int? maxIntensity,
        [FromQuery] StartType[]? startTypes,
        [FromQuery] ResistanceKind[]? resistanceKinds,
        [FromQuery] bool? hasResistance,
        CancellationToken cancellationToken = default)
    {
        var filter = new TimedSwimFilter(sessionId, from, to, distances, strokes, EffortScale.MinPercent(minPercent, minIntensity), EffortScale.MaxPercent(maxPercent, maxIntensity), startTypes, resistanceKinds, hasResistance);
        var found = await timedSwims.ListAsync(currentUser.UserId, filter, cancellationToken);
        return Ok(found.Select(TimedSwimResponse.FromView));
    }

    /// <summary>
    /// Filter, group and measure timed swims, e.g. <c>?distances=25&amp;strokes=free&amp;groupBy=startType&amp;measure=bestTime</c> (best 25 free by start)
    /// or <c>?distances=50&amp;groupBy=resistanceKind&amp;measure=avgTime</c>. A swim with several kinds of resistance counts under each kind.
    /// </summary>
    [HttpGet("metrics")]
    public async Task<IActionResult> GetMetrics(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] decimal[]? distances,
        [FromQuery] Stroke[]? strokes,
        [FromQuery] SwimMode[]? modes,
        [FromQuery] PoolCourse[]? courses,
        [FromQuery] StartType[]? startTypes,
        [FromQuery] FinishReference[]? finishes,
        [FromQuery] ResistanceKind[]? resistanceKinds,
        [FromQuery] bool? hasResistance,
        [FromQuery] int? minPercent,
        [FromQuery] int? maxPercent,
        [FromQuery] int? minIntensity,
        [FromQuery] int? maxIntensity,
        [FromQuery] string? scale,
        [FromQuery] TimedSwimDimension[]? groupBy,
        [FromQuery] TimedSwimDimension? thenBy,
        [FromQuery] TimedSwimMeasure measure = TimedSwimMeasure.Count,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var query = new TimedSwimMetricsQuery(
                from, to, distances, strokes, modes, courses, startTypes, finishes, resistanceKinds, hasResistance,
                EffortScale.MinPercent(minPercent, minIntensity), EffortScale.MaxPercent(maxPercent, maxIntensity),
                Dimensions(groupBy, thenBy), measure, scale);
            return Ok(await metrics.QueryAsync(currentUser.UserId, query, cancellationToken));
        }
        catch (InvalidMetricsQueryException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary><c>thenBy</c> is the second <c>groupBy</c>, for a request that groups by exactly one dimension first.</summary>
    private static List<TimedSwimDimension> Dimensions(TimedSwimDimension[]? groupBy, TimedSwimDimension? thenBy) => thenBy switch
    {
        null => [.. groupBy ?? []],
        { } second when groupBy is { Length: 1 } => [groupBy[0], second],
        _ => throw new InvalidMetricsQueryException("thenBy needs exactly one groupBy."),
    };

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(TimedSwimResponse.FromView(await timedSwims.GetAsync(currentUser.UserId, id, cancellationToken)));
        }
        catch (TimedSwimNotFoundException)
        {
            return NotFound();
        }
    }

    /// <summary>Replaces the swim wholesale with the request body, the same shape as the log request.</summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] LogTimedSwimRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(TimedSwimResponse.FromView(await timedSwims.UpdateAsync(currentUser.UserId, id, request, cancellationToken)));
        }
        catch (InvalidTimedSwimException ex)
        {
            return BadRequest(new { error = ex.Message, problems = ex.Problems });
        }
        catch (TimedSwimNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await timedSwims.DeleteAsync(currentUser.UserId, id, cancellationToken);
            return NoContent();
        }
        catch (TimedSwimNotFoundException)
        {
            return NotFound();
        }
    }
}

public sealed record TimedSwimResponse(
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
    StartType StartType,
    FinishReference Finish,
    IReadOnlyList<ResistanceItem> Resistances,
    IReadOnlyList<Split> Splits,
    double? StrokeRateCyclesPerMinute,
    DateTimeOffset CreatedAt)
{
    public static TimedSwimResponse FromView(TimedSwimView v) => new(
        v.Id, v.SessionId, v.PerformedOn, v.Course, v.Unit, v.Dist, v.Stroke, v.Mode, v.Seconds, v.Effort, v.Intensity,
        v.StartType, v.Finish, v.Resistances, v.Splits, v.StrokeRateCyclesPerMinute, v.CreatedAt);
}
