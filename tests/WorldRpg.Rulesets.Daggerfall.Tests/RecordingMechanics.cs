using System.Reflection;
using Rusty.Engine;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>Focused mechanics double that stays insulated from unrelated published operations.</summary>
internal class RecordingMechanics : DispatchProxy
{
    private readonly Dictionary<MechanicsEntity, EntityState> _entities = [];
    private ulong _next = 1;

    internal IMechanicsService Service { get; private set; } = null!;
    internal List<MechanicsTrackMutationRequest> SpendRequests { get; } = [];
    internal int CatalogDisposals { get; private set; }
    internal int EntityDisposals { get; private set; }
    internal int? FailOnDefineStatCall { get; set; }
    internal int? FailOnInitialStatCall { get; set; }
    private int DefineStatCalls { get; set; }
    private int InitialStatCalls { get; set; }

    internal static RecordingMechanics Create()
    {
        IMechanicsService service = DispatchProxy.Create<IMechanicsService, RecordingMechanics>();
        RecordingMechanics proxy = (RecordingMechanics)(object)service;
        proxy.Service = service;
        return proxy;
    }

    internal MechanicsTrackReadLeaseReceipt ReadTrack(MechanicsTrackReadRequest request) => ReadTrackCore(request);
    internal MechanicsTrackSetLeaseReceipt SetTrack(MechanicsTrackSetRequest request) => SetTrackCore(request);
    internal MechanicsTrackMutationLeaseReceipt RestoreTrack(MechanicsTrackMutationRequest request) => AdjustTrack(request, spend: false);

    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(method);
        object? argument = args is { Length: > 0 } ? args[0] : null;
        return method.Name switch
        {
            nameof(IMechanicsService.CreateCatalog) => CreateCatalog(),
            nameof(IMechanicsService.DefineStat) => DefineStat(),
            nameof(IMechanicsService.DefineTrack) or nameof(IMechanicsService.DefineItem) or nameof(IMechanicsService.DefineContribution) or nameof(IMechanicsService.AdmitCatalog) => null,
            nameof(IMechanicsService.BindEntity) => BindEntity((MechanicsEntityBindRequest)argument!),
            nameof(IMechanicsService.SetInitialStat) => SetInitialStat((MechanicsInitialStatRequest)argument!),
            nameof(IMechanicsService.SetInitialTrack) => SetInitialTrack((MechanicsInitialTrackRequest)argument!),
            nameof(IMechanicsService.SetInitialComponents) => SetInitialComponents((MechanicsInitialComponentsRequest)argument!),
            nameof(IMechanicsService.BindIntrinsicSource) => null,
            nameof(IMechanicsService.CommitEntity) => CommitEntity((MechanicsEntity)argument!),
            nameof(IMechanicsService.ReadStat) => ReadStat((MechanicsStatReadRequest)argument!),
            nameof(IMechanicsService.EvaluateStat) => EvaluateStat((MechanicsStatOperationRequest)argument!),
            nameof(IMechanicsService.ReadTrack) => ReadTrackCore((MechanicsTrackReadRequest)argument!),
            nameof(IMechanicsService.SetTrack) => SetTrackCore((MechanicsTrackSetRequest)argument!),
            nameof(IMechanicsService.SpendTrack) => SpendTrack((MechanicsTrackMutationRequest)argument!),
            nameof(IMechanicsService.RestoreTrack) => AdjustTrack((MechanicsTrackMutationRequest)argument!, spend: false),
            _ => throw new NotSupportedException($"Focused mechanics double does not implement {method.Name}.")
        };
    }

    private MechanicsCatalog CreateCatalog() => new(new MechanicsCatalogHandle(_next++), () =>
    {
        if (_entities.Count != 0) throw new InvalidOperationException("Mechanics catalog cannot be disposed while bound entities remain.");
        CatalogDisposals++;
    });
    private object? DefineStat()
    {
        if (++DefineStatCalls == FailOnDefineStatCall) throw new InvalidOperationException("Injected mechanics stat-definition failure.");
        return null;
    }
    private MechanicsEntity BindEntity(MechanicsEntityBindRequest request)
    {
        MechanicsEntity entity = null!;
        entity = new(new MechanicsEntityHandle(_next++), () => { EntityDisposals++; _entities.Remove(entity); });
        _entities.Add(entity, new EntityState(request.EntityId));
        return entity;
    }
    private object? SetInitialStat(MechanicsInitialStatRequest request)
    {
        if (++InitialStatCalls == FailOnInitialStatCall) throw new InvalidOperationException("Injected mechanics initial-stat failure.");
        State(request.Entity).Stats.Add(request.Stat, request.Base);
        return null;
    }
    private object? SetInitialTrack(MechanicsInitialTrackRequest request)
    {
        State(request.Entity).Tracks.Add(request.Track, request.Current);
        return null;
    }
    private object? SetInitialComponents(MechanicsInitialComponentsRequest request)
    {
        if (!request.HasInventory) return null;
        EntityState state = State(request.Entity);
        foreach (MechanicsInitialInventoryStack stack in request.InventoryStacks.Span)
            state.Inventory.Add(stack.Definition, stack.Quantity);
        return null;
    }
    private MechanicsEntityReceipt CommitEntity(MechanicsEntity entity)
    {
        EntityState state = State(entity);
        return new(0, 0, StatsRevision(state), TracksRevision(state), default, default, default, default, default, default, default, default);
    }
    private MechanicsStatReadReceipt ReadStat(MechanicsStatReadRequest request)
    {
        EntityState state = State(request.Entity);
        return new(state.Stats[request.Stat], StatsRevision(state));
    }
    private MechanicsStatEvaluationLeaseReceipt EvaluateStat(MechanicsStatOperationRequest request)
    {
        EntityState state = State(request.Entity);
        long value = state.Stats[request.Stat];
        return new(ReadOnlyMemory<MechanicsStatDecisionRow>.Empty, ReadOnlyMemory<MechanicsObservedComponentRevisionRow>.Empty, 0, string.Empty, string.Empty, state.EntityId, request.Stat, value, value, default, default, value, value, value, 0, 10_000, StatsRevision(state), default);
    }
    private MechanicsTrackReadLeaseReceipt ReadTrackCore(MechanicsTrackReadRequest request)
    {
        EntityState state = State(request.Entity);
        return new(ReadOnlyMemory<MechanicsObservedComponentRevisionRow>.Empty, 0, string.Empty, string.Empty, request.Operation, state.EntityId, request.Track, state.Tracks[request.Track], 0, Maximum(state, request.Track), TracksRevision(state), default);
    }
    private MechanicsTrackSetLeaseReceipt SetTrackCore(MechanicsTrackSetRequest request)
    {
        EntityState state = State(request.Entity);
        long before = state.Tracks[request.Track];
        long maximum = Maximum(state, request.Track);
        long target = request.Policy == MechanicsTrackSetPolicy.ClampToBounds ? Math.Clamp(request.Value, 0, maximum) : request.Value;
        state.Tracks[request.Track] = target;
        MechanicsTracksRevision observed = TracksRevision(state);
        state.TrackRevision++;
        return new(ReadOnlyMemory<MechanicsObservedComponentRevisionRow>.Empty, 0, string.Empty, string.Empty, request.Operation, default, state.EntityId, request.Track, request.Policy, target, before, target, 0, maximum, observed, TracksRevision(state), default);
    }
    private MechanicsTrackMutationLeaseReceipt SpendTrack(MechanicsTrackMutationRequest request) => AdjustTrack(request, spend: true);
    private MechanicsTrackMutationLeaseReceipt AdjustTrack(MechanicsTrackMutationRequest request, bool spend)
    {
        SpendRequests.Add(request);
        EntityState state = State(request.Entity);
        long before = state.Tracks[request.Track];
        long maximum = Maximum(state, request.Track);
        long applied = spend ? Math.Min(request.Amount, before) : Math.Min(request.Amount, maximum - before);
        long after = spend ? before - applied : before + applied;
        state.Tracks[request.Track] = after;
        MechanicsTracksRevision observed = TracksRevision(state);
        state.TrackRevision++;
        return new(ReadOnlyMemory<MechanicsObservedComponentRevisionRow>.Empty, 0, string.Empty, string.Empty, request.Operation, default, state.EntityId, request.Track, default, request.Amount, applied, before, after, 0, maximum, observed, TracksRevision(state), default);
    }

    private EntityState State(MechanicsEntity entity) => _entities[entity];
    private static MechanicsStatsRevision StatsRevision(EntityState state) => new(state.EntityId, state.StatRevision, MechanicsRevisionComponent.Stats);
    private static MechanicsTracksRevision TracksRevision(EntityState state) => new(state.EntityId, state.TrackRevision, MechanicsRevisionComponent.Tracks);
    private static long Maximum(EntityState state, string track) => state.Stats[track + "_maximum"];

    private sealed class EntityState(ulong entityId)
    {
        internal ulong EntityId { get; } = entityId;
        internal Dictionary<string, long> Stats { get; } = [];
        internal Dictionary<string, long> Tracks { get; } = [];
        internal Dictionary<string, ulong> Inventory { get; } = [];
        internal ulong StatRevision { get; } = 1;
        internal ulong TrackRevision { get; set; } = 1;
    }
}
