using Sessions.Application.Metrics;
using Sessions.Domain.Models;

namespace SwimRebase.Tests.Sessions;

public class TimedSwimFactsTests
{
    private static TimedSwim Swim(bool includeInMetrics, Guid? sessionId) => new()
    {
        Id = Guid.NewGuid(),
        OwnerId = Guid.NewGuid(),
        SessionId = sessionId,
        PerformedOn = new DateOnly(2026, 9, 27),
        Course = PoolCourse.Scm,
        Dist = 50,
        Stroke = Stroke.Free,
        Mode = SwimMode.Swim,
        Seconds = 30,
        Intensity = 90,
        IncludeInMetrics = includeInMetrics,
    };

    [Fact]
    public void A_linked_and_opted_in_swim_contributes()
    {
        var sessionId = Guid.NewGuid();
        var totals = TimedSwimFacts.ToFactTotals([Swim(includeInMetrics: true, sessionId)]).ToList();

        var total = Assert.Single(totals);
        Assert.Equal(sessionId, total.SessionId);
        Assert.Equal(50, total.Distance);
        Assert.Equal(Stroke.Free, total.Stroke);
        Assert.Equal(90, total.Intensity);
    }

    [Fact]
    public void A_standalone_swim_never_contributes_even_when_opted_in()
    {
        Assert.Empty(TimedSwimFacts.ToFactTotals([Swim(includeInMetrics: true, sessionId: null)]));
    }

    [Fact]
    public void A_linked_swim_that_is_not_opted_in_does_not_contribute()
    {
        Assert.Empty(TimedSwimFacts.ToFactTotals([Swim(includeInMetrics: false, Guid.NewGuid())]));
    }
}
