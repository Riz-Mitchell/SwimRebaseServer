using Sessions.Application.Services;
using Sessions.Domain.Calculations;
using Sessions.Domain.Models;
using SessionConversion.Application.Services;
using SwimRebase.Tests.SessionConversion;

namespace SwimRebase.Tests.Sessions;

public class SessionServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateOnly Day = new(2026, 9, 21);

    private static SwimSession Sample() => TestSessions.Ingest().Prepare(SessionJson.Deserialize<SwimSession>(DraftTests.ModelSession));

    [Fact]
    public async Task Saving_a_session_stores_the_tree_the_total_and_the_facts_together()
    {
        var repository = new InMemorySessionRepository();
        var service = TestSessions.Service(repository);

        var saved = await service.CreateAsync(UserId, Sample(), Day, PoolCourse.Scm, "Tuesday squad", SessionSource.Ai, Guid.NewGuid(), CancellationToken.None);

        var stored = Assert.Single(repository.Sessions);
        Assert.Equal(saved.Id, stored.Id);
        Assert.Equal(1700, stored.TotalDistance);
        Assert.Equal("Tuesday squad", stored.Title);
        Assert.Equal(SessionSource.Ai, stored.Source);
        Assert.Equal(DistanceUnit.Metres, saved.Unit);
        Assert.Equal(stored.TotalDistance, repository.Facts.Sum(f => f.Distance));
        Assert.All(repository.Facts, f => Assert.False(string.IsNullOrEmpty(f.NodeId)));

        // The stored tree carries node ids, and the intensity mapped from the board's words.
        var content = SessionJson.Deserialize<SwimSession>(stored.ContentJson);
        var first = Assert.IsType<SetItem>(content.Sets[0]);
        Assert.False(string.IsNullOrEmpty(first.Id));
        Assert.Equal(2, first.Parts[0].Intensity!.Level);
    }

    [Fact]
    public async Task Saving_the_same_draft_twice_returns_the_first_session()
    {
        var repository = new InMemorySessionRepository();
        var service = TestSessions.Service(repository);
        var draftId = Guid.NewGuid();

        var first = await service.CreateAsync(UserId, Sample(), Day, PoolCourse.Scy, null, SessionSource.Ai, draftId, CancellationToken.None);
        var again = await service.CreateAsync(UserId, Sample(), Day, PoolCourse.Scy, null, SessionSource.Ai, draftId, CancellationToken.None);

        Assert.Equal(first.Id, again.Id);
        Assert.Single(repository.Sessions);
        Assert.Equal(first.TotalDistance, repository.Facts.Sum(f => f.Distance));
    }

    [Fact]
    public async Task A_session_without_a_title_is_named_for_the_day_it_was_swum()
    {
        var saved = await TestSessions.Service(new InMemorySessionRepository())
            .CreateAsync(UserId, Sample(), Day, PoolCourse.Scy, "  ", SessionSource.Logged, null, CancellationToken.None);

        Assert.Equal("Swim 2026-09-21", saved.Title);
    }

    [Fact]
    public async Task An_invalid_session_is_never_stored()
    {
        var repository = new InMemorySessionRepository();
        var service = TestSessions.Service(repository);

        await Assert.ThrowsAsync<InvalidSessionException>(() =>
            service.CreateAsync(UserId, new SwimSession(), Day, PoolCourse.Scy, null, SessionSource.Logged, null, CancellationToken.None));

        Assert.Empty(repository.Sessions);
        Assert.Empty(repository.Facts);
    }

    [Fact]
    public async Task Editing_a_session_rebuilds_its_total_and_facts_and_keeps_node_ids()
    {
        var repository = new InMemorySessionRepository();
        var service = TestSessions.Service(repository);
        var saved = await service.CreateAsync(UserId, Sample(), Day, PoolCourse.Scy, null, SessionSource.Ai, null, CancellationToken.None);
        var firstItemId = SessionJson.Deserialize<SwimSession>(repository.Sessions[0].ContentJson).Sets[0].Id;

        // Forgot a set: add 8 x 25 kick fast at the end.
        var edited = await service.EditAsync(UserId, saved.Id,
        [
            new SessionEdit(SessionEditOps.Insert, "3", new SetItem { Reps = 8, Parts = [new Part { Dist = 25, Stroke = Stroke.Free, Mode = SwimMode.Kick, Effort = "fast" }] }, null),
        ], CancellationToken.None);

        Assert.Equal(1700 + 200, edited.TotalDistance);
        Assert.Equal(1900, repository.Sessions[0].TotalDistance);
        Assert.Equal(1900, repository.Facts.Sum(f => f.Distance));
        Assert.Equal(200, repository.Facts.Where(f => f.Mode == SwimMode.Kick && f.Intensity == 8).Sum(f => f.Distance));
        Assert.Equal(firstItemId, SessionJson.Deserialize<SwimSession>(repository.Sessions[0].ContentJson).Sets[0].Id);
        Assert.All(repository.Facts, f => Assert.Equal(saved.Id, f.SessionId));
    }

    [Fact]
    public async Task An_edit_that_makes_the_session_invalid_changes_nothing()
    {
        var repository = new InMemorySessionRepository();
        var service = TestSessions.Service(repository);
        var saved = await service.CreateAsync(UserId, Sample(), Day, PoolCourse.Scy, null, SessionSource.Ai, null, CancellationToken.None);
        var before = repository.Sessions[0].ContentJson;

        await Assert.ThrowsAsync<InvalidSessionException>(() =>
            service.EditAsync(UserId, saved.Id, [new SessionEdit(SessionEditOps.Replace, "0", new SetItem { Reps = 4 }, null)], CancellationToken.None));

        Assert.Equal(before, repository.Sessions[0].ContentJson);
        Assert.Equal(1700, repository.Facts.Sum(f => f.Distance));
    }

    [Fact]
    public async Task A_session_belongs_to_its_owner()
    {
        var repository = new InMemorySessionRepository();
        var service = TestSessions.Service(repository);
        var saved = await service.CreateAsync(UserId, Sample(), Day, PoolCourse.Scy, null, SessionSource.Ai, null, CancellationToken.None);
        var stranger = Guid.NewGuid();

        await Assert.ThrowsAsync<SessionNotFoundException>(() => service.GetAsync(stranger, saved.Id, CancellationToken.None));
        await Assert.ThrowsAsync<SessionNotFoundException>(() =>
            service.EditAsync(stranger, saved.Id, [new SessionEdit(SessionEditOps.Remove, "0", null, null)], CancellationToken.None));
        Assert.Empty(await service.ListAsync(stranger, null, null, 50, 0, CancellationToken.None));
    }

    [Fact]
    public async Task Sessions_list_newest_first_and_can_be_narrowed_by_date()
    {
        var repository = new InMemorySessionRepository();
        var service = TestSessions.Service(repository);
        foreach (var day in new[] { new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 20), new DateOnly(2026, 9, 10) })
        {
            await service.CreateAsync(UserId, Sample(), day, PoolCourse.Scy, null, SessionSource.Logged, null, CancellationToken.None);
        }

        var all = await service.ListAsync(UserId, null, null, 50, 0, CancellationToken.None);
        Assert.Equal(new[] { new DateOnly(2026, 9, 20), new DateOnly(2026, 9, 10), new DateOnly(2026, 9, 1) }, all.Select(s => s.PerformedOn));

        var narrowed = await service.ListAsync(UserId, new DateOnly(2026, 9, 5), new DateOnly(2026, 9, 15), 50, 0, CancellationToken.None);
        Assert.Equal(new DateOnly(2026, 9, 10), Assert.Single(narrowed).PerformedOn);
    }

    [Fact]
    public async Task Promoting_a_draft_saves_it_as_an_ai_session_and_is_repeatable()
    {
        var draftRepository = new InMemoryDraftRepository();
        var drafts = new SessionDraftService(draftRepository, TestSessions.Ingest(), new NoOpUnitOfWork());
        var sessionRepository = new InMemorySessionRepository();
        var promotion = new SessionPromotionService(drafts, TestSessions.Service(sessionRepository));
        var draft = await drafts.CreateAsync(UserId, Guid.NewGuid(), DraftTests.ModelSession, CancellationToken.None);

        var first = await promotion.SaveAsync(UserId, draft.Id, Day, PoolCourse.Lcm, "Board", CancellationToken.None);
        var again = await promotion.SaveAsync(UserId, draft.Id, Day, PoolCourse.Lcm, "Board", CancellationToken.None);

        Assert.Equal(first.Id, again.Id);
        var session = Assert.Single(sessionRepository.Sessions);
        Assert.Equal(SessionSource.Ai, session.Source);
        Assert.Equal(draft.Id, session.SourceDraftId);
        Assert.Equal(1700, session.TotalDistance);
        Assert.Equal(1, Assert.Single(draftRepository.Drafts).Version);
        await Assert.ThrowsAsync<DraftNotFoundException>(() =>
            promotion.SaveAsync(Guid.NewGuid(), draft.Id, Day, PoolCourse.Lcm, null, CancellationToken.None));
    }
}
