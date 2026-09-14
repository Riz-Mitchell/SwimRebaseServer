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

/// <summary>Only valid when the slot's <see cref="SessionType"/> is <see cref="SessionType.Swim"/>.</summary>
public sealed record ExpectedSessionInput(string Summary, int TotalDistanceMeters, EffortZone PrimaryEffortZone);
