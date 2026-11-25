using Sessions.Application.Metrics;
using Sessions.Application.Services;
using Sessions.Domain.Models;
using SwimRebase.Tests.SessionConversion;

namespace SwimRebase.Tests.Sessions;

public class SessionDetailsTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateOnly Day = new(2026, 9, 21);

    private static SwimSession Sample() => TestSessions.Ingest().Prepare(SessionJson.Deserialize<SwimSession>(DraftTests.ModelSession));

    private static async Task<(SessionService Service, InMemorySessionRepository Repository, SessionView Saved)> SavedAsync(PoolCourse course = PoolCourse.Scm)
    {
        var repository = new InMemorySessionRepository();
        var service = TestSessions.Service(repository);
        var saved = await service.CreateAsync(UserId, Sample(), Day, course, "Tuesday squad", SessionSource.Ai, null, CancellationToken.None);
        return (service, repository, saved);
    }

    private static Task<SessionView> UpdateAsync(SessionService service, Guid id, string? title = null, DateOnly? day = null, PoolCourse? course = null, Guid? user = null) =>
        service.UpdateDetailsAsync(user ?? UserId, id, new SessionDetailsUpdate(title, day, course), CancellationToken.None);

    private static IReadOnlyList<FactTotal> Totals(InMemorySessionRepository repository) =>
        repository.Facts.Select(f => new FactTotal(f.PerformedOn, f.SessionId, f.Stroke, f.Mode, f.Intensity, f.Unit, f.Distance)).ToList();

    private static MetricsQuery Query(DistanceUnit? display, string? from = null, string? to = null) =>
        new(from is null ? null : DateOnly.Parse(from), to is null ? null : DateOnly.Parse(to), null, null, null, null, [], MetricMeasure.Distance, DisplayUnit: display);

    [Fact]
    public async Task A_new_date_moves_every_fact_and_leaves_the_tree_and_total_alone()
    {
        var (service, repository, saved) = await SavedAsync();
        var contentBefore = repository.Sessions.Single().ContentJson;
        var newDay = new DateOnly(2026, 10, 5);

        var updated = await UpdateAsync(service, saved.Id, day: newDay);

        Assert.Equal(newDay, updated.PerformedOn);
        Assert.Equal(newDay, repository.Sessions.Single().PerformedOn);
        Assert.All(repository.Facts, f => Assert.Equal(newDay, f.PerformedOn));
        Assert.Equal(contentBefore, repository.Sessions.Single().ContentJson);
        Assert.Equal(saved.TotalDistance, repository.Facts.Sum(f => f.Distance));

        // Metrics over the old date no longer see the session; over the new date they do.
        Assert.Equal(0, MetricsCalculator.Compute(Query(DistanceUnit.Metres, "2026-09-21", "2026-09-21"), Totals(repository)).TotalDistance);
        Assert.Equal(1700, MetricsCalculator.Compute(Query(DistanceUnit.Metres, "2026-10-05", "2026-10-05"), Totals(repository)).TotalDistance);
    }

    [Fact]
    public async Task Changing_the_course_to_scy_keeps_the_distances_and_changes_their_unit()
    {
        var (service, repository, saved) = await SavedAsync(PoolCourse.Scm);

        var updated = await UpdateAsync(service, saved.Id, course: PoolCourse.Scy);

        Assert.Equal(PoolCourse.Scy, updated.Course);
        Assert.Equal(DistanceUnit.Yards, updated.Unit);
        Assert.Equal(saved.TotalDistance, updated.TotalDistance);
        Assert.All(repository.Facts, f =>
        {
            Assert.Equal(PoolCourse.Scy, f.Course);
            Assert.Equal(DistanceUnit.Yards, f.Unit);
        });
        Assert.Equal(saved.TotalDistance, repository.Facts.Sum(f => f.Distance));
    }

    [Fact]
    public async Task Changing_between_metre_courses_changes_no_unit()
    {
        var (service, repository, saved) = await SavedAsync(PoolCourse.Scm);

        await UpdateAsync(service, saved.Id, course: PoolCourse.Lcm);

        Assert.All(repository.Facts, f =>
        {
            Assert.Equal(PoolCourse.Lcm, f.Course);
            Assert.Equal(DistanceUnit.Metres, f.Unit);
        });
    }

    [Fact]
    public async Task A_title_change_alone_is_cleaned_and_leaves_the_facts_untouched()
    {
        var (service, repository, saved) = await SavedAsync();
        var factsBefore = repository.Facts.ToList();

        var updated = await UpdateAsync(service, saved.Id, title: "  Thursday   sprints \n");

        Assert.Equal("Thursday sprints", updated.Title);
        Assert.Equal("Thursday sprints", repository.Sessions.Single().Title);
        Assert.True(factsBefore.SequenceEqual(repository.Facts, ReferenceEqualityComparer.Instance));
    }

    [Fact]
    public async Task Nobody_can_change_another_swimmers_session()
    {
        var (service, repository, saved) = await SavedAsync();

        await Assert.ThrowsAsync<SessionNotFoundException>(() => UpdateAsync(service, saved.Id, title: "Mine now", user: Guid.NewGuid()));

        Assert.Equal("Tuesday squad", repository.Sessions.Single().Title);
    }

    [Fact]
    public async Task An_empty_or_unusable_change_is_refused_and_changes_nothing()
    {
        var (service, repository, saved) = await SavedAsync();

        await Assert.ThrowsAsync<InvalidSessionDetailsException>(() => UpdateAsync(service, saved.Id));
        await Assert.ThrowsAsync<InvalidSessionDetailsException>(() => UpdateAsync(service, saved.Id, title: "   "));
        await Assert.ThrowsAsync<InvalidSessionDetailsException>(() => UpdateAsync(service, saved.Id, day: default(DateOnly)));
        await Assert.ThrowsAsync<InvalidSessionDetailsException>(() => UpdateAsync(service, saved.Id, course: (PoolCourse)99));

        var stored = repository.Sessions.Single();
        Assert.Equal("Tuesday squad", stored.Title);
        Assert.Equal(Day, stored.PerformedOn);
        Assert.Equal(PoolCourse.Scm, stored.Course);
    }

    [Fact]
    public void Yards_and_metres_add_up_in_the_display_unit()
    {
        var yards = new FactTotal(new DateOnly(2026, 9, 21), Guid.NewGuid(), Stroke.Free, SwimMode.Swim, 50, DistanceUnit.Yards, 2000);
        var metres = new FactTotal(new DateOnly(2026, 9, 22), Guid.NewGuid(), Stroke.Free, SwimMode.Swim, 50, DistanceUnit.Metres, 2000);

        var inMetres = MetricsCalculator.Compute(Query(DistanceUnit.Metres), [yards, metres]);
        Assert.Equal(3828.8m, inMetres.TotalDistance);
        Assert.Equal([DistanceUnit.Metres], inMetres.Units);
        Assert.Equal(3828.8, Assert.Single(inMetres.Rows).Value);

        var inYards = MetricsCalculator.Compute(Query(DistanceUnit.Yards), [yards, metres]);
        Assert.Equal(4187.23m, inYards.TotalDistance);
        Assert.Equal([DistanceUnit.Yards], inYards.Units);
    }

    [Fact]
    public void Without_a_display_unit_nothing_is_converted()
    {
        var yards = new FactTotal(new DateOnly(2026, 9, 21), Guid.NewGuid(), Stroke.Free, SwimMode.Swim, 50, DistanceUnit.Yards, 2000);
        var metres = new FactTotal(new DateOnly(2026, 9, 22), Guid.NewGuid(), Stroke.Free, SwimMode.Swim, 50, DistanceUnit.Metres, 2000);

        var result = MetricsCalculator.Compute(Query(null), [yards, metres]);

        Assert.Equal(4000, result.TotalDistance);
        Assert.Equal(2, result.Units.Count);
    }

    [Fact]
    public async Task The_metrics_service_uses_the_swimmers_display_unit()
    {
        var session = Guid.NewGuid();
        var data = new InMemoryMetricsRepository([new FactTotal(new DateOnly(2026, 9, 21), session, Stroke.Free, SwimMode.Swim, 50, DistanceUnit.Yards, 1000)]);
        var preferences = new InMemoryPreferencesRepository();

        var defaultUnit = await new MetricsService(data, preferences).QueryAsync(UserId, Query(null), CancellationToken.None);
        Assert.Equal(914.4m, defaultUnit.TotalDistance);
        Assert.Equal([DistanceUnit.Metres], defaultUnit.Units);

        preferences.Rows.Add(new SessionPreferences { OwnerId = UserId, DisplayUnit = DistanceUnit.Yards });
        var yards = await new MetricsService(data, preferences).QueryAsync(UserId, Query(null), CancellationToken.None);
        Assert.Equal(1000m, yards.TotalDistance);
        Assert.Equal([DistanceUnit.Yards], yards.Units);
    }

    [Fact]
    public async Task After_a_course_change_the_metrics_total_is_the_converted_figure()
    {
        var (service, repository, saved) = await SavedAsync(PoolCourse.Scm);

        await UpdateAsync(service, saved.Id, course: PoolCourse.Scy);

        var inMetres = MetricsCalculator.Compute(Query(DistanceUnit.Metres), Totals(repository));
        Assert.Equal(decimal.Round(1700 * 0.9144m, 2), inMetres.TotalDistance);
        var inYards = MetricsCalculator.Compute(Query(DistanceUnit.Yards), Totals(repository));
        Assert.Equal(1700m, inYards.TotalDistance);
    }
}
