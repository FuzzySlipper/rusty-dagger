using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Entities;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Presentation;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Loot;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// The site the player is in, as the exterior reads it: which profile is active and its projection, the admitted
/// catalog, and the action triggers that move with the origin. The site's owner answers.
/// </summary>
internal interface IDaggerfallExteriorSite
{
    DaggerfallWorldProfileKey ActiveProfile { get; }

    /// <summary>The active site's live projection: outdoors, the town the player is in.</summary>
    DaggerfallSiteProjection Projection { get; }

    DaggerfallSiteProfiles? Profiles { get; }

    /// <summary>The active profile's action triggers move with an origin commit.</summary>
    void RebaseActionTriggers(Vector3 delta);

    /// <summary>The player's place among the action triggers is read again in the new frame.</summary>
    void RebaseRestoredPlayerTriggers();
}

/// <summary>
/// The session's save boundary for one site: what its actors, inventories, corpses, doors and effects become as it
/// leaves, and what they were as it returns. The session's persistence implements it.
/// </summary>
internal interface IDaggerfallSiteDeltas
{
    /// <summary>Captures a site's live state, its actors limited to <paramref name="actorFilter"/> and moved into its profile frame.</summary>
    DaggerfallSiteRuntimeDelta Capture(DaggerfallSiteProfile inputs, DaggerfallSiteProjection projection,
        IReadOnlyDictionary<long, DaggerfallActorId> dynamicActors, IReadOnlySet<long>? actorFilter, Vector3 actorFrameOffset);

    /// <summary>Reapplies a returning site's inventories, corpses, doors and effects onto its materialized actors.</summary>
    void Restore(DaggerfallSiteRuntimeDelta delta);
}

/// <summary>
/// The session consequences the exterior reaches that it does not own. The exterior calls each at one fixed point; the
/// session answers.
/// </summary>
internal interface IDaggerfallExteriorHost
{
    /// <summary>Installs a landing pose, clearing retained input and open interactions first.</summary>
    void RelocatePlayer(WorldPoint position, float yawRadians, float pitchRadians);

    /// <summary>Rebuilds contextual activation over the current projection.</summary>
    void RebuildActivation();

    /// <summary>Binds live session presentation policy to every newly created projection.</summary>
    void ConfigureSiteAppearance(DaggerfallSiteAppearance appearance);

    void RebaseTransientWorld(Vector3 delta);

    /// <summary>Admits retained source-civilian billboards into a resident projection.</summary>
    void AdmitResidentCivilianAppearances(DaggerfallSiteProjection projection);

    /// <summary>
    /// The collision a staged site change handed off is live: the actors it placed while that collision was still to
    /// come are settled on the floor below them now.
    /// </summary>
    void GroundArrivedActors();
}

/// <summary>
/// The exterior: the cells streamed around the player and the towns placed on them. It owns the seven-by-seven cell
/// window and its save, the cells' ground and the collider standing on it, the environment (terrain, water, nature,
/// the sea), the session origin and its rebase, the resident neighbouring towns and the active town's admission as the
/// window moves, and the collision every site places, in the origin's frame. The site the player is in is read through
/// <see cref="IDaggerfallExteriorSite"/>; nothing here names the session.
/// </summary>
/// <remarks>
/// <para><b>Off the update.</b> This is the one place the ruleset computes off the update, and the rule is this:</para>
/// <list type="bullet">
/// <item>What may run on the thread pool is pure product computation that calls no Engine service and reads only what
/// nothing changes while it runs: a cell's ground (<see cref="DaggerfallExteriorCellGrounds"/>) is built from the terrain
/// and location definitions and the ground tuning, with the location ground already read on the update.</item>
/// <item>The ground function (<see cref="DaggerfallHorizonGround"/>) is sampled by the Engine's streamer workers. They read
/// only the snapshot the update hands it (the locations' ground read so far, replaced whole, and the frame set before the
/// tiles it changes are invalidated); content is never read on a worker.</item>
/// <item>Results are taken only on the update: a cell the update needs before a worker has taken up its build is built on
/// the update, and one a worker is running is waited for. Collision handed to the Engine to prepare off the update
/// (<see cref="DaggerfallDeferredCollision"/>) is followed on the update, and play holds until it is live.</item>
/// </list>
/// </remarks>
internal sealed class DaggerfallExteriorWorld
{
    private readonly IEngineContext _engine;
    private readonly ActorsState _actors;
    private readonly DaggerfallNpcRegistry _npcs;
    private readonly PlayerControlState _player;
    private readonly DaggerfallEffectLifecycle _effects;
    private readonly DaggerfallDefinitions _definitions;
    private readonly DaggerfallSiteTuning _tuning;
    private readonly IRandomService _random;
    private readonly DaggerfallWorldTime _time;
    private readonly DaggerfallSiteContext _site;
    private readonly SpatialMovementSystem _spatial;
    private readonly FirstPersonCameraSystem _camera;
    private readonly DaggerfallSiteAudioBundles? _audioBundles;
    private readonly DaggerfallActorRoster _roster;
    private readonly IDaggerfallSiteDeltas _siteDeltas;
    private readonly DaggerfallGroundContainers _groundContainers;
    private readonly IDaggerfallExteriorSite _active;
    private readonly IDaggerfallExteriorHost _host;
    // The inactive sites' detached state, which the site's owner holds: a town leaving the window parks its state here.
    private readonly Dictionary<DaggerfallWorldProfileKey, DaggerfallSiteRuntimeDelta> _deltas;
    /// <summary>The session-wide classic presentation every site projection draws held items and effects from.</summary>
    private readonly NormalizedClassicPresentation? _sessionPresentation;
    private DaggerfallExteriorCellResidency? _exteriorResidency;
    private DaggerfallHorizonGround? _ground;
    private Func<IEnumerable<Vector3>> _walkers = static () => [];
    private DaggerfallExteriorGroundCollider? _groundCollider;
    private Action<List<AppearanceFact>>? _seaFacts;
    private Action? _seaAccepted;
    private Action? _releaseSea;
    // A window was admitted whose ground is still to stand: what arrives with it waits (CollisionPending).
    private bool _groundAwaited;
    private DaggerfallExteriorTerrainAppearance? _exteriorTerrainAppearance;
    private (DaggerfallArtReplacements Art, Func<bool> Refreshed)? _groundArt;
    private DaggerfallGroundBlendSettings _groundBlend;
    private DaggerfallExteriorEnvironment? _exteriorEnvironment;
    private DaggerfallExteriorCellGrounds? _exteriorGrounds;
    private Dictionary<DaggerfallExteriorCellId, DaggerfallSiteExterior>? _exteriorLocations;
    private Dictionary<DaggerfallSiteId, DaggerfallExteriorCellId>? _exteriorCellsBySite;
    private Dictionary<DaggerfallExteriorCellId, DaggerfallSiteId>? _exteriorSitesByCell;
    private readonly Dictionary<DaggerfallExteriorCellId, float> _exteriorLocationHeights = [];
    private readonly HashSet<ulong> _exteriorWaterTriggers = [];
    private readonly Dictionary<DaggerfallWorldProfileKey, ResidentExteriorLocation> _residentExteriorLocations = [];
    private bool _locationLoaded;
    private DaggerfallExteriorCellId? _locationCell;
    private bool _admittingInitialResidency;
    private bool _retired;
    // A staged site change holds its location placements to hand them over deferred, and stands the arriving player
    // only once they and the ground under the arrival are live.
    private bool _deferringCollision;
    // The window is moving with the player on the road: the locations it brings in prepare their presentation off the update too.
    private bool _onTheRoad;
    private bool _arrivalStandPending;
    // Collision a site change handed off that play must not run without: the Engine refused it, or the change failed
    // with it still being prepared. The world is held until it is live and the arrival stood on it.
    private bool _awaitingGround;
    private DaggerfallDeferredCollision? _deferredCollision;
    // The location parts placed, by placement id, as the product means them and as handed to the Engine. They differ
    // only while a staged change holds its placements (_heldPlacementGrid), or after the Engine refused a batch.
    private readonly Dictionary<ulong, PlacedLocationPart> _placements = [];
    private Dictionary<ulong, PlacedLocationPart> _enginePlacements = [];
    private ulong? _heldPlacementGrid;
    private ulong? _placementGrid;
    // The Engine composes placed artifacts on the local frame current when the first one was placed,
    // and moves that grid with every origin commit. A new game first places at origin zero; a restore
    // commits the saved origin before placing anything, so placements are expressed against this.
    private Vector3? _artifactFrameCompensation;

    internal DaggerfallExteriorWorld(IEngineContext engine, ActorsState actors, DaggerfallNpcRegistry npcs, PlayerControlState player,
        DaggerfallEffectLifecycle effects, DaggerfallDefinitions definitions,
        DaggerfallSiteTuning tuning, DaggerfallWorldTime time, DaggerfallSiteContext site, SpatialMovementSystem spatial,
        FirstPersonCameraSystem camera, DaggerfallSiteAudioBundles? audioBundles, DaggerfallActorRoster roster,
        IDaggerfallSiteDeltas siteDeltas, DaggerfallGroundContainers groundContainers, IDaggerfallExteriorSite active,
        IDaggerfallExteriorHost host, Dictionary<DaggerfallWorldProfileKey, DaggerfallSiteRuntimeDelta> deltas,
        NormalizedClassicPresentation? sessionPresentation = null)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _actors = actors ?? throw new ArgumentNullException(nameof(actors));
        _npcs = npcs ?? throw new ArgumentNullException(nameof(npcs));
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _effects = effects ?? throw new ArgumentNullException(nameof(effects));
        if (!_actors.Store.Diagnostics(0).Components.Any(component => component.Key == GlobalPositions.Key))
            _actors.Store.Register(GlobalPositions);
        _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
        _tuning = tuning ?? throw new ArgumentNullException(nameof(tuning));
        _groundBlend = tuning.Ground.Blend;
        _random = engine.Random;
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _site = site ?? throw new ArgumentNullException(nameof(site));
        _spatial = spatial ?? throw new ArgumentNullException(nameof(spatial));
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));
        _audioBundles = audioBundles;
        _roster = roster ?? throw new ArgumentNullException(nameof(roster));
        _siteDeltas = siteDeltas ?? throw new ArgumentNullException(nameof(siteDeltas));
        _groundContainers = groundContainers ?? throw new ArgumentNullException(nameof(groundContainers));
        _active = active ?? throw new ArgumentNullException(nameof(active));
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _deltas = deltas ?? throw new ArgumentNullException(nameof(deltas));
        _sessionPresentation = sessionPresentation;
        _locationLoaded = active.ActiveProfile.Kind != DaggerfallWorldProfileKind.Exterior;
    }

    private DaggerfallWorldProfileKey ActiveProfile => _active.ActiveProfile;
    private DaggerfallSiteProjection Projection => _active.Projection;
    private DaggerfallSiteProfiles? Profiles => _active.Profiles;

    private DaggerfallSiteProfiles RequireProfiles() =>
        Profiles ?? throw new InvalidOperationException("Site profiles have not been admitted.");

    private DaggerfallAudioBundle? AudioFor(DaggerfallSiteProfile inputs) => _audioBundles?.Require(inputs);

    /// <summary>
    /// The product column the Engine <see cref="EntityOriginRebaser"/> reads global positions from.
    /// Values exist only for the duration of one origin commit.
    /// </summary>
    internal static readonly ComponentType<WorldOriginGlobalPosition> GlobalPositions =
        ComponentType<WorldOriginGlobalPosition>.Create(ProductComponentKeys.Create(GlobalPositionComponentId));
    private const uint GlobalPositionComponentId = 1;

    // A retired neighbour's drawn location stays in the last accepted snapshot until the next one is accepted
    // without it; only then can the Engine release it.
    private readonly List<IDisposable> _retiredResidentLocations = [];
    // Exterior artifacts publish navigation cells at the source terrain's 0.8-unit grid. One
    // wilderness map pixel is 819.2 world units, so adjacent location closures are exactly 1024
    // navigation cells apart in the shared Engine artifact grid.
    internal const long NavigationCellsPerExteriorCell = 1024;
    private const float NavigationCellSize = DaggerfallExteriorCellResidency.CellSize / NavigationCellsPerExteriorCell;

    /// <summary>
    /// A nearby normalized exterior profile has the same lifetime as its cell admission, while the
    /// selected profile remains the session's live Projection. Keeping this small owner separate
    /// prevents adjacent doors, actors and appearance resources from being mistaken for the active
    /// interaction projection or silently sharing its durable identity.
    /// </summary>
    private sealed class ResidentExteriorLocation(DaggerfallSiteProfile profile, DaggerfallSiteProjection projection, IReadOnlySet<long> actorIds, int slot)
    {
        internal DaggerfallSiteProfile Profile { get; } = profile;
        internal DaggerfallSiteProjection Projection { get; } = projection;
        internal IReadOnlySet<long> ActorIds { get; } = actorIds;
        /// <summary>The presentation identity slot the active snapshot draws this location under.</summary>
        internal int Slot { get; } = slot;
    }

    /// <summary>One placed part of a location's collision and navigation, with the profile whose source resolves it.</summary>
    private sealed record PlacedLocationPart(SpatialContentArtifactPlacement Placement, DaggerfallSiteProfile Profile);

    /// <summary>Whether the active profile's location resources and actor roster are currently admitted.</summary>
    internal bool ActiveLocationLoaded =>ActiveProfile.Kind != DaggerfallWorldProfileKind.Exterior || _locationLoaded;

    /// <summary>Source-backed terrain, water and nature facts for the admitted exterior window.</summary>
    internal DaggerfallExteriorEnvironment? ExteriorEnvironment => _exteriorEnvironment;

    /// <summary>
    /// Supplies the current player or NPC movement proposal with the one admitted site environment
    /// plus source-derived water for the resident exterior window. Water trigger registration is
    /// reconciled here because the spatial session owns trigger lifetime while the environment owns
    /// only source facts and the active origin transforms them into Engine-local bounds.
    /// </summary>
    internal CharacterStepEnvironment CharacterEnvironment(CharacterMotion motion)
    {
        CharacterStepEnvironment[] parts = [Projection.CharacterEnvironment(motion), .. _residentExteriorLocations.Values
            .OrderBy(value => value.Profile.ProfileKey.LogicalId, StringComparer.Ordinal)
            .Select(resident => resident.Projection.CharacterEnvironment(motion))];
        IReadOnlyList<CharacterWaterVolume>? sourceWater = ActiveProfile.Kind == DaggerfallWorldProfileKind.Exterior
            && Projection.Inputs.ProfileKey == ActiveProfile
            && _exteriorResidency is { IsInitialized: true } residency
            && _exteriorEnvironment is { IsInitialized: true } environment
                ? environment.CharacterWaterVolumes(ActiveOrigin(residency)) : null;
        // Each part keeps its arrays while it is unchanged, so the joined environment is too: a city's doors are
        // thousands of obstacles, and every proposal of every character asks for them.
        if (parts.AsSpan().SequenceEqual(_environmentParts) && ReferenceEquals(sourceWater, _environmentWater))
            return _environment;
        CharacterStepEnvironment joined = JoinCharacterEnvironment(parts, sourceWater);
        (_environmentParts, _environmentWater, _environment) = (parts, sourceWater, joined);
        return joined;
    }

    private CharacterStepEnvironment[] _environmentParts = [];
    private IReadOnlyList<CharacterWaterVolume>? _environmentWater;
    private CharacterStepEnvironment _environment;

    private CharacterStepEnvironment JoinCharacterEnvironment(CharacterStepEnvironment[] parts, IReadOnlyList<CharacterWaterVolume>? sourceWater)
    {
        CharacterStepEnvironment projection = parts.Length == 1 ? parts[0] : DaggerfallSiteProjection.CombineCharacterEnvironments(parts);
        if (sourceWater is null) return projection;

        Dictionary<ulong, CharacterWaterVolume> water = [];
        foreach (CharacterWaterVolume volume in projection.WaterVolumes.Span)
        {
            CharacterWaterVolume validated = volume.Validate();
            if (!water.TryAdd(validated.Trigger, validated))
                throw new InvalidOperationException($"Projection published duplicate water trigger {validated.Trigger}.");
        }
        foreach (CharacterWaterVolume volume in sourceWater)
        {
            CharacterWaterVolume validated = volume.Validate();
            if (!water.TryAdd(validated.Trigger, validated))
                throw new InvalidOperationException($"Exterior water trigger {validated.Trigger} overlaps an admitted projection trigger.");
        }
        ReconcileExteriorWaterTriggers(sourceWater);
        if (sourceWater.Count == 0) return projection;
        return new CharacterStepEnvironment(projection.Support, projection.Obstacles, projection.MeshInstances,
            water.Values.OrderBy(value => value.Trigger).ToArray());
    }

    /// <summary>The water someone at a position is really in, given the volume they are inside; none past a river's banks.</summary>
    internal CharacterWaterVolume? WaterAt(CharacterWaterVolume? volume, Vector3 position) =>
        volume is { } water && _exteriorResidency is { IsInitialized: true } residency && _exteriorEnvironment is { IsInitialized: true } environment
            ? environment.AtSurface(water, position, ActiveOrigin(residency)) : volume;

    private static ulong LocationPlacementId(DaggerfallWorldProfileKey profile, DaggerfallSiteSpatialPart part) =>
        StableHash.NonZeroFnv1a64($"daggerfall.location-artifact.v1|{profile.LogicalId}|{part.Id}");

    /// <summary>The Engine placement identities of every collision/navigation part a profile places.</summary>
    internal static ulong[] LocationPlacementIds(DaggerfallSiteProfile profile) =>
        [.. profile.Geometry.Spatial.Select(part => LocationPlacementId(profile.ProfileKey, part))];

    /// <summary>
    /// A profile's collision/navigation parts, each placed by its own whole-cell offset in the profile frame
    /// and, for an exterior, by the location's map-pixel offset from the active origin and its frame in that map pixel
    /// (<see cref="ExteriorLocationFrame"/>): whole grid cells across, its terrain height up.
    /// </summary>
    private SpatialContentArtifactPlacement[] LocationPlacements(
        DaggerfallSiteProfile profile, Vector3 frameCompensation,
        DaggerfallExteriorWorldOrigin? exteriorOrigin = null)
    {
        long columnOffset = 0, rowOffset = 0;
        Vector3 translation = Vector3.Zero;
        if (profile.ProfileKind == DaggerfallWorldProfileKind.Exterior)
        {
            if (!TryExteriorProfileCell(profile, out DaggerfallExteriorCellId cell))
                throw new InvalidOperationException($"Exterior artifact '{profile.ProfileKey.LogicalId}' has no normalized map-pixel identity.");
            DaggerfallExteriorWorldOrigin origin = exteriorOrigin
                ?? (_exteriorResidency is { IsInitialized: true } residency
                    ? ActiveOrigin(residency)
                    : DaggerfallExteriorWorldOrigin.At(cell));
            (long frameColumns, long frameRows) = ExteriorLocationFrameCells(profile);
            columnOffset = checked(((long)(cell.X - origin.MapPixelX) * NavigationCellsPerExteriorCell) + frameColumns);
            rowOffset = checked(((long)(cell.Y - origin.MapPixelY) * NavigationCellsPerExteriorCell) + frameRows);
            translation = Vector3.UnitY * ExteriorLocationSampleHeight(profile);
        }

        // Translation is in the composition's frame, not the current local one.
        translation += frameCompensation;
        return [.. profile.Geometry.Spatial.Select(part => new SpatialContentArtifactPlacement(
            LocationPlacementId(profile.ProfileKey, part), part.Path, part.Sha256,
            ColumnOffset: checked(columnOffset + part.ColumnOffset),
            RowOffset: checked(rowOffset + part.RowOffset),
            Translation: translation))];
    }

    /// <summary>
    /// Places one profile's parts and removes others in one Engine admission, holding the source its parts
    /// resolve from (the per-block publication's bundle, for an assembled location) open for the call.
    /// </summary>
    /// <remarks>
    /// Inside a staged site change, or while the window moves on the road, a town's placements and removals are held
    /// instead, and handed to the Engine as one deferred batch once the change's own synchronous calls are done
    /// (<see cref="ReleaseHeldPlacements"/>): composing a town's navigation takes hundreds of milliseconds. A dungeon's or an interior's placement is small and is made
    /// at once, after anything held, so the Engine receives them in the product's order.
    /// </remarks>
    internal void ApplyLocationResidency(DaggerfallSiteProfile? placed, DaggerfallExteriorWorldOrigin? exteriorOrigin,
        IEnumerable<ulong> removals, ulong navigationGridId)
    {
        // Before any placement there is nothing resident to remove, and the composition starts with its first artifact.
        if (placed is null && _artifactFrameCompensation is null) return;
        Vector3 frame = _artifactFrameCompensation ?? EngineCompensation();
        SpatialContentArtifactPlacement[] placements = placed is null ? [] : LocationPlacements(placed, frame, exteriorOrigin);
        ulong[] removed = [.. removals];
        bool hold = _deferringCollision && placed is not { ProfileKind: not DaggerfallWorldProfileKind.Exterior };
        // One request composes one navigation grid.
        if (!hold || _heldPlacementGrid is { } grid && grid != navigationGridId) ReleaseHeldPlacements();
        if (!hold)
        {
            using IDisposable? source = placed?.Geometry.SpatialSource?.Invoke();
            _ = _spatial.ApplyContentArtifactResidency(placements, removed, navigationGridId);
            Place(_enginePlacements, placements, placed, removed);
        }
        else _heldPlacementGrid = navigationGridId;
        Place(_placements, placements, placed, removed);
        _placementGrid = navigationGridId;
        _artifactFrameCompensation = frame;
    }

    private static void Place(Dictionary<ulong, PlacedLocationPart> parts, SpatialContentArtifactPlacement[] placements,
        DaggerfallSiteProfile? profile, ulong[] removals)
    {
        foreach (ulong id in removals) parts.Remove(id);
        foreach (SpatialContentArtifactPlacement placement in placements) parts[placement.Id] = new(placement, profile!);
    }

    /// <summary>
    /// Hands the Engine, as one deferred batch, what the placements held since the change began amount to: the parts
    /// placed or moved and the parts gone. A part placed and removed again inside the change, or removed and placed
    /// back unchanged (a failed change's rollback), costs nothing. A refused batch returns what the Engine is known
    /// to have to before it, so handing the placements over again repeats it. A handover that throws (an artifact
    /// that does not resolve, a request the Engine rejects in the call) leaves them held, to be handed over again.
    /// </summary>
    private void ReleaseHeldPlacements()
    {
        if (_heldPlacementGrid is not { } grid) return;
        HandOverPlacements(grid);
        _heldPlacementGrid = null;
    }

    private void HandOverPlacements(ulong grid)
    {
        PlacedLocationPart[] placing = [.. _placements.Values
            .Where(part => !_enginePlacements.TryGetValue(part.Placement.Id, out PlacedLocationPart? handed) || handed.Placement != part.Placement)
            .OrderBy(part => part.Placement.Id)];
        ulong[] removing = [.. _enginePlacements.Keys.Where(id => !_placements.ContainsKey(id)).Order()];
        if (placing.Length == 0 && removing.Length == 0) return;
        DaggerfallDeferredCollision deferred = DeferredCollision();
        List<IDisposable> sources = [];
        try
        {
            foreach (DaggerfallSiteProfile profile in placing.Select(part => part.Profile).Distinct())
                if (profile.Geometry.SpatialSource?.Invoke() is { } source) sources.Add(source);
            CollisionResidencyReadout before = deferred.Read();
            SpatialContentArtifactResidencyReceipt receipt = _spatial.ApplyContentArtifactResidency(
                placing.Select(part => part.Placement), removing, grid, deferred: true);
            Dictionary<ulong, PlacedLocationPart> engineBefore = _enginePlacements;
            _enginePlacements = new(_placements);
            deferred.Track(receipt.DeferredBatch, before, () => _enginePlacements = engineBefore);
        }
        finally
        {
            foreach (IDisposable source in sources) source.Dispose();
        }
    }

    /// <summary>The session's one follower of collision handed to the Engine to prepare off the update.</summary>
    private DaggerfallDeferredCollision DeferredCollision() =>
        _deferredCollision ??= new DaggerfallDeferredCollision(_engine.Spatial, _spatial.Session);

    /// <summary>
    /// Whether collision a staged site change admits is still held or being prepared: the floor below an arrival may
    /// not be there yet, so anything settled onto it waits.
    /// </summary>
    internal bool CollisionPending =>
        _groundAwaited || _heldPlacementGrid is not null || _deferredCollision is { Awaiting: true };

    internal void AdmitLocationArtifact(DaggerfallSiteProfile profile, DaggerfallExteriorWorldOrigin? exteriorOrigin = null) =>
        ApplyLocationResidency(profile, exteriorOrigin, [], profile.Geometry.NavigationGridId);

    private void RemoveLocationArtifact(DaggerfallSiteProfile profile) =>
        ApplyLocationResidency(null, null, LocationPlacementIds(profile), profile.Geometry.NavigationGridId);

    /// <summary>Nearby exterior profiles whose full location closures are currently admitted.</summary>
    internal IReadOnlyCollection<DaggerfallWorldProfileKey> ResidentExteriorProfiles =>
        _residentExteriorLocations.Keys.OrderBy(key => key.Site.Region)
            .ThenBy(key => key.Site.Index).ThenBy(key => key.LogicalId, StringComparer.Ordinal).ToArray();

    /// <summary>
    /// Stands a player placed at an anchor on the admitted collision below it. Anchors (a door's threshold, a location's
    /// edge, an interior's or dungeon's enter and start markers, a quest marker) name the floor the player arrives on, while
    /// the player's position is its capsule's centre; the donor likewise lifts every arrival onto its floor
    /// (<c>StreamingWorld.RepositionPlayer</c> outside, <c>PlayerEnterExit.SetStanding</c> after entering a building or a
    /// dungeon). A pose that already stands (a recalled or remembered capsule centre) keeps its height. Runs once the
    /// destination's collision, and outside its terrain window, are admitted.
    /// Outside, the terrain under the arrival bounds it as the donor's <c>RepositionPlayer</c> does: an anchor below the
    /// terrain (a location's edge on rising ground) is raised onto it, and one above the terrain settles onto the highest
    /// support between them (the terrain, or a structure standing on it) rather than falling there.
    /// </summary>
    internal void StandArrivingPlayer()
    {
        // The ground this arrival would stand on is still to be prepared: stand once it is live (AwaitArrivalCollision).
        if (CollisionPending)
        {
            _arrivalStandPending = true;
            return;
        }
        PlayerControlState player = _player;
        WorldPoint arrival = player.Position ?? throw new InvalidOperationException("An arrival requires a player position.");
        WorldPoint start = arrival;
        float settle = 0F;
        if (TryExteriorTerrainHeight(arrival, out float terrain))
        {
            start = arrival with { Y = MathF.Max(arrival.Y, terrain) };
            settle = start.Y - terrain;
        }
        WorldPoint standing = _spatial.StandingPosition(start, CharacterEnvironment(player.Motion), settle);
        if (standing != arrival) _host.RelocatePlayer(standing, player.YawRadians, player.PitchRadians);
    }

    /// <summary>
    /// Runs a staged site change with its collision prepared off the update: its towns' placements are held while the
    /// change makes its own synchronous collision and content calls, which would otherwise wait for them, then handed
    /// to the Engine as a deferred batch, and the ground under the arrival streams in meanwhile. An arrival inside the
    /// change is stood, and the actors it placed are settled, once <see cref="AwaitArrivalCollision"/> finds both live. Play does not
    /// resume meanwhile: the loading screen holds the updates. A change that fails with its rollback's collision still
    /// to come leaves the world held until it is live (<see cref="RecoverArrivalGround"/>). So does a handover that
    /// throws: what it could not hand over stays held, and a change that had otherwise succeeded fails with it, its
    /// arrival committed but still to be stood.
    /// </summary>
    internal void DeferCollision(Action change)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (_deferringCollision) throw new InvalidOperationException("A site change is already deferring its collision.");
        _deferringCollision = true;
        bool changed = false;
        Exception? handover = null;
        try
        {
            change();
            changed = true;
        }
        finally
        {
            _deferringCollision = false;
            // A failed change's rollback has made its own placements and window; whatever is still held matches what
            // they record, so the Engine is given it either way.
            try
            {
                ReleaseHeldPlacements();
            }
            catch (Exception error)
            {
                handover = error;
            }
            // A rolled-back change has nothing to stand; a committed one stands once its collision is live.
            if (!changed) _arrivalStandPending = false;
            if (!changed || handover is not null)
            {
                if (CollisionPending) _awaitingGround = true;
                else _host.GroundArrivedActors();
            }
        }
        // The change's own failure, if any, has already left this method; a handover failure fails a committed one.
        if (handover is not null) throw new DaggerfallArrivalCollisionRefusedException(handover);
    }

    /// <summary>
    /// How far the collision a <see cref="DeferCollision"/> change handed off has come; once it is live, stands the
    /// arriving player on it, settles the actors placed with it, and reports done. A refused batch fails the arrival:
    /// the Engine kept none of it, so the arrival waits, still to be stood, while <see cref="RecoverArrivalGround"/>
    /// hands it over again.
    /// </summary>
    internal LoadingProgress AwaitArrivalCollision()
    {
        DaggerfallDeferredCollisionReading reading = _deferredCollision?.Poll() ?? DaggerfallDeferredCollisionReading.Live;
        switch (reading.State)
        {
            case DaggerfallDeferredCollisionState.Refused:
                _awaitingGround = true;
                throw new DaggerfallArrivalCollisionRefusedException();
            case DaggerfallDeferredCollisionState.Preparing:
                return reading.Fraction is { } fraction ? LoadingProgress.Measured(fraction) : LoadingProgress.Unmeasured;
        }
        if (_groundAwaited)
        {
            LoadingProgress ground = AwaitGroundCollider();
            if (!ground.Done) return ground;
        }
        // What a handover that threw left held is not live either: RecoverArrivalGround hands it over again.
        if (CollisionPending) return LoadingProgress.Unmeasured;
        FinishArrival();
        return LoadingProgress.Finished;
    }

    /// <summary>Stands the arrival and whoever was placed with it, now that what they stand on is live.</summary>
    private void FinishArrival()
    {
        if (_arrivalStandPending)
        {
            _arrivalStandPending = false;
            StandArrivingPlayer();
        }
        _host.GroundArrivedActors();
    }

    /// <summary>
    /// Keeps the ground under the player and the walkers in the active exterior: anyone else out in the wilderness, whom
    /// no town ground plane holds up. It stands in one map pixel's frame, so travel makes it anew.
    /// </summary>
    private void UpdateGroundCollider()
    {
        if (ActiveProfile.Kind != DaggerfallWorldProfileKind.Exterior
            || _exteriorResidency is not { IsInitialized: true } residency
            || _player.Position is not WorldPoint player) return;
        DaggerfallExteriorWorldOrigin origin = ActiveOrigin(residency);
        if (_groundCollider is { } standing && standing.MapPixel != (origin.MapPixelX, origin.MapPixelY))
        {
            DropGroundCollider();
            _groundAwaited = true;
        }
        _groundCollider ??= new(_engine, _spatial.Session, _ground ?? throw new InvalidOperationException("Exterior ground is admitted before its function."), origin);
        _groundCollider.Update(_walkers().Prepend(player.ToVector()), origin);
    }

    /// <summary>
    /// How far the ground under the player and the walkers has come. The tiles arrive over updates, so this also keeps
    /// them coming; a tile the Engine refused would leave someone standing on nothing, so it fails the arrival instead.
    /// </summary>
    internal LoadingProgress AwaitGroundCollider()
    {
        UpdateGroundCollider();
        if (_groundCollider is not { } collider)
        {
            _groundAwaited = false;
            return LoadingProgress.Finished;
        }
        HeightfieldColliderReadout read = collider.Read();
        if (read.RefusedTiles != 0)
        {
            _awaitingGround = true;
            throw new DaggerfallArrivalCollisionRefusedException();
        }
        if (!read.Ready) return LoadingProgress.Measured(read.ResidentTiles / Math.Max(1d, read.ResidentTiles + read.PendingTiles));
        bool arrived = _groundAwaited;
        _groundAwaited = false;
        // Staged collision still on its way has the last word: AwaitArrivalCollision finishes the arrival after it.
        if (arrived && !CollisionPending) FinishArrival();
        return LoadingProgress.Finished;
    }

    /// <summary>Lets go of the ground, which dies with the spatial session it stands in.</summary>
    internal void DropGroundCollider()
    {
        _groundCollider?.Dispose();
        _groundCollider = null;
    }

    /// <summary>
    /// Brings back the collision a site change handed off and play must not run without, one admitted update at a
    /// time; true when there is nothing to wait for. Until then the world must not run: nothing holds the player or
    /// anyone else up. Each call reads the batches handed over; once they are refused, or a handover threw, it hands
    /// the placements and the window over again off the update, and once they are live, stands the arrival on them.
    /// Locations handed over on the road are followed here too, and hold nothing until the Engine refuses them.
    /// </summary>
    internal bool RecoverArrivalGround()
    {
        SettleRoadsideCollision();
        if (!_awaitingGround)
        {
            // Ground that a character fell through while it was missing would install around them, which the Engine
            // refuses: anyone moved past its reach waits for it here, as an arrival does, rather than falling.
            try { return AwaitGroundCollider().Done; }
            catch (DaggerfallArrivalCollisionRefusedException) { return false; }
        }
        try
        {
            if (_deferredCollision is not { Awaiting: true })
                DeferCollision(() =>
                {
                    _heldPlacementGrid = _placementGrid;
                    if (ActiveProfile.Kind == DaggerfallWorldProfileKind.Exterior) _ = UpdateExteriorResidency();
                });
            if (!AwaitArrivalCollision().Done) return false;
        }
        catch (DaggerfallArrivalCollisionRefusedException)
        {
            // Refused again, or a handover threw again: what is not live stays held, and the next update retries.
            return false;
        }
        _awaitingGround = false;
        return true;
    }

    /// <summary>
    /// Returns a player who has fallen through the world outside back onto it. The terrain is a height field with nothing
    /// beneath it, so a capsule wholly below the surface (dropped there by a placement or a collision gap) would otherwise
    /// fall without end. The player is stood on the surface above where they fell, with their fall forgotten as the
    /// donor's <c>RepositionPlayer</c> clears falling damage. True when the player was moved.
    /// </summary>
    internal bool RecoverPlayerBelowTerrain()
    {
        PlayerControlState player = _player;
        if (player.Position is not WorldPoint position || player.Motion.Grounded
            || !TryExteriorTerrainHeight(position, out float terrain)) return false;
        if (position.Y + (_spatial.CurrentController.Shape.StandingHeight * .5F) >= terrain) return false;
        WorldPoint standing = _spatial.StandingPosition(position with { Y = terrain }, CharacterEnvironment(player.Motion));
        _host.RelocatePlayer(standing, player.YawRadians, player.PitchRadians);
        return true;
    }

    /// <summary>
    /// The height of the admitted terrain surface under a local position in the active exterior, sampled as the donor's
    /// <c>Terrain.SampleHeight</c> does; false outside an exterior or the admitted terrain window.
    /// </summary>
    internal bool TryExteriorTerrainHeight(WorldPoint local, out float height)
    {
        height = 0F;
        if (ActiveProfile.Kind != DaggerfallWorldProfileKind.Exterior
            || _exteriorResidency is not { IsInitialized: true } residency
            || _exteriorGrounds is not { } grounds) return false;
        DaggerfallExteriorWorldOrigin origin = ActiveOrigin(residency);
        DaggerfallExteriorWorldBounds bounds = new(_definitions.Terrain.Width, _definitions.Terrain.Height);
        DaggerfallExteriorCellId cell = DaggerfallExteriorSessionOrigin.CellForLocalPosition(local, origin, bounds);
        if (!residency.ResidentCells.Contains(cell)) return false;
        Vector3 corner = origin.LocalTranslation(cell);
        float x = Math.Clamp((local.X - corner.X) / DaggerfallExteriorCellResidency.CellSize, 0F, 1F);
        float z = Math.Clamp((corner.Z - local.Z) / DaggerfallExteriorCellResidency.CellSize, 0F, 1F);
        height = corner.Y + DaggerfallTerrainSurfaceBuilder.SampleWorldHeight(grounds.Ground(cell).Surface, x, z);
        return true;
    }

    /// <summary>Whether the current spatial session has an admitted exterior window.</summary>
    internal bool ExteriorResidencyInitialized => _exteriorResidency?.IsInitialized == true;

    /// <summary>The frame the exterior stands in while the player is outdoors in it, read back from the Engine origin.</summary>
    internal DaggerfallExteriorWorldOrigin? ActiveExteriorOrigin =>
        ActiveProfile.Kind == DaggerfallWorldProfileKind.Exterior && _exteriorResidency is { IsInitialized: true } residency
            ? ActiveOrigin(residency) : null;

    /// <summary>
    /// Admits a constructed session's exterior: its terrain window and the town's location artifact. A saved unloaded
    /// location restores detached state without recreating native geometry until the player returns to the authored map
    /// pixel.
    /// </summary>
    internal void AdmitInitialResidency(DaggerfallExteriorCellResidencySave? savedExterior,
        DaggerfallExteriorLocationResidencySave? savedLocation = null)
    {
        RequireExteriorProfile();
        _locationLoaded = savedLocation?.Loaded != false;
        _locationCell = savedLocation?.Cell;
        _admittingInitialResidency = true;
        try
        {
            if (savedExterior is { } exterior)
                RestoreExteriorResidency(exterior);
            else
            {
                // The start is a profile position; the window is centred where it stands once in the location's frame,
                // which lies across the map pixel from the profile's origin.
                DaggerfallExteriorWorldOrigin origin = DaggerfallExteriorWorldOrigin.At(ActiveExteriorCell());
                _ = EnsureExteriorResidency();
                DaggerfallExteriorCellId center = _player.Position is WorldPoint start
                    ? DaggerfallExteriorSessionOrigin.CellForLocalPosition(
                        DaggerfallSceneOrigin.Shift(start, ExteriorProfileTranslation(Projection.Inputs, origin)),
                        origin, new DaggerfallExteriorWorldBounds(_definitions.Terrain.Width, _definitions.Terrain.Height))
                    : ActiveExteriorCell();
                AdmitExteriorWindow(center, origin);
            }
        }
        finally { _admittingInitialResidency = false; }
        _locationCell ??= ActiveExteriorCell();
        DaggerfallExteriorWorldOrigin activeOrigin = _exteriorResidency is { IsInitialized: true } residency
            ? ActiveOrigin(residency)
            : throw new InvalidOperationException("Initial exterior admission did not establish a world origin.");
        bool restoringSavedFrame = savedExterior is not null;
        Vector3 locationFrameOffset = ExteriorProfileTranslation(Projection.Inputs, activeOrigin,
            includeOriginCompensation: !restoringSavedFrame);
        Projection.Rebase(locationFrameOffset);
        if (restoringSavedFrame)
        {
            HashSet<long> staticActorIds = _npcs.All
                .Where(npc => npc.Profile == Projection.Inputs.ProfileKey && npc.Kind == DaggerfallNpcKind.Static)
                .Select(npc => npc.DurableId).ToHashSet();
            RebaseActors(staticActorIds, locationFrameOffset);
        }
        else
        {
            ShiftPlayer(locationFrameOffset);
            RebaseActors(ProfileActorIds(Projection.Inputs), locationFrameOffset);
        }
        if (savedLocation?.Loaded == false)
        {
            _locationLoaded = false;
            // Restored wilderness actors are live independently of the detached location.
            Projection.Suspend(_roster.Dynamic.Keys.ToHashSet(), keepSky: true);
            RemoveLocationArtifact(Projection.Inputs);
            _host.RebuildActivation();
        }
        else
        {
            AdmitLocationArtifact(Projection.Inputs, activeOrigin);
            _locationLoaded = true;
            UpdateExteriorLocation(CurrentExteriorCell());
        }
        if (savedLocation?.Loaded == false && _exteriorResidency is { IsInitialized: true } residentWindow)
            ReconcileResidentExteriorLocations(residentWindow);
    }


    /// <summary>
    /// Resolves the active site's normalized map-pixel identity. An interior or dungeon profile does
    /// not own a wilderness cell window, even though it retains the same geographic site context.
    /// </summary>
    internal DaggerfallExteriorCellId ActiveExteriorCell()
    {
        RequireExteriorProfile();
        DaggerfallSiteRecord site = _site.ActiveSite
            ?? throw new InvalidOperationException("An exterior profile requires an active Daggerfall site.");
        return new(site.MapPixelX, site.MapPixelY);
    }

    /// <summary>
    /// Resolves the current streaming center from the authoritative player local pose. Before the
    /// first admission the active site's map pixel supplies the local origin, so an authored landing
    /// pose at the site still starts in the site's cell; after admission, crossing a terrain tile
    /// boundary advances the center without creating a second world-position authority.
    /// </summary>
    internal WorldPoint ExteriorSitePosition(WorldPoint position)
    {
        DaggerfallExteriorCellId site = ActiveExteriorCell();
        DaggerfallExteriorWorldOrigin origin = _exteriorResidency is { IsInitialized: true } residency
            ? ActiveOrigin(residency) : DaggerfallExteriorWorldOrigin.At(site);
        Vector3 translation = origin.LocalTranslation(site) + ExteriorLocationFrame(Projection.Inputs);
        return new(position.X - translation.X, position.Y - translation.Y, position.Z - translation.Z);
    }

    /// <summary>
    /// The exterior location whose classic location rect (footprint plus one RMB block) holds the
    /// accepted player position, or null in the wilderness. This is the donor's
    /// <c>PlayerGPS.IsPlayerInLocationRect</c> over the actually admitted location profiles.
    /// </summary>
    internal DaggerfallSiteRecord? LocationAtPlayer()
    {
        if (ActiveProfile.Kind != DaggerfallWorldProfileKind.Exterior
            || _player.Position is not WorldPoint position) return null;
        // Location artifacts use their normalized RMB frame, not the source terrain-tile origin.
        // Check the actual admitted profiles, including neighbors in the shared exterior window.
        var profiles = (_locationLoaded ? new[] { Projection.Inputs } : [])
            .Concat(_residentExteriorLocations.Values.Select(value => value.Profile));
        foreach (var profile in profiles)
        {
            if (profile.Site is not { } id || !_site.TryFind(id, out var location) || location.Exterior is not { } footprint) continue;
            var translation = ExteriorProfileTranslation(profile);
            if (InsideLocationFootprint(footprint, position.X - translation.X, position.Z - translation.Z))
                return location;
        }
        return null;
    }

    internal static bool InsideLocationFootprint(DaggerfallSiteExterior location, float x, float z)
    {
        // PlayerGPS adds one RMB block of classic town-boundary clearance. Published geometry
        // starts at the zero-based RMB origin and reflects source +Z into Engine -Z.
        const float block = 16 * DaggerfallTerrainSurfaceBuilder.SampleSpacing;
        return x >= -block && x <= (location.Width + 1) * block
            && z >= -(location.Height + 1) * block && z <= block;
    }

    internal DaggerfallExteriorCellId CurrentExteriorCell()
    {
        DaggerfallExteriorCellId site = ActiveExteriorCell();
        DaggerfallExteriorWorldOrigin origin = _exteriorResidency is { IsInitialized: true } residency
            ? ActiveOrigin(residency)
            : DaggerfallExteriorWorldOrigin.At(site);
        return _player.Position is WorldPoint position
            ? DaggerfallExteriorSessionOrigin.CellForLocalPosition(
                position,
                origin,
                new DaggerfallExteriorWorldBounds(_definitions.Terrain.Width, _definitions.Terrain.Height))
            : site;
    }

    /// <summary>
    /// Stops a player who has crossed the world edge at it, as the donor's <c>PlayerGPS</c> does, keeping their motion
    /// so that a held key still walks them along it. Runs before anything reads the player's cell or moves the origin.
    /// </summary>
    internal void HoldPlayerInsideWorldEdge()
    {
        if (_player.Position is not WorldPoint position
            || _exteriorResidency is not { IsInitialized: true } residency) return;
        WorldPoint held = DaggerfallExteriorSessionOrigin.InsideWorldEdge(position, ActiveOrigin(residency),
            new DaggerfallExteriorWorldBounds(_definitions.Terrain.Width, _definitions.Terrain.Height));
        if (held != position) ShiftPlayer(held.ToVector() - position.ToVector());
    }

    /// <summary>
    /// Admits or advances the seven-by-seven window at the active site's map pixel, retaining the
    /// coordinator's current local-origin compensation after ordinary movement.
    /// </summary>
    internal DaggerfallExteriorCellResidencyUpdate UpdateExteriorResidency()
    {
        DaggerfallExteriorCellId center = CurrentExteriorCell();
        DaggerfallExteriorCellResidency residency = EnsureExteriorResidency();
        // The first window is admitted in the frame the center was just resolved in: the active site's
        // map pixel. Anchoring it at the center instead would place every cell one tile away from the
        // player whenever the landing pose lies outside the site's own cell, and the next step would
        // then read the unmoved player as standing in yet another cell.
        DaggerfallExteriorWorldOrigin origin = residency.IsInitialized
            ? ActiveOrigin(residency)
            : DaggerfallExteriorWorldOrigin.At(ActiveExteriorCell());
        return AdmitExteriorWindow(center, origin);
    }

    /// <summary>
    /// Moves the window with a player going about the exterior. The locations it brings in or lets go are handed to the
    /// Engine as one deferred batch, as a staged change's are: composing a location's collision and navigation would
    /// otherwise take the update the window moved in. A location enters the window cells away from the player, long
    /// before anyone can reach it, and whoever it places waits for its floor (<see cref="SettleRoadsideCollision"/>).
    /// Its meshes and textures are prepared on the renderer's workers too, and it is drawn once they are in.
    /// A recall or an arrival, which can land beside a location, moves the window with <see cref="UpdateExteriorResidency()"/>.
    /// </summary>
    internal void FollowPlayerOnTheRoad()
    {
        if (_deferringCollision)
        {
            _ = UpdateExteriorResidency();
            return;
        }
        _deferringCollision = true;
        _onTheRoad = true;
        try
        {
            _ = UpdateExteriorResidency();
        }
        finally
        {
            _onTheRoad = false;
            _deferringCollision = false;
            ReleaseHeldPlacements();
        }
    }

    /// <summary>Admits the active exterior window with an explicit saved or rebased local origin.</summary>
    internal DaggerfallExteriorCellResidencyUpdate UpdateExteriorResidency(
        DaggerfallExteriorWorldOrigin origin)
    {
        RequireExteriorProfile();
        DaggerfallExteriorCellId center = _player.Position is WorldPoint position
            ? DaggerfallExteriorSessionOrigin.CellForLocalPosition(
                position,
                origin,
                new DaggerfallExteriorWorldBounds(_definitions.Terrain.Width, _definitions.Terrain.Height))
            : ActiveExteriorCell();
        return AdmitExteriorWindow(center, origin);
    }

    /// <summary>Admits or advances the window centred on <paramref name="center"/> in <paramref name="origin"/>'s frame.</summary>
    private DaggerfallExteriorCellResidencyUpdate AdmitExteriorWindow(DaggerfallExteriorCellId center, DaggerfallExteriorWorldOrigin origin)
    {
        DaggerfallExteriorCellResidency residency = EnsureExteriorResidency();
        DaggerfallExteriorCellResidencyUpdate update = residency.Update(center, origin);
        // A first window has no ground under it yet, which is sampled and installed over the updates to come.
        if (_groundCollider is null) _groundAwaited = true;
        _groundContainers.ReconcileExteriorResidency(DaggerfallExteriorSessionOrigin.ResidentAt(residency.ResidentCells, ActiveOrigin(residency)));
        ReconcileExteriorTerrainAppearance(residency);
        UpdateExteriorLocation(center);
        return update;
    }

    private void UpdateExteriorLocation(DaggerfallExteriorCellId center)
    {
        if (ActiveProfile.Kind != DaggerfallWorldProfileKind.Exterior) return;
        if (_admittingInitialResidency) return;
        DaggerfallExteriorCellId siteCell = ActiveExteriorCell();
        DaggerfallExteriorCellResidency residency = EnsureExteriorResidency();
        bool locationWindowContainsSite = residency.ResidentCells.Contains(siteCell);
        if (locationWindowContainsSite)
        {
            if (!_locationLoaded) LoadExteriorLocation(center);
        }
        else if (_locationLoaded)
        {
            UnloadExteriorLocation(center);
        }
        ReconcileResidentExteriorLocations(residency);
        _locationCell = center;
    }

    /// <summary>
    /// Follows the locations the window handed over on the road. Once they are live, whoever they placed is settled on
    /// them; a refused batch holds the world, as a refused arrival does, until it is handed over again and live.
    /// </summary>
    private void SettleRoadsideCollision()
    {
        if (_deferredCollision is not { Awaiting: true } deferred) return;
        switch (deferred.Poll().State)
        {
            case DaggerfallDeferredCollisionState.Live when !CollisionPending:
                _host.GroundArrivedActors();
                break;
            case DaggerfallDeferredCollisionState.Refused:
                _awaitingGround = true;
                break;
        }
    }

    /// <summary>
    /// Keeps every normalized exterior profile in the admitted terrain window alive. The selected
    /// profile remains the one interaction projection; neighboring profiles own their own doors,
    /// motion, actors, appearance and artifact identity until the cell leaves the window.
    /// </summary>
    private void ReconcileResidentExteriorLocations(DaggerfallExteriorCellResidency residency)
    {
        if (Profiles is null) return;
        HashSet<DaggerfallWorldProfileKey> desired = [];
        // Every catalog location in the window streams its exterior: an authored override, else assembled from
        // its blocks, each resolved when its cell first enters the window.
        Dictionary<DaggerfallExteriorCellId, DaggerfallSiteId> sites = ExteriorSitesByCell();
        foreach (DaggerfallExteriorCellId cell in residency.ResidentCells)
        {
            if (!sites.TryGetValue(cell, out DaggerfallSiteId site)) continue;
            DaggerfallWorldProfileKey key = DaggerfallWorldProfileIds.Exterior(site);
            if (key != ActiveProfile && Profiles.Contains(key)) desired.Add(key);
        }

        foreach (DaggerfallWorldProfileKey key in _residentExteriorLocations.Keys
            .Where(key => !desired.Contains(key)).OrderBy(key => key.Site.Region).ThenBy(key => key.Site.Index)
            .ThenBy(key => key.LogicalId, StringComparer.Ordinal).ToArray())
            RetireResidentExteriorLocation(key, capture: true);

        foreach (DaggerfallWorldProfileKey key in desired.OrderBy(key => key.Site.Region).ThenBy(key => key.Site.Index)
            .ThenBy(key => key.LogicalId, StringComparer.Ordinal))
            AdmitResidentExteriorLocation(Profiles.Require(key));
    }

    internal bool TryExteriorProfileCell(DaggerfallSiteProfile profile, out DaggerfallExteriorCellId cell)
    {
        cell = default;
        if (profile.ProfileKind != DaggerfallWorldProfileKind.Exterior || profile.Site is not { } siteId)
            return false;
        return ExteriorCellsBySite().TryGetValue(siteId, out cell);
    }

    internal Vector3 ExteriorProfileTranslation(DaggerfallSiteProfile profile,
        DaggerfallExteriorWorldOrigin? origin = null, bool includeOriginCompensation = true)
    {
        if (!TryExteriorProfileCell(profile, out DaggerfallExteriorCellId cell))
            throw new InvalidOperationException($"Exterior profile '{profile.ProfileKey.LogicalId}' has no normalized map-pixel identity.");
        DaggerfallExteriorWorldOrigin resolvedOrigin = origin
            ?? (_exteriorResidency is { IsInitialized: true } residency
                ? ActiveOrigin(residency)
                : throw new InvalidOperationException("An exterior profile requires initialized terrain residency."));
        Vector3 translation = resolvedOrigin.LocalTranslation(cell);
        if (!includeOriginCompensation) translation -= resolvedOrigin.Compensation;
        return translation + ExteriorLocationFrame(profile);
    }

    /// <summary>Returns the current local frame for a resident or active exterior profile.</summary>
    internal Vector3 ExteriorProfileFrameTranslation(DaggerfallWorldProfileKey profile)
    {
        DaggerfallSiteProfile inputs = profile == Projection.Inputs.ProfileKey
            ? Projection.Inputs
            : RequireProfiles().Require(profile);
        if (inputs.ProfileKind != DaggerfallWorldProfileKind.Exterior)
            throw new InvalidOperationException($"Profile '{profile.LogicalId}' is not an exterior location.");
        return ExteriorProfileTranslation(inputs);
    }

    /// <summary>
    /// Where a location's profile frame sits in its map pixel's terrain cell: over the terrain tiles the donor lays its
    /// blocks on (<c>TerrainHelper.GetLocationTerrainTileOrigin</c>, the tiles the terrain is flattened and cleared of
    /// nature under) and at the height the donor samples for the location's parent. The profile runs its blocks from its
    /// origin along +X and -Z, at the donor's terrain-tile origin.
    /// </summary>
    private Vector3 ExteriorLocationFrame(DaggerfallSiteProfile profile)
    {
        (long columns, long rows) = ExteriorLocationFrameCells(profile);
        return new Vector3(columns * NavigationCellSize, ExteriorLocationSampleHeight(profile), rows * NavigationCellSize);
    }

    /// <summary>The location frame's horizontal offset in its cell, in whole cells of the shared Engine artifact grid.</summary>
    private (long Columns, long Rows) ExteriorLocationFrameCells(DaggerfallSiteProfile profile)
    {
        if (!TryExteriorProfileCell(profile, out DaggerfallExteriorCellId cell))
            throw new InvalidOperationException($"Exterior profile '{profile.ProfileKey.LogicalId}' has no normalized map-pixel identity.");
        (int tileX, int tileY) = DaggerfallLocationTerrainFrame.TileOffset(ExteriorLocations()[cell]);
        return (checked(tileX * DaggerfallLocationTerrainFrame.NavigationCellsPerTile), checked(-tileY * DaggerfallLocationTerrainFrame.NavigationCellsPerTile));
    }

    /// <summary>
    /// The height a location's frame stands at. It never changes, and every update asks it through
    /// <see cref="LocalToProfile"/>, so it outlives the surface it came from: past the window that cell's surface is gone
    /// and would be built whole for each asking.
    /// </summary>
    private float ExteriorLocationSampleHeight(DaggerfallSiteProfile profile)
    {
        if (!TryExteriorProfileCell(profile, out DaggerfallExteriorCellId cell))
            throw new InvalidOperationException($"Exterior profile '{profile.ProfileKey.LogicalId}' has no normalized map-pixel identity.");
        if (_exteriorLocationHeights.TryGetValue(cell, out float height)) return height;
        height = DaggerfallTerrainSurfaceBuilder.SampleWorldHeight(ExteriorSurface(profile),
            DaggerfallTerrainSurfaceBuilder.LocationSampleCoordinate,
            DaggerfallTerrainSurfaceBuilder.LocationSampleCoordinate);
        _exteriorLocationHeights.Add(cell, height);
        return height;
    }

    /// <summary>Whether a cell's ground is built or building. The window keeps its own cells' and those bordering it.</summary>
    internal bool HoldsExteriorSurface(DaggerfallExteriorCellId cell) => _exteriorGrounds?.Holds(cell) == true;

    /// <summary>The cells the walked window holds.</summary>
    internal IReadOnlyCollection<DaggerfallExteriorCellId> ResidentExteriorCells => _exteriorResidency?.ResidentCells ?? [];

    private DaggerfallTerrainSurface ExteriorSurface(DaggerfallSiteProfile profile)
    {
        if (!TryExteriorProfileCell(profile, out DaggerfallExteriorCellId cell))
            throw new InvalidOperationException($"Exterior profile '{profile.ProfileKey.LogicalId}' has no normalized map-pixel identity.");
        _ = EnsureExteriorResidency();
        return _exteriorGrounds!.Ground(cell).Surface;
    }

    internal HashSet<long> ProfileActorIds(DaggerfallSiteProfile profile, DaggerfallSiteRuntimeDelta? delta = null)
    {
        HashSet<long> actorIds = profile.Project.Actors.Keys.ToHashSet();
        foreach (DaggerfallNpc npc in _npcs.All.Where(npc => npc.Profile == profile.ProfileKey))
            actorIds.Add(npc.DurableId);
        if (delta is not null)
            foreach (DaggerfallDynamicActorSave actor in delta.DynamicActors)
                actorIds.Add(actor.EntityId);
        return actorIds;
    }

    internal IReadOnlySet<long> ActiveDynamicActorIds()
    {
        HashSet<long> residentActorIds = [];
        foreach (ResidentExteriorLocation resident in _residentExteriorLocations.Values)
            residentActorIds.UnionWith(resident.ActorIds);
        return _roster.DynamicActorIdsExcluding(residentActorIds);
    }

    private void AdmitResidentExteriorLocation(DaggerfallSiteProfile profile)
    {
        DaggerfallWorldProfileKey key = profile.ProfileKey;
        if (key == ActiveProfile || _residentExteriorLocations.ContainsKey(key)) return;
        _deltas.TryGetValue(key, out DaggerfallSiteRuntimeDelta? delta);
        DaggerfallSiteProjection? projection = null;
        bool artifactAdmitted = false;
        try
        {
            DaggerfallExteriorWorldOrigin origin = _exteriorResidency is { IsInitialized: true } residency
                ? ActiveOrigin(residency)
                : throw new InvalidOperationException("An adjacent exterior location requires initialized terrain residency.");
            if (!TryExteriorProfileCell(profile, out DaggerfallExteriorCellId cell))
                throw new InvalidOperationException($"Exterior profile '{key.LogicalId}' has no normalized map-pixel identity.");
            Vector3 translation = ExteriorProfileTranslation(profile, origin);
            projection = DaggerfallSiteProjection.Create(_engine, _actors.Entities, _random, _tuning, _time.Calendar,
                profile, AudioFor(profile), _spatial, _host.ConfigureSiteAppearance, delta?.Doors, delta?.Motion, deferMotionCollisionAdmission: true,
                sessionPresentation: _sessionPresentation, definitions: _definitions, carriesSky: false, prepareLocationOffUpdate: _onTheRoad);
            projection.Appearance.DrawThroughActiveSnapshot();
            // The profile's authored geometry and actors use its own source frame. Rebase every
            // projection owner before admitting collision so doors, motion, portals, appearance
            // and lighting all share the same active exterior origin as the terrain cell.
            projection.Rebase(translation);
            AdmitLocationArtifact(profile, origin);
            artifactAdmitted = true;
            projection.ActivateMotionCollisionResidency();
            _roster.MaterializeSite(profile, delta?.Actors, delta?.DynamicActors, delta?.BanishedActors, restoreAuthoredAppearance: true,
                siteProfile: projection.Inputs, siteAppearance: projection.Appearance);
            _host.AdmitResidentCivilianAppearances(projection);
            _roster.MaterializeStaticNpcs(profile, projection.Appearance);
            HashSet<long> actorIds = profile.Project.Actors.Keys.ToHashSet();
            foreach (DaggerfallNpc npc in _npcs.All.Where(npc => npc.Profile == key))
                actorIds.Add(npc.DurableId);
            if (delta is not null) foreach (DaggerfallDynamicActorSave actor in delta.DynamicActors) actorIds.Add(actor.EntityId);
            if (delta is not null)
            {
                _siteDeltas.Restore(delta);
                projection.Appearance.SyncRestoredDefeat(_actors);
                _deltas.Remove(key);
            }
            RebaseActors(actorIds, translation);
            _residentExteriorLocations.Add(key, new ResidentExteriorLocation(profile, projection, actorIds, FreeResidentLocationSlot()));
        }
        catch
        {
            if (projection is not null)
            {
                try { _roster.UnloadSite(profile, delta?.DynamicActors, projection.Appearance); }
                catch { /* preserve the admission failure */ }
                try { projection.Dispose(); }
                catch { /* preserve the admission failure */ }
            }
            if (artifactAdmitted)
            {
                try { RemoveLocationArtifact(profile); }
                catch { /* preserve the admission failure */ }
            }
            throw;
        }
    }

    internal void RebaseActors(IReadOnlySet<long> actorIds, Vector3 translation)
    {
        foreach (long actorId in actorIds)
        {
            if (!_actors.TryGet(actorId, out ActorState? actor)) continue;
            ActorPose pose = actor.Pose;
            actor.ApplyPose(new ActorPose(DaggerfallSceneOrigin.Shift(pose.Position, translation), pose.HeadingYawRadians));
        }
    }

    internal void RetireResidentExteriorLocation(DaggerfallWorldProfileKey key, bool capture)
    {
        if (!_residentExteriorLocations.Remove(key, out ResidentExteriorLocation? resident)) return;
        DaggerfallSiteRuntimeDelta? delta = null;
        try
        {
            _retiredResidentLocations.Add(resident.Projection.DetachAppearance());
            if (capture)
            {
                Vector3 profileFrameOffset = ExteriorProfileTranslation(resident.Profile);
                delta = _siteDeltas.Capture(resident.Profile, resident.Projection,
                    _roster.Dynamic, resident.ActorIds, profileFrameOffset);
                _deltas[key] = delta;
            }
            resident.Projection.Suspend();
            _roster.UnloadSite(resident.Profile, delta?.DynamicActors, resident.Projection.Appearance, resident.ActorIds);
            RemoveLocationArtifact(resident.Profile);
        }
        finally
        {
            resident.Projection.Dispose();
        }
    }

    /// <summary>The lowest presentation identity slot no resident neighbour holds; the active location holds slot 0.</summary>
    private int FreeResidentLocationSlot()
    {
        HashSet<int> held = [.. _residentExteriorLocations.Values.Select(resident => resident.Slot)];
        for (int slot = DaggerfallPresentationObjectIds.ActiveLocationSlot + 1; slot < DaggerfallPresentationObjectIds.LocationSlots; slot++)
            if (!held.Contains(slot)) return slot;
        throw new InvalidOperationException("Every presentation location slot is held by a resident neighbour.");
    }

    /// <summary>The appearance of every neighbouring town resident in the window.</summary>
    internal IEnumerable<DaggerfallSiteAppearance> ResidentAppearances =>
        _residentExteriorLocations.Values.Select(resident => resident.Projection.Appearance);

    /// <summary>
    /// Completes the active appearance's snapshot with the exterior window: every resident neighbour's meshes,
    /// doors, gates, action models and actors under its own location slot, then the terrain and its nature batches.
    /// </summary>
    private void AppendExteriorFacts(List<AppearanceFact> facts)
    {
        foreach (ResidentExteriorLocation resident in _residentExteriorLocations.Values.OrderBy(value => value.Slot))
        {
            resident.Projection.Appearance.AppendLocationFacts(facts, resident.Slot);
            resident.Projection.Appearance.AppendActorFacts(facts, _actors, resident.Slot,
                _effects.PerceptionFor, _npcs.IsGameplayActive);
        }
        _exteriorTerrainAppearance?.AppendFacts(facts);
        _seaFacts?.Invoke(facts);
    }

    /// <summary>A snapshot was accepted: what the window and the retired neighbours no longer draw is released.</summary>
    private void CompleteExteriorSnapshot()
    {
        List<Exception>? failures = null;
        try { _exteriorTerrainAppearance?.CompleteAcceptedSnapshot(); }
        catch (Exception exception) { (failures ??= []).Add(exception); }
        try { _seaAccepted?.Invoke(); }
        catch (Exception exception) { (failures ??= []).Add(exception); }
        ReleaseRetiredResidentLocations(ref failures);
        if (failures is { Count: > 0 }) throw new AggregateException(failures);
    }

    private void ReleaseRetiredResidentLocations(ref List<Exception>? failures)
    {
        foreach (IDisposable retired in _retiredResidentLocations)
        {
            try { retired.Dispose(); }
            catch (Exception exception) { (failures ??= []).Add(exception); }
        }
        _retiredResidentLocations.Clear();
    }

    /// <summary>Opens or closes the city gates of the active location and every resident neighbour for the time of day.</summary>
    internal void SyncCityGates()
    {
        Projection.SyncCityGates(_time.Calendar);
        foreach (ResidentExteriorLocation resident in _residentExteriorLocations.Values) resident.Projection.SyncCityGates(_time.Calendar);
    }

    /// <summary>Retires neighboring exterior closures before a profile transition or session dispose.</summary>
    internal void RetireResidentExteriorLocations(bool capture = true)
    {
        foreach (DaggerfallWorldProfileKey key in _residentExteriorLocations.Keys
            .OrderBy(key => key.Site.Region).ThenBy(key => key.Site.Index).ThenBy(key => key.LogicalId, StringComparer.Ordinal).ToArray())
            RetireResidentExteriorLocation(key, capture);
    }

    /// <summary>
    /// Save boundaries temporarily detach neighboring closures so the existing current-state save
    /// schema can keep one active profile and all other profile-owned actors in Deltas. The returned
    /// scope restores the same resident set after serialization.
    /// </summary>
    internal IDisposable SuspendResidentExteriorLocationsForSave()
    {
        if (_residentExteriorLocations.Count == 0) return NoopScope.Instance;
        RetireResidentExteriorLocations(capture: true);
        return new DelegateScope(() =>
        {
            if (_exteriorResidency is { IsInitialized: true } residency)
                ReconcileResidentExteriorLocations(residency);
        });
    }

    private sealed class NoopScope : IDisposable
    {
        internal static readonly NoopScope Instance = new();
        public void Dispose() { }
    }

    private sealed class DelegateScope(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;
        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }

    private void UnloadExteriorLocation(DaggerfallExteriorCellId center)
    {
        if (!_locationLoaded) return;
        DaggerfallSiteProfile profile = Projection.Inputs;
        IReadOnlySet<long> dynamicActorIds = ActiveDynamicActorIds();
        DaggerfallSiteRuntimeDelta delta = _siteDeltas.Capture(profile, Projection,
            _roster.Dynamic, dynamicActorIds, ExteriorProfileTranslation(profile));
        _deltas[ActiveProfile] = delta;
        _roster.UnloadSite(profile, delta.DynamicActors);
        Projection.Suspend(keepSky: true);
        RemoveLocationArtifact(profile);
        _locationLoaded = false;
        _locationCell = center;
        _host.RebuildActivation();
    }

    private void LoadExteriorLocation(DaggerfallExteriorCellId center)
    {
        if (_locationLoaded) return;
        DaggerfallSiteProfile profile = Projection.Inputs;
        AdmitLocationArtifact(profile);
        Projection.Resume(offUpdate: _onTheRoad);
        _deltas.TryGetValue(ActiveProfile, out DaggerfallSiteRuntimeDelta? delta);
        _roster.MaterializeSite(profile, delta?.Actors, delta?.DynamicActors, delta?.BanishedActors, restoreAuthoredAppearance: true);
        RebaseActors(ProfileActorIds(profile, delta), ExteriorProfileTranslation(profile));
        if (delta is not null)
        {
            _siteDeltas.Restore(delta);
            Projection.Appearance.SyncRestoredDefeat(_actors);
            _deltas.Remove(ActiveProfile);
        }
        _locationLoaded = true;
        _locationCell = center;
        _host.RebuildActivation();
    }

    /// <summary>Captures only durable exterior identity/origin facts; Engine handles stay native.</summary>
    internal DaggerfallExteriorCellResidencySave? CaptureExteriorResidency() =>
        _exteriorResidency is { IsInitialized: true }
            ? _exteriorResidency.Capture(ReadEngineOrigin())
            : null;

    internal DaggerfallExteriorLocationResidencySave? CaptureExteriorLocationResidency()
    {
        if (ActiveProfile.Kind != DaggerfallWorldProfileKind.Exterior) return null;
        return new(
            DaggerfallWorldProfileKeySave.Capture(ActiveProfile),
            _locationCell ?? CurrentExteriorCell(),
            _locationLoaded);
    }

    /// <summary>Restores the saved exterior window after the owning SpatialSession is admitted.</summary>
    internal DaggerfallExteriorCellResidencyUpdate RestoreExteriorResidency(
        DaggerfallExteriorCellResidencySave save)
    {
        RequireExteriorProfile();
        using (WorldOriginPrepared prepared = _engine.WorldOrigin.Prepare(new(_spatial.Session,
            save.EngineOriginCellX, save.EngineOriginCellY, save.EngineOriginCellZ,
            ReadOnlyMemory<WorldOriginEntityRow>.Empty)))
        {
            WorldOriginCommitReceipt receipt = _engine.WorldOrigin.Commit(new(prepared));
            // Save restoration already supplied player, actor and ground positions in this local
            // frame. Only fresh authored owners and the native artifact need the translation.
            Vector3 delta = receipt.LocalDelta;
            Projection.Rebase(delta);
            _active.RebaseActionTriggers(delta);
            _active.RebaseRestoredPlayerTriggers();
        }
        DaggerfallExteriorCellResidency residency = EnsureExteriorResidency();
        DaggerfallExteriorCellResidencyUpdate update = residency.Restore(save);
        if (_groundCollider is null) _groundAwaited = true;
        _groundContainers.ReconcileExteriorResidency(DaggerfallExteriorSessionOrigin.ResidentAt(residency.ResidentCells, ActiveOrigin(residency)));
        ReconcileExteriorTerrainAppearance(residency);
        return update;
    }

    /// <summary>
    /// The active exterior frame's compensation, derived from the Engine WorldOrigin readout. The
    /// product keeps no compensation of its own beside the Engine origin cell.
    /// </summary>
    internal Vector3 LocalCompensation => _exteriorResidency is { IsInitialized: true }
        ? EngineCompensation() : Vector3.Zero;

    private WorldOriginReadout ReadEngineOrigin() => _engine.WorldOrigin.Read(new(_spatial.Session));

    private Vector3 EngineCompensation()
    {
        WorldOriginReadout origin = ReadEngineOrigin();
        return DaggerfallExteriorWorldOrigin.CompensationFor(origin.CellX, origin.CellY, origin.CellZ);
    }

    /// <summary>The residency's anchor map pixel in the Engine's current local frame.</summary>
    private DaggerfallExteriorWorldOrigin ActiveOrigin(DaggerfallExteriorCellResidency residency) =>
        residency.Origin with { Compensation = EngineCompensation() };

    private Vector3 ActiveExteriorFrameOffset() =>
        ActiveProfile.Kind == DaggerfallWorldProfileKind.Exterior
        && _exteriorResidency is { IsInitialized: true }
            ? ExteriorLocationFrame(Projection.Inputs)
            : Vector3.Zero;

    internal WorldPoint ProfileToLocal(WorldPoint position) =>
        DaggerfallSceneOrigin.Shift(position, LocalCompensation + ActiveExteriorFrameOffset());

    internal Vector3 LocalToProfile(Vector3 position) =>
        position - LocalCompensation - ActiveExteriorFrameOffset();

    /// <summary>Moves the exterior origin under the player once it strays a cell away; true when it moved.</summary>
    internal bool RebaseExteriorIfNeeded()
    {
        if (ActiveProfile.Kind != DaggerfallWorldProfileKind.Exterior
            || _player.Position is not WorldPoint position) return false;
        float cellSize = DaggerfallExteriorCellResidency.CellSize;
        bool horizontal = MathF.Abs(position.X) >= cellSize || MathF.Abs(position.Z) >= cellSize;
        bool vertical = MathF.Abs(position.Y) > _tuning.WorldOrigin.VerticalRebaseDistance;
        if (!horizontal && !vertical) return false;
        WorldOriginReadout origin = ReadEngineOrigin();
        long x = horizontal ? checked(origin.CellX + (long)Math.Floor(position.X)) : origin.CellX;
        long y = vertical ? checked(origin.CellY + (long)Math.Floor(position.Y)) : origin.CellY;
        long z = horizontal ? checked(origin.CellZ + (long)Math.Floor(position.Z)) : origin.CellZ;
        CommitExteriorOrigin(origin, x, y, z);
        return true;
    }

    /// <summary>
    /// Returns the Engine origin to cell zero so detached site state is captured in profile frames.
    /// After a long walk from the active site, a root's local translation in that frame can leave the
    /// Engine's local-coordinate envelope, so the reset asks <see cref="EntityOriginRebaser"/> to
    /// exclude such roots rather than refuse the commit. Every other root is rebased with its motion
    /// in the Engine's one batch; the excluded ones are about to be captured and unloaded with the
    /// site, so only their Transforms are carried into the new frame, by the committed delta, for that
    /// capture to read.
    /// </summary>
    internal void NormalizeExteriorOrigin()
    {
        WorldOriginReadout origin = ReadEngineOrigin();
        if (origin.CellX == 0 && origin.CellY == 0 && origin.CellZ == 0) return;
        CommitExteriorOrigin(origin, 0, 0, 0, excludeOutsideEnvelope: true);
    }

    /// <summary>
    /// Moves the Engine origin and rebases every actor-family root (canonical actors and projected
    /// quest people). An in-play rebase goes through the Engine's <see cref="EntityOriginRebaser"/>,
    /// which publishes the rebased Transforms and every stored character motion in one batch. Each
    /// root's global position is a call-time projection of its Transform in the current origin cell,
    /// attached only for the prepare and removed afterwards, so no second pose survives the commit.
    /// The player is shifted beside it: its pose is player-control state rather than an entity
    /// Transform the rebaser could see, and its motion anchors move through CharacterMotion.Rebased.
    /// </summary>
    private void CommitExteriorOrigin(WorldOriginReadout current, long x, long y, long z, bool excludeOutsideEnvelope = false)
    {
        RequireExteriorProfile();
        if (_player.Position is not WorldPoint position)
            throw new InvalidOperationException("An origin commit requires a player pose; otherwise world positions would be lost.");
        _ = DaggerfallSceneOrigin.Shift(position, Vector3.Zero);
        if (_exteriorResidency is not { IsInitialized: true })
            throw new InvalidOperationException("An origin commit requires admitted exterior residency; otherwise world coordinates would detach from their cell.");
        // The commit installs pending batches and moves them with the rest; a held one would miss the move.
        ReleaseHeldPlacements();
        EntityStore store = _actors.Store;
        EntityId[] roots = [.. _actors.All.Select(actor => actor.Actor.Entity),
            .. store.Query<DaggerfallNpcBody>().Select(entry => entry.Entity)];
        (WorldOriginCommitReceipt receipt, EntityId[] excluded) = CommitThroughRebaser(store, roots, current, x, y, z, excludeOutsideEnvelope);
        try
        {
            ShiftRoots(store, excluded, receipt.LocalDelta);
            ApplyExteriorOriginCommit(receipt);
        }
        catch (Exception error)
        {
            // A native commit is immediate. Reporting a recoverable transition refusal here
            // would continue with potentially mismatched collision and product poses.
            throw new DaggerfallOriginCommitException(error);
        }
    }

    private (WorldOriginCommitReceipt Receipt, EntityId[] Excluded) CommitThroughRebaser(EntityStore store, EntityId[] roots,
        WorldOriginReadout current, long x, long y, long z, bool excludeOutsideEnvelope)
    {
        try
        {
            foreach (EntityId root in roots)
            {
                Vector3 local = store.Get(root, EngineComponentTypes.Transform).Translation;
                store.Set(root, GlobalPositions, new WorldOriginGlobalPosition(
                    current.CellX, current.CellY, current.CellZ, local.X, local.Y, local.Z));
            }
            EntityOriginRebaser rebaser = new(store, _engine.WorldOrigin, _spatial.Session, GlobalPositions);
            using EntityOriginRebaserPrepared prepared = rebaser.Prepare(x, y, z, excludeOutsideEnvelope);
            EntityId[] excluded = [.. prepared.Receipt.Excluded.ToArray().Select(row => new EntityId(row.EntityId))];
            return (prepared.Commit().Native, excluded);
        }
        finally
        {
            foreach (EntityId root in roots)
                if (store.IsAlive(root)) store.Remove(root, GlobalPositions);
        }
    }

    private static void ShiftRoots(EntityStore store, EntityId[] roots, Vector3 delta)
    {
        if (delta == Vector3.Zero) return;
        foreach (EntityId root in roots)
        {
            Transform transform = store.Get(root, EngineComponentTypes.Transform);
            Vector3 shifted = DaggerfallSceneOrigin.Shift(WorldPoint.From(transform.Translation), delta).ToVector();
            store.Set(root, EngineComponentTypes.Transform, transform with { Translation = shifted });
        }
    }

    /// <summary>
    /// Forgets the exterior window and lets go of its ground before Spatial ReplaceContentArtifact replaces the
    /// complete static collision set. A null result means this session had no exterior window.
    /// </summary>
    internal DaggerfallExteriorCellResidencyUpdate? ClearExteriorResidency()
    {
        DaggerfallExteriorCellResidencyUpdate? update = _exteriorResidency is { IsInitialized: true } residency
            ? residency.Clear()
            : null;
        DropGroundCollider();
        ReleaseExteriorWaterTriggers();
        _exteriorTerrainAppearance?.Clear();
        _exteriorGrounds?.Clear();
        return update;
    }

    /// <summary>Releases all source-water trigger references before SpatialSession disposal or a profile switch.</summary>
    internal void ReleaseExteriorWaterTriggers()
    {
        _reconciledWater = null;
        _environmentParts = [];
        foreach (ulong trigger in _exteriorWaterTriggers.OrderBy(value => value).ToArray())
        {
            _spatial.ReleaseTrigger(trigger, tick: 0);
            _exteriorWaterTriggers.Remove(trigger);
        }
    }

    /// <summary>
    /// Applies a committed Engine WorldOrigin rebase to C# authoritative player/actor poses and
    /// reprojects the exterior window in the new local frame. The Engine receipt is expected to have
    /// already shifted its retained static colliders; Update then supplies the same absolute local
    /// translations for resident terrain and admits/removes cells around the active site.
    /// </summary>
    internal DaggerfallExteriorCellResidencyUpdate ApplyExteriorOriginCommit(
        WorldOriginCommitReceipt receipt)
    {
        RequireExteriorProfile();
        DaggerfallExteriorCellResidency residency = EnsureExteriorResidency();
        if (!residency.IsInitialized)
            throw new InvalidOperationException("An exterior WorldOrigin commit requires admitted exterior residency.");

        Vector3 localDelta = DaggerfallExteriorSessionOrigin.LocalDelta(receipt);
        if (localDelta != Vector3.Zero)
        {
            // Actor and projected-person Transforms were rebased with the commit (by the Engine
            // helper in play, by the receipt delta on normalization); the player's pose lives in
            // its control state.
            ShiftPlayer(localDelta);
            Projection.Rebase(localDelta);
            foreach (ResidentExteriorLocation resident in _residentExteriorLocations.Values)
                resident.Projection.Rebase(localDelta);
            _active.RebaseActionTriggers(localDelta);
            _groundContainers.RebaseActive(localDelta);
            _host.RebaseTransientWorld(localDelta);
            // RestoreTriggers replaces the complete native active-trigger set. Release the
            // environment-owned IDs before restoring the dungeon baseline so the later terrain
            // reconciliation can register and activate them again instead of leaving the local
            // SpatialMovementSystem registry ahead of Engine's inactive set.
            ReleaseExteriorWaterTriggers();
            _active.RebaseRestoredPlayerTriggers();

            residency.AdoptRebasedOrigin(ActiveOrigin(residency));
        }

        // The camera is Engine-owned but its descriptor is derived from the product player pose.
        // Refresh it after shifting that pose, before the caller publishes the next presentation
        // snapshot.
        _camera.Update(_player);
        DaggerfallExteriorCellResidencyUpdate update = residency.Update(CurrentExteriorCell(), ActiveOrigin(residency));
        _groundContainers.ReconcileExteriorResidency(DaggerfallExteriorSessionOrigin.ResidentAt(residency.ResidentCells, ActiveOrigin(residency)));
        ReconcileExteriorTerrainAppearance(residency);
        ReconcileResidentExteriorLocations(residency);
        return update;
    }

    /// <summary>Names who adds the sea to the exterior's snapshot, and how it lets go when the exterior retires.</summary>
    internal void DrawSeaWith(Action<List<AppearanceFact>> facts, Action accepted, Action release) => (_seaFacts, _seaAccepted, _releaseSea) = (facts, accepted, release);

    /// <summary>
    /// Releases retained terrain visuals after the complete appearance snapshot is empty. The session
    /// calls this once while it is disposed; the exterior window cannot be admitted afterwards.
    /// </summary>
    internal void RetireExteriorAppearance()
    {
        ReleaseExteriorWaterTriggers();
        _retired = true;
        Projection.Appearance.SetSnapshotSupplement(null, null);
        // The session's final snapshot is empty by now, so nothing a retired neighbour drew is still in use.
        List<Exception>? failures = null;
        ReleaseRetiredResidentLocations(ref failures);
        if (failures is { Count: > 0 }) throw new AggregateException(failures);
        _releaseSea?.Invoke();
        _exteriorTerrainAppearance?.Dispose();
        _exteriorTerrainAppearance = null;
        _exteriorEnvironment = null;
        _exteriorGrounds?.Dispose();
        _exteriorGrounds = null;
    }

    /// <summary>The terrain tiles the resident environment draws over one cell, empty for a cell outside the window.</summary>
    internal IReadOnlyList<DaggerfallExteriorTerrainTile> ExteriorTerrainTiles(DaggerfallExteriorCellId cell) =>
        _exteriorEnvironment?.TerrainTilesFor(cell) ?? [];

    private DaggerfallExteriorCellResidency EnsureExteriorResidency()
    {
        ObjectDisposedException.ThrowIf(_retired, this);
        if (_exteriorResidency is not null) return _exteriorResidency;
        _exteriorGrounds = new(_definitions.Terrain, ExteriorLocations(), _tuning.Ground);
        return _exteriorResidency = new DaggerfallExteriorCellResidency(
            new DaggerfallExteriorWorldBounds(_definitions.Terrain.Width, _definitions.Terrain.Height));
    }

    /// <summary>The locations by the map pixel they stand on.</summary>
    internal IReadOnlyDictionary<DaggerfallExteriorCellId, DaggerfallSiteExterior> Locations => ExteriorLocations();

    /// <summary>The ground function exterior collision reads, the one near ground draws, and who needs it beside the player.</summary>
    internal void UseGround(DaggerfallHorizonGround ground, Func<IEnumerable<Vector3>> walkers)
    {
        _ground = ground ?? throw new ArgumentNullException(nameof(ground));
        _walkers = walkers ?? throw new ArgumentNullException(nameof(walkers));
    }

    /// <summary>A ground archive's layers.</summary>
    internal DaggerfallGroundLayers GroundLayers(int archive) => (_exteriorTerrainAppearance ??= NewTerrainAppearance()).GroundLayers(archive);

    /// <summary>What a nature archive's record draws as flora.</summary>
    internal DaggerfallFloraLook? FloraLook(int archive, int record) => (_exteriorTerrainAppearance ??= NewTerrainAppearance()).FloraLook(archive, record);

    /// <summary>Layers exterior ground by these settings.</summary>
    internal void UseGroundBlend(DaggerfallGroundBlendSettings settings)
    {
        _groundBlend = settings;
        _exteriorTerrainAppearance?.UseBlend(_engine.AuthoredContent, _tuning.Ground, settings);
    }

    /// <summary>Layers ground in the refreshed art set's surfaces, and draws nature in its flats while <paramref name="refreshed"/> answers they are drawn.</summary>
    internal void UseGroundArt(DaggerfallArtReplacements art, Func<bool> refreshed)
    {
        _groundArt = (art, refreshed);
        _exteriorTerrainAppearance?.UseArt(art, refreshed);
    }

    private DaggerfallExteriorTerrainAppearance NewTerrainAppearance()
    {
        DaggerfallExteriorTerrainAppearance appearance = new(_engine.Content, _engine.Graphics, Projection.Inputs, _tuning.Flats);
        if (_groundArt is var (art, refreshed)) appearance.UseArt(art, refreshed);
        appearance.UseBlend(_engine.AuthoredContent, _tuning.Ground, _groundBlend);
        return appearance;
    }

    private void ReconcileExteriorTerrainAppearance(DaggerfallExteriorCellResidency residency)
    {
        if (!residency.IsInitialized) return;
        DaggerfallExteriorCellGrounds grounds = _exteriorGrounds
            ?? throw new InvalidOperationException("Exterior cell grounds were not initialized.");
        // The window's missing cells build side by side, and its bordering cells after them, ready for the next crossing.
        grounds.Hold(residency.ResidentCells.Concat(residency.Bordering()));
        DaggerfallExteriorEnvironment environment = _exteriorEnvironment ??= new(_random);
        environment.Reconcile(residency.ResidentCells, ActiveOrigin(residency), grounds.Ground, ExteriorLocations(), _definitions.Grids, residency.Center);
        DaggerfallExteriorTerrainAppearance appearance =
            _exteriorTerrainAppearance ??= NewTerrainAppearance();
        appearance.ConfigureProfile(Projection.Inputs);
        Projection.Appearance.SetSnapshotSupplement(AppendExteriorFacts, CompleteExteriorSnapshot);
        appearance.Reconcile(residency.ResidentCells, ActiveOrigin(residency), environment);
        ReconcileExteriorWaterTriggers(environment.CharacterWaterVolumes(ActiveOrigin(residency)));
    }

    private Dictionary<DaggerfallExteriorCellId, DaggerfallSiteExterior> ExteriorLocations()
    {
        if (_exteriorLocations is not null) return _exteriorLocations;

        Dictionary<DaggerfallExteriorCellId, DaggerfallSiteExterior> byCell = [];
        Dictionary<DaggerfallSiteId, DaggerfallExteriorCellId> bySite = [];
        foreach (DaggerfallSiteRecord site in _definitions.Locations.Records)
        {
            if (site.Exterior is not { } exterior) continue;
            DaggerfallExteriorCellId cell = new(exterior.MapPixelX, exterior.MapPixelY);
            if (!byCell.TryAdd(cell, exterior))
                throw new InvalidOperationException($"Multiple normalized exterior locations occupy map pixel ({cell.X},{cell.Y}).");
            bySite.Add(site.Id, cell);
        }
        _exteriorCellsBySite = bySite;
        return _exteriorLocations = byCell;
    }

    private Dictionary<DaggerfallSiteId, DaggerfallExteriorCellId> ExteriorCellsBySite()
    {
        _ = ExteriorLocations();
        return _exteriorCellsBySite!;
    }

    /// <summary>The location whose exterior occupies each map pixel; a pixel holds at most one.</summary>
    private Dictionary<DaggerfallExteriorCellId, DaggerfallSiteId> ExteriorSitesByCell() =>
        _exteriorSitesByCell ??= ExteriorCellsBySite().ToDictionary(pair => pair.Value, pair => pair.Key);

    private void ReconcileExteriorWaterTriggers(IReadOnlyList<CharacterWaterVolume> desired)
    {
        ArgumentNullException.ThrowIfNull(desired);
        if (ReferenceEquals(desired, _reconciledWater)) return;
        Dictionary<ulong, CharacterWaterVolume> unique = [];
        foreach (CharacterWaterVolume volume in desired)
        {
            CharacterWaterVolume validated = volume.Validate();
            if (!unique.TryAdd(validated.Trigger, validated))
                throw new InvalidOperationException($"Exterior environment published duplicate water trigger {validated.Trigger}.");
        }

        HashSet<ulong> desiredIds = unique.Keys.ToHashSet();
        List<ulong> added = [];
        try
        {
            foreach (ulong trigger in desiredIds.OrderBy(value => value))
            {
                if (_exteriorWaterTriggers.Contains(trigger)) continue;
                _ = _spatial.RegisterTrigger(trigger, "daggerfall.exterior-water", "water");
                added.Add(trigger);
                _spatial.ActivateTrigger(trigger, tick: 0);
                _exteriorWaterTriggers.Add(trigger);
            }
        }
        catch
        {
            foreach (ulong trigger in added)
            {
                try { _spatial.ReleaseTrigger(trigger, tick: 0); }
                catch { /* preserve the registration failure as the source exception */ }
                _exteriorWaterTriggers.Remove(trigger);
            }
            throw;
        }

        foreach (ulong trigger in _exteriorWaterTriggers.Where(value => !desiredIds.Contains(value)).OrderBy(value => value).ToArray())
        {
            _spatial.ReleaseTrigger(trigger, tick: 0);
            _exteriorWaterTriggers.Remove(trigger);
        }
        _reconciledWater = desired;
    }

    // The window's water last registered as triggers, which the environment keeps until its cells or origin change.
    private IReadOnlyList<CharacterWaterVolume>? _reconciledWater;

    /// <summary>The light the eye stands in at <paramref name="eye"/>: the one sky, and the lamps of every location resident around it.</summary>
    internal Vector3 ViewLight(Vector3 eye)
    {
        Vector3 light = Projection.Lighting.SkyLight + Projection.Lighting.LampLight(eye);
        foreach (ResidentExteriorLocation resident in _residentExteriorLocations.Values)
            if (!ReferenceEquals(resident.Projection, Projection)) light += resident.Projection.Lighting.LampLight(eye);
        return light;
    }

    internal void UpdateResidentCityLights()
    {
        DaggerfallSkyTime sky = _time.Calendar.Sky;
        foreach (var resident in _residentExteriorLocations.Values)
        {
            resident.Projection.Lighting.UpdateCityLights(sky);
            resident.Projection.Lighting.UpdateWindowGlass(resident.Projection.Appearance, sky);
            resident.Projection.Lighting.UpdateLampGlow(resident.Projection.Appearance, sky);
        }
    }

    internal void SetExteriorSeason(DaggerfallExteriorSeason season, Func<int, bool>? raining = null)
    {
        if (ActiveProfile.Kind != DaggerfallWorldProfileKind.Exterior) return;
        DaggerfallExteriorSeason ForProfile(DaggerfallSiteProfile profile)
        {
            var location = _site.Require(profile.ProfileKey.Site);
            // Match the weather owner's geography query; texture climate selection stays in the profile.
            int climate = _definitions.Grids.ClimateAtWorldPixel(location.MapPixelX, location.MapPixelY).Value;
            return season == DaggerfallExteriorSeason.Winter || raining?.Invoke(climate) != true
                ? season : DaggerfallExteriorSeason.Rain;
        }
        Projection.Appearance.SetExteriorSeason(ForProfile(Projection.Inputs));
        foreach (var resident in _residentExteriorLocations.Values)
            resident.Projection.Appearance.SetExteriorSeason(ForProfile(resident.Profile));
        _exteriorEnvironment ??= new(_random, season);
        bool weatherChanged = _exteriorEnvironment.UpdateRain(_definitions.Grids.Climate.Values, raining ?? (_ => false));
        if (_exteriorEnvironment.Season == season && !weatherChanged) return;
        // SetSeason invalidates the retained source facts before the next appearance admission.
        // Retire their trigger references first so a failed rebuild cannot leave water from the
        // previous season active against an uninitialized environment.
        ReleaseExteriorWaterTriggers();
        _exteriorEnvironment.SetSeason(season, weatherChanged);
        if (_exteriorResidency is { IsInitialized: true } residency)
            ReconcileExteriorTerrainAppearance(residency);
    }

    private void RequireExteriorProfile()
    {
        if (ActiveProfile.Kind != DaggerfallWorldProfileKind.Exterior)
        {
            throw new InvalidOperationException(
                $"Exterior residency belongs only to an exterior profile; active profile is '{ActiveProfile.LogicalId}'.");
        }
    }

    private void ShiftPlayer(Vector3 localDelta)
    {
        if (_player.Position is WorldPoint position)
        {
            _player.MoveTo(
                DaggerfallSceneOrigin.Shift(position, localDelta).ToVector());
        }
        // The support anchor, tether anchor and fall/peak heights are local-frame
        // values too; left alone, the next step on a support snaps the player back.
        _player.Motion = _player.Motion.Rebased(localDelta);
    }

    /// <summary>The map pixel the active town was last admitted or let go at; null once the player is indoors.</summary>
    internal DaggerfallExteriorCellId? LocationCell => _locationCell;

    /// <summary>Records which town a crossing committed, or the one a failed crossing restores, and whether it is admitted.</summary>
    internal void RecordLocation(bool loaded, DaggerfallExteriorCellId? cell) => (_locationLoaded, _locationCell) = (loaded, cell);

    /// <summary>
    /// Follows a recall within the active exterior: the player is held inside the world edge, the origin follows them and
    /// the window moves. A recall can land past the ground's reach: the arrival stands once it is there.
    /// </summary>
    internal void FollowRecall()
    {
        HoldPlayerInsideWorldEdge();
        _ = RebaseExteriorIfNeeded();
        UpdateExteriorResidency();
        _groundAwaited = true;
    }

    /// <summary>Whether a neighbouring town's closure is resident in the window.</summary>
    internal bool HoldsResidentLocation(DaggerfallWorldProfileKey key) => _residentExteriorLocations.ContainsKey(key);

    /// <summary>
    /// Detaches a resident neighbour's state into the inactive sites' deltas, as it is about to become the active site;
    /// null when it is not resident.
    /// </summary>
    internal DaggerfallSiteRuntimeDelta? CaptureResidentLocation(DaggerfallWorldProfileKey key)
    {
        if (!_residentExteriorLocations.TryGetValue(key, out ResidentExteriorLocation? resident)) return null;
        Vector3 residentFrameOffset = ExteriorProfileTranslation(resident.Profile);
        DaggerfallSiteRuntimeDelta delta = _siteDeltas.Capture(resident.Profile, resident.Projection,
            _roster.Dynamic, resident.ActorIds, residentFrameOffset);
        _deltas[key] = delta;
        return delta;
    }

    /// <summary>
    /// Puts the window back as a failed crossing found it: the saved residency, when the crossing had let it go, with the
    /// source town's actors placed in it again (<paramref name="sourceTown"/>), and the neighbours it had retired.
    /// </summary>
    internal void RestoreAfterFailedCrossing(DaggerfallExteriorCellResidencySave prior, bool cleared, bool locationLoaded,
        DaggerfallSiteProfile? sourceTown, DaggerfallSiteRuntimeDelta sourceDelta, bool residentsRetired)
    {
        try
        {
            _admittingInitialResidency = !locationLoaded;
            if (cleared)
            {
                RestoreExteriorResidency(prior);
                if (sourceTown is not null)
                    RebaseActors(ProfileActorIds(sourceTown, sourceDelta), ExteriorProfileTranslation(sourceTown, prior.WorldOrigin));
            }
            if (residentsRetired && _exteriorResidency is { IsInitialized: true } restoredResidency)
                ReconcileResidentExteriorLocations(restoredResidency);
        }
        finally { _admittingInitialResidency = false; }
    }
}

/// <summary>
/// The Engine refused the arrival's collision, so the player arrived on ground that is not solid yet. The Engine kept
/// none of the batch and its host log names why, or rejected it in the call (<paramref name="inner"/>); either way
/// it is handed over again on the next update.
/// </summary>
internal sealed class DaggerfallArrivalCollisionRefusedException(Exception? inner = null)
    : InvalidOperationException("The ground here could not be made solid: the Engine refused its collision, and its log says why. The world waits while it is made again.", inner);
