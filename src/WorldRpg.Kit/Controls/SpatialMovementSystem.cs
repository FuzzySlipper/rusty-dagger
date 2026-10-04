using System.Numerics;
using Rusty.Engine;

namespace WorldRpg.Kit.Controls;

/// <summary>One normalized water volume admitted by the current world projection.</summary>
/// <remarks>
/// The volume is a trigger identity and an axis-aligned world-space envelope.  It is
/// deliberately a value supplied for the current proposal; the Kit does not retain a
/// second water map or infer immersion from product geometry.
/// </remarks>
public readonly record struct CharacterWaterVolume(ulong Trigger, Vector3 Minimum, Vector3 Maximum)
{
    public CharacterWaterVolume Validate()
    {
        if (Trigger == 0
            || !float.IsFinite(Minimum.X) || !float.IsFinite(Minimum.Y) || !float.IsFinite(Minimum.Z)
            || !float.IsFinite(Maximum.X) || !float.IsFinite(Maximum.Y) || !float.IsFinite(Maximum.Z)
            || Minimum.X > Maximum.X || Minimum.Y > Maximum.Y || Minimum.Z > Maximum.Z)
            throw new ArgumentOutOfRangeException(nameof(CharacterWaterVolume), "Water volumes require a non-zero trigger and finite ordered bounds.");
        return this;
    }

    /// <summary>The normalized surface height used by product movement policy.</summary>
    public float SurfaceY => Maximum.Y;
}

/// <summary>Call-local facts supplied for one proposal; the Engine does not retain product support or obstacle ownership.</summary>
public readonly record struct CharacterStepEnvironment(
    CharacterSupport Support,
    ReadOnlyMemory<CharacterObstacle> Obstacles,
    ReadOnlyMemory<CharacterMeshInstance> MeshInstances,
    ReadOnlyMemory<CharacterWaterVolume> WaterVolumes)
{
    public CharacterStepEnvironment(CharacterSupport support, ReadOnlyMemory<CharacterObstacle> obstacles)
        : this(support, obstacles, ReadOnlyMemory<CharacterMeshInstance>.Empty, ReadOnlyMemory<CharacterWaterVolume>.Empty)
    {
    }

    public CharacterStepEnvironment(CharacterSupport support, ReadOnlyMemory<CharacterObstacle> obstacles,
        ReadOnlyMemory<CharacterMeshInstance> meshInstances)
        : this(support, obstacles, meshInstances, ReadOnlyMemory<CharacterWaterVolume>.Empty)
    {
    }

    public static CharacterStepEnvironment Empty { get; } = new(
        default,
        ReadOnlyMemory<CharacterObstacle>.Empty,
        ReadOnlyMemory<CharacterMeshInstance>.Empty,
        ReadOnlyMemory<CharacterWaterVolume>.Empty);
}

/// <summary>Horizontal direction used by a call-local wall contact query.</summary>
public enum CharacterWallProbeDirection
{
    Forward,
    Backward,
}

/// <summary>Ruleset-selected controls and speeds for one Engine-owned character proposal.</summary>
public readonly record struct CharacterStepControls(
    bool JumpPressed = false,
    bool JumpHeld = false,
    bool CrouchRequested = false,
    float? ForwardSpeed = null,
    float? BackwardSpeed = null,
    float? StrafeSpeed = null,
    float? JumpSpeed = null,
    Vector2? PlanarIntent = null,
    float? VerticalVelocity = null,
    CharacterMovementRequest? Movement = null)
{
    internal CharacterControllerConfig ApplyTo(CharacterControllerConfig defaults) => defaults with
    {
        Ground = defaults.Ground with
        {
            ForwardSpeed = ForwardSpeed ?? defaults.Ground.ForwardSpeed,
            BackwardSpeed = BackwardSpeed ?? defaults.Ground.BackwardSpeed,
            StrafeSpeed = StrafeSpeed ?? defaults.Ground.StrafeSpeed,
        },
        Vertical = defaults.Vertical with { JumpSpeed = JumpSpeed ?? defaults.Vertical.JumpSpeed },
    };
}

/// <summary>Owns one Engine spatial session and persistent character continuation.</summary>
public sealed class SpatialMovementSystem : IDisposable
{
    private readonly ISpatialService _spatial;
    private readonly IContentService _contentService;
    private ContentReference? _content;
    private readonly SpatialTuning _tuning;
    private readonly CharacterControllerConfig _baseController;
    private CharacterControllerConfig _controller;
    private readonly SpatialSession _session;
    private ulong? _latestGeneration;
    private CharacterContinuationCheckpoint? _latestCheckpoint;
    private CharacterContinuationCheckpoint? _restoredCheckpoint;
    private readonly Dictionary<ulong, (string Scope, string Tag, int References, bool Active)> _registeredTriggers = [];
    private bool _verticalDriven;
    private bool _disposed;

    /// <summary>The Engine-owned scene session that other named Engine services may query during this system's lifetime.</summary>
    public SpatialSession Session
    {
        get
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SpatialMovementSystem));
            return _session;
        }
    }

    /// <summary>The current admitted character configuration, including stance geometry and step limits.</summary>
    public CharacterControllerConfig CurrentController => _controller;

    /// <summary>
    /// Creates one spatial session. A null base artifact is useful when a product owns all of its
    /// static closures through <see cref="ApplyContentArtifactResidency"/>; the first admitted
    /// placement establishes the Engine navigation grid and later placements can be removed without
    /// replacing the whole scene.
    /// </summary>
    public SpatialMovementSystem(ISpatialService spatial, IContentService content, SpatialContentArtifact? inputs, SpatialTuning tuning)
    {
        ArgumentNullException.ThrowIfNull(spatial);
        ArgumentNullException.ThrowIfNull(content);
        tuning = (tuning ?? throw new ArgumentNullException(nameof(tuning))).Validate();
        _spatial = spatial;
        _contentService = content;
        _tuning = tuning;
        CharacterControllerConfig defaults = spatial.DefaultCharacterControllerConfig();
        _baseController = (tuning.CharacterController ?? new CharacterControllerTuning()).ApplyTo(defaults);
        _controller = _baseController;
        _spatial.ValidateCharacterControllerConfig(_baseController);
        SpatialSession session = spatial.CreateSession(new SpatialSessionConfig(
            tuning.CollisionVoxelSize,
            tuning.CollisionChunkSize,
            VoxelSurfaceMode.GreedyCubes));
        try
        {
            if (inputs is { } artifact)
            {
                ContentReference resolved = content.ResolveReference(new ContentResolveRequest(artifact.Path, artifact.Sha256));
                try
                {
                    spatial.ReplaceContentArtifact(new SpatialContentArtifactReplaceRequest(
                        session,
                        resolved,
                        artifact.NavigationGridId,
                        tuning.NavigationChunkSize,
                        tuning.NavigationMaximumStepCells));
                    _content = resolved;
                    resolved = null!;
                }
                finally { resolved?.Dispose(); }
            }
            _session = session;
        }
        catch { session.Dispose(); throw; }
    }

    /// <summary>Submits the current control state to the Engine and applies its receipt in the admitted update order.</summary>
    public CharacterStepReceipt? Step(PlayerControlState player, ProductUpdateState update, CharacterStepEnvironment? environment = null, CharacterStepControls? controls = null)
    {
        if (_disposed) return null;
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(update);
        if (player.Position is not WorldPoint position) return null;

        CharacterStepEnvironment stepEnvironment = environment ?? CharacterStepEnvironment.Empty;
        ulong sequence = checked(player.Motion.LastCommandSequence + 1);
        CharacterStepControls selected = controls ?? default;
        if (selected.VerticalVelocity is float verticalVelocity && !float.IsFinite(verticalVelocity))
            throw new ArgumentOutOfRangeException(nameof(controls), "Controlled vertical velocity must be finite.");
        if (selected.Movement is CharacterMovementRequest movement)
            ValidateMovement(movement, nameof(controls));

        bool verticalDriveSelected = selected.VerticalVelocity.HasValue;
        bool verticalDriveReleased = !verticalDriveSelected && _verticalDriven;
        CharacterMotion motion = player.Motion;
        CharacterControllerConfig config = selected.ApplyTo(verticalDriveReleased ? _baseController : _controller);
        if (verticalDriveSelected)
        {
            Vector3 controlledVelocity = motion.ControlledVelocity;
            controlledVelocity.Y = selected.VerticalVelocity!.Value;
            motion = motion with
            {
                ControlledVelocity = controlledVelocity,
                JumpBufferRemaining = 0f,
                CoyoteRemaining = 0f,
            };
            config = config with
            {
                Vertical = config.Vertical with { Gravity = 0f },
                Jump = config.Jump with { BufferSeconds = 0f, CoyoteSeconds = 0f },
            };
        }
        else if (verticalDriveReleased)
        {
            Vector3 controlledVelocity = motion.ControlledVelocity;
            controlledVelocity.Y = 0f;
            motion = motion with
            {
                ControlledVelocity = controlledVelocity,
                FallOriginY = position.Y,
                PeakY = position.Y,
            };
        }

        CharacterControllerCommand command = new(
            selected.Movement ?? default,
            selected.PlanarIntent ?? update.PlanarIntent,
            player.YawRadians,
            verticalDriveSelected ? false : selected.JumpPressed,
            verticalDriveSelected ? false : selected.JumpHeld,
            selected.CrouchRequested,
            ExternalVelocity: Vector3.Zero,
            ExternalImpulse: Vector3.Zero,
            update.DeltaSeconds,
            sequence);
        CharacterStepRequest request = new(
            _session,
            position.ToVector(),
            motion,
            stepEnvironment.Support,
            stepEnvironment.Obstacles,
            stepEnvironment.MeshInstances,
            config,
            command);
        CharacterStepReceipt receipt = _spatial.ProposeCharacterStep(request);
        _latestGeneration = receipt.Generation;
        // Population may admit direct Engine character steps after the player. Capture the
        // player's full native continuation at this boundary so a later save never asks the
        // Engine to reconstruct it from another actor's latest receipt.
        _latestCheckpoint = _spatial.CaptureCharacterContinuation(
            new CharacterContinuationCaptureRequest(_session, receipt.Generation));
        _restoredCheckpoint = null;
        if (verticalDriveReleased) _controller = _baseController;
        _verticalDriven = verticalDriveSelected;
        player.Apply(receipt);
        return receipt;
    }

    /// <summary>
    /// Queries the nearest admitted world hit along the player's horizontal facing direction.
    /// The range is the configured capsule radius plus contact skin and recovery nudge; hit meaning and climbability
    /// remain caller policy. A vertical offset can sample another height for edge detection.
    /// </summary>
    public bool TryProbeClimbWall(
        PlayerControlState player,
        CharacterWallProbeDirection direction,
        out SpatialHit hit,
        CharacterStepEnvironment? environment = null) => TryProbeClimbWall(player, direction, 0f, out hit, environment);

    /// <summary>Queries at the capsule's lower edge, with configured skin and clearance padding.</summary>
    public bool TryProbeClimbWallAtFeet(
        PlayerControlState player,
        CharacterWallProbeDirection direction,
        out SpatialHit hit,
        CharacterStepEnvironment? environment = null)
    {
        hit = default;
        if (_disposed) return false;
        ArgumentNullException.ThrowIfNull(player);
        if (player.Position is null) return false;
        CharacterShapeConfig shape = _controller.Shape;
        float height = player.Motion.Stance switch
        {
            CharacterStance.Standing => shape.StandingHeight,
            CharacterStance.Crouched => shape.CrouchedHeight,
            _ => throw new ArgumentOutOfRangeException(nameof(player), "Character stance is invalid."),
        };
        float feetOffset = -height * 0.5f + shape.ContactSkin + shape.ClearancePadding;
        return TryProbeClimbWall(player, direction, feetOffset, out hit, environment);
    }

    /// <summary>Queries wall contact from a caller-selected height relative to the character center.</summary>
    public bool TryProbeClimbWall(
        PlayerControlState player,
        CharacterWallProbeDirection direction,
        float verticalOffset,
        out SpatialHit hit,
        CharacterStepEnvironment? environment = null)
    {
        hit = default;
        if (_disposed) return false;
        ArgumentNullException.ThrowIfNull(player);
        if (player.Position is not WorldPoint position) return false;
        if (!float.IsFinite(verticalOffset)) throw new ArgumentOutOfRangeException(nameof(verticalOffset));
        if (direction is not CharacterWallProbeDirection.Forward and not CharacterWallProbeDirection.Backward)
            throw new ArgumentOutOfRangeException(nameof(direction));
        if (!float.IsFinite(player.YawRadians)) throw new ArgumentOutOfRangeException(nameof(player.YawRadians));

        (float sinYaw, float cosYaw) = MathF.SinCos(player.YawRadians);
        float directionSign = direction == CharacterWallProbeDirection.Forward ? 1f : -1f;
        Vector3 facing = new(sinYaw * directionSign, 0f, -cosYaw * directionSign);
        CharacterShapeConfig shape = _controller.Shape;
        hit = CastRay(position.ToVector() + Vector3.UnitY * verticalOffset, facing,
            shape.Radius + shape.ContactSkin + _controller.Recovery.NormalNudge, environment);
        return hit.Present;
    }

    /// <summary>Queries this admitted scene and current call-local obstacles through the Engine.</summary>
    public SpatialHit CastRay(Vector3 origin, Vector3 direction, float maxDistance,
        CharacterStepEnvironment? environment = null) => CastRay(
            origin,
            direction,
            maxDistance,
            ReadOnlyMemory<SpatialEntityCollider>.Empty,
            environment);

    /// <summary>
    /// Queries this admitted scene with caller-projected world-space entity bounds and the
    /// current call-local obstacles through the Engine. The supplied rows are copied into the
    /// one Engine request; this method retains no product collider state.
    /// </summary>
    public SpatialHit CastRay(
        Vector3 origin,
        Vector3 direction,
        float maxDistance,
        ReadOnlyMemory<SpatialEntityCollider> entities,
        CharacterStepEnvironment? environment = null)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SpatialMovementSystem));
        if (!float.IsFinite(origin.X) || !float.IsFinite(origin.Y) || !float.IsFinite(origin.Z)
            || !float.IsFinite(direction.X) || !float.IsFinite(direction.Y) || !float.IsFinite(direction.Z)
            || !float.IsFinite(maxDistance) || maxDistance <= 0f)
            throw new ArgumentOutOfRangeException(nameof(maxDistance), "Spatial ray inputs must be finite and distance positive.");
        ValidateColliders(entities.Span, nameof(entities));
        ReadOnlyMemory<SpatialEntityCollider> projected = QueryColliders(entities, environment);
        SpatialRaycastRequest request = new(
            _session,
            origin,
            direction,
            maxDistance,
            new SpatialQueryFilter(uint.MaxValue, uint.MaxValue),
            projected,
            ReadOnlyMemory<ulong>.Empty,
            ReadOnlyMemory<SpatialEntityCollider>.Empty);
        return _spatial.CastRay(request);
    }

    /// <summary>Queries Engine capsule overlap against the same admitted geometry and call-local obstacles as movement.</summary>
    /// <summary>Sweeps an Engine capsule against admitted geometry and current call-local obstacles.</summary>
    public SpatialHit CastCapsule(Vector3 center, double halfHeight, double radius, Vector3 translation,
        ReadOnlyMemory<SpatialEntityCollider> entities, CharacterStepEnvironment? environment = null)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SpatialMovementSystem));
        return _spatial.CastCapsule(new SpatialCapsuleQueryRequest(_session, center, halfHeight, radius,
            translation, 0d, new SpatialQueryFilter(uint.MaxValue, uint.MaxValue), QueryColliders(entities, environment), ReadOnlyMemory<ulong>.Empty));
    }

    public SpatialHit OverlapCapsule(Vector3 center, double halfHeight, double radius,
        ReadOnlyMemory<SpatialEntityCollider> entities, CharacterStepEnvironment? environment = null)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SpatialMovementSystem));
        return _spatial.OverlapCapsule(new SpatialCapsuleQueryRequest(_session, center, halfHeight, radius,
            Vector3.Zero, 0d, new SpatialQueryFilter(uint.MaxValue, uint.MaxValue),
            QueryColliders(entities, environment), ReadOnlyMemory<ulong>.Empty));
    }

    private static ReadOnlyMemory<SpatialEntityCollider> QueryColliders(
        ReadOnlyMemory<SpatialEntityCollider> entities, CharacterStepEnvironment? environment)
    {
        ReadOnlyMemory<SpatialEntityCollider> obstacles = SpatialColliders(environment);
        if (obstacles.IsEmpty) return entities;
        SpatialEntityCollider[] merged = new SpatialEntityCollider[entities.Length + obstacles.Length];
        entities.Span.CopyTo(merged);
        obstacles.Span.CopyTo(merged.AsSpan(entities.Length));
        return merged;
    }

    /// <summary>
    /// Projects the current Engine character shape as a world-space AABB for a caller-owned
    /// query such as the session trigger service. Trigger overlap uses this conservative envelope;
    /// character movement continues to use the Engine's capsule proposal and remains authoritative.
    /// </summary>
    public SpatialEntityCollider ProjectCharacterCollider(PlayerControlState player, ulong entity)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SpatialMovementSystem));
        ArgumentNullException.ThrowIfNull(player);
        if (entity == 0) throw new ArgumentOutOfRangeException(nameof(entity));
        if (player.Position is not WorldPoint position)
            throw new InvalidOperationException("A character collider requires a live player position.");

        CharacterShapeConfig shape = _controller.Shape;
        float height = player.Motion.Stance switch
        {
            CharacterStance.Standing => shape.StandingHeight,
            CharacterStance.Crouched => shape.CrouchedHeight,
            _ => throw new ArgumentOutOfRangeException(nameof(player), "Character stance is invalid."),
        };
        float radius = shape.Radius + shape.ContactSkin;
        float halfHeight = height * .5f + shape.ClearancePadding;
        Vector3 center = position.ToVector();
        return new SpatialEntityCollider(
            entity,
            center - new Vector3(radius, halfHeight, radius),
            center + new Vector3(radius, halfHeight, radius),
            0,
            0,
            Enabled: true,
            StaticCollider: false,
            Trigger: false);
    }

    /// <summary>Projects one normalized water volume for the Engine trigger service.</summary>
    public static SpatialEntityCollider ProjectWaterCollider(CharacterWaterVolume volume)
    {
        volume.Validate();
        return new SpatialEntityCollider(
            volume.Trigger,
            volume.Minimum,
            volume.Maximum,
            0,
            0,
            Enabled: true,
            StaticCollider: false,
            Trigger: true);
    }

    /// <summary>Registers a product-owned trigger identity with the current Engine spatial session.</summary>
    public bool RegisterTrigger(ulong trigger, string scope, string tag)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SpatialMovementSystem));
        if (trigger == 0) throw new ArgumentOutOfRangeException(nameof(trigger));
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        if (_registeredTriggers.TryGetValue(trigger, out (string Scope, string Tag, int References, bool Active) prior))
        {
            if (!StringComparer.Ordinal.Equals(prior.Scope, scope) || !StringComparer.Ordinal.Equals(prior.Tag, tag))
                throw new ArgumentException($"Spatial trigger {trigger} is already registered with different scope or tag.", nameof(trigger));
            _registeredTriggers[trigger] = (prior.Scope, prior.Tag, checked(prior.References + 1), prior.Active);
            return false;
        }
        _spatial.RegisterTrigger(new SpatialTriggerRegisterRequest(
            _session, trigger, scope, tag, SpatialTriggerGeometry.EntityBounds));
        _registeredTriggers.Add(trigger, (scope, tag, 1, true));
        return true;
    }

    /// <summary>Reactivates a retained trigger after its prior projection released the last reference.</summary>
    public void ActivateTrigger(ulong trigger, ulong tick)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SpatialMovementSystem));
        if (!_registeredTriggers.TryGetValue(trigger, out (string Scope, string Tag, int References, bool Active) registration))
            throw new InvalidOperationException($"Spatial trigger {trigger} has not been registered.");
        if (registration.References <= 0)
            throw new InvalidOperationException($"Spatial trigger {trigger} has no retained projection.");
        if (registration.Active) return;
        _ = _spatial.SetTriggerActive(new SpatialTriggerSetActiveRequest(_session, trigger, Active: true, tick));
        _registeredTriggers[trigger] = (registration.Scope, registration.Tag, registration.References, true);
    }

    /// <summary>Releases one projection's trigger reference, retiring the Engine trigger at zero.</summary>
    public void ReleaseTrigger(ulong trigger, ulong tick)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SpatialMovementSystem));
        if (!_registeredTriggers.TryGetValue(trigger, out (string Scope, string Tag, int References, bool Active) registration))
            throw new InvalidOperationException($"Spatial trigger {trigger} has not been registered.");
        if (registration.References <= 0)
            throw new InvalidOperationException($"Spatial trigger {trigger} has already released all projection references.");
        int references = registration.References - 1;
        bool active = registration.Active;
        if (references == 0 && active)
        {
            _ = _spatial.SetTriggerActive(new SpatialTriggerSetActiveRequest(_session, trigger, Active: false, tick));
            active = false;
        }
        _registeredTriggers[trigger] = (registration.Scope, registration.Tag, references, active);
    }

    /// <summary>Reconciles current player/entity bounds through Engine trigger admission.</summary>
    public SpatialTriggerReconcileResult ReconcileTriggers(ulong tick, ReadOnlyMemory<SpatialEntityCollider> entities)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SpatialMovementSystem));
        ValidateColliders(entities.Span, nameof(entities));
        return _spatial.ReconcileTriggers(new SpatialTriggerReconcileRequest(
            _session, tick, SpatialTriggerCause.Movement, entities));
    }

    /// <summary>Changes a registered trigger's active state through Engine lifecycle admission.</summary>
    public SpatialTriggerLifecycleResult SetTriggerActive(ulong trigger, bool active, ulong tick)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SpatialMovementSystem));
        if (trigger == 0) throw new ArgumentOutOfRangeException(nameof(trigger));
        SpatialTriggerLifecycleResult result = _spatial.SetTriggerActive(new SpatialTriggerSetActiveRequest(_session, trigger, active, tick));
        if (_registeredTriggers.TryGetValue(trigger, out (string Scope, string Tag, int References, bool Active) registration))
            _registeredTriggers[trigger] = (registration.Scope, registration.Tag, registration.References, active);
        return result;
    }

    private static ReadOnlyMemory<SpatialEntityCollider> SpatialColliders(CharacterStepEnvironment? environment)
    {
        if (environment is not { } selected || selected.Obstacles.IsEmpty)
            return ReadOnlyMemory<SpatialEntityCollider>.Empty;

        ReadOnlySpan<CharacterObstacle> obstacles = selected.Obstacles.Span;
        List<SpatialEntityCollider> colliders = new(obstacles.Length);
        foreach (CharacterObstacle obstacle in obstacles)
        {
            if (!obstacle.CollisionEnabled) continue;
            Transform transform = obstacle.Transform;
            if (transform.Scale != Vector3.One)
                throw new ArgumentException("Character obstacle transforms require unit scale.", nameof(environment));
            Vector3 translation = transform.Translation;
            colliders.Add(new SpatialEntityCollider(
                obstacle.Entity,
                translation + obstacle.BoundsMin,
                translation + obstacle.BoundsMax,
                0,
                0,
                Enabled: true,
                StaticCollider: false,
                Trigger: false));
        }
        return colliders.Count == 0 ? ReadOnlyMemory<SpatialEntityCollider>.Empty : colliders.ToArray();
    }

    private static void ValidateColliders(ReadOnlySpan<SpatialEntityCollider> colliders, string parameterName)
    {
        foreach (SpatialEntityCollider collider in colliders)
        {
            if (collider.Entity == 0
                || !float.IsFinite(collider.Min.X) || !float.IsFinite(collider.Min.Y) || !float.IsFinite(collider.Min.Z)
                || !float.IsFinite(collider.Max.X) || !float.IsFinite(collider.Max.Y) || !float.IsFinite(collider.Max.Z)
                || collider.Min.X > collider.Max.X || collider.Min.Y > collider.Max.Y || collider.Min.Z > collider.Max.Z)
                throw new ArgumentOutOfRangeException(parameterName, "Projected spatial colliders require non-zero entities and finite ordered bounds.");
        }
    }

    private static void ValidateMovement(CharacterMovementRequest movement, string parameterName)
    {
        if (!Enum.IsDefined(movement.Mode)
            || !float.IsFinite(movement.VerticalIntent)
            || !float.IsFinite(movement.Speed) || movement.Speed < 0f
            || !float.IsFinite(movement.Acceleration) || movement.Acceleration < 0f
            || !float.IsFinite(movement.Drag) || movement.Drag < 0f
            || !float.IsFinite(movement.Minimum.X) || !float.IsFinite(movement.Minimum.Y) || !float.IsFinite(movement.Minimum.Z)
            || !float.IsFinite(movement.Maximum.X) || !float.IsFinite(movement.Maximum.Y) || !float.IsFinite(movement.Maximum.Z)
            || movement.Minimum.X > movement.Maximum.X || movement.Minimum.Y > movement.Maximum.Y || movement.Minimum.Z > movement.Maximum.Z
            || !float.IsFinite(movement.GravityScale) || !float.IsFinite(movement.Buoyancy)
            || !float.IsFinite(movement.ClimbReach) || movement.ClimbReach < 0f)
            throw new ArgumentOutOfRangeException(parameterName, "Engine movement requests require a defined mode and finite ordered values.");
    }

    /// <summary>
    /// Replaces this session's admitted spatial artifact. The current content reference remains owned
    /// until the Engine accepts the replacement, so a rejected destination leaves the source session live.
    /// </summary>
    public SpatialContentArtifactReplaceReceipt ReplaceContent(SpatialContentArtifact inputs)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SpatialMovementSystem));
        ArgumentNullException.ThrowIfNull(inputs);

        ContentReference? candidate = null;
        try
        {
            candidate = _contentService.ResolveReference(new ContentResolveRequest(inputs.Path, inputs.Sha256));
            SpatialContentArtifactReplaceReceipt receipt = _spatial.ReplaceContentArtifact(new SpatialContentArtifactReplaceRequest(
                _session,
                candidate,
                inputs.NavigationGridId,
                _tuning.NavigationChunkSize,
                _tuning.NavigationMaximumStepCells));
            ContentReference? previous = _content;
            _content = candidate;
            candidate = null;
            previous?.Dispose();
            return receipt;
        }
        finally
        {
            candidate?.Dispose();
        }
    }

    /// <summary>
    /// Applies an atomic set of precompiled spatial closures. The Engine owns their collision and
    /// navigation composition; this facade only resolves product references for the duration of the
    /// call and retains no content handles. Stable placement IDs belong to the product cell/profile
    /// owner and therefore survive unload/re-admission and origin rebases.
    /// </summary>
    public SpatialContentArtifactResidencyReceipt ApplyContentArtifactResidency(
        IEnumerable<SpatialContentArtifactPlacement> placements,
        IEnumerable<ulong> removals,
        ulong navigationGridId)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SpatialMovementSystem));
        ArgumentNullException.ThrowIfNull(placements);
        ArgumentNullException.ThrowIfNull(removals);
        SpatialContentArtifactPlacement[] selected = placements.ToArray();
        ulong[] removed = removals.ToArray();
        HashSet<ulong> ids = [];
        foreach (SpatialContentArtifactPlacement placement in selected)
        {
            placement.Validate();
            if (!ids.Add(placement.Id))
                throw new ArgumentException($"Spatial placement identity {placement.Id} is repeated.", nameof(placements));
        }
        if (removed.Any(id => id == 0) || removed.Distinct().Count() != removed.Length)
            throw new ArgumentException("Spatial placement removals must be distinct and non-zero.", nameof(removals));

        List<ContentReference> resolved = [];
        try
        {
            SpatialContentArtifactInstance[] instances = new SpatialContentArtifactInstance[selected.Length];
            for (int index = 0; index < selected.Length; index++)
            {
                SpatialContentArtifactPlacement placement = selected[index];
                ContentReference reference = _contentService.ResolveReference(
                    new ContentResolveRequest(placement.Path, placement.Sha256));
                resolved.Add(reference);
                instances[index] = new SpatialContentArtifactInstance(placement.Id, reference,
                    placement.ColumnOffset, placement.LevelOffset, placement.RowOffset, placement.QuarterTurns,
                    placement.Translation);
            }

            return _spatial.ApplyContentArtifactResidency(new SpatialContentArtifactResidencyRequest(
                _session,
                instances,
                removed,
                navigationGridId,
                _tuning.NavigationChunkSize,
                _tuning.NavigationMaximumStepCells));
        }
        finally
        {
            foreach (ContentReference reference in resolved)
                reference.Dispose();
        }
    }

    /// <summary>
    /// Starts the next Engine character proposal at a newly selected world position without carrying
    /// motion or support references from the former position. The Engine still owns grounding on
    /// that next proposal; this product state only supplies its safe, detached starting point.
    /// </summary>
    public void Relocate(PlayerControlState player, WorldPoint position)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SpatialMovementSystem));
        ArgumentNullException.ThrowIfNull(player);
        position.Validate();
        player.Restore(position, default);
        _latestGeneration = null;
        _latestCheckpoint = null;
        _restoredCheckpoint = null;
    }

    /// <summary>Captures the Engine-owned continuation only at a completed proposal boundary.</summary>
    public CharacterContinuationCheckpoint CaptureContinuation()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SpatialMovementSystem));
        if (_restoredCheckpoint is { } restored) return restored;
        if (_latestCheckpoint is { } captured) return captured;
        if (_latestGeneration is not ulong generation)
            throw new InvalidOperationException("The spatial character has no completed proposal checkpoint.");
        return _spatial.CaptureCharacterContinuation(new CharacterContinuationCaptureRequest(_session, generation));
    }

    /// <summary>Restores an Engine-validated continuation into this otherwise fresh canonical session.</summary>
    public CharacterContinuationRestoreReceipt RestoreContinuation(CharacterContinuationCheckpoint checkpoint)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SpatialMovementSystem));
        if (_latestGeneration is not null)
            throw new InvalidOperationException("Spatial continuation can only be restored before the first proposal.");
        CharacterContinuationRestoreReceipt receipt = _spatial.RestoreCharacterContinuation(
            new CharacterContinuationRestoreRequest(_session, checkpoint));
        // Restore admission validates the full checkpoint but does not create
        // an Engine receipt in the fresh target session.  Keep the detached
        // checkpoint for an immediate re-save; use its config for the next
        // proposal so continuation compatibility remains explicit.
        _controller = checkpoint.Config;
        _verticalDriven = checkpoint.Config.Vertical.Gravity == 0f;
        _latestCheckpoint = null;
        _restoredCheckpoint = checkpoint;
        return receipt;
    }

    /// <summary>True when either an admitted receipt or a restored detached checkpoint can be saved.</summary>
    public bool HasContinuation => !_disposed && (_latestGeneration is not null || _restoredCheckpoint is not null);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        List<Exception>? failures = null;
        try { _session.Dispose(); }
        catch (Exception exception) { failures = [exception]; }
        try { _content?.Dispose(); }
        catch (Exception exception) { (failures ??= []).Add(exception); }
        if (failures is { Count: > 0 }) throw new AggregateException(failures);
    }
}

/// <summary>Ruleset-provided identity for one Engine-admitted spatial artifact; the Kit never reads its format.</summary>
public sealed record SpatialContentArtifact(string Path, ContentSha256 Sha256, ulong NavigationGridId);

/// <summary>One product-owned placement of a precompiled spatial closure in the current grid.</summary>
public sealed record SpatialContentArtifactPlacement(
    ulong Id,
    string Path,
    ContentSha256 Sha256,
    long ColumnOffset = 0,
    long LevelOffset = 0,
    long RowOffset = 0,
    uint QuarterTurns = 0,
    Vector3 Translation = default)
{
    internal void Validate()
    {
        if (Id == 0) throw new ArgumentOutOfRangeException(nameof(Id));
        ArgumentException.ThrowIfNullOrWhiteSpace(Path);
        if (QuarterTurns > 3) throw new ArgumentOutOfRangeException(nameof(QuarterTurns));
        if (!float.IsFinite(Translation.X) || !float.IsFinite(Translation.Y) || !float.IsFinite(Translation.Z))
            throw new ArgumentOutOfRangeException(nameof(Translation), "Spatial placement translation must be finite.");
    }
}
