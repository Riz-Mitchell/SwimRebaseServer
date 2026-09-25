using Sessions.Application.Services;
using Sessions.Domain.Models;

namespace SessionConversion.Application.Services;

/// <summary>The input of an edit_draft call: which draft, and the edits to apply in order (each sees the result of the ones before it).</summary>
public sealed record DraftEditRequest(Guid DraftId, IReadOnlyList<SessionEdit> Edits)
{
    private sealed record Payload(string? DraftId, List<SessionEditDto>? Edits);

    public static DraftEditRequest Parse(string inputJson)
    {
        var payload = SessionJson.Deserialize<Payload>(inputJson);
        if (!Guid.TryParse(payload.DraftId, out var draftId))
        {
            throw new InvalidSessionEditException("draftId is missing or not a valid id - use the draft id from the read_swim_board result.");
        }

        return new DraftEditRequest(draftId, (payload.Edits ?? []).Select(e => e.ToEdit()).ToList());
    }
}
