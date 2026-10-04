using System.Numerics;
using System.Reflection;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Ai;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Facts;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.Loot;
using WorldRpg.Kit.World;
using Xunit;

namespace WorldRpg.Kit.Tests;

public sealed class PursuitAndCorpseLootTests
{
    [Fact]
    public void Pursuit_memory_keeps_the_previous_state_for_a_ruleset_transition_fact()
    {
        PursuitMemoryComponent memory = new();

        Assert.Equal(PursuitState.Idle, memory.TransitionTo(PursuitState.Chase));
        Assert.Equal(PursuitState.Chase, memory.TransitionTo(PursuitState.Attack));
        Assert.Equal(PursuitState.Attack, memory.State);
    }

    [Fact]
    public void Pursuit_reports_an_unloaded_target_without_querying_or_chasing_its_stale_pose()
    {
        using ActorsState actors = new();
        actors.CreatePlayer(1, new EntityTypeId("player"), Stats(), "health");
        actors.CreateActor(42, new EntityTypeId("enemy"), Stats(), new ActorPose(new WorldPoint(2, 1, 2), 0), "health");
        PerceptionDouble perception = PerceptionDouble.Create();
        SpatialDouble spatial = SpatialDouble.Create();
        using SpatialMovementSystem movement = new(spatial.Service, ContentDouble.Create().Service,
            new SpatialContentArtifact("spatial/test", new ContentSha256(1, 2, 3, 4), 1), new SpatialTuning(.5, 8, 8, 1));
        RecordingAttacks attacks = new(1.5d);
        PursuitCoordinator<TestFact> pursuit = new(perception.Service, movement,
            new ActorNavigationCoordinator(spatial.Service, movement.Session), attacks);
        PursuitMemoryComponent memory = new();
        memory.TransitionTo(PursuitState.Chase);

        PursuitEvidence evidence = pursuit.Update(
            actors.Get(42), memory, new PursuitTarget(1, new WorldPoint(200, 50, 200), IsLoaded: false),
            new(64, 0, 2, 8), new(0, 0, 8), 1, 2, .1f, new FactBuffer<TestFact>(),
            new(ActorNavigationMode.Swimming, CanRetreat: true, CanStrafe: true, EmitTargetLost: true));

        Assert.Equal(PursuitState.Unloaded, evidence.Current);
        Assert.Empty(perception.Requests);
        Assert.Empty(spatial.NavigationRequests);
        Assert.Empty(attacks.Attacks);
    }

    private static StatsComponent Stats()
    {
        Stat maximum = new(100);
        StatsComponent stats = new();
        stats.AddTrack(TrackId.Parse("health"), new Track(maximum, 100));
        return stats;
    }

    [Fact]
    public void Corpse_transfer_keeps_one_actor_owned_container_until_its_contents_are_exhausted()
    {
        EntityDirectory entities = new();
        InventoryStore inventory = new();
        MechanicsInventoryContainerCoordinator containers = new(inventory, entities,
            new Dictionary<InventoryItemId, ItemDefinition>
            {
                [new InventoryItemId("gold")] = new(ItemDefinitionId.Parse("gold"), ItemKind.Fungible, 10),
            });
        EntityId recipient = entities.Create(new DurableIdentityReference(DurableIdentityKind.Actor, 1), new EntityTypeId("player"));
        containers.RegisterOwner(recipient);
        CorpseLootCoordinator loot = new(entities, containers);
        CorpseLootComponent corpse = loot.Create(
            new DurableIdentityReference(DurableIdentityKind.Container, 2000),
            new EntityTypeId("corpse"),
            originatingSequence: 7,
            [new InventoryContainerSeed(new InventoryItemId("gold"), 2, Stack: InventoryStackId.Parse("corpse-gold"))]);

        CorpseLootTransferResult first = loot.Transfer(corpse, recipient,
            new InventoryContainerSelection(new InventoryItemId("gold"), 1, InventoryStackId.Parse("corpse-gold"), InventoryStackId.Parse("player-gold")));

        Assert.False(first.IsEmpty);
        Assert.True(corpse.IsInteractable);
        Assert.Equal(1UL, Assert.Single(containers.Read(recipient).Stacks).Quantity);
        Assert.Equal(1UL, Assert.Single(loot.Read(corpse)!.Stacks).Quantity);

        CorpseLootTransferResult last = loot.TransferAll(corpse, recipient);

        Assert.True(last.IsEmpty);
        Assert.False(corpse.IsInteractable);
        Assert.Equal(2UL, containers.Read(recipient).Stacks.Aggregate(0UL, (total, stack) => total + stack.Quantity));
        Assert.Empty(loot.Read(corpse)!.Stacks);
        Assert.Throws<InvalidOperationException>(() => loot.TransferAll(corpse, recipient));
    }

    private sealed record TestFact : IWorldRpgFact;

    private sealed class RecordingAttacks(double reach) : IAttackCapabilities<TestFact>
    {
        internal List<(long Attacker, long Target)> Attacks { get; } = [];
        public double? ReachOf(long actorId) => reach;
        public bool IsReady(long actorId, ulong generation, ulong step) => true;
        public void TryPlayerMelee(PlayerControlState player, LookReceipt look, ulong generation, ulong step, double delta, FactBuffer<TestFact> facts) { }
        public bool TryBeginEnemyAttack(long attacker, long target, ulong generation, ulong step, double delta, FactBuffer<TestFact> facts)
        {
            Attacks.Add((attacker, target));
            return true;
        }
        public void InterruptPendingAttack(long attacker, ulong generation) { }
    }

    private class PerceptionDouble : DispatchProxy
    {
        internal IPerceptionService Service { get; private set; } = null!;
        internal List<PerceptionQueryRequest> Requests { get; } = [];
        internal static PerceptionDouble Create()
        {
            IPerceptionService service = DispatchProxy.Create<IPerceptionService, PerceptionDouble>();
            PerceptionDouble proxy = (PerceptionDouble)(object)service;
            proxy.Service = service;
            return proxy;
        }
        protected override object? Invoke(MethodInfo? method, object?[]? arguments)
        {
            if (method?.Name != nameof(IPerceptionService.QueryVisibility)) throw new NotSupportedException(method?.Name);
            Requests.Add((PerceptionQueryRequest)arguments![0]!);
            return new PerceptionReadoutResult(ReadOnlyMemory<PerceptionPair>.Empty, ReadOnlyMemory<PerceptionAggregate>.Empty, 0, false, 0, 1, 1, 0, 0, 0, 0, 0, 0);
        }
    }

    private class ContentDouble : DispatchProxy
    {
        internal IContentService Service { get; private set; } = null!;
        internal static ContentDouble Create()
        {
            IContentService service = DispatchProxy.Create<IContentService, ContentDouble>();
            ContentDouble proxy = (ContentDouble)(object)service;
            proxy.Service = service;
            return proxy;
        }
        protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name == nameof(IContentService.ResolveReference)
            ? new ContentReference(new ContentReferenceHandle(1), static () => { }) : throw new NotSupportedException(method?.Name);
    }

    private class SpatialDouble : DispatchProxy
    {
        internal ISpatialService Service { get; private set; } = null!;
        internal List<NavigationStepRequest> NavigationRequests { get; } = [];
        internal static SpatialDouble Create()
        {
            ISpatialService service = DispatchProxy.Create<ISpatialService, SpatialDouble>();
            SpatialDouble proxy = (SpatialDouble)(object)service;
            proxy.Service = service;
            return proxy;
        }
        protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name switch
        {
            nameof(ISpatialService.DefaultCharacterControllerConfig) => default(CharacterControllerConfig),
            nameof(ISpatialService.ValidateCharacterControllerConfig) => null,
            nameof(ISpatialService.CreateSession) => new SpatialSession(new SpatialSessionHandle(1), static () => { }),
            nameof(ISpatialService.ReplaceContentArtifact) => new SpatialContentArtifactReplaceReceipt(),
            nameof(ISpatialService.EvaluateNavigationStep) => Evaluate((NavigationStepRequest)arguments![0]!),
            _ => throw new NotSupportedException(method?.Name),
        };

        private NavigationStepResult Evaluate(NavigationStepRequest request)
        {
            NavigationRequests.Add(request);
            return default;
        }
    }
}
