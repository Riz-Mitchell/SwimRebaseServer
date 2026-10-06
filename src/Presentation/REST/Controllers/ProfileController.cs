using System.Text.Json;
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
            () => onboarding.SaveAnswersAsync(currentUser.UserId, request.ToAnswers(), cancellationToken),
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

/// <summary>Each answer is the chosen option's value, or an array of values for a question that takes several.</summary>
public sealed record SaveQuestionnaireRequest(Dictionary<string, JsonElement> Answers)
{
    public IReadOnlyDictionary<string, IReadOnlyList<string>> ToAnswers() =>
        Answers.ToDictionary(a => a.Key, a => (IReadOnlyList<string>)Read(a.Key, a.Value));

    private static List<string> Read(string key, JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => [value.GetString()!],
        JsonValueKind.Array when value.EnumerateArray().All(v => v.ValueKind == JsonValueKind.String) => value.EnumerateArray().Select(v => v.GetString()!).ToList(),
        _ => throw new InvalidOnboardingRequestException($"The answer to '{key}' must be a string or an array of strings."),
    };
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
    IReadOnlyDictionary<string, object> Answers,
    bool RemindersEnabled,
    int ReminderLeadMinutes,
    ProTrialResponse? ProTrial)
{
    public static ProfileResponse FromEntity(UserProfile profile) => new(
        profile.CurrentStep == OnboardingStep.Completed,
        AnswersOf(profile),
        profile.RemindersEnabled,
        profile.ReminderLeadMinutes,
        TrialOf(profile));

    /// <summary>A question that takes several answers reads as an array; every other one as its single value.</summary>
    private static Dictionary<string, object> AnswersOf(UserProfile profile) => profile.Answers
        .GroupBy(a => a.QuestionKey)
        .ToDictionary(
            g => g.Key,
            g => QuestionnaireCatalog.Questions.FirstOrDefault(q => q.Key == g.Key)?.Multiple == true || g.Count() > 1
                ? (object)g.Select(a => a.AnswerValue).Order().ToList()
                : g.Single().AnswerValue);

    public static ProTrialResponse? TrialOf(UserProfile profile) =>
        profile.ProTrialEndsAt is { } endsAt
            ? new ProTrialResponse(endsAt, Math.Max(0, (int)Math.Ceiling((endsAt - DateTimeOffset.UtcNow).TotalDays)))
            : null;
}

public sealed record QuestionOptionResponse(string Value, string Label, bool Exclusive);

/// <param name="Answer">The chosen value of a single-answer question; null when unanswered or when <paramref name="Multiple"/>.</param>
/// <param name="Answers">Every chosen value (at most one unless <paramref name="Multiple"/>).</param>
public sealed record QuestionResponse(
    string Key, string Prompt, IReadOnlyList<QuestionOptionResponse> Options, bool Multiple, string? Answer, IReadOnlyList<string> Answers);

public sealed record QuestionnaireResponse(IReadOnlyList<QuestionResponse> Questions, bool Complete)
{
    public static QuestionnaireResponse FromEntity(UserProfile profile) => new(
        QuestionnaireCatalog.Questions
            .Select(q => new QuestionResponse(
                q.Key,
                q.Prompt,
                q.Options.Select(o => new QuestionOptionResponse(o.Value, o.Label, o.Exclusive)).ToList(),
                q.Multiple,
                q.Multiple ? null : profile.Answers.FirstOrDefault(a => a.QuestionKey == q.Key)?.AnswerValue,
                profile.Answers.Where(a => a.QuestionKey == q.Key).Select(a => a.AnswerValue).Order().ToList()))
            .ToList(),
        OnboardingService.HasAnsweredAll(profile));
}
