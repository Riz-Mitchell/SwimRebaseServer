using Analytics.Domain.Models;
using Microsoft.Extensions.Logging;
using Shared.Application;

namespace Analytics.Infrastructure;

/// <summary>
/// Writes directly to <see cref="AnalyticsDbContext"/> rather than going through the IUnitOfWork
/// pattern the other features use - there's never more than this one insert to batch per call, so
/// the extra indirection would add nothing. Never throws: this writes to a separate DbContext/
/// connection than whatever business operation is calling it, so a hiccup here must never fail a
/// chat send, a metric edit, or a signup.
/// </summary>
public sealed class EventTracker(AnalyticsDbContext db, ILogger<EventTracker> logger) : IEventTracker
{
    public async Task TrackAsync(Guid userId, string eventType, string? metadataJson, CancellationToken cancellationToken)
    {
        try
        {
            db.EventLogs.Add(new EventLog
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                EventType = eventType,
                MetadataJson = metadataJson,
                OccurredAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[{ClassName}] Failed to record analytics event {EventType} for user {UserId}", nameof(EventTracker), eventType, userId);
        }
    }
}
