using Ai.Application.Interfaces;
using Ai.Application.Models;

namespace Ai.Application.Services;

/// <summary>Executes one tool call and converts the outcome into a tool_result block, uniformly handling unknown tools and exceptions.</summary>
public sealed class ToolExecutor(IAiToolRegistry toolRegistry)
{
    public async Task<ToolResultContentBlock> ExecuteAsync(
        Guid userId, string toolUseId, string toolName, string inputJson, CancellationToken cancellationToken)
    {
        try
        {
            var tool = toolRegistry.Find(toolName)
                ?? throw new InvalidOperationException($"Unknown tool '{toolName}'.");

            var result = await tool.ExecuteAsync(userId, inputJson, cancellationToken);
            return new ToolResultContentBlock(toolUseId, result, false);
        }
        catch (Exception ex)
        {
            return new ToolResultContentBlock(toolUseId, $"Tool execution failed: {ex.Message}", true);
        }
    }
}
