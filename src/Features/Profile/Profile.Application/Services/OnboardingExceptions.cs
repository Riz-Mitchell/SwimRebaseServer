namespace Profile.Application.Services;

/// <summary>The request itself is invalid (unknown answer, missing prerequisite, out-of-range value).</summary>
public sealed class InvalidOnboardingRequestException(string message) : Exception(message);

/// <summary>The request is valid but not allowed at the user's current step.</summary>
public sealed class OnboardingStepConflictException(string message) : Exception(message);
