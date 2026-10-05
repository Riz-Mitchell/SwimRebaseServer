namespace Ai.Application.Models;

/// <summary>Coarse classification of what a tool does to the user's data, for UI iconography/copy - not a security boundary (<see cref="Interfaces.IAiTool.RequiresApproval"/> is).</summary>
public enum ToolEffect
{
    /// <summary>Read-only, or mutates existing data without creating or destroying anything (e.g. editing a focus in place).</summary>
    Neutral,

    /// <summary>Adds something new - a row, a version, an entity.</summary>
    Creative,

    /// <summary>Permanently removes or ends something.</summary>
    Destructive,
}

/// <summary>
/// What a tool actually did, alongside the raw JSON it hands back to the model. <see cref="AffectedCount"/>
/// is tool-defined - "6" for six timetable entries fetched, "1" for one focus created, "0" for a
/// delete/edit that found nothing to act on.
/// </summary>
public sealed record ToolExecutionResult(string ResultJson, int AffectedCount, IReadOnlyList<ToolResource>? Resources = null);

/// <summary>A user-facing thing a tool created, changed or removed, so the client can render a card and link to it without parsing the tool's own result JSON.</summary>
public sealed record ToolResource(string Kind, Guid Id, string Action, string? Label = null);

public static class ToolResourceKinds
{
    public const string Session = "session";
    public const string Focus = "focus";
    public const string TimetableSlot = "timetable_slot";
}

public static class ToolResourceActions
{
    public const string Created = "created";
    public const string Updated = "updated";
    public const string Deleted = "deleted";
}
