namespace Ai.Application.Models;

/// <summary>
/// Everything needed to both replay a tool call to the model (<see cref="ToContentBlock"/>) and
/// render it richly in the UI - name/display name/effect for iconography and copy, <c>AffectedCount</c>
/// for a one-line summary ("Fetched 6 sessions"), the raw result for a details/expand view.
/// </summary>
public sealed record ToolResultSummary(
    string ToolUseId, string ToolName, string DisplayName, ToolEffect Effect, bool Success, int AffectedCount, string ResultJson)
{
    public ToolResultContentBlock ToContentBlock() => new(ToolUseId, ResultJson, !Success);
}
