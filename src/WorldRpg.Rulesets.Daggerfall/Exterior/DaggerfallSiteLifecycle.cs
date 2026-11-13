using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Loot;
using WorldRpg.Rulesets.Daggerfall.Modules.Behavior;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// The session consequences a site transition reaches that the site lifecycle does not own, beside those the exterior
/// reaches. The lifecycle calls each at one fixed point of the transition; the session answers.
/// </summary>
internal interface IDaggerfallSiteTransitionHost : IDaggerfallExteriorHost
{
    /// <summary>The destination became the active site; site-scoped session presentation moves on.</summary>
    void EnteredSite();

    /// <summary>
    /// An exterior arrival has its terrain window admitted and the player stood in it; the location's wandering population
    /// is admitted around them now that both are placed in the world.
    /// </summary>
    void ReconcileArrivalPopulation();

    /// <summary>The departing projection is about to be released; retire what it was still timing.</summary>
    void RetireDepartingProjection(DaggerfallSiteProjection source);
}

/// <summary>
/// The one owner of where the session is projected: the live site projection, the admitted site catalog, the active and
/// return profiles, the inactive sites' detached deltas, the site-bound bank provider and the action-trigger runtime.
/// Every site transition and relocation goes through here, and a failed transition restores the source. The cells
/// streamed around an exterior site and the towns on them are <see cref="Exterior"/>'s.
/// </summary>
internal sealed class DaggerfallSiteLifecycle : IDaggerfallExteriorSite
{
    private readonly IEngineContext _engine;
    private readonly DaggerfallState _state;
    private readonly DaggerfallDefinitions _definitions;
    private readonly DaggerfallTuning _tuning;
    private readonly IRandomService _random;
    private readonly DaggerfallWorldTime _time;
    private readonly DaggerfallSiteContext _site;
    private readonly SpatialMovementSystem _spatial;
    private readonly DaggerfallSiteAudioBundles? _audioBundles;
    private readonly DaggerfallActorRoster _roster;
    private readonly DaggerSessionPersistence _persistence;
    private readonly DaggerfallGroundContainers _groundContainers;
    private readonly DaggerfallEnemyBehaviorModule _enemyBehavior;
    private readonly Func<DaggerfallDungeonActionDefinition, DaggerfallDungeonActionExecution?> _executeFamilyAction;
    private readonly IDaggerfallSiteTransitionHost _host;
    private readonly Dictionary<DaggerfallWorldProfileKey, DaggerfallSiteRuntimeDelta> _deltas = [];
    private readonly DaggerfallExteriorWorld _exterior;

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
        _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
        _tuning = tuning ?? throw new ArgumentNullException(nameof(tuning));
        _random = engine.Random;
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _site = site ?? throw new ArgumentNullException(nameof(site));
        _spatial = spatial ?? throw new ArgumentNullException(nameof(spatial));
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
        _exterior = new DaggerfallExteriorWorld(engine, state, definitions, tuning, time, site, spatial, camera, audioBundles,
            roster, persistence, groundContainers, this, host, _deltas, sessionPresentation);
    }

    /// <summary>The cells streamed around the player and the towns placed on them.</summary>
    internal DaggerfallExteriorWorld Exterior => _exterior;

    /// <summary>The active site's live projection: doors, motion, appearance, lighting and portals.</summary>
    internal DaggerfallSiteProjection Projection { get; private set; }

    /// <summary>The action-trigger runtime spanning every admitted profile.</summary>
    internal DaggerfallDungeonActionTriggerRuntime ActionTriggers { get; }

    /// <summary>The admitted site catalog, once composition has admitted it.</summary>
    internal DaggerfallSiteProfiles? Profiles { get; private set; }

    internal DaggerfallWorldProfileKey ActiveProfile { get; private set; }

    /// <summary>The profile a return portal leads back to, when the player entered from one.</summary>
    internal DaggerfallWorldProfileKey? ReturnProfile { get; private set; }

    DaggerfallWorldProfileKey IDaggerfallExteriorSite.ActiveProfile => ActiveProfile;
    DaggerfallSiteProjection IDaggerfallExteriorSite.Projection => Projection;
    DaggerfallSiteProfiles? IDaggerfallExteriorSite.Profiles => Profiles;

    void IDaggerfallExteriorSite.RebaseActionTriggers(Vector3 delta) => ActionTriggers.RebaseActive(delta);

    void IDaggerfallExteriorSite.RebaseRestoredPlayerTriggers() =>
        ActionTriggers.RebaseRestoredPlayer(_state.PlayerControl, _state.Actors.Player.Actor.Entity);

    /// <summary>All live appearance owners share the session's admitted animation and retirement boundaries.</summary>
    internal IEnumerable<DaggerfallSiteAppearance> Appearances
    {
        get
        {
            yield return Projection.Appearance;
            foreach (DaggerfallSiteAppearance resident in _exterior.ResidentAppearances)
                yield return resident;
        }
    }

    /// <summary>Inactive sites' detached actor, door, effect and motion state, by profile.</summary>
    internal IReadOnlyDictionary<DaggerfallWorldProfileKey, DaggerfallSiteRuntimeDelta> Deltas => _deltas;

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
            actor.ApplyPose(new ActorPose(_exterior.ProfileToLocal(anchor.Position), anchor.YawRadians));
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
            _host.RelocatePlayer(_exterior.ProfileToLocal(pose.Position), pose.YawRadians, pose.PitchRadians);
            if (profile.Kind == DaggerfallWorldProfileKind.Exterior) _exterior.FollowRecall();
            _exterior.StandArrivingPlayer();
            return true;
        }
        return TryTransitionTo(profile, pose, useReturnDestination: false);
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

    internal bool TryEnterBuilding(DaggerfallWorldProfileKey destination, DaggerfallSiteAnchor arrival) =>
        TryTransitionTo(destination, arrival, useReturnDestination: false);

    /// <summary>Walking into a streamed town changes its gameplay owner without relocating the player.</summary>
    internal bool ActivateLocationAtPlayer()
    {
        if (_exterior.LocationAtPlayer() is not { } location || location.Id == ActiveProfile.Site) return false;
        return TryTransitionTo(DaggerfallWorldProfileIds.Exterior(location.Id), null,
            useReturnDestination: false, walking: true);
    }

    private bool TryTransitionTo(DaggerfallWorldProfileKey destination, DaggerfallSiteAnchor? arrival, bool useReturnDestination,
        bool walking = false)
    {
        DaggerfallSiteProfile target = RequireProfiles().Require(destination);
        PlayerControlState player = _state.PlayerControl;
        _ = player.Position ?? throw new InvalidOperationException("A site transition requires a player position.");
        // Detached site state and return poses use the profile's authored frame. Restore that
        // frame before capture and before replacing the native artifact (which retains its origin).
        if (ActiveProfile.Kind == DaggerfallWorldProfileKind.Exterior)
            _exterior.NormalizeExteriorOrigin();
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
        bool sourceLocationLoaded = _exterior.ActiveLocationLoaded;
        DaggerfallExteriorCellId? sourceLocationCell = _exterior.LocationCell;
        IReadOnlySet<long> sourceDynamicActorIds = _exterior.ActiveDynamicActorIds();
        Vector3 sourceFrameOffset = sourceProfile.Kind == DaggerfallWorldProfileKind.Exterior
            ? _exterior.ExteriorProfileTranslation(source.Inputs)
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
        DaggerfallExteriorCellResidencySave? sourceExterior = _exterior.CaptureExteriorResidency();
        DaggerfallExteriorWorldOrigin? destinationOrigin = null;
        Vector3 destinationFrameOffset = Vector3.Zero;
        if (target.ProfileKind == DaggerfallWorldProfileKind.Exterior)
        {
            if (!_exterior.TryExteriorProfileCell(target, out DaggerfallExteriorCellId destinationCell))
                throw new InvalidOperationException($"Exterior profile '{target.ProfileKey.LogicalId}' has no normalized map-pixel identity.");
            // Travel establishes the destination cell as the local origin. Keeping
            // the source map origin here would add a far-away map translation to the authored arrival
            // pose in a float Vector3, irreversibly dropping sub-cell precision before the destination
            // profile can read it back through ExteriorSitePosition. Walking preserves the admitted
            // window's frame and the player's accepted movement instead of landing at an anchor.
            destinationOrigin = walking && sourceExterior is { } walkingExterior
                ? walkingExterior.WorldOrigin : DaggerfallExteriorWorldOrigin.At(destinationCell);
            destinationFrameOffset = _exterior.ExteriorProfileTranslation(target, destinationOrigin.Value);
        }
        _deltas.TryGetValue(destination, out DaggerfallSiteRuntimeDelta? destinationDelta);
        if (_exterior.CaptureResidentLocation(destination) is { } residentDelta) destinationDelta = residentDelta;
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
            // A resident destination already owns these door/entity identities. Its state was
            // captured above; retire that owner before constructing the active projection.
            if (_exterior.HoldsResidentLocation(destination))
            {
                _exterior.RetireResidentExteriorLocation(destination, capture: false);
                adjacentLocationsRetired = true;
            }
            candidate = DaggerfallSiteProjection.Create(_engine, _state.Actors.Entities, _random, _tuning, _time.Calendar,
                target, AudioFor(target), _spatial, _host.ConfigureSiteAppearance, destinationDelta?.Doors, destinationDelta?.Motion,
                deferMotionCollisionAdmission: true, sessionPresentation: _sessionPresentation, definitions: _definitions);
            if (target.ProfileKind == DaggerfallWorldProfileKind.Exterior)
                candidate.Rebase(destinationFrameOffset);
            if (sourceExterior is not null)
            {
                _exterior.RetireResidentExteriorLocations(capture: true);
                adjacentLocationsRetired = true;
                _exterior.ClearExteriorResidency();
                exteriorCleared = true;
            }
            _exterior.ApplyLocationResidency(target, destinationOrigin,
                sourceLocationLoaded ? DaggerfallExteriorWorld.LocationPlacementIds(source.Inputs) : [],
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
            else source.Lighting.Suspend();
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
                _exterior.RebaseActors(_exterior.ProfileActorIds(target, destinationDelta), destinationFrameOffset);
            _groundContainers.SwitchProfile(destination);
            groundProfileSwitched = true;
            // Activation depends only on the admitted candidate projection.  Prepare it before
            // mutating player or site state so an Engine service rejection has nothing semantic
            // to roll back.
            _host.RebuildActivation();
            if (walking)
            {
                _site.Enter(destination.Site);
                _site.ClearReturnDestination();
            }
            else if (returnDestination is { } returned)
            {
                _site.Leave();
                playerRelocated = true;
                // The player entered facing the doorway; as the donor's exit leaves them outside it, still
                // facing the way they walked out, they stand where they entered turned away from the door.
                _host.RelocatePlayer(DaggerfallExteriorSessionOrigin.Shift(returned.Pose.Position, destinationFrameOffset),
                    MathF.IEEERemainder(returned.Pose.YawRadians + MathF.PI, MathF.Tau), returned.Pose.PitchRadians);
            }
            else
            {
                // A return belongs to the departed profile, independent of which town anchored
                // the terrain window before the player walked here.
                _site.Enter(destination.Site, DaggerfallExteriorSessionOrigin.Shift(sourcePosition, -sourceFrameOffset),
                    player.YawRadians, player.PitchRadians);
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
                _exterior.RecordLocation(true, _exterior.ActiveExteriorCell());
                // The player pose was translated from the captured destination origin above. The
                // residency coordinator was cleared while replacing the source artifact, so the
                // no-argument overload would derive a new At(site) frame and interpret that pose
                // in the wrong map cell. Re-admit the target window in the same canonical frame.
                _exterior.UpdateExteriorResidency(destinationOrigin
                    ?? throw new InvalidOperationException("An exterior transition did not resolve its destination origin."));
            }
            else _exterior.RecordLocation(true, null);
            // A returning player resumes the standing pose they left; an arrival lands on its anchor's floor.
            if (returnDestination is null && !walking) _exterior.StandArrivingPlayer();
            if (destination.Kind == DaggerfallWorldProfileKind.Exterior) _host.ReconcileArrivalPopulation();
            if (destination.Kind != DaggerfallWorldProfileKind.Exterior)
                _state.Transport.ForceFootOnInteriorTransition();
            ReturnProfile = !walking && returnDestination is null ? sourceProfile : null;
            _enemyBehavior.ClearPerceptionMemory();
            if (target.DungeonMap is { } destinationMap)
            {
                if (!_state.DungeonDiscoveries.TryGetValue(destination, out DaggerfallDungeonDiscovery? discovery))
                    _state.DungeonDiscoveries.Add(destination, discovery = new DaggerfallDungeonDiscovery(destination, destinationMap));
                discovery.BeginVisit();
            }
            EnsureDungeonActionGraph(destination, target);
            ActionTriggers.ActivateProfile(destination);
            if (DaggerfallWorldProfileIds.TryParse(destination.LogicalId, out _, out var visitedBuilding) && visitedBuilding is { } building)
                _site.DiscoverBuilding(destination.Site, building);
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
                        _exterior.ApplyLocationResidency(source.Inputs, sourceExterior is { } saved ? saved.WorldOrigin : null,
                            DaggerfallExteriorWorld.LocationPlacementIds(target), source.Inputs.Geometry.NavigationGridId);
                    else
                        _exterior.ApplyLocationResidency(null, null, DaggerfallExteriorWorld.LocationPlacementIds(target), source.Inputs.Geometry.NavigationGridId);
                }
                catch (Exception rollbackFailure) { failures.Add(rollbackFailure); }
                // Whole-content replacement removes every incremental resident collider. Drop the
                // destination coordinator's remembered set before restoring the source window;
                // otherwise overlapping cells would be skipped as already admitted.
                try { _exterior.ClearExteriorResidency(); }
                catch (Exception rollbackFailure) { failures.Add(rollbackFailure); }
            }
            if (sourceProjectionSuspended)
            {
                try { source.Resume(); }
                catch (Exception rollbackFailure) { failures.Add(rollbackFailure); }
            }
            else if (!sourceLocationLoaded)
            {
                try { source.Lighting.RestoreSky(); }
                catch (Exception rollbackFailure) { failures.Add(rollbackFailure); }
            }
            try { source.Lighting.ApplyBackground(); }
            catch (Exception rollbackFailure) { failures.Add(rollbackFailure); }
            try { _site.RestoreCheckpoint(sourceSite); }
            catch (Exception rollbackFailure) { failures.Add(rollbackFailure); }
            _deltas.Clear();
            foreach ((DaggerfallWorldProfileKey key, DaggerfallSiteRuntimeDelta delta) in sourceDeltas) _deltas.Add(key, delta);
            ActiveProfile = sourceProfile;
            _exterior.RecordLocation(sourceLocationLoaded, sourceLocationCell);
            ReturnProfile = sourceReturnProfile;
            BankProvider = sourceBankProvider;
            if (sourceExterior is { } priorExterior)
            {
                try
                {
                    _exterior.RestoreAfterFailedCrossing(priorExterior, exteriorCleared, sourceLocationLoaded,
                        sourceLocationLoaded && sourceProfile.Kind == DaggerfallWorldProfileKind.Exterior ? source.Inputs : null,
                        sourceDelta, adjacentLocationsRetired);
                }
                catch (Exception rollbackFailure) { failures.Add(rollbackFailure); }
            }
            else if (_exterior.ExteriorResidencyInitialized)
            {
                try { _exterior.ClearExteriorResidency(); }
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

    /// <summary>Quest location facts read the accepted player position in the current world frame.</summary>
    internal DaggerfallQuestLocationRead ReadQuestLocation()
    {
        if (ActiveProfile.Kind != DaggerfallWorldProfileKind.Exterior)
            return new(Projection.Inputs, _site.ActiveSite, _site.ActiveSite?.DungeonType);
        return new(Projection.Inputs, _exterior.LocationAtPlayer(), null);
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
}
