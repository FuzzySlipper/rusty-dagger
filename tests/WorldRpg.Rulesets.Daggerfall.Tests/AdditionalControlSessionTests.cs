using System.Numerics;
using System.Text;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class AdditionalControlSessionTests
{
    [Theory]
    [InlineData("spell", "spells")]
    [InlineData("rest", "rest")]
    [InlineData("transport", "transport")]
    [InlineData("logbook", "journal")]
    [InlineData("notebook", "journal")]
    [InlineData("travel-map", "transport")]
    [InlineData("use-magic-item", "magic-items")]
    public void Mapped_shortcuts_request_the_existing_panels(string intent, string panel)
    {
        using var f = new SanguineRoseSessionTests.Fixture();
        Press(f.Session, intent);
        Assert.Equal(panel, f.Session.LatestPanelRequest!.Panel);
        Assert.Contains(new DaggerfallControlSettings().PhysicalMappings(), mapping => Encoding.UTF8.GetString(mapping.Intent.Span) == intent);
    }

    [Theory]
    [InlineData("steal")]
    [InlineData("grab")]
    [InlineData("info")]
    [InlineData("talk")]
    public void Activation_shortcuts_change_the_current_mode(string mode)
    {
        using var f = new SanguineRoseSessionTests.Fixture();
        Press(f.Session, "activation." + mode);
        Assert.Equal(mode, f.Session.ActivationView.Mode);
    }

    [Theory]
    [InlineData("quick-save", SaveSlotOperation.QuickSave)]
    [InlineData("quick-load", SaveSlotOperation.QuickLoad)]
    public void Quick_slots_use_the_existing_host_request(string intent, SaveSlotOperation operation)
    {
        using var f = new SanguineRoseSessionTests.Fixture();
        Press(f.Session, intent);
        Assert.Equal(operation, f.Session.TakeSaveSlotRequest()!.Operation);
        Assert.Null(f.Session.TakeSaveSlotRequest());
    }

    [Fact]
    public void Recast_readies_the_last_cast_spell_and_abort_clears_it()
    {
        using var f = new SanguineRoseSessionTests.Fixture();
        var session = f.Session;
        var factory = new DaggerfallItemFactory(TestPayload.Definitions, f.Engine.Context.Random);
        factory.Materialize(factory.Create(new("MiscItems", "recast-book", DaggerfallItemOwner.Player, Quantity: 1, TemplateIndex: 132)),
            session.State.Inventory, session.State.ItemInstances, unique: session.UniqueItemAllocator.AllocateReference());
        session.State.Character.LearnSpell("spell.075");
        var magicka = session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("magicka"));
        magicka.Maximum.BaseValue = 1000; magicka.SetCurrent(1000);
        Assert.Equal(DaggerfallCastOutcome.Ready, session.ReadyPlayerSpell("spell.075").Outcome);
        Assert.Equal(DaggerfallCastOutcome.DeliveryCompleted, session.ReleaseReadySpell(1, Vector3.UnitZ).Outcome);
        magicka.Maximum.BaseValue = 1000; magicka.SetCurrent(1000);
        Press(session, "recast");
        Assert.True(session.Casting.ReadyFor(1)?.SpellKey == "spell.075", session.Presentation.LastOutcome);
        Press(session, "abort-spell");
        Assert.Null(session.Casting.ReadyFor(1));
    }

    [Fact]
    public void The_interface_scale_is_a_bounded_persisted_preference_that_reset_keeps()
    {
        using var f = new SanguineRoseSessionTests.Fixture();
        ProductInputEvent Action(string json) => Input(InputEventKind.DirectDigital) with { ValueKind = InputValueKind.ProductPayload,
            PayloadContract = "dagger.ui.action.v1"u8.ToArray(), PayloadData = System.Text.Encoding.UTF8.GetBytes(json) };

        f.Session.Update(new ProductUpdate(OuterUpdate(100), [Action("{\"action\":\"controls-ui-scale\",\"scale\":1.5}")]));
        string saved = Assert.IsType<string>(f.Session.TakePlayerPreferencesSave());
        Assert.Equal(1.5f, DaggerfallControlSettings.Parse(saved).UiScale);
        using var resumed = new SanguineRoseSessionTests.Fixture();
        resumed.Session.ApplyPlayerPreferences(saved);
        Assert.Equal(saved, resumed.Session.CapturePlayerPreferences());

        // Off the steps or out of bounds is refused and changes nothing.
        f.Session.Update(new ProductUpdate(OuterUpdate(101), [Action("{\"action\":\"controls-ui-scale\",\"scale\":1.3}")]));
        f.Session.Update(new ProductUpdate(OuterUpdate(102), [Action("{\"action\":\"controls-ui-scale\",\"scale\":3.25}")]));
        Assert.Null(f.Session.TakePlayerPreferencesSave());
        Assert.Equal(1.5f, DaggerfallControlSettings.Parse(f.Session.CapturePlayerPreferences()).UiScale);

        // Resetting the controls keeps the scale; no scale is automatic.
        f.Session.Update(new ProductUpdate(OuterUpdate(103), [Action("{\"action\":\"controls-reset\"}")]));
        Assert.Equal(1.5f, DaggerfallControlSettings.Parse(Assert.IsType<string>(f.Session.TakePlayerPreferencesSave())).UiScale);
        f.Session.Update(new ProductUpdate(OuterUpdate(104), [Action("{\"action\":\"controls-ui-scale\"}")]));
        Assert.Null(DaggerfallControlSettings.Parse(Assert.IsType<string>(f.Session.TakePlayerPreferencesSave())).UiScale);
    }

    [Fact]
    public void A_persisted_interface_scale_off_its_steps_is_refused_by_name()
    {
        string saved = new DaggerfallControlSettings().Serialize().Replace("\"uiScale\":null", "\"uiScale\":1.1", StringComparison.Ordinal);
        Assert.Contains("\"uiScale\":1.1", saved, StringComparison.Ordinal);
        ArgumentOutOfRangeException failure = Assert.Throws<ArgumentOutOfRangeException>(() => DaggerfallControlSettings.Parse(saved));
        Assert.Contains("interface scale", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Look_preferences_are_saved_then_applied_to_pointer_input_after_reload()
    {
        using var f = new SanguineRoseSessionTests.Fixture();
        var action = Input(InputEventKind.DirectDigital) with { ValueKind = InputValueKind.ProductPayload,
            PayloadContract = "dagger.ui.action.v1"u8.ToArray(),
            PayloadData = "{\"action\":\"controls-look\",\"sensitivity\":2,\"invert\":false}"u8.ToArray() };
        f.Session.Update(new ProductUpdate(OuterUpdate(100), [action]));
        string saved = Assert.IsType<string>(f.Session.TakePlayerPreferencesSave());
        using var resumed = new SanguineRoseSessionTests.Fixture();
        resumed.Session.ApplyPlayerPreferences(saved);
        Assert.Equal(saved, resumed.Session.CapturePlayerPreferences());
        float yaw = resumed.Session.State.PlayerControl.YawRadians;
        resumed.Session.Update(new ProductUpdate(OuterUpdate(101), [Input(InputEventKind.PointerDelta, x: 10, y: 0)]));
        Assert.Equal(DaggerfallTuning.Defaults.PlayerControl.LookSensitivity * 20,
            MathF.IEEERemainder(resumed.Session.State.PlayerControl.YawRadians - yaw, MathF.Tau), 4);
        Assert.False(DaggerfallControlSettings.Parse(saved).InvertVertical);
    }

    private static void Press(DaggerfallSession session, string intent) => session.Update(new ProductUpdate(OuterUpdate(100),
        [Input(InputEventKind.MappedDigital, InputEdge.Pressed, x: 1, phase: InputPhase.Pressed, intent: intent)]));
}
