using System.Numerics;
using Rusty.Engine;

namespace WorldRpg.Kit.Controls;

/// <summary>Call-local facts supplied for one proposal; the Engine does not retain product support or obstacle ownership.</summary>
public readonly record struct CharacterStepEnvironment(CharacterSupport Support, ReadOnlyMemory<CharacterObstacle> Obstacles)
{
    public static CharacterStepEnvironment Empty { get; } = new(default, ReadOnlyMemory<CharacterObstacle>.Empty);
}

/// <summary>Ruleset-selected controls and speeds for one Engine-owned character proposal.</summary>
public readonly record struct CharacterStepControls(
    bool JumpPressed = false,
    bool JumpHeld = false,
    bool CrouchRequested = false,
    float? ForwardSpeed = null,
    float? BackwardSpeed = null,
    float? StrafeSpeed = null,
    float? JumpSpeed = null)
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
                spatial.ReplaceContentArtifact(new SpatialContentArtifactReplaceRequest(
                    session,
                    resolved,
                    inputs.NavigationGridId,
                    tuning.NavigationChunkSize,
                    tuning.NavigationMaximumStepCells));
                _content = resolved;
                resolved = null!;
            }
            finally { resolved?.Dispose(); }
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
        CharacterControllerCommand command = new(
            update.PlanarIntent,
            player.YawRadians,
            selected.JumpPressed,
            selected.JumpHeld,
            selected.CrouchRequested,
            ExternalVelocity: Vector3.Zero,
            ExternalImpulse: Vector3.Zero,
            update.DeltaSeconds,
            sequence);
        CharacterStepRequest request = new(
            _session,
            position.ToVector(),
            player.Motion,
            stepEnvironment.Support,
            stepEnvironment.Obstacles,
            selected.ApplyTo(_controller),
            command);
        CharacterStepReceipt receipt = _spatial.ProposeCharacterStep(request);
        _latestGeneration = receipt.Generation;
        _restoredCheckpoint = null;
        player.Apply(receipt);
        return receipt;
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
