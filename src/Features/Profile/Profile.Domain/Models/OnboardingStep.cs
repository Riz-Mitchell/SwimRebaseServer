namespace Profile.Domain.Models;

/// <summary>Declaration order is the order users move through - <see cref="Completed"/> is terminal.</summary>
public enum OnboardingStep
{
    /// <summary>Informed consent to send photos and chat messages to the AI provider; must precede anything that does so.</summary>
    AiConsent,
    FirstSession,
    ReviewWorkout,
    ProTrial,
    Completed,
}
