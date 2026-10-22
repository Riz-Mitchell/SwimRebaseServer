using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Sessions.Application.Services;
using Sessions.Domain.Calculations;
using Sessions.Domain.Models;

namespace Sessions.Infrastructure;

/// <summary>
/// Brings data saved before intensity became a percent up to date by re-mapping every effort text with the current vocabulary. Safe to run
/// on every start: a session is only touched until it is written at <see cref="SwimSession.CurrentSchemaVersion"/>, and a timed swim only
/// when its mapped value changes.
/// </summary>
public sealed class IntensityBackfill(SessionsDbContext db, IntensityResolver resolver, ILogger<IntensityBackfill> logger)
{
    private const int BatchSize = 100;

    // Arbitrary but fixed: instances starting together compete for this lock, and only the one that gets it runs the backfill.
    private const long AdvisoryLockKey = 7_402_650_113;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        // The lock belongs to the connection, so it is held open for the whole run.
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            if (!await db.Database.SqlQuery<bool>($"SELECT pg_try_advisory_lock({AdvisoryLockKey}) AS \"Value\"").SingleAsync(cancellationToken))
            {
                logger.LogInformation("[{ClassName}] Intensity backfill is already running on another instance; skipping.", nameof(IntensityBackfill));
                return;
            }

            try
            {
                var sessions = await BackfillSessionsAsync(cancellationToken);
                var timedSwims = await BackfillTimedSwimsAsync(cancellationToken);
                if (sessions + timedSwims > 0)
                {
                    logger.LogInformation("[{ClassName}] Intensity backfill updated {Sessions} sessions and {TimedSwims} timed swims.", nameof(IntensityBackfill), sessions, timedSwims);
                }
            }
            finally
            {
                await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_unlock({AdvisoryLockKey})", CancellationToken.None);
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private async Task<int> BackfillSessionsAsync(CancellationToken cancellationToken)
    {
        var marker = $"\"schemaVersion\":{SwimSession.CurrentSchemaVersion}";
        var updated = 0;

        while (true)
        {
            // Tracked, so each batch leaves the change tracker empty again and the next query no longer matches what was just written.
            var batch = await db.Sessions.Where(s => !s.ContentJson.Contains(marker)).OrderBy(s => s.Id).Take(BatchSize).ToListAsync(cancellationToken);
            if (batch.Count == 0)
            {
                return updated;
            }

            foreach (var session in batch)
            {
                var content = resolver.Resolve(SessionJson.Deserialize<SwimSession>(session.ContentJson)) with { SchemaVersion = SwimSession.CurrentSchemaVersion };
                session.ContentJson = SessionJson.Serialize(content);

                db.SessionFacts.RemoveRange(await db.SessionFacts.Where(f => f.SessionId == session.Id).ToListAsync(cancellationToken));
                db.SessionFacts.AddRange(SessionFactBuilder.Build(session, content));
            }

            await db.SaveChangesAsync(cancellationToken);
            db.ChangeTracker.Clear();
            updated += batch.Count;
        }
    }

    private async Task<int> BackfillTimedSwimsAsync(CancellationToken cancellationToken)
    {
        var updated = 0;
        foreach (var swim in await db.TimedSwims.Where(t => t.Effort != null).ToListAsync(cancellationToken))
        {
            var percent = resolver.Resolve(swim.Effort)?.Percent;
            if (swim.Intensity != percent)
            {
                swim.Intensity = percent;
                updated++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return updated;
    }
}
