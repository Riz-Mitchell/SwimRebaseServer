using Sessions.Domain.Calculations;
using Sessions.Domain.Models;
using static SwimRebase.Tests.Sessions.CalculatorTests;

namespace SwimRebase.Tests.Sessions;

public class FactTests
{
    private static Session SessionRow(SwimSession content) => new()
    {
        Id = Guid.NewGuid(),
        OwnerId = Guid.NewGuid(),
        PerformedOn = new DateOnly(2026, 9, 21),
        Course = PoolCourse.Scy,
        Source = SessionSource.Ai,
        TotalDistance = SessionCalculator.Total(content),
    };

    [Fact]
    public void Facts_carry_the_session_columns_and_add_up_to_its_total()
    {
        var content = NodeIds.Assign(new SwimSession
        {
            Sets =
            [
                Item(1, Swim(300, Stroke.Free, effort: "easy", percent: 20)),
                new SetGroup { Repeat = 4, Items = [Item(1, Swim(100, Stroke.Fly, percent: 80)), Item(1, Swim(50, Stroke.Free, SwimMode.Kick, percent: 30, equip: "fins"))] },
                Item(1, Swim(200, Stroke.Im)),
            ],
        });
        var session = SessionRow(content);

        var facts = SessionFactBuilder.Build(session, content);

        Assert.Equal(session.TotalDistance, facts.Sum(f => f.Distance));
        Assert.All(facts, f =>
        {
            Assert.Equal(session.Id, f.SessionId);
            Assert.Equal(session.OwnerId, f.OwnerId);
            Assert.Equal(session.PerformedOn, f.PerformedOn);
            Assert.Equal(DistanceUnit.Yards, f.Unit);
            Assert.Equal(PoolCourse.Scy, f.Course);
            Assert.Equal(SessionSource.Ai, f.Source);
            Assert.True(f.Distance > 0);
        });
        Assert.Equal(400, facts.Where(f => f.Stroke == Stroke.Fly && f.Intensity == 80).Sum(f => f.Distance));
        Assert.Equal(200, facts.Where(f => f.Mode == SwimMode.Kick).Sum(f => f.Distance));
        Assert.Equal(new[] { "fins" }, facts.Single(f => f.Mode == SwimMode.Kick).Equip);
    }

    [Fact]
    public void An_im_part_becomes_four_facts_sharing_one_node_id()
    {
        var content = NodeIds.Assign(new SwimSession { Sets = [Item(1, Swim(200, Stroke.Im))] });

        var facts = SessionFactBuilder.Build(SessionRow(content), content);

        Assert.Equal(4, facts.Count);
        Assert.Single(facts.Select(f => f.NodeId).Distinct());
        Assert.All(facts, f => Assert.Equal(50, f.Distance));
    }

    [Fact]
    public void Yards_follow_the_scy_course_and_metres_the_others()
    {
        Assert.Equal(DistanceUnit.Yards, new Session { Course = PoolCourse.Scy }.Unit);
        Assert.Equal(DistanceUnit.Metres, new Session { Course = PoolCourse.Scm }.Unit);
        Assert.Equal(DistanceUnit.Metres, new Session { Course = PoolCourse.Lcm }.Unit);
    }

    [Fact]
    public void Node_ids_are_assigned_where_missing_kept_where_present_and_unique()
    {
        var tree = new SwimSession
        {
            Sets =
            [
                new SetItem { Id = "keep-me", Parts = [new Part { Dist = 50, Stroke = Stroke.Free, Id = "keep-part" }, Swim(50, Stroke.Back)] },
                new SetGroup { Items = [Item(1, Swim(50, Stroke.Fly))] },
            ],
        };

        var assigned = NodeIds.Assign(tree);
        var item = (SetItem)assigned.Sets[0];
        var group = (SetGroup)assigned.Sets[1];
        var inner = (SetItem)group.Items[0];

        Assert.Equal("keep-me", item.Id);
        Assert.Equal("keep-part", item.Parts[0].Id);
        var ids = new[] { item.Id, item.Parts[0].Id, item.Parts[1].Id, group.Id, inner.Id, inner.Parts[0].Id };
        Assert.All(ids, id => Assert.False(string.IsNullOrEmpty(id)));
        Assert.Equal(ids.Length, ids.Distinct().Count());
        // The input is not changed.
        Assert.Null(((SetGroup)tree.Sets[1]).Id);
    }
}
