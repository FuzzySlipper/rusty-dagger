using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using Rusty.Engine.Testing;
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

    [Fact]
    public void Actors_placed_while_their_floor_was_pending_are_grounded_once_it_is_live()
    {
        using ActorsState actors = new();
        List<string> releases = [];
        SpatialFake fake = SpatialFake.Create(new ContentSha256(1, 2, 3, 4), releases);
        using SpatialMovementSystem spatial = new(fake.Service, new ContentFake(releases), null, DaggerfallTuning.Defaults.Spatial);
        DaggerfallTuning tuning = DaggerfallTuning.Defaults;
        DaggerfallActorGrounding grounding = new(fake.Service, spatial, tuning.EnemyBehavior.SpawnGroundProbeLift, tuning.EnemyBehavior.SpawnGroundProbeDistance);
        ActorState Create(long id) => actors.CreateActor(id, new EntityTypeId("rat"), TestSessions.HealthyMechanics(),
            new ActorPose(new WorldPoint(0f, 0f, 0f), 0f), "health");
        int asked = 0;
        grounding.FloorPending = () => { asked++; return true; };
        grounding.Ground(Create(200));
        grounding.Ground(Create(201));
        asked = 0;
        grounding.FloorPending = () => { asked++; return false; };

        grounding.GroundAwaiting(actors);
        // Grounding asks for the floor once for each actor it settles; settled, none waits any longer.
        Assert.Equal(2, asked);
        grounding.GroundAwaiting(actors);
        Assert.Equal(2, asked);
    }

    [Fact]
    public void An_actor_placed_under_open_ground_before_it_streamed_in_settles_on_it_once_it_is_live()
    {
        // A met band's people are placed beside the player's landing, a height guessed before the ground streamed in:
        // in hills, the ground stands tens of metres above it (#11212, Woodcroft Court: 410 against 451).
        const float Ground = 41F;
        using EngineTestHost host = EngineTestHost.Create(new EngineTestHostOptions());
        host.Call(engine =>
        {
            DaggerfallTuning tuning = DaggerfallTuning.Defaults;
            using SpatialMovementSystem spatial = new(engine.Spatial, engine.Content, null, tuning.Spatial);
            using ActorsState actors = new();
            ActorState Create(long id) => actors.CreateActor(id, new EntityTypeId("person"), TestSessions.HealthyMechanics(),
                new ActorPose(new WorldPoint(0F, 0F, 0F), 0F), "health");
            DaggerfallActorGrounding grounding = new(engine.Spatial, spatial, tuning.EnemyBehavior.SpawnGroundProbeLift, tuning.EnemyBehavior.SpawnGroundProbeDistance);
            grounding.TerrainHeight = _ => Ground;
            DaggerfallActorGrounding blind = new(engine.Spatial, spatial, tuning.EnemyBehavior.SpawnGroundProbeLift, tuning.EnemyBehavior.SpawnGroundProbeDistance);

            // Placed while the floor is still on its way, it waits for it.
            grounding.FloorPending = () => true;
            ActorState arrived = Create(300);
            grounding.Ground(arrived);
            Assert.Equal(0F, arrived.Position.Y);

            // The ground streams in; the actor settles on it, not where it was guessed.
            _ = engine.Spatial.ApplyCollisionResidency(new CollisionResidencyRequest(spatial.Session,
                new[] { new StaticMeshAsset(1, 0, 4, 0, 2) },
                new Vector3[] { new(-20, Ground, -20), new(-20, Ground, 20), new(20, Ground, -20), new(20, Ground, 20) },
                new Triangle[] { new(0, 1, 2), new(1, 3, 2) },
                new[] { new StaticMeshInstance(2, 1, new Transform(Vector3.Zero, Quaternion.Identity, Vector3.One)) },
                ReadOnlyMemory<ulong>.Empty, ReadOnlyMemory<ulong>.Empty));
            grounding.FloorPending = () => false;
            grounding.GroundAwaiting(actors);
            Assert.Equal(Ground, arrived.Position.Y, 3);

            // Looking down only from where it was guessed, the ground above is never found.
            ActorState buried = Create(301);
            blind.Ground(buried);
            Assert.Equal(0F, buried.Position.Y);
        });
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
