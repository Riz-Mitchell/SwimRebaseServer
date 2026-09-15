namespace Ai.Domain.Models;

/// <summary>
/// The Ai schema's own anchor row for a user - one per Supabase-authenticated user (see
/// <c>Auth.Domain.Models.AppUser</c>), sharing the same <see cref="Id"/> (the JWT `sub` claim) but
/// with no cross-database foreign key back to Auth's own user table. Every other Ai entity hangs
/// off this one via a real FK, so the Ai schema keeps referential integrity at the database level
/// while staying free to live in its own database and scale independently of Auth.
/// </summary>
public class AiUser
{
    public Guid Id { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset LastActiveAt { get; set; }

    public List<Conversation> Conversations { get; set; } = [];

    public List<ToolExecutionRequest> ToolExecutionRequests { get; set; } = [];

    public List<TokenUsageRecord> TokenUsageRecords { get; set; } = [];

    public List<UploadedImage> UploadedImages { get; set; } = [];
}
