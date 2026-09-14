namespace Training.Domain.Models;

/// <summary>One "if you stick to it.../if you don't..." point attached to a <see cref="Focus"/>.</summary>
public class ComparisonPoint
{
    public Guid Id { get; set; }

    public Guid FocusId { get; set; }

    public ComparisonPointPolarity Polarity { get; set; }

    public int Order { get; set; }

    public string TagLine { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public Focus? Focus { get; set; }
}
