namespace Sessions.Domain.Models;

/// <summary>What a swimmer's own effort wording (a club's zone name, say) means, as a percent of maximum effort.</summary>
public class UserEffortTerm
{
    public Guid Id { get; set; }

    public Guid OwnerId { get; set; }

    /// <summary>Lower-cased with single spaces (see <see cref="Calculations.PersonalIntensityMapper.Normalize"/>), so it matches however the board wrote it.</summary>
    public string Phrase { get; set; } = string.Empty;

    public int Percent { get; set; }
}
