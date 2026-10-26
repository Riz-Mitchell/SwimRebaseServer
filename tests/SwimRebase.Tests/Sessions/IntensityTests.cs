using Sessions.Application.Services;
using Sessions.Domain.Calculations;
using Sessions.Domain.Models;
using static SwimRebase.Tests.Sessions.CalculatorTests;

namespace SwimRebase.Tests.Sessions;

public class IntensityTests
{
    private static readonly VocabularyIntensityMapper Words = new();

    [Theory]
    [InlineData("easy", 20)]
    [InlineData("Easy", 20)]
    [InlineData("very easy", 10)]
    [InlineData("relax", 20)]
    [InlineData("recovery", 20)]
    [InlineData("steady", 40)]
    [InlineData("build", 75)]
    [InlineData("fast", 80)]
    [InlineData("hard", 80)]
    [InlineData("threshold", 83)]
    [InlineData("race pace", 88)]
    [InlineData("sub max", 90)]
    [InlineData("Sub-Max", 90)]
    [InlineData("submax", 90)]
    [InlineData("sub max dive", 90)]
    [InlineData("max", 100)]
    [InlineData("all out", 100)]
    [InlineData("sprint", 100)]
    [InlineData("80%", 80)]
    [InlineData("95%", 95)]
    [InlineData(" 100 % ", 100)]
    [InlineData("0%", 1)]
    public void Everyday_effort_words_and_percentages_map_onto_the_global_scale(string raw, int percent)
    {
        Assert.True(Words.TryMap(raw, out var intensity));
        Assert.Equal(percent, intensity.Percent);
        Assert.InRange(intensity.Percent, Intensity.Min, Intensity.Max);
    }

    [Theory]
    [InlineData("A0", 40)]
    [InlineData("A1", 60)]
    [InlineData("a2", 70)]
    [InlineData("A3", 76)]
    [InlineData("TH", 83)]
    [InlineData("VO2", 88)]
    [InlineData("vo2 max", 88)]
    [InlineData("A2 pace", 70)]
    public void Zone_names_map_to_their_percent(string raw, int percent)
    {
        Assert.True(Words.TryMap(raw, out var intensity));
        Assert.Equal(percent, intensity.Percent);
        Assert.Equal(EffortVocabulary.ZonesScale, intensity.Scale);
    }

    [Fact]
    public void Sub_max_is_below_max_and_a_longer_phrase_is_not_also_read_as_its_parts()
    {
        Words.TryMap("sub max", out var subMax);
        Words.TryMap("max", out var max);

        Assert.True(subMax.Percent < max.Percent);
    }

    [Theory]
    [InlineData("easy/hard")]
    [InlineData("descend 1-4")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("200%!")]
    [InlineData("A1 easy fast")]
    [InlineData("a10")]
    public void Ambiguous_or_unknown_effort_is_left_unspecified(string raw)
    {
        Assert.False(Words.TryMap(raw, out _));
    }

    [Fact]
    public void The_scale_the_effort_came_from_is_recorded()
    {
        Words.TryMap("hard", out var word);
        Words.TryMap("70%", out var percent);

        Assert.Equal(EffortVocabulary.WordsScale, word.Scale);
        Assert.Equal(VocabularyIntensityMapper.PercentScale, percent.Scale);
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
                new SetGroup { Items = [Item(1, Swim(100, Stroke.Free, effort: "odds fast, evens easy", percent: 80))] },
                Item(1, Swim(100, Stroke.Free, percent: 60)),
            ],
        };

        var resolved = resolver.Resolve(tree);

        Assert.Equal(80, ((SetItem)resolved.Sets[0]).Parts[0].Intensity!.Percent);
        Assert.Null(((SetItem)((SetGroup)resolved.Sets[1]).Items[0]).Parts[0].Intensity);
        // No effort text: whatever level is already there is left alone.
        Assert.Equal(60, ((SetItem)resolved.Sets[2]).Parts[0].Intensity!.Percent);
    }

    [Fact]
    public void Ingest_maps_intensity_then_validates()
    {
        var ingest = TestSessions.Ingest();

        var prepared = ingest.Prepare(new SwimSession { Sets = [Item(2, Swim(50, Stroke.Fly, effort: "fast"))] });
        Assert.Equal(80, ((SetItem)prepared.Sets[0]).Parts[0].Intensity!.Percent);

        Assert.Throws<InvalidSessionException>(() => ingest.Prepare(new SwimSession()));
        Assert.Throws<InvalidSessionException>(() => ingest.Parse("{not json"));
    }
}
