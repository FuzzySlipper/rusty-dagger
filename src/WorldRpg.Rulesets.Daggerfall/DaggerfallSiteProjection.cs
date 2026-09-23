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
    private bool _disposed;

    private DaggerfallSiteProjection(PrivateersHoldInputs inputs, DaggerfallDoorRuntime doors, DaggerfallDungeonMotionProjection motion, PrivateersHoldAppearance appearance, DaggerfallSiteLighting lighting, DaggerfallSitePortalRuntime portals)
    {
        Inputs = inputs;
        Doors = doors;
        Motion = motion;
        Appearance = appearance;
        Lighting = lighting;
        Portals = portals;
    }

    internal PrivateersHoldInputs Inputs { get; }
    internal DaggerfallDoorRuntime Doors { get; }
    internal DaggerfallDungeonMotionProjection Motion { get; }
    internal PrivateersHoldAppearance Appearance { get; }
    internal DaggerfallSiteLighting Lighting { get; }
    internal DaggerfallSitePortalRuntime Portals { get; }

    internal static DaggerfallSiteProjection Create(
        IEngineContext engine,
        EntityDirectory actors,
        IRandomService random,
        DaggerfallTuning tuning,
        DaggerfallCalendar calendar,
        PrivateersHoldInputs inputs,
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
        PrivateersHoldAppearance? appearance = null;
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
            return new(inputs, doors, motion, appearance, lighting, portals);
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

    internal CharacterStepEnvironment CharacterEnvironment(CharacterMotion motion)
    {
        CharacterStepEnvironment motionModels = Motion.CharacterEnvironment();
        // Motion models already enter Engine Spatial as exact triangle instances. Projecting the
        // same models as call-local CharacterObstacle AABBs duplicates their collision and can
        // over-block. Entity-bound triangle support/carry awaits the Engine capability tracked by
        // the owning task; retain only the existing door obstacle/support facts here.
        _ = motion;
        CharacterStepEnvironment doors = Doors.CharacterEnvironment();
        CharacterObstacle[] obstacles = [.. doors.Obstacles.ToArray(), .. motionModels.Obstacles.ToArray()];
        return new CharacterStepEnvironment(doors.Support, obstacles);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        List<Exception>? failures = null;
        try { Lighting.Dispose(); }
        catch (Exception exception) { failures = [exception]; }
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
    private readonly IReadOnlyDictionary<string, (DaggerfallSitePortal Portal, DurableIdentityReference Identity, EntityId Entity)> _portals;
    private bool _disposed;

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
        .OrderBy(value => value.Portal.Id, StringComparer.Ordinal).Select(value => (value.Portal, value.Identity, value.Entity));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
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
    DaggerfallDungeonMotionSnapshot? Motion = null);
