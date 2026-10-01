using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Targeting;
using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Presentation;
using WorldRpg.Kit.Facts;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.Modules.Behavior;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>Player attacks: swing admission, skill use, target selection, backstab and impact resolution.</summary>
public sealed class PlayerAttackSessionTests
{
    [Fact]
    public void Admitted_player_hits_record_skill_uses_once_per_operation_and_preserve_them_through_save()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        PerceptionFake perception = PerceptionFake.Create();
        perception.Receipt = Receipt(new PerceptionPair(1, 2000, 1d, 1d, PerceptionPairKind.Visible, 1d));
        AppearanceFake appearance = new(releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, appearance, perception.Service);
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults));
        // This test exercises combat skill-use cadence; retain the authored weapon through both
        // accepted operations so its physical-wear removal cannot change the selected skill.
        DaggerfallItemInstanceMetadata sword = session.State.ItemInstances.RequireUnique(1001);
        session.State.ItemInstances.ReplaceUnique(1001, sword with { CurrentCondition = 100, MaximumCondition = 100 });
        ProductInputEvent pressed = Input(InputEventKind.MappedDigital, InputEdge.Pressed, x: 1, phase: InputPhase.Pressed, intent: "attack");
        ProductUpdateFacts first = new(ProductUpdateMode.Realtime, ProductLifecycleState.Running, 1, 1, 1, 1, 60, 1, 0, 1d / 60d);

        session.Update(new ProductUpdate(first, [pressed]));
        // The operation is decided and charged at admission; the delivered hit is what tallies it.
        appearance.AdvanceReceiptForAll = Reading(2, 2);
        session.Update(new ProductUpdate(new ProductUpdateFacts(ProductUpdateMode.Realtime, ProductLifecycleState.Running, 1, 1, 1, 2, 60, 1, 0, 1d / 60d), []));
        Assert.Equal(1, session.State.Progression.SkillUses["long-blade"]);
        Assert.Equal(1, session.State.Progression.SkillUses["critical-strike"]);

        // The Engine admitted this exact generation/step already. Replaying its UI input reaches
        // the normal session path, but AttackExecution refuses it before Daggerfall can tally again.
        session.Update(new ProductUpdate(first, [pressed]));
        Assert.Equal(1, session.State.Progression.SkillUses["long-blade"]);
        Assert.Equal(1, session.State.Progression.SkillUses["critical-strike"]);

        // Clear the presentation strike latch, then submit a distinct later operation in the same
        // generation after its combat cooldown. It is an independent admitted hit, so it counts.
        appearance.AdvanceReceiptForAll = CompletedMarker(1);
        session.Update(new ProductUpdate(new ProductUpdateFacts(ProductUpdateMode.Realtime, ProductLifecycleState.Running, 1, 1, 2, 2, 60, 1, 0, 1d / 60d), []));
        session.Update(new ProductUpdate(new ProductUpdateFacts(ProductUpdateMode.Realtime, ProductLifecycleState.Running, 1, 1, 3, 3, 60, 1, 0, 1d / 60d), []));
        appearance.AdvanceReceiptForAll = null;
        session.Update(new ProductUpdate(new ProductUpdateFacts(ProductUpdateMode.Realtime, ProductLifecycleState.Running, 1, 1, 100, 100, 60, 1, 0, 1d / 60d), [pressed]));
        appearance.AdvanceReceiptForAll = Reading(2, 2);
        session.Update(new ProductUpdate(new ProductUpdateFacts(ProductUpdateMode.Realtime, ProductLifecycleState.Running, 1, 1, 101, 101, 60, 1, 0, 1d / 60d), []));
        Assert.Equal(2, session.State.Progression.SkillUses["long-blade"]);
        Assert.Equal(2, session.State.Progression.SkillUses["critical-strike"]);

        DaggerfallSavePayload saved = DaggerfallSavePayload.Read(session.CaptureSave());
        Assert.Equal(2, saved.SkillUses.Counters.Single(counter => counter.Skill == "long-blade").Uses);
        Assert.Equal(2, saved.SkillUses.Counters.Single(counter => counter.Skill == "critical-strike").Uses);
        Assert.True(saved.SkillUses.StartingLevelUpSkillSum > 0);

        ContentFake resumedContent = new(releases);
        PopulateContent(resumedContent, inputs);
        SpatialFake resumedSpatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake resumedEngine = EngineContextFake.Create(resumedContent, resumedSpatial.Service, new AppearanceFake(releases), PerceptionFake.Create().Service);
        ResolvedCompositionIdentity identity = GameCompositionResolver.Resolve(FullContent(root), new GameBundleId("daggerfall.privateers-hold")).RequireComposition().Identity;
        using DaggerfallSession restored = DaggerfallSession.Restore(resumedEngine.Context, new(definitions, inputs, DaggerfallTuning.Defaults, identity), DaggerfallSavePayload.Encode(saved));

        Assert.Equal(2, restored.State.Progression.SkillUses["long-blade"]);
        Assert.Equal(2, restored.State.Progression.SkillUses["critical-strike"]);
        int baselineBeforeRest = restored.State.SkillUses.StartingLevelUpSkillSum;
        Assert.Equal(baselineBeforeRest, restored.State.SkillUses.CurrentLevelUpSkillSum);

        double longBladeBeforeRest = restored.State.Actors.Player.Stats.GetStat(StatId.Parse("long-blade")).BaseValue;
        restored.State.Progression.TallySkillUse("long-blade", DaggerfallSkillUseReactions.MaximumSkillUses, DaggerfallSkillUseReactions.MaximumSkillUses);
        long expectedCheckSecond = new DaggerfallCalendar(saved.Calendar.Year, saved.Calendar.Month, saved.Calendar.Day, saved.Calendar.Hour, saved.Calendar.Minute, saved.Calendar.Second).ToAbsoluteSeconds() + 361;
        _ = restored.AdvanceElapsedTime(361);
        Assert.Equal(longBladeBeforeRest + 1d, restored.State.Actors.Player.Stats.GetStat(StatId.Parse("long-blade")).BaseValue);
        DaggerfallSavePayload restSaved = DaggerfallSavePayload.Read(restored.CaptureSave());
        Assert.Equal(expectedCheckSecond, restSaved.SkillUses.LastSkillIncreaseCheckSecond);

        _ = restored.AdvanceElapsedTime(0);
        Assert.Equal(longBladeBeforeRest + 1d, restored.State.Actors.Player.Stats.GetStat(StatId.Parse("long-blade")).BaseValue);
        Assert.Equal(expectedCheckSecond, DaggerfallSavePayload.Read(restored.CaptureSave()).SkillUses.LastSkillIncreaseCheckSecond);
    }

    [Fact]
    public void Player_swing_admission_starts_once_for_empty_space_and_explicit_material_rejection()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        PerceptionFake perception = PerceptionFake.Create();
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), perception.Service);
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults));
        using SpatialMovementSystem targetingSpatial = new(spatial.Service, content, inputs.SpatialArtifact, DaggerfallTuning.Defaults.Spatial);
        Dictionary<long, DaggerfallActorDefinition> authored = inputs.Project.Actors.Values.ToDictionary(
            placement => placement.EntityId,
            placement => definitions.RequireActor(placement.ActorId));
        authored[DaggerfallActorIdentity.PlayerEntityId] = definitions.RequireActor(new DaggerfallActorId("player"));
        authored[2000] = authored[2000] with { MinimumMaterial = "daedric" };
        TargetingService targeting = new(perception.Service, targetingSpatial, session.State.Actors, new DaggerTargetingPolicy(authored, DaggerfallTuning.Defaults.MeleeTargeting, () => inputs));
        DaggerCombatRules combat = new(RandomMinimum.Create(), session.State.Actors, session.State.Equipment, session.State.ActorInventories.InventoryFor,
            session.State.ItemInstances, definitions, authored, targeting);

        double staminaBefore = session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("stamina")).Current;
        FactBuffer<IProductFact> facts = new();
        combat.Attacks.TryPlayerMelee(session.State.PlayerControl, ForwardLook(), 7, 13, .125, facts);
        List<IProductFact> emptySpace = [];
        facts.Deliver(emptySpace.Add);

        Assert.Equal(staminaBefore - 5, session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("stamina")).Current);
        Assert.Equal(new PlayerAttackStartedFact(7, 13), Assert.Single(emptySpace.OfType<PlayerAttackStartedFact>()));
        Assert.Contains(new AttackRejectedFact(AttackRejection.NoTargetInReach), emptySpace);
        Assert.Equal(new AttackCooldown(DaggerfallActorIdentity.PlayerEntityId, 6), Assert.Single(combat.Execution.CaptureCooldowns(7, 13)));

        combat.Attacks.TryPlayerMelee(session.State.PlayerControl, ForwardLook(), 7, 14, .125, facts);
        List<IProductFact> coolingDown = [];
        facts.Deliver(coolingDown.Add);
        Assert.Equal([new AttackRejectedFact(AttackRejection.Cooldown)], coolingDown);
        Assert.Equal(staminaBefore - 5, session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("stamina")).Current);

        combat.ResolveExplicit(new ExplicitMeleeRequest(DaggerfallActorIdentity.PlayerEntityId, 2000, 8, 20, .125), facts);
        List<IProductFact> materialImmune = [];
        facts.Deliver(materialImmune.Add);
        Assert.Equal(new PlayerAttackStartedFact(8, 20), Assert.Single(materialImmune.OfType<PlayerAttackStartedFact>()));
        Assert.Contains(new AttackRejectedFact(AttackRejection.InsufficientWeaponMaterial), materialImmune);
        Assert.DoesNotContain(materialImmune, fact => fact is AttackHitFact or AttackMissedFact);

        // The factory's material is instance meaning, not the iron source definition used to give
        // template 113 its weapon shape. Equip the factory product and prove combat reads that
        // durable metadata before applying the target's minimum-material gate.
        DaggerfallItemFactory factory = new(definitions, RandomMinimum.Create());
        DaggerfallCreatedItem steelDagger = factory.Create(new DaggerfallItemCreateRequest("Weapons", "combat-steel", DaggerfallItemOwner.Player,
            TemplateIndex: 113, Material: "steel"));
        DurableIdentityReference steelIdentity = new(DurableIdentityKind.Item, 9_000_000);
        factory.Materialize(steelDagger, session.State.Inventory, session.State.ItemInstances, unique: steelIdentity);
        var steelItem = Assert.Single(session.State.Inventory.Read().UniqueItems, item => item.Definition.Value == steelDagger.Item.Value);
        var equipped = session.State.Equipment.Read().Assignments
            .Single(assignment => assignment.Slot == new WorldRpg.Kit.Inventory.EquipmentSlotId("right-hand")).Item;
        session.State.Equipment.Swap(equipped, new WorldRpg.Kit.Inventory.UniqueInventoryItem(steelItem.Entity.Value, steelDagger.Item),
            [new WorldRpg.Kit.Inventory.EquipmentSlotId("right-hand")]);
        authored[2000] = authored[2000] with { MinimumMaterial = "steel" };
        DaggerCombatRules steelCombat = new(RandomMinimum.Create(), session.State.Actors, session.State.Equipment, session.State.ActorInventories.InventoryFor,
            session.State.ItemInstances, definitions, authored, targeting,
            actorEquipment: session.State.ActorInventories.EquipmentFor, itemCondition: session.ItemCondition);

        session.State.Actors.Player.Stats.GetStat(StatId.Parse("strength")).BaseValue = 60; // classic damage floors at zero; stage a swing this fixture can rely on
        int healthBeforeSteelHit = session.State.Actors.Get(2000).Stats.GetTrack(TrackId.Parse("health")).ValueInt;
        int conditionBeforeSteelHit = session.State.ItemInstances.RequireUnique(steelIdentity.Value).CurrentCondition;
        steelCombat.ResolveExplicit(new ExplicitMeleeRequest(DaggerfallActorIdentity.PlayerEntityId, 2000, 9, 30, .125), facts);
        List<IProductFact> steelCanHit = [];
        facts.Deliver(steelCanHit.Add);
        AttackHitFact hit = Assert.Single(steelCanHit.OfType<AttackHitFact>());
        DamageAppliedFact applied = Assert.Single(steelCanHit.OfType<DamageAppliedFact>());
        ActorDamagedFact damaged = Assert.Single(steelCanHit.OfType<ActorDamagedFact>());
        Assert.Equal((DaggerfallActorIdentity.PlayerEntityId, 2000, DaggerfallDamageCause.PhysicalAttack),
            (applied.SourceActorId, applied.TargetActorId, applied.Cause));
        Assert.Equal((hit.CalculatedDamage, hit.ActualHealthLost), (applied.CalculatedDamage, applied.ActualHealthLost));
        Assert.Equal((applied.CalculatedDamage, applied.ActualHealthLost), (damaged.CalculatedDamage, damaged.ActualHealthLost));
        Assert.Equal(healthBeforeSteelHit - applied.ActualHealthLost,
            session.State.Actors.Get(2000).Stats.GetTrack(TrackId.Parse("health")).ValueInt);
        Assert.True(applied.ActualHealthLost > 0);
        Assert.True(session.State.ItemInstances.RequireUnique(steelIdentity.Value).CurrentCondition < conditionBeforeSteelHit);
        Assert.DoesNotContain(new AttackRejectedFact(AttackRejection.InsufficientWeaponMaterial), steelCanHit);
    }

    /// <summary>
    /// An attacker with no authored reach stays idle even at point-blank range, rather than entering the
    /// attack state and leaving combat to refuse behind it.
    /// </summary>
    /// <remarks>
    /// The behavior module reads the actor's authored reach to decide whether it can attack and the combat
    /// module refuses an attack with no reach. Reading a missing reach as zero distance would make those two
    /// disagree in the one direction a player sees: a state machine that says the actor attacked, a
    /// navigation receipt that says it never moved, and no damage. The shipped pack gives every placed actor
    /// a policy - the content check refuses one that does not - so this is built from definitions with the
    /// policies stripped, and it asserts Idle rather than merely not-Attack: a separation inside a reach the
    /// actor does not have is exactly where the two readings differ, and the difference is Idle against
    /// Chase.
    /// </remarks>
    [Fact]
    public void An_attacker_with_no_authored_reach_stays_idle_at_point_blank_range()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        Dictionary<DaggerfallActorId, DaggerfallActorDefinition> unreached =
            definitions.Actors.ToDictionary(pair => pair.Key, pair => pair.Value with { ActionId = null });
        DaggerfallDefinitions withoutPolicies = new(
            definitions.Catalogs, definitions.Vocabulary, unreached, definitions.Items, definitions.EquipmentSlots,
            definitions.ArmorValuesByMaterial, definitions.Actions, definitions.LootTables, definitions.HudResources,
            definitions.LootCategoryPools, definitions.DonorErrata, definitions.ItemTemplates,
            definitions.CharacterPresentation, definitions.Locations, definitions.Text, definitions.Magic, definitions.Mobiles,
            definitions.Names, definitions.Rumors, definitions.Biographies, definitions.Grids, definitions.Books, definitions.Factions, definitions.Terrain, definitions.ItemTemplateCatalog, definitions.QuestSources, definitions.Cinematics);

        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        PerceptionFake perception = PerceptionFake.Create();
        AppearanceFake appearance = new(releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, appearance, perception.Service);
        // Inside melee distance and facing, with the line clear: the one configuration where a missing reach
        // read as zero would admit an attack.
        perception.Receipt = Receipt([.. inputs.Project.Actors.Values.Select(placement =>
            new PerceptionPair(checked((ulong)placement.EntityId), (ulong)DaggerfallActorIdentity.PlayerEntityId, 0.5d, 1d, PerceptionPairKind.Visible, 1d))]);
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(withoutPolicies, inputs, DaggerfallTuning.Defaults));
        double healthBefore = session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).Current;
        appearance.AdvanceReceiptForAll = CrossedMarker(1, markerId: AuthoredMeleeMarker(2000));

        session.Update(new ProductUpdate(OuterUpdate(1), []));

        Assert.All(session.LastEnemyBehavior.Values, evidence =>
            Assert.Equal(EnemyBehaviorState.Idle, evidence.State));
        Assert.Equal(healthBefore, session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).Current);
    }

    [Fact]
    public void A_melee_request_that_found_nothing_reports_what_the_query_actually_saw()
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
        // Every placed actor is outside melee reach: the receipt says so, and the product has to say so
        // too rather than printing the same line it would print in a world where nothing is visible.
        perception.Receipt = new PerceptionReadoutResult(ReadOnlyMemory<PerceptionPair>.Empty, ReadOnlyMemory<PerceptionAggregate>.Empty, 0, false, 0, 1, 42, 42, 42, 41, 1, 0, 0);
        session.Update(new ProductUpdate(OuterUpdate(1), [PadButton(ControllerButton.Button0, InputEdge.Pressed)]));

        Assert.Equal("No target in melee reach (42 observer(s) against 42 target(s), 42 compared: 41 out of range, 1 out of cone, 0 cast, 0 occluded)", session.Presentation.LastOutcome);
        // The same line is what the DOM draws, so a human sees the counters too.
        Dictionary<string, object?> published = (Dictionary<string, object?>)engine.Published()!;
        Assert.Contains("No target in melee reach (42 observer(s) against 42 target(s)", (string)published["lastOutcome"]!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_held_attack_button_cannot_hide_what_actually_happened()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallActorDefinition rat = definitions.RequireActor(new DaggerfallActorId("rat"));
        PresentationState presentation = new("Ready");
        DaggerfallOutcomePresentation outcomes = new(presentation, new Dictionary<long, DaggerfallActorDefinition> { [2008] = rat });

        // A hit lands, and then the attack button stays held: the cooldown rejection repeats every
        // update, and the player still has to be able to read what happened.
        outcomes.React(new AttackHitFact(1, 2008, 7, 7d, 3, false, 1, 100));
        Assert.Equal("Hit rat for 7 damage", presentation.LastOutcome);
        for (int repeat = 0; repeat < 40; repeat++)
        {
            outcomes.React(new AttackRejectedFact(AttackRejection.Cooldown));
        }

        Assert.Equal("Hit rat for 7 damage", presentation.LastOutcome);

        // A miss reports the roll the same way, and once the result has aged out the rejection shows.
        outcomes.React(new AttackMissedFact(1, 2008, 41, 8, false, 1, 200));
        Assert.Equal("Missed rat (41 vs 8)", presentation.LastOutcome);
        presentation.Advance(PresentationState.LifetimeSeconds);
        outcomes.React(new AttackRejectedFact(AttackRejection.Cooldown));
        Assert.Equal("Cooldown", presentation.LastOutcome);
    }

    [Fact]
    public void Backstab_opportunity_requires_an_accepted_operation_and_survives_replay_and_save_load()
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
        Dictionary<long, DaggerfallActorDefinition> authored = inputs.Project.Actors.Values.ToDictionary(
            placement => placement.EntityId,
            placement => definitions.RequireActor(placement.ActorId));
        authored[DaggerfallActorIdentity.PlayerEntityId] = definitions.RequireActor(new DaggerfallActorId("player"));
        DaggerCombatRules combat = new(RandomMaximum.Create(), session.State.Actors, session.State.Equipment, session.State.ActorInventories.InventoryFor,
            session.State.ItemInstances, definitions, authored, null!, use => session.State.SkillUses.Record(use),
            actorEquipment: session.State.ActorInventories.EquipmentFor, itemCondition: session.ItemCondition,
            playerPosition: () => session.State.PlayerControl.Position);
        session.State.Actors.Player.Stats.GetStat(StatId.Parse("backstabbing")).BaseValue = 37;

        WorldPoint player = session.State.PlayerControl.Position!.Value;
        ActorState target = session.State.Actors.Get(2000);
        // With zero heading an actor faces -Z; putting the player at +Z leaves the actor's back
        // toward the accepted player attack, matching the donor's IsBackFacing opportunity.
        target.ApplyPose(new ActorPose(new WorldPoint(player.X, player.Y, player.Z - 1f), 0f));
        FactBuffer<IProductFact> facts = new();
        combat.ResolveExplicit(new ExplicitMeleeRequest(DaggerfallActorIdentity.PlayerEntityId, 9999, 30, 1, .125), facts);
        List<IProductFact> rejectedFacts = [];
        facts.Deliver(rejectedFacts.Add);
        Assert.Contains(rejectedFacts, fact => fact is AttackRejectedFact { Reason: AttackRejection.UnknownExplicitCombatant });
        Assert.Equal(0, session.State.Progression.SkillUses["backstabbing"]);

        combat.ResolveExplicit(new ExplicitMeleeRequest(DaggerfallActorIdentity.PlayerEntityId, 2000, 31, 1, .125), facts);
        List<IProductFact> awayFacts = [];
        facts.Deliver(awayFacts.Add);
        AttackMissedFact away = Assert.Single(awayFacts.OfType<AttackMissedFact>());
        Assert.Equal(1, session.State.Progression.SkillUses["backstabbing"]);

        combat.ResolveExplicit(new ExplicitMeleeRequest(DaggerfallActorIdentity.PlayerEntityId, 2000, 31, 1, .125), facts);
        List<IProductFact> replayFacts = [];
        facts.Deliver(replayFacts.Add);
        Assert.Contains(replayFacts, fact => fact is AttackRejectedFact { Reason: AttackRejection.Cooldown });
        Assert.Equal(1, session.State.Progression.SkillUses["backstabbing"]);

        // DFU's 100-degree sector rounds to facing index 2, which is a side-facing opportunity
        // and therefore contributes neither chance nor a use tally.
        target.ApplyPose(new ActorPose(target.Position, MathF.PI * 80f / 180f));
        combat.ResolveExplicit(new ExplicitMeleeRequest(DaggerfallActorIdentity.PlayerEntityId, 2000, 32, 1, .125), facts);
        List<IProductFact> sideFacts = [];
        facts.Deliver(sideFacts.Add);
        AttackMissedFact side = Assert.Single(sideFacts.OfType<AttackMissedFact>());
        Assert.Equal(away.Chance - 37, side.Chance);
        Assert.Equal(1, session.State.Progression.SkillUses["backstabbing"]);

        // The donor's 135-degree sector rounds to facing index 3, the first back-facing sector.
        target.ApplyPose(new ActorPose(target.Position, MathF.PI / 4f));
        combat.ResolveExplicit(new ExplicitMeleeRequest(DaggerfallActorIdentity.PlayerEntityId, 2000, 33, 1, .125), facts);
        List<IProductFact> diagonalFacts = [];
        facts.Deliver(diagonalFacts.Add);
        AttackMissedFact diagonal = Assert.Single(diagonalFacts.OfType<AttackMissedFact>());
        Assert.Equal(away.Chance, diagonal.Chance);
        Assert.Equal(2, session.State.Progression.SkillUses["backstabbing"]);

        DaggerfallSavePayload saved = DaggerfallSavePayload.Read(session.CaptureSave());
        List<string> restoredReleases = [];
        ContentFake restoredContent = new(restoredReleases);
        PopulateContent(restoredContent, inputs);
        SpatialFake restoredSpatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, restoredReleases);
        EngineContextFake restoredEngine = EngineContextFake.Create(restoredContent, restoredSpatial.Service,
            new AppearanceFake(restoredReleases), PerceptionFake.Create().Service);
        ResolvedCompositionIdentity identity = GameCompositionResolver.Resolve(FullContent(root), new GameBundleId("daggerfall.privateers-hold")).RequireComposition().Identity;
        using DaggerfallSession restored = DaggerfallSession.Restore(restoredEngine.Context, new(definitions, inputs, DaggerfallTuning.Defaults, identity), DaggerfallSavePayload.Encode(saved));
        Assert.Equal(2, restored.State.Progression.SkillUses["backstabbing"]);
    }

    [Fact]
    public void Ordinary_attack_uses_the_engine_visibility_receipt_then_the_shared_explicit_melee_policy()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        PerceptionFake perception = PerceptionFake.Create();
        perception.Receipt = Receipt(new PerceptionPair(1, 2000, 2.25d, .5d, PerceptionPairKind.Visible, 1d));
        AppearanceFake appearance = new(releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, appearance, perception.Service);

        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults));
        double healthBefore = session.State.Actors.Get(2000).Stats.GetTrack(TrackId.Parse("health")).Current;
        session.State.Actors.Player.Stats.GetStat(StatId.Parse("strength")).BaseValue = 60; // classic damage floors at zero; stage a swing this fixture can rely on
        session.Update(AttackUpdate());

        TargetingEvidence evidence = Assert.IsType<TargetingEvidence>(session.LastMeleeTargeting);
        Assert.Equal(2000, evidence.SelectedTargetId);
        Assert.Equal(perception.Requests.Last(), evidence.Request);
        Assert.True(perception.Requests.Count > 1);
        Assert.Equal((ulong)1, evidence.Request.Observers.Span[0].Entity);
        Assert.Equal(2.25d, evidence.Request.Observers.Span[0].MaximumDistance);
        Assert.Equal(.5d, evidence.Request.Observers.Span[0].MinimumFacingCosine);
        Assert.Equal(1, evidence.Receipt.Pairs.Length);
        // The visibility receipt selects the target at admission; that same target takes the swing's
        // damage when the animation reaches its hit frame.
        Assert.Equal(healthBefore, session.State.Actors.Get(2000).Stats.GetTrack(TrackId.Parse("health")).Current);
        appearance.AdvanceReceiptForAll = Reading(2, 2);
        session.Update(new ProductUpdate(new ProductUpdateFacts(ProductUpdateMode.Realtime, ProductLifecycleState.Running, 0, 1, 1, 1, 60, 1, 0, 1d / 60d), []));
        Assert.True(session.State.Actors.Get(2000).Stats.GetTrack(TrackId.Parse("health")).Current < healthBefore);
    }

    [Fact]
    public void A_transition_mid_swing_retires_the_swing_it_can_no_longer_deliver()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        DaggerfallSiteProfile castle = DaggerfallSiteContent.Read(FullContent(root),
            File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.castle-necromoghan.json")), definitions);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        PopulateContent(content, castle);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        spatial.KeepPosition = true;
        PerceptionFake perception = PerceptionFake.Create();
        perception.Receipt = Receipt(new PerceptionPair(1, 2000, 1d, 1d, PerceptionPairKind.Visible, 1d));
        AppearanceFake appearance = new(releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, appearance, perception.Service);
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults));
        session.AdmitSiteProfiles(new DaggerfallSiteProfiles([inputs, castle]));
        session.State.Actors.Player.Stats.GetStat(StatId.Parse("strength")).BaseValue = 60; // classic damage floors at zero; stage a swing this fixture can rely on
        ProductInputEvent pressed = Input(InputEventKind.MappedDigital, InputEdge.Pressed, x: 1, phase: InputPhase.Pressed, intent: "attack");

        session.Update(new ProductUpdate(new ProductUpdateFacts(ProductUpdateMode.Realtime, ProductLifecycleState.Running, 1, 1, 1, 1, 60, 3, 0, 1d / 60d), [pressed]));
        double targetHealth = session.State.Actors.Get(2000).Stats.GetTrack(TrackId.Parse("health")).Current;
        double playerHealth = session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).Current;

        // The swing is admitted, charged and still in flight: the projection playing it owns the
        // impact frame, so the shared state holds the attack even long past its cooldown.
        Assert.False(session.State.Kit.AttackExecution.IsReady(DaggerfallActorIdentity.PlayerEntityId, 1, 500));

        double targetHealthBeforeTransition = session.State.Actors.Get(2000).Stats.GetTrack(TrackId.Parse("health")).Current;
        Assert.Equal(targetHealth, targetHealthBeforeTransition);
        Assert.True(session.TryTransitionTo(castle.ProfileKey));

        // The departing projection could not deliver that frame, so the charge is retired with it —
        // the admitted swing never landed on anything, and melee is admitted again instead of staying
        // charged forever. The target itself left with the source site, so its health is read before
        // the transition.
        Assert.Equal(targetHealth, targetHealthBeforeTransition);
        Assert.Equal(playerHealth, session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).Current);
        Assert.True(session.State.Kit.AttackExecution.IsReady(DaggerfallActorIdentity.PlayerEntityId, 1, 500));
    }

    [Fact]
    public void Daggerfall_target_selection_accepts_engine_inclusive_boundaries_and_rejects_other_engine_classifications()
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
        using SpatialMovementSystem movement = new(spatial.Service, content, inputs.SpatialArtifact, DaggerfallTuning.Defaults.Spatial);
        Dictionary<long, DaggerfallActorDefinition> authored = inputs.Project.Actors.Values.ToDictionary(
            placement => placement.EntityId,
            placement => definitions.RequireActor(placement.ActorId));
        authored[DaggerfallActorIdentity.PlayerEntityId] = definitions.RequireActor(new DaggerfallActorId("player"));
        TargetingService targeting = new(perception.Service, movement, session.State.Actors, new DaggerTargetingPolicy(authored, DaggerfallTuning.Defaults.MeleeTargeting, () => inputs));
        perception.Receipt = Receipt(new PerceptionPair(1, 2000, 2.25d, .5d, PerceptionPairKind.Visible, 1d));
        Assert.Equal(2000, targeting.Select(session.State.PlayerControl.Position, ForwardLook().Forward, 2.25d));

        foreach (PerceptionPairKind rejected in new[] { PerceptionPairKind.FacingRejected, PerceptionPairKind.Occluded })
        {
            perception.Receipt = Receipt(new PerceptionPair(1, 2000, 2.25d, .5d, rejected, 1d));
            Assert.Null(targeting.Select(session.State.PlayerControl.Position, ForwardLook().Forward, 2.25d));
        }

        perception.Receipt = new PerceptionReadoutResult(ReadOnlyMemory<PerceptionPair>.Empty, ReadOnlyMemory<PerceptionAggregate>.Empty, 0, false, 0, 1, 1, 1, 1, 1, 0, 0, 0);
        Assert.Null(targeting.Select(session.State.PlayerControl.Position, ForwardLook().Forward, 2.25d));
        Assert.Equal(1U, targeting.LastEvidence?.Receipt.DistanceRejects);
    }

    [Fact]
    public void Daggerfall_target_selection_excludes_defeated_actors_and_uses_durable_ids_over_runtime_ids()
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

        using (DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults)))
        {
            using SpatialMovementSystem sessionMovement = new(spatial.Service, content, inputs.SpatialArtifact, DaggerfallTuning.Defaults.Spatial);
            Dictionary<long, DaggerfallActorDefinition> authored = inputs.Project.Actors.Values.ToDictionary(
                placement => placement.EntityId,
                placement => definitions.RequireActor(placement.ActorId));
            authored[DaggerfallActorIdentity.PlayerEntityId] = definitions.RequireActor(new DaggerfallActorId("player"));
            TargetingService targeting = new(perception.Service, sessionMovement, session.State.Actors, new DaggerTargetingPolicy(authored, DaggerfallTuning.Defaults.MeleeTargeting, () => inputs));
            perception.Receipt = Receipt(
                new PerceptionPair(1, 2007, 1d, .8d, PerceptionPairKind.Visible, 1d),
                new PerceptionPair(1, 2000, 1d, .8d, PerceptionPairKind.Visible, 1d),
                new PerceptionPair(1, 2008, .5d, .8d, PerceptionPairKind.Visible, 1d));
            Assert.Equal(2008, targeting.Select(session.State.PlayerControl.Position, ForwardLook().Forward, 2.25d));

            perception.Receipt = Receipt(
                new PerceptionPair(1, 2007, 1d, .8d, PerceptionPairKind.Visible, 1d),
                new PerceptionPair(1, 2000, 1d, .8d, PerceptionPairKind.Visible, 1d));
            Assert.Equal(2000, targeting.Select(session.State.PlayerControl.Position, ForwardLook().Forward, 2.25d));

            session.State.Actors.Get(2008).Stats.GetTrack(TrackId.Parse("health")).SetCurrent(0, clamp: true);
            perception.Receipt = Receipt(new PerceptionPair(1, 2008, .5d, .8d, PerceptionPairKind.Visible, 1d));
            Assert.Null(targeting.Select(session.State.PlayerControl.Position, ForwardLook().Forward, 2.25d));
        }

        using SpatialMovementSystem movement = new(spatial.Service, content, new SpatialContentArtifact(inputs.SpatialArtifact.Path, inputs.SpatialArtifact.Sha256, inputs.SpatialArtifact.NavigationGridId), DaggerfallTuning.Defaults.Spatial);
        using ActorsState actors = ActorsWithNpc(2000, HealthyMechanics(), new WorldPoint(0f, 0f, 1f));
        ActorState runtimeActor = actors.Get(2000);
        TargetingService runtimeTargeting = new(
            perception.Service,
            movement,
            actors,
            new DaggerTargetingPolicy(new Dictionary<long, DaggerfallActorDefinition> { [2000] = definitions.RequireActor(new DaggerfallActorId("skeletal-warrior")) },
            DaggerfallTuning.Defaults.MeleeTargeting, () => inputs));
        perception.Receipt = Receipt(new PerceptionPair(1, 2000, 1d, .8d, PerceptionPairKind.Visible, 1d));
        Assert.NotEqual(new EntityId(checked((ulong)runtimeActor.DurableId)), runtimeActor.Actor.Entity);
        long? selected = runtimeTargeting.Select(
            new WorldPoint(0f, 0f, 0f),
            Vector3.UnitZ,
            2.25d);
        Assert.Equal(2000, selected);
        Assert.Single(perception.Requests.Last().Targets.Span.ToArray());
    }

    private static ProductUpdateState AttackUpdate()
    {
        ProductUpdateState update = new(.125f);
        update.Add(Input(InputEventKind.DirectDigital, x: 1f, phase: InputPhase.DirectUi, intent: "attack"));
        return update;
    }

    [Fact]
    public void An_impact_whose_target_was_defeated_after_the_decision_produces_no_hit_or_miss_fact()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        PerceptionFake perception = PerceptionFake.Create();
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), perception.Service);
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults));
        using SpatialMovementSystem targetingSpatial = new(spatial.Service, content, inputs.SpatialArtifact, DaggerfallTuning.Defaults.Spatial);
        Dictionary<long, DaggerfallActorDefinition> authored = inputs.Project.Actors.Values.ToDictionary(
            placement => placement.EntityId,
            placement => definitions.RequireActor(placement.ActorId));
        authored[DaggerfallActorIdentity.PlayerEntityId] = definitions.RequireActor(new DaggerfallActorId("player"));
        TargetingService targeting = new(perception.Service, targetingSpatial, session.State.Actors, new DaggerTargetingPolicy(authored, DaggerfallTuning.Defaults.MeleeTargeting, () => inputs));
        DaggerCombatRules combat = new(RandomMinimum.Create(), session.State.Actors, session.State.Equipment, session.State.ActorInventories.InventoryFor,
            session.State.ItemInstances, definitions, authored, targeting);
        FactBuffer<IProductFact> facts = new();

        // The enemy decides a swing against the living player.
        Assert.True(combat.Attacks.TryBeginEnemyAttack(2000, DaggerfallActorIdentity.PlayerEntityId, 77, 400, .125, facts));
        List<IProductFact> decided = [];
        facts.Deliver(decided.Add);
        Assert.Contains(decided, fact => fact is EnemyAttackStartedFact);

        // The player is defeated before the damage frame is reached.
        session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).SetCurrent(0, clamp: true);
        combat.Execution.ApplyImpacts([new AttackImpactNotice(2000, DaggerfallActorIdentity.PlayerEntityId, 77, 400, Expired: false)], 77, facts);
        List<IProductFact> impacts = [];
        facts.Deliver(impacts.Add);

        // A defeated target is dropped, not struck for zero: the clamp would otherwise
        // still publish a hit fact for an impact that changed nothing.
        Assert.DoesNotContain(impacts, fact => fact is AttackHitFact or AttackMissedFact);
        Assert.Equal(0d, session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).Current);

        // The swing is consumed by the dropped impact rather than left blocking its
        // attacker: a same-generation retry past the cooldown is accepted only if the
        // pending entry for this exact (generation, attacker) key is gone.
        session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).SetCurrent(100, clamp: true);
        Assert.True(combat.Attacks.TryBeginEnemyAttack(2000, DaggerfallActorIdentity.PlayerEntityId, 77, 1_000, .125, facts));
    }
}
