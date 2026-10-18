using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Combat;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>The contact variant's active block through the live session: time admission, the swing lifetime and deferred acts.</summary>
public sealed class ContactGuardSessionTests
{
    private static readonly ProductInputEvent HoldBlock = Input(InputEventKind.MappedDigital, InputEdge.Pressed, x: 1, phase: InputPhase.Pressed, intent: "block");
    private static readonly ProductInputEvent ReleaseBlock = Input(InputEventKind.MappedDigital, InputEdge.Released, x: 0, phase: InputPhase.Released, intent: "block");

    [Fact]
    public void Holding_block_while_action_driven_time_is_idle_runs_time_to_raise_the_guard_spend_stamina_and_settle_its_release()
    {
        using Fixture f = new(contact: true);
        f.Step();
        f.Session.State.WeaponDrawn = true;
        f.Observe(Ui("""{"action":"action-driven-time","enabled":true}"""));
        Assert.True(f.Session.PlayerTimeHeld);

        f.Observe(HoldBlock);
        Assert.False(f.Session.PlayerTimeHeld);
        Assert.False(f.Session.PlayerGuardRaised);
        f.Step();
        Assert.True(f.Session.PlayerGuardRaised);

        double raised = f.Stamina;
        f.Step();
        Assert.True(f.Stamina < raised);
        f.Observe();
        Assert.False(f.Session.PlayerTimeHeld);

        f.Observe(ReleaseBlock);
        Assert.True(f.Session.PlayerGuardRaised);
        Assert.False(f.Session.PlayerTimeHeld);
        f.Step();
        Assert.False(f.Session.PlayerGuardRaised);
        f.Observe();
        Assert.True(f.Session.PlayerTimeHeld);
    }

    [Fact]
    public void Classic_reads_no_block_and_keeps_idle_time_held()
    {
        using Fixture f = new(contact: false);
        f.Step();
        f.Session.State.WeaponDrawn = true;
        f.Observe(Ui("""{"action":"action-driven-time","enabled":true}"""));
        f.Observe(HoldBlock);
        Assert.True(f.Session.PlayerTimeHeld);
        Assert.False(f.Session.PlayerGuardRaised);
    }

    [Fact]
    public void A_swing_keeps_the_guard_down_past_its_hit_frame_until_its_strike_completes()
    {
        using Fixture f = new(contact: true);
        f.Step();
        f.Session.State.WeaponDrawn = true;
        f.Observe(HoldBlock);
        f.Step();
        Assert.True(f.Session.PlayerGuardRaised);

        f.Observe(Input(InputEventKind.MappedDigital, InputEdge.Pressed, x: 1, phase: InputPhase.Pressed, intent: "attack"));
        f.Step();
        Assert.NotNull(f.Session.State.Actors.Player.Actor.Get<AttackState>().Pending);
        Assert.False(f.Session.PlayerGuardRaised);

        // The hit frame resolves the swing, but its strike still plays.
        f.Appearance.AdvanceReceiptForAll = CrossedMarker(1, markerId: 1);
        f.Step();
        Assert.Null(f.Session.State.Actors.Player.Actor.Get<AttackState>().Pending);
        Assert.False(f.Session.PlayerGuardRaised);
        f.Step();
        Assert.False(f.Session.PlayerGuardRaised);

        f.Appearance.AdvanceReceiptForAll = CompletedMarker(2);
        for (int i = 0; i < 3 && !f.Session.PlayerGuardRaised; i++) f.Step();
        Assert.True(f.Session.PlayerGuardRaised);
    }

    [Fact]
    public void A_swing_deferred_behind_an_enemy_hit_lowers_the_guard_when_it_is_admitted()
    {
        List<string> releases = [];
        (DaggerfallSession session, AppearanceFake _, _) = VisibleEnemySession(releases, contact: true);
        using DaggerfallSession disposable = session;
        session.State.WeaponDrawn = true;
        ProductInputEvent block = Input(InputEventKind.DirectDigital, x: 1f, phase: InputPhase.DirectUi, intent: "block");

        // The enemy decides a swing at the player whose frame is not reached; the player's guard rises.
        session.Update(new ProductUpdate(OuterUpdate(1), [block]));
        Assert.True(session.PlayerGuardRaised);

        // The player's swing waits behind that enemy frame, then is admitted at the update's end.
        session.Update(new ProductUpdate(OuterUpdate(2), [Input(InputEventKind.DirectDigital, x: 1f, phase: InputPhase.DirectUi, intent: "attack")]));
        Assert.NotNull(session.State.Actors.Player.Actor.Get<AttackState>().Pending);
        Assert.False(session.PlayerGuardRaised);
    }

    private sealed class Fixture : IDisposable
    {
        internal readonly AppearanceFake Appearance;
        internal readonly EngineContextFake Engine;
        internal readonly DaggerfallSession Session;
        private ulong _step = 1;
        internal Fixture(bool contact)
        {
            var inputs = ReadInputs(TestData.RepositoryRoot);
            List<string> releases = [];
            ContentFake content = new(releases); PopulateContent(content, inputs);
            SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
            Appearance = new(releases);
            Engine = EngineContextFake.Create(content, spatial.Service, Appearance);
            Session = DaggerfallSession.StartNew(Engine.Context, contact ? ContactComposition(inputs) : new(TestPayload.Definitions, inputs, DaggerfallTuning.Defaults));
        }
        internal double Stamina => Session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("stamina")).Current;
        internal void Observe(params ProductInputEvent[] input) => Session.Update(new ProductUpdate(
            new ProductUpdateFacts(ProductLifecycleState.Running, 1, 1, 1, _step, 60, 0, 0, 1d / 60d,
                true, Engine.GameplayTime.Read().Rate, 0, .02), input));
        internal void Step() => Session.Update(new ProductUpdate(OuterUpdate(_step++), []));
        public void Dispose() => Session.Dispose();
    }
}
