using Sessions.Domain.Calculations;
using Sessions.Domain.Models;

namespace SwimRebase.Tests.Sessions;

public class StrokeRateCalculatorTests
{
    private static Split Split(int? rate) => new() { DistanceFromStart = 50, Seconds = 30, StrokeRateCyclesPerMinute = rate };

    [Fact]
    public void No_readings_means_no_stroke_rate()
    {
        Assert.Null(StrokeRateCalculator.Compute([Split(null), Split(null)]));
    }

    [Fact]
    public void One_reading_stands_for_the_whole_swim()
    {
        Assert.Equal(32, StrokeRateCalculator.Compute([Split(null), Split(32)]));
    }

    [Fact]
    public void Several_readings_are_simply_averaged()
    {
        Assert.Equal(31, StrokeRateCalculator.Compute([Split(30), Split(32)]));
        Assert.Equal(30.7, StrokeRateCalculator.Compute([Split(30), Split(31), Split(31)]));
    }
}
