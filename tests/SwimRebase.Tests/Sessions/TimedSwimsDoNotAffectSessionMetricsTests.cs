using System.Reflection;
using Sessions.Application.Interfaces;
using Sessions.Application.Metrics;
using Sessions.Domain.Models;

namespace SwimRebase.Tests.Sessions;

/// <summary>Timed swims have their own metrics; nothing about them may reach the session volume pipeline.</summary>
public class TimedSwimsDoNotAffectSessionMetricsTests
{
    [Fact]
    public void A_timed_swim_has_no_flag_that_could_opt_it_into_session_metrics()
    {
        Assert.Null(typeof(TimedSwim).GetProperty("IncludeInMetrics"));
        Assert.Null(typeof(MetricsService).Assembly.GetType("Sessions.Application.Metrics.TimedSwimFacts"));
    }

    [Fact]
    public void The_session_metrics_repository_contract_only_knows_fact_totals()
    {
        var methods = typeof(IMetricsRepository).GetMethods(BindingFlags.Public | BindingFlags.Instance);

        Assert.DoesNotContain(methods, m => m.GetParameters().Any(p => p.ParameterType == typeof(TimedSwim)) || m.ReturnType.ToString().Contains("TimedSwim"));
    }

    [Fact]
    public async Task Session_metrics_come_only_from_the_session_facts_they_are_given()
    {
        var totals = new List<FactTotal> { new(new DateOnly(2026, 11, 2), Guid.NewGuid(), Stroke.Free, SwimMode.Swim, 80, DistanceUnit.Metres, 400) };
        var service = new MetricsService(new InMemoryMetricsRepository(totals), new InMemoryPreferencesRepository());

        var result = await service.QueryAsync(Guid.NewGuid(), new MetricsQuery(null, null, null, null, null, null, [], MetricMeasure.Distance), default);

        Assert.Equal(400, result.TotalDistance);
    }
}
