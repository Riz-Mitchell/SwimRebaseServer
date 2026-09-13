namespace Auth.Domain.Models;

/// <summary>
/// A user provisioned from a validated Supabase JWT. <see cref="Id"/> is always the token's
/// `sub` claim - Supabase remains the sole source of truth for credentials; this table only
/// tracks the subset of user data our own backend needs to key data off of.
/// </summary>
public sealed class AppUser
{
    public Guid Id { get; set; }

    public string? Email { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset LastSeenAt { get; set; }
}
