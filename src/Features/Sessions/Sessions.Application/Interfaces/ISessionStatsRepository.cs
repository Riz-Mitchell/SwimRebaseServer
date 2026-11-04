namespace Sessions.Application.Interfaces;

/// <summary>
/// The per-user session count. <see cref="AdjustAsync"/> writes immediately (a single atomic statement, not tracked), so call it inside the
/// same transaction as the session change it counts.
/// </summary>
public interface ISessionStatsRepository
{
    /// <summary>Adds <paramref name="delta"/> to the user's count, creating their row if needed. The count never goes below zero.</summary>
    Task AdjustAsync(Guid userId, int delta, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>The user's count; zero when they have no row yet.</summary>
    Task<int> GetCountAsync(Guid userId, CancellationToken cancellationToken);
}
