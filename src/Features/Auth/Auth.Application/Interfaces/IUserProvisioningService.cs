namespace Auth.Application.Interfaces;

/// <summary>
/// Ensures a Supabase-authenticated caller has a corresponding local <c>AppUser</c> row,
/// creating one on first sight. Supabase remains the identity provider - this only lets our own
/// database key data (conversations, token usage, etc.) off of a user id we know is provisioned.
/// </summary>
public interface IUserProvisioningService
{
    Task EnsureUserExistsAsync(Guid userId, string? email, CancellationToken cancellationToken);
}
