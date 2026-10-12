namespace Auth.Application.Exceptions;

public sealed class RecentLoginRequiredException() : Exception("Sign in again before deleting your account.");

public sealed class InvalidDeletionConfirmationException() : Exception("The confirmation token is invalid or has expired.");
