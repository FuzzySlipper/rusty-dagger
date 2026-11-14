using System.Numerics;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// <see cref="DaggerfallBuildingEntrance"/> built without the session, over Society's stage (the pack, the site entered)
/// with its later domains' answers as plain inputs, and one door of each kind: a building's entrance and a plain door.
/// </summary>
public sealed class DaggerfallBuildingEntranceTests
{
    [Fact]
    public void A_buildings_door_reads_its_name_its_rule_now_and_its_discovery()
    {
        using SocietyStage stage = new();
        (DaggerfallSiteBuildingId building, DaggerfallSiteBuildingSource source) = stage.Site.Exterior!.Buildings.First();
        List<(int Faction, int Region)> asked = [];
        bool owned = false;
        DaggerfallBuildingEntrance entrance = Entrance(stage, hallStanding: (faction, region) => { asked.Add((faction, region)); return (false, false); },
            ownsHouse: (_, _) => owned);
        using EntityDirectory store = new();
        using DaggerfallDoorRuntime doors = new(store, RandomMinimum.Create(), [Door(building), Door(null, model: 4)], "town");

        DaggerfallBuildingDoor door = Assert.IsType<DaggerfallBuildingDoor>(entrance.Door(doors, Door(building).Id, stage.Site.Id));
        Assert.Equal(entrance.DisplayName(stage.Site.Id, source), door.Name);
        Assert.Equal(entrance.Name(doors, Door(building).Id, stage.Site.Id) ?? string.Empty, door.Name);
        Assert.Equal([(source.Source.FactionId, stage.Site.Id.Region)], asked);
        Assert.False(string.IsNullOrWhiteSpace(door.LockedMessage));
        owned = true;
        Assert.True(Assert.IsType<DaggerfallBuildingDoor>(entrance.Door(doors, Door(building).Id, stage.Site.Id)).Entry.Unlocked);

        // A plain door enters no building: it has no rule, name or entry.
        Assert.Null(entrance.Door(doors, Door(null, model: 4).Id, stage.Site.Id));
        Assert.Null(entrance.Name(doors, Door(null, model: 4).Id, stage.Site.Id));
        Assert.Null(entrance.Plan(doors, Door(null, model: 4).Id, null, false, DaggerfallWorldProfileIds.Exterior(stage.Site.Id), new([])));

        entrance.Discover(doors, Door(building).Id, stage.Site.Id);
        Assert.True(stage.Sites.IsBuildingDiscovered(stage.Site.Id, building));
    }

    [Fact]
    public void A_building_whose_interior_is_not_in_the_catalog_refuses_entry()
    {
        using SocietyStage stage = new();
        DaggerfallSiteBuildingId building = stage.Site.Exterior!.Buildings.Keys.First();
        DaggerfallBuildingEntrance entrance = Entrance(stage);
        using EntityDirectory store = new();
        using DaggerfallDoorRuntime doors = new(store, RandomMinimum.Create(), [Door(building)], "town");

        DaggerfallBuildingEntryPlan plan = Assert.IsType<DaggerfallBuildingEntryPlan>(
            entrance.Plan(doors, Door(building).Id, null, false, DaggerfallWorldProfileIds.Exterior(stage.Site.Id), new([])));
        Assert.Equal(new DaggerfallActivationOutcome(false, "This building has no interior to enter."), plan.Outcome);
        Assert.Null(plan.Greeting);
        Assert.Null(plan.Crime);
    }

    private static DaggerfallBuildingEntrance Entrance(SocietyStage stage,
        Func<int, int, (bool Member, bool HallAnytime)>? hallStanding = null,
        Func<DaggerfallSiteId, DaggerfallSiteBuildingSource, bool>? ownsHouse = null) =>
        new(stage.Sites, stage.Definitions, new DaggerfallWorldTime(DaggerfallCalendar.Start, 0d, 12d),
            new DaggerfallBuildingGreeting(stage.Definitions, RandomMinimum.Create()), () => DaggerfallTextContext.Empty,
            hallStanding ?? ((_, _) => (false, false)), ownsHouse ?? ((_, _) => false), () => false, _ => false, (_, _) => false);

    private static DaggerfallRdbDoorDefinition Door(DaggerfallSiteBuildingId? building, int model = 3) => new(
        new DaggerfallRdbDoorId("S0000007.RMB", 1, 1, model), new Vector3(2, 0, -4), new Vector3(0, 90, 0),
        new Vector3(-.5F, 0, -.1F), new Vector3(.5F, 2, .1F), DaggerfallDoorKind.Normal, 4)
    {
        LockSurface = building is null ? DaggerfallLockInteractionSurface.Interior : DaggerfallLockInteractionSurface.Exterior,
        ExteriorBuilding = building,
    };
}
