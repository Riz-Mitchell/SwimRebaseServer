namespace Auth.Application.Interfaces;

/// <summary>A short-lived token proving the user asked to delete their account moments ago.</summary>
public interface IDeletionConfirmation
{
    string Issue(Guid userId, DateTimeOffset expiresAt);

    /// <summary>False if the token is expired, tampered with, or was issued for a different user.</summary>
    bool IsValid(Guid userId, string token);
}
