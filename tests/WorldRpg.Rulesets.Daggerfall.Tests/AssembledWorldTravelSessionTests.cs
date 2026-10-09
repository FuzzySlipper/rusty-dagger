using System.Numerics;
using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Entities;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Travel;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The whole catalog is reachable: a journey arrives at a location no pack publishes, its buildings and its
/// dungeon are entered through their source doors and left again, and saves taken before, at and after the
/// arrival restore there. Every place is the profile its id names, assembled from the per-block publication.
/// </summary>
public sealed class AssembledWorldTravelSessionTests
{
    private static readonly SharedFixture<AssembledWorld> Shared = new(() => new AssembledWorld());

    /// <summary>
    /// A journey to an unpublished city lands at its start marker nearest the side it arrives from; one of its
    /// buildings is entered through its door, which the block states, and left through the exit its interior
    /// states. Saves before the journey, at the arrival and inside the building each restore where they were taken.
    /// </summary>
    [Fact]
    public void Travel_to_an_unpublished_city_enters_a_building_and_leaves_it_across_saves()
    {
        AssembledWorld world = Shared.Value;
        DaggerfallSiteRecord city = world.Unpublished(record => record.Kind == DaggerfallSiteKind.TownCity && record.Exterior is not null);
        DaggerfallWorldProfileKey exterior = DaggerfallWorldProfileIds.Exterior(city.Id);

        // The building entered: one whose door starts unlocked and whose interior the catalog places.
        DaggerfallRdbDoorDefinition door = world.Profiles.Require(exterior).Doors.First(candidate => candidate.StartingLockValue == 0
            && candidate.ExteriorBuilding is { } building && world.Profiles.Contains(DaggerfallWorldProfileIds.Interior(city.Id, building)));
        DaggerfallSiteBuildingId entered = door.ExteriorBuilding!.Value;
        DaggerfallWorldProfileKey interior = DaggerfallWorldProfileIds.Interior(city.Id, entered);

        using AssembledWorldRun start = world.Start(city.Id, entered);
        RulesetSavePayload beforeJourney = start.Session.CaptureSave();
        using AssembledWorldRun run = world.Restore(beforeJourney, city.Id, entered);
        run.Travel(city);
        Assert.Equal(exterior, run.Session.Sites.ActiveProfile);
        DaggerfallSiteProfile town = world.Profiles.Require(exterior);
        WorldPoint landed = run.Session.Sites.ExteriorSitePosition(run.Session.State.PlayerControl.Position!.Value);
        // A city receives the traveller at one of its own start markers.
        Assert.Contains(town.Anchors.Values, anchor => anchor.Id.StartsWith(DaggerfallLocationAssembly.StartMarkerAnchorPrefix, StringComparison.Ordinal)
            && Vector3.Distance(anchor.Position.ToVector(), landed.ToVector()) < 1e-3F);
        // Its neighbours in the exterior window stream from their blocks too.
        Assert.All(run.Session.Sites.ResidentExteriorProfiles, key => Assert.Equal(DaggerfallWorldProfileIds.Exterior(key.Site), key));

        RulesetSavePayload atArrival = run.Session.CaptureSave();
        using AssembledWorldRun arrived = world.Restore(atArrival, city.Id, entered);
        Assert.Equal(exterior, arrived.Session.Sites.ActiveProfile);
        DaggerfallPlayerSave saved = DaggerfallSavePayload.Read(atArrival).Player, again = DaggerfallSavePayload.Read(arrived.Session.CaptureSave()).Player;
        Assert.Equal((saved.X, saved.Y, saved.Z, saved.YawRadians), (again.X, again.Y, again.Z, again.YawRadians));

        // Enter the building through its door.
        DaggerfallDoorView live = arrived.Session.Sites.Projection.Doors.Read(door.Id);
        Assert.False(live.IsLocked);
        WorldPoint outside = arrived.Use(live.Pose.Translation, live.Entity);
        Assert.Equal(interior, arrived.Session.Sites.ActiveProfile);
        Assert.Equal(exterior, arrived.Session.Sites.ReturnProfile);
        RulesetSavePayload inside = arrived.Session.CaptureSave();

        using AssembledWorldRun resumed = world.Restore(inside, city.Id, entered);
        Assert.Equal(interior, resumed.Session.Sites.ActiveProfile);
        // Every building door plane inside leads out.
        Assert.All(resumed.Session.Sites.Projection.Portals.All, value => Assert.Equal(exterior.LogicalId, value.Portal.DestinationLogicalProfile));
        (DaggerfallSitePortal Portal, DurableIdentityReference _, EntityId Entity) exit = resumed.Session.Sites.Projection.Portals.All.First();
        resumed.Use(exit.Portal, exit.Entity);
        // Out through the door the player came in by, where they stood to use it.
        Assert.Equal(exterior, resumed.Session.Sites.ActiveProfile);
        Assert.True(Vector3.Distance(outside.ToVector(), resumed.Session.Sites.ExteriorSitePosition(resumed.Session.State.PlayerControl.Position!.Value).ToVector()) < 1e-2F);
    }

    /// <summary>
    /// A journey to an unpublished dungeon lands outside its location; its source dungeon entrance leads in to
    /// the dungeon's start, facing away from its exit, and its source exit leads back out. Without an entrance to
    /// return through, leaving lands in front of the lowest entrance, facing away from it. A save inside the
    /// dungeon restores there.
    /// </summary>
    [Fact]
    public void Travel_to_an_unpublished_dungeon_enters_it_and_leaves_through_its_exit()
    {
        AssembledWorld world = Shared.Value;
        DaggerfallSiteRecord site = world.Unpublished(record => record.Kind is DaggerfallSiteKind.DungeonLabyrinth or DaggerfallSiteKind.DungeonKeep or DaggerfallSiteKind.DungeonRuin
            && record.Exterior is not null && record.DungeonBlocks.Count != 0
            && world.Profiles.Require(DaggerfallWorldProfileIds.Exterior(record.Id)).Portals.Count != 0);
        DaggerfallWorldProfileKey exterior = DaggerfallWorldProfileIds.Exterior(site.Id), dungeon = DaggerfallWorldProfileIds.Dungeon(site.Id);

        using AssembledWorldRun run = world.Start(site.Id);
        run.Travel(site);
        Assert.Equal(exterior, run.Session.Sites.ActiveProfile);
        DaggerfallSiteProfile outside = world.Profiles.Require(exterior);
        // A dungeon location is no city: the traveller stands a tenth of a block outside its edge, facing in.
        DaggerfallSiteAnchor[] sides = [.. Enum.GetValues<DaggerfallArrivalSide>().Select(side => DaggerfallLocationArrival.Landing(outside, site, side))];
        WorldPoint landed = run.Session.Sites.ExteriorSitePosition(run.Session.State.PlayerControl.Position!.Value);
        Assert.Contains(sides, side => Vector3.Distance(side.Position.ToVector(), landed.ToVector()) < 1e-2F);

        DaggerfallSitePortal entrance = outside.Portals.First(portal => portal.DestinationLogicalProfile == dungeon.LogicalId);
        (DaggerfallSitePortal Portal, DurableIdentityReference _, EntityId Entity) live = run.Session.Sites.Projection.Portals.All.Single(value => value.Portal.Id == entrance.Id);
        WorldPoint stood = run.Use(live.Portal, live.Entity);
        Assert.Equal(dungeon, run.Session.Sites.ActiveProfile);
        DaggerfallSiteProfile inside = world.Profiles.Require(dungeon);
        DaggerfallSiteAnchor start = inside.RequireAnchor(DaggerfallLocationAssembly.StartAnchor);
        Assert.Equal(start.Position, run.Session.State.PlayerControl.Position);
        Assert.Equal(start.YawRadians, run.Session.State.PlayerControl.YawRadians);
        Assert.NotEmpty(inside.Portals);
        Assert.All(inside.Portals, portal => Assert.Equal(exterior.LogicalId, portal.DestinationLogicalProfile));

        using AssembledWorldRun resumed = world.Restore(run.Session.CaptureSave(), site.Id);
        Assert.Equal(dungeon, resumed.Session.Sites.ActiveProfile);
        Assert.Equal(exterior, resumed.Session.Sites.ReturnProfile);
        (DaggerfallSitePortal Portal, DurableIdentityReference _, EntityId Entity) exit = resumed.Session.Sites.Projection.Portals.All.First();
        resumed.Use(exit.Portal, exit.Entity);
        Assert.Equal(exterior, resumed.Session.Sites.ActiveProfile);
        WorldPoint returned = resumed.Session.Sites.ExteriorSitePosition(resumed.Session.State.PlayerControl.Position!.Value);
        Assert.True(Vector3.Distance(stood.ToVector(), returned.ToVector()) < 1e-2F, $"{stood} vs {returned}");

        // Brought inside rather than walking in, the player has no entrance to return through.
        Assert.True(resumed.Session.TryTransitionTo(dungeon, DaggerfallLocationAssembly.StartAnchor));
        resumed.Session.Sites.ClearReturnDestination();
        exit = resumed.Session.Sites.Projection.Portals.All.First();
        resumed.Use(exit.Portal, exit.Entity);
        DaggerfallSiteAnchor landing = outside.RequireAnchor(DaggerfallLocationAssembly.DungeonEntranceAnchor);
        Assert.Equal(exterior, resumed.Session.Sites.ActiveProfile);
        Assert.True(Vector3.Distance(landing.Position.ToVector(), resumed.Session.Sites.ExteriorSitePosition(resumed.Session.State.PlayerControl.Position!.Value).ToVector()) < 1e-2F);
        Assert.Equal(landing.YawRadians, resumed.Session.State.PlayerControl.YawRadians);
    }

    /// <summary>
    /// A walled city's gates are drawn and collide as their open model by day and their closed model at night,
    /// swapping as the calendar crosses dusk and dawn (DaggerfallCityGate); the gate models are not part of the
    /// static meshes, and the state is not saved but follows the restored calendar.
    /// </summary>
    [Fact]
    public void A_walled_city_s_gates_close_at_dusk_and_open_at_dawn()
    {
        AssembledWorld world = Shared.Value;
        DaggerfallSiteRecord city = world.Unpublished(record => record.Exterior is not null
            && world.Profiles.Require(DaggerfallWorldProfileIds.Exterior(record.Id)).CityGates.Count != 0);
        DaggerfallWorldProfileKey exterior = DaggerfallWorldProfileIds.Exterior(city.Id);
        DaggerfallSiteProfile walled = world.Profiles.Require(exterior);
        Assert.All(walled.CityGates, gate => Assert.Equal((true, true),
            (gate.Open.Visual.Path.EndsWith("/mesh-446.rstatmsh", StringComparison.Ordinal), gate.Closed.Visual.Path.EndsWith("/mesh-447.rstatmsh", StringComparison.Ordinal))));
        Assert.DoesNotContain(walled.Geometry.Meshes, mesh => walled.CityGates.Any(gate => mesh.Pose == gate.Pose && mesh.Path == gate.Open.Visual.Path));

        using AssembledWorldRun run = world.Start(city.Id);
        Assert.True(run.Session.TryTransitionTo(exterior, DaggerfallLocationAssembly.StartAnchor));
        bool day = Calendar(run.Session).IsDay;
        DaggerfallCityGates gates = run.Session.Sites.Projection.CityGates;
        Assert.Equal(day, gates.Open);
        Assert.Equal(walled.CityGates.Count, gates.Visuals.Count());
        Dictionary<ulong, ulong> before = new(run.Spatial.ResidentCollisionInstances);
        Assert.Equal(walled.CityGates.Count, gates.CharacterEnvironment().MeshInstances.Length);

        // Cross the next dusk or dawn: every gate swaps to its other model and collision.
        run.Session.AdvanceElapsedTime(12 * 60 * 60);
        run.Step();
        Assert.Equal(!day, Calendar(run.Session).IsDay);
        Assert.Equal(!day, run.Session.Sites.Projection.CityGates.Open);
        IReadOnlyDictionary<ulong, ulong> after = run.Spatial.ResidentCollisionInstances;
        Assert.Equal(walled.CityGates.Count, before.Keys.Except(after.Keys).Count());
        Assert.Equal(walled.CityGates.Count, after.Keys.Except(before.Keys).Count());
        Assert.All(after.Keys.Except(before.Keys), instance => Assert.DoesNotContain(after[instance], before.Values));

        // The state follows the restored calendar rather than a save.
        using AssembledWorldRun restored = world.Restore(run.Session.CaptureSave(), city.Id);
        Assert.Equal(!day, restored.Session.Sites.Projection.CityGates.Open);
    }

    private static DaggerfallCalendar Calendar(DaggerfallSession session)
    {
        DaggerfallCalendarSave saved = DaggerfallSavePayload.Read(session.CaptureSave()).Calendar;
        return new DaggerfallCalendar(saved.Year, saved.Month, saved.Day, saved.Hour, saved.Minute, saved.Second);
    }
}
