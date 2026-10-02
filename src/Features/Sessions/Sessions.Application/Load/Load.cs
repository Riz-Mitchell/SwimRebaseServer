using Sessions.Domain.Models;

namespace Sessions.Application.Load;

public enum LoadGrouping { Session, Day, Week, Month }

/// <param name="BaselinePeriods">How many rows before each row the baseline averages.</param>
public sealed record LoadQuery(DateOnly? From, DateOnly? To, LoadGrouping GroupBy, int BaselinePeriods = LoadQuery.DefaultBaselinePeriods)
{
    public const int DefaultBaselinePeriods = 4;
    public const int MaxBaselinePeriods = 26;
    public const int MaxDays = 366;
}

/// <summary>A session's stored load, as the series calculator reads it.</summary>
public sealed record SessionLoadRow(Guid SessionId, DateOnly PerformedOn, double Load, int UnratedDistance, DistanceUnit Unit);

/// <param name="Period">The session's date, the day, the Monday that starts the week, or yyyy-MM.</param>
/// <param name="SessionId">Set only when grouped by session.</param>
/// <param name="Load">The period's mean load per day (rest days count as 0); for a session, its own load.</param>
/// <param name="TotalLoad">The loads of every session in the period added up.</param>
/// <param name="Days">How many days <paramref name="Load"/> is averaged over: those of the period that fall in the range and are not in the future.</param>
/// <param name="Baseline">Mean of the previous rows' <paramref name="Load"/>. Null until that many rows exist.</param>
public sealed record LoadRow(
    string Period, Guid? SessionId, double Load, double TotalLoad, int SessionCount, int Days, int UnratedDistance, double? Baseline);

/// <param name="Units">Every distance unit present. More than one means loads mix metres and yards - nothing is converted.</param>
public sealed record LoadResult(LoadGrouping GroupBy, int BaselinePeriods, IReadOnlyList<LoadRow> Rows, IReadOnlyList<DistanceUnit> Units);

public sealed class InvalidLoadQueryException(string message) : Exception(message);
