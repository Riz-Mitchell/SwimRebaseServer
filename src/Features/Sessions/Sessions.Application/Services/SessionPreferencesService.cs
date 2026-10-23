using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Sessions.Application.Interfaces;
using Sessions.Domain.Calculations;
using Sessions.Domain.Models;
using Shared.Application;

namespace Sessions.Application.Services;

public sealed class InvalidSessionPreferencesException(string message) : Exception(message);

/// <summary>A swimmer's settings. <paramref name="EffortScale"/> is the scale id metrics group intensity by when a request names none.</summary>
public sealed record SessionPreferencesView(int DefaultEffort, string EffortScale, IReadOnlyList<EffortBand>? CustomEffortScale, DistanceUnit DisplayUnit);

/// <summary>Only the fields that are set change. An empty <paramref name="CustomEffortScale"/> removes the saved one.</summary>
public sealed record SessionPreferencesUpdate(int? DefaultEffort, string? EffortScale, IReadOnlyList<EffortBand>? CustomEffortScale, DistanceUnit? DisplayUnit = null);

public sealed class SessionPreferencesService(
    ISessionPreferencesRepository preferences,
    [FromKeyedServices(SessionsKeys.UnitOfWork)] IUnitOfWork unitOfWork,
    ILogger<SessionPreferencesService>? logger = null)
{
    private readonly ILogger<SessionPreferencesService> _logger = logger ?? NullLogger<SessionPreferencesService>.Instance;

    public async Task<int> GetDefaultEffortAsync(Guid userId, CancellationToken cancellationToken) =>
        (await preferences.FindAsync(userId, cancellationToken))?.DefaultEffort ?? SessionPreferences.BuiltInDefaultEffort;

    public async Task<SessionPreferencesView> GetAsync(Guid userId, CancellationToken cancellationToken) =>
        ToView(await preferences.FindAsync(userId, cancellationToken));

    /// <exception cref="InvalidSessionPreferencesException">The default effort is outside 1-100, the scale or display unit is unknown, or the custom bands are not a usable scale.</exception>
    public async Task SetDefaultEffortAsync(Guid userId, int defaultEffort, CancellationToken cancellationToken) =>
        await UpdateAsync(userId, new SessionPreferencesUpdate(defaultEffort, null, null), cancellationToken);

    /// <exception cref="InvalidSessionPreferencesException">The default effort is outside 1-100, the scale or display unit is unknown, or the custom bands are not a usable scale.</exception>
    public async Task<SessionPreferencesView> UpdateAsync(Guid userId, SessionPreferencesUpdate update, CancellationToken cancellationToken)
    {
        if (update.DefaultEffort is < Intensity.Min or > Intensity.Max)
        {
            _logger.LogWarning("[{ClassName}] User {UserId} sent default effort {DefaultEffort} outside {Min}-{Max}", nameof(SessionPreferencesService), userId, update.DefaultEffort, Intensity.Min, Intensity.Max);
            throw new InvalidSessionPreferencesException($"defaultEffort must be between {Intensity.Min} and {Intensity.Max}.");
        }

        if (update.DisplayUnit is { } unit && !Enum.IsDefined(unit))
        {
            _logger.LogWarning("[{ClassName}] User {UserId} sent unknown display unit {DisplayUnit}", nameof(SessionPreferencesService), userId, update.DisplayUnit);
            throw new InvalidSessionPreferencesException($"displayUnit must be one of {string.Join(", ", Enum.GetNames<DistanceUnit>())}.");
        }

        if (update.CustomEffortScale is { Count: > 0 } proposed && EffortScale.Validate(proposed) is { Count: > 0 } problems)
        {
            _logger.LogWarning("[{ClassName}] User {UserId} sent an invalid custom effort scale: {Problems}", nameof(SessionPreferencesService), userId, string.Join("; ", problems));
            throw new InvalidSessionPreferencesException($"customEffortScale is not valid: {string.Join("; ", problems)}.");
        }

        var existing = await preferences.FindAsync(userId, cancellationToken);
        var customJson = update.CustomEffortScale switch
        {
            null => existing?.CustomEffortScaleJson,
            { Count: 0 } => null,
            var bands => SessionJson.Serialize(EffortScale.Custom(bands).Bands),
        };

        var requested = update.EffortScale?.Trim();
        var scaleId = requested ?? existing?.EffortScale ?? EffortScale.TensId;
        if (string.Equals(scaleId, EffortScale.CustomId, StringComparison.OrdinalIgnoreCase))
        {
            if (customJson is null && requested is not null)
            {
                _logger.LogWarning("[{ClassName}] User {UserId} selected the custom effort scale without defining one", nameof(SessionPreferencesService), userId);
                throw new InvalidSessionPreferencesException("effortScale \"custom\" needs a customEffortScale.");
            }

            // Removing the custom scale while it is selected falls back to the default.
            scaleId = customJson is null ? EffortScale.TensId : EffortScale.CustomId;
        }
        else
        {
            var known = EffortVocabulary.Default.FindScale(scaleId)?.Id;
            if (known is null)
            {
                _logger.LogWarning("[{ClassName}] User {UserId} selected unknown effort scale '{ScaleId}'", nameof(SessionPreferencesService), userId, scaleId);
                throw new InvalidSessionPreferencesException($"unknown effortScale \"{scaleId}\"; use {string.Join(", ", EffortVocabulary.Default.Scales.Select(s => s.Id))} or custom.");
            }

            scaleId = known;
        }

        if (existing is null)
        {
            existing = new SessionPreferences { OwnerId = userId };
            preferences.Add(existing);
        }

        existing.DefaultEffort = update.DefaultEffort ?? existing.DefaultEffort;
        existing.DisplayUnit = update.DisplayUnit ?? existing.DisplayUnit;
        existing.EffortScale = scaleId;
        existing.CustomEffortScaleJson = customJson;

        await unitOfWork.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("[{ClassName}] Updated preferences for user {UserId}: default effort {DefaultEffort}, scale {ScaleId}",
            nameof(SessionPreferencesService), userId, existing.DefaultEffort, scaleId);
        return ToView(existing);
    }

    private static SessionPreferencesView ToView(SessionPreferences? saved) => new(
        saved?.DefaultEffort ?? SessionPreferences.BuiltInDefaultEffort,
        saved?.EffortScale ?? EffortScale.TensId,
        saved?.CustomEffortScaleJson is { Length: > 0 } json ? SessionJson.Deserialize<List<EffortBand>>(json) : null,
        saved?.DisplayUnit ?? DistanceUnit.Metres);
}
