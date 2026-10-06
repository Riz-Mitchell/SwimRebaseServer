using Profile.Application.Interfaces;
using Profile.Application.Services;
using Profile.Domain.Models;

namespace SwimRebase.Tests.Profile;

public sealed class OnboardingServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> ValidAnswers = Answers(("level", ["Regional"]), ("goal", ["DataInsights"]));

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> Answers(params (string Key, string[] Values)[] pairs) =>
        pairs.ToDictionary(p => p.Key, p => (IReadOnlyList<string>)p.Values);

    private static string[] Saved(UserProfile profile, string key) =>
        profile.Answers.Where(a => a.QuestionKey == key).Select(a => a.AnswerValue).Order().ToArray();

    private readonly InMemoryProfileRepository repository = new();
    private readonly OnboardingService service;

    public OnboardingServiceTests() =>
        service = new OnboardingService(repository, new NoOpUnitOfWork(), new NoOpEventTracker());

    [Fact]
    public async Task NewUser_StartsAtQuestionnaire()
    {
        var profile = await service.GetAsync(UserId, CancellationToken.None);

        Assert.Equal(OnboardingStep.Questionnaire, profile.CurrentStep);
    }

    [Fact]
    public async Task PartialAnswers_KeepUserOnQuestionnaire()
    {
        var profile = await service.SaveAnswersAsync(UserId, Answers(("level", ["Regional"])), CancellationToken.None);

        Assert.Equal(OnboardingStep.Questionnaire, profile.CurrentStep);
    }

    [Fact]
    public async Task AllAnswers_AdvanceToFirstSession()
    {
        var profile = await service.SaveAnswersAsync(UserId, ValidAnswers, CancellationToken.None);

        Assert.Equal(OnboardingStep.FirstSession, profile.CurrentStep);
    }

    [Fact]
    public async Task ReAnswering_DoesNotMoveUserBackwards()
    {
        await service.SaveAnswersAsync(UserId, ValidAnswers, CancellationToken.None);
        var profile = await service.SaveAnswersAsync(UserId, Answers(("goal", ["Other"])), CancellationToken.None);

        Assert.Equal(OnboardingStep.FirstSession, profile.CurrentStep);
        Assert.Equal(["Other"], Saved(profile, "goal"));
    }

    [Theory]
    [InlineData("nope", "Regional")]
    [InlineData("level", "Elite")]
    public async Task UnknownQuestionOrAnswer_IsRejected(string key, string value) =>
        await Assert.ThrowsAsync<InvalidOnboardingRequestException>(() =>
            service.SaveAnswersAsync(UserId, Answers((key, [value])), CancellationToken.None));

    [Fact]
    public async Task GoalAcceptsSeveralAnswers_AndAdvancesOnceEveryQuestionHasOne()
    {
        var profile = await service.SaveAnswersAsync(
            UserId, Answers(("level", ["Regional"]), ("goal", ["DataInsights", "GeneralFitness", "CompetitiveImprovement"])), CancellationToken.None);

        Assert.Equal(["CompetitiveImprovement", "DataInsights", "GeneralFitness"], Saved(profile, "goal"));
        Assert.Equal(OnboardingStep.FirstSession, profile.CurrentStep);
    }

    [Fact]
    public async Task ReSavingGoal_ReplacesTheWholeSelection()
    {
        await service.SaveAnswersAsync(UserId, Answers(("level", ["Regional"]), ("goal", ["DataInsights", "GeneralFitness"])), CancellationToken.None);

        var profile = await service.SaveAnswersAsync(UserId, Answers(("goal", ["GeneralFitness", "SocialCommunityAndGamification"])), CancellationToken.None);

        Assert.Equal(["GeneralFitness", "SocialCommunityAndGamification"], Saved(profile, "goal"));
        Assert.Equal(["Regional"], Saved(profile, "level"));
    }

    [Fact]
    public async Task DuplicateValuesInASelection_AreCountedOnce()
    {
        var profile = await service.SaveAnswersAsync(UserId, Answers(("goal", ["DataInsights", "DataInsights"])), CancellationToken.None);

        Assert.Equal(["DataInsights"], Saved(profile, "goal"));
    }

    [Fact]
    public async Task Other_CannotBeCombinedWithAnotherGoal()
    {
        await Assert.ThrowsAsync<InvalidOnboardingRequestException>(() =>
            service.SaveAnswersAsync(UserId, Answers(("goal", ["Other", "DataInsights"])), CancellationToken.None));
    }

    [Fact]
    public async Task ChoosingOther_ReplacesTheEarlierGoals()
    {
        await service.SaveAnswersAsync(UserId, Answers(("goal", ["DataInsights", "GeneralFitness"])), CancellationToken.None);

        var profile = await service.SaveAnswersAsync(UserId, Answers(("goal", ["Other"])), CancellationToken.None);

        Assert.Equal(["Other"], Saved(profile, "goal"));
    }

    [Fact]
    public async Task LevelStillTakesExactlyOneAnswer()
    {
        await Assert.ThrowsAsync<InvalidOnboardingRequestException>(() =>
            service.SaveAnswersAsync(UserId, Answers(("level", ["Regional", "StateQualified"])), CancellationToken.None));
    }

    [Fact]
    public async Task AnEmptySelection_IsRejected()
    {
        await Assert.ThrowsAsync<InvalidOnboardingRequestException>(() =>
            service.SaveAnswersAsync(UserId, Answers(("goal", [])), CancellationToken.None));
    }

    [Fact]
    public async Task ChangingLevel_ReplacesTheOldAnswer()
    {
        await service.SaveAnswersAsync(UserId, ValidAnswers, CancellationToken.None);

        var profile = await service.SaveAnswersAsync(UserId, Answers(("level", ["StateQualified"])), CancellationToken.None);

        Assert.Equal(["StateQualified"], Saved(profile, "level"));
    }

    [Fact]
    public async Task CompletingAheadOfCurrentStep_IsRejected()
    {
        await Assert.ThrowsAsync<OnboardingStepConflictException>(() =>
            service.CompleteStepAsync(UserId, OnboardingStep.Schedule, CancellationToken.None));
    }

    [Fact]
    public async Task CompletingAPastStep_IsANoOp()
    {
        await service.SaveAnswersAsync(UserId, ValidAnswers, CancellationToken.None);

        var profile = await service.CompleteStepAsync(UserId, OnboardingStep.Questionnaire, CancellationToken.None);

        Assert.Equal(OnboardingStep.FirstSession, profile.CurrentStep);
    }

    [Fact]
    public async Task FirstSession_RequiresAPathBeforeCompleting()
    {
        await service.SaveAnswersAsync(UserId, ValidAnswers, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOnboardingRequestException>(() =>
            service.CompleteStepAsync(UserId, OnboardingStep.FirstSession, CancellationToken.None));
    }

    [Fact]
    public async Task ChoosingAPath_BeforeQuestionnaireIsDone_IsRejected()
    {
        await Assert.ThrowsAsync<OnboardingStepConflictException>(() =>
            service.ChooseFirstSessionPathAsync(UserId, FirstSessionPath.Sample, CancellationToken.None));
    }

    [Theory]
    [InlineData(OnboardingStep.Questionnaire)]
    [InlineData(OnboardingStep.FirstSession)]
    [InlineData(OnboardingStep.ReviewWorkout)]
    public async Task RequiredSteps_CannotBeSkipped(OnboardingStep step) =>
        await Assert.ThrowsAsync<InvalidOnboardingRequestException>(() =>
            service.SkipStepAsync(UserId, step, CancellationToken.None));

    [Fact]
    public async Task FullFlow_CompletesAndStartsProTrial()
    {
        await service.SaveAnswersAsync(UserId, ValidAnswers, CancellationToken.None);
        await service.ChooseFirstSessionPathAsync(UserId, FirstSessionPath.Sample, CancellationToken.None);
        await service.CompleteStepAsync(UserId, OnboardingStep.FirstSession, CancellationToken.None);
        await service.CompleteStepAsync(UserId, OnboardingStep.ReviewWorkout, CancellationToken.None);
        await service.SkipStepAsync(UserId, OnboardingStep.Schedule, CancellationToken.None);
        var before = DateTimeOffset.UtcNow;
        var profile = await service.CompleteStepAsync(UserId, OnboardingStep.ProTrial, CancellationToken.None);

        Assert.Equal(OnboardingStep.Completed, profile.CurrentStep);
        Assert.NotNull(profile.OnboardingCompletedAt);
        Assert.True(profile.ProTrialEndsAt >= before.AddDays(UserProfile.ProTrialDays));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(1441)]
    public async Task ReminderLeadTime_OutOfRange_IsRejected(int minutes) =>
        await Assert.ThrowsAsync<InvalidOnboardingRequestException>(() =>
            service.UpdateNotificationPreferencesAsync(UserId, true, minutes, CancellationToken.None));

    private sealed class InMemoryProfileRepository : IUserProfileRepository
    {
        private readonly Dictionary<Guid, UserProfile> profiles = [];

        public Task<UserProfile?> FindAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(profiles.GetValueOrDefault(userId));

        public Task EnsureExistsAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken)
        {
            profiles.TryAdd(userId, new UserProfile
            {
                Id = userId,
                CurrentStep = OnboardingStep.Questionnaire,
                ReminderLeadMinutes = 60,
                CreatedAt = now,
                UpdatedAt = now,
            });
            return Task.CompletedTask;
        }

        public void AddAnswer(ProfileAnswer answer)
        {
        }

        public void RemoveAnswer(ProfileAnswer answer)
        {
        }
    }
}
