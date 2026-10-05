using System.Globalization;
using System.Text.Json;
using Ai.Application.Interfaces;
using Ai.Application.Models;
using Training.Application.Services;
using Training.Domain.Models;

namespace Ai.Infrastructure.Tools;

/// <summary>
/// Writes a timetable slot - always gated, since it commits the athlete to a schedule change.
/// Creates a new slot when no slotId is given, or a new version of an existing one otherwise;
/// never mutates a slot's history in place (see <see cref="TimetableService.UpsertSlotAsync"/>).
/// </summary>
public sealed class UpsertTimetableSlotTool(TimetableService timetable) : IAiTool
{
    public string Name => "upsert_timetable_slot";

    public string DisplayName => "Updating your timetable";

    // The versioned timetable model never mutates a slot in place - even "editing" an existing slot
    // (by passing its slotId) closes the old version and inserts a new one, so this is always creative.
    public ToolEffect Effect => ToolEffect.Creative;

    public string Description =>
        "Creates a new recurring timetable slot, or a new version of an existing one (pass the same slotId to " +
        "edit it - this closes the previous version rather than overwriting it, preserving history). " +
        "expectedSession is only valid when sessionType is Swim; it will be rejected for any other session type.";

    public bool RequiresApproval => true;

    public string JsonSchema => """
        {
          "type": "object",
          "required": ["dayOfWeek", "startTime", "endTime", "sessionType", "effectiveFrom"],
          "properties": {
            "slotId": {
              "type": "string",
              "format": "uuid",
              "description": "Omit to create a new slot; provide an existing slotId to create a new version of it."
            },
            "dayOfWeek": { "type": "string", "enum": ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"] },
            "startTime": { "type": "string", "description": "24-hour time, e.g. 07:00" },
            "endTime": { "type": "string", "description": "24-hour time, e.g. 09:00" },
            "sessionType": { "type": "string", "enum": ["Swim", "Gym", "Dryland", "Other"] },
            "label": { "type": "string" },
            "effectiveFrom": { "type": "string", "format": "date", "description": "ISO date (yyyy-MM-dd) this version takes effect from." },
            "expectedSession": {
              "type": "object",
              "description": "Only valid when sessionType is Swim.",
              "required": ["summary", "totalDistanceMeters", "primaryEffortZone"],
              "properties": {
                "summary": { "type": "string" },
                "totalDistanceMeters": { "type": "integer" },
                "primaryEffortZone": { "type": "string", "enum": ["Easy", "Moderate", "Threshold", "Sprint", "Max"] }
              }
            }
          }
        }
        """;

    public async Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, string inputJson, CancellationToken cancellationToken)
    {
        var input = JsonSerializer.Deserialize<Input>(inputJson, ToolJsonOptions.Web)
            ?? throw new InvalidOperationException("Missing tool input.");

        var expectedSession = input.ExpectedSession is { } expected
            ? new ExpectedSessionInput(expected.Summary, expected.TotalDistanceMeters, expected.PrimaryEffortZone)
            : null;

        var slotInput = new TimetableSlotInput(
            input.DayOfWeek, ParseTime(input.StartTime), ParseTime(input.EndTime), input.SessionType, input.Label, expectedSession);

        var version = await timetable.UpsertSlotAsync(context.UserId, input.SlotId, slotInput, input.EffectiveFrom, cancellationToken);

        var resultJson = JsonSerializer.Serialize(
            new
            {
                id = version.Id,
                slotId = version.SlotId,
                dayOfWeek = version.DayOfWeek.ToString(),
                startTime = version.StartTime.ToString("HH:mm"),
                endTime = version.EndTime.ToString("HH:mm"),
                sessionType = version.SessionType.ToString(),
                label = version.Label,
                effectiveFrom = version.EffectiveFrom,
            },
            ToolJsonOptions.Web);

        var action = input.SlotId is null ? ToolResourceActions.Created : ToolResourceActions.Updated;
        return new ToolExecutionResult(
            resultJson,
            AffectedCount: 1,
            [new ToolResource(ToolResourceKinds.TimetableSlot, version.SlotId, action, version.Label)]);
    }

    private static TimeOnly ParseTime(string value) =>
        TimeOnly.TryParse(value, CultureInfo.InvariantCulture, out var time)
            ? time
            : throw new FormatException($"'{value}' is not a valid time (expected HH:mm).");

    private sealed record Input(
        Guid? SlotId,
        DayOfWeek DayOfWeek,
        string StartTime,
        string EndTime,
        SessionType SessionType,
        string? Label,
        DateOnly EffectiveFrom,
        ExpectedSessionPayload? ExpectedSession);

    private sealed record ExpectedSessionPayload(string Summary, int TotalDistanceMeters, EffortZone PrimaryEffortZone);
}
