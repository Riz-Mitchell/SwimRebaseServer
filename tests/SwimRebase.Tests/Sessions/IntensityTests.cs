using Sessions.Application.Services;
using Sessions.Domain.Calculations;
using Sessions.Domain.Models;
using static SwimRebase.Tests.Sessions.CalculatorTests;

namespace SwimRebase.Tests.Sessions;

public class IntensityTests
{
    private static readonly WordsIntensityMapper Words = new();

    [Theory]
    [InlineData("easy", 2)]
    [InlineData("Easy", 2)]
    [InlineData("very easy", 1)]
    [InlineData("recovery", 2)]
    [InlineData("steady", 4)]
    [InlineData("build", 5)]
    [InlineData("threshold", 7)]
    [InlineData("fast", 8)]
    [InlineData("hard", 8)]
    [InlineData("race pace", 9)]
    [InlineData("all out", 10)]
    [InlineData("sprint", 10)]
    [InlineData("80%", 8)]
    [InlineData(" 100 % ", 10)]
    [InlineData("5%", 1)]
    public void Everyday_effort_words_and_percentages_map_onto_the_global_scale(string raw, int level)
    {
        Assert.True(Words.TryMap(raw, out var intensity));
        Assert.Equal(level, intensity.Level);
        Assert.InRange(intensity.Level, Intensity.Min, Intensity.Max);
    }

    [Theory]
    [InlineData("easy/hard")]
    [InlineData("descend 1-4")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("200%!")]
    public void Ambiguous_or_unknown_effort_is_left_unspecified(string raw)
    {
        Assert.False(Words.TryMap(raw, out _));
    }

    [Fact]
    public void The_scale_the_effort_came_from_is_recorded()
    {
        Words.TryMap("hard", out var word);
        Words.TryMap("70%", out var percent);

        Assert.Equal(WordsIntensityMapper.WordsScale, word.Scale);
        Assert.Equal(WordsIntensityMapper.PercentScale, percent.Scale);
    }

    [Fact]
    public void The_resolver_sets_intensity_from_effort_text_and_clears_it_when_the_text_cannot_be_mapped()
    {
        var resolver = new IntensityResolver([Words]);
        var tree = new SwimSession
        {
            Sets =
            [
                Item(1, Swim(100, Stroke.Free, effort: "hard")),
                new SetGroup { Items = [Item(1, Swim(100, Stroke.Free, effort: "odds fast, evens easy", level: 8))] },
                Item(1, Swim(100, Stroke.Free, level: 6)),
            ],
        };

        var resolved = resolver.Resolve(tree);

        Assert.Equal(8, ((SetItem)resolved.Sets[0]).Parts[0].Intensity!.Level);
        Assert.Null(((SetItem)((SetGroup)resolved.Sets[1]).Items[0]).Parts[0].Intensity);
        // No effort text: whatever level is already there is left alone.
        Assert.Equal(6, ((SetItem)resolved.Sets[2]).Parts[0].Intensity!.Level);
    }

    [Fact]
    public void Ingest_maps_intensity_then_validates()
    {
        var ingest = TestSessions.Ingest();

        var prepared = ingest.Prepare(new SwimSession { Sets = [Item(2, Swim(50, Stroke.Fly, effort: "fast"))] });
        Assert.Equal(8, ((SetItem)prepared.Sets[0]).Parts[0].Intensity!.Level);

        Assert.Throws<InvalidSessionException>(() => ingest.Prepare(new SwimSession()));
        Assert.Throws<InvalidSessionException>(() => ingest.Parse("{not json"));
    }
}
