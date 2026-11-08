using Auth.Application.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Profile.Application.Interfaces;
using Profile.Application.Services;
using Profile.Domain.Models;
using REST.Controllers;
using REST.Filters;

namespace SwimRebase.Tests.Rest;

public sealed class RequireAiConsentAttributeTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private readonly Dictionary<Guid, UserProfile> profiles = [];
    private readonly OnboardingService onboarding;

    public RequireAiConsentAttributeTests() =>
        onboarding = new OnboardingService(new Repo(profiles), new NoOpUnitOfWork(), new NoOpEventTracker());

    [Fact]
    public async Task WithoutConsent_Returns403WithTheConsentCode_AndDoesNotRunTheAction()
    {
        var (result, ran) = await Run();

        var body = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, body.StatusCode);
        Assert.Contains("ai_consent_required", System.Text.Json.JsonSerializer.Serialize(body.Value));
        Assert.False(ran);
    }

    [Fact]
    public async Task WithConsent_RunsTheAction()
    {
        await onboarding.GrantAiConsentAsync(UserId, "2026-11-01", CancellationToken.None);

        var (result, ran) = await Run();

        Assert.Null(result);
        Assert.True(ran);
    }

    [Fact]
    public async Task AfterRevoking_Returns403Again()
    {
        await onboarding.GrantAiConsentAsync(UserId, "2026-11-01", CancellationToken.None);
        await onboarding.RevokeAiConsentAsync(UserId, CancellationToken.None);

        var (result, ran) = await Run();

        Assert.Equal(403, Assert.IsType<ObjectResult>(result).StatusCode);
        Assert.False(ran);
    }

    [Theory]
    [InlineData(nameof(AiChatController.SendMessage))]
    [InlineData(nameof(AiChatController.ResolveToolRequest))]
    [InlineData(nameof(AiChatController.AnswerToolRequest))]
    [InlineData(nameof(AiChatController.CancelToolRequest))]
    public void EveryEndpointThatReachesTheProvider_IsGuarded(string action) =>
        Assert.NotEmpty(typeof(AiChatController).GetMethod(action)!.GetCustomAttributes(typeof(RequireAiConsentAttribute), false));

    private async Task<(IActionResult? Result, bool Ran)> Run()
    {
        var services = new ServiceCollection()
            .AddSingleton(onboarding)
            .AddSingleton<ICurrentUserAccessor>(new FakeUser())
            .AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(NullLoggerFactory.Instance)
            .BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        var actionContext = new ActionContext(http, new RouteData(), new ActionDescriptor());
        var executing = new ActionExecutingContext(actionContext, [], new Dictionary<string, object?>(), new object());
        var ran = false;

        await new RequireAiConsentAttribute().OnActionExecutionAsync(executing, () =>
        {
            ran = true;
            return Task.FromResult(new ActionExecutedContext(actionContext, [], new object()));
        });
        return (executing.Result, ran);
    }

    private sealed class FakeUser : ICurrentUserAccessor
    {
        public Guid UserId => RequireAiConsentAttributeTests.UserId;

        public DateTimeOffset? LastSignInAt => null;
    }

    private sealed class Repo(Dictionary<Guid, UserProfile> profiles) : IUserProfileRepository
    {
        public Task<UserProfile?> FindAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(profiles.GetValueOrDefault(userId));

        public Task EnsureExistsAsync(Guid userId, DateTimeOffset now, CancellationToken cancellationToken)
        {
            profiles.TryAdd(userId, new UserProfile { Id = userId, CurrentStep = OnboardingStep.AiConsent, ReminderLeadMinutes = 60, CreatedAt = now, UpdatedAt = now });
            return Task.CompletedTask;
        }
    }
}
