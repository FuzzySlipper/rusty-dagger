using WorldRpg.Kit.Controls;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>The player's acts in a step and when they run, with no session.</summary>
public sealed class DaggerfallPlayerActsTests
{
    private static readonly DaggerfallPlayerActs None = new(false, false, false, false, false, false, false, false, []);

    [Fact]
    public void A_shortcut_takes_the_slices_attack_kick_and_use_and_interaction_or_a_sheathed_weapon_keeps_the_swing_each_saying_why()
    {
        DaggerfallInputRefusals refusals = new();
        DaggerfallPlayerActs swing = None with { Attack = true, Kick = true, Interact = true, Shortcuts = ["spell"] };
        DaggerfallPlayerActs after = swing.AfterShortcuts(refusals);
        Assert.False(after.Attack || after.Kick || after.Interact);
        // Each act the shortcut took is reported; the last one stands.
        Assert.Equal("Use dropped: a shortcut took the same update", refusals.Latest.ToString());
        Assert.Same(None, None.AfterShortcuts(refusals));

        DaggerfallPlayerActs attack = None with { Attack = true };
        Assert.True(attack.Swings(default, weaponDrawn: true, refusals));
        Assert.False(attack.Swings(default, weaponDrawn: false, refusals));
        Assert.Equal("Attack refused: weapon sheathed", refusals.Latest.ToString());
        Assert.False(attack.Swings(new ActorControlRestrictions { PhysicalAttacks = true }, weaponDrawn: true, refusals));
        Assert.Equal("Attack refused: paralysed", refusals.Latest.ToString());
        Assert.False((attack with { Interact = true }).Swings(default, weaponDrawn: true, refusals));
        Assert.Equal("Attack dropped: use took the same update", refusals.Latest.ToString());
        Assert.False((None with { Kick = true }).Swings(default, weaponDrawn: false, refusals));
        Assert.Equal("Kick refused: weapon sheathed", refusals.Latest.ToString());
    }

    [Fact]
    public void Acts_wait_behind_an_enemy_hit_in_flight_fold_in_order_and_never_outlive_the_update()
    {
        bool inFlight = false;
        DaggerfallInputRefusals refusals = new();
        DaggerfallPlayerActTiming timing = new(() => inFlight, refusals);
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
        Assert.Equal("Attack dropped: the enemy's hit it waited for downed the player", refusals.Latest.ToString());
        Assert.Null(timing.PerformNow(attack));
        Assert.Null(timing.TakeDeferred(worldHeld: true, playerDown: false));
        Assert.Equal("Attack dropped: the enemy's hit it waited for brought a modal up", refusals.Latest.ToString());
        Assert.Null(timing.PerformNow(None with { Interact = true }));
        timing.Drop();
        Assert.Equal("Use dropped: the world held before the enemy's hit it waited for landed", refusals.Latest.ToString());
        Assert.Null(timing.TakeDeferred(worldHeld: false, playerDown: false));
    }

    [Fact]
    public void A_report_reads_for_five_seconds_of_host_time_and_the_latest_one_stands()
    {
        DaggerfallInputRefusals refusals = new();
        Assert.Null(refusals.Latest);
        refusals.Refused("Attack", "recovering, 0.3 s left");
        refusals.Age(4.9);
        Assert.Equal(new DaggerfallInputRefusal("Attack", false, "recovering, 0.3 s left"), refusals.Latest);
        refusals.Dropped("Cast", "menu open");
        refusals.Age(4.9);
        Assert.Equal("Cast dropped: menu open", refusals.Latest.ToString());
        refusals.Age(double.NaN);
        refusals.Age(.1);
        Assert.Null(refusals.Latest);
        Assert.Equal("out of range", DaggerfallInputRefusals.Words("OutOfRange"));
    }
}
