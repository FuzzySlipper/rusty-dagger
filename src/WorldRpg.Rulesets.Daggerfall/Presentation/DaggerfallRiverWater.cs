using System.Numerics;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>
/// River water about the player: one mesh a map pixel, a ribbon along each course it holds, each section level across and
/// falling with the river. The sea has one height, so it fills a channel only where that reaches the coast; everywhere else
/// the ribbon stands at the river's own surface. Sections sit on the courses' stations, so neighbouring pixels meet exactly.
/// A mesh leaves the snapshot when it falls behind and is let go once a snapshot without it has been accepted.
/// </summary>
internal sealed class DaggerfallRiverWater(IEngineContext engine, DaggerfallTerrainRivers rivers, Material water, float repeatMetres)
{
    /// <summary>How many map pixels either side of the player hold ribbons: as far as the walked ground holds a channel.</summary>
    private const int Reach = 2;
    /// <summary>How many meshes are built an update, so a walk into new water never stalls one.</summary>
    private const int BuildsPerUpdate = 2;
    private const double CellSize = DaggerfallExteriorCellResidency.CellSize;

    private sealed record Chunk(MeshResource Mesh, Appearance Look);

    private readonly Dictionary<(int X, int Y), Chunk?> _chunks = [];
    private readonly List<Chunk> _retiring = [];
    private readonly List<(ulong Id, Appearance Look, Vector3 At)> _shown = [];

    /// <summary>Keeps the pixels about a player at map metres (x, z), nearest first, and lays out what stands.</summary>
    internal void Follow(DaggerfallExteriorWorldOrigin origin, double x, double z)
    {
        // Heightmap pixel (X, Y) is map pixel (X + 1, Y): the courses are measured from the heightmap's corner.
        int px = (int)Math.Floor(x / CellSize) - 1, py = (int)Math.Floor(z / CellSize);
        foreach ((int X, int Y) key in _chunks.Keys.Where(key => Math.Max(Math.Abs(key.X - px), Math.Abs(key.Y - py)) > Reach).ToArray())
        {
            if (_chunks[key] is { } chunk) _retiring.Add(chunk);
            _chunks.Remove(key);
        }

        int built = 0;
        for (int ring = 0; ring <= Reach && built < BuildsPerUpdate; ring++)
        for (int y = py - ring; y <= py + ring && built < BuildsPerUpdate; y++)
        for (int column = px - ring; column <= px + ring && built < BuildsPerUpdate; column++)
        {
            if (Math.Max(Math.Abs(column - px), Math.Abs(y - py)) != ring || _chunks.ContainsKey((column, y))) continue;
            _chunks[(column, y)] = Build(column, y);
            built++;
        }

        _shown.Clear();
        foreach (((int X, int Y) key, Chunk? chunk) in _chunks)
        {
            if (chunk is null) continue;
            Vector3 corner = new((float)((key.X + 1 - origin.MapPixelX) * CellSize), 0, (float)((key.Y - origin.MapPixelY) * CellSize));
            _shown.Add((DaggerfallPresentationObjectIds.River(key.X, key.Y), chunk.Look, corner + origin.Compensation));
        }
    }

    internal void AppendFacts(List<AppearanceFact> facts)
    {
        foreach ((ulong id, Appearance look, Vector3 at) in _shown)
            facts.Add(new AppearanceFact(id, false, 0, new Transform(at, Quaternion.Identity, Vector3.One), look, true, RenderLayer.Scene));
    }

    /// <summary>Takes the ribbons out of the snapshot, keeping their meshes for the next time outdoors.</summary>
    internal void Hide() => _shown.Clear();

    /// <summary>A snapshot was accepted, and it held none of the meshes that left: they go now.</summary>
    internal void CompleteSnapshot()
    {
        foreach (Chunk chunk in _retiring) Dispose(chunk);
        _retiring.Clear();
    }

    /// <summary>Lets every mesh go, once a snapshot without them has been accepted.</summary>
    internal void Release()
    {
        CompleteSnapshot();
        foreach (Chunk? chunk in _chunks.Values)
            if (chunk is not null) Dispose(chunk);
        _chunks.Clear();
        _shown.Clear();
    }

    private static void Dispose(Chunk chunk)
    {
        chunk.Look.Dispose();
        chunk.Mesh.Dispose();
    }

    private Chunk? Build(int px, int py)
    {
        List<Vector3> positions = [];
        List<Vector2> uvs = [];
        List<uint> indices = [];
        double cornerX = px * CellSize, cornerZ = py * CellSize;
        foreach (DaggerfallRiverSection section in rivers.Sections(px, py))
        {
            uint at = (uint)positions.Count;
            Add(section.From);
            Add(section.To);
            // Wound as the sea's sheet is, so the surface faces up: from's left and right, then to's.
            indices.AddRange([at, at + 1, at + 2, at + 1, at + 3, at + 2]);
        }

        void Add(DaggerfallRiverSpan span)
        {
            foreach ((double x, double z) in new[] { (span.LeftX, span.LeftZ), (span.RightX, span.RightZ) })
            {
                positions.Add(new((float)(x - cornerX), (float)span.Surface, (float)(z - cornerZ)));
                // The art repeats from the map's corner, so it runs on from pixel to pixel and into the sea's sheets.
                uvs.Add(new((float)(x % (repeatMetres * 1024) / repeatMetres), (float)(z % (repeatMetres * 1024) / repeatMetres)));
            }
        }

        if (indices.Count == 0) return null;
        Vector3[] normals = new Vector3[positions.Count];
        Array.Fill(normals, Vector3.UnitY);
        MeshResource mesh = engine.Graphics.CreateMeshResource(new MeshResourceCreateRequest(positions.ToArray(), normals, uvs.ToArray(), indices.ToArray(),
            new MeshGroup[] { new(0, 0, (uint)indices.Count) }, new MeshMaterialBinding[] { new(0, water) }));
        return new(mesh, engine.Graphics.CreateMeshAppearance(mesh));
    }
}
