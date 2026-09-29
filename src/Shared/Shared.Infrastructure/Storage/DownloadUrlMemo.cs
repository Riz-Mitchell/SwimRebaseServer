using System.Collections.Concurrent;

namespace Shared.Infrastructure.Storage;

/// <summary>
/// Hands out the same presigned download URL for an object for most of its lifetime. A presigned URL carries the signing time and a
/// signature, so signing afresh on every use gives a different string each time; sent to a model as an image source, that changes the
/// request bytes every turn and the provider's prompt cache never matches past the image. Reusing one URL keeps the bytes identical
/// across turns. It is reused for two thirds of its validity, which leaves the rest for whoever fetches it. Per-process and
/// bounded: entries past their reuse window are dropped once the table fills up.
/// </summary>
public sealed class DownloadUrlMemo(TimeProvider clock)
{
    private const int MaxEntries = 2048;

    private readonly ConcurrentDictionary<(string ObjectKey, TimeSpan Expiry), Entry> _entries = new();

    public string GetOrCreate(string objectKey, TimeSpan expiry, Func<string> create)
    {
        var now = clock.GetUtcNow();
        if (_entries.TryGetValue((objectKey, expiry), out var entry) && now - entry.CreatedAt < ReuseWindow(expiry))
        {
            return entry.Url;
        }

        if (_entries.Count >= MaxEntries)
        {
            Purge(now);
        }

        var url = create();
        _entries[(objectKey, expiry)] = new Entry(url, now);
        return url;
    }

    private static TimeSpan ReuseWindow(TimeSpan expiry) => expiry * 2 / 3;

    private void Purge(DateTimeOffset now)
    {
        foreach (var (key, entry) in _entries)
        {
            if (now - entry.CreatedAt >= ReuseWindow(key.Expiry))
            {
                _entries.TryRemove(key, out _);
            }
        }

        if (_entries.Count >= MaxEntries)
        {
            _entries.Clear();
        }
    }

    private sealed record Entry(string Url, DateTimeOffset CreatedAt);
}
