using Ai.Infrastructure;
using Analytics.Infrastructure;
using Auth.Infrastructure;
using Billing.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Profile.Infrastructure;
using SessionConversion.Infrastructure;
using Sessions.Infrastructure;

namespace SwimRebase.Tests.Account;

/// <summary>
/// Fails when a table that holds a user's data is added or removed, as a prompt to check the matching
/// <c>IUserDataEraser</c> still removes it. Each feature's erasers are in its Infrastructure project.
/// </summary>
public class UserDataEraserCoverageTests
{
    private static readonly string[] KnownUserOwnedEntities =
    [
        "Conversation", "CustomMetric", "EventLog", "ProfileAnswer", "Session", "SessionDraft", "SessionFact", "SessionPreferences",
        "Subscription", "ConversionUsage", "TimedSwim", "TokenUsageRecord", "ToolExecutionRequest", "UploadedImage", "UserEffortTerm", "UserSessionStats",
    ];

    [Fact]
    public void Every_user_owned_entity_is_known()
    {
        var found = Owned<AiDbContext>(o => new AiDbContext(o))
            .Concat(Owned<AnalyticsDbContext>(o => new AnalyticsDbContext(o)))
            .Concat(Owned<AuthDbContext>(o => new AuthDbContext(o)))
            .Concat(Owned<BillingDbContext>(o => new BillingDbContext(o)))
            .Concat(Owned<ProfileDbContext>(o => new ProfileDbContext(o)))
            .Concat(Owned<SessionConversionDbContext>(o => new SessionConversionDbContext(o)))
            .Concat(Owned<SessionsDbContext>(o => new SessionsDbContext(o)))
            .Order()
            .ToList();

        Assert.Equal(KnownUserOwnedEntities.Order().ToList(), found);
    }

    private static IEnumerable<string> Owned<TContext>(Func<DbContextOptions<TContext>, TContext> create)
        where TContext : DbContext
    {
        var options = new DbContextOptionsBuilder<TContext>().UseNpgsql("Host=localhost").Options;
        using var context = create(options);
        return context.Model.GetEntityTypes()
            .Where(t => t.FindProperty("UserId") is not null || t.FindProperty("OwnerId") is not null)
            .Select(t => t.ClrType.Name)
            .ToList();
    }
}
