using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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

    public SwimSession Resolve(SwimSession session, IIntensityMapper? personal = null) =>
        session with { Sets = session.Sets.Select(node => Resolve(node, personal)).ToList() };

    /// <summary>Null when the text is blank or no mapper recognises it. A <paramref name="personal"/> mapper is tried before the built-in ones.</summary>
    public Intensity? Resolve(string? effort, IIntensityMapper? personal = null)
    {
        if (string.IsNullOrWhiteSpace(effort))
        {
            return null;
        }

        foreach (var mapper in personal is null ? _mappers : _mappers.Prepend(personal))
        {
            if (mapper.TryMap(effort, out var intensity))
            {
                return intensity;
            }
        }

        return null;
    }

    private SessionNode Resolve(SessionNode node, IIntensityMapper? personal) => node switch
    {
        SetGroup group => group with { Items = group.Items.Select(child => Resolve(child, personal)).ToList() },
        SetItem item => item with { Parts = item.Parts.Select(part => Resolve(part, personal)).ToList() },
        _ => node,
    };

    // Effort text is the truth: it is mapped again whenever present, so an edit to it can never leave a stale level behind.
    private Part Resolve(Part part, IIntensityMapper? personal)
    {
        if (string.IsNullOrWhiteSpace(part.Effort))
        {
            return part;
        }

        return part with { Intensity = Resolve(part.Effort, personal) };
    }
}

/// <summary>The one door a session goes through before it is stored anywhere (draft or saved): read, map intensity, validate.</summary>
public sealed class SessionIngest(IntensityResolver intensity, ILogger<SessionIngest>? logger = null)
{
    private readonly ILogger<SessionIngest> _logger = logger ?? NullLogger<SessionIngest>.Instance;

    /// <exception cref="InvalidSessionException">The JSON could not be read as a session.</exception>
    public SwimSession Parse(string sessionJson)
    {
        try
        {
            return SessionJson.Deserialize<SwimSession>(sessionJson);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning("[{ClassName}] Could not read session JSON: {Reason}", nameof(SessionIngest), ex.Message);
            throw new InvalidSessionException([$"could not read the session: {ex.Message}"]);
        }
    }

    /// <exception cref="InvalidSessionException">The session is not valid.</exception>
    public SwimSession Prepare(SwimSession session, IIntensityMapper? personal = null)
    {
        var prepared = intensity.Resolve(session, personal);
        var problems = SessionValidator.Validate(prepared);
        if (problems.Count > 0)
        {
            _logger.LogWarning("[{ClassName}] Session failed validation with {ProblemCount} problem(s): {Problems}", nameof(SessionIngest), problems.Count, string.Join("; ", problems));
            throw new InvalidSessionException(problems);
        }

        return prepared;
    }
}
