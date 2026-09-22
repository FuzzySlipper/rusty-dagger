using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Targeting;
using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using Rusty.Engine.Persistence;
using WorldRpg.Host;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Presentation;
using WorldRpg.Kit.Facts;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.Progression;
using WorldRpg.Kit.World;
using WorldRpg.Kit.Effects;
using WorldRpg.Rulesets.Daggerfall;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.Modules.Behavior;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using WorldRpg.Rulesets.Daggerfall.Modules.Loot;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class NormalizedRuntimeSeamTests
{
    private static readonly ContentSha256 Hash = new(1, 2, 3, 4);

    [Fact]
    public void Selected_RDB_door_visuals_share_their_runtime_pose_and_keep_the_appearance_snapshot_unique()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        AppearanceFake graphics = new(releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, graphics, PerceptionFake.Create().Service);

        using DaggerfallSession session = new(engine.Context, definitions, inputs, DaggerfallTuning.Defaults);
        session.PublishInitial();

        AppearanceFact[] snapshot = Assert.Single(graphics.Snapshots);
        Assert.Equal(snapshot.Length, snapshot.Select(fact => fact.ObjectId).Distinct().Count());
        Assert.Equal(1 + inputs.Doors.Count, graphics.StaticMeshContentRequests.Count);
        foreach (DaggerfallRdbDoorDefinition door in inputs.Doors)
            Assert.Contains(snapshot, fact => fact.Transform.Translation == door.Position);
    }

    [Fact]
    public void Ordinary_rest_opens_a_saved_level_allocation_that_commits_one_health_gain_after_reload()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), PerceptionFake.Create().Service);
        DaggerfallSavePayload saved;
        using (DaggerfallSession original = new(engine.Context, definitions, inputs, DaggerfallTuning.Defaults))
        {
            foreach (DaggerfallStatId skill in definitions.Vocabulary.Skills)
                original.State.Actors.Player.Stats.GetStat(StatId.Parse(skill.Value)).BaseValue = 100;
            _ = original.AdvanceElapsedTime(361);
            DaggerfallLevelUpSave pending = Assert.IsType<DaggerfallLevelUpSave>(original.State.LevelUps.Pending);
            Assert.Equal(1, original.State.Progression.Level);
            saved = DaggerfallSavePayload.Read(original.CaptureSave());
            AssertLevelUpEqual(pending, Assert.IsType<DaggerfallLevelUpSave>(saved.LevelUp));
        }

        ContentFake resumedContent = new(releases);
        PopulateContent(resumedContent, inputs);
        SpatialFake resumedSpatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake resumedEngine = EngineContextFake.Create(resumedContent, resumedSpatial.Service, new AppearanceFake(releases), PerceptionFake.Create().Service);
        ResolvedCompositionIdentity identity = GameCompositionResolver.Resolve(FullContent(root), new GameBundleId("daggerfall.privateers-hold")).RequireComposition().Identity;
        using DaggerfallSession restored = DaggerfallSession.Restore(resumedEngine.Context, identity, definitions, inputs, DaggerfallTuning.Defaults, DaggerfallSavePayload.Encode(saved), RandomMinimum.Create());

        DaggerfallLevelUpSave restoredPending = Assert.IsType<DaggerfallLevelUpSave>(restored.State.LevelUps.Pending);
        AssertLevelUpEqual(Assert.IsType<DaggerfallLevelUpSave>(saved.LevelUp), restoredPending);
        while (restoredPending.Allocations.Sum(allocation => allocation.Points) < restoredPending.BonusPool)
        {
            restored.State.LevelUps.Allocate("strength");
            restoredPending = Assert.IsType<DaggerfallLevelUpSave>(restored.State.LevelUps.Pending);
        }
        restored.State.LevelUps.Commit();

        Assert.Equal(2, restored.State.Progression.Level);
        Assert.Single(restored.State.Actors.Player.Stats.GetStat(StatId.Parse("health-maximum")).Sources,
            source => DaggerfallLevelUpHealthSource.IsForLevel(source, restored.State.Actors.Player.Actor.Entity, 2));
        Assert.Null(DaggerfallSavePayload.Read(restored.CaptureSave()).LevelUp);

        static void AssertLevelUpEqual(DaggerfallLevelUpSave expected, DaggerfallLevelUpSave actual)
        {
            Assert.Equal(expected.Level, actual.Level);
            Assert.Equal(expected.BonusPool, actual.BonusPool);
            Assert.Equal(expected.HealthGain, actual.HealthGain);
            Assert.Equal(expected.Allocations.Select(allocation => (allocation.Attribute, allocation.Points)), actual.Allocations.Select(allocation => (allocation.Attribute, allocation.Points)));
        }
    }

    [Fact]
    public void Quest_instances_keep_same_definition_runs_and_typed_bindings_across_save_restore()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), PerceptionFake.Create().Service);

        DaggerfallSavePayload saved;
        using (DaggerfallSession original = new(engine.Context, definitions, inputs, DaggerfallTuning.Defaults))
        {
            DaggerfallSavePayload baseline = DaggerfallSavePayload.Read(original.CaptureSave());
            ulong rewardItem = baseline.Inventory.UniqueItems.First().EntityId;
            DaggerfallStackSave questGold = baseline.Inventory.Stacks.First();
            DaggerfallSiteRecord place = definitions.Locations.Records.First();
            DaggerfallQuestResourceState[] resources =
            [
                new("_questgiver_", DaggerfallQuestResourceBinding.Actors(2000)),
                new("_mondung_", DaggerfallQuestResourceBinding.Place(new DaggerfallSiteIdSave(place.Region, place.Index))),
                new("_monster_", DaggerfallQuestResourceBinding.Actors(2000)),
                new("_reward_", DaggerfallQuestResourceBinding.Stack(new DaggerfallItemOwnerSave("player", DaggerfallActorIdentity.PlayerEntityId), questGold.StackId)),
            ];
            DaggerfallQuestResourceState[] uniqueResources = resources
                .Select(resource => resource.Symbol == "_reward_" ? resource with { Binding = DaggerfallQuestResourceBinding.UniqueItem(rewardItem) } : resource)
                .ToArray();
            DaggerfallQuestSymbolState[] symbols = [new("_task_state_", "waiting"), new("arbitrary_macro", "Mundus")];
            DaggerfallQuestInstanceSave first = new("00B00Y00:1", "00B00Y00.txt", "00B00Y00", DaggerfallQuestLifecycle.Active, null, resources, symbols);
            DaggerfallQuestInstanceSave second = new("00B00Y00:2", "00B00Y00.txt", "00B00Y00", DaggerfallQuestLifecycle.Active, null, uniqueResources, [new("_task_state_", "other-run")]);
            DaggerfallQuestInstanceSave active = new("00B00Y00:3", "00B00Y00.txt", "00B00Y00", DaggerfallQuestLifecycle.Active, null, resources, []);

            Assert.Throws<ArgumentException>(() => original.State.Quests.Start(first with { SourceFile = "missing.txt" }));
            original.State.Quests.Start(first);
            original.State.Quests.Start(second);
            original.State.Quests.Start(active);
            original.State.Quests.Complete("00B00Y00:1", "reward-delivered");
            original.State.Quests.Fail("00B00Y00:2", "deadline-expired");
            saved = DaggerfallSavePayload.Read(original.CaptureSave());

            Assert.Equal(3, saved.Quests.Instances.Length);
            Assert.NotEmpty(saved.Quests.Instances.Single(instance => instance.InstanceId == "00B00Y00:1").Tasks);
            Assert.Equal(DaggerfallQuestLifecycle.Completed, saved.Quests.Instances.Single(instance => instance.InstanceId == "00B00Y00:1").Lifecycle);
            Assert.Equal(DaggerfallQuestLifecycle.Failed, saved.Quests.Instances.Single(instance => instance.InstanceId == "00B00Y00:2").Lifecycle);
            Assert.Equal(DaggerfallQuestResourceBindingKind.Item, saved.Quests.Instances.Single(instance => instance.InstanceId == "00B00Y00:1").Resources.Single(resource => resource.Symbol == "_reward_").Binding.Kind);
            Assert.Equal(questGold.StackId, saved.Quests.Instances.Single(instance => instance.InstanceId == "00B00Y00:1").Resources.Single(resource => resource.Symbol == "_reward_").Binding.Stacks.Single().StackId);
            Assert.Equal(rewardItem, saved.Quests.Instances.Single(instance => instance.InstanceId == "00B00Y00:2").Resources.Single(resource => resource.Symbol == "_reward_").Binding.UniqueItemIds.Single());
            Assert.Contains(saved.Quests.Instances.Single(instance => instance.InstanceId == "00B00Y00:1").Symbols, symbol => symbol.Symbol == "arbitrary_macro");
        }

        ContentFake resumedContent = new(releases);
        PopulateContent(resumedContent, inputs);
        SpatialFake resumedSpatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake resumedEngine = EngineContextFake.Create(resumedContent, resumedSpatial.Service, new AppearanceFake(releases), PerceptionFake.Create().Service);
        ResolvedCompositionIdentity identity = GameCompositionResolver.Resolve(FullContent(root), new GameBundleId("daggerfall.privateers-hold")).RequireComposition().Identity;
        using DaggerfallSession resumed = DaggerfallSession.Restore(resumedEngine.Context, identity, definitions, inputs, DaggerfallTuning.Defaults, DaggerfallSavePayload.Encode(saved), RandomMinimum.Create());

        JsonObject missingQuestSection = JsonNode.Parse(DaggerfallSavePayload.Encode(saved).Bytes.Span)!.AsObject();
        Assert.True(missingQuestSection.Remove("Quests"));
        Assert.Throws<ArgumentException>(() => DaggerfallSavePayload.Read(new RulesetSavePayload(
            DaggerfallRuleset.Identity, Encoding.UTF8.GetBytes(missingQuestSection.ToJsonString()))));

        Assert.Equal(3, resumed.State.Quests.All.Count);
        Assert.Equal("reward-delivered", resumed.State.Quests.All.Single(instance => instance.InstanceId == "00B00Y00:1").Outcome);
        Assert.Equal("deadline-expired", resumed.State.Quests.All.Single(instance => instance.InstanceId == "00B00Y00:2").Outcome);
        Assert.Equal(DaggerfallQuestLifecycle.Active, resumed.State.Quests.All.Single(instance => instance.InstanceId == "00B00Y00:3").Lifecycle);
        DaggerfallQuestTaskState[] savedTaskState = saved.Quests.Instances.Single(instance => instance.InstanceId == "00B00Y00:3").Tasks;
        DaggerfallQuestTaskState[] restoredTaskState = resumed.State.Quests.All.Single(instance => instance.InstanceId == "00B00Y00:3").Tasks;
        Assert.Equal(savedTaskState.Select(task => (task.Symbol, task.Kind, task.IsSet, task.WasSet, task.IsDropped)), restoredTaskState.Select(task => (task.Symbol, task.Kind, task.IsSet, task.WasSet, task.IsDropped)));
        Assert.Equal(savedTaskState.Select(task => task.OperationCompleted), restoredTaskState.Select(task => task.OperationCompleted), EqualityComparer<bool[]>.Create((left, right) => left!.SequenceEqual(right), value => value.Aggregate(0, (hash, bit) => HashCode.Combine(hash, bit))));

        DaggerfallQuestInstanceSave firstWithMissingDefinition = saved.Quests.Instances.Single(instance => instance.InstanceId == "00B00Y00:1") with { SourceFile = "missing.txt" };
        DaggerfallSavePayload missingDefinition = saved with
        {
            Quests = new([.. saved.Quests.Instances.Where(instance => instance.InstanceId != "00B00Y00:1"), firstWithMissingDefinition]),
        };
        Assert.Throws<ArgumentException>(() => DaggerfallSession.Restore(resumedEngine.Context, identity, definitions, inputs, DaggerfallTuning.Defaults, DaggerfallSavePayload.Encode(missingDefinition), RandomMinimum.Create()));

        DaggerfallQuestInstanceSave firstWithDanglingActor = saved.Quests.Instances.Single(instance => instance.InstanceId == "00B00Y00:1") with
        {
            Resources = saved.Quests.Instances.Single(instance => instance.InstanceId == "00B00Y00:1").Resources
                .Select(resource => resource.Symbol == "_questgiver_" ? resource with { Binding = DaggerfallQuestResourceBinding.Actors(999_999) } : resource)
                .ToArray(),
        };
        DaggerfallSavePayload dangling = saved with
        {
            Quests = new([.. saved.Quests.Instances.Where(instance => instance.InstanceId != "00B00Y00:1"), firstWithDanglingActor]),
        };
        Assert.Throws<ArgumentException>(() => DaggerfallSession.Restore(resumedEngine.Context, identity, definitions, inputs, DaggerfallTuning.Defaults, DaggerfallSavePayload.Encode(dangling), RandomMinimum.Create()));

        DaggerfallQuestInstanceSave firstWithDanglingStack = saved.Quests.Instances.Single(instance => instance.InstanceId == "00B00Y00:1") with
        {
            Resources = saved.Quests.Instances.Single(instance => instance.InstanceId == "00B00Y00:1").Resources
                .Select(resource => resource.Symbol == "_reward_" ? resource with { Binding = DaggerfallQuestResourceBinding.Stack(new DaggerfallItemOwnerSave("player", DaggerfallActorIdentity.PlayerEntityId), "missing-stack") } : resource)
                .ToArray(),
        };
        DaggerfallSavePayload missingStack = saved with
        {
            Quests = new([.. saved.Quests.Instances.Where(instance => instance.InstanceId != "00B00Y00:1"), firstWithDanglingStack]),
        };
        Assert.Throws<ArgumentException>(() => DaggerfallSession.Restore(resumedEngine.Context, identity, definitions, inputs, DaggerfallTuning.Defaults, DaggerfallSavePayload.Encode(missingStack), RandomMinimum.Create()));
    }

    [Fact]
    public void Mapped_attack_press_reaches_combat_once_without_held_or_catchup_replay()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        PerceptionFake perception = PerceptionFake.Create();
        perception.Receipt = Receipt(new PerceptionPair(1, 2000, 1d, 1d, PerceptionPairKind.Visible, 1d));
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), perception.Service);
        using DaggerfallSession session = new(engine.Context, definitions, inputs, DaggerfallTuning.Defaults);
        double before = session.State.Actors.Get(2000).Stats.GetTrack(TrackId.Parse("health")).Current;
        double staminaBefore = session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("stamina")).Current;
        ProductInputEvent pressed = Input(InputEventKind.MappedDigital, InputEdge.Pressed, x: 1, phase: InputPhase.Pressed, intent: "attack");
        session.Update(new ProductUpdateFacts(ProductUpdateMode.Realtime, ProductLifecycleState.Running, 1, 1, 1, 1, 60, 3, 0, 1d / 60d), [pressed, pressed]);
        double after = session.State.Actors.Get(2000).Stats.GetTrack(TrackId.Parse("health")).Current;
        Assert.True(after < before);
        Assert.True(session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("stamina")).Current < staminaBefore);
        session.Update(new ProductUpdateFacts(ProductUpdateMode.Realtime, ProductLifecycleState.Running, 2, 1, 1, 100, 60, 3, 0, 1d / 60d),
            [Input(InputEventKind.MappedDigital, InputEdge.Held, x: 1, phase: InputPhase.Held, intent: "attack")]);
        Assert.Equal(after, session.State.Actors.Get(2000).Stats.GetTrack(TrackId.Parse("health")).Current);
    }

    [Fact]
    public void Admitted_player_hits_record_skill_uses_once_per_operation_and_preserve_them_through_save()
    {
        string root = RepositoryRoot();
        PrivateersHoldInputs inputs = ReadInputs(root);
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        PerceptionFake perception = PerceptionFake.Create();
        perception.Receipt = Receipt(new PerceptionPair(1, 2000, 1d, 1d, PerceptionPairKind.Visible, 1d));
        AppearanceFake appearance = new(releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, appearance, perception.Service);
        using DaggerfallSession session = new(engine.Context, definitions, inputs, DaggerfallTuning.Defaults);
        ProductInputEvent pressed = Input(InputEventKind.MappedDigital, InputEdge.Pressed, x: 1, phase: InputPhase.Pressed, intent: "attack");
        ProductUpdateFacts first = new(ProductUpdateMode.Realtime, ProductLifecycleState.Running, 1, 1, 1, 1, 60, 1, 0, 1d / 60d);

        session.Update(new ProductUpdate(first, [pressed]));
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
        using DaggerfallSession restored = DaggerfallSession.Restore(resumedEngine.Context, identity, definitions, inputs, DaggerfallTuning.Defaults, DaggerfallSavePayload.Encode(saved), RandomMinimum.Create());

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
        string root = RepositoryRoot();
        PrivateersHoldInputs inputs = ReadInputs(root);
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        PerceptionFake perception = PerceptionFake.Create();
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), perception.Service);
        using DaggerfallSession session = new(engine.Context, definitions, inputs, DaggerfallTuning.Defaults);
        using SpatialMovementSystem targetingSpatial = new(spatial.Service, content, inputs.SpatialArtifact, DaggerfallTuning.Defaults.Spatial);
        Dictionary<long, DaggerfallActorDefinition> authored = inputs.Project.Actors.Values.ToDictionary(
            placement => placement.EntityId,
            placement => definitions.RequireActor(placement.ActorId));
        authored[DaggerfallActorIdentity.PlayerEntityId] = definitions.RequireActor(new DaggerfallActorId("player"));
        authored[2000] = authored[2000] with { MinimumMaterial = "daedric" };
        TargetingService targeting = new(perception.Service, targetingSpatial, session.State.Actors, new DaggerTargetingPolicy(authored, DaggerfallTuning.Defaults.MeleeTargeting));
        DaggerCombatRules combat = new(RandomMinimum.Create(), session.State.Actors, session.State.Equipment, session.State.InventoryFor,
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
        DaggerCombatRules steelCombat = new(RandomMinimum.Create(), session.State.Actors, session.State.Equipment, session.State.InventoryFor,
            session.State.ItemInstances, definitions, authored, targeting);

        steelCombat.ResolveExplicit(new ExplicitMeleeRequest(DaggerfallActorIdentity.PlayerEntityId, 2000, 9, 30, .125), facts);
        List<IProductFact> steelCanHit = [];
        facts.Deliver(steelCanHit.Add);
        Assert.Contains(steelCanHit, fact => fact is AttackHitFact);
        Assert.DoesNotContain(new AttackRejectedFact(AttackRejection.InsufficientWeaponMaterial), steelCanHit);
    }

    [Fact]
    public void Grounded_spawns_use_engine_floor_hits_while_flying_markers_keep_their_height()
    {
        string root = RepositoryRoot();
        PrivateersHoldInputs inputs = ReadInputs(root);
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        spatial.FloorHit = request => new SpatialHit { Present = true, Point = request.Origin - Vector3.UnitY, Normal = Vector3.UnitY };
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));

        using DaggerfallSession session = new(engine.Context, definitions, inputs, DaggerfallTuning.Defaults);

        foreach (AuthoredActor source in inputs.Project.Actors.Values)
        {
            WorldPoint actual = session.State.Actors.Get(source.EntityId).Position;
            bool grounded = definitions.Actors[source.ActorId].GroundOnSpawn;
            Assert.Equal(source.Position.Y + (grounded ? DaggerfallTuning.Defaults.EnemyBehavior.SpawnGroundProbeLift - 1f : 0f), actual.Y, precision: 4);
            Assert.Equal(source.Position.X, actual.X);
            Assert.Equal(source.Position.Z, actual.Z);
        }
        Assert.Equal(inputs.Project.Actors.Values.Count(actor => definitions.Actors[actor.ActorId].GroundOnSpawn), spatial.FloorProbes.Count);
        Assert.All(spatial.FloorProbes, request => Assert.Equal(-Vector3.UnitY, request.Direction));
    }

    [Fact]
    public void Every_authored_enemy_has_a_live_appearance_at_its_world_position()
    {
        string root = RepositoryRoot();
        PrivateersHoldInputs inputs = ReadInputs(root);
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        AppearanceFake appearance = new(releases);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, appearance);
        using DaggerfallSession session = new(engine.Context, definitions, inputs, DaggerfallTuning.Defaults);
        // The outer update owns final publication: simulation completes first, then one snapshot.
        session.Update(new ProductUpdate(new ProductUpdateFacts(ProductUpdateMode.Realtime, ProductLifecycleState.Running, 1, 1, 1, 1, 60, 1, 0, 1d / 60d), []));

        AppearanceFact[] snapshot = appearance.Snapshots.Last();
        foreach (AuthoredActor actor in inputs.Project.Actors.Values)
        {
            Assert.False(session.State.Actors.Get(actor.EntityId).IsDefeated);
            AppearanceFact fact = Assert.Single(snapshot, value => value.ObjectId == checked((ulong)actor.EntityId));
            Assert.True(fact.Visible);
            Assert.Equal(RenderLayer.Scene, fact.Layer);
            Assert.Equal(new Transform(actor.Position.ToVector(), Quaternion.Identity, Vector3.One), fact.Transform);
        }
    }

    [Fact]
    public void Spatial_system_admits_one_content_artifact_and_releases_session_before_reference()
    {
        List<string> releases = [];
        ContentFake content = new("spatial/hold.json", Hash, releases);
        SpatialFake spatial = SpatialFake.Create(Hash, releases);
        SpatialTuning tuning = new(.5, 32, 32, 2);

        using (SpatialMovementSystem system = new(spatial.Service, content, new SpatialContentArtifact("spatial/hold.json", Hash, 7), tuning))
        {
            Assert.Equal(1, spatial.ReplaceCalls);
            Assert.Equal(0, spatial.ReadCalls);
            Assert.Equal((ulong)7, spatial.LastRequest!.Value.NavigationGridId);
            Assert.Equal((ulong)1, spatial.LastRequest.Value.Content.Handle.Value);
        }

        Assert.Equal(["session", "content"], releases);
    }

    [Fact]
    public void Rejected_controller_config_does_not_create_a_session_or_admit_content()
    {
        List<string> releases = [];
        ContentFake content = new("spatial/hold.json", Hash, releases);
        SpatialFake spatial = SpatialFake.Create(Hash, releases);
        spatial.RejectConfigValidation = true;

        Assert.Throws<InvalidOperationException>(() => new SpatialMovementSystem(spatial.Service, content, new SpatialContentArtifact("spatial/hold.json", Hash, 7), new SpatialTuning(.5, 32, 32, 2)));

        Assert.Equal(1, spatial.ConfigValidationCalls);
        Assert.Equal(0, spatial.CreateSessionCalls);
        Assert.Equal(0, content.ResolveCalls);
    }

    [Fact]
    public void Spatial_system_layers_only_ruleset_controller_overrides_and_persists_the_engine_receipt()
    {
        List<string> releases = [];
        ContentFake content = new("spatial/hold.json", Hash, releases);
        SpatialFake spatial = SpatialFake.Create(Hash, releases);
        SpatialTuning tuning = new(.5, 32, 32, 2, new CharacterControllerTuning(
            StandingHeight: 1.8f,
            Radius: .25f,
            ForwardSpeed: 3.5f,
            BackwardSpeed: 3.5f,
            StrafeSpeed: 3.5f,
            RecoveryMaximumDistance: 1f,
            MaximumStepHeight: .75f));
        PlayerControlState player = new(new WorldPoint(1f, 2f, 3f), .25f, 0f);

        using (SpatialMovementSystem system = new(spatial.Service, content, new SpatialContentArtifact("spatial/hold.json", Hash, 7), tuning))
        {
            system.Step(player, new ProductUpdateState(1f / 60f) { PlanarIntent = new Vector2(1f, 0f) });
            system.Step(player, new ProductUpdateState(1f / 60f) { PlanarIntent = new Vector2(0f, 1f) });
        }

        CharacterStepRequest first = spatial.StepRequests[0];
        CharacterControllerConfig config = first.Config;
        Assert.Equal(1.8f, config.Shape.StandingHeight);
        Assert.Equal(.25f, config.Shape.Radius);
        Assert.Equal(3.5f, config.Ground.ForwardSpeed);
        Assert.Equal(3.5f, config.Ground.BackwardSpeed);
        Assert.Equal(3.5f, config.Ground.StrafeSpeed);
        Assert.Equal(1f, config.Recovery.MaximumDistance);
        Assert.Equal(.75f, config.Surface.MaximumStepHeight);
        Assert.Equal(spatial.RepresentativeValidConfig.Shape.CrouchedHeight, config.Shape.CrouchedHeight);
        Assert.Equal(spatial.RepresentativeValidConfig.Ground.Acceleration, config.Ground.Acceleration);
        Assert.Equal(spatial.RepresentativeValidConfig.Recovery.MaximumSpeed, config.Recovery.MaximumSpeed);
        Assert.Equal(spatial.RepresentativeValidConfig.Surface.FloorSnapDistance, config.Surface.FloorSnapDistance);
        Assert.Equal(1, spatial.ConfigValidationCalls);
        Assert.Equal(0, spatial.CommandValidationCalls);
        Assert.Equal([1UL, 2UL], spatial.StepRequests.Select(request => request.Command.Sequence));
        Assert.Equal(.25f, first.Command.HeadingYawRadians);
        Assert.Equal(new Vector2(1f, 0f), first.Command.PlanarIntent);
        Assert.Equal(new WorldPoint(3f, 2f, 3f), player.Position);
        Assert.True(player.Motion.Grounded);
        Assert.True(player.Ground.Present);
    }

    [Fact]
    public void Restored_spatial_continuation_can_be_captured_again_before_its_next_step()
    {
        List<string> releases = [];
        ContentFake content = new("spatial/hold.json", Hash, releases);
        SpatialFake source = SpatialFake.Create(Hash, releases);
        SpatialFake resumed = SpatialFake.Create(Hash, releases);
        SpatialTuning tuning = new(.5, 32, 32, 2);
        PlayerControlState player = new(new WorldPoint(0, 0, 0), 0, 0);
        using SpatialMovementSystem first = new(source.Service, content, new SpatialContentArtifact("spatial/hold.json", Hash, 7), tuning);
        first.Step(player, new ProductUpdateState(.125f));
        CharacterContinuationCheckpoint checkpoint = first.CaptureContinuation();

        using SpatialMovementSystem second = new(resumed.Service, content, new SpatialContentArtifact("spatial/hold.json", Hash, 7), tuning);
        CharacterContinuationRestoreReceipt receipt = second.RestoreContinuation(checkpoint);

        Assert.Equal(checkpoint.SourceGeneration, receipt.SourceGeneration);
        Assert.True(second.HasContinuation);
        Assert.Equal(checkpoint, second.CaptureContinuation());
    }

    [Fact]
    public void Zero_controller_overrides_are_layered_then_left_for_engine_validation()
    {
        List<string> releases = [];
        ContentFake content = new("spatial/hold.json", Hash, releases);
        SpatialFake spatial = SpatialFake.Create(Hash, releases);
        SpatialTuning tuning = new(.5, 32, 32, 2, new CharacterControllerTuning(
            ForwardSpeed: 0f,
            BackwardSpeed: 0f,
            StrafeSpeed: 0f,
            RecoveryMaximumDistance: 0f,
            MaximumStepHeight: 0f));
        PlayerControlState player = new(new WorldPoint(1f, 2f, 3f), 0f, 0f);

        using (SpatialMovementSystem system = new(spatial.Service, content, new SpatialContentArtifact("spatial/hold.json", Hash, 7), tuning))
            system.Step(player, new ProductUpdateState(1f / 60f));

        CharacterControllerConfig config = Assert.Single(spatial.StepRequests).Config;
        Assert.Equal(0f, config.Ground.ForwardSpeed);
        Assert.Equal(0f, config.Ground.BackwardSpeed);
        Assert.Equal(0f, config.Ground.StrafeSpeed);
        Assert.Equal(0f, config.Recovery.MaximumDistance);
        Assert.Equal(0f, config.Surface.MaximumStepHeight);
        Assert.Equal(1, spatial.ConfigValidationCalls);
    }

    [Fact]
    public void Spatial_step_submits_current_control_state_and_applies_the_engine_receipt()
    {
        List<string> releases = [];
        ContentFake content = new("spatial/hold.json", Hash, releases);
        SpatialFake spatial = SpatialFake.Create(Hash, releases);
        PlayerControlState player = new(new WorldPoint(1f, 2f, 3f), 0f, 0f);
        ProductUpdateState update = new(1f / 60f);
        update.PlanarIntent = new Vector2(.25f, .5f);

        using SpatialMovementSystem system = new(spatial.Service, content, new SpatialContentArtifact("spatial/hold.json", Hash, 7), new SpatialTuning(.5, 32, 32, 2));
        system.Step(player, update);

        CharacterStepRequest request = Assert.Single(spatial.StepRequests);
        Assert.Equal(update.PlanarIntent, request.Command.PlanarIntent);
        Assert.Equal(new WorldPoint(2f, 2f, 3f), player.Position);
        Assert.Equal(1UL, player.Motion.LastCommandSequence);
    }

    [Fact]
    public void Daggerfall_tuning_exposes_its_controller_values_in_loaded_payloads()
    {
        string root = RepositoryRoot();
        DaggerfallTuning tuning = DaggerfallTuning.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/tuning-payloads/daggerfall.defaults.json")));
        CharacterControllerTuning controller = Assert.IsType<CharacterControllerTuning>(tuning.Spatial.CharacterController);

        Assert.Equal(3.5f, controller.ForwardSpeed);
        Assert.Equal(3.5f, controller.BackwardSpeed);
        Assert.Equal(3.5f, controller.StrafeSpeed);
        Assert.Equal(1.8f, controller.StandingHeight);
        Assert.Equal(.25f, controller.Radius);
        Assert.Equal(1f, controller.RecoveryMaximumDistance);
        Assert.Equal(.75f, controller.MaximumStepHeight);
        Assert.Equal(2.25d, tuning.MeleeTargeting.MaximumDistance);
        Assert.Equal(.5d, tuning.MeleeTargeting.MinimumFacingCosine);
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
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs inputs = ReadInputs(root);
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
        using DaggerfallSession session = new(engine.Context, withoutPolicies, inputs, DaggerfallTuning.Defaults);
        double healthBefore = session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).Current;
        appearance.AdvanceReceiptForAll = CrossedMarker(1);

        session.Update(new ProductUpdate(OuterUpdate(1), []));

        Assert.All(session.LastEnemyBehavior.Values, evidence =>
            Assert.Equal(EnemyBehaviorState.Idle, evidence.State));
        Assert.Equal(healthBefore, session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).Current);
    }

    /// <summary>
    /// The tuning profile shape the ruleset reads is the one it ships, and a profile still carrying the
    /// key that used to tune enemy reach is refused rather than loaded with its intent dropped.
    /// </summary>
    /// <remarks>
    /// Reach is authored on the action that carries it now, so the key is no longer read. Loading a profile
    /// that still names it would apply every other value and silently discard the tuning the operator set,
    /// which is the kind of difference no diagnostic would ever surface.
    /// </remarks>
    [Fact]
    public void An_obsolete_enemy_reach_tuning_key_is_refused_rather_than_ignored()
    {
        string root = RepositoryRoot();
        string path = Path.Combine(root, "content/worldrpg/tuning-payloads/daggerfall.defaults.json");
        using JsonDocument profile = JsonDocument.Parse(File.ReadAllBytes(path));
        Dictionary<string, object?> mutated = [];
        foreach (JsonProperty property in profile.RootElement.EnumerateObject())
        {
            if (property.NameEquals("enemyBehavior"))
            {
                Dictionary<string, object?> behavior = [];
                foreach (JsonProperty entry in property.Value.EnumerateObject()) behavior[entry.Name] = entry.Value.Clone();
                behavior["attackReach"] = 9.99;
                mutated[property.Name] = behavior;
                continue;
            }

            mutated[property.Name] = property.Value.Clone();
        }

        string obsolete = JsonSerializer.Serialize(mutated);
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => DaggerfallTuning.Read(Encoding.UTF8.GetBytes(obsolete)));
        Assert.Contains("attackReach", error.Message, StringComparison.Ordinal);
        // The shipped profile does not carry it, so the refusal is the shape's and not the payload's.
        Assert.DoesNotContain("attackReach", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public void The_pads_mapping_is_tuning_and_every_payload_agrees_with_the_ruleset_defaults()
    {
        string root = RepositoryRoot();
        ControllerInputTuning loaded = DaggerfallTuning.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/tuning-payloads/daggerfall.defaults.json"))).ControllerInput;
        ControllerInputTuning scenario = DaggerfallTuning.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/tuning-payloads/daggerfall.privateers-hold.json"))).ControllerInput;

        AssertSamePad(DaggerfallTuning.Defaults.ControllerInput, loaded);
        AssertSamePad(loaded, scenario);

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
        string root = RepositoryRoot();
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
        foreach (string payload in new[] { "daggerfall.defaults.json", "daggerfall.privateers-hold.json" })
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
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        DaggerfallTuning tuning = DaggerfallTuning.Defaults;

        using (DaggerfallSession session = new(engine.Context, definitions, inputs, tuning))
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
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));

        List<string> requested = [];
        using (DaggerfallSession session = new(engine.Context, definitions, inputs, DaggerfallTuning.Defaults))
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
        string root = RepositoryRoot();
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

    [Fact]
    public void The_pad_owns_its_controls_and_the_compiled_product_mapping_declares_none_of_them()
    {
        string root = RepositoryRoot();
        // Pad bindings are tuning because the Engine's controller vocabulary is positional and a
        // different pad has to be a configuration change rather than a rebuild. That makes the
        // compiled product mapping the wrong place for a controller trigger: declaring one there
        // would describe the same press twice, once as a mapped intent and once as the raw fact the
        // pad tuning reads, and nothing else would notice.
        //
        // The check reads the artifact the runtime reads rather than one spelling in one build file,
        // so it sees any declaration site and any legal MSBuild form. It is build output, so its
        // absence is a failure: a guard that passes when it cannot read what it guards is worse than
        // no guard.
        string manifest = Path.Combine(root, "src/WorldRpg.Host/obj/Rusty.Engine/Product/product.json");
        Assert.True(File.Exists(manifest), $"The compiled product manifest '{manifest}' is missing; the host composition has to be built before this suite reads it.");
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
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));

        using DaggerfallSession session = new(engine.Context, definitions, inputs, DaggerfallTuning.Defaults);
        session.Update(new ProductUpdate(OuterUpdate(1), [PadButton(ControllerButton.Button8, InputEdge.Pressed)]));
        Assert.Equal("inventory", engine.PublishedNested("panelRequest", "panel"));

        // A request that outlived the page that performed it would re-open a panel nobody asked for on
        // the next load, so it ages out on admitted world time like the message line does.
        for (ulong step = 2; step <= 62; step++) session.Update(new ProductUpdate(OuterUpdate(step), []));
        Assert.Null(engine.PublishedNested("panelRequest", "panel"));
        Assert.Null(session.LatestPanelRequest);
    }

    [Fact]
    public void A_melee_request_that_found_nothing_reports_what_the_query_actually_saw()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        PerceptionFake perception = PerceptionFake.Create();
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), perception.Service);

        using DaggerfallSession session = new(engine.Context, definitions, inputs, DaggerfallTuning.Defaults);
        // Every placed actor is outside melee reach: the receipt says so, and the product has to say so
        // too rather than printing the same line it would print in a world where nothing is visible.
        perception.Receipt = new PerceptionReadoutLeaseReceipt(ReadOnlyMemory<PerceptionPair>.Empty, ReadOnlyMemory<PerceptionAggregate>.Empty, 0, false, 0, 1, 42, 42, 42, 41, 1, 0, 0);
        session.Update(new ProductUpdate(OuterUpdate(1), [PadButton(ControllerButton.Button0, InputEdge.Pressed)]));

        Assert.Equal("No target in melee reach (42 observer(s) against 42 target(s), 42 compared: 41 out of range, 1 out of cone, 0 cast, 0 occluded)", session.Presentation.LastOutcome);
        // The same line is what the DOM draws, so a human sees the counters too.
        Dictionary<string, object?> published = (Dictionary<string, object?>)engine.Published()!;
        Assert.Contains("No target in melee reach (42 observer(s) against 42 target(s)", (string)published["lastOutcome"]!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_held_attack_button_cannot_hide_what_actually_happened()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        DaggerfallActorDefinition rat = definitions.RequireActor(new DaggerfallActorId("rat"));
        PresentationState presentation = new("Ready");
        DaggerfallOutcomePresentation outcomes = new(presentation, new Dictionary<long, DaggerfallActorDefinition> { [2008] = rat });

        // A hit lands, and then the attack button stays held: the cooldown rejection repeats every
        // update, and the player still has to be able to read what happened.
        outcomes.React(new AttackHitFact(1, 2008, 7, 3, false, 1, 100));
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
    public void The_archer_carries_a_ranged_policy_its_donor_record_supports()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));

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
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs inputs = ReadInputs(root);
        List<DaggerfallActorId> swinging = [];
        foreach (AuthoredActor placement in inputs.Project.Actors.Values)
        {
            DaggerfallActorDefinition definition = definitions.RequireActor(placement.ActorId);
            // Every placed actor carries a policy. There is no exception list: the archer's ranged attack
            // is the capability this check was waiting for, and an actor placed without one is an oversight
            // rather than a disposition.
            Assert.NotNull(definition.ActionId);
            DaggerfallActionDefinition action = definitions.Actions[definition.ActionId!];
            Assert.Contains(action.Interpretation, new[] { "fixed-melee", "fixed-ranged" });
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
    public void Two_panel_buttons_in_one_slice_leave_the_later_request_standing()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));

        using DaggerfallSession session = new(engine.Context, definitions, inputs, DaggerfallTuning.Defaults);
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
    public void Realtime_substeps_reuse_postlook_held_movement_with_one_sequence_each()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs inputs = ReadInputs(root);
        // The authored start pose is π. With yaw wrapping enabled, the
        // admitted positive pointer delta crosses into [-π, π), rather than
        // leaving the product with an unbounded yaw accumulator.
        float expectedYaw = -MathF.PI + (.25f * DaggerfallTuning.Defaults.PlayerControl.LookSensitivity);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        ProductInputEvent[] input =
        [
            Input(InputEventKind.Key, InputEdge.Pressed, keyboard: KeyboardControl.KeyW),
            Input(InputEventKind.PointerDelta, x: .25f, y: -.5f),
        ];

        using (DaggerfallSession session = new(engine.Context, definitions, inputs, DaggerfallTuning.Defaults))
        {
            session.Update(new ProductUpdateFacts(ProductUpdateMode.Realtime, ProductLifecycleState.Running, 3, 1, 1, 1, 60, 3, 0, 1d / 60d), input);
        }

        Assert.Equal([1UL, 2UL, 3UL], spatial.StepRequests.Select(request => request.Command.Sequence));
        Assert.All(spatial.StepRequests, request =>
        {
            Assert.Equal(new Vector2(0f, 1f), request.Command.PlanarIntent);
            Assert.Equal(expectedYaw, request.Command.HeadingYawRadians);
            Assert.Equal(1f / 60f, request.Command.StepSeconds);
        });
    }

    [Fact]
    public void Direct_axis_is_first_substep_only_while_held_input_continues()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));

        using (DaggerfallSession session = new(engine.Context, definitions, inputs, DaggerfallTuning.Defaults))
        {
            session.Update(new ProductUpdateFacts(ProductUpdateMode.Realtime, ProductLifecycleState.Running, 3, 1, 1, 1, 60, 3, 0, 1d / 60d), [Input(InputEventKind.DirectAxis, x: .5f, y: .75f, intent: "move")]);
        }

        Assert.Equal(new Vector2(.5f, .75f), spatial.StepRequests[0].Command.PlanarIntent);
        Assert.Equal(Vector2.Zero, spatial.StepRequests[1].Command.PlanarIntent);
        Assert.Equal(Vector2.Zero, spatial.StepRequests[2].Command.PlanarIntent);
    }

    [Fact]
    public void Direct_digital_movement_is_first_substep_only()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));

        using (DaggerfallSession session = new(engine.Context, definitions, inputs, DaggerfallTuning.Defaults))
        {
            session.Update(new ProductUpdateFacts(ProductUpdateMode.Realtime, ProductLifecycleState.Running, 3, 1, 1, 1, 60, 3, 0, 1d / 60d), [Input(InputEventKind.DirectDigital, x: 1f, intent: "move")]);
        }

        Assert.Equal(new Vector2(0f, 1f), spatial.StepRequests[0].Command.PlanarIntent);
        Assert.Equal(Vector2.Zero, spatial.StepRequests[1].Command.PlanarIntent);
        Assert.Equal(Vector2.Zero, spatial.StepRequests[2].Command.PlanarIntent);
    }

    [Fact]
    public void Native_step_failure_keeps_applied_input_without_claiming_rollback()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        using DaggerfallSession session = new(engine.Context, definitions, inputs, DaggerfallTuning.Defaults);
        WorldPoint? positionBefore = session.State.PlayerControl.Position;
        CharacterMotion motionBefore = session.State.PlayerControl.Motion;
        CharacterGround groundBefore = session.State.PlayerControl.Ground;
        float yawBefore = session.State.PlayerControl.YawRadians;
        float pitchBefore = session.State.PlayerControl.PitchRadians;
        spatial.RejectProposedStep = true;

        Assert.Throws<InvalidOperationException>(() => session.Update(new ProductUpdateFacts(ProductUpdateMode.Realtime, ProductLifecycleState.Running, 1, 1, 1, 1, 60, 1, 0, 1d / 60d),
        [
            Input(InputEventKind.Key, InputEdge.Pressed, keyboard: KeyboardControl.KeyW),
            Input(InputEventKind.PointerDelta, x: .25f, y: -.5f),
            Input(InputEventKind.DirectDigital, x: 1f, phase: InputPhase.DirectUi, intent: "attack"),
        ]));

        Assert.Equal(0, spatial.CommandValidationCalls);
        Assert.Equal(0, spatial.StepCalls);
        Assert.Equal(positionBefore, session.State.PlayerControl.Position);
        Assert.Equal(motionBefore, session.State.PlayerControl.Motion);
        Assert.Equal(groundBefore, session.State.PlayerControl.Ground);
        Assert.NotEqual(yawBefore, session.State.PlayerControl.YawRadians);
        Assert.NotEqual(pitchBefore, session.State.PlayerControl.PitchRadians);
        // The outer Engine callback treats this exception as terminal; there is no same-instance replay.
    }

    [Fact]
    public void Call_local_support_and_obstacles_are_forwarded_without_a_product_registry()
    {
        List<string> releases = [];
        ContentFake content = new("spatial/hold.json", Hash, releases);
        SpatialFake spatial = SpatialFake.Create(Hash, releases);
        PlayerControlState player = new(new WorldPoint(1f, 2f, 3f), 0f, 0f) { Motion = default(CharacterMotion) with { LastCommandSequence = 41 } };
        Transform transform = new(Vector3.One, Quaternion.Identity, Vector3.One);
        CharacterSupport support = new(true, CharacterSupportLifecycle.Active, 99, transform);
        CharacterObstacle obstacle = new(100, transform, new Vector3(-1f), Vector3.One, true, Vector3.Zero, Vector3.Zero);

        using (SpatialMovementSystem system = new(spatial.Service, content, new SpatialContentArtifact("spatial/hold.json", Hash, 7), new SpatialTuning(.5, 32, 32, 2)))
            system.Step(player, new ProductUpdateState(1f / 60f), new CharacterStepEnvironment(support, new[] { obstacle }));

        CharacterStepRequest request = Assert.Single(spatial.StepRequests);
        Assert.Equal(support, request.Support);
        Assert.Equal([obstacle], request.Obstacles.ToArray());
        Assert.Equal(42UL, request.Command.Sequence);
    }

    [Fact]
    public void Appearance_uses_normalized_material_slots_and_atlases_then_releases_dependents_in_order()
    {
        List<string> releases = [];
        ContentFake content = new("mesh/hold.json", Hash, releases);
        content.Add("texture/wall.png", Hash);
        content.Add("sprite/rat.png", Hash);
        AppearanceFake appearance = new(releases);
        PrivateersHoldInputs inputs = new(
            new ProjectFacts(null, new Dictionary<long, AuthoredActor>()),
            new SpatialContentArtifact("spatial/hold.json", Hash, 1),
            new ContentArtifact("mesh/hold.json", Hash),
            new AuthoredWorldAppearance(new Color(1, 1, 1, 1), new Transform(Vector3.Zero, Quaternion.Identity, Vector3.One), true, RenderLayer.Scene),
            new PlayerInitialLook(0, 0),
            [new NormalizedMaterial(3, "texture/wall.png", Hash)],
            new Dictionary<long, NormalizedActorSprite>
            {
                [11] = new("sprite/rat.png", Hash, 32, 32, [new NormalizedAtlasFrame(0, 0, 0, 16, 16)], 0, new Vector2(.5F, 0F), Vector2.One),
            });

        using (PrivateersHoldAppearance presentation = new(content, appearance, inputs))
        {
            Assert.Equal([3u], appearance.StaticMeshBindings.Select(binding => binding.MaterialSlot));
            Assert.Single(appearance.AtlasRequests);
            Assert.Single(appearance.SpriteRequests);
            presentation.Dispose();
        }

        Assert.True(releases.IndexOf("appearance") < releases.IndexOf("atlas"));
        Assert.True(releases.IndexOf("atlas") < releases.IndexOf("material"));

        // Engine resources are owned by whoever opens them now, so the appearance releases each one it
        // opened (the material texture and the actor sprite texture) after the materials that name them.
        Assert.Equal(2, appearance.ReleasedResources);
        Assert.True(releases.IndexOf("material") < releases.IndexOf("resource"));
    }

    [Fact]
    public void Normalized_actor_states_preserve_all_directional_sectors_and_select_an_explicit_sector()
    {
        PrivateersHoldInputs inputs = ReadInputs(RepositoryRoot());
        NormalizedSpriteState state = inputs.ActorSprites.Values
            .SelectMany(sprite => sprite.States.Values)
            .First(value => value.Orientations.Count == 8);

        Assert.Equal(Enumerable.Range(0, 8), state.Orientations.Keys.OrderBy(key => key));
        foreach ((int sector, IReadOnlyList<uint> frames) in state.Orientations)
        {
            Assert.NotEmpty(frames);
            Assert.Equal(frames, state.SelectOrientation(sector));
        }

        NormalizedSpriteState sparse = new("idle", [10], 8F, true)
        {
            Orientations = new Dictionary<int, IReadOnlyList<uint>>
            {
                [0] = [10],
                [4] = [40],
            },
        };
        Assert.Equal([40u], sparse.SelectOrientation(4));
        Assert.Throws<InvalidOperationException>(() => sparse.SelectOrientation(1));
    }

    [Fact]
    public void Authored_actor_presentation_resolves_rest_state_and_effective_playback_without_mobile_specific_runtime_policy()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        string scenario = File.ReadAllText(Path.Combine(root, "content/worldrpg/payloads/daggerfall.privateers-hold.json"));

        PrivateersHoldInputs ratInputs = PrivateersHoldContent.Read(ImportContent(root), Encoding.UTF8.GetBytes(scenario), definitions);
        NormalizedActorSprite rat = SpriteFor(ratInputs, "rat");
        Assert.Equal("ratIdle", rat.PreferredRestState);

        PrivateersHoldInputs impInputs = PrivateersHoldContent.Read(ImportContent(root), Encoding.UTF8.GetBytes(scenario.Replace("\"actor\": \"rat\"", "\"actor\": \"imp\"", StringComparison.Ordinal)), definitions);
        Assert.Equal("move", SpriteFor(impInputs, "imp").PreferredRestState);
        Assert.Equal(10F, SpriteFor(impInputs, "imp").States["move"].EffectiveFramesPerSecond);
        Assert.DoesNotContain("idle", SpriteFor(impInputs, "imp").States.Keys);

        PrivateersHoldInputs batInputs = PrivateersHoldContent.Read(ImportContent(root), Encoding.UTF8.GetBytes(scenario.Replace("\"actor\": \"rat\"", "\"actor\": \"giant-bat\"", StringComparison.Ordinal)), definitions);
        Assert.Equal("move", SpriteFor(batInputs, "giant-bat").PreferredRestState);
        Assert.Equal(10F, SpriteFor(batInputs, "giant-bat").States["move"].EffectiveFramesPerSecond);
        Assert.DoesNotContain("idle", SpriteFor(batInputs, "giant-bat").States.Keys);

        NormalizedActorSprite ordinary = SpriteFor(ratInputs, "skeletal-warrior");
        Assert.Equal("idle", ordinary.PreferredRestState);
        Assert.All(ordinary.States.Values, state => Assert.Equal(state.FramesPerSecond, state.EffectiveFramesPerSecond));
    }

    [Fact]
    public void Normalized_media_parses_an_imported_preferred_rest_state_and_rejects_unknown_presentation_states()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        byte[] scenario = File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.privateers-hold.json"));

        ProductContent withImportedPreference = MutateDungeonMedia(root, media => media["actors"]!.AsArray()
            .Single(value => value!["mobileId"]!.GetValue<int>() == 15)!["preferredRestState"] = "idle");
        PrivateersHoldInputs inputs = PrivateersHoldContent.Read(withImportedPreference, scenario, definitions);
        Assert.Equal("idle", SpriteFor(inputs, "skeletal-warrior").PreferredRestState);

        Assert.Throws<DaggerfallContentException>(() => PrivateersHoldContent.Read(
            MutateDungeonMedia(root, media => media["actors"]!.AsArray().Single(value => value!["mobileId"]!.GetValue<int>() == 15)!["preferredRestState"] = "missingState"),
            scenario,
            definitions));

        DaggerfallDefinitions unknownAuthoredState = DaggerfallBaseContent.Read(Encoding.UTF8.GetBytes(File.ReadAllText(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")).Replace("\"preferredRestState\": \"ratIdle\"", "\"preferredRestState\": \"missingState\"", StringComparison.Ordinal)));
        Assert.Throws<DaggerfallContentException>(() => PrivateersHoldContent.Read(ImportContent(root), scenario, unknownAuthoredState));
    }

    [Fact]
    public void Directional_sprite_sectors_follow_actor_heading_with_classic_octant_boundaries()
    {
        Assert.Equal(0, PrivateersHoldAppearance.RelativeSector(0f, 0f, -1f));
        Assert.Equal(4, PrivateersHoldAppearance.RelativeSector(0f, 0f, 1f));
        Assert.Equal(6, PrivateersHoldAppearance.RelativeSector(0f, 1f, 0f));
        Assert.Equal(2, PrivateersHoldAppearance.RelativeSector(0f, -1f, 0f));
        Assert.Equal(0, PrivateersHoldAppearance.RelativeSector(MathF.PI / 2f, 1f, 0f));
        Assert.Equal(0, PrivateersHoldAppearance.RelativeSector(0f, 0f, 0f));

        float twentyTwo = 22f * MathF.PI / 180f;
        float twentyThree = 23f * MathF.PI / 180f;
        float halfSector = MathF.PI / 8f;
        Assert.Equal(0, PrivateersHoldAppearance.RelativeSector(0f, MathF.Sin(twentyTwo), -MathF.Cos(twentyTwo)));
        Assert.Equal(7, PrivateersHoldAppearance.RelativeSector(0f, MathF.Sin(halfSector), -MathF.Cos(halfSector)));
        Assert.Equal(1, PrivateersHoldAppearance.RelativeSector(0f, -MathF.Sin(halfSector), -MathF.Cos(halfSector)));
        Assert.Equal(7, PrivateersHoldAppearance.RelativeSector(0f, MathF.Sin(twentyThree), -MathF.Cos(twentyThree)));
        Assert.Equal(1, PrivateersHoldAppearance.RelativeSector(0f, -MathF.Sin(twentyThree), -MathF.Cos(twentyThree)));
    }

    [Fact]
    public void Enemy_idle_transition_returns_to_the_authored_preferred_rest_state()
    {
        List<string> releases = [];
        using PrivateersHoldAppearance presentation = new(MediaContent(releases), new AppearanceFake(releases), MediaInputs(preferredRestState: "ratIdle"));

        presentation.React(new EnemyBehaviorTransitionFact(11, EnemyBehaviorState.Idle, EnemyBehaviorState.Chase, 1, 1));
        Assert.Equal("move", Visual(presentation).State);
        presentation.React(new EnemyBehaviorTransitionFact(11, EnemyBehaviorState.Chase, EnemyBehaviorState.Idle, 1, 2));

        Assert.Equal("ratIdle", Visual(presentation).State);
    }

    [Fact]
    public void Direction_change_maps_the_current_idle_and_attack_frame_without_recreating_playback()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        AppearanceFake appearance = new(releases);
        using PrivateersHoldAppearance presentation = new(content, appearance, MediaInputs(directional: true));
        using ActorsState actors = ActorsWithNpc(11, HealthyMechanics(), new WorldPoint(0f, 0f, 0f));

        presentation.UpdateDirections(actors, new WorldPoint(0f, 0f, -1f));
        Assert.Equal(0u, appearance.SetFrameRequests.Last().FrameId);
        presentation.React(new EnemyAttackStartedFact(11, 12, true, 1, 1));
        int playbackCount = appearance.PlaybackRequests.Count;
        appearance.AdvanceReceipts.Enqueue(new SpritePlaybackAdvanceLeaseReceipt(
            ReadOnlyMemory<SpritePlaybackMarkerCrossing>.Empty,
            new SpritePlaybackReadout(2, 0, SpritePlaybackState.Playing, 0d, 0, 1, false),
            true));
        presentation.Advance(OuterUpdate(1));
        presentation.UpdateDirections(actors, new WorldPoint(1f, 0f, 0f));

        Assert.Equal(playbackCount, appearance.PlaybackRequests.Count);
        Assert.Equal(3u, appearance.SetFrameRequests.Last().FrameId);
    }

    [Fact]
    public void Attack_alternate_selection_is_keyed_by_stable_event_identity_and_respects_authored_weights()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        AppearanceFake appearance = new(releases);
        KeyedRandomFake random = KeyedRandomFake.Create(40);
        PrivateersHoldInputs favoredAlternate = MediaInputs(primaryChance: 60);

        using (PrivateersHoldAppearance first = new(content, appearance, favoredAlternate, random: random.Service))
        {
            first.React(new EnemyAttackStartedFact(11, 12, true, 7, 9));
            SpritePlaybackCreateRequest selected = appearance.PlaybackRequests.Last();
            Assert.Equal([3u], selected.Frames.Span.ToArray().Select(frame => frame.FrameId));

            int beforeDuplicate = appearance.PlaybackRequests.Count;
            first.React(new EnemyAttackStartedFact(11, 12, true, 7, 9));
            Assert.Equal(beforeDuplicate, appearance.PlaybackRequests.Count);
        }

        AppearanceFake secondAppearance = new(releases);
        using (PrivateersHoldAppearance second = new(content, secondAppearance, MediaInputs(primaryChance: 20), random: KeyedRandomFake.Create(40).Service))
        {
            second.React(new EnemyAttackStartedFact(11, 12, true, 7, 9));
            Assert.Equal([2u], secondAppearance.PlaybackRequests.Last().Frames.Span.ToArray().Select(frame => frame.FrameId));
        }

        Assert.Equal("daggerfall.media.attack-alternate.v1", random.Requests[0].Scope);
        Assert.Equal("daggerfall.media.hit-cue.v1", random.Requests[1].Scope);
    }

    [Fact]
    public void A_ranged_mobile_plays_its_published_ranged_state_for_every_attack_without_the_melee_roll()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        AppearanceFake appearance = new(releases);
        KeyedRandomFake random = KeyedRandomFake.Create(40);
        using PrivateersHoldAppearance presentation = new(content, appearance, MediaInputs(rangedFrames: [3, 2, 0, 0, 0, -1, 1, 1, 2, 3]), random: random.Service);

        presentation.React(new EnemyAttackStartedFact(11, 12, true, 7, 9));

        // The donor plays a ranged mobile's ranged animation for every attack, so the published
        // ranged sequence replaces the melee alternate pool instead of joining it: its authored
        // frame order plays verbatim, and the marker step becomes the playback marker the donor
        // launches its missile on, with no alternate roll drawn.
        Assert.Equal("rangedAttack1", Visual(presentation).State);
        SpritePlaybackCreateRequest request = appearance.PlaybackRequests.Last();
        Assert.Equal(new uint[] { 3, 2, 0, 0, 0, 1, 1, 2, 3 }, request.Frames.Span.ToArray().Select(frame => frame.FrameId).ToArray());
        Assert.Equal([new SpritePlaybackMarker(6, 5)], request.Markers.Span.ToArray());
        Assert.DoesNotContain(random.Requests, request => request.Scope == "daggerfall.media.attack-alternate.v1");
    }

    [Fact]
    public void A_mobile_without_a_ranged_declaration_keeps_playing_its_melee_pool()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        AppearanceFake appearance = new(releases);
        using PrivateersHoldAppearance presentation = new(content, appearance, MediaInputs(primaryFrames: [0, -1, 1], includeAlternate: false));

        presentation.React(new EnemyAttackStartedFact(11, 12, true, 7, 9));

        Assert.Equal("primaryAttack", Visual(presentation).State);
        SpritePlaybackCreateRequest request = appearance.PlaybackRequests.Last();
        Assert.Equal(new uint[] { 2, 3 }, request.Frames.Span.ToArray().Select(frame => frame.FrameId).ToArray());
        Assert.Equal([new SpritePlaybackMarker(2, 1)], request.Markers.Span.ToArray());
    }

    [Fact]
    public void Marker_crossings_are_consumed_once_without_emitting_a_second_combat_presentation()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        AppearanceFake appearance = new(releases);
        AudioRecorder audio = AudioRecorder.Create();
        appearance.AdvanceReceipts.Enqueue(new SpritePlaybackAdvanceLeaseReceipt(
            new[] { new SpritePlaybackMarkerCrossing(1, 3, 1, 0, 1) },
            new SpritePlaybackReadout(3, 1, SpritePlaybackState.Playing, 0D, 0, 1, false),
            true));
        appearance.AdvanceReceipts.Enqueue(new SpritePlaybackAdvanceLeaseReceipt(
            new[] { new SpritePlaybackMarkerCrossing(1, 3, 1, 0, 1) },
            new SpritePlaybackReadout(3, 1, SpritePlaybackState.Playing, 0D, 0, 2, false),
            true));

        using PrivateersHoldAppearance presentation = new(content, appearance, MediaInputs(primaryFrames: [0, -1, 1]), audio.Service);
        presentation.React(new EnemyAttackStartedFact(11, 12, true, 3, 4));
        int playbacksBefore = appearance.PlaybackRequests.Count;
        int audioBefore = audio.Emits.Count;

        presentation.Advance(OuterUpdate(1));
        presentation.Advance(OuterUpdate(2));

        Assert.Equal(playbacksBefore, appearance.PlaybackRequests.Count);
        // The authored damage frame is the strike beat: it sounds once and reports the
        // impact once, and the ruleset — not the presentation — owns the consequence.
        Assert.Equal(audioBefore + 1, audio.Emits.Count);
        AttackImpactNotice impact = Assert.Single(presentation.TakeAttackImpacts());
        Assert.False(impact.Expired);
        Assert.Equal(11, impact.AttackerId);
        Assert.Equal(12, impact.TargetId);
        Assert.Empty(presentation.TakeAttackImpacts());
        FieldInfo actorsField = typeof(PrivateersHoldAppearance).GetField("actors", BindingFlags.Instance | BindingFlags.NonPublic)!;
        System.Collections.IDictionary visuals = (System.Collections.IDictionary)actorsField.GetValue(presentation)!;
        object visual = visuals[11L]!;
        FieldInfo crossingField = visual.GetType().GetField("<LastMarkerCrossing>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!;
        ulong consumed = (ulong)crossingField.GetValue(visual)!;
        Assert.Equal((ulong)1, consumed);
    }

    [Fact]
    public void Duplicate_attack_delivery_does_not_restart_playback_or_duplicate_tuned_audio()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        AppearanceFake appearance = new(releases);
        AudioRecorder audio = AudioRecorder.Create();
        DaggerfallPresentationAudioTuning tuning = new(.25F, 1.5F, .75F, 12F);
        using PrivateersHoldAppearance presentation = new(content, appearance, MediaInputs(), audio.Service, tuning);
        EnemyAttackStartedFact hit = new(11, 12, true, 7, 9);

        presentation.React(hit);
        int playbackCount = appearance.PlaybackRequests.Count;
        presentation.React(hit);

        Assert.Equal(playbackCount, appearance.PlaybackRequests.Count);
        AudioEmitRequest emitted = Assert.Single(audio.Emits);
        Assert.Equal(.25F, emitted.Descriptor.Volume);
        Assert.Equal(1.5F, emitted.Descriptor.Pitch);
        Assert.Equal(.75F, emitted.Descriptor.SpatialBlend);
        Assert.Equal(12F, emitted.Descriptor.Attenuation);
        Assert.True(float.IsFinite(emitted.Descriptor.Attenuation));
        Assert.True(emitted.Descriptor.Attenuation > 0F);
    }

    [Fact]
    public void Completed_one_shot_returns_to_rest_on_the_next_distinct_outer_update_even_when_engine_does_not_advance()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        AppearanceFake appearance = new(releases);
        using PrivateersHoldAppearance presentation = new(content, appearance, MediaInputs());
        presentation.React(new EnemyAttackStartedFact(11, 12, true, 7, 9));
        int beforeCompletion = appearance.PlaybackRequests.Count;
        appearance.AdvanceReceipts.Enqueue(new SpritePlaybackAdvanceLeaseReceipt(
            ReadOnlyMemory<SpritePlaybackMarkerCrossing>.Empty,
            new SpritePlaybackReadout(2, 0, SpritePlaybackState.Completed, 0D, 0, 1, true),
            true));
        appearance.AdvanceReceipts.Enqueue(new SpritePlaybackAdvanceLeaseReceipt(
            ReadOnlyMemory<SpritePlaybackMarkerCrossing>.Empty,
            new SpritePlaybackReadout(2, 0, SpritePlaybackState.Completed, 0D, 0, 2, true),
            false));

        presentation.Advance(OuterUpdate(1));
        Assert.Equal(beforeCompletion, appearance.PlaybackRequests.Count);

        presentation.Advance(OuterUpdate(2));
        Assert.Equal(beforeCompletion + 1, appearance.PlaybackRequests.Count);
        Assert.Equal([0u], appearance.PlaybackRequests.Last().Frames.Span.ToArray().Select(frame => frame.FrameId));
        int advancesAfterRest = appearance.AdvanceRequests.Count;

        presentation.Advance(OuterUpdate(2));
        Assert.Equal(advancesAfterRest, appearance.AdvanceRequests.Count);
        Assert.Equal(beforeCompletion + 1, appearance.PlaybackRequests.Count);
    }

    [Fact]
    public void Appearance_failures_release_staged_handles_and_keep_the_previous_playback_live()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        AppearanceFake constructionFailure = new(releases) { FailSpritePlaybackCreateAt = 1 };
        Assert.Throws<InvalidOperationException>(() => new PrivateersHoldAppearance(content, constructionFailure, MediaInputs()));
        Assert.Equal(constructionFailure.CreatedAtlases, constructionFailure.DisposedAtlases);
        Assert.Equal(constructionFailure.CreatedAppearances, constructionFailure.DisposedAppearances);

        AppearanceFake replacementFailure = new(releases);
        using PrivateersHoldAppearance presentation = new(content, replacementFailure, MediaInputs());
        SpritePlaybackHandle original = Assert.Single(replacementFailure.CreatedPlaybacks).Handle;
        replacementFailure.FailSpritePlaybackControlAt = replacementFailure.ControlRequests.Count + 1;
        Assert.Throws<InvalidOperationException>(() => presentation.React(new EnemyAttackStartedFact(11, 12, true, 7, 9)));

        presentation.Advance(OuterUpdate(1));
        Assert.Equal(original, Assert.Single(replacementFailure.AdvanceRequests).Playback.Handle);
        Assert.Equal(1, replacementFailure.DisposedPlaybacks);
    }

    [Fact]
    public void Trailing_attack_marker_is_rejected_before_engine_playback_creation()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        AppearanceFake appearance = new(releases);
        PrivateersHoldInputs inputs = MediaInputs(primaryFrames: [0, -1]);
        using PrivateersHoldAppearance presentation = new(content, appearance, inputs);
        int before = appearance.PlaybackRequests.Count;

        Assert.Throws<InvalidOperationException>(() => presentation.React(new EnemyAttackStartedFact(11, 12, true, 7, 9)));
        Assert.Equal(before, appearance.PlaybackRequests.Count);
    }

    [Fact]
    public void Actor_atlas_frames_preserve_normalized_per_crop_world_geometry()
    {
        List<string> releases = [];
        AppearanceFake appearance = new(releases);
        IReadOnlyList<NormalizedAtlasFrame> crops =
        [
            new NormalizedAtlasFrame(0, 0, 0, 4, 8, new Vector2(1.5F, 3F)),
            new NormalizedAtlasFrame(1, 4, 0, 6, 12, new Vector2(2.25F, 4.5F)),
            new NormalizedAtlasFrame(2, 10, 0, 10, 5, new Vector2(3.75F, 1.875F)),
            new NormalizedAtlasFrame(3, 20, 0, 12, 16, new Vector2(4.5F, 6F)),
        ];
        using PrivateersHoldAppearance presentation = new(MediaContent(releases), appearance, MediaInputs(actorFrames: crops));

        SpriteAtlasFrame[] frames = appearance.AtlasRequests.Single().Frames.Span.ToArray();
        Assert.All(frames, frame => Assert.True(frame.HasSize));
        Assert.Equal(crops.Select(crop => crop.DisplaySize), frames.Select(frame => (Vector2?)frame.Size));
        Assert.Equal(Vector2.One, appearance.SpriteRequests.Single().Size);
    }

    [Fact]
    public void Presentation_failure_is_terminal_and_disposal_releases_staged_resources()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        AppearanceFake appearance = new(releases);
        AudioRecorder audio = AudioRecorder.Create();
        PrivateersHoldAppearance presentation = new(content, appearance, MediaInputs(), audio.Service);
        EnemyAttackStartedFact hit = new(11, 12, true, 17, 23);

        presentation.BeginAdmittedUpdate();
        presentation.React(hit);
        SpritePlayback staged = Visual(presentation).Playback!;
        appearance.FailPublishAt = appearance.PublishCalls + 1;
        using ActorsState actors = EmptyActors();
        Assert.Throws<InvalidOperationException>(() => presentation.Publish(actors));

        presentation.Dispose();

        Assert.Contains(staged.Handle, appearance.DisposedPlaybackHandles);
        Assert.NotEmpty(releases);
    }

    [Fact]
    public void Classic_textures_are_admitted_during_construction_and_retired_playback_releases_on_the_next_admission()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        content.Add("weapon/dagger.png", Hash);
        AppearanceFake appearance = new(releases);
        NormalizedClassicPresentation weapon = ClassicWeapon();
        NormalizedClassicPresentation classic = new(weapon.Weapons, ClassicEffects().Effects)
        {
            CompatibleItemVisuals = weapon.CompatibleItemVisuals,
            Viewmodel = weapon.Viewmodel,
        };
        using PrivateersHoldAppearance presentation = new(content, appearance, MediaInputs(classic: classic));
        int constructionResourceRequests = appearance.OpenResourceRequests.Count;
        appearance.RejectLateResourceOpen = true;
        presentation.UpdateRightHandEquipment(RightHand("iron-dagger"));
        SpritePlayback actorPlayback = Visual(presentation).Playback!;
        presentation.BeginAdmittedUpdate();
        presentation.React(new PlayerAttackStartedFact(2, 3));
        presentation.React(new EnemyAttackStartedFact(11, 12, true, 2, 3));
        presentation.React(new AttackMissedFact(DaggerfallActorIdentity.PlayerEntityId, 12, 1, 1, false, 2, 3));
        presentation.React(new AttackMissedFact(DaggerfallActorIdentity.PlayerEntityId, 12, 1, 2, false, 2, 3));
        using ActorsState actors = ActorsAt(new WorldPoint(2F, 0F, 3F));
        presentation.React(new AttackHitFact(DaggerfallActorIdentity.PlayerEntityId, 12, 1, 3, false, 2, 3), actors);
        presentation.React(new AttackHitFact(DaggerfallActorIdentity.PlayerEntityId, 12, 1, 4, false, 2, 3), actors);

        presentation.CompleteAdmittedUpdate();
        presentation.BeginAdmittedUpdate();

        Assert.Equal(constructionResourceRequests, appearance.OpenResourceRequests.Count);
        Assert.Contains(actorPlayback.Handle, appearance.DisposedPlaybackHandles);
        Assert.Empty(appearance.OpenResourceRequests.Skip(constructionResourceRequests));
    }

    [Fact]
    public void Failed_outer_update_disposes_staged_appearance_and_reraises()
    {
        static ProductInputEvent Ui(string json) => Input(InputEventKind.DirectDigital) with
        {
            ValueKind = InputValueKind.ProductPayload,
            PayloadContract = "dagger.ui.action.v1"u8.ToArray(),
            PayloadData = Encoding.UTF8.GetBytes(json),
        };

        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        PerceptionFake perception = PerceptionFake.Create();
        AppearanceFake appearance = new(releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, appearance, perception.Service);
        using DaggerfallSession session = new(engine.Context, definitions, inputs, DaggerfallTuning.Defaults);
        // Baseline success on a fresh weapon: no swing staged yet.
        session.Update(new ProductUpdate(OuterUpdate(1), []));
        int playbacksBefore = appearance.CreatedPlaybacks.Count;

        // The next admitted update stages a weapon strike, then its publish throws.
        // The callback is terminal; it is disposed rather than rolled back for retry.
        appearance.FailPublishAt = appearance.PublishCalls + 1;
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
            () => session.Update(new ProductUpdate(OuterUpdate(2), [Ui("{\"action\":\"attack\"}")])));
        Assert.Equal("Injected presentation publish failure.", failure.Message);

        Assert.True(appearance.CreatedPlaybacks.Count > playbacksBefore, "The failed update should have staged new appearance playback before its publish threw.");
        foreach (SpritePlayback staged in appearance.CreatedPlaybacks.Skip(playbacksBefore))
            Assert.Contains(staged.Handle, appearance.DisposedPlaybackHandles);
    }

    [Fact]
    public void Retired_playback_is_lagged_to_the_next_admitted_update()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        AppearanceFake appearance = new(releases);
        using PrivateersHoldAppearance presentation = new(content, appearance, MediaInputs());
        SpritePlayback original = Visual(presentation).Playback!;
        presentation.React(new EnemyAttackStartedFact(11, 12, true, 1, 2));

        Assert.Equal(0, appearance.DisposedPlaybacks);
        presentation.BeginAdmittedUpdate();
        Assert.Equal(1, appearance.DisposedPlaybacks);
        Assert.Equal(original.Handle, appearance.DisposedPlaybackHandles[0]);
        appearance.CommitPendingPlaybackReleases();
        presentation.CompleteAdmittedUpdate();
    }

    [Fact]
    public void Non_advanced_receipts_cannot_consume_markers_or_complete_a_one_shot()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        AppearanceFake appearance = new(releases);
        AudioRecorder audio = AudioRecorder.Create();
        using PrivateersHoldAppearance presentation = new(content, appearance, MediaInputs(primaryFrames: [0, -1, 1]), audio.Service);
        presentation.React(new EnemyAttackStartedFact(11, 12, true, 2, 3));
        appearance.AdvanceReceipts.Enqueue(new SpritePlaybackAdvanceLeaseReceipt(
            new[] { new SpritePlaybackMarkerCrossing(1, 3, 1, 0, 1) },
            new SpritePlaybackReadout(3, 1, SpritePlaybackState.Completed, 0D, 0, 1, true),
            false));

        presentation.Advance(OuterUpdate(1));

        PrivateersHoldAppearance.ActorVisual visual = Visual(presentation);
        Assert.Equal((ulong)0, visual.LastMarkerCrossing);
        Assert.False(visual.CompletedOuterUpdate);
        Assert.Empty(audio.Emits);
    }

    [Fact]
    public void Missed_attack_markers_never_emit_hit_variants()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        AppearanceFake appearance = new(releases);
        AudioRecorder audio = AudioRecorder.Create();
        using PrivateersHoldAppearance presentation = new(content, appearance, MediaInputs(primaryFrames: [0, -1, 1]), audio.Service);
        presentation.React(new EnemyAttackStartedFact(11, 12, false, 2, 3));
        appearance.AdvanceReceipts.Enqueue(new SpritePlaybackAdvanceLeaseReceipt(
            new[] { new SpritePlaybackMarkerCrossing(1, 3, 1, 0, 1) },
            new SpritePlaybackReadout(3, 1, SpritePlaybackState.Playing, 0D, 0, 1, false),
            true));

        presentation.Advance(OuterUpdate(1));

        Assert.DoesNotContain(audio.Emits, emitted => emitted.SignalId.Contains("hit", StringComparison.Ordinal));
    }

    [Fact]
    public void Hit_variant_is_keyed_by_event_identity_and_can_select_beyond_the_first_classic_hit_clip()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        EnemyAttackStartedFact hit = new(11, 12, true, 8, 13);
        AudioRecorder firstAudio = AudioRecorder.Create();
        AppearanceFake firstAppearance = new(releases);
        PrivateersHoldInputs hitInputs = MediaInputs(includeAlternate: false);
        using (PrivateersHoldAppearance first = new(content, firstAppearance, hitInputs, firstAudio.Service, random: KeyedRandomFake.Create(5).Service))
        {
            first.React(hit);
            AudioEmitRequest emitted = Assert.Single(firstAudio.Emits);
            Assert.Equal((ulong)6, emitted.Descriptor.Clip.Handle.Value);
        }

        // The clips an appearance opens are its own now, so closing it releases every one of them.
        Assert.Equal(hitInputs.Audio.Count, firstAudio.ReleasedClips);

        AudioRecorder secondAudio = AudioRecorder.Create();
        using (PrivateersHoldAppearance second = new(content, new AppearanceFake(releases), MediaInputs(includeAlternate: false), secondAudio.Service, random: KeyedRandomFake.Create(5).Service))
        {
            second.React(hit);
        }

        Assert.Equal(firstAudio.Emits.Single().SignalId, secondAudio.Emits.Single().SignalId);
        Assert.Equal(firstAudio.Emits.Single().Descriptor.Clip.Handle, secondAudio.Emits.Single().Descriptor.Clip.Handle);
    }

    [Fact]
    public void Hit_variant_selection_uses_the_authored_contiguous_catalog_cardinality()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        AudioRecorder audio = AudioRecorder.Create();
        IReadOnlyList<NormalizedAudioClip> authoredAudio =
        [
            new NormalizedAudioClip("swing", "audio/swing.wav", Hash),
            new NormalizedAudioClip("hit1", "audio/hit.wav", Hash),
            new NormalizedAudioClip("hit2", "audio/hit2.wav", Hash),
        ];

        using PrivateersHoldAppearance presentation = new(content, new AppearanceFake(releases), MediaInputs(includeAlternate: false, audio: authoredAudio), audio.Service, random: KeyedRandomFake.Create(2).Service);
        presentation.React(new EnemyAttackStartedFact(11, 12, true, 8, 13));

        Assert.Equal((ulong)3, Assert.Single(audio.Emits).Descriptor.Clip.Handle.Value);
    }

    [Fact]
    public void Classic_blood_effect_is_keyed_to_the_player_hit_and_retires_after_its_final_frame()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        AppearanceFake appearance = new(releases);
        using PrivateersHoldAppearance presentation = new(content, appearance, MediaInputs(classic: ClassicEffects()));
        WorldPoint targetPosition = new(7F, 2F, -3F);
        using ActorsState actors = ActorsAt(new WorldPoint(1F, 1F, 1F));
        presentation.Publish(actors);
        actors.Get(12).ApplyPose(new ActorPose(targetPosition, 0F));
        AttackHitFact hit = new(DaggerfallActorIdentity.PlayerEntityId, 12, 1, 0, false, 5, 8);

        presentation.React(hit, actors);
        presentation.React(hit, actors);

        Assert.Equal(2, appearance.PlaybackRequests.Count);
        Assert.Equal(1, EffectCount(presentation));
        Assert.Equal(targetPosition, Effect(presentation).Position);
        appearance.AdvanceReceipts.Enqueue(default);
        appearance.AdvanceReceipts.Enqueue(new SpritePlaybackAdvanceLeaseReceipt(default, new SpritePlaybackReadout(0, 0, SpritePlaybackState.Completed, 0D, 0, 1, true), true));
        presentation.Advance(OuterUpdate(1));
        Assert.Equal(1, EffectCount(presentation));
        appearance.AdvanceReceipts.Enqueue(default);
        appearance.AdvanceReceipts.Enqueue(new SpritePlaybackAdvanceLeaseReceipt(default, new SpritePlaybackReadout(0, 0, SpritePlaybackState.Completed, 0D, 0, 2, true), true));
        presentation.Advance(OuterUpdate(2));
        Assert.Equal(0, EffectCount(presentation));
    }

    [Fact]
    public void Compatible_right_hand_creates_a_viewmodel_and_uses_one_shot_strike_playback()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        content.Add("weapon/dagger.png", Hash);
        AppearanceFake appearance = new(releases);
        using PrivateersHoldAppearance presentation = new(content, appearance, MediaInputs(classic: ClassicWeapon()));

        presentation.UpdateRightHandEquipment(RightHand("iron-longsword"));
        Assert.Single(appearance.PlaybackRequests);
        presentation.UpdateRightHandEquipment(RightHand("iron-dagger"));
        Assert.Equal(2, appearance.PlaybackRequests.Count);
        Assert.All(appearance.AtlasRequests.Last().Frames.Span.ToArray(), frame => Assert.False(frame.HasSize));
        Assert.Equal(new Vector2(8, 8), appearance.SpriteRequests.Last().Size);
        presentation.Publish(EmptyActors());
        Assert.Contains(appearance.Snapshots.Last(), fact => fact.Layer == RenderLayer.Viewmodel);

        PlayerAttackStartedFact miss = new(3, 4);
        presentation.React(miss);
        presentation.React(miss);
        Assert.Equal(3, appearance.PlaybackRequests.Count);
        appearance.AdvanceReceipts.Enqueue(default);
        appearance.AdvanceReceipts.Enqueue(new SpritePlaybackAdvanceLeaseReceipt(default, new SpritePlaybackReadout(0, 0, SpritePlaybackState.Completed, 0D, 0, 1, true), true));
        presentation.Advance(OuterUpdate(1));
        appearance.AdvanceReceipts.Enqueue(default);
        appearance.AdvanceReceipts.Enqueue(new SpritePlaybackAdvanceLeaseReceipt(default, new SpritePlaybackReadout(0, 0, SpritePlaybackState.Completed, 0D, 0, 2, true), true));
        presentation.Advance(OuterUpdate(2));
        Assert.Equal(4, appearance.PlaybackRequests.Count);

        presentation.UpdateRightHandEquipment(RightHand("iron-longsword"));
        presentation.Publish(EmptyActors());
        Assert.DoesNotContain(appearance.Snapshots.Last(), fact => fact.Layer == RenderLayer.Viewmodel);
    }

    [Fact]
    public void Classic_effect_atlas_frames_do_not_override_varied_authored_display_geometry()
    {
        Vector2[] expectedSizes = [new(.25F, .5F), new(.75F, .3F), new(.4F, .9F)];
        for (int ordinal = 0; ordinal < expectedSizes.Length; ordinal++)
        {
            List<string> releases = [];
            ContentFake content = MediaContent(releases);
            AppearanceFake appearance = new(releases);
            NormalizedClassicPresentation classic = ClassicEffects(expectedSizes);
            using PrivateersHoldAppearance presentation = new(content, appearance, MediaInputs(classic: classic), random: KeyedRandomFake.Create(ordinal).Service);
            using ActorsState actors = ActorsAt(new WorldPoint(2F, 0F, 3F));

            presentation.React(new AttackHitFact(DaggerfallActorIdentity.PlayerEntityId, 12, 1, ordinal, false, 2, 3), actors);

            Assert.All(appearance.AtlasRequests.Last().Frames.Span.ToArray(), frame => Assert.False(frame.HasSize));
            Assert.Equal(expectedSizes[ordinal], appearance.SpriteRequests.Last().Size);
            presentation.Publish(actors);
            Assert.All(appearance.Snapshots.Last(), fact => Assert.InRange(fact.ObjectId, 1UL, (1UL << 53) - 1));
            Assert.Equal(appearance.Snapshots.Last().Count(), appearance.Snapshots.Last().Select(fact => fact.ObjectId).Distinct().Count());
        }
    }

    [Fact]
    public void Drawn_weapon_swaps_and_empty_hands_replace_art_and_sheathing_suppresses_attacks()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        content.Add("weapon/dagger.png", Hash);
        content.Add("weapon/sword.png", Hash);
        content.Add("weapon/unarmed.png", Hash);
        NormalizedClassicPresentation original = ClassicWeapon();
        NormalizedClassicWeapon dagger = original.Weapons["weapon.dagger.steel"];
        NormalizedClassicPresentation classic = original with
        {
            Weapons = new Dictionary<string, NormalizedClassicWeapon>
            {
                [dagger.ResourceId] = dagger,
                ["weapon.longblade"] = dagger with { ResourceId = "weapon.longblade", TexturePath = "weapon/sword.png" },
                ["weapon.unarmed"] = dagger with { ResourceId = "weapon.unarmed", TexturePath = "weapon/unarmed.png" },
            },
            CompatibleItemVisuals = new Dictionary<string, string> { ["iron-dagger"] = dagger.ResourceId, ["iron-longsword"] = "weapon.longblade" },
            UnarmedVisual = "weapon.unarmed",
        };
        AppearanceFake appearance = new(releases);
        using PrivateersHoldAppearance presentation = new(content, appearance, MediaInputs(classic: classic));
        presentation.UpdateRightHandEquipment(RightHand("iron-longsword"));
        Assert.Equal("weapon.longblade", Viewmodel(presentation).Weapon.ResourceId);
        presentation.React(new PlayerAttackStartedFact(1, 1));
        Assert.False(presentation.CanStartPlayerAttack);
        presentation.UpdateRightHandEquipment(RightHand("iron-dagger"));
        Assert.Equal(dagger.ResourceId, Viewmodel(presentation).Weapon.ResourceId);
        Assert.True(presentation.CanStartPlayerAttack);
        presentation.UpdateRightHandEquipment(RightHand("gold"));
        Assert.Equal("weapon.unarmed", Viewmodel(presentation).Weapon.ResourceId);
        presentation.ToggleWeaponDrawn();
        presentation.UpdateRightHandEquipment(RightHand("gold"));
        Assert.False(presentation.CanStartPlayerAttack);
        using ActorsState actors = EmptyActors();
        presentation.Publish(actors);
        Assert.DoesNotContain(appearance.Snapshots.Last(), fact => fact.Layer == RenderLayer.Viewmodel);
    }

    [Fact]
    public void Viewmodel_uses_stable_viewport_placement()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        content.Add("weapon/dagger.png", Hash);
        AppearanceFake appearance = new(releases);
        using PrivateersHoldAppearance presentation = new(content, appearance, MediaInputs(classic: ClassicWeapon()));
        presentation.UpdateRightHandEquipment(RightHand("iron-dagger"));
        using ActorsState actors = EmptyActors();

        presentation.Publish(actors);
        AppearanceFact first = Assert.Single(appearance.Snapshots.Last(), fact => fact.Layer == RenderLayer.Viewmodel);
        Assert.InRange(first.ObjectId, 2UL, (1UL << 53) - 1);
        Assert.Equal(Vector3.Zero, first.Transform.Translation);
        Assert.Single(appearance.ViewportRequests);
        Assert.Equal(Quaternion.Identity, first.Transform.Rotation);
        Assert.All([first.Transform.Translation.X, first.Transform.Translation.Y, first.Transform.Translation.Z], coordinate => Assert.InRange(coordinate, -16F, 16F));
        presentation.Publish(actors);
        Assert.Equal(first.Transform, Assert.Single(appearance.Snapshots.Last(), fact => fact.Layer == RenderLayer.Viewmodel).Transform);
        Assert.Equal(first.Transform, Viewmodel(presentation).Transform);
    }

    [Fact]
    public void Normalized_media_rejects_invalid_sector_loop_and_per_sector_attack_index()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        byte[] payload = File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.privateers-hold.json"));

        Assert.Throws<DaggerfallContentException>(() => PrivateersHoldContent.Read(MutateDungeonMedia(root, media => FirstActorState(media, "idle")["frames"]!.AsArray()[0]!["orientation"] = 8), payload, definitions));
        Assert.Throws<DaggerfallContentException>(() => PrivateersHoldContent.Read(MutateDungeonMedia(root, media => FirstActorState(media, "idle")["playback"]!["loops"] = "true"), payload, definitions));
        Assert.Throws<DaggerfallContentException>(() => PrivateersHoldContent.Read(MutateDungeonMedia(root, media =>
        {
            JsonArray frames = FirstActorState(media, "primaryAttack")["frames"]!.AsArray();
            JsonNode frame = frames.Last(value => value!["orientation"]!.GetValue<int>() == 7)!;
            frames.Remove(frame);
        }), payload, definitions));
    }

    [Fact]
    public void Generated_media_declares_ranged_states_only_for_mobiles_the_donor_makes_ranged()
    {
        string root = RepositoryRoot();
        JsonObject media = JsonNode.Parse(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/imports/privateers-hold/media/dungeon/manifest.json")))!.AsObject();
        Dictionary<int, JsonObject> actors = media["actors"]!.AsArray()
            .Select(value => value!.AsObject())
            .ToDictionary(actor => actor["mobileId"]!.GetValue<int>(), actor => actor);

        foreach (int rangedMobile in new[] { 138, 141 })
        {
            JsonObject actor = actors[rangedMobile];
            JsonObject ranged = actor["states"]!.AsArray().Select(value => value!.AsObject())
                .Single(state => state["state"]!.GetValue<string>() == "rangedAttack1");
            Assert.Equal(10F, ranged["sourcePlayback"]!["framesPerSecond"]!.GetValue<float>());
            Assert.False(ranged["sourcePlayback"]!["loops"]!.GetValue<bool>());
            Assert.Equal(new[] { 3, 2, 0, 0, 0, -1, 1, 1, 2, 3 },
                actor["sourceAttackSequence"]!["rangedFrames"]!.AsArray().Select(value => value!.GetValue<int>()).ToArray());
            Assert.Equal(20, ranged["frames"]!.AsArray().First(frame => frame!["orientation"]!.GetValue<int>() == 0)!["sourceRecord"]!.GetValue<int>());
        }

        foreach (int meleeMobile in new[] { 0, 1, 3, 7, 15 })
        {
            JsonObject actor = actors[meleeMobile];
            Assert.DoesNotContain(actor["states"]!.AsArray(), value => value!["state"]!.GetValue<string>() == "rangedAttack1");
            // JSON null is published explicitly: the mobile declares no ranged sequence.
            Assert.True(actor["sourceAttackSequence"]!.AsObject().TryGetPropertyValue("rangedFrames", out JsonNode? rangedFrames) && rangedFrames is null);
        }
    }

    [Fact]
    public void Normalized_classic_audio_requires_a_contiguous_canonical_hit_cue_family()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        byte[] payload = File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.privateers-hold.json"));

        Assert.Throws<DaggerfallContentException>(() => PrivateersHoldContent.Read(MutateClassicMedia(root, media =>
        {
            JsonArray audio = media["audio"]!.AsArray();
            audio.Single(value => value!["clip"]!.GetValue<string>() == "hit2")!.AsObject()["clip"] = "hit3";
        }), payload, definitions));
        Assert.Throws<DaggerfallContentException>(() => PrivateersHoldContent.Read(MutateClassicMedia(root, media =>
        {
            JsonArray audio = media["audio"]!.AsArray();
            audio.Remove(audio.Single(value => value!["clip"]!.GetValue<string>() == "hit2"));
        }), payload, definitions));
    }

    [Fact]
    public void Normalized_classic_sidecar_rejects_noncanonical_source_records_ranges_frames_effects_and_descriptors()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        byte[] payload = File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.privateers-hold.json"));

        Assert.Throws<DaggerfallContentException>(() => PrivateersHoldContent.Read(MutateClassicMedia(root, media => media["weaponMedia"]!.AsArray()[0]!["actions"]!.AsArray()[0]!["frameStart"] = -1), payload, definitions));
        Assert.Throws<DaggerfallContentException>(() => PrivateersHoldContent.Read(MutateClassicMedia(root, media => media["weaponMedia"]!.AsArray()[0]!["actions"]!.AsArray()[1]!["frameStart"] = -1), payload, definitions));
        Assert.Throws<DaggerfallContentException>(() => PrivateersHoldContent.Read(MutateClassicMedia(root, media => media["weaponMedia"]!.AsArray()[0]!["actions"]!.AsArray()[0]!["frameCount"] = int.MaxValue), payload, definitions));
        Assert.Throws<DaggerfallContentException>(() => PrivateersHoldContent.Read(MutateClassicMedia(root, media => WeaponResource(media)["frames"]!.AsArray()[0]!["frameIndex"] = 4), payload, definitions));
        Assert.Throws<DaggerfallContentException>(() => PrivateersHoldContent.Read(MutateClassicMedia(root, media =>
        {
            JsonObject frame = WeaponResource(media)["frames"]!.AsArray()[0]!.AsObject();
            frame["x"] = 1;
            frame["width"] = int.MaxValue;
        }), payload, definitions));
        Assert.Throws<DaggerfallContentException>(() => PrivateersHoldContent.Read(MutateClassicMedia(root, media => media["effects"]!.AsArray()[0]!["sourceRecordOrdinal"] = 3), payload, definitions));
        Assert.Throws<DaggerfallContentException>(() => PrivateersHoldContent.Read(MutateClassicMedia(root, media => media["effects"]!.AsArray()[0]!["timing"]!["framesPerSecond"] = 12), payload, definitions));
        Assert.Throws<DaggerfallContentException>(() => PrivateersHoldContent.Read(MutateClassicMedia(root, media => EffectResource(media, "effect.blood.0")["frames"]!.AsArray()[0]!["frameIndex"] = 1), payload, definitions));
        Assert.Throws<DaggerfallContentException>(() => PrivateersHoldContent.Read(MutateClassicMedia(root, media => WeaponResource(media)["byteLength"] = 1), payload, definitions));
        Assert.Throws<DaggerfallContentException>(() => PrivateersHoldContent.Read(MutateClassicMedia(root, media => WeaponResource(media)["mimeType"] = ""), payload, definitions));
        Assert.Throws<DaggerfallContentException>(() => PrivateersHoldContent.Read(MutateClassicMedia(root, media => WeaponResource(media)["sourceWidth"] = -1), payload, definitions));
        Assert.Throws<DaggerfallContentException>(() => PrivateersHoldContent.Read(MutateClassicMedia(root, media => WeaponResource(media)["relativePath"] = "../weapon.png"), payload, definitions));
    }

    [Fact]
    public void Host_admits_one_realtime_update_and_releases_the_normalized_session_owners()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        List<string> releases = [];
        ContentFake content = new(releases);
        PrivateersHoldInputs inputs = ReadInputs(root);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        ProductInputConfiguration input = new(default, default, ReadOnlyMemory<ProductInputDescriptor>.Empty, ReadOnlyMemory<ProductInputMapping>.Empty);

        using (WorldRpgProduct product = new(new ProductCreateContext(engine.Context, FullContent(root), input)))
        {
            product.Start();
            // The product a launcher starts shows its entry screen, and a client that has read it asks to
            // begin; the admitted update under test is the first one that reaches the world.
            Assert.Equal(ProductMode.Title, product.Mode);
            product.Begin();
            ProductUpdateFacts facts = new(ProductUpdateMode.Realtime, ProductLifecycleState.Running, 1, 1, 1, 1, 60, 1, 0, 1d / 60d);
            Assert.Equal(ProductUpdateResult.None, product.Update(new ProductUpdate(facts, ReadOnlySpan<ProductInputEvent>.Empty)));
            Assert.Equal(1, spatial.StepCalls);
            product.Shutdown();
        }

        Assert.Equal(1, engine.UiOpenCalls);
        Assert.True(releases.IndexOf("session") < releases.LastIndexOf("content"));
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

        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        using DaggerfallSession session = new(engine.Context, definitions, inputs, DaggerfallTuning.Defaults);
        double healthBefore = session.State.Actors.Get(2000).Stats.GetTrack(Rusty.Engine.Mechanics.TrackId.Parse("health")).Current;
        session.ResolveExplicitMelee(new ExplicitMeleeRequest(1, 2000, 1, 1, .125));
        Assert.True(session.State.Actors.Get(2000).Stats.GetTrack(Rusty.Engine.Mechanics.TrackId.Parse("health")).Current < healthBefore);
    }

    [Fact]
    public void Session_fixed_steps_restore_exhausted_player_stamina()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        using DaggerfallSession session = new(engine.Context, definitions, inputs, DaggerfallTuning.Defaults);
        var stamina = Rusty.Engine.Mechanics.TrackId.Parse("stamina");
        session.State.Actors.Player.Stats.GetTrack(stamina).SetCurrent(0);

        for (int step = 0; step < 8; step++) session.Update(new ProductUpdateState(.125f));

        Assert.Equal(5d, session.State.Actors.Player.Stats.GetTrack(stamina).Current);
    }

    [Fact]
    public void Current_save_roundtrip_rebuilds_stats_aliases_sources_tracks_and_modifier_handles()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake sourceContent = new(releases);
        PopulateContent(sourceContent, inputs);
        SpatialFake sourceSpatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake source = EngineContextFake.Create(sourceContent, sourceSpatial.Service, new AppearanceFake(releases));
        RulesetSavePayload payload;
        StatId maximumId = StatId.Parse("health-maximum");
        TrackId healthId = TrackId.Parse("health");
        using (DaggerfallSession original = new(source.Context, definitions, inputs, DaggerfallTuning.Defaults))
        {
            PlayerActorState player = original.State.Actors.Player;
            Stat maximum = player.Stats.GetStat(maximumId);
            Track health = player.Stats.GetTrack(healthId);
            player.Stats.AddStat(StatId.Parse("health-cap-alias"), maximum);
            player.Stats.AddTrack(TrackId.Parse("health-current-alias"), health);
            maximum.SetSources(maximumId,
            [
                new StatSource(
                    new IntrinsicSourceIdentity(player.Actor.Entity, SourceInstanceId.Parse("daggerfall.test.save-source")),
                    SourceDefinitionId.Parse("daggerfall.test.save-source"),
                    priority: 0,
                    [new StatContributionDefinition(
                        maximumId,
                        StackingGroupId.Parse("daggerfall.test.save-source"),
                        MechanicsStackingPolicy.Sum,
                        new StatContribution.Add(7))]),
            ]);
            _ = maximum.AddModifier(4);
            player.Progression.AdvanceTo(100, 2);
            payload = original.CaptureSave();
        }

        DaggerfallSavePayload captured = DaggerfallSavePayload.Read(payload);
        Assert.Contains(captured.Player.Stats.Snapshot.StatAliases,
            alias => alias.Id == "health-maximum" && alias.TargetId == "health-cap-alias");
        Assert.Contains(captured.Player.Stats.Snapshot.TrackAliases, alias => alias.Id == "health-current-alias");
        Assert.Single(captured.Player.Stats.Sources);

        ContentFake resumedContent = new(releases);
        PopulateContent(resumedContent, inputs);
        SpatialFake resumedSpatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake resumedEngine = EngineContextFake.Create(resumedContent, resumedSpatial.Service, new AppearanceFake(releases));
        ResolvedCompositionIdentity identity = GameCompositionResolver.Resolve(FullContent(root), new GameBundleId("daggerfall.privateers-hold")).RequireComposition().Identity;
        using DaggerfallSession resumed = DaggerfallSession.Restore(
            resumedEngine.Context, identity, definitions, inputs, DaggerfallTuning.Defaults, payload, RandomMinimum.Create());

        PlayerActorState restored = resumed.State.Actors.Player;
        Stat restoredMaximum = restored.Stats.GetStat(maximumId);
        Assert.Same(restoredMaximum, restored.Stats.GetStat(StatId.Parse("health-cap-alias")));
        Assert.Same(restored.Stats.GetTrack(healthId), restored.Stats.GetTrack(TrackId.Parse("health-current-alias")));
        Assert.Same(restoredMaximum, restored.Stats.GetTrack(healthId).Maximum);
        Assert.Equal(restored.Actor.Entity, ((IntrinsicSourceIdentity)Assert.Single(restoredMaximum.Sources).Identity).Entity);
        Assert.Equal(100, resumed.State.Progression.Experience);
        Assert.Equal(2, resumed.State.Progression.Level);
        DaggerfallRestoredStats handles = restored.Actor.Get<DaggerfallRestoredStats>();
        Assert.True(restoredMaximum.RemoveModifier(Assert.Single(handles.ModifierHandles["health-cap-alias"])));
        Assert.Equal(7d, restoredMaximum.Value - restoredMaximum.BaseValue);
        Assert.Equal(0, resumedSpatial.StepCalls);
    }

    [Fact]
    public void Session_save_restore_retains_social_reaction_and_guild_eligibility_without_a_loaded_npc()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs inputs = ReadInputs(root);
        DaggerfallFactionDefinition faction = definitions.Factions.Factions[15];
        List<string> releases = [];
        ContentFake sourceContent = new(releases);
        PopulateContent(sourceContent, inputs);
        SpatialFake sourceSpatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake source = EngineContextFake.Create(sourceContent, sourceSpatial.Service, new AppearanceFake(releases));
        RulesetSavePayload payload;
        DaggerfallFactionReaction expected;
        DaggerfallGuildEligibility expectedEligibility;
        DaggerfallNpc unloaded = new(9000, DaggerfallNpcKind.Static, "unloaded-social-test",
            new DaggerfallNpcSite(0, "Daggerfall", "social-test"),
            new DaggerfallNpcAppearance("Breton", "Male", 0, 0, 0, faction.Id), "talker", ["talk", "quest"],
            DaggerfallNpcPresence.Hidden, null, null, null);
        using (DaggerfallSession original = new(source.Context, definitions, inputs, DaggerfallTuning.Defaults))
        {
            _ = original.State.Social.ChangeFactionReputation(faction.Id, 10, DaggerfallFactionReputationChange.Propagate);
            _ = original.State.Social.ChangePersonalReputation(faction.SocialGroup, 7);
            _ = original.State.Social.ChangeRegionalReputation(0, -4);
            _ = original.State.Social.JoinGuild(faction.Id, currentDay: 8);
            _ = original.State.Social.PromoteGuild(faction.Id, currentDay: 12);
            _ = original.State.Social.PromoteGuild(faction.Id, currentDay: 13);
            _ = original.State.Social.ChangeGuildRecognition(faction.Id, 3);
            expected = original.State.Social.ReactionForNpc(unloaded);
            expectedEligibility = original.State.Social.GuildEligibility(faction.Id);
            payload = original.CaptureSave();
        }

        DaggerfallSocialSave saved = DaggerfallSavePayload.Read(payload).Social;
        Assert.Contains(saved.Factions, entry => entry.FactionId == faction.Id);
        Assert.Contains(saved.Regions, entry => entry.Region == 0 && entry.Value == -4);
        Assert.Contains(saved.Personal, entry => entry.SocialGroup == faction.SocialGroup && entry.Value == 7);
        Assert.Contains(saved.Memberships, entry => entry.FactionId == faction.Id && entry.Rank == 2 && entry.NotedByGuild == 3);

        ContentFake restoredContent = new(releases);
        PopulateContent(restoredContent, inputs);
        SpatialFake restoredSpatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake restoredEngine = EngineContextFake.Create(restoredContent, restoredSpatial.Service, new AppearanceFake(releases));
        ResolvedCompositionIdentity identity = GameCompositionResolver.Resolve(FullContent(root), new GameBundleId("daggerfall.privateers-hold")).RequireComposition().Identity;
        using DaggerfallSession restored = DaggerfallSession.Restore(
            restoredEngine.Context, identity, definitions, inputs, DaggerfallTuning.Defaults, payload, RandomMinimum.Create());

        Assert.Equal(expected, restored.State.Social.ReactionForNpc(unloaded));
        Assert.Equal(expectedEligibility, restored.State.Social.GuildEligibility(faction.Id));
        Assert.Equal(-4, restored.State.Social.RegionalReputation(0));
    }

    [Fact]
    public void Elapsed_calendar_time_normalizes_social_faction_and_regional_reputation_once_per_112_days()
    {
        using DaggerfallSession session = FreshSession();
        const int factionId = 15;
        int factionBefore = session.State.Social.FactionReputation(factionId);
        _ = session.State.Social.ChangeFactionReputation(factionId, 10);
        _ = session.State.Social.ChangeRegionalReputation(0, -4);

        DaggerfallCalendarAdvance elapsed = session.AdvanceElapsedTime(DaggerfallSocialState.NormalizeIntervalMinutes * 60L);

        Assert.Equal(DaggerfallSocialState.NormalizeIntervalMinutes * 60L, elapsed.AppliedSeconds);
        Assert.Equal(factionBefore + 9, session.State.Social.FactionReputation(factionId));
        Assert.Equal(-3, session.State.Social.RegionalReputation(0));
    }

    [Fact]
    public void Normal_magic_minutes_and_elapsed_time_match_and_preserve_active_effect_local_state_through_save()
    {
        using DaggerfallSession normal = FreshSession(TimedEffectCatalog());
        using DaggerfallSession elapsed = FreshSession(TimedEffectCatalog());
        _ = normal.State.Effects.Start(TimedEffectRequest());
        _ = elapsed.State.Effects.Start(TimedEffectRequest());

        // Two five-second admitted updates buy two game minutes at the default 12:1 tuning.  Each
        // minute is a normal magic round, and each effect gets its initial round when admitted.
        normal.Update(new ProductUpdate(MinuteUpdate(1), []));
        normal.Update(new ProductUpdate(MinuteUpdate(2), []));
        // A consequence interrupts elapsed time at one minute.  Resuming exactly its remaining
        // interval must add the second minute once, rather than replaying the first one.
        DaggerfallCalendarAdvance elapsedAdvance = elapsed.AdvanceElapsedTime(120, [(99, 60)]);

        Assert.Equal(60, elapsedAdvance.AppliedSeconds);
        Assert.Equal(60, elapsedAdvance.RemainingSeconds);
        Assert.Equal(99, elapsedAdvance.Consequence);
        _ = elapsed.AdvanceElapsedTime(elapsedAdvance.RemainingSeconds);
        DaggerfallActiveEffect normalEffect = Assert.Single(normal.State.Effects.Active);
        DaggerfallActiveEffect elapsedEffect = Assert.Single(elapsed.State.Effects.Active);
        Assert.Equal(normalEffect.Lifecycle.RemainingRounds, elapsedEffect.Lifecycle.RemainingRounds);
        Assert.Equal(normalEffect.State.GetProperty("ticks").GetInt32(), elapsedEffect.State.GetProperty("ticks").GetInt32());
        Assert.Equal((uint)3, elapsedEffect.Lifecycle.RemainingRounds);
        Assert.Equal(3, normalEffect.State.GetProperty("ticks").GetInt32());

        RulesetSavePayload payload = elapsed.CaptureSave();
        DaggerfallSavePayload captured = DaggerfallSavePayload.Read(payload);
        DaggerfallActiveEffectSave saved = Assert.Single(captured.ActiveEffects);
        Assert.Equal((uint)3, saved.RemainingRounds);
        Assert.Equal(3, saved.State.GetProperty("ticks").GetInt32());

        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        ResolvedCompositionIdentity identity = GameCompositionResolver.Resolve(FullContent(root), new GameBundleId("daggerfall.privateers-hold")).RequireComposition().Identity;
        using DaggerfallSession restored = DaggerfallSession.Restore(
            engine.Context, identity, definitions, inputs, DaggerfallTuning.Defaults, payload, RandomMinimum.Create(), TimedEffectCatalog());

        DaggerfallActiveEffect resumed = Assert.Single(restored.State.Effects.Active);
        Assert.Equal((uint)3, resumed.Lifecycle.RemainingRounds);
        Assert.Equal(3, resumed.State.GetProperty("ticks").GetInt32());
        _ = restored.AdvanceElapsedTime(60);
        Assert.Equal((uint)2, resumed.Lifecycle.RemainingRounds);
        Assert.Equal(4, resumed.State.GetProperty("ticks").GetInt32());

        static ProductUpdateFacts MinuteUpdate(ulong step) =>
            new(ProductUpdateMode.Realtime, ProductLifecycleState.Running, 1, 1, step, step, 60, 1, 0, 5d);

        static DaggerfallEffectCatalog TimedEffectCatalog() => new(
        [new DaggerfallEffectDefinition("timed", "timed", DaggerfallEffectStacking.Stack, 1, 1,
            MagicRound: effect => effect.State = TimedState(effect.State.GetProperty("ticks").GetInt32() + 1))]);

        static DaggerfallEffectRequest TimedEffectRequest() => new(
            "timed-instance", "timed", "rest-travel-prison", null, DaggerfallActorIdentity.PlayerEntityId,
            "timer", "magic", null, 1, 6, TimedState(0));

        static JsonElement TimedState(int ticks)
        {
            using JsonDocument state = JsonDocument.Parse($"{{\"ticks\":{ticks},\"localTimer\":\"minute\"}}");
            return state.RootElement.Clone();
        }
    }

    [Fact]
    public void Session_save_restore_rebinds_source_backed_effect_without_mutating_live_stats_or_replaying_start()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake sourceContent = new(releases);
        PopulateContent(sourceContent, inputs);
        SpatialFake sourceSpatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake sourceEngine = EngineContextFake.Create(sourceContent, sourceSpatial.Service, new AppearanceFake(releases));
        int sourceRounds = 0;
        int applyCalls = 0;
        int resumeCalls = 0;
        int removals = 0;
        RulesetSavePayload payload;
        double baseMaximum;
        double expectedMaximum;
        using (DaggerfallSession source = new(sourceEngine.Context, definitions, inputs, DaggerfallTuning.Defaults,
            EffectCatalog(() => sourceRounds++, () => applyCalls++, () => resumeCalls++, () => removals++)))
        {
            PlayerActorState player = source.State.Actors.Player;
            Stat maximum = player.Stats.GetStat(StatId.Parse("health-maximum"));
            Track health = player.Stats.GetTrack(TrackId.Parse("health"));
            baseMaximum = maximum.Value;
            source.State.Effects.Start(SessionEffectRequest());
            expectedMaximum = baseMaximum + 10;
            health.SetCurrent(expectedMaximum);
            Assert.Equal(expectedMaximum, maximum.Value);
            Assert.Equal(expectedMaximum, health.Current);
            payload = source.CaptureSave();
            Assert.Equal(expectedMaximum, maximum.Value);
            Assert.Equal(expectedMaximum, health.Current);
            Assert.Equal(1, applyCalls);
            Assert.Equal(0, resumeCalls);
        }

        Assert.Equal(1, sourceRounds);
        Assert.Equal(1, removals);
        RulesetSavePayload missingEffect = DaggerfallSavePayload.Encode(DaggerfallSavePayload.Read(payload) with { ActiveEffects = [] });
        ArgumentException missingOwner = Assert.Throws<ArgumentException>(() =>
            DaggerfallSavePayload.Read(missingEffect).ResolveRestore(definitions, inputs));
        Assert.Contains("has no matching active effect cleanup owner", missingOwner.Message);
        ContentFake restoredContent = new(releases);
        PopulateContent(restoredContent, inputs);
        SpatialFake restoredSpatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake restoredEngine = EngineContextFake.Create(restoredContent, restoredSpatial.Service, new AppearanceFake(releases));
        int restoredRounds = 0;
        ResolvedCompositionIdentity identity = GameCompositionResolver.Resolve(FullContent(root), new GameBundleId("daggerfall.privateers-hold")).RequireComposition().Identity;
        using (DaggerfallSession restored = DaggerfallSession.Restore(restoredEngine.Context, identity, definitions, inputs,
            DaggerfallTuning.Defaults, payload, RandomMinimum.Create(), EffectCatalog(() => restoredRounds++, () => applyCalls++, () => resumeCalls++, () => removals++)))
        {
            PlayerActorState player = restored.State.Actors.Player;
            Stat maximum = player.Stats.GetStat(StatId.Parse("health-maximum"));
            Track health = player.Stats.GetTrack(TrackId.Parse("health"));
            Assert.Equal(0, restoredRounds);
            Assert.Equal(1, applyCalls);
            Assert.Equal(1, resumeCalls);
            Assert.Equal(expectedMaximum, maximum.Value);
            Assert.Equal(expectedMaximum, health.Current);
            EffectSourceIdentity source = Assert.IsType<EffectSourceIdentity>(Assert.Single(maximum.Sources).Identity);
            Assert.Equal(player.Actor.Entity, source.Entity);
            Assert.Equal("session-effect-instance", source.Effect.Value);
            DaggerfallActiveEffect restoredEffect = Assert.Single(restored.State.Effects.Active);
            Assert.Equal(("session-source", "session-settings", (uint)3),
                (restoredEffect.Context.Source.Key, restoredEffect.Context.Settings, restoredEffect.Lifecycle.RemainingRounds));
            Assert.True(restored.State.Effects.Cancel(EffectInstanceId.Parse("session-effect-instance")));
            Assert.Equal(baseMaximum, maximum.Value);
            Assert.Equal(baseMaximum, health.Current);
        }

        Assert.Equal(2, removals);

        static DaggerfallEffectCatalog EffectCatalog(Action magicRound, Action applied, Action resumed, Action removed) => new(
        [new DaggerfallEffectDefinition("session-effect", "session-effect", DaggerfallEffectStacking.Stack, 1, 1,
            effect =>
            {
                applied();
                Stat maximum = effect.Target.Get<StatsComponent>().GetStat(StatId.Parse("health-maximum"));
                EffectSourceIdentity identity = EffectIdentity(effect);
                maximum.SetSources(StatId.Parse("health-maximum"), maximum.Sources.Append(new StatSource(
                    identity,
                    SourceDefinitionId.Parse("daggerfall.session-effect.health"),
                    priority: 0,
                    [new StatContributionDefinition(
                        StatId.Parse("health-maximum"),
                        StackingGroupId.Parse("daggerfall.session-effect.health"),
                        MechanicsStackingPolicy.Sum,
                        new StatContribution.Add(10))])));
                return [Remove(maximum, identity, removed)];
            },
            _ => magicRound(),
            effect =>
            {
                resumed();
                Stat maximum = effect.Target.Get<StatsComponent>().GetStat(StatId.Parse("health-maximum"));
                EffectSourceIdentity identity = EffectIdentity(effect);
                Assert.Contains(maximum.Sources, source => source.Identity == identity);
                return [Remove(maximum, identity, removed)];
            })]);

        static EffectSourceIdentity EffectIdentity(DaggerfallActiveEffect effect) => new(
            effect.Target.Entity,
            effect.Context.Instance,
            1,
            SourceDefinitionId.Parse("daggerfall.session-effect.health"));

        static IActiveEffectContribution Remove(Stat maximum, EffectSourceIdentity identity, Action removed) =>
            new DelegateActiveEffectContribution(() =>
            {
                Assert.True(maximum.RemoveSource(identity));
                removed();
            });

        static DaggerfallEffectRequest SessionEffectRequest()
        {
            using JsonDocument state = JsonDocument.Parse("{\"session\":true}");
            return new DaggerfallEffectRequest("session-effect-instance", "session-effect", "session-source", null,
                DaggerfallActorIdentity.PlayerEntityId, "session-settings", "magic", null, 1, 4, state.RootElement.Clone());
        }
    }

    /// <summary>
    /// The donor's holiday announcement through the session: restoring onto a kept holiday at a
    /// settlement announces once on the first playing update, and a dungeon session never announces.
    /// The donor shows text 8349 + holidayId on entering a city; the session observes the same entry
    /// on its first tick, the way the donor also announces after loading.
    /// </summary>
    [Fact]
    public void Restoring_onto_a_holiday_at_a_settlement_announces_once()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs inputs = ReadInputs(root);
        // Day 103 of the year is the eighteenth holiday, kept in region 17 alone; Charing is its city.
        DaggerfallSavePayload saved = CapturedSave(root) with
        {
            Calendar = new DaggerfallCalendarSave(405, 3, 12, 12, 0, 0, 0d),
            Site = new DaggerfallSiteSave(new DaggerfallSiteIdSave(17, 4), null, []),
        };
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        ResolvedCompositionIdentity identity = GameCompositionResolver.Resolve(FullContent(root), new GameBundleId("daggerfall.privateers-hold")).RequireComposition().Identity;
        using DaggerfallSession session = DaggerfallSession.Restore(
            engine.Context, identity, definitions, inputs, DaggerfallTuning.Defaults, DaggerfallSavePayload.Encode(saved), RandomMinimum.Create());

        Assert.Null(session.HolidayAnnouncement);
        session.Update(new ProductUpdate(OuterUpdate(1), []));

        Assert.NotNull(session.HolidayAnnouncement);
        Assert.Equal(18, session.HolidayAnnouncement.HolidayId);
        Assert.Equal(new DaggerfallTextKey(DaggerfallTextKind.Resource, "8367"), session.HolidayAnnouncement.TextKey);
        string announced = session.Presentation.LastOutcome;
        Assert.Contains("Day of the Dead", announced, StringComparison.Ordinal);
        session.Update(new ProductUpdate(OuterUpdate(2), []));
        Assert.Equal(announced, session.Presentation.LastOutcome);
        Assert.Equal(18, session.HolidayAnnouncement.HolidayId);
    }

    /// <summary>
    /// The clock crossing midnight into a kept holiday announces at a settlement, once, through the
    /// existing outcome line. The donor's entry-only check cannot observe this; the product's
    /// continuous clock can, so the announcement follows the date rather than only the border.
    /// </summary>
    [Fact]
    public void Crossing_midnight_into_a_holiday_announces()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs inputs = ReadInputs(root);
        // An hour before midnight on the eve of region 17's eighteenth holiday, at its city.
        DaggerfallSavePayload saved = CapturedSave(root) with
        {
            Calendar = new DaggerfallCalendarSave(405, 3, 11, 23, 0, 0, 0d),
            Site = new DaggerfallSiteSave(new DaggerfallSiteIdSave(17, 4), null, []),
        };
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        ResolvedCompositionIdentity identity = GameCompositionResolver.Resolve(FullContent(root), new GameBundleId("daggerfall.privateers-hold")).RequireComposition().Identity;
        using DaggerfallSession session = DaggerfallSession.Restore(
            engine.Context, identity, definitions, inputs, DaggerfallTuning.Defaults, DaggerfallSavePayload.Encode(saved), RandomMinimum.Create());
        session.Update(new ProductUpdate(OuterUpdate(1), []));
        Assert.Null(session.HolidayAnnouncement);

        // Two admitted hours cross midnight into the holiday.
        session.Update(new ProductUpdate(FactsWithDelta(7200d), []));
        Assert.Equal(18, session.HolidayAnnouncement?.HolidayId);
        Assert.Contains("Day of the Dead", session.Presentation.LastOutcome, StringComparison.Ordinal);
    }

    [Fact]
    public void A_dungeon_session_never_announces()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs inputs = ReadInputs(root);
        DaggerfallSavePayload saved = CapturedSave(root) with
        {
            Calendar = new DaggerfallCalendarSave(405, 3, 12, 12, 0, 0, 0d),
            Site = new DaggerfallSiteSave(new DaggerfallSiteIdSave(17, 179), null, []),
        };
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        ResolvedCompositionIdentity identity = GameCompositionResolver.Resolve(FullContent(root), new GameBundleId("daggerfall.privateers-hold")).RequireComposition().Identity;
        using DaggerfallSession restored = DaggerfallSession.Restore(
            engine.Context, identity, definitions, inputs, DaggerfallTuning.Defaults, DaggerfallSavePayload.Encode(saved), RandomMinimum.Create());
        restored.Update(new ProductUpdate(OuterUpdate(1), []));
        Assert.Null(restored.HolidayAnnouncement);

        // The fresh bundle session starts in a dungeon on an ordinary day: silent too.
        using DaggerfallSession fresh = FreshSession();
        fresh.Update(new ProductUpdate(OuterUpdate(1), []));
        Assert.Null(fresh.HolidayAnnouncement);
    }

    private static ProductUpdateFacts FactsWithDelta(double deltaSeconds) =>
        new(ProductUpdateMode.Realtime, ProductLifecycleState.Running, 1, 1, 1, 1, 60, 1, 0, deltaSeconds);

    [Fact]
    public void Current_save_rejects_malformed_bytes_and_missing_content_definitions_before_a_session_is_published()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs inputs = ReadInputs(root);
        DaggerfallSavePayload saved = CapturedSave(root);
        Assert.Throws<ArgumentException>(() => DaggerfallSavePayload.Read(new RulesetSavePayload(DaggerfallRuleset.Identity, "{"u8)));

        DaggerfallSavePayload missingDefinition = saved with
        {
            Inventory = saved.Inventory with { Stacks = [new DaggerfallStackSave("missing-stack", "missing-item", 1, saved.Inventory.Stacks[0].Metadata)] },
        };
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        ResolvedCompositionIdentity identity = GameCompositionResolver.Resolve(FullContent(root), new GameBundleId("daggerfall.privateers-hold")).RequireComposition().Identity;

        Assert.Throws<ArgumentException>(() => DaggerfallSession.Restore(
            engine.Context, identity, definitions, inputs, DaggerfallTuning.Defaults,
            DaggerfallSavePayload.Encode(missingDefinition), RandomMinimum.Create()));
        DaggerfallSavePayload missingActorInventory = saved with { ActorInventories = [] };
        Assert.Throws<ArgumentException>(() => DaggerfallSession.Restore(
            engine.Context, identity, definitions, inputs, DaggerfallTuning.Defaults,
            DaggerfallSavePayload.Encode(missingActorInventory), RandomMinimum.Create()));
        Assert.Equal(0, spatial.StepCalls);
    }

    [Fact]
    public void Current_save_keeps_relative_cooldown_but_starts_a_fresh_spatial_continuation()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake sourceContent = new(releases);
        PopulateContent(sourceContent, inputs);
        SpatialFake sourceSpatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake source = EngineContextFake.Create(sourceContent, sourceSpatial.Service, new AppearanceFake(releases));
        RulesetSavePayload payload;
        using (DaggerfallSession original = new(source.Context, definitions, inputs, DaggerfallTuning.Defaults))
        {
            original.ResolveExplicitMelee(new ExplicitMeleeRequest(1, 2000, 77, 400, .125));
            payload = original.CaptureSave();
        }

        string json = Encoding.UTF8.GetString(payload.Bytes.Span);
        Assert.DoesNotContain("Continuation", json, StringComparison.Ordinal);
        ContentFake resumedContent = new(releases);
        PopulateContent(resumedContent, inputs);
        SpatialFake resumedSpatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake resumedEngine = EngineContextFake.Create(resumedContent, resumedSpatial.Service, new AppearanceFake(releases));
        ResolvedCompositionIdentity identity = GameCompositionResolver.Resolve(FullContent(root), new GameBundleId("daggerfall.privateers-hold")).RequireComposition().Identity;
        using DaggerfallSession resumed = DaggerfallSession.Restore(
            resumedEngine.Context, identity, definitions, inputs, DaggerfallTuning.Defaults, payload, RandomMinimum.Create());
        double before = resumed.State.Actors.Get(2000).Stats.GetTrack(TrackId.Parse("health")).Current;

        resumed.ResolveExplicitMelee(new ExplicitMeleeRequest(1, 2000, 2, 1, .125));

        Assert.Equal(before, resumed.State.Actors.Get(2000).Stats.GetTrack(TrackId.Parse("health")).Current);
        Assert.Equal(0, resumedSpatial.StepCalls);
    }

    [Fact]
    public void Host_save_close_reopen_resumes_a_fresh_daggerfall_session_with_current_state()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs inputs = ReadInputs(root);
        ProductInputConfiguration input = new(default, default, ReadOnlyMemory<ProductInputDescriptor>.Empty, ReadOnlyMemory<ProductInputMapping>.Empty);
        InMemoryPersistenceService persistence = new();
        List<string> releases = [];
        ContentFake sourceContent = new(releases);
        PopulateContent(sourceContent, inputs);
        SpatialFake sourceSpatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake sourceEngine = EngineContextFake.Create(sourceContent, sourceSpatial.Service, new AppearanceFake(releases), persistence: persistence);
        CapturingDaggerfallRuleset sourceRuleset = new();
        double savedHealth;
        double savedMaximum;
        WorldPoint savedPosition;
        ulong savedStackQuantity;
        ulong savedNpcUniqueId;
        HashSet<ulong> savedUniqueIds;
        DaggerfallItemInstanceMetadata savedStackMetadata = null!;
        DaggerfallItemInstanceMetadata savedUniqueMetadata = null!;
        DaggerfallSession sourceSession;
        using (WorldRpgSaveStore store = new(sourceEngine.Context, "daggerfall-host-save"))
        using (WorldRpgProduct sourceProduct = new(new ProductCreateContext(sourceEngine.Context, FullContent(root), input), sourceRuleset, new GameBundleId("daggerfall.privateers-hold")))
        {
            sourceProduct.Begin();
            sourceSession = sourceRuleset.RequireSession();
            PlayerActorState player = sourceSession.State.Actors.Player;
            Stat maximum = player.Stats.GetStat(StatId.Parse("health-maximum"));
            Track health = player.Stats.GetTrack(TrackId.Parse("health"));
            _ = maximum.AddModifier(3);
            health.SetCurrent(health.Current - 1);
            savedPosition = new WorldPoint(12, 3, -7);
            sourceSession.State.PlayerControl.Restore(savedPosition, default);
            var stack = sourceSession.State.Inventory.Read().Stacks.First();
            sourceSession.State.Inventory.Grant(new InventoryGrant(new InventoryItemId(stack.Definition.Value), stack.Id, 2));
            savedStackMetadata = new DaggerfallItemInstanceMetadata(stack.Definition.Value, "steel", 4, 3, 8,
                false, true, "quest-99", "relic", "soul-trap", DaggerfallItemOwner.Player).Validate();
            sourceSession.State.ItemInstances.ReplaceStack(DaggerfallItemOwner.Player, stack.Id, savedStackMetadata);
            DurableIdentityReference npcUniqueIdentity = sourceSession.UniqueItemAllocator.AllocateReference();
            MechanicsEquipmentCoordinator npcEquipment = sourceSession.State.EquipmentFor(2000);
            var npcUnique = npcEquipment.Materialize(npcUniqueIdentity, new InventoryItemId("iron-dagger"));
            sourceSession.State.ItemInstances.RegisterDefaultUnique(npcUniqueIdentity.Value,
                definitions.Items[new DaggerfallItemId("iron-dagger")], DaggerfallItemOwner.Actor(2000));
            savedUniqueMetadata = new DaggerfallItemInstanceMetadata("iron-dagger", "ebony", 2, 7, 12,
                false, true, "quest-99", "blade", "fire", DaggerfallItemOwner.Actor(2000)).Validate();
            sourceSession.State.ItemInstances.ReplaceUnique(npcUniqueIdentity.Value, savedUniqueMetadata);
            npcEquipment.Equip(npcUnique, [new WorldRpg.Kit.Inventory.EquipmentSlotId("right-hand")]);
            savedHealth = health.Current;
            savedMaximum = maximum.Value;
            savedStackQuantity = sourceSession.State.Inventory.Read().Stacks.Single(value => value.Definition == stack.Definition).Quantity;
            savedNpcUniqueId = npcUniqueIdentity.Value;

            PersistenceSaveReceipt receipt = sourceProduct.Save(store, "slot", PersistenceRevisionGuard.Absent);
            Assert.Equal(PersistenceSaveOutcome.Saved, receipt.Outcome);
            savedUniqueIds = [.. CapturedUniqueItemIds(DaggerfallSavePayload.Read(store.Load("slot").State!.Payload))];
        }

        ContentFake resumedContent = new(releases);
        PopulateContent(resumedContent, inputs);
        SpatialFake resumedSpatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake resumedEngine = EngineContextFake.Create(resumedContent, resumedSpatial.Service, new AppearanceFake(releases), persistence: persistence);
        CapturingDaggerfallRuleset resumedRuleset = new();
        using WorldRpgSaveStore reopened = new(resumedEngine.Context, "daggerfall-host-save");
        WorldRpgResumeResult resumed = WorldRpgProduct.TryResume(
            new ProductCreateContext(resumedEngine.Context, FullContent(root), input),
            reopened,
            "slot",
            resumedRuleset,
            new GameBundleId("daggerfall.privateers-hold"));

        Assert.True(resumed.IsComplete, string.Join("; ", resumed.Diagnostics.Select(value => value.Message)));
        using WorldRpgProduct resumedProduct = Assert.IsType<WorldRpgProduct>(resumed.Product);
        DaggerfallSession restoredSession = resumedRuleset.RequireSession();
        PlayerActorState restoredPlayer = restoredSession.State.Actors.Player;
        Stat restoredMaximum = restoredPlayer.Stats.GetStat(StatId.Parse("health-maximum"));
        Track restoredHealth = restoredPlayer.Stats.GetTrack(TrackId.Parse("health"));
        Assert.NotSame(sourceSession, restoredSession);
        Assert.NotSame(sourceSession.State.Actors.Player.Actor, restoredPlayer.Actor);
        Assert.Equal(savedPosition, restoredSession.State.PlayerControl.Position);
        Assert.Equal(savedHealth, restoredHealth.Current);
        Assert.Equal(savedMaximum, restoredMaximum.Value);
        Assert.Same(restoredMaximum, restoredHealth.Maximum);
        Assert.Equal(savedStackQuantity, restoredSession.State.Inventory.Read().Stacks.Single(value => value.Definition == sourceSession.State.Inventory.Read().Stacks.First().Definition).Quantity);
        Assert.Equal(savedStackMetadata, restoredSession.State.ItemInstances.RequireStack(DaggerfallItemOwner.Player,
            restoredSession.State.Inventory.Read().Stacks.Single(value => value.Definition == sourceSession.State.Inventory.Read().Stacks.First().Definition).Id));
        MechanicsInventoryCoordinator restoredNpcInventory = Assert.IsType<MechanicsInventoryCoordinator>(restoredSession.State.InventoryFor(2000));
        var restoredNpcUnique = Assert.Single(restoredNpcInventory.Read().UniqueItems, item => item.Definition.Value == "iron-dagger");
        Assert.Equal(savedNpcUniqueId, restoredSession.State.Actors.Entities.IdentityOf(restoredNpcUnique.Entity).Value);
        Assert.Equal(savedUniqueMetadata, restoredSession.State.ItemInstances.RequireUnique(savedNpcUniqueId));
        Assert.Contains(restoredSession.State.EquipmentFor(2000).Read().Assignments,
            assignment => assignment.Slot.Value == "right-hand" && assignment.Item.EntityId == restoredNpcUnique.Entity.Value);

        _ = restoredMaximum.AddModifier(1);
        Assert.Equal(savedMaximum + 1, restoredHealth.MaximumValue);
        Assert.Same(restoredMaximum, restoredHealth.Maximum);
        Assert.DoesNotContain(restoredSession.UniqueItemAllocator.AllocateReference().Value, savedUniqueIds);
        Assert.Equal(0, resumedSpatial.StepCalls);
    }

    [Fact]
    public void Default_disease_catalog_restores_live_modifiers_without_replaying_damage_and_cures_its_source()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        EngineContextFake Engine() => EngineContextFake.Create(content,
            SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases).Service, new AppearanceFake(releases), random: RandomMaximum.Create());
        EngineContextFake engine = Engine();
        using DaggerfallSession session = new(engine.Context, definitions, inputs, DaggerfallTuning.Defaults);
        session.State.Progression.AdvanceTo(0, 2);
        var will = session.State.Actors.Player.Stats.GetStat(StatId.Parse("willpower"));
        double originalBase = will.BaseValue;
        Assert.Equal(DaggerfallDiseaseAdmission.Started, session.InflictDisease(new(
            "test-disease", "accepted-hit", null, session.State.Actors.Player.DurableId, [DaggerfallClassicDisease.BrainFever])));
        session.AdvanceElapsedTime(DaggerfallCalendar.SecondsPerDay);
        Assert.Equal(originalBase, will.BaseValue);
        Assert.Equal(originalBase - 5, will.Value);
        var save = session.CaptureSave();
        EngineContextFake restoredEngine = Engine();
        var identity = GameCompositionResolver.Resolve(FullContent(root), new GameBundleId("daggerfall.privateers-hold")).RequireComposition().Identity;
        using DaggerfallSession restored = DaggerfallSession.Restore(restoredEngine.Context, identity, definitions, inputs,
            DaggerfallTuning.Defaults, save, RandomMaximum.Create());
        var restoredWill = restored.State.Actors.Player.Stats.GetStat(StatId.Parse("willpower"));
        Assert.Equal(originalBase, restoredWill.BaseValue);
        Assert.Equal(will.Value, restoredWill.Value);
        double health = restored.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).Current;
        restored.AdvanceElapsedTime(0);
        Assert.Equal(will.Value, restoredWill.Value);
        Assert.Equal(1, restored.CureDisease(DaggerfallClassicDisease.BrainFever));
        Assert.Equal(originalBase, restoredWill.Value);
        Assert.Equal(health, restored.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).Current);
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
        string root = RepositoryRoot();
        PrivateersHoldInputs inputs = ReadInputs(root);
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
    public void Enabled_production_opening_refuses_to_begin_when_its_admitted_cinematic_bundle_is_missing()
    {
        string root = RepositoryRoot();
        PrivateersHoldInputs inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake engineContent = new(releases);
        PopulateContent(engineContent, inputs);
        EngineContextFake engine = EngineContextFake.Create(engineContent,
            SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases).Service, new AppearanceFake(releases));
        ProductInputConfiguration input = new(default, default, ReadOnlyMemory<ProductInputDescriptor>.Empty, ReadOnlyMemory<ProductInputMapping>.Empty);
        CapturingDaggerfallRuleset ruleset = new(videosEnabled: true);
        using WorldRpgProduct product = new(new ProductCreateContext(engine.Context, FullContent(root), input), ruleset, new GameBundleId("daggerfall.privateers-hold"));
        product.Start();
        ProductModeChange result = product.Begin();
        Assert.Equal(ProductMode.Title, product.Mode);
        Assert.Equal(ProductModeChangeOutcome.Refused, result.Outcome);
    }

    [Fact]
    public void Ordinary_save_slot_actions_roundtrip_selected_state_through_the_product()
    {
        // 8342 acceptance through ordinary controls: real input changes state, the menu save
        // action persists it through the product's own store, more input changes state again,
        // and the menu load action restores the saved point with honest outcome feedback.
        static ProductInputEvent Ui(string json) => Input(InputEventKind.DirectDigital) with
        {
            ValueKind = InputValueKind.ProductPayload,
            PayloadContract = "dagger.ui.action.v1"u8.ToArray(),
            PayloadData = Encoding.UTF8.GetBytes(json),
        };

        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs inputs = ReadInputs(root);
        List<string> releases = [];
        InMemoryPersistenceService persistence = new();
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        PerceptionFake perception = PerceptionFake.Create();
        perception.Receipt = Receipt(new PerceptionPair(1, 2000, 1d, 1d, PerceptionPairKind.Visible, 1d));
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), perception.Service, persistence: persistence);
        ProductInputConfiguration input = new(default, default, ReadOnlyMemory<ProductInputDescriptor>.Empty, ReadOnlyMemory<ProductInputMapping>.Empty);
        CapturingDaggerfallRuleset ruleset = new();
        using WorldRpgProduct product = new(new ProductCreateContext(engine.Context, FullContent(root), input), ruleset, new GameBundleId("daggerfall.privateers-hold"));
        product.Start();
        product.Begin();
        DaggerfallSession session = ruleset.RequireSession();
        TrackId staminaId = TrackId.Parse("stamina");

        // Loading an empty named slot is an honest outcome, not a session replacement.
        product.Update(new ProductUpdate(OuterUpdate(1), [Ui("{\"action\":\"load-slot\",\"key\":\"slot-1\"}")]));
        product.Update(new ProductUpdate(OuterUpdate(2), []));
        Assert.Contains("No save is indexed under 'slot-1'.", engine.PublishedField("lastOutcome"), StringComparison.Ordinal);

        // Ordinary held-key input steps the world: the spatial answer moves the player, so
        // position is the cooldown-free proof that gameplay input changed world state.
        static WorldPoint PlayerPos(DaggerfallSession target) => target.State.PlayerControl.Position
            ?? throw new InvalidOperationException("The player has no position.");
        WorldPoint positionBefore = PlayerPos(session);
        product.Update(new ProductUpdate(
            new ProductUpdateFacts(ProductUpdateMode.Realtime, ProductLifecycleState.Running, 1, 1, 3, 3, 60, 3, 0, 1d / 60d),
            [Input(InputEventKind.Key, InputEdge.Pressed, keyboard: KeyboardControl.KeyW)]));
        WorldPoint positionStepped = PlayerPos(session);
        Assert.True(positionStepped.X > positionBefore.X);

        // A named save uses the Host catalog and captures the first selected state.
        product.Update(new ProductUpdate(OuterUpdate(4), [Ui("{\"action\":\"save-slot\",\"label\":\"Before the dungeon\"}")]));
        WorldPoint firstPosition = PlayerPos(session);
        product.Update(new ProductUpdate(OuterUpdate(5), []));
        Assert.Contains("Saved 'Before the dungeon' (revision 1).", engine.PublishedField("lastOutcome"), StringComparison.Ordinal);

        // More ordinary input produces a distinct second slot state.
        product.Update(new ProductUpdate(
            new ProductUpdateFacts(ProductUpdateMode.Realtime, ProductLifecycleState.Running, 1, 1, 6, 6, 60, 3, 0, 1d / 60d),
            [Input(InputEventKind.Key, InputEdge.Pressed, keyboard: KeyboardControl.KeyW)]));
        WorldPoint movedSecondPosition = PlayerPos(session);
        Assert.True(movedSecondPosition.X > firstPosition.X);
        product.Update(new ProductUpdate(OuterUpdate(7), [Ui("{\"action\":\"save-slot\",\"label\":\"After the dungeon\"}")]));
        WorldPoint secondPosition = PlayerPos(session);

        // Overwriting an explicit selection requires a confirmation action and cannot alter the
        // second slot. The confirmed overwrite records the later world state in slot 1.
        product.Update(new ProductUpdate(
            new ProductUpdateFacts(ProductUpdateMode.Realtime, ProductLifecycleState.Running, 1, 1, 8, 8, 60, 3, 0, 1d / 60d),
            [Input(InputEventKind.Key, InputEdge.Pressed, keyboard: KeyboardControl.KeyW)]));
        WorldPoint overwrittenPosition = PlayerPos(session);
        product.Update(new ProductUpdate(OuterUpdate(9), [Ui("{\"action\":\"save-slot\",\"key\":\"slot-1\",\"label\":\"Revisited dungeon\"}")]));
        Assert.Contains("Confirm overwriting 'slot-1'.", engine.PublishedField("lastOutcome"), StringComparison.Ordinal);
        product.Update(new ProductUpdate(OuterUpdate(10), [Ui("{\"action\":\"save-slot\",\"key\":\"slot-1\",\"label\":\"Revisited dungeon\",\"confirm\":true}")]));
        Assert.Contains("Saved 'Revisited dungeon'", engine.PublishedField("lastOutcome"), StringComparison.Ordinal);

        // Loading the selected second slot replaces the session with its own earlier state rather
        // than the overwritten first slot.
        product.Update(new ProductUpdate(OuterUpdate(11), [Ui("{\"action\":\"load-slot\",\"key\":\"slot-2\"}")]));
        DaggerfallSession restored = ruleset.RequireSession();
        Assert.NotSame(session, restored);
        Assert.Equal(secondPosition, PlayerPos(restored));
        Assert.NotEqual(overwrittenPosition, PlayerPos(restored));
        product.Update(new ProductUpdate(OuterUpdate(12), []));
        Assert.Contains("Game loaded.", engine.PublishedField("lastOutcome"), StringComparison.Ordinal);

        // Deletion also demands confirmation and removes the first payload from reopened storage.
        product.Update(new ProductUpdate(OuterUpdate(13), [Ui("{\"action\":\"delete-slot\",\"key\":\"slot-1\"}")]));
        Assert.Contains("Confirm deleting 'slot-1'.", engine.PublishedField("lastOutcome"), StringComparison.Ordinal);
        product.Update(new ProductUpdate(OuterUpdate(14), [Ui("{\"action\":\"delete-slot\",\"key\":\"slot-1\",\"confirm\":true}")]));
        using WorldRpgSaveSlots reopened = new(engine.Context, "worldrpg.saves");
        (GameSaveEnvelope? deleted, WorldRpgSlotLoadDiagnostic? deletedDiagnostic) = reopened.LoadSlot("slot-1", "daggerfall");
        Assert.Null(deleted);
        Assert.Equal("missing", deletedDiagnostic!.Kind);
    }

    [Fact]
    public void Host_resume_refuses_unique_items_that_are_unissued_or_tombstoned_in_the_saved_ledger()
    {
        string root = RepositoryRoot();
        PrivateersHoldInputs inputs = ReadInputs(root);
        DaggerfallSavePayload valid = CapturedSave(root);
        DaggerfallUniqueSave tombstoned = Assert.IsType<DaggerfallUniqueSave>(valid.Inventory.UniqueItems.FirstOrDefault());
        DurableIdentityAllocator ledger = DurableIdentityAllocator.Restore(valid.RestoredIdentities());
        ulong unissued = ledger.NextIdentity(DurableIdentityKind.Item);
        DaggerfallSavePayload forged = valid with
        {
            Inventory = valid.Inventory with
            {
                UniqueItems = [.. valid.Inventory.UniqueItems, new DaggerfallUniqueSave("iron-tanto", unissued, valid.Inventory.UniqueItems[0].Metadata)],
            },
        };
        KindAllocatorState[] removedKinds = valid.Identities.Kinds.Select(state => state.Kind == DurableIdentityKind.Item
            ? state with
            {
                Reserved = state.Reserved.Where(value => value != tombstoned.EntityId).ToArray(),
                Removed = [.. state.Removed, tombstoned.EntityId],
            }
            : state).ToArray();
        DaggerfallSavePayload removed = valid with { Identities = new DurableIdentityState(removedKinds) };
        ProductInputConfiguration input = new(default, default, ReadOnlyMemory<ProductInputDescriptor>.Empty, ReadOnlyMemory<ProductInputMapping>.Empty);

        foreach ((DaggerfallSavePayload rejected, string classification) in new[]
        {
            (forged, nameof(DurableIdentityClassification.NeverIssued)),
            (removed, nameof(DurableIdentityClassification.Removed)),
        })
        {
            InMemoryPersistenceService persistence = new();
            List<string> releases = [];
            ContentFake content = new(releases);
            PopulateContent(content, inputs);
            SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
            EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), persistence: persistence);
            using WorldRpgSaveStore store = new(engine.Context, "daggerfall-host-save");
            store.Save("slot", new GameSaveEnvelope(DaggerfallSavePayload.Encode(rejected)), PersistenceRevisionGuard.Absent);
            CapturingDaggerfallRuleset ruleset = new();

            WorldRpgResumeResult result = WorldRpgProduct.TryResume(
                new ProductCreateContext(engine.Context, FullContent(root), input),
                store,
                "slot",
                ruleset,
                new GameBundleId("daggerfall.privateers-hold"));

            Assert.False(result.IsResumed);
            Assert.Null(result.Product);
            WorldRpgSaveDiagnostic diagnostic = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == "payload");
            Assert.Contains(classification, diagnostic.Message, StringComparison.Ordinal);
            Assert.Null(ruleset.Session);
            Assert.Equal(0, spatial.StepCalls);
        }
    }

    [Fact]
    public void Spawned_actors_register_retire_and_restore_with_distinct_state()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs inputs = ReadInputs(root);
        long authoredPlacement = inputs.Project.Actors.Keys.Order().First();
        RulesetSavePayload payload;
        long first;
        long second;
        using (DaggerfallSession session = FreshSession())
        {
            first = session.SpawnActor("imp", new ActorPose(new WorldPoint(10, 0, 10), 0f));
            second = session.SpawnActor("imp", new ActorPose(new WorldPoint(20, 0, 20), 1f));
            Assert.NotEqual(first, second);
            Assert.True(first >= (long)DaggerfallSession.DynamicActorFirstIdentity);
            Assert.True(second >= (long)DaggerfallSession.DynamicActorFirstIdentity);
            Assert.Equal("imp", session.DefinitionsByActor[first].Id.Value);
            Assert.Equal(new DaggerfallActorId("imp"), session.DynamicActors[second]);
            // The spawn carries the same Mechanics binding an authored actor is built with.
            Assert.NotNull(session.State.Actors.Get(second).Stats.GetTrack(TrackId.Parse("health")));
            Assert.NotNull(session.State.InventoryFor(second));
            // Distinct state before retirement: only the second moves.
            session.State.Actors.Get(second).ApplyPose(new ActorPose(new WorldPoint(21, 0, 21), 2f));
            session.RetireActor(first);
            Assert.False(session.State.Actors.TryGet(first, out _));
            Assert.False(session.DefinitionsByActor.ContainsKey(first));
            Assert.False(session.DynamicActors.ContainsKey(first));
            // Removal is terminal and distinct from never-loaded: every other shape refuses loudly.
            Assert.Throws<InvalidOperationException>(() => session.RetireActor(first));
            Assert.Throws<InvalidOperationException>(() => session.RetireActor(1));
            Assert.Throws<InvalidOperationException>(() => session.RetireActor(authoredPlacement));
            Assert.Throws<ArgumentOutOfRangeException>(() => session.RetireActor(-5));
            Assert.Throws<InvalidOperationException>(() => session.SpawnActor("missing-definition", new ActorPose(new WorldPoint(0, 0, 0), 0f)));
            Assert.Throws<InvalidOperationException>(() => session.SpawnActor("player", new ActorPose(new WorldPoint(0, 0, 0), 0f)));
            payload = session.CaptureSave();
        }

        DaggerfallSavePayload captured = DaggerfallSavePayload.Read(payload);
        Assert.DoesNotContain(captured.DynamicActors, actor => actor.EntityId == first);
        DaggerfallDynamicActorSave saved = Assert.Single(captured.DynamicActors);
        Assert.Equal(second, saved.EntityId);
        Assert.Equal("imp", saved.Definition);
        // No stale bindings survive the retired actor: no cooldown, corpse, or inventory section names it.
        Assert.DoesNotContain(captured.CombatCooldowns, cooldown => cooldown.AttackerId == first);
        Assert.DoesNotContain(captured.Corpses, corpse => corpse.ActorId == first);
        Assert.DoesNotContain(captured.ActorInventories, section => section.EntityId == first);
        Assert.Contains(captured.ActorInventories, section => section.EntityId == second);

        List<string> releases = [];
        ContentFake resumedContent = new(releases);
        PopulateContent(resumedContent, inputs);
        SpatialFake resumedSpatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake resumedEngine = EngineContextFake.Create(resumedContent, resumedSpatial.Service, new AppearanceFake(releases));
        ResolvedCompositionIdentity identity = GameCompositionResolver.Resolve(FullContent(root), new GameBundleId("daggerfall.privateers-hold")).RequireComposition().Identity;
        using DaggerfallSession resumed = DaggerfallSession.Restore(resumedEngine.Context, identity, definitions, inputs, DaggerfallTuning.Defaults, payload, RandomMinimum.Create());

        Assert.False(resumed.State.Actors.TryGet(first, out _));
        Assert.True(resumed.State.Actors.TryGet(second, out ActorState? restored));
        Assert.Equal(new WorldPoint(21, 0, 21), restored.Position);
        Assert.Equal("imp", resumed.DefinitionsByActor[second].Id.Value);
        Assert.Equal(new DaggerfallActorId("imp"), resumed.DynamicActors[second]);
        // The tombstone holds: a later spawn never reissues the retired identity.
        long third = resumed.SpawnActor("rat", new ActorPose(new WorldPoint(30, 0, 30), 0f));
        Assert.NotEqual(first, third);
        Assert.NotEqual(second, third);
    }

    [Fact]
    public void Retiring_a_dynamic_caster_cancels_its_effect_on_another_actor_before_save()
    {
        DaggerfallEffectCatalog catalog = new(
        [new DaggerfallEffectDefinition("retire-bound", "retire-bound", DaggerfallEffectStacking.Stack, 1, 1)]);
        using DaggerfallSession session = FreshSession(catalog);
        long caster = session.SpawnActor("rat", new ActorPose(new WorldPoint(10, 0, 10), 0f));
        using JsonDocument state = JsonDocument.Parse("{\"bound\":true}");
        _ = session.State.Effects.Start(new DaggerfallEffectRequest(
            "retire-caster-effect", "retire-bound", "caster-spell", caster,
            DaggerfallActorIdentity.PlayerEntityId, "classic", "magic", null, 1, 5, state.RootElement.Clone()));
        Assert.Single(session.State.Effects.Active);

        session.RetireActor(caster);

        Assert.Empty(session.State.Effects.Active);
        Assert.Empty(DaggerfallSavePayload.Read(session.CaptureSave()).ActiveEffects);
    }

    [Fact]
    public void Retiring_an_actor_with_open_loot_closes_the_interaction_without_throwing()
    {
        // B1 (behavior lane): retiring a corpse with its loot open used to leave the loot
        // presentation naming a definition that no longer exists, so the next Read threw
        // KeyNotFoundException instead of showing no loot. The retire now closes the container
        // and the mode machine follows back to play through the ordinary Read-null path.
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        spatial.KeepPosition = true;
        PerceptionFake perception = PerceptionFake.Create();
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), perception.Service);
        using DaggerfallSession session = new(engine.Context, definitions, inputs, DaggerfallTuning.Defaults);

        WorldPoint playerPosition = session.State.PlayerControl.Position ?? throw new InvalidOperationException("The test session has no player position.");
        // A rat carries no minimum-metal gate, so the player's iron weapon can kill it; an imp
        // would honestly refuse the same swing (steel gate) and never produce the corpse.
        long spawned = session.SpawnActor("rat", new ActorPose(playerPosition, 0f));
        session.State.Actors.Get(spawned).Stats.GetTrack(TrackId.Parse("health")).SetCurrent(1, clamp: true);
        session.ResolveExplicitMelee(new ExplicitMeleeRequest(1, spawned, 1, 1, .125));
        Assert.True(session.State.Actors.Get(spawned).IsDefeated);
        Assert.True(session.Corpses.ContainsKey(spawned));
        AimActivationAt(session, spawned);
        perception.Receipt = Receipt(new PerceptionPair(1, checked((ulong)spawned), 1d, 1d, PerceptionPairKind.Visible, 1d));
        ProductInputEvent loot = Input(InputEventKind.DirectDigital) with
        {
            ValueKind = InputValueKind.ProductPayload,
            PayloadContract = "dagger.ui.action.v1"u8.ToArray(),
            PayloadData = Encoding.UTF8.GetBytes("""{"action":"loot"}"""),
        };
        perception.Requests.Clear();
        session.Update(new ProductUpdate(OuterUpdate(2), [loot]));
        Assert.IsType<LootPresentation>(session.OpenLoot);
        Assert.Single(perception.Requests, request => request.Targets.Span.ToArray()
            .Any(target => target.Entity == checked((ulong)spawned)));
        session.ApplyProductMode(ProductMode.Modal);
        session.RetireActor(spawned);
        Assert.Null(session.OpenLoot);
        Assert.Equal(ProductMode.Playing, session.PendingModeRequest);
        session.Update(new ProductUpdate(OuterUpdate(3), []));
        Assert.Null(session.OpenLoot);
    }

    [Fact]
    public void Activation_mode_change_consumes_a_coincident_attack_without_starting_melee()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        PerceptionFake perception = PerceptionFake.Create();
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), perception.Service);
        using DaggerfallSession session = new(engine.Context, definitions, inputs, DaggerfallTuning.Defaults);
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
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), PerceptionFake.Create().Service);
        using DaggerfallSession session = new(engine.Context, definitions, inputs, DaggerfallTuning.Defaults);

        session.Update(new ProductUpdate(OuterUpdate(1), [
            Input(InputEventKind.DirectDigital, x: 1f, phase: InputPhase.DirectUi, intent: "attack"),
            Input(InputEventKind.DirectDigital, x: 1f, phase: InputPhase.DirectUi, intent: "interact"),
        ]));

        Assert.Null(session.LastMeleeTargeting);
        Assert.Contains("No eligible target", session.ActivationView.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Contextual_activation_opens_the_current_corpse_with_one_visibility_query()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        spatial.KeepPosition = true;
        PerceptionFake perception = PerceptionFake.Create();
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), perception.Service);
        using DaggerfallSession session = new(engine.Context, definitions, inputs, DaggerfallTuning.Defaults);

        WorldPoint playerPosition = session.State.PlayerControl.Position ?? throw new InvalidOperationException("The test session has no player position.");
        long spawned = session.SpawnActor("rat", new ActorPose(playerPosition with { Z = playerPosition.Z - 1f }, 0f));
        session.State.Actors.Get(spawned).Stats.GetTrack(TrackId.Parse("health")).SetCurrent(1, clamp: true);
        session.ResolveExplicitMelee(new ExplicitMeleeRequest(1, spawned, 1, 1, .125));
        Assert.True(session.Corpses.ContainsKey(spawned));
        AimActivationAt(session, spawned);
        perception.Receipt = Receipt(new PerceptionPair(1, checked((ulong)spawned), 1d, 1d, PerceptionPairKind.Visible, 1d));
        perception.Requests.Clear();

        session.Update(new ProductUpdate(OuterUpdate(1), [
            Input(InputEventKind.DirectDigital, x: 1f, phase: InputPhase.DirectUi, intent: "interact"),
        ]));

        Assert.Single(perception.Requests, request => request.Targets.Span.ToArray()
            .Any(target => target.Entity == checked((ulong)spawned)));
        Assert.IsType<LootPresentation>(session.OpenLoot);
        Assert.True(session.ActivationView.Applied);
        Assert.Equal("grab", session.ActivationView.Mode);
    }

    private static DaggerfallSavePayload CapturedSave(string root)
    {
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        using DaggerfallSession session = new(engine.Context, definitions, inputs, DaggerfallTuning.Defaults);
        return DaggerfallSavePayload.Read(session.CaptureSave());
    }

    private static DaggerfallSavePayload RoundTrip(DaggerfallSavePayload value) => DaggerfallSavePayload.Read(DaggerfallSavePayload.Encode(value));

    private static IEnumerable<ulong> CapturedUniqueItemIds(DaggerfallSavePayload saved) =>
        saved.Inventory.UniqueItems.Select(item => item.EntityId)
            .Concat(saved.Corpses.SelectMany(corpse => corpse.UniqueItems).Select(item => item.EntityId))
            .Concat(saved.ActorInventories.SelectMany(actor => actor.Inventory.UniqueItems).Select(item => item.EntityId));


    [Fact]
    public void Ordinary_attack_uses_the_engine_visibility_receipt_then_the_shared_explicit_melee_policy()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        PerceptionFake perception = PerceptionFake.Create();
        perception.Receipt = Receipt(new PerceptionPair(1, 2000, 2.25d, .5d, PerceptionPairKind.Visible, 1d));
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), perception.Service);

        using DaggerfallSession session = new(engine.Context, definitions, inputs, DaggerfallTuning.Defaults);
        double healthBefore = session.State.Actors.Get(2000).Stats.GetTrack(TrackId.Parse("health")).Current;
        session.Update(AttackUpdate());

        TargetingEvidence evidence = Assert.IsType<TargetingEvidence>(session.LastMeleeTargeting);
        Assert.Equal(2000, evidence.SelectedTargetId);
        Assert.Equal(perception.Requests.Last(), evidence.Request);
        Assert.True(perception.Requests.Count > 1);
        Assert.Equal((ulong)1, evidence.Request.Observers.Span[0].Entity);
        Assert.Equal(2.25d, evidence.Request.Observers.Span[0].MaximumDistance);
        Assert.Equal(.5d, evidence.Request.Observers.Span[0].MinimumFacingCosine);
        Assert.Equal(1, evidence.Receipt.Pairs.Length);
        Assert.True(session.State.Actors.Get(2000).Stats.GetTrack(TrackId.Parse("health")).Current < healthBefore);
    }

    [Fact]
    public void Loot_ui_actions_open_without_transfer_then_take_one_at_the_admitted_boundary_and_close()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        spatial.KeepPosition = true;
        PerceptionFake perception = PerceptionFake.Create();
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), perception.Service);
        using DaggerfallSession session = new(engine.Context, definitions, inputs, DaggerfallTuning.Defaults);
        session.State.Actors.Get(2000).Stats.GetTrack(TrackId.Parse("health")).SetCurrent(1, clamp: true);
        session.ResolveExplicitMelee(new ExplicitMeleeRequest(1, 2000, 1, 1, .125));
        AimActivationAt(session, 2000);
        CorpseContainer corpse = session.Corpses[2000];
        Assert.True(corpse.IsRegistered);
        session.State.Containers.Seed(corpse.Owner, [new InventoryContainerSeed(new InventoryItemId("gold-piece"), 5, Stack: InventoryStackId.Parse("test.loot.2807"))]);
        RegisterCorpseStack(session, definitions, 2000, "test.loot.2807");
        perception.Receipt = Receipt(new PerceptionPair(1, 2000, 1d, 1d, PerceptionPairKind.Visible, 1d));
        ulong before = session.State.Inventory.Read().StoreRevision;
        void Ui(string json, ulong step)
        {
            ProductInputEvent action = Input(InputEventKind.DirectDigital) with
            {
                ValueKind = InputValueKind.ProductPayload,
                PayloadContract = "dagger.ui.action.v1"u8.ToArray(),
                PayloadData = Encoding.UTF8.GetBytes(json),
            };
            session.Update(new ProductUpdate(OuterUpdate(step), [action]));
        }
        Ui("{\"action\":\"loot\"}", 2);
        LootPresentation opened = Assert.IsType<LootPresentation>(session.OpenLoot);
        Assert.Equal(before, session.State.Inventory.Read().StoreRevision);
        InventoryItemPresentation gold = opened.Items.Single(item => item.Key == DaggerfallInventoryPresentation.StackKey(InventoryStackId.Parse("test.loot.2807")));
        string take = System.Text.Json.JsonSerializer.Serialize(new { action = "loot-take", container = opened.Container, revision = opened.Revision, item = gold.Key });
        Ui(take, 3);
        Assert.Equal(ulong.Parse(gold.Quantity) - 1, ulong.Parse(session.OpenLoot!.Items.Single(item => item.Key == gold.Key).Quantity));
        ulong after = session.State.Inventory.Read().StoreRevision;
        Ui(take, 4);
        Assert.Equal(after, session.State.Inventory.Read().StoreRevision);
        Ui(System.Text.Json.JsonSerializer.Serialize(new { action = "loot-close", container = opened.Container }), 5);
        Assert.Null(session.OpenLoot);
    }

    [Fact]
    public void Corpse_loot_uses_engine_visibility_and_transfers_to_the_player_only_after_explicit_interaction()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        PerceptionFake perception = PerceptionFake.Create();
        Dictionary<InventoryItemId, ItemDefinition> items = definitions.Items.Values.Concat(definitions.TemplateItems.Values).ToDictionary(
            item => new InventoryItemId(item.Id.Value),
            item => new ItemDefinition(ItemDefinitionId.Parse(item.Id.Value), item.IsFungible ? ItemKind.Fungible : ItemKind.Unique, item.MaximumQuantity));
        using ActorsState actors = ActorsWithNpc(2000, DefeatedMechanics(), new WorldPoint(0f, 0f, 1f));
        InventoryStore world = new();
        EntityId playerOwner = actors.Player.Actor.Entity;
        MechanicsInventoryContainerCoordinator containers = new(world, actors.Entities, items);
        containers.RegisterOwner(playerOwner);
        using SpatialMovementSystem movement = new(spatial.Service, content, inputs.SpatialArtifact, DaggerfallTuning.Defaults.Spatial);
        DaggerfallItemInstances itemInstances = new();
        DaggerfallCorpseLootModule loot = new(
            perception.Service, movement, containers, itemInstances, playerOwner, actors,
            new Dictionary<long, DaggerfallActorDefinition> { [2000] = definitions.RequireActor(new DaggerfallActorId("thief")) },
            definitions, RandomMinimum.Create(), new DaggerfallUniqueItemAllocator(1_000), new ProgressionState(), DaggerfallTuning.Defaults.LootInteraction,
            CharacterForLoot(definitions));
        // Corpse policy is independent of player XP credit.
        ActorDiedFact death = new(2000, 77, 3, 2, 3);
        loot.Create(death);
        Assert.True(loot.Corpses[2000].IsInteractable);
        Assert.Empty(containers.Read(playerOwner).Stacks);
        var magicItem = Assert.Single(containers.Read(loot.Corpses[2000].Owner).UniqueItems,
            item => item.Definition.Value.Contains("-magic-", StringComparison.Ordinal));
        ulong magicIdentity = actors.Entities.IdentityOf(magicItem.Entity).Value;
        Assert.StartsWith("magic-item.", itemInstances.RequireUnique(magicIdentity).Enchantment);

        perception.Receipt = Receipt(new PerceptionPair(1, 2000, 2.25d, .5d, PerceptionPairKind.Occluded, 0d));
        Assert.Null(loot.PrepareLoot(new PlayerControlState(new WorldPoint(0, 0, 0), 0, 0), ForwardLook()));
        Assert.True(loot.Corpses[2000].IsInteractable);

        perception.Receipt = Receipt(new PerceptionPair(1, 2000, 2.25d, .5d, PerceptionPairKind.Visible, 1d));
        world.RegisterEquipment(new EquipmentState(playerOwner));
        InventoryComponent inventory = actors.Player.Actor.Get<InventoryComponent>();
        EquipmentComponent equipmentComponent = new(world, playerOwner);
        actors.Player.Actor.Add(equipmentComponent);
        MechanicsEquipmentCoordinator equipment = new(inventory, equipmentComponent, actors.Entities, items,
            definitions.EquipmentSlots.Values.ToDictionary(slot => new WorldRpg.Kit.Inventory.EquipmentSlotId(slot.Id.Value), DaggerActorFactory.ToManagedSlot));
        DaggerfallEquipmentMoves inventoryMoves = new(new MechanicsInventoryCoordinator(inventory, actors.Entities, items), equipment, definitions);
        DaggerfallInventoryPresentation inventoryUi = new(inventoryMoves, definitions,
            new Dictionary<string, string>());
        DaggerfallLootPresentation panel = new(loot, inventoryUi);
        PlayerControlState player = new(new WorldPoint(0, 0, 0), 0, 0);
        // An ordinary generated corpse can hold mixed contents. Ensure the selected stack
        // has several units so this exercise distinguishes taking one from taking all.
        InventoryStackId testStack = InventoryStackId.Parse("test.loot.2882");
        containers.Seed(loot.Corpses[2000].Owner, [new InventoryContainerSeed(new InventoryItemId("gold-piece"), 5, Stack: testStack)]);
        itemInstances.RegisterDefaultStack(DaggerfallItemOwner.Corpse(2000),
            containers.Read(loot.Corpses[2000].Owner).Stacks.Single(stack => stack.Id == testStack), definitions.Items[new DaggerfallItemId("gold-piece")]);
        ulong beforeOpen = world.Revision;
        Assert.Null(panel.Open(player, ForwardLook()));
        LootPresentation opened = Assert.IsType<LootPresentation>(panel.Read());
        Assert.Equal(beforeOpen, world.Revision);
        Assert.Empty(containers.Read(playerOwner).Stacks);
        InventoryItemPresentation gold = opened.Items.Single(item => item.Key == DaggerfallInventoryPresentation.StackKey(testStack));
        ulong goldBefore = ulong.Parse(gold.Quantity);
        DaggerfallPlayerUiAction take = new("loot-take", opened.Revision, gold.Key, Container: opened.Container);
        Assert.Null(panel.PrepareTake(take with { Container = "other" }, player, ForwardLook()));
        Assert.Equal(beforeOpen, world.Revision);
        perception.Receipt = Receipt(new PerceptionPair(1, 2000, 2.25d, .5d, PerceptionPairKind.Occluded, 0d));
        Assert.Null(panel.PrepareTake(take, player, ForwardLook()));
        Assert.Equal(beforeOpen, world.Revision);
        perception.Receipt = Receipt(new PerceptionPair(1, 2000, 2.25d, .5d, PerceptionPairKind.Visible, 1d));
        PendingCorpseLoot pending = Assert.IsType<PendingCorpseLoot>(panel.PrepareTake(take, player, ForwardLook()));
        FactBuffer<IProductFact> facts = new();
        panel.Complete(loot.TryCommitLoot(pending, facts));
        Assert.Equal(1UL, containers.Read(playerOwner).Stacks.Single(stack => stack.Definition.Value == "gold-piece").Quantity);
        Assert.Equal(goldBefore - 1, containers.Read(loot.Corpses[2000].Owner).Stacks.Single(stack => stack.Id == testStack).Quantity);
        Assert.True(loot.Corpses[2000].IsInteractable);
        ulong afterTake = world.Revision;
        Assert.Null(panel.PrepareTake(take, player, ForwardLook()));
        Assert.Equal(afterTake, world.Revision);
        Assert.Contains("changed", panel.Message);
        while (panel.Read() is { Empty: false } current)
        {
            InventoryItemPresentation next = current.Items[0];
            PendingCorpseLoot one = Assert.IsType<PendingCorpseLoot>(panel.PrepareTake(
                new("loot-take", current.Revision, next.Key, Container: current.Container), player, ForwardLook()));
            panel.Complete(loot.TryCommitLoot(one, facts));
        }
        Assert.False(loot.Corpses[2000].IsInteractable);
        Assert.True(Assert.IsType<LootPresentation>(panel.Read()).Empty);
        Assert.Contains("until Exit", panel.Message);
        List<IProductFact> delivered = [];
        facts.Deliver(delivered.Add);
        Assert.All(delivered.OfType<LootAwardedFact>(), fact => Assert.Equal(1UL, fact.Quantity));
        Assert.Single(delivered.OfType<CorpseLootedFact>());
        Assert.Equal(2.25d, loot.LastEvidence?.Request.Observers.Span[0].MaximumDistance);
        Assert.Equal(.5d, loot.LastEvidence?.Request.Observers.Span[0].MinimumFacingCosine);
        panel.Close(opened.Container);
        Assert.Null(panel.Read());

    }

    [Fact]
    public void Empty_corpse_is_explicitly_searchable_once_without_an_engine_inventory_owner()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        PerceptionFake perception = PerceptionFake.Create();
        Dictionary<InventoryItemId, ItemDefinition> items = definitions.Items.Values.Concat(definitions.TemplateItems.Values).ToDictionary(
            item => new InventoryItemId(item.Id.Value),
            item => new ItemDefinition(ItemDefinitionId.Parse(item.Id.Value), item.IsFungible ? ItemKind.Fungible : ItemKind.Unique, item.MaximumQuantity));
        using ActorsState actors = ActorsWithNpc(2000, DefeatedMechanics(), new WorldPoint(0f, 0f, 1f));
        InventoryStore world = new();
        EntityId playerOwner = actors.Player.Actor.Entity;
        MechanicsInventoryContainerCoordinator containers = new(world, actors.Entities, items);
        containers.RegisterOwner(playerOwner);
        using SpatialMovementSystem movement = new(spatial.Service, content, inputs.SpatialArtifact, DaggerfallTuning.Defaults.Spatial);
        DaggerfallCorpseLootModule loot = new(
            perception.Service, movement, containers, new DaggerfallItemInstances(), playerOwner, actors,
            new Dictionary<long, DaggerfallActorDefinition> { [2000] = definitions.RequireActor(new DaggerfallActorId("rat")) },
            definitions, RandomMinimum.Create(), new DaggerfallUniqueItemAllocator(1_000), new ProgressionState(), DaggerfallTuning.Defaults.LootInteraction,
            CharacterForLoot(definitions));
        loot.Create(new ActorDiedFact(2000, 77, 3, 2, 3));
        Assert.True(loot.Corpses[2000].IsInteractable);
        Assert.False(loot.Corpses[2000].IsRegistered);
        Assert.Equal([playerOwner], world.InventoryOwners);

        perception.Receipt = Receipt(new PerceptionPair(1, 2000, 1d, .8d, PerceptionPairKind.Visible, 1d));
        PendingCorpseLoot pending = Assert.IsType<PendingCorpseLoot>(loot.PrepareLoot(new PlayerControlState(new WorldPoint(0, 0, 0), 0, 0), ForwardLook()));
        Assert.True(pending.IsEmpty);
        FactBuffer<IProductFact> facts = new();
        Assert.Equal(CorpseLootCommitResult.Committed, loot.TryCommitLoot(pending, facts));
        Assert.False(loot.Corpses[2000].IsInteractable);
        List<IProductFact> delivered = [];
        facts.Deliver(delivered.Add);
        Assert.Single(delivered.OfType<CorpseSearchedEmptyFact>());
        Assert.Null(loot.PrepareLoot(new PlayerControlState(new WorldPoint(0, 0, 0), 0, 0), ForwardLook()));
    }

    /// <summary>
    /// The archer's ranged policy: it damages the player from further than any melee reaches, without ever
    /// closing, and stops when the player is out of its own attack's range.
    /// </summary>
    /// <remarks>
    /// This is the capability the archer erratum recorded as missing. The distance the Engine classifies is
    /// the fixture's, set well past melee reach and inside the archer's own authored reach; the archer's
    /// state and the damage that lands are the ruleset's answer to it.
    /// </remarks>
    [Fact]
    public void The_archer_damages_the_player_from_beyond_melee_reach_without_closing()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        spatial.KeepPosition = true;
        PerceptionFake perception = PerceptionFake.Create();
        AppearanceFake appearance = new(releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, appearance, perception.Service);

        using DaggerfallSession session = new(engine.Context, definitions, inputs, DaggerfallTuning.Defaults);
        long archer = 2004;
        Assert.Equal("archer", inputs.Project.Actors[archer].ActorId.Value);
        double shotReach = definitions.Actions["archer-shot"].Reach!.Value;
        // Past every melee reach this corpus authors, and inside the archer's own.
        double separation = definitions.Actions.Values.Where(action => action.Interpretation == "fixed-melee").Max(action => action.Reach!.Value) + 1d;
        Assert.True(separation < shotReach, "the fixture's separation must sit between melee reach and the archer's shot");

        double healthBefore = session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).Current;
        // The archer sees the player at that separation, facing them, with the line clear.
        perception.Receipt = Receipt(new PerceptionPair(checked((ulong)archer), 1, separation, 1d, PerceptionPairKind.Visible, 1d));
        // The swing is decided and released on its authored frame, but the arrow is still in flight.
        appearance.AdvanceReceiptForAll = CrossedMarker(1);
        session.Update(new ProductUpdate(OuterUpdate(1), []));
        Assert.Equal(healthBefore, session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).Current);

        ulong arrivedStep = 1;
        for (ulong step = 2; step <= 240 && session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).Current == healthBefore; step++)
        {
            session.Update(new ProductUpdate(OuterUpdate(step), []));
            arrivedStep = step;
        }

        // It shot rather than closed: the state is the ranged attack, the player took damage, and no
        // navigation was asked for, which is what "without closing" means here.
        Assert.Equal(EnemyBehaviorState.Attack, session.LastEnemyBehavior[archer].State);
        Assert.Null(session.LastEnemyBehavior[archer].Navigation);
        double healthAfterShot = session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).Current;
        Assert.True(healthAfterShot < healthBefore, "the archer's shot must damage the player at that separation");
        // The line names the attacker, so the player can tell which of the enemies in front of them is
        // doing it: a hit the player took reads as the actor that landed it.
        string shotOutcome = engine.PublishedField("lastOutcome") ?? string.Empty;
        Assert.Contains("archer", shotOutcome, StringComparison.Ordinal);

        // The facing limit is the Engine's and the behaviour honours it: an attacker turned away does not
        // shoot, which is the same evidence kind the melee behaviour test uses for the other actor.
        perception.Receipt = Receipt(new PerceptionPair(checked((ulong)archer), 1, separation, 0d, PerceptionPairKind.FacingRejected, 0d));
        session.Update(new ProductUpdate(OuterUpdate(arrivedStep + 1), []));
        Assert.NotEqual(EnemyBehaviorState.Attack, session.LastEnemyBehavior[archer].State);

        // The same shot cannot land twice, and out past its own reach it stops entirely rather than
        // chasing: a ranged attacker that walks into melee is a melee attacker.
        appearance.AdvanceReceiptForAll = null;
        session.Update(new ProductUpdate(OuterUpdate(arrivedStep + 2), []));
        Assert.Equal(healthAfterShot, session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).Current);
        perception.Receipt = Receipt(new PerceptionPair(checked((ulong)archer), 1, shotReach + 1d, 1d, PerceptionPairKind.Visible, 1d));
        session.Update(new ProductUpdate(OuterUpdate(arrivedStep + 3), []));
        Assert.NotEqual(EnemyBehaviorState.Attack, session.LastEnemyBehavior[archer].State);

        // Every placed actor carries a policy, the archer included: an actor that cannot attack is a
        // missing capability its owner has to see, and there is no exception list left to hide one in.
        Assert.All(inputs.Project.Actors.Values, placement =>
            Assert.NotNull(definitions.RequireActor(placement.ActorId).ActionId));
    }

    [Fact]
    public void An_archer_shot_misses_when_the_player_leaves_its_release_aim_during_flight()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        spatial.KeepPosition = true;
        PerceptionFake perception = PerceptionFake.Create();
        AppearanceFake appearance = new(releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, appearance, perception.Service);
        using DaggerfallSession session = new(engine.Context, definitions, inputs, DaggerfallTuning.Defaults);
        const long archer = 2004;
        double separation = definitions.Actions.Values.Where(action => action.Interpretation == "fixed-melee").Max(action => action.Reach!.Value) + 1d;
        perception.Receipt = Receipt(new PerceptionPair(archer, 1, separation, 1d, PerceptionPairKind.Visible, 1d));
        double healthBefore = session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).Current;

        appearance.AdvanceReceiptForAll = CrossedMarker(1);
        session.Update(new ProductUpdate(OuterUpdate(1), []));
        WorldPoint releaseAim = session.State.PlayerControl.Position!.Value;
        session.State.PlayerControl.Restore(new WorldPoint(releaseAim.X + 1f, releaseAim.Y, releaseAim.Z), default);
        // Do not start a later shot while the first one flies; this test isolates the first release.
        appearance.AdvanceReceiptForAll = null;
        for (ulong step = 2; step <= 240; step++) session.Update(new ProductUpdate(OuterUpdate(step), []));

        Assert.Equal(healthBefore, session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).Current);
        Assert.Contains("missed", engine.PublishedField("lastOutcome"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Ranged_flight_discards_stale_generations_and_retires_missing_attackers()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        PerceptionFake perception = PerceptionFake.Create();
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), perception.Service);
        using DaggerfallSession session = new(engine.Context, definitions, inputs, DaggerfallTuning.Defaults);
        using SpatialMovementSystem targetingSpatial = new(spatial.Service, content, inputs.SpatialArtifact, DaggerfallTuning.Defaults.Spatial);
        Dictionary<long, DaggerfallActorDefinition> authored = inputs.Project.Actors.Values.ToDictionary(
            placement => placement.EntityId, placement => definitions.RequireActor(placement.ActorId));
        authored[DaggerfallActorIdentity.PlayerEntityId] = definitions.RequireActor(new DaggerfallActorId("player"));
        TargetingService targeting = new(perception.Service, targetingSpatial, session.State.Actors,
            new DaggerTargetingPolicy(authored, DaggerfallTuning.Defaults.MeleeTargeting));
        DaggerCombatRules combat = new(RandomMinimum.Create(), session.State.Actors, session.State.Equipment,
            session.State.InventoryFor, session.State.ItemInstances, definitions, authored, targeting);
        const long archer = 2004;
        const ulong generation = 77;
        const ulong releaseStep = 400;
        Dictionary<long, WorldPoint> positions = new()
        {
            [archer] = new WorldPoint(0, 0, 0),
            [DaggerfallActorIdentity.PlayerEntityId] = new WorldPoint(10, 0, 0),
        };
        FactBuffer<IProductFact> facts = new();
        double healthBefore = session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).Current;

        Assert.True(combat.Attacks.TryBeginEnemyAttack(archer, DaggerfallActorIdentity.PlayerEntityId, generation, releaseStep, .125, facts));
        combat.Execution.ApplyImpacts([new AttackImpactNotice(archer, DaggerfallActorIdentity.PlayerEntityId, generation, releaseStep, Expired: false)], generation, facts);
        combat.AdvanceRangedFlight(generation, releaseStep, .125, positions, facts);
        // A fresh admitted generation drops the old transient record rather than comparing its
        // release step to the new timeline's present step and accidentally landing it.
        combat.AdvanceRangedFlight(generation + 1, releaseStep + 1, .125, positions, facts);
        combat.AdvanceRangedFlight(generation, releaseStep + 100, .125, positions, facts);
        Assert.Equal(healthBefore, session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).Current);

        const ulong nextGeneration = 79;
        const ulong nextReleaseStep = 1000;
        Assert.True(combat.Attacks.TryBeginEnemyAttack(archer, DaggerfallActorIdentity.PlayerEntityId, nextGeneration, nextReleaseStep, .125, facts));
        combat.Execution.ApplyImpacts([new AttackImpactNotice(archer, DaggerfallActorIdentity.PlayerEntityId, nextGeneration, nextReleaseStep, Expired: false)], nextGeneration, facts);
        combat.AdvanceRangedFlight(nextGeneration, nextReleaseStep, .125, positions, facts);
        session.State.Actors.Entities.Destroy(ActorsState.Identity(archer));
        combat.AdvanceRangedFlight(nextGeneration, nextReleaseStep + 100, .125, positions, facts);
        Assert.Equal(healthBefore, session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).Current);
    }

    [Fact]
    public void A_save_after_ranged_release_drops_the_transient_in_flight_shot()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs inputs = ReadInputs(root);
        ResolvedCompositionIdentity composition = GameCompositionResolver.Resolve(FullContent(root), new GameBundleId("daggerfall.privateers-hold")).RequireComposition().Identity;
        List<string> releases = [];
        RulesetSavePayload saved;
        double healthAtRelease;
        using (DaggerfallSession original = CreateArcherSession(root, definitions, inputs, releases, out AppearanceFake appearance, out PerceptionFake perception))
        {
            perception.Receipt = Receipt(new PerceptionPair(2004, 1, 4d, 1d, PerceptionPairKind.Visible, 1d));
            appearance.AdvanceReceiptForAll = CrossedMarker(1);
            original.Update(new ProductUpdate(OuterUpdate(1), []));
            healthAtRelease = original.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).Current;
            saved = original.CaptureSave();
        }

        ContentFake resumedContent = new(releases);
        PopulateContent(resumedContent, inputs);
        SpatialFake resumedSpatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        resumedSpatial.KeepPosition = true;
        EngineContextFake resumedEngine = EngineContextFake.Create(resumedContent, resumedSpatial.Service, new AppearanceFake(releases));
        using DaggerfallSession resumed = DaggerfallSession.Restore(resumedEngine.Context, composition, definitions, inputs, DaggerfallTuning.Defaults, saved, RandomMinimum.Create());
        // The original release would arrive within this bound. A resumed session has no in-flight
        // record, so crossing that deadline cannot replay a shot from the discarded runtime queue.
        for (ulong step = 2; step <= 240; step++) resumed.Update(new ProductUpdate(OuterUpdate(step), []));

        Assert.Equal(healthAtRelease, resumed.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).Current);
    }

    [Fact]
    public void Enemy_behavior_uses_engine_visibility_then_shared_combat_and_transitions_without_replaying_damage()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        PerceptionFake perception = PerceptionFake.Create();
        perception.Receipt = Receipt(new PerceptionPair(2000, 1, 1d, 1d, PerceptionPairKind.Visible, 1d));
        AppearanceFake appearance = new(releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, appearance, perception.Service);

        using DaggerfallSession session = new(engine.Context, definitions, inputs, DaggerfallTuning.Defaults);
        double healthBefore = session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).Current;
        // The swing is decided on the admitted step and lands when its authored damage
        // frame is reached, so the update that carries the crossing is the one that hurts.
        appearance.AdvanceReceiptForAll = CrossedMarker(1);
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
        Assert.Equal(EnemyBehaviorState.Idle, session.LastEnemyBehavior[2000].State);
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
    public void Daggerfall_target_selection_accepts_engine_inclusive_boundaries_and_rejects_other_engine_classifications()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        PerceptionFake perception = PerceptionFake.Create();
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), perception.Service);

        using DaggerfallSession session = new(engine.Context, definitions, inputs, DaggerfallTuning.Defaults);
        using SpatialMovementSystem movement = new(spatial.Service, content, inputs.SpatialArtifact, DaggerfallTuning.Defaults.Spatial);
        Dictionary<long, DaggerfallActorDefinition> authored = inputs.Project.Actors.Values.ToDictionary(
            placement => placement.EntityId,
            placement => definitions.RequireActor(placement.ActorId));
        authored[DaggerfallActorIdentity.PlayerEntityId] = definitions.RequireActor(new DaggerfallActorId("player"));
        TargetingService targeting = new(perception.Service, movement, session.State.Actors, new DaggerTargetingPolicy(authored, DaggerfallTuning.Defaults.MeleeTargeting));
        perception.Receipt = Receipt(new PerceptionPair(1, 2000, 2.25d, .5d, PerceptionPairKind.Visible, 1d));
        Assert.Equal(2000, targeting.Select(session.State.PlayerControl.Position, ForwardLook().Forward, 2.25d));

        foreach (PerceptionPairKind rejected in new[] { PerceptionPairKind.FacingRejected, PerceptionPairKind.Occluded })
        {
            perception.Receipt = Receipt(new PerceptionPair(1, 2000, 2.25d, .5d, rejected, 1d));
            Assert.Null(targeting.Select(session.State.PlayerControl.Position, ForwardLook().Forward, 2.25d));
        }

        perception.Receipt = new PerceptionReadoutLeaseReceipt(ReadOnlyMemory<PerceptionPair>.Empty, ReadOnlyMemory<PerceptionAggregate>.Empty, 0, false, 0, 1, 1, 1, 1, 1, 0, 0, 0);
        Assert.Null(targeting.Select(session.State.PlayerControl.Position, ForwardLook().Forward, 2.25d));
        Assert.Equal(1U, targeting.LastEvidence?.Receipt.DistanceRejects);
    }

    [Fact]
    public void Daggerfall_target_selection_excludes_defeated_actors_and_uses_durable_ids_over_runtime_ids()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        PerceptionFake perception = PerceptionFake.Create();
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), perception.Service);

        using (DaggerfallSession session = new(engine.Context, definitions, inputs, DaggerfallTuning.Defaults))
        {
            using SpatialMovementSystem sessionMovement = new(spatial.Service, content, inputs.SpatialArtifact, DaggerfallTuning.Defaults.Spatial);
            Dictionary<long, DaggerfallActorDefinition> authored = inputs.Project.Actors.Values.ToDictionary(
                placement => placement.EntityId,
                placement => definitions.RequireActor(placement.ActorId));
            authored[DaggerfallActorIdentity.PlayerEntityId] = definitions.RequireActor(new DaggerfallActorId("player"));
            TargetingService targeting = new(perception.Service, sessionMovement, session.State.Actors, new DaggerTargetingPolicy(authored, DaggerfallTuning.Defaults.MeleeTargeting));
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
            DaggerfallTuning.Defaults.MeleeTargeting));
        perception.Receipt = Receipt(new PerceptionPair(1, 2000, 1d, .8d, PerceptionPairKind.Visible, 1d));
        Assert.NotEqual(new EntityId(checked((ulong)runtimeActor.DurableId)), runtimeActor.Actor.Entity);
        long? selected = runtimeTargeting.Select(
            new WorldPoint(0f, 0f, 0f),
            Vector3.UnitZ,
            2.25d);
        Assert.Equal(2000, selected);
        Assert.Single(perception.Requests.Last().Targets.Span.ToArray());
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

    [Fact]
    public void Stamina_recovery_is_held_back_outside_ordinary_play_and_resumes_with_it()
    {
        using DaggerfallSession session = FreshSession();
        StatsComponent mechanics = session.State.Actors.Player.Stats;
        TrackId stamina = TrackId.Parse("stamina");

        // Ordinary play recovers stamina over admitted world time, which is the calibration: without
        // it the held-back assertion below would pass on a mechanic that never runs at all.
        double maximum = mechanics.GetTrack(stamina).MaximumValue;
        mechanics.GetTrack(stamina).SetCurrent(1, clamp: true);
        // Recovery is per second against an integer track, so one step of a sixtieth recovers less
        // than one unit: the calibration has to give the mechanic enough admitted time to show.
        for (ulong step = 1; step <= 120; step++) session.Update(new ProductUpdate(OuterUpdate(step), []));
        double recovered = mechanics.GetTrack(stamina).Current;
        Assert.True(recovered > 1, $"ordinary play should recover stamina, but it stayed at {recovered}");

        // A modal holds the world still, so the same amount of admitted time recovers nothing.
        mechanics.GetTrack(stamina).SetCurrent(1, clamp: true);
        session.ApplyProductMode(ProductMode.Modal);
        for (ulong step = 121; step <= 240; step++) session.Update(new ProductUpdate(OuterUpdate(step), []));
        Assert.Equal(1d, mechanics.GetTrack(stamina).Current);

        // And ordinary play resumes it, so the gate is a gate rather than a stopped mechanic.
        session.ApplyProductMode(ProductMode.Playing);
        for (ulong step = 241; step <= 360; step++) session.Update(new ProductUpdate(OuterUpdate(step), []));
        Assert.True(mechanics.GetTrack(stamina).Current > 1);
        Assert.True(mechanics.GetTrack(stamina).Current <= maximum);
    }

    [Fact]
    public void Paused_mode_holds_enemy_facts_stamina_and_sprite_playback_until_play_resumes()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        PerceptionFake perception = PerceptionFake.Create();
        perception.Receipt = Receipt(new PerceptionPair(2000, 1, 1d, 1d, PerceptionPairKind.Visible, 1d));
        AppearanceFake appearance = new(releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, appearance, perception.Service);
        using DaggerfallSession session = new(engine.Context, definitions, inputs, DaggerfallTuning.Defaults);
        Track stamina = session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("stamina"));
        stamina.SetCurrent(1, clamp: true);

        session.ApplyProductMode(ProductMode.Paused);
        int factReactionsBefore = appearance.ControlRequests.Count;
        int playbackAdvancesBefore = appearance.AdvanceRequests.Count;
        AppearanceFact[] snapshotBefore = appearance.Snapshots[^1];
        session.Update(new ProductUpdate(OuterUpdate(1), []));

        // A held update republishes the existing scene for the mode UI, but does not simulate an
        // enemy, deliver its EnemyAttackStartedFact into appearance, recover stamina, or advance
        // any sprite playback.
        Assert.Empty(session.LastEnemyBehavior);
        Assert.Equal(1d, stamina.Current);
        Assert.Equal(factReactionsBefore, appearance.ControlRequests.Count);
        Assert.Equal(playbackAdvancesBefore, appearance.AdvanceRequests.Count);
        Assert.Equal(snapshotBefore, appearance.Snapshots[^1]);

        // Ordinary play is the sensitivity control: the same perception now runs behavior, its
        // delivered attack-start fact drives appearance, recovery advances, and the outer path
        // advances playback.
        session.ApplyProductMode(ProductMode.Playing);
        for (ulong step = 2; step <= 121; step++) session.Update(new ProductUpdate(OuterUpdate(step), []));
        Assert.Equal(EnemyBehaviorState.Attack, session.LastEnemyBehavior[2000].State);
        Assert.True(stamina.Current > 1d);
        Assert.True(appearance.ControlRequests.Count > factReactionsBefore);
        Assert.True(appearance.AdvanceRequests.Count > playbackAdvancesBefore);
    }

    /// <summary>A session that has not swung, so its weapon is ready.</summary>
    private static DaggerfallSession FreshSession(DaggerfallEffectCatalog? effects = null)
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        PerceptionFake perception = PerceptionFake.Create();
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), perception.Service);
        return effects is null
            ? new DaggerfallSession(engine.Context, definitions, inputs, DaggerfallTuning.Defaults)
            : new DaggerfallSession(engine.Context, definitions, inputs, DaggerfallTuning.Defaults, effects);
    }

    /// <summary>A session with one lootable corpse within reach, and the appearance it drives.</summary>
    private static DaggerfallSession LootableSession(out AppearanceFake appearance)
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        spatial.KeepPosition = true;
        PerceptionFake perception = PerceptionFake.Create();
        appearance = new AppearanceFake(releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, appearance, perception.Service);
        DaggerfallSession session = new(engine.Context, definitions, inputs, DaggerfallTuning.Defaults);
        session.State.Actors.Get(2000).Stats.GetTrack(TrackId.Parse("health")).SetCurrent(1, clamp: true);
        session.ResolveExplicitMelee(new ExplicitMeleeRequest(1, 2000, 1, 1, .125));
        AimActivationAt(session, 2000);
        CorpseContainer corpse = session.Corpses[2000];
        session.State.Containers.Seed(corpse.Owner, [new InventoryContainerSeed(new InventoryItemId("gold-piece"), 5, Stack: InventoryStackId.Parse("test.loot.3486"))]);
        RegisterCorpseStack(session, definitions, 2000, "test.loot.3486");
        perception.Receipt = Receipt(new PerceptionPair(1, 2000, 2.25d, .5d, PerceptionPairKind.Visible, 1d));
        return session;
    }

    private static ProductUpdateState AttackUpdate()
    {
        ProductUpdateState update = new(.125f);
        update.Add(Input(InputEventKind.DirectDigital, x: 1f, phase: InputPhase.DirectUi, intent: "attack"));
        return update;
    }

    private static LookReceipt ForwardLook() => new(default, default, Quaternion.Identity, Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY);

    /// <summary>Places the fixture player inside the Engine interaction cone for the named world target.</summary>
    private static void AimActivationAt(DaggerfallSession session, long actorId)
    {
        WorldPoint target = session.State.Actors.Get(actorId).Position;
        session.State.PlayerControl.MoveTo(target.ToVector() + Vector3.UnitZ);
        session.State.PlayerControl.YawRadians = 0f;
        session.State.PlayerControl.PitchRadians = 0f;
    }

    private static StatsComponent DefeatedMechanics()
    {
        Stat maximum = new(100, quantum: 1, rounding: MidpointRounding.ToZero, integerRounding: MidpointRounding.ToZero);
        StatsComponent stats = new();
        stats.AddStat(StatId.Parse("health-maximum"), maximum);
        stats.AddTrack(TrackId.Parse("health"), new Track(
            maximum,
            0,
            quantum: 1,
            rounding: MidpointRounding.ToZero,
            integerRounding: MidpointRounding.ToZero));
        return stats;
    }

    private static PerceptionReadoutLeaseReceipt Receipt(params PerceptionPair[] pairs) => new(pairs, ReadOnlyMemory<PerceptionAggregate>.Empty, checked((uint)pairs.Length), false, 0, 1, 1, checked((uint)pairs.Length), checked((ulong)pairs.Length), 0, 0, 0, 0);

    private static PrivateersHoldInputs ReadInputs(string root)
    {
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        return PrivateersHoldContent.Read(ImportContent(root), File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.privateers-hold.json")), definitions);
    }

    private static void RegisterCorpseStack(DaggerfallSession session, DaggerfallDefinitions definitions, long actorId, string stackId)
    {
        CorpseContainer corpse = session.Corpses[actorId];
        InventoryStackId id = InventoryStackId.Parse(stackId);
        InventoryStack stack = session.State.Containers.Read(corpse.Owner).Stacks.Single(value => value.Id == id);
        session.State.ItemInstances.RegisterDefaultStack(DaggerfallItemOwner.Corpse(actorId), stack,
            definitions.Items[new DaggerfallItemId(stack.Definition.Value)]);
    }

    private static NormalizedActorSprite SpriteFor(PrivateersHoldInputs inputs, string actorId) => inputs.ActorSprites.First(pair => inputs.Project.Actors[pair.Key].ActorId.Value == actorId).Value;

    private static ProductContent ImportContent(string root) => ContentAt(root, "worldrpg/imports/privateers-hold");

    private static ProductContent FullContent(string root)
    {
        const string publicAudioRoot = "worldrpg/media/audio/clips";
        const string importedAudioRoot = "worldrpg/imports/privateers-hold/media/audio/clips";
        const string publicAudioBundle = "daggerfall.classic-audio";
        const string importedAudioBundle = "daggerfall.privateers-hold-audio";
        string contentRoot = Path.Combine(root, "content");
        BundleContentFake bundles = new();
        List<ProductContentFile> eager = [];

        foreach (string file in Directory.GetFiles(Path.Combine(contentRoot, "worldrpg"), "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(contentRoot, file).Replace(Path.DirectorySeparatorChar, '/');
            string? bundle = relative.StartsWith(publicAudioRoot + "/", StringComparison.Ordinal) ? publicAudioBundle
                : relative.StartsWith(importedAudioRoot + "/", StringComparison.Ordinal) ? importedAudioBundle
                : null;
            if (bundle is null)
            {
                eager.Add(new ProductContentFile(Encoding.UTF8.GetBytes(relative), File.ReadAllBytes(file)));
                continue;
            }

            string rootPath = bundle == publicAudioBundle ? publicAudioRoot : importedAudioRoot;
            bundles.Add(bundle, relative[(rootPath.Length + 1)..], File.ReadAllBytes(file));
        }

        return new ProductContent(eager.ToArray(), bundles);
    }

    private static ProductContent ContentAt(string root, string relativeDirectory)
    {
        string contentRoot = Path.Combine(root, "content");
        string selected = Path.Combine(contentRoot, relativeDirectory);
        return new ProductContent(Directory.GetFiles(selected, "*", SearchOption.AllDirectories)
            .Select(path => new ProductContentFile(Encoding.UTF8.GetBytes(Path.GetRelativePath(contentRoot, path).Replace(Path.DirectorySeparatorChar, '/')), File.ReadAllBytes(path)))
            .ToArray());
    }

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md"))) return current.FullName;
        throw new InvalidOperationException("Could not locate the Rusty Dagger repository root.");
    }

    // The facts around this one pin the projection: what it carries, how often, how it is chunked and
    // what it refuses. The DOM half - adopting a block, retrying a revision it lacks, repainting panels
    // when art arrives late, showing the death screen - has no behaviour harness in this repository, and
    // the death screen's on-screen rendering additionally needs a live damage path that does not exist
    // yet (task #8262). Those are checked by a real host run, not here.
    [Fact]
    public void The_projection_carries_the_published_ui_art_the_dom_draws()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        List<string> releases = [];
        ContentFake content = new(releases);
        PrivateersHoldInputs inputs = ReadInputs(root);
        PopulateContent(content, inputs);
        EngineContextFake engine = EngineContextFake.Create(content, SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases).Service, new AppearanceFake(releases));

        using DaggerfallSession session = new(engine.Context, definitions, inputs, DaggerfallTuning.Defaults);
        session.PublishInitial();
        Dictionary<string, object?> hud = Assert.IsType<Dictionary<string, object?>>(engine.Published());
        Dictionary<string, object?> art = Assert.IsType<Dictionary<string, object?>>(hud["uiArt"]);
        Assert.Equal(hud["uiArtRevision"], art["revision"]);

        Dictionary<string, object?> images = Assert.IsType<object?[]>(art["images"])
            .Cast<Dictionary<string, object?>>()
            .ToDictionary(image => Assert.IsType<string>(image["id"]), image => (object?)Assert.IsType<string>(image["image"]), StringComparer.Ordinal);
        // One artifact per identity the DOM draws: the mode screen, the chrome, the three authored
        // inventory skins the panels paint their frames with, plus every inventory icon the content
        // pack names for its items.
        // The always-shown set plus every admitted item icon: the supplied screens a mode is shown with
        // joined the set, so the count moves with it rather than being pinned to the older five.
        Assert.Equal(10 + inputs.ClassicPresentation.InventoryIcons.Count, images.Count);
        Assert.All(images.Values, image => Assert.StartsWith("data:image/png;base64,", Assert.IsType<string>(image), StringComparison.Ordinal));

        // The bytes are the published artifacts, read from admitted content by their content name.
        Assert.Equal(
            $"data:image/png;base64,{Convert.ToBase64String(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/media/ui/screen-death.png")))}",
            images["screen.death"]);
        // Every supplied screen arrives byte for byte from the artifact the inventory names, so a screen
        // published from the wrong file - or from a re-encode that lost its palette - fails here rather
        // than only differing in prefix.
        foreach ((string id, string file) in new[]
        {
            ("screen.character-generation", "screen-character-generation"),
            ("screen.pick.02", "screen-pick-02"),
            ("screen.prison", "screen-prison"),
            ("screen.start-menu", "screen-start-menu"),
            ("screen.title", "screen-title"),
        })
        {
            Assert.Equal(
                $"data:image/png;base64,{Convert.ToBase64String(File.ReadAllBytes(Path.Combine(root, "content", "worldrpg", "media", "ui", $"{file}.png")))}",
                images[id]);
        }
        // The set is exactly what this presentation draws - the screens it shows plus the icons the
        // pack names - so an artifact silently added to or dropped from the payload fails here.
        string[] expected =
        [
            "screen.death",
            "window.character-sheet.chrome",
            // The supplied screens a mode is shown with, each published in the palette its own file
            // carries.
            "screen.character-generation",
            "screen.pick.02",
            "screen.prison",
            "screen.start-menu",
            "screen.title",
            // The panel frames are published art now, not files staged beside the UI bundle.
            "inventory.skin.grid-slot-slate.v1",
            "inventory.skin.panel-slate.v1",
            "inventory.skin.titlebar-slate.v1",
            .. inputs.ClassicPresentation.InventoryIcons.Values,
        ];
        Assert.Equal([.. expected.Order(StringComparer.Ordinal)], [.. images.Keys.Order(StringComparer.Ordinal)]);

        // One icon byte for byte, from the path the inventory states, so a wrong file under a right
        // identity cannot pass on a prefix check.
        using JsonDocument inventory = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, "content", DaggerfallUiArt.InventoryPath)));
        string iconPath = inventory.RootElement.GetProperty("artifacts").EnumerateArray()
            .Where(artifact => artifact.TryGetProperty("mediaId", out JsonElement mediaId) && mediaId.GetString() == "inventory.icon.iron-dagger")
            .Single()
            .GetProperty("path").GetString()!;
        Assert.Equal(
            $"data:image/png;base64,{Convert.ToBase64String(File.ReadAllBytes(Path.Combine(root, "content", iconPath)))}",
            images["inventory.icon.iron-dagger"]);
    }

    [Fact]
    public void Admitted_art_uses_its_current_bytes_without_runtime_digest_or_revision_hashing()
    {
        string root = RepositoryRoot();
        List<string> releases = [];
        PrivateersHoldInputs inputs = ReadInputs(root);
        string[] icons = [.. inputs.ClassicPresentation.InventoryIcons.Values];

        ContentFake original = new(releases);
        PopulateContent(original, inputs);
        DaggerfallUiArt baseline = DaggerfallUiArt.Read(original, icons);

        JsonNode inventory = JsonNode.Parse(File.ReadAllBytes(Path.Combine(root, "content", DaggerfallUiArt.InventoryPath)))!;
        JsonObject screen = inventory["artifacts"]!.AsArray().Select(node => node!.AsObject())
            .Single(artifact => (string?)artifact["mediaId"] == "screen.death");
        string path = (string)screen["path"]!;
        byte[] edited = File.ReadAllBytes(Path.Combine(root, "content", path));
        edited[^1] ^= 0xFF;

        // The current admitted bytes are what the DOM receives. Runtime does not derive a cache key
        // by hashing every image merely to detect that a trusted local publication changed.
        ContentFake republished = new(releases);
        PopulateContent(republished, inputs);
        republished.Add(path, edited);
        screen["byteLength"] = edited.Length;
        screen["sha256"] = Convert.ToHexStringLower(SHA256.HashData(edited));
        republished.Add(DaggerfallUiArt.InventoryPath, Encoding.UTF8.GetBytes(inventory.ToJsonString()));
        DaggerfallUiArt changedArt = DaggerfallUiArt.Read(republished, icons);
        Assert.NotEqual(baseline.Revision, changedArt.Revision);
        Assert.NotEqual(
            baseline.Images.Single(image => image.Id == "screen.death").Image,
            changedArt.Images.Single(image => image.Id == "screen.death").Image);

        // Runtime presentation trusts the admitted resource by name. The inventory's digest is build
        // provenance, not an every-session integrity protocol, so an edited admitted byte is served.
        ContentFake stale = new(releases);
        PopulateContent(stale, inputs);
        stale.Add(path, edited);
        Assert.NotEqual(
            baseline.Images.Single(image => image.Id == "screen.death").Image,
            DaggerfallUiArt.Read(stale, icons).Images.Single(image => image.Id == "screen.death").Image);
    }

    [Fact]
    public void Art_larger_than_one_admitted_read_is_joined_from_bounded_chunks()
    {
        List<string> releases = [];
        ContentFake content = new(releases);
        // An artifact past the Engine's per-read bound: the reader has to ask more than once and the
        // bytes it publishes have to be the whole artifact, not the first slice.
        byte[] large = new byte[(1024 * 1024) + 7];
        for (int index = 0; index < large.Length; index++) large[index] = (byte)(index % 251);
        byte[] chrome = [1, 2, 3, 4];
        const string Screen = "worldrpg/media/ui/screen-death.png";
        const string Chrome = "worldrpg/media/ui/window-character-sheet-chrome.png";
        byte[] skin = [9, 8, 7];
        byte[] titlebar = [6, 5, 4];
        byte[] slot = [3, 2, 1];
        const string Skin = "worldrpg/media/ui/authored/inventory-skin-panel-slate-v1.png";
        const string Titlebar = "worldrpg/media/ui/authored/inventory-titlebar-slate-v1.png";
        const string Slot = "worldrpg/media/ui/authored/inventory-grid-slot-slate-v1.png";
        content.Add(Screen, large);
        content.Add(Chrome, chrome);
        content.Add(Skin, skin);
        content.Add(Titlebar, titlebar);
        content.Add(Slot, slot);
        // The supplied screens are served from the published group rather than fabricated: this fixture
        // states a minimal inventory, so every identity the art set carries has to be present here, and
        // reading the real artifacts keeps its entries honest about their bytes and digests.
        string[] supplied = ["screen-character-generation", "screen-pick-02", "screen-prison", "screen-start-menu", "screen-title"];
        Dictionary<string, (string Path, byte[] Bytes)> published = [];
        foreach (string name in supplied)
        {
            string path = $"worldrpg/media/ui/{name}.png";
            byte[] bytes = File.ReadAllBytes(Path.Combine(RepositoryRoot(), "content", "worldrpg", "media", "ui", $"{name}.png"));
            published[name] = (path, bytes);
            content.Add(path, bytes);
        }
        content.Add(DaggerfallUiArt.InventoryPath, Encoding.UTF8.GetBytes(new JsonObject
        {
            ["generator"] = "fixture",
            ["artifacts"] = new JsonArray(
                Entry("screen.death", Screen, large),
                Entry("window.character-sheet.chrome", Chrome, chrome),
                Entry("inventory.skin.panel-slate.v1", Skin, skin),
                Entry("inventory.skin.titlebar-slate.v1", Titlebar, titlebar),
                Entry("inventory.skin.grid-slot-slate.v1", Slot, slot),
                Entry("screen.character-generation", published["screen-character-generation"].Path, published["screen-character-generation"].Bytes),
                Entry("screen.pick.02", published["screen-pick-02"].Path, published["screen-pick-02"].Bytes),
                Entry("screen.prison", published["screen-prison"].Path, published["screen-prison"].Bytes),
                Entry("screen.start-menu", published["screen-start-menu"].Path, published["screen-start-menu"].Bytes),
                Entry("screen.title", published["screen-title"].Path, published["screen-title"].Bytes)),
        }.ToJsonString()));

        DaggerfallUiArt art = DaggerfallUiArt.Read(content, []);
        Assert.Equal($"data:image/png;base64,{Convert.ToBase64String(large)}", art.Images.Single(image => image.Id == "screen.death").Image);
        Assert.Equal(2, content.Reads(Screen));
        Assert.Equal(1, content.Reads(Chrome));
        Assert.Equal(1, content.Reads(Skin));

        static JsonObject Entry(string mediaId, string path, byte[] bytes) => new()
        {
            ["path"] = path,
            ["byteLength"] = bytes.Length,
            ["sha256"] = Convert.ToHexStringLower(SHA256.HashData(bytes)),
            ["mediaId"] = mediaId,
        };
    }

    [Fact]
    public void Art_the_dom_holds_travels_once_and_comes_back_when_the_dom_asks_for_it()
    {
        string root = RepositoryRoot();
        List<string> releases = [];
        ContentFake content = new(releases);
        PrivateersHoldInputs inputs = ReadInputs(root);
        PopulateContent(content, inputs);
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        EngineContextFake engine = EngineContextFake.Create(content, SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases).Service, new AppearanceFake(releases));

        using DaggerfallSession session = new(engine.Context, definitions, inputs, DaggerfallTuning.Defaults);
        session.PublishInitial();
        Dictionary<string, object?> first = Assert.IsType<Dictionary<string, object?>>(engine.Published());
        string revision = Assert.IsType<string>(first["uiArtRevision"]);
        Assert.Contains("uiArt", first.Keys);

        // The block does not ride every admitted update: it is worth hundreds of kilobytes.
        session.Update(new ProductUpdate(OuterUpdate(2), [Input(InputEventKind.DirectDigital)]));
        Dictionary<string, object?> second = Assert.IsType<Dictionary<string, object?>>(engine.Published());
        Assert.DoesNotContain("uiArt", second.Keys);
        Assert.Equal(revision, second["uiArtRevision"]);

        // A newly attached client needs the art even while the title screen holds gameplay.
        session.PublishInitial();
        Dictionary<string, object?> attached = Assert.IsType<Dictionary<string, object?>>(engine.Published());
        Assert.Contains("uiArt", attached.Keys);
        Assert.Equal(revision, attached["uiArtRevision"]);

        // A DOM that reloaded holds nothing and names the revision it is missing.
        ProductInputEvent ask = Input(InputEventKind.DirectDigital) with
        {
            ValueKind = InputValueKind.ProductPayload,
            PayloadContract = "dagger.ui.action.v1"u8.ToArray(),
            PayloadData = Encoding.UTF8.GetBytes($"{{\"action\":\"art-request\",\"revision\":\"{revision}\"}}"),
        };
        int before = engine.PublishedHistory().Count;
        session.Update(new ProductUpdate(OuterUpdate(3), [ask]));
        // An admitted update publishes per step and once more at its end, so the block is answered by
        // whichever snapshot follows the request rather than necessarily by the update's last one.
        Assert.Contains(engine.PublishedHistory().Skip(before), snapshot =>
            snapshot is Dictionary<string, object?> fields
            && fields.ContainsKey("uiArt")
            && string.Equals(Assert.IsType<string>(fields["uiArtRevision"]), revision, StringComparison.Ordinal));
    }

    [Fact]
    public void An_artifact_the_published_inventory_does_not_describe_refuses_the_session_by_name()
    {
        string root = RepositoryRoot();
        List<string> releases = [];
        ContentFake content = new(releases);
        PrivateersHoldInputs inputs = ReadInputs(root);
        PopulateContent(content, inputs);
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        EngineContextFake engine = EngineContextFake.Create(content, SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases).Service, new AppearanceFake(releases));

        // A group whose inventory no longer describes the death screen must refuse to start rather
        // than run with a blank screen, and the refusal has to name what was looked for and where.
        JsonNode inventory = JsonNode.Parse(File.ReadAllBytes(Path.Combine(root, "content", DaggerfallUiArt.InventoryPath)))!;
        JsonArray artifacts = inventory["artifacts"]!.AsArray();
        for (int index = artifacts.Count - 1; index >= 0; index--)
        {
            if (string.Equals(artifacts[index]!["mediaId"]?.GetValue<string>(), "screen.death", StringComparison.Ordinal)) artifacts.RemoveAt(index);
        }

        content.Add(DaggerfallUiArt.InventoryPath, Encoding.UTF8.GetBytes(inventory.ToJsonString()));
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
            () => new DaggerfallSession(engine.Context, definitions, inputs, DaggerfallTuning.Defaults));
        Assert.Contains("screen.death", failure.Message, StringComparison.Ordinal);
        Assert.Contains(DaggerfallUiArt.InventoryPath, failure.Message, StringComparison.Ordinal);
    }

    private static void PopulateContent(ContentFake content, PrivateersHoldInputs inputs)
    {
        content.Add(inputs.SpatialArtifact.Path, inputs.SpatialArtifact.Sha256);
        content.Add(inputs.StaticMesh.Path, inputs.StaticMesh.Sha256);
        foreach (NormalizedMaterial material in inputs.Materials) content.Add(material.TexturePath, material.TextureSha256);
        foreach (NormalizedActorSprite sprite in inputs.ActorSprites.Values)
        {
            content.Add(sprite.TexturePath, sprite.TextureSha256);
            if (sprite.Corpse is { } corpse) content.Add(corpse.TexturePath, corpse.TextureSha256);
        }
        foreach (NormalizedAudioClip clip in inputs.Audio) content.Add(clip.Path, clip.Sha256);
        foreach (NormalizedClassicEffect effect in inputs.ClassicPresentation.Effects) content.Add(effect.TexturePath, effect.TextureSha256);
        foreach (NormalizedClassicWeapon weapon in inputs.ClassicPresentation.Weapons.Values) content.Add(weapon.TexturePath, weapon.TextureSha256);
        // The session resolves the DOM's art by media identity through the published group inventory,
        // so this fake serves the real group: its generated inventory and every artifact it describes.
        foreach ((string path, byte[] bytes) in PublishedUiArt(RepositoryRoot())) content.Add(path, bytes);
    }

    /// <summary>The published UI art group as admitted content: the inventory and the artifacts it names.</summary>
    private static IEnumerable<(string Path, byte[] Bytes)> PublishedUiArt(string root)
    {
        string inventoryPath = Path.Combine(root, "content", DaggerfallUiArt.InventoryPath);
        byte[] inventory = File.ReadAllBytes(inventoryPath);
        yield return (DaggerfallUiArt.InventoryPath, inventory);
        using JsonDocument document = JsonDocument.Parse(inventory);
        foreach (JsonElement artifact in document.RootElement.GetProperty("artifacts").EnumerateArray())
        {
            string path = artifact.GetProperty("path").GetString()!;
            yield return (path, File.ReadAllBytes(Path.Combine(root, "content", path)));
        }
    }

    private static ContentSha256 Digest(ReadOnlySpan<byte> bytes)
    {
        byte[] hash = SHA256.HashData(bytes);
        return new ContentSha256(
            BinaryPrimitives.ReadUInt64BigEndian(hash.AsSpan(0, 8)),
            BinaryPrimitives.ReadUInt64BigEndian(hash.AsSpan(8, 8)),
            BinaryPrimitives.ReadUInt64BigEndian(hash.AsSpan(16, 8)),
            BinaryPrimitives.ReadUInt64BigEndian(hash.AsSpan(24, 8)));
    }

    private static ContentFake MediaContent(List<string> releases)
    {
        ContentFake content = new(releases);
        content.Add("mesh/hold.json", Hash);
        content.Add("sprite/enemy.png", Hash);
        content.Add("audio/swing.wav", Hash);
        content.Add("audio/hit.wav", Hash);
        content.Add("audio/hit2.wav", Hash);
        content.Add("audio/hit3.wav", Hash);
        content.Add("audio/hit4.wav", Hash);
        content.Add("audio/hit5.wav", Hash);
        content.Add("effect/blood0.png", Hash);
        content.Add("effect/blood1.png", Hash);
        content.Add("effect/blood2.png", Hash);
        content.Add("effect/sparkle.png", Hash);
        return content;
    }

    private static PrivateersHoldInputs MediaInputs(int primaryChance = 50, IReadOnlyList<int>? primaryFrames = null, bool includeAlternate = true, bool directional = false, IReadOnlyList<NormalizedAudioClip>? audio = null, string? preferredRestState = null, NormalizedClassicPresentation? classic = null, IReadOnlyList<NormalizedAtlasFrame>? actorFrames = null, IReadOnlyList<int>? rangedFrames = null)
    {
        NormalizedSpriteState idle = new("idle", [0], 10F, true)
        {
            Orientations = directional
                ? Enumerable.Range(0, 8).ToDictionary(sector => sector, sector => (IReadOnlyList<uint>)(sector == 6 ? [1] : [0]))
                : new Dictionary<int, IReadOnlyList<uint>>(),
        };
        NormalizedSpriteState move = new("move", [0], 10F, true);
        NormalizedSpriteState hurt = new("hurt", [1], 10F, false);
        NormalizedSpriteState attack = new("primaryAttack", [2, 3], 10F, false)
        {
            Orientations = directional
                ? Enumerable.Range(0, 8).ToDictionary(sector => sector, sector => (IReadOnlyList<uint>)(sector == 6 ? [3, 2] : [2, 3]))
                : new Dictionary<int, IReadOnlyList<uint>>(),
        };
        Dictionary<string, NormalizedSpriteState> states = new()
        {
            [idle.Name] = idle,
            [move.Name] = move,
            [hurt.Name] = hurt,
            [attack.Name] = attack,
        };
        if (preferredRestState is not null && !states.ContainsKey(preferredRestState)) states.Add(preferredRestState, new(preferredRestState, [0], 10F, true));
        if (rangedFrames is not null) states.Add("rangedAttack1", new("rangedAttack1", [0, 1, 2, 3], 10F, false));
        NormalizedActorSprite sprite = new("sprite/enemy.png", Hash, 32, 32,
            actorFrames ?? [new NormalizedAtlasFrame(0, 0, 0, 8, 8), new NormalizedAtlasFrame(1, 8, 0, 8, 8), new NormalizedAtlasFrame(2, 16, 0, 8, 8), new NormalizedAtlasFrame(3, 24, 0, 8, 8)],
            0, new Vector2(.5F, 0F), Vector2.One)
        {
            States = states,
            PreferredRestState = preferredRestState,
            AttackSequences = includeAlternate
                ? [new NormalizedAttackSequence(100 - primaryChance, primaryFrames ?? [0]), new NormalizedAttackSequence(primaryChance, [1])]
                : [new NormalizedAttackSequence(100, primaryFrames ?? [0])],
            RangedAttackSequence = rangedFrames is null ? null : new NormalizedAttackSequence(100, rangedFrames, "rangedAttack1"),
        };
        return new PrivateersHoldInputs(
            new ProjectFacts(null, new Dictionary<long, AuthoredActor>()),
            new SpatialContentArtifact("spatial/hold.json", Hash, 1),
            new ContentArtifact("mesh/hold.json", Hash),
            new AuthoredWorldAppearance(new Color(1, 1, 1, 1), new Transform(Vector3.Zero, Quaternion.Identity, Vector3.One), true, RenderLayer.Scene),
            new PlayerInitialLook(0, 0),
            [],
            new Dictionary<long, NormalizedActorSprite> { [11] = sprite },
            audio ??
            [
                new NormalizedAudioClip("swing", "audio/swing.wav", Hash),
                new NormalizedAudioClip("hit1", "audio/hit.wav", Hash),
                new NormalizedAudioClip("hit2", "audio/hit2.wav", Hash),
                new NormalizedAudioClip("hit3", "audio/hit3.wav", Hash),
                new NormalizedAudioClip("hit4", "audio/hit4.wav", Hash),
                new NormalizedAudioClip("hit5", "audio/hit5.wav", Hash),
            ],
            classic);
    }

    private static NormalizedClassicPresentation ClassicEffects(IReadOnlyList<Vector2>? bloodDisplaySizes = null)
    {
        IReadOnlyList<NormalizedAtlasFrame> frames = [new NormalizedAtlasFrame(0, 0, 0, 8, 8)];
        Vector2 Size(int sourceRecordOrdinal) => sourceRecordOrdinal < 3 && bloodDisplaySizes is { Count: 3 } ? bloodDisplaySizes[sourceRecordOrdinal] : Vector2.One;
        NormalizedClassicEffect Effect(string name, int sourceRecordOrdinal, string path) => new(name, sourceRecordOrdinal, path, Hash, 8, 8, frames, new Vector2(.5F, .5F), Size(sourceRecordOrdinal), [0], 10F, false);
        return new NormalizedClassicPresentation(new Dictionary<string, NormalizedClassicWeapon>(), [Effect("blood0", 0, "effect/blood0.png"), Effect("blood1", 1, "effect/blood1.png"), Effect("blood2", 2, "effect/blood2.png"), Effect("magicSparkle", 3, "effect/sparkle.png")]);
    }

    private static NormalizedClassicPresentation ClassicWeapon()
    {
        IReadOnlyList<NormalizedAtlasFrame> frames = [new NormalizedAtlasFrame(0, 0, 0, 8, 8)];
        string[] names = ["idle", "strikeDown", "strikeDownLeft", "strikeLeft", "strikeRight", "strikeDownRight", "strikeUp"];
        IReadOnlyDictionary<string, NormalizedClassicWeaponAction> actions = names.Select((name, sourceRecordOrdinal) => new NormalizedClassicWeaponAction(name, sourceRecordOrdinal, 0, 1, "right", name == "idle" ? .1F : .4F, 10F, name == "idle", 0, 0)).ToDictionary(action => action.Name);
        return new NormalizedClassicPresentation(new Dictionary<string, NormalizedClassicWeapon> { ["weapon.dagger.steel"] = new("weapon.dagger.steel", "weapon/dagger.png", Hash, 8, 8, frames, new Vector2(.5F, .5F), Vector2.One, [0], actions) }, [])
        {
            CompatibleItemVisuals = new Dictionary<string, string> { ["iron-dagger"] = "weapon.dagger.steel" },
            Viewmodel = new ClassicViewmodelStyle(0),
        };
    }

    private static PrivateersHoldAppearance.ActorVisual Visual(PrivateersHoldAppearance presentation)
    {
        FieldInfo field = typeof(PrivateersHoldAppearance).GetField("actors", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return ((Dictionary<long, PrivateersHoldAppearance.ActorVisual>)field.GetValue(presentation)!)[11];
    }

    private static ActorsState EmptyActors()
    {
        ActorsState actors = new();
        actors.CreatePlayer(99, new EntityTypeId("player"), new StatsComponent(), "health");
        return actors;
    }
    private static WorldRpg.Kit.Inventory.EquipmentRead RightHand(string itemId) => new([new WorldRpg.Kit.Inventory.EquipmentAssignment(new WorldRpg.Kit.Inventory.EquipmentSlotId("right-hand"), new WorldRpg.Kit.Inventory.UniqueInventoryItem(1, new WorldRpg.Kit.Inventory.InventoryItemId(itemId)))], 1, 1);
    private static ActorsState ActorsAt(WorldPoint point) => ActorsWithNpc(12, HealthyMechanics(), point);

    private static ActorsState ActorsWithNpc(long durableId, StatsComponent stats, WorldPoint point)
    {
        ActorsState actors = new();
        actors.CreatePlayer(DaggerfallActorIdentity.PlayerEntityId, new EntityTypeId("player"), new StatsComponent(), "health");
        actors.CreateActor(durableId, new EntityTypeId("test"), stats, new ActorPose(point, 0f), "health");
        return actors;
    }

    private static StatsComponent HealthyMechanics()
    {
        Stat maximum = new(100);
        StatsComponent stats = new();
        stats.AddStat(StatId.Parse("health-maximum"), maximum);
        stats.AddTrack(TrackId.Parse("health"), new Track(maximum, 100));
        return stats;
    }

    private static DaggerfallCharacterState CharacterForLoot(DaggerfallDefinitions definitions)
    {
        DaggerfallActorDefinition player = definitions.RequireActor(new DaggerfallActorId("player"));
        DaggerfallCareerDefinition career = definitions.Catalogs.RequireCareer(player.Career!);
        StatsComponent stats = new DaggerfallMechanicsState().CreateStats(player, DaggerfallPlayerVitals.Initial(player.Stats, career));
        return new DaggerfallCharacterState(definitions, stats, player);
    }
    private static int EffectCount(PrivateersHoldAppearance presentation) => ((List<PrivateersHoldAppearance.EffectVisual>)typeof(PrivateersHoldAppearance).GetField("effects", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(presentation)!).Count;
    private static PrivateersHoldAppearance.EffectVisual Effect(PrivateersHoldAppearance presentation) => Assert.Single((List<PrivateersHoldAppearance.EffectVisual>)typeof(PrivateersHoldAppearance).GetField("effects", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(presentation)!);
    private static PrivateersHoldAppearance.ViewmodelVisual Viewmodel(PrivateersHoldAppearance presentation) => Assert.IsType<PrivateersHoldAppearance.ViewmodelVisual>(typeof(PrivateersHoldAppearance).GetField("viewmodel", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(presentation));

    [Fact]
    public void An_impact_whose_target_was_defeated_after_the_decision_produces_no_hit_or_miss_fact()
    {
        string root = RepositoryRoot();
        PrivateersHoldInputs inputs = ReadInputs(root);
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        PerceptionFake perception = PerceptionFake.Create();
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), perception.Service);
        using DaggerfallSession session = new(engine.Context, definitions, inputs, DaggerfallTuning.Defaults);
        using SpatialMovementSystem targetingSpatial = new(spatial.Service, content, inputs.SpatialArtifact, DaggerfallTuning.Defaults.Spatial);
        Dictionary<long, DaggerfallActorDefinition> authored = inputs.Project.Actors.Values.ToDictionary(
            placement => placement.EntityId,
            placement => definitions.RequireActor(placement.ActorId));
        authored[DaggerfallActorIdentity.PlayerEntityId] = definitions.RequireActor(new DaggerfallActorId("player"));
        TargetingService targeting = new(perception.Service, targetingSpatial, session.State.Actors, new DaggerTargetingPolicy(authored, DaggerfallTuning.Defaults.MeleeTargeting));
        DaggerCombatRules combat = new(RandomMinimum.Create(), session.State.Actors, session.State.Equipment, session.State.InventoryFor,
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

    [Fact]
    public void A_ranged_shot_draws_one_arrow_from_the_shooter_s_authored_quiver()
    {
        string root = RepositoryRoot();
        PrivateersHoldInputs inputs = ReadInputs(root);
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        PerceptionFake perception = PerceptionFake.Create();
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), perception.Service);
        using DaggerfallSession session = new(engine.Context, definitions, inputs, DaggerfallTuning.Defaults);
        using SpatialMovementSystem targetingSpatial = new(spatial.Service, content, inputs.SpatialArtifact, DaggerfallTuning.Defaults.Spatial);
        Dictionary<long, DaggerfallActorDefinition> authored = inputs.Project.Actors.Values.ToDictionary(
            placement => placement.EntityId,
            placement => definitions.RequireActor(placement.ActorId));
        authored[DaggerfallActorIdentity.PlayerEntityId] = definitions.RequireActor(new DaggerfallActorId("player"));
        TargetingService targeting = new(perception.Service, targetingSpatial, session.State.Actors, new DaggerTargetingPolicy(authored, DaggerfallTuning.Defaults.MeleeTargeting));
        DaggerCombatRules combat = new(RandomMinimum.Create(), session.State.Actors, session.State.Equipment, session.State.InventoryFor,
            session.State.ItemInstances, definitions, authored, targeting);
        FactBuffer<IProductFact> facts = new();
        long archer = Assert.Single(inputs.Project.Actors.Values, placement => placement.ActorId == new DaggerfallActorId("archer")).EntityId;
        WorldRpg.Kit.Inventory.MechanicsInventoryCoordinator quiver = Assert.IsType<WorldRpg.Kit.Inventory.MechanicsInventoryCoordinator>(session.State.InventoryFor(archer));

        // The pack authors the archer's quiver; the managed inventory carries it.
        InventoryStackId arrowStack = quiver.Read().Stacks.Single(stack => stack.Definition.Value == "arrow").Id;
        Assert.Equal(12UL, quiver.Read().Stacks.Single(stack => stack.Id == arrowStack).Quantity);
        quiver.Consume(new WorldRpg.Kit.Inventory.InventoryConsume(arrowStack, 11));

        Assert.True(combat.Attacks.TryBeginEnemyAttack(archer, DaggerfallActorIdentity.PlayerEntityId, 77, 400, .125, facts));
        Assert.DoesNotContain(quiver.Read().Stacks, stack => stack.Id == arrowStack);
        Assert.Throws<InvalidOperationException>(() => session.State.ItemInstances.RequireStack(DaggerfallItemOwner.Actor(archer), arrowStack));
        List<IProductFact> decided = [];
        facts.Deliver(decided.Add);
        Assert.Contains(decided, fact => fact is EnemyAttackStartedFact);
    }

    [Fact]
    public void An_archer_with_no_arrows_refuses_the_shot_and_reports_it_instead_of_missing()
    {
        string root = RepositoryRoot();
        PrivateersHoldInputs inputs = ReadInputs(root);
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        PerceptionFake perception = PerceptionFake.Create();
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), perception.Service);
        using DaggerfallSession session = new(engine.Context, definitions, inputs, DaggerfallTuning.Defaults);
        using SpatialMovementSystem targetingSpatial = new(spatial.Service, content, inputs.SpatialArtifact, DaggerfallTuning.Defaults.Spatial);
        Dictionary<long, DaggerfallActorDefinition> authored = inputs.Project.Actors.Values.ToDictionary(
            placement => placement.EntityId,
            placement => definitions.RequireActor(placement.ActorId));
        authored[DaggerfallActorIdentity.PlayerEntityId] = definitions.RequireActor(new DaggerfallActorId("player"));
        TargetingService targeting = new(perception.Service, targetingSpatial, session.State.Actors, new DaggerTargetingPolicy(authored, DaggerfallTuning.Defaults.MeleeTargeting));
        DaggerCombatRules combat = new(RandomMinimum.Create(), session.State.Actors, session.State.Equipment, session.State.InventoryFor,
            session.State.ItemInstances, definitions, authored, targeting);
        FactBuffer<IProductFact> facts = new();
        long archer = Assert.Single(inputs.Project.Actors.Values, placement => placement.ActorId == new DaggerfallActorId("archer")).EntityId;
        WorldRpg.Kit.Inventory.MechanicsInventoryCoordinator archerInventory = Assert.IsType<WorldRpg.Kit.Inventory.MechanicsInventoryCoordinator>(session.State.InventoryFor(archer));
        archerInventory.Consume(new WorldRpg.Kit.Inventory.InventoryConsume(
            archerInventory.Read().Stacks.Single(stack => stack.Definition.Value == "arrow").Id, 12));
        double playerHealthBefore = session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).Current;

        Assert.False(combat.Attacks.TryBeginEnemyAttack(archer, DaggerfallActorIdentity.PlayerEntityId, 77, 400, .125, facts));
        List<IProductFact> decided = [];
        facts.Deliver(decided.Add);
        AttackRejectedFact rejection = Assert.Single(decided.OfType<AttackRejectedFact>());
        Assert.Equal(AttackRejection.EmptyQuiver, rejection.Reason);
        Assert.Equal(archer, rejection.ActorId);
        Assert.DoesNotContain(decided, fact => fact is EnemyAttackStartedFact);

        // No pending impact exists behind the refusal, so the player is never damaged by a
        // shot that was never made, and every later attempt is refused the same way.
        Assert.Equal(playerHealthBefore, session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).Current);
        Assert.False(combat.Attacks.TryBeginEnemyAttack(archer, DaggerfallActorIdentity.PlayerEntityId, 78, 401, .125, facts));
        Assert.Equal(playerHealthBefore, session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).Current);
    }

    [Fact]
    public void A_restored_session_keeps_the_quiver_count_its_save_carried()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake sourceContent = new(releases);
        PopulateContent(sourceContent, inputs);
        SpatialFake sourceSpatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake source = EngineContextFake.Create(sourceContent, sourceSpatial.Service, new AppearanceFake(releases));
        long archer = Assert.Single(inputs.Project.Actors.Values, placement => placement.ActorId == new DaggerfallActorId("archer")).EntityId;
        RulesetSavePayload payload;
        using (DaggerfallSession original = new(source.Context, definitions, inputs, DaggerfallTuning.Defaults))
        {
            // One drawn arrow leaves eleven; the save must carry exactly that, not a refill.
            WorldRpg.Kit.Inventory.MechanicsInventoryCoordinator archerInventory = Assert.IsType<WorldRpg.Kit.Inventory.MechanicsInventoryCoordinator>(original.State.InventoryFor(archer));
            archerInventory.Consume(new WorldRpg.Kit.Inventory.InventoryConsume(
                archerInventory.Read().Stacks.Single(stack => stack.Definition.Value == "arrow").Id, 1));
            payload = original.CaptureSave();
        }

        Assert.Equal(11UL, DaggerfallSavePayload.Read(payload).ActorInventories
            .Single(section => section.EntityId == archer).Inventory.Stacks.Single(stack => stack.ItemId == "arrow").Quantity);

        ContentFake resumedContent = new(releases);
        PopulateContent(resumedContent, inputs);
        SpatialFake resumedSpatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake resumedEngine = EngineContextFake.Create(resumedContent, resumedSpatial.Service, new AppearanceFake(releases));
        ResolvedCompositionIdentity identity = GameCompositionResolver.Resolve(FullContent(root), new GameBundleId("daggerfall.privateers-hold")).RequireComposition().Identity;
        using DaggerfallSession resumed = DaggerfallSession.Restore(resumedEngine.Context, identity, definitions, inputs, DaggerfallTuning.Defaults, payload, RandomMinimum.Create());

        Assert.Equal(11UL, Assert.IsType<WorldRpg.Kit.Inventory.MechanicsInventoryCoordinator>(resumed.State.InventoryFor(archer)).Read().Stacks.Single(stack => stack.Definition.Value == "arrow").Quantity);
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
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs inputs = ReadInputs(root);
        ResolvedCompositionIdentity composition = GameCompositionResolver.Resolve(FullContent(root), new GameBundleId("daggerfall.privateers-hold")).RequireComposition().Identity;
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
        using DaggerfallSession resumed = DaggerfallSession.Restore(engine.Context, composition, definitions, inputs, DaggerfallTuning.Defaults, DaggerfallSavePayload.Encode(saved), RandomMinimum.Create());
        long resumedHealth = PlayerHealth(resumed);

        resumedAppearance.AdvanceReceiptForAll = CrossedMarker(1);
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
        appearance.AdvanceReceipts.Enqueue(new SpritePlaybackAdvanceLeaseReceipt(
            new[]
            {
                new SpritePlaybackMarkerCrossing(1, 3, 1, 0, 1),
                new SpritePlaybackMarkerCrossing(1, 3, 1, 0, 2),
            },
            new SpritePlaybackReadout(3, 1, SpritePlaybackState.Playing, 0D, 0, 2, false),
            true));
        using PrivateersHoldAppearance presentation = new(content, appearance, MediaInputs(primaryFrames: [0, -1, 1, -1, 0]), audio.Service);

        presentation.React(new EnemyAttackStartedFact(11, 12, true, 3, 4));
        presentation.Advance(OuterUpdate(1));

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
        using PrivateersHoldAppearance presentation = new(content, appearance, MediaInputs(), audio.Service);

        presentation.React(new EnemyAttackStartedFact(11, 12, true, 3, 4));

        AttackImpactNotice impact = Assert.Single(presentation.TakeAttackImpacts());
        Assert.False(impact.Expired);
    }

    [Fact]
    public void An_enemy_that_loses_reach_before_the_damage_frame_cancels_its_swing()
    {
        List<string> releases = [];
        (DaggerfallSession session, AppearanceFake appearance, PerceptionFake perception) = VisibleEnemySession(releases);
        using DaggerfallSession disposable = session;
        long healthBefore = PlayerHealth(session);
        session.Update(new ProductUpdate(OuterUpdate(1), []));

        // Still visible, but beyond the authored reach: the behaviour chases instead
        // of attacking, which cancels the swing already in flight.
        perception.Receipt = Receipt(new PerceptionPair(2000, 1, 5d, 1d, PerceptionPairKind.Visible, 5d));
        appearance.AdvanceReceiptForAll = CrossedMarker(1);
        session.Update(new ProductUpdate(OuterUpdate(2), []));

        Assert.Equal(EnemyBehaviorState.Chase, session.LastEnemyBehavior[2000].State);
        Assert.Equal(healthBefore, PlayerHealth(session));
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
        appearance.AdvanceReceiptForAll = CrossedMarker(1);
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
        appearance.AdvanceReceiptForAll = new SpritePlaybackAdvanceLeaseReceipt(
            Array.Empty<SpritePlaybackMarkerCrossing>(),
            new SpritePlaybackReadout(3, 1, SpritePlaybackState.Completed, 0D, 0, 3, true),
            true);
        session.Update(new ProductUpdate(OuterUpdate(2), []));
        Assert.Equal(healthBefore, PlayerHealth(session));

        // A crossing arriving afterwards cannot land the expired swing.
        appearance.AdvanceReceiptForAll = CrossedMarker(1);
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
        appearance.AdvanceReceipts.Enqueue(new SpritePlaybackAdvanceLeaseReceipt(
            Array.Empty<SpritePlaybackMarkerCrossing>(),
            new SpritePlaybackReadout(3, 1, SpritePlaybackState.Completed, 0D, 0, 1, true),
            false));
        appearance.AdvanceReceipts.Enqueue(new SpritePlaybackAdvanceLeaseReceipt(
            new[] { new SpritePlaybackMarkerCrossing(1, 3, 1, 0, 1) },
            new SpritePlaybackReadout(3, 1, SpritePlaybackState.Playing, 0D, 0, 2, false),
            true));
        using PrivateersHoldAppearance presentation = new(content, appearance, MediaInputs(primaryFrames: [0, -1, 1]), audio.Service);
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
    public void A_swing_that_ends_without_reaching_its_damage_frame_reports_an_expired_impact()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        AppearanceFake appearance = new(releases);
        AudioRecorder audio = AudioRecorder.Create();
        appearance.AdvanceReceipts.Enqueue(new SpritePlaybackAdvanceLeaseReceipt(
            Array.Empty<SpritePlaybackMarkerCrossing>(),
            new SpritePlaybackReadout(3, 1, SpritePlaybackState.Completed, 0D, 0, 1, true),
            true));
        using PrivateersHoldAppearance presentation = new(content, appearance, MediaInputs(primaryFrames: [0, -1, 1]), audio.Service);

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
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        PerceptionFake perception = PerceptionFake.Create();
        perception.Receipt = Receipt(new PerceptionPair(2000, 1, 1d, 1d, PerceptionPairKind.Visible, 1d));
        AppearanceFake appearance = new(releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, appearance, perception.Service);
        using DaggerfallSession session = new(engine.Context, definitions, inputs, DaggerfallTuning.Defaults);
        double healthBefore = session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).Current;

        // The update that decides the swing carries no damage: nothing has been struck
        // until the authored damage frame is reached.
        session.Update(new ProductUpdate(OuterUpdate(1), []));
        Assert.Equal(EnemyBehaviorState.Attack, session.LastEnemyBehavior[2000].State);
        Assert.Equal(healthBefore, session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).Current);

        appearance.AdvanceReceiptForAll = CrossedMarker(1);
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
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        PerceptionFake perception = PerceptionFake.Create();
        perception.Receipt = Receipt(new PerceptionPair(2000, 1, 1d, 1d, PerceptionPairKind.Visible, 1d));
        AppearanceFake appearance = new(releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, appearance, perception.Service);
        using DaggerfallSession session = new(engine.Context, definitions, inputs, DaggerfallTuning.Defaults);
        double healthBefore = session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).Current;

        session.Update(new ProductUpdate(OuterUpdate(1), []));
        // Losing sight cancels the swing; a crossing from the already-playing animation
        // must not land the strike the attacker is no longer making.
        perception.Receipt = Receipt(new PerceptionPair(2000, 1, 1d, 0d, PerceptionPairKind.Occluded, 0d));
        appearance.AdvanceReceiptForAll = CrossedMarker(1);
        session.Update(new ProductUpdate(OuterUpdate(2), []));

        Assert.Equal(EnemyBehaviorState.Idle, session.LastEnemyBehavior[2000].State);
        Assert.Equal(healthBefore, session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).Current);
    }

    /// <summary>
    /// The entry-screen mode the title screen owns: while it holds, no world time and no gameplay input
    /// reach the session, it asks the product for nothing on its own, and the projection names it so the
    /// thin UI knows which screen the mode is showing.
    /// </summary>
    [Fact]
    public void The_entry_screen_mode_holds_the_world_and_names_itself_on_the_wire()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        using DaggerfallSession session = new(engine.Context, definitions, inputs, DaggerfallTuning.Defaults);

        // The product decides the mode, which is what the session applies.
        session.ApplyProductMode(ProductMode.Title);
        Assert.Equal(ProductMode.Title, session.Mode);

        // The screen is up, so the world is not: an admitted update with held movement in it takes no
        // step, and the projection still publishes so the screen can be read.
        int stepsBefore = spatial.StepCalls;
        session.Update(new ProductUpdate(OuterUpdate(1), [Input(InputEventKind.Key, InputEdge.Pressed, keyboard: KeyboardControl.KeyW)]));
        Assert.Equal(stepsBefore, spatial.StepCalls);
        Assert.Equal("title", engine.PublishedField("mode"));

        // The session asks the product for nothing while the entry screen holds: it has no loot container
        // to follow, and the mode is the product's own decision rather than one it reports. (The case with
        // a container open is the modal test's, which has the corpse this fixture does not: a container
        // cannot be opened from a world that has not started, so the state it protects is unreachable
        // here and is covered where it is reachable.)
        Assert.Null(session.PendingModeRequest);

        // A gameplay action read while the entry screen holds is dropped rather than acted on, so a key
        // pressed during the screen cannot land in the world behind it.
        ProductInputEvent loot = Input(InputEventKind.DirectDigital) with
        {
            ValueKind = InputValueKind.ProductPayload,
            PayloadContract = "dagger.ui.action.v1"u8.ToArray(),
            PayloadData = Encoding.UTF8.GetBytes("""{"action":"loot"}"""),
        };
        session.Update(new ProductUpdate(OuterUpdate(2), [loot]));
        Assert.Null(session.OpenLoot);
        Assert.Equal(ProductMode.Title, session.Mode);
        Assert.Equal(stepsBefore, spatial.StepCalls);

        // A status line would compete with the screen that is up, so the entry screen says nothing.
        Assert.Equal(string.Empty, engine.PublishedField("lastOutcome"));

        // A slice carrying the entry screen's own action is a shape this knows and does not act on rather
        // than an unrecognized one: the product answers it, and reporting it over the screen that asked
        // would put a message on the entry screen for doing the one thing the entry screen does.
        ProductInputEvent begin = Input(InputEventKind.DirectDigital) with
        {
            ValueKind = InputValueKind.ProductPayload,
            PayloadContract = "dagger.ui.action.v1"u8.ToArray(),
            PayloadData = Encoding.UTF8.GetBytes("""{"action":"begin"}"""),
        };
        session.Update(new ProductUpdate(OuterUpdate(3), [begin]));
        Assert.DoesNotContain("Unrecognized", engine.PublishedField("lastOutcome"), StringComparison.Ordinal);
        Assert.Equal(ProductMode.Title, session.Mode);
        Assert.Equal(stepsBefore, spatial.StepCalls);

        // Leaving the entry screen is the product's transition, and ordinary play resumes under it.
        session.ApplyProductMode(ProductMode.Playing);
        Assert.Equal("playing", engine.PublishedField("mode"));
        session.Update(new ProductUpdate(OuterUpdate(4), []));
        Assert.True(spatial.StepCalls > stepsBefore, "ordinary play admits world time again");
    }

    /// <summary>A session whose one placed enemy can see the player at the given distance.</summary>
    [Fact]
    public void Product_modes_gate_gameplay_input_and_world_time_while_the_modal_keeps_acting()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        spatial.KeepPosition = true;
        PerceptionFake perception = PerceptionFake.Create();
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), perception.Service);
        using DaggerfallSession session = new(engine.Context, definitions, inputs, DaggerfallTuning.Defaults);
        session.State.Actors.Get(2000).Stats.GetTrack(TrackId.Parse("health")).SetCurrent(1, clamp: true);
        session.ResolveExplicitMelee(new ExplicitMeleeRequest(1, 2000, 1, 1, .125));
        AimActivationAt(session, 2000);
        CorpseContainer corpse = session.Corpses[2000];
        Assert.True(corpse.IsRegistered);
        session.State.Containers.Seed(corpse.Owner, [new InventoryContainerSeed(new InventoryItemId("gold-piece"), 5, Stack: InventoryStackId.Parse("test.loot.4534"))]);
        RegisterCorpseStack(session, definitions, 2000, "test.loot.4534");
        // The pair the passing attack test uses, so the melee assertions below can actually fire.
        perception.Receipt = Receipt(new PerceptionPair(1, 2000, 2.25d, .5d, PerceptionPairKind.Visible, 1d));
        void Ui(string json, ulong step)
        {
            ProductInputEvent action = Input(InputEventKind.DirectDigital) with
            {
                ValueKind = InputValueKind.ProductPayload,
                PayloadContract = "dagger.ui.action.v1"u8.ToArray(),
                PayloadData = Encoding.UTF8.GetBytes(json),
            };
            session.Update(new ProductUpdate(OuterUpdate(step), [action]));
        }

        // The appearance starts with the weapon drawn, so the attack assertions below test the mode
        // gate rather than an unarmed player. Intent drives the ordinary input path.
        void Intent(string intent, ulong step) =>
            session.Update(new ProductUpdate(OuterUpdate(step), [Input(InputEventKind.DirectDigital, x: 1f, phase: InputPhase.DirectUi, intent: intent)]));

        // Nothing owns input, so the session asks the product for nothing.
        Assert.Null(session.PendingModeRequest);

        // The loot key opens a container, and that is what the session reports: it can open an
        // interaction the product cannot see, so it asks rather than deciding.
        Ui("{\"action\":\"loot\"}", 2);
        Assert.Equal(ProductMode.Modal, session.PendingModeRequest);

        // The product decides, and the session applies it.
        session.ApplyProductMode(ProductMode.Modal);
        Assert.Equal(ProductMode.Modal, session.Mode);

        // A product that holds the world somewhere other than play asks for nothing, even with this
        // container open: the request follows the container, so a held world with one open would otherwise
        // ask to be put back into a modal - or into play - behind the product's back. The entry screen is
        // that held world here; a pause is the same fact with a different mode.
        session.ApplyProductMode(ProductMode.Title);
        Assert.NotEqual(ProductMode.Modal, session.PendingModeRequest);
        Assert.NotEqual(ProductMode.Playing, session.PendingModeRequest);
        session.ApplyProductMode(ProductMode.Paused);
        Assert.NotEqual(ProductMode.Modal, session.PendingModeRequest);
        session.ApplyProductMode(ProductMode.Modal);

        // Held movement reaches no world step while a modal owns input, and the modal's own action
        // still lands: the gold moves even though the world does not.
        int stepsBeforeModal = spatial.StepCalls;
        ulong revisionBefore = session.State.Inventory.Read().StoreRevision;
        session.Update(new ProductUpdate(OuterUpdate(3), [Input(InputEventKind.Key, InputEdge.Pressed, keyboard: KeyboardControl.KeyW)]));
        Assert.Equal(stepsBeforeModal, spatial.StepCalls);

        // An attack asked for while the modal owns input reaches no combat: the per-step update that
        // would run it is the one the mode holds back, which is the same gate the step assertion
        // above measures. (An outer-path melee cannot be observed landing in this fixture even in
        // ordinary play - the existing melee test drives the internal per-step seam - so the attack
        // half is stated as this structural fact rather than claimed as an executed refusal.)
        Intent("attack", 4);
        Assert.Null(session.LastMeleeTargeting);
        Assert.Equal(stepsBeforeModal, spatial.StepCalls);
        LootPresentation opened = Assert.IsType<LootPresentation>(session.OpenLoot);
        InventoryItemPresentation gold = opened.Items.Single(item => item.Key == DaggerfallInventoryPresentation.StackKey(InventoryStackId.Parse("test.loot.4534")));

        // The projection the thin UI renders carries the mode the product decided and the token a
        // close has to name, so the UI keeps no focus authority of its own.
        Assert.Equal("modal", engine.PublishedField("mode"));
        Assert.Equal(opened.Container, engine.PublishedNested("focus", "container"));
        Assert.Equal("loot-close", engine.PublishedNested("focus", "close"));
        Assert.Equal("modal", engine.PublishedNested("view", "interaction"));

        // A status row an owner publishes reaches the projection without the projection knowing
        // what it means: this is the slot path effects, escorts and quests will use.
        session.Slots.Publish(new PresentationSlot("effect.poison", "tick", "Poisoned", "3 damage", 10));
        session.ApplyProductMode(ProductMode.Playing);
        session.ApplyProductMode(ProductMode.Modal);
        Assert.Equal("Poisoned", engine.PublishedArrayItem("slots", 0, "label"));
        Assert.Equal("effect.poison", engine.PublishedArrayItem("slots", 0, "owner"));
        Ui(System.Text.Json.JsonSerializer.Serialize(new { action = "loot-take", container = opened.Container, revision = opened.Revision, item = gold.Key }), 4);
        Assert.NotEqual(revisionBefore, session.State.Inventory.Read().StoreRevision);

        // Closing through the interaction's own token is what returns the session to ordinary play,
        // and it asks the product for it rather than deciding for itself.
        Ui(System.Text.Json.JsonSerializer.Serialize(new { action = "loot-close", container = opened.Container }), 4);
        Assert.Equal(ProductMode.Playing, session.PendingModeRequest);
        session.ApplyProductMode(ProductMode.Playing);
        Assert.Null(session.OpenLoot);
        Assert.Equal("playing", engine.PublishedField("mode"));
        Assert.Null(engine.PublishedNested("focus", "container"));


        session.Update(new ProductUpdate(OuterUpdate(5), []));
        Assert.True(spatial.StepCalls > stepsBeforeModal, "ordinary play admits world time again");
        Assert.Equal(Vector2.Zero, spatial.StepRequests[^1].Command.PlanarIntent);
        session.Update(new ProductUpdate(OuterUpdate(6), [Input(InputEventKind.Key, InputEdge.Pressed, keyboard: KeyboardControl.KeyW)]));
        Assert.True(spatial.StepCalls > stepsBeforeModal, "ordinary play admits world steps again");
        Assert.NotEqual(Vector2.Zero, spatial.StepRequests[^1].Command.PlanarIntent);

        // A player whose health track reached zero is dead, whatever mode they were in, and death
        // admits no world time either. The negative is a *valid* take - the same container token and
        // revision that moved the gold while the modal was open - so it would move it again if the
        // dead gate were missing.
        session.Update(new ProductUpdate(OuterUpdate(6), [Input(InputEventKind.Key, InputEdge.Pressed, keyboard: KeyboardControl.KeyW)]));
        perception.Receipt = Receipt(new PerceptionPair(1, 2000, 2.25d, .5d, PerceptionPairKind.Visible, 1d));
        Ui("{\"action\":\"loot\"}", 7);
        LootPresentation reopened = Assert.IsType<LootPresentation>(session.OpenLoot);
        session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).SetCurrent(0, clamp: true);
        Assert.Equal(ProductMode.Dead, session.PendingModeRequest);
        session.ApplyProductMode(ProductMode.Dead);
        int stepsBeforeDeath = spatial.StepCalls;
        ulong afterDeath = session.State.Inventory.Read().StoreRevision;
        InventoryItemPresentation remaining = reopened.Items.Single(item => item.Key == gold.Key);
        session.Update(new ProductUpdate(OuterUpdate(8), [Input(InputEventKind.Key, InputEdge.Pressed, keyboard: KeyboardControl.KeyW)]));
        Ui(System.Text.Json.JsonSerializer.Serialize(new { action = "loot-take", container = reopened.Container, revision = reopened.Revision, item = remaining.Key }), 9);
        Assert.Equal(stepsBeforeDeath, spatial.StepCalls);
        Assert.Equal(afterDeath, session.State.Inventory.Read().StoreRevision);

        // A dead product advertises no closable interaction: its own gate would ignore the close, so
        // offering the token would be a control that silently does nothing. The contents panel is
        // the same affordance and is withheld for the same reason.
        Assert.Equal("dead", engine.PublishedField("mode"));
        Assert.Null(engine.PublishedNested("focus", "container"));
        Assert.Null(engine.PublishedNested("loot", "container"));
        Assert.Equal(ulong.Parse(remaining.Quantity), ulong.Parse(session.OpenLoot!.Items.Single(item => item.Key == remaining.Key).Quantity));

    }

    [Fact]
    public void Host_adopts_the_loot_close_and_resumes_play_through_the_real_session()
    {
        // The defect this pins: closing loot asked the session for Playing, but the product
        // adopted the request without closesModal and refused Modal -> Playing, so gameplay
        // stayed held. Driving the session alone cannot show it; only the real handshake can.
        string root = RepositoryRoot();
        List<string> releases = [];
        ContentFake content = new(releases);
        PrivateersHoldInputs inputs = ReadInputs(root);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        spatial.KeepPosition = true;
        PerceptionFake perception = PerceptionFake.Create();
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), perception.Service);
        ProductInputConfiguration input = new(default, default, ReadOnlyMemory<ProductInputDescriptor>.Empty, ReadOnlyMemory<ProductInputMapping>.Empty);
        CapturingDaggerfallRuleset ruleset = new();

        static ProductInputEvent UiAction(string json) => Input(InputEventKind.DirectDigital) with
        {
            ValueKind = InputValueKind.ProductPayload,
            PayloadContract = "dagger.ui.action.v1"u8.ToArray(),
            PayloadData = Encoding.UTF8.GetBytes(json),
        };

        using (WorldRpgProduct product = new(new ProductCreateContext(engine.Context, FullContent(root), input), ruleset, new GameBundleId("daggerfall.privateers-hold")))
        {
            product.Start();
            product.Begin();
            DaggerfallSession session = ruleset.RequireSession();

            // One lootable corpse within reach, mirroring the session-level loot fixture.
            session.State.Actors.Get(2000).Stats.GetTrack(TrackId.Parse("health")).SetCurrent(1, clamp: true);
            session.ResolveExplicitMelee(new ExplicitMeleeRequest(1, 2000, 1, 1, .125));
            AimActivationAt(session, 2000);
            CorpseContainer corpse = session.Corpses[2000];
            session.State.Containers.Seed(corpse.Owner, [new InventoryContainerSeed(new InventoryItemId("gold-piece"), 5, Stack: InventoryStackId.Parse("test.loot.4691"))]);
            session.State.ItemInstances.RegisterStack(DaggerfallItemOwner.Corpse(2000), InventoryStackId.Parse("test.loot.4691"),
                new DaggerfallItemInstanceMetadata("gold-piece", "none", 0, 1, 1, true, false, null, null, null, DaggerfallItemOwner.Corpse(2000)).Validate());
            perception.Receipt = Receipt(new PerceptionPair(1, 2000, 2.25d, .5d, PerceptionPairKind.Visible, 1d));

            // Opening loot through the product puts the product into the modal the session asked for.
            product.Update(new ProductUpdate(OuterUpdate(1), [UiAction("{\"action\":\"loot\"}")]));
            Assert.Equal(ProductMode.Modal, product.Mode);
            LootPresentation opened = Assert.IsType<LootPresentation>(session.OpenLoot);

            // Closing through the interaction's own token returns the product to ordinary play.
            // The update adopts twice — before and after the session runs — so the trailing entry
            // is the post-adopt no-op; what matters is that this update caused Modal->Playing.
            int stepsBeforeClose = spatial.StepCalls;
            int historyBeforeClose = product.ModeHistory.Count;
            product.Update(new ProductUpdate(OuterUpdate(2), [UiAction(JsonSerializer.Serialize(new { action = "loot-close", container = opened.Container }))]));
            Assert.Equal(ProductMode.Playing, product.Mode);
            ProductModeChange close = product.ModeHistory.Skip(historyBeforeClose).First(change => change.Changed);
            Assert.Equal(ProductMode.Modal, close.From);
            Assert.Equal(ProductMode.Playing, close.To);
            Assert.Null(session.OpenLoot);

            // World advancement and input resume: a held key steps the world with intent again.
            product.Update(new ProductUpdate(OuterUpdate(3), [Input(InputEventKind.Key, InputEdge.Pressed, keyboard: KeyboardControl.KeyW)]));
            Assert.True(spatial.StepCalls > stepsBeforeClose, "ordinary play admits world time again");
            Assert.NotEqual(Vector2.Zero, spatial.StepRequests[^1].Command.PlanarIntent);
            product.Shutdown();
        }
    }

    [Fact]
    public void Loot_takes_apply_deliver_and_publish_inside_the_modal_update()
    {
        // The deferred defect this pins: a take prepared its transfer and facts, but the facts
        // waited for a playing step while the published presentation still showed the item.
        // A take must now move the item, deliver its facts, and republish within the same modal
        // update, with the panel still open and the world still held.
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        spatial.KeepPosition = true;
        PerceptionFake perception = PerceptionFake.Create();
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), perception.Service);
        using DaggerfallSession session = new(engine.Context, definitions, inputs, DaggerfallTuning.Defaults);
        session.State.Actors.Get(2000).Stats.GetTrack(TrackId.Parse("health")).SetCurrent(1, clamp: true);
        session.ResolveExplicitMelee(new ExplicitMeleeRequest(1, 2000, 1, 1, .125));
        AimActivationAt(session, 2000);
        CorpseContainer corpse = session.Corpses[2000];
        Assert.True(corpse.IsRegistered);
        session.State.Containers.Seed(corpse.Owner, [new InventoryContainerSeed(new InventoryItemId("gold-piece"), 5, Stack: InventoryStackId.Parse("test.loot.4740"))]);
        RegisterCorpseStack(session, definitions, 2000, "test.loot.4740");
        perception.Receipt = Receipt(new PerceptionPair(1, 2000, 2.25d, .5d, PerceptionPairKind.Visible, 1d));

        static ulong PlayerGold(DaggerfallSession session) => session.State.Inventory.Read().Stacks
            .Where(stack => stack.Definition.Value == "gold-piece").Select(stack => stack.Quantity).Aggregate(0UL, (a, b) => a + b);
        static ulong CorpseGold(LootPresentation loot) => loot.Items.Where(item => item.Definition == "gold-piece")
            .Select(item => ulong.Parse(item.Quantity, CultureInfo.InvariantCulture)).Aggregate(0UL, (a, b) => a + b);
        void Ui(string json, ulong step)
        {
            ProductInputEvent action = Input(InputEventKind.DirectDigital) with
            {
                ValueKind = InputValueKind.ProductPayload,
                PayloadContract = "dagger.ui.action.v1"u8.ToArray(),
                PayloadData = Encoding.UTF8.GetBytes(json),
            };
            session.Update(new ProductUpdate(OuterUpdate(step), [action]));
        }

        Ui("{\"action\":\"loot\"}", 2);
        Assert.Equal(ProductMode.Modal, session.PendingModeRequest);
        session.ApplyProductMode(ProductMode.Modal);
        LootPresentation opened = Assert.IsType<LootPresentation>(session.OpenLoot);
        ulong playerBefore = PlayerGold(session);
        ulong corpseBefore = CorpseGold(opened);
        Assert.True(corpseBefore > 0, "the corpse should hold loot to take");
        int stepsBefore = spatial.StepCalls;

        // One take moves one unit, delivers its facts, and republishes: the panel stays open,
        // the world takes no step, and the outcome line carries the delivered fact.
        string take = JsonSerializer.Serialize(new
        {
            action = "loot-take",
            container = opened.Container,
            revision = opened.Revision,
            item = DaggerfallInventoryPresentation.StackKey(InventoryStackId.Parse("test.loot.4740")),
        });
        Ui(take, 3);
        Assert.Equal(playerBefore + 1, PlayerGold(session));
        LootPresentation afterTake = Assert.IsType<LootPresentation>(session.OpenLoot);
        Assert.Equal(opened.Container, afterTake.Container);
        Assert.Equal(corpseBefore - 1, CorpseGold(afterTake));
        Assert.Equal(stepsBefore, spatial.StepCalls);
        Assert.Contains("looted", engine.PublishedField("lastOutcome"), StringComparison.Ordinal);

        // Replaying the same take is refused as stale: no duplicate transfer, no world step.
        Ui(take, 4);
        Assert.Equal(playerBefore + 1, PlayerGold(session));
        Assert.Equal(corpseBefore - 1, CorpseGold(Assert.IsType<LootPresentation>(session.OpenLoot)));
        Assert.Equal(stepsBefore, spatial.StepCalls);
        Assert.Contains("Loot changed", engine.PublishedField("lastOutcome"), StringComparison.Ordinal);

        // Draining the container through the panel ends in the empty state, still modal.
        for (ulong step = 5; step < 30; step++)
        {
            LootPresentation current = Assert.IsType<LootPresentation>(session.OpenLoot);
            if (current.Empty) break;
            InventoryItemPresentation first = current.Items[0];
            Ui(JsonSerializer.Serialize(new { action = "loot-take", container = current.Container, revision = current.Revision, item = first.Key }), step);
            if (step == 29) Assert.Fail("draining the corpse did not reach the empty state");
        }
        LootPresentation drained = Assert.IsType<LootPresentation>(session.OpenLoot);
        Assert.True(drained.Empty);
        Assert.Equal(ProductMode.Modal, session.Mode);
        Assert.Equal(stepsBefore, spatial.StepCalls);
    }

    [Fact]
    public void Admitted_authored_entity_ids_cover_construction_and_allocator_inputs_and_reject_duplicates()
    {
        // One helper now serves actor construction validation and allocator reservations. The
        // deleted payload copy silently dropped duplicate loadout ids from the reservation set
        // while construction threw; this pins the unified strict behavior.
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs inputs = ReadInputs(root);
        DaggerfallActorDefinition player = definitions.RequireActor(new DaggerfallActorId("player"));

        HashSet<ulong> admitted = DaggerActorFactory.AdmittedAuthoredEntityIds(inputs, player.Loadout);
        Assert.Contains((ulong)DaggerfallActorIdentity.PlayerEntityId, admitted);
        Assert.Equal(
            1 + inputs.Project.Actors.Values.Count() + player.Loadout.Count(entry => entry.UniqueEntityId is not null),
            admitted.Count);

        DaggerfallLoadoutEntry duplicated = player.Loadout.First(entry => entry.UniqueEntityId is not null);
        InvalidOperationException rejected = Assert.Throws<InvalidOperationException>(() =>
            DaggerActorFactory.AdmittedAuthoredEntityIds(inputs, [.. player.Loadout, duplicated]));
        Assert.Contains("collides", rejected.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Multi_step_outer_update_steps_each_catch_up_step_but_publishes_once()
    {
        // Structural evidence for publish-once: three admitted steps advance the world three
        // times, but graphics/UI publication happens exactly once, after animation impacts.
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        AppearanceFake appearance = new(releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, appearance, PerceptionFake.Create().Service);
        using DaggerfallSession session = new(engine.Context, definitions, inputs, DaggerfallTuning.Defaults);

        static ProductUpdateFacts ThreeSteps(ulong step) =>
            new(ProductUpdateMode.Realtime, ProductLifecycleState.Running, 1, 1, step, step, 60, 3, 0, 1d / 60d);

        int stepsBefore = spatial.StepCalls;
        int publishesBefore = appearance.PublishCalls;
        session.Update(new ProductUpdate(ThreeSteps(1),
            [Input(InputEventKind.Key, InputEdge.Pressed, keyboard: KeyboardControl.KeyW)]));
        Assert.Equal(stepsBefore + 3, spatial.StepCalls);
        Assert.Equal(publishesBefore + 1, appearance.PublishCalls);
        Assert.NotEqual(Vector2.Zero, spatial.StepRequests[^1].Command.PlanarIntent);

        // A held world still publishes its single presentation per outer update, with no steps.
        // (The mode transition above publishes on its own; only the update's publication counts.)
        session.ApplyProductMode(ProductMode.Modal);
        int modalPublishesBefore = appearance.PublishCalls;
        session.Update(new ProductUpdate(ThreeSteps(4), []));
        Assert.Equal(stepsBefore + 3, spatial.StepCalls);
        Assert.Equal(modalPublishesBefore + 1, appearance.PublishCalls);

        // An update with no admitted steps publishes nothing and steps nothing.
        ProductUpdateFacts noSteps = new(ProductUpdateMode.Realtime, ProductLifecycleState.Running, 1, 1, 7, 7, 60, 0, 0, 1d / 60d);
        session.Update(new ProductUpdate(noSteps, []));
        Assert.Equal(stepsBefore + 3, spatial.StepCalls);
        Assert.Equal(modalPublishesBefore + 1, appearance.PublishCalls);
    }

    [Fact]
    public void Corpse_save_roundtrip_preserves_populated_and_empty_contents_identity_and_allocation()
    {
        // The shared contents mapping must carry a populated corpse (stacks + a unique), a looted
        // registered-empty corpse, and an empty unregistered corpse, with durable identity
        // agreement into the next allocation.
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake sourceContent = new(releases);
        PopulateContent(sourceContent, inputs);
        SpatialFake sourceSpatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        sourceSpatial.KeepPosition = true;
        PerceptionFake sourcePerception = PerceptionFake.Create();
        EngineContextFake source = EngineContextFake.Create(sourceContent, sourceSpatial.Service, new AppearanceFake(releases), sourcePerception.Service);
        RulesetSavePayload populatedPayload;
        RulesetSavePayload lootedPayload;
        static void Ui(DaggerfallSession session, string json, ulong step)
        {
            ProductInputEvent action = Input(InputEventKind.DirectDigital) with
            {
                ValueKind = InputValueKind.ProductPayload,
                PayloadContract = "dagger.ui.action.v1"u8.ToArray(),
                PayloadData = Encoding.UTF8.GetBytes(json),
            };
            session.Update(new ProductUpdate(OuterUpdate(step), [action]));
        }
        using (DaggerfallSession original = new(source.Context, definitions, inputs, DaggerfallTuning.Defaults))
        {
            static void Kill(DaggerfallSession session, long target)
            {
                session.State.Actors.Get(target).Stats.GetTrack(TrackId.Parse("health")).SetCurrent(1, clamp: true);
                session.ResolveExplicitMelee(new ExplicitMeleeRequest(1, target, 1, 1, .125));
            }
            Kill(original, 2000);
            AimActivationAt(original, 2000);
            CorpseContainer thief = original.Corpses[2000];
            DaggerfallItemFactory itemFactory = new(definitions, RandomMinimum.Create());
            DaggerfallCreatedItem potion = itemFactory.Create(new DaggerfallItemCreateRequest(
                "UselessItems1", "test.loot.4903.potion", DaggerfallItemOwner.Corpse(2000), TemplateIndex: 83, PotionRecipeKey: 221871));
            DaggerfallCreatedItem recipe = itemFactory.Create(new DaggerfallItemCreateRequest(
                "MiscItems", "test.loot.4904.recipe", DaggerfallItemOwner.Corpse(2000), TemplateIndex: 278, PotionRecipeKey: 221871));
            original.State.Containers.Seed(thief.Owner, [
                new InventoryContainerSeed(new InventoryItemId("gold-piece"), 5, Stack: InventoryStackId.Parse("test.loot.4902")),
                new InventoryContainerSeed(potion.Item, potion.Quantity, Stack: InventoryStackId.Parse("test.loot.4903")),
                new InventoryContainerSeed(recipe.Item, UniqueItem: new(DurableIdentityKind.Item, 5002)),
                new InventoryContainerSeed(new InventoryItemId("iron-dagger"), 1, new(DurableIdentityKind.Item, 5001))]);
            RegisterCorpseStack(original, definitions, 2000, "test.loot.4902");
            original.State.ItemInstances.RegisterStack(DaggerfallItemOwner.Corpse(2000), InventoryStackId.Parse("test.loot.4903"), potion.Metadata);
            original.State.ItemInstances.RegisterUnique(5002, recipe.Metadata);
            original.State.ItemInstances.RegisterDefaultUnique(5001, definitions.Items[new DaggerfallItemId("iron-dagger")], DaggerfallItemOwner.Corpse(2000));
            // The giant-bat carries no loot table, so its corpse stays unregistered and empty.
            // It is tougher than the thief: repeat the explicit swing until the death lands.
            original.State.Actors.Get(2006).Stats.GetTrack(TrackId.Parse("health")).SetCurrent(1, clamp: true);
            ulong swing = 2;
            while (!original.State.Actors.Get(2006).IsDefeated && swing < 20)
            {
                original.ResolveExplicitMelee(new ExplicitMeleeRequest(1, 2006, swing, swing, .125));
                original.Update(new ProductUpdate(OuterUpdate(swing), []));
                swing++;
            }
            Assert.True(original.State.Actors.Get(2006).IsDefeated, "the giant-bat should die within bounded swings");
            Assert.False(original.Corpses[2006].IsRegistered);
            populatedPayload = original.CaptureSave();

            // Drain the thief through the panel: the looted save must carry a registered corpse
            // with empty contents, not an unregistered one.
            sourcePerception.Receipt = Receipt(new PerceptionPair(1, 2000, 2.25d, .5d, PerceptionPairKind.Visible, 1d));
            Ui(original, "{\"action\":\"loot\"}", swing++);
            var activation = Assert.IsType<InteractionTargetingEvidence>(original.LastActivationTargeting);
            Assert.Equal(Rusty.Engine.Interaction.InteractionReason.Ready, activation.Focus.Reason);
            Assert.Equal(ProductMode.Modal, original.PendingModeRequest);
            original.ApplyProductMode(ProductMode.Modal);
            for (ulong step = swing; step < swing + 30; step++)
            {
                LootPresentation current = Assert.IsType<LootPresentation>(original.OpenLoot);
                if (current.Empty) break;
                InventoryItemPresentation first = current.Items[0];
                Ui(original, JsonSerializer.Serialize(new { action = "loot-take", container = current.Container, revision = current.Revision, item = first.Key }), step);
                if (step == swing + 29) Assert.Fail("draining the corpse did not reach the empty state");
            }
            LootPresentation drained = Assert.IsType<LootPresentation>(original.OpenLoot);
            Assert.True(drained.Empty);
            Assert.False(original.Corpses[2000].IsInteractable);
            lootedPayload = original.CaptureSave();
        }

        DaggerfallSavePayload captured = DaggerfallSavePayload.Read(populatedPayload);
        DaggerfallCorpseSave savedThief = captured.Corpses.Single(corpse => corpse.ActorId == 2000);
        Assert.True(savedThief.IsRegistered);
        // The thief's loot table generates gold on top of the seeded stack; the save carries both.
        Assert.True(savedThief.Stacks.Where(stack => stack.ItemId == "gold-piece")
            .Aggregate(0UL, (total, stack) => total + stack.Quantity) >= 5UL);
        Assert.Equal(5001UL, Assert.Single(savedThief.UniqueItems, item => item.ItemId == "iron-dagger").EntityId);
        Assert.Equal(221871, savedThief.Stacks.Single(stack => stack.StackId == "test.loot.4903").Metadata.PotionRecipeKey);
        Assert.Equal(221871, savedThief.UniqueItems.Single(item => item.EntityId == 5002).Metadata.PotionRecipeKey);
        // The live corpse path now uses retained template definitions, and its material and
        // appearance facts must survive the normal save boundary rather than becoming defaults.
        DaggerfallUniqueSave savedTemplateWeapon = savedThief.UniqueItems.First(item => definitions.RequireItem(new DaggerfallItemId(item.ItemId)).Weapon is not null);
        DaggerfallCorpseSave savedBat = captured.Corpses.Single(corpse => corpse.ActorId == 2006);
        Assert.False(savedBat.IsRegistered);
        Assert.Empty(savedBat.Stacks);
        Assert.Empty(savedBat.UniqueItems);

        // The looted save keeps the thief registered with empty contents and no interaction.
        DaggerfallSavePayload looted = DaggerfallSavePayload.Read(lootedPayload);
        DaggerfallCorpseSave lootedThief = looted.Corpses.Single(corpse => corpse.ActorId == 2000);
        Assert.True(lootedThief.IsRegistered);
        Assert.Empty(lootedThief.Stacks);
        Assert.Empty(lootedThief.UniqueItems);
        Assert.False(lootedThief.IsInteractable);

        ContentFake resumedContent = new(releases);
        PopulateContent(resumedContent, inputs);
        SpatialFake resumedSpatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake resumedEngine = EngineContextFake.Create(resumedContent, resumedSpatial.Service, new AppearanceFake(releases), PerceptionFake.Create().Service);
        ResolvedCompositionIdentity identity = GameCompositionResolver.Resolve(FullContent(root), new GameBundleId("daggerfall.privateers-hold")).RequireComposition().Identity;
        using (DaggerfallSession resumed = DaggerfallSession.Restore(resumedEngine.Context, identity, definitions, inputs, DaggerfallTuning.Defaults, populatedPayload, RandomMinimum.Create()))
        {
            InventoryView restoredThief = resumed.State.Containers.Read(resumed.Corpses[2000].Owner);
            Assert.Equal(
                savedThief.Stacks.Where(stack => stack.ItemId == "gold-piece")
                    .OrderBy(stack => stack.StackId, StringComparer.Ordinal)
                    .Select(stack => (stack.StackId, stack.Quantity)),
                restoredThief.Stacks.Where(stack => stack.Definition.Value == "gold-piece")
                    .OrderBy(stack => stack.Id.Value, StringComparer.Ordinal)
                    .Select(stack => (stack.Id.Value, stack.Quantity)));
            var restoredDagger = Assert.Single(restoredThief.UniqueItems, item => item.Definition.Value == "iron-dagger");
            Assert.Equal(5001UL, resumed.State.Actors.Entities.IdentityOf(restoredDagger.Entity).Value);
            var restoredTemplateWeapon = restoredThief.UniqueItems.First(item => definitions.RequireItem(new DaggerfallItemId(item.Definition.Value)).Weapon is not null);
            ulong restoredTemplateIdentity = resumed.State.Actors.Entities.IdentityOf(restoredTemplateWeapon.Entity).Value;
            Assert.Equal(savedTemplateWeapon.Metadata.Material, resumed.State.ItemInstances.RequireUnique(restoredTemplateIdentity).Material);
            Assert.Equal(221871, resumed.State.ItemInstances.RequireStack(DaggerfallItemOwner.Corpse(2000), InventoryStackId.Parse("test.loot.4903")).PotionRecipeKey);
            Assert.Equal(221871, resumed.State.ItemInstances.RequireUnique(5002).PotionRecipeKey);
            Assert.False(resumed.Corpses[2006].IsRegistered);

            // The next generated unique must not reuse a restored live identity.
            ulong nextUnique = resumed.UniqueItemAllocator.AllocateReference().Value;
            Assert.NotEqual(5001UL, nextUnique);
            Assert.NotEqual(5002UL, nextUnique);
        }

        ContentFake lootedContent = new(releases);
        PopulateContent(lootedContent, inputs);
        SpatialFake lootedSpatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake lootedEngine = EngineContextFake.Create(lootedContent, lootedSpatial.Service, new AppearanceFake(releases), PerceptionFake.Create().Service);
        using DaggerfallSession lootedSession = DaggerfallSession.Restore(lootedEngine.Context, identity, definitions, inputs, DaggerfallTuning.Defaults, lootedPayload, RandomMinimum.Create());

        // The looted corpse restores registered and empty rather than unregistered or reseeded.
        Assert.True(lootedSession.Corpses[2000].IsRegistered);
        Assert.False(lootedSession.Corpses[2000].IsInteractable);
        InventoryView restoredLooted = lootedSession.State.Containers.Read(lootedSession.Corpses[2000].Owner);
        Assert.Empty(restoredLooted.Stacks);
        Assert.Empty(restoredLooted.UniqueItems);
        Assert.False(lootedSession.Corpses[2006].IsRegistered);

        // The next generated unique still allocates cleanly after the looted restore.
        _ = lootedSession.UniqueItemAllocator.AllocateReference();
    }

    private static (DaggerfallSession Session, AppearanceFake Appearance, PerceptionFake Perception) VisibleEnemySession(List<string> releases, double distance = 1d)
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
        PrivateersHoldInputs inputs = ReadInputs(root);
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        PerceptionFake perception = PerceptionFake.Create();
        perception.Receipt = Receipt(new PerceptionPair(2000, 1, distance, 1d, PerceptionPairKind.Visible, distance));
        AppearanceFake appearance = new(releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, appearance, perception.Service);
        return (new DaggerfallSession(engine.Context, definitions, inputs, DaggerfallTuning.Defaults), appearance, perception);
    }

    private static DaggerfallSession CreateArcherSession(string root, DaggerfallDefinitions definitions, PrivateersHoldInputs inputs,
        List<string> releases, out AppearanceFake appearance, out PerceptionFake perception)
    {
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        spatial.KeepPosition = true;
        perception = PerceptionFake.Create();
        appearance = new AppearanceFake(releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, appearance, perception.Service);
        return new DaggerfallSession(engine.Context, definitions, inputs, DaggerfallTuning.Defaults);
    }

    private static long PlayerHealth(DaggerfallSession session) => session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).ValueInt64;

    private static ProductUpdateFacts OuterUpdate(ulong simulationStep) => new(ProductUpdateMode.Realtime, ProductLifecycleState.Running, 1, 1, simulationStep, simulationStep, 60, 1, 0, 1d / 60d);

    /// <summary>One advanced sprite frame whose authored damage frame was crossed.</summary>
    /// <summary>A playback receipt that completes without crossing a marker, which is what clears a swing.</summary>
    private static SpritePlaybackAdvanceLeaseReceipt CompletedMarker(uint frame) => new(
        Array.Empty<SpritePlaybackMarkerCrossing>(),
        new SpritePlaybackReadout(frame, 1, SpritePlaybackState.Completed, 0D, 0, frame, true),
        true);

    private static SpritePlaybackAdvanceLeaseReceipt CrossedMarker(uint frame, ulong crossing = 1) => new(
        new[] { new SpritePlaybackMarkerCrossing(1, 3, 1, 0, crossing) },
        new SpritePlaybackReadout(frame, 1, SpritePlaybackState.Playing, 0D, 0, frame, false),
        true);

    private static JsonObject FirstActorState(JsonObject media, string name) => media["actors"]!.AsArray()
        .Select(value => value!.AsObject())
        .SelectMany(actor => actor["states"]!.AsArray())
        .Select(value => value!.AsObject())
        .First(state => state["state"]!.GetValue<string>() == name);

    private static JsonObject WeaponResource(JsonObject media) => media["media"]!["resources"]!.AsArray()
        .Select(value => value!.AsObject())
        .Single(resource => resource["id"]!.GetValue<string>() == "weapon.dagger.steel");

    private static JsonObject EffectResource(JsonObject media, string id) => media["media"]!["resources"]!.AsArray()
        .Select(value => value!.AsObject())
        .Single(resource => resource["id"]!.GetValue<string>() == id);

    private static ProductContent MutateDungeonMedia(string repositoryRoot, Action<JsonObject> mutate)
    {
        string contentRoot = Path.Combine(repositoryRoot, "content");
        ProductContentFile[] files = Directory.GetFiles(Path.Combine(contentRoot, "worldrpg/imports/privateers-hold"), "*", SearchOption.AllDirectories)
            .Select(path => new ProductContentFile(Encoding.UTF8.GetBytes(Path.GetRelativePath(contentRoot, path).Replace(Path.DirectorySeparatorChar, '/')), File.ReadAllBytes(path)))
            .ToArray();
        int mediaIndex = Array.FindIndex(files, file => Encoding.UTF8.GetString(file.Path.Span).EndsWith("media/dungeon/manifest.json", StringComparison.Ordinal));
        int importsIndex = Array.FindIndex(files, file => Encoding.UTF8.GetString(file.Path.Span).EndsWith("import-manifest.json", StringComparison.Ordinal));
        Assert.True(mediaIndex >= 0 && importsIndex >= 0);

        JsonObject media = JsonNode.Parse(files[mediaIndex].Bytes.Span)!.AsObject();
        mutate(media);
        byte[] mediaBytes = Encoding.UTF8.GetBytes(media.ToJsonString());
        files[mediaIndex] = new ProductContentFile(files[mediaIndex].Path, mediaBytes);

        JsonObject imports = JsonNode.Parse(files[importsIndex].Bytes.Span)!.AsObject();
        JsonObject artifact = imports["artifacts"]!.AsArray().Select(value => value!.AsObject())
            .Single(value => value["relativePath"]!.GetValue<string>() == "media/dungeon/manifest.json");
        artifact["contentHash"] = Convert.ToHexString(SHA256.HashData(mediaBytes));
        files[importsIndex] = new ProductContentFile(files[importsIndex].Path, Encoding.UTF8.GetBytes(imports.ToJsonString()));
        return new ProductContent(files);
    }

    private static ProductContent MutateClassicMedia(string repositoryRoot, Action<JsonObject> mutate)
    {
        string contentRoot = Path.Combine(repositoryRoot, "content");
        ProductContentFile[] files = Directory.GetFiles(Path.Combine(contentRoot, "worldrpg/imports/privateers-hold"), "*", SearchOption.AllDirectories)
            .Select(path => new ProductContentFile(Encoding.UTF8.GetBytes(Path.GetRelativePath(contentRoot, path).Replace(Path.DirectorySeparatorChar, '/')), File.ReadAllBytes(path)))
            .ToArray();
        int mediaIndex = Array.FindIndex(files, file => Encoding.UTF8.GetString(file.Path.Span).EndsWith("media/classic/manifest.json", StringComparison.Ordinal));
        int importsIndex = Array.FindIndex(files, file => Encoding.UTF8.GetString(file.Path.Span).EndsWith("import-manifest.json", StringComparison.Ordinal));
        Assert.True(mediaIndex >= 0 && importsIndex >= 0);

        JsonObject media = JsonNode.Parse(files[mediaIndex].Bytes.Span)!.AsObject();
        mutate(media);
        byte[] mediaBytes = Encoding.UTF8.GetBytes(media.ToJsonString());
        files[mediaIndex] = new ProductContentFile(files[mediaIndex].Path, mediaBytes);

        JsonObject imports = JsonNode.Parse(files[importsIndex].Bytes.Span)!.AsObject();
        JsonObject artifact = imports["artifacts"]!.AsArray().Select(value => value!.AsObject())
            .Single(value => value["relativePath"]!.GetValue<string>() == "media/classic/manifest.json");
        artifact["contentHash"] = Convert.ToHexString(SHA256.HashData(mediaBytes));
        files[importsIndex] = new ProductContentFile(files[importsIndex].Path, Encoding.UTF8.GetBytes(imports.ToJsonString()));
        return new ProductContent(files);
    }

    private static ProductInputEvent Input(InputEventKind kind, InputEdge edge = InputEdge.None, KeyboardControl keyboard = KeyboardControl.None, float x = 0F, float y = 0F, InputPhase phase = InputPhase.None, string intent = "") => new(kind, edge, InputDevice.None, InputChannel.None, InputAxis.None, keyboard, PointerButton.None, ControllerButton.None, ControllerAxis.None, InputClearReason.None, InputValueKind.None, phase, InputProvenance.None, default, default, default, x, y, ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty, Encoding.UTF8.GetBytes(intent), ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty);

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

    /// <summary>One physical controller button edge, as the shell publishes it.</summary>
    private static ProductInputEvent PadButton(ControllerButton button, InputEdge edge) =>
        Input(InputEventKind.ControllerButton, edge, x: edge == InputEdge.Pressed ? 1F : 0F) with
        {
            Device = InputDevice.Controller,
            Channel = InputChannel.Button,
            ValueKind = InputValueKind.Digital,
            ControllerButton = button,
            Phase = edge == InputEdge.Pressed ? InputPhase.Pressed : InputPhase.Released,
            Provenance = InputProvenance.Physical,
        };

    /// <summary>The default tuning payload with one test mutation applied.</summary>
    private static byte[] MutatedTuning(string repositoryRoot, Action<JsonObject> mutate)
    {
        JsonObject tuning = JsonNode.Parse(File.ReadAllText(Path.Combine(repositoryRoot, "content/worldrpg/tuning-payloads/daggerfall.defaults.json")))!.AsObject();
        mutate(tuning);
        return Encoding.UTF8.GetBytes(tuning.ToJsonString());
    }

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

    private sealed class ContentFake : IContentService
    {
        // The pinned pair grew a bundle surface. Nothing in this product opens a bundle yet - content is
        // admitted as one snapshot - so the fake refuses these rather than pretending a bundle exists:
        // a test double that answered with an empty bundle would hide a caller that started using one.
        public ReadOnlyMemory<ContentBundleInfo> ListBundles() =>
            throw new NotSupportedException("This fake admits one content snapshot and carries no bundles.");

        public ContentBundle OpenBundle(ContentBundleOpenRequest request) =>
            throw new NotSupportedException($"This fake admits one content snapshot, so bundle '{request}' cannot be opened.");

        public ReadOnlyMemory<ContentReferenceInfo> ReadBundleFiles(ContentBundle bundle) =>
            throw new NotSupportedException("This fake admits one content snapshot and carries no bundle files.");

        public ContentReference OpenBundleReference(ContentBundleReferenceRequest request) =>
            throw new NotSupportedException($"This fake admits one content snapshot, so bundle reference '{request}' cannot be opened.");

        private readonly List<string> releases;
        private readonly Dictionary<string, ContentSha256> values = new(StringComparer.Ordinal);
        private readonly Dictionary<string, byte[]> bodies = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> reads = new(StringComparer.Ordinal);
        private readonly Dictionary<ulong, KeyValuePair<string, ContentSha256>> references = [];
        private ulong nextHandle = 1;

        internal ContentFake(string path, ContentSha256 hash, List<string> releaseLog) : this(releaseLog) => Add(path, hash);
        internal ContentFake(List<string> releaseLog) => releases = releaseLog;

        internal void Add(string contentPath, ContentSha256 contentHash) => values[contentPath] = contentHash;

        /// <summary>Admits one file with the bytes an admitted read returns for it.</summary>
        internal void Add(string contentPath, byte[] body) => Add(contentPath, Digest(body), body);

        internal void Add(string contentPath, ContentSha256 contentHash, byte[] body)
        {
            values[contentPath] = contentHash;
            bodies[contentPath] = body;
        }

        public ContentReference OpenReference(ContentOpenRequest request) => ResolveReference(new ContentResolveRequest(request.Path, values[request.Path]));

        public ContentReference ResolveReference(ContentResolveRequest request)
        {
            ResolveCalls++;
            if (!values.TryGetValue(request.Path, out ContentSha256 known) || known != request.Sha256) throw new InvalidOperationException("Unexpected content reference.");
            ulong value = nextHandle++;
            references.Add(value, new KeyValuePair<string, ContentSha256>(request.Path, known));
            return new ContentReference(new ContentReferenceHandle(value), () => releases.Add("content"));
        }

        /// <summary>How many admitted reads this fake served for one path, which shows how it was chunked.</summary>
        internal int Reads(string path) => reads.GetValueOrDefault(path);

        public ReadOnlyMemory<ContentReferenceInfo> ReadReferenceInfo(ContentReference reference)
        {
            KeyValuePair<string, ContentSha256> item = references[reference.Handle.Value];
            ulong length = bodies.TryGetValue(item.Key, out byte[]? body) ? (ulong)body.Length : 1;
            return new[] { new ContentReferenceInfo(item.Key, item.Value, length) };
        }

        public ReadOnlyMemory<byte> ReadBytes(ContentReadBytesRequest request)
        {
            string path = references[request.Reference.Handle.Value].Key;
            // The Engine serves at most one mebibyte per admitted read, so this fake does too: a fake
            // that answered a larger request would hide a reader that asked for a whole artifact.
            if (request.MaxBytes > 1024 * 1024) throw new InvalidOperationException($"Admitted content reads are bounded to one mebibyte, not {request.MaxBytes}.");
            reads[path] = reads.GetValueOrDefault(path) + 1;
            if (!bodies.TryGetValue(path, out byte[]? body)) return ReadOnlyMemory<byte>.Empty;
            if (request.Offset > (ulong)body.Length) throw new InvalidOperationException($"Read of '{path}' starts past its admitted bytes.");
            int available = checked((int)Math.Min(request.MaxBytes, (ulong)body.Length - request.Offset));
            return body.AsMemory(checked((int)request.Offset), available);
        }
        internal int ResolveCalls { get; private set; }
    }

    /// <summary>Build-declared bundles for full-product tests; bodies stay outside the eager snapshot.</summary>
    private sealed class BundleContentFake : IContentService
    {
        private readonly Dictionary<string, Dictionary<string, byte[]>> files = new(StringComparer.Ordinal);
        private readonly Dictionary<ulong, string> openedBundles = [];
        private ulong nextHandle = 1;

        internal void Add(string bundle, string path, byte[] bytes)
        {
            if (!files.TryGetValue(bundle, out Dictionary<string, byte[]>? values)) files.Add(bundle, values = new(StringComparer.Ordinal));
            values.Add(path, bytes);
        }

        public ReadOnlyMemory<ContentBundleInfo> ListBundles() => files
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => new ContentBundleInfo(entry.Key, checked((ulong)entry.Value.Count), checked((ulong)entry.Value.Values.Sum(bytes => bytes.Length))))
            .ToArray();

        public ContentBundle OpenBundle(ContentBundleOpenRequest request)
        {
            if (!files.ContainsKey(request.Id)) throw new FileNotFoundException("Test bundle is not declared.", request.Id);
            ulong handle = nextHandle++;
            openedBundles.Add(handle, request.Id);
            return new(new ContentBundleHandle(handle), () => openedBundles.Remove(handle));
        }

        public ReadOnlyMemory<ContentReferenceInfo> ReadBundleFiles(ContentBundle bundle)
        {
            if (!openedBundles.TryGetValue(bundle.Handle.Value, out string? id)) throw new InvalidOperationException("Test bundle handle is not open.");
            return files[id].OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => new ContentReferenceInfo(entry.Key, Digest(entry.Value), checked((ulong)entry.Value.Length)))
                .ToArray();
        }

        public ContentReference OpenBundleReference(ContentBundleReferenceRequest request)
        {
            if (!openedBundles.TryGetValue(request.Bundle.Handle.Value, out string? id)
                || !files[id].ContainsKey(request.Path)) throw new FileNotFoundException("Test bundle file is not declared.", request.Path);
            return new(new ContentReferenceHandle(nextHandle++), static () => { });
        }

        public ContentReference OpenReference(ContentOpenRequest request) => throw new NotSupportedException();
        public ContentReference ResolveReference(ContentResolveRequest request) => throw new NotSupportedException();
        public ReadOnlyMemory<ContentReferenceInfo> ReadReferenceInfo(ContentReference reference) => throw new NotSupportedException();
        public ReadOnlyMemory<byte> ReadBytes(ContentReadBytesRequest request) => throw new NotSupportedException();
    }

    private class SpatialFake : DispatchProxy
    {
        internal bool KeepPosition { get; set; }
        internal Func<SpatialRaycastRequest, SpatialHit> FloorHit { get; set; } = _ => default;
        internal List<SpatialRaycastRequest> FloorProbes { get; } = [];
        private SpatialHit ProbeFloor(SpatialRaycastRequest request) { FloorProbes.Add(request); return FloorHit(request); }
        private ContentSha256 hash = Hash;
        private List<string> releases = null!;
        internal ISpatialService Service { get; private set; } = null!;
        internal int ReplaceCalls { get; private set; }
        internal int ReadCalls { get; private set; }
        internal int CreateSessionCalls { get; private set; }
        internal int StepCalls { get; private set; }
        internal int ConfigValidationCalls { get; private set; }
        internal int CommandValidationCalls { get; private set; }
        internal bool RejectConfigValidation { get; set; }
        internal bool RejectProposedStep { get; set; }
        internal SpatialContentArtifactReplaceRequest? LastRequest { get; private set; }
        internal List<CharacterStepRequest> StepRequests { get; } = [];
        // Representative fixture only: Engine owns the actual default and validity contract.
        internal CharacterControllerConfig RepresentativeValidConfig { get; } = default(CharacterControllerConfig) with
        {
            Shape = new CharacterShapeConfig(2.2f, 1.3f, .45f, .03f, .02f),
            Ground = new CharacterGroundConfig(6f, 5f, 4f, 31f, 42f, 7f, 3f, 2f),
            Air = new CharacterAirConfig(4f, 10f, 1f, 4f, 1f, 0f),
            Vertical = new CharacterVerticalConfig(18f, 48f, 46f, 6f, .4f),
            Jump = new CharacterJumpConfig(.2f, .15f, 0f, false),
            Surface = new CharacterSurfaceConfig(.9f, .02f, 16f, 9f, .35f, .04f, .2f, 8f, .2f),
            Recovery = new CharacterRecoveryConfig(.7f, 18f, .002f, .003f),
            Platform = new CharacterPlatformConfig(true, true, true, .8f, 0f, .03f),
            ExternalMotion = new CharacterExternalMotionConfig(1f, 0f, 40f, 70f, 1f, 400f),
            Solver = new CharacterSolverConfig(4, 7, 3, 24, 1, 8f, 48),
        };

        internal static SpatialFake Create(ContentSha256 contentHash, List<string> releaseLog)
        {
            ISpatialService service = DispatchProxy.Create<ISpatialService, SpatialFake>();
            SpatialFake fake = (SpatialFake)(object)service;
            fake.Service = service;
            fake.hash = contentHash;
            fake.releases = releaseLog;
            return fake;
        }

        protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name switch
        {
            nameof(ISpatialService.CreateSession) => CreateSession(),
            nameof(ISpatialService.DefaultCharacterControllerConfig) => RepresentativeValidConfig,
            nameof(ISpatialService.CastRay) => ProbeFloor((SpatialRaycastRequest)arguments![0]!),
            nameof(ISpatialService.ValidateCharacterControllerConfig) => ValidateConfig((CharacterControllerConfig)arguments![0]!),
            nameof(ISpatialService.ValidateCharacterControllerCommand) => ValidateCommand((CharacterControllerValidationRequest)arguments![0]!),
            nameof(ISpatialService.ReplaceContentArtifact) => Replace((SpatialContentArtifactReplaceRequest)arguments![0]!),
            nameof(ISpatialService.ReadContentArtifact) => Read(),
            nameof(ISpatialService.ProposeCharacterStep) => Step((CharacterStepRequest)arguments![0]!),
            // Navigation is not under test here: an honest no-path receipt leaves the
            // actor's pose intact instead of reporting a bogus waypoint.
            nameof(ISpatialService.EvaluateNavigationStep) => NoNavigationPath((NavigationStepRequest)arguments![0]!),
            nameof(ISpatialService.CaptureCharacterContinuation) => Capture((CharacterContinuationCaptureRequest)arguments![0]!),
            nameof(ISpatialService.RestoreCharacterContinuation) => Restore((CharacterContinuationRestoreRequest)arguments![0]!),
            _ => throw new NotSupportedException(method?.Name),
        };

        private static NavigationStepReceipt NoNavigationPath(NavigationStepRequest request) => new(
            NavigationPathOutcome.NoPath, request.Target, default, 0, 0, 0, 0, 0, 0);

        private SpatialContentArtifactReplaceReceipt Replace(SpatialContentArtifactReplaceRequest request)
        {
            ReplaceCalls++;
            LastRequest = request;
            return new(request.Content.Handle.Value, hash, 1, 2, 3, 4, 5, 6, 7, 8);
        }

        private SpatialSession CreateSession()
        {
            CreateSessionCalls++;
            return new SpatialSession(new SpatialSessionHandle(1), () => releases.Add("session"));
        }

        private object? ValidateConfig(CharacterControllerConfig config)
        {
            ConfigValidationCalls++;
            if (RejectConfigValidation) throw new InvalidOperationException("Rejected controller configuration.");
            return null;
        }

        private object? ValidateCommand(CharacterControllerValidationRequest request)
        {
            CommandValidationCalls++;
            return null;
        }

        private SpatialContentArtifactReadout Read()
        {
            ReadCalls++;
            SpatialContentArtifactReplaceRequest request = LastRequest ?? throw new InvalidOperationException("Read before replace.");
            return new(true, request.Content.Handle.Value, hash, 2, 3, 4, 5, 6, 7, 8);
        }

        private CharacterStepReceipt Step(CharacterStepRequest request)
        {
            if (RejectProposedStep) throw new InvalidOperationException("Rejected controller command.");
            StepCalls++;
            StepRequests.Add(request);
            return default(CharacterStepReceipt) with
            {
                Generation = checked((ulong)StepCalls),
                Transform = new Transform(KeepPosition ? request.Position : request.Position + new Vector3(1f, 0f, 0f), Quaternion.Identity, Vector3.One),
                Motion = request.Motion with { Grounded = true, LastCommandSequence = request.Command.Sequence },
                Ground = default(CharacterGround) with { Present = true },
            };
        }

        private CharacterContinuationCheckpoint Capture(CharacterContinuationCaptureRequest request)
        {
            if (request.ExpectedGeneration != checked((ulong)StepCalls)) throw new InvalidOperationException("Stale checkpoint generation.");
            CharacterMotion motion = StepRequests.Last().Motion with { LastCommandSequence = StepRequests.Last().Command.Sequence };
            return new CharacterContinuationCheckpoint(1, request.ExpectedGeneration, 1, 1, 1, RepresentativeValidConfig, motion);
        }

        private static CharacterContinuationRestoreReceipt Restore(CharacterContinuationRestoreRequest request) =>
            new(request.Checkpoint.SourceGeneration, request.Checkpoint.Motion);
    }

    private class RandomMaximum : DispatchProxy
    {
        internal static IRandomService Create() => DispatchProxy.Create<IRandomService, RandomMaximum>();
        protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name == nameof(IRandomService.DrawKeyed)
            ? new KeyedRngReceipt(((KeyedRngRequest)arguments![0]!).Maximum)
            : throw new NotSupportedException(method?.Name);
    }

    private class RandomMinimum : DispatchProxy
    {
        internal IRandomService Service { get; private set; } = null!;

        internal static IRandomService Create()
        {
            IRandomService service = DispatchProxy.Create<IRandomService, RandomMinimum>();
            ((RandomMinimum)(object)service).Service = service;
            return service;
        }

        protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name == nameof(IRandomService.DrawKeyed)
            ? new KeyedRngReceipt(((KeyedRngRequest)arguments![0]!).Minimum)
            : throw new NotSupportedException(method?.Name);
    }

    private class PerceptionFake : DispatchProxy
    {
        internal IPerceptionService Service { get; private set; } = null!;
        internal List<PerceptionQueryRequest> Requests { get; } = [];
        internal PerceptionReadoutLeaseReceipt Receipt { get; set; }

        internal static PerceptionFake Create()
        {
            IPerceptionService service = DispatchProxy.Create<IPerceptionService, PerceptionFake>();
            PerceptionFake proxy = (PerceptionFake)(object)service;
            proxy.Service = service;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? method, object?[]? arguments)
        {
            if (method?.Name != nameof(IPerceptionService.QueryVisibility)) throw new NotSupportedException(method?.Name);
            PerceptionQueryRequest request = (PerceptionQueryRequest)arguments![0]!;
            Requests.Add(request);
            return Receipt;
        }
    }

    /// <summary>Delegates every session to the compiled Daggerfall ruleset while exposing the real session a test created.</summary>
    private sealed class CapturingDaggerfallRuleset : ISaveableGameRuleset
    {
        // Ordinary integration tests exercise gameplay and persistence, not the Engine video runtime.
        private readonly DaggerfallRuleset _inner;
        internal CapturingDaggerfallRuleset(bool videosEnabled = false) => _inner = new(videosEnabled);
        internal DaggerfallSession? Session { get; private set; }
        public RulesetId Id => _inner.Id;

        public IGameSession CreateSession(GameSessionContext context) => Capture(_inner.CreateSession(context));
        public IGameSession CreateSession(GameSessionContext context, RulesetSavePayload saved) => Capture(_inner.CreateSession(context, saved));

        internal DaggerfallSession RequireSession() => Session ?? throw new InvalidOperationException("The delegated Daggerfall ruleset did not create a session.");
        private IGameSession Capture(IGameSession session)
        {
            Session = Assert.IsType<DaggerfallSession>(session);
            return session;
        }
    }

    /// <summary>Small persistence service shared across two fresh EngineContext fakes in the host close/reopen test.</summary>
    private sealed class InMemoryPersistenceService : IPersistenceService
    {
        private readonly Dictionary<(string Scope, string Key), Entry> _values = [];
        private readonly Dictionary<ulong, Entry?> _blobs = [];
        private ulong _nextBlob;

        public PersistenceStore OpenStore(PersistenceOpenRequest request) => new(new PersistenceStoreHandle(1), static () => { });
        public PersistenceSaveReceipt Save(PersistenceSaveRequest request)
        {
            (string Scope, string Key) key = (request.Store.Handle.Value.ToString(), request.Key);
            bool present = _values.TryGetValue(key, out Entry? existing);
            if ((request.RevisionGuard == PersistenceRevisionGuard.Absent && present)
                || (request.RevisionGuard == PersistenceRevisionGuard.Exact && (!present || existing!.Revision != request.ExpectedRevision)))
                return new PersistenceSaveReceipt(PersistenceSaveOutcome.RevisionConflict, existing?.Revision ?? 0);
            ulong revision = present ? checked(existing!.Revision + 1) : 1;
            _values[key] = new(revision, request.Payload.ToArray());
            return new PersistenceSaveReceipt(revision);
        }
        public PersistenceDeleteReceipt Delete(PersistenceDeleteRequest request)
        {
            (string Scope, string Key) key = (request.Store.Handle.Value.ToString(), request.Key);
            _values.TryGetValue(key, out Entry? existing);
            bool matches = request.RevisionGuard switch
            {
                PersistenceRevisionGuard.Any => true,
                PersistenceRevisionGuard.Exact => existing is not null && existing.Revision == request.ExpectedRevision,
                PersistenceRevisionGuard.Absent => existing is null,
                _ => throw new ArgumentOutOfRangeException(nameof(request)),
            };
            if (!matches) return new(PersistenceDeleteOutcome.RevisionConflict, existing?.Revision ?? 0);
            if (existing is null) return new(PersistenceDeleteOutcome.Missing, 0);
            _values.Remove(key);
            return new(PersistenceDeleteOutcome.Deleted, existing.Revision);
        }
        public PersistenceBlob Load(PersistenceLoadRequest request)
        {
            Entry? value = _values.TryGetValue((request.Store.Handle.Value.ToString(), request.Key), out Entry? found) ? found : null;
            ulong handle = ++_nextBlob;
            _blobs.Add(handle, value);
            return new(new PersistenceBlobHandle(handle), static () => { });
        }
        public PersistenceBlobInfo DescribeBlob(PersistenceBlob blob)
        {
            Entry? value = Require(blob);
            return value is null ? new(false, 0, 0) : new(true, value.Revision, checked((nuint)value.Payload.Length));
        }
        public void CopyBlob(PersistenceCopyBlobRequest request) => Require(request.Blob)?.Payload.CopyTo(request.Destination);
        public ReadOnlyMemory<byte> ReadBlobBytes(PersistenceBlob blob) => Require(blob)?.Payload.ToArray() ?? [];
        private Entry? Require(PersistenceBlob blob) => _blobs.TryGetValue(blob.Handle.Value, out Entry? value)
            ? value : throw new InvalidOperationException("Unknown persistence blob.");
        private sealed record Entry(ulong Revision, byte[] Payload);
    }

    private sealed class InputServiceFake : IInputService
    {
        internal ProductInputMapping[] Mappings = [];
        internal InputMappingReplacementOutcome Outcome = InputMappingReplacementOutcome.Staged;
        public InputMappingReplacementOutcome ReplacePhysicalMappings(ReadOnlySpan<ProductInputMapping> mappings)
        {
            if (Outcome == InputMappingReplacementOutcome.Staged) Mappings = mappings.ToArray();
            return Outcome;
        }
    }

    private class EngineContextFake : DispatchProxy
    {
        internal IEngineContext Context { get; private set; } = null!;
        internal int UiOpenCalls { get; private set; }

        /// <summary>Read one named field of the last published projection, or null when none was.</summary>
        internal string? PublishedField(string key) => ((UiServiceFake)(object)ui).Field(key);

        /// <summary>The whole published projection decoded into plain values, or null before one is published.</summary>
        internal object? Published() => ((UiServiceFake)(object)ui).Decoded();

        /// <summary>Every snapshot published so far, decoded in order: one admitted update can publish more than one.</summary>
        internal IReadOnlyList<object?> PublishedHistory() => ((UiServiceFake)(object)ui).History;

        /// <summary>Read one named field of a nested object of the last published projection.</summary>
        internal string? PublishedNested(string parent, string key) => ((UiServiceFake)(object)ui).Nested(parent, key);

        /// <summary>Read one named field of one element of a published array.</summary>
        internal string? PublishedArrayItem(string array, int index, string key) => ((UiServiceFake)(object)ui).ArrayItemField(array, index, key);
        internal readonly InputServiceFake PhysicalInput = new();
        private IContentService content = null!;
        private ISpatialService spatial = null!;
        private IGraphicsService appearance = null!;
        private IPerceptionService perception = null!;
        private ICameraViewService camera = null!;
        private IAudioService audio = null!;
        private IVideoService video = null!;
        private IRandomService random = null!;
        private IUiService ui = null!;
        private IPersistenceService persistence = null!;

        internal static EngineContextFake Create(IContentService content, ISpatialService spatial, IGraphicsService appearance,
            IPerceptionService? perception = null, IPersistenceService? persistence = null, IRandomService? random = null)
        {
            IEngineContext context = DispatchProxy.Create<IEngineContext, EngineContextFake>();
            EngineContextFake fake = (EngineContextFake)(object)context;
            fake.Context = context;
            fake.content = content;
            fake.spatial = spatial;
            fake.appearance = appearance;
            fake.perception = perception ?? PerceptionFake.Create().Service;
            fake.camera = ServiceProxy<ICameraViewService, CameraServiceFake>.Create();
            fake.audio = ServiceProxy<IAudioService, AudioServiceFake>.Create();
            fake.video = ServiceProxy<IVideoService, VideoServiceFake>.Create();
            fake.random = random ?? ServiceProxy<IRandomService, RandomServiceFake>.Create();
            fake.ui = UiServiceFake.Create(fake);
            fake.persistence = persistence ?? new InMemoryPersistenceService();
            return fake;
        }

        protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name switch
        {
            "get_Input" => PhysicalInput,
            "get_Content" => content,
            "get_Spatial" => spatial,
            "get_Graphics" => appearance,
            "get_Perception" => perception,
            "get_CameraView" => camera,
            "get_Audio" => audio,
            "get_Video" => video,
            "get_Random" => random,
            "get_Ui" => ui,
            "get_Persistence" => persistence,
            _ => throw new NotSupportedException(method?.Name),
        };

        private class CameraServiceFake : DispatchProxy
        {
            protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name switch
            {
                nameof(ICameraViewService.CreateCamera) => new Camera(new CameraHandle(1), () => { }),
                nameof(ICameraViewService.UpdateCamera) or nameof(ICameraViewService.SetActiveCamera) or nameof(ICameraViewService.ClearActiveCamera) or nameof(ICameraViewService.SetSkyBackground) or nameof(ICameraViewService.ClearSkyBackground) => null,
                nameof(ICameraViewService.ReplaceCamera) => new Camera(new CameraHandle(1), () => { }),
                _ => throw new NotSupportedException(method?.Name),
            };
        }

        private class RandomServiceFake : DispatchProxy
        {
            protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name == nameof(IRandomService.DrawKeyed)
                ? new KeyedRngReceipt(((KeyedRngRequest)arguments![0]!).Minimum)
                : throw new NotSupportedException(method?.Name);
        }

        private class AudioServiceFake : DispatchProxy
        {
            protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name switch
            {
                nameof(IAudioService.OpenClip) => new AudioClip(new AudioClipHandle(1), static () => { }),
                nameof(IAudioService.OpenClipFromContent) => new AudioClip(new AudioClipHandle(1), static () => { }),
                nameof(IAudioService.Emit) => new AudioSignalHandle(1),
                _ => throw new NotSupportedException(method?.Name),
            };
        }

        private class VideoServiceFake : DispatchProxy
        {
            private ulong _nextHandle;
            protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name switch
            {
                nameof(IVideoService.PlayFromContent) => new VideoPlaybackHandle(++_nextHandle),
                nameof(IVideoService.ReadRealization) => new VideoRealizationReadout(0, 0),
                nameof(IVideoService.Stop) or nameof(IVideoService.Skip) => null,
                _ => throw new NotSupportedException(method?.Name),
            };
        }

        private class UiServiceFake : DispatchProxy
        {
            private EngineContextFake owner = null!;
            internal UiProjection? LastProjection { get; private set; }
            internal static IUiService Create(EngineContextFake parent)
            {
                IUiService service = DispatchProxy.Create<IUiService, UiServiceFake>();
                ((UiServiceFake)(object)service).owner = parent;
                return service;
            }
            protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name switch
            {
                nameof(IUiService.OpenStream) => Open(),
                nameof(IUiService.PublishProjection) => Publish(arguments),
                _ => throw new NotSupportedException(method?.Name),
            };
            private UiStream Open() { owner.UiOpenCalls++; return new UiStream(new UiStreamHandle(1), () => { }); }

            private object? Publish(object?[]? arguments)
            {
                LastProjection = arguments is [UiProjection projection, ..] ? projection : null;
                History.Add(LastProjection is { } published ? Decode(published, published.Value.Root) : null);
                return null;
            }

            /// <summary>Every snapshot this session published, decoded in order.</summary>
            internal List<object?> History { get; } = [];

            /// <summary>The string one named field of the published object carries, or null.</summary>
            internal string? Field(string key)
            {
                if (LastProjection is not { } projection) return null;
                foreach (uint edge in Edges(projection, projection.Value.Root))
                {
                    StructuredValueNode node = projection.Value.Nodes.Span[checked((int)edge)];
                    if (Key(projection, node) != key) continue;
                    return node.Kind == StructuredValueKind.String ? Text(projection, node) : null;
                }

                return null;
            }

            /// <summary>The node index of one named field of the published object, or null.</summary>
            internal uint? Object(string key)
            {
                if (LastProjection is not { } projection) return null;
                foreach (uint edge in Edges(projection, projection.Value.Root))
                {
                    if (Key(projection, projection.Value.Nodes.Span[checked((int)edge)]) == key) return edge;
                }

                return null;
            }

            /// <summary>The string one named field of one published array element carries, or null.</summary>
            internal string? ArrayItemField(string array, int index, string key)
            {
                if (LastProjection is not { } projection || Object(array) is not { } arrayIndex) return null;
                List<uint> items = [.. Edges(projection, arrayIndex)];
                if (index >= items.Count) return null;
                foreach (uint edge in Edges(projection, items[index]))
                {
                    StructuredValueNode node = projection.Value.Nodes.Span[checked((int)edge)];
                    if (Key(projection, node) == key && node.Kind == StructuredValueKind.String) return Text(projection, node);
                }

                return null;
            }

            /// <summary>The string one named field of one nested object carries, or null.</summary>
            internal string? Nested(string parent, string key)
            {
                if (LastProjection is not { } projection || Object(parent) is not { } parentIndex) return null;
                foreach (uint edge in Edges(projection, parentIndex))
                {
                    StructuredValueNode node = projection.Value.Nodes.Span[checked((int)edge)];
                    if (Key(projection, node) == key && node.Kind == StructuredValueKind.String) return Text(projection, node);
                }

                return null;
            }

            /// <summary>The whole published value decoded into objects, arrays, strings and numbers.</summary>
            internal object? Decoded() => LastProjection is { } projection ? Decode(projection, projection.Value.Root) : null;

            private static object? Decode(UiProjection projection, uint index)
            {
                StructuredValueNode node = projection.Value.Nodes.Span[checked((int)index)];
                return node.Kind switch
                {
                    StructuredValueKind.Null => null,
                    StructuredValueKind.String => Text(projection, node),
                    StructuredValueKind.Number => node.NumberValue,
                    StructuredValueKind.Array => Edges(projection, index).Select(edge => Decode(projection, edge)).ToArray(),
                    StructuredValueKind.Object => Edges(projection, index).ToDictionary(
                        edge => Key(projection, projection.Value.Nodes.Span[checked((int)edge)]),
                        edge => Decode(projection, edge),
                        StringComparer.Ordinal),
                    _ => (object?)node.Kind,
                };
            }

            private static IEnumerable<uint> Edges(UiProjection projection, uint index)
            {
                StructuredValueNode node = projection.Value.Nodes.Span[checked((int)index)];
                for (uint offset = 0; offset < node.ChildCount; offset++) yield return projection.Value.Edges.Span[checked((int)(node.FirstEdge + offset))];
            }

            private static string Key(UiProjection projection, StructuredValueNode node) =>
                System.Text.Encoding.UTF8.GetString(projection.Value.Utf8.Span[checked((int)node.KeyOffset)..checked((int)(node.KeyOffset + node.KeyLen))]);

            private static string Text(UiProjection projection, StructuredValueNode node) =>
                System.Text.Encoding.UTF8.GetString(projection.Value.Utf8.Span[checked((int)node.TextOffset)..checked((int)(node.TextOffset + node.TextLen))]);
        }
    }

    private class ServiceProxy<TService, TProxy> : DispatchProxy where TService : class where TProxy : DispatchProxy
    {
        internal static TService Create()
        {
            return DispatchProxy.Create<TService, TProxy>();
        }
        protected override object? Invoke(MethodInfo? method, object?[]? arguments) => throw new NotSupportedException(method?.Name);
    }

    private class KeyedRandomFake : DispatchProxy
    {
        internal IRandomService Service { get; private set; } = null!;
        internal List<KeyedRngRequest> Requests { get; } = [];
        private long value;

        internal static KeyedRandomFake Create(long returnedValue)
        {
            IRandomService service = DispatchProxy.Create<IRandomService, KeyedRandomFake>();
            KeyedRandomFake fake = (KeyedRandomFake)(object)service;
            fake.Service = service;
            fake.value = returnedValue;
            return fake;
        }

        protected override object? Invoke(MethodInfo? method, object?[]? arguments)
        {
            if (method?.Name != nameof(IRandomService.DrawKeyed)) throw new NotSupportedException(method?.Name);
            KeyedRngRequest request = (KeyedRngRequest)arguments![0]!;
            Requests.Add(request);
            return new KeyedRngReceipt(Math.Clamp(value, request.Minimum, request.Maximum));
        }
    }

    private class AudioRecorder : DispatchProxy
    {
        internal IAudioService Service { get; private set; } = null!;
        internal List<AudioEmitRequest> Emits { get; } = [];
        internal int ReleasedClips { get; private set; }
        private ulong nextHandle = 1;

        internal static AudioRecorder Create()
        {
            IAudioService service = DispatchProxy.Create<IAudioService, AudioRecorder>();
            AudioRecorder recorder = (AudioRecorder)(object)service;
            recorder.Service = service;
            return recorder;
        }

        protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name switch
        {
            nameof(IAudioService.OpenClip) => new AudioClip(new AudioClipHandle(nextHandle++), () => ReleasedClips++),
            nameof(IAudioService.Emit) => Emit((AudioEmitRequest)arguments![0]!),
            _ => throw new NotSupportedException(method?.Name),
        };

        private AudioSignalHandle Emit(AudioEmitRequest request)
        {
            Emits.Add(request);
            return new AudioSignalHandle(nextHandle++);
        }
    }

    private sealed class AppearanceFake(List<string> releases) : IGraphicsService
    {
        internal List<RenderResourceRequest> OpenResourceRequests { get; } = [];
        internal List<StaticMeshContentAppearanceRequest> StaticMeshContentRequests { get; } = [];
        internal List<MeshMaterialBinding> StaticMeshBindings { get; } = [];
        internal List<SpriteAtlasCreateRequest> AtlasRequests { get; } = [];
        internal List<SpriteFromAtlasRequest> SpriteRequests { get; } = [];
        internal List<SpritePlaybackCreateRequest> PlaybackRequests { get; } = [];
        internal List<SpritePlaybackControlRequest> ControlRequests { get; } = [];
        internal List<SpritePlaybackAdvanceRequest> AdvanceRequests { get; } = [];
        internal List<AppearanceFact[]> Snapshots { get; } = [];
        internal List<SpriteFrameUpdateRequest> SetFrameRequests { get; } = [];
        internal List<SpritePlayback> CreatedPlaybacks { get; } = [];
        internal Queue<SpritePlaybackAdvanceLeaseReceipt> AdvanceReceipts { get; } = [];
        internal int CreatedAtlases { get; private set; }
        internal int DisposedAtlases { get; private set; }
        internal int CreatedAppearances { get; private set; }
        internal int DisposedAppearances { get; private set; }
        internal int DisposedPlaybacks { get; private set; }
        internal List<SpritePlaybackHandle> DisposedPlaybackHandles { get; } = [];
        internal int FailSpritePlaybackCreateAt { get; set; }
        internal int FailSpritePlaybackControlAt { get; set; }
        internal int FailPublishAt { get; set; }
        internal bool RejectLateResourceOpen { get; set; }
        internal bool RejectDisposeOfRetainedAppearance { get; set; }
        internal int PublishCalls { get; private set; }
        internal ulong LastCrossingSequence { get; private set; }
        internal IReadOnlyCollection<Appearance> RetainedAppearances => retainedAppearances;
        private readonly List<Action> pendingPlaybackCommits = [];
        private readonly List<Action> pendingPlaybackRollbacks = [];
        private readonly HashSet<Appearance> retainedAppearances = new(ReferenceEqualityComparer.Instance);
        private ulong nextHandle = 1;

        public RenderResourceInfo OpenResource(RenderResourceRequest request)
        {
            if (RejectLateResourceOpen) throw new InvalidOperationException("Render resource selection is sealed after product creation.");
            OpenResourceRequests.Add(request);
            return new(OwnResource(checked((ulong)OpenResourceRequests.Count)), default, 0);
        }
        public RenderResourceInfo OpenResourceFromContent(RenderResourceContentRequest request)
        {
            if (RejectLateResourceOpen) throw new InvalidOperationException("Render resource selection is sealed after product creation.");
            OpenResourceContentRequests.Add(request);
            return new(OwnResource(checked((ulong)OpenResourceContentRequests.Count)), default, 0);
        }
        internal List<RenderResourceContentRequest> OpenResourceContentRequests { get; } = [];

        /// <summary>Counts how many opened render resources the caller released.</summary>
        internal int ReleasedResources { get; private set; }

        private RenderResource OwnResource(ulong handle) => new(new RenderResourceHandle(handle), () =>
        {
            ReleasedResources++;
            releases.Add("resource");
        });
        public Material CreateMaterial(MaterialRequest request) => new(new MaterialHandle(1), () => releases.Add("material"));
        public Material CreateAuthoredMaterial(AuthoredMaterialAppearanceRequest request) => CreateMaterial(default);
        public void UpdateMaterial(MaterialUpdateRequest request) { }
        public Material ReplaceMaterial(MaterialUpdateRequest request) => CreateMaterial(request.Replacement);
        public Appearance CreatePrimitive(PrimitiveAppearanceRequest request) => CreateAppearance();
        public Appearance ReplacePrimitive(PrimitiveAppearanceReplaceRequest request) => CreateAppearance();
        public Appearance CreateStaticMesh(StaticMeshAppearanceRequest request) => CreateAppearance();
        public MeshResource CreateMeshResource(MeshResourceCreateRequest request) => throw new NotSupportedException();
        public Appearance CreateMeshAppearance(MeshResource resource) => throw new NotSupportedException();
        public MeshPartition PartitionMesh(MeshPartitionRequest request) => throw new NotSupportedException();
        public MeshPartitionReadout ReadMeshPartition(MeshPartition partition) => throw new NotSupportedException();
        public MeshResource TakeMeshPartitionPart(MeshPartitionPartRequest request) => throw new NotSupportedException();
        public Appearance CreateStaticMeshFromContent(StaticMeshContentAppearanceRequest request) { StaticMeshContentRequests.Add(request); return CreateAppearance(); }
        public Appearance CreateStaticMeshFromContentReference(StaticMeshContentReferenceRequest request) => CreateAppearance();
        public Appearance ReplaceStaticMesh(Appearance appearance, StaticMeshAppearanceRequest request) => CreateAppearance();
        public Appearance ReplaceStaticMeshFromContent(Appearance appearance, StaticMeshContentAppearanceRequest request) => CreateAppearance();
        public void UpdateStaticMeshMaterials(StaticMeshMaterialUpdateRequest request) => StaticMeshBindings.AddRange(request.Bindings.ToArray());
        public Appearance CreateSprite(SpriteAppearanceRequest request) => CreateAppearance();
        public Appearance ReplaceSprite(SpriteAppearanceReplaceRequest request) => CreateAppearance();
        public SpriteAtlas CreateSpriteAtlas(SpriteAtlasCreateRequest request)
        {
            AtlasRequests.Add(request);
            CreatedAtlases++;
            return new(new SpriteAtlasHandle(nextHandle++), () => { DisposedAtlases++; releases.Add("atlas"); });
        }
        public Appearance CreateSpriteFromAtlas(SpriteFromAtlasRequest request) { SpriteRequests.Add(request); return CreateAppearance(); }
        public Appearance ReplaceSpriteFromAtlas(SpriteFromAtlasReplaceRequest request) => CreateAppearance();
        public void SetSpriteViewport(SpriteViewportUpdateRequest request) => ViewportRequests.Add(request);
        internal List<SpriteViewportUpdateRequest> ViewportRequests { get; } = [];
        public void SetSpriteFrame(SpriteFrameUpdateRequest request) => SetFrameRequests.Add(request);
        public SpriteReadout ReadSprite(Appearance appearance) => default;
        public SpritePlayback CreateSpritePlayback(SpritePlaybackCreateRequest request)
        {
            PlaybackRequests.Add(request);
            if (FailSpritePlaybackCreateAt == PlaybackRequests.Count) throw new InvalidOperationException("Injected sprite playback create failure.");
            SpritePlaybackHandle handle = new(nextHandle++);
            SpritePlayback playback = new(handle, () =>
            {
                DisposedPlaybacks++;
                DisposedPlaybackHandles.Add(handle);
                releases.Add("playback");
            }, static () => false, (commit, rollback) =>
            {
                pendingPlaybackCommits.Add(commit);
                pendingPlaybackRollbacks.Add(rollback);
            });
            CreatedPlaybacks.Add(playback);
            return playback;
        }
        public SpritePlaybackReadout ControlSpritePlayback(SpritePlaybackControlRequest request)
        {
            ControlRequests.Add(request);
            if (FailSpritePlaybackControlAt == ControlRequests.Count) throw new InvalidOperationException("Injected sprite playback control failure.");
            return default;
        }
        public SpritePlaybackReadout SelectSpritePlaybackFrame(SpritePlaybackFrameSelectionRequest request) => default;
        /// <summary>When set, every advanced playback reports this receipt, so a crossing reaches whichever actor is attacking.</summary>
        internal SpritePlaybackAdvanceLeaseReceipt? AdvanceReceiptForAll { get; set; }
        public SpritePlaybackAdvanceLeaseReceipt AdvanceSpritePlayback(SpritePlaybackAdvanceRequest request)
        {
            AdvanceRequests.Add(request);
            SpritePlaybackAdvanceLeaseReceipt receipt = AdvanceReceiptForAll ?? (AdvanceReceipts.Count == 0 ? default : AdvanceReceipts.Dequeue());
            foreach (SpritePlaybackMarkerCrossing crossing in receipt.Crossings.Span) LastCrossingSequence = Math.Max(LastCrossingSequence, crossing.CrossingSequence);
            return receipt;
        }
        public SpritePlaybackSample SampleSpritePlayback(SpritePlaybackSampleRequest request) => default;
        public SpritePlaybackReadout ReadSpritePlayback(SpritePlayback playback) => default;
        public void PublishSnapshot(ReadOnlySpan<AppearanceFact> values)
        {
            PublishCalls++;
            Snapshots.Add(values.ToArray());
            retainedAppearances.Clear();
            foreach (AppearanceFact value in values) retainedAppearances.Add(value.Appearance);
            if (FailPublishAt == PublishCalls) throw new InvalidOperationException("Injected presentation publish failure.");
        }
        public Light CreateLight(LightRequest request) => new(new LightHandle(1), () => { });
        public void UpdateLight(LightUpdateRequest request) { }
        public Light ReplaceLight(LightUpdateRequest request) => NewLight(request.Replacement);
        public LightReadout ReadLight(Light light) => default;
        public PresentationReadout ReadPresentation() => default;

        private Appearance CreateAppearance()
        {
            CreatedAppearances++;
            Appearance value = null!;
            value = new(new AppearanceHandle(nextHandle++), () =>
            {
                if (RejectDisposeOfRetainedAppearance && retainedAppearances.Contains(value)) throw new InvalidOperationException("CSHARP_APPEARANCE_IN_USE");
                DisposedAppearances++;
                releases.Add("appearance");
            });
            return value;
        }
        internal void CommitPendingPlaybackReleases()
        {
            foreach (Action commit in pendingPlaybackCommits) commit();
            pendingPlaybackCommits.Clear();
            pendingPlaybackRollbacks.Clear();
        }
        internal void RollbackPendingPlaybackReleases()
        {
            foreach (Action rollback in pendingPlaybackRollbacks) rollback();
            pendingPlaybackCommits.Clear();
            pendingPlaybackRollbacks.Clear();
        }
        private static Light NewLight(LightRequest request) => new(new LightHandle(1), () => { });
    }
}
