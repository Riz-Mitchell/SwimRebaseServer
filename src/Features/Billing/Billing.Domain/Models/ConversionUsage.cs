namespace Billing.Domain.Models;

/// <summary>One swim-board conversion run counted against the user's weekly allowance.</summary>
public sealed class ConversionUsage
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    /// <summary>The sub-agent conversation the run happens in.</summary>
    public Guid ConversationId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Set when the run failed without producing a draft, which gives the use back.</summary>
    public DateTimeOffset? RefundedAt { get; set; }
}
