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
    [Theory]
    [InlineData(20, true)]
    [InlineData(2, false)]
    public void Current_weapon_reach_controls_attack_eligibility(double selectedReach, bool attacks)
    {
        using ActorsState actors = new();
        actors.CreatePlayer(1, new EntityTypeId("player"), Stats(), "health");
        var actor = actors.CreateActor(42, new EntityTypeId("enemy"), Stats(), new ActorPose(new WorldPoint(0, 0, 0), 0), "health");
        var perception = PerceptionDouble.Create();
        perception.Pairs = new[] { new PerceptionPair(42, 1, 8, 1, PerceptionPairKind.Visible, 1) };
        var spatial = SpatialDouble.Create();
        using SpatialMovementSystem movement = new(spatial.Service, ContentDouble.Create().Service,
            new SpatialContentArtifact("spatial/test", new ContentSha256(1, 2, 3, 4), 1), new SpatialTuning(.5, 8, 8, 1));
        var action = new RecordingAttacks(1.5);
        var pursuit = new PursuitCoordinator<TestFact>(perception.Service, movement,
            new ActorNavigationCoordinator(spatial.Service, movement.Session), action);
        var evidence = pursuit.Update(actor, new PursuitMemoryComponent(), new(1, new(0, 0, 8)),
            new(64, 0, 2, 8), new(0, 8), 1, 1, .1f, new FactBuffer<TestFact>(),
            new PursuitPolicy(ActorNavigationMode.Ground), selectPolicy: _ => new PursuitPolicy(ActorNavigationMode.Ground) { AttackReach = selectedReach });
        Assert.Equal(attacks, evidence.AttackEligible);
        Assert.Equal(attacks ? PursuitState.Attack : PursuitState.Chase, evidence.Current);
        Assert.Equal(attacks ? 1 : 0, action.Attacks.Count);
    }

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
            new(64, 0, 2, 8), new(0, 8), 1, 2, .1f, new FactBuffer<TestFact>(),
            new PursuitPolicy(ActorNavigationMode.Swimming, CanRetreat: true, CanStrafe: true, 1f, 1f, EmitTargetLost: true));

        Assert.Equal(PursuitState.Unloaded, evidence.Current);
        Assert.Empty(perception.Requests);
        Assert.Empty(spatial.NavigationRequests);
        Assert.Empty(attacks.Attacks);
    }

    [Fact]
    public void Approaching_melee_actor_does_not_retreat_merely_because_it_is_outside_reach()
    {
        using ActorsState actors = new();
        actors.CreatePlayer(1, new EntityTypeId("player"), Stats(), "health");
        var actor = actors.CreateActor(42, new EntityTypeId("enemy"), Stats(), new ActorPose(new WorldPoint(0, 0, 0), 0), "health");
        var perception = PerceptionDouble.Create();
        perception.Pairs = new[] { new PerceptionPair(42, 1, 2d, 1d, PerceptionPairKind.Visible, 2d) };
        var spatial = SpatialDouble.Create();
        using SpatialMovementSystem movement = new(spatial.Service, ContentDouble.Create().Service,
            new SpatialContentArtifact("spatial/test", new ContentSha256(1, 2, 3, 4), 1), new SpatialTuning(.5, 8, 8, 1));
        var pursuit = new PursuitCoordinator<TestFact>(perception.Service, movement,
            new ActorNavigationCoordinator(spatial.Service, movement.Session), new RecordingAttacks(1.5d));
        var evidence = pursuit.Update(actor, new PursuitMemoryComponent(), new(1, new(0, 0, 2)),
            new(64, 0, 2, 8), new(0, 8), 1, 1, .1f, new FactBuffer<TestFact>(),
            new PursuitPolicy(CanRetreat: true, RetreatDistance: 2.5f));
        Assert.Equal(PursuitState.Chase, evidence.Current);
    }

    [Fact]
    public void Search_uses_the_last_observed_position_then_direction_without_reading_a_hidden_target_pose()
    {
        using ActorsState actors = new();
        actors.CreatePlayer(1, new("player"), Stats(), "health");
        var actor = actors.CreateActor(42, new("enemy"), Stats(), new(new(0, 0, 0), 0), "health");
        var perception = PerceptionDouble.Create();
        var spatial = SpatialDouble.Create();
        using SpatialMovementSystem movement = new(spatial.Service, ContentDouble.Create().Service,
            new SpatialContentArtifact("spatial/test", new ContentSha256(1, 2, 3, 4), 1), new SpatialTuning(.5, 8, 8, 1));
        var pursuit = new PursuitCoordinator<TestFact>(perception.Service, movement,
            new ActorNavigationCoordinator(spatial.Service, movement.Session), new RecordingAttacks(1.5));
        var memory = new PursuitMemoryComponent();
        var policy = new PursuitPolicy(EmitTargetLost: true) { SearchLastKnownPosition = true, SearchDistance = 10, SearchStep = 1, BlockedFailures = 100 };
        PursuitEvidence Update(WorldPoint position, bool visible, ulong step)
        {
            perception.Pairs = new PerceptionPair[] { new(42, 1, 10, 1, visible ? PerceptionPairKind.Visible : PerceptionPairKind.Occluded, visible ? 1 : 0) };
            return pursuit.Update(actor, memory, new(1, position), new(64, -1, 2, 8), new(0, 8), 1, step, .1f, new(), policy);
        }
        Update(new(5, 0, 0), true, 1);
        Update(new(6, 0, 0), true, 2);
        Assert.Equal(PursuitState.Search, Update(new(100, 0, 100), false, 3).Current);
        Assert.Equal(new Vector3(6, 0, 0), spatial.NavigationRequests.Last().Target);
        actor.ApplyPose(new(new(6, 0, 0), 0));
        Update(new(100, 0, 100), false, 4);
        Assert.Equal(new Vector3(7, 0, 0), spatial.NavigationRequests.Last().Target);
        Assert.Equal(new WorldPoint(6, 0, 0), memory.LastKnownPosition);
        for (ulong step = 5; step < 14; step++)
        {
            actor.ApplyPose(new(WorldPoint.From(spatial.NavigationRequests.Last().Target), 0));
            Update(new(100, 0, 100), false, step);
        }
        actor.ApplyPose(new(new(16, 0, 0), 0));
        Assert.Equal(PursuitState.TargetLost, Update(new(100, 0, 100), false, 14).Current);
        Assert.Null(memory.LastKnownPosition);
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
        Assert.Equal(1UL, Assert.Single(containers.Read(recipient).Stacks).Quantity);
        Assert.Equal(1UL, Assert.Single(loot.Read(corpse)!.Stacks).Quantity);

        CorpseLootTransferResult last = loot.TransferAll(corpse, recipient);

        Assert.True(last.IsEmpty);
        Assert.Equal(2UL, containers.Read(recipient).Stacks.Aggregate(0UL, (total, stack) => total + stack.Quantity));
        Assert.Empty(loot.Read(corpse)!.Stacks);
        // Whether an emptied corpse stays interactable is ruleset eligibility; Kit leaves it as created.
        Assert.True(corpse.IsInteractable);
        Assert.True(loot.TransferAll(corpse, recipient).IsEmpty);
    }

    [Fact]
    public void Receiving_new_contents_registers_an_empty_corpse_and_reports_its_contents()
    {
        EntityDirectory entities = new();
        InventoryStore inventory = new();
        MechanicsInventoryContainerCoordinator containers = new(inventory, entities,
            new Dictionary<InventoryItemId, ItemDefinition> { [new("gold")] = new(ItemDefinitionId.Parse("gold"), ItemKind.Fungible, 10) });
        var recipient = entities.Create(new(DurableIdentityKind.Actor, 1), new("player"));
        containers.RegisterOwner(recipient);
        CorpseLootCoordinator loot = new(entities, containers);
        var corpse = loot.Create(new(DurableIdentityKind.Container, 2000), new("corpse"), 1, []);
        Assert.False(corpse.HasRegisteredInventory);
        Assert.True(loot.TransferAll(corpse, recipient).IsEmpty);
        var owner = corpse.Owner;
        InventoryView received = loot.Receive(corpse, target => containers.Seed(target, [new(new("gold"), 1, Stack: InventoryStackId.Parse("first"))]));
        Assert.True(corpse.HasRegisteredInventory);
        Assert.Equal(1UL, Assert.Single(received.Stacks).Quantity);
        Assert.Equal(owner, corpse.Owner);
        Assert.True(loot.TransferAll(corpse, recipient).IsEmpty);
        corpse.IsInteractable = false;
        received = loot.Receive(corpse, target => containers.Seed(target, [new(new("gold"), 2, Stack: InventoryStackId.Parse("second"))]));
        Assert.False(corpse.IsInteractable);
        Assert.Equal(2UL, Assert.Single(received.Stacks).Quantity);
        Assert.Equal(2UL, Assert.Single(loot.Read(corpse)!.Stacks).Quantity);
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
        internal ReadOnlyMemory<PerceptionPair> Pairs { get; set; } = ReadOnlyMemory<PerceptionPair>.Empty;
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
            return new PerceptionReadoutResult(Pairs, ReadOnlyMemory<PerceptionAggregate>.Empty, 0, false, 0, 1, 1, 0, 0, 0, 0, 0, 0);
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
