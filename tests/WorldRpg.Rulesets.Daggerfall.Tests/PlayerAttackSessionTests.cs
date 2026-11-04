using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Targeting;
using System.Numerics;
using System.Reflection;
using System.Text.Json;
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
    public void Equipment_delay_blocks_attacks_ages_only_in_play_and_survives_save()
    {
        using var fixture = new ConditionSessionFixture();
        var session = fixture.Session;
        var sword = session.State.Equipment.Read().Assignments.Single(value => value.Slot.Value == "right-hand").Item;
        Assert.Equal(EquipmentMoveOutcome.Applied, session.EquipmentMoves.MoveToGrid(sword, 49).Outcome);
        var saved = DaggerfallSavePayload.Read(session.CaptureSave());
        Assert.True(saved.EquipCountdown.RightSeconds > 0);
        FactBuffer<IProductFact> facts = new();
        Assert.False(session.State.Kit.AttackExecution.Start(new(1, 2000, 1, 1, 1d / 60, false), facts));
        List<IProductFact> rejected = []; facts.Deliver(rejected.Add);
        Assert.Contains(rejected, fact => fact is AttackRejectedFact { Reason: AttackRejection.Equipping });
        session.ApplyProductMode(ProductMode.Paused);
        session.Update(new ProductUpdate(OuterUpdate(2), []));
        Assert.Equal(saved.EquipCountdown, DaggerfallSavePayload.Read(session.CaptureSave()).EquipCountdown);
        using var restored = fixture.Restore(session.CaptureSave());
        Assert.Equal(saved.EquipCountdown, DaggerfallSavePayload.Read(restored.CaptureSave()).EquipCountdown);
        restored.ApplyProductMode(ProductMode.Playing);
        ulong steps = (ulong)Math.Ceiling(saved.EquipCountdown.RightSeconds * 60) + 1;
        for (ulong step = 1; step <= steps; step++) restored.Update(new ProductUpdate(OuterUpdate(step), []));
        Assert.Equal(0, DaggerfallSavePayload.Read(restored.CaptureSave()).EquipCountdown.RightSeconds);
        _ = restored.State.Kit.AttackExecution.Start(new(1, 2000, 1, steps + 1, 1d / 60, false), facts);
        rejected.Clear(); facts.Deliver(rejected.Add);
        Assert.DoesNotContain(rejected, fact => fact is AttackRejectedFact { Reason: AttackRejection.Equipping });
    }

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
        session.Touches(2000);
        double windup = DaggerfallTuning.Defaults.Attacks.For("long-blade").Fast.Windup + 2d / 60d;

        ulong step = 1;
        session.Update(new ProductUpdate(OuterUpdate(step++), AttackTap));
        // The operation is decided and charged at admission; the delivered hit is what tallies it.
        Assert.Equal(0, session.State.Progression.SkillUses["long-blade"]);
        RunSeconds(session, ref step, windup);
        Assert.Equal(1, session.State.Progression.SkillUses["long-blade"]);
        Assert.Equal(1, session.State.Progression.SkillUses["critical-strike"]);

        // The rest of the motion adds nothing to the one hit.
        RunSeconds(session, ref step, 1d);
        Assert.Equal(1, session.State.Progression.SkillUses["long-blade"]);

        // A distinct later operation is an independent admitted hit, so it counts.
        session.Update(new ProductUpdate(OuterUpdate(step++), AttackTap));
        RunSeconds(session, ref step, windup);
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
        ResolvedCompositionIdentity identity = TestSessions.Resolve(FullContent(root), new GameBundleId("daggerfall.classic")).RequireComposition().Identity;
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
        DaggerCombatRules combat = CombatCollaborators.SessionRules(session, definitions, authored, targeting, targetingSpatial);

        double staminaBefore = session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("stamina")).Current;
        FactBuffer<IProductFact> facts = new();
        combat.Attacks.TryPlayerMelee(session.State.PlayerControl, ForwardLook(), 7, 13, .125, facts);
        List<IProductFact> emptySpace = [];
        facts.Deliver(emptySpace.Add);

        Assert.Equal(staminaBefore - 10, session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("stamina")).Current);
        // A swing at empty space waits for its hit frame like any other, and only there meets nothing.
        PlayerAttackStartedFact started = Assert.Single(emptySpace.OfType<PlayerAttackStartedFact>());
        Assert.Equal(((long?)null, new DaggerfallStrikeFeedback(true, "sound.347")), (started.TargetId, started.Feedback));
        Assert.DoesNotContain(emptySpace, fact => fact is AttackRejectedFact);
        Assert.Equal(new AttackCooldown(DaggerfallActorIdentity.PlayerEntityId, 1), Assert.Single(combat.Execution.CaptureCooldowns(7, 13)));

        combat.Attacks.TryPlayerMelee(session.State.PlayerControl, ForwardLook(), 7, 13, .125, facts);
        List<IProductFact> swinging = [];
        facts.Deliver(swinging.Add);
        Assert.Equal([new AttackRejectedFact(AttackRejection.AttackInProgress, DaggerfallActorIdentity.PlayerEntityId)], swinging);

        combat.Execution.ApplyImpacts([new AttackImpactNotice(DaggerfallActorIdentity.PlayerEntityId, 0, 7, 13, Expired: false)], 7, facts);
        List<IProductFact> whiffed = [];
        facts.Deliver(whiffed.Add);
        Assert.Single(whiffed.OfType<AttackWhiffedFact>());

        combat.Attacks.TryPlayerMelee(session.State.PlayerControl, ForwardLook(), 7, 13, .125, facts);
        List<IProductFact> coolingDown = [];
        facts.Deliver(coolingDown.Add);
        Assert.Equal([new AttackRejectedFact(AttackRejection.Cooldown, DaggerfallActorIdentity.PlayerEntityId)], coolingDown);
        Assert.Equal(staminaBefore - 10, session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("stamina")).Current);

        session.Touches(2000);
        combat.ResolveExplicit(new ExplicitMeleeRequest(DaggerfallActorIdentity.PlayerEntityId, 2000, 8, 20, .125), facts);
        List<IProductFact> materialImmune = [];
        facts.Deliver(materialImmune.Add);
        Assert.Equal(new PlayerAttackStartedFact(8, 20, 2000) { Feedback = new(true, "sound.347") },
            Assert.Single(materialImmune.OfType<PlayerAttackStartedFact>()));
        Assert.Contains(new AttackRejectedFact(AttackRejection.InsufficientWeaponMaterial), materialImmune);
        Assert.DoesNotContain(materialImmune, fact => fact is AttackHitFact or AttackWhiffedFact);

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
        DaggerCombatRules steelCombat = CombatCollaborators.SessionRules(session, definitions, authored, targeting, targetingSpatial);

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
            definitions.DonorErrata, definitions.ItemTemplates,
            definitions.CharacterPresentation, definitions.Locations, definitions.Text, definitions.Magic, definitions.Mobiles,
            definitions.Names, definitions.Rumors, definitions.Biographies, definitions.Grids, definitions.Books, definitions.Factions, definitions.Terrain, definitions.ItemTemplateCatalog, definitions.QuestSources, definitions.Cinematics, definitions.Encounters)
        { NewGame = definitions.NewGame, EnemyRanged = definitions.EnemyRanged, EnemySpells = definitions.EnemySpells,
            BuildingNames = definitions.BuildingNames, DialogueWorldRules = definitions.DialogueWorldRules };

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

        // The rat has no independent ranged or spell action to replace its missing melee reach.
        Assert.Equal(EnemyBehaviorState.Idle, session.LastEnemyBehavior[2000].State);
        Assert.Equal(healthBefore, session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).Current);
    }

    [Fact]
    public void A_melee_request_that_found_nothing_is_a_silent_whiff_and_keeps_the_query_counts_in_diagnostics()
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
        // Every placed actor is outside melee reach. A swing at nothing is a whiff, which the donor
        // shows no text for; what the perception query compared belongs to the playtest diagnostics.
        perception.Receipt = new PerceptionReadoutResult(ReadOnlyMemory<PerceptionPair>.Empty, ReadOnlyMemory<PerceptionAggregate>.Empty, 0, false, 0, 1, 42, 42, 42, 41, 1, 0, 0);
        string before = session.Presentation.LastOutcome;
        session.Update(new ProductUpdate(OuterUpdate(1), [PadButton(ControllerButton.Button0, InputEdge.Pressed), PadButton(ControllerButton.Button0, InputEdge.Released)]));

        Assert.Equal(before, session.Presentation.LastOutcome);
        Assert.DoesNotContain("Nothing in reach", session.Presentation.LastOutcome);
        Dictionary<string, object?> published = (Dictionary<string, object?>)engine.Published()!;
        Assert.DoesNotContain("Nothing in reach", (string)published["lastOutcome"]!);
        using System.Text.Json.JsonDocument readout = System.Text.Json.JsonDocument.Parse(session.ReadPlaytestTargets().Message);
        System.Text.Json.JsonElement query = readout.RootElement.GetProperty("lastMeleeQuery");
        Assert.Equal(System.Text.Json.JsonValueKind.Null, query.GetProperty("selectedTarget").ValueKind);
        Assert.Equal(42, query.GetProperty("observers").GetInt32());
        Assert.Equal(42, query.GetProperty("targets").GetInt32());
        Assert.Equal(42, query.GetProperty("compared").GetInt32());
        Assert.Equal(41, query.GetProperty("outOfRange").GetInt32());
        Assert.Equal(1, query.GetProperty("outOfCone").GetInt32());
        Assert.Equal(0, query.GetProperty("occluded").GetInt32());
    }

    [Fact]
    public void A_held_attack_button_cannot_hide_what_actually_happened()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallActorDefinition rat = definitions.RequireActor(new DaggerfallActorId("rat"));
        PresentationState presentation = new("Ready", DaggerfallOutcomePresentation.MessageLifetimeSeconds);
        DaggerfallOutcomePresentation outcomes = new(presentation, new Dictionary<long, DaggerfallActorDefinition> { [2008] = rat }, definitions.Mobiles);

        // A hit lands, and then the attack button stays held: the cooldown rejection repeats every
        // update, and the player still has to be able to read what happened.
        outcomes.React(new AttackHitFact(1, 2008, 7, 7d, 3, false, 1, 100));
        Assert.Equal("Hit Rat for 7 damage", presentation.LastOutcome);
        for (int repeat = 0; repeat < 40; repeat++)
        {
            outcomes.React(new AttackRejectedFact(AttackRejection.Cooldown));
        }

        Assert.Equal("Hit Rat for 7 damage", presentation.LastOutcome);

        // A blow that met nothing is a result too, and once the result has aged out the rejection shows.
        outcomes.React(new AttackWhiffedFact(1, 2008, false, 1, 200));
        Assert.Equal("Your blow misses Rat", presentation.LastOutcome);
        outcomes.React(new AttackWhiffedFact(2008, DaggerfallActorIdentity.PlayerEntityId, true, 1, 201));
        Assert.Equal("Rat's blow misses you", presentation.LastOutcome);
        presentation.Advance(presentation.LifetimeSeconds);
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
        TargetingService backstabTargeting = CombatCollaborators.Targeting(session.State.Actors, authored, out SpatialMovementSystem backstabSpatial, out SpatialFake contact);
        using SpatialMovementSystem ownedBackstabSpatial = backstabSpatial;
        bool unaware = true;
        DaggerCombatRules combat = CombatCollaborators.SessionRules(session, definitions, authored, backstabTargeting, backstabSpatial,
            random: RandomMaximum.Create(), skillUses: use => session.State.SkillUses.Record(use),
            playerPosition: () => session.State.PlayerControl.Position, targetUnaware: _ => unaware);
        session.State.Actors.Player.Stats.GetStat(StatId.Parse("backstabbing")).BaseValue = 37;

        WorldPoint player = session.State.PlayerControl.Position!.Value;
        ActorState target = session.State.Actors.Get(2000);
        // Every swing lands, and this fact needs the target alive for each of them.
        void Heal() => target.Stats.GetTrack(TrackId.Parse("health")).SetCurrent(target.Stats.GetTrack(TrackId.Parse("health")).Maximum.Value);
        target.ApplyPose(new ActorPose(new WorldPoint(player.X, player.Y, player.Z - 1f), 0f));
        contact.OverlapContactsResult = _ => Touching(target.Actor.Entity.Value);
        FactBuffer<IProductFact> facts = new();
        combat.ResolveExplicit(new ExplicitMeleeRequest(DaggerfallActorIdentity.PlayerEntityId, 9999, 30, 1, .125), facts);
        List<IProductFact> rejectedFacts = [];
        facts.Deliver(rejectedFacts.Add);
        Assert.Contains(rejectedFacts, fact => fact is AttackRejectedFact { Reason: AttackRejection.UnknownExplicitCombatant });
        Assert.Equal(0, session.State.Progression.SkillUses["backstabbing"]);

        combat.ResolveExplicit(new ExplicitMeleeRequest(DaggerfallActorIdentity.PlayerEntityId, 2000, 31, 1, .125), facts);
        List<IProductFact> awayFacts = [];
        facts.Deliver(awayFacts.Add);
        Assert.Single(awayFacts.OfType<AttackHitFact>());
        Assert.Equal(1, session.State.Progression.SkillUses["backstabbing"]);

        Heal();
        combat.ResolveExplicit(new ExplicitMeleeRequest(DaggerfallActorIdentity.PlayerEntityId, 2000, 31, 1, .125), facts);
        List<IProductFact> replayFacts = [];
        facts.Deliver(replayFacts.Add);
        Assert.Contains(replayFacts, fact => fact is AttackRejectedFact { Reason: AttackRejection.Cooldown });
        Assert.Equal(1, session.State.Progression.SkillUses["backstabbing"]);

        // A target that has noticed the player is no opportunity, whichever way it faces.
        unaware = false;
        Heal();
        combat.ResolveExplicit(new ExplicitMeleeRequest(DaggerfallActorIdentity.PlayerEntityId, 2000, 32, 1, .125), facts);
        List<IProductFact> awareFacts = [];
        facts.Deliver(awareFacts.Add);
        Assert.Single(awareFacts.OfType<AttackHitFact>());
        Assert.Equal(1, session.State.Progression.SkillUses["backstabbing"]);

        unaware = true;
        Heal();
        combat.ResolveExplicit(new ExplicitMeleeRequest(DaggerfallActorIdentity.PlayerEntityId, 2000, 33, 1, .125), facts);
        List<IProductFact> unawareFacts = [];
        facts.Deliver(unawareFacts.Add);
        Assert.Single(unawareFacts.OfType<AttackHitFact>());
        Assert.Equal(2, session.State.Progression.SkillUses["backstabbing"]);

        DaggerfallSavePayload saved = DaggerfallSavePayload.Read(session.CaptureSave());
        List<string> restoredReleases = [];
        ContentFake restoredContent = new(restoredReleases);
        PopulateContent(restoredContent, inputs);
        SpatialFake restoredSpatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, restoredReleases);
        EngineContextFake restoredEngine = EngineContextFake.Create(restoredContent, restoredSpatial.Service,
            new AppearanceFake(restoredReleases), PerceptionFake.Create().Service);
        ResolvedCompositionIdentity identity = TestSessions.Resolve(FullContent(root), new GameBundleId("daggerfall.classic")).RequireComposition().Identity;
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
        session.Touches(2000);
        session.Update(AttackUpdate());

        TargetingEvidence evidence = Assert.IsType<TargetingEvidence>(session.LastMeleeTargeting);
        Assert.Equal(2000, evidence.SelectedTargetId);
        // The evidence is a query the Engine was actually sent; the HUD's interaction prompt asks its own afterwards.
        Assert.Contains(evidence.Request, perception.Requests);
        Assert.True(perception.Requests.Count > 1);
        Assert.Equal((ulong)1, evidence.Request.Observers.Span[0].Entity);
        Assert.Equal(2.25d, evidence.Request.Observers.Span[0].MaximumDistance);
        Assert.Equal(.5d, evidence.Request.Observers.Span[0].MinimumFacingCosine);
        Assert.Equal(1, evidence.Receipt.Pairs.Length);
        // The visibility receipt selects the target at admission; that same target takes the swing's
        // damage when its windup ends.
        Assert.Equal(healthBefore, session.State.Actors.Get(2000).Stats.GetTrack(TrackId.Parse("health")).Current);
        for (ulong step = 2; step <= 40; step++)
            session.Update(new ProductUpdate(new ProductUpdateFacts(ProductLifecycleState.Running, 0, 1, step, step, 60, 1, 0, 1d / 60d), []));
        Assert.True(session.State.Actors.Get(2000).Stats.GetTrack(TrackId.Parse("health")).Current < healthBefore);
    }

    [Fact]
    public void A_transition_mid_swing_retires_the_swing_it_can_no_longer_deliver()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        DaggerfallSiteProfile castle = DaggerfallSiteContent.Read(TestSessions.Data(FullContent(root)),
            TestContentFiles.ReadCopy(Path.Combine(root, "content/worldrpg/payloads/daggerfall.castle-necromoghan.json")), definitions);
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

        session.Update(new ProductUpdate(new ProductUpdateFacts(ProductLifecycleState.Running, 1, 1, 1, 1, 60, 3, 0, 1d / 60d), AttackTap));
        double targetHealth = session.State.Actors.Get(2000).Stats.GetTrack(TrackId.Parse("health")).Current;
        double playerHealth = session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).Current;

        // The swing is admitted, charged and still winding up, so the shared state holds the attack
        // even long past its cooldown.
        Assert.False(session.State.Kit.AttackExecution.IsReady(DaggerfallActorIdentity.PlayerEntityId, 1, 500));

        double targetHealthBeforeTransition = session.State.Actors.Get(2000).Stats.GetTrack(TrackId.Parse("health")).Current;
        Assert.Equal(targetHealth, targetHealthBeforeTransition);
        Assert.True(session.TryTransitionTo(castle.ProfileKey));

        // A site change retires the attack in progress, so the charge goes with it:
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

    /// <summary>A session whose hands draw nothing: no weapon art, so no viewmodel, with one enemy in the swing's way.</summary>
    private static (DaggerfallSession Session, Func<double> EnemyHealth) WithoutViewmodel()
    {
        DaggerfallSiteProfile authored = ReadInputs(TestData.RepositoryRoot);
        DaggerfallSiteProfile inputs = new(authored.Project, authored.Geometry, authored.WorldAppearance, authored.InitialLook, authored.Materials, authored.ActorSprites,
            authored.MobileSprites, authored.Audio, authored.ClassicPresentation with
            {
                Weapons = new Dictionary<string, NormalizedClassicWeapon>(),
                CompatibleItemVisuals = new Dictionary<string, string>(),
                UnarmedVisual = null,
            },
            authored.Site, authored.Doors, authored.ProfileKind, authored.ProfileKey.LogicalId, authored.Portals, authored.Anchors.Values.ToArray(),
            authored.Lights, authored.GroundContainerSprites, authored.DungeonMap, authored.DungeonActions, authored.DungeonActionModels,
            authored.InteriorBuilding, authored.QuestMarkers, authored.BillboardSprites);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        EngineContextFake engine = EngineContextFake.Create(content, SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases).Service,
            new AppearanceFake(releases), PerceptionFake.Create().Service);
        DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(TestPayload.Definitions, inputs, DaggerfallTuning.Defaults));
        session.State.WeaponDrawn = true;
        session.Touches(2000);
        session.State.Actors.Player.Stats.GetStat(StatId.Parse("strength")).BaseValue = 60; // classic damage floors at zero; stage a swing this fixture can rely on
        return (session, () => session.State.Actors.Get(2000).Stats.GetTrack(TrackId.Parse("health")).Current);
    }

    /// <summary>A body the player's rays meet between two bearings, degrees right of a look along -Z, at a distance.</summary>
    private readonly record struct BearingBody(long Actor, float FromDegrees, float ToDegrees, float Distance = 1f);

    /// <summary>Answers level rays by bearing, the first listed body in range taking the ray; a body gets a health pool to take a blow from.</summary>
    private static void PlaceByBearing(DaggerfallSession session, params BearingBody[] bodies)
    {
        session.State.PlayerControl.YawRadians = 0f;
        session.State.PlayerControl.PitchRadians = 0f;
        foreach (BearingBody body in bodies)
        {
            Track health = session.State.Actors.Get(body.Actor).Stats.GetTrack(TrackId.Parse("health"));
            health.Maximum.BaseValue = 1000;
            health.SetCurrent(1000);
        }
        session.Spatial().FloorHit = request =>
        {
            if (MathF.Abs(request.Direction.Y) > .9f) return default;
            float bearing = float.RadiansToDegrees(MathF.Atan2(request.Direction.X, -request.Direction.Z));
            foreach (BearingBody body in bodies)
                if (bearing >= body.FromDegrees && bearing <= body.ToDegrees && body.Distance <= request.MaxDistance)
                    return new SpatialHit(true, SpatialHitKind.Entity, session.State.Actors.Get(body.Actor).Actor.Entity.Value, 0, 0, 0, 0, 0, 0, default,
                        request.Origin + request.Direction * body.Distance, -request.Direction, body.Distance, 0d, 0d, false, true);
            return default;
        };
    }

    private static double Wounds(DaggerfallSession session, long actor) => 1000 - session.State.Actors.Get(actor).Stats.GetTrack(TrackId.Parse("health")).Current;

    private static string LastCombatEvent(DaggerfallSession session)
    {
        using JsonDocument observation = JsonDocument.Parse(session.ReadPlaytestObservation().Message);
        return observation.RootElement.GetProperty("lastCombatEvent").GetProperty("kind").GetString()!;
    }

    private static DaggerfallAttackTiming Fast => DaggerfallTuning.Defaults.Attacks.For("long-blade").Fast;

    [Fact]
    public void A_right_to_left_cut_meets_the_right_hand_of_two_adjacent_enemies_and_only_that_one()
    {
        (DaggerfallSession session, Func<double> _) = WithoutViewmodel();
        using DaggerfallSession disposable = session;
        long left = session.SpawnActor("rat", new ActorPose(session.State.Actors.Get(2000).Position, 0f));
        PlaceByBearing(session, new BearingBody(2000, 5f, 20f), new BearingBody(left, -20f, -5f));

        ulong step = 1;
        session.Update(new ProductUpdate(OuterUpdate(step++), AttackTap)); // fastA runs from 37 right to 61 left.
        RunSeconds(session, ref step, Fast.Windup + Fast.Commit + Fast.Recovery + .1d);

        Assert.True(Wounds(session, 2000) > 0d);
        Assert.Equal(0d, Wounds(session, left));
    }

    [Fact]
    public void A_left_to_right_cut_meets_the_left_hand_of_the_same_two()
    {
        (DaggerfallSession session, Func<double> _) = WithoutViewmodel();
        using DaggerfallSession disposable = session;
        long left = session.SpawnActor("rat", new ActorPose(session.State.Actors.Get(2000).Position, 0f));
        // The first cut passes wide of both, and the press in its recovery chains the left-to-right fastB.
        ulong step = 1;
        session.Touches();
        session.Update(new ProductUpdate(OuterUpdate(step++), AttackTap));
        RunSeconds(session, ref step, Fast.Windup + Fast.Commit + 2d / 60d);
        PlaceByBearing(session, new BearingBody(2000, 5f, 20f), new BearingBody(left, -20f, -5f));
        session.Update(new ProductUpdate(OuterUpdate(step++), AttackTap));
        RunSeconds(session, ref step, Fast.Windup + Fast.Commit + 2d / 60d);

        Assert.True(Wounds(session, left) > 0d);
        Assert.Equal(0d, Wounds(session, 2000));
    }

    [Theory]
    [InlineData(40f, 60f, 1f)] // Beside the cut, past where it starts.
    [InlineData(5f, 20f, 3f)] // In its path, beyond the weapon's reach.
    public void A_cut_that_passes_beside_or_short_of_an_enemy_whiffs(float from, float to, float distance)
    {
        (DaggerfallSession session, Func<double> _) = WithoutViewmodel();
        using DaggerfallSession disposable = session;
        PlaceByBearing(session, new BearingBody(2000, from, to, distance));

        ulong step = 1;
        session.Update(new ProductUpdate(OuterUpdate(step++), AttackTap));
        RunSeconds(session, ref step, Fast.Windup + Fast.Commit + 2d / 60d);

        Assert.Equal(0d, Wounds(session, 2000));
        Assert.Equal("AttackWhiffed", LastCombatEvent(session));
    }

    /// <summary>When a body between two bearings takes the blow, in seconds after the tap.</summary>
    private static double BlowLandsAt(float from, float to)
    {
        (DaggerfallSession session, Func<double> _) = WithoutViewmodel();
        using DaggerfallSession disposable = session;
        PlaceByBearing(session, new BearingBody(2000, from, to));
        session.Update(new ProductUpdate(OuterUpdate(1), AttackTap));
        for (ulong step = 2; step < 60; step++)
        {
            session.Update(new ProductUpdate(OuterUpdate(step), []));
            if (Wounds(session, 2000) > 0d) return (step - 1) / 60d;
        }
        throw new Xunit.Sdk.XunitException("The blow never landed.");
    }

    [Fact]
    public void The_blow_resolves_inside_the_hit_window_and_later_for_a_body_later_in_the_arc()
    {
        const double step = 1d / 60d;
        double early = BlowLandsAt(30f, 36f), late = BlowLandsAt(-58f, -50f);

        Assert.InRange(early, Fast.Windup - step / 2d, Fast.Windup + Fast.Commit + step / 2d);
        Assert.InRange(late, Fast.Windup - step / 2d, Fast.Windup + Fast.Commit + step / 2d);
        Assert.True(early < late, $"A body at the arc's start took the blow at {early}, one at its end at {late}.");
    }

    [Fact]
    public void A_sweep_that_meets_nothing_whiffs_when_the_window_closes()
    {
        (DaggerfallSession session, Func<double> _) = WithoutViewmodel();
        using DaggerfallSession disposable = session;
        PlaceByBearing(session);

        ulong step = 1;
        session.Update(new ProductUpdate(OuterUpdate(step++), AttackTap));
        RunSeconds(session, ref step, Fast.Windup + Fast.Commit - 3d / 60d);
        Assert.NotNull(session.State.Actors.Player.Attack.Pending);

        RunSeconds(session, ref step, 4d / 60d);
        Assert.Null(session.State.Actors.Player.Attack.Pending);
        Assert.Equal("AttackWhiffed", LastCombatEvent(session));
    }

    [Fact]
    public void The_blow_lands_by_the_clock_in_its_hit_window_with_no_viewmodel_and_no_marker()
    {
        (DaggerfallSession session, Func<double> health) = WithoutViewmodel();
        using DaggerfallSession disposable = session;
        object appearance = typeof(DaggerfallSession).GetProperty("_appearance", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(session)!;
        Assert.Null(typeof(DaggerfallSiteAppearance).GetField("viewmodel", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(appearance));
        double before = health();
        double windup = DaggerfallTuning.Defaults.Attacks.For("long-blade").Fast.Windup;

        ulong step = 1;
        session.Update(new ProductUpdate(OuterUpdate(step++), AttackTap));
        Assert.NotNull(session.State.Actors.Player.Attack.Pending);
        RunSeconds(session, ref step, windup - 2d / 60d);
        Assert.Equal(before, health());

        RunSeconds(session, ref step, 3d / 60d);
        Assert.True(health() < before);
        Assert.Null(session.State.Actors.Player.Attack.Pending);
    }

    [Fact]
    public void A_landed_blow_catches_world_time_for_a_step_then_lets_it_run()
    {
        List<string> releases = [];
        (DaggerfallSession session, AppearanceFake _, PerceptionFake _) = VisibleEnemySession(releases, distance: 5d);
        using DaggerfallSession disposable = session;
        session.State.WeaponDrawn = true;
        session.Touches(2000);
        session.State.Kit.Rules.RegisterAction(session.DefinitionsByActor[1].ActionId!, new TheBlowItself());
        Track health = session.State.Actors.Get(2000).Stats.GetTrack(TrackId.Parse("health"));
        health.Maximum.BaseValue = 1000;
        health.SetCurrent(1000);

        ulong step = 1;
        session.Update(new ProductUpdate(OuterUpdate(step++), AttackTap));
        int stopped = 0;
        for (int index = 0; index < 90; index++)
        {
            session.Update(new ProductUpdate(OuterUpdate(step++), []));
            if (session.HitStopped) stopped++;
        }

        Assert.True(health.Current < 1000);
        Assert.Equal(DaggerfallTuning.Defaults.Attacks.Strike.HitStopSteps, stopped);
        Assert.False(session.HitStopped);
    }

    /// <summary>A real click or a real hold of one blow on the enemy: what it took off the enemy and the player's stamina.</summary>
    private static (double Damage, double Stamina) MeasuredBlow(bool power)
    {
        List<string> releases = [];
        (DaggerfallSession session, AppearanceFake _, PerceptionFake _) = VisibleEnemySession(releases, distance: 5d);
        using DaggerfallSession disposable = session;
        session.State.WeaponDrawn = true;
        session.Touches(2000);
        session.State.Kit.Rules.RegisterAction(session.DefinitionsByActor[1].ActionId!, new TheBlowItself());
        session.State.Actors.Player.Stats.GetStat(StatId.Parse("strength")).BaseValue = 60; // classic damage floors at zero; stage a swing this fixture can rely on
        Track health = session.State.Actors.Get(2000).Stats.GetTrack(TrackId.Parse("health"));
        health.Maximum.BaseValue = 1000;
        health.SetCurrent(1000);
        ulong step = 1;
        RunSeconds(session, ref step, 1d / 60d); // The first update sizes the pool for the character.
        Track stamina = session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("stamina"));
        double staminaBefore = stamina.Current;
        DaggerfallWeaponClassAttacks attacks = DaggerfallTuning.Defaults.Attacks.For("long-blade");

        if (power)
        {
            session.Update(new ProductUpdate(OuterUpdate(step++), [AttackDown]));
            RunSeconds(session, ref step, DaggerfallTuning.Defaults.Attacks.PowerThresholdSeconds + attacks.PowerCharge + .1d);
            Assert.Equal(1000, health.Current); // Charging spends nothing and strikes nothing.
            Assert.Equal(staminaBefore, stamina.Current);
            session.Update(new ProductUpdate(OuterUpdate(step++), [AttackUp]));
            RunSeconds(session, ref step, attacks.PowerRelease.Windup + 2d / 60d);
        }
        else
        {
            session.Update(new ProductUpdate(OuterUpdate(step++), AttackTap));
            RunSeconds(session, ref step, attacks.Fast.Windup + 2d / 60d);
        }
        return (1000 - health.Current, staminaBefore - stamina.Current);
    }

    [Fact]
    public void A_power_release_does_double_the_damage_for_double_the_stamina_of_a_tap()
    {
        (double tapDamage, double tapStamina) = MeasuredBlow(power: false);
        (double powerDamage, double powerStamina) = MeasuredBlow(power: true);

        Assert.True(tapDamage > 0d);
        Assert.Equal(tapDamage * DaggerfallTuning.Defaults.Attacks.PowerDamageMultiplier, powerDamage);
        Assert.Equal(tapStamina * DaggerfallTuning.Defaults.Attacks.PowerStaminaMultiplier, powerStamina);
    }

    /// <summary>The enemy's natural armour is no part of what a power attack doubles.</summary>
    private sealed class TheBlowItself : ICombatContribution
    {
        public void Mitigate(MitigationEvent value) => value.Absorbed = 0;
    }

    [Fact]
    public void A_kick_lands_from_its_own_binding_for_its_own_cost()
    {
        (DaggerfallSession session, Func<double> health) = WithoutViewmodel();
        using DaggerfallSession disposable = session;
        ulong step = 1;
        RunSeconds(session, ref step, 1d / 60d); // The first update sizes the pool for the character.
        Track stamina = session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("stamina"));
        double before = stamina.Current;
        int spent = 0;
        session.State.Kit.Rules.RegisterAction(session.DefinitionsByActor[1].ActionId!, new CountingBlow(() => spent++));

        session.Update(new ProductUpdate(OuterUpdate(step++), [Input(InputEventKind.MappedDigital, InputEdge.Pressed, x: 1, phase: InputPhase.Pressed, intent: "kick")]));
        Assert.Equal(before - DaggerfallTuning.Defaults.Attacks.KickStaminaCost, stamina.Current);
        RunSeconds(session, ref step, DaggerfallTuning.Defaults.Attacks.Kick.Windup + 2d / 60d);

        Assert.Equal(1, spent);
    }

    private sealed class CountingBlow(Action counted) : ICombatContribution
    {
        public void Damage(DamageEvent value) => counted();
    }

    [Fact]
    public void Guard_and_kick_are_in_the_control_catalog_with_their_keys_labels_and_pad_buttons()
    {
        DaggerfallControlAction guard = Assert.Single(DaggerfallControlSettings.Catalog, action => action.Id == "block");
        DaggerfallControlAction kick = Assert.Single(DaggerfallControlSettings.Catalog, action => action.Id == "kick");

        Assert.Equal(["Secondary"], guard.DefaultKeys);
        Assert.Equal(["KeyG"], kick.DefaultKeys);
        Assert.Equal(("combat", "combat"), (guard.Category, kick.Category));
        Assert.Equal("Guard", DaggerfallControlSettings.Labels["block"]);
        Assert.Equal("Kick", DaggerfallControlSettings.Labels["kick"]);
        Assert.Contains(DaggerfallTuning.Defaults.ControllerInput.Actions, binding => binding.Button == ControllerButton.Button4 && binding.Action == DaggerfallInput.Block);
        Assert.Contains(DaggerfallTuning.Defaults.ControllerInput.Actions, binding => binding.Button == ControllerButton.Button5 && binding.Action == DaggerfallInput.Kick);
    }

    [Fact]
    public void The_pads_guard_button_raises_the_guard_while_held()
    {
        (DaggerfallSession session, Func<double> _) = WithoutViewmodel();
        using DaggerfallSession disposable = session;

        ulong step = 1;
        session.Update(new ProductUpdate(OuterUpdate(step++), [PadButton(ControllerButton.Button4, InputEdge.Pressed)]));
        session.Update(new ProductUpdate(OuterUpdate(step++), []));

        Assert.True(session.PlayerGuardRaised);
    }

    [Fact]
    public void The_pads_attack_button_held_past_the_threshold_charges_a_power_attack()
    {
        (DaggerfallSession session, Func<double> _) = WithoutViewmodel();
        using DaggerfallSession disposable = session;
        ulong step = 1;
        RunSeconds(session, ref step, 1d / 60d); // The first update sizes the pool for the character.
        Track stamina = session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("stamina"));
        double before = stamina.Current;

        session.Update(new ProductUpdate(OuterUpdate(step++), [PadButton(ControllerButton.Button0, InputEdge.Pressed)]));

        // A hold spends nothing until it is released; a tap would have spent the swing already.
        Assert.Equal(before, stamina.Current);
    }

    private static ProductUpdateState AttackUpdate()
    {
        ProductUpdateState update = new(.125f);
        foreach (ProductInputEvent input in AttackTap) update.Add(input);
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
        DaggerCombatRules combat = CombatCollaborators.SessionRules(session, definitions, authored, targeting, targetingSpatial);
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
        Assert.DoesNotContain(impacts, fact => fact is AttackHitFact or AttackWhiffedFact);
        Assert.Equal(0d, session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).Current);

        // The swing is consumed by the dropped impact rather than left blocking its
        // attacker: a same-generation retry past the cooldown is accepted only if the
        // pending entry for this exact (generation, attacker) key is gone.
        session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).SetCurrent(100, clamp: true);
        Assert.True(combat.Attacks.TryBeginEnemyAttack(2000, DaggerfallActorIdentity.PlayerEntityId, 77, 1_000, .125, facts));
    }
}
