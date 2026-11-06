using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Heightfields;

namespace WorldRpg.Rulesets.Daggerfall.World;

/// <summary>
/// The ground exterior characters stand on: an Engine heightfield collider reading the function near ground draws, in
/// the same frame and on the same cells as near ground's finest ring, so feet and the drawn surface agree. It holds the
/// ground about each focus, which is the player and anyone walking the wilderness; the people of a town stand on the
/// town's own ground plane past its reach. The frame is one map pixel's, as near ground's is: travel makes a new
/// collider, while an Engine origin rebase carries this one with the session's other collision.
/// </summary>
internal sealed class DaggerfallExteriorGroundCollider : IDisposable
{
    /// <summary>Near ground's finest ring reaches this far, so nobody walks a coarser surface than they see.</summary>
    internal const double ReachMetres = 409.6;
    /// <summary>The ground an arrival waits for; the rest of the reach streams in around the player.</summary>
    private const double ArrivalReachMetres = 64;
    /// <summary>Two near tiles along a side: the same cells, in half as many collision tiles.</summary>
    private const uint TileCells = DaggerfallHorizonGround.TileCells * 2;
    private const int CommitsPerUpdate = 8;

    private readonly ISpatialService _spatial;
    private readonly HeightfieldCollider _collider;
    private readonly HeightfieldColliderStreamer _streamer;
    private readonly List<HeightfieldColliderFocus> _focuses = [];

    internal DaggerfallExteriorGroundCollider(IEngineContext engine, SpatialSession session, DaggerfallHorizonGround ground, DaggerfallExteriorWorldOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(ground);
        _spatial = engine.Spatial;
        MapPixel = (origin.MapPixelX, origin.MapPixelY);
        _collider = _spatial.CreateHeightfieldCollider(new(session, TileCells, DaggerfallHorizonGround.FinestNearCellMetres, ReachMetres, default));
        double offsetX = origin.MapPixelX * (double)DaggerfallExteriorCellResidency.CellSize, offsetZ = origin.MapPixelY * (double)DaggerfallExteriorCellResidency.CellSize;
        _streamer = new HeightfieldColliderStreamer(engine, _collider, new Heights(ground, offsetX, offsetZ), CommitsPerUpdate);
    }

    /// <summary>The map pixel this collider's frame stands on.</summary>
    internal (int X, int Y) MapPixel { get; }

    /// <summary>Holds the ground about these local points in <paramref name="origin"/>'s frame, and takes what has been sampled.</summary>
    internal void Update(IEnumerable<Vector3> focuses, DaggerfallExteriorWorldOrigin origin)
    {
        _focuses.Clear();
        foreach (Vector3 focus in focuses)
        {
            Vector3 frame = focus - origin.Compensation;
            _focuses.Add(new(frame.X, frame.Z));
        }
        _spatial.SetHeightfieldColliderFocuses(new(_collider, _focuses.ToArray()));
        _streamer.Update();
    }

    /// <summary>
    /// Whether the ground stands about every focus, far enough to arrive on. Anyone a focus stands for falls without it,
    /// and ground arriving late would install around them, so one focus still waiting is the whole answer.
    /// </summary>
    internal HeightfieldColliderReadout Read()
    {
        bool ready = true;
        uint resident = 0, pending = 0, refused = 0;
        foreach (HeightfieldColliderFocus focus in _focuses)
        {
            HeightfieldColliderReadout read = _spatial.ReadHeightfieldCollider(new(_collider, focus.X, focus.Z, ArrivalReachMetres));
            ready &= read.Ready;
            (resident, pending, refused) = (read.ResidentTiles, read.PendingTiles, read.RefusedTiles);
        }
        return new(ready, resident, pending, refused);
    }

    public void Dispose()
    {
        _streamer.Dispose();
        _collider.Dispose();
    }

    private sealed class Heights(DaggerfallHorizonGround ground, double offsetX, double offsetZ) : IHeightfieldSampler
    {
        public void Sample(HeightfieldTileRequest tile, HeightfieldTileSamples samples) => ground.SampleHeights(tile, samples, offsetX, offsetZ);
    }
}
