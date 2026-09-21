using SessionConversion.Domain.Calculations;
using SessionConversion.Domain.Models;

namespace SwimRebase.Tests.SessionConversion;

public class CalculatorTests
{
    private static SwimSession Session(params SessionNode[] sets) => new() { Sets = [.. sets] };

    [Fact]
    public void Total_multiplies_reps_and_group_repeats()
    {
        var session = Session(
            new SetItem { Reps = 4, Dist = 100, Strokes = [Stroke.Free] },
            new SetGroup { Repeat = 3, Items = [new SetItem { Reps = 1, Dist = 200 }, new SetItem { Reps = 2, Dist = 50 }] });

        Assert.Equal(400 + 3 * (200 + 100), SessionCalculator.Total(session));
    }

    [Fact]
    public void Breakdown_totals_agree_with_the_calculator()
    {
        var session = Session(
            new SetItem { Reps = 4, Dist = 100, Strokes = [Stroke.Free], Effort = "Easy" },
            new SetGroup { Repeat = 2, Items = [new SetItem { Reps = 3, Dist = 50, Strokes = [Stroke.Fly], Mode = SwimMode.Kick, Equip = ["Fins"] }] });

        var breakdown = BreakdownCalculator.Of(session);

        Assert.Equal(SessionCalculator.Total(session), breakdown.Total);
        Assert.Equal(400, breakdown.ByStroke["free"]);
        Assert.Equal(300, breakdown.ByStroke["fly"]);
        Assert.Equal(300, breakdown.ByMode["kick"]);
        Assert.Equal(300, breakdown.ByEquipment["fins"]);
        Assert.Equal(400, breakdown.ByEquipment["none"]);
        Assert.Equal(400, breakdown.ByEffort["easy"]);
    }

    [Fact]
    public void A_plain_im_rep_is_split_evenly_in_stroke_order()
    {
        var breakdown = BreakdownCalculator.Of(Session(new SetItem { Reps = 2, Dist = 100, Strokes = [Stroke.Im] }));

        Assert.Equal(50, breakdown.ByStroke["fly"]);
        Assert.Equal(50, breakdown.ByStroke["back"]);
        Assert.Equal(50, breakdown.ByStroke["breast"]);
        Assert.Equal(50, breakdown.ByStroke["free"]);
    }

    [Fact]
    public void Parts_are_counted_per_part_and_inherit_from_the_item()
    {
        var item = new SetItem
        {
            Reps = 4,
            Dist = 100,
            Mode = SwimMode.Swim,
            Effort = "fast",
            Parts = [new Part { Dist = 25, Stroke = Stroke.Fly }, new Part { Dist = 75, Stroke = Stroke.Free, Mode = SwimMode.Kick }],
        };

        var breakdown = BreakdownCalculator.Of(Session(item));

        Assert.Equal(400, breakdown.Total);
        Assert.Equal(100, breakdown.ByStroke["fly"]);
        Assert.Equal(300, breakdown.ByStroke["free"]);
        Assert.Equal(100, breakdown.ByMode["swim"]);
        Assert.Equal(300, breakdown.ByMode["kick"]);
        Assert.Equal(400, breakdown.ByEffort["fast"]);
    }

    [Fact]
    public void Several_strokes_with_no_parts_are_reported_as_unresolved()
    {
        var breakdown = BreakdownCalculator.Of(Session(new SetItem { Reps = 4, Dist = 50, Strokes = [Stroke.Fly, Stroke.Back] }));

        Assert.Equal(200, breakdown.ByStroke["mixed"]);
        Assert.Equal(200, breakdown.UnresolvedStroke);
    }

    [Fact]
    public void Validator_flags_impossible_numbers_empty_groups_and_parts_that_do_not_add_up()
    {
        var session = Session(
            new SetItem { Reps = 0, Dist = 100 },
            new SetGroup { Repeat = 2, Items = [] },
            new SetItem { Reps = 1, Dist = 100, Parts = [new Part { Dist = 25 }, new Part { Dist = 50 }] });

        var problems = SessionValidator.Validate(session);

        Assert.Contains(problems, p => p.Contains("sets[0]") && p.Contains("reps"));
        Assert.Contains(problems, p => p.Contains("sets[1]") && p.Contains("no items"));
        Assert.Contains(problems, p => p.Contains("sets[2]") && p.Contains("parts add up to 75"));
    }

    [Fact]
    public void Validator_rejects_an_empty_session_and_accepts_a_good_one()
    {
        Assert.NotEmpty(SessionValidator.Validate(new SwimSession()));
        Assert.Empty(SessionValidator.Validate(Session(new SetItem { Reps = 4, Dist = 100 })));
    }
}
