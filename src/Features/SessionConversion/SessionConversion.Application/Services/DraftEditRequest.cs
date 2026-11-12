using System.Text.Json;
using Sessions.Application.Services;
using Sessions.Domain.Models;

namespace SessionConversion.Application.Services;

/// <summary>The input of an edit_draft call: which draft, and the edits to apply in order (each sees the result of the ones before it).</summary>
public sealed record DraftEditRequest(Guid DraftId, IReadOnlyList<SessionEdit> Edits)
{
    private sealed record Payload(string? DraftId, JsonElement? Edits);

    public static DraftEditRequest Parse(string inputJson)
    {
        Payload payload;
        try
        {
            payload = SessionJson.Deserialize<Payload>(inputJson);
        }
        catch (JsonException ex)
        {
            throw new InvalidSessionEditException($"could not read the edit_draft input: {ex.Message}");
        }

        if (!Guid.TryParse(payload.DraftId, out var draftId))
        {
            throw new InvalidSessionEditException("draftId is missing or not a valid id - use the draft id from the read_swim_board result.");
        }

        return new DraftEditRequest(draftId, ReadEdits(payload.Edits).Select(e => e.ToEdit()).ToList());
    }

    /// <summary>
    /// Reads the edits array. A model sometimes sends the array as a JSON-encoded string ("[{\"op\":...}]") instead of a real array;
    /// that is unambiguous, so it is accepted rather than costing the model a retry.
    /// </summary>
    private static List<SessionEditDto> ReadEdits(JsonElement? edits)
    {
        const string Shape = "edits must be a JSON array of edit objects, e.g. [{\"op\":\"remove\",\"path\":\"2\"}] - not a string or a single object.";

        if (edits is null or { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined })
        {
            return [];
        }

        var element = edits.Value;
        if (element.ValueKind == JsonValueKind.String)
        {
            try
            {
                using var inner = JsonDocument.Parse(element.GetString() ?? string.Empty);
                element = inner.RootElement.Clone();
            }
            catch (JsonException)
            {
                throw new InvalidSessionEditException(Shape);
            }
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidSessionEditException(Shape);
        }

        try
        {
            return SessionJson.Deserialize<List<SessionEditDto>>(element);
        }
        catch (JsonException ex)
        {
            throw new InvalidSessionEditException($"could not read an entry in edits: {ex.Message}. {Shape}");
        }
    }
}
