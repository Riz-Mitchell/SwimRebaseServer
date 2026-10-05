using System.Text.Json;
using Ai.Application.Interfaces;
using Ai.Application.Models;
using Training.Application.Services;

namespace Ai.Infrastructure.Tools;

/// <summary>
/// Replaces a focus's tagLine/prompt/totalDays/points wholesale - always gated, since it changes what
/// the user committed to. Priority is not editable here; use a dedicated priority move if needed.
/// Active-only: editing a completed or abandoned focus is rejected.
/// </summary>
public sealed class EditFocusTool(FocusService focuses) : IAiTool
{
    public string Name => "edit_focus";

    public string DisplayName => "Updating a focus";

    public ToolEffect Effect => ToolEffect.Neutral;

    public string Description =>
        "Replaces an existing active focus's tagLine, prompt, totalDays, and comparison points. This is a full " +
        "replacement, not a partial update - include every positivePoint and negativePoint you want the focus to " +
        "keep, not just the ones that changed. Does not change priority. Only works on a focus that is still " +
        "active; editing a completed or abandoned focus will fail.";

    public bool RequiresApproval => true;

    public string JsonSchema => """
        {
          "type": "object",
          "required": ["focusId", "tagLine", "prompt", "totalDays"],
          "properties": {
            "focusId": { "type": "string", "format": "uuid" },
            "tagLine": { "type": "string", "description": "A short, memorable line summarizing the focus." },
            "prompt": { "type": "string", "description": "A sentence or two elaborating on what the user should do or feel." },
            "totalDays": { "type": "integer", "description": "How many days of honoring this focus completes it." },
            "positivePoints": {
              "type": "array",
              "items": {
                "type": "object",
                "required": ["tagLine", "description"],
                "properties": {
                  "tagLine": { "type": "string" },
                  "description": { "type": "string" }
                }
              }
            },
            "negativePoints": {
              "type": "array",
              "items": {
                "type": "object",
                "required": ["tagLine", "description"],
                "properties": {
                  "tagLine": { "type": "string" },
                  "description": { "type": "string" }
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

        var editInput = new FocusEditInput(
            input.TagLine, input.Prompt, input.TotalDays, ToPoints(input.PositivePoints), ToPoints(input.NegativePoints));

        var focus = await focuses.EditAsync(context.UserId, input.FocusId, editInput, cancellationToken);

        if (focus is null)
        {
            return new ToolExecutionResult(JsonSerializer.Serialize(new { edited = false }, ToolJsonOptions.Web), AffectedCount: 0);
        }

        var resultJson = JsonSerializer.Serialize(
            new
            {
                edited = true,
                id = focus.Id,
                tagLine = focus.TagLine,
                prompt = focus.Prompt,
                totalDays = focus.TotalDays,
            },
            ToolJsonOptions.Web);

        return new ToolExecutionResult(
            resultJson,
            AffectedCount: 1,
            [new ToolResource(ToolResourceKinds.Focus, focus.Id, ToolResourceActions.Updated, focus.TagLine)]);
    }

    private static IReadOnlyList<ComparisonPointInput> ToPoints(IReadOnlyList<PointPayload>? points) =>
        points?.Select(p => new ComparisonPointInput(p.TagLine, p.Description)).ToList() ?? [];

    private sealed record Input(
        Guid FocusId,
        string TagLine,
        string Prompt,
        int TotalDays,
        IReadOnlyList<PointPayload>? PositivePoints,
        IReadOnlyList<PointPayload>? NegativePoints);

    private sealed record PointPayload(string TagLine, string Description);
}
