using Sessions.Application.Services;
using Sessions.Domain.Models;

namespace SwimRebase.Tests.Sessions;

public class SessionPreferencesServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static SessionPreferencesService Service(InMemoryPreferencesRepository repository) => new(repository, new NoOpUnitOfWork());

    [Fact]
    public async Task Until_one_is_chosen_the_default_effort_is_the_built_in_level()
    {
        var level = await Service(new InMemoryPreferencesRepository()).GetDefaultEffortAsync(UserId, CancellationToken.None);

        Assert.Equal(SessionPreferences.BuiltInDefaultEffort, level);
    }

    [Fact]
    public async Task A_chosen_default_effort_is_stored_and_read_back()
    {
        var repository = new InMemoryPreferencesRepository();
        var service = Service(repository);

        await service.SetDefaultEffortAsync(UserId, 8, CancellationToken.None);

        Assert.Equal(8, await service.GetDefaultEffortAsync(UserId, CancellationToken.None));
    }

    [Fact]
    public async Task Choosing_again_updates_the_same_row()
    {
        var repository = new InMemoryPreferencesRepository();
        var service = Service(repository);

        await service.SetDefaultEffortAsync(UserId, 8, CancellationToken.None);
        await service.SetDefaultEffortAsync(UserId, 3, CancellationToken.None);

        var row = Assert.Single(repository.Rows);
        Assert.Equal(3, row.DefaultEffort);
    }

    [Fact]
    public async Task One_swimmers_default_does_not_change_anothers()
    {
        var service = Service(new InMemoryPreferencesRepository());

        await service.SetDefaultEffortAsync(UserId, 8, CancellationToken.None);

        Assert.Equal(SessionPreferences.BuiltInDefaultEffort, await service.GetDefaultEffortAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public async Task A_default_effort_outside_the_scale_is_rejected_and_nothing_is_stored(int level)
    {
        var repository = new InMemoryPreferencesRepository();

        await Assert.ThrowsAsync<InvalidSessionPreferencesException>(() => Service(repository).SetDefaultEffortAsync(UserId, level, CancellationToken.None));

        Assert.Empty(repository.Rows);
    }
}
