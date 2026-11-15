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

/// <summary>The plain inputs the developer command owners' tests share.</summary>
internal static class DeveloperOwnerStage
{
    internal static ActorsState Actors(double health)
    {
        ActorsState actors = new();
        actors.CreatePlayer(DaggerfallActorIdentity.PlayerEntityId, new EntityTypeId("player"), DaggerCombatFixture.Stats(health, 600d), "health",
            DaggerActorFactory.PlayerCapabilities);
        return actors;
    }

    /// <summary>A hold over the mode, the player and the open screens; the owners of its later questions are not reached.</summary>
    internal static DaggerfallDeveloperHold Hold(Func<ProductMode> mode, ActorsState actors, PlayerControlState player, Func<IEnumerable<string>> open) =>
        new(mode, actors, player, open, null!, null!, null!, null!, null!, null!, null!, null);
}
