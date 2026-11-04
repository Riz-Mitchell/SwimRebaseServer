using Auth.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Profile.Application.Services;
using Profile.Domain.Models;

namespace REST.Controllers;

[ApiController]
[Authorize]
[Route("api/profile")]
public sealed class ProfileController(OnboardingService onboarding, ICurrentUserAccessor currentUser, ILogger<ProfileController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetProfile(CancellationToken cancellationToken)
    {
        var profile = await onboarding.GetAsync(currentUser.UserId, cancellationToken);
        return Ok(ProfileResponse.FromEntity(profile));
    }

    [HttpGet("onboarding")]
    public async Task<IActionResult> GetOnboarding(CancellationToken cancellationToken)
    {
        var profile = await onboarding.GetAsync(currentUser.UserId, cancellationToken);
        return Ok(OnboardingStatusResponse.FromEntity(profile));
    }

    [HttpPut("onboarding/first-session")]
    public Task<IActionResult> ChooseFirstSessionPath([FromBody] ChooseFirstSessionRequest request, CancellationToken cancellationToken) =>
        RunAsync(
            () => onboarding.ChooseFirstSessionPathAsync(currentUser.UserId, request.Path, cancellationToken),
            OnboardingStatusResponse.FromEntity);

    [HttpPost("onboarding/steps/{step}/complete")]
    public Task<IActionResult> CompleteStep(OnboardingStep step, CancellationToken cancellationToken) =>
        RunAsync(
            () => onboarding.CompleteStepAsync(currentUser.UserId, step, cancellationToken),
            OnboardingStatusResponse.FromEntity);

    [HttpPost("onboarding/steps/{step}/skip")]
    public Task<IActionResult> SkipStep(OnboardingStep step, CancellationToken cancellationToken) =>
        RunAsync(
            () => onboarding.SkipStepAsync(currentUser.UserId, step, cancellationToken),
            OnboardingStatusResponse.FromEntity);

    [HttpPut("notification-preferences")]
    public Task<IActionResult> UpdateNotificationPreferences(
        [FromBody] NotificationPreferencesRequest request, CancellationToken cancellationToken) =>
        RunAsync(
            () => onboarding.UpdateNotificationPreferencesAsync(
                currentUser.UserId, request.RemindersEnabled, request.ReminderLeadMinutes, cancellationToken),
            ProfileResponse.FromEntity);

    private async Task<IActionResult> RunAsync<T>(Func<Task<UserProfile>> action, Func<UserProfile, T> map)
    {
        try
        {
            return Ok(map(await action()));
        }
        catch (InvalidOnboardingRequestException ex)
        {
            logger.LogWarning("[{ClassName}] Bad onboarding request from user {UserId}: {Reason}", nameof(ProfileController), currentUser.UserId, ex.Message);
            return BadRequest(new { error = ex.Message });
        }
        catch (OnboardingStepConflictException ex)
        {
            logger.LogWarning("[{ClassName}] Onboarding step conflict for user {UserId}: {Reason}", nameof(ProfileController), currentUser.UserId, ex.Message);
            return Conflict(new { error = ex.Message });
        }
    }
}

public sealed record ChooseFirstSessionRequest(FirstSessionPath Path);

public sealed record NotificationPreferencesRequest(bool RemindersEnabled, int ReminderLeadMinutes);

public sealed record ProTrialResponse(DateTimeOffset EndsAt, int DaysRemaining);

public sealed record OnboardingStatusResponse(
    string Status,
    OnboardingStep CurrentStep,
    OnboardingStep ResumeStep,
    IReadOnlyList<OnboardingStep> CompletedSteps,
    FirstSessionPath? FirstSessionPath,
    ProTrialResponse? ProTrial)
{
    public static OnboardingStatusResponse FromEntity(UserProfile profile) => new(
        profile.CurrentStep == OnboardingStep.Completed ? "Completed" : "InProgress",
        profile.CurrentStep,
        profile.CurrentStep,
        Enum.GetValues<OnboardingStep>().Where(s => s < profile.CurrentStep).ToList(),
        profile.FirstSessionPath,
        ProfileResponse.TrialOf(profile));
}

public sealed record ProfileResponse(
    bool OnboardingCompleted,
    bool RemindersEnabled,
    int ReminderLeadMinutes,
    ProTrialResponse? ProTrial)
{
    public static ProfileResponse FromEntity(UserProfile profile) => new(
        profile.CurrentStep == OnboardingStep.Completed,
        profile.RemindersEnabled,
        profile.ReminderLeadMinutes,
        TrialOf(profile));

    public static ProTrialResponse? TrialOf(UserProfile profile) =>
        profile.ProTrialEndsAt is { } endsAt
            ? new ProTrialResponse(endsAt, Math.Max(0, (int)Math.Ceiling((endsAt - DateTimeOffset.UtcNow).TotalDays)))
            : null;
}
