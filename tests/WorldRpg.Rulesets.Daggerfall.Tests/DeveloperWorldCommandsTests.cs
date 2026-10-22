using System.Numerics;
using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Debugging;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

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
        using AssembledWorldRun run = world.Start(AssembledWorld.PrivateersHold);
        DaggerfallSession session = run.Session;
        Vector3 start = Position(session);

        DebugCommandResult moved = session.Teleport(start.X + 3, start.Y - 1, start.Z - 2, force: false);
        Assert.True(moved.Succeeded, moved.Message);
        Assert.Equal(new Vector2(start.X + 3, start.Z - 2), Planar(Position(session)), new PlanarComparer());
        Assert.Equal(session.Sites.ActiveProfile.LogicalId, Json(moved).GetProperty("profile").GetString());

        // A spot the standing body would overlap is refused with the clearance facts, and the player stays put.
        Vector3 before = Position(session);
        run.Spatial.OverlapHit = _ => default(SpatialHit) with { Present = true, Converged = true };
        DebugCommandResult refused = session.Teleport(before.X + 5, before.Y, before.Z, force: false);
        Assert.False(refused.Succeeded);
        Assert.Contains("overlap", refused.Message, StringComparison.Ordinal);
        Assert.Contains("targetStatus", refused.Message, StringComparison.Ordinal);
        Assert.Equal(before, Position(session));
        Assert.False(session.Forward(4).Succeeded);
        DebugCommandResult forced = session.Teleport(before.X + 5, before.Y, before.Z, force: true);
        Assert.True(forced.Succeeded, forced.Message);
        Assert.Equal(new Vector2(before.X + 5, before.Z), Planar(Position(session)), new PlanarComparer());
        run.Spatial.OverlapHit = _ => default;

        // Relative moves: an offset, and a distance along the facing.
        Vector3 here = Position(session);
        Assert.True(session.Move(-1, 0, 1).Succeeded);
        Assert.Equal(new Vector2(here.X - 1, here.Z + 1), Planar(Position(session)), new PlanarComparer());
        session.State.PlayerControl.YawRadians = 0;
        here = Position(session);
        Assert.True(session.Forward(2).Succeeded);
        Assert.Equal(new Vector2(here.X, here.Z - 2), Planar(Position(session)), new PlanarComparer());

        // The saved game holds the moved player.
        Vector3 saved = Position(session);
        using AssembledWorldRun restored = world.Restore(session.CaptureSave(), AssembledWorld.PrivateersHold);
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

        Assert.False(session.Travel("Nowhere At All").Succeeded);
        DebugCommandResult arrived = session.Travel("Charing");
        Assert.True(arrived.Succeeded, arrived.Message);
        Assert.Equal(DaggerfallWorldProfileIds.Exterior(Charing), session.Sites.ActiveProfile);
        Assert.Equal("Charing", Json(arrived).GetProperty("place").GetString());

        // world.find names the building and how to enter it before it is entered.
        JsonElement found = Json(session.Find("training"));
        JsonElement temple = found.GetProperty("buildings").EnumerateArray().Single(value => value.GetProperty("id").GetString() == TempleOfKynareth.ToString());
        Assert.Equal("The Temple of Kynareth", temple.GetProperty("building").GetString());

        DebugCommandResult entered = session.Enter("Charing", "Temple of Kynareth");
        Assert.True(entered.Succeeded, entered.Message);
        Assert.Equal(DaggerfallWorldProfileIds.Interior(Charing, TempleOfKynareth), session.Sites.ActiveProfile);
        Assert.Equal("The Temple of Kynareth", Json(entered).GetProperty("building").GetString());

        // The temple's trainer is loaded; goto stands the player within reach of them, facing them.
        DaggerfallNpc trainer = session.State.Npcs.All.Single(npc => npc.Presence == DaggerfallNpcPresence.Active
            && npc.Services.Contains("training") && session.State.Actors.TryGet(npc.DurableId, out _));
        Assert.Contains(Json(session.Find("training")).GetProperty("loaded").EnumerateArray(), value => value.GetProperty("id").GetString() == $"Actor:{trainer.DurableId}");
        DebugCommandResult reached = session.GoTo($"Actor:{trainer.DurableId}");
        Assert.True(reached.Succeeded, reached.Message);
        Vector3 player = Position(session), them = session.State.Actors.Get(trainer.DurableId).Position.ToVector();
        float apart = Vector2.Distance(Planar(player), Planar(them));
        Assert.InRange(apart, 1.2f, 1.8f);
        Vector3 facing = WorldRpg.Kit.Actors.ActorHeading.Forward(session.State.PlayerControl.YawRadians);
        Assert.True(Vector2.Dot(Vector2.Normalize(Planar(them) - Planar(player)), new(facing.X, facing.Z)) > .99f);

        // From there the trainer's person screen offers training.
        DaggerfallActivationTarget target = Assert.Single(session.Dialogue.NpcTargets(), value => value.Identity.Value == checked((ulong)trainer.DurableId));
        Assert.True(session.Dialogue.ActivateNpc(new(DaggerfallActivationMode.Talk, target)).Applied);
        Assert.Contains(session.ActivationView.Dialogue!.Services, service => service.Id == DaggerfallPersonServices.Training);

        // Unknown and ambiguous names are refused with what was found.
        Assert.False(session.GoTo("Nobody Of That Name").Succeeded);
        Assert.False(session.Enter("Charing", "Nowhere Hall").Succeeded);

        // The saved game is inside the temple, by the trainer.
        Vector3 saved = Position(session);
        using AssembledWorldRun restored = world.Restore(session.CaptureSave(), Charing, TempleOfKynareth);
        Assert.Equal(DaggerfallWorldProfileIds.Interior(Charing, TempleOfKynareth), restored.Session.Sites.ActiveProfile);
        Assert.Equal(Planar(saved), Planar(Position(restored.Session)), new PlanarComparer());
    }

    [Fact]
    public void Commands_list_the_places_a_name_matches_and_refuse_a_dead_player()
    {
        AssembledWorld world = Shared.Value;
        using AssembledWorldRun run = world.Start(AssembledWorld.PrivateersHold);
        DebugCommandResult several = run.Session.Travel("Castle");
        Assert.False(several.Succeeded);
        Assert.Contains("Name one by region/index", several.Message, StringComparison.Ordinal);

        run.Session.State.Actors.Player.Stats.GetTrack(Rusty.Engine.Mechanics.TrackId.Parse("health")).SetCurrent(0);
        run.Step();
        Assert.True(run.Session.State.Actors.Player.IsDefeated);
        Assert.Equal("The player is dead.", run.Session.Teleport(0, 0, 0, force: true).Message);
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
}
