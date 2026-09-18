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

/// <summary>Input for editing a focus. Priority is not editable here - see <see cref="Training.Application.Services.FocusService.MoveToPriorityAsync"/>.</summary>
public sealed record FocusEditInput(
    string TagLine,
    string Prompt,
    int TotalDays,
    IReadOnlyList<ComparisonPointInput> PositivePoints,
    IReadOnlyList<ComparisonPointInput> NegativePoints);
