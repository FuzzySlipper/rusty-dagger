using Rusty.Engine;

namespace WorldRpg.Kit.Presentation;

/// <summary>
/// Waits, as a loading screen's last step, until the scene a load put up is drawn: every batch of textures, meshes and
/// product shaders the Engine had been asked to prepare when the wait began is taken in. Until a batch is taken in,
/// whatever draws its resources draws nothing, so a load that ended before them would show an empty or half-drawn
/// scene while the world already runs. A batch that failed counts as drawn: the renderer reports it, and the wait
/// does not outlast it.
/// </summary>
/// <param name="read">The Engine's preparation readout, read in the update that asks.</param>
public sealed class DrawnScene(Func<ResourcePreparationReadout> read)
{
    private readonly Func<ResourcePreparationReadout> _read = read ?? throw new ArgumentNullException(nameof(read));
    private (ulong Batch, uint Pending)? _awaited;

    /// <summary>
    /// How far the scene has come. The first call after a finished wait fixes the batches waited for (those opened by
    /// then); resources opened later, such as ground streaming in as play goes on, do not hold the load.
    /// </summary>
    public LoadingProgress Await()
    {
        ResourcePreparationReadout readout = _read();
        (ulong batch, uint pending) = _awaited ??= (readout.LastBatch, readout.PendingResources);
        if (readout.CompletedBatch >= batch)
        {
            _awaited = null;
            return LoadingProgress.Finished;
        }
        // Later batches count among the pending too, so the share is a reading, not a promise.
        return pending == 0 ? LoadingProgress.Unmeasured
            : LoadingProgress.Measured(1d - (double)Math.Min(readout.PendingResources, pending) / pending);
    }
}
