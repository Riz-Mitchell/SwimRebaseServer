using Auth.Application.Exceptions;
using Auth.Application.Interfaces;
using Auth.Application.Models;
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
    TimeProvider clock)
{
    public static readonly TimeSpan RecentLoginWindow = TimeSpan.FromMinutes(5);

    public static readonly TimeSpan ConfirmationLifetime = TimeSpan.FromMinutes(10);

    public AccountDeletionRequest Request(Guid userId, DateTimeOffset? lastSignInAt)
    {
        var now = clock.GetUtcNow();
        if (lastSignInAt is not { } signedInAt || now - signedInAt > RecentLoginWindow)
        {
            throw new RecentLoginRequiredException();
        }

        var expiresAt = now + ConfirmationLifetime;
        return new AccountDeletionRequest(confirmation.Issue(userId, expiresAt), expiresAt);
    }

    public async Task ConfirmAsync(Guid userId, string confirmationToken, CancellationToken cancellationToken)
    {
        if (!confirmation.IsValid(userId, confirmationToken))
        {
            throw new InvalidDeletionConfirmationException();
        }

        foreach (var eraser in erasers)
        {
            await eraser.EraseAsync(userId, cancellationToken);
        }

        await supabase.DeleteUserAsync(userId, cancellationToken);
        deletedUsers.Add(userId);
    }
}
