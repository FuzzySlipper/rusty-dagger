using System.Reflection;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Facts;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.Progression;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using Xunit;
using EngineItemDefinition = Rusty.Engine.Mechanics.ItemDefinition;
using EngineItemDefinitionId = Rusty.Engine.Mechanics.ItemDefinitionId;
using KitInventoryItemId = WorldRpg.Kit.Inventory.InventoryItemId;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallRewardReactionTests
{
    [Fact]
    public void Generated_unique_loot_allocator_skips_composed_entity_reservations()
    {
        DaggerfallUniqueItemAllocator allocator = new(1_000, [1_000, 1_001]);

        Assert.Equal(1_002UL, allocator.Allocate());
        Assert.Equal(1_003UL, allocator.Allocate());
    }

    [Fact]
    public void Daggerfall_actor_construction_uses_one_engine_pair_for_health_only()
    {
        DaggerfallDefinitions definitions = LoadDefinitions();
        DaggerfallActorDefinition player = definitions.RequireActor(new DaggerfallActorId("player"));
        ActorMechanicsState mechanics = new DaggerfallMechanicsState().CreateActor(
            player,
            player.PlayerInitialVitals,
            DaggerfallActorIdentity.PlayerEntityId);

        ExactStatTrackState health = mechanics.ReadStatTrack(TrackId.Parse("health"));
        Assert.Equal(player.PlayerInitialVitals.HealthMaximum, health.Read().Stat.Value.Raw);
        Assert.Equal(player.PlayerInitialVitals.HealthMaximum, health.Read().TrackCurrent.Raw);
        Assert.Throws<MechanicsException>(() => mechanics.ReadStatTrack(TrackId.Parse("stamina")));
        Assert.Equal(player.PlayerInitialVitals.StaminaMaximum, mechanics.ReadTrack(TrackId.Parse("stamina")).Current.Raw);
    }

    [Fact]
    public void Failed_loot_grant_does_not_award_xp_and_retry_is_exactly_once()
    {
        DaggerfallDefinitions definitions = LoadDefinitions();
        DaggerfallActorDefinition player = definitions.RequireActor(new DaggerfallActorId("player"));
        DaggerfallActorDefinition thief = definitions.RequireActor(new DaggerfallActorId("thief"));
        Dictionary<long, DaggerfallActorDefinition> actors = new() { [9000] = thief };
        EntityId owner = new(DaggerfallActorIdentity.PlayerEntityId);
        InventoryWorld world = new();
        world.RegisterInventory(new InventoryState(owner));
        world.RegisterEquipment(new EquipmentState(owner));
        Dictionary<KitInventoryItemId, EngineItemDefinition> managed = new()
        {
            [new KitInventoryItemId("gold-piece")] = ToManaged(definitions.Items[new DaggerfallItemId("gold-piece")]),
        };
        MechanicsInventoryCoordinator inventory = new(world, owner, managed);
        ProgressionState progression = new();
        DaggerfallRewardReactions reactions = new(
            progression,
            CreatePlayerMechanics(player),
            player,
            RandomMinimums(),
            actors);
        ActorDiedFact death = new(9000, DaggerfallActorIdentity.PlayerEntityId, 5, 2, 3);
        FactBuffer<IProductFact> facts = new();

        reactions.React(death, facts);
        reactions.React(death, facts);
        Assert.Equal(50, progression.Experience);
        Assert.Equal(1, progression.Level);
        List<IProductFact> delivered = [];
        facts.Deliver(delivered.Add);
        Assert.Single(delivered.OfType<ExperienceAwardedFact>());
        Assert.Empty(delivered.OfType<LootAwardedFact>());
    }

    [Fact]
    public void Death_by_non_player_does_not_award_player_rewards()
    {
        DaggerfallDefinitions definitions = LoadDefinitions();
        DaggerfallActorDefinition player = definitions.RequireActor(new DaggerfallActorId("player"));
        DaggerfallActorDefinition thief = definitions.RequireActor(new DaggerfallActorId("thief"));
        EntityId owner = new(DaggerfallActorIdentity.PlayerEntityId);
        InventoryWorld world = new();
        world.RegisterInventory(new InventoryState(owner));
        world.RegisterEquipment(new EquipmentState(owner));
        MechanicsInventoryCoordinator inventory = new(world, owner, new Dictionary<KitInventoryItemId, EngineItemDefinition>());
        ProgressionState progression = new();
        DaggerfallRewardReactions reactions = new(
            progression,
            CreatePlayerMechanics(player),
            player,
            RandomMinimums(),
            new Dictionary<long, DaggerfallActorDefinition> { [9000] = thief });

        reactions.React(new ActorDiedFact(9000, 777, 5, 2, 3), new FactBuffer<IProductFact>());

        Assert.Equal(0, progression.Experience);
        Assert.Empty(inventory.Read().Stacks);
    }

    [Fact]
    public void Exact_xp_threshold_applies_one_keyed_health_source_and_preserves_health_distance()
    {
        DaggerfallDefinitions definitions = LoadDefinitions();
        DaggerfallActorDefinition player = definitions.RequireActor(new DaggerfallActorId("player"));
        DaggerfallActorDefinition defeated = definitions.RequireActor(new DaggerfallActorId("thief")) with
        {
            Rewards = new DaggerfallRewardPolicy(500),
            LootTableKey = null,
        };
        ActorMechanicsState mechanics = CreatePlayerMechanics(player, healthCurrent: 70);
        ProgressionState progression = new();
        (IRandomService random, RecordingRandomProxy recorder) = RecordingRandom(8);
        DaggerfallRewardReactions reactions = CreateReactions(definitions, player, mechanics, progression, random, new Dictionary<long, DaggerfallActorDefinition> { [9000] = defeated });

        reactions.React(new ActorDiedFact(9000, DaggerfallActorIdentity.PlayerEntityId, 5, 2, 3), new FactBuffer<IProductFact>());

        Assert.Equal(500, progression.Experience);
        Assert.Equal(2, progression.Level);
        Assert.Equal(107, mechanics.ReadStat(StatId.Parse("health-maximum")).Value.Raw);
        Assert.Equal(77, mechanics.ReadTrack(TrackId.Parse("health")).Current.Raw);
        ExactStatTrackState health = mechanics.ReadStatTrack(TrackId.Parse("health"));
        ExactSource source = Assert.Single(health.Sources);
        Assert.Equal("daggerfall.player.level-up.2.health", ((IntrinsicSourceIdentity)source.Identity).Instance.Value);
        KeyedRngRequest roll = Assert.Single(recorder.Requests);
        Assert.Equal(CombatRandomKey.PlayerScope, roll.Scope);
        Assert.Equal("player.level-up.2.hp-roll", roll.Key);
        Assert.Equal(4, roll.Minimum);
        Assert.Equal(8, roll.Maximum);
    }

    [Fact]
    public void One_reward_can_cross_multiple_xp_thresholds_without_replacing_prior_health_sources()
    {
        DaggerfallDefinitions definitions = LoadDefinitions();
        DaggerfallActorDefinition player = definitions.RequireActor(new DaggerfallActorId("player"));
        DaggerfallActorDefinition defeated = definitions.RequireActor(new DaggerfallActorId("thief")) with
        {
            Rewards = new DaggerfallRewardPolicy(1_000),
            LootTableKey = null,
        };
        ActorMechanicsState mechanics = CreatePlayerMechanics(player, healthCurrent: 50);
        ProgressionState progression = new();
        (IRandomService random, RecordingRandomProxy recorder) = RecordingRandom(4, 8);
        DaggerfallRewardReactions reactions = CreateReactions(definitions, player, mechanics, progression, random, new Dictionary<long, DaggerfallActorDefinition> { [9000] = defeated });

        reactions.React(new ActorDiedFact(9000, DaggerfallActorIdentity.PlayerEntityId, 5, 2, 3), new FactBuffer<IProductFact>());

        Assert.Equal(1_000, progression.Experience);
        Assert.Equal(3, progression.Level);
        Assert.Equal(110, mechanics.ReadStat(StatId.Parse("health-maximum")).Value.Raw);
        Assert.Equal(60, mechanics.ReadTrack(TrackId.Parse("health")).Current.Raw);
        Assert.Equal(
            ["daggerfall.player.level-up.2.health", "daggerfall.player.level-up.3.health"],
            mechanics.ReadStatTrack(TrackId.Parse("health")).Sources
                .Select(source => ((IntrinsicSourceIdentity)source.Identity).Instance.Value)
                .OrderBy(value => value));
        Assert.Equal(["player.level-up.2.hp-roll", "player.level-up.3.hp-roll"], recorder.Requests.Select(request => request.Key));
    }

    [Fact]
    public void Reward_progression_remains_exactly_once_when_loot_is_deferred_to_the_corpse_container()
    {
        DaggerfallDefinitions definitions = LoadDefinitions();
        DaggerfallActorDefinition player = definitions.RequireActor(new DaggerfallActorId("player"));
        DaggerfallActorDefinition thief = definitions.RequireActor(new DaggerfallActorId("thief")) with
        {
            Rewards = new DaggerfallRewardPolicy(500),
        };
        ActorMechanicsState mechanics = CreatePlayerMechanics(player);
        ProgressionState progression = new();
        (IRandomService random, RecordingRandomProxy recorder) = RecordingRandom(4);
        DaggerfallRewardReactions reactions = new(
            progression,
            mechanics,
            player,
            random,
            new Dictionary<long, DaggerfallActorDefinition> { [9000] = thief });
        ActorDiedFact death = new(9000, DaggerfallActorIdentity.PlayerEntityId, 5, 2, 3);
        FactBuffer<IProductFact> facts = new();

        reactions.React(death, facts);
        reactions.React(death, facts);
        Assert.Equal(500, progression.Experience);
        Assert.Equal(2, progression.Level);
        Assert.Single(mechanics.ReadStatTrack(TrackId.Parse("health")).Sources);
        List<IProductFact> delivered = [];
        facts.Deliver(delivered.Add);
        Assert.Single(delivered.OfType<ExperienceAwardedFact>());
        Assert.Empty(delivered.OfType<LootAwardedFact>());
        Assert.Equal(["player.level-up.2.hp-roll"], recorder.Requests.Select(request => request.Key));

        reactions.React(death, facts);
        List<IProductFact> duplicateDelivery = [];
        facts.Deliver(duplicateDelivery.Add);
        Assert.Empty(duplicateDelivery);
    }

    [Fact]
    public void Experience_overflow_rejects_before_loot_rng_health_facts_or_reward_markers()
    {
        DaggerfallDefinitions definitions = LoadDefinitions();
        DaggerfallActorDefinition player = definitions.RequireActor(new DaggerfallActorId("player"));
        DaggerfallActorDefinition thief = definitions.RequireActor(new DaggerfallActorId("thief"));
        EntityId owner = new(DaggerfallActorIdentity.PlayerEntityId);
        InventoryWorld world = new();
        world.RegisterInventory(new InventoryState(owner));
        world.RegisterEquipment(new EquipmentState(owner));
        MechanicsInventoryCoordinator inventory = new(world, owner, new Dictionary<KitInventoryItemId, EngineItemDefinition>());
        ActorMechanicsState mechanics = CreatePlayerMechanics(player);
        ProgressionState progression = new();
        progression.AdvanceTo(int.MaxValue, 1);
        (IRandomService random, RecordingRandomProxy recorder) = RecordingRandom();
        DaggerfallRewardReactions reactions = new(
            progression,
            mechanics,
            player,
            random,
            new Dictionary<long, DaggerfallActorDefinition> { [9000] = thief });
        FactBuffer<IProductFact> facts = new();

        Assert.Throws<OverflowException>(() => reactions.React(new ActorDiedFact(9000, DaggerfallActorIdentity.PlayerEntityId, 5, 2, 3), facts));
        Assert.Empty(recorder.Requests);
        Assert.Empty(inventory.Read().Stacks);
        Assert.Empty(inventory.Read().UniqueItems);
        Assert.Empty(mechanics.ReadStatTrack(TrackId.Parse("health")).Sources);
        Assert.Equal(100, mechanics.ReadTrack(TrackId.Parse("health")).Current.Raw);
        Assert.Equal(int.MaxValue, progression.Experience);
        Assert.Equal(1, progression.Level);
        List<IProductFact> delivered = [];
        facts.Deliver(delivered.Add);
        Assert.Empty(delivered);
    }

    private static IRandomService RandomMinimums()
    {
        IRandomService service = DispatchProxy.Create<IRandomService, RandomMinimumProxy>();
        return service;
    }

    private static (IRandomService Service, RecordingRandomProxy Recorder) RecordingRandom(params int[] values)
    {
        IRandomService service = DispatchProxy.Create<IRandomService, RecordingRandomProxy>();
        RecordingRandomProxy recorder = (RecordingRandomProxy)(object)service;
        recorder.Values.AddRange(values);
        return (service, recorder);
    }

    private static (IRandomService Service, StaleAfterLootRandomProxy Recorder) StaleAfterLootRandom(ActorMechanicsState mechanics)
    {
        IRandomService service = DispatchProxy.Create<IRandomService, StaleAfterLootRandomProxy>();
        StaleAfterLootRandomProxy recorder = (StaleAfterLootRandomProxy)(object)service;
        recorder.Mechanics = mechanics;
        return (service, recorder);
    }

    private static DaggerfallRewardReactions CreateReactions(
        DaggerfallDefinitions definitions,
        DaggerfallActorDefinition player,
        ActorMechanicsState mechanics,
        ProgressionState progression,
        IRandomService random,
        IReadOnlyDictionary<long, DaggerfallActorDefinition> actors)
    {
        EntityId owner = new(DaggerfallActorIdentity.PlayerEntityId);
        InventoryWorld world = new();
        world.RegisterInventory(new InventoryState(owner));
        world.RegisterEquipment(new EquipmentState(owner));
        return new DaggerfallRewardReactions(
            progression,
            mechanics,
            player,
            random,
            actors);
    }

    private static ActorMechanicsState CreatePlayerMechanics(DaggerfallActorDefinition player, int healthCurrent = 100)
    {
        ExactStatDefinition endurance = new(StatId.Parse("endurance"), ExactValue.Zero, new ExactValue(10_000));
        ExactStatDefinition healthMaximum = new(StatId.Parse("health-maximum"), ExactValue.Zero, new ExactValue(10_000));
        ExactTrackDefinition health = new(
            TrackId.Parse("health"),
            ExactValue.Zero,
            new ExactTrackMaximum.FromStat(healthMaximum.Id));
        return new ActorMechanicsState(
            new EntityId(DaggerfallActorIdentity.PlayerEntityId),
            [(endurance, new ExactValue(player.Stats.Endurance))],
            Array.Empty<ExactTrack>(),
            [new ExactStatTrackState(healthMaximum, new ExactValue(100), Array.Empty<ExactSource>(), health, new ExactValue(healthCurrent))]);
    }

    private class RecordingRandomProxy : RandomMinimumProxy
    {
        private int _next;
        public List<int> Values { get; } = [];
        public List<KeyedRngRequest> Requests { get; } = [];

        protected override object? Invoke(MethodInfo? method, object?[]? arguments)
        {
            if (method?.Name != nameof(IRandomService.DrawKeyed)) throw new NotSupportedException(method?.Name);
            KeyedRngRequest request = (KeyedRngRequest)arguments![0]!;
            Requests.Add(request);
            return new KeyedRngReceipt(Values[_next++]);
        }
    }

    private class StaleAfterLootRandomProxy : RandomMinimumProxy
    {
        private bool _staled;
        internal ActorMechanicsState Mechanics { private get; set; } = null!;
        internal int LootCalls { get; private set; }

        protected override object? Invoke(MethodInfo? method, object?[]? arguments)
        {
            if (method?.Name != nameof(IRandomService.DrawKeyed)) throw new NotSupportedException(method?.Name);
            KeyedRngRequest request = (KeyedRngRequest)arguments![0]!;
            if (request.Key.StartsWith("generation:", StringComparison.Ordinal))
            {
                LootCalls++;
                if (!_staled)
                {
                    _staled = true;
                    Mechanics.SetTrack(TrackId.Parse("health"), new ExactValue(80));
                }
            }

            return new KeyedRngReceipt(request.Minimum);
        }
    }

    private class RandomMinimumProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name == nameof(IRandomService.DrawKeyed)
            ? new KeyedRngReceipt(((KeyedRngRequest)arguments![0]!).Minimum)
            : throw new NotSupportedException(method?.Name);
    }

    private static EngineItemDefinition ToManaged(DaggerfallItemDefinition item)
    {
        ItemEquipmentPolicy? equipment = item.Equipment is null
            ? null
            : new ItemEquipmentPolicy(
                item.Equipment.RequiredSlots,
                item.Equipment.ExclusiveGroup is { } group ? EquipmentExclusivityId.Parse(group) : null);
        return new EngineItemDefinition(
            EngineItemDefinitionId.Parse(item.Id.Value),
            item.IsFungible ? ItemKind.Fungible : ItemKind.Unique,
            item.MaximumQuantity,
            item.Equipment?.Classifications.Select(ItemClassificationId.Parse),
            null,
            equipment);
    }

    private static DaggerfallDefinitions LoadDefinitions() => DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(RepositoryRoot(), "content/worldrpg/payloads/daggerfall.base.json")));

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md"))) return current.FullName;
        throw new InvalidOperationException("Could not locate the Rusty Dagger repository root.");
    }
}
