using System.Text.Json;
using Ai.Application.Interfaces;
using Ai.Application.Models;
using Training.Application.Services;

namespace Ai.Infrastructure.Tools;

/// <summary>Permanently deletes a focus of any status - always gated, since it's a hard, irreversible delete.</summary>
public sealed class DeleteFocusTool(FocusService focuses) : IAiTool
{
    public string Name => "delete_focus";

    public string DisplayName => "Deleting a focus";

    public ToolEffect Effect => ToolEffect.Destructive;

    public string Description =>
        "Permanently deletes a focus - active, completed, or abandoned. This is a hard delete: the focus and its " +
        "check-in history are gone for good, not archived. If the focus was active, the remaining active focuses " +
        "are automatically re-ranked to close the gap.";

    public bool RequiresApproval => true;

    public string JsonSchema => """
        {
          "type": "object",
          "required": ["focusId"],
          "properties": {
            "focusId": { "type": "string", "format": "uuid" }
          }
        }
        """;

    public async Task<ToolExecutionResult> ExecuteAsync(Guid userId, string inputJson, CancellationToken cancellationToken)
    {
        var input = JsonSerializer.Deserialize<Input>(inputJson, ToolJsonOptions.Web)
            ?? throw new InvalidOperationException("Missing tool input.");

        var deleted = await focuses.DeleteAsync(userId, input.FocusId, cancellationToken);
        return new ToolExecutionResult(JsonSerializer.Serialize(new { deleted }, ToolJsonOptions.Web), AffectedCount: deleted ? 1 : 0);
    }

    private sealed record Input(Guid FocusId);
}
