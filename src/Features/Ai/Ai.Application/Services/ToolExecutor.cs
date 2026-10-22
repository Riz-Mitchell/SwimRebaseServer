using Ai.Application.Interfaces;
using Ai.Application.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ai.Application.Services;

/// <summary>Executes one tool call and converts the outcome into a <see cref="ToolResultSummary"/>, uniformly handling unknown tools and exceptions.</summary>
public sealed class ToolExecutor(IAiToolRegistry toolRegistry, ILogger<ToolExecutor>? logger = null)
{
    private readonly ILogger<ToolExecutor> _logger = logger ?? NullLogger<ToolExecutor>.Instance;

    public async Task<ToolResultSummary> ExecuteAsync(
        Guid userId, Guid conversationId, string toolUseId, string toolName, string inputJson, CancellationToken cancellationToken)
    {
        var definition = toolRegistry.Find(toolName);
        var displayName = definition?.DisplayName ?? toolName;
        var effect = definition?.Effect ?? ToolEffect.Neutral;

        try
        {
            var tool = definition as IAiTool;
            if (tool is null)
            {
                _logger.LogError("[{ClassName}] Model called unknown or non-executable tool '{ToolName}' in conversation {ConversationId}", nameof(ToolExecutor), toolName, conversationId);
                throw new InvalidOperationException($"Unknown or non-executable tool '{toolName}'.");
            }

            var result = await tool.ExecuteAsync(new ToolExecutionContext(userId, conversationId, toolUseId), inputJson, cancellationToken);
            return new ToolResultSummary(toolUseId, toolName, displayName, effect, Success: true, result.AffectedCount, result.ResultJson, result.Resources);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[{ClassName}] Tool {ToolName} failed for user {UserId} in conversation {ConversationId}", nameof(ToolExecutor), toolName, userId, conversationId);
            return new ToolResultSummary(toolUseId, toolName, displayName, effect, Success: false, AffectedCount: 0, $"Tool execution failed: {ex.Message}");
        }
    }
}
