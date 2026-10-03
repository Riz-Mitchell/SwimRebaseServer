using Microsoft.Extensions.DependencyInjection;
using Sessions.Application.Interfaces;
using Sessions.Domain.Models;
using Shared.Application;

namespace Sessions.Application.Services;

public sealed class InvalidSessionPreferencesException(string message) : Exception(message);

public sealed class SessionPreferencesService(
    ISessionPreferencesRepository preferences,
    [FromKeyedServices(SessionsKeys.UnitOfWork)] IUnitOfWork unitOfWork)
{
    public async Task<int> GetDefaultEffortAsync(Guid userId, CancellationToken cancellationToken) =>
        (await preferences.FindAsync(userId, cancellationToken))?.DefaultEffort ?? SessionPreferences.BuiltInDefaultEffort;

    /// <exception cref="InvalidSessionPreferencesException">The level is outside 1-10.</exception>
    public async Task SetDefaultEffortAsync(Guid userId, int defaultEffort, CancellationToken cancellationToken)
    {
        if (defaultEffort is < Intensity.Min or > Intensity.Max)
        {
            throw new InvalidSessionPreferencesException($"defaultEffort must be between {Intensity.Min} and {Intensity.Max}.");
        }

        if (await preferences.FindAsync(userId, cancellationToken) is { } existing)
        {
            existing.DefaultEffort = defaultEffort;
        }
        else
        {
            preferences.Add(new SessionPreferences { OwnerId = userId, DefaultEffort = defaultEffort });
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
