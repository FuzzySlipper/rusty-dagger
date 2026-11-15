using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Debugging;
using Rusty.Engine.Entities;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// <see cref="DaggerfallDeveloperPlacement"/> built without the session: the actor definitions, the player's pose and a
/// recording spawn as plain inputs. Landing in places and buildings runs over the site crossing and collision and is
/// <c>DeveloperWorldCommandsTests</c>' end-to-end case.
/// </summary>
public sealed class DaggerfallDeveloperPlacementTests
{
    private readonly List<(string Actor, ActorPose Pose)> _spawned = [];

    [Fact]
    public void A_spawn_stands_the_actor_ahead_of_the_player_facing_back()
    {
        PlayerControlState player = new(new WorldPoint(1f, 0f, 2f), .5f, 0f);
        DaggerfallDeveloperPlacement placement = Placement(player, ProductMode.Playing);

        DebugCommandResult spawned = placement.Spawn("fire-daedra", 3);
        Assert.True(spawned.Succeeded);
        Assert.Equal("fire-daedra (7) stands 3 m ahead.", spawned.Message);
        (string actor, ActorPose pose) = Assert.Single(_spawned);
        Assert.Equal("fire-daedra", actor);
        Assert.True(Vector3.Distance(new Vector3(1f, 0f, 2f) + ActorHeading.Forward(.5f) * 3, pose.Position.ToVector()) < 1e-5f);
        Assert.Equal(.5f + MathF.PI, pose.HeadingYawRadians, 5);

        Assert.Equal("A distance is finite and above 0.", placement.Spawn("fire-daedra", 0).Message);
        Assert.Equal("'no-such-actor' is no actor definition that can be spawned.", placement.Spawn("no-such-actor", 3).Message);
        Assert.Equal("'player' is no actor definition that can be spawned.", placement.Spawn("player", 3).Message);
        Assert.Single(_spawned);
    }

    [Fact]
    public void Movement_waits_for_the_hold_and_where_needs_a_position()
    {
        DaggerfallDeveloperPlacement title = Placement(new PlayerControlState(null, 0f, 0f), ProductMode.Title);
        foreach (DebugCommandResult refused in new[] { title.Teleport(0, 0, 0, force: true), title.Move(1, 0, 0), title.Forward(1),
            title.Travel("Charing"), title.Enter("Charing", "Temple of Kynareth"), title.GoTo("Door"), title.Stand(1, 1), title.Scout("grass", 1) })
        {
            Assert.Equal(DebugCommandStatus.Failed, refused.Status);
            Assert.Equal("No game is running.", refused.Message);
        }
        Assert.Equal("The player has no position.", title.Where().Message);
        Assert.Equal("The player has no position.", title.Spawn("fire-daedra", 3).Message);
    }

    private DaggerfallDeveloperPlacement Placement(PlayerControlState player, ProductMode mode) => new(
        DeveloperOwnerStage.Hold(() => mode, DeveloperOwnerStage.Actors(health: 100), player, () => []), null!, null!, null!, null!, player, null!,
        null!, TestPayload.Definitions, null!, null!, null!, null!, null!, () => mode, null!,
        (actor, pose) => { _spawned.Add((actor, pose)); return 7; }, null!, null!);
}
