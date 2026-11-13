namespace Shared.Application;

/// <summary>
/// Removes everything one feature (or external service) holds about a user. Implementations must be idempotent -
/// an account deletion that fails part way is simply run again.
/// </summary>
public interface IUserDataEraser
{
    /// <summary>
    /// Throws if something this eraser needs (a secret, a key) is not configured. Called before any eraser runs, so a deployment with
    /// missing configuration refuses the deletion up front instead of failing after some data is already gone.
    /// </summary>
    void EnsureReady() { }

    Task EraseAsync(Guid userId, CancellationToken cancellationToken);
}
