using WorldRpg.Kit;
using System.Numerics;
using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Debugging;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using WorldRpg.Rulesets.Daggerfall.World;
using WorldRpg.Rulesets.Daggerfall.Modules.Behavior;
using Xunit;

using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The developer world commands move the player through the same site and movement owners ordinary play uses: a
/// teleport stands the body where it fits, travel lands at a place as guild teleport does, a building is entered as its
/// door enters it, and goto stands within reach of a named person. Each result is what the next save holds.
/// </summary>
public sealed class DeveloperWorldCommandsTests
{
    private static readonly SharedFixture<AssembledWorld> Shared = new(() => new AssembledWorld());
    private static readonly DaggerfallSiteId Charing = new(17, 4);
    private static readonly DaggerfallSiteBuildingId TempleOfKynareth = new(3, 1, 13);

    [Fact]
    public void A_teleport_stands_the_player_where_the_body_fits_refuses_an_occupied_spot_unless_forced_and_is_saved()
    {
        AssembledWorld world = Shared.Value;
        using AssembledWorldRun run = world.Start(PrivateersHold);
        DaggerfallSession session = run.Session;
        Vector3 start = Position(session);
        // With nothing below, a spot is refused unless forced: an interior is a box in empty space.
        DebugCommandResult floorless = session.Commands<IDeveloperPlacementCommands>().Teleport(start.X + 3, start.Y - 1, start.Z - 2, force: false);
        Assert.False(floorless.Succeeded);
        Assert.Contains("no floor", floorless.Message, StringComparison.Ordinal);
        Assert.Equal(start, Position(session));
        // From here on the fake world is level ground at the player's starting floor.
        float ground = Json(session.Commands<IDeveloperPlacementCommands>().Where()).GetProperty("feet").GetProperty("y").GetSingle();
        run.Spatial.FloorHit = request => request.Direction.Y < -.99f && request.Origin.Y > ground
            ? default(SpatialHit) with { Present = true, Converged = true, Point = request.Origin with { Y = ground }, Normal = Vector3.UnitY }
            : default;

        DebugCommandResult moved = session.Commands<IDeveloperPlacementCommands>().Teleport(start.X + 3, ground, start.Z - 2, force: false);
        Assert.True(moved.Succeeded, moved.Message);
        Assert.Equal(new Vector2(start.X + 3, start.Z - 2), Planar(Position(session)), new PlanarComparer());
        Assert.Equal(session.Sites.ActiveProfile.LogicalId, Json(moved).GetProperty("profile").GetString());

        // A spot the standing body would overlap is refused with the clearance facts, and the player stays put.
        Vector3 before = Position(session);
        run.Spatial.OverlapHit = _ => default(SpatialHit) with { Present = true, Converged = true };
        DebugCommandResult refused = session.Commands<IDeveloperPlacementCommands>().Teleport(before.X + 5, before.Y, before.Z, force: false);
        Assert.False(refused.Succeeded);
        Assert.Contains("overlap", refused.Message, StringComparison.Ordinal);
        Assert.Contains("targetStatus", refused.Message, StringComparison.Ordinal);
        Assert.Equal(before, Position(session));
        Assert.False(session.Commands<IDeveloperPlacementCommands>().Forward(4).Succeeded);
        DebugCommandResult forced = session.Commands<IDeveloperPlacementCommands>().Teleport(before.X + 5, before.Y, before.Z, force: true);
        Assert.True(forced.Succeeded, forced.Message);
        Assert.Equal(new Vector2(before.X + 5, before.Z), Planar(Position(session)), new PlanarComparer());
        run.Spatial.OverlapHit = _ => default;

        // world.spawn brings enemies onto the floor near the player, in front of them and facing them, hostile; a name
        // no definition has is refused, and so is a place with no clear floor.
        session.State.PlayerControl.YawRadians = 0;
        Vector3 eye = Position(session);
        DebugCommandResult spawned = session.Commands<IDeveloperPlacementCommands>().Spawn("encounter-warrior", 2, 5);
        Assert.True(spawned.Succeeded, spawned.Message);
        long[] warriors = [.. session.DynamicActors.Where(actor => actor.Value.Value == "encounter-warrior").Select(actor => actor.Key)];
        Assert.Equal(2, warriors.Length);
        AssertBroughtNear(session, warriors, eye, 5);
        Assert.False(session.Commands<IDeveloperPlacementCommands>().Spawn("no-such-actor", 1, 3).Succeeded);
        run.Spatial.OverlapHit = _ => default(SpatialHit) with { Present = true, Converged = true };
        DebugCommandResult crowded = session.Commands<IDeveloperPlacementCommands>().Spawn("encounter-warrior", 1, 5);
        Assert.False(crowded.Succeeded);
        Assert.StartsWith("No clear floor within 5 m", crowded.Message, StringComparison.Ordinal);
        run.Spatial.OverlapHit = _ => default;
        foreach (long warrior in warriors) session.RetireActor(warrior);

        // Relative moves: an offset, and a distance along the facing.
        Vector3 here = Position(session);
        Assert.True(session.Commands<IDeveloperPlacementCommands>().Move(-1, 0, 1).Succeeded);
        Assert.Equal(new Vector2(here.X - 1, here.Z + 1), Planar(Position(session)), new PlanarComparer());
        session.State.PlayerControl.YawRadians = 0;
        here = Position(session);
        Assert.True(session.Commands<IDeveloperPlacementCommands>().Forward(2).Succeeded);
        Assert.Equal(new Vector2(here.X, here.Z - 2), Planar(Position(session)), new PlanarComparer());

        // Repeated moves keep the body standing on the same ground: the feet given are its floor, not its centre.
        float height = Position(session).Y;
        Assert.True(session.Commands<IDeveloperPlacementCommands>().Move(1, 0, 0).Succeeded);
        Assert.True(session.Commands<IDeveloperPlacementCommands>().Move(1, 0, 0).Succeeded);
        Assert.Equal(height, Position(session).Y, .01f);

        // The saved game holds the moved player.
        Vector3 saved = Position(session);
        using AssembledWorldRun restored = world.Restore(session.CaptureSave(), PrivateersHold);
        Assert.Equal(Planar(saved), Planar(Position(restored.Session)), new PlanarComparer());
    }

    [Fact]
    public void Travel_lands_at_a_named_place_enter_lands_inside_its_building_and_goto_stands_within_reach_of_its_trainer()
    {
        AssembledWorld world = Shared.Value;
        using AssembledWorldRun run = world.Start(Charing, TempleOfKynareth);
        DaggerfallSession session = run.Session;
        // The fake world has floor a metre below wherever a straight-down ray asks, and nothing else in the way.
        run.Spatial.FloorHit = request => request.Direction.Y < -.99f
            ? default(SpatialHit) with { Present = true, Converged = true, Point = request.Origin - Vector3.UnitY, Normal = Vector3.UnitY }
            : default;

        Assert.False(session.Commands<IDeveloperPlacementCommands>().Travel("Nowhere At All").Succeeded);
        DebugCommandResult arrived = session.Commands<IDeveloperPlacementCommands>().Travel("Charing");
        Assert.True(arrived.Succeeded, arrived.Message);
        Assert.Equal(DaggerfallWorldProfileIds.Exterior(Charing), session.Sites.ActiveProfile);
        Assert.Equal("Charing", Json(arrived).GetProperty("place").GetString());

        // Outdoors too, world.spawn brings an enemy onto the ground near the player.
        Vector3 street = Position(session);
        DebugCommandResult spawned = session.Commands<IDeveloperPlacementCommands>().Spawn("encounter-barbarian", 1, 4);
        Assert.True(spawned.Succeeded, spawned.Message);
        long barbarian = Assert.Single(session.DynamicActors, actor => actor.Value.Value == "encounter-barbarian").Key;
        AssertBroughtNear(session, [barbarian], street, 4);
        session.RetireActor(barbarian);

        // A closed door is collision movement meets, though the admitted scene does not hold it: a spot it
        // fills is refused unless forced.
        DaggerfallDoorView door = session.Sites.Projection.Doors.All.First(value => session.Sites.Projection.Doors.ExteriorBuildingOf(value.Id) == TempleOfKynareth);
        run.Spatial.OverlapHit = request => request.Entities.ToArray().Any(collider => collider.Entity == door.Entity.Value)
            ? default(SpatialHit) with { Present = true, Converged = true, Kind = SpatialHitKind.Entity, Entity = door.Entity.Value }
            : default;
        Vector3 outside = Position(session), doorway = door.Pose.Translation;
        DebugCommandResult blocked = session.Commands<IDeveloperPlacementCommands>().Teleport(doorway.X, doorway.Y, doorway.Z, force: false);
        Assert.False(blocked.Succeeded);
        Assert.Contains("overlap", blocked.Message, StringComparison.Ordinal);
        Assert.Equal(outside, Position(session));
        Assert.True(session.Commands<IDeveloperPlacementCommands>().Teleport(doorway.X, doorway.Y, doorway.Z, force: true).Succeeded);
        run.Spatial.OverlapHit = _ => default;

        // With no clear spot at the door, world.enter refuses rather than entering from where the player stands,
        // which leaving would return them to.
        run.Spatial.OverlapHit = _ => default(SpatialHit) with { Present = true, Converged = true, Kind = SpatialHitKind.StaticMesh };
        Vector3 standing = Position(session);
        DebugCommandResult crowded = session.Commands<IDeveloperPlacementCommands>().Enter("Charing", "Temple of Kynareth");
        Assert.False(crowded.Succeeded);
        Assert.Contains("No clear spot", crowded.Message, StringComparison.Ordinal);
        Assert.Equal(DaggerfallWorldProfileIds.Exterior(Charing), session.Sites.ActiveProfile);
        Assert.Equal(standing, Position(session));
        run.Spatial.OverlapHit = _ => default;

        // world.find names the building and how to enter it before it is entered.
        JsonElement found = Json(session.Commands<IDeveloperPlacementCommands>().Find("training"));
        JsonElement temple = found.GetProperty("buildings").EnumerateArray().Single(value => value.GetProperty("id").GetString() == TempleOfKynareth.ToString());
        Assert.Equal("The Temple of Kynareth", temple.GetProperty("building").GetString());

        DebugCommandResult entered = session.Commands<IDeveloperPlacementCommands>().Enter("Charing", "Temple of Kynareth");
        Assert.True(entered.Succeeded, entered.Message);
        Assert.Equal(DaggerfallWorldProfileIds.Interior(Charing, TempleOfKynareth), session.Sites.ActiveProfile);
        Assert.Equal("The Temple of Kynareth", Json(entered).GetProperty("building").GetString());

        // The temple's trainer is loaded; goto stands the player within reach of them, facing them.
        DaggerfallNpc trainer = session.State.Npcs.All.Single(npc => npc.Presence == DaggerfallNpcPresence.Active
            && npc.Services.Contains("training") && session.State.Actors.TryGet(npc.DurableId, out _));
        Assert.Contains(Json(session.Commands<IDeveloperPlacementCommands>().Find("training")).GetProperty("loaded").EnumerateArray(), value => value.GetProperty("id").GetString() == $"Actor:{trainer.DurableId}");
        DebugCommandResult reached = session.Commands<IDeveloperPlacementCommands>().GoTo($"Actor:{trainer.DurableId}");
        Assert.True(reached.Succeeded, reached.Message);
        Vector3 player = Position(session), them = session.State.Actors.Get(trainer.DurableId).Position.ToVector();
        float apart = Vector2.Distance(Planar(player), Planar(them));
        Assert.InRange(apart, 1.2f, 1.8f);
        Vector3 facing = WorldRpg.Kit.Actors.ActorHeading.Forward(session.State.PlayerControl.YawRadians);
        Assert.True(Vector2.Dot(Vector2.Normalize(Planar(them) - Planar(player)), new(facing.X, facing.Z)) > .99f);

        // From there the trainer's person screen offers training.
        DaggerfallActivationTarget target = Assert.Single(session.Dialogue.NpcTargets(), value => value.Identity.Value == checked((ulong)trainer.DurableId));
        Assert.True(session.Dialogue.ActivateNpc(new(DaggerfallActivationMode.Talk, target)).Applied);
        Assert.Contains(session.ActivationPanel.View.Dialogue!.Services, service => service.Id == DaggerfallPersonServices.Training);

        // While the conversation holds the world, movement is refused and nothing moves; the conversation stays open.
        Vector3 talking = Position(session);
        DebugCommandResult held = session.Commands<IDeveloperPlacementCommands>().Teleport(talking.X + 1, talking.Y, talking.Z, force: true);
        Assert.False(held.Succeeded);
        Assert.Contains("Dialogue", held.Message, StringComparison.Ordinal);
        Assert.False(session.Commands<IDeveloperPlacementCommands>().Travel("Charing").Succeeded);
        Assert.False(session.Commands<IDeveloperPlacementCommands>().GoTo($"Actor:{trainer.DurableId}").Succeeded);
        Assert.Equal(talking, Position(session));
        Assert.NotNull(session.ActivationPanel.View.Dialogue);
        session.Dialogue.Close();

        // Unknown and ambiguous names are refused with what was found.
        Assert.False(session.Commands<IDeveloperPlacementCommands>().GoTo("Nobody Of That Name").Succeeded);
        Assert.False(session.Commands<IDeveloperPlacementCommands>().Enter("Charing", "Nowhere Hall").Succeeded);

        // The saved game is inside the temple, by the trainer.
        Vector3 saved = Position(session);
        using AssembledWorldRun restored = world.Restore(session.CaptureSave(), Charing, TempleOfKynareth);
        Assert.Equal(DaggerfallWorldProfileIds.Interior(Charing, TempleOfKynareth), restored.Session.Sites.ActiveProfile);
        Assert.Equal(Planar(saved), Planar(Position(restored.Session)), new PlanarComparer());
    }

    [Fact]
    public void A_variant_selected_by_name_draws_from_the_places_next_build_is_saved_and_clears_with_a_dash()
    {
        AssembledWorld world = Shared.Value;
        using AssembledWorldRun run = world.Start(Charing, variant: "closed");
        IDeveloperPlacementCommands commands = run.Session.Commands<IDeveloperPlacementCommands>();
        DaggerfallWorldProfileKey exterior = DaggerfallWorldProfileIds.Exterior(Charing);
        DaggerfallSiteProfile open = world.Profiles.Require(exterior);

        Assert.Equal("Charing (17/4) holds variant 'flooded', which no selected pack carries, so its base still shows.", commands.Variant("17/4", "flooded").Message);
        DebugCommandResult closed = commands.Variant("Charing", "closed");
        Assert.True(closed.Succeeded, closed.Message);
        Assert.Equal("Charing (17/4) draws variant 'closed' from its next build.", closed.Message);
        Assert.Equal(new DaggerfallWorldVariantSave(Charing.Region, Charing.Index, "closed"), Assert.Single(DaggerfallSavePayload.Read(run.Session.CaptureSave()).WorldVariants));
        Assert.True(commands.Travel("Charing").Succeeded);
        Assert.NotSame(open, run.Session.Sites.Projection.Inputs);
        Assert.Contains(run.Session.Sites.Projection.Inputs.Geometry.Spatial, part => part.Id == "block/3/3" && !open.Geometry.Spatial.Contains(part));

        Assert.Equal("Charing (17/4) draws its base from its next build.", commands.Variant("Charing", "-").Message);
        Assert.Empty(DaggerfallSavePayload.Read(run.Session.CaptureSave()).WorldVariants);
        Assert.False(commands.Variant("Charing", "a b").Succeeded);
    }

    [Fact]
    public void Commands_list_the_places_a_name_matches_and_refuse_a_dead_player()
    {
        AssembledWorld world = Shared.Value;
        using AssembledWorldRun run = world.Start(PrivateersHold);
        DebugCommandResult several = run.Session.Commands<IDeveloperPlacementCommands>().Travel("Castle");
        Assert.False(several.Succeeded);
        Assert.Contains("Name one by region/index", several.Message, StringComparison.Ordinal);

        run.Session.State.Actors.Player.Stats.GetTrack(Rusty.Engine.Mechanics.TrackId.Parse("health")).SetCurrent(0);
        run.Step();
        Assert.True(run.Session.State.Actors.Player.IsDefeated);
        Assert.Equal("The player is dead.", run.Session.Commands<IDeveloperPlacementCommands>().Teleport(0, 0, 0, force: true).Message);
    }

    private static Vector3 Position(DaggerfallSession session) => session.State.PlayerControl.Position!.Value.ToVector();
    private static Vector2 Planar(Vector3 value) => new(value.X, value.Z);
    private static JsonElement Json(DebugCommandResult result)
    {
        Assert.True(result.Succeeded, result.Message);
        return JsonDocument.Parse(result.Message).RootElement.Clone();
    }

    private sealed class PlanarComparer : IEqualityComparer<Vector2>
    {
        public bool Equals(Vector2 x, Vector2 y) => Vector2.Distance(x, y) < .02f;
        public int GetHashCode(Vector2 value) => 0;
    }

    /// <summary>Each brought actor stands within METRES of the player but never on top of them, ahead of them and facing them, hostile and provoked.</summary>
    private static void AssertBroughtNear(DaggerfallSession session, long[] brought, Vector3 player, float metres)
    {
        Vector3 forward = WorldRpg.Kit.Actors.ActorHeading.Forward(session.State.PlayerControl.YawRadians);
        foreach (long id in brought)
        {
            WorldRpg.Kit.Actors.ActorState actor = session.State.Actors.Get(id);
            Vector2 offset = Planar(actor.Position.ToVector()) - Planar(player);
            Assert.InRange(offset.Length(), DaggerfallNearbySpawn.MinimumMetres - .01f, metres + .01f);
            Assert.True(Vector2.Dot(Vector2.Normalize(offset), new Vector2(forward.X, forward.Z)) > .8f, $"{offset} is not ahead");
            Assert.Equal(MathF.Atan2(-offset.X, offset.Y), actor.HeadingYawRadians, 3);
            // Hostile, and provoked: it knows where the player is.
            DaggerfallEnemyPerceptionMemory senses = actor.Actor.Get<DaggerfallEnemyPerceptionMemory>();
            Assert.True(senses.ForcedHostile);
            Assert.True(senses.HasEncounteredPlayer);
            Assert.NotNull(senses.ProvokedUntilMinute);
        }
    }
}
