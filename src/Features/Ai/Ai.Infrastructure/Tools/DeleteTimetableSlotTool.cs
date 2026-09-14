using System.Text.Json;
using Ai.Application.Interfaces;
using Training.Application.Services;

namespace Ai.Infrastructure.Tools;

/// <summary>Ends a slot's history as of a given date - always gated, since it removes a scheduled commitment.</summary>
public sealed class DeleteTimetableSlotTool(TimetableService timetable) : IAiTool
{
    public string Name => "delete_timetable_slot";

    public string Description =>
        "Ends a recurring timetable slot as of a given date - no successor version is created, so the slot simply " +
        "stops occurring from that date onward. The slot's history before that date is preserved.";

    public bool RequiresApproval => true;

    public string JsonSchema => """
        {
          "type": "object",
          "required": ["slotId", "effectiveTo"],
          "properties": {
            "slotId": { "type": "string", "format": "uuid" },
            "effectiveTo": {
              "type": "string",
              "format": "date",
              "description": "ISO date (yyyy-MM-dd) - the slot's last active date."
            }
          }
        }
        """;

    public async Task<string> ExecuteAsync(Guid userId, string inputJson, CancellationToken cancellationToken)
    {
        var input = JsonSerializer.Deserialize<Input>(inputJson, ToolJsonOptions.Web)
            ?? throw new InvalidOperationException("Missing tool input.");

        var deleted = await timetable.DeleteSlotAsync(userId, input.SlotId, input.EffectiveTo, cancellationToken);
        return JsonSerializer.Serialize(new { deleted }, ToolJsonOptions.Web);
    }

    private sealed record Input(Guid SlotId, DateOnly EffectiveTo);
}
