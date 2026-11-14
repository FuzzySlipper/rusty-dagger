using WorldRpg.Rulesets.Daggerfall.Modules.Transport;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary><see cref="DaggerfallRadialCommands"/> built without the session, over a transport and <see cref="SocietyStage"/>'s racial overrides.</summary>
public sealed class DaggerfallRadialCommandsTests
{
    [Fact]
    public void The_commands_follow_the_weapon_the_ride_the_mode_and_the_beast_form()
    {
        using SocietyStage stage = new();
        DaggerfallTransportPolicy transport = new(DaggerfallTuning.Defaults.Transport);
        bool drawn = false;
        DaggerfallRadialCommands radial = new(transport, stage.Overrides, () => drawn);

        IReadOnlyList<DaggerfallRadialCommand> commands = radial.Read("talk");
        Assert.Equal("weapon", commands[0].Id);
        Assert.Equal("Ready weapon", commands[0].Label);
        Assert.True(Assert.Single(commands, command => command.Id == "mode-talk").Current);
        drawn = true;
        Assert.Equal("Sheathe weapon", radial.Read(null)[0].Label);

        transport.Restore(new DaggerfallTransportSave(DaggerfallTransportMode.Horse, false));
        Assert.False(transport.WeaponHandsFree);
        Assert.Equal("The reins take your hands while you ride.", radial.Read(null)[0].Reason);

        stage.Overrides.Select(DaggerfallRacialKind.Werewolf, "curse", 0);
        stage.Overrides.SetBeastForm(true, 0);
        DaggerfallRadialCommand inventory = Assert.Single(radial.Read(null), command => command.Id == DaggerfallPanel.Inventory);
        Assert.False(inventory.Available);
        Assert.Equal(DaggerfallRacialOverrideView.InventoryRefusal, inventory.Reason);
    }
}
