using Ai.Application.Interfaces;
using Ai.Application.Models;
using Sessions.Application.Metrics;

namespace Ai.Infrastructure.Tools;

/// <summary>Read-only: answers "how much of X did I swim" from the user's saved sessions, so the model reports numbers instead of guessing.</summary>
public sealed class GetTrainingMetricsTool(MetricsService metrics) : IAiTool
{
    public string Name => MetricsTool.Name;

    public string DisplayName => "Checking your training";

    public ToolEffect Effect => ToolEffect.Neutral;

    public string Description => MetricsTool.Description;

    public string JsonSchema => MetricsTool.Schema;

    public bool RequiresApproval => false;

    public async Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, string inputJson, CancellationToken cancellationToken)
    {
        var result = await metrics.QueryAsync(context.UserId, MetricsTool.Parse(inputJson), cancellationToken);
        return new ToolExecutionResult(MetricsTool.Format(result), AffectedCount: result.Rows.Count);
    }
}
