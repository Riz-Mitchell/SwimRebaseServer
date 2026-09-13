namespace Ai.Application.Interfaces;

/// <summary>
/// Resolves the authenticated Supabase user (JWT `sub` claim) for the current request.
/// Every repository and tool call is scoped through this - nothing in Ai.Application ever
/// accepts a caller-supplied user id.
/// </summary>
public interface ICurrentUserAccessor
{
    /// <summary>Throws if there is no authenticated user on the current request.</summary>
    Guid UserId { get; }
}
