using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Behavior;
using WorldRpg.Rulesets.Daggerfall.Modules.Loot;
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
    private Func<DaggerfallExteriorCellId, DaggerfallTerrainSurface>? _exteriorSurfaceFactory;
    private bool _retired;

    internal DaggerfallSiteLifecycle(IEngineContext engine, DaggerfallState state, DaggerfallDefinitions definitions,
        DaggerfallTuning tuning, DaggerfallWorldTime time, DaggerfallSiteContext site, SpatialMovementSystem spatial,
        FirstPersonCameraSystem camera, DaggerfallSiteAudioBundles? audioBundles, DaggerfallActorRoster roster,
        DaggerSessionPersistence persistence, DaggerfallGroundContainers groundContainers, DaggerfallEnemyBehaviorModule enemyBehavior,
        Func<DaggerfallDungeonActionDefinition, DaggerfallDungeonActionExecution?> executeFamilyAction, IDaggerfallSiteTransitionHost host,
        DaggerfallSiteProjection projection, DaggerfallDungeonActionTriggerRuntime actionTriggers, DaggerfallSiteProfiles? profiles,
        DaggerfallWorldProfileKey activeProfile, DaggerfallWorldProfileKey? returnProfile)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _state = state ?? throw new ArgumentNullException(nameof(state));
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
    /// Every admitted action profile, the active one included, in the stable order action graph
    /// save identity uses.
    /// </summary>
    internal static IReadOnlyList<(DaggerfallWorldProfileKey Key, DaggerfallSiteProfile Inputs)> ActionProfiles(
        DaggerfallSiteProfile active,
        DaggerfallSiteProfiles? profiles)
    {
        ArgumentNullException.ThrowIfNull(active);
        if (profiles is null) return [(active.ProfileKey, active)];

        Dictionary<DaggerfallWorldProfileKey, DaggerfallSiteProfile> admitted = [];
        foreach (DaggerfallWorldProfileKey key in profiles.Keys)
            admitted.Add(key, profiles.Require(key));
        admitted.TryAdd(active.ProfileKey, active);
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
        _ = profiles.Require(ActiveProfile);
        if (_audioBundles is not null)
            foreach (DaggerfallWorldProfileKey key in profiles.Keys) _ = _audioBundles.Require(key);
        Profiles = profiles;
        foreach (DaggerfallWorldProfileKey key in profiles.Keys)
            EnsureDungeonActionGraph(key, profiles.Require(key));
    }

    internal DaggerfallAudioBundle? AudioFor(DaggerfallSiteProfile inputs) => _audioBundles?.Require(inputs.ProfileKey);

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
        if (destination.Profile == ActiveProfile)
        {
            _host.RelocatePlayer(ProfileToLocal(anchor.Position), anchor.YawRadians, anchor.PitchRadians);
            return true;
        }
        return TryTransitionTo(destination.Profile, anchor, useReturnDestination: false);
    }

    /// <summary>World relocation has no doorway back; its caller may own a different return workflow.</summary>
    internal void ClearReturnDestination()
    {
        ReturnProfile = null;
        _site.ClearReturnDestination();
    }

    /// <summary>Attempts one real site transition; failed destination admission leaves the source projection live.</summary>
    internal bool TryTransitionTo(DaggerfallWorldProfileKey destination) => TryTransitionTo(destination, null, useReturnDestination: true);

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
        Dictionary<DaggerfallWorldProfileKey, DaggerfallSiteRuntimeDelta> sourceDeltas = new(_deltas);
        DaggerfallSiteRuntimeDelta sourceDelta = _persistence.CaptureSiteDelta(source.Inputs, source.Doors,
            source.Motion, _roster.Dynamic);
        DaggerfallExteriorCellResidencySave? sourceExterior = CaptureExteriorResidency();
        _deltas.TryGetValue(destination, out DaggerfallSiteRuntimeDelta? destinationDelta);
        DaggerfallSiteProjection? candidate = null;
        bool spatialReplaced = false;
        bool sourceActorsUnloaded = false;
        bool groundProfileSwitched = false;
        bool playerRelocated = false;
        bool exteriorCleared = false;
        try
        {
            candidate = DaggerfallSiteProjection.Create(_engine, _state.Actors.Entities, _random, _tuning, _time.Calendar,
                target, AudioFor(target), _spatial, destinationDelta?.Doors, destinationDelta?.Motion,
                deferMotionCollisionAdmission: true);
            if (sourceExterior is not null)
            {
                ClearExteriorResidency();
                exteriorCleared = true;
            }
            _spatial.ReplaceContent(target.SpatialArtifact);
            spatialReplaced = true;
            candidate.ActivateMotionCollisionResidency();
            _roster.UnloadSite(source.Inputs, sourceDelta);
            sourceActorsUnloaded = true;
            Projection = candidate;
            candidate = null;
            MaterializeSiteActors(target, destinationDelta);
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
                    _host.RelocatePlayer(selected.Position, selected.YawRadians, selected.PitchRadians);
                else
                    _host.RelocatePlayer(target.Project.PlayerPosition ?? sourcePosition, player.YawRadians, player.PitchRadians);
            }
            _deltas[sourceProfile] = sourceDelta;
            _deltas.Remove(destination);
            ActiveProfile = destination;
            // Entering a place retires the previous world's loop: the donor gives a dungeon a new song
            // per location rather than carrying the last one through the door.
            _host.EnteredSite();
            BankProvider = null;
            if (destination.Kind == DaggerfallWorldProfileKind.Exterior)
                UpdateExteriorResidency();
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
                try { MaterializeSiteActors(source.Inputs, sourceDelta); }
                catch (Exception restoreFailure) { failures.Add(restoreFailure); }
            }
            if (spatialReplaced)
            {
                try { _spatial.ReplaceContent(source.Inputs.SpatialArtifact); }
                catch (Exception rollbackFailure) { failures.Add(rollbackFailure); }
                // Whole-content replacement removes every incremental resident collider. Drop the
                // destination coordinator's remembered set before restoring the source window;
                // otherwise overlapping cells would be skipped as already admitted.
                try { ClearExteriorResidency(); }
                catch (Exception rollbackFailure) { failures.Add(rollbackFailure); }
                try { source.RebuildMotionCollisionResidency(); }
                catch (Exception rollbackFailure) { failures.Add(rollbackFailure); }
            }
            try { source.Lighting.ApplyBackground(); }
            catch (Exception rollbackFailure) { failures.Add(rollbackFailure); }
            try { _site.RestoreCheckpoint(sourceSite); }
            catch (Exception rollbackFailure) { failures.Add(rollbackFailure); }
            _deltas.Clear();
            foreach ((DaggerfallWorldProfileKey key, DaggerfallSiteRuntimeDelta delta) in sourceDeltas) _deltas.Add(key, delta);
            ActiveProfile = sourceProfile;
            ReturnProfile = sourceReturnProfile;
            BankProvider = sourceBankProvider;
            if (sourceExterior is { } priorExterior && exteriorCleared)
            {
                try { RestoreExteriorResidency(priorExterior); }
                catch (Exception rollbackFailure) { failures.Add(rollbackFailure); }
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
            DaggerfallDungeonMapMarker? usedPortal = sourceMap.Markers
                .Where(marker => marker.Kind == DaggerfallDungeonMapMarkerKind.Portal
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
    private void MaterializeSiteActors(DaggerfallSiteProfile destination, DaggerfallSiteRuntimeDelta? delta)
    {
        _roster.MaterializeSite(destination, delta);
        if (delta is null) return;
        _persistence.RestoreSiteDelta(delta);
        Projection.Appearance.SyncRestoredDefeat(_state.Actors);
    }

    /// <summary>Whether the current spatial session has an admitted exterior window.</summary>
    internal bool ExteriorResidencyInitialized => _exteriorResidency?.IsInitialized == true;

    /// <summary>
    /// Admits a constructed session's exterior window: the saved window when the save carries one,
    /// otherwise the window around the player. Interiors and dungeons own no window.
    /// </summary>
    internal void AdmitInitialExterior(DaggerfallExteriorCellResidencySave? saved)
    {
        if (ActiveProfile.Kind != DaggerfallWorldProfileKind.Exterior) return;
        if (saved is { } exterior)
            RestoreExteriorResidency(exterior);
        else
            UpdateExteriorResidency();
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
            ? residency.Origin : DaggerfallExteriorWorldOrigin.At(site);
        Vector3 translation = origin.LocalTranslation(site);
        return new(position.X - translation.X, position.Y - translation.Y, position.Z - translation.Z);
    }

    internal DaggerfallExteriorCellId CurrentExteriorCell()
    {
        DaggerfallExteriorCellId site = ActiveExteriorCell();
        DaggerfallExteriorWorldOrigin origin = _exteriorResidency is { IsInitialized: true } residency
            ? residency.Origin
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
            ? residency.Origin
            : DaggerfallExteriorWorldOrigin.At(ActiveExteriorCell());
        DaggerfallExteriorCellResidencyUpdate update = residency.Update(center, origin);
        ReconcileExteriorTerrainAppearance(residency);
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
        ReconcileExteriorTerrainAppearance(residency);
        return update;
    }

    /// <summary>Captures only durable exterior identity/origin facts; Engine handles stay native.</summary>
    internal DaggerfallExteriorCellResidencySave? CaptureExteriorResidency() =>
        _exteriorResidency is { IsInitialized: true }
            ? _exteriorResidency.Capture()
            : null;

    /// <summary>Restores the saved exterior window after the owning SpatialSession is admitted.</summary>
    internal DaggerfallExteriorCellResidencyUpdate RestoreExteriorResidency(
        DaggerfallExteriorCellResidencySave save)
    {
        RequireExteriorProfile();
        Vector3 compensation = new(save.CompensationX, save.CompensationY, save.CompensationZ);
        if (!float.IsFinite(compensation.X) || !float.IsFinite(compensation.Y) || !float.IsFinite(compensation.Z)
            || compensation.X != MathF.Truncate(compensation.X)
            || compensation.Y != MathF.Truncate(compensation.Y)
            || compensation.Z != MathF.Truncate(compensation.Z))
            throw new InvalidOperationException("Saved exterior compensation must represent whole Engine origin units.");
        using (WorldOriginPrepared prepared = _engine.WorldOrigin.Prepare(new(_spatial.Session,
            checked(-(long)compensation.X), checked(-(long)compensation.Y), checked(-(long)compensation.Z),
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
        ReconcileExteriorTerrainAppearance(residency);
        return update;
    }

    internal Vector3 LocalCompensation => _exteriorResidency is { IsInitialized: true } residency
        ? residency.Origin.Compensation : Vector3.Zero;

    internal WorldPoint ProfileToLocal(WorldPoint position) => DaggerfallExteriorSessionOrigin.Shift(position, LocalCompensation);
    internal Vector3 LocalToProfile(Vector3 position) => position - LocalCompensation;

    /// <summary>Rebase at a terrain-cell boundary, inside the existing admitted update.</summary>
    internal void RebaseExteriorIfNeeded()
    {
        if (ActiveProfile.Kind != DaggerfallWorldProfileKind.Exterior
            || _state.PlayerControl.Position is not WorldPoint position) return;
        float cellSize = DaggerfallExteriorCellResidency.CellSize;
        if (MathF.Abs(position.X) < cellSize && MathF.Abs(position.Z) < cellSize) return;
        WorldOriginReadout origin = _engine.WorldOrigin.Read(new(_spatial.Session));
        RequireOriginPair(origin);
        long x = checked(origin.CellX + (long)Math.Floor(position.X));
        long z = checked(origin.CellZ + (long)Math.Floor(position.Z));
        using WorldOriginPrepared prepared = _engine.WorldOrigin.Prepare(new(_spatial.Session,
            x, origin.CellY, z, ReadOnlyMemory<WorldOriginEntityRow>.Empty));
        CommitExteriorOrigin(prepared);
    }

    internal void NormalizeExteriorOrigin()
    {
        WorldOriginReadout origin = _engine.WorldOrigin.Read(new(_spatial.Session));
        RequireOriginPair(origin);
        if (origin.CellX == 0 && origin.CellY == 0 && origin.CellZ == 0) return;
        using WorldOriginPrepared prepared = _engine.WorldOrigin.Prepare(new(_spatial.Session,
            0, 0, 0, ReadOnlyMemory<WorldOriginEntityRow>.Empty));
        CommitExteriorOrigin(prepared);
    }

    private void CommitExteriorOrigin(WorldOriginPrepared prepared)
    {
        RequireExteriorProfile();
        if (_state.PlayerControl.Position is not WorldPoint position)
            throw new InvalidOperationException("An origin commit requires a player pose; otherwise world positions would be lost.");
        _ = DaggerfallExteriorSessionOrigin.Shift(position, Vector3.Zero);
        if (_exteriorResidency is not { IsInitialized: true })
            throw new InvalidOperationException("An origin commit requires admitted exterior residency; otherwise world coordinates would detach from their cell.");
        WorldOriginCommitReceipt receipt = _engine.WorldOrigin.Commit(new(prepared));
        try { ApplyExteriorOriginCommit(receipt); }
        catch (Exception error)
        {
            // A native commit is immediate. Reporting a recoverable transition refusal here
            // would continue with potentially mismatched collision and product poses.
            throw new InvalidOperationException("The Engine origin was committed but product rebasing failed; the session cannot continue with inconsistent world coordinates.", error);
        }
    }

    private void RequireOriginPair(WorldOriginReadout origin)
    {
        if (LocalCompensation != new Vector3(-origin.CellX, -origin.CellY, -origin.CellZ))
            throw new InvalidOperationException("Exterior product compensation does not match the Engine origin; rebasing would corrupt world positions.");
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
        _exteriorTerrainAppearance?.Clear();
        return update;
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
            ShiftPlayer(localDelta);
            foreach (ActorState actor in _state.Actors.All)
            {
                ActorPose pose = actor.Pose;
                actor.ApplyPose(new ActorPose(
                    DaggerfallExteriorSessionOrigin.Shift(pose.Position, localDelta),
                    pose.HeadingYawRadians));
            }

            Projection.Rebase(localDelta);
            ActionTriggers.RebaseActive(localDelta);
            _groundContainers.RebaseActive(localDelta);
            _host.RebaseTransientWorld(localDelta);
            ActionTriggers.RebaseRestoredPlayer(_state.PlayerControl, _state.Actors.Player.Actor.Entity);

            DaggerfallExteriorWorldOrigin prior = residency.Origin;
            residency.AdoptRebasedOrigin(prior with { Compensation = prior.Compensation + localDelta });
        }

        // The camera is Engine-owned but its descriptor is derived from the product player pose.
        // Refresh it after shifting that pose, before the caller publishes the next presentation
        // snapshot.
        _camera.Update(_state.PlayerControl);
        DaggerfallExteriorCellResidencyUpdate update = residency.Update(CurrentExteriorCell(), residency.Origin);
        ReconcileExteriorTerrainAppearance(residency);
        return update;
    }

    /// <summary>
    /// Releases retained terrain visuals after the complete appearance snapshot is empty. The session
    /// calls this once while it is disposed; the exterior window cannot be admitted afterwards.
    /// </summary>
    internal void RetireExteriorAppearance()
    {
        _retired = true;
        Projection.Appearance.SetSnapshotSupplement(null, null);
        _exteriorTerrainAppearance?.Dispose();
        _exteriorTerrainAppearance = null;
    }

    private DaggerfallExteriorCellResidency EnsureExteriorResidency()
    {
        ObjectDisposedException.ThrowIf(_retired, this);
        if (_exteriorResidency is not null) return _exteriorResidency;
        Dictionary<DaggerfallExteriorCellId, DaggerfallSiteExterior> exteriors = _definitions.Locations.Records
            .Where(site => site.Exterior is not null)
            .ToDictionary(site => new DaggerfallExteriorCellId(site.Exterior!.MapPixelX, site.Exterior.MapPixelY),
                site => site.Exterior!);
        Func<DaggerfallExteriorCellId, DaggerfallTerrainSurface> surfaceFactory = cell =>
        {
            DaggerfallTerrainSurface surface = DaggerfallTerrainSurfaceBuilder.Build(_definitions.Terrain, cell.X, cell.Y);
            if (!exteriors.TryGetValue(cell, out DaggerfallSiteExterior? exterior)) return surface;
            return DaggerfallTerrainSurfaceBuilder.ApplyLocationFlattening(surface,
                new DaggerfallTerrainLocationFlattening(exterior.MinX, exterior.MaxX, exterior.MinY, exterior.MaxY));
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
        DaggerfallExteriorTerrainAppearance appearance =
            _exteriorTerrainAppearance ??= new DaggerfallExteriorTerrainAppearance(_engine.Graphics);
        Projection.Appearance.SetSnapshotSupplement(appearance.AppendFacts, appearance.CompleteAcceptedSnapshot);
        appearance.Reconcile(residency.ResidentCells, residency.Origin, surfaceFactory);
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
