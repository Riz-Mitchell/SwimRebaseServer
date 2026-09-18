using Auth.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Training.Application.Services;
using Training.Domain.Models;

namespace REST.Controllers;

/// <summary>
/// Focuses can be created manually here or by the AI (see the Ai feature's <c>create_focus</c> tool,
/// which calls the same <see cref="FocusService"/>). Priority is a dense 1..N rank among a user's
/// active focuses, maintained entirely by <see cref="FocusService"/> - callers only ever request a
/// desired position, never set it directly.
/// </summary>
[ApiController]
[Authorize]
[Route("api/focuses")]
public sealed class FocusController(FocusService focusService, ICurrentUserAccessor currentUser) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetActive(CancellationToken cancellationToken)
    {
        var focuses = await focusService.GetActiveAsync(currentUser.UserId, cancellationToken);
        return Ok(focuses.Select(FocusResponse.FromEntity));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateFocusRequest request, CancellationToken cancellationToken)
    {
        var input = new FocusInput(
            request.TagLine, request.Prompt, request.TotalDays, request.Priority, request.PositivePoints, request.NegativePoints);

        var focus = await focusService.CreateAsync(currentUser.UserId, input, FocusOrigin.Manual, originalUserRequest: null, cancellationToken);
        return Ok(FocusResponse.FromEntity(focus));
    }

    [HttpPost("{focusId:guid}/check-ins")]
    public async Task<IActionResult> LogCheckIn(Guid focusId, [FromQuery] DateOnly? date, CancellationToken cancellationToken)
    {
        var checkInDate = date ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var logged = await focusService.LogCheckInAsync(currentUser.UserId, focusId, checkInDate, cancellationToken);
        return logged ? NoContent() : NotFound();
    }

    [HttpPut("{focusId:guid}")]
    public async Task<IActionResult> Edit(Guid focusId, [FromBody] EditFocusRequest request, CancellationToken cancellationToken)
    {
        var input = new FocusEditInput(request.TagLine, request.Prompt, request.TotalDays, request.PositivePoints, request.NegativePoints);
        var focus = await focusService.EditAsync(currentUser.UserId, focusId, input, cancellationToken);
        return focus is null ? NotFound() : Ok(FocusResponse.FromEntity(focus));
    }

    [HttpDelete("{focusId:guid}")]
    public async Task<IActionResult> Delete(Guid focusId, CancellationToken cancellationToken)
    {
        var deleted = await focusService.DeleteAsync(currentUser.UserId, focusId, cancellationToken);
        return deleted ? NoContent() : NotFound();
    }

    [HttpPut("{focusId:guid}/priority")]
    public async Task<IActionResult> MoveToPriority(Guid focusId, [FromBody] MoveFocusPriorityRequest request, CancellationToken cancellationToken)
    {
        var moved = await focusService.MoveToPriorityAsync(currentUser.UserId, focusId, request.Priority, cancellationToken);
        return moved ? NoContent() : NotFound();
    }

    [HttpPost("{focusId:guid}/abandon")]
    public async Task<IActionResult> Abandon(Guid focusId, CancellationToken cancellationToken)
    {
        var abandoned = await focusService.AbandonAsync(currentUser.UserId, focusId, cancellationToken);
        return abandoned ? NoContent() : NotFound();
    }
}

public sealed record CreateFocusRequest(
    string TagLine,
    string Prompt,
    int TotalDays,
    int? Priority,
    IReadOnlyList<ComparisonPointInput> PositivePoints,
    IReadOnlyList<ComparisonPointInput> NegativePoints);

public sealed record EditFocusRequest(
    string TagLine,
    string Prompt,
    int TotalDays,
    IReadOnlyList<ComparisonPointInput> PositivePoints,
    IReadOnlyList<ComparisonPointInput> NegativePoints);

public sealed record MoveFocusPriorityRequest(int Priority);

public sealed record FocusResponse(
    Guid Id,
    string TagLine,
    string Prompt,
    int TotalDays,
    int DaysCompleted,
    int Priority,
    FocusStatus Status,
    FocusOrigin Origin,
    DateOnly StartedAt,
    DateOnly? EndedAt,
    IReadOnlyList<ComparisonPointResponse> PositivePoints,
    IReadOnlyList<ComparisonPointResponse> NegativePoints)
{
    public static FocusResponse FromEntity(Focus focus) => new(
        focus.Id,
        focus.TagLine,
        focus.Prompt,
        focus.TotalDays,
        focus.DaysCompleted,
        focus.Priority,
        focus.Status,
        focus.Origin,
        focus.StartedAt,
        focus.EndedAt,
        focus.PositivePoints.Select(ComparisonPointResponse.FromEntity).ToList(),
        focus.NegativePoints.Select(ComparisonPointResponse.FromEntity).ToList());
}

public sealed record ComparisonPointResponse(int Order, string TagLine, string Description)
{
    public static ComparisonPointResponse FromEntity(ComparisonPoint point) => new(point.Order, point.TagLine, point.Description);
}
