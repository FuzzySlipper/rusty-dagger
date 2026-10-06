using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Effects;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>Session save and restore: mechanics, quests, effects, calendar state and refusal of malformed saves.</summary>
public sealed class SessionPersistenceTests
{
    [Fact]
    public void Ordinary_rest_opens_a_saved_level_allocation_that_commits_one_health_gain_after_reload()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), PerceptionFake.Create().Service);
        DaggerfallSavePayload saved;
        using (DaggerfallSession original = DaggerfallSession.StartNew(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults)))
        {
            foreach (DaggerfallStatId skill in definitions.Vocabulary.Skills)
                original.State.Actors.Player.Stats.GetStat(StatId.Parse(skill.Value)).BaseValue = 100;
            _ = original.AdvanceElapsedTime(361);
            DaggerfallLevelUpSave pending = Assert.IsType<DaggerfallLevelUpSave>(original.State.LevelUps.Pending);
            Assert.Equal(1, original.State.Progression.Level);
            long wagonId = original.State.Wagon.EnsureCreated().Id;
            saved = DaggerfallSavePayload.Read(original.CaptureSave());
            Assert.NotNull(saved.RegionalPrices);
            Assert.Equal(original.State.RegionalPrices.Factors, saved.RegionalPrices.Factors);
            Assert.Equal(wagonId, Assert.IsType<WorldRpg.Rulesets.Daggerfall.Modules.Transport.DaggerfallWagonSave>(saved.Wagon).Id);
            AssertLevelUpEqual(pending, Assert.IsType<DaggerfallLevelUpSave>(saved.LevelUp));
        }

        ContentFake resumedContent = new(releases);
        PopulateContent(resumedContent, inputs);
        SpatialFake resumedSpatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake resumedEngine = EngineContextFake.Create(resumedContent, resumedSpatial.Service, new AppearanceFake(releases), PerceptionFake.Create().Service);
        ResolvedCompositionIdentity identity = GameCompositionResolver.Resolve(FullContent(root), new GameBundleId("daggerfall.classic")).RequireComposition().Identity;
        using DaggerfallSession restored = DaggerfallSession.Restore(resumedEngine.Context, new(definitions, inputs, DaggerfallTuning.Defaults, identity), DaggerfallSavePayload.Encode(saved));
        Assert.Equal(saved.RegionalPrices!.Factors, restored.State.RegionalPrices.Factors);
        Assert.Equal(saved.RegionalPrices.LastAdvancedDay, restored.State.RegionalPrices.LastAdvancedDay);
        Assert.Equal(saved.Wagon!.Id, restored.State.Wagon.Current?.Id);

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
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), PerceptionFake.Create().Service);

        DaggerfallSavePayload saved;
        using (DaggerfallSession original = DaggerfallSession.StartNew(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults)))
        {
            DaggerfallSavePayload baseline = DaggerfallSavePayload.Read(original.CaptureSave());
            ulong rewardItem = baseline.Inventory.UniqueItems.First().EntityId;
            DaggerfallStackSave questGold = baseline.Inventory.Stacks.First();
            DaggerfallSiteRecord place = definitions.Locations.Records.First();
            long giver = original.State.Npcs.RegisterStable(DaggerfallNpcKind.Questor, "persistent-giver",
                new(inputs.Site!.Value.Region, definitions.Locations.Records.First(record => record.Id == inputs.Site.Value).Name, ""),
                new("Breton", "Male", 0, 0, 0, 0), "quest giver", ["talk"]);
            DaggerfallQuestResourceState[] resources =
            [
                new("_questgiver_", DaggerfallQuestResourceBinding.Actors(giver)),
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
        ResolvedCompositionIdentity identity = GameCompositionResolver.Resolve(FullContent(root), new GameBundleId("daggerfall.classic")).RequireComposition().Identity;
        using DaggerfallSession resumed = DaggerfallSession.Restore(resumedEngine.Context, new(definitions, inputs, DaggerfallTuning.Defaults, identity), DaggerfallSavePayload.Encode(saved));

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
        Assert.Throws<ArgumentException>(() => DaggerfallSession.Restore(resumedEngine.Context, new(definitions, inputs, DaggerfallTuning.Defaults, identity), DaggerfallSavePayload.Encode(missingDefinition)));

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
        Assert.Throws<ArgumentException>(() => DaggerfallSession.Restore(resumedEngine.Context, new(definitions, inputs, DaggerfallTuning.Defaults, identity), DaggerfallSavePayload.Encode(dangling)));

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
        Assert.Throws<ArgumentException>(() => DaggerfallSession.Restore(resumedEngine.Context, new(definitions, inputs, DaggerfallTuning.Defaults, identity), DaggerfallSavePayload.Encode(missingStack)));
    }

    [Fact]
    public void Running_calendar_fatigue_pending_before_a_minute_boundary_survives_save_and_restores_once()
    {
        using DaggerfallSession session = FreshSession();
        Track stamina = session.State.Actors.Player.Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Stamina.Value));
        double maximum = stamina.MaximumValue;
        session.Update(new ProductUpdate(
            new ProductUpdateFacts(ProductUpdateMode.Realtime, ProductLifecycleState.Running, 1, 1, 1, 1, 60, 1, 0, 4d),
            [Input(InputEventKind.Key, InputEdge.Pressed, KeyboardControl.KeyW), PhysicalKey(KeyboardControl.KeyW), PhysicalKey(KeyboardControl.ShiftLeft)]));

        Assert.Equal(maximum, stamina.Current);
        RulesetSavePayload payload = session.CaptureSave();

        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        ResolvedCompositionIdentity identity = GameCompositionResolver.Resolve(FullContent(root), new GameBundleId("daggerfall.classic")).RequireComposition().Identity;
        using DaggerfallSession restored = DaggerfallSession.Restore(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults, identity), payload);

        restored.Update(new ProductUpdate(
            new ProductUpdateFacts(ProductUpdateMode.Realtime, ProductLifecycleState.Running, 1, 1, 2, 2, 60, 1, 0, 1d),
            [Input(InputEventKind.Key, InputEdge.Pressed, KeyboardControl.KeyW), PhysicalKey(KeyboardControl.KeyW), PhysicalKey(KeyboardControl.ShiftLeft)]));

        Assert.Equal(maximum - 88d, restored.State.Actors.Player.Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Stamina.Value)).Current);

        static ProductInputEvent PhysicalKey(KeyboardControl key) => Input(InputEventKind.Key, InputEdge.Pressed, key) with
        {
            Device = InputDevice.Keyboard,
            Channel = InputChannel.Button,
            ValueKind = InputValueKind.Digital,
            Phase = InputPhase.Pressed,
            Provenance = InputProvenance.Physical,
        };
    }

    [Fact]
    public void Current_save_roundtrip_rebuilds_stats_aliases_sources_tracks_and_modifier_handles()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake sourceContent = new(releases);
        PopulateContent(sourceContent, inputs);
        SpatialFake sourceSpatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake source = EngineContextFake.Create(sourceContent, sourceSpatial.Service, new AppearanceFake(releases));
        RulesetSavePayload payload;
        StatId maximumId = StatId.Parse("health-maximum");
        TrackId healthId = TrackId.Parse("health");
        using (DaggerfallSession original = DaggerfallSession.StartNew(source.Context, new(definitions, inputs, DaggerfallTuning.Defaults)))
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
        ResolvedCompositionIdentity identity = GameCompositionResolver.Resolve(FullContent(root), new GameBundleId("daggerfall.classic")).RequireComposition().Identity;
        using DaggerfallSession resumed = DaggerfallSession.Restore(resumedEngine.Context, new(definitions, inputs, DaggerfallTuning.Defaults, identity), payload);

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
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
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
        using (DaggerfallSession original = DaggerfallSession.StartNew(source.Context, new(definitions, inputs, DaggerfallTuning.Defaults)))
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
        ResolvedCompositionIdentity identity = GameCompositionResolver.Resolve(FullContent(root), new GameBundleId("daggerfall.classic")).RequireComposition().Identity;
        using DaggerfallSession restored = DaggerfallSession.Restore(restoredEngine.Context, new(definitions, inputs, DaggerfallTuning.Defaults, identity), payload);

        Assert.Equal(expected, restored.State.Social.ReactionForNpc(unloaded));
        Assert.Equal(expectedEligibility, restored.State.Social.GuildEligibility(faction.Id));
        Assert.Equal(-4, restored.State.Social.RegionalReputation(0));
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

        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        ResolvedCompositionIdentity identity = GameCompositionResolver.Resolve(FullContent(root), new GameBundleId("daggerfall.classic")).RequireComposition().Identity;
        using DaggerfallSession restored = DaggerfallSession.Restore(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults, identity) { Effects = TimedEffectCatalog() }, payload);

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
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
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
        using (DaggerfallSession source = DaggerfallSession.StartNew(sourceEngine.Context, new(definitions, inputs, DaggerfallTuning.Defaults) { Effects = EffectCatalog(() => sourceRounds++, () => applyCalls++, () => resumeCalls++, () => removals++) }))
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
        ResolvedCompositionIdentity identity = GameCompositionResolver.Resolve(FullContent(root), new GameBundleId("daggerfall.classic")).RequireComposition().Identity;
        using (DaggerfallSession restored = DaggerfallSession.Restore(restoredEngine.Context, new(definitions, inputs, DaggerfallTuning.Defaults, identity) { Effects = EffectCatalog(() => restoredRounds++, () => applyCalls++, () => resumeCalls++, () => removals++) }, payload))
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

    [Fact]
    public void Current_save_rejects_malformed_bytes_and_missing_content_definitions_before_a_session_is_published()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
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
        ResolvedCompositionIdentity identity = GameCompositionResolver.Resolve(FullContent(root), new GameBundleId("daggerfall.classic")).RequireComposition().Identity;

        Assert.Throws<ArgumentException>(() => DaggerfallSession.Restore(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults, identity), DaggerfallSavePayload.Encode(missingDefinition)));
        DaggerfallSavePayload missingActorInventory = saved with { ActorInventories = [] };
        Assert.Throws<ArgumentException>(() => DaggerfallSession.Restore(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults, identity), DaggerfallSavePayload.Encode(missingActorInventory)));
        Assert.Equal(0, spatial.StepCalls);
    }

    [Fact]
    public void Current_save_rejects_book_identity_that_is_not_a_readable_catalog_book()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        DaggerfallSavePayload valid = CapturedSave(root);
        DaggerfallItemDefinition nonBook = definitions.TemplateItems.Values
            .First(definition => definition.IsFungible && definition.Template?.Groups.Contains("Books", StringComparer.Ordinal) != true);
        DaggerfallBookDefinition unreadable = definitions.Books.Books.Values
            .First(book => book.Disposition != DaggerfallBookDisposition.Read);

        foreach ((DaggerfallItemDefinition item, int bookId, string expected) in new[]
        {
            (nonBook, 59, "not a book"),
            (definitions.RequireItem(new DaggerfallItemId("template-277")), 999999, "unpublished"),
            (definitions.RequireItem(new DaggerfallItemId("template-277")), unreadable.BookId, "unreadable"),
        })
        {
            DaggerfallItemMetadataSave metadata = (DaggerfallItemInstanceMetadata.Default(item, DaggerfallItemOwner.Player) with { BookId = bookId }).Capture();
            DaggerfallSavePayload forged = valid with
            {
                Inventory = valid.Inventory with
                {
                    Stacks = [new DaggerfallStackSave($"forged-book-{bookId}", item.Id.Value, 1, metadata)],
                },
            };

            ArgumentException exception = Assert.Throws<ArgumentException>(() => forged.ResolveRestore(definitions, inputs));
            Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
        }

        DaggerfallItemDefinition bookItem = definitions.RequireItem(new DaggerfallItemId("template-277"));
        DaggerfallItemMetadataSave missingBookId = DaggerfallItemInstanceMetadata.Default(bookItem, DaggerfallItemOwner.Player).Capture();
        DaggerfallSavePayload missingBookIdentity = valid with
        {
            Inventory = valid.Inventory with
            {
                Stacks = [new DaggerfallStackSave("forged-book-missing-id", bookItem.Id.Value, 1, missingBookId)],
            },
        };
        ArgumentException missingBookException = Assert.Throws<ArgumentException>(() => missingBookIdentity.ResolveRestore(definitions, inputs));
        Assert.Contains("no selected book identity", missingBookException.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Current_save_rejects_ground_container_for_an_unadmitted_world_profile()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        DaggerfallSavePayload valid = CapturedSave(root);
        DaggerfallWorldProfileKey unknown = inputs.ProfileKey with { LogicalId = "unadmitted-profile" };
        DaggerfallSavePayload forged = valid with
        {
            GroundContainers =
            [
                new DaggerfallGroundContainerSave(
                    DaggerfallWorldProfileKeySave.Capture(unknown),
                    999,
                    0F,
                    0F,
                    0F,
                    new DaggerfallInventorySave([], [], [])),
            ],
        };

        ArgumentException exception = Assert.Throws<ArgumentException>(() => forged.ResolveRestore(definitions, inputs));
        Assert.Contains("unadmitted world profile", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Current_save_keeps_relative_cooldown_but_starts_a_fresh_spatial_continuation()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake sourceContent = new(releases);
        PopulateContent(sourceContent, inputs);
        SpatialFake sourceSpatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake source = EngineContextFake.Create(sourceContent, sourceSpatial.Service, new AppearanceFake(releases));
        RulesetSavePayload payload;
        using (DaggerfallSession original = DaggerfallSession.StartNew(source.Context, new(definitions, inputs, DaggerfallTuning.Defaults)))
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
        ResolvedCompositionIdentity identity = GameCompositionResolver.Resolve(FullContent(root), new GameBundleId("daggerfall.classic")).RequireComposition().Identity;
        using DaggerfallSession resumed = DaggerfallSession.Restore(resumedEngine.Context, new(definitions, inputs, DaggerfallTuning.Defaults, identity), payload);
        double before = resumed.State.Actors.Get(2000).Stats.GetTrack(TrackId.Parse("health")).Current;

        resumed.ResolveExplicitMelee(new ExplicitMeleeRequest(1, 2000, 2, 1, .125));

        Assert.Equal(before, resumed.State.Actors.Get(2000).Stats.GetTrack(TrackId.Parse("health")).Current);
        Assert.Equal(0, resumedSpatial.StepCalls);
    }

    [Fact]
    public void Default_disease_catalog_restores_live_modifiers_without_replaying_damage_and_cures_its_source()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        EngineContextFake Engine() => EngineContextFake.Create(content,
            SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases).Service, new AppearanceFake(releases), random: RandomMaximum.Create());
        EngineContextFake engine = Engine();
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults));
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
        var identity = GameCompositionResolver.Resolve(FullContent(root), new GameBundleId("daggerfall.classic")).RequireComposition().Identity;
        using DaggerfallSession restored = DaggerfallSession.Restore(restoredEngine.Context, new(definitions, inputs, DaggerfallTuning.Defaults, identity), save);
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
}
