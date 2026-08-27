using Rusty.Engine;

namespace Dagger.Game.Tests;

/// <summary>Focused test infrastructure for the reviewed Engine mechanics contract.</summary>
internal sealed class RecordingMechanics : IMechanicsService
{
    private readonly Dictionary<MechanicsEntity, EntityState> _entities = [];
    private ulong _next = 1;

    internal List<MechanicsTrackMutationRequest> SpendRequests { get; } = [];
    internal int CatalogDisposals { get; private set; }
    internal int EntityDisposals { get; private set; }
    internal int? FailOnDefineStatCall { get; set; }
    internal int? FailOnInitialStatCall { get; set; }
    private int DefineStatCalls { get; set; }
    private int InitialStatCalls { get; set; }

    public MechanicsCatalog CreateCatalog(MechanicsCatalogCreateRequest request) => new(new MechanicsCatalogHandle(_next++), () => CatalogDisposals++);
    public void DefineStat(MechanicsStatDefinitionRequest request)
    {
        if (++DefineStatCalls == FailOnDefineStatCall) throw new InvalidOperationException("Injected mechanics stat-definition failure.");
    }
    public void DefineTrack(MechanicsTrackDefinitionRequest request) { }
    public void DefineContribution(MechanicsContributionDefinitionRequest request) { }
    public void AdmitCatalog(MechanicsCatalog catalog) { }
    public MechanicsEntity BindEntity(MechanicsEntityBindRequest request)
    {
        MechanicsEntity entity = null!;
        entity = new(new MechanicsEntityHandle(_next++), () =>
        {
            EntityDisposals++;
            _entities.Remove(entity);
        });
        _entities.Add(entity, new EntityState(request.EntityId));
        return entity;
    }
    public void SetInitialStat(MechanicsInitialStatRequest request)
    {
        if (++InitialStatCalls == FailOnInitialStatCall) throw new InvalidOperationException("Injected mechanics initial-stat failure.");
        State(request.Entity).Stats.Add(request.Stat, request.Base);
    }
    public void SetInitialTrack(MechanicsInitialTrackRequest request) => State(request.Entity).Tracks.Add(request.Track, request.Current);
    public void BindIntrinsicSource(MechanicsIntrinsicSourceRequest request) { }
    public MechanicsEntityReceipt CommitEntity(MechanicsEntity entity) => new(StatsRevision(State(entity)), TracksRevision(State(entity)));
    public MechanicsStatReadReceipt ReadStat(MechanicsStatReadRequest request) { EntityState state = State(request.Entity); return new(state.Stats[request.Stat], StatsRevision(state)); }
    public MechanicsStatEvaluationReceipt EvaluateStat(MechanicsStatOperationRequest request) { EntityState state = State(request.Entity); long value = state.Stats[request.Stat]; return new(value, value, 0, 10_000, StatsRevision(state)); }
    public MechanicsTrackReadReceipt ReadTrack(MechanicsTrackReadRequest request) { EntityState state = State(request.Entity); return new(state.Tracks[request.Track], 0, Maximum(state, request.Track), TracksRevision(state)); }
    public MechanicsStatMutationReceipt SetStatBase(MechanicsStatBaseMutationRequest request) => throw new NotSupportedException();
    public MechanicsTrackSetReceipt SetTrack(MechanicsTrackSetRequest request)
    {
        EntityState state = State(request.Entity);
        long before = state.Tracks[request.Track];
        long minimum = 0;
        long maximum = Maximum(state, request.Track);
        long target = request.Policy == MechanicsTrackSetPolicy.ClampToBounds ? Math.Clamp(request.Value, minimum, maximum) : request.Value;
        state.Tracks[request.Track] = target;
        ulong observed = state.TrackRevision++;
        return new(target, before, target, minimum, maximum, new MechanicsTracksRevision(state.EntityId, observed, MechanicsRevisionComponent.Tracks), TracksRevision(state));
    }
    public MechanicsTrackMutationReceipt SpendTrack(MechanicsTrackMutationRequest request)
    {
        SpendRequests.Add(request);
        EntityState state = State(request.Entity);
        long before = state.Tracks[request.Track];
        long maximum = Maximum(state, request.Track);
        long applied = Math.Min(request.Amount, before);
        long after = before - applied;
        state.Tracks[request.Track] = after;
        ulong observed = state.TrackRevision++;
        return new(request.Amount, applied, before, after, 0, maximum, new MechanicsTracksRevision(state.EntityId, observed, MechanicsRevisionComponent.Tracks), TracksRevision(state));
    }
    public MechanicsTrackMutationReceipt RestoreTrack(MechanicsTrackMutationRequest request)
    {
        EntityState state = State(request.Entity);
        long before = state.Tracks[request.Track];
        long maximum = Maximum(state, request.Track);
        long applied = Math.Min(request.Amount, maximum - before);
        long after = before + applied;
        state.Tracks[request.Track] = after;
        ulong observed = state.TrackRevision++;
        return new(request.Amount, applied, before, after, 0, maximum, new MechanicsTracksRevision(state.EntityId, observed, MechanicsRevisionComponent.Tracks), TracksRevision(state));
    }
    public MechanicsTrackReconciliationReceipt ReconcileTrack(MechanicsTrackReconciliationRequest request) => throw new NotSupportedException();

    private EntityState State(MechanicsEntity entity) => _entities[entity];
    private static MechanicsStatsRevision StatsRevision(EntityState state) => new(state.EntityId, state.StatRevision, MechanicsRevisionComponent.Stats);
    private static MechanicsTracksRevision TracksRevision(EntityState state) => new(state.EntityId, state.TrackRevision, MechanicsRevisionComponent.Tracks);
    private static long Maximum(EntityState state, string track) => state.Stats[track + "_maximum"];

    private sealed class EntityState(ulong entityId)
    {
        internal ulong EntityId { get; } = entityId;
        internal Dictionary<string, long> Stats { get; } = [];
        internal Dictionary<string, long> Tracks { get; } = [];
        internal ulong StatRevision { get; } = 1;
        internal ulong TrackRevision { get; set; } = 1;
    }
}
