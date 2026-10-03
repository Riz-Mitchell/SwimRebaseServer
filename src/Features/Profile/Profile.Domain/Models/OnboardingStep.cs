namespace Profile.Domain.Models;

/// <summary>Declaration order is the order users move through - <see cref="Completed"/> is terminal.</summary>
public enum OnboardingStep
{
    Questionnaire,
    FirstSession,
    ReviewWorkout,
    Schedule,
    ProTrial,
    Completed,
}
