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
using WorldRpg.Kit.World;
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

        Assert.Equal(new DurableIdentityReference(DurableIdentityKind.Item, 1_002), allocator.AllocateReference());
        Assert.Equal(new DurableIdentityReference(DurableIdentityKind.Item, 1_003), allocator.AllocateReference());
    }

    [Fact]
    public void Daggerfall_actor_construction_shares_health_maximum_with_its_track()
    {
        DaggerfallDefinitions definitions = LoadDefinitions();
        DaggerfallActorDefinition player = definitions.RequireActor(new DaggerfallActorId("player"));
        ActorMechanicsState mechanics = new DaggerfallMechanicsState().CreateActor(
            player,
            player.PlayerInitialVitals,
            DaggerfallActorIdentity.PlayerEntityId);

        Stat healthMaximum = mechanics.ReadStat(StatId.Parse("health-maximum"));
        Track health = mechanics.ReadTrack(TrackId.Parse("health"));
        Assert.Same(healthMaximum, health.Maximum);
        Assert.Equal(player.PlayerInitialVitals.HealthMaximum, healthMaximum.Value);
        Assert.Equal(player.PlayerInitialVitals.HealthMaximum, health.Current);
        Assert.Equal(player.PlayerInitialVitals.StaminaMaximum, mechanics.ReadTrack(TrackId.Parse("stamina")).Current);
    }

    [Fact]
    public void Failed_loot_grant_does_not_award_xp_and_retry_is_exactly_once()
    {
        DaggerfallDefinitions definitions = LoadDefinitions();
        DaggerfallActorDefinition player = definitions.RequireActor(new DaggerfallActorId("player"));
        DaggerfallActorDefinition thief = definitions.RequireActor(new DaggerfallActorId("thief"));
        Dictionary<long, DaggerfallActorDefinition> actors = new() { [9000] = thief };
        EntityId owner = new(DaggerfallActorIdentity.PlayerEntityId);
        InventoryStore world = new();
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
        InventoryStore world = new();
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
    public void Xp_threshold_applies_one_keyed_health_source_and_preserves_health_distance()
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
        Assert.Equal(107d, mechanics.ReadStat(StatId.Parse("health-maximum")).Value);
        Assert.Equal(77d, mechanics.ReadTrack(TrackId.Parse("health")).Current);
        StatDecision source = Assert.Single(mechanics.ReadStat(StatId.Parse("health-maximum")).Explain().Decisions);
        Assert.Equal("daggerfall.player.level-up.2.health", ((IntrinsicSourceIdentity)source.Source).Instance.Value);
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
        Assert.Equal(110d, mechanics.ReadStat(StatId.Parse("health-maximum")).Value);
        Assert.Equal(60d, mechanics.ReadTrack(TrackId.Parse("health")).Current);
        Assert.Equal(
            ["daggerfall.player.level-up.2.health", "daggerfall.player.level-up.3.health"],
            mechanics.ReadStat(StatId.Parse("health-maximum")).Explain().Decisions
                .Select(source => ((IntrinsicSourceIdentity)source.Source).Instance.Value)
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
        Assert.Single(mechanics.ReadStat(StatId.Parse("health-maximum")).Explain().Decisions);
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
        InventoryStore world = new();
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
        Assert.Empty(mechanics.ReadStat(StatId.Parse("health-maximum")).Explain().Decisions);
        Assert.Equal(100d, mechanics.ReadTrack(TrackId.Parse("health")).Current);
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
        InventoryStore world = new();
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
        StatId enduranceId = StatId.Parse("endurance");
        StatId healthMaximumId = StatId.Parse("health-maximum");
        Stat healthMaximum = new(100, 0, 10_000, quantum: 1, rounding: MidpointRounding.ToZero, integerRounding: MidpointRounding.ToZero);
        return new ActorMechanicsState(
            new EntityId(DaggerfallActorIdentity.PlayerEntityId),
            [(enduranceId, new Stat(player.Stats.Endurance, 0, 10_000, quantum: 1, rounding: MidpointRounding.ToZero, integerRounding: MidpointRounding.ToZero)), (healthMaximumId, healthMaximum)],
            [(TrackId.Parse("health"), new Track(
                healthMaximum,
                healthCurrent,
                0,
                TrackMaximumChangePolicy.PreserveMissingAmount,
                quantum: 1,
                rounding: MidpointRounding.ToZero,
                integerRounding: MidpointRounding.ToZero))]);
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
                    Mechanics.ReadTrack(TrackId.Parse("health")).SetCurrent(80);
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
