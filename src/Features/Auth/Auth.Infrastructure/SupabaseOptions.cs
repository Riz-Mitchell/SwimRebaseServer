namespace Auth.Infrastructure;

public sealed class SupabaseOptions
{
    public const string SectionName = "Supabase";

    public string Url { get; set; } = string.Empty;

    /// <summary>The project's service-role key, used only to delete users through the Auth admin API.</summary>
    public string ServiceRoleKey { get; set; } = string.Empty;
}
