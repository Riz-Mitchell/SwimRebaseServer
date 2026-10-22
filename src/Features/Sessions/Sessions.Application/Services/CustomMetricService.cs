using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Sessions.Application.Interfaces;
using Sessions.Application.Metrics;
using Sessions.Domain.Models;
using Shared.Application;

namespace Sessions.Application.Services;

public sealed class CustomMetricService(
    ICustomMetricRepository metrics,
    [FromKeyedServices(SessionsKeys.UnitOfWork)] IUnitOfWork unitOfWork,
    ILogger<CustomMetricService>? logger = null)
{
    private readonly ILogger<CustomMetricService> _logger = logger ?? NullLogger<CustomMetricService>.Instance;

    public const int MaxMetrics = 12;

    public const int MaxNameLength = 60;

    public async Task<IReadOnlyList<CustomMetricView>> ListAsync(Guid userId, CancellationToken cancellationToken)
    {
        var found = await metrics.ListAsync(userId, cancellationToken);
        _logger.LogDebug("[{ClassName}] Listed {Count} custom metric(s) for user {UserId}", nameof(CustomMetricService), found.Count, userId);
        return found.Select(CustomMetricView.From).ToList();
    }

    /// <exception cref="InvalidCustomMetricException">The definition is not usable, or the swimmer is at the limit.</exception>
    /// <exception cref="InvalidMetricsQueryException">The filters cannot be answered as asked.</exception>
    public async Task<CustomMetricView> CreateAsync(Guid userId, CustomMetricDefinition definition, CancellationToken cancellationToken)
    {
        Validate(userId, definition);
        var existing = await metrics.ListAsync(userId, cancellationToken);
        if (existing.Count >= MaxMetrics)
        {
            _logger.LogWarning("[{ClassName}] User {UserId} is at the limit of {MaxMetrics} custom metrics", nameof(CustomMetricService), userId, MaxMetrics);
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
        _logger.LogInformation("[{ClassName}] Created custom metric {MetricId} '{Name}' for user {UserId}", nameof(CustomMetricService), metric.Id, definition.Name, userId);
        return CustomMetricView.From(metric);
    }

    /// <returns>Null when the swimmer has no such metric.</returns>
    public async Task<CustomMetricView?> UpdateAsync(Guid userId, Guid id, CustomMetricDefinition definition, CancellationToken cancellationToken)
    {
        Validate(userId, definition);
        if (await metrics.FindAsync(userId, id, cancellationToken) is not { } metric)
        {
            _logger.LogWarning("[{ClassName}] User {UserId} tried to update custom metric {MetricId}, which does not exist for them", nameof(CustomMetricService), userId, id);
            return null;
        }

        metric.Apply(definition);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("[{ClassName}] Updated custom metric {MetricId} for user {UserId}", nameof(CustomMetricService), id, userId);
        return CustomMetricView.From(metric);
    }

    public async Task<bool> DeleteAsync(Guid userId, Guid id, CancellationToken cancellationToken)
    {
        if (await metrics.FindAsync(userId, id, cancellationToken) is not { } metric)
        {
            _logger.LogWarning("[{ClassName}] User {UserId} tried to delete custom metric {MetricId}, which does not exist for them", nameof(CustomMetricService), userId, id);
            return false;
        }

        metrics.Remove(metric);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("[{ClassName}] Deleted custom metric {MetricId} for user {UserId}", nameof(CustomMetricService), id, userId);
        return true;
    }

    private void Validate(Guid userId, CustomMetricDefinition definition)
    {
        try
        {
            ValidateDefinition(definition);
        }
        catch (Exception ex) when (ex is InvalidCustomMetricException or InvalidMetricsQueryException)
        {
            _logger.LogWarning("[{ClassName}] Rejected custom metric definition from user {UserId}: {Reason}", nameof(CustomMetricService), userId, ex.Message);
            throw;
        }
    }

    private static void ValidateDefinition(CustomMetricDefinition definition)
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
