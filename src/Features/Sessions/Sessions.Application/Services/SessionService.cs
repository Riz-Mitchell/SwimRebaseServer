using Microsoft.Extensions.DependencyInjection;
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
    int TotalDistance,
    SwimSession Content,
    double Load,
    int UnratedDistance)
{
    public string Outline => SessionText.Outline(Content);

    public static SessionView Of(Session session) => new(
        session.Id, session.PerformedOn, session.Title, session.Course, session.Unit, session.Source, session.TotalDistance,
        SessionJson.Deserialize<SwimSession>(session.ContentJson), session.Load, session.UnratedDistance);
}

public sealed class SessionNotFoundException(Guid id) : Exception($"No session with id {id} exists for this user.");

/// <summary>Saves and edits sessions. The tree, the total and the fact rows are always written together, so they cannot drift apart.</summary>
public sealed class SessionService(
    ISessionRepository sessions,
    SessionIngest ingest,
    SessionLoadService load,
    [FromKeyedServices(SessionsKeys.UnitOfWork)] IUnitOfWork unitOfWork)
{
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
            return SessionView.Of(existing);
        }

        var prepared = NodeIds.Assign(ingest.Prepare(content));
        var now = DateTimeOffset.UtcNow;
        var session = new Session
        {
            Id = Guid.NewGuid(),
            OwnerId = userId,
            PerformedOn = performedOn,
            Course = course,
            Source = source,
            SourceDraftId = sourceDraftId,
            Title = string.IsNullOrWhiteSpace(title) ? DefaultTitle(performedOn) : title.Trim(),
            TotalDistance = SessionCalculator.Total(prepared),
            ContentJson = SessionJson.Serialize(prepared),
            CreatedAt = now,
            UpdatedAt = now,
        };

        await load.ApplyAsync(userId, session, SessionLeaves.Flatten(prepared), cancellationToken);
        sessions.Add(session, SessionFactBuilder.Build(session, prepared));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new SessionView(session.Id, session.PerformedOn, session.Title, session.Course, session.Unit, session.Source, session.TotalDistance, prepared, session.Load, session.UnratedDistance);
    }

    /// <exception cref="SessionNotFoundException">There is no such session for this user.</exception>
    /// <exception cref="InvalidSessionEditException">An edit is malformed or does not fit the session.</exception>
    /// <exception cref="InvalidSessionException">The edited session would be invalid.</exception>
    public async Task<SessionView> EditAsync(Guid userId, Guid id, IReadOnlyList<SessionEdit> edits, CancellationToken cancellationToken)
    {
        var session = await sessions.FindAsync(userId, id, cancellationToken) ?? throw new SessionNotFoundException(id);

        var edited = NodeIds.Assign(ingest.Prepare(SessionEditor.Apply(SessionJson.Deserialize<SwimSession>(session.ContentJson), edits)));

        session.ContentJson = SessionJson.Serialize(edited);
        session.TotalDistance = SessionCalculator.Total(edited);
        session.UpdatedAt = DateTimeOffset.UtcNow;
        await load.ApplyAsync(userId, session, SessionLeaves.Flatten(edited), cancellationToken);
        await sessions.ReplaceFactsAsync(session.Id, SessionFactBuilder.Build(session, edited), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new SessionView(session.Id, session.PerformedOn, session.Title, session.Course, session.Unit, session.Source, session.TotalDistance, edited, session.Load, session.UnratedDistance);
    }

    public async Task<SessionView> GetAsync(Guid userId, Guid id, CancellationToken cancellationToken) =>
        SessionView.Of(await sessions.FindAsync(userId, id, cancellationToken) ?? throw new SessionNotFoundException(id));

    public async Task<IReadOnlyList<Session>> ListAsync(Guid userId, DateOnly? from, DateOnly? to, int limit, int offset, CancellationToken cancellationToken) =>
        await sessions.ListAsync(userId, from, to, Math.Clamp(limit, 1, 100), Math.Max(offset, 0), cancellationToken);

    private static string DefaultTitle(DateOnly performedOn) => $"Swim {performedOn:yyyy-MM-dd}";
}
