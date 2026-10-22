using Auth.Application.Exceptions;
using Auth.Application.Interfaces;
using Auth.Application.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shared.Application;

namespace Auth.Application.Services;

/// <summary>
/// Deleting an account is two steps: request a confirmation token, then confirm with it. The Supabase identity goes last so
/// that a failure part way leaves the user able to sign in and run the deletion again.
/// </summary>
public sealed class AccountDeletionService(
    IEnumerable<IUserDataEraser> erasers,
    ISupabaseAdminClient supabase,
    IDeletionConfirmation confirmation,
    IDeletedUserList deletedUsers,
    TimeProvider clock,
    ILogger<AccountDeletionService>? logger = null)
{
    private readonly ILogger<AccountDeletionService> _logger = logger ?? NullLogger<AccountDeletionService>.Instance;

    public static readonly TimeSpan RecentLoginWindow = TimeSpan.FromMinutes(5);

    public static readonly TimeSpan ConfirmationLifetime = TimeSpan.FromMinutes(10);

    public AccountDeletionRequest Request(Guid userId, DateTimeOffset? lastSignInAt)
    {
        var now = clock.GetUtcNow();
        if (lastSignInAt is not { } signedInAt || now - signedInAt > RecentLoginWindow)
        {
            _logger.LogWarning("[{ClassName}] Deletion requested by user {UserId} without a recent sign-in (last sign-in {LastSignInAt})",
                nameof(AccountDeletionService), userId, lastSignInAt);
            throw new RecentLoginRequiredException();
        }

        var expiresAt = now + ConfirmationLifetime;
        _logger.LogInformation("[{ClassName}] Issued deletion confirmation for user {UserId}, expires {ExpiresAt}",
            nameof(AccountDeletionService), userId, expiresAt);
        return new AccountDeletionRequest(confirmation.Issue(userId, expiresAt), expiresAt);
    }

    public async Task ConfirmAsync(Guid userId, string confirmationToken, CancellationToken cancellationToken)
    {
        if (!confirmation.IsValid(userId, confirmationToken))
        {
            _logger.LogWarning("[{ClassName}] Invalid or expired deletion confirmation for user {UserId}", nameof(AccountDeletionService), userId);
            throw new InvalidDeletionConfirmationException();
        }

        _logger.LogInformation("[{ClassName}] Deleting account for user {UserId}", nameof(AccountDeletionService), userId);
        try
        {
            foreach (var eraser in erasers)
            {
                await eraser.EraseAsync(userId, cancellationToken);
                _logger.LogInformation("[{ClassName}] Erased data for user {UserId} via {Eraser}",
                    nameof(AccountDeletionService), userId, eraser.GetType().Name);
            }

            await supabase.DeleteUserAsync(userId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[{ClassName}] Account deletion failed part way for user {UserId}; they can sign in and retry",
                nameof(AccountDeletionService), userId);
            throw;
        }

        deletedUsers.Add(userId);
        _logger.LogInformation("[{ClassName}] Account deleted for user {UserId}", nameof(AccountDeletionService), userId);
    }
}
