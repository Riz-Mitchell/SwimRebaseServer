using Ai.Application.Interfaces;
using Ai.Application.Models;

namespace Ai.Application.Services;

/// <summary>Executes one tool call and converts the outcome into a <see cref="ToolResultSummary"/>, uniformly handling unknown tools and exceptions.</summary>
public sealed class ToolExecutor(IAiToolRegistry toolRegistry)
{
    public async Task<ToolResultSummary> ExecuteAsync(
        Guid userId, string toolUseId, string toolName, string inputJson, CancellationToken cancellationToken)
    {
        var tool = toolRegistry.Find(toolName);
        var displayName = tool?.DisplayName ?? toolName;
        var effect = tool?.Effect ?? ToolEffect.Neutral;

        try
        {
            if (tool is null)
            {
                throw new InvalidOperationException($"Unknown tool '{toolName}'.");
            }

            var result = await tool.ExecuteAsync(userId, inputJson, cancellationToken);
            return new ToolResultSummary(toolUseId, toolName, displayName, effect, Success: true, result.AffectedCount, result.ResultJson);
        }
        catch (Exception ex)
        {
            return new ToolResultSummary(toolUseId, toolName, displayName, effect, Success: false, AffectedCount: 0, $"Tool execution failed: {ex.Message}");
        }
    }
}
