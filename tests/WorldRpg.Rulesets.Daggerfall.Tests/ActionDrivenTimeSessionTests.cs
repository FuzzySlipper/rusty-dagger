using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Host;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class ActionDrivenTimeSessionTests
{
    [Fact]
    public void Held_observations_accept_look_menus_and_preferences_without_advancing_the_world()
    {
        using Fixture f = new();
        f.Step(); // Establish grounding through the ordinary controller.
        f.Session.State.PlayerControl.Motion = f.Session.State.PlayerControl.Motion with
        { ControlledVelocity = new System.Numerics.Vector3(0, -.5f, 0) }; // Engine ground adhesion is not activity.
        f.Observe(Ui("""{"action":"action-driven-time","enabled":true}"""));
        Assert.True(f.Session.PlayerTimeHeld);
        int steps = f.Spatial.StepCalls;
        var position = f.Session.State.PlayerControl.Position;
        float yaw = f.Session.State.PlayerControl.YawRadians;
        double health = f.Session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).Current;
        string calendar = f.Engine.PublishedNested("calendar", "time")!;
        f.Observe(Input(InputEventKind.PointerDelta, x: 30));
        f.Observe(Input(InputEventKind.ControllerAxis, x: .5f) with { ControllerAxis = ControllerAxis.Axis2 });
        Assert.NotEqual(yaw, f.Session.State.PlayerControl.YawRadians);
        Assert.Equal(position, f.Session.State.PlayerControl.Position);
        Assert.Equal(steps, f.Spatial.StepCalls);
        Assert.Equal(health, f.Session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).Current);
        Assert.Equal(calendar, f.Engine.PublishedNested("calendar", "time"));
        Assert.True(f.Session.PlayerTimeHeld);
        Assert.True(DaggerfallControlSettings.Parse(f.Session.TakePlayerPreferencesSave()!).ActionDrivenTime);
        f.Observe(Input(InputEventKind.MappedDigital, InputEdge.Pressed, x: 1, phase: InputPhase.Pressed, intent: "inventory"));
        Assert.Equal("inventory", f.Engine.PublishedNested("panelRequest", "panel"));
        Assert.Equal(steps, f.Spatial.StepCalls);
        f.Observe(Ui("""{"action":"menu","open":true}"""));
        f.Observe(Ui("""{"action":"action-driven-time","enabled":false}"""));
        Assert.False(f.Session.PlayerTimeHeld);
        Assert.Equal(1, f.Engine.GameplayTime.Read().Rate);
        Assert.False(DaggerfallControlSettings.Parse(f.Session.CapturePlayerPreferences()).ActionDrivenTime);
    }

    [Fact]
    public void Movement_and_attack_wake_time_and_stop_after_release_or_action_completion()
    {
        using Fixture f = new();
        f.Step();
        f.Observe(Ui("""{"action":"action-driven-time","enabled":true}"""));
        f.Observe(Input(InputEventKind.Key, InputEdge.Pressed, keyboard: KeyboardControl.KeyW));
        Assert.False(f.Session.PlayerTimeHeld);
        f.Step();
        Assert.NotEqual(System.Numerics.Vector2.Zero, f.Spatial.StepRequests.Last().Command.PlanarIntent);
        f.Observe(Input(InputEventKind.Key, InputEdge.Released, keyboard: KeyboardControl.KeyW));
        Assert.True(f.Session.PlayerTimeHeld);
        double before = f.Session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("stamina")).Current;
        f.Observe(Input(InputEventKind.MappedDigital, InputEdge.Pressed, x: 1, phase: InputPhase.Pressed, intent: "attack"));
        f.Observe();
        Assert.False(f.Session.PlayerTimeHeld);
        Assert.Equal(before, f.Session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("stamina")).Current);
        f.Step();
        double after = f.Session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("stamina")).Current;
        Assert.True(after < before);
        Assert.False(f.Session.PlayerTimeHeld);
        f.Appearance.AdvanceReceiptForAll = CompletedMarker(1);
        f.Step(1000); // The admitted timeline passes the attack's recovery, with a completed sprite receipt.
        Assert.False(f.Session.PlayerTimeHeld); // Keep its final frame visible for one admitted update.
        f.Step();
        Assert.True(f.Session.PlayerTimeHeld);
        f.Observe();
        Assert.Equal(after, f.Session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("stamina")).Current);
        f.Observe(Input(InputEventKind.Key, InputEdge.Pressed, keyboard: KeyboardControl.Space));
        Assert.False(f.Session.PlayerTimeHeld);
        f.Observe(Input(InputEventKind.Key, InputEdge.Released, keyboard: KeyboardControl.Space));
        f.Step();
        Assert.True(f.Spatial.StepRequests.Last().Command.JumpPressed);
        f.Observe(Input(InputEventKind.Key, InputEdge.Pressed, keyboard: KeyboardControl.ControlLeft));
        Assert.False(f.Session.PlayerTimeHeld);
        f.Step();
        // The fake leaves stance unchanged, like a controller unable to perform the requested change.
        // A blocked stance request consumes one attempt rather than keeping time running forever.
        Assert.True(f.Session.PlayerTimeHeld);
        f.Observe(Input(InputEventKind.Key, InputEdge.Released, keyboard: KeyboardControl.ControlLeft));
        Assert.False(f.Session.PlayerTimeHeld);
        f.Step();
        Assert.True(f.Session.PlayerTimeHeld);
    }

    [Fact]
    public void Applying_preferences_and_mode_changes_reselects_the_time_policy()
    {
        using Fixture f = new();
        DaggerfallControlSettings settings = new();
        settings.SetActionDrivenTime(true);
        f.Session.ApplyPlayerPreferences(settings.Serialize());
        f.Step();
        Assert.True(f.Session.PlayerTimeHeld);
        f.Session.ApplyProductMode(ProductMode.Title);
        f.Observe();
        Assert.Equal(1, f.Engine.GameplayTime.Read().Rate);
        f.Session.ApplyProductMode(ProductMode.Playing);
        f.Observe();
        Assert.True(f.Session.PlayerTimeHeld);
        f.Session.ApplyPlayerPreferences(null);
        f.Observe();
        Assert.Equal(1, f.Engine.GameplayTime.Read().Rate);
    }

    [Fact]
    public void Host_quick_load_preserves_the_saved_player_preference_during_a_held_update()
    {
        var inputs = ReadInputs(TestData.RepositoryRoot);
        List<string> releases = [];
        ContentFake content = new(releases); PopulateContent(content, inputs);
        var spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        var engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        CapturingDaggerfallRuleset ruleset = new(videosEnabled: false);
        ProductInputConfiguration input = new(default, default, ReadOnlyMemory<ProductInputDescriptor>.Empty, ReadOnlyMemory<ProductInputMapping>.Empty);
        using WorldRpgProduct product = new(new ProductCreateContext(engine.Context, FullContent(TestData.RepositoryRoot), input),
            ruleset, new GameBundleId("daggerfall.classic"));
        product.Start();
        NewGameSessionTests.Commit(ruleset.RequireSession());
        product.Begin();
        product.Update(new ProductUpdate(OuterUpdate(1), []));
        ProductUpdateFacts held = new(ProductLifecycleState.Running, 1, 1, 1, 2, 60, 0, 0, 1d / 60d, true, 0, 0, .02);
        product.Update(new ProductUpdate(held, [Ui("""{"action":"action-driven-time","enabled":true}""")]));
        Assert.True(ruleset.RequireSession().ActionDrivenPlay);
        product.Update(new ProductUpdate(held, [Ui("""{"action":"save-game"}""")]));
        DaggerfallSession saved = ruleset.RequireSession();
        product.Update(new ProductUpdate(held, [Ui("""{"action":"load-game"}""")]));
        Assert.NotSame(saved, ruleset.RequireSession());
        Assert.True(ruleset.RequireSession().ActionDrivenPlay);
        product.Update(new ProductUpdate(held, []));
        product.Update(new ProductUpdate(OuterUpdate(2), [])); // Re-establish the loaded controller's grounding.
        Assert.True(ruleset.RequireSession().PlayerTimeHeld);
    }

    [Theory]
    [InlineData("{\"action\":\"action-driven-time\",\"enabled\":true}", true)]
    [InlineData("{\"action\":\"action-driven-time\",\"enabled\":false}", true)]
    [InlineData("{\"action\":\"action-driven-time\"}", false)]
    [InlineData("{\"action\":\"action-driven-time\",\"enabled\":\"yes\"}", false)]
    public void Toggle_contract_requires_a_boolean_choice(string json, bool valid) =>
        Assert.Equal(valid, DaggerfallUiAction.Parse(System.Text.Encoding.UTF8.GetBytes(json)) is not null);

    private sealed class Fixture : IDisposable
    {
        internal readonly SpatialFake Spatial;
        internal readonly AppearanceFake Appearance;
        internal readonly EngineContextFake Engine;
        internal readonly DaggerfallSession Session;
        private ulong _step = 1;
        internal Fixture()
        {
            var inputs = ReadInputs(TestData.RepositoryRoot);
            List<string> releases = [];
            ContentFake content = new(releases); PopulateContent(content, inputs);
            Spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
            Appearance = new(releases);
            Engine = EngineContextFake.Create(content, Spatial.Service, Appearance);
            Session = DaggerfallSession.StartNew(Engine.Context, new(TestPayload.Definitions, inputs, DaggerfallTuning.Defaults));
        }
        internal void Observe(params ProductInputEvent[] input) => Session.Update(new ProductUpdate(
            new ProductUpdateFacts(ProductLifecycleState.Running, 1, 1, 1, _step, 60, 0, 0, 1d / 60d,
                true, Engine.GameplayTime.Read().Rate, 0, .02), input));
        internal void Step(ulong? at = null)
        {
            if (at is { } value) _step = value;
            Session.Update(new ProductUpdate(OuterUpdate(_step++), []));
        }
        public void Dispose() => Session.Dispose();
    }
}
