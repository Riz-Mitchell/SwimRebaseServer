using Microsoft.Extensions.DependencyInjection;
using Sessions.Application.Interfaces;
using Sessions.Domain.Calculations;
using Sessions.Domain.Models;
using Shared.Application;

namespace Sessions.Application.Services;

/// <summary>The effort wording a swimmer has defined for themselves, layered over the built-in vocabulary when a session is read.</summary>
public interface IPersonalIntensityProvider
{
    Task<IIntensityMapper> MapperAsync(Guid userId, CancellationToken cancellationToken);
}

/// <summary>Finds effort wording nobody has defined yet, and records what the swimmer says it means.</summary>
public interface IEffortTermLearning
{
    /// <summary>The distinct wordings that neither the built-in vocabulary nor the swimmer's own terms understand, in the order given.</summary>
    Task<IReadOnlyList<string>> UnknownAsync(Guid userId, IEnumerable<string> efforts, CancellationToken cancellationToken);

    /// <summary>
    /// Saves each phrase with the percent its meaning reads as ("A2", "sub max", "76%"). A meaning that is not an effort is ignored;
    /// a phrase already saved takes the new percent.
    /// </summary>
    Task SaveAsync(Guid userId, IEnumerable<(string Phrase, string Meaning)> meanings, CancellationToken cancellationToken);
}

public sealed class UserEffortTermService(
    IUserEffortTermRepository terms,
    IntensityResolver intensity,
    [FromKeyedServices(SessionsKeys.UnitOfWork)] IUnitOfWork unitOfWork) : IPersonalIntensityProvider, IEffortTermLearning
{
    /// <summary>Longer wording is a sentence or note rather than an effort name.</summary>
    public const int MaxPhraseLength = 40;

    public async Task<IIntensityMapper> MapperAsync(Guid userId, CancellationToken cancellationToken) =>
        new PersonalIntensityMapper(await terms.ListAsync(userId, cancellationToken));

    public async Task<IReadOnlyList<string>> UnknownAsync(Guid userId, IEnumerable<string> efforts, CancellationToken cancellationToken)
    {
        var personal = await MapperAsync(userId, cancellationToken);
        var seen = new HashSet<string>();
        var unknown = new List<string>();
        foreach (var effort in efforts.Select(e => e.Trim()).Where(e => e.Length is > 0 and <= MaxPhraseLength))
        {
            if (seen.Add(PersonalIntensityMapper.Normalize(effort)) && intensity.Resolve(effort, personal) is null)
            {
                unknown.Add(effort);
            }
        }

        return unknown;
    }

    public async Task SaveAsync(Guid userId, IEnumerable<(string Phrase, string Meaning)> meanings, CancellationToken cancellationToken)
    {
        var changed = false;
        foreach (var (phrase, meaning) in meanings)
        {
            var key = PersonalIntensityMapper.Normalize(phrase);
            if (key.Length == 0 || key.Length > MaxPhraseLength || intensity.Resolve(meaning) is not { } resolved)
            {
                continue;
            }

            if (await terms.FindAsync(userId, key, cancellationToken) is { } existing)
            {
                existing.Percent = resolved.Percent;
            }
            else
            {
                terms.Add(new UserEffortTerm { Id = Guid.NewGuid(), OwnerId = userId, Phrase = key, Percent = resolved.Percent });
            }

            changed = true;
        }

        if (changed)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }
}
