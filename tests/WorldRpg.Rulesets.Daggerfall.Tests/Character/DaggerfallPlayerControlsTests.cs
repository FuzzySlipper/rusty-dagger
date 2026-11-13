using Rusty.Engine;
using WorldRpg.Kit.Controls;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>The control settings owner, constructed without the session over the Engine's input and audio fakes.</summary>
public sealed class DaggerfallPlayerControlsTests
{
    private sealed class Fixture
    {
        internal readonly EngineContextFake Engine = EngineContextFake.Create(null!, null!, null!);
        internal readonly List<string> Told = [];
        internal readonly DaggerfallPlayerControls Controls;

        internal Fixture()
        {
            DaggerfallTuning tuning = DaggerfallTuning.Defaults;
            DaggerfallControlSettings initial = new();
            Controls = new DaggerfallPlayerControls(Engine.Context.Input, Engine.Context.Audio,
                new PlayerInputSystem(tuning.PlayerControl, DaggerfallInput.Controls, DaggerfallInput.Bindings, tuning.ControllerInput),
                new DaggerfallLocomotionPolicy(tuning.Locomotion, initial), initial, tuning.PlayerControl.LookSensitivity,
                tuning.ControllerInput, actionDriven => Told.Add($"time:{actionDriven}"),
                (grows, distance) => Told.Add($"ground:{grows}:{distance}"));
        }
    }

    [Fact]
    public void A_change_is_applied_whole_and_kept_once_for_the_host_to_save()
    {
        var f = new Fixture();
        f.Controls.Change(settings => settings.SetVolume("music", .35f));

        Assert.Equal("Control settings applied.", f.Controls.Diagnostic);
        Assert.Equal(.35f, f.Controls.Settings.Volume("music"), 3);
        Assert.Equal(.35f, f.Engine.BusVolumes[AudioBus.Music], 3);
        Assert.NotEmpty(f.Engine.PhysicalInput.Mappings);
        // Time is told before the ground, as the change has always told them.
        Assert.Equal(["time:False", "ground:True:1"], f.Told);
        string saved = Assert.IsType<string>(f.Controls.TakePreferencesSave());
        Assert.Equal(.35f, DaggerfallControlSettings.Parse(saved).Volume("music"), 3);
        Assert.Null(f.Controls.TakePreferencesSave());
    }

    [Fact]
    public void Bindings_the_engine_refuses_leave_the_settings_in_force_and_nothing_to_save()
    {
        var f = new Fixture();
        DaggerfallControlSettings before = f.Controls.Settings;
        f.Engine.PhysicalInput.Outcome = InputMappingReplacementOutcome.InvalidMappings;
        f.Controls.Change(settings => settings.SetVolume("music", .35f));

        Assert.Same(before, f.Controls.Settings);
        Assert.Contains("InvalidMappings", f.Controls.Diagnostic, StringComparison.Ordinal);
        Assert.Null(f.Controls.TakePreferencesSave());
        Assert.Empty(f.Told);
    }

    [Fact]
    public void Reset_returns_the_bindings_and_keeps_scale_ground_cover_and_sound()
    {
        var f = new Fixture();
        f.Controls.Change(settings =>
        {
            settings.Rebind("attack", ["KeyB"]);
            settings.SetUiScale(1.5f);
            settings.SetGroundCover(false);
            settings.SetVolume("effects", .2f);
        });
        Assert.Equal(["KeyB"], f.Controls.Settings.KeysFor("attack"));

        f.Controls.Reset();
        Assert.Equal(new DaggerfallControlSettings().KeysFor("attack"), f.Controls.Settings.KeysFor("attack"));
        Assert.Equal(1.5f, f.Controls.Settings.UiScale);
        Assert.False(f.Controls.Settings.GroundCover);
        Assert.Equal(.2f, f.Controls.Settings.Volume("effects"), 3);
    }

    [Fact]
    public void Action_driven_time_tells_the_clock_and_leaves_the_choice_to_save()
    {
        var f = new Fixture();
        f.Controls.ChangePlayerTime(true);

        Assert.True(f.Controls.ActionDrivenTime);
        Assert.Equal(["time:True"], f.Told);
        Assert.True(DaggerfallControlSettings.Parse(f.Controls.TakePreferencesSave()!).ActionDrivenTime);
    }

    [Fact]
    public void The_pad_starts_from_the_authored_layout_until_settings_are_applied()
    {
        var f = new Fixture();
        Assert.Equal(DaggerfallInput.PadActions, f.Controls.PadBindings);
        f.Controls.ApplyPreferences(null);
        Assert.Equal(new DaggerfallControlSettings().ControllerActions(DaggerfallInput.PadActions), f.Controls.PadBindings);
    }
}
