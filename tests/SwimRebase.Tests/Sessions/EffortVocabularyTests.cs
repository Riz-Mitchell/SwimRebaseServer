using SessionConversion.Application.Interview;
using Sessions.Application.Services;
using Sessions.Domain.Calculations;
using Sessions.Domain.Models;
using static SwimRebase.Tests.Sessions.CalculatorTests;

namespace SwimRebase.Tests.Sessions;

public class EffortVocabularyTests
{
    private static readonly EffortVocabulary Vocabulary = EffortVocabulary.Default;
    private static readonly VocabularyIntensityMapper Mapper = new();

    [Fact]
    public void Every_built_in_scale_covers_one_to_one_hundred_without_gaps_or_overlaps()
    {
        Assert.Contains(Vocabulary.Scales, s => s.Id == EffortScale.TensId);
        Assert.Contains(Vocabulary.Scales, s => s.Id == "zones");

        foreach (var scale in Vocabulary.Scales)
        {
            Assert.Empty(EffortScale.Validate(scale.Bands));
            for (var percent = Intensity.Min; percent <= Intensity.Max; percent++)
            {
                Assert.Single(scale.Bands, b => percent >= b.Min && percent <= b.Max);
            }
        }
    }

    [Fact]
    public void The_tens_scale_buckets_by_rounding_to_the_nearest_ten()
    {
        var tens = EffortScale.Tens;

        Assert.Equal(10, tens.Bands.Count);
        for (var percent = Intensity.Min; percent <= Intensity.Max; percent++)
        {
            Assert.Equal(Intensity.ToLevel(percent).ToString(), tens.Bucket(percent));
        }

        Assert.Equal("2", tens.Bucket(20));
        Assert.Equal("10", tens.Bucket(95));
        Assert.Equal(EffortScale.Unspecified, tens.Bucket(null));
    }

    [Fact]
    public void The_zones_scale_separates_sub_max_from_max_and_orders_its_bands()
    {
        var zones = Vocabulary.FindScale("zones")!;

        Assert.Equal(new[] { "A0", "A1", "A2", "A3", "TH", "VO2", "Sub Max", "Max" }, zones.Bands.Select(b => b.Key));
        Assert.NotEqual(zones.Bucket(90), zones.Bucket(100));
        Assert.True(zones.Order("Sub Max") < zones.Order("Max"));
        Assert.True(zones.Order("Max") < zones.Order(EffortScale.Unspecified));
    }

    [Fact]
    public void Every_phrase_maps_to_the_percent_the_file_gives_it()
    {
        foreach (var entry in Vocabulary.Entries)
        {
            foreach (var phrase in entry.Phrases)
            {
                Assert.True(Mapper.TryMap(phrase, out var intensity), phrase);
                Assert.Equal(entry.Percent, intensity.Percent);
                Assert.Equal(entry.Scale, intensity.Scale);
            }
        }
    }

    [Fact]
    public void Every_zone_name_lands_in_its_own_band_of_the_zones_scale()
    {
        var zones = Vocabulary.FindScale("zones")!;

        foreach (var (token, band) in new[] { ("a0", "A0"), ("a1", "A1"), ("a2", "A2"), ("a3", "A3"), ("th", "TH"), ("vo2", "VO2"), ("sub max", "Sub Max"), ("max", "Max") })
        {
            Assert.True(Mapper.TryMap(token, out var intensity), token);
            Assert.Equal(band, zones.Bucket(intensity.Percent));
        }
    }

    [Fact]
    public void The_conversion_prompt_lists_every_word_and_zone_and_tells_the_model_to_copy_them()
    {
        var prompt = InterviewPrompt.Conversion;

        foreach (var phrase in Vocabulary.Entries.Where(e => e.Scale == EffortVocabulary.WordsScale).SelectMany(e => e.Phrases))
        {
            Assert.Contains(phrase, prompt);
        }

        foreach (var band in Vocabulary.FindScale("zones")!.Bands)
        {
            Assert.Contains(band.Key, prompt);
        }

        Assert.Contains("never reworded", prompt);
    }

    [Fact]
    public void A_session_saved_with_a_level_still_reads_and_is_written_back_as_a_percent()
    {
        const string legacy = """
            {"schemaVersion":1,"sets":[{"kind":"item","reps":2,"parts":[{"dist":50,"stroke":"free","mode":"swim","equip":[],"effort":"sub max","intensity":{"level":10,"scale":"words"}}]}]}
            """;

        var read = SessionJson.Deserialize<SwimSession>(legacy);
        var part = ((SetItem)read.Sets[0]).Parts[0];
        Assert.Equal(100, part.Intensity!.Percent);

        // What the startup backfill does: re-map from the effort text, then write at the current version.
        var fixedUp = new IntensityResolver([Mapper]).Resolve(read) with { SchemaVersion = SwimSession.CurrentSchemaVersion };
        var written = SessionJson.Serialize(fixedUp);

        Assert.Contains("\"schemaVersion\":2", written);
        Assert.Contains("\"percent\":90", written);
        Assert.Equal(90, ((SetItem)SessionJson.Deserialize<SwimSession>(written).Sets[0]).Parts[0].Intensity!.Percent);
    }

    [Fact]
    public void An_intensity_is_written_with_a_percent_and_the_derived_tens_level()
    {
        var json = SessionJson.Serialize(new Intensity { Percent = 95, Scale = "percent" });

        Assert.Equal("""{"percent":95,"scale":"percent","level":10}""", json);
        Assert.Equal(95, SessionJson.Deserialize<Intensity>(json).Percent);
    }

    [Fact]
    public void A_zone_that_was_never_mapped_before_is_picked_up_when_the_session_is_re_mapped()
    {
        var old = new SwimSession { Sets = [Item(1, Swim(100, Stroke.Free, effort: "A1"))] };
        Assert.Null(((SetItem)old.Sets[0]).Parts[0].Intensity);

        var mapped = new IntensityResolver([Mapper]).Resolve(old);

        Assert.Equal(60, ((SetItem)mapped.Sets[0]).Parts[0].Intensity!.Percent);
    }
}
