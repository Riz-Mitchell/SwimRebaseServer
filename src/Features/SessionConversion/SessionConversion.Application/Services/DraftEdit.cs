using System.Text.Json;
using SessionConversion.Domain.Models;

namespace SessionConversion.Application.Services;

public static class DraftEditOps
{
    public const string Remove = "remove";
    public const string Replace = "replace";
    public const string Insert = "insert";
    public const string SetGroup = "set_group";
}

/// <param name="Op">One of <see cref="DraftEditOps"/>.</param>
/// <param name="Path">Dot-separated indexes into the outline, e.g. "2" or "1.0" (item 0 inside set 1). For an insert, the position to insert at.</param>
/// <param name="Node">The new set, for replace and insert.</param>
/// <param name="Group">The new group, for set_group (empty clears it).</param>
public sealed record DraftEdit(string Op, string? Path, SessionNode? Node, string? Group);

/// <summary>The input of an edit_draft call: which draft, and the edits to apply in order (each sees the result of the ones before it).</summary>
public sealed record DraftEditRequest(Guid DraftId, IReadOnlyList<DraftEdit> Edits)
{
    private sealed record Payload(string? DraftId, List<RawEdit>? Edits);

    private sealed record RawEdit(string? Op, string? Path, JsonElement? Node, string? Group);

    public static DraftEditRequest Parse(string inputJson)
    {
        var payload = SessionJson.Deserialize<Payload>(inputJson);
        if (!Guid.TryParse(payload.DraftId, out var draftId))
        {
            throw new InvalidDraftEditException("draftId is missing or not a valid id - use the draft id from the read_swim_board result.");
        }

        var edits = (payload.Edits ?? [])
            .Select(e => new DraftEdit(
                e.Op ?? string.Empty,
                e.Path,
                e.Node is { ValueKind: JsonValueKind.Object } node ? SessionJson.Deserialize<SessionNode>(node) : null,
                e.Group))
            .ToList();

        return new DraftEditRequest(draftId, edits);
    }
}

/// <summary>A malformed or impossible edit. The message is written for the model that made it.</summary>
public sealed class InvalidDraftEditException(string message) : Exception(message);

/// <summary>Applies <see cref="DraftEdit"/>s to a session without mutating the original.</summary>
public static class DraftEditor
{
    public static SwimSession Apply(SwimSession session, IReadOnlyList<DraftEdit> edits)
    {
        if (edits.Count == 0)
        {
            throw new InvalidDraftEditException("edits is empty - nothing to change.");
        }

        // Records hold mutable lists, so edit a deep copy rather than the caller's session.
        var working = SessionJson.Deserialize<SwimSession>(SessionJson.Serialize(session));

        for (var n = 0; n < edits.Count; n++)
        {
            var edit = edits[n];
            try
            {
                working = ApplyOne(working, edit);
            }
            catch (InvalidDraftEditException ex)
            {
                throw new InvalidDraftEditException($"edit {n} ({edit.Op}): {ex.Message}");
            }
        }

        return working;
    }

    private static SwimSession ApplyOne(SwimSession session, DraftEdit edit)
    {
        if (edit.Op == DraftEditOps.SetGroup)
        {
            return session with { Group = string.IsNullOrWhiteSpace(edit.Group) ? null : edit.Group.Trim() };
        }

        if (edit.Op is not (DraftEditOps.Remove or DraftEditOps.Replace or DraftEditOps.Insert))
        {
            throw new InvalidDraftEditException($"unknown op '{edit.Op}'. Use remove, replace, insert or set_group.");
        }

        var (list, index) = Locate(session, edit.Path);

        switch (edit.Op)
        {
            case DraftEditOps.Remove:
                RequireIndexInRange(index, list.Count);
                list.RemoveAt(index);
                break;
            case DraftEditOps.Replace:
                RequireIndexInRange(index, list.Count);
                list[index] = edit.Node ?? throw new InvalidDraftEditException("replace needs a node.");
                break;
            case DraftEditOps.Insert:
                if (index < 0 || index > list.Count)
                {
                    throw new InvalidDraftEditException($"cannot insert at {index}; this level has {list.Count} entries (use {list.Count} to append).");
                }

                list.Insert(index, edit.Node ?? throw new InvalidDraftEditException("insert needs a node."));
                break;
        }

        return session;
    }

    private static void RequireIndexInRange(int index, int count)
    {
        if (index < 0 || index >= count)
        {
            throw new InvalidDraftEditException($"there is no entry {index}; this level has {count}.");
        }
    }

    /// <summary>Walks a path like "1.0" down through groups and returns the list holding the target plus the target's index in it.</summary>
    private static (List<SessionNode> List, int Index) Locate(SwimSession session, string? path)
    {
        var parts = (path ?? string.Empty).Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || parts.Any(p => !int.TryParse(p, out _)))
        {
            throw new InvalidDraftEditException($"path '{path}' is not valid - use the index shown in the outline, e.g. \"2\" or \"1.0\".");
        }

        var indexes = parts.Select(int.Parse).ToArray();
        var list = session.Sets;
        for (var depth = 0; depth < indexes.Length - 1; depth++)
        {
            if (indexes[depth] < 0 || indexes[depth] >= list.Count || list[indexes[depth]] is not SetGroup group)
            {
                throw new InvalidDraftEditException($"path '{path}' does not lead through a set of rounds.");
            }

            list = group.Items;
        }

        return (list, indexes[^1]);
    }
}
