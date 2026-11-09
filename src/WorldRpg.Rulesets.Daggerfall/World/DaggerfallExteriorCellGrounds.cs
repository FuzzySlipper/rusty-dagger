using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.World;

/// <summary>
/// Builds exterior cells' ground on the thread pool ahead of the window that needs it, so crossing into a new map pixel
/// does not build a row of cells in one update. A build reads only terrain and location definitions and ground tuning,
/// which nothing changes, and calls no Engine service: the pure product computation the Engine's call-affinity contract lets a
/// product run on its own workers. Results are taken only on the update, and the update waits only for a cell that
/// is not finished when it is admitted (a travel's first window, or a crossing faster than the build).
/// </summary>
internal sealed class DaggerfallExteriorCellGrounds(
    DaggerfallTerrainSet terrain,
    IReadOnlyDictionary<DaggerfallExteriorCellId, DaggerfallSiteExterior> locations,
    DaggerfallGroundTuning tuning) : IDisposable
{
    private readonly Dictionary<DaggerfallExteriorCellId, Task<DaggerfallExteriorCellGround>> _cells = [];
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

    /// <summary>A cell's ground, waiting for its build if it is still running; one not held is built and kept.</summary>
    internal DaggerfallExteriorCellGround Ground(DaggerfallExteriorCellId cell)
    {
        if (!_cells.TryGetValue(cell, out Task<DaggerfallExteriorCellGround>? build))
            _cells.Add(cell, build = Start(cell));
        // A build that has not started yet runs here instead.
        return build.GetAwaiter().GetResult();
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

    private Task<DaggerfallExteriorCellGround> Start(DaggerfallExteriorCellId cell)
    {
        // The location is read here, on the update, so the build is handed its inputs.
        locations.TryGetValue(cell, out DaggerfallSiteExterior? location);
        return Task.Run(() => Build(terrain, cell, location, tuning), _stopping.Token);
    }

    private static DaggerfallExteriorCellGround Build(
        DaggerfallTerrainSet terrain, DaggerfallExteriorCellId cell, DaggerfallSiteExterior? location, DaggerfallGroundTuning tuning)
    {
        DaggerfallTerrainSurface surface = DaggerfallTerrainSurfaceBuilder.Build(terrain, cell.X, cell.Y);
        if (location is not null)
            surface = DaggerfallTerrainSurfaceBuilder.ApplyLocationFlattening(surface, DaggerfallLocationTerrainFrame.Flattening(location));
        return DaggerfallExteriorEnvironment.Ground(cell, surface, location, tuning);
    }
}
