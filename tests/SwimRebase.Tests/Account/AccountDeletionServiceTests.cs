using Auth.Application.Exceptions;
using Auth.Application.Interfaces;
using Auth.Application.Services;
using Shared.Application;

namespace SwimRebase.Tests.Account;

public class AccountDeletionServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);

    private readonly List<string> calls = [];
    private readonly FakeDeletedUsers deletedUsers = new();

    private AccountDeletionService CreateService(params IUserDataEraser[] erasers) =>
        new(erasers, new RecordingSupabase(calls), new FakeConfirmation(), deletedUsers, new FixedClock(Now));

    [Fact]
    public void Request_requires_a_recent_sign_in()
    {
        var service = CreateService();

        Assert.Throws<RecentLoginRequiredException>(() => service.Request(UserId, null));
        Assert.Throws<RecentLoginRequiredException>(() => service.Request(UserId, Now - AccountDeletionService.RecentLoginWindow - TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void Request_issues_a_token_that_expires_after_the_confirmation_lifetime()
    {
        var request = CreateService().Request(UserId, Now - TimeSpan.FromMinutes(1));

        Assert.Equal(Now + AccountDeletionService.ConfirmationLifetime, request.ExpiresAt);
    }

    [Fact]
    public async Task Confirm_rejects_a_bad_token_without_deleting_anything()
    {
        var service = CreateService(new RecordingEraser("a", calls));

        await Assert.ThrowsAsync<InvalidDeletionConfirmationException>(() => service.ConfirmAsync(UserId, "nope", CancellationToken.None));

        Assert.Empty(calls);
        Assert.False(deletedUsers.Contains(UserId));
    }

    [Fact]
    public async Task Confirm_erases_every_feature_then_the_supabase_user()
    {
        var service = CreateService(new RecordingEraser("a", calls), new RecordingEraser("b", calls));
        var token = service.Request(UserId, Now).ConfirmationToken;

        await service.ConfirmAsync(UserId, token, CancellationToken.None);

        Assert.Equal(new[] { "a", "b", "supabase" }, calls);
        Assert.True(deletedUsers.Contains(UserId));
    }

    [Fact]
    public async Task A_failing_eraser_leaves_the_supabase_user_and_can_be_retried()
    {
        var failing = new RecordingEraser("b", calls) { Fail = true };
        var service = CreateService(new RecordingEraser("a", calls), failing);
        var token = service.Request(UserId, Now).ConfirmationToken;

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ConfirmAsync(UserId, token, CancellationToken.None));
        Assert.DoesNotContain("supabase", calls);
        Assert.False(deletedUsers.Contains(UserId));

        failing.Fail = false;
        await service.ConfirmAsync(UserId, token, CancellationToken.None);

        Assert.Contains("supabase", calls);
        Assert.True(deletedUsers.Contains(UserId));
    }

    private sealed class RecordingEraser(string name, List<string> calls) : IUserDataEraser
    {
        public bool Fail { get; set; }

        public Task EraseAsync(Guid userId, CancellationToken cancellationToken)
        {
            calls.Add(name);
            return Fail ? throw new InvalidOperationException("boom") : Task.CompletedTask;
        }
    }

    private sealed class RecordingSupabase(List<string> calls) : ISupabaseAdminClient
    {
        public Task DeleteUserAsync(Guid userId, CancellationToken cancellationToken)
        {
            calls.Add("supabase");
            return Task.CompletedTask;
        }
    }

    private sealed class FakeConfirmation : IDeletionConfirmation
    {
        public string Issue(Guid userId, DateTimeOffset expiresAt) => $"token-{userId}";

        public bool IsValid(Guid userId, string token) => token == $"token-{userId}";
    }

    private sealed class FakeDeletedUsers : IDeletedUserList
    {
        private readonly HashSet<Guid> ids = [];

        public void Add(Guid userId) => ids.Add(userId);

        public bool Contains(Guid userId) => ids.Contains(userId);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
