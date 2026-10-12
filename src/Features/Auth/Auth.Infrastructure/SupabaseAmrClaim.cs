using System.Text.Json;

namespace Auth.Infrastructure;

/// <summary>
/// Supabase lists each way a user authenticated in the <c>amr</c> claim, with the time it happened. Depending on the token handler the claim
/// arrives as one JSON array or as one JSON object per entry.
/// </summary>
public static class SupabaseAmrClaim
{
    public static DateTimeOffset? LatestSignIn(IEnumerable<string> claimValues)
    {
        long? latest = null;
        foreach (var value in claimValues)
        {
            foreach (var timestamp in Timestamps(value))
            {
                latest = latest is null ? timestamp : Math.Max(latest.Value, timestamp);
            }
        }

        return latest is { } seconds ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null;
    }

    private static List<long> Timestamps(string json)
    {
        var timestamps = new List<long>();
        try
        {
            using var document = JsonDocument.Parse(json);
            var entries = document.RootElement.ValueKind == JsonValueKind.Array
                ? document.RootElement.EnumerateArray().ToList()
                : [document.RootElement];

            foreach (var entry in entries)
            {
                if (entry.ValueKind == JsonValueKind.Object
                    && entry.TryGetProperty("timestamp", out var timestamp)
                    && timestamp.TryGetInt64(out var seconds))
                {
                    timestamps.Add(seconds);
                }
            }
        }
        catch (JsonException)
        {
        }

        return timestamps;
    }
}
