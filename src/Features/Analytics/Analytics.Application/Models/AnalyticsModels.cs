namespace Analytics.Application.Models;

public sealed record WeeklyActiveUserCount(DateOnly WeekStart, int DistinctUserCount);

public sealed record FeatureUsageCount(string EventType, int TotalCount, int DistinctUserCount);

/// <summary>One signup-week cohort's retention: how many of its users were still active N weeks later.</summary>
public sealed record CohortRow(DateOnly CohortWeekStart, int CohortSize, IReadOnlyDictionary<int, int> ActiveUsersByWeeksSinceCohort);

public sealed record CohortRetentionResult(IReadOnlyList<CohortRow> Cohorts);
