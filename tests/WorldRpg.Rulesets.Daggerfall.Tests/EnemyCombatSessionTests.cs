using WorldRpg.Kit.Combat;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.Modules.Behavior;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>Enemy combat: attack policies, visibility-driven behavior and swing timing at the authored damage frame.</summary>
public sealed class EnemyCombatSessionTests
{
    [Fact]
    public void The_archer_carries_a_ranged_policy_its_donor_record_supports()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;

        // The archer's policy is a fact about the source, not a preference: its published donor record
        // declares a ranged attack group and carries no melee damage range at all, so the attack it is
        // given is ranged and its own attacks stay empty.
        DaggerfallMobileDefinition archerMobile = definitions.Mobiles.Mobiles[141];
        Assert.Equal("archer", archerMobile.Actor);
        Assert.True(archerMobile.HasRangedAttack1, "the donor record must declare the archer's ranged attack");
        Assert.False(archerMobile.HasRangedAttack2, "the donor record must declare which ranged group the archer uses");
        Assert.Null(archerMobile.DamageRange);

        DaggerfallActorDefinition archer = definitions.RequireActor(new DaggerfallActorId("archer"));
        Assert.Equal("archer-shot", archer.ActionId);
        Assert.Empty(archer.Attacks);
        DaggerfallActionDefinition shot = definitions.Actions["archer-shot"];
        Assert.Equal("fixed-ranged", shot.Interpretation);
        // The authored values and where they come from, stated so a later reader does not re-derive a
        // claim the corpus does not support. The damage is the iron long bow's range exactly - 4 to 18 -
        // which is an authored choice anchored on the long bow rather than a range both bows cover: the
        // iron short bow is 4 to 16, so 18 sits above it. Archery is chosen because the corpus's bows use
        // that skill and the archer's donor record declares a ranged attack; the record itself carries no
        // skills at all, and the archer's own twelve skills are the corpus's uniform filler.
        Assert.Equal("archery", shot.Skill);
        Assert.Equal((4, 18), (shot.MinimumDamage, shot.MaximumDamage));
        Assert.Equal(4, definitions.Items[new DaggerfallItemId("iron-short-bow")].Weapon!.MinimumDamage);
        Assert.Equal(18, definitions.Items[new DaggerfallItemId("iron-long-bow")].Weapon!.MaximumDamage);
        // A shot carries further than a swing, and the erratum that named the gap is gone: the capability
        // it recorded now exists, so leaving it would be a claim the corpus no longer supports.
        Assert.True(shot.Reach > definitions.Actions["monster-strike"].Reach, "a shot must carry further than a swing");
        Assert.DoesNotContain(definitions.DonorErrata, erratum => erratum.Id == "archer-ranged-attack-is-not-implemented");
        // The thief is the opposite case: no donor damage range either, but the donor's own enemy setup
        // gives it a melee policy, which is why it is authored rather than dispositioned away.
        DaggerfallMobileDefinition thief = definitions.Mobiles.Mobiles[138];
        Assert.Null(thief.DamageRange);
        Assert.Equal("thief-strike", definitions.Actors[new DaggerfallActorId("thief")].ActionId);
    }

    [Fact]
    public void Every_placed_attacker_has_a_policy_keyed_on_a_skill_it_carries()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<DaggerfallActorId> swinging = [];
        foreach (AuthoredActor placement in inputs.Project.Actors.Values)
        {
            DaggerfallActorDefinition definition = definitions.RequireActor(placement.ActorId);
            // Every placed actor carries a policy. There is no exception list: the archer's ranged attack
            // is the capability this check was waiting for, and an actor placed without one is an oversight
            // rather than a disposition.
            Assert.NotNull(definition.ActionId);
            DaggerfallActionDefinition action = definitions.Actions[definition.ActionId!];
            Assert.Contains(action.Interpretation, new[] { "fixed-melee", "fixed-ranged", "enemy-equipped-melee" });
            if (action.Interpretation == "enemy-equipped-melee")
            {
                // A class enemy swings what it has equipped: the weapon supplies the skill and damage.
                Assert.Equal("equipped", action.Skill);
                swinging.Add(placement.ActorId);
                continue;
            }
            // A swing either uses one of the actor's own authored damage ranges or carries authored
            // damage of its own; anything else would admit an attack with no damage frame.
            Assert.True(
                action.AttackRangeIndex is not null || (action.MinimumDamage is > 0 && action.MaximumDamage is > 0),
                $"'{placement.ActorId.Value}' swings with '{action.Id}', which names neither a damage range nor damage.");
            // The donor resolves a monster's swing with a skill its record carries; a swing keyed on a
            // skill the actor reads as zero would be a hit chance the donor never describes.
            Assert.True(definition.Stats.Values[new DaggerfallStatId(action.Skill)] > 0, $"'{placement.ActorId.Value}' swings with '{action.Skill}', which its record does not carry.");
            swinging.Add(placement.ActorId);
        }

        Assert.Contains(new DaggerfallActorId("imp"), swinging);
        Assert.Contains(new DaggerfallActorId("giant-bat"), swinging);
        Assert.Contains(new DaggerfallActorId("orc"), swinging);
    }

    [Fact]
    public void Enemy_behavior_uses_engine_visibility_then_shared_combat_and_transitions_without_replaying_damage()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        PerceptionFake perception = PerceptionFake.Create();
        perception.Receipt = Receipt(new PerceptionPair(2000, 1, 1d, 1d, PerceptionPairKind.Visible, 1d));
        AppearanceFake appearance = new(releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, appearance, perception.Service);

        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults));
        double healthBefore = session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).Current;
        // The swing is decided on the admitted step and lands when its authored damage
        // frame is reached, so the update that carries the crossing is the one that hurts.
        appearance.AdvanceReceiptForAll = CrossedMarker(1, markerId: AuthoredMeleeMarker(2000));
        session.Update(new ProductUpdate(OuterUpdate(1), []));
        Assert.Equal(EnemyBehaviorState.Attack, session.LastEnemyBehavior[2000].State);
        double healthAfterAttack = session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).Current;
        Assert.True(healthAfterAttack < healthBefore);

        // The same swing cannot land twice, and a frame that never crosses lands nothing.
        appearance.AdvanceReceiptForAll = null;
        session.Update(new ProductUpdate(OuterUpdate(2), []));
        Assert.Equal(healthAfterAttack, session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).Current);

        perception.Receipt = Receipt(new PerceptionPair(2000, 1, 1d, 0d, PerceptionPairKind.FacingRejected, 0d));
        session.Update(new ProductUpdateState(.125f));
        Assert.Equal(EnemyBehaviorState.TargetLost, session.LastEnemyBehavior[2000].State);
        perception.Receipt = Receipt(new PerceptionPair(2000, 1, 1d, 1d, PerceptionPairKind.Occluded, 0d));
        session.Update(new ProductUpdateState(.125f));
        Assert.Equal(EnemyBehaviorState.Idle, session.LastEnemyBehavior[2000].State);

        ActorState rat = session.State.Actors.Get(2000);
        rat.Stats.GetTrack(TrackId.Parse("health")).SetCurrent(-999, clamp: true);
        perception.Receipt = Receipt(new PerceptionPair(2000, 1, 1d, 1d, PerceptionPairKind.Visible, 1d));
        session.Update(new ProductUpdateState(.125f));
        Assert.Equal(EnemyBehaviorState.Dead, session.LastEnemyBehavior[2000].State);
    }

    [Fact]
    public void Lethal_enemy_resolution_does_not_activate_a_corpse_from_the_same_step()
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
        AppearanceFake appearance = new(releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, appearance, perception.Service);
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults));

        WorldPoint playerPosition = session.State.PlayerControl.Position ?? throw new InvalidOperationException("The test session has no player position.");
        long corpse = session.SpawnActor("rat", new ActorPose(playerPosition with { Z = playerPosition.Z - 1f }, 0f));
        session.State.Actors.Get(corpse).Stats.GetTrack(TrackId.Parse("health")).SetCurrent(1, clamp: true);
        session.State.Actors.Player.Stats.GetStat(StatId.Parse("strength")).BaseValue = 60; // classic damage floors at zero; stage a swing this fixture can rely on
        session.ResolveExplicitMelee(new ExplicitMeleeRequest(1, corpse, 1, 1, .125));
        Assert.True(session.Corpses.ContainsKey(corpse));

        session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).SetCurrent(1, clamp: true);
        AimActivationAt(session, corpse);
        perception.Receipt = Receipt(
            new PerceptionPair(2000, 1, 1d, 1d, PerceptionPairKind.Visible, 1d),
            new PerceptionPair(1, checked((ulong)corpse), 1d, 1d, PerceptionPairKind.Visible, 1d));
        appearance.AdvanceReceiptForAll = CrossedMarker(1, markerId: AuthoredMeleeMarker(2000));

        session.Update(new ProductUpdate(OuterUpdate(1), [
            Input(InputEventKind.DirectDigital, x: 1f, phase: InputPhase.DirectUi, intent: "interact"),
        ]));

        Assert.True(session.State.Actors.Player.IsDefeated);
        Assert.Null(session.OpenLoot);
        Assert.False(session.ActivationView.Applied);
    }

    [Fact]
    public void A_player_swing_is_admitted_while_an_enemy_swing_at_the_player_waits_for_its_frame()
    {
        List<string> releases = [];
        (DaggerfallSession session, AppearanceFake appearance, _) = VisibleEnemySession(releases);
        using DaggerfallSession disposable = session;
        double healthBefore = session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).Current;
        ProductInputEvent attack = Input(InputEventKind.DirectDigital, x: 1f, phase: InputPhase.DirectUi, intent: "attack");

        // The enemy decides a hitting swing; its damage frame has not been reached.
        session.Update(new ProductUpdate(OuterUpdate(1), []));
        Assert.Equal(EnemyBehaviorState.Attack, session.LastEnemyBehavior[2000].State);

        // The player swings while that strike is still in flight. The swing is admitted in this update
        // and starts the player's own cooldown; the enemy's strike has still not landed.
        Assert.True(session.State.Kit.AttackExecution.IsReady(DaggerfallActorIdentity.PlayerEntityId, 1, 2));
        session.Update(new ProductUpdate(OuterUpdate(2), [attack]));
        Assert.NotNull(session.LastMeleeTargeting);
        Assert.False(session.State.Kit.AttackExecution.IsReady(DaggerfallActorIdentity.PlayerEntityId, 1, 3));
        Assert.Equal(healthBefore, session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).Current);

        // The enemy's strike lands at its own frame, independently of the player's swing.
        appearance.AdvanceReceiptForAll = CrossedMarker(1, markerId: AuthoredMeleeMarker(2000));
        session.Update(new ProductUpdate(OuterUpdate(3), []));
        Assert.True(session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).Current < healthBefore);
    }

    [Fact]
    public void A_lethal_enemy_frame_in_the_same_update_resolves_before_a_deferred_player_swing()
    {
        List<string> releases = [];
        (DaggerfallSession session, AppearanceFake appearance, _) = VisibleEnemySession(releases);
        using DaggerfallSession disposable = session;

        session.Update(new ProductUpdate(OuterUpdate(1), []));
        Assert.Equal(EnemyBehaviorState.Attack, session.LastEnemyBehavior[2000].State);
        session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).SetCurrent(1, clamp: true);
        appearance.AdvanceReceiptForAll = CrossedMarker(1, markerId: AuthoredMeleeMarker(2000));

        // The swing asked for in the update whose frame kills the player is never made.
        session.Update(new ProductUpdate(OuterUpdate(2), [
            Input(InputEventKind.DirectDigital, x: 1f, phase: InputPhase.DirectUi, intent: "attack"),
        ]));

        Assert.True(session.State.Actors.Player.IsDefeated);
        Assert.Null(session.LastMeleeTargeting);
    }

    [Fact]
    public void Grounded_rat_melee_aim_uses_its_visible_body_above_the_floor()
    {
        DaggerfallSiteProfile inputs = ReadInputs(TestData.RepositoryRoot);
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        Dictionary<long, DaggerfallActorDefinition> authored = inputs.Project.Actors.Values.ToDictionary(
            placement => placement.EntityId, placement => definitions.RequireActor(placement.ActorId));
        DaggerTargetingPolicy policy = new(authored, DaggerfallTuning.Defaults.MeleeTargeting,
            () => inputs);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases),
            PerceptionFake.Create().Service);
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults));

        ActorState rat = session.State.Actors.Get(2008);
        Assert.True(authored[rat.DurableId].GroundOnSpawn);
        Assert.Equal(rat.Position.ToVector().Y + inputs.ActorSprites[rat.DurableId].Size.Y * .5f,
            policy.AimPoint(rat).Y, 4);
        ActorState imp = session.State.Actors.Get(2009);
        Assert.False(authored[imp.DurableId].GroundOnSpawn);
        Assert.Equal(imp.Position.ToVector(), policy.AimPoint(imp));
    }

    [Fact]
    public void The_immediate_resolver_refuses_an_enemy_swing()
    {
        List<string> releases = [];
        (DaggerfallSession session, _, _) = VisibleEnemySession(releases);
        using DaggerfallSession disposable = session;

        // One resolver owns enemy swings; the in-step path is the player's, and
        // accepting an enemy here would silently restore the old timing.
        Assert.Throws<ArgumentException>(() => session.ResolveExplicitMelee(new ExplicitMeleeRequest(2000, 1, 1, 1, .125)));
    }

    [Fact]
    public void A_save_taken_mid_swing_keeps_the_charge_and_never_replays_the_strike()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        ResolvedCompositionIdentity composition = GameCompositionResolver.Resolve(FullContent(root), new GameBundleId("daggerfall.classic")).RequireComposition().Identity;
        List<string> releases = [];
        DaggerfallSavePayload saved;
        using (DaggerfallSession original = VisibleEnemySession(releases).Session)
        {
            original.Update(new ProductUpdate(OuterUpdate(1), []));
            saved = DaggerfallSavePayload.Read(original.CaptureSave());
        }

        // The decision charged the attack before the save, and the swing itself is
        // transient: the resumed session must not replay damage it never saw land.
        Assert.Contains(saved.CombatCooldowns, cooldown => cooldown.AttackerId == 2000);
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        PerceptionFake perception = PerceptionFake.Create();
        perception.Receipt = Receipt(new PerceptionPair(2000, 1, 1d, 1d, PerceptionPairKind.Visible, 1d));
        AppearanceFake resumedAppearance = new(releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, resumedAppearance, perception.Service);
        using DaggerfallSession resumed = DaggerfallSession.Restore(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults, composition), DaggerfallSavePayload.Encode(saved));
        long resumedHealth = PlayerHealth(resumed);

        resumedAppearance.AdvanceReceiptForAll = CrossedMarker(1, markerId: AuthoredMeleeMarker(2000));
        resumed.Update(new ProductUpdate(OuterUpdate(2), []));

        Assert.Equal(resumedHealth, PlayerHealth(resumed));
    }

    [Fact]
    public void A_swing_with_several_damage_frames_sounds_and_lands_once()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        AppearanceFake appearance = new(releases);
        AudioRecorder audio = AudioRecorder.Create();
        // Authored sequences really do carry two or three -1 frames, so both cross in
        // one advance. One decided swing still owns exactly one strike beat.
        appearance.AdvanceReceipts.Enqueue(new SpritePlaybackAdvanceResult(
            new[]
            {
                new SpritePlaybackMarkerCrossing(2, 3, 1, 0, 1),
                new SpritePlaybackMarkerCrossing(4, 3, 1, 0, 2),
            },
            new SpritePlaybackReadout(3, 1, SpritePlaybackState.Playing, 0D, 0, 2, false),
            true));
        using DaggerfallSiteAppearance presentation = new(content, appearance, MediaInputs(primaryFrames: [0, -1, 1, -1, 0]), audio.Service);

        presentation.React(new EnemyAttackStartedFact(11, 12, true, 3, 4));
        presentation.Advance(OuterUpdate(1));

        Assert.Empty(audio.Emits);
        AttackHitFact applied = new(11, 12, 3, 3, 0, true, 3, 4);
        presentation.React(applied);
        presentation.React(applied);
        Assert.Single(audio.Emits);
        AttackImpactNotice impact = Assert.Single(presentation.TakeAttackImpacts());
        Assert.False(impact.Expired);
    }

    [Fact]
    public void A_swing_without_an_authored_damage_frame_resolves_immediately()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        AppearanceFake appearance = new(releases);
        AudioRecorder audio = AudioRecorder.Create();
        // Media without a -1 frame has no strike beat to wait for, so the decision
        // resolves where it is made rather than hanging unresolved.
        using DaggerfallSiteAppearance presentation = new(content, appearance, MediaInputs(), audio.Service);

        presentation.React(new EnemyAttackStartedFact(11, 12, true, 3, 4));

        AttackImpactNotice impact = Assert.Single(presentation.TakeAttackImpacts());
        Assert.False(impact.Expired);
    }

    [Fact]
    public void An_enemy_that_loses_reach_before_the_damage_frame_cancels_its_swing()
    {
        List<string> releases = [];
        EngineContextFake engine = null!;
        (DaggerfallSession session, AppearanceFake appearance, PerceptionFake perception) = VisibleEnemySession(releases, engineCreated: created => engine = created);
        using DaggerfallSession disposable = session;
        long healthBefore = PlayerHealth(session);
        session.Update(new ProductUpdate(OuterUpdate(1), []));

        // Still visible, but beyond the authored reach: the behaviour chases instead
        // of attacking, which cancels the swing already in flight.
        perception.Receipt = Receipt(new PerceptionPair(2000, 1, 5d, 1d, PerceptionPairKind.Visible, 5d));
        appearance.AdvanceReceiptForAll = CrossedMarker(1, markerId: AuthoredMeleeMarker(2000));
        session.Update(new ProductUpdate(OuterUpdate(2), []));

        Assert.Equal(EnemyBehaviorState.Chase, session.LastEnemyBehavior[2000].State);
        Assert.Equal(healthBefore, PlayerHealth(session));
        Assert.DoesNotContain(engine.EmittedAudio, cue => cue.SignalId.Split('.').Last().StartsWith("hit", StringComparison.Ordinal));
    }

    [Fact]
    public void An_enemy_defeated_mid_swing_never_lands_its_strike()
    {
        List<string> releases = [];
        (DaggerfallSession session, AppearanceFake appearance, _) = VisibleEnemySession(releases);
        using DaggerfallSession disposable = session;
        long healthBefore = PlayerHealth(session);
        session.Update(new ProductUpdate(OuterUpdate(1), []));

        session.State.Actors.Get(2000).Stats.GetTrack(TrackId.Parse("health")).SetCurrent(-999, clamp: true);
        appearance.AdvanceReceiptForAll = CrossedMarker(1, markerId: AuthoredMeleeMarker(2000));
        session.Update(new ProductUpdate(OuterUpdate(2), []));

        Assert.Equal(EnemyBehaviorState.Dead, session.LastEnemyBehavior[2000].State);
        Assert.Equal(healthBefore, PlayerHealth(session));
    }

    [Fact]
    public void An_enemy_swing_that_expires_never_lands_even_when_a_frame_crosses_later()
    {
        List<string> releases = [];
        (DaggerfallSession session, AppearanceFake appearance, _) = VisibleEnemySession(releases);
        using DaggerfallSession disposable = session;
        long healthBefore = PlayerHealth(session);
        session.Update(new ProductUpdate(OuterUpdate(1), []));

        // The animation ends without ever reaching its damage frame.
        appearance.AdvanceReceiptForAll = new SpritePlaybackAdvanceResult(
            Array.Empty<SpritePlaybackMarkerCrossing>(),
            new SpritePlaybackReadout(3, 1, SpritePlaybackState.Completed, 0D, 0, 3, true),
            true);
        session.Update(new ProductUpdate(OuterUpdate(2), []));
        Assert.Equal(healthBefore, PlayerHealth(session));

        // A crossing arriving afterwards cannot land the expired swing.
        appearance.AdvanceReceiptForAll = CrossedMarker(1, markerId: AuthoredMeleeMarker(2000));
        session.Update(new ProductUpdate(OuterUpdate(3), []));
        Assert.Equal(healthBefore, PlayerHealth(session));
    }

    [Fact]
    public void A_multi_step_update_does_not_damage_inside_the_deciding_update()
    {
        List<string> releases = [];
        (DaggerfallSession session, _, _) = VisibleEnemySession(releases);
        using DaggerfallSession disposable = session;
        long healthBefore = PlayerHealth(session);

        // Three admitted catch-up steps own one swing, and none of them damages: the
        // strike still waits for its authored frame.
        ProductUpdateFacts facts = new(ProductUpdateMode.Realtime, ProductLifecycleState.Running, 1, 1, 1, 1, 60, 3, 0, 1d / 60d);
        session.Update(new ProductUpdate(facts, []));

        Assert.Equal(EnemyBehaviorState.Attack, session.LastEnemyBehavior[2000].State);
        Assert.Equal(healthBefore, PlayerHealth(session));
    }

    [Fact]
    public void A_non_advanced_completed_receipt_does_not_cancel_a_swing_that_can_still_land()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        AppearanceFake appearance = new(releases);
        AudioRecorder audio = AudioRecorder.Create();
        appearance.AdvanceReceipts.Enqueue(new SpritePlaybackAdvanceResult(
            Array.Empty<SpritePlaybackMarkerCrossing>(),
            new SpritePlaybackReadout(3, 1, SpritePlaybackState.Completed, 0D, 0, 1, true),
            false));
        appearance.AdvanceReceipts.Enqueue(new SpritePlaybackAdvanceResult(
            new[] { new SpritePlaybackMarkerCrossing(2, 3, 1, 0, 1) },
            new SpritePlaybackReadout(3, 1, SpritePlaybackState.Playing, 0D, 0, 2, false),
            true));
        using DaggerfallSiteAppearance presentation = new(content, appearance, MediaInputs(primaryFrames: [0, -1, 1]), audio.Service);
        presentation.React(new EnemyAttackStartedFact(11, 12, true, 3, 4));

        // Completion without an advance is not authoritative, so nothing expires yet.
        presentation.Advance(OuterUpdate(1));
        Assert.Empty(presentation.TakeAttackImpacts());

        // The next advanced frame reaches the authored damage frame and the swing lands.
        presentation.Advance(OuterUpdate(2));
        AttackImpactNotice impact = Assert.Single(presentation.TakeAttackImpacts());
        Assert.False(impact.Expired);
    }

    [Fact]
    public void A_marker_the_author_did_not_designate_as_the_damage_frame_lands_no_strike()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        AppearanceFake appearance = new(releases);
        AudioRecorder audio = AudioRecorder.Create();
        // Engine's marker contract is deliberately semantics-free, so the strike beat is the crossing
        // whose identity is the authored damage frame. This sequence publishes markers 2 and 4; a
        // crossing of anything else names no beat, and it must neither sound nor land.
        appearance.AdvanceReceiptForAll = new SpritePlaybackAdvanceResult(
            new[] { new SpritePlaybackMarkerCrossing(3, 3, 1, 0, 1) },
            new SpritePlaybackReadout(3, 1, SpritePlaybackState.Playing, 0D, 0, 1, false),
            true);
        using DaggerfallSiteAppearance presentation = new(content, appearance, MediaInputs(primaryFrames: [0, -1, 1, -1, 0]), audio.Service);
        presentation.React(new EnemyAttackStartedFact(11, 12, true, 3, 4));

        presentation.Advance(OuterUpdate(1));

        Assert.Empty(presentation.TakeAttackImpacts());
        Assert.Empty(audio.Emits);
    }

    [Fact]
    public void A_swing_that_ends_without_reaching_its_damage_frame_reports_an_expired_impact()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        AppearanceFake appearance = new(releases);
        AudioRecorder audio = AudioRecorder.Create();
        appearance.AdvanceReceipts.Enqueue(new SpritePlaybackAdvanceResult(
            Array.Empty<SpritePlaybackMarkerCrossing>(),
            new SpritePlaybackReadout(3, 1, SpritePlaybackState.Completed, 0D, 0, 1, true),
            true));
        using DaggerfallSiteAppearance presentation = new(content, appearance, MediaInputs(primaryFrames: [0, -1, 1]), audio.Service);

        presentation.React(new EnemyAttackStartedFact(11, 12, true, 3, 4));
        presentation.Advance(OuterUpdate(1));

        // Playback ended before the beat: the swing must be reported as expired rather
        // than left pending, so it can never land after the animation is over.
        AttackImpactNotice impact = Assert.Single(presentation.TakeAttackImpacts());
        Assert.True(impact.Expired);
        Assert.Empty(audio.Emits);
    }

    [Fact]
    public void An_enemy_swing_lands_once_at_its_damage_frame_and_never_on_the_decision_update()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        PerceptionFake perception = PerceptionFake.Create();
        perception.Receipt = Receipt(new PerceptionPair(2000, 1, 1d, 1d, PerceptionPairKind.Visible, 1d));
        AppearanceFake appearance = new(releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, appearance, perception.Service);
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults));
        double healthBefore = session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).Current;

        // The update that decides the swing carries no damage: nothing has been struck
        // until the authored damage frame is reached.
        session.Update(new ProductUpdate(OuterUpdate(1), []));
        Assert.Equal(EnemyBehaviorState.Attack, session.LastEnemyBehavior[2000].State);
        Assert.Equal(healthBefore, session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).Current);

        appearance.AdvanceReceiptForAll = CrossedMarker(1, markerId: AuthoredMeleeMarker(2000));
        session.Update(new ProductUpdate(OuterUpdate(2), []));
        double healthAfterImpact = session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).Current;
        Assert.True(healthAfterImpact < healthBefore);

        // The same frame cannot land a second time.
        session.Update(new ProductUpdate(OuterUpdate(3), []));
        Assert.Equal(healthAfterImpact, session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).Current);
    }

    [Fact]
    public void An_enemy_swing_cancelled_before_its_damage_frame_never_lands_late()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        PerceptionFake perception = PerceptionFake.Create();
        perception.Receipt = Receipt(new PerceptionPair(2000, 1, 1d, 1d, PerceptionPairKind.Visible, 1d));
        AppearanceFake appearance = new(releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, appearance, perception.Service);
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults));
        double healthBefore = session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).Current;

        session.Update(new ProductUpdate(OuterUpdate(1), []));
        // Losing sight cancels the swing; a crossing from the already-playing animation
        // must not land the strike the attacker is no longer making.
        perception.Receipt = Receipt(new PerceptionPair(2000, 1, 1d, 0d, PerceptionPairKind.Occluded, 0d));
        appearance.AdvanceReceiptForAll = CrossedMarker(1, markerId: AuthoredMeleeMarker(2000));
        session.Update(new ProductUpdate(OuterUpdate(2), []));

        Assert.Equal(EnemyBehaviorState.TargetLost, session.LastEnemyBehavior[2000].State);
        Assert.Equal(healthBefore, session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).Current);
    }
}
