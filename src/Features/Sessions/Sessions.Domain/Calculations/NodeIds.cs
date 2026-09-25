using Sessions.Domain.Models;

namespace Sessions.Domain.Calculations;

/// <summary>Gives every node and part of a session that is being saved a stable id. Ids that already exist are kept.</summary>
public static class NodeIds
{
    public static SwimSession Assign(SwimSession session) => session with { Sets = session.Sets.Select(Assign).ToList() };

    private static SessionNode Assign(SessionNode node) => node switch
    {
        SetGroup group => group with { Id = group.Id ?? NewId(), Items = group.Items.Select(Assign).ToList() },
        SetItem item => item with
        {
            Id = item.Id ?? NewId(),
            Parts = item.Parts.Select(p => p with { Id = p.Id ?? NewId() }).ToList(),
        },
        _ => node,
    };

    private static string NewId() => Guid.NewGuid().ToString("N")[..10];
}
