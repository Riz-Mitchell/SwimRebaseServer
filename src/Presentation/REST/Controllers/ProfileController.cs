using Auth.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Profile.Application.Services;
using Profile.Domain.Models;

namespace REST.Controllers;

[ApiController]
[Authorize]
[Route("api/profile")]
public sealed class ProfileController(OnboardingService onboarding, ICurrentUserAccessor currentUser) : ControllerBase
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

    [HttpGet("onboarding/questionnaire")]
    public async Task<IActionResult> GetQuestionnaire(CancellationToken cancellationToken)
    {
        var profile = await onboarding.GetAsync(currentUser.UserId, cancellationToken);
        return Ok(QuestionnaireResponse.FromEntity(profile));
    }

    [HttpPut("onboarding/questionnaire")]
    public Task<IActionResult> SaveQuestionnaire([FromBody] SaveQuestionnaireRequest request, CancellationToken cancellationToken) =>
        RunAsync(
            () => onboarding.SaveAnswersAsync(currentUser.UserId, request.Answers, cancellationToken),
            OnboardingStatusResponse.FromEntity);

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
            return BadRequest(new { error = ex.Message });
        }
        catch (OnboardingStepConflictException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }
}

public sealed record SaveQuestionnaireRequest(Dictionary<string, string> Answers);

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
    IReadOnlyDictionary<string, string> Answers,
    bool RemindersEnabled,
    int ReminderLeadMinutes,
    ProTrialResponse? ProTrial)
{
    public static ProfileResponse FromEntity(UserProfile profile) => new(
        profile.CurrentStep == OnboardingStep.Completed,
        profile.Answers.ToDictionary(a => a.QuestionKey, a => a.AnswerValue),
        profile.RemindersEnabled,
        profile.ReminderLeadMinutes,
        TrialOf(profile));

    public static ProTrialResponse? TrialOf(UserProfile profile) =>
        profile.ProTrialEndsAt is { } endsAt
            ? new ProTrialResponse(endsAt, Math.Max(0, (int)Math.Ceiling((endsAt - DateTimeOffset.UtcNow).TotalDays)))
            : null;
}

public sealed record QuestionOptionResponse(string Value, string Label);

public sealed record QuestionResponse(string Key, string Prompt, IReadOnlyList<QuestionOptionResponse> Options, string? Answer);

public sealed record QuestionnaireResponse(IReadOnlyList<QuestionResponse> Questions, bool Complete)
{
    public static QuestionnaireResponse FromEntity(UserProfile profile) => new(
        QuestionnaireCatalog.Questions
            .Select(q => new QuestionResponse(
                q.Key,
                q.Prompt,
                q.Options.Select(o => new QuestionOptionResponse(o.Value, o.Label)).ToList(),
                profile.Answers.FirstOrDefault(a => a.QuestionKey == q.Key)?.AnswerValue))
            .ToList(),
        OnboardingService.HasAnsweredAll(profile));
}
