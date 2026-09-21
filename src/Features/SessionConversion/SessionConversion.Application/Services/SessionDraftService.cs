using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SessionConversion.Application.Interfaces;
using SessionConversion.Domain.Calculations;
using SessionConversion.Domain.Models;
using Shared.Application;

namespace SessionConversion.Application.Services;

/// <summary>A stored draft with its session parsed. <see cref="Outline"/> is the numbered text form the main chat's model reads.</summary>
public sealed record DraftView(Guid Id, int Version, SwimSession Session)
{
    public string Outline => SessionText.Outline(Session);
}

/// <summary>The session failed validation. The message lists what to fix and is written for the model that produced it.</summary>
public sealed class InvalidSessionException(IReadOnlyList<string> problems)
    : Exception($"The session is not valid: {string.Join("; ", problems)}. Call the tool again with these fixed.")
{
    public IReadOnlyList<string> Problems { get; } = problems;
}

public sealed class DraftNotFoundException(Guid draftId) : Exception($"No draft with id {draftId} exists for this user.");

/// <summary>Creates, edits and reads drafts. Everything is validated here, so nothing invalid is ever stored.</summary>
public sealed class SessionDraftService(
    ISessionDraftRepository drafts,
    [FromKeyedServices(SessionConversionKeys.UnitOfWork)] IUnitOfWork unitOfWork)
{
    /// <exception cref="InvalidSessionException">The JSON could not be read as a session, or it failed validation.</exception>
    public async Task<DraftView> CreateAsync(Guid userId, Guid? sourceConversationId, string sessionJson, CancellationToken cancellationToken)
    {
        var session = Parse(sessionJson);
        RequireValid(session);

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
        return new DraftView(draft.Id, draft.Version, session);
    }

    /// <exception cref="DraftNotFoundException">There is no such draft for this user.</exception>
    /// <exception cref="InvalidDraftEditException">An edit is malformed or does not fit the draft.</exception>
    /// <exception cref="InvalidSessionException">The edited session would be invalid.</exception>
    public async Task<DraftView> EditAsync(Guid userId, Guid draftId, IReadOnlyList<DraftEdit> edits, CancellationToken cancellationToken)
    {
        var draft = await drafts.FindAsync(userId, draftId, cancellationToken) ?? throw new DraftNotFoundException(draftId);

        var edited = DraftEditor.Apply(SessionJson.Deserialize<SwimSession>(draft.ContentJson), edits);
        RequireValid(edited);

        draft.ContentJson = SessionJson.Serialize(edited);
        draft.Version++;
        draft.UpdatedAt = DateTimeOffset.UtcNow;
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new DraftView(draft.Id, draft.Version, edited);
    }

    public async Task<DraftView?> FindLatestForConversationAsync(Guid userId, Guid conversationId, CancellationToken cancellationToken)
    {
        var draft = await drafts.FindLatestForConversationAsync(userId, conversationId, cancellationToken);
        return draft is null ? null : new DraftView(draft.Id, draft.Version, SessionJson.Deserialize<SwimSession>(draft.ContentJson));
    }

    private static SwimSession Parse(string sessionJson)
    {
        try
        {
            return SessionJson.Deserialize<SwimSession>(sessionJson);
        }
        catch (JsonException ex)
        {
            throw new InvalidSessionException([$"could not read the session: {ex.Message}"]);
        }
    }

    private static void RequireValid(SwimSession session)
    {
        var problems = SessionValidator.Validate(session);
        if (problems.Count > 0)
        {
            throw new InvalidSessionException(problems);
        }
    }
}
