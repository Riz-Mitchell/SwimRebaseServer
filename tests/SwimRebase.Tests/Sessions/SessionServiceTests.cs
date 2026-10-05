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
        Assert.Equal(20, first.Parts[0].Intensity!.Percent);
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
    public async Task A_session_without_a_title_is_named_from_what_was_swum()
    {
        var saved = await TestSessions.Service(new InMemorySessionRepository())
            .CreateAsync(UserId, Sample(), Day, PoolCourse.Scy, "  ", SessionSource.Logged, null, CancellationToken.None);

        Assert.Equal("Lane 2 - Freestyle and kick - 1,700 yd", saved.Title);
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
        Assert.Equal(200, repository.Facts.Where(f => f.Mode == SwimMode.Kick && f.Intensity == 80).Sum(f => f.Distance));
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

    private static async Task<SessionService> ServiceWithSessions(InMemorySessionRepository repository, int count)
    {
        var service = TestSessions.Service(repository);
        for (var i = 0; i < count; i++)
        {
            await service.CreateAsync(UserId, Sample(), Day.AddDays(i), PoolCourse.Scm, null, SessionSource.Logged, null, CancellationToken.None);
        }

        return service;
    }

    [Fact]
    public async Task Listing_with_content_returns_each_sessions_parsed_tree_newest_first()
    {
        var repository = new InMemorySessionRepository();
        var service = await ServiceWithSessions(repository, 3);

        var listed = await service.ListWithContentAsync(UserId, null, null, 15, 0, CancellationToken.None);

        Assert.Equal(new[] { Day.AddDays(2), Day.AddDays(1), Day }, listed.Select(s => s.Session.PerformedOn));
        Assert.All(listed, s => Assert.Equal(3, s.Content.Sets.Count));
        Assert.All(listed, s => Assert.Equal(1700, s.Session.TotalDistance));
        Assert.All(listed, s => Assert.False(string.IsNullOrEmpty(Assert.IsType<SetItem>(s.Content.Sets[0]).Id)));
    }

    [Fact]
    public async Task Listing_with_content_returns_at_most_the_content_limit()
    {
        var service = await ServiceWithSessions(new InMemorySessionRepository(), SessionService.MaxContentLimit + 2);

        var listed = await service.ListWithContentAsync(UserId, null, null, 100, 0, CancellationToken.None);

        Assert.Equal(SessionService.MaxContentLimit, listed.Count);
    }

    [Fact]
    public async Task Listing_with_content_treats_a_negative_offset_as_the_start_and_pages_with_a_positive_one()
    {
        var service = await ServiceWithSessions(new InMemorySessionRepository(), 3);

        var first = await service.ListWithContentAsync(UserId, null, null, 2, -5, CancellationToken.None);
        var next = await service.ListWithContentAsync(UserId, null, null, 2, 2, CancellationToken.None);

        Assert.Equal(2, first.Count);
        Assert.Equal(Day.AddDays(2), first[0].Session.PerformedOn);
        Assert.Equal(Day, Assert.Single(next).Session.PerformedOn);
    }

    [Fact]
    public async Task Listing_with_content_respects_the_date_range()
    {
        var service = await ServiceWithSessions(new InMemorySessionRepository(), 5);

        var listed = await service.ListWithContentAsync(UserId, Day.AddDays(1), Day.AddDays(3), 15, 0, CancellationToken.None);

        Assert.Equal(new[] { Day.AddDays(3), Day.AddDays(2), Day.AddDays(1) }, listed.Select(s => s.Session.PerformedOn));
    }

    [Fact]
    public async Task Listing_with_content_only_returns_the_callers_sessions()
    {
        var repository = new InMemorySessionRepository();
        await ServiceWithSessions(repository, 2);

        var listed = await TestSessions.Service(repository).ListWithContentAsync(Guid.NewGuid(), null, null, 15, 0, CancellationToken.None);

        Assert.Empty(listed);
    }

    [Fact]
    public async Task An_explicit_title_wins_then_the_converters_then_one_built_from_the_session()
    {
        var titled = Sample() with { Title = "Fly kick ladder" };
        var service = TestSessions.Service(new InMemorySessionRepository());

        var explicitTitle = await service.CreateAsync(UserId, titled, Day, PoolCourse.Scy, "Tuesday squad", SessionSource.Ai, null, CancellationToken.None);
        var converters = await service.CreateAsync(UserId, titled, Day, PoolCourse.Scy, null, SessionSource.Ai, null, CancellationToken.None);
        var built = await service.CreateAsync(UserId, Sample(), Day, PoolCourse.Scy, null, SessionSource.Ai, null, CancellationToken.None);

        Assert.Equal("Tuesday squad", explicitTitle.Title);
        Assert.Equal("Fly kick ladder", converters.Title);
        Assert.Equal("Lane 2 - Freestyle and kick - 1,700 yd", built.Title);
    }

    [Fact]
    public async Task The_title_lives_in_its_column_and_not_in_the_stored_tree_and_is_clamped()
    {
        var repository = new InMemorySessionRepository();
        var service = TestSessions.Service(repository);

        await service.CreateAsync(UserId, Sample() with { Title = new string('x', 300) }, Day, PoolCourse.Scy, null, SessionSource.Ai, null, CancellationToken.None);

        var stored = Assert.Single(repository.Sessions);
        Assert.Equal(SessionTitles.MaxLength, stored.Title.Length);
        Assert.Null(SessionJson.Deserialize<SwimSession>(stored.ContentJson).Title);
    }

    [Fact]
    public async Task Recent_titles_are_newest_first_without_repeats_and_only_the_swimmers_own()
    {
        var service = TestSessions.Service(new InMemorySessionRepository());
        foreach (var (title, day) in new[] { ("Sprint starts", 1), ("Threshold 200s", 2), ("Sprint starts", 3) })
        {
            await service.CreateAsync(UserId, Sample(), new DateOnly(2026, 9, day), PoolCourse.Scy, title, SessionSource.Ai, null, CancellationToken.None);
        }

        await service.CreateAsync(Guid.NewGuid(), Sample(), Day, PoolCourse.Scy, "Someone else's", SessionSource.Ai, null, CancellationToken.None);

        Assert.Equal(["Sprint starts", "Threshold 200s"], await service.RecentTitlesAsync(UserId, 10, CancellationToken.None));
    }
}
