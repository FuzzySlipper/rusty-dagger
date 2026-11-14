using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>The building greeting built without the session: the pack's text, a keyed random and a placed building.</summary>
public sealed class DaggerfallBuildingGreetingTests
{
    private static readonly DaggerfallRdbDoorId Door = new("BLOCK.RMB", 1, 2, 3);
    private const int GeneralStore = 9;
    private const int GuildHall = 17;

    [Fact]
    public void A_shop_greets_by_its_quality_and_holds_the_entry_until_it_is_read()
    {
        DaggerfallBuildingGreeting greeting = new(TestPayload.Definitions, RandomMinimum.Create());

        DaggerfallDungeonTextProjection shabby = Assert.IsType<DaggerfallDungeonTextProjection>(
            greeting.Present(Door, null, Building(GeneralStore, quality: 2), closed: false, owned: false, "town", () => DaggerfallTextContext.Empty));
        Assert.Equal(270, shabby.TextId);
        Assert.Equal(DaggerfallDungeonTextActionKind.BuildingGreeting, shabby.Kind);
        Assert.Equal($"building-entry/{Door}", shabby.ActionId);
        Assert.False(string.IsNullOrWhiteSpace(shabby.Text));
        Assert.Equal(1UL, shabby.Revision);

        // A forced or closed shop still greets; the finest shop greets with its own resource.
        DaggerfallDungeonTextProjection fine = Assert.IsType<DaggerfallDungeonTextProjection>(
            greeting.Present(Door, DaggerfallActivationMode.Bash, Building(GeneralStore, quality: 20), closed: true, owned: false, "town", () => DaggerfallTextContext.Empty));
        Assert.Equal(266, fine.TextId);
        Assert.Equal(2UL, fine.Revision);

        Assert.Equal((Door, (DaggerfallActivationMode?)DaggerfallActivationMode.Bash), greeting.TakeHeldEntry());
        Assert.Null(greeting.TakeHeldEntry());
    }

    [Fact]
    public void A_guild_hall_greets_only_an_open_unforced_visit_and_draws_its_line_by_profile_door_and_count()
    {
        KeyedRandomFake random = KeyedRandomFake.Create(0);
        DaggerfallBuildingGreeting greeting = new(TestPayload.Definitions, random.Service);
        DaggerfallSiteBuildingSource hall = Building(GuildHall, quality: 10);

        Assert.Null(greeting.Present(Door, null, hall, closed: true, owned: false, "town", () => DaggerfallTextContext.Empty));
        Assert.Null(greeting.Present(Door, null, hall, closed: false, owned: true, "town", () => DaggerfallTextContext.Empty));
        Assert.Null(greeting.Present(Door, DaggerfallActivationMode.Bash, hall, closed: false, owned: false, "town", () => DaggerfallTextContext.Empty));
        Assert.Null(greeting.Present(Door, null, Building(GuildHall, quality: 10, faction: 42), closed: false, owned: false, "town", () => DaggerfallTextContext.Empty));
        Assert.Null(greeting.TakeHeldEntry());

        DaggerfallDungeonTextProjection open = Assert.IsType<DaggerfallDungeonTextProjection>(
            greeting.Present(Door, null, hall, closed: false, owned: false, "town", () => DaggerfallTextContext.Empty));
        Assert.Equal(256, open.TextId);
        Assert.Equal($"town:{Door}:0", Assert.Single(random.Requests).Key);

        // Leaving the site drops the held entry.
        greeting.Release();
        Assert.Null(greeting.TakeHeldEntry());
    }

    private static DaggerfallSiteBuildingSource Building(int type, int quality, int faction = 0) =>
        new(new DaggerfallSiteBuildingId(0, 0, 1), new DaggerfallRmbBuildingSource(new DaggerfallRmbBuildingId("BLOCK.RMB", 1), type, faction, 0), quality);
}
