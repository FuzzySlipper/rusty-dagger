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
/// <see cref="DaggerfallDeveloperHold"/> built without the session: the mode, the player and the open screens as plain
/// inputs. The questions it asks after those (offers, court, spell choices, cinematics) need their owners and are
/// <c>DeveloperWorldCommandsTests</c>' and <c>DeveloperPlayerCommandsTests</c>' end-to-end cases.
/// </summary>
public sealed class DaggerfallDeveloperHoldTests
{
    [Fact]
    public void A_command_waits_for_a_game_a_living_player_with_a_position_and_every_open_screen()
    {
        ProductMode mode = ProductMode.Title;
        string[] open = [];
        ActorsState actors = DeveloperOwnerStage.Actors(health: 100);
        DaggerfallDeveloperHold nowhere = DeveloperOwnerStage.Hold(() => mode, actors, new PlayerControlState(null, 0f, 0f), () => open);
        Assert.Equal("No game is running.", nowhere.Refusal());
        mode = ProductMode.Playing;
        Assert.Equal("The player has no position.", nowhere.Refusal());

        PlayerControlState player = new(new WorldPoint(0f, 0f, 0f), 0f, 0f);
        DaggerfallDeveloperHold hold = DeveloperOwnerStage.Hold(() => mode, actors, player, () => open);
        open = ["Dialogue", "LevelUp"];
        Assert.Equal("Close the open Dialogue, LevelUp first.", hold.Refusal());

        DaggerfallDeveloperHold dead = DeveloperOwnerStage.Hold(() => ProductMode.Playing, DeveloperOwnerStage.Actors(health: 0), player, () => open);
        Assert.Equal("The player is dead.", dead.Refusal());
    }
}
