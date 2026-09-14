namespace Training.Domain.Models;

/// <summary>
/// The Training schema's own anchor row for a user - one per Supabase-authenticated user (see
/// <c>Auth.Domain.Models.AppUser</c>), sharing the same <see cref="Id"/> (the JWT `sub` claim) but
/// with no cross-database foreign key back to Auth's own user table. Every other Training entity
/// hangs off this one via a real FK, keeping this schema free to live in its own database and
/// scale independently of Auth and Ai.
/// </summary>
public class TrainingUser
{
    public Guid Id { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset LastActiveAt { get; set; }

    public List<TimetableEntry> TimetableEntries { get; set; } = [];

    public List<Focus> Focuses { get; set; } = [];
}
