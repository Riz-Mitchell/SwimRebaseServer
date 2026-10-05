using Sessions.Domain.Calculations;
using Sessions.Domain.Models;
using static SwimRebase.Tests.Sessions.CalculatorTests;

namespace SwimRebase.Tests.Sessions;

public class SessionTitleTests
{
    private static readonly DateOnly Day = new(2026, 9, 21);

    private static SwimSession Tree(params SessionNode[] sets) => new() { Sets = [.. sets] };

    private static string Title(SwimSession session, DistanceUnit unit = DistanceUnit.Metres) => SessionTitles.Build(session, unit, Day);

    [Fact]
    public void A_single_stroke_session_is_named_for_the_stroke_and_the_total()
    {
        Assert.Equal("Freestyle - 800 m", Title(Tree(Item(8, Swim(100, Stroke.Free)))));
        Assert.Equal("Butterfly - 200 m", Title(Tree(Item(4, Swim(50, Stroke.Fly)))));
    }

    [Fact]
    public void A_real_share_of_kick_pull_or_drill_is_named_too_but_a_small_one_is_not()
    {
        var half = Tree(Item(4, Swim(100, Stroke.Free)), Item(4, Swim(100, Stroke.Free, SwimMode.Kick)));
        var fifth = Tree(Item(8, Swim(100, Stroke.Free)), Item(2, Swim(100, Stroke.Free, SwimMode.Kick)));
        var pull = Tree(Item(3, Swim(100, Stroke.Free)), Item(7, Swim(100, Stroke.Free, SwimMode.Pull)), Item(2, Swim(100, Stroke.Free, SwimMode.Kick)));

        Assert.Equal("Freestyle and kick - 800 m", Title(half));
        Assert.Equal("Freestyle - 1,000 m", Title(fifth));
        Assert.Equal("Freestyle and pull - 1,200 m", Title(pull));
    }

    [Fact]
    public void A_session_with_no_stroke_over_half_is_mixed_and_an_im_session_is_im()
    {
        Assert.Equal("Mixed - 600 m", Title(Tree(Item(1, Swim(200, Stroke.Free)), Item(1, Swim(200, Stroke.Back)), Item(1, Swim(200, Stroke.Fly)))));
        Assert.Equal("IM - 800 m", Title(Tree(Item(2, Swim(400, Stroke.Im)))));
        Assert.Equal("Mixed - 400 m", Title(Tree(Item(4, Swim(100, Stroke.Choice)))));
    }

    [Fact]
    public void The_group_leads_and_yards_and_decimals_are_written_out()
    {
        var session = new SwimSession { Group = "  Lane 2 ", Sets = [Item(3, Swim(12.5m, Stroke.Free))] };

        Assert.Equal("Lane 2 - Freestyle - 37.5 yd", Title(session, DistanceUnit.Yards));
    }

    [Fact]
    public void Repeats_are_multiplied_into_the_total()
    {
        var session = Tree(new SetGroup { Repeat = 3, Items = [Item(2, Swim(100, Stroke.Back))] });

        Assert.Equal("Backstroke - 600 m", Title(session));
    }

    [Fact]
    public void A_session_with_no_distance_falls_back_to_the_date()
    {
        Assert.Equal("Swim 2026-09-21", Title(new SwimSession()));
    }

    [Theory]
    [InlineData("  Fly   kick \n ladder ", "Fly kick ladder")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void A_title_is_one_line_with_single_spaces(string? raw, string? expected)
    {
        Assert.Equal(expected, SessionTitles.Clean(raw));
    }

    [Fact]
    public void A_long_title_is_cut_to_the_limit_without_trailing_space()
    {
        var cleaned = SessionTitles.Clean(string.Join(' ', Enumerable.Repeat("threshold", 20)));

        Assert.NotNull(cleaned);
        Assert.True(cleaned.Length <= SessionTitles.MaxLength);
        Assert.Equal(cleaned.TrimEnd(), cleaned);
    }
}
