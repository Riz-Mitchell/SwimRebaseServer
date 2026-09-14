using Auth.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Training.Application.Services;
using Training.Domain.Models;

namespace REST.Controllers;

/// <summary>
/// The timetable is versioned - <see cref="UpsertSlot"/> never edits a slot in place, it closes
/// the current version and opens a new one, so <see cref="GetTimetable"/> can reconstruct the
/// schedule as it stood on any past (or future) date.
/// </summary>
[ApiController]
[Authorize]
[Route("api/timetable")]
public sealed class TimetableController(TimetableService timetable, ICurrentUserAccessor currentUser) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetTimetable([FromQuery] DateOnly? asOf, CancellationToken cancellationToken)
    {
        var date = asOf ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var entries = await timetable.GetTimetableAsOfAsync(currentUser.UserId, date, cancellationToken);
        return Ok(entries.Select(TimetableEntryResponse.FromEntity));
    }

    [HttpPut("slots/{slotId:guid?}")]
    public async Task<IActionResult> UpsertSlot(
        Guid? slotId, [FromBody] UpsertTimetableSlotRequest request, CancellationToken cancellationToken)
    {
        var version = await timetable.UpsertSlotAsync(
            currentUser.UserId,
            slotId,
            new TimetableSlotInput(request.DayOfWeek, request.StartTime, request.EndTime, request.SessionType, request.Label, request.ExpectedSession),
            request.EffectiveFrom,
            cancellationToken);

        return Ok(TimetableEntryResponse.FromEntity(version));
    }

    [HttpDelete("slots/{slotId:guid}")]
    public async Task<IActionResult> DeleteSlot(Guid slotId, [FromQuery] DateOnly effectiveTo, CancellationToken cancellationToken)
    {
        var deleted = await timetable.DeleteSlotAsync(currentUser.UserId, slotId, effectiveTo, cancellationToken);
        return deleted ? NoContent() : NotFound();
    }
}

public sealed record UpsertTimetableSlotRequest(
    DayOfWeek DayOfWeek,
    TimeOnly StartTime,
    TimeOnly EndTime,
    SessionType SessionType,
    string? Label,
    DateOnly EffectiveFrom,
    ExpectedSessionInput? ExpectedSession);

public sealed record TimetableEntryResponse(
    Guid Id,
    Guid SlotId,
    DayOfWeek DayOfWeek,
    TimeOnly StartTime,
    TimeOnly EndTime,
    SessionType SessionType,
    string? Label,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    ExpectedSessionResponse? ExpectedSession)
{
    public static TimetableEntryResponse FromEntity(TimetableEntry entry) => new(
        entry.Id,
        entry.SlotId,
        entry.DayOfWeek,
        entry.StartTime,
        entry.EndTime,
        entry.SessionType,
        entry.Label,
        entry.EffectiveFrom,
        entry.EffectiveTo,
        entry.ExpectedSession is { } expected
            ? new ExpectedSessionResponse(expected.Summary, expected.TotalDistanceMeters, expected.PrimaryEffortZone)
            : null);
}

public sealed record ExpectedSessionResponse(string Summary, int TotalDistanceMeters, EffortZone PrimaryEffortZone);
