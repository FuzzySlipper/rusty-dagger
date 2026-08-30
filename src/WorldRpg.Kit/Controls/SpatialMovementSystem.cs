using System.Numerics;
using Rusty.Engine;
namespace WorldRpg.Kit.Controls;

/// <summary>Owns one Engine spatial session and persistent character continuation.</summary>
public sealed class SpatialMovementSystem : IDisposable
{
    private readonly ISpatialService _spatial;
    private readonly ContentReference _content;
    private readonly SpatialTuning _tuning;
    private readonly CharacterControllerConfig _controller;
    private readonly SpatialSession _session;
    private ulong _movementSequence;
    private bool _disposed;

    public SpatialMovementSystem(ISpatialService spatial, IContentService content, SpatialContentArtifact inputs, SpatialTuning tuning)
    {
        ArgumentNullException.ThrowIfNull(spatial);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(inputs);
        _spatial = spatial;
        _tuning = tuning;
        _controller = spatial.DefaultCharacterControllerConfig();
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

    public void Step(PlayerControlState player, ProductUpdateState update)
    {
        if (_disposed || player.Position is not WorldPoint position) return;
        var receipt = _spatial.ProposeCharacterStep(new CharacterStepRequest(
            _session,
            position.ToVector(),
            player.Motion,
            default,
            ReadOnlyMemory<CharacterObstacle>.Empty,
            _controller,
            new CharacterControllerCommand(
                update.PlanarIntent,
                player.YawRadians,
                JumpPressed: false,
                JumpHeld: false,
                CrouchRequested: false,
                ExternalVelocity: Vector3.Zero,
                ExternalImpulse: Vector3.Zero,
                update.DeltaSeconds,
                ++_movementSequence)));
        player.MoveTo(receipt.Transform.Translation);
        player.Motion = receipt.Motion;
    }

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
