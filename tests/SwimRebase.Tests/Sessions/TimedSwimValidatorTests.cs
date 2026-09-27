using Sessions.Domain.Calculations;
using Sessions.Domain.Models;

namespace SwimRebase.Tests.Sessions;

public class TimedSwimValidatorTests
{
    private static Split Split(int dist, double seconds, int? rate = null) => new() { DistanceFromStart = dist, Seconds = seconds, StrokeRateCyclesPerMinute = rate };

    [Fact]
    public void A_bare_time_with_no_splits_is_valid()
    {
        Assert.Empty(TimedSwimValidator.Validate(dist: 50, seconds: 32.1, resistance: null, splits: []));
    }

    [Fact]
    public void Dist_and_seconds_must_be_positive()
    {
        var errors = TimedSwimValidator.Validate(dist: 0, seconds: 0, resistance: null, splits: []);

        Assert.Contains(errors, e => e.Contains("dist"));
        Assert.Contains(errors, e => e.Contains("seconds"));
    }

    [Fact]
    public void Resistance_without_a_type_is_rejected()
    {
        var errors = TimedSwimValidator.Validate(dist: 50, seconds: 32, new Resistance { Type = "" }, splits: []);

        Assert.Contains(errors, e => e.Contains("resistance type"));
    }

    [Fact]
    public void Splits_must_increase_in_both_distance_and_time_and_stay_short_of_the_finish()
    {
        var valid = TimedSwimValidator.Validate(dist: 200, seconds: 122, null, [Split(100, 60), Split(150, 90, rate: 32)]);
        Assert.Empty(valid);

        var outOfOrderDistance = TimedSwimValidator.Validate(dist: 200, seconds: 122, null, [Split(100, 60), Split(100, 90)]);
        Assert.Contains(outOfOrderDistance, e => e.Contains("distanceFromStart"));

        var outOfOrderTime = TimedSwimValidator.Validate(dist: 200, seconds: 122, null, [Split(100, 60), Split(150, 50)]);
        Assert.Contains(outOfOrderTime, e => e.Contains("seconds must increase"));

        var reachesTheFinish = TimedSwimValidator.Validate(dist: 200, seconds: 122, null, [Split(200, 100)]);
        Assert.Contains(reachesTheFinish, e => e.Contains("finish time is the swim's own seconds"));

        var beyondTheFinish = TimedSwimValidator.Validate(dist: 200, seconds: 122, null, [Split(100, 130)]);
        Assert.Contains(beyondTheFinish, e => e.Contains("less than the swim's total seconds"));
    }

    [Fact]
    public void A_stroke_rate_of_zero_or_less_is_rejected()
    {
        var errors = TimedSwimValidator.Validate(dist: 200, seconds: 122, null, [Split(100, 60, rate: 0)]);

        Assert.Contains(errors, e => e.Contains("strokeRateCyclesPerMinute"));
    }
}
