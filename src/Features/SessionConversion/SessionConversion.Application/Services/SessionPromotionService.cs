using Sessions.Application.Services;
using Sessions.Domain.Models;

namespace SessionConversion.Application.Services;

/// <summary>Turns a draft into a saved session. The draft is not changed: the session records which draft it came from.</summary>
public sealed class SessionPromotionService(SessionDraftService drafts, SessionService sessions)
{
    /// <summary>Saving the same draft twice returns the session the first save made.</summary>
    /// <exception cref="DraftNotFoundException">There is no such draft for this user.</exception>
    public async Task<SessionView> SaveAsync(
        Guid userId, Guid draftId, DateOnly performedOn, PoolCourse course, string? title, CancellationToken cancellationToken)
    {
        var draft = await drafts.GetAsync(userId, draftId, cancellationToken);
        return await sessions.CreateAsync(userId, draft.Session, performedOn, course, title, SessionSource.Ai, draft.Id, cancellationToken);
    }
}
