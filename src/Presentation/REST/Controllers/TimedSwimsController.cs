using Auth.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sessions.Application.Services;
using Sessions.Domain.Models;

namespace REST.Controllers;

/// <summary>
/// Timed swims: how a swim went, logged separately from training-volume sessions - usually during a practice
/// (<see cref="LogTimedSwimRequest.SessionId"/> set), sometimes standalone (e.g. a race result).
/// </summary>
[ApiController]
[Authorize]
[Route("api/timed-swims")]
public sealed class TimedSwimsController(TimedSwimService timedSwims, ICurrentUserAccessor currentUser) : ControllerBase
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
        [FromQuery] int? minIntensity,
        [FromQuery] int? maxIntensity,
        [FromQuery] StartType[]? startTypes,
        [FromQuery] string[]? resistanceTypes,
        [FromQuery] bool? hasResistance,
        CancellationToken cancellationToken = default)
    {
        var filter = new TimedSwimFilter(sessionId, from, to, distances, strokes, minIntensity, maxIntensity, startTypes, resistanceTypes, hasResistance);
        var found = await timedSwims.ListAsync(currentUser.UserId, filter, cancellationToken);
        return Ok(found.Select(TimedSwimResponse.FromView));
    }

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
    StartType? StartType,
    FinishReference? Finish,
    Resistance? Resistance,
    IReadOnlyList<Split> Splits,
    double? StrokeRateCyclesPerMinute,
    bool IncludeInMetrics,
    DateTimeOffset CreatedAt)
{
    public static TimedSwimResponse FromView(TimedSwimView v) => new(
        v.Id, v.SessionId, v.PerformedOn, v.Course, v.Unit, v.Dist, v.Stroke, v.Mode, v.Seconds, v.Effort, v.Intensity,
        v.StartType, v.Finish, v.Resistance, v.Splits, v.StrokeRateCyclesPerMinute, v.IncludeInMetrics, v.CreatedAt);
}
