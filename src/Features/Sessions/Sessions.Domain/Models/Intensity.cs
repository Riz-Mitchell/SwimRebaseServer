namespace Sessions.Domain.Models;

/// <summary>
/// How hard a part was swum, on one global scale. Every external scale (words, RPE, zones) is mapped onto it by an
/// <see cref="Calculations.IIntensityMapper"/>, so metrics only ever compare like with like.
/// </summary>
public sealed record Intensity
{
    public const int Min = 1;
    public const int Max = 10;

    public int Level { get; init; }

    /// <summary>The scale the input was mapped from ("words", "percent"), so a later change to a mapping can be replayed.</summary>
    public string Scale { get; init; } = string.Empty;
}
