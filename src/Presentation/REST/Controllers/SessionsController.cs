using Auth.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Sessions.Application.Metrics;
using Sessions.Application.Services;
using Sessions.Domain.Calculations;
using Sessions.Domain.Models;

namespace REST.Controllers;

/// <summary>
/// Saved swim sessions and what they add up to. Sessions are created by the AI import (the chat's save_session tool); here they can be
/// read, corrected with the same path-addressed edits the chat uses, and summed with <see cref="GetMetrics"/>.
/// </summary>
[ApiController]
[Authorize]
[Route("api/sessions")]
public sealed class SessionsController(
    SessionService sessions,
    MetricsService metrics,
    SessionPreferencesService preferences,
    ICurrentUserAccessor currentUser,
    ILogger<SessionsController> logger) : ControllerBase
{
    /// <summary>Newest first. <c>?include=content</c> adds each session's tree, so a page of full sessions is one call (at most <see cref="SessionService.MaxContentLimit"/>).</summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] int limit = 50,
        [FromQuery] int offset = 0,
        [FromQuery] SessionInclude[]? include = null,
        CancellationToken cancellationToken = default)
    {
        if (include?.Contains(SessionInclude.Content) == true)
        {
            var withContent = await sessions.ListWithContentAsync(currentUser.UserId, from, to, limit, offset, cancellationToken);
            return Ok(withContent.Select(SessionWithContentResponse.From));
        }

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
            logger.LogWarning("[{ClassName}] Invalid edit to session {SessionId} from user {UserId}: {Reason}", nameof(SessionsController), id, currentUser.UserId, ex.Message);
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidSessionException ex)
        {
            logger.LogWarning("[{ClassName}] Edit would leave session {SessionId} invalid for user {UserId}: {Problems}", nameof(SessionsController), id, currentUser.UserId, string.Join("; ", ex.Problems));
            return BadRequest(new { error = ex.Message, problems = ex.Problems });
        }
    }

    /// <summary>
    /// Filter, group and measure what was swum, e.g. <c>?strokes=fly&amp;groupBy=month</c> or <c>?sessionId=...&amp;groupBy=stroke</c> for one session or <c>?minPercent=80&amp;groupBy=week</c>.
    /// <c>scale</c> picks how <c>groupBy=intensity</c> is bucketed; <c>applyDefault=false</c> stops distance with no stated effort being counted at the swimmer's default effort; <c>minIntensity</c>/<c>maxIntensity</c> are the earlier 1-10 bounds.
    /// </summary>
    [HttpGet("metrics")]
    public async Task<IActionResult> GetMetrics(
        [FromQuery] Guid? sessionId,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] Stroke[]? strokes,
        [FromQuery] SwimMode[]? modes,
        [FromQuery] int? minPercent,
        [FromQuery] int? maxPercent,
        [FromQuery] int? minIntensity,
        [FromQuery] int? maxIntensity,
        [FromQuery] string? scale,
        [FromQuery] MetricDimension[]? groupBy,
        [FromQuery] MetricDimension? thenBy,
        [FromQuery] MetricMeasure measure = MetricMeasure.Distance,
        [FromQuery] bool applyDefault = true,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var query = new MetricsQuery(
                from, to, strokes, modes,
                EffortScale.MinPercent(minPercent, minIntensity), EffortScale.MaxPercent(maxPercent, maxIntensity),
                Dimensions(groupBy, thenBy), measure, scale, SessionId: sessionId, ApplyDefaultEffort: applyDefault);
            return Ok(await metrics.QueryAsync(currentUser.UserId, query, cancellationToken));
        }
        catch (InvalidMetricsQueryException ex)
        {
            logger.LogWarning("[{ClassName}] Invalid metrics query from user {UserId}: {Reason}", nameof(SessionsController), currentUser.UserId, ex.Message);
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary><c>thenBy</c> is the second <c>groupBy</c>, for a request that groups by exactly one dimension first (<c>groupBy=day&amp;thenBy=intensity</c>).</summary>
    private static List<MetricDimension> Dimensions(MetricDimension[]? groupBy, MetricDimension? thenBy) => thenBy switch
    {
        null => [.. groupBy ?? []],
        { } second when groupBy is { Length: 1 } => [groupBy[0], second],
        _ => throw new InvalidMetricsQueryException("thenBy needs exactly one groupBy."),
    };

    /// <summary>The built-in effort scales, the swimmer's own (null until saved) and which one metrics use by default.</summary>
    [HttpGet("effort-scales")]
    public async Task<IActionResult> GetEffortScales(CancellationToken cancellationToken)
    {
        var saved = await preferences.GetAsync(currentUser.UserId, cancellationToken);
        return Ok(new EffortScalesResponse(
            EffortVocabulary.Default.Scales.Select(EffortScaleResponse.From).ToList(),
            saved.CustomEffortScale is { } custom ? EffortScaleResponse.From(EffortScale.Custom(custom)) : null,
            saved.EffortScale,
            saved.DefaultEffort));
    }

    /// <summary>The scale metrics group intensity by when a request names none, resolved, with the swimmer's default effort.</summary>
    [HttpGet("effort-scale")]
    public async Task<IActionResult> GetEffortScale(CancellationToken cancellationToken) =>
        Ok(EffortScaleSettingResponse.From(await preferences.GetAsync(currentUser.UserId, cancellationToken)));

    /// <summary>Chooses the scale; <c>customBands</c> saves the swimmer's own bands (required the first time <c>scale</c> is "custom").</summary>
    [HttpPut("effort-scale")]
    public async Task<IActionResult> SetEffortScale([FromBody] EffortScaleChoiceRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Scale))
        {
            logger.LogWarning("[{ClassName}] User {UserId} sent an effort scale choice without a scale", nameof(SessionsController), currentUser.UserId);
            return BadRequest(new { error = "scale is required." });
        }

        try
        {
            var updated = await preferences.UpdateAsync(
                currentUser.UserId,
                new SessionPreferencesUpdate(null, request.Scale, request.CustomBands?.Select(b => b.ToBand()).ToList()),
                cancellationToken);
            return Ok(EffortScaleSettingResponse.From(updated));
        }
        catch (InvalidSessionPreferencesException ex)
        {
            logger.LogWarning("[{ClassName}] Invalid effort scale choice from user {UserId}: {Reason}", nameof(SessionsController), currentUser.UserId, ex.Message);
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("preferences")]
    public async Task<IActionResult> GetPreferences(CancellationToken cancellationToken) =>
        Ok(SessionPreferencesResponse.From(await preferences.GetAsync(currentUser.UserId, cancellationToken)));

    /// <summary>Only the fields sent change. <c>customEffortScale: []</c> removes the saved custom scale.</summary>
    [HttpPut("preferences")]
    public async Task<IActionResult> SetPreferences([FromBody] SessionPreferencesRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var updated = await preferences.UpdateAsync(
                currentUser.UserId,
                new SessionPreferencesUpdate(request.DefaultEffort, request.EffortScale, request.CustomEffortScale?.Select(b => b.ToBand()).ToList(), request.DisplayUnit),
                cancellationToken);
            return Ok(SessionPreferencesResponse.From(updated));
        }
        catch (InvalidSessionPreferencesException ex)
        {
            logger.LogWarning("[{ClassName}] Invalid preferences from user {UserId}: {Reason}", nameof(SessionsController), currentUser.UserId, ex.Message);
            return BadRequest(new { error = ex.Message });
        }
    }
}

public sealed record EffortBandDto(string Key, string? Label, int Min, int Max)
{
    public EffortBand ToBand() => new(Key ?? string.Empty, Label ?? string.Empty, Min, Max);
}

public sealed record EffortScaleResponse(string Id, string Name, IReadOnlyList<EffortBand> Bands)
{
    public static EffortScaleResponse From(EffortScale scale) => new(scale.Id, scale.Name, scale.Bands);
}

public sealed record EffortScalesResponse(IReadOnlyList<EffortScaleResponse> Scales, EffortScaleResponse? Custom, string Selected, int DefaultEffort);

public sealed record EffortScaleChoiceRequest(string? Scale, List<EffortBandDto>? CustomBands);

public sealed record EffortScaleSettingResponse(string Selected, EffortScaleResponse Scale, EffortScaleResponse? Custom, int DefaultEffort)
{
    public static EffortScaleSettingResponse From(SessionPreferencesView view)
    {
        var custom = view.CustomEffortScale is { } bands ? EffortScale.Custom(bands) : null;
        var active = view.EffortScale == EffortScale.CustomId && custom is not null
            ? custom
            : EffortVocabulary.Default.FindScale(view.EffortScale) ?? EffortScale.Tens;
        return new(view.EffortScale, EffortScaleResponse.From(active), custom is null ? null : EffortScaleResponse.From(custom), view.DefaultEffort);
    }
}

public sealed record SessionPreferencesRequest(int? DefaultEffort, string? EffortScale, List<EffortBandDto>? CustomEffortScale, DistanceUnit? DisplayUnit);

public sealed record SessionPreferencesResponse(int DefaultEffort, string EffortScale, IReadOnlyList<EffortBand>? CustomEffortScale, DistanceUnit DisplayUnit)
{
    public static SessionPreferencesResponse From(SessionPreferencesView view) => new(view.DefaultEffort, view.EffortScale, view.CustomEffortScale, view.DisplayUnit);
}

public enum SessionInclude { Content }

public sealed record EditSessionRequest(List<SessionEditDto> Edits);

public sealed record SessionSummaryResponse(
    Guid Id, DateOnly PerformedOn, string Title, PoolCourse Course, DistanceUnit Unit, SessionSource Source, decimal TotalDistance, DateTimeOffset CreatedAt)
{
    public static SessionSummaryResponse FromEntity(Session s) =>
        new(s.Id, s.PerformedOn, s.Title, s.Course, s.Unit, s.Source, s.TotalDistance, s.CreatedAt);
}

public sealed record SessionWithContentResponse(
    Guid Id, DateOnly PerformedOn, string Title, PoolCourse Course, DistanceUnit Unit, SessionSource Source, decimal TotalDistance, DateTimeOffset CreatedAt, SwimSession Content)
{
    public static SessionWithContentResponse From(SessionWithContent s) =>
        new(s.Session.Id, s.Session.PerformedOn, s.Session.Title, s.Session.Course, s.Session.Unit, s.Session.Source, s.Session.TotalDistance, s.Session.CreatedAt, s.Content);
}

public sealed record SessionResponse(
    Guid Id, DateOnly PerformedOn, string Title, PoolCourse Course, DistanceUnit Unit, SessionSource Source, decimal TotalDistance, SwimSession Content, string Outline)
{
    public static SessionResponse FromView(SessionView v) =>
        new(v.Id, v.PerformedOn, v.Title, v.Course, v.Unit, v.Source, v.TotalDistance, v.Content, v.Outline);
}
