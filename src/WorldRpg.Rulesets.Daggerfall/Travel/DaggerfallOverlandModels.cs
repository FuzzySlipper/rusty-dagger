using System.Numerics;
using Rusty.Engine;

namespace WorldRpg.Rulesets.Daggerfall.Travel;

/// <summary>
/// The travel map's small models, made once in code: a house, a keep, a temple and a ruin for the places; a standard
/// with its flag for the party and the bands; a disc for the dots of the sight ring and the route; a pin. Each stands on
/// its origin, one unit wide, and faces flat-shaded, as a map's pieces do. Colours are the ruleset's look, not rules.
/// </summary>
internal sealed class DaggerfallOverlandModels : IDisposable
{
    private readonly List<IDisposable> _owned = [];
    private readonly IEngineContext _engine;

    internal DaggerfallOverlandModels(IEngineContext engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        Material plaster = Solid(.80f, .72f, .58f), thatch = Solid(.46f, .30f, .17f), stone = Solid(.50f, .50f, .52f),
            slate = Solid(.24f, .25f, .30f), gold = Solid(.86f, .68f, .24f), dark = Solid(.20f, .18f, .19f), wood = Solid(.32f, .22f, .13f);
        House = Building(.7f, .55f, plaster, thatch, spire: false);
        Keep = Building(2.1f, .5f, stone, slate, spire: false, battlement: true);
        Temple = Building(1.0f, 1.5f, plaster, gold, spire: true);
        Shrine = Building(1.0f, 1.3f, dark, slate, spire: true);
        Ruin = Building(.6f, .0f, dark, dark, spire: false, battlement: true);
        Stone = Building(.45f, .15f, stone, stone, spire: true);
        Pole = Mesh(Box(new(-.03f, 0, -.03f), new(.03f, 1, .03f)), wood);
        Disc = Mesh(Disk(), Glow(1, 1, 1));
        Pin = Mesh(Diamond(), gold);
        Flags = new Dictionary<DaggerfallOverlandBanner, Appearance>
        {
            [DaggerfallOverlandBanner.Party] = Flag(Solid(.15f, .32f, .78f)),
            [DaggerfallOverlandBanner.Destination] = Flag(Solid(.86f, .68f, .24f)),
            [DaggerfallOverlandBanner.Bandits] = Flag(Solid(.62f, .12f, .10f)),
            [DaggerfallOverlandBanner.Travellers] = Flag(Solid(.55f, .55f, .60f)),
            [DaggerfallOverlandBanner.Beasts] = Flag(Solid(.48f, .34f, .16f)),
            [DaggerfallOverlandBanner.Undead] = Flag(Solid(.56f, .70f, .52f)),
            [DaggerfallOverlandBanner.Monsters] = Flag(Solid(.42f, .18f, .52f)),
        };
        Dots = new Dictionary<DaggerfallOverlandDot, Appearance>
        {
            [DaggerfallOverlandDot.Sight] = Mesh(Disk(), Glow(1, .92f, .62f)),
            [DaggerfallOverlandDot.Route] = Mesh(Disk(), Glow(1, 1, 1)),
            [DaggerfallOverlandDot.Spotted] = Mesh(Disk(), Glow(1, .25f, .2f)),
        };
    }

    internal Appearance House { get; }
    internal Appearance Keep { get; }
    internal Appearance Temple { get; }
    internal Appearance Shrine { get; }
    internal Appearance Ruin { get; }
    internal Appearance Stone { get; }
    internal Appearance Pole { get; }
    internal Appearance Disc { get; }
    internal Appearance Pin { get; }
    internal IReadOnlyDictionary<DaggerfallOverlandBanner, Appearance> Flags { get; }
    internal IReadOnlyDictionary<DaggerfallOverlandDot, Appearance> Dots { get; }

    public void Dispose()
    {
        for (int index = _owned.Count - 1; index >= 0; index--) _owned[index].Dispose();
        _owned.Clear();
    }

    private Material Solid(float r, float g, float b) => Own(_engine.Graphics.CreateMaterial(
        new MaterialRequest(Color.FromSrgb(r, g, b), default(RenderResourceReference), .9f, new Color(1, 1, 1, 1), Vector3.Zero, 0, false) with { FlatShading = true }));

    /// <summary>A colour drawn as it is, unlit, so the ring and the route read by night as by day.</summary>
    private Material Glow(float r, float g, float b) => Own(_engine.Graphics.CreateMaterial(
        new MaterialRequest(Color.FromSrgb(r, g, b), default(RenderResourceReference), 1, new Color(1, 1, 1, 1), Vector3.Zero, 0, false) with { Unlit = true }));

    /// <summary>Walls <paramref name="height"/> high under a roof <paramref name="roof"/> high: a gable, or a spire to a point.</summary>
    private Appearance Building(float height, float roof, Material walls, Material top, bool spire, bool battlement = false)
    {
        Shape body = Box(new(-.5f, 0, -.5f), new(.5f, height, .5f));
        Shape cap = battlement ? Battlement(height) : roof <= 0 ? Shape.Empty : spire ? Spire(height, roof) : Gable(height, roof);
        return Mesh(body, walls, cap, top);
    }

    private Appearance Flag(Material cloth) => Mesh(Box(new(.03f, .55f, -.01f), new(.58f, .97f, .01f)), cloth);

    private Appearance Mesh(Shape first, Material firstMaterial, Shape? second = null, Material? secondMaterial = null)
    {
        Shape all = second is { } more && more.Indices.Length > 0 ? first.Then(more) : first;
        MeshGroup[] groups = second is { Indices.Length: > 0 }
            ? [new(0, 0, (uint)first.Indices.Length), new(1, (uint)first.Indices.Length, (uint)second.Value.Indices.Length)]
            : [new(0, 0, (uint)first.Indices.Length)];
        MeshMaterialBinding[] bindings = second is { Indices.Length: > 0 } ? [new(0, firstMaterial), new(1, secondMaterial!)] : [new(0, firstMaterial)];
        MeshResource mesh = Own(_engine.Graphics.CreateMeshResource(new MeshResourceCreateRequest(all.Positions, all.Normals,
            new Vector2[all.Positions.Length], all.Indices, groups, bindings)));
        return Own(_engine.Graphics.CreateMeshAppearance(mesh));
    }

    private T Own<T>(T value) where T : IDisposable
    {
        _owned.Add(value);
        return value;
    }

    /// <summary>Triangles, each with its own corners so it shades flat.</summary>
    private readonly record struct Shape(Vector3[] Positions, Vector3[] Normals, uint[] Indices)
    {
        internal static Shape Empty { get; } = new([], [], []);

        internal static Shape Of(IEnumerable<(Vector3 A, Vector3 B, Vector3 C)> triangles)
        {
            List<Vector3> positions = [], normals = [];
            foreach ((Vector3 a, Vector3 b, Vector3 c) in triangles)
            {
                Vector3 normal = Vector3.Normalize(Vector3.Cross(b - a, c - a));
                positions.AddRange([a, b, c]);
                normals.AddRange([normal, normal, normal]);
            }
            return new([.. positions], [.. normals], [.. Enumerable.Range(0, positions.Count).Select(index => (uint)index)]);
        }

        internal Shape Then(Shape next)
        {
            uint offset = (uint)Positions.Length;
            return new([.. Positions, .. next.Positions], [.. Normals, .. next.Normals], [.. Indices, .. next.Indices.Select(index => index + offset)]);
        }
    }

    private static IEnumerable<(Vector3, Vector3, Vector3)> Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d) => [(a, b, c), (a, c, d)];

    /// <summary>A box from <paramref name="low"/> to <paramref name="high"/>, its faces wound outward.</summary>
    private static Shape Box(Vector3 low, Vector3 high)
    {
        Vector3 Corner(int x, int y, int z) => new(x == 0 ? low.X : high.X, y == 0 ? low.Y : high.Y, z == 0 ? low.Z : high.Z);
        return Shape.Of([
            .. Quad(Corner(0, 0, 1), Corner(1, 0, 1), Corner(1, 1, 1), Corner(0, 1, 1)),
            .. Quad(Corner(1, 0, 0), Corner(0, 0, 0), Corner(0, 1, 0), Corner(1, 1, 0)),
            .. Quad(Corner(1, 0, 1), Corner(1, 0, 0), Corner(1, 1, 0), Corner(1, 1, 1)),
            .. Quad(Corner(0, 0, 0), Corner(0, 0, 1), Corner(0, 1, 1), Corner(0, 1, 0)),
            .. Quad(Corner(0, 1, 1), Corner(1, 1, 1), Corner(1, 1, 0), Corner(0, 1, 0)),
        ]);
    }

    /// <summary>A pitched roof over a unit square, its ridge running along x and its eaves a little past the walls.</summary>
    private static Shape Gable(float y, float rise)
    {
        const float eave = .58f;
        Vector3 ridgeWest = new(-eave, y + rise, 0), ridgeEast = new(eave, y + rise, 0);
        Vector3 nw = new(-eave, y, -eave), ne = new(eave, y, -eave), se = new(eave, y, eave), sw = new(-eave, y, eave);
        return Shape.Of([.. Quad(sw, se, ridgeEast, ridgeWest), .. Quad(ne, nw, ridgeWest, ridgeEast), (se, ne, ridgeEast), (nw, sw, ridgeWest)]);
    }

    /// <summary>A roof rising to a point over a unit square.</summary>
    private static Shape Spire(float y, float rise)
    {
        const float eave = .56f;
        Vector3 top = new(0, y + rise, 0), nw = new(-eave, y, -eave), ne = new(eave, y, -eave), se = new(eave, y, eave), sw = new(-eave, y, eave);
        return Shape.Of([(sw, se, top), (se, ne, top), (ne, nw, top), (nw, sw, top)]);
    }

    /// <summary>Four merlons on a keep's flat top.</summary>
    private static Shape Battlement(float y)
    {
        Shape all = Shape.Empty;
        foreach ((float x, float z) in new[] { (-.5f, -.5f), (.3f, -.5f), (.3f, .3f), (-.5f, .3f) })
            all = all.Then(Box(new(x, y, z), new(x + .2f, y + .18f, z + .2f)));
        return all;
    }

    /// <summary>A flat disc facing up, one unit across.</summary>
    private static Shape Disk()
    {
        const int sides = 10;
        List<(Vector3, Vector3, Vector3)> triangles = [];
        for (int side = 0; side < sides; side++)
        {
            double from = side * Math.Tau / sides, to = (side + 1) * Math.Tau / sides;
            triangles.Add((Vector3.Zero, new((float)(Math.Cos(to) * .5), 0, (float)(Math.Sin(to) * .5)), new((float)(Math.Cos(from) * .5), 0, (float)(Math.Sin(from) * .5))));
        }
        return Shape.Of(triangles);
    }

    /// <summary>A pin: a diamond standing on its point.</summary>
    private static Shape Diamond()
    {
        Vector3 foot = Vector3.Zero, head = new(0, 1, 0);
        Vector3[] ring = [new(.25f, .65f, 0), new(0, .65f, .25f), new(-.25f, .65f, 0), new(0, .65f, -.25f)];
        List<(Vector3, Vector3, Vector3)> triangles = [];
        for (int side = 0; side < ring.Length; side++)
        {
            Vector3 a = ring[side], b = ring[(side + 1) % ring.Length];
            triangles.Add((foot, a, b));
            triangles.Add((head, b, a));
        }
        return Shape.Of(triangles);
    }
}

/// <summary>Whose standard a flag is: the party's, where it heads, or a band's by its kind.</summary>
internal enum DaggerfallOverlandBanner { Party, Destination, Bandits, Travellers, Beasts, Undead, Monsters }

/// <summary>What a dot on the map marks: the edge of the party's sight, the way it goes, a band that has seen it.</summary>
internal enum DaggerfallOverlandDot { Sight, Route, Spotted }
