using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Entities;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Kit.World;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// Resources whose lifetime is one admitted Daggerfall site projection.  A candidate owns its own
/// door entities and render resources until the spatial service accepts its content artifact; it is
/// then the only projection the session publishes.
/// </summary>
internal sealed class DaggerfallSiteProjection : IDisposable
{
    private readonly SpatialMovementSystem _spatialMovement;
    private bool _disposed;
    private bool _waterTriggersActive;
    private bool _suspended;
    private Vector3 _worldOffset;

    private DaggerfallSiteProjection(DaggerfallSiteProfile inputs, DaggerfallDoorRuntime doors, DaggerfallDungeonMotionProjection motion, DaggerfallSiteAppearance appearance, DaggerfallSiteLighting lighting, DaggerfallSitePortalRuntime portals, SpatialMovementSystem spatialMovement)
    {
        Inputs = inputs;
        Doors = doors;
        Motion = motion;
        Appearance = appearance;
        Lighting = lighting;
        Portals = portals;
        _spatialMovement = spatialMovement ?? throw new ArgumentNullException(nameof(spatialMovement));
        // Creation admits authored water below. Keep this false until registration and activation
        // have both succeeded so the initial projection cannot skip its trigger admission.
        _waterTriggersActive = false;
    }

    internal DaggerfallSiteProfile Inputs { get; }
    internal DaggerfallDoorRuntime Doors { get; }
    internal DaggerfallDungeonMotionProjection Motion { get; }
    internal DaggerfallSiteAppearance Appearance { get; }
    internal DaggerfallSiteLighting Lighting { get; }
    internal DaggerfallSitePortalRuntime Portals { get; }
    internal bool IsSuspended => _suspended;

    /// <summary>
    /// Accumulated product-space displacement from the profile's authored source frame. Ambient
    /// zones and other source-coordinate consumers use this value when the Engine origin rebases;
    /// it is kept beside the projection so those consumers never infer it from native transforms.
    /// </summary>
    internal Vector3 WorldOffset => _worldOffset;

    internal static DaggerfallSiteProjection Create(
        IEngineContext engine,
        EntityDirectory actors,
        IRandomService random,
        DaggerfallTuning tuning,
        DaggerfallCalendar calendar,
        DaggerfallSiteProfile inputs,
        DaggerfallAudioBundle? audioBundle,
        SpatialMovementSystem spatialMovement,
        IEnumerable<DaggerfallDoorSave>? restoredDoors = null,
        DaggerfallDungeonMotionSnapshot? restoredMotion = null,
        bool deferMotionCollisionAdmission = false)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(actors);
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(tuning);
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(spatialMovement);
        DaggerfallDoorRuntime doors = new(actors, random, inputs.Doors, inputs.ProfileKey.LogicalId, restoredDoors);
        DaggerfallDungeonMotionProjection? motion = null;
        DaggerfallSiteAppearance? appearance = null;
        DaggerfallSiteLighting? lighting = null;
        DaggerfallSitePortalRuntime? portals = null;
        try
        {
            motion = new(actors, engine.Spatial, spatialMovement.Session, doors, inputs.ProfileKey.LogicalId,
                inputs.DungeonActions, inputs.DungeonActionModels, restoredMotion, deferMotionCollisionAdmission);
            appearance = new(engine.Content, engine.Graphics, inputs, engine.Audio,
                tuning.PresentationAudio, random, audioBundle, doors, motion);
            lighting = new(engine.Graphics, engine.CameraView, inputs, tuning.SiteLighting, calendar);
            portals = new(actors, inputs.ProfileKey, inputs.Portals);
            DaggerfallSiteProjection projection = new(inputs, doors, motion, appearance, lighting, portals, spatialMovement);
            projection.ActivateWaterTriggers(tick: 0);
            return projection;
        }
        catch
        {
            try { portals?.Dispose(); }
            finally
            {
                try { lighting?.Dispose(); }
                finally
                {
                    try { appearance?.Dispose(); }
                    finally
                    {
                        try { motion?.Dispose(); }
                        finally { doors.Dispose(); }
                    }
                }
            }
            throw;
        }
    }

    internal void AdvanceMotion(double deltaSeconds) => Motion.Advance(deltaSeconds);

    internal DaggerfallDungeonMotionActivation ActivateMotion(string actionId) => Motion.Activate(actionId);

    internal DaggerfallDungeonMotionSnapshot CaptureMotion() => Motion.Capture();

    internal void ActivateMotionCollisionResidency() => Motion.ActivateCollisionResidency();

    internal void RebuildMotionCollisionResidency() => Motion.RebuildCollisionResidency();

    /// <summary>Retires this projection's Engine trigger definitions without retaining a second overlap map.</summary>
    internal void DeactivateWaterTriggers(ulong tick)
    {
        if (!_waterTriggersActive) return;
        foreach (CharacterWaterVolume volume in Inputs.WaterVolumes)
            _spatialMovement.ReleaseTrigger(volume.Trigger, tick);
        _waterTriggersActive = false;
    }

    /// <summary>Re-admits this profile's authored water triggers after a suspended location resumes.</summary>
    private void ActivateWaterTriggers(ulong tick)
    {
        if (_waterTriggersActive || Inputs.WaterVolumes.Count == 0) return;
        List<ulong> registered = [];
        try
        {
            foreach (CharacterWaterVolume volume in Inputs.WaterVolumes)
            {
                _ = _spatialMovement.RegisterTrigger(volume.Trigger, Inputs.ProfileKey.LogicalId, "water");
                registered.Add(volume.Trigger);
                _spatialMovement.ActivateTrigger(volume.Trigger, tick);
            }
            _waterTriggersActive = true;
        }
        catch
        {
            foreach (ulong trigger in registered)
            {
                try { _spatialMovement.ReleaseTrigger(trigger, tick); }
                catch { /* preserve the admission failure */ }
            }
            throw;
        }
    }
    /// <summary>
    /// Retires every Engine-backed resource belonging to this location cell while retaining the
    /// profile projection and its product state. Durable resource identities are recreated by
    /// <see cref="Resume"/> under the same profile key.
    /// </summary>
    internal void Suspend()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(DaggerfallSiteProjection));
        if (_suspended) return;
        DeactivateWaterTriggers(tick: 0);
        Appearance.RetireAllActors();
        Appearance.SuspendLocationResources();
        Motion.Suspend();
        Doors.Suspend();
        Portals.Suspend();
        Lighting.Suspend();
        _suspended = true;
    }

    /// <summary>Re-admits the location's geometry, actors' supporting action models and portal resources.</summary>
    internal void Resume()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(DaggerfallSiteProjection));
        if (!_suspended) return;
        Doors.Resume();
        Motion.Resume();
        Appearance.ResumeLocationResources(Doors, Motion);
        Portals.Resume();
        Lighting.Resume();
        ActivateWaterTriggers(tick: 0);
        _suspended = false;
    }

    internal void Rebase(Vector3 delta)
    {
        if (!float.IsFinite(delta.X) || !float.IsFinite(delta.Y) || !float.IsFinite(delta.Z))
            throw new ArgumentOutOfRangeException(nameof(delta), "A site rebase delta must be finite.");
        _worldOffset += delta;
        Doors.Rebase(delta);
        Motion.Rebase(delta);
        Portals.Rebase(delta);
        Appearance.Rebase(delta);
        Lighting.Rebase(delta);
    }

    internal CharacterStepEnvironment CharacterEnvironment(CharacterMotion motion)
    {
        if (_suspended) return CharacterStepEnvironment.Empty;
        CharacterStepEnvironment motionModels = Motion.CharacterEnvironment();
        // Motion models enter Engine Spatial as exact triangle instances bound to their current
        // entity identities. Passing those instances lets Engine resolve support and carry from
        // retained triangle geometry; projecting the same models as AABB obstacles would duplicate
        // collision and over-block.
        _ = motion;
        CharacterStepEnvironment doors = Doors.CharacterEnvironment();
        CharacterWaterVolume[] water = Inputs.WaterVolumes
            .Select(volume => _worldOffset == Vector3.Zero
                ? volume
                : new CharacterWaterVolume(volume.Trigger, volume.Minimum + _worldOffset, volume.Maximum + _worldOffset))
            .ToArray();
        return CombineCharacterEnvironments(doors, motionModels, water);
    }

    internal static CharacterStepEnvironment CombineCharacterEnvironments(
        CharacterStepEnvironment doors,
        CharacterStepEnvironment motionModels,
        IReadOnlyList<CharacterWaterVolume>? waterVolumes = null)
    {
        CharacterMeshInstance[] meshInstances = [.. doors.MeshInstances.ToArray(), .. motionModels.MeshInstances.ToArray()];
        HashSet<ulong> meshEntities = meshInstances.Select(mesh => mesh.Entity).ToHashSet();
        CharacterObstacle[] obstacles = [.. doors.Obstacles.ToArray(), .. motionModels.Obstacles.ToArray()];
        if (meshEntities.Count != 0)
            obstacles = obstacles.Where(obstacle => !meshEntities.Contains(obstacle.Entity)).ToArray();
        CharacterWaterVolume[] water = (waterVolumes ?? [.. doors.WaterVolumes.ToArray(), .. motionModels.WaterVolumes.ToArray()])
            .Select(volume => volume.Validate())
            .OrderBy(volume => volume.Trigger)
            .ToArray();
        return new CharacterStepEnvironment(doors.Support, obstacles, meshInstances, water);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        List<Exception>? failures = null;
        try { DeactivateWaterTriggers(0); }
        catch (Exception exception) { failures = [exception]; }
        try { Lighting.Dispose(); }
        catch (Exception exception) { (failures ??= []).Add(exception); }
        try { Appearance.Dispose(); }
        catch (Exception exception) { (failures ??= []).Add(exception); }
        try { Portals.Dispose(); }
        catch (Exception exception) { (failures ??= []).Add(exception); }
        try { Motion.Dispose(); }
        catch (Exception exception) { (failures ??= []).Add(exception); }
        try { Doors.Dispose(); }
        catch (Exception exception) { (failures ??= []).Add(exception); }
        if (failures is { Count: > 0 }) throw new AggregateException(failures);
    }
}

/// <summary>Transient Engine entities used only to expose source-derived portals to the ordinary interaction service.</summary>
internal sealed class DaggerfallSitePortalRuntime : IDisposable
{
    private static readonly EntityTypeId PortalType = new("daggerfall.site-portal");
    private readonly EntityDirectory _entities;
    private readonly Dictionary<string, (DaggerfallSitePortal Portal, DurableIdentityReference Identity, EntityId Entity)> _portals;
    private bool _disposed;
    private bool _suspended;

    internal DaggerfallSitePortalRuntime(EntityDirectory entities, DaggerfallWorldProfileKey profile, IEnumerable<DaggerfallSitePortal> portals)
    {
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        profile.Validate();
        ArgumentNullException.ThrowIfNull(portals);
        Dictionary<string, (DaggerfallSitePortal Portal, DurableIdentityReference Identity, EntityId Entity)> admitted = new(StringComparer.Ordinal);
        try
        {
            foreach (DaggerfallSitePortal portal in portals.Select(portal => portal.Validate()).OrderBy(portal => portal.Id, StringComparer.Ordinal))
            {
                DurableIdentityReference identity = new(DurableIdentityKind.Resource, StableIdentity(profile.LogicalId, portal.Id));
                EntityId entity = _entities.Create(identity, PortalType);
                admitted.Add(portal.Id, (portal, identity, entity));
            }
            _portals = admitted;
        }
        catch
        {
            foreach ((DaggerfallSitePortal _, DurableIdentityReference identity, EntityId _) in admitted.Values)
                _entities.Destroy(identity);
            throw;
        }
    }

    internal IEnumerable<(DaggerfallSitePortal Portal, DurableIdentityReference Identity, EntityId Entity)> All => _portals.Values
        .Where(value => !_suspended)
        .OrderBy(value => value.Portal.Id, StringComparer.Ordinal).Select(value => (value.Portal, value.Identity, value.Entity));

    /// <summary>Releases portal entities while retaining their durable destination records.</summary>
    internal void Suspend()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(DaggerfallSitePortalRuntime));
        if (_suspended) return;
        foreach (var value in _portals.Values.ToArray())
        {
            _entities.Destroy(value.Identity);
            _portals[value.Portal.Id] = (value.Portal, value.Identity, default);
        }
        _suspended = true;
    }

    /// <summary>Re-admits suspended portal entities under their original resource identities.</summary>
    internal void Resume()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(DaggerfallSitePortalRuntime));
        if (!_suspended) return;
        List<(DaggerfallSitePortal Portal, DurableIdentityReference Identity, EntityId Entity)> created = [];
        try
        {
            foreach (var value in _portals.Values.ToArray())
            {
                EntityId entity = _entities.Create(value.Identity, PortalType);
                _portals[value.Portal.Id] = (value.Portal, value.Identity, entity);
                created.Add((value.Portal, value.Identity, entity));
            }
            _suspended = false;
        }
        catch
        {
            foreach ((DaggerfallSitePortal portal, DurableIdentityReference identity, EntityId entity) in created)
            {
                _entities.Destroy(identity);
                _portals[portal.Id] = (portal, identity, default);
            }
            throw;
        }
    }

    internal void Rebase(Vector3 delta)
    {
        if (_suspended) return;
        foreach (string id in _portals.Keys.ToArray())
        {
            var value = _portals[id];
            _portals[id] = (value.Portal with { Position = DaggerfallExteriorSessionOrigin.Shift(value.Portal.Position, delta) }, value.Identity, value.Entity);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (!_suspended)
            foreach ((DaggerfallSitePortal _, DurableIdentityReference identity, EntityId _) in _portals.Values)
                _entities.Destroy(identity);
    }

    private static ulong StableIdentity(string profile, string id)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        ulong hash = offset;
        foreach (char value in $"site-portal:{profile}:{id}") { hash ^= value; hash *= prime; }
        return hash;
    }
}

/// <summary>
/// Product values detached with an inactive site. Returning always creates fresh Engine entities
/// and resources; no entity, content reference, or native handle crosses the site boundary.
/// </summary>
internal sealed record DaggerfallSiteRuntimeDelta(
    DaggerfallActorSave[] Actors,
    DaggerfallDynamicActorSave[] DynamicActors,
    DaggerfallActorInventorySave[] ActorInventories,
    DaggerfallCorpseSave[] Corpses,
    DaggerfallDoorSave[] Doors,
    DaggerfallActiveEffectSave[] Effects,
    DaggerfallDungeonMotionSnapshot? Motion = null)
{
    internal long[] BanishedActors { get; init; } = [];
}
