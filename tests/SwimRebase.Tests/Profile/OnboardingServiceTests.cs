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
    public async Task NewUser_StartsAtFirstSession()
    {
        var profile = await service.GetAsync(UserId, CancellationToken.None);

        Assert.Equal(OnboardingStep.FirstSession, profile.CurrentStep);
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
        await service.ChooseFirstSessionPathAsync(UserId, FirstSessionPath.Sample, CancellationToken.None);
        await service.CompleteStepAsync(UserId, OnboardingStep.FirstSession, CancellationToken.None);

        var profile = await service.CompleteStepAsync(UserId, OnboardingStep.FirstSession, CancellationToken.None);

        Assert.Equal(OnboardingStep.ReviewWorkout, profile.CurrentStep);
    }

    [Fact]
    public async Task FirstSession_RequiresAPathBeforeCompleting()
    {
        await Assert.ThrowsAsync<InvalidOnboardingRequestException>(() =>
            service.CompleteStepAsync(UserId, OnboardingStep.FirstSession, CancellationToken.None));
    }

    [Theory]
    [InlineData(OnboardingStep.FirstSession)]
    [InlineData(OnboardingStep.ReviewWorkout)]
    public async Task RequiredSteps_CannotBeSkipped(OnboardingStep step) =>
        await Assert.ThrowsAsync<InvalidOnboardingRequestException>(() =>
            service.SkipStepAsync(UserId, step, CancellationToken.None));

    [Fact]
    public async Task FullFlow_CompletesAndStartsProTrial()
    {
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
                CurrentStep = OnboardingStep.FirstSession,
                ReminderLeadMinutes = 60,
                CreatedAt = now,
                UpdatedAt = now,
            });
            return Task.CompletedTask;
        }
    }
}
