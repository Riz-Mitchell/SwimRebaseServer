namespace Profile.Domain.Models;

/// <summary>
/// The Profile schema's per-user anchor, sharing its <see cref="Id"/> with the Supabase `sub` claim
/// (no cross-database FK to Auth). Holds onboarding progress so a user who leaves mid-way resumes
/// at <see cref="CurrentStep"/>.
/// </summary>
public class UserProfile
{
    public const int ProTrialDays = 21;

    public Guid Id { get; set; }

    public OnboardingStep CurrentStep { get; set; }

    public FirstSessionPath? FirstSessionPath { get; set; }

    public bool RemindersEnabled { get; set; }

    public int ReminderLeadMinutes { get; set; }

    public DateTimeOffset? OnboardingCompletedAt { get; set; }

    public DateTimeOffset? ProTrialEndsAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public List<ProfileAnswer> Answers { get; set; } = [];
}
