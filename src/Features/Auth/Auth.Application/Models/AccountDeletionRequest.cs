namespace Auth.Application.Models;

public sealed record AccountDeletionRequest(string ConfirmationToken, DateTimeOffset ExpiresAt);
