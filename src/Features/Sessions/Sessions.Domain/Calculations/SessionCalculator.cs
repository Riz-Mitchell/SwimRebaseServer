using Sessions.Domain.Models;

namespace Sessions.Domain.Calculations;

/// <summary>Distance maths over what was swum. Never taken from the model - always derived from the structure, so it always agrees with it.</summary>
public static class SessionCalculator
{
    public static int Total(SwimSession session) => session.Sets.Sum(Total);

    public static int Total(SessionNode node) => node switch
    {
        SetGroup group => Math.Max(group.Repeat, 0) * group.Items.Sum(Total),
        SetItem item => Math.Max(item.Reps, 0) * item.RepDistance,
        _ => 0,
    };
}
