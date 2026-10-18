using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Host;
using WorldRpg.Kit;
using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Facts;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.Variants;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// End-to-end acceptance of the <c>daggerfall.contact</c> bundle: every delivery reaches by contact in a game
/// composed from the bundle, and a save made under it loads under it and is refused under classic.
/// </summary>
public sealed class ContactVariantAcceptanceTests
{
    private const long Player = DaggerfallActorIdentity.PlayerEntityId;
    private const long Enemy = 2000;

    [Fact]
    public void A_contact_game_saves_and_loads_under_its_bundle_and_classic_refuses_the_save()
    {
        string root = TestData.RepositoryRoot;
        InMemoryPersistenceService persistence = new();
        ProductInputConfiguration input = new(default, default, ReadOnlyMemory<ProductInputDescriptor>.Empty, ReadOnlyMemory<ProductInputMapping>.Empty);

        (EngineContextFake engine, CapturingDaggerfallRuleset ruleset) = Product(persistence);
        using (WorldRpgProduct product = new(new ProductCreateContext(engine.Context, FullContent(root), input), ruleset, new GameBundleId("daggerfall.contact")))
        {
            product.Start();
            NewGameSessionTests.Commit(ruleset.RequireSession());
            product.Begin();
            DaggerfallSession game = ruleset.RequireSession();
            // The contact owners are live: a held block raises the guard classic does not have.
            game.State.WeaponDrawn = true;
            product.Update(new ProductUpdate(OuterUpdate(1), [Input(InputEventKind.DirectDigital, x: 1f, phase: InputPhase.DirectUi, intent: "block")]));
            Assert.True(game.PlayerGuardRaised);
            game.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).SetCurrent(11);

            product.Update(new ProductUpdate(OuterUpdate(2), [Ui("{\"action\":\"save-slot\",\"label\":\"Contact run\"}")]));
            product.Update(new ProductUpdate(OuterUpdate(3), []));
            Assert.Contains("Saved 'Contact run'", engine.PublishedField("lastOutcome"), StringComparison.Ordinal);
            product.Update(new ProductUpdate(OuterUpdate(4), [Ui("{\"action\":\"load-slot\",\"key\":\"slot-1\"}")]));
            DaggerfallSession loaded = ruleset.RequireSession();
            Assert.NotSame(game, loaded);
            Assert.Equal(11, loaded.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).Current);
        }

        (EngineContextFake classicEngine, CapturingDaggerfallRuleset classicRuleset) = Product(persistence);
        using (WorldRpgProduct classic = new(new ProductCreateContext(classicEngine.Context, FullContent(root), input), classicRuleset, new GameBundleId("daggerfall.classic")))
        {
            classic.Start();
            DaggerfallSession title = classicRuleset.RequireSession();
            classic.Update(new ProductUpdate(OuterUpdate(1), [Ui("{\"action\":\"load-slot\",\"key\":\"slot-1\"}")]));
            Assert.Same(title, classicRuleset.RequireSession());
            string outcome = classicEngine.PublishedField("lastOutcome")!;
            Assert.StartsWith("Load failed:", outcome, StringComparison.Ordinal);
            Assert.Contains(DaggerfallVariants.Contact.Value, outcome, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_player_swing_lands_on_the_enemy_its_volume_touches_at_the_hit_frame()
    {
        List<string> releases = [];
        (DaggerfallSession session, AppearanceFake appearance, _) = VisibleEnemySession(releases, out SpatialFake spatial, contact: true, random: RandomMaximum.Create());
        using DaggerfallSession disposable = session;
        session.State.WeaponDrawn = true;
        spatial.OverlapContactsResult = _ => Touching(session.State.Actors.Get(Enemy).Actor.Entity.Value);
        double health = Health(session, Enemy);

        session.Update(new ProductUpdate(OuterUpdate(1), [Input(InputEventKind.DirectDigital, x: 1f, phase: InputPhase.DirectUi, intent: "attack")]));
        Assert.NotNull(session.State.Actors.Player.Attack.Pending);
        Assert.Equal(health, Health(session, Enemy));

        appearance.AdvanceReceiptForAll = CrossedMarker(1, markerId: 1);
        session.Update(new ProductUpdate(OuterUpdate(2), []));

        Assert.Null(session.State.Actors.Player.Attack.Pending);
        Assert.Contains(spatial.OverlapContactsRequests, request => request.Shape.Kind == SpatialShapeKind.Box);
        Assert.True(Health(session, Enemy) < health, session.ReadPlaytestObservation().Message);
    }

    [Fact]
    public void An_enemy_swing_lands_on_the_player_its_volume_touches_once_its_wind_up_has_passed()
    {
        List<string> releases = [];
        double reach;
        {
            (DaggerfallSession probe, _, _) = VisibleEnemySession([]);
            using DaggerfallSession disposableProbe = probe;
            reach = probe.State.Kit.Attacks.ReachOf(Enemy)!.Value;
        }
        (DaggerfallSession session, AppearanceFake appearance, _) = VisibleEnemySession(releases, out SpatialFake spatial, distance: reach * .5d, contact: true, random: RandomMaximum.Create());
        using DaggerfallSession disposable = session;
        spatial.OverlapContactsResult = _ => Touching(session.State.Actors.Player.Actor.Entity.Value);
        double health = Health(session, Player);

        session.Update(new ProductUpdate(OuterUpdate(1), []));
        Assert.NotNull(session.State.Actors.Get(Enemy).Attack.Pending);

        // The strike's frame comes at once; the blow waits for the published wind-up, then connects.
        appearance.AdvanceReceiptForAll = CrossedMarker(1, markerId: AuthoredMeleeMarker(Enemy));
        session.Update(new ProductUpdate(OuterUpdate(2), []));
        Assert.Equal(health, Health(session, Player));
        appearance.AdvanceReceiptForAll = null;
        session.Update(new ProductUpdate(OuterUpdate(2) with { SimulationStep = 62, AdmittedStepCount = 60 }, []));

        Assert.True(Health(session, Player) < health, session.ReadPlaytestObservation().Message);
    }

    [Fact]
    public void A_released_player_missile_flies_through_ordinary_updates_to_the_enemy_it_meets()
    {
        List<string> releases = [];
        // Far enough that the enemy only closes, so no combat line replaces the spell's.
        (DaggerfallSession session, _, _) = VisibleEnemySession(releases, out SpatialFake spatial, distance: 20d, contact: true, random: RandomMaximum.Create());
        using DaggerfallSession disposable = session;
        Track magicka = session.State.Actors.Player.Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Magicka.Value));
        magicka.Maximum.BaseValue = 1000;
        magicka.SetCurrent(1000);
        ulong enemy = session.State.Actors.Get(Enemy).Actor.Entity.Value;
        // The missile's level segments meet the enemy's body; the controller's downward probes still find no floor.
        spatial.FloorHit = request => MathF.Abs(request.Direction.Y) < .5f
            ? new SpatialHit(true, SpatialHitKind.Entity, enemy, 0, 0, 0, 0, 0, 0, default, request.Origin + request.Direction * .1f, -request.Direction, .1d, 0d, 0d, false, true)
            : default;
        string spell = Learn(session, DaggerfallSpellTarget.SingleTargetAtRange);

        session.Update(new ProductUpdate(OuterUpdate(1), [Ui($$"""{"action":"spell-ready","key":"{{spell}}"}""")]));
        session.Update(new ProductUpdate(OuterUpdate(2), [Ui("""{"action":"spell-cast"}""")]));
        Assert.True(magicka.Current < 1000);
        for (ulong step = 3; step <= 60 && session.Casting.PendingRangedFlights.Count != 0; step++)
            session.Update(new ProductUpdate(OuterUpdate(step), []));

        Assert.Empty(session.Casting.PendingRangedFlights);
        Assert.Contains(spatial.FloorProbes, probe => MathF.Abs(probe.Direction.Y) < .5f);
        string line = session.Presentation.LastOutcome ?? string.Empty;
        Assert.Contains(TestPayload.Definitions.Magic.Spells[spell].Name, line, StringComparison.Ordinal);
        Assert.DoesNotContain("Missed", line, StringComparison.Ordinal);
    }

    [Fact]
    public void A_player_touch_reaches_the_enemy_its_volume_contacts()
    {
        List<string> releases = [];
        // Far enough that the enemy only closes, so no combat line replaces the spell's.
        (DaggerfallSession session, _, _) = VisibleEnemySession(releases, out SpatialFake spatial, distance: 20d, contact: true, random: RandomMaximum.Create());
        using DaggerfallSession disposable = session;
        Track magicka = session.State.Actors.Player.Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Magicka.Value));
        magicka.Maximum.BaseValue = 1000;
        magicka.SetCurrent(1000);
        spatial.OverlapContactsResult = _ => Touching(session.State.Actors.Get(Enemy).Actor.Entity.Value);
        string spell = Learn(session, DaggerfallSpellTarget.ByTouch);

        session.Update(new ProductUpdate(OuterUpdate(1), [Ui($$"""{"action":"spell-ready","key":"{{spell}}"}""")]));
        session.Update(new ProductUpdate(OuterUpdate(2), [Ui("""{"action":"spell-cast"}""")]));

        Assert.True(magicka.Current < 1000);
        Assert.Contains(spatial.OverlapContactsRequests, request => request.Shape.Kind == SpatialShapeKind.Box);
        string touchLine = session.Presentation.LastOutcome ?? string.Empty;
        Assert.Contains(TestPayload.Definitions.Magic.Spells[spell].Name, touchLine, StringComparison.Ordinal);
        Assert.DoesNotContain("Missed", touchLine, StringComparison.Ordinal);
    }

    [Fact]
    public void An_archers_arrow_flies_to_the_first_body_it_meets_which_is_the_player()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        spatial.KeepPosition = true;
        PerceptionFake perception = PerceptionFake.Create();
        AppearanceFake appearance = new(releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, appearance, perception.Service, random: RandomMaximum.Create());
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, ContactComposition(inputs));
        const long archer = 2004;
        session.State.PlayerControl.MoveTo(Vector3.Zero);
        session.State.Actors.Get(archer).ApplyPose(new(new(0, 0, -8), MathF.PI));
        session.State.Actors.Get(archer).Actor.Get<Modules.Behavior.DaggerfallEnemyPerceptionMemory>().SetForcedHostile(true);
        ulong player = session.State.Actors.Player.Actor.Entity.Value;
        // Every ray the line of fire and the arrow cast meets the player's body first.
        spatial.FloorHit = request => new SpatialHit(true, SpatialHitKind.Entity, player, 0, 0, 0, 0, 0, 0, default,
            request.Origin + request.Direction * .2f, -request.Direction, .2d, 0d, 0d, false, true);
        double separation = TestPayload.Definitions.EnemyRanged.MinimumDistance + 2d;
        perception.Receipt = Receipt(new PerceptionPair(archer, 1, separation, 1d, PerceptionPairKind.Visible, 1d));
        appearance.AdvanceReceiptForAll = CrossedMarker(1, markerId: AuthoredMarker(archer, "rangedFrames"));
        double health = Health(session, Player);

        for (ulong step = 1; step <= 120 && Health(session, Player) == health; step++)
            session.Update(new ProductUpdate(OuterUpdate(step), []));

        Assert.True(Health(session, Player) < health, session.ReadPlaytestObservation().Message);
    }

    [Fact]
    public void A_new_archers_arrow_leaves_on_its_release_frame_and_flies_through_ordinary_updates_into_the_target()
    {
        using NewGameBowSessionTests.Fixture f = new(4, contact: true, random: RandomMaximum.Create());
        ulong target = f.Game.State.Actors.Get(NewGameBowSessionTests.Fixture.Target).Actor.Entity.Value;
        f.Spatial.FloorHit = request => MathF.Abs(request.Direction.Y) < .5f
            ? new SpatialHit(true, SpatialHitKind.Entity, target, 0, 0, 0, 0, 0, 0, default, request.Origin + request.Direction * .2f, -request.Direction, .2d, 0d, 0d, false, true)
            : default;
        double health = Health(f.Game, NewGameBowSessionTests.Fixture.Target);

        f.Attack();
        Assert.NotNull(f.Game.State.Actors.Player.Attack.Pending);
        uint releaseFrame = DaggerfallFormulaPolicy.BowWeaponHitFrame;
        f.Appearance.AdvanceReceiptForAll = WeaponHitReading(f.Appearance.PlaybackRequests.Last().Frames.Span[(int)releaseFrame].FrameId, releaseFrame);
        for (int step = 0; step < 30 && Health(f.Game, NewGameBowSessionTests.Fixture.Target) == health; step++) f.Advance();

        Assert.True(Health(f.Game, NewGameBowSessionTests.Fixture.Target) < health, f.Game.ReadPlaytestObservation().Message);
    }

    private static (EngineContextFake Engine, CapturingDaggerfallRuleset Ruleset) Product(InMemoryPersistenceService persistence)
    {
        DaggerfallSiteProfile inputs = ReadInputs(TestData.RepositoryRoot);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), PerceptionFake.Create().Service, persistence: persistence);
        return (engine, new CapturingDaggerfallRuleset(variants: DaggerfallVariants.All));
    }

    /// <summary>Teaches the first stock spell of a delivery this ruleset can ready, and leaves it unready for the UI to ready.</summary>
    private static string Learn(DaggerfallSession session, DaggerfallSpellTarget target)
    {
        foreach (DaggerfallSpellDefinition spell in TestPayload.Definitions.Magic.Spells.Values.OrderBy(spell => spell.Key, StringComparer.Ordinal))
        {
            if (DaggerfallMagicCostPolicy.TargetForRangeType(spell.RangeType) != target) continue;
            session.State.Character.LearnSpell(spell.Key);
            if (session.ReadyPlayerSpell(spell.Key).Outcome != DaggerfallCastOutcome.Ready) continue;
            session.Casting.Cancel(Player);
            return spell.Key;
        }
        throw new InvalidOperationException($"No stock {target} spell can be readied.");
    }

    private static double Health(DaggerfallSession session, long actor) =>
        (actor == Player ? session.State.Actors.Player.Stats : session.State.Actors.Get(actor).Stats).GetTrack(TrackId.Parse("health")).Current;

    private static SpatialContactResult Touching(params ulong[] entities) => new(
        entities.Select(entity => new SpatialContact(SpatialHitKind.Entity, entity, 0, 0, 0, 0, 0, Vector3.Zero, Vector3.Zero, 0d, .1d, false)).ToArray(),
        false, false, 1, 1);
}
