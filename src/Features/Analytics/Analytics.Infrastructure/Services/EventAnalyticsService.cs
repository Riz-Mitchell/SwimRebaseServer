using Analytics.Application.Interfaces;
using Analytics.Application.Models;
using Microsoft.EntityFrameworkCore;

namespace Analytics.Infrastructure.Services;

/// <summary>
/// Ad hoc reporting queries against <see cref="AnalyticsDbContext"/>. Uses raw SQL (a first for this
/// codebase) rather than LINQ - cohort retention in particular (each user's earliest-ever event
/// bucketed against every later event's week offset) isn't reasonably expressible in LINQ, and the
/// other two queries use raw SQL too for consistency. Every SQL column is aliased in quotes to match
/// its destination record property exactly, since <see cref="Microsoft.EntityFrameworkCore.RelationalQueryableExtensions.SqlQuery{TResult}"/>
/// matches unmapped result shapes by column name, not through the snake_case naming convention
/// applied to normal entity mappings.
/// </summary>
public sealed class EventAnalyticsService(AnalyticsDbContext db) : IEventAnalyticsService
{
    public async Task<IReadOnlyList<WeeklyActiveUserCount>> GetWeeklyActiveUserCountsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var rows = await db.Database.SqlQuery<WeeklyActiveUserCount>($"""
            SELECT date_trunc('week', occurred_at)::date AS "WeekStart",
                   COUNT(DISTINCT user_id)::int AS "DistinctUserCount"
            FROM event_logs
            WHERE occurred_at >= {from} AND occurred_at < {to}
            GROUP BY 1
            ORDER BY 1
            """).ToListAsync(cancellationToken);

        return rows;
    }

    public async Task<IReadOnlyList<FeatureUsageCount>> GetFeatureUsageCountsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var rows = await db.Database.SqlQuery<FeatureUsageCount>($"""
            SELECT event_type AS "EventType",
                   COUNT(*)::int AS "TotalCount",
                   COUNT(DISTINCT user_id)::int AS "DistinctUserCount"
            FROM event_logs
            WHERE occurred_at >= {from} AND occurred_at < {to}
            GROUP BY event_type
            ORDER BY event_type
            """).ToListAsync(cancellationToken);

        return rows;
    }

    public async Task<CohortRetentionResult> GetCohortRetentionAsync(CancellationToken cancellationToken)
    {
        var rows = await db.Database.SqlQuery<CohortRetentionRawRow>($"""
            WITH user_cohort AS (
                SELECT user_id, date_trunc('week', MIN(occurred_at))::date AS cohort_week
                FROM event_logs
                GROUP BY user_id
            ),
            user_activity AS (
                SELECT DISTINCT e.user_id, uc.cohort_week,
                       date_trunc('week', e.occurred_at)::date AS activity_week
                FROM event_logs e
                JOIN user_cohort uc ON uc.user_id = e.user_id
            )
            SELECT cohort_week AS "CohortWeekStart",
                   ((activity_week - cohort_week) / 7)::int AS "WeeksSinceCohort",
                   COUNT(DISTINCT user_id)::int AS "ActiveUserCount"
            FROM user_activity
            GROUP BY cohort_week, ((activity_week - cohort_week) / 7)
            ORDER BY cohort_week, ((activity_week - cohort_week) / 7)
            """).ToListAsync(cancellationToken);

        // Week 0 of a user's own cohort always has every one of that cohort's users active in it
        // (by construction - it's their earliest event), so it doubles as the cohort's total size.
        var cohorts = rows
            .GroupBy(r => r.CohortWeekStart)
            .Select(g => new CohortRow(
                g.Key,
                g.First(r => r.WeeksSinceCohort == 0).ActiveUserCount,
                g.ToDictionary(r => r.WeeksSinceCohort, r => r.ActiveUserCount)))
            .ToList();

        return new CohortRetentionResult(cohorts);
    }

    private sealed record CohortRetentionRawRow(DateOnly CohortWeekStart, int WeeksSinceCohort, int ActiveUserCount);
}
