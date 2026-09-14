namespace Ai.Application.Interfaces;

/// <summary>
/// Ensures the caller has an <c>AiUser</c> anchor row before any Ai data is written on their
/// behalf - the single point of contact that lets every other Ai entity rely on a real,
/// database-enforced FK instead of a bare, unchecked user id.
/// </summary>
public interface IAiUserProvisioningService
{
    Task EnsureExistsAsync(Guid userId, CancellationToken cancellationToken);
}
