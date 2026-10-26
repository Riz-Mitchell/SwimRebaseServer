using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Sessions.Application.Interfaces;
using Sessions.Domain.Calculations;
using Sessions.Domain.Models;
using Shared.Application;

namespace Sessions.Application.Services;

/// <summary>A saved session with its tree parsed. <see cref="Outline"/> is the numbered text form the main chat's model reads.</summary>
public sealed record SessionView(
    Guid Id,
    DateOnly PerformedOn,
    string Title,
    PoolCourse Course,
    DistanceUnit Unit,
    SessionSource Source,
    decimal TotalDistance,
    SwimSession Content)
{
    public string Outline => SessionText.Outline(Content);

    public static SessionView Of(Session session) => new(
        session.Id, session.PerformedOn, session.Title, session.Course, session.Unit, session.Source, session.TotalDistance,
        SessionJson.Deserialize<SwimSession>(session.ContentJson));
}

/// <summary>A saved session with its tree parsed, for lists that carry the content along with the summary.</summary>
public sealed record SessionWithContent(Session Session, SwimSession Content);

public sealed class SessionNotFoundException(Guid id) : Exception($"No session with id {id} exists for this user.");

/// <summary>Saves and edits sessions. The tree, the total and the fact rows are always written together, so they cannot drift apart.</summary>
public sealed class SessionService(
    ISessionRepository sessions,
    SessionIngest ingest,
    IPersonalIntensityProvider personal,
    IProgressionDefaultsProvider progressionDefaults,
    [FromKeyedServices(SessionsKeys.UnitOfWork)] IUnitOfWork unitOfWork,
    ILogger<SessionService>? logger = null)
{
    private readonly ILogger<SessionService> _logger = logger ?? NullLogger<SessionService>.Instance;

    /// <summary>The most sessions one list call returns when it carries their content.</summary>
    public const int MaxContentLimit = 20;

    /// <summary>
    /// Idempotent on <paramref name="sourceDraftId"/>: saving the same draft again returns the session it already made.
    /// </summary>
    /// <exception cref="InvalidSessionException">The session is not valid.</exception>
    public async Task<SessionView> CreateAsync(
        Guid userId,
        SwimSession content,
        DateOnly performedOn,
        PoolCourse course,
        string? title,
        SessionSource source,
        Guid? sourceDraftId,
        CancellationToken cancellationToken)
    {
        if (sourceDraftId is { } draftId && await sessions.FindBySourceDraftAsync(userId, draftId, cancellationToken) is { } existing)
        {
            _logger.LogInformation("[{ClassName}] Draft {DraftId} was already saved as session {SessionId} for user {UserId}", nameof(SessionService), draftId, existing.Id, userId);
            return SessionView.Of(existing);
        }

        var prepared = NodeIds.Assign(ingest.Prepare(content, await personal.MapperAsync(userId, cancellationToken), await progressionDefaults.DefaultsAsync(userId, cancellationToken))) with { Title = null };
        var now = DateTimeOffset.UtcNow;
        var session = new Session
        {
            Id = Guid.NewGuid(),
            OwnerId = userId,
            PerformedOn = performedOn,
            Course = course,
            Source = source,
            SourceDraftId = sourceDraftId,
            Title = string.Empty,
            TotalDistance = SessionCalculator.Total(prepared),
            ContentJson = SessionJson.Serialize(prepared),
            CreatedAt = now,
            UpdatedAt = now,
        };

        // An explicit title wins, then the one the converter wrote, then one built from what was swum.
        session.Title = SessionTitles.Clean(title) ?? SessionTitles.Clean(content.Title) ?? SessionTitles.Build(prepared, session.Unit, performedOn);

        sessions.Add(session, SessionFactBuilder.Build(session, prepared));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("[{ClassName}] Saved session {SessionId} '{Title}' ({TotalDistance} {Unit}) for user {UserId} from {Source}",
            nameof(SessionService), session.Id, session.Title, session.TotalDistance, session.Unit, userId, source);
        return new SessionView(session.Id, session.PerformedOn, session.Title, session.Course, session.Unit, session.Source, session.TotalDistance, prepared);
    }

    /// <exception cref="SessionNotFoundException">There is no such session for this user.</exception>
    /// <exception cref="InvalidSessionEditException">An edit is malformed or does not fit the session.</exception>
    /// <exception cref="InvalidSessionException">The edited session would be invalid.</exception>
    public async Task<SessionView> EditAsync(Guid userId, Guid id, IReadOnlyList<SessionEdit> edits, CancellationToken cancellationToken)
    {
        var session = await sessions.FindAsync(userId, id, cancellationToken);
        if (session is null)
        {
            _logger.LogWarning("[{ClassName}] User {UserId} tried to edit session {SessionId}, which does not exist for them", nameof(SessionService), userId, id);
            throw new SessionNotFoundException(id);
        }

        var edited = NodeIds.Assign(ingest.Prepare(
            SessionEditor.Apply(SessionJson.Deserialize<SwimSession>(session.ContentJson), edits),
            await personal.MapperAsync(userId, cancellationToken),
            await progressionDefaults.DefaultsAsync(userId, cancellationToken)));

        session.ContentJson = SessionJson.Serialize(edited);
        session.TotalDistance = SessionCalculator.Total(edited);
        session.UpdatedAt = DateTimeOffset.UtcNow;
        await sessions.ReplaceFactsAsync(session.Id, SessionFactBuilder.Build(session, edited), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("[{ClassName}] Applied {EditCount} edit(s) to session {SessionId} for user {UserId}; total now {TotalDistance}",
            nameof(SessionService), edits.Count, id, userId, session.TotalDistance);
        return new SessionView(session.Id, session.PerformedOn, session.Title, session.Course, session.Unit, session.Source, session.TotalDistance, edited);
    }

    public async Task<SessionView> GetAsync(Guid userId, Guid id, CancellationToken cancellationToken)
    {
        var session = await sessions.FindAsync(userId, id, cancellationToken);
        if (session is null)
        {
            _logger.LogWarning("[{ClassName}] User {UserId} asked for session {SessionId}, which does not exist for them", nameof(SessionService), userId, id);
            throw new SessionNotFoundException(id);
        }

        return SessionView.Of(session);
    }

    public async Task<IReadOnlyList<Session>> ListAsync(Guid userId, DateOnly? from, DateOnly? to, int limit, int offset, CancellationToken cancellationToken)
    {
        var found = await sessions.ListAsync(userId, from, to, Math.Clamp(limit, 1, 100), Math.Max(offset, 0), cancellationToken);
        _logger.LogDebug("[{ClassName}] Listed {Count} session(s) for user {UserId}", nameof(SessionService), found.Count, userId);
        return found;
    }

    public async Task<IReadOnlyList<SessionWithContent>> ListWithContentAsync(
        Guid userId, DateOnly? from, DateOnly? to, int limit, int offset, CancellationToken cancellationToken)
    {
        var found = await sessions.ListAsync(userId, from, to, Math.Clamp(limit, 1, MaxContentLimit), Math.Max(offset, 0), cancellationToken);
        _logger.LogDebug("[{ClassName}] Listed {Count} session(s) with content for user {UserId}", nameof(SessionService), found.Count, userId);
        return found.Select(s => new SessionWithContent(s, SessionJson.Deserialize<SwimSession>(s.ContentJson))).ToList();
    }

    /// <summary>The titles of the swimmer's most recent sessions, newest first and without repeats.</summary>
    public async Task<IReadOnlyList<string>> RecentTitlesAsync(Guid userId, int count, CancellationToken cancellationToken) =>
        (await sessions.RecentTitlesAsync(userId, Math.Clamp(count, 1, 50), cancellationToken)).Distinct().ToList();
}
