using Sessions.Application.Services;
using Sessions.Domain.Models;

namespace SwimRebase.Tests.Sessions;

public class TimedSwimServiceTests
{
    private static readonly Guid User = Guid.NewGuid();

    private static (TimedSwimService Service, InMemoryTimedSwimRepository Repo, InMemoryUserEffortTermRepository Terms) Build(InMemorySessionRepository? sessions = null)
    {
        var repo = new InMemoryTimedSwimRepository();
        var terms = new InMemoryUserEffortTermRepository();
        return (new TimedSwimService(repo, sessions ?? new InMemorySessionRepository(), TestSessions.Resolver(), TestSessions.Terms(terms), new NoOpUnitOfWork()), repo, terms);
    }

    private static LogTimedSwimRequest Request(decimal dist = 25, double seconds = 12.4, string? effort = "max", List<ResistanceItem>? gear = null, Guid? sessionId = null) =>
        new(sessionId, new DateOnly(2026, 11, 23), PoolCourse.Scm, dist, Stroke.Free, SwimMode.Swim, seconds, effort, StartType.Dive, FinishReference.Head, gear, null);

    [Fact]
    public async Task Logging_resolves_intensity_and_keeps_the_gear()
    {
        var (service, repo, _) = Build();

        var view = await service.LogAsync(User, Request(gear: [new() { Kind = ResistanceKind.Sponges, Count = 2 }, new() { Kind = ResistanceKind.Parachute, WidthCm = 30, HeightCm = 20 }]), default);

        Assert.Equal(100, view.Intensity);
        Assert.Equal([ResistanceKind.Sponges, ResistanceKind.Parachute], view.Resistances.Select(r => r.Kind));
        Assert.Equal(600m, view.Resistances[1].AreaCm2);
        Assert.Single(repo.Swims);
    }

    [Fact]
    public async Task An_invalid_swim_is_rejected_and_not_saved()
    {
        var (service, repo, _) = Build();

        var ex = await Assert.ThrowsAsync<InvalidTimedSwimException>(() => service.LogAsync(User, Request(gear: [new() { Kind = ResistanceKind.Sponges }]), default));

        Assert.Contains(ex.Problems, p => p.Contains("needs count"));
        Assert.Empty(repo.Swims);
    }

    [Fact]
    public async Task The_swimmers_own_effort_terms_resolve_intensity()
    {
        var (service, _, terms) = Build();
        terms.Terms.Add(new UserEffortTerm { Id = Guid.NewGuid(), OwnerId = User, Phrase = "dive hat", Percent = 97 });

        Assert.Equal(97, (await service.LogAsync(User, Request(effort: "dive hat"), default)).Intensity);
        Assert.Null((await service.LogAsync(Guid.NewGuid(), Request(effort: "dive hat"), default)).Intensity);
    }

    [Fact]
    public async Task Updating_replaces_the_swim_and_its_gear_but_keeps_identity()
    {
        var (service, repo, _) = Build();
        var logged = await service.LogAsync(User, Request(gear: [new() { Kind = ResistanceKind.Sponges, Count = 2 }]), default);

        var updated = await service.UpdateAsync(User, logged.Id, Request(seconds: 11.8, effort: "easy", gear: [new() { Kind = ResistanceKind.WeightedBelt, WeightKg = 2 }]), default);

        Assert.Equal(logged.Id, updated.Id);
        Assert.Equal(logged.CreatedAt, updated.CreatedAt);
        Assert.Equal(11.8, updated.Seconds);
        Assert.Equal(20, updated.Intensity);
        Assert.Equal(ResistanceKind.WeightedBelt, Assert.Single(updated.Resistances).Kind);
        Assert.Single(repo.Swims);
    }

    [Fact]
    public async Task An_invalid_update_changes_nothing()
    {
        var (service, repo, _) = Build();
        var logged = await service.LogAsync(User, Request(), default);

        await Assert.ThrowsAsync<InvalidTimedSwimException>(() => service.UpdateAsync(User, logged.Id, Request(seconds: 0), default));

        Assert.Equal(12.4, repo.Swims.Single().Seconds);
    }

    [Fact]
    public async Task Another_swimmers_swim_cannot_be_read_updated_or_deleted()
    {
        var (service, repo, _) = Build();
        var logged = await service.LogAsync(User, Request(), default);
        var stranger = Guid.NewGuid();

        await Assert.ThrowsAsync<TimedSwimNotFoundException>(() => service.GetAsync(stranger, logged.Id, default));
        await Assert.ThrowsAsync<TimedSwimNotFoundException>(() => service.UpdateAsync(stranger, logged.Id, Request(), default));
        await Assert.ThrowsAsync<TimedSwimNotFoundException>(() => service.DeleteAsync(stranger, logged.Id, default));
        Assert.Single(repo.Swims);
    }

    [Fact]
    public async Task A_swim_logged_without_a_start_or_finish_is_a_dive_with_a_hand_finish()
    {
        var (service, repo, _) = Build();

        var view = await service.LogAsync(User, Request() with { StartType = null, Finish = null }, default);

        Assert.Equal(StartType.Dive, view.StartType);
        Assert.Equal(FinishReference.Hand, view.Finish);
        Assert.Equal(StartType.Dive, repo.Swims.Single().StartType);
        Assert.Equal(FinishReference.Hand, repo.Swims.Single().Finish);
    }

    [Fact]
    public async Task A_stated_start_and_finish_are_kept_and_each_defaults_on_its_own()
    {
        var (service, _, _) = Build();

        var pushed = await service.LogAsync(User, Request() with { StartType = StartType.Push, Finish = null }, default);
        var feet = await service.LogAsync(User, Request() with { StartType = null, Finish = FinishReference.Feet }, default);

        Assert.Equal((StartType.Push, FinishReference.Hand), (pushed.StartType, pushed.Finish));
        Assert.Equal((StartType.Dive, FinishReference.Feet), (feet.StartType, feet.Finish));
    }

    [Fact]
    public async Task An_update_without_a_start_or_finish_goes_back_to_the_defaults()
    {
        var (service, _, _) = Build();
        var logged = await service.LogAsync(User, Request() with { StartType = StartType.Push, Finish = FinishReference.Feet }, default);

        var updated = await service.UpdateAsync(User, logged.Id, Request() with { StartType = null, Finish = null }, default);

        Assert.Equal((StartType.Dive, FinishReference.Hand), (updated.StartType, updated.Finish));
    }

    [Fact]
    public async Task Deleting_removes_the_swim()
    {
        var (service, repo, _) = Build();
        var logged = await service.LogAsync(User, Request(), default);

        await service.DeleteAsync(User, logged.Id, default);

        Assert.Empty(repo.Swims);
        await Assert.ThrowsAsync<TimedSwimNotFoundException>(() => service.GetAsync(User, logged.Id, default));
    }

    [Fact]
    public async Task A_swim_can_be_linked_to_the_swimmers_own_session()
    {
        var sessions = new InMemorySessionRepository();
        var own = new Session { Id = Guid.NewGuid(), OwnerId = User };
        sessions.Sessions.Add(own);
        var (service, _, _) = Build(sessions);

        var logged = await service.LogAsync(User, Request(sessionId: own.Id), default);
        var updated = await service.UpdateAsync(User, logged.Id, Request(seconds: 12.0, sessionId: own.Id), default);

        Assert.Equal(own.Id, logged.SessionId);
        Assert.Equal(own.Id, updated.SessionId);
    }

    [Fact]
    public async Task Logging_against_another_swimmers_or_a_missing_session_is_rejected_and_not_saved()
    {
        var sessions = new InMemorySessionRepository();
        var theirs = new Session { Id = Guid.NewGuid(), OwnerId = Guid.NewGuid() };
        sessions.Sessions.Add(theirs);
        var (service, repo, _) = Build(sessions);

        var ex = await Assert.ThrowsAsync<InvalidTimedSwimException>(() => service.LogAsync(User, Request(sessionId: theirs.Id), default));
        await Assert.ThrowsAsync<InvalidTimedSwimException>(() => service.LogAsync(User, Request(sessionId: Guid.NewGuid()), default));

        Assert.Contains(ex.Problems, p => p.Contains("not one of your sessions"));
        Assert.Empty(repo.Swims);
    }

    [Fact]
    public async Task Updating_a_swim_onto_another_swimmers_session_is_rejected_and_changes_nothing()
    {
        var sessions = new InMemorySessionRepository();
        var theirs = new Session { Id = Guid.NewGuid(), OwnerId = Guid.NewGuid() };
        sessions.Sessions.Add(theirs);
        var (service, repo, _) = Build(sessions);
        var logged = await service.LogAsync(User, Request(), default);

        await Assert.ThrowsAsync<InvalidTimedSwimException>(() => service.UpdateAsync(User, logged.Id, Request(seconds: 11.0, sessionId: theirs.Id), default));

        var stored = repo.Swims.Single();
        Assert.Null(stored.SessionId);
        Assert.Equal(12.4, stored.Seconds);
    }
}
