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

    public SwimSession Resolve(SwimSession session, IIntensityMapper? personal = null, ProgressionDefaults? defaults = null) =>
        session with { Sets = session.Sets.Select(node => Resolve(node, personal, defaults ?? ProgressionDefaults.BuiltIn)).ToList() };

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

    private SessionNode Resolve(SessionNode node, IIntensityMapper? personal, ProgressionDefaults defaults) => node switch
    {
        SetGroup group => group with { Items = group.Items.Select(child => Resolve(child, personal, defaults)).ToList() },
        SetItem item => Resolve(item, personal, defaults),
        _ => node,
    };

    private SetItem Resolve(SetItem item, IIntensityMapper? personal, ProgressionDefaults defaults)
    {
        var parts = item.Parts.Select(part => Resolve(part, personal)).ToList();
        return item with { Parts = parts, Progression = item.Progression is { } progression ? Resolve(progression, parts, personal, defaults) : null };
    }

    // A stated endpoint is mapped again every time, like a part's effort. An unstated one keeps what it was saved with and is only filled from the defaults the first time.
    private Progression Resolve(Progression progression, List<Part> parts, IIntensityMapper? personal, ProgressionDefaults defaults)
    {
        var itemEffort = parts.Select(p => p.Intensity).FirstOrDefault(i => i is not null) ?? Assumed(defaults.DefaultEffort);
        var from = Endpoint(progression.From, progression.FromIntensity, itemEffort, personal);
        var to = Endpoint(progression.To, progression.ToIntensity, Assumed(Math.Max(defaults.DescendTarget, from.Percent)), personal);
        return progression with { From = Blank(progression.From), To = Blank(progression.To), FromIntensity = from, ToIntensity = to };
    }

    private Intensity Endpoint(string? text, Intensity? saved, Intensity fallback, IIntensityMapper? personal) =>
        string.IsNullOrWhiteSpace(text) ? saved ?? fallback : Resolve(text, personal) ?? fallback;

    private static Intensity Assumed(int percent) => new() { Percent = percent, Scale = ProgressionDefaults.AssumedScale };

    private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

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
    public SwimSession Prepare(SwimSession session, IIntensityMapper? personal = null, ProgressionDefaults? defaults = null)
    {
        var prepared = intensity.Resolve(session, personal, defaults);
        var problems = SessionValidator.Validate(prepared);
        if (problems.Count > 0)
        {
            _logger.LogWarning("[{ClassName}] Session failed validation with {ProblemCount} problem(s): {Problems}", nameof(SessionIngest), problems.Count, string.Join("; ", problems));
            throw new InvalidSessionException(problems);
        }

        return prepared;
    }
}
