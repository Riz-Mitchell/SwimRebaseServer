using Sessions.Domain.Calculations;
using Sessions.Domain.Models;

namespace SwimRebase.Tests.Sessions;

public class CalculatorTests
{
    private static SwimSession Tree(params SessionNode[] sets) => new() { Sets = [.. sets] };

    internal static SetItem Item(int reps, params Part[] parts) => new() { Reps = reps, Parts = [.. parts] };

    internal static Part Swim(decimal dist, Stroke stroke, SwimMode mode = SwimMode.Swim, string? effort = null, int? percent = null, params string[] equip) => new()
    {
        Dist = dist,
        Stroke = stroke,
        Mode = mode,
        Effort = effort,
        Equip = [.. equip],
        Intensity = percent is { } p ? new Intensity { Percent = p, Scale = "words" } : null,
    };

    [Fact]
    public void Total_multiplies_reps_and_group_repeats()
    {
        var session = Tree(
            Item(4, Swim(100, Stroke.Free)),
            new SetGroup { Repeat = 3, Items = [Item(1, Swim(200, Stroke.Free)), Item(2, Swim(50, Stroke.Free))] });

        Assert.Equal(400 + 3 * (200 + 100), SessionCalculator.Total(session));
    }

    [Fact]
    public void Decimal_distances_are_summed_exactly()
    {
        Assert.Equal(8 * 12.5m + 4 * 33.33m, SessionCalculator.Total(Tree(Item(8, Swim(12.5m, Stroke.Free)), Item(4, Swim(33.33m, Stroke.Back)))));
    }

    [Fact]
    public void An_im_part_splits_a_decimal_distance_into_exact_quarters()
    {
        var leaves = SessionLeaves.Flatten(Tree(Item(1, Swim(50.5m, Stroke.Im))));

        Assert.Equal(4, leaves.Count);
        Assert.All(leaves, l => Assert.Equal(12.625m, l.Distance));
    }

    [Fact]
    public void A_rep_made_of_parts_adds_up_the_parts()
    {
        Assert.Equal(4 * 100, SessionCalculator.Total(Tree(Item(4, Swim(25, Stroke.Fly), Swim(75, Stroke.Free)))));
    }

    [Fact]
    public void Repeats_multiply_down_three_levels_of_groups()
    {
        var session = Tree(new SetGroup
        {
            Repeat = 2,
            Items = [new SetGroup { Repeat = 3, Items = [new SetGroup { Repeat = 4, Items = [Item(5, Swim(50, Stroke.Fly))] }] }],
        });

        var leaf = Assert.Single(SessionLeaves.Flatten(session));
        Assert.Equal(2 * 3 * 4 * 5 * 50, leaf.Distance);
        Assert.Equal("0.0.0.0.0", leaf.NodeId);
    }

    [Fact]
    public void Leaves_always_add_up_to_the_calculator_total()
    {
        SwimSession[] trees =
        [
            Tree(Item(4, Swim(100, Stroke.Free))),
            Tree(Item(2, Swim(100, Stroke.Im)), new SetGroup { Repeat = 3, Items = [Item(2, Swim(25, Stroke.Fly), Swim(75, Stroke.Free, SwimMode.Kick))] }),
            Tree(new SetGroup { Repeat = 2, Items = [new SetGroup { Repeat = 2, Items = [Item(3, Swim(50, Stroke.Back)), Item(1, Swim(400, Stroke.Im))] }] }),
        ];

        foreach (var tree in trees)
        {
            Assert.Equal(SessionCalculator.Total(tree), SessionLeaves.Flatten(tree).Sum(l => l.Distance));
            Assert.Equal(SessionCalculator.Total(tree), BreakdownCalculator.Of(tree).Total);
        }
    }

    [Fact]
    public void An_im_part_is_split_into_equal_quarters_in_stroke_order()
    {
        var leaves = SessionLeaves.Flatten(Tree(Item(2, Swim(100, Stroke.Im))));

        Assert.Equal(new[] { Stroke.Fly, Stroke.Back, Stroke.Breast, Stroke.Free }, leaves.Select(l => l.Stroke));
        Assert.All(leaves, l => Assert.Equal(50, l.Distance));
        Assert.Single(leaves.Select(l => l.NodeId).Distinct());
    }

    [Fact]
    public void An_alternating_set_with_an_odd_rep_count_is_a_group_plus_one_extra_item()
    {
        // 7 x 50 alt fly/back: fly, back, fly, back, fly, back, fly
        var breakdown = BreakdownCalculator.Of(Tree(
            new SetGroup { Repeat = 3, Items = [Item(1, Swim(50, Stroke.Fly)), Item(1, Swim(50, Stroke.Back))] },
            Item(1, Swim(50, Stroke.Fly))));

        Assert.Equal(200, breakdown.ByStroke["fly"]);
        Assert.Equal(150, breakdown.ByStroke["back"]);
    }

    [Fact]
    public void Breakdown_groups_by_stroke_mode_equipment_and_intensity()
    {
        var session = Tree(
            Item(4, Swim(100, Stroke.Free, effort: "easy", percent: 20)),
            new SetGroup { Repeat = 2, Items = [Item(3, Swim(50, Stroke.Fly, SwimMode.Kick, "hard", 80, "fins"))] },
            Item(1, Swim(200, Stroke.Choice)));

        var breakdown = BreakdownCalculator.Of(session);

        Assert.Equal(900, breakdown.Total);
        Assert.Equal(400, breakdown.ByStroke["free"]);
        Assert.Equal(300, breakdown.ByStroke["fly"]);
        Assert.Equal(200, breakdown.ByStroke["choice"]);
        Assert.Equal(300, breakdown.ByMode["kick"]);
        Assert.Equal(300, breakdown.ByEquipment["fins"]);
        Assert.Equal(600, breakdown.ByEquipment["none"]);
        Assert.Equal(400, breakdown.ByIntensity["2"]);
        Assert.Equal(300, breakdown.ByIntensity["8"]);
        Assert.Equal(200, breakdown.ByIntensity[SessionBreakdown.Unspecified]);
    }

    [Fact]
    public void Validator_flags_impossible_numbers_empty_groups_and_items_with_no_parts()
    {
        var session = Tree(
            Item(0, Swim(100, Stroke.Free)),
            new SetGroup { Repeat = 2, Items = [] },
            Item(1),
            Item(1, new Part { Dist = 0, Stroke = Stroke.Free }),
            Item(1, new Part { Dist = 50 }));

        var problems = SessionValidator.Validate(session);

        Assert.Contains(problems, p => p.Contains("sets[0]") && p.Contains("reps"));
        Assert.Contains(problems, p => p.Contains("sets[1]") && p.Contains("no items"));
        Assert.Contains(problems, p => p.Contains("sets[2]") && p.Contains("no parts"));
        Assert.Contains(problems, p => p.Contains("sets[3].parts[0]") && p.Contains("dist"));
        Assert.Contains(problems, p => p.Contains("sets[4].parts[0]") && p.Contains("stroke is required"));
    }

    [Fact]
    public void Validator_requires_im_parts_to_divide_into_quarters()
    {
        Assert.Contains(SessionValidator.Validate(Tree(Item(1, Swim(150, Stroke.Im)))), p => p.Contains("divide by 4"));
        Assert.Empty(SessionValidator.Validate(Tree(Item(1, Swim(200, Stroke.Im)))));
    }

    [Fact]
    public void Groups_may_nest_three_deep_but_not_four()
    {
        static SetGroup Nest(int levels) => levels == 1
            ? new SetGroup { Items = [Item(1, Swim(50, Stroke.Free))] }
            : new SetGroup { Items = [Nest(levels - 1)] };

        Assert.Empty(SessionValidator.Validate(Tree(Nest(SessionRules.MaxGroupDepth))));
        Assert.Contains(SessionValidator.Validate(Tree(Nest(SessionRules.MaxGroupDepth + 1))), p => p.Contains("nest more than 3"));
    }

    [Fact]
    public void Validator_rejects_an_empty_session_and_accepts_a_good_one()
    {
        Assert.NotEmpty(SessionValidator.Validate(new SwimSession()));
        Assert.Empty(SessionValidator.Validate(Tree(Item(4, Swim(100, Stroke.Free)))));
    }
}
