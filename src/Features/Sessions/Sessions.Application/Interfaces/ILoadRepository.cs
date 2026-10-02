using Sessions.Application.Load;

namespace Sessions.Application.Interfaces;

public interface ILoadRepository
{
    /// <summary>The user's sessions in the date range, with their stored load. Either bound can be null for open-ended.</summary>
    Task<IReadOnlyList<SessionLoadRow>> ListAsync(Guid userId, DateOnly? from, DateOnly? to, CancellationToken cancellationToken);
}
