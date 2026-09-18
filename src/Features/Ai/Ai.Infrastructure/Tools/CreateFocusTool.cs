using System.Text.Json;
using Ai.Application.Interfaces;
using Ai.Application.Models;
using Training.Application.Services;
using Training.Domain.Models;

namespace Ai.Infrastructure.Tools;

/// <summary>
/// Creates a focus on the user's behalf from their own description of what they want to work on -
/// always gated, since it commits the athlete to a new focus. The model is expected to turn the
/// user's own words into a short tagLine/prompt and a few concrete positive/negative points.
/// </summary>
public sealed class CreateFocusTool(FocusService focuses) : IAiTool
{
    public string Name => "create_focus";

    public string DisplayName => "Creating a focus";

    public ToolEffect Effect => ToolEffect.Creative;

    public string Description =>
        "Creates a new focus for the user from their own description of what they want to work on. " +
        "Write tagLine and prompt yourself based on what the user said - keep tagLine short and memorable, " +
        "and prompt a sentence or two elaborating on it. Include a few concrete positivePoints (what " +
        "happens if they stick to it) and negativePoints (what happens if they don't).";

    public bool RequiresApproval => true;

    public string JsonSchema => """
        {
          "type": "object",
          "required": ["userRequest", "tagLine", "prompt", "totalDays"],
          "properties": {
            "userRequest": { "type": "string", "description": "The user's own words describing what they want to focus on." },
            "tagLine": { "type": "string", "description": "A short, memorable line summarizing the focus." },
            "prompt": { "type": "string", "description": "A sentence or two elaborating on what the user should do or feel." },
            "totalDays": { "type": "integer", "description": "How many days of honoring this focus completes it." },
            "priority": { "type": "integer", "description": "Omit to add it after the user's other active focuses." },
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

    public async Task<ToolExecutionResult> ExecuteAsync(Guid userId, string inputJson, CancellationToken cancellationToken)
    {
        var input = JsonSerializer.Deserialize<Input>(inputJson, ToolJsonOptions.Web)
            ?? throw new InvalidOperationException("Missing tool input.");

        var focusInput = new FocusInput(
            input.TagLine,
            input.Prompt,
            input.TotalDays,
            input.Priority,
            ToPoints(input.PositivePoints),
            ToPoints(input.NegativePoints));

        var focus = await focuses.CreateAsync(userId, focusInput, FocusOrigin.Ai, input.UserRequest, cancellationToken);

        var resultJson = JsonSerializer.Serialize(
            new
            {
                id = focus.Id,
                tagLine = focus.TagLine,
                prompt = focus.Prompt,
                totalDays = focus.TotalDays,
                priority = focus.Priority,
            },
            ToolJsonOptions.Web);

        return new ToolExecutionResult(resultJson, AffectedCount: 1);
    }

    private static IReadOnlyList<ComparisonPointInput> ToPoints(IReadOnlyList<PointPayload>? points) =>
        points?.Select(p => new ComparisonPointInput(p.TagLine, p.Description)).ToList() ?? [];

    private sealed record Input(
        string UserRequest,
        string TagLine,
        string Prompt,
        int TotalDays,
        int? Priority,
        IReadOnlyList<PointPayload>? PositivePoints,
        IReadOnlyList<PointPayload>? NegativePoints);

    private sealed record PointPayload(string TagLine, string Description);
}
