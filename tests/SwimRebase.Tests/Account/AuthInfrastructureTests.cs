using Auth.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace SwimRebase.Tests.Account;

public class AuthInfrastructureTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public void Confirmation_token_is_only_valid_for_the_user_it_was_issued_to()
    {
        var confirmation = new DeletionConfirmation(new EphemeralDataProtectionProvider());
        var token = confirmation.Issue(UserId, DateTimeOffset.UtcNow.AddMinutes(10));

        Assert.True(confirmation.IsValid(UserId, token));
        Assert.False(confirmation.IsValid(Guid.NewGuid(), token));
        Assert.False(confirmation.IsValid(UserId, token + "x"));
        Assert.False(confirmation.IsValid(UserId, "garbage"));
    }

    [Fact]
    public void Confirmation_token_is_invalid_once_expired()
    {
        var confirmation = new DeletionConfirmation(new EphemeralDataProtectionProvider());
        var token = confirmation.Issue(UserId, DateTimeOffset.UtcNow.AddMinutes(-1));

        Assert.False(confirmation.IsValid(UserId, token));
    }

    [Fact]
    public void Deleted_users_are_remembered()
    {
        var list = new DeletedUserList(new MemoryCache(Options.Create(new MemoryCacheOptions())));

        Assert.False(list.Contains(UserId));
        list.Add(UserId);
        Assert.True(list.Contains(UserId));
    }

    [Fact]
    public void Latest_sign_in_reads_a_whole_array_claim()
    {
        var signIn = SupabaseAmrClaim.LatestSignIn(["""[{"method":"otp","timestamp":1700000000},{"method":"otp","timestamp":1700000500}]"""]);

        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000500), signIn);
    }

    [Fact]
    public void Latest_sign_in_reads_one_claim_per_entry()
    {
        var signIn = SupabaseAmrClaim.LatestSignIn(["""{"method":"otp","timestamp":1700000000}""", """{"method":"otp","timestamp":1700000900}"""]);

        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1700000900), signIn);
    }

    [Fact]
    public void Latest_sign_in_is_unknown_without_a_usable_claim()
    {
        Assert.Null(SupabaseAmrClaim.LatestSignIn([]));
        Assert.Null(SupabaseAmrClaim.LatestSignIn(["not json", """{"method":"otp"}"""]));
    }
}
