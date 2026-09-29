using Auth.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sessions.Application.Metrics;
using Sessions.Application.Services;
using Sessions.Domain.Models;

namespace REST.Controllers;

/// <summary>
/// Saved swim sessions and what they add up to. Sessions are created by the AI import (the chat's save_session tool); here they can be
/// read, corrected with the same path-addressed edits the chat uses, and summed with <see cref="GetMetrics"/>.
/// </summary>
[ApiController]
[Authorize]
[Route("api/sessions")]
public sealed class SessionsController(SessionService sessions, MetricsService metrics, ICurrentUserAccessor currentUser) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] int limit = 50, [FromQuery] int offset = 0, CancellationToken cancellationToken = default)
    {
        var found = await sessions.ListAsync(currentUser.UserId, from, to, limit, offset, cancellationToken);
        return Ok(found.Select(SessionSummaryResponse.FromEntity));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(SessionResponse.FromView(await sessions.GetAsync(currentUser.UserId, id, cancellationToken)));
        }
        catch (SessionNotFoundException)
        {
            return NotFound();
        }
    }

    /// <summary>Changes a session with small path-addressed edits (remove, replace, insert, set_group); the totals and facts are rebuilt with it.</summary>
    [HttpPost("{id:guid}/edits")]
    public async Task<IActionResult> Edit(Guid id, [FromBody] EditSessionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var edited = await sessions.EditAsync(currentUser.UserId, id, request.Edits.Select(e => e.ToEdit()).ToList(), cancellationToken);
            return Ok(SessionResponse.FromView(edited));
        }
        catch (SessionNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidSessionEditException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidSessionException ex)
        {
            return BadRequest(new { error = ex.Message, problems = ex.Problems });
        }
    }

    /// <summary>Filter, group and measure what was swum, e.g. <c>?strokes=fly&amp;groupBy=month</c> or <c>?minIntensity=8&amp;groupBy=week</c>.</summary>
    [HttpGet("metrics")]
    public async Task<IActionResult> GetMetrics(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] Stroke[]? strokes,
        [FromQuery] SwimMode[]? modes,
        [FromQuery] int? minIntensity,
        [FromQuery] int? maxIntensity,
        [FromQuery] MetricDimension[]? groupBy,
        [FromQuery] MetricMeasure measure = MetricMeasure.Distance,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var query = new MetricsQuery(from, to, strokes, modes, minIntensity, maxIntensity, groupBy ?? [], measure);
            return Ok(await metrics.QueryAsync(currentUser.UserId, query, cancellationToken));
        }
        catch (InvalidMetricsQueryException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}

public sealed record EditSessionRequest(List<SessionEditDto> Edits);

public sealed record SessionSummaryResponse(
    Guid Id, DateOnly PerformedOn, string Title, PoolCourse Course, DistanceUnit Unit, SessionSource Source, int TotalDistance, DateTimeOffset CreatedAt)
{
    public static SessionSummaryResponse FromEntity(Session s) =>
        new(s.Id, s.PerformedOn, s.Title, s.Course, s.Unit, s.Source, s.TotalDistance, s.CreatedAt);
}


public sealed record SessionResponse(
    Guid Id, DateOnly PerformedOn, string Title, PoolCourse Course, DistanceUnit Unit, SessionSource Source, int TotalDistance, SwimSession Content, string Outline)
{
    public static SessionResponse FromView(SessionView v) =>
        new(v.Id, v.PerformedOn, v.Title, v.Course, v.Unit, v.Source, v.TotalDistance, v.Content, v.Outline);
}
