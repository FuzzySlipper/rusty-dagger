using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.World;

/// <summary>
/// Builds exterior cells' ground on the thread pool ahead of the window that needs it, so crossing into a new map pixel
/// does not build a row of cells in one update. A build reads only terrain and location definitions and ground tuning,
/// which nothing changes, with the location's ground already read on the update, and calls no Engine service: the pure
/// product computation the Engine's call-affinity contract lets a product run on its own workers. Results are taken
/// only on the update. A cell the update needs before a worker has taken up its build is built on the update itself,
/// so the update never waits behind other work queued on the thread pool; it waits only for a build a worker is
/// already running (a travel's first window, or a crossing faster than the build).
/// </summary>
internal sealed class DaggerfallExteriorCellGrounds(
    DaggerfallTerrainSet terrain,
    IReadOnlyDictionary<DaggerfallExteriorCellId, DaggerfallSiteExterior> locations,
    DaggerfallGroundTuning tuning) : IDisposable
{
    private readonly Dictionary<DaggerfallExteriorCellId, CellBuild> _cells = [];
    private CancellationTokenSource _stopping = new();

    /// <summary>
    /// Holds exactly these cells, starting a build for each one not yet held, in the order given. The window's cells
    /// come first so they finish first. A build of a cell let go of still finishes, and its result is dropped.
    /// </summary>
    internal void Hold(IEnumerable<DaggerfallExteriorCellId> cells)
    {
        List<DaggerfallExteriorCellId> wanted = [.. cells];
        HashSet<DaggerfallExteriorCellId> kept = [.. wanted];
        foreach (DaggerfallExteriorCellId cell in _cells.Keys.Where(cell => !kept.Contains(cell)).ToArray())
            _cells.Remove(cell);
        foreach (DaggerfallExteriorCellId cell in wanted)
            if (!_cells.ContainsKey(cell)) _cells.Add(cell, Start(cell));
    }

    /// <summary>
    /// A cell's ground: built here if no worker has taken up its build, waited for if one is running it. One not held is
    /// built here and kept.
    /// </summary>
    internal DaggerfallExteriorCellGround Ground(DaggerfallExteriorCellId cell)
    {
        if (!_cells.TryGetValue(cell, out CellBuild? build))
            _cells.Add(cell, build = Prepare(cell));
        build.Run();
        return build.Result.GetAwaiter().GetResult();
    }

    /// <summary>Whether a cell's ground is held, built or building.</summary>
    internal bool Holds(DaggerfallExteriorCellId cell) => _cells.ContainsKey(cell);

    /// <summary>Lets go of every cell; builds not yet started never run.</summary>
    internal void Clear()
    {
        _stopping.Cancel();
        _stopping.Dispose();
        _stopping = new();
        _cells.Clear();
    }

    public void Dispose()
    {
        _stopping.Cancel();
        _stopping.Dispose();
        _cells.Clear();
    }

    private CellBuild Start(DaggerfallExteriorCellId cell)
    {
        CellBuild build = Prepare(cell);
        CancellationToken stopping = _stopping.Token;
        _ = Task.Run(() => { if (!stopping.IsCancellationRequested) build.Run(); }, stopping);
        return build;
    }

    private CellBuild Prepare(DaggerfallExteriorCellId cell)
    {
        // The location and its ground are read here, on the update, so the build is handed its inputs: its ground is
        // read from the data container, which no worker may do.
        if (locations.TryGetValue(cell, out DaggerfallSiteExterior? location))
            location = location with { GroundTiles = location.GroundTiles };
        return new(() => Build(terrain, cell, location, tuning));
    }

    private static DaggerfallExteriorCellGround Build(
        DaggerfallTerrainSet terrain, DaggerfallExteriorCellId cell, DaggerfallSiteExterior? location, DaggerfallGroundTuning tuning)
    {
        DaggerfallTerrainSurface surface = DaggerfallTerrainSurfaceBuilder.Build(terrain, cell.X, cell.Y);
        if (location is not null)
            surface = DaggerfallTerrainSurfaceBuilder.ApplyLocationFlattening(surface, DaggerfallLocationTerrainFrame.Flattening(location));
        return DaggerfallExteriorEnvironment.Ground(cell, surface, location, tuning);
    }

    /// <summary>One cell's build, run once by whichever of a worker and the update takes it up first.</summary>
    private sealed class CellBuild(Func<DaggerfallExteriorCellGround> build)
    {
        private readonly TaskCompletionSource<DaggerfallExteriorCellGround> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _taken;

        internal Task<DaggerfallExteriorCellGround> Result => _result.Task;

        internal void Run()
        {
            if (Interlocked.Exchange(ref _taken, 1) != 0) return;
            try
            {
                _result.SetResult(build());
            }
            catch (Exception failure)
            {
                _result.SetException(failure);
            }
        }
    }
}
