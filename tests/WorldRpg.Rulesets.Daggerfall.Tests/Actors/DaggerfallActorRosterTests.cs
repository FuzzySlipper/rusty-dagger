using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Loot;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>The roster built from its own inputs, with no session.</summary>
public sealed class DaggerfallActorRosterTests
{
    [Fact]
    public void A_detached_actor_retires_its_items_stacks_effects_corpse_and_identity_and_keeps_the_tombstones()
    {
        using Stage stage = new();
        long actor = checked((long)stage.Identities.Allocate(DurableIdentityKind.Actor).Value);
        ulong item = stage.UniqueItems.AllocateReference().Value;
        ulong corpseItem = stage.UniqueItems.AllocateReference().Value;
        ulong container = stage.Identities.Allocate(DurableIdentityKind.Container).Value;
        DaggerfallItemDefinition definition = stage.Definitions.Items.Values.First();
        stage.Instances.RegisterDefaultUnique(item, definition, DaggerfallItemOwner.Actor(actor));
        stage.Instances.RegisterDefaultUnique(corpseItem, definition, DaggerfallItemOwner.Corpse(actor));
        stage.Instances.RegisterStack(DaggerfallItemOwner.Actor(actor), InventoryStackId.Parse("arrows"), stage.Instances.RequireUnique(item));
        // A stack the delta no longer lists still belongs to the corpse's owner until the owners are let go.
        stage.Instances.RegisterStack(DaggerfallItemOwner.Corpse(actor), InventoryStackId.Parse("bones"), stage.Instances.RequireUnique(item));
        DaggerfallActorInventorySave[] inventories =
        [
            new(actor, new([new("arrows", definition.Id.Value, 3, null!)], [new(definition.Id.Value, item, null!)], [])),
            new(actor + 1, new([], [], [])),
        ];
        DaggerfallCorpseSave[] corpses = [new(actor, container, 1, true, true, [], [new(definition.Id.Value, corpseItem, null!)])];
        List<ulong> cancelled = [];

        Assert.Throws<InvalidOperationException>(() => stage.Roster.RetireDetached(Saved(actor), inventories, corpses, "bandit", cancelled.Add));
        Assert.True(stage.Instances.ContainsUnique(item));
        stage.Roster.RetireDetached(Saved(actor), inventories, corpses, "rat", cancelled.Add);

        Assert.Equal([item, corpseItem], cancelled);
        Assert.False(stage.Instances.ContainsUnique(item));
        Assert.False(stage.Instances.ContainsUnique(corpseItem));
        Assert.DoesNotContain(stage.Instances.StackItems, stack => stack.Owner == DaggerfallItemOwner.Actor(actor));
        Assert.Contains(stage.Instances.StackItems, stack => stack.Owner == DaggerfallItemOwner.Corpse(actor));
        stage.Roster.ReleaseDetachedOwners(actor);
        Assert.DoesNotContain(stage.Instances.StackItems, stack => stack.Owner == DaggerfallItemOwner.Corpse(actor));
        Assert.Equal(DurableIdentityClassification.Removed, stage.Identities.Classify(new(DurableIdentityKind.Item, item)));
        Assert.Equal(DurableIdentityClassification.Removed, stage.Identities.Classify(new(DurableIdentityKind.Item, corpseItem)));
        Assert.Equal(DurableIdentityClassification.Removed, stage.Identities.Classify(new(DurableIdentityKind.Container, container)));
        Assert.Equal(DurableIdentityClassification.Removed, stage.Identities.Classify(ActorsState.Identity(actor)));
    }

    private static DaggerfallDynamicActorSave Saved(long id) =>
        new(id, "rat", 0f, 0f, 0f, 0f, DaggerfallStatsSaveBoundary.Capture(TestSessions.HealthyMechanics(), new EntityId((ulong)id)))
        { Level = 1, SpawnDistance = 0, Passive = false };

    /// <summary>The roster's inputs: the player, the Engine inventory store, item meaning, effects, identities, corpses and grounding over fakes.</summary>
    private sealed class Stage : IDisposable
    {
        private readonly SpatialMovementSystem _spatial;

        internal Stage()
        {
            DaggerfallActorDefinition player = Definitions.RequireActor(new DaggerfallActorId("player"));
            StatsComponent stats = new DaggerfallMechanicsState().CreateStats(player,
                DaggerfallPlayerVitals.Initial(player.Stats, Definitions.Catalogs.RequireCareer("class00")));
            Actors.CreatePlayer(DaggerfallActorIdentity.PlayerEntityId, new EntityTypeId("player"), stats, "health", DaggerfallActorFactory.PlayerCapabilities);
            DaggerfallCharacterState character = new(Definitions, stats, player);
            Dictionary<InventoryItemId, ItemDefinition> items = Definitions.Items.Values.Concat(Definitions.TemplateItems.Values)
                .ToDictionary(value => new InventoryItemId(value.Id.Value), DaggerfallActorFactory.ToManagedItem);
            Dictionary<WorldRpg.Kit.Inventory.EquipmentSlotId, EquipmentSlotDefinition> slots = Definitions.EquipmentSlots.Values
                .ToDictionary(slot => new WorldRpg.Kit.Inventory.EquipmentSlotId(slot.Id.Value), DaggerfallActorFactory.ToManagedSlot);
            MechanicsInventoryContainerCoordinator containers = new(Store, Actors.Entities, items);
            Identities = DurableIdentityAllocator.Restore(new DurableIdentityState(
            [
                new KindAllocatorState(DurableIdentityKind.Actor, DaggerfallActorRoster.DynamicFirstIdentity, [DaggerfallActorIdentity.PlayerEntityId], []),
                new KindAllocatorState(DurableIdentityKind.Item, DaggerfallUniqueItemAllocator.DefaultFirstEntityId, [], []),
                new KindAllocatorState(DurableIdentityKind.Container, DaggerfallGroundContainers.FirstIdentity, [], []),
            ]));
            UniqueItems = DaggerfallUniqueItemAllocator.Sharing(Identities, new HashSet<ulong>());
            List<string> releases = [];
            SpatialFake fake = SpatialFake.Create(new ContentSha256(1, 2, 3, 4), releases);
            _spatial = new SpatialMovementSystem(fake.Service, new ContentFake(releases), null, DaggerfallTuning.Defaults.Spatial);
            DaggerfallCorpseLootModule corpses = new(PerceptionFake.Create().Service, _spatial, containers, Instances,
                Actors.Player.Actor.Entity, Actors, new Dictionary<long, DaggerfallActorDefinition>(), Definitions, null, RandomMinimum.Create(),
                UniqueItems, Actors.Player.Progression, DaggerfallTuning.Defaults.LootInteraction, character, Identities);
            DaggerfallTuning tuning = DaggerfallTuning.Defaults;
            Roster = new DaggerfallActorRoster(Actors, new DaggerfallActorInventories(Actors, items, slots), character, containers,
                new DaggerfallEffectLifecycle(Actors, new DaggerfallEffectCatalog([])), Store, Instances, new DaggerfallNpcRegistry(), Definitions,
                RandomMinimum.Create(), new DaggerfallMechanicsState(), Identities, UniqueItems,
                new Dictionary<long, DaggerfallActorDefinition>(), [],
                new DaggerfallActorGrounding(fake.Service, _spatial, tuning.EnemyBehavior.SpawnGroundProbeLift, tuning.EnemyBehavior.SpawnGroundProbeDistance),
                () => throw new InvalidOperationException("No site in this stage."), () => throw new InvalidOperationException("No appearance in this stage."),
                _ => { }, corpses);
        }

        internal DaggerfallDefinitions Definitions { get; } = TestPayload.Definitions;
        internal ActorsState Actors { get; } = new();
        internal InventoryStore Store { get; } = new();
        internal DaggerfallItemInstances Instances { get; } = new();
        internal DurableIdentityAllocator Identities { get; }
        internal DaggerfallUniqueItemAllocator UniqueItems { get; }
        internal DaggerfallActorRoster Roster { get; }

        public void Dispose()
        {
            _spatial.Dispose();
            Actors.Dispose();
        }
    }
}
