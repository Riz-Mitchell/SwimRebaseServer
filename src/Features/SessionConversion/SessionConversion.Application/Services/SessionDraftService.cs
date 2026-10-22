using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Sessions.Application.Services;
using Sessions.Domain.Models;
using SessionConversion.Application.Interfaces;
using SessionConversion.Domain.Models;
using Shared.Application;

namespace SessionConversion.Application.Services;

/// <summary>A stored draft with its session parsed. <see cref="Outline"/> is the numbered text form the main chat's model reads.</summary>
public sealed record DraftView(Guid Id, int Version, SwimSession Session)
{
    public string Outline => SessionText.Outline(Session);
}

public sealed class DraftNotFoundException(Guid draftId) : Exception($"No draft with id {draftId} exists for this user.");

/// <summary>Creates, edits and reads drafts. Everything goes through <see cref="SessionIngest"/>, so nothing invalid is ever stored.</summary>
public sealed class SessionDraftService(
    ISessionDraftRepository drafts,
    SessionIngest ingest,
    IPersonalIntensityProvider personal,
    [FromKeyedServices(SessionConversionKeys.UnitOfWork)] IUnitOfWork unitOfWork,
    ILogger<SessionDraftService>? logger = null)
{
    private readonly ILogger<SessionDraftService> _logger = logger ?? NullLogger<SessionDraftService>.Instance;

    /// <exception cref="InvalidSessionException">The JSON could not be read as a session, or it failed validation.</exception>
    public async Task<DraftView> CreateAsync(Guid userId, Guid? sourceConversationId, string sessionJson, CancellationToken cancellationToken)
    {
        var session = ingest.Prepare(ingest.Parse(sessionJson), await personal.MapperAsync(userId, cancellationToken));

        var now = DateTimeOffset.UtcNow;
        var draft = new SessionDraft
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            SourceConversationId = sourceConversationId,
            Version = 1,
            ContentJson = SessionJson.Serialize(session),
            CreatedAt = now,
            UpdatedAt = now,
        };

        drafts.Add(draft);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("[{ClassName}] Created draft {DraftId} for user {UserId} from conversation {ConversationId}", nameof(SessionDraftService), draft.Id, userId, sourceConversationId);
        return new DraftView(draft.Id, draft.Version, session);
    }

    /// <exception cref="DraftNotFoundException">There is no such draft for this user.</exception>
    /// <exception cref="InvalidSessionEditException">An edit is malformed or does not fit the draft.</exception>
    /// <exception cref="InvalidSessionException">The edited session would be invalid.</exception>
    public async Task<DraftView> EditAsync(Guid userId, Guid draftId, IReadOnlyList<SessionEdit> edits, CancellationToken cancellationToken)
    {
        var draft = await drafts.FindAsync(userId, draftId, cancellationToken);
        if (draft is null)
        {
            _logger.LogWarning("[{ClassName}] User {UserId} tried to edit draft {DraftId}, which does not exist for them", nameof(SessionDraftService), userId, draftId);
            throw new DraftNotFoundException(draftId);
        }

        var edited = ingest.Prepare(
            SessionEditor.Apply(SessionJson.Deserialize<SwimSession>(draft.ContentJson), edits), await personal.MapperAsync(userId, cancellationToken));

        draft.ContentJson = SessionJson.Serialize(edited);
        draft.Version++;
        draft.UpdatedAt = DateTimeOffset.UtcNow;
        await unitOfWork.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("[{ClassName}] Applied {EditCount} edit(s) to draft {DraftId} for user {UserId}; now version {Version}", nameof(SessionDraftService), edits.Count, draftId, userId, draft.Version);
        return new DraftView(draft.Id, draft.Version, edited);
    }

    /// <exception cref="DraftNotFoundException">There is no such draft for this user.</exception>
    public async Task<DraftView> GetAsync(Guid userId, Guid draftId, CancellationToken cancellationToken)
    {
        var draft = await drafts.FindAsync(userId, draftId, cancellationToken);
        if (draft is null)
        {
            _logger.LogWarning("[{ClassName}] User {UserId} asked for draft {DraftId}, which does not exist for them", nameof(SessionDraftService), userId, draftId);
            throw new DraftNotFoundException(draftId);
        }

        return new DraftView(draft.Id, draft.Version, SessionJson.Deserialize<SwimSession>(draft.ContentJson));
    }

    public async Task<DraftView?> FindLatestForConversationAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken)
    {
        var draft = await drafts.FindLatestForConversationAsync(userId, conversationId, cancellationToken);
        return draft is null ? null : new DraftView(draft.Id, draft.Version, SessionJson.Deserialize<SwimSession>(draft.ContentJson));
    }
}
