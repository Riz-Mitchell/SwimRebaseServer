using System.Text.Json;
using Ai.Application.Interfaces;
using Ai.Application.Models;
using Training.Application.Services;

namespace Ai.Infrastructure.Tools;

/// <summary>
/// Read-only - never requires approval. Lets the model see what the athlete's timetable looked
/// like on any date; the timetable is versioned, so this answers past and future questions too,
/// not just "today's schedule".
/// </summary>
public sealed class GetTimetableTool(TimetableService timetable) : IAiTool
{
    public string Name => "get_timetable";

    public string DisplayName => "Checking your timetable";

    public ToolEffect Effect => ToolEffect.Neutral;

    public string Description =>
        "Gets the athlete's training timetable (recurring weekly slots: Swim/Gym/Dryland/Other) as it stood on a " +
        "given date. Swim slots may carry an expectedSession (a short summary, total distance, and primary effort " +
        "zone) describing what that session is roughly meant to be.";

    public bool RequiresApproval => false;

    public string JsonSchema => """
        {
          "type": "object",
          "properties": {
            "asOf": {
              "type": "string",
              "format": "date",
              "description": "ISO date (yyyy-MM-dd) to view the timetable as of. Defaults to today if omitted."
            }
          }
        }
        """;

    public async Task<ToolExecutionResult> ExecuteAsync(Guid userId, string inputJson, CancellationToken cancellationToken)
    {
        var input = string.IsNullOrWhiteSpace(inputJson)
            ? new Input(null)
            : JsonSerializer.Deserialize<Input>(inputJson, ToolJsonOptions.Web) ?? new Input(null);

        var asOf = input.AsOf ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var entries = await timetable.GetTimetableAsOfAsync(userId, asOf, cancellationToken);

        var result = new
        {
            asOf,
            entries = entries.Select(e => new
            {
                slotId = e.SlotId,
                dayOfWeek = e.DayOfWeek.ToString(),
                startTime = e.StartTime.ToString("HH:mm"),
                endTime = e.EndTime.ToString("HH:mm"),
                sessionType = e.SessionType.ToString(),
                label = e.Label,
                expectedSession = e.ExpectedSession is { } expected
                    ? new
                    {
                        summary = expected.Summary,
                        totalDistanceMeters = expected.TotalDistanceMeters,
                        primaryEffortZone = expected.PrimaryEffortZone.ToString(),
                    }
                    : null,
            }),
        };

        return new ToolExecutionResult(JsonSerializer.Serialize(result, ToolJsonOptions.Web), entries.Count);
    }

    private sealed record Input(DateOnly? AsOf);
}
