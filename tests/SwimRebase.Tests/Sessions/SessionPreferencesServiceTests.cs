using Sessions.Application.Services;
using Sessions.Domain.Calculations;
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

        await service.SetDefaultEffortAsync(UserId, 80, CancellationToken.None);

        Assert.Equal(80, await service.GetDefaultEffortAsync(UserId, CancellationToken.None));
    }

    [Fact]
    public async Task Choosing_again_updates_the_same_row()
    {
        var repository = new InMemoryPreferencesRepository();
        var service = Service(repository);

        await service.SetDefaultEffortAsync(UserId, 80, CancellationToken.None);
        await service.SetDefaultEffortAsync(UserId, 30, CancellationToken.None);

        var row = Assert.Single(repository.Rows);
        Assert.Equal(30, row.DefaultEffort);
    }

    [Fact]
    public async Task One_swimmers_default_does_not_change_anothers()
    {
        var service = Service(new InMemoryPreferencesRepository());

        await service.SetDefaultEffortAsync(UserId, 80, CancellationToken.None);

        Assert.Equal(SessionPreferences.BuiltInDefaultEffort, await service.GetDefaultEffortAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task A_default_effort_outside_the_scale_is_rejected_and_nothing_is_stored(int percent)
    {
        var repository = new InMemoryPreferencesRepository();

        await Assert.ThrowsAsync<InvalidSessionPreferencesException>(() => Service(repository).SetDefaultEffortAsync(UserId, percent, CancellationToken.None));

        Assert.Empty(repository.Rows);
    }

    private static readonly EffortBand[] TwoBands = [new("easy", "Easy", 1, 60), new("hard", "Hard", 61, 100)];

    [Fact]
    public async Task The_effort_scale_defaults_to_tens_and_a_chosen_one_is_stored()
    {
        var service = Service(new InMemoryPreferencesRepository());
        Assert.Equal("tens", (await service.GetAsync(UserId, CancellationToken.None)).EffortScale);

        var updated = await service.UpdateAsync(UserId, new SessionPreferencesUpdate(null, "ZONES", null), CancellationToken.None);

        Assert.Equal("zones", updated.EffortScale);
        Assert.Equal(SessionPreferences.BuiltInDefaultEffort, updated.DefaultEffort);
    }

    [Fact]
    public async Task A_custom_scale_is_saved_selected_and_can_be_removed()
    {
        var service = Service(new InMemoryPreferencesRepository());

        var saved = await service.UpdateAsync(UserId, new SessionPreferencesUpdate(null, "custom", TwoBands), CancellationToken.None);
        Assert.Equal("custom", saved.EffortScale);
        Assert.Equal(TwoBands, saved.CustomEffortScale);

        var removed = await service.UpdateAsync(UserId, new SessionPreferencesUpdate(null, null, []), CancellationToken.None);
        Assert.Equal("tens", removed.EffortScale);
        Assert.Null(removed.CustomEffortScale);
    }

    [Fact]
    public async Task Fields_that_are_not_sent_keep_their_value()
    {
        var service = Service(new InMemoryPreferencesRepository());
        await service.UpdateAsync(UserId, new SessionPreferencesUpdate(70, "zones", null), CancellationToken.None);

        var updated = await service.UpdateAsync(UserId, new SessionPreferencesUpdate(null, null, TwoBands), CancellationToken.None);

        Assert.Equal(70, updated.DefaultEffort);
        Assert.Equal("zones", updated.EffortScale);
        Assert.Equal(TwoBands, updated.CustomEffortScale);
    }

    [Fact]
    public async Task An_unknown_scale_or_custom_without_bands_is_rejected_and_nothing_is_stored()
    {
        var repository = new InMemoryPreferencesRepository();
        var service = Service(repository);

        await Assert.ThrowsAsync<InvalidSessionPreferencesException>(() => service.UpdateAsync(UserId, new SessionPreferencesUpdate(null, "nonsense", null), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidSessionPreferencesException>(() => service.UpdateAsync(UserId, new SessionPreferencesUpdate(null, "custom", null), CancellationToken.None));

        Assert.Empty(repository.Rows);
    }

    [Fact]
    public async Task Bands_that_leave_a_gap_or_do_not_cover_the_scale_are_rejected()
    {
        var service = Service(new InMemoryPreferencesRepository());
        EffortBand[][] invalid =
        [
            [new("a", "A", 1, 40), new("b", "B", 50, 100)],
            [new("a", "A", 1, 40), new("b", "B", 41, 90)],
            [new("a", "A", 5, 50), new("b", "B", 51, 100)],
            [new("a", "A", 1, 100)],
            [new("a", "A", 1, 50), new("A", "Again", 51, 100)],
            [new("a", "A", 1, 50), new("unspecified", "None", 51, 100)],
        ];

        foreach (var bands in invalid)
        {
            await Assert.ThrowsAsync<InvalidSessionPreferencesException>(() => service.UpdateAsync(UserId, new SessionPreferencesUpdate(null, null, bands), CancellationToken.None));
        }
    }

    [Fact]
    public async Task The_display_unit_is_metres_until_one_is_chosen()
    {
        var view = await Service(new InMemoryPreferencesRepository()).GetAsync(UserId, CancellationToken.None);

        Assert.Equal(DistanceUnit.Metres, view.DisplayUnit);
    }

    [Fact]
    public async Task A_chosen_display_unit_is_stored_and_leaves_the_other_settings_alone()
    {
        var service = Service(new InMemoryPreferencesRepository());
        await service.UpdateAsync(UserId, new SessionPreferencesUpdate(70, "zones", null), CancellationToken.None);

        var updated = await service.UpdateAsync(UserId, new SessionPreferencesUpdate(null, null, null, DistanceUnit.Yards), CancellationToken.None);

        Assert.Equal(DistanceUnit.Yards, updated.DisplayUnit);
        Assert.Equal(70, updated.DefaultEffort);
        Assert.Equal("zones", updated.EffortScale);
    }

    [Fact]
    public async Task Changing_other_settings_keeps_the_display_unit()
    {
        var service = Service(new InMemoryPreferencesRepository());
        await service.UpdateAsync(UserId, new SessionPreferencesUpdate(null, null, null, DistanceUnit.Yards), CancellationToken.None);

        var updated = await service.UpdateAsync(UserId, new SessionPreferencesUpdate(40, null, null), CancellationToken.None);

        Assert.Equal(DistanceUnit.Yards, updated.DisplayUnit);
    }

    [Fact]
    public async Task An_unknown_display_unit_is_rejected()
    {
        var service = Service(new InMemoryPreferencesRepository());

        await Assert.ThrowsAsync<InvalidSessionPreferencesException>(() =>
            service.UpdateAsync(UserId, new SessionPreferencesUpdate(null, null, null, (DistanceUnit)7), CancellationToken.None));
    }

    [Fact]
    public async Task The_descend_target_is_85_until_one_is_chosen()
    {
        var view = await Service(new InMemoryPreferencesRepository()).GetAsync(UserId, CancellationToken.None);

        Assert.Equal(SessionPreferences.BuiltInDescendTarget, view.DescendTarget);
        Assert.Equal(85, view.DescendTarget);
    }

    [Fact]
    public async Task A_chosen_descend_target_is_stored_and_other_changes_leave_it_alone()
    {
        var service = Service(new InMemoryPreferencesRepository());
        await service.UpdateAsync(UserId, new SessionPreferencesUpdate(null, null, null, null, 92), CancellationToken.None);

        var updated = await service.UpdateAsync(UserId, new SessionPreferencesUpdate(40, null, null), CancellationToken.None);

        Assert.Equal(92, updated.DescendTarget);
        Assert.Equal(40, updated.DefaultEffort);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task A_descend_target_outside_1_to_100_is_rejected(int percent)
    {
        await Assert.ThrowsAsync<InvalidSessionPreferencesException>(() =>
            Service(new InMemoryPreferencesRepository()).UpdateAsync(UserId, new SessionPreferencesUpdate(null, null, null, null, percent), CancellationToken.None));
    }

    [Fact]
    public async Task Descends_default_from_the_default_effort_to_the_descend_target()
    {
        var service = Service(new InMemoryPreferencesRepository());
        await service.UpdateAsync(UserId, new SessionPreferencesUpdate(45, null, null, null, 90), CancellationToken.None);

        var defaults = await service.DefaultsAsync(UserId, CancellationToken.None);

        Assert.Equal(new ProgressionDefaults(45, 90), defaults);
    }
}
