using System.Globalization;
using System.Text.Json;
using Ai.Application.Interfaces;
using Ai.Application.Models;
using Training.Application.Services;
using Training.Domain.Models;

namespace Ai.Infrastructure.Tools;

/// <summary>
/// Writes several timetable slots behind a single approval, e.g. a whole week read from a photo. All slots share one
/// effectiveFrom and are saved together, so a rejected slot leaves the timetable untouched.
/// </summary>
public sealed class UpsertTimetableSlotsTool(TimetableService timetable) : IAiTool, IToolPreview
{
    public string Name => "upsert_timetable_slots";

    public string DisplayName => "Updating your timetable";

    public ToolEffect Effect => ToolEffect.Creative;

    public string Description =>
        "Creates or versions several recurring timetable slots in one call, with a single approval for the whole set. " +
        "Prefer this over upsert_timetable_slot whenever more than one slot is being written. Each slot is a new slot, " +
        "or pass its slotId to create a new version of an existing one. All slots take effect from the same effectiveFrom. " +
        "expectedSession is only valid when sessionType is Swim. If any slot is invalid none are saved.";

    public bool RequiresApproval => true;

    public string JsonSchema => """
        {
          "type": "object",
          "required": ["effectiveFrom", "slots"],
          "properties": {
            "effectiveFrom": { "type": "string", "format": "date", "description": "ISO date (yyyy-MM-dd) every slot takes effect from." },
            "slots": {
              "type": "array",
              "minItems": 1,
              "items": {
                "type": "object",
                "required": ["dayOfWeek", "startTime", "endTime", "sessionType"],
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
            }
          }
        }
        """;

    public async Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, string inputJson, CancellationToken cancellationToken)
    {
        var input = JsonSerializer.Deserialize<Input>(inputJson, ToolJsonOptions.Web)
            ?? throw new InvalidOperationException("Missing tool input.");

        var slots = (input.Slots ?? []).Select(ToUpsert).ToList();
        var versions = await timetable.UpsertSlotsAsync(context.UserId, slots, input.EffectiveFrom, cancellationToken);

        var resultJson = JsonSerializer.Serialize(
            new
            {
                effectiveFrom = input.EffectiveFrom,
                slots = versions.Select(v => new
                {
                    id = v.Id,
                    slotId = v.SlotId,
                    dayOfWeek = v.DayOfWeek.ToString(),
                    startTime = v.StartTime.ToString("HH:mm"),
                    endTime = v.EndTime.ToString("HH:mm"),
                    sessionType = v.SessionType.ToString(),
                    label = v.Label,
                }),
            },
            ToolJsonOptions.Web);

        var resources = versions
            .Zip(slots, (version, slot) => new ToolResource(
                ToolResourceKinds.TimetableSlot,
                version.SlotId,
                slot.SlotId is null ? ToolResourceActions.Created : ToolResourceActions.Updated,
                version.Label))
            .ToList();

        return new ToolExecutionResult(resultJson, AffectedCount: versions.Count, resources);
    }

    public Task<string?> PreviewAsync(Guid userId, string inputJson, CancellationToken cancellationToken)
    {
        try
        {
            var input = JsonSerializer.Deserialize<Input>(inputJson, ToolJsonOptions.Web);
            if (input?.Slots is not { Count: > 0 } slots)
            {
                return Task.FromResult<string?>(null);
            }

            var lines = slots
                .OrderBy(s => ((int)s.DayOfWeek + 6) % 7)
                .ThenBy(s => s.StartTime, StringComparer.Ordinal)
                .Select(s => $"{s.DayOfWeek} {s.StartTime}-{s.EndTime} {s.SessionType}{(string.IsNullOrWhiteSpace(s.Label) ? "" : $" - {s.Label}")}");

            return Task.FromResult<string?>($"From {input.EffectiveFrom:yyyy-MM-dd}:\n{string.Join('\n', lines)}");
        }
        catch (JsonException)
        {
            return Task.FromResult<string?>(null);
        }
    }

    private static TimetableSlotUpsert ToUpsert(SlotPayload slot)
    {
        var expectedSession = slot.ExpectedSession is { } expected
            ? new ExpectedSessionInput(expected.Summary, expected.TotalDistanceMeters, expected.PrimaryEffortZone)
            : null;

        return new TimetableSlotUpsert(
            slot.SlotId,
            new TimetableSlotInput(
                slot.DayOfWeek, ParseTime(slot.StartTime), ParseTime(slot.EndTime), slot.SessionType, slot.Label, expectedSession));
    }

    private static TimeOnly ParseTime(string value) =>
        TimeOnly.TryParse(value, CultureInfo.InvariantCulture, out var time)
            ? time
            : throw new FormatException($"'{value}' is not a valid time (expected HH:mm).");

    private sealed record Input(DateOnly EffectiveFrom, IReadOnlyList<SlotPayload>? Slots);

    private sealed record SlotPayload(
        Guid? SlotId,
        DayOfWeek DayOfWeek,
        string StartTime,
        string EndTime,
        SessionType SessionType,
        string? Label,
        ExpectedSessionPayload? ExpectedSession);

    private sealed record ExpectedSessionPayload(string Summary, int TotalDistanceMeters, EffortZone PrimaryEffortZone);
}
