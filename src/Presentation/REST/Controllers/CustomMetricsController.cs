using Auth.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sessions.Application.Metrics;
using Sessions.Application.Services;
using Sessions.Domain.Models;

namespace REST.Controllers;

/// <summary>The metrics a swimmer keeps on their home page: saved questions about their training, answered with the same engine as <c>api/sessions/metrics</c>.</summary>
[ApiController]
[Authorize]
[Route("api/metrics/custom")]
public sealed class CustomMetricsController(
    CustomMetricService metrics,
    CustomMetricEvaluator evaluator,
    ICurrentUserAccessor currentUser,
    TimeProvider clock) : ControllerBase
{
    /// <summary>The definitions, for editing.</summary>
    [HttpGet("definitions")]
    public async Task<IActionResult> Definitions(CancellationToken cancellationToken) =>
        Ok(await metrics.ListAsync(currentUser.UserId, cancellationToken));

    /// <summary>Every metric with its current value. <paramref name="today"/> (yyyy-MM-dd) is the swimmer's own date, so "this week" follows their time zone.</summary>
    [HttpGet]
    public async Task<IActionResult> Values([FromQuery] string? today, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await evaluator.EvaluateAsync(currentUser.UserId, CustomMetricDetailRequest.ParseToday(today, ServerToday()), cancellationToken));
        }
        catch (InvalidCustomMetricException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// One metric over the whole period around <paramref name="today"/>, for stepping back and forward in time. <paramref name="window"/> and
    /// <paramref name="groupBy"/> override the saved ones for this request only; nothing is written.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Detail(
        Guid id, [FromQuery] string? today, [FromQuery] string? window, [FromQuery] string? groupBy, CancellationToken cancellationToken)
    {
        try
        {
            var request = CustomMetricDetailRequest.Parse(today, window, groupBy, ServerToday());
            return await evaluator.EvaluateOneAsync(currentUser.UserId, id, request, cancellationToken) is { } detail ? Ok(detail) : NotFound();
        }
        catch (InvalidCustomMetricException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CustomMetricDefinition definition, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await metrics.CreateAsync(currentUser.UserId, definition, cancellationToken));
        }
        catch (Exception ex) when (ex is InvalidCustomMetricException or InvalidMetricsQueryException)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] CustomMetricDefinition definition, CancellationToken cancellationToken)
    {
        try
        {
            return await metrics.UpdateAsync(currentUser.UserId, id, definition, cancellationToken) is { } updated ? Ok(updated) : NotFound();
        }
        catch (Exception ex) when (ex is InvalidCustomMetricException or InvalidMetricsQueryException)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        await metrics.DeleteAsync(currentUser.UserId, id, cancellationToken) ? NoContent() : NotFound();

    private DateOnly ServerToday() => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
}
