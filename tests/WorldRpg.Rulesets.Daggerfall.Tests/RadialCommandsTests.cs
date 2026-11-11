using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Modules.Transport;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class RadialCommandsTests
{
    private static DaggerfallRadialCommand Command(DaggerfallSession session, string id) =>
        Assert.Single(session.RadialCommands(session.ActivationView.Mode), command => command.Id == id);

    [Fact]
    public void The_radial_offers_its_commands_in_ring_order_with_the_rulesets_words()
    {
        using DaggerfallSession session = FreshSession();
        Assert.Equal(["weapon", "mode-grab", "mode-info", "mode-talk", "mode-steal", "spells", "inventory", "character", "journal", "map",
            "transport", "rest", "menu"], session.RadialCommands(session.ActivationView.Mode).Select(command => command.Id));
        Assert.All(session.RadialCommands(session.ActivationView.Mode), command => Assert.True(command.Available));
        Assert.True(Command(session, $"mode-{session.ActivationView.Mode}").Current);
        Assert.Equal("Steal / Lockpick", Command(session, "mode-steal").Label);

        session.State.WeaponDrawn = true;
        Assert.Equal("Sheathe weapon", Command(session, "weapon").Label);
        session.State.WeaponDrawn = false;
        Assert.Equal("Ready weapon", Command(session, "weapon").Label);
    }

    [Fact]
    public void Riding_takes_the_weapon_command_away_as_the_rule_does_and_says_why()
    {
        using DaggerfallSession session = FreshSession();
        session.State.Transport.Restore(new DaggerfallTransportSave(DaggerfallTransportMode.Horse, false));
        DaggerfallRadialCommand weapon = Command(session, "weapon");
        Assert.False(weapon.Available);
        Assert.Equal("The reins take your hands while you ride.", weapon.Reason);
        // The command the radial withholds is the one the rule refuses.
        bool drawn = session.State.WeaponDrawn;
        session.Update(new ProductUpdate(OuterUpdate(1), [Ui("""{"action":"weapon-toggle"}""")]));
        Assert.Equal(drawn, session.State.WeaponDrawn);

        session.State.Transport.Restore(DaggerfallTransportSave.Foot);
        Assert.True(Command(session, "weapon").Available);
    }

    [Fact]
    public void Beast_form_takes_the_inventory_command_away_with_the_rules_refusal()
    {
        using DaggerfallSession session = FreshSession();
        session.State.RacialOverrides.Select(DaggerfallRacialKind.Werewolf, "curse", 0);
        session.State.RacialOverrides.SetBeastForm(true, 0);
        DaggerfallRadialCommand inventory = Command(session, "inventory");
        Assert.False(inventory.Available);
        Assert.Equal(DaggerfallRacialOverrideView.InventoryRefusal, inventory.Reason);
        session.State.RacialOverrides.SetBeastForm(false, 0);
        Assert.True(Command(session, "inventory").Available);
    }
}
