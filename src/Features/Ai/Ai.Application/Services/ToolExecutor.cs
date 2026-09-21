using Ai.Application.Interfaces;
using Ai.Application.Models;

namespace Ai.Application.Services;

/// <summary>Executes one tool call and converts the outcome into a <see cref="ToolResultSummary"/>, uniformly handling unknown tools and exceptions.</summary>
public sealed class ToolExecutor(IAiToolRegistry toolRegistry)
{
    public async Task<ToolResultSummary> ExecuteAsync(
        Guid userId, Guid conversationId, string toolUseId, string toolName, string inputJson, CancellationToken cancellationToken)
    {
        var definition = toolRegistry.Find(toolName);
        var displayName = definition?.DisplayName ?? toolName;
        var effect = definition?.Effect ?? ToolEffect.Neutral;

        try
        {
            var tool = definition as IAiTool
                ?? throw new InvalidOperationException($"Unknown or non-executable tool '{toolName}'.");

            var result = await tool.ExecuteAsync(new ToolExecutionContext(userId, conversationId, toolUseId), inputJson, cancellationToken);
            return new ToolResultSummary(toolUseId, toolName, displayName, effect, Success: true, result.AffectedCount, result.ResultJson);
        }
        catch (Exception ex)
        {
            return new ToolResultSummary(toolUseId, toolName, displayName, effect, Success: false, AffectedCount: 0, $"Tool execution failed: {ex.Message}");
        }
    }
}
