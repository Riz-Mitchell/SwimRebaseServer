using Sessions.Application.Load;
using Sessions.Application.Services;
using Sessions.Domain.Calculations;
using Sessions.Domain.Models;
using SwimRebase.Tests.SessionConversion;

namespace SwimRebase.Tests.Sessions;

public class SessionLoadServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateOnly Day = new(2026, 9, 21);

    // The sample board: 4 x 100 easy (2), 3 x (200 kick + 2 x 50 hard (8)), 400 im. Only 400 + 600 + 300 + 400 has effort worked out
    // for it: the kick and the im have none and so count at the default.
    private const double LoadAtFive = (16 + 150 + 192 + 100) / 1000.0;

    private static SwimSession Sample() => TestSessions.Ingest().Prepare(SessionJson.Deserialize<SwimSession>(DraftTests.ModelSession));

    private sealed class Rig
    {
        public InMemorySessionRepository Sessions { get; } = new();

        public InMemoryTimedSwimRepository TimedSwims { get; } = new();

        public InMemoryPreferencesRepository Preferences { get; } = new();

        public SessionLoadService Load => TestSessions.Load(Sessions, TimedSwims, Preferences);

        public SessionService Service => TestSessions.Service(Sessions, Load);

        public SessionPreferencesService PreferencesService => new(Preferences, Load, new NoOpUnitOfWork());

        public TimedSwimService TimedSwimService => new(TimedSwims, Load, [new WordsIntensityMapper()], new NoOpUnitOfWork());

        public Task<SessionView> Save() =>
            Service.CreateAsync(UserId, Sample(), Day, PoolCourse.Scm, null, SessionSource.Logged, null, CancellationToken.None);
    }

    [Fact]
    public async Task Saving_a_session_stores_its_load_and_the_distance_that_had_no_effort()
    {
        var rig = new Rig();

        var saved = await rig.Save();

        var stored = Assert.Single(rig.Sessions.Sessions);
        Assert.Equal(LoadAtFive, stored.Load, 6);
        Assert.Equal(1000, stored.UnratedDistance);
        Assert.Equal(stored.Load, saved.Load);
    }

    [Fact]
    public async Task Editing_a_session_rebuilds_its_load()
    {
        var rig = new Rig();
        var saved = await rig.Save();

        // Dropping the first set removes 4 x 100 easy: 16 of the 458.
        await rig.Service.EditAsync(UserId, saved.Id, [new SessionEdit(SessionEditOps.Remove, "0", null, null)], CancellationToken.None);

        Assert.Equal((150 + 192 + 100) / 1000.0, rig.Sessions.Sessions.Single().Load, 6);
    }

    [Fact]
    public async Task Changing_the_default_effort_rebuilds_every_sessions_load()
    {
        var rig = new Rig();
        await rig.Save();
        await rig.Save();

        await rig.PreferencesService.SetDefaultEffortAsync(UserId, 8, CancellationToken.None);

        // 1000 unrated at level 8 is 640, plus the 16 and the 192 that had an effort.
        Assert.All(rig.Sessions.Sessions, s => Assert.Equal((16 + 192 + 640) / 1000.0, s.Load, 6));
        Assert.Equal(8, await rig.PreferencesService.GetDefaultEffortAsync(UserId, CancellationToken.None));
    }

    [Fact]
    public async Task A_new_session_uses_the_swimmers_default_effort()
    {
        var rig = new Rig();
        await rig.PreferencesService.SetDefaultEffortAsync(UserId, 8, CancellationToken.None);

        await rig.Save();

        Assert.Equal((16 + 192 + 640) / 1000.0, rig.Sessions.Sessions.Single().Load, 6);
    }

    [Fact]
    public async Task Until_one_is_chosen_the_default_effort_is_the_built_in_level()
    {
        var rig = new Rig();

        Assert.Equal(SessionPreferences.BuiltInDefaultEffort, await rig.PreferencesService.GetDefaultEffortAsync(UserId, CancellationToken.None));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public async Task A_default_effort_outside_the_scale_is_rejected_and_nothing_changes(int level)
    {
        var rig = new Rig();
        await rig.Save();

        await Assert.ThrowsAsync<InvalidSessionPreferencesException>(() => rig.PreferencesService.SetDefaultEffortAsync(UserId, level, CancellationToken.None));

        Assert.Empty(rig.Preferences.Rows);
        Assert.Equal(LoadAtFive, rig.Sessions.Sessions.Single().Load, 6);
    }

    [Fact]
    public async Task A_timed_swim_that_counts_towards_metrics_adds_to_its_sessions_load()
    {
        var rig = new Rig();
        var saved = await rig.Save();

        await rig.TimedSwimService.LogAsync(UserId, Swim(saved.Id, includeInMetrics: true), CancellationToken.None);

        // 100 at "hard" (8) is 64 more.
        Assert.Equal(LoadAtFive + 64 / 1000.0, rig.Sessions.Sessions.Single().Load, 6);
    }

    [Fact]
    public async Task A_timed_swim_that_does_not_count_or_is_standalone_leaves_the_load_alone()
    {
        var rig = new Rig();
        var saved = await rig.Save();

        await rig.TimedSwimService.LogAsync(UserId, Swim(saved.Id, includeInMetrics: false), CancellationToken.None);
        await rig.TimedSwimService.LogAsync(UserId, Swim(null, includeInMetrics: true), CancellationToken.None);

        Assert.Equal(LoadAtFive, rig.Sessions.Sessions.Single().Load, 6);
    }

    [Fact]
    public async Task Editing_a_session_keeps_the_timed_swims_already_counted_towards_it()
    {
        var rig = new Rig();
        var saved = await rig.Save();
        await rig.TimedSwimService.LogAsync(UserId, Swim(saved.Id, includeInMetrics: true), CancellationToken.None);

        await rig.Service.EditAsync(UserId, saved.Id, [new SessionEdit(SessionEditOps.Remove, "0", null, null)], CancellationToken.None);

        Assert.Equal((150 + 192 + 100 + 64) / 1000.0, rig.Sessions.Sessions.Single().Load, 6);
    }

    [Fact]
    public async Task The_series_reads_the_stored_loads()
    {
        var rig = new Rig();
        await rig.Save();

        var result = await rig.Load.QueryAsync(
            UserId, new LoadQuery(null, null, LoadGrouping.Session), Day, CancellationToken.None);

        var row = Assert.Single(result.Rows);
        Assert.Equal(Math.Round(LoadAtFive, 3), row.Load);
        Assert.Equal(1000, row.UnratedDistance);
    }

    private static LogTimedSwimRequest Swim(Guid? sessionId, bool includeInMetrics) =>
        new(sessionId, Day, PoolCourse.Scm, 100, Stroke.Free, SwimMode.Swim, 70, "hard", null, null, null, null, includeInMetrics);
}
