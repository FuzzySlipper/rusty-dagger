using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallHudPresentationTests
{
    [Theory]
    [InlineData("Grab", "Door", "Open")]
    [InlineData("Grab", "Corpse", "Search")]
    [InlineData("Grab", "Item", "Take")]
    [InlineData("Grab", "Portal", "Go through")]
    [InlineData("Talk", "Npc", "Talk to")]
    [InlineData("Info", "Container", "Examine")]
    [InlineData("Steal", "Door", "Pick the lock of")]
    [InlineData("Steal", "Npc", "Pickpocket")]
    [InlineData("Steal", "Container", "Steal from")]
    [InlineData("Bash", "Door", "Bash")]
    public void The_prompt_names_what_use_does_in_each_mode(string mode, string kind, string action) =>
        Assert.Equal(action, DaggerfallHudText.PromptAction(Enum.Parse<DaggerfallActivationMode>(mode), Enum.Parse<DaggerfallActivationTargetKind>(kind)));

    [Fact]
    public void Mode_names_cover_every_activation_mode_and_distances_read_in_whole_metres()
    {
        Assert.Equal(Enum.GetValues<DaggerfallActivationMode>().Select(mode => mode.ToString().ToLowerInvariant()),
            DaggerfallHudText.ActivationModes.Select(row => row.Id));
        Assert.Equal("Steal / Lockpick", DaggerfallHudText.ModeLabel("steal"));
        Assert.Equal("5 m", DaggerfallHudText.Distance(4.6));
        Assert.Equal("0 m", DaggerfallHudText.Distance(0.2));
        Assert.Equal("Detect enemy", DaggerfallHudText.DetectorLabel("enemy"));
    }

    [Fact]
    public void The_hud_publishes_heading_weapon_state_mode_names_and_the_device_last_pressed()
    {
        using var f = new SanguineRoseSessionTests.Fixture();
        var session = f.Session;
        session.State.WeaponDrawn = true;
        session.Update(new ProductUpdate(OuterUpdate(2), []));
        var hud = Assert.IsType<Dictionary<string, object?>>(f.Engine.Published());
        var view = Assert.IsType<Dictionary<string, object?>>(hud["view"]);
        Assert.Equal(DaggerfallMapProjection.Compass(session.State.PlayerControl.YawRadians), view["heading"]);
        Assert.False(view.ContainsKey("pitchRadians"));
        var weapon = Assert.IsType<Dictionary<string, object?>>(hud["weapon"]);
        Assert.Equal(true, weapon["drawn"]);
        Assert.Equal("Drawn", weapon["state"]);
        Assert.False(string.IsNullOrWhiteSpace(Assert.IsType<string>(weapon["name"])));
        var activation = Assert.IsType<Dictionary<string, object?>>(hud["activation"]);
        Assert.Equal(DaggerfallHudText.ModeLabel(session.ActivationView.Mode), activation["label"]);
        Assert.Equal(DaggerfallHudText.ActivationModes.Count, Assert.IsType<object[]>(activation["modes"]).Length);
        Assert.Equal("keyboard", hud["inputDevice"]);

        // Sheathing shows on the HUD; a pad press makes it name the pad's bindings, a key press the keyboard's.
        session.State.WeaponDrawn = false;
        session.Update(new ProductUpdate(OuterUpdate(3), [PadButton(ControllerButton.Button10, InputEdge.Pressed)]));
        hud = Assert.IsType<Dictionary<string, object?>>(f.Engine.Published());
        Assert.Equal("Sheathed", Assert.IsType<Dictionary<string, object?>>(hud["weapon"])["state"]);
        Assert.Equal("controller", hud["inputDevice"]);
        session.Update(new ProductUpdate(OuterUpdate(4), [Input(InputEventKind.Key, InputEdge.Pressed, KeyboardControl.KeyQ)]));
        Assert.Equal("keyboard", Assert.IsType<Dictionary<string, object?>>(f.Engine.Published())["inputDevice"]);
    }
}
