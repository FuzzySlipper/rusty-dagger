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
/// <see cref="DaggerfallDeveloperGrants"/> built without the session: every grant waits for the hold, which a title
/// screen fills. What each grant gives through the item, equipment, currency, skill, transport and affliction owners is
/// <c>DeveloperPlayerCommandsTests</c>' end-to-end case.
/// </summary>
public sealed class DaggerfallDeveloperGrantsTests
{
    [Fact]
    public void Every_grant_waits_for_the_hold()
    {
        DaggerfallDeveloperGrants grants = new(DeveloperOwnerStage.Hold(() => ProductMode.Title, DeveloperOwnerStage.Actors(health: 100),
            new PlayerControlState(null, 0f, 0f), () => []), TestPayload.Definitions, null!, null!, null!, null!, () => throw new InvalidOperationException(),
            null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, () => throw new InvalidOperationException(), _ => throw new InvalidOperationException());
        foreach (DebugCommandResult refused in new[] { grants.Give("Steel Dagger", 1), grants.Hold("Steel Dagger"), grants.HoldMagic(),
            grants.Equip("Leather Cuirass"), grants.Money(10), grants.Skill("Dodging", 40), grants.LevelUp(), grants.Heal(), grants.Spell("Fireball"),
            grants.Ride("horse"), grants.RideState(), grants.Morph("werewolf") })
            Assert.Equal("No game is running.", refused.Message);
    }
}
