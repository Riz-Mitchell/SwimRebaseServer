namespace Auth.Application.Interfaces;

public interface ISupabaseAdminClient
{
    /// <summary>Permanently removes the user's Supabase identity. Succeeds if it is already gone.</summary>
    Task DeleteUserAsync(Guid userId, CancellationToken cancellationToken);
}
