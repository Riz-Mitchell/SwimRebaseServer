using System.Text.Json;
using Sessions.Domain.Models;

namespace Sessions.Application.Services;

public static class SessionEditOps
{
    public const string Remove = "remove";
    public const string Replace = "replace";
    public const string Insert = "insert";
    public const string SetGroup = "set_group";
}

/// <param name="Op">One of <see cref="SessionEditOps"/>.</param>
/// <param name="Path">Dot-separated indexes into the outline, e.g. "2" or "1.0" (item 0 inside set 1). For an insert, the position to insert at.</param>
/// <param name="Node">The new set, for replace and insert.</param>
/// <param name="Group">The new group, for set_group (empty clears it).</param>
public sealed record SessionEdit(string Op, string? Path, SessionNode? Node, string? Group);

/// <summary>An edit as it arrives over the wire (a model's tool call or a REST body); <see cref="ToEdit"/> reads the node with the session's own serializer.</summary>
public sealed record SessionEditDto(string? Op, string? Path, JsonElement? Node, string? Group)
{
    public SessionEdit ToEdit() => new(
        Op ?? string.Empty,
        Path,
        Node is { ValueKind: JsonValueKind.Object } node ? ReadNode(node) : null,
        Group);

    private static SessionNode ReadNode(JsonElement node)
    {
        try
        {
            return SessionJson.Deserialize<SessionNode>(node);
        }
        catch (JsonException ex)
        {
            throw new InvalidSessionEditException($"could not read the node: {ex.Message}");
        }
    }
}

/// <summary>A malformed or impossible edit. The message is written for the model that made it.</summary>
public sealed class InvalidSessionEditException(string message) : Exception(message);

/// <summary>Applies <see cref="SessionEdit"/>s to a session without mutating the original.</summary>
public static class SessionEditor
{
    public static SwimSession Apply(SwimSession session, IReadOnlyList<SessionEdit> edits)
    {
        if (edits.Count == 0)
        {
            throw new InvalidSessionEditException("edits is empty - nothing to change.");
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
            catch (InvalidSessionEditException ex)
            {
                throw new InvalidSessionEditException($"edit {n} ({edit.Op}): {ex.Message}");
            }
        }

        return working;
    }

    private static SwimSession ApplyOne(SwimSession session, SessionEdit edit)
    {
        if (edit.Op == SessionEditOps.SetGroup)
        {
            return session with { Group = string.IsNullOrWhiteSpace(edit.Group) ? null : edit.Group.Trim() };
        }

        if (edit.Op is not (SessionEditOps.Remove or SessionEditOps.Replace or SessionEditOps.Insert))
        {
            throw new InvalidSessionEditException($"unknown op '{edit.Op}'. Use remove, replace, insert or set_group.");
        }

        var (list, index) = Locate(session, edit.Path);

        switch (edit.Op)
        {
            case SessionEditOps.Remove:
                RequireIndexInRange(index, list.Count);
                list.RemoveAt(index);
                break;
            case SessionEditOps.Replace:
                RequireIndexInRange(index, list.Count);
                list[index] = edit.Node ?? throw new InvalidSessionEditException("replace needs a node.");
                break;
            case SessionEditOps.Insert:
                if (index < 0 || index > list.Count)
                {
                    throw new InvalidSessionEditException($"cannot insert at {index}; this level has {list.Count} entries (use {list.Count} to append).");
                }

                list.Insert(index, edit.Node ?? throw new InvalidSessionEditException("insert needs a node."));
                break;
        }

        return session;
    }

    private static void RequireIndexInRange(int index, int count)
    {
        if (index < 0 || index >= count)
        {
            throw new InvalidSessionEditException($"there is no entry {index}; this level has {count}.");
        }
    }

    /// <summary>Walks a path like "1.0" down through groups and returns the list holding the target plus the target's index in it.</summary>
    private static (List<SessionNode> List, int Index) Locate(SwimSession session, string? path)
    {
        var parts = (path ?? string.Empty).Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || parts.Any(p => !int.TryParse(p, out _)))
        {
            throw new InvalidSessionEditException($"path '{path}' is not valid - use the index shown in the outline, e.g. \"2\" or \"1.0\".");
        }

        var indexes = parts.Select(int.Parse).ToArray();
        var list = session.Sets;
        for (var depth = 0; depth < indexes.Length - 1; depth++)
        {
            if (indexes[depth] < 0 || indexes[depth] >= list.Count || list[indexes[depth]] is not SetGroup group)
            {
                throw new InvalidSessionEditException($"path '{path}' does not lead through a set of rounds.");
            }

            list = group.Items;
        }

        return (list, indexes[^1]);
    }
}
