using Rusty.Engine;

namespace WorldRpg.Rulesets.Daggerfall.World;

/// <summary>How far the collision a staged site change handed to the Engine to prepare off the update has come.</summary>
internal enum DaggerfallDeferredCollisionState
{
    /// <summary>Nothing is being prepared: what was handed over is live.</summary>
    Live,
    /// <summary>A batch is still validating, baking or composing.</summary>
    Preparing,
    /// <summary>The Engine refused at least one batch, so none of that batch is live; the host log names why.</summary>
    Refused,
}

/// <summary>One reading of the deferred collision.</summary>
/// <param name="Fraction">
/// While preparing, the share done: batches the Engine finished of those ahead of and including these, plus the
/// share of the batch being prepared (its navigation cells composed, or else its instances baked). Null while
/// that batch still validates or parses and none is finished.
/// </param>
internal readonly record struct DaggerfallDeferredCollisionReading(DaggerfallDeferredCollisionState State, double? Fraction = null)
{
    internal static DaggerfallDeferredCollisionReading Live => new(DaggerfallDeferredCollisionState.Live, 1d);
}

/// <summary>
/// Follows the deferred batches a staged site change hands the Engine (its location placements and its terrain
/// window) in the session's one collision residency order, until each is live or refused. Only a staged change
/// defers, and it waits for its batches before anything else changes the session, so every refusal the Engine
/// counts after the first of them was made is one of these.
/// </summary>
internal sealed class DaggerfallDeferredCollision(ISpatialService spatial, SpatialSession session)
{
    private readonly ISpatialService _spatial = spatial ?? throw new ArgumentNullException(nameof(spatial));
    private readonly List<(ulong Batch, Action Refused)> _batches = [];
    private ulong _refusedBefore;
    private uint _ahead;

    /// <summary>Whether batches handed over are still to be read live or refused.</summary>
    internal bool Awaiting => _batches.Count != 0;

    /// <summary>The session's deferred collision now; read before a batch is made, it is what that batch follows.</summary>
    internal CollisionResidencyReadout Read() => _spatial.ReadCollisionResidency(new CollisionResidencyReadRequest(session));

    /// <summary>
    /// Follows a batch the Engine accepted to prepare off the update; nothing for one it applied in the call (0).
    /// </summary>
    /// <param name="before">The readout taken before the batch was made.</param>
    /// <param name="refused">Returns its owner to what it had before the batch, should the Engine refuse it.</param>
    internal void Track(ulong batch, CollisionResidencyReadout before, Action refused)
    {
        ArgumentNullException.ThrowIfNull(refused);
        if (batch == 0) return;
        if (_batches.Count == 0)
        {
            _refusedBefore = before.RefusedBatches;
            _ahead = before.PendingBatches;
        }
        _batches.Add((batch, refused));
    }

    /// <summary>
    /// Reads how far the batches have come. Once every one is done, a refused one returns its owner to what it had
    /// before it, and the reading says so; the batches are then no longer followed.
    /// </summary>
    internal DaggerfallDeferredCollisionReading Poll()
    {
        if (_batches.Count == 0) return DaggerfallDeferredCollisionReading.Live;
        CollisionResidencyReadout readout = Read();
        if (_batches.Any(batch => readout.CompletedBatch < batch.Batch))
        {
            double total = _ahead + (double)_batches.Count;
            double done = total - Math.Min(readout.PendingBatches, total);
            double? current = readout.PlannedCells != 0 ? (double)readout.ComposedCells / readout.PlannedCells
                : readout.PlannedInstances != 0 ? (double)readout.BakedInstances / readout.PlannedInstances
                : null;
            return new(DaggerfallDeferredCollisionState.Preparing,
                current is null && done == 0 ? null : (done + (current ?? 0d)) / total);
        }
        // As many refusals as batches refuse them all; fewer (one of two) are the one the Engine names last.
        ulong refusals = readout.RefusedBatches - _refusedBefore;
        (ulong Batch, Action Refused)[] refused = [.. _batches.Where(batch =>
            refusals >= (ulong)_batches.Count || (refusals != 0 && batch.Batch == readout.LastRefusedBatch))];
        _batches.Clear();
        foreach ((ulong _, Action revert) in refused) revert();
        return refused.Length == 0
            ? DaggerfallDeferredCollisionReading.Live
            : new(DaggerfallDeferredCollisionState.Refused);
    }
}
