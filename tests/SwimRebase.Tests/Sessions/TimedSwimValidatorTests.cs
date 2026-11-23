using Sessions.Domain.Calculations;
using Sessions.Domain.Models;

namespace SwimRebase.Tests.Sessions;

public class TimedSwimValidatorTests
{
    private static Split Split(decimal dist, double seconds, int? rate = null) => new() { DistanceFromStart = dist, Seconds = seconds, StrokeRateCyclesPerMinute = rate };

    [Fact]
    public void A_bare_time_with_no_splits_is_valid()
    {
        Assert.Empty(TimedSwimValidator.Validate(dist: 50, seconds: 32.1, resistances: [], splits: []));
    }

    [Fact]
    public void A_decimal_dist_and_split_are_valid()
    {
        Assert.Empty(TimedSwimValidator.Validate(dist: 12.5m, seconds: 8.4, resistances: [], splits: [Split(6.25m, 4.1)]));
    }

    [Fact]
    public void More_than_two_decimal_places_are_rejected()
    {
        var errors = TimedSwimValidator.Validate(dist: 12.555m, seconds: 8.4, resistances: [], splits: [Split(6.255m, 4.1)]);

        Assert.Contains(errors, e => e.StartsWith("dist"));
        Assert.Contains(errors, e => e.Contains("distanceFromStart can have"));
    }

    [Fact]
    public void Dist_and_seconds_must_be_positive()
    {
        var errors = TimedSwimValidator.Validate(dist: 0, seconds: 0, resistances: [], splits: []);

        Assert.Contains(errors, e => e.Contains("dist"));
        Assert.Contains(errors, e => e.Contains("seconds"));
    }

    [Fact]
    public void Resistance_missing_its_required_measure_is_rejected()
    {
        var errors = TimedSwimValidator.Validate(dist: 50, seconds: 32, [new ResistanceItem { Kind = ResistanceKind.Other }], splits: []);

        Assert.Contains(errors, e => e.Contains("Other needs note"));
    }

    [Fact]
    public void Splits_must_increase_in_both_distance_and_time_and_stay_short_of_the_finish()
    {
        var valid = TimedSwimValidator.Validate(dist: 200, seconds: 122, [], [Split(100, 60), Split(150, 90, rate: 32)]);
        Assert.Empty(valid);

        var outOfOrderDistance = TimedSwimValidator.Validate(dist: 200, seconds: 122, [], [Split(100, 60), Split(100, 90)]);
        Assert.Contains(outOfOrderDistance, e => e.Contains("distanceFromStart"));

        var outOfOrderTime = TimedSwimValidator.Validate(dist: 200, seconds: 122, [], [Split(100, 60), Split(150, 50)]);
        Assert.Contains(outOfOrderTime, e => e.Contains("seconds must increase"));

        var reachesTheFinish = TimedSwimValidator.Validate(dist: 200, seconds: 122, [], [Split(200, 100)]);
        Assert.Contains(reachesTheFinish, e => e.Contains("finish time is the swim's own seconds"));

        var beyondTheFinish = TimedSwimValidator.Validate(dist: 200, seconds: 122, [], [Split(100, 130)]);
        Assert.Contains(beyondTheFinish, e => e.Contains("less than the swim's total seconds"));
    }

    [Fact]
    public void A_stroke_rate_of_zero_or_less_is_rejected()
    {
        var errors = TimedSwimValidator.Validate(dist: 200, seconds: 122, [], [Split(100, 60, rate: 0)]);

        Assert.Contains(errors, e => e.Contains("strokeRateCyclesPerMinute"));
    }

    [Fact]
    public void Valid_gear_for_each_kind_passes()
    {
        ResistanceItem[] gear =
        [
            new() { Kind = ResistanceKind.Sponges, Count = 2 },
            new() { Kind = ResistanceKind.Parachute, WidthCm = 30, HeightCm = 20 },
            new() { Kind = ResistanceKind.StretchCord, LengthCm = 300, TensionLevel = 6 },
            new() { Kind = ResistanceKind.WeightedBelt, WeightKg = 2.5m },
            new() { Kind = ResistanceKind.DragSuit },
            new() { Kind = ResistanceKind.DragBucket, Note = "small" },
            new() { Kind = ResistanceKind.Other, Note = "ankle band" },
        ];

        Assert.Empty(TimedSwimValidator.Validate(dist: 25, seconds: 18, gear, splits: []));
    }

    [Fact]
    public void Gear_must_have_its_required_measures_and_none_it_does_not_use()
    {
        var errors = TimedSwimValidator.Validate(dist: 25, seconds: 18,
        [
            new ResistanceItem { Kind = ResistanceKind.Sponges },
            new ResistanceItem { Kind = ResistanceKind.Parachute, WidthCm = 30 },
            new ResistanceItem { Kind = ResistanceKind.Sponges, Count = 2, WeightKg = 1 },
        ], splits: []);

        Assert.Contains(errors, e => e.StartsWith("resistances[0]") && e.Contains("needs count"));
        Assert.Contains(errors, e => e.StartsWith("resistances[1]") && e.Contains("needs heightCm"));
        Assert.Contains(errors, e => e.StartsWith("resistances[2]") && e.Contains("does not use weightKg"));
    }

    [Fact]
    public void Gear_measures_must_be_positive_and_tension_within_range()
    {
        var errors = TimedSwimValidator.Validate(dist: 25, seconds: 18,
        [
            new ResistanceItem { Kind = ResistanceKind.Sponges, Count = 0 },
            new ResistanceItem { Kind = ResistanceKind.StretchCord, LengthCm = 100, TensionLevel = 11 },
        ], splits: []);

        Assert.Contains(errors, e => e.Contains("count must be > 0"));
        Assert.Contains(errors, e => e.Contains("tensionLevel must be between 1 and 10"));
    }

    [Fact]
    public void A_parachutes_area_is_derived_from_its_width_and_height()
    {
        Assert.Equal(600m, new ResistanceItem { Kind = ResistanceKind.Parachute, WidthCm = 30, HeightCm = 20 }.AreaCm2);
        Assert.Null(new ResistanceItem { Kind = ResistanceKind.Sponges, Count = 2 }.AreaCm2);
    }
}
