using Training.Domain.Models;

namespace Training.Application.Services;

/// <summary>Input for creating or editing a recurring timetable slot.</summary>
public sealed record TimetableSlotInput(
    DayOfWeek DayOfWeek,
    TimeOnly StartTime,
    TimeOnly EndTime,
    SessionType SessionType,
    string? Label,
    ExpectedSessionInput? ExpectedSession);

/// <summary>One slot in a bulk upsert: a new slot when <paramref name="SlotId"/> is null, otherwise a new version of that slot.</summary>
public sealed record TimetableSlotUpsert(Guid? SlotId, TimetableSlotInput Input);

/// <summary>Only valid when the slot's <see cref="SessionType"/> is <see cref="SessionType.Swim"/>.</summary>
public sealed record ExpectedSessionInput(string Summary, int TotalDistanceMeters, EffortZone PrimaryEffortZone);
