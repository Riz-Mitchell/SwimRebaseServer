namespace Auth.Application.Interfaces;

/// <summary>
/// Resolves the authenticated Supabase user (JWT `sub` claim) for the current request.
/// Every feature scopes its data access through this - nothing downstream ever accepts a
/// caller-supplied user id.
/// </summary>
public interface ICurrentUserAccessor
{
    /// <summary>Throws if there is no authenticated user on the current request.</summary>
    Guid UserId { get; }
}
