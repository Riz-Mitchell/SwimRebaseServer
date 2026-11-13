namespace Auth.Application.Interfaces;

public interface ISupabaseAdminClient
{
    /// <summary>Throws <see cref="InvalidOperationException"/> if the configuration needed to delete a user is missing.</summary>
    void EnsureConfigured();

    /// <summary>Permanently removes the user's Supabase identity. Succeeds if it is already gone.</summary>
    Task DeleteUserAsync(Guid userId, CancellationToken cancellationToken);
}
