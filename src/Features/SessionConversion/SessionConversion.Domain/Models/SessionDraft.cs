namespace SessionConversion.Domain.Models;

/// <summary>
/// A converted session that has not been saved anywhere yet. The content lives as JSON so the model of a session can grow
/// without a migration per field; <see cref="Version"/> increases with every edit.
/// </summary>
public class SessionDraft
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    /// <summary>The conversation (the interview) this draft was produced in, if any.</summary>
    public Guid? SourceConversationId { get; set; }

    public int Version { get; set; }

    /// <summary>A <see cref="SwimSession"/> serialized with <see cref="SessionJson.Options"/>.</summary>
    public string ContentJson { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
