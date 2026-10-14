using Microsoft.Extensions.DependencyInjection;
using Sessions.Application.Interfaces;
using Sessions.Application.Metrics;
using Sessions.Domain.Models;
using Shared.Application;

namespace Sessions.Application.Services;

public sealed class CustomMetricService(
    ICustomMetricRepository metrics,
    [FromKeyedServices(SessionsKeys.UnitOfWork)] IUnitOfWork unitOfWork)
{
    public const int MaxMetrics = 12;

    public const int MaxNameLength = 60;

    public async Task<IReadOnlyList<CustomMetricView>> ListAsync(Guid userId, CancellationToken cancellationToken) =>
        (await metrics.ListAsync(userId, cancellationToken)).Select(CustomMetricView.From).ToList();

    /// <exception cref="InvalidCustomMetricException">The definition is not usable, or the swimmer is at the limit.</exception>
    /// <exception cref="InvalidMetricsQueryException">The filters cannot be answered as asked.</exception>
    public async Task<CustomMetricView> CreateAsync(Guid userId, CustomMetricDefinition definition, CancellationToken cancellationToken)
    {
        Validate(definition);
        var existing = await metrics.ListAsync(userId, cancellationToken);
        if (existing.Count >= MaxMetrics)
        {
            throw new InvalidCustomMetricException($"you can keep up to {MaxMetrics} metrics; remove one first");
        }

        var metric = new CustomMetric
        {
            Id = Guid.NewGuid(),
            OwnerId = userId,
            Position = existing.Count == 0 ? 0 : existing.Max(m => m.Position) + 1,
        };
        metric.Apply(definition);
        metrics.Add(metric);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return CustomMetricView.From(metric);
    }

    /// <returns>Null when the swimmer has no such metric.</returns>
    public async Task<CustomMetricView?> UpdateAsync(Guid userId, Guid id, CustomMetricDefinition definition, CancellationToken cancellationToken)
    {
        Validate(definition);
        if (await metrics.FindAsync(userId, id, cancellationToken) is not { } metric)
        {
            return null;
        }

        metric.Apply(definition);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return CustomMetricView.From(metric);
    }

    public async Task<bool> DeleteAsync(Guid userId, Guid id, CancellationToken cancellationToken)
    {
        if (await metrics.FindAsync(userId, id, cancellationToken) is not { } metric)
        {
            return false;
        }

        metrics.Remove(metric);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static void Validate(CustomMetricDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.Name) || definition.Name.Trim().Length > MaxNameLength)
        {
            throw new InvalidCustomMetricException($"name must be 1-{MaxNameLength} characters");
        }

        if (!Enum.IsDefined(definition.Measure) || !Enum.IsDefined(definition.Window))
        {
            throw new InvalidCustomMetricException("unknown measure or window");
        }

        if (definition.GroupBy is not (null or MetricDimension.Day or MetricDimension.Week or MetricDimension.Month))
        {
            throw new InvalidCustomMetricException("a series can only be grouped by day, week or month");
        }

        if (definition.MinPercent is < 1 or > 100 || definition.MaxPercent is < 1 or > 100)
        {
            throw new InvalidCustomMetricException("intensity bounds are percents from 1 to 100");
        }

        // The calculator owns the rules for filters (min above max), so a metric that would never answer is refused here.
        MetricsCalculator.Compute(CustomMetricQuery.For(definition, null, null), []);
    }
}
