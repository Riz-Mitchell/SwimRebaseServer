using Shared.Infrastructure.Storage;

namespace SwimRebase.Tests.Storage;

public class DownloadUrlMemoTests
{
    private static readonly TimeSpan Expiry = TimeSpan.FromMinutes(15);

    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }


    [Fact]
    public void The_same_url_is_reused_within_the_reuse_window()
    {
        var clock = new ManualClock();
        var memo = new DownloadUrlMemo(clock);
        var signed = 0;
        string Sign() => $"https://blob/x?sig={++signed}";

        var first = memo.GetOrCreate("boards/a.png", Expiry, Sign);
        clock.Now += TimeSpan.FromMinutes(9);
        var second = memo.GetOrCreate("boards/a.png", Expiry, Sign);

        Assert.Equal(first, second);
        Assert.Equal(1, signed);
    }

    [Fact]
    public void A_new_url_is_signed_once_the_reuse_window_has_passed_so_it_is_never_near_expiry()
    {
        var clock = new ManualClock();
        var memo = new DownloadUrlMemo(clock);
        var signed = 0;
        string Sign() => $"https://blob/x?sig={++signed}";

        var first = memo.GetOrCreate("boards/a.png", Expiry, Sign);
        clock.Now += TimeSpan.FromMinutes(10);
        var second = memo.GetOrCreate("boards/a.png", Expiry, Sign);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Different_objects_and_expiries_get_their_own_urls()
    {
        var memo = new DownloadUrlMemo(new ManualClock());
        var signed = 0;
        string Sign() => $"https://blob/x?sig={++signed}";

        var a = memo.GetOrCreate("boards/a.png", Expiry, Sign);
        var b = memo.GetOrCreate("boards/b.png", Expiry, Sign);
        var shorter = memo.GetOrCreate("boards/a.png", TimeSpan.FromMinutes(5), Sign);

        Assert.Equal(3, new[] { a, b, shorter }.Distinct().Count());
    }
}
