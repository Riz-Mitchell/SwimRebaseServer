using Sessions.Domain.Calculations;
using Sessions.Domain.Models;

namespace Sessions.Application.Metrics;

/// <summary>Works out which <see cref="EffortScale"/> a request means: the one it names, else the swimmer's saved choice, else the tens scale.</summary>
public static class EffortScaleChoice
{
    /// <exception cref="InvalidMetricsQueryException">The scale is unknown, or "custom" with no custom scale saved.</exception>
    public static EffortScale Resolve(string? requestedId, SessionPreferences? saved)
    {
        var id = string.IsNullOrWhiteSpace(requestedId) ? saved?.EffortScale ?? EffortScale.TensId : requestedId.Trim();
        if (string.Equals(id, EffortScale.CustomId, StringComparison.OrdinalIgnoreCase))
        {
            return Custom(saved) ?? throw new InvalidMetricsQueryException("there is no custom effort scale saved; save one with PUT /api/sessions/preferences first.");
        }

        return EffortVocabulary.Default.FindScale(id)
            ?? throw new InvalidMetricsQueryException($"unknown effort scale \"{id}\"; use {string.Join(", ", EffortVocabulary.Default.Scales.Select(s => s.Id))} or custom.");
    }

    /// <summary>The swimmer's own scale, or null when they have not saved one.</summary>
    public static EffortScale? Custom(SessionPreferences? saved) =>
        saved?.CustomEffortScaleJson is { Length: > 0 } json ? EffortScale.Custom(SessionJson.Deserialize<List<EffortBand>>(json)) : null;
}
