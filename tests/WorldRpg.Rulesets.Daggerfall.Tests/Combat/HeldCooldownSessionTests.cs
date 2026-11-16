using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Host;
using WorldRpg.Kit;
using WorldRpg.Kit.Combat;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Behavior;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// A held world holds Engine gameplay time: a pause, the game menu or any other screen asks the Engine to admit no
/// step, so nothing the world counts in steps (an attack's cooldown, an enemy's wind-up) ages behind it. Updates here
/// come as the Engine sends them: one step each while gameplay time runs, none while it is held. The one slice that
/// still counts is the update that opens a screen: its steps were admitted before the hold could be asked for, and the
/// world does not take them, so a step-indexed timer runs that slice as it would have with no screen at all.
/// </summary>
public sealed class HeldCooldownSessionTests
{
    private const ulong Charged = 30;
    private const int Held = 100;

    /// <summary>
    /// The Engine as it admits steps: one an observation while gameplay time runs, none while it is held, whether or
    /// not the product forwards the update to its session.
    /// </summary>
    private sealed class EngineClock(GameplayTimeFake time)
    {
        private ulong _observation = 1;
        internal ulong Next { get; private set; } = 1;
        internal ProductUpdateFacts Facts()
        {
            uint admitted = time.Read().Rate == 0d ? 0u : 1u;
            ProductUpdateFacts facts = new(ProductLifecycleState.Running, 1, 1, _observation++, Next, 60, admitted, 0, 1d / 60d);
            Next += admitted;
            return facts;
        }
    }

    private static ProductInputEvent Menu(bool open) => Ui(open ? """{"action":"menu","open":true}""" : """{"action":"menu","open":false}""");

    [Fact]
    public void A_charged_cooldown_holds_across_a_pause_and_behind_the_game_menu()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        EngineContextFake engine = EngineContextFake.Create(content,
            SpatialFake.Create(inputs.Geometry.Spatial[0].Sha256, releases).Service, new AppearanceFake(releases));
        ProductInputConfiguration input = new(default, default, ReadOnlyMemory<ProductInputDescriptor>.Empty, ReadOnlyMemory<ProductInputMapping>.Empty);
        using WorldRpgProduct product = new(new ProductCreateContext(engine.Context, FullContent(root), input),
            new TestDaggerfallRuleset(videosEnabled: false), new GameBundleId("daggerfall.classic"));
        product.Start();
        NewGameSessionTests.Commit(RunningSession(product));
        product.Begin();
        DaggerfallSession session = RunningSession(product);
        EngineClock clock = new(engine.GameplayTime);
        void Update(params ProductInputEvent[] events) => product.Update(new ProductUpdate(clock.Facts(), events));
        ulong Remaining() => Assert.Single(session.State.Kit.AttackExecution.CaptureCooldowns(1, clock.Next - 1),
            cooldown => cooldown.AttackerId == DaggerfallActorIdentity.PlayerEntityId).RemainingSteps;

        // Charged on the step the world takes next: thirty steps to run from there.
        session.State.Kit.AttackExecution.RestoreCooldowns([new AttackCooldown(DaggerfallActorIdentity.PlayerEntityId, Charged)]);
        Update();
        Assert.Equal(Charged, Remaining());

        // A host pause forwards no update, so the session holds gameplay time as the mode changes.
        product.Pause();
        Assert.Equal(0d, engine.GameplayTime.Read().Rate);
        for (int i = 0; i < Held; i++) Update();
        product.Resume();
        Update(); // Admitted while still held; it asks for time again.
        Update();
        Assert.Equal(Charged - 1, Remaining());

        // The game menu holds the world from the update that opens it, which had already admitted its step.
        Update(Menu(true));
        Assert.Equal(ProductMode.Modal, product.Mode);
        Assert.Equal(0d, engine.GameplayTime.Read().Rate);
        for (int i = 0; i < Held; i++) Update();
        Update(Menu(false));
        Assert.Equal(ProductMode.Playing, product.Mode);
        Update();
        Update();
        Assert.Equal(Charged - 3, Remaining());
    }

    [Fact]
    public void An_enemy_wind_up_lands_at_its_remaining_steps_after_the_game_menu() =>
        WindUpAcrossAHold((session, tick, _) =>
        {
            tick([Menu(true)]);
            Assert.Equal(ProductMode.Modal, session.PendingModeRequest);
            session.ApplyProductMode(ProductMode.Modal);
            for (int i = 0; i < Held; i++) tick([]);
            tick([Menu(false)]);
            session.ApplyProductMode(ProductMode.Playing);
        });

    [Fact]
    public void An_enemy_wind_up_lands_at_its_remaining_steps_after_a_pause() =>
        // The host forwards nothing while paused; the hold the mode change asked for is what keeps the steps.
        WindUpAcrossAHold((session, _, unforwarded) =>
        {
            session.ApplyProductMode(ProductMode.Paused);
            for (int i = 0; i < Held; i++) unforwarded();
            session.ApplyProductMode(ProductMode.Playing);
        });

    private static void WindUpAcrossAHold(Action<DaggerfallSession, Action<ProductInputEvent[]>, Action> hold)
    {
        string root = TestData.RepositoryRoot;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.Geometry.Spatial[0].Sha256, releases);
        PerceptionFake perception = PerceptionFake.Create();
        perception.Receipt = Receipt(new PerceptionPair((ulong)HoldActor(1), 1, 1d, 1d, PerceptionPairKind.Visible, 1d));
        AppearanceFake appearance = new(releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, appearance, perception.Service);
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(TestPayload.Definitions, inputs, DaggerfallTuning.Defaults));
        session.ApplyProductMode(ProductMode.Playing);
        EngineClock clock = new(engine.GameplayTime);
        void Tick(ProductInputEvent[] events) => session.Update(new ProductUpdate(clock.Facts(), events));
        double Health() => session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).Current;
        double before = Health();
        session.Touches(DaggerfallActorIdentity.PlayerEntityId);

        // Decided on step 1, the blow is due when the Engine's count reaches the wind-up's end.
        appearance.AdvanceReceiptForAll = CrossedMarker(1, markerId: AuthoredMeleeMarker(HoldActor(1)));
        Tick([]);
        Assert.Equal(EnemyBehaviorState.Attack, session.EnemyBehavior.LastEvidence[HoldActor(1)].State);
        appearance.AdvanceReceiptForAll = null;
        while (clock.Next < 5) Tick([]);

        hold(session, Tick, () => clock.Facts());
        Assert.Equal(before, Health());
        Assert.True(clock.Next <= 6, "the hold admitted no step but the opening slice");

        // The world runs again, and the blow lands exactly as its wind-up runs out on the steps the world was given.
        while (clock.Next <= WindUpEnd(1))
        {
            Assert.Equal(before, Health());
            Tick([]);
        }
        Assert.True(Health() < before);
    }
}
