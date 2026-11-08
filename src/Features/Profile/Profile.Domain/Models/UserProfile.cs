namespace Profile.Domain.Models;

/// <summary>
/// The Profile schema's per-user anchor, sharing its <see cref="Id"/> with the Supabase `sub` claim
/// (no cross-database FK to Auth). Holds onboarding progress so a user who leaves mid-way resumes
/// at <see cref="CurrentStep"/>.
/// </summary>
public class UserProfile
{
    public const int ProTrialDays = 21;

    public const int MaxAiConsentVersionLength = 50;

    public Guid Id { get; set; }

    public OnboardingStep CurrentStep { get; set; }

    public FirstSessionPath? FirstSessionPath { get; set; }

    /// <summary>The wording version of the AI data consent the user last accepted; null when no consent is held (never given, or withdrawn).</summary>
    public string? AiConsentVersion { get; set; }

    /// <summary>When <see cref="AiConsentVersion"/> was accepted (UTC).</summary>
    public DateTimeOffset? AiConsentAt { get; set; }

    public bool HasAiConsent => AiConsentVersion is not null && AiConsentAt is not null;

    public bool RemindersEnabled { get; set; }

    public int ReminderLeadMinutes { get; set; }

    public DateTimeOffset? OnboardingCompletedAt { get; set; }

    public DateTimeOffset? ProTrialEndsAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
