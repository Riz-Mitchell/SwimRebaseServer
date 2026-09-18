namespace Shared.Application;

/// <summary>
/// Records a lightweight app event (see <see cref="EventTypes"/>) for later analytics (weekly active
/// users, cohort retention, feature usage). Implemented against its own separate store in the
/// Analytics feature - callers never need to worry about that store being unavailable: an
/// implementation of this interface must never let a tracking failure fail the caller's own request.
/// </summary>
public interface IEventTracker
{
    Task TrackAsync(Guid userId, string eventType, string? metadataJson, CancellationToken cancellationToken);
}
