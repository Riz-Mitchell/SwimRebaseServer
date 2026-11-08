using Profile.Application.Interfaces;
using Profile.Application.Services;
using Profile.Domain.Models;

namespace SwimRebase.Tests.Profile;

public sealed class OnboardingServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private readonly InMemoryProfileRepository repository = new();
    private readonly OnboardingService service;

    public OnboardingServiceTests() =>
        service = new OnboardingService(repository, new NoOpUnitOfWork(), new NoOpEventTracker());

    [Fact]
    public async Task NewUser_StartsAtAiConsent()
    {
        var profile = await service.GetAsync(UserId, CancellationToken.None);

        Assert.Equal(OnboardingStep.AiConsent, profile.CurrentStep);
    }

    [Fact]
    public async Task CompletingAheadOfCurrentStep_IsRejected()
    {
        await Assert.ThrowsAsync<OnboardingStepConflictException>(() =>
            service.CompleteStepAsync(UserId, OnboardingStep.ProTrial, CancellationToken.None));
    }

    [Fact]
    public async Task CompletingAPastStep_IsANoOp()
    {
        await Consent();
        await service.ChooseFirstSessionPathAsync(UserId, FirstSessionPath.Sample, CancellationToken.None);
        await service.CompleteStepAsync(UserId, OnboardingStep.FirstSession, CancellationToken.None);

        var profile = await service.CompleteStepAsync(UserId, OnboardingStep.FirstSession, CancellationToken.None);

        Assert.Equal(OnboardingStep.ReviewWorkout, profile.CurrentStep);
    }

    [Fact]
    public async Task FirstSession_RequiresAPathBeforeCompleting()
    {
        await Consent();
        await Assert.ThrowsAsync<InvalidOnboardingRequestException>(() =>
            service.CompleteStepAsync(UserId, OnboardingStep.FirstSession, CancellationToken.None));
    }

    [Theory]
    [InlineData(OnboardingStep.AiConsent)]
    [InlineData(OnboardingStep.FirstSession)]
    [InlineData(OnboardingStep.ReviewWorkout)]
    public async Task RequiredSteps_CannotBeSkipped(OnboardingStep step) =>
        await Assert.ThrowsAsync<InvalidOnboardingRequestException>(() =>
            service.SkipStepAsync(UserId, step, CancellationToken.None));

    [Fact]
    public async Task FullFlow_CompletesAndStartsProTrial()
    {
        await Consent();
        await service.ChooseFirstSessionPathAsync(UserId, FirstSessionPath.Sample, CancellationToken.None);
        await service.CompleteStepAsync(UserId, OnboardingStep.FirstSession, CancellationToken.None);
        await service.CompleteStepAsync(UserId, OnboardingStep.ReviewWorkout, CancellationToken.None);
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

    private Task<UserProfile> Consent(string version = "2026-11-01") =>
        service.CompleteStepAsync(UserId, OnboardingStep.AiConsent, CancellationToken.None, version);

    [Fact]
    public async Task CompletingAiConsent_StoresVersionAndTimeAndAdvances()
    {
        var before = DateTimeOffset.UtcNow;
        var profile = await Consent();

        Assert.Equal(OnboardingStep.FirstSession, profile.CurrentStep);
        Assert.Equal("2026-11-01", profile.AiConsentVersion);
        Assert.True(profile.AiConsentAt >= before);
        Assert.True(profile.HasAiConsent);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CompletingAiConsent_WithoutAVersion_IsRejectedAndStaysPut(string? version)
    {
        await Assert.ThrowsAsync<InvalidOnboardingRequestException>(() =>
            service.CompleteStepAsync(UserId, OnboardingStep.AiConsent, CancellationToken.None, version));

        var profile = await service.GetAsync(UserId, CancellationToken.None);
        Assert.Equal(OnboardingStep.AiConsent, profile.CurrentStep);
        Assert.False(profile.HasAiConsent);
    }

    [Fact]
    public async Task CompletingAiConsent_WithAnOverlongVersion_IsRejected() =>
        await Assert.ThrowsAsync<InvalidOnboardingRequestException>(() =>
            Consent(new string('v', UserProfile.MaxAiConsentVersionLength + 1)));

    [Fact]
    public async Task ReCompletingAiConsent_KeepsTheFirstTimestamp()
    {
        var first = (await Consent()).AiConsentAt;
        await Task.Delay(10);

        var profile = await Consent("2026-12-01");

        Assert.Equal(first, profile.AiConsentAt);
        Assert.Equal("2026-11-01", profile.AiConsentVersion);
        Assert.Equal(OnboardingStep.FirstSession, profile.CurrentStep);
    }

    [Fact]
    public async Task GrantAiConsent_ForAUserPastOnboardingStep_StoresItWithoutMovingTheStep()
    {
        await Consent();
        await service.GrantAiConsentAsync(UserId, "2026-11-01", CancellationToken.None);
        await service.RevokeAiConsentAsync(UserId, CancellationToken.None);

        var profile = await service.GrantAiConsentAsync(UserId, "2026-12-01", CancellationToken.None);

        Assert.Equal("2026-12-01", profile.AiConsentVersion);
        Assert.Equal(OnboardingStep.FirstSession, profile.CurrentStep);
    }

    [Fact]
    public async Task GrantAiConsent_WithoutAVersion_IsRejected() =>
        await Assert.ThrowsAsync<InvalidOnboardingRequestException>(() =>
            service.GrantAiConsentAsync(UserId, null, CancellationToken.None));

    [Fact]
    public async Task GrantAiConsent_OfTheVersionAlreadyHeld_KeepsTheTimestamp()
    {
        var first = (await service.GrantAiConsentAsync(UserId, "2026-11-01", CancellationToken.None)).AiConsentAt;
        await Task.Delay(10);

        var profile = await service.GrantAiConsentAsync(UserId, "2026-11-01", CancellationToken.None);

        Assert.Equal(first, profile.AiConsentAt);
    }

    [Fact]
    public async Task RevokeAiConsent_ClearsConsentButNotOnboardingProgress()
    {
        await Consent();

        var profile = await service.RevokeAiConsentAsync(UserId, CancellationToken.None);

        Assert.False(profile.HasAiConsent);
        Assert.Null(profile.AiConsentVersion);
        Assert.Null(profile.AiConsentAt);
        Assert.False(await service.HasAiConsentAsync(UserId, CancellationToken.None));
        Assert.Equal(OnboardingStep.FirstSession, profile.CurrentStep);
    }

    [Fact]
    public async Task HasAiConsent_IsFalseForUnknownUsersAndTrueAfterConsenting()
    {
        Assert.False(await service.HasAiConsentAsync(UserId, CancellationToken.None));
        await Consent();
        Assert.True(await service.HasAiConsentAsync(UserId, CancellationToken.None));
    }

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
                CurrentStep = OnboardingStep.AiConsent,
                ReminderLeadMinutes = 60,
                CreatedAt = now,
                UpdatedAt = now,
            });
            return Task.CompletedTask;
        }
    }
}
