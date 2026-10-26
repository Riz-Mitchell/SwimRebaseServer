using Sessions.Application.Services;
using Sessions.Domain.Calculations;
using Sessions.Domain.Models;
using static SwimRebase.Tests.Sessions.CalculatorTests;

namespace SwimRebase.Tests.Sessions;

public class ProgressionTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateOnly Day = new(2026, 10, 12);

    private static SetItem Descend(int reps, Progression progression, string? effort = null) =>
        Item(reps, Swim(100, Stroke.Free, effort: effort)) with { Progression = progression };

    private static SwimSession Prepared(SetItem item, ProgressionDefaults? defaults = null) =>
        TestSessions.Ingest().Prepare(new SwimSession { Sets = [item] }, defaults: defaults);

    private static Progression Of(SwimSession session) => Assert.IsType<SetItem>(session.Sets[0]).Progression!;

    private static List<(int Percent, int Reps)> Ramp(SwimSession session) =>
        [.. ProgressionRamp.Slices(Assert.IsType<SetItem>(session.Sets[0]))!.Select(s => (s.Percent, s.Reps))];

    [Theory]
    [InlineData(2, new[] { 60, 70 }, new[] { 1, 1 })]
    [InlineData(3, new[] { 60, 70 }, new[] { 2, 1 })]
    [InlineData(4, new[] { 60, 70 }, new[] { 2, 2 })]
    public void Adjacent_zones_double_the_easier_zone_and_finish_on_the_target(int reps, int[] percents, int[] counts)
    {
        var session = Prepared(Descend(reps, new Progression { From = "A1", To = "A2" }));

        Assert.Equal(percents.Zip(counts), Ramp(session).Select(r => (r.Percent, r.Reps)));
    }

    [Fact]
    public void Zones_further_apart_step_through_every_zone_between()
    {
        var session = Prepared(Descend(4, new Progression { From = "A1", To = "TH" }));

        Assert.Equal([(60, 1), (70, 1), (76, 1), (83, 1)], Ramp(session));
    }

    [Fact]
    public void Percent_endpoints_are_interpolated()
    {
        var session = Prepared(Descend(4, new Progression { From = "60%", To = "90%" }));

        Assert.Equal([(60, 1), (70, 1), (80, 1), (90, 1)], Ramp(session));
    }

    [Fact]
    public void A_span_repeats_the_ramp_and_a_short_last_ramp_covers_what_is_left()
    {
        Assert.Equal([(60, 2), (90, 2)], Ramp(Prepared(Descend(4, new Progression { Span = 2, From = "60%", To = "90%" }))));
        Assert.Equal([(60, 2), (70, 1), (80, 1), (90, 2)], Ramp(Prepared(Descend(6, new Progression { Span = 4, From = "60%", To = "90%" }))));
    }

    [Fact]
    public void An_endpoint_the_board_does_not_give_comes_from_the_item_then_the_swimmers_settings()
    {
        var defaults = new ProgressionDefaults(40, 90);

        var bare = Of(Prepared(Descend(4, new Progression { Span = 4 }), defaults));
        var withEffort = Of(Prepared(Descend(4, new Progression { Span = 4 }, effort: "A1"), defaults));
        var withEnd = Of(Prepared(Descend(4, new Progression { To = "80%" }), defaults));

        Assert.Equal((40, ProgressionDefaults.AssumedScale), (bare.FromIntensity!.Percent, bare.FromIntensity.Scale));
        Assert.Equal(90, bare.ToIntensity!.Percent);
        Assert.Equal(60, withEffort.FromIntensity!.Percent);
        Assert.Equal((40, 80), (withEnd.FromIntensity!.Percent, withEnd.ToIntensity!.Percent));
        Assert.Null(bare.From);
    }

    [Fact]
    public void A_target_below_the_start_never_turns_the_descend_around()
    {
        var progression = Of(Prepared(Descend(4, new Progression(), effort: "race pace"), new ProgressionDefaults(50, 85)));

        Assert.Equal(88, progression.ToIntensity!.Percent);
    }

    [Fact]
    public void Defaults_are_taken_when_the_session_is_saved_and_kept_after_the_settings_change()
    {
        var saved = Prepared(Descend(4, new Progression()), new ProgressionDefaults(50, 85));

        var later = TestSessions.Ingest().Prepare(saved, defaults: new ProgressionDefaults(30, 95));

        Assert.Equal((50, 85), (Of(later).FromIntensity!.Percent, Of(later).ToIntensity!.Percent));
    }

    [Fact]
    public void A_stated_endpoint_is_mapped_again_so_an_edit_to_its_text_wins()
    {
        var saved = Prepared(Descend(4, new Progression { From = "A1", To = "TH" }));

        var edited = TestSessions.Ingest().Prepare(saved with { Sets = [Descend(4, Of(saved) with { To = "A3" })] });

        Assert.Equal(76, Of(edited).ToIntensity!.Percent);
    }

    [Theory]
    [InlineData(1, null, "at least 2 reps")]
    [InlineData(4, 1, "span must be between 2 and the item's 4 reps")]
    [InlineData(4, 5, "span must be between 2 and the item's 4 reps")]
    public void An_impossible_descend_is_rejected(int reps, int? span, string problem)
    {
        var error = Assert.Throws<InvalidSessionException>(() => Prepared(Descend(reps, new Progression { Span = span })));

        Assert.Contains(error.Problems, p => p.Contains(problem));
    }

    [Fact]
    public void A_descend_that_goes_from_harder_to_easier_is_rejected()
    {
        var error = Assert.Throws<InvalidSessionException>(() => Prepared(Descend(4, new Progression { From = "90%", To = "60%" })));

        Assert.Contains(error.Problems, p => p.Contains("rises from easier to harder"));
    }

    [Fact]
    public void Distance_is_split_across_the_efforts_without_changing_the_total()
    {
        var session = Prepared(Descend(4, new Progression { From = "60%", To = "90%" }));

        var leaves = SessionLeaves.Flatten(session);

        Assert.Equal(400, SessionCalculator.Total(session));
        Assert.Equal(400, leaves.Sum(l => l.Distance));
        Assert.Equal([60, 70, 80, 90], leaves.Select(l => l.Intensity!.Value));
        Assert.All(leaves, l => Assert.Equal(100, l.Distance));
    }

    [Fact]
    public void A_descend_inside_rounds_is_repeated_with_the_rounds()
    {
        var group = new SetGroup { Repeat = 3, Items = [Descend(2, new Progression { From = "60%", To = "90%" })] };

        var leaves = SessionLeaves.Flatten(TestSessions.Ingest().Prepare(new SwimSession { Sets = [group] }));

        Assert.Equal([(60, 300m), (90, 300m)], leaves.Select(l => (l.Intensity!.Value, l.Distance)));
    }

    [Fact]
    public void The_outline_says_the_set_is_a_descend()
    {
        var item = Assert.IsType<SetItem>(Prepared(Descend(8, new Progression { Span = 4, From = "60%", To = "90%" })).Sets[0]);

        Assert.Contains("descending 60% to 90% every 4 reps", SessionText.Describe(item));
    }

    [Fact]
    public void The_descend_survives_being_stored_and_read_back()
    {
        var json = SessionJson.Serialize(Prepared(Descend(4, new Progression { Span = 4, From = "A1", To = "A2" })));

        var read = Of(SessionJson.Deserialize<SwimSession>(json));

        Assert.Equal((4, "A1", "A2", 60, 70), (read.Span, read.From, read.To, read.FromIntensity!.Percent, read.ToIntensity!.Percent));
    }

    [Fact]
    public async Task A_saved_session_counts_each_rep_at_its_own_effort_and_an_edit_keeps_the_saved_target()
    {
        var repository = new InMemorySessionRepository();
        var content = new SwimSession { Sets = [Descend(4, new Progression { Span = 4 })] };
        var saved = await TestSessions.Service(repository, new ProgressionDefaults(40, 80))
            .CreateAsync(UserId, content, Day, PoolCourse.Scy, null, SessionSource.Logged, null, CancellationToken.None);

        Assert.Equal([40, 53, 67, 80], repository.Facts.Select(f => f.Intensity!.Value).Order());

        await TestSessions.Service(repository, new ProgressionDefaults(10, 20)).EditAsync(UserId, saved.Id,
            [new SessionEdit(SessionEditOps.Insert, "1", new SetItem { Reps = 1, Parts = [new Part { Dist = 50, Stroke = Stroke.Free }] }, null)],
            CancellationToken.None);

        Assert.Equal(450, repository.Facts.Sum(f => f.Distance));
        Assert.Equal(400, repository.Facts.Where(f => f.Intensity is 40 or 53 or 67 or 80).Sum(f => f.Distance));
    }
}
