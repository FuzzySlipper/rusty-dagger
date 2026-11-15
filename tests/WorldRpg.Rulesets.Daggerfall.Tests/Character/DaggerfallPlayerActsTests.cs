using WorldRpg.Kit.Controls;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>The player's acts in a step and when they run, with no session.</summary>
public sealed class DaggerfallPlayerActsTests
{
    private static readonly DaggerfallPlayerActs None = new(false, false, false, false, false, false, false, false, []);

    [Fact]
    public void A_shortcut_takes_the_slices_attack_kick_and_use_and_interaction_or_a_sheathed_weapon_keeps_the_swing()
    {
        DaggerfallPlayerActs swing = None with { Attack = true, Kick = true, Interact = true, Shortcuts = ["spell"] };
        DaggerfallPlayerActs after = swing.AfterShortcuts();
        Assert.False(after.Attack || after.Kick || after.Interact);
        Assert.Same(None, None.AfterShortcuts());

        DaggerfallPlayerActs attack = None with { Attack = true };
        Assert.True(attack.Swings(default, weaponDrawn: true));
        Assert.False(attack.Swings(default, weaponDrawn: false));
        Assert.False(attack.Swings(new ActorControlRestrictions { PhysicalAttacks = true }, weaponDrawn: true));
        Assert.False((attack with { Interact = true }).Swings(default, weaponDrawn: true));
    }

    [Fact]
    public void Acts_wait_behind_an_enemy_hit_in_flight_fold_in_order_and_never_outlive_the_update()
    {
        bool inFlight = false;
        DaggerfallPlayerActTiming timing = new(() => inFlight);
        DaggerfallPlayerActs attack = None with { Attack = true, Shortcuts = ["map"] };
        Assert.Same(attack, timing.PerformNow(attack));
        Assert.Null(timing.TakeDeferred(worldHeld: false, playerDown: false));

        inFlight = true;
        Assert.Same(None, timing.PerformNow(None));
        Assert.Null(timing.PerformNow(attack));
        // Once a step defers, the rest of the update defers behind it, even with the hit landed.
        inFlight = false;
        Assert.Null(timing.PerformNow(None with { Inventory = true, Shortcuts = ["map", "rest"] }));
        DaggerfallPlayerActs? waited = timing.TakeDeferred(worldHeld: false, playerDown: false);
        Assert.NotNull(waited);
        Assert.True(waited.Attack && waited.Inventory);
        Assert.Equal(["map", "rest"], waited.Shortcuts);
        Assert.Null(timing.TakeDeferred(worldHeld: false, playerDown: false));

        inFlight = true;
        Assert.Null(timing.PerformNow(attack));
        Assert.Null(timing.TakeDeferred(worldHeld: false, playerDown: true));
        Assert.Null(timing.PerformNow(attack));
        Assert.Null(timing.TakeDeferred(worldHeld: true, playerDown: false));
        Assert.Null(timing.PerformNow(attack));
        timing.Drop();
        Assert.Null(timing.TakeDeferred(worldHeld: false, playerDown: false));
    }
}
