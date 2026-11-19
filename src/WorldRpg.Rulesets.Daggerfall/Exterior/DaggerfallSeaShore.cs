using System.Numerics;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>
/// The sea's fine tiles where it meets the shore (#10910). Waves die in shallow water, and on Daggerfall's low beaches a
/// trough would bare the sand in blotches and a crest flood it. So a fine tile with ground close under its water gets a
/// mesh of its own, whose normals carry how shallow each vertex's water is: the sea's shader reads that as how little the
/// waves move it, none at the waterline and all of them past <see cref="ShoalMetres"/> of depth. A tile deep all over
/// draws with the shared fine mesh. Built a few an update, and let go once a snapshot without them has been accepted.
/// </summary>
internal sealed class DaggerfallSeaShore(IEngineContext engine, DaggerfallHorizonGround ground, Material water, float repeatMetres)
{
    /// <summary>The waves stand at their full height over water this deep, metres.</summary>
    internal const double ShoalMetres = 4;
    /// <summary>How many depths are read along a tile's side; the vertices between take theirs between them.</summary>
    private const int Samples = 26;
    private const int BuildsPerUpdate = 4;

    private sealed record Piece(MeshResource Mesh, Appearance Look);

    private readonly Dictionary<ulong, Piece?> _tiles = [];
    private readonly HashSet<ulong> _used = [];
    private readonly List<Piece> _retiring = [];
    private int _built;

    /// <summary>A new layout of the sea's tiles begins.</summary>
    internal void Begin()
    {
        _used.Clear();
        _built = 0;
    }

    /// <summary>
    /// The look of the fine tile <paramref name="id"/> whose least corner is map point (<paramref name="x"/>,
    /// <paramref name="z"/>), <paramref name="side"/> metres and <paramref name="cells"/> cells a side, if its water is
    /// shallow anywhere; null where it is deep all over, or not yet read.
    /// </summary>
    internal Appearance? Look(ulong id, double x, double z, double side, int cells)
    {
        _used.Add(id);
        if (_tiles.TryGetValue(id, out Piece? piece)) return piece?.Look;
        if (_built >= BuildsPerUpdate) return null;
        _built++;
        float[]? shallow = Shallows(x, z, side);
        piece = shallow is null ? null : Build(side, cells, shallow);
        _tiles[id] = piece;
        return piece?.Look;
    }

    /// <summary>The layout is complete: tiles it left out go once a snapshot without them is accepted.</summary>
    internal void End()
    {
        foreach (ulong id in _tiles.Keys.Where(id => !_used.Contains(id)).ToArray())
        {
            if (_tiles[id] is { } piece) _retiring.Add(piece);
            _tiles.Remove(id);
        }
    }

    /// <summary>A snapshot was accepted, and it held none of the meshes that left: they go now.</summary>
    internal void CompleteSnapshot()
    {
        foreach (Piece piece in _retiring) Dispose(piece);
        _retiring.Clear();
    }

    /// <summary>Lets every mesh go, once a snapshot without them has been accepted.</summary>
    internal void Release()
    {
        CompleteSnapshot();
        foreach (Piece? piece in _tiles.Values)
            if (piece is not null) Dispose(piece);
        _tiles.Clear();
    }

    /// <summary>
    /// How shallow the water is over a tile, on a lattice of <see cref="Samples"/> cells a side: 1 at the waterline (and on
    /// land), 0 at <see cref="ShoalMetres"/> deep and deeper; null where it is that deep everywhere.
    /// </summary>
    private float[]? Shallows(double x, double z, double side)
    {
        int count = Samples + 1, ringed = count + 2;
        double[] heights = ground.WalkedGrid(x, z, side / Samples, count);
        float[] shallow = new float[count * count];
        bool any = false;
        for (int j = 0; j < count; j++)
        for (int i = 0; i < count; i++)
        {
            double depth = DaggerfallHorizonGround.OceanMetres - heights[((j + 1) * ringed) + i + 1];
            double t = Math.Clamp(depth / ShoalMetres, 0, 1);
            shallow[(j * count) + i] = (float)(1 - (t * t * (3 - (2 * t))));
            any |= shallow[(j * count) + i] > 0;
        }
        return any ? shallow : null;
    }

    private Piece Build(double side, int cells, float[] shallow)
    {
        int count = Samples + 1;
        (MeshResource mesh, Appearance look) = Sheet(engine, water, side, cells, repeatMetres, (u, v) =>
        {
            // Between the lattice's depths, by where the vertex lies among them.
            double gx = u * Samples, gz = v * Samples;
            int i = Math.Min((int)gx, Samples - 1), j = Math.Min((int)gz, Samples - 1);
            float fx = (float)(gx - i), fz = (float)(gz - j);
            float top = float.Lerp(shallow[(j * count) + i], shallow[(j * count) + i + 1], fx);
            float bottom = float.Lerp(shallow[((j + 1) * count) + i], shallow[((j + 1) * count) + i + 1], fx);
            return float.Lerp(top, bottom, fz);
        });
        return new(mesh, look);
    }

    /// <summary>
    /// A square of flat water at the sea's surface, <paramref name="side"/> metres and <paramref name="cells"/> cells a side:
    /// a map pixel or a tile of one. Land stands through it, so the shore is wherever ground meets the water. It is a mesh,
    /// not a heightfield: a heightfield's tiles hang skirts, which show through water. The art's repeats fill a pixel and a
    /// tile exactly, so it runs on from one to the next. Its normals point up, leaning along +x by how shallow the water is
    /// at a vertex (<paramref name="shallow"/>, from 0 to 1 by where it lies in the square), which the sea's shader reads.
    /// </summary>
    internal static (MeshResource Mesh, Appearance Look) Sheet(IEngineContext engine, Material water, double side, int cells, float repeatMetres,
        Func<double, double, float>? shallow = null)
    {
        int count = cells + 1;
        Vector3[] positions = new Vector3[count * count];
        Vector3[] normals = new Vector3[positions.Length];
        Vector2[] uvs = new Vector2[positions.Length];
        for (int j = 0; j < count; j++)
        for (int i = 0; i < count; i++)
        {
            float u = (float)(side * i / cells), v = (float)(side * j / cells);
            positions[(j * count) + i] = new(u, 0, v);
            normals[(j * count) + i] = new(shallow?.Invoke((double)i / cells, (double)j / cells) ?? 0, 1, 0);
            uvs[(j * count) + i] = new(u / repeatMetres, v / repeatMetres);
        }
        uint[] indices = new uint[cells * cells * 6];
        int at = 0;
        for (int j = 0; j < cells; j++)
        for (int i = 0; i < cells; i++)
        {
            uint corner = (uint)((j * count) + i), right = corner + 1, below = corner + (uint)count, both = below + 1;
            // Wound to face up.
            foreach (uint index in new[] { corner, both, right, corner, below, both }) indices[at++] = index;
        }
        MeshResource mesh = engine.Graphics.CreateMeshResource(new MeshResourceCreateRequest(positions, normals, uvs, indices,
            new MeshGroup[] { new(0, 0, (uint)indices.Length) }, new MeshMaterialBinding[] { new(0, water) }));
        return (mesh, engine.Graphics.CreateMeshAppearance(mesh));
    }

    private static void Dispose(Piece piece)
    {
        piece.Look.Dispose();
        piece.Mesh.Dispose();
    }
}
