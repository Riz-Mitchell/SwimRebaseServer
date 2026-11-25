using Sessions.Domain.Models;

namespace Sessions.Domain.Calculations;

/// <summary>Converts a distance between yards and metres. Stored distances are never converted; only aggregates are, at query time.</summary>
public static class DistanceUnits
{
    /// <summary>One yard in metres, exactly (the international yard).</summary>
    public const decimal MetresPerYard = 0.9144m;

    /// <summary>The distance as it reads in <paramref name="to"/>, unrounded.</summary>
    public static decimal Convert(decimal distance, DistanceUnit from, DistanceUnit to) => (from, to) switch
    {
        (DistanceUnit.Yards, DistanceUnit.Metres) => distance * MetresPerYard,
        (DistanceUnit.Metres, DistanceUnit.Yards) => distance / MetresPerYard,
        _ => distance,
    };
}
