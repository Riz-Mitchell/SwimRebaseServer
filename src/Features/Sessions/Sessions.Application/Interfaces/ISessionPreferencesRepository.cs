using Sessions.Domain.Models;

namespace Sessions.Application.Interfaces;

/// <summary>Pure query/tracking - no SaveChanges (see <see cref="Shared.Application.IUnitOfWork"/>).</summary>
public interface ISessionPreferencesRepository
{
    /// <summary>Tracked, so it can be edited. Null until the user has changed a preference.</summary>
    Task<SessionPreferences?> FindAsync(Guid userId, CancellationToken cancellationToken);

    void Add(SessionPreferences preferences);
}
