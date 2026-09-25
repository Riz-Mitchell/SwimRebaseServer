using Sessions.Domain.Models;

namespace Sessions.Domain.Calculations;

/// <summary>Builds the fact rows of a saved session: its leaves plus the session columns copied down.</summary>
public static class SessionFactBuilder
{
    public static List<SessionFact> Build(Session session, SwimSession content) =>
        SessionLeaves.Flatten(content)
            .Select(leaf => new SessionFact
            {
                SessionId = session.Id,
                NodeId = leaf.NodeId,
                Stroke = leaf.Stroke,
                Mode = leaf.Mode,
                Equip = [.. leaf.Equip],
                Intensity = leaf.Intensity,
                Distance = leaf.Distance,
                OwnerId = session.OwnerId,
                PerformedOn = session.PerformedOn,
                Unit = session.Unit,
                Course = session.Course,
                Source = session.Source,
            })
            .ToList();
}
