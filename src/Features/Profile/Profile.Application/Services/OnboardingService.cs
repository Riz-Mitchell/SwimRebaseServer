using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Profile.Application.Interfaces;
using Profile.Domain.Models;
using Shared.Application;

namespace Profile.Application.Services;

/// <summary>
/// Drives the linear onboarding flow. <see cref="UserProfile.CurrentStep"/> is the single source of
/// truth for where a user resumes; steps only ever move forward.
/// </summary>
public sealed class OnboardingService(
    IUserProfileRepository profiles,
    [FromKeyedServices(ProfileKeys.UnitOfWork)] IUnitOfWork unitOfWork,
    IEventTracker eventTracker,
    ILogger<OnboardingService>? logger = null)
{
    private readonly ILogger<OnboardingService> _logger = logger ?? NullLogger<OnboardingService>.Instance;

    private static readonly OnboardingStep[] SkippableSteps = [OnboardingStep.ProTrial];

    public async Task<UserProfile> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        await profiles.EnsureExistsAsync(userId, DateTimeOffset.UtcNow, cancellationToken);
        var profile = (await profiles.FindAsync(userId, cancellationToken))!;
        _logger.LogDebug("[{ClassName}] Loaded profile for user {UserId} at step {Step}", nameof(OnboardingService), userId, profile.CurrentStep);
        return profile;
    }

    public async Task<UserProfile> ChooseFirstSessionPathAsync(
        Guid userId, FirstSessionPath path, CancellationToken cancellationToken)
    {
        var profile = await GetAsync(userId, cancellationToken);
        profile.FirstSessionPath = path;
        profile.UpdatedAt = DateTimeOffset.UtcNow;
        await unitOfWork.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("[{ClassName}] User {UserId} chose first-session path {Path}", nameof(OnboardingService), userId, path);
        return profile;
    }

    /// <summary>Completing a step the user is already past is a no-op, so a client retry is safe.</summary>
    public async Task<UserProfile> CompleteStepAsync(
        Guid userId, OnboardingStep step, CancellationToken cancellationToken, string? aiConsentVersion = null)
    {
        var profile = await GetAsync(userId, cancellationToken);
        if (step == OnboardingStep.Completed)
        {
            _logger.LogWarning("[{ClassName}] User {UserId} tried to complete the 'Completed' step", nameof(OnboardingService), userId);
            throw new InvalidOnboardingRequestException("'Completed' is not a step that can be completed.");
        }

        var consentVersion = step == OnboardingStep.AiConsent ? ValidateConsentVersion(userId, aiConsentVersion) : null;

        if (step < profile.CurrentStep)
        {
            _logger.LogInformation("[{ClassName}] User {UserId} re-completed {Step} but is already at {CurrentStep}; nothing to do",
                nameof(OnboardingService), userId, step, profile.CurrentStep);
            if (consentVersion is not null && !profile.HasAiConsent)
            {
                // Past the step but holding no consent (withdrawn): accepting again restores it.
                await GrantConsentAsync(profile, consentVersion, cancellationToken);
            }

            return profile;
        }

        if (step > profile.CurrentStep)
        {
            _logger.LogWarning("[{ClassName}] User {UserId} tried to complete {Step} while at {CurrentStep}", nameof(OnboardingService), userId, step, profile.CurrentStep);
            throw new OnboardingStepConflictException($"Cannot complete '{step}' while the current step is '{profile.CurrentStep}'.");
        }

        if (step == OnboardingStep.FirstSession && profile.FirstSessionPath is null)
        {
            _logger.LogWarning("[{ClassName}] User {UserId} tried to complete the first session without choosing a path", nameof(OnboardingService), userId);
            throw new InvalidOnboardingRequestException("Choose how to analyse the first session first.");
        }

        if (consentVersion is not null && !profile.HasAiConsent)
        {
            SetConsent(profile, consentVersion, DateTimeOffset.UtcNow);
        }

        return await AdvanceAndSaveAsync(profile, cancellationToken);
    }

    /// <summary>
    /// Records acceptance outside onboarding (users who onboarded before consent existed, or who withdrew).
    /// Re-accepting the version already held keeps the original timestamp; a different version replaces it.
    /// </summary>
    public async Task<UserProfile> GrantAiConsentAsync(Guid userId, string? version, CancellationToken cancellationToken)
    {
        var consentVersion = ValidateConsentVersion(userId, version);
        var profile = await GetAsync(userId, cancellationToken);
        if (profile.HasAiConsent && profile.AiConsentVersion == consentVersion)
        {
            return profile;
        }

        await GrantConsentAsync(profile, consentVersion, cancellationToken);
        return profile;
    }

    /// <summary>Withdraws consent; AI endpoints refuse the user again. Does not move onboarding. Withdrawing when none is held is a no-op.</summary>
    public async Task<UserProfile> RevokeAiConsentAsync(Guid userId, CancellationToken cancellationToken)
    {
        var profile = await GetAsync(userId, cancellationToken);
        if (!profile.HasAiConsent)
        {
            return profile;
        }

        profile.AiConsentVersion = null;
        profile.AiConsentAt = null;
        profile.UpdatedAt = DateTimeOffset.UtcNow;
        await unitOfWork.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("[{ClassName}] User {UserId} withdrew AI consent", nameof(OnboardingService), userId);
        await eventTracker.TrackAsync(userId, EventTypes.AiConsentWithdrawn, null, cancellationToken);
        return profile;
    }

    public async Task<bool> HasAiConsentAsync(Guid userId, CancellationToken cancellationToken) =>
        (await profiles.FindAsync(userId, cancellationToken))?.HasAiConsent == true;

    private async Task GrantConsentAsync(UserProfile profile, string version, CancellationToken cancellationToken)
    {
        SetConsent(profile, version, DateTimeOffset.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("[{ClassName}] User {UserId} accepted AI consent version {Version}", nameof(OnboardingService), profile.Id, version);
        await eventTracker.TrackAsync(profile.Id, EventTypes.AiConsentGranted, $"{{\"version\":\"{version}\"}}", cancellationToken);
    }

    private static void SetConsent(UserProfile profile, string version, DateTimeOffset now)
    {
        profile.AiConsentVersion = version;
        profile.AiConsentAt = now;
        profile.UpdatedAt = now;
    }

    private string ValidateConsentVersion(Guid userId, string? version)
    {
        var trimmed = version?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > UserProfile.MaxAiConsentVersionLength
            || trimmed.Any(c => char.IsControl(c) || c is '"' or '\\'))
        {
            _logger.LogWarning("[{ClassName}] User {UserId} sent an invalid AI consent version", nameof(OnboardingService), userId);
            throw new InvalidOnboardingRequestException(
                $"A consent 'version' of 1 to {UserProfile.MaxAiConsentVersionLength} characters (no quotes, backslashes or control characters) is required.");
        }

        return trimmed;
    }

    public async Task<UserProfile> SkipStepAsync(Guid userId, OnboardingStep step, CancellationToken cancellationToken)
    {
        if (!SkippableSteps.Contains(step))
        {
            _logger.LogWarning("[{ClassName}] User {UserId} tried to skip {Step}, which cannot be skipped", nameof(OnboardingService), userId, step);
            throw new InvalidOnboardingRequestException($"'{step}' cannot be skipped.");
        }

        var profile = await GetAsync(userId, cancellationToken);
        if (step < profile.CurrentStep)
        {
            _logger.LogInformation("[{ClassName}] User {UserId} re-skipped {Step} but is already at {CurrentStep}; nothing to do",
                nameof(OnboardingService), userId, step, profile.CurrentStep);
            return profile;
        }

        if (step > profile.CurrentStep)
        {
            _logger.LogWarning("[{ClassName}] User {UserId} tried to skip {Step} while at {CurrentStep}", nameof(OnboardingService), userId, step, profile.CurrentStep);
            throw new OnboardingStepConflictException($"Cannot skip '{step}' while the current step is '{profile.CurrentStep}'.");
        }

        return await AdvanceAndSaveAsync(profile, cancellationToken);
    }

    public async Task<UserProfile> UpdateNotificationPreferencesAsync(
        Guid userId, bool remindersEnabled, int reminderLeadMinutes, CancellationToken cancellationToken)
    {
        if (reminderLeadMinutes is < 5 or > 1440)
        {
            _logger.LogWarning("[{ClassName}] User {UserId} sent an out-of-range reminder lead time of {LeadMinutes} minutes", nameof(OnboardingService), userId, reminderLeadMinutes);
            throw new InvalidOnboardingRequestException("Reminder lead time must be between 5 and 1440 minutes.");
        }

        var profile = await GetAsync(userId, cancellationToken);
        profile.RemindersEnabled = remindersEnabled;
        profile.ReminderLeadMinutes = reminderLeadMinutes;
        profile.UpdatedAt = DateTimeOffset.UtcNow;
        await unitOfWork.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("[{ClassName}] User {UserId} set reminders enabled={RemindersEnabled}, lead {LeadMinutes} minutes",
            nameof(OnboardingService), userId, remindersEnabled, reminderLeadMinutes);
        return profile;
    }

    private async Task<UserProfile> AdvanceAndSaveAsync(UserProfile profile, CancellationToken cancellationToken)
    {
        var finished = profile.CurrentStep;
        Advance(profile, DateTimeOffset.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("[{ClassName}] User {UserId} moved from {Finished} to {CurrentStep}", nameof(OnboardingService), profile.Id, finished, profile.CurrentStep);
        await TrackAdvanceAsync(profile, finished, cancellationToken);
        return profile;
    }

    private static void Advance(UserProfile profile, DateTimeOffset now)
    {
        profile.CurrentStep++;
        profile.UpdatedAt = now;
        if (profile.CurrentStep == OnboardingStep.Completed)
        {
            profile.OnboardingCompletedAt = now;
            profile.ProTrialEndsAt = now.AddDays(UserProfile.ProTrialDays);
        }
    }

    private async Task TrackAdvanceAsync(UserProfile profile, OnboardingStep finished, CancellationToken cancellationToken)
    {
        await eventTracker.TrackAsync(profile.Id, EventTypes.OnboardingStepCompleted, $"{{\"step\":\"{finished}\"}}", cancellationToken);
        if (profile.CurrentStep == OnboardingStep.Completed)
        {
            _logger.LogInformation("[{ClassName}] User {UserId} completed onboarding; pro trial ends {TrialEndsAt}", nameof(OnboardingService), profile.Id, profile.ProTrialEndsAt);
            await eventTracker.TrackAsync(profile.Id, EventTypes.OnboardingCompleted, null, cancellationToken);
        }
    }
}
