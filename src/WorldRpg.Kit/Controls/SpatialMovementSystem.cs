using System.Numerics;
using Rusty.Engine;

namespace WorldRpg.Kit.Controls;

/// <summary>Call-local facts supplied for one proposal; the Engine does not retain product support or obstacle ownership.</summary>
public readonly record struct CharacterStepEnvironment(CharacterSupport Support, ReadOnlyMemory<CharacterObstacle> Obstacles)
{
    public static CharacterStepEnvironment Empty { get; } = new(default, ReadOnlyMemory<CharacterObstacle>.Empty);
}

/// <summary>An Engine-validated, owner-bound character proposal that can be submitted once.</summary>
public sealed class PreparedSpatialStep
{
    private readonly SpatialMovementSystem _owner;
    private readonly PlayerControlState _player;
    private readonly WorldPoint _position;
    private readonly CharacterMotion _motion;
    private readonly CharacterStepRequest _request;
    private bool _consumed;

    internal PreparedSpatialStep(SpatialMovementSystem owner, PlayerControlState player, WorldPoint position, CharacterMotion motion, CharacterStepRequest request)
    {
        _owner = owner;
        _player = player;
        _position = position;
        _motion = motion;
        _request = request;
    }

    internal CharacterStepRequest ConsumeBy(SpatialMovementSystem owner)
    {
        if (!ReferenceEquals(_owner, owner)) throw new InvalidOperationException("Prepared spatial step belongs to a different spatial system.");
        if (_consumed) throw new InvalidOperationException("Prepared spatial step was already consumed.");
        if (_player.Position != _position || _player.Motion != _motion) throw new InvalidOperationException("Prepared spatial step is stale relative to player continuation state.");
        _consumed = true;
        return _request;
    }
}

/// <summary>Owns one Engine spatial session and persistent character continuation.</summary>
public sealed class SpatialMovementSystem : IDisposable
{
    private readonly ISpatialService _spatial;
    private readonly ContentReference _content;
    private readonly SpatialTuning _tuning;
    private CharacterControllerConfig _controller;
    private readonly SpatialSession _session;
    private ulong? _latestGeneration;
    private CharacterContinuationCheckpoint? _restoredCheckpoint;
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

    public SpatialMovementSystem(ISpatialService spatial, IContentService content, SpatialContentArtifact inputs, SpatialTuning tuning)
    {
        ArgumentNullException.ThrowIfNull(spatial);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(inputs);
        tuning = (tuning ?? throw new ArgumentNullException(nameof(tuning))).Validate();
        _spatial = spatial;
        _tuning = tuning;
        CharacterControllerConfig defaults = spatial.DefaultCharacterControllerConfig();
        _controller = (tuning.CharacterController ?? new CharacterControllerTuning()).ApplyTo(defaults);
        _spatial.ValidateCharacterControllerConfig(_controller);
        SpatialSession session = spatial.CreateSession(new SpatialSessionConfig(
            tuning.CollisionVoxelSize,
            tuning.CollisionChunkSize,
            VoxelSurfaceMode.GreedyCubes));
        try
        {
            ContentReference resolved = content.ResolveReference(new ContentResolveRequest(inputs.Path, inputs.Sha256));
            try
            {
                ReadOnlyMemory<ContentReferenceInfo> references = content.ReadReferenceInfo(resolved);
                if (references.Length != 1 || references.Span[0].Path != inputs.Path || references.Span[0].Sha256 != inputs.Sha256)
                {
                    throw new InvalidOperationException("Resolved spatial artifact does not retain its expected content identity.");
                }

                SpatialContentArtifactReplaceReceipt receipt = spatial.ReplaceContentArtifact(new SpatialContentArtifactReplaceRequest(
                    session,
                    resolved,
                    inputs.NavigationGridId,
                    tuning.NavigationChunkSize,
                    tuning.NavigationMaximumStepCells));
                SpatialContentArtifactReadout readback = spatial.ReadContentArtifact(new SpatialContentArtifactReadRequest(session));
                if (!readback.Present
                    || readback.ContentReferenceValue != resolved.Handle.Value
                    || readback.ContentSha256 != inputs.Sha256
                    || readback.CollisionRevision != receipt.CollisionRevisionAfter
                    || readback.NavigationRevision != receipt.NavigationRevision
                    || readback.CollisionVertexCount != receipt.CollisionVertexCount
                    || readback.CollisionTriangleCount != receipt.CollisionTriangleCount
                    || readback.NavigationCellCount != receipt.NavigationCellCount)
                {
                    throw new InvalidOperationException("Engine spatial artifact readback did not match the admitted content replacement.");
                }

                _content = resolved;
                resolved = null!;
            }
            finally { resolved?.Dispose(); }
            _session = session;
        }
        catch { session.Dispose(); throw; }
    }

    /// <summary>Builds and Engine-validates a command without changing product continuation state.</summary>
    public PreparedSpatialStep? Prepare(PlayerControlState player, ProductUpdateState update, PreparedPlayerInput input, CharacterStepEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(input);
        return PrepareCore(player, update, input.PlanarIntent, input.YawRadians, environment);
    }

    private PreparedSpatialStep? PrepareCore(PlayerControlState player, ProductUpdateState update, Vector2 planarIntent, float yawRadians, CharacterStepEnvironment environment)
    {
        if (_disposed) return null;
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(update);
        player.ValidateForInput();
        update.Validate();
        if (player.Position is not WorldPoint position) return null;

        ulong sequence = checked(player.Motion.LastCommandSequence + 1);
        CharacterControllerCommand command = new(
            planarIntent,
            yawRadians,
            JumpPressed: false,
            JumpHeld: false,
            CrouchRequested: false,
            ExternalVelocity: Vector3.Zero,
            ExternalImpulse: Vector3.Zero,
            update.DeltaSeconds,
            sequence);
        _spatial.ValidateCharacterControllerCommand(new CharacterControllerValidationRequest(_controller, command));
        CharacterStepRequest request = new(
            _session,
            position.ToVector(),
            player.Motion,
            environment.Support,
            environment.Obstacles,
            _controller,
            command);
        return new PreparedSpatialStep(this, player, position, player.Motion, request);
    }

    /// <summary>Submits an already validated candidate. Receipt application remains caller-controlled until this returns.</summary>
    public CharacterStepReceipt Propose(PreparedSpatialStep step)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SpatialMovementSystem));
        ArgumentNullException.ThrowIfNull(step);
        CharacterStepReceipt receipt = _spatial.ProposeCharacterStep(step.ConsumeBy(this));
        _latestGeneration = receipt.Generation;
        _restoredCheckpoint = null;
        return receipt;
    }

    /// <summary>Convenience path for callers that have no separate staged input interpreter.</summary>
    public void Step(PlayerControlState player, ProductUpdateState update, CharacterStepEnvironment? environment = null)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(update);
        PreparedSpatialStep? prepared = PrepareCore(player, update, update.PlanarIntent, player.YawRadians, environment ?? CharacterStepEnvironment.Empty);
        if (prepared is { } step) player.Apply(Propose(step));
    }

    /// <summary>Captures the Engine-owned continuation only at a completed proposal boundary.</summary>
    public CharacterContinuationCheckpoint CaptureContinuation()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SpatialMovementSystem));
        if (_restoredCheckpoint is { } restored) return restored;
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
        try { _content.Dispose(); }
        catch (Exception exception) { (failures ??= []).Add(exception); }
        if (failures is { Count: > 0 }) throw new AggregateException(failures);
    }
}

/// <summary>Ruleset-provided identity for one Engine-admitted spatial artifact; the Kit never reads its format.</summary>
public sealed record SpatialContentArtifact(string Path, ContentSha256 Sha256, ulong NavigationGridId);
