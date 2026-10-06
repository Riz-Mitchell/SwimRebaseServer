namespace Billing.Application.Options;

public sealed class BillingOptions
{
    public const string SectionName = "Billing";

    /// <summary>Conversions a week for a user with neither a subscription nor a trial.</summary>
    public int FreeWeeklyConversions { get; set; } = 2;

    /// <summary>A ceiling for subscribers and trial users, so spend stays bounded.</summary>
    public int FairUseWeeklyConversions { get; set; } = 50;

    /// <summary>Whether purchases made in a store sandbox grant access. Off in production.</summary>
    public bool AcceptSandbox { get; set; }
}
