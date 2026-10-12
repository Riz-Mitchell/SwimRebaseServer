namespace Shared.Application;

/// <summary>
/// Removes everything one feature (or external service) holds about a user. Implementations must be idempotent -
/// an account deletion that fails part way is simply run again.
/// </summary>
public interface IUserDataEraser
{
    Task EraseAsync(Guid userId, CancellationToken cancellationToken);
}
