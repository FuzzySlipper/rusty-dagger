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
/// recording nearby spawn as plain inputs. Landing in places and buildings runs over the site crossing and collision and is
/// <c>DeveloperWorldCommandsTests</c>' end-to-end case.
/// </summary>
public sealed class DaggerfallDeveloperPlacementTests
{
    private readonly List<(string Actor, int Count, float Metres)> _asked = [];
    private DaggerfallNearbySpawnResult _bring = new([], null);

    [Fact]
    public void A_spawn_brings_what_the_nearby_spawn_brings_and_says_why_it_brought_no_more()
    {
        PlayerControlState player = new(new WorldPoint(1f, 0f, 2f), .5f, 0f);
        DaggerfallDeveloperPlacement placement = Placement(player, ProductMode.Playing);

        _bring = new([7, 8], null);
        DebugCommandResult spawned = placement.Spawn("encounter-warrior", 2, 5);
        Assert.True(spawned.Succeeded);
        Assert.Equal("Brought 2 encounter-warrior (7, 8) within 5 m.", spawned.Message);
        Assert.Equal(("encounter-warrior", 2, 5f), Assert.Single(_asked));

        _bring = new([7], "No clear floor within 5 m of the player, in sight of them, for one more.");
        DebugCommandResult partly = placement.Spawn("encounter-warrior", 2, 5);
        Assert.True(partly.Succeeded);
        Assert.Equal("Brought only 1 encounter-warrior (7) within 5 m. No clear floor within 5 m of the player, in sight of them, for one more.", partly.Message);

        _bring = new([], "No clear floor within 5 m of the player, in sight of them, for 2 more.");
        DebugCommandResult refused = placement.Spawn("encounter-warrior", 2, 5);
        Assert.Equal(DebugCommandStatus.Failed, refused.Status);
        Assert.Equal("No clear floor within 5 m of the player, in sight of them, for 2 more.", refused.Message);
    }

    [Fact]
    public void Movement_waits_for_the_hold_and_where_needs_a_position()
    {
        DaggerfallDeveloperPlacement title = Placement(new PlayerControlState(null, 0f, 0f), ProductMode.Title);
        foreach (DebugCommandResult refused in new[] { title.Teleport(0, 0, 0, force: true), title.Move(1, 0, 0), title.Forward(1),
            title.Travel("Charing"), title.Enter("Charing", "Temple of Kynareth"), title.GoTo("Door"), title.Stand(1, 1), title.Scout("grass", 1), title.Variant("Charing", "closed") })
        {
            Assert.Equal(DebugCommandStatus.Failed, refused.Status);
            Assert.Equal("No game is running.", refused.Message);
        }
        Assert.Equal("The player has no position.", title.Where().Message);

    }

    private DaggerfallDeveloperPlacement Placement(PlayerControlState player, ProductMode mode) => new(
        DeveloperOwnerStage.Hold(() => mode, DeveloperOwnerStage.Actors(health: 100), player, () => []), null!, null!, null!, null!, player, null!,
        null!, TestPayload.Definitions, null!, null!, null!, null!, null!, () => mode, null!,
        (actor, count, metres) => { _asked.Add((actor, count, metres)); return _bring; }, null!, null!);
}
