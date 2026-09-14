namespace Training.Application.Services;

/// <summary>Input for creating a focus. A null <see cref="Priority"/> appends it after all current active focuses.</summary>
public sealed record FocusInput(
    string TagLine,
    string Prompt,
    int TotalDays,
    int? Priority,
    IReadOnlyList<ComparisonPointInput> PositivePoints,
    IReadOnlyList<ComparisonPointInput> NegativePoints);

public sealed record ComparisonPointInput(string TagLine, string Description);
