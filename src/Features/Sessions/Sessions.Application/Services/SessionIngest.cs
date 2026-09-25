using System.Text.Json;
using Sessions.Domain.Calculations;
using Sessions.Domain.Models;

namespace Sessions.Application.Services;

/// <summary>The session failed validation. The message lists what to fix and is written for the model that produced it.</summary>
public sealed class InvalidSessionException(IReadOnlyList<string> problems)
    : Exception($"The session is not valid: {string.Join("; ", problems)}. Call the tool again with these fixed.")
{
    public IReadOnlyList<string> Problems { get; } = problems;
}

/// <summary>Maps each part's effort text onto the global intensity scale using the registered mappers.</summary>
public sealed class IntensityResolver(IEnumerable<IIntensityMapper> mappers)
{
    private readonly IIntensityMapper[] _mappers = [.. mappers];

    public SwimSession Resolve(SwimSession session) => session with { Sets = session.Sets.Select(Resolve).ToList() };

    private SessionNode Resolve(SessionNode node) => node switch
    {
        SetGroup group => group with { Items = group.Items.Select(Resolve).ToList() },
        SetItem item => item with { Parts = item.Parts.Select(Resolve).ToList() },
        _ => node,
    };

    // Effort text is the truth: it is mapped again whenever present, so an edit to it can never leave a stale level behind.
    private Part Resolve(Part part)
    {
        if (string.IsNullOrWhiteSpace(part.Effort))
        {
            return part;
        }

        foreach (var mapper in _mappers)
        {
            if (mapper.TryMap(part.Effort, out var intensity))
            {
                return part with { Intensity = intensity };
            }
        }

        return part with { Intensity = null };
    }
}

/// <summary>The one door a session goes through before it is stored anywhere (draft or saved): read, map intensity, validate.</summary>
public sealed class SessionIngest(IntensityResolver intensity)
{
    /// <exception cref="InvalidSessionException">The JSON could not be read as a session.</exception>
    public SwimSession Parse(string sessionJson)
    {
        try
        {
            return SessionJson.Deserialize<SwimSession>(sessionJson);
        }
        catch (JsonException ex)
        {
            throw new InvalidSessionException([$"could not read the session: {ex.Message}"]);
        }
    }

    /// <exception cref="InvalidSessionException">The session is not valid.</exception>
    public SwimSession Prepare(SwimSession session)
    {
        var prepared = intensity.Resolve(session);
        var problems = SessionValidator.Validate(prepared);
        if (problems.Count > 0)
        {
            throw new InvalidSessionException(problems);
        }

        return prepared;
    }
}
