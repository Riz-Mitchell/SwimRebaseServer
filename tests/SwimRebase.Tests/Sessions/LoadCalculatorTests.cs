using Sessions.Domain.Calculations;

namespace SwimRebase.Tests.Sessions;

public class LoadCalculatorTests
{
    [Fact]
    public void Load_is_distance_times_effort_squared_over_a_thousand()
    {
        var load = LoadCalculator.Compute([(400, 8)], defaultLevel: 5);

        Assert.Equal(0.256, load.Load, 6);
        Assert.Equal(0, load.UnratedDistance);
    }

    [Fact]
    public void Each_effort_level_is_added_up()
    {
        var load = LoadCalculator.Compute([(400, 2), (200, 8), (100, 10)], defaultLevel: 5);

        Assert.Equal((400 * 0.04 + 200 * 0.64 + 100 * 1.0) / 1000, load.Load, 6);
    }

    [Fact]
    public void Distance_without_intensity_is_counted_at_the_default_effort()
    {
        var atFive = LoadCalculator.Compute([(1000, null)], defaultLevel: 5);
        var atEight = LoadCalculator.Compute([(1000, null)], defaultLevel: 8);

        Assert.Equal(0.25, atFive.Load, 6);
        Assert.Equal(0.64, atEight.Load, 6);
        Assert.Equal(1000, atFive.UnratedDistance);
    }

    [Fact]
    public void Nothing_swum_is_no_load()
    {
        var load = LoadCalculator.Compute([], defaultLevel: 5);

        Assert.Equal(0, load.Load);
        Assert.Equal(0, load.UnratedDistance);
    }
}
