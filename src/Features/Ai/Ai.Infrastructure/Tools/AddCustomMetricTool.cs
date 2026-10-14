using Ai.Application.Interfaces;
using Ai.Application.Models;
using Sessions.Application.Metrics;
using Sessions.Application.Services;

namespace Ai.Infrastructure.Tools;

/// <summary>Gated: it adds a tile to the swimmer's home page, so they approve the name and filters first.</summary>
public sealed class AddCustomMetricTool(CustomMetricService metrics) : IAiTool
{
    public string Name => CustomMetricTool.Name;

    public string DisplayName => "Adding a metric to your home page";

    public ToolEffect Effect => ToolEffect.Creative;

    public string Description => CustomMetricTool.Description;

    public string JsonSchema => CustomMetricTool.Schema;

    public bool RequiresApproval => true;

    public async Task<ToolExecutionResult> ExecuteAsync(ToolExecutionContext context, string inputJson, CancellationToken cancellationToken)
    {
        try
        {
            var saved = await metrics.CreateAsync(context.UserId, CustomMetricTool.Parse(inputJson), cancellationToken);
            return new ToolExecutionResult(CustomMetricTool.Format(saved), AffectedCount: 1);
        }
        catch (Exception ex) when (ex is InvalidCustomMetricException or InvalidMetricsQueryException)
        {
            return new ToolExecutionResult($"Not saved: {ex.Message}", AffectedCount: 0);
        }
    }
}
