using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Entities;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Behavior;
using WorldRpg.Rulesets.Daggerfall.Modules.Loot;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// The session consequences a site transition reaches that the site lifecycle does not own. The
/// lifecycle calls each at one fixed point of the transition; the session answers.
/// </summary>
internal interface IDaggerfallSiteTransitionHost
{
    /// <summary>Installs a landing pose, clearing retained input and open interactions first.</summary>
    void RelocatePlayer(WorldPoint position, float yawRadians, float pitchRadians);

    /// <summary>Rebuilds contextual activation over the lifecycle's current projection.</summary>
    void RebuildActivation();

    void RebaseTransientWorld(Vector3 delta);

    /// <summary>The destination became the active site; site-scoped session presentation moves on.</summary>
    void EnteredSite();

    /// <summary>Admits retained source-civilian billboards into a resident projection.</summary>
    void AdmitResidentCivilianAppearances(DaggerfallSiteProjection projection);

    /// <summary>The departing projection is about to be released; retire what it was still timing.</summary>
    void RetireDepartingProjection(DaggerfallSiteProjection source);
}

/// <summary>
/// The one owner of where the session is projected: the live site projection, the admitted site
/// catalog, the active and return profiles, the inactive sites' detached deltas, the site-bound bank
/// provider, the action-trigger runtime and the exterior cell window. Every site transition, relocation
/// and exterior residency change goes through here, and a failed transition restores the source.
/// </summary>
internal sealed class DaggerfallSiteLifecycle
{
    /// <summary>
    /// The product column the Engine <see cref="EntityOriginRebaser"/> reads global positions from.
    /// Values exist only for the duration of one origin commit.
    /// </summary>
    internal static readonly ComponentType<WorldOriginGlobalPosition> GlobalPositions =
        ComponentType<WorldOriginGlobalPosition>.Create(ProductComponentKeys.Create(GlobalPositionComponentId));
    private const uint GlobalPositionComponentId = 1;

    private readonly IEngineContext _engine;
    private readonly DaggerfallState _state;
    private readonly DaggerfallDefinitions _definitions;
    private readonly DaggerfallTuning _tuning;
    private readonly IRandomService _random;
    private readonly DaggerfallWorldTime _time;
    private readonly DaggerfallSiteContext _site;
    private readonly SpatialMovementSystem _spatial;
    private readonly FirstPersonCameraSystem _camera;
    private readonly DaggerfallSiteAudioBundles? _audioBundles;
    private readonly DaggerfallActorRoster _roster;
    private readonly DaggerSessionPersistence _persistence;
    private readonly DaggerfallGroundContainers _groundContainers;
    private readonly DaggerfallEnemyBehaviorModule _enemyBehavior;
    private readonly Func<DaggerfallDungeonActionDefinition, DaggerfallDungeonActionExecution?> _executeFamilyAction;
    private readonly IDaggerfallSiteTransitionHost _host;
    private readonly Dictionary<DaggerfallWorldProfileKey, DaggerfallSiteRuntimeDelta> _deltas = [];
    private DaggerfallExteriorCellResidency? _exteriorResidency;
    private DaggerfallExteriorTerrainAppearance? _exteriorTerrainAppearance;
    private DaggerfallExteriorEnvironment? _exteriorEnvironment;
    private Func<DaggerfallExteriorCellId, DaggerfallTerrainSurface>? _exteriorSurfaceFactory;
    private Dictionary<DaggerfallExteriorCellId, DaggerfallSiteExterior>? _exteriorLocations;
    private Dictionary<DaggerfallSiteId, DaggerfallExteriorCellId>? _exteriorCellsBySite;
    private Dictionary<DaggerfallExteriorCellId, DaggerfallSiteId>? _exteriorSitesByCell;
    private readonly Dictionary<DaggerfallExteriorCellId, DaggerfallTerrainSurface> _exteriorSurfaceCache = [];
    private readonly HashSet<ulong> _exteriorWaterTriggers = [];
    private readonly Dictionary<DaggerfallWorldProfileKey, ResidentExteriorLocation> _residentExteriorLocations = [];
    // A retired neighbour's drawn location stays in the last accepted snapshot until the next one is accepted
    // without it; only then can the Engine release it.
    private readonly List<IDisposable> _retiredResidentLocations = [];
    // Exterior artifacts publish navigation cells at the source terrain's 0.8-unit grid. One
    // wilderness map pixel is 819.2 world units, so adjacent location closures are exactly 1024
    // navigation cells apart in the shared Engine artifact grid.
    internal const long NavigationCellsPerExteriorCell = 1024;
    private bool _locationLoaded;
    private DaggerfallExteriorCellId? _locationCell;
    private bool _admittingInitialResidency;
    private bool _retired;

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

    /// <summary>The session-wide classic presentation every site projection draws held items and effects from.</summary>
    private readonly NormalizedClassicPresentation? _sessionPresentation;

    internal DaggerfallSiteLifecycle(IEngineContext engine, DaggerfallState state, DaggerfallDefinitions definitions,
        DaggerfallTuning tuning, DaggerfallWorldTime time, DaggerfallSiteContext site, SpatialMovementSystem spatial,
        FirstPersonCameraSystem camera, DaggerfallSiteAudioBundles? audioBundles, DaggerfallActorRoster roster,
        DaggerSessionPersistence persistence, DaggerfallGroundContainers groundContainers, DaggerfallEnemyBehaviorModule enemyBehavior,
        Func<DaggerfallDungeonActionDefinition, DaggerfallDungeonActionExecution?> executeFamilyAction, IDaggerfallSiteTransitionHost host,
        DaggerfallSiteProjection projection, DaggerfallDungeonActionTriggerRuntime actionTriggers, DaggerfallSiteProfiles? profiles,
        DaggerfallWorldProfileKey activeProfile, DaggerfallWorldProfileKey? returnProfile,
        NormalizedClassicPresentation? sessionPresentation = null)
    {
        _sessionPresentation = sessionPresentation;
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _state = state ?? throw new ArgumentNullException(nameof(state));
        if (!_state.Actors.Store.Diagnostics(0).Components.Any(component => component.Key == GlobalPositions.Key))
            _state.Actors.Store.Register(GlobalPositions);
        _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
        _tuning = tuning ?? throw new ArgumentNullException(nameof(tuning));
        _random = engine.Random;
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _site = site ?? throw new ArgumentNullException(nameof(site));
        _spatial = spatial ?? throw new ArgumentNullException(nameof(spatial));
        _camera = camera ?? throw new ArgumentNullException(nameof(camera));
        _audioBundles = audioBundles;
        _roster = roster ?? throw new ArgumentNullException(nameof(roster));
        _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
        _groundContainers = groundContainers ?? throw new ArgumentNullException(nameof(groundContainers));
        _enemyBehavior = enemyBehavior ?? throw new ArgumentNullException(nameof(enemyBehavior));
        _executeFamilyAction = executeFamilyAction ?? throw new ArgumentNullException(nameof(executeFamilyAction));
        _host = host ?? throw new ArgumentNullException(nameof(host));
        Projection = projection ?? throw new ArgumentNullException(nameof(projection));
        ActionTriggers = actionTriggers ?? throw new ArgumentNullException(nameof(actionTriggers));
        Profiles = profiles;
        ActiveProfile = activeProfile;
        ReturnProfile = returnProfile;
        _locationLoaded = activeProfile.Kind != DaggerfallWorldProfileKind.Exterior;
    }

    /// <summary>The active site's live projection: doors, motion, appearance, lighting and portals.</summary>
    internal DaggerfallSiteProjection Projection { get; private set; }

    /// <summary>The action-trigger runtime spanning every admitted profile.</summary>
    internal DaggerfallDungeonActionTriggerRuntime ActionTriggers { get; }

    /// <summary>The admitted site catalog, once composition has admitted it.</summary>
    internal DaggerfallSiteProfiles? Profiles { get; private set; }

    internal DaggerfallWorldProfileKey ActiveProfile { get; private set; }

    /// <summary>The profile a return portal leads back to, when the player entered from one.</summary>
    internal DaggerfallWorldProfileKey? ReturnProfile { get; private set; }

    /// <summary>Whether the active profile's location resources and actor roster are currently admitted.</summary>
    internal bool ActiveLocationLoaded => ActiveProfile.Kind != DaggerfallWorldProfileKind.Exterior || _locationLoaded;

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
        CharacterStepEnvironment projection = Projection.CharacterEnvironment(motion);
        foreach (ResidentExteriorLocation resident in _residentExteriorLocations.Values
            .OrderBy(value => value.Profile.ProfileKey.LogicalId, StringComparer.Ordinal))
            projection = DaggerfallSiteProjection.CombineCharacterEnvironments(
                projection, resident.Projection.CharacterEnvironment(motion));
        if (ActiveProfile.Kind != DaggerfallWorldProfileKind.Exterior
            || Projection.Inputs.ProfileKey != ActiveProfile
            || _exteriorResidency is not { IsInitialized: true } residency
            || _exteriorEnvironment is not { IsInitialized: true } environment)
            return projection;

        CharacterWaterVolume[] sourceWater = [.. environment.CharacterWaterVolumes(ActiveOrigin(residency))];
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
        if (sourceWater.Length == 0) return projection;
        return new CharacterStepEnvironment(projection.Support, projection.Obstacles, projection.MeshInstances,
            water.Values.OrderBy(value => value.Trigger).ToArray());
    }

    private static ulong LocationPlacementId(DaggerfallWorldProfileKey profile, DaggerfallSiteSpatialPart part) =>
        StableHash.NonZeroFnv1a64($"daggerfall.location-artifact.v1|{profile.LogicalId}|{part.Id}");

    /// <summary>The Engine placement identities of every collision/navigation part a profile places.</summary>
    private static ulong[] LocationPlacementIds(DaggerfallSiteProfile profile) =>
        [.. profile.Geometry.Spatial.Select(part => LocationPlacementId(profile.ProfileKey, part))];

    /// <summary>
    /// A profile's collision/navigation parts, each placed by its own whole-cell offset in the profile frame
    /// and, for an exterior, by the location's map-pixel offset from the active origin and its terrain height.
    /// </summary>
    private SpatialContentArtifactPlacement[] LocationPlacements(
        DaggerfallSiteProfile profile,
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
            columnOffset = checked((long)(cell.X - origin.MapPixelX) * NavigationCellsPerExteriorCell);
            rowOffset = checked((long)(origin.MapPixelY - cell.Y) * NavigationCellsPerExteriorCell);
            translation = ExteriorLocationPlacementTranslation(profile);
        }

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
    private void ApplyLocationResidency(DaggerfallSiteProfile? placed, DaggerfallExteriorWorldOrigin? exteriorOrigin,
        IEnumerable<ulong> removals, ulong navigationGridId)
    {
        using IDisposable? source = placed?.Geometry.SpatialSource?.Invoke();
        _ = _spatial.ApplyContentArtifactResidency(placed is null ? [] : LocationPlacements(placed, exteriorOrigin), removals, navigationGridId);
    }

    private void AdmitLocationArtifact(DaggerfallSiteProfile profile, DaggerfallExteriorWorldOrigin? exteriorOrigin = null) =>
        ApplyLocationResidency(profile, exteriorOrigin, [], profile.Geometry.NavigationGridId);

    private void RemoveLocationArtifact(DaggerfallSiteProfile profile) =>
        ApplyLocationResidency(null, null, LocationPlacementIds(profile), profile.Geometry.NavigationGridId);

    /// <summary>Inactive sites' detached actor, door, effect and motion state, by profile.</summary>
    internal IReadOnlyDictionary<DaggerfallWorldProfileKey, DaggerfallSiteRuntimeDelta> Deltas => _deltas;

    /// <summary>Nearby exterior profiles whose full location closures are currently admitted.</summary>
    internal IReadOnlyCollection<DaggerfallWorldProfileKey> ResidentExteriorProfiles =>
        _residentExteriorLocations.Keys.OrderBy(key => key.Site.Region)
            .ThenBy(key => key.Site.Index).ThenBy(key => key.LogicalId, StringComparer.Ordinal).ToArray();

    /// <summary>The live banking provider the player opened at this site; any site change closes it.</summary>
    internal DaggerfallServiceProvider? BankProvider { get; set; }

    internal DaggerfallSiteProfiles RequireProfiles() =>
        Profiles ?? throw new InvalidOperationException("Site profiles have not been admitted.");

    /// <summary>Adds the saved inactive sites' deltas to a restoring session; ResolveRestore admitted each profile once.</summary>
    internal void RestoreDeltas(IEnumerable<DaggerfallSiteDeltaSave> saved)
    {
        foreach (DaggerfallSiteDeltaSave delta in saved)
            _deltas.Add(delta.Profile.Require(), new DaggerfallSiteRuntimeDelta(delta.Actors, delta.DynamicActors, delta.ActorInventories,
                delta.Corpses, delta.Doors, delta.Effects, delta.Motion) { BanishedActors = delta.BanishedActors });
    }

    /// <summary>Replaces one inactive site's delta after an owner edited its detached state.</summary>
    internal void ReplaceDelta(DaggerfallWorldProfileKey profile, DaggerfallSiteRuntimeDelta delta)
    {
        if (!_deltas.ContainsKey(profile))
            throw new InvalidOperationException($"Site '{profile.LogicalId}' has no inactive delta to replace.");
        _deltas[profile] = delta ?? throw new ArgumentNullException(nameof(delta));
    }

    /// <summary>
    /// The profiles a session starts with action graphs for: the active one, and on a restore every
    /// profile the save carries action state for, in the stable order action graph save identity uses.
    /// Any other profile gains its graph when the session first enters it.
    /// </summary>
    internal static IReadOnlyList<(DaggerfallWorldProfileKey Key, DaggerfallSiteProfile Inputs)> ActionProfiles(
        DaggerfallSiteProfile active,
        DaggerfallSiteProfiles? profiles,
        IEnumerable<string>? savedProfileIds = null)
    {
        ArgumentNullException.ThrowIfNull(active);
        Dictionary<DaggerfallWorldProfileKey, DaggerfallSiteProfile> admitted = new() { [active.ProfileKey] = active };
        foreach (string id in savedProfileIds ?? [])
        {
            if (StringComparer.Ordinal.Equals(id, active.ProfileKey.LogicalId)) continue;
            DaggerfallSiteProfile saved = (profiles ?? throw new ArgumentException($"Saved action state names profile '{id}' without an admitted site catalog.", nameof(profiles)))
                .RequireLogicalProfile(id);
            admitted.TryAdd(saved.ProfileKey, saved);
        }
        if (admitted.Keys.Select(key => key.LogicalId).Distinct(StringComparer.Ordinal).Count() != admitted.Count)
            throw new ArgumentException("Admitted world profiles must use distinct logical ids for action graph save identity.", nameof(profiles));
        return admitted.OrderBy(entry => entry.Key.Site.Region)
            .ThenBy(entry => entry.Key.Site.Index)
            .ThenBy(entry => entry.Key.LogicalId, StringComparer.Ordinal)
            .Select(entry => (entry.Key, entry.Value))
            .ToArray();
    }

    /// <summary>Admits the full selected site catalog once composition has constructed this session.</summary>
    internal void AdmitProfiles(DaggerfallSiteProfiles profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        if (Profiles is not null)
        {
            if (ReferenceEquals(Profiles, profiles)) return;
            throw new InvalidOperationException("Daggerfall site profiles are already admitted for this session.");
        }
        // The catalog resolves profiles lazily: the active one is admitted now, and every other profile's
        // action graph, triggers and audio are admitted when the session first enters it.
        DaggerfallSiteProfile active = profiles.Require(ActiveProfile);
        Profiles = profiles;
        EnsureDungeonActionGraph(ActiveProfile, active);
    }

    internal DaggerfallAudioBundle? AudioFor(DaggerfallSiteProfile inputs) => _audioBundles?.Require(inputs);

    private void EnsureDungeonActionGraph(DaggerfallWorldProfileKey key, DaggerfallSiteProfile inputs)
    {
        key.Validate();
        ArgumentNullException.ThrowIfNull(inputs);
        ActionTriggers.AdmitProfile(key, inputs);
        if (_state.DungeonActions.ContainsKey(key)) return;
        if (inputs.ProfileKey != key)
            throw new InvalidOperationException($"World profile '{key.LogicalId}' action graph was paired with '{inputs.ProfileKey.LogicalId}'.");
        _state.DungeonActions.Add(key, new DaggerfallDungeonActionGraph(key.LogicalId, inputs.DungeonActions,
            _state.Variables, executeFamilyAction: _executeFamilyAction));
    }

    /// <summary>
    /// Relocates the existing player to an admitted named anchor. The destination is resolved
    /// before a source projection is touched, so a missing anchor cannot unload the player or
    /// source actors. Cross-profile relocation reuses the one site-transition lifecycle.
    /// </summary>
    internal bool TryRelocate(DaggerfallRelocationDestination destination)
    {
        destination = (destination ?? throw new ArgumentNullException(nameof(destination))).Validate();
        DaggerfallSiteProfile target = RequireProfiles().Require(destination.Profile);
        DaggerfallSiteAnchor anchor = target.RequireAnchor(destination.AnchorId);
        if (destination.ActorId != DaggerfallActorIdentity.PlayerEntityId)
        {
            if (!_state.Actors.TryGet(destination.ActorId, out _))
                throw new InvalidOperationException($"Actor {destination.ActorId} is not live in the active profile.");
            // Moving an actor into a site the player is not in edits that site's delta; no caller
            // needs it yet, and a temporary round trip through the player's own transition is not
            // how it should be done, so it is refused rather than approximated.
            if (destination.Profile != ActiveProfile)
                throw new InvalidOperationException($"Actor {destination.ActorId} cannot relocate to an inactive world profile.");
            ActorState actor = _state.Actors.TryGet(destination.ActorId, out ActorState? live)
                ? live
                : throw new InvalidOperationException($"Actor {destination.ActorId} is not live in the active profile.");
            actor.ApplyPose(new ActorPose(ProfileToLocal(anchor.Position), anchor.YawRadians));
            return true;
        }
        return TryRelocatePlayer(destination.Profile, anchor);
    }

    /// <summary>Recalls a saved pose through the same admission and movement owner as named anchors.</summary>
    internal bool TryRelocatePlayer(DaggerfallWorldProfileKey profile, DaggerfallSiteAnchor pose)
    {
        profile.Validate();
        if (profile != ActiveProfile) _ = RequireProfiles().Require(profile);
        pose.Validate();
        if (profile == ActiveProfile)
        {
            _host.RelocatePlayer(ProfileToLocal(pose.Position), pose.YawRadians, pose.PitchRadians);
            if (profile.Kind == DaggerfallWorldProfileKind.Exterior)
            {
                _ = RebaseExteriorIfNeeded();
                UpdateExteriorResidency();
                StandArrivingPlayer();
            }
            return true;
        }
        return TryTransitionTo(profile, pose, useReturnDestination: false);
    }

    /// <summary>
    /// Stands a player placed at an exterior anchor on the admitted collision below it. Exterior anchors (a door's
    /// threshold, a location's edge, a start marker) name the ground the player arrives on, while the player's position
    /// is its capsule's centre; the donor's <c>StreamingWorld.RepositionPlayer</c> likewise lifts an arrival to at least
    /// half a controller height above the ground. Runs once the destination's collision and terrain window are admitted.
    /// </summary>
    private void StandArrivingPlayer()
    {
        PlayerControlState player = _state.PlayerControl;
        WorldPoint arrival = player.Position ?? throw new InvalidOperationException("An exterior arrival requires a player position.");
        WorldPoint standing = _spatial.StandingPosition(arrival, CharacterEnvironment(player.Motion));
        if (standing != arrival) _host.RelocatePlayer(standing, player.YawRadians, player.PitchRadians);
    }

    /// <summary>World relocation has no doorway back; its caller may own a different return workflow.</summary>
    internal void ClearReturnDestination()
    {
        ReturnProfile = null;
        _site.ClearReturnDestination();
    }

    internal void RestoreReturnDestination(DaggerfallWorldProfileKey? profile, DaggerfallSiteReturnPose? pose)
    {
        if ((profile is null) != (pose is null)) throw new ArgumentException("A saved entrance requires its profile and pose together.");
        if (profile is { } destination) _ = RequireProfiles().Require(destination);
        _site.RestoreReturnDestination(profile?.Site, pose);
        ReturnProfile = profile;
    }

    /// <summary>
    /// Attempts one real site transition; failed destination admission leaves the source projection live. A
    /// remembered entrance back into the destination wins; otherwise the player lands at the named arrival
    /// anchor, or at the destination's start. An authored destination may publish no landing for a door an
    /// assembled location names, and then receives the player at its start as any entrance without one does.
    /// </summary>
    internal bool TryTransitionTo(DaggerfallWorldProfileKey destination, string? arrivalAnchor = null)
    {
        DaggerfallSiteAnchor? arrival = null;
        if (arrivalAnchor is not null && RequireProfiles().Require(destination).Anchors.TryGetValue(arrivalAnchor, out DaggerfallSiteAnchor? anchor))
            arrival = anchor;
        return TryTransitionTo(destination, arrival, useReturnDestination: true);
    }

    private bool TryTransitionTo(DaggerfallWorldProfileKey destination, DaggerfallSiteAnchor? arrival, bool useReturnDestination)
    {
        DaggerfallSiteProfile target = RequireProfiles().Require(destination);
        PlayerControlState player = _state.PlayerControl;
        _ = player.Position ?? throw new InvalidOperationException("A site transition requires a player position.");
        // Detached site state and return poses use the profile's authored frame. Restore that
        // frame before capture and before replacing the native artifact (which retains its origin).
        if (ActiveProfile.Kind == DaggerfallWorldProfileKind.Exterior)
            NormalizeExteriorOrigin();
        WorldPoint sourcePosition = player.Position.Value;
        DaggerfallSiteReturnDestination? returnDestination = useReturnDestination && ReturnProfile == destination
            ? _site.RequireReturnDestination()
            : null;
        DaggerfallSiteProjection source = Projection;
        DaggerfallWorldProfileKey sourceProfile = ActiveProfile;
        DaggerfallWorldProfileKey? sourceReturnProfile = ReturnProfile;
        DaggerfallServiceProvider? sourceBankProvider = BankProvider;
        DaggerfallSiteContextCheckpoint sourceSite = _site.CaptureCheckpoint();
        float sourceYawRadians = player.YawRadians;
        float sourcePitchRadians = player.PitchRadians;
        bool sourceLocationLoaded = ActiveLocationLoaded;
        DaggerfallExteriorCellId? sourceLocationCell = _locationCell;
        IReadOnlySet<long> sourceDynamicActorIds = ActiveDynamicActorIds();
        Vector3 sourceFrameOffset = sourceProfile.Kind == DaggerfallWorldProfileKind.Exterior
            ? ExteriorProfileTranslation(source.Inputs)
            : Vector3.Zero;
        DaggerfallSiteRuntimeDelta sourceDelta;
        DaggerfallSiteRuntimeDelta? sourceLiveActorDelta = null;
        if (sourceLocationLoaded)
        {
            sourceDelta = _persistence.CaptureSiteDelta(source.Inputs, source.Doors, source.Motion, _roster.Dynamic,
                sourceDynamicActorIds, sourceFrameOffset);
        }
        else
        {
            DaggerfallSiteRuntimeDelta detachedSource = _deltas.TryGetValue(sourceProfile, out DaggerfallSiteRuntimeDelta? savedSource)
                ? savedSource
                : throw new InvalidOperationException($"Unloaded source profile '{sourceProfile.LogicalId}' has no detached delta.");
            (sourceDelta, sourceLiveActorDelta) = CaptureDetachedLiveActors(detachedSource, sourceDynamicActorIds, sourceFrameOffset);
        }
        DaggerfallExteriorCellResidencySave? sourceExterior = CaptureExteriorResidency();
        DaggerfallExteriorWorldOrigin? destinationOrigin = null;
        Vector3 destinationFrameOffset = Vector3.Zero;
        if (target.ProfileKind == DaggerfallWorldProfileKind.Exterior)
        {
            if (!TryExteriorProfileCell(target, out DaggerfallExteriorCellId destinationCell))
                throw new InvalidOperationException($"Exterior profile '{target.ProfileKey.LogicalId}' has no normalized map-pixel identity.");
            // Each exterior transition establishes the destination cell as the local origin. Keeping
            // the source map origin here would add a far-away map translation to the authored arrival
            // pose in a float Vector3, irreversibly dropping sub-cell precision before the destination
            // profile can read it back through ExteriorSitePosition.
            destinationOrigin = DaggerfallExteriorWorldOrigin.At(destinationCell);
            destinationFrameOffset = ExteriorProfileTranslation(target, destinationOrigin.Value);
        }
        _deltas.TryGetValue(destination, out DaggerfallSiteRuntimeDelta? destinationDelta);
        if (destinationDelta is null && _residentExteriorLocations.TryGetValue(destination, out ResidentExteriorLocation? residentDestination))
        {
            Vector3 residentFrameOffset = ExteriorProfileTranslation(residentDestination.Profile);
            destinationDelta = _persistence.CaptureSiteDelta(residentDestination.Profile, residentDestination.Projection.Doors,
                residentDestination.Projection.Motion, _roster.Dynamic, residentDestination.ActorIds, residentFrameOffset);
            _deltas[destination] = destinationDelta;
        }
        // A resident destination was just detached into _deltas above. Include that snapshot in
        // rollback state so a later admission failure can restore the resident with its captured
        // identity and values instead of recreating a fresh profile with no delta.
        Dictionary<DaggerfallWorldProfileKey, DaggerfallSiteRuntimeDelta> sourceDeltas = new(_deltas);
        DaggerfallSiteProjection? candidate = null;
        bool spatialReplaced = false;
        bool sourceProjectionSuspended = false;
        bool sourceActorsUnloaded = false;
        bool groundProfileSwitched = false;
        bool playerRelocated = false;
        bool exteriorCleared = false;
        bool adjacentLocationsRetired = false;
        try
        {
            candidate = DaggerfallSiteProjection.Create(_engine, _state.Actors.Entities, _random, _tuning, _time.Calendar,
                target, AudioFor(target), _spatial, destinationDelta?.Doors, destinationDelta?.Motion,
                deferMotionCollisionAdmission: true, sessionPresentation: _sessionPresentation);
            if (target.ProfileKind == DaggerfallWorldProfileKind.Exterior)
                candidate.Rebase(destinationFrameOffset);
            if (sourceExterior is not null)
            {
                RetireResidentExteriorLocations(capture: true);
                adjacentLocationsRetired = true;
                ClearExteriorResidency();
                exteriorCleared = true;
            }
            ApplyLocationResidency(target, destinationOrigin,
                sourceLocationLoaded ? LocationPlacementIds(source.Inputs) : [],
                target.Geometry.NavigationGridId);
            spatialReplaced = true;
            candidate.ActivateMotionCollisionResidency();
            // Admit the destination collision models before retiring the source.  The Engine
            // residency seam is additive, so this keeps a transition continuously covered while
            // the source actors and their projection are being torn down.
            if (sourceLocationLoaded)
            {
                source.Suspend();
                sourceProjectionSuspended = true;
            }
            // A profile can already have its authored closure detached while a caller still holds a
            // live spawned actor in the one canonical roster.  Capture and retire that actor at the
            // same profile boundary; otherwise the durable entity remains materialized while the
            // source site is inactive and can be mistaken for a destination actor on re-entry.
            if (sourceLocationLoaded || sourceLiveActorDelta is not null)
            {
                _roster.UnloadSite(source.Inputs, sourceDelta);
                sourceActorsUnloaded = true;
            }
            Projection = candidate;
            candidate = null;
            MaterializeSiteActors(target, destinationDelta);
            if (target.ProfileKind == DaggerfallWorldProfileKind.Exterior)
                RebaseActors(ProfileActorIds(target, destinationDelta), destinationFrameOffset);
            _groundContainers.SwitchProfile(destination);
            groundProfileSwitched = true;
            // Activation depends only on the admitted candidate projection.  Prepare it before
            // mutating player or site state so an Engine service rejection has nothing semantic
            // to roll back.
            _host.RebuildActivation();
            if (returnDestination is { } returned)
            {
                _site.Leave();
                playerRelocated = true;
                _host.RelocatePlayer(returned.Pose.Position, returned.Pose.YawRadians, returned.Pose.PitchRadians);
            }
            else
            {
                _site.Enter(destination.Site, sourcePosition, player.YawRadians, player.PitchRadians);
                playerRelocated = true;
                if (arrival is { } selected)
                    _host.RelocatePlayer(destination.Kind == DaggerfallWorldProfileKind.Exterior
                        ? DaggerfallExteriorSessionOrigin.Shift(selected.Position, destinationFrameOffset)
                        : selected.Position, selected.YawRadians, selected.PitchRadians);
                else
                    _host.RelocatePlayer(destination.Kind == DaggerfallWorldProfileKind.Exterior
                        ? DaggerfallExteriorSessionOrigin.Shift(target.Project.PlayerPosition ?? sourcePosition, destinationFrameOffset)
                        : target.Project.PlayerPosition ?? sourcePosition, player.YawRadians, player.PitchRadians);
            }
            _deltas[sourceProfile] = sourceDelta;
            _deltas.Remove(destination);
            ActiveProfile = destination;
            // Entering a place retires the previous world's loop: the donor gives a dungeon a new song
            // per location rather than carrying the last one through the door.
            _host.EnteredSite();
            BankProvider = null;
            if (destination.Kind == DaggerfallWorldProfileKind.Exterior)
            {
                _locationLoaded = true;
                _locationCell = ActiveExteriorCell();
                // The player pose was translated from the captured destination origin above. The
                // residency coordinator was cleared while replacing the source artifact, so the
                // no-argument overload would derive a new At(site) frame and interpret that pose
                // in the wrong map cell. Re-admit the target window in the same canonical frame.
                UpdateExteriorResidency(destinationOrigin
                    ?? throw new InvalidOperationException("An exterior transition did not resolve its destination origin."));
                // A returning player resumes the pose they left; an arrival lands on its anchor's ground.
                if (returnDestination is null) StandArrivingPlayer();
            }
            else
            {
                _locationLoaded = true;
                _locationCell = null;
            }
            if (destination.Kind != DaggerfallWorldProfileKind.Exterior)
                _state.Transport.ForceFootOnInteriorTransition();
            ReturnProfile = returnDestination is null ? sourceProfile : null;
            _enemyBehavior.ClearPerceptionMemory();
            if (target.DungeonMap is { } destinationMap)
            {
                if (!_state.DungeonDiscoveries.TryGetValue(destination, out DaggerfallDungeonDiscovery? discovery))
                    _state.DungeonDiscoveries.Add(destination, discovery = new DaggerfallDungeonDiscovery(destination, destinationMap));
                discovery.BeginVisit();
            }
            EnsureDungeonActionGraph(destination, target);
            ActionTriggers.ActivateProfile(destination);
        }
        catch (Exception failure)
        {
            List<Exception> failures = [failure];
            DaggerfallSiteProjection? rejectedProjection = null;
            if (!ReferenceEquals(Projection, source))
            {
                rejectedProjection = Projection;
                Projection = source;
            }
            if (groundProfileSwitched)
            {
                try { _groundContainers.SwitchProfile(sourceProfile); }
                catch (Exception groundFailure) { failures.Add(groundFailure); }
            }
            if (sourceActorsUnloaded)
            {
                try { _roster.UnloadSite(target, destinationDelta); }
                catch (Exception teardownFailure) { failures.Add(teardownFailure); }
                try
                {
                    if (sourceLocationLoaded)
                        MaterializeSiteActors(source.Inputs, sourceDelta, sourceLocationLoaded);
                    else if (sourceLiveActorDelta is not null)
                        RestoreDetachedLiveActors(sourceLiveActorDelta, source);
                }
                catch (Exception restoreFailure) { failures.Add(restoreFailure); }
            }
            if (spatialReplaced)
            {
                try
                {
                    if (sourceLocationLoaded)
                        ApplyLocationResidency(source.Inputs, sourceExterior is { } saved ? saved.WorldOrigin : null,
                            LocationPlacementIds(target), source.Inputs.Geometry.NavigationGridId);
                    else
                        ApplyLocationResidency(null, null, LocationPlacementIds(target), source.Inputs.Geometry.NavigationGridId);
                }
                catch (Exception rollbackFailure) { failures.Add(rollbackFailure); }
                // Whole-content replacement removes every incremental resident collider. Drop the
                // destination coordinator's remembered set before restoring the source window;
                // otherwise overlapping cells would be skipped as already admitted.
                try { ClearExteriorResidency(); }
                catch (Exception rollbackFailure) { failures.Add(rollbackFailure); }
            }
            if (sourceProjectionSuspended)
            {
                try { source.Resume(); }
                catch (Exception rollbackFailure) { failures.Add(rollbackFailure); }
            }
            try { source.Lighting.ApplyBackground(); }
            catch (Exception rollbackFailure) { failures.Add(rollbackFailure); }
            try { _site.RestoreCheckpoint(sourceSite); }
            catch (Exception rollbackFailure) { failures.Add(rollbackFailure); }
            _deltas.Clear();
            foreach ((DaggerfallWorldProfileKey key, DaggerfallSiteRuntimeDelta delta) in sourceDeltas) _deltas.Add(key, delta);
            ActiveProfile = sourceProfile;
            _locationLoaded = sourceLocationLoaded;
            _locationCell = sourceLocationCell;
            ReturnProfile = sourceReturnProfile;
            BankProvider = sourceBankProvider;
            if (sourceExterior is { } priorExterior && exteriorCleared)
            {
                try
                {
                    _admittingInitialResidency = !sourceLocationLoaded;
                    RestoreExteriorResidency(priorExterior);
                    if (sourceLocationLoaded && sourceProfile.Kind == DaggerfallWorldProfileKind.Exterior)
                        RebaseActors(ProfileActorIds(source.Inputs, sourceDelta),
                            ExteriorProfileTranslation(source.Inputs, priorExterior.WorldOrigin));
                    if (adjacentLocationsRetired && _exteriorResidency is { IsInitialized: true } restoredResidency)
                        ReconcileResidentExteriorLocations(restoredResidency);
                }
                catch (Exception rollbackFailure) { failures.Add(rollbackFailure); }
                finally { _admittingInitialResidency = false; }
            }
            else if (_exteriorResidency is { IsInitialized: true })
            {
                try { ClearExteriorResidency(); }
                catch (Exception rollbackFailure) { failures.Add(rollbackFailure); }
            }
            try { ActionTriggers.ActivateProfile(sourceProfile); }
            catch (Exception rollbackFailure) { failures.Add(rollbackFailure); }
            if (playerRelocated)
            {
                try { _host.RelocatePlayer(sourcePosition, sourceYawRadians, sourcePitchRadians); }
                catch (Exception rollbackFailure) { failures.Add(rollbackFailure); }
            }
            try { _host.RebuildActivation(); }
            catch (Exception rollbackFailure) { failures.Add(rollbackFailure); }
            try { candidate?.Dispose(); }
            catch (Exception disposeFailure) { failures.Add(disposeFailure); }
            try { rejectedProjection?.Dispose(); }
            catch (Exception disposeFailure) { failures.Add(disposeFailure); }
            if (failures.Count == 1) throw;
            throw new AggregateException("Site transition failed and source restoration was incomplete.", failures);
        }

        if (_state.DungeonDiscoveries.TryGetValue(sourceProfile, out DaggerfallDungeonDiscovery? sourceDiscovery)
            && source.Inputs.DungeonMap is { } sourceMap)
        {
            DaggerfallSiteMarker? usedPortal = sourceMap.Markers
                .Where(marker => marker.Kind == DaggerfallSiteMarkerKind.Portal
                    && marker.DestinationLogicalProfile == destination.LogicalId)
                .OrderBy(marker => Vector3.DistanceSquared(marker.Position.ToVector(), sourcePosition.ToVector()))
                .FirstOrDefault();
            if (usedPortal is not null && Vector3.DistanceSquared(usedPortal.Position.ToVector(), sourcePosition.ToVector()) <= 9f)
                sourceDiscovery.RevealMarker(usedPortal.Id);
        }

        // Releasing the old projection happens only after every durable and live owner has
        // committed to the destination. A disposal failure therefore leaves the destination as
        // the honest current state instead of pretending the torn-down source can be restored.
        _host.RetireDepartingProjection(source);
        source.Dispose();
        return true;
    }

    /// <summary>Creates a destination's actors and reapplies the detached values its delta kept.</summary>
    private void MaterializeSiteActors(DaggerfallSiteProfile destination, DaggerfallSiteRuntimeDelta? delta,
        bool restoreAuthoredAppearance = false)
    {
        _roster.MaterializeSite(destination, delta, restoreAuthoredAppearance);
        _roster.MaterializeStaticNpcs(destination);
        if (delta is null) return;
        _persistence.RestoreSiteDelta(delta);
        Projection.Appearance.SyncRestoredDefeat(_state.Actors);
    }

    /// <summary>Whether the current spatial session has an admitted exterior window.</summary>
    internal bool ExteriorResidencyInitialized => _exteriorResidency?.IsInitialized == true;

    /// <summary>
    /// Admits a constructed session's location artifact and, for an exterior, its terrain window.
    /// A saved unloaded location restores detached state without recreating native geometry until
    /// the player returns to the authored map pixel.
    /// </summary>
    internal void AdmitInitialResidency(DaggerfallExteriorCellResidencySave? savedExterior,
        DaggerfallExteriorLocationResidencySave? savedLocation = null)
    {
        if (ActiveProfile.Kind != DaggerfallWorldProfileKind.Exterior)
        {
            AdmitLocationArtifact(Projection.Inputs);
            _locationLoaded = true;
            return;
        }
        _locationLoaded = savedLocation?.Loaded != false;
        _locationCell = savedLocation?.Cell;
        _admittingInitialResidency = true;
        try
        {
            if (savedExterior is { } exterior)
                RestoreExteriorResidency(exterior);
            else
                UpdateExteriorResidency();
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
            HashSet<long> staticActorIds = _state.Npcs.All
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
            Projection.Suspend();
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

    /// <summary>Compatibility entry point retained for callers that only persisted terrain state.</summary>
    internal void AdmitInitialExterior(DaggerfallExteriorCellResidencySave? saved) => AdmitInitialResidency(saved);

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
        Vector3 translation = origin.LocalTranslation(site)
            + (Vector3.UnitY * ExteriorLocationFrameHeight(Projection.Inputs));
        return new(position.X - translation.X, position.Y - translation.Y, position.Z - translation.Z);
    }

    /// <summary>Quest location facts read the accepted player position in the current world frame.</summary>
    internal DaggerfallQuestLocationRead ReadQuestLocation()
    {
        if (ActiveProfile.Kind != DaggerfallWorldProfileKind.Exterior)
            return new(Projection.Inputs, _site.ActiveSite, _site.ActiveSite?.DungeonType);
        if (_state.PlayerControl.Position is not WorldPoint position) return new(Projection.Inputs, null, null);
        // Location artifacts use their normalized RMB frame, not the source terrain-tile origin.
        // Check the actual admitted profiles, including neighbors in the shared exterior window.
        var profiles = (_locationLoaded ? new[] { Projection.Inputs } : [])
            .Concat(_residentExteriorLocations.Values.Select(value => value.Profile));
        foreach (var profile in profiles)
        {
            if (profile.Site is not { } id || !_site.TryFind(id, out var location) || location.Exterior is not { } footprint) continue;
            var translation = ExteriorProfileTranslation(profile);
            if (InsideLocationFootprint(footprint, position.X - translation.X, position.Z - translation.Z))
                return new(Projection.Inputs, location, null);
        }
        return new(Projection.Inputs, null, null);
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
        return _state.PlayerControl.Position is WorldPoint position
            ? DaggerfallExteriorSessionOrigin.CellForLocalPosition(
                position,
                origin,
                new DaggerfallExteriorWorldBounds(_definitions.Terrain.Width, _definitions.Terrain.Height))
            : site;
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
        DaggerfallExteriorCellResidencyUpdate update = residency.Update(center, origin);
        _groundContainers.ReconcileExteriorResidency(residency.ResidentCells, ActiveOrigin(residency));
        ReconcileExteriorTerrainAppearance(residency);
        UpdateExteriorLocation(center);
        return update;
    }

    /// <summary>Admits the active exterior window with an explicit saved or rebased local origin.</summary>
    internal DaggerfallExteriorCellResidencyUpdate UpdateExteriorResidency(
        DaggerfallExteriorWorldOrigin origin)
    {
        RequireExteriorProfile();
        DaggerfallExteriorCellId center = _state.PlayerControl.Position is WorldPoint position
            ? DaggerfallExteriorSessionOrigin.CellForLocalPosition(
                position,
                origin,
                new DaggerfallExteriorWorldBounds(_definitions.Terrain.Width, _definitions.Terrain.Height))
            : ActiveExteriorCell();
        DaggerfallExteriorCellResidency residency = EnsureExteriorResidency();
        DaggerfallExteriorCellResidencyUpdate update = residency.Update(center, origin);
        _groundContainers.ReconcileExteriorResidency(residency.ResidentCells, ActiveOrigin(residency));
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

    private bool TryExteriorProfileCell(DaggerfallSiteProfile profile, out DaggerfallExteriorCellId cell)
    {
        cell = default;
        if (profile.ProfileKind != DaggerfallWorldProfileKind.Exterior || profile.Site is not { } siteId)
            return false;
        return ExteriorCellsBySite().TryGetValue(siteId, out cell);
    }

    private Vector3 ExteriorProfileTranslation(DaggerfallSiteProfile profile,
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
        return translation + (Vector3.UnitY * ExteriorLocationFrameHeight(profile));
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

    private float ExteriorLocationFrameHeight(DaggerfallSiteProfile profile)
    {
        return ExteriorLocationSampleHeight(profile);
    }

    private Vector3 ExteriorLocationPlacementTranslation(DaggerfallSiteProfile profile)
    {
        return Vector3.UnitY * ExteriorLocationSampleHeight(profile);
    }

    private float ExteriorLocationSampleHeight(DaggerfallSiteProfile profile)
    {
        DaggerfallTerrainSurface surface = ExteriorSurface(profile);
        return DaggerfallTerrainSurfaceBuilder.SampleWorldHeight(surface,
            DaggerfallTerrainSurfaceBuilder.LocationSampleCoordinate,
            DaggerfallTerrainSurfaceBuilder.LocationSampleCoordinate);
    }

    private DaggerfallTerrainSurface ExteriorSurface(DaggerfallSiteProfile profile)
    {
        if (!TryExteriorProfileCell(profile, out DaggerfallExteriorCellId cell))
            throw new InvalidOperationException($"Exterior profile '{profile.ProfileKey.LogicalId}' has no normalized map-pixel identity.");
        if (_exteriorSurfaceFactory is null)
            _ = EnsureExteriorResidency();
        return _exteriorSurfaceFactory!(cell);
    }

    private HashSet<long> ProfileActorIds(DaggerfallSiteProfile profile, DaggerfallSiteRuntimeDelta? delta = null)
    {
        HashSet<long> actorIds = profile.Project.Actors.Keys.ToHashSet();
        foreach (DaggerfallNpc npc in _state.Npcs.All.Where(npc => npc.Profile == profile.ProfileKey))
            actorIds.Add(npc.DurableId);
        if (delta is not null)
            foreach (DaggerfallDynamicActorSave actor in delta.DynamicActors)
                actorIds.Add(actor.EntityId);
        return actorIds;
    }

    private IReadOnlySet<long> ActiveDynamicActorIds()
    {
        HashSet<long> residentActorIds = [];
        foreach (ResidentExteriorLocation resident in _residentExteriorLocations.Values)
            residentActorIds.UnionWith(resident.ActorIds);
        return _roster.DynamicActorIdsExcluding(residentActorIds);
    }

    /// <summary>
    /// Merges live actors that escaped an already-detached source into that source's durable delta.
    /// Their source-frame saves are kept separately so a rejected transition can restore precisely
    /// the live entities it retired without materializing the rest of the detached profile.
    /// </summary>
    private (DaggerfallSiteRuntimeDelta Delta, DaggerfallSiteRuntimeDelta? LiveActors) CaptureDetachedLiveActors(
        DaggerfallSiteRuntimeDelta detachedSource, IReadOnlySet<long> actorIds, Vector3 actorFrameOffset)
    {
        ArgumentNullException.ThrowIfNull(detachedSource);
        ArgumentNullException.ThrowIfNull(actorIds);
        if (actorIds.Count == 0) return (detachedSource, null);

        List<DaggerfallSiteRuntimeDelta> captured = [];
        foreach (long id in actorIds.Order())
        {
            if (!_roster.Dynamic.TryGetValue(id, out DaggerfallActorId definition)) continue;
            captured.Add(_persistence.CaptureDynamicActorDelta(id, definition.Value, actorFrameOffset));
        }
        if (captured.Count == 0) return (detachedSource, null);

        HashSet<long> capturedIds = [.. captured.SelectMany(delta => delta.DynamicActors).Select(actor => actor.EntityId)];
        DaggerfallDynamicActorSave[] dynamicActors = [
            .. detachedSource.DynamicActors.Where(actor => !capturedIds.Contains(actor.EntityId)),
            .. captured.SelectMany(delta => delta.DynamicActors),
        ];
        DaggerfallActorInventorySave[] inventories = [
            .. detachedSource.ActorInventories.Where(inventory => !capturedIds.Contains(inventory.EntityId)),
            .. captured.SelectMany(delta => delta.ActorInventories),
        ];
        DaggerfallCorpseSave[] corpses = [
            .. detachedSource.Corpses.Where(corpse => !capturedIds.Contains(corpse.ActorId)),
            .. captured.SelectMany(delta => delta.Corpses),
        ];
        DaggerfallActiveEffectSave[] effects = [
            .. detachedSource.Effects.Where(effect => !capturedIds.Contains(effect.TargetId)),
            .. captured.SelectMany(delta => delta.Effects),
        ];
        DaggerfallSiteRuntimeDelta merged = detachedSource with
        {
            DynamicActors = [.. dynamicActors.OrderBy(actor => actor.EntityId)],
            ActorInventories = [.. inventories.OrderBy(inventory => inventory.EntityId)],
            Corpses = [.. corpses.OrderBy(corpse => corpse.ActorId)],
            Effects = effects,
        };
        DaggerfallSiteRuntimeDelta liveActors = new([], [.. captured.SelectMany(delta => delta.DynamicActors)],
            [.. captured.SelectMany(delta => delta.ActorInventories)], [.. captured.SelectMany(delta => delta.Corpses)], [],
            [.. captured.SelectMany(delta => delta.Effects)]);
        return (merged, liveActors);
    }

    /// <summary>Restores only actors that were live in a detached source before a transition failed.</summary>
    private void RestoreDetachedLiveActors(DaggerfallSiteRuntimeDelta liveActors, DaggerfallSiteProjection source)
    {
        foreach (DaggerfallDynamicActorSave actor in liveActors.DynamicActors.OrderBy(actor => actor.EntityId))
            _roster.MaterializeRetainedActor(actor, projectAppearance: true, projection: source);
        _persistence.RestoreSiteDelta(liveActors);
        source.Appearance.SyncRestoredDefeat(_state.Actors);
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
            projection = DaggerfallSiteProjection.Create(_engine, _state.Actors.Entities, _random, _tuning, _time.Calendar,
                profile, AudioFor(profile), _spatial, delta?.Doors, delta?.Motion, deferMotionCollisionAdmission: true,
                sessionPresentation: _sessionPresentation);
            projection.Appearance.DrawThroughActiveSnapshot();
            // The profile's authored geometry and actors use its own source frame. Rebase every
            // projection owner before admitting collision so doors, motion, portals, appearance
            // and lighting all share the same active exterior origin as the terrain cell.
            projection.Rebase(translation);
            AdmitLocationArtifact(profile, origin);
            artifactAdmitted = true;
            projection.ActivateMotionCollisionResidency();
            _roster.MaterializeSite(profile, delta, restoreAuthoredAppearance: true, projection: projection);
            _host.AdmitResidentCivilianAppearances(projection);
            _roster.MaterializeStaticNpcs(profile, projection);
            HashSet<long> actorIds = profile.Project.Actors.Keys.ToHashSet();
            foreach (DaggerfallNpc npc in _state.Npcs.All.Where(npc => npc.Profile == key))
                actorIds.Add(npc.DurableId);
            if (delta is not null) foreach (DaggerfallDynamicActorSave actor in delta.DynamicActors) actorIds.Add(actor.EntityId);
            if (delta is not null)
            {
                _persistence.RestoreSiteDelta(delta);
                projection.Appearance.SyncRestoredDefeat(_state.Actors);
                _deltas.Remove(key);
            }
            RebaseActors(actorIds, translation);
            _residentExteriorLocations.Add(key, new ResidentExteriorLocation(profile, projection, actorIds, FreeResidentLocationSlot()));
        }
        catch
        {
            if (projection is not null)
            {
                try { _roster.UnloadSite(profile, delta, projection); }
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

    private void RebaseActors(IReadOnlySet<long> actorIds, Vector3 translation)
    {
        foreach (long actorId in actorIds)
        {
            if (!_state.Actors.TryGet(actorId, out ActorState? actor)) continue;
            ActorPose pose = actor.Pose;
            actor.ApplyPose(new ActorPose(DaggerfallExteriorSessionOrigin.Shift(pose.Position, translation), pose.HeadingYawRadians));
        }
    }

    private void RetireResidentExteriorLocation(DaggerfallWorldProfileKey key, bool capture)
    {
        if (!_residentExteriorLocations.Remove(key, out ResidentExteriorLocation? resident)) return;
        DaggerfallSiteRuntimeDelta? delta = null;
        try
        {
            _retiredResidentLocations.Add(resident.Projection.Appearance.TakeDrawnLocation());
            if (capture)
            {
                Vector3 profileFrameOffset = ExteriorProfileTranslation(resident.Profile);
                delta = _persistence.CaptureSiteDelta(resident.Profile, resident.Projection.Doors,
                    resident.Projection.Motion, _roster.Dynamic, resident.ActorIds, profileFrameOffset);
                _deltas[key] = delta;
            }
            resident.Projection.Suspend();
            _roster.UnloadSite(resident.Profile, delta, resident.Projection, resident.ActorIds);
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

    /// <summary>
    /// Completes the active appearance's snapshot with the exterior window: every resident neighbour's meshes,
    /// doors, gates and action models under its own location slot, then the terrain and its nature batches.
    /// </summary>
    private void AppendExteriorFacts(List<AppearanceFact> facts)
    {
        foreach (ResidentExteriorLocation resident in _residentExteriorLocations.Values.OrderBy(value => value.Slot))
            resident.Projection.Appearance.AppendLocationFacts(facts, resident.Slot);
        _exteriorTerrainAppearance?.AppendFacts(facts);
    }

    /// <summary>A snapshot was accepted: what the window and the retired neighbours no longer draw is released.</summary>
    private void CompleteExteriorSnapshot()
    {
        List<Exception>? failures = null;
        try { _exteriorTerrainAppearance?.CompleteAcceptedSnapshot(); }
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
        DaggerfallSiteRuntimeDelta delta = _persistence.CaptureSiteDelta(profile, Projection.Doors,
            Projection.Motion, _roster.Dynamic, dynamicActorIds, ExteriorProfileTranslation(profile));
        _deltas[ActiveProfile] = delta;
        _roster.UnloadSite(profile, delta);
        Projection.Suspend();
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
        Projection.Resume();
        _deltas.TryGetValue(ActiveProfile, out DaggerfallSiteRuntimeDelta? delta);
        _roster.MaterializeSite(profile, delta, restoreAuthoredAppearance: true);
        RebaseActors(ProfileActorIds(profile, delta), ExteriorProfileTranslation(profile));
        if (delta is not null)
        {
            _persistence.RestoreSiteDelta(delta);
            Projection.Appearance.SyncRestoredDefeat(_state.Actors);
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
            ActionTriggers.RebaseActive(delta);
            ActionTriggers.RebaseRestoredPlayer(_state.PlayerControl, _state.Actors.Player.Actor.Entity);
        }
        DaggerfallExteriorCellResidency residency = EnsureExteriorResidency();
        DaggerfallExteriorCellResidencyUpdate update = residency.Restore(save);
        _groundContainers.ReconcileExteriorResidency(residency.ResidentCells, ActiveOrigin(residency));
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
            ? Vector3.UnitY * ExteriorLocationFrameHeight(Projection.Inputs)
            : Vector3.Zero;

    internal WorldPoint ProfileToLocal(WorldPoint position) =>
        DaggerfallExteriorSessionOrigin.Shift(position, LocalCompensation + ActiveExteriorFrameOffset());

    internal Vector3 LocalToProfile(Vector3 position) =>
        position - LocalCompensation - ActiveExteriorFrameOffset();

    /// <summary>Rebase at a terrain-cell boundary, inside the existing admitted update.</summary>
    /// <summary>Moves the exterior origin under the player once it strays a cell away; true when it moved.</summary>
    internal bool RebaseExteriorIfNeeded()
    {
        if (ActiveProfile.Kind != DaggerfallWorldProfileKind.Exterior
            || _state.PlayerControl.Position is not WorldPoint position) return false;
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
        if (_state.PlayerControl.Position is not WorldPoint position)
            throw new InvalidOperationException("An origin commit requires a player pose; otherwise world positions would be lost.");
        _ = DaggerfallExteriorSessionOrigin.Shift(position, Vector3.Zero);
        if (_exteriorResidency is not { IsInitialized: true })
            throw new InvalidOperationException("An origin commit requires admitted exterior residency; otherwise world coordinates would detach from their cell.");
        EntityStore store = _state.Actors.Store;
        EntityId[] roots = [.. _state.Actors.All.Select(actor => actor.Actor.Entity),
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
            Vector3 shifted = DaggerfallExteriorSessionOrigin.Shift(WorldPoint.From(transform.Translation), delta).ToVector();
            store.Set(root, EngineComponentTypes.Transform, transform with { Translation = shifted });
        }
    }

    /// <summary>
    /// Removes product-owned exterior colliders before Spatial ReplaceContentArtifact replaces the
    /// complete static collision set. A null result means this session had no exterior window.
    /// </summary>
    internal DaggerfallExteriorCellResidencyUpdate? ClearExteriorResidency()
    {
        DaggerfallExteriorCellResidencyUpdate? update = _exteriorResidency is { IsInitialized: true } residency
            ? residency.Clear()
            : null;
        ReleaseExteriorWaterTriggers();
        _exteriorTerrainAppearance?.Clear();
        _exteriorSurfaceCache.Clear();
        return update;
    }

    /// <summary>Releases all source-water trigger references before SpatialSession disposal or a profile switch.</summary>
    internal void ReleaseExteriorWaterTriggers()
    {
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
            ActionTriggers.RebaseActive(localDelta);
            _groundContainers.RebaseActive(localDelta);
            _host.RebaseTransientWorld(localDelta);
            // RestoreTriggers replaces the complete native active-trigger set. Release the
            // environment-owned IDs before restoring the dungeon baseline so the later terrain
            // reconciliation can register and activate them again instead of leaving the local
            // SpatialMovementSystem registry ahead of Engine's inactive set.
            ReleaseExteriorWaterTriggers();
            ActionTriggers.RebaseRestoredPlayer(_state.PlayerControl, _state.Actors.Player.Actor.Entity);

            residency.AdoptRebasedOrigin(ActiveOrigin(residency));
        }

        // The camera is Engine-owned but its descriptor is derived from the product player pose.
        // Refresh it after shifting that pose, before the caller publishes the next presentation
        // snapshot.
        _camera.Update(_state.PlayerControl);
        DaggerfallExteriorCellResidencyUpdate update = residency.Update(CurrentExteriorCell(), ActiveOrigin(residency));
        _groundContainers.ReconcileExteriorResidency(residency.ResidentCells, ActiveOrigin(residency));
        ReconcileExteriorTerrainAppearance(residency);
        ReconcileResidentExteriorLocations(residency);
        return update;
    }

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
        _exteriorTerrainAppearance?.Dispose();
        _exteriorTerrainAppearance = null;
        _exteriorEnvironment = null;
        _exteriorSurfaceCache.Clear();
    }

    private DaggerfallExteriorCellResidency EnsureExteriorResidency()
    {
        ObjectDisposedException.ThrowIf(_retired, this);
        if (_exteriorResidency is not null) return _exteriorResidency;
        Dictionary<DaggerfallExteriorCellId, DaggerfallSiteExterior> exteriors = ExteriorLocations();
        Func<DaggerfallExteriorCellId, DaggerfallTerrainSurface> surfaceFactory = cell =>
        {
            if (_exteriorSurfaceCache.TryGetValue(cell, out DaggerfallTerrainSurface? cached)) return cached;
            DaggerfallTerrainSurface surface = DaggerfallTerrainSurfaceBuilder.Build(_definitions.Terrain, cell.X, cell.Y);
            if (exteriors.TryGetValue(cell, out DaggerfallSiteExterior? exterior))
                surface = DaggerfallTerrainSurfaceBuilder.ApplyLocationFlattening(surface,
                    new DaggerfallTerrainLocationFlattening(exterior.MinX, exterior.MaxX, exterior.MinY, exterior.MaxY));
            _exteriorSurfaceCache.Add(cell, surface);
            return surface;
        };
        _exteriorSurfaceFactory = surfaceFactory;
        return _exteriorResidency = new DaggerfallExteriorCellResidency(
            _engine.Spatial,
            _spatial.Session,
            new DaggerfallExteriorWorldBounds(_definitions.Terrain.Width, _definitions.Terrain.Height),
            surfaceFactory);
    }

    private void ReconcileExteriorTerrainAppearance(DaggerfallExteriorCellResidency residency)
    {
        if (!residency.IsInitialized) return;
        Func<DaggerfallExteriorCellId, DaggerfallTerrainSurface> surfaceFactory = _exteriorSurfaceFactory
            ?? throw new InvalidOperationException("Exterior terrain surface factory was not initialized.");
        Dictionary<DaggerfallExteriorCellId, DaggerfallSiteExterior> exteriors = ExteriorLocations();
        DaggerfallExteriorEnvironment environment = _exteriorEnvironment ??= new(_random);
        environment.Reconcile(residency.ResidentCells, ActiveOrigin(residency), surfaceFactory, exteriors, _definitions.Grids);
        DaggerfallExteriorTerrainAppearance appearance =
            _exteriorTerrainAppearance ??= new DaggerfallExteriorTerrainAppearance(_engine.Graphics, Projection.Inputs);
        appearance.ConfigureProfile(Projection.Inputs);
        Projection.Appearance.SetSnapshotSupplement(AppendExteriorFacts, CompleteExteriorSnapshot);
        appearance.Reconcile(residency.ResidentCells, ActiveOrigin(residency), surfaceFactory, environment);
        ReconcileExteriorWaterTriggers(environment.CharacterWaterVolumes(ActiveOrigin(residency)));
        TrimExteriorSurfaceCache(residency.ResidentCells);
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

    private void TrimExteriorSurfaceCache(IReadOnlyCollection<DaggerfallExteriorCellId> residentCells)
    {
        HashSet<DaggerfallExteriorCellId> retained = residentCells.ToHashSet();
        foreach (DaggerfallExteriorCellId cell in _exteriorSurfaceCache.Keys
            .Where(cell => !retained.Contains(cell)).ToArray())
            _exteriorSurfaceCache.Remove(cell);
    }

    private void ReconcileExteriorWaterTriggers(IReadOnlyList<CharacterWaterVolume> desired)
    {
        ArgumentNullException.ThrowIfNull(desired);
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
    }

    /// <summary>Applies the calendar/weather season to the resident terrain environment.</summary>
    internal void SetExteriorSeason(DaggerfallExteriorSeason season)
    {
        if (ActiveProfile.Kind != DaggerfallWorldProfileKind.Exterior) return;
        if (_exteriorEnvironment is null)
        {
            _exteriorEnvironment = new(_random, season);
            return;
        }
        if (_exteriorEnvironment.Season == season) return;
        // SetSeason invalidates the retained source facts before the next appearance admission.
        // Retire their trigger references first so a failed rebuild cannot leave water from the
        // previous season active against an uninitialized environment.
        ReleaseExteriorWaterTriggers();
        _exteriorEnvironment.SetSeason(season);
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
        if (_state.PlayerControl.Position is WorldPoint position)
        {
            _state.PlayerControl.MoveTo(
                DaggerfallExteriorSessionOrigin.Shift(position, localDelta).ToVector());
        }
        // The support anchor, tether anchor and fall/peak heights are local-frame
        // values too; left alone, the next step on a support snaps the player back.
        _state.PlayerControl.Motion = _state.PlayerControl.Motion.Rebased(localDelta);
    }
}
