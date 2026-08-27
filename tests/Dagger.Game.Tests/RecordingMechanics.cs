using Rusty.Engine;

namespace Dagger.Game.Tests;

/// <summary>Focused test infrastructure for the reviewed Engine mechanics contract.</summary>
internal sealed class RecordingMechanics : IMechanicsService
{
    private readonly Dictionary<MechanicsEntity, EntityState> _entities = [];
    private ulong _next = 1;

    internal List<MechanicsTrackMutationRequest> SpendRequests { get; } = [];

    public MechanicsCatalog CreateCatalog(MechanicsCatalogCreateRequest request) => new(new MechanicsCatalogHandle(_next++), () => { });
    public void DefineStat(MechanicsStatDefinitionRequest request) { }
    public void DefineTrack(MechanicsTrackDefinitionRequest request) { }
    public void DefineContribution(MechanicsContributionDefinitionRequest request) { }
    public void AdmitCatalog(MechanicsCatalog catalog) { }
    public MechanicsEntity BindEntity(MechanicsEntityBindRequest request)
    {
        MechanicsEntity entity = new(new MechanicsEntityHandle(_next++), () => { });
        _entities.Add(entity, new EntityState(request.EntityId));
        return entity;
    }
    public void SetInitialStat(MechanicsInitialStatRequest request) => State(request.Entity).Stats.Add(request.Stat, request.Base);
    public void SetInitialTrack(MechanicsInitialTrackRequest request) => State(request.Entity).Tracks.Add(request.Track, request.Current);
    public void BindIntrinsicSource(MechanicsIntrinsicSourceRequest request) { }
    public MechanicsEntityReceipt CommitEntity(MechanicsEntity entity) => new(StatsRevision(State(entity)), TracksRevision(State(entity)));
    public MechanicsStatReadReceipt ReadStat(MechanicsStatReadRequest request) { EntityState state = State(request.Entity); return new(state.Stats[request.Stat], StatsRevision(state)); }
    public MechanicsStatEvaluationReceipt EvaluateStat(MechanicsStatOperationRequest request) { EntityState state = State(request.Entity); long value = state.Stats[request.Stat]; return new(value, value, 0, 10_000, StatsRevision(state)); }
    public MechanicsTrackReadReceipt ReadTrack(MechanicsTrackReadRequest request) { EntityState state = State(request.Entity); return new(state.Tracks[request.Track], 0, Maximum(state, request.Track), TracksRevision(state)); }
    public MechanicsStatMutationReceipt SetStatBase(MechanicsStatBaseMutationRequest request) => throw new NotSupportedException();
    public MechanicsTrackSetReceipt SetTrack(MechanicsTrackSetRequest request) => throw new NotSupportedException();
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
    public MechanicsTrackMutationReceipt RestoreTrack(MechanicsTrackMutationRequest request) => throw new NotSupportedException();
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
