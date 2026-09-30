using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Host;
using WorldRpg.Kit;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Inventory;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using WorldRpg.Rulesets.Daggerfall.Modules.Loot;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>Controls and input: mapped actions, the pad, panel requests and how one delivery arbitrates attack against interaction.</summary>
public sealed class ControlsInputSessionTests
{
    [Fact]
    public void Mapped_attack_press_reaches_combat_once_without_held_or_catchup_replay()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        PerceptionFake perception = PerceptionFake.Create();
        perception.Receipt = Receipt(new PerceptionPair(1, 2000, 1d, 1d, PerceptionPairKind.Visible, 1d));
        AppearanceFake appearance = new(releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, appearance, perception.Service);
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults));
        double before = session.State.Actors.Get(2000).Stats.GetTrack(TrackId.Parse("health")).Current;
        double staminaBefore = session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("stamina")).Current;
        session.State.Actors.Player.Stats.GetStat(StatId.Parse("strength")).BaseValue = 60; // classic damage floors at zero; stage a swing this fixture can rely on
        ProductInputEvent pressed = Input(InputEventKind.MappedDigital, InputEdge.Pressed, x: 1, phase: InputPhase.Pressed, intent: "attack");
        session.Update(new ProductUpdate(new ProductUpdateFacts(ProductUpdateMode.Realtime, ProductLifecycleState.Running, 1, 1, 1, 1, 60, 3, 0, 1d / 60d), [pressed, pressed]));
        // Two copies of one press are one admitted swing and one stamina charge, and the damage itself
        // waits for the classic hit frame of the swing the viewmodel is playing.
        Assert.Equal(before, session.State.Actors.Get(2000).Stats.GetTrack(TrackId.Parse("health")).Current);
        Assert.True(session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("stamina")).Current < staminaBefore);
        appearance.AdvanceReceiptForAll = Reading(2, 2);
        session.Update(new ProductUpdate(new ProductUpdateFacts(ProductUpdateMode.Realtime, ProductLifecycleState.Running, 1, 1, 1, 2, 60, 3, 0, 1d / 60d), []));
        double after = session.State.Actors.Get(2000).Stats.GetTrack(TrackId.Parse("health")).Current;
        Assert.True(after < before);
        // Held input cannot start a second swing while the first is still playing.
        appearance.AdvanceReceiptForAll = null;
        session.Update(new ProductUpdate(new ProductUpdateFacts(ProductUpdateMode.Realtime, ProductLifecycleState.Running, 2, 1, 1, 100, 60, 3, 0, 1d / 60d),
            [Input(InputEventKind.MappedDigital, InputEdge.Held, x: 1, phase: InputPhase.Held, intent: "attack")]));
        Assert.Equal(after, session.State.Actors.Get(2000).Stats.GetTrack(TrackId.Parse("health")).Current);
    }

    [Fact]
    public void The_pads_mapping_is_tuning_and_every_payload_agrees_with_the_ruleset_defaults()
    {
        string root = TestData.RepositoryRoot;
        ControllerInputTuning loaded = DaggerfallTuning.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/tuning-payloads/daggerfall.defaults.json"))).ControllerInput;

        AssertSamePad(DaggerfallTuning.Defaults.ControllerInput, loaded);
        foreach (string payload in Directory.GetFiles(Path.Combine(root, "content/worldrpg/tuning-payloads"), "*.json"))
            AssertSamePad(loaded, DaggerfallTuning.Read(File.ReadAllBytes(payload)).ControllerInput);

        // The values the payloads name, so a payload edit that silently changed the layout fails here
        // rather than in a playtest: the shell delivers left stick 0/1, right stick 2/3, positive down.
        Assert.Equal(ControllerAxis.Axis0, loaded.MovementX);
        Assert.Equal(ControllerAxis.Axis1, loaded.MovementY);
        Assert.Equal(ControllerAxis.Axis2, loaded.LookX);
        Assert.Equal(ControllerAxis.Axis3, loaded.LookY);
        Assert.Equal(.2f, loaded.MovementDeadzone);
        Assert.Equal(.2f, loaded.LookDeadzone);
        Assert.True(loaded.InvertMovementY);
        Assert.True(loaded.InvertLookY);
        Assert.False(loaded.InvertMovementX);
        Assert.False(loaded.InvertLookX);
        Assert.Equal(2.5f, loaded.LookYawRadiansPerSecond);
        Assert.Equal(
            [ControllerButton.Button0, ControllerButton.Button1, ControllerButton.Button2, ControllerButton.Button3, ControllerButton.Button8, ControllerButton.Button9],
            loaded.Actions.Select(binding => binding.Button));
        Assert.Equal(
            ["daggerfall.attack", "daggerfall.interact", "daggerfall.toggle-weapon", "daggerfall.character", "daggerfall.inventory", "daggerfall.menu"],
            loaded.Actions.Select(binding => binding.Action.Value));
    }

    [Fact]
    public void Every_action_a_payload_binds_is_an_action_the_ruleset_actually_requests()
    {
        string root = TestData.RepositoryRoot;
        // Content naming an action no code asks for is a button that silently does nothing, which is
        // exactly what a renamed action id would produce: nothing else reads the payload's string back.
        HashSet<string> declared = [
            DaggerfallInput.Attack.Value,
            DaggerfallInput.ToggleWeapon.Value,
            DaggerfallInput.Interact.Value,
            DaggerfallInput.Inventory.Value,
            DaggerfallInput.Character.Value,
            DaggerfallInput.Menu.Value,
        ];
        foreach (string payload in new[] { "daggerfall.defaults.json" })
        {
            DaggerfallTuning tuning = DaggerfallTuning.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/tuning-payloads", payload)));
            Assert.NotEmpty(tuning.ControllerInput.Actions);
            foreach (ControllerActionBinding binding in tuning.ControllerInput.Actions)
                Assert.Contains(binding.Action.Value, declared);
        }
    }

    [Fact]
    public void A_pad_alone_moves_and_turns_the_player_on_the_ordinary_input_path()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        DaggerfallTuning tuning = DaggerfallTuning.Defaults;

        using (DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, inputs, tuning)))
        {
            // No keyboard and no synthetic pointer step: one stick pushed forward with the other
            // pushed right is the whole input slice.
            session.Update(new ProductUpdateFacts(ProductUpdateMode.Realtime, ProductLifecycleState.Running, 1, 1, 1, 1, 60, 1, 0, 1d / 60d),
            [
                PadAxis(ControllerAxis.Axis1, -1f),
                PadAxis(ControllerAxis.Axis2, 1f),
            ]);

            // Full deflection is one unit of planar intent, and the authored start yaw is pi, which
            // wrapping puts just inside the negative end after the stick's positive yaw travel.
            Assert.Equal(new Vector2(0f, 1f), spatial.StepRequests[0].Command.PlanarIntent);
            Assert.Equal(-MathF.PI + (tuning.ControllerInput.LookYawRadiansPerSecond / 60f), session.State.PlayerControl.YawRadians, precision: 4);
        }
    }

    [Fact]
    public void A_pad_button_asks_the_dom_for_the_menu_action_that_opens_that_panel()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));

        List<string> requested = [];
        using (DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults)))
        {
            // Collects a request only where the published revision moved, so the panel a button asks
            // for is read from the projection's own edge rather than from the call that made it.
            void Press(ControllerButton button, ulong step)
            {
                session.Update(new ProductUpdate(OuterUpdate(step), [PadButton(button, InputEdge.Pressed)]));
                string? panel = engine.PublishedNested("panelRequest", "panel");
                string? revision = engine.PublishedNested("panelRequest", "revision");
                if (panel is null || revision is null || requested.Count == int.Parse(revision, CultureInfo.InvariantCulture)) return;
                requested.Add(panel);
            }

            Assert.Null(engine.PublishedNested("panelRequest", "panel"));
            Press(ControllerButton.Button8, 1);
            Assert.Equal("inventory", engine.PublishedNested("panelRequest", "panel"));
            Assert.Equal("1", engine.PublishedNested("panelRequest", "revision"));
            Press(ControllerButton.Button3, 2);
            Assert.Equal("character", engine.PublishedNested("panelRequest", "panel"));
            Assert.Equal("2", engine.PublishedNested("panelRequest", "revision"));
            Press(ControllerButton.Button9, 3);
            Assert.Equal("menu", engine.PublishedNested("panelRequest", "panel"));
            Assert.Equal("3", engine.PublishedNested("panelRequest", "revision"));

            // An action that opens no panel leaves the last request standing rather than clearing it:
            // clearing would retire a request the DOM may not have performed yet.
            Assert.Contains(DaggerfallTuning.Defaults.ControllerInput.Actions, binding => binding.Button == ControllerButton.Button0);
            Press(ControllerButton.Button0, 4);
            Assert.Equal("menu", engine.PublishedNested("panelRequest", "panel"));
            Assert.Equal("3", engine.PublishedNested("panelRequest", "revision"));
        }

        // The panel names are the DOM's own menu actions, so a request opens the panel that the menu's
        // button of the same name opens. There is no DOM harness in this repository, so the agreement
        // is pinned where the DOM spells it: as the data-action of a button in the DOM's own source.
        Assert.Equal(["inventory", "character", "menu"], requested);
        string dom = File.ReadAllText(Path.Combine(root, "src/ui/main.ts"));
        Assert.All(requested, panel => Assert.Contains($"data-action=\"{panel}\"", dom, StringComparison.Ordinal));
    }

    [Fact]
    public void A_payload_axis_or_button_the_engine_does_not_publish_is_refused_rather_than_bound()
    {
        string root = TestData.RepositoryRoot;
        // The Engine publishes four axes and sixteen buttons. An index beyond them is a payload that
        // means a device this build cannot hear, so it is refused rather than clamped to the nearest.
        Assert.Throws<JsonException>(() => DaggerfallTuning.Read(MutatedTuning(root, tuning => tuning["controllerInput"]!["movementXAxis"] = 4)));
        Assert.Throws<JsonException>(() => DaggerfallTuning.Read(MutatedTuning(root, tuning => tuning["controllerInput"]!["actions"]!.AsArray()[0]!["button"] = 16)));
        // Two bindings on one button is a press that cannot mean one thing, which the payload reader
        // refuses the same way the Kit refuses it in code instead of letting the first one win.
        Assert.Throws<ArgumentException>(() => DaggerfallTuning.Read(MutatedTuning(root, tuning => tuning["controllerInput"]!["actions"]!.AsArray()[1]!["button"] = 0)));
        // The same axis in two roles, and a bound action with no name, are the other two ways a pad
        // payload describes a device that cannot work.
        Assert.Throws<ArgumentException>(() => DaggerfallTuning.Read(MutatedTuning(root, tuning => tuning["controllerInput"]!["movementYAxis"] = 0)));
        Assert.Throws<JsonException>(() => DaggerfallTuning.Read(MutatedTuning(root, tuning => tuning["controllerInput"]!["actions"]!.AsArray()[0]!["action"] = "")));
    }

    [StagedProductFact]
    public void The_pad_owns_its_controls_and_the_compiled_product_mapping_declares_none_of_them()
    {
        string root = TestData.RepositoryRoot;
        // Pad bindings are tuning because the Engine's controller vocabulary is positional and a
        // different pad has to be a configuration change rather than a rebuild. That makes the
        // compiled product mapping the wrong place for a controller trigger: declaring one there
        // would describe the same press twice, once as a mapped intent and once as the raw fact the
        // pad tuning reads, and nothing else would notice.
        //
        // The check reads the artifact the runtime reads rather than one spelling in one build file,
        // so it sees any declaration site and any legal MSBuild form. It is build output, so without a
        // staged Host the fact skips and names the build rather than passing without reading it; the
        // gate stages the Host before this suite, so there the guard always reads the manifest.
        string manifest = Path.Combine(root, StagedProductFactAttribute.ManifestPath);
        using JsonDocument product = JsonDocument.Parse(File.ReadAllBytes(manifest));
        string[] triggers = [.. product.RootElement.GetProperty("input").GetProperty("mappings").EnumerateArray()
            .Select(mapping => mapping.GetProperty("trigger").GetString() ?? string.Empty)];
        Assert.NotEmpty(triggers);
        Assert.DoesNotContain(triggers, trigger => trigger.StartsWith("controller-", StringComparison.Ordinal));
        // The ownership claim is only meaningful while the tuning table is the one carrying the pad.
        Assert.NotEmpty(DaggerfallTuning.Defaults.ControllerInput.Actions);
    }

    [Fact]
    public void A_panel_request_is_an_event_that_stops_standing_once_the_dom_has_had_its_chance()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));

        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults));
        session.Update(new ProductUpdate(OuterUpdate(1), [PadButton(ControllerButton.Button8, InputEdge.Pressed)]));
        Assert.Equal("inventory", engine.PublishedNested("panelRequest", "panel"));

        // A request that outlived the page that performed it would re-open a panel nobody asked for on
        // the next load, so it ages out on admitted world time like the message line does.
        for (ulong step = 2; step <= 62; step++) session.Update(new ProductUpdate(OuterUpdate(step), []));
        Assert.Null(engine.PublishedNested("panelRequest", "panel"));
        Assert.Null(session.LatestPanelRequest);
    }

    [Fact]
    public void Two_panel_buttons_in_one_slice_leave_the_later_request_standing()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));

        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults));
        // One admitted slice can carry both presses, and one panel can open: the later press in the
        // fixed order is the one the DOM is asked for, and the earlier one is not silently preferred.
        session.Update(new ProductUpdate(OuterUpdate(1),
        [
            PadButton(ControllerButton.Button8, InputEdge.Pressed),
            PadButton(ControllerButton.Button9, InputEdge.Pressed),
        ]));

        Assert.Equal("menu", engine.PublishedNested("panelRequest", "panel"));
        Assert.Equal("2", engine.PublishedNested("panelRequest", "revision"));
    }

    [Fact]
    public void Typed_input_and_explicit_combat_remain_product_semantic_above_the_normalized_engine_seams()
    {
        InputActionId attack = new("test.attack");
        PlayerControlBindings controls = new(["move"u8.ToArray()], KeyboardControl.KeyW, KeyboardControl.KeyS, KeyboardControl.KeyA, KeyboardControl.KeyD, new DirectionalMovementBindings("forward"u8.ToArray(), "backward"u8.ToArray(), "left"u8.ToArray(), "right"u8.ToArray()));
        PlayerInputSystem input = new(DaggerfallTuning.Defaults.PlayerControl, controls, [new InputActionBinding(attack, "attack"u8.ToArray())]);
        PlayerControlState player = new(new WorldPoint(0, 0, 0), 0, 0);
        ProductUpdateState update = new(.125F);
        update.Add(Input(InputEventKind.Key, InputEdge.Pressed, keyboard: KeyboardControl.KeyW));
        update.Add(Input(InputEventKind.PointerDelta, x: .25F, y: -.5F));
        update.Add(Input(InputEventKind.DirectDigital, x: 1F, phase: InputPhase.DirectUi, intent: "attack"));
        input.Apply(player, update);
        Assert.Equal(new Vector2(0, 1), update.PlanarIntent);
        Assert.True(update.IsRequested(attack));
        Assert.Equal(.25f * DaggerfallTuning.Defaults.PlayerControl.LookSensitivity, player.YawRadians, precision: 6);
        // The ordinary non-inverted look convention turns upward mouse motion into positive pitch.
        Assert.Equal(.5f * DaggerfallTuning.Defaults.PlayerControl.LookSensitivity, player.PitchRadians, precision: 6);

        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults));
        double healthBefore = session.State.Actors.Get(2000).Stats.GetTrack(Rusty.Engine.Mechanics.TrackId.Parse("health")).Current;
        session.State.Actors.Player.Stats.GetStat(StatId.Parse("strength")).BaseValue = 60; // classic damage floors at zero; stage a swing this fixture can rely on
        session.ResolveExplicitMelee(new ExplicitMeleeRequest(1, 2000, 1, 1, .125));
        Assert.True(session.State.Actors.Get(2000).Stats.GetTrack(Rusty.Engine.Mechanics.TrackId.Parse("health")).Current < healthBefore);
    }

    [Fact]
    public void Ordinary_control_actions_replace_engine_mappings_and_survive_restart_and_load()
    {
        static ProductInputEvent Ui(string json) => Input(InputEventKind.DirectDigital) with
        {
            ValueKind = InputValueKind.ProductPayload,
            PayloadContract = "dagger.ui.action.v1"u8.ToArray(),
            PayloadData = Encoding.UTF8.GetBytes(json),
        };
        string root = TestData.RepositoryRoot;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        ProductInputConfiguration input = new(default, default, ReadOnlyMemory<ProductInputDescriptor>.Empty, ReadOnlyMemory<ProductInputMapping>.Empty);
        CapturingDaggerfallRuleset ruleset = new();
        using WorldRpgProduct product = new(new ProductCreateContext(engine.Context, FullContent(root), input), ruleset, new GameBundleId("daggerfall.privateers-hold"));
        product.Start(); product.Begin();
        ProductInputMapping Mapping(string intent) => Assert.Single(engine.PhysicalInput.Mappings, value => Encoding.UTF8.GetString(value.Intent.Span) == intent);
        Assert.Equal(KeyboardControl.KeyW, Mapping("move.forward").Keyboard);
        product.Update(new ProductUpdate(OuterUpdate(1), [Ui("""{"action":"controls-rebind","item":"move.forward","key":"KeyQ"}""")]));
        Assert.Equal(KeyboardControl.KeyQ, Mapping("move.forward").Keyboard);
        product.Restart();
        Assert.Equal(KeyboardControl.KeyQ, Mapping("move.forward").Keyboard);
        product.Update(new ProductUpdate(OuterUpdate(2), [Ui("""{"action":"save-slot","label":"Controls are preferences"}""")]));
        product.Update(new ProductUpdate(OuterUpdate(3), [Ui("""{"action":"controls-rebind","item":"attack","key":"KeyQ"}""")]));
        Assert.Equal(PointerButton.Primary, Mapping("attack").PointerButton);
        Assert.Contains("already answers", engine.PublishedNested("controls", "diagnostic"));
        product.Update(new ProductUpdate(OuterUpdate(4), [Ui("""{"action":"controls-rebind","item":"attack","key":"KeyQ","confirm":true}""")]));
        Assert.Equal(KeyboardControl.KeyQ, Mapping("attack").Keyboard);
        Assert.Equal(PointerButton.Primary, Mapping("move.forward").PointerButton);
        product.Update(new ProductUpdate(OuterUpdate(5), [Ui("""{"action":"load-slot","key":"slot-1"}""")]));
        Assert.Equal(KeyboardControl.KeyQ, Mapping("attack").Keyboard);
        engine.PhysicalInput.Outcome = InputMappingReplacementOutcome.InvalidMappings;
        product.Update(new ProductUpdate(OuterUpdate(6), [Ui("""{"action":"controls-reset"}""")]));
        Assert.Equal(KeyboardControl.KeyQ, Mapping("attack").Keyboard);
        Assert.Contains("InvalidMappings", engine.PublishedNested("controls", "diagnostic"));
        engine.PhysicalInput.Outcome = InputMappingReplacementOutcome.Staged;
        product.Update(new ProductUpdate(OuterUpdate(7), [Ui("""{"action":"controls-reset"}""")]));
        Assert.Equal(KeyboardControl.KeyW, Mapping("move.forward").Keyboard);
        Assert.Equal(PointerButton.Primary, Mapping("attack").PointerButton);
    }

    [Fact]
    public void Activation_mode_change_consumes_a_coincident_attack_without_starting_melee()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        PerceptionFake perception = PerceptionFake.Create();
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), perception.Service);
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults));
        ProductInputEvent mode = Input(InputEventKind.DirectDigital) with
        {
            ValueKind = InputValueKind.ProductPayload,
            PayloadContract = "dagger.ui.action.v1"u8.ToArray(),
            PayloadData = Encoding.UTF8.GetBytes("""{"action":"activation-mode","mode":"info"}"""),
        };
        ProductInputEvent attack = Input(InputEventKind.DirectDigital) with
        {
            ValueKind = InputValueKind.ProductPayload,
            PayloadContract = "dagger.ui.action.v1"u8.ToArray(),
            PayloadData = Encoding.UTF8.GetBytes("""{"action":"attack"}"""),
        };

        session.Update(new ProductUpdate(OuterUpdate(1), [attack, mode]));

        Assert.Equal(DaggerfallActivationMode.Info, session.ActivationMode);
        Assert.Null(session.LastMeleeTargeting);
        Assert.False(session.ActivationView.Applied);
    }

    [Fact]
    public void Direct_interaction_input_consumes_a_coincident_direct_attack()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), PerceptionFake.Create().Service);
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults));

        session.Update(new ProductUpdate(OuterUpdate(1), [
            Input(InputEventKind.DirectDigital, x: 1f, phase: InputPhase.DirectUi, intent: "attack"),
            Input(InputEventKind.DirectDigital, x: 1f, phase: InputPhase.DirectUi, intent: "interact"),
        ]));

        Assert.Null(session.LastMeleeTargeting);
        Assert.Contains("No eligible target", session.ActivationView.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_slice_that_opens_an_interaction_admits_no_attack_in_either_order()
    {
        static ProductInputEvent Ui(string json) => Input(InputEventKind.DirectDigital) with
        {
            ValueKind = InputValueKind.ProductPayload,
            PayloadContract = "dagger.ui.action.v1"u8.ToArray(),
            PayloadData = Encoding.UTF8.GetBytes(json),
        };

        // Calibration, in a session that has not swung yet: the melee targeting policy records its
        // evidence before any admit or target check, so an attack alone in ordinary play proves the
        // payload reached combat. The loot fixture below cannot calibrate this way, because the melee
        // that registers its corpse leaves the weapon mid-strike and `CanStartPlayerAttack` false.
        using (DaggerfallSession control = FreshSession())
        {
            control.Update(new ProductUpdate(OuterUpdate(1), [Ui("{\"action\":\"attack\"}")]));
            Assert.NotNull(control.LastMeleeTargeting);
        }

        // Either delivery order: the interaction still opens, so the pre-scan does not consume the
        // interaction it is protecting. The attack half of this pair cannot be observed in this
        // fixture for the strike reason above; the calibration session is what establishes the path.
        ProductInputEvent[][] slices =
        [
            [Ui("{\"action\":\"loot\"}"), Ui("{\"action\":\"attack\"}")],
            [Ui("{\"action\":\"attack\"}"), Ui("{\"action\":\"loot\"}")],
        ];
        foreach (ProductInputEvent[] slice in slices)
        {
            using DaggerfallSession session = LootableSession(out _);
            session.Update(new ProductUpdate(OuterUpdate(1), slice));
            Assert.Equal(ProductMode.Modal, session.PendingModeRequest);
        }
    }

    [Fact]
    public void The_gate_that_drops_a_batched_attack_is_sensitive_once_the_weapon_is_ready()
    {
        static ProductInputEvent Ui(string json) => Input(InputEventKind.DirectDigital) with
        {
            ValueKind = InputValueKind.ProductPayload,
            PayloadContract = "dagger.ui.action.v1"u8.ToArray(),
            PayloadData = Encoding.UTF8.GetBytes(json),
        };

        // A ready weapon needs the setup melee's strike latch cleared: the fixture's own swing leaves
        // CanStartPlayerAttack false until a Completed playback arrives, which is why every post-setup
        // attack negative passed with or without the gate.
        static void Ready(DaggerfallSession session, AppearanceFake appearance)
        {
            appearance.AdvanceReceiptForAll = CompletedMarker(1);
            session.Update(new ProductUpdate(OuterUpdate(1), []));
            session.Update(new ProductUpdate(OuterUpdate(2), []));
            appearance.AdvanceReceiptForAll = null;
        }

        // The batched negative, in both delivery orders: an attack in the same admitted slice as the
        // interaction reaches no combat, and this one is sensitive - with the gate deleted the melee
        // runs and the evidence is recorded.
        ProductInputEvent[][] slices =
        [
            [Ui("{\"action\":\"loot\"}"), Ui("{\"action\":\"attack\"}")],
            [Ui("{\"action\":\"attack\"}"), Ui("{\"action\":\"loot\"}")],
        ];
        foreach (ProductInputEvent[] slice in slices)
        {
            using DaggerfallSession batched = LootableSession(out AppearanceFake appearance);
            Ready(batched, appearance);
            batched.Update(new ProductUpdate(OuterUpdate(3), slice));
            Assert.Equal(ProductMode.Modal, batched.PendingModeRequest);
            Assert.Null(batched.LastMeleeTargeting);
        }

        // The same-session positive control the negative needs: an attack alone reaches combat.
        using DaggerfallSession control = LootableSession(out AppearanceFake controlAppearance);
        Ready(control, controlAppearance);
        control.Update(new ProductUpdate(OuterUpdate(3), [Ui("{\"action\":\"attack\"}")]));
        Assert.NotNull(control.LastMeleeTargeting);
    }

    /// <summary>A session with one lootable corpse within reach, and the appearance it drives.</summary>
    private static DaggerfallSession LootableSession(out AppearanceFake appearance)
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        spatial.KeepPosition = true;
        PerceptionFake perception = PerceptionFake.Create();
        appearance = new AppearanceFake(releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, appearance, perception.Service);
        DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults));
        session.State.Actors.Get(2000).Stats.GetTrack(TrackId.Parse("health")).SetCurrent(1, clamp: true);
        session.State.Actors.Player.Stats.GetStat(StatId.Parse("strength")).BaseValue = 60; // classic damage floors at zero; stage a swing this fixture can rely on
        session.ResolveExplicitMelee(new ExplicitMeleeRequest(1, 2000, 1, 1, .125));
        AimActivationAt(session, 2000);
        CorpseContainer corpse = session.Corpses[2000];
        session.State.Containers.Seed(corpse.Owner, [new InventoryContainerSeed(new InventoryItemId("gold-piece"), 5, Stack: InventoryStackId.Parse("test.loot.3486"))]);
        RegisterCorpseStack(session, definitions, 2000, "test.loot.3486");
        perception.Receipt = Receipt(new PerceptionPair(1, 2000, 2.25d, .5d, PerceptionPairKind.Visible, 1d));
        return session;
    }

    /// <summary>One physical controller axis event, as the shell publishes it.</summary>
    private static ProductInputEvent PadAxis(ControllerAxis axis, float value) =>
        Input(InputEventKind.ControllerAxis, x: value) with
        {
            Device = InputDevice.Controller,
            Channel = InputChannel.Axis,
            ValueKind = InputValueKind.Axis,
            ControllerAxis = axis,
            Phase = InputPhase.Axis,
            Provenance = InputProvenance.Physical,
        };

    /// <summary>
    /// Field-by-field pad agreement. The record holds a list, so record equality compares that list by
    /// reference and would call two identical payloads different.
    /// </summary>
    private static void AssertSamePad(ControllerInputTuning expected, ControllerInputTuning actual)
    {
        Assert.Equal(expected.MovementX, actual.MovementX);
        Assert.Equal(expected.MovementY, actual.MovementY);
        Assert.Equal(expected.LookX, actual.LookX);
        Assert.Equal(expected.LookY, actual.LookY);
        Assert.Equal(expected.MovementDeadzone, actual.MovementDeadzone);
        Assert.Equal(expected.LookDeadzone, actual.LookDeadzone);
        Assert.Equal(expected.MovementStrafeSensitivity, actual.MovementStrafeSensitivity);
        Assert.Equal(expected.MovementForwardSensitivity, actual.MovementForwardSensitivity);
        Assert.Equal(expected.LookYawRadiansPerSecond, actual.LookYawRadiansPerSecond);
        Assert.Equal(expected.LookPitchRadiansPerSecond, actual.LookPitchRadiansPerSecond);
        Assert.Equal(expected.InvertMovementX, actual.InvertMovementX);
        Assert.Equal(expected.InvertMovementY, actual.InvertMovementY);
        Assert.Equal(expected.InvertLookX, actual.InvertLookX);
        Assert.Equal(expected.InvertLookY, actual.InvertLookY);
        Assert.Equal(expected.Actions.Select(binding => (binding.Button, binding.Action)), actual.Actions.Select(binding => (binding.Button, binding.Action)));
    }
}
