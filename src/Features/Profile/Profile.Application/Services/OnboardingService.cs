using Microsoft.Extensions.DependencyInjection;
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
    IEventTracker eventTracker)
{
    private static readonly OnboardingStep[] SkippableSteps = [OnboardingStep.ProTrial];

    public async Task<UserProfile> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        await profiles.EnsureExistsAsync(userId, DateTimeOffset.UtcNow, cancellationToken);
        return (await profiles.FindAsync(userId, cancellationToken))!;
    }

    /// <summary>Each question's answers replace what was saved for it; a question not in <paramref name="answers"/> keeps its answers.</summary>
    public async Task<UserProfile> SaveAnswersAsync(
        Guid userId, IReadOnlyDictionary<string, IReadOnlyList<string>> answers, CancellationToken cancellationToken)
    {
        var chosen = new Dictionary<string, List<string>>();
        foreach (var (key, values) in answers)
        {
            chosen[key] = Validate(key, values);
        }

        var profile = await GetAsync(userId, cancellationToken);
        var now = DateTimeOffset.UtcNow;

        foreach (var (key, values) in chosen)
        {
            foreach (var stale in profile.Answers.Where(a => a.QuestionKey == key && !values.Contains(a.AnswerValue)).ToList())
            {
                profile.Answers.Remove(stale);
                profiles.RemoveAnswer(stale);
            }

            foreach (var value in values.Where(v => profile.Answers.All(a => a.QuestionKey != key || a.AnswerValue != v)))
            {
                var answer = new ProfileAnswer { UserId = userId, QuestionKey = key, AnswerValue = value, AnsweredAt = now };
                profile.Answers.Add(answer);
                profiles.AddAnswer(answer);
            }
        }

        profile.UpdatedAt = now;
        var advanced = profile.CurrentStep == OnboardingStep.Questionnaire && HasAnsweredAll(profile);
        if (advanced)
        {
            Advance(profile, now);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        if (advanced)
        {
            await TrackAdvanceAsync(profile, OnboardingStep.Questionnaire, cancellationToken);
        }

        return profile;
    }

    private static List<string> Validate(string key, IReadOnlyList<string> values)
    {
        var question = QuestionnaireCatalog.Questions.FirstOrDefault(q => q.Key == key)
            ?? throw new InvalidOnboardingRequestException($"Unknown question '{key}'.");
        var distinct = values.Distinct().ToList();
        if (distinct.Count == 0)
        {
            throw new InvalidOnboardingRequestException($"Choose an answer to '{key}'.");
        }

        if (!question.Multiple && distinct.Count > 1)
        {
            throw new InvalidOnboardingRequestException($"'{key}' takes one answer.");
        }

        foreach (var value in distinct)
        {
            var option = question.Options.FirstOrDefault(o => o.Value == value)
                ?? throw new InvalidOnboardingRequestException($"'{value}' is not a valid answer to '{key}'.");
            if (option.Exclusive && distinct.Count > 1)
            {
                throw new InvalidOnboardingRequestException($"'{value}' cannot be combined with other answers to '{key}'.");
            }
        }

        return distinct;
    }

    public async Task<UserProfile> ChooseFirstSessionPathAsync(
        Guid userId, FirstSessionPath path, CancellationToken cancellationToken)
    {
        var profile = await GetAsync(userId, cancellationToken);
        if (profile.CurrentStep < OnboardingStep.FirstSession)
        {
            throw new OnboardingStepConflictException("Finish the questionnaire first.");
        }

        profile.FirstSessionPath = path;
        profile.UpdatedAt = DateTimeOffset.UtcNow;
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return profile;
    }

    /// <summary>Completing a step the user is already past is a no-op, so a client retry is safe.</summary>
    public async Task<UserProfile> CompleteStepAsync(Guid userId, OnboardingStep step, CancellationToken cancellationToken)
    {
        var profile = await GetAsync(userId, cancellationToken);
        if (step == OnboardingStep.Completed)
        {
            throw new InvalidOnboardingRequestException("'Completed' is not a step that can be completed.");
        }

        if (step < profile.CurrentStep)
        {
            return profile;
        }

        if (step > profile.CurrentStep)
        {
            throw new OnboardingStepConflictException($"Cannot complete '{step}' while the current step is '{profile.CurrentStep}'.");
        }

        if (step == OnboardingStep.Questionnaire && !HasAnsweredAll(profile))
        {
            throw new InvalidOnboardingRequestException("Answer every questionnaire question first.");
        }

        if (step == OnboardingStep.FirstSession && profile.FirstSessionPath is null)
        {
            throw new InvalidOnboardingRequestException("Choose how to analyse the first session first.");
        }

        return await AdvanceAndSaveAsync(profile, cancellationToken);
    }

    public async Task<UserProfile> SkipStepAsync(Guid userId, OnboardingStep step, CancellationToken cancellationToken)
    {
        if (!SkippableSteps.Contains(step))
        {
            throw new InvalidOnboardingRequestException($"'{step}' cannot be skipped.");
        }

        var profile = await GetAsync(userId, cancellationToken);
        if (step < profile.CurrentStep)
        {
            return profile;
        }

        if (step > profile.CurrentStep)
        {
            throw new OnboardingStepConflictException($"Cannot skip '{step}' while the current step is '{profile.CurrentStep}'.");
        }

        return await AdvanceAndSaveAsync(profile, cancellationToken);
    }

    public async Task<UserProfile> UpdateNotificationPreferencesAsync(
        Guid userId, bool remindersEnabled, int reminderLeadMinutes, CancellationToken cancellationToken)
    {
        if (reminderLeadMinutes is < 5 or > 1440)
        {
            throw new InvalidOnboardingRequestException("Reminder lead time must be between 5 and 1440 minutes.");
        }

        var profile = await GetAsync(userId, cancellationToken);
        profile.RemindersEnabled = remindersEnabled;
        profile.ReminderLeadMinutes = reminderLeadMinutes;
        profile.UpdatedAt = DateTimeOffset.UtcNow;
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return profile;
    }

    public static bool HasAnsweredAll(UserProfile profile) =>
        QuestionnaireCatalog.Questions.All(q => profile.Answers.Any(a => a.QuestionKey == q.Key));

    private async Task<UserProfile> AdvanceAndSaveAsync(UserProfile profile, CancellationToken cancellationToken)
    {
        var finished = profile.CurrentStep;
        Advance(profile, DateTimeOffset.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
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
            await eventTracker.TrackAsync(profile.Id, EventTypes.OnboardingCompleted, null, cancellationToken);
        }
    }
}
