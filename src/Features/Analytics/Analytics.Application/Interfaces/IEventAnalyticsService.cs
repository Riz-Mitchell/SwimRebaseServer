using Analytics.Application.Models;

namespace Analytics.Application.Interfaces;

/// <summary>Read-only reporting queries over the event log - no writes here, see Shared.Application.IEventTracker for that.</summary>
public interface IEventAnalyticsService
{
    Task<IReadOnlyList<WeeklyActiveUserCount>> GetWeeklyActiveUserCountsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);

    Task<IReadOnlyList<FeatureUsageCount>> GetFeatureUsageCountsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);

    /// <summary>
    /// Buckets every user by the week of their earliest-ever event (their "cohort"), then for each
    /// cohort reports how many of its users were still active in each subsequent week.
    /// </summary>
    Task<CohortRetentionResult> GetCohortRetentionAsync(CancellationToken cancellationToken);
}
