using System.Numerics;
using Rusty.Engine;

namespace WorldRpg.Kit.Presentation;

/// <summary>One line of a held look's arcs, in the model's space; lines of a group are drawn together.</summary>
public sealed record HeldArcPath(int Group, IReadOnlyList<Vector3> Points);

/// <summary>
/// How a held look's arcs draw. <paramref name="Width"/>, <paramref name="SparkWidth"/> and <paramref name="RimWidth"/> are shares of the view's height,
/// so a line keeps its weight however far off it is; <paramref name="Wander"/> is how far an arc strays from its path in
/// metres, <paramref name="Rate"/> its redraws a second and <paramref name="Share"/> and <paramref name="SparkShare"/> how
/// many arcs and sparks show at once, and <paramref name="HotShare"/> how many of them burn <paramref name="Hot"/>.
/// </summary>
public sealed record HeldArcLook(float Width, float SparkWidth, float Wander, float Rate, float Share, float SparkShare, Vector3 Edge, Vector3 Core, Vector3 Hot, float HotShare, float RimWidth, float RimBehind, Vector3 Rim);

/// <summary>The surface a held look's outline is grown from, welded with its normals smoothed across edges, in the model's space.</summary>
public sealed record HeldArcHull(IReadOnlyList<Vector3> Points, IReadOnlyList<Vector3> Normals, IReadOnlyList<uint> Corners);

/// <summary>What a held look's lightning follows: its arcs, the sparks a swing throws, and the surface of its outline.</summary>
public sealed record HeldArcShape(IReadOnlyList<HeldArcPath> Paths, IReadOnlyList<HeldArcPath> Sparks, HeldArcHull Hull);

/// <summary>
/// Lines that crawl over a held look and an outline around it, drawn by a product shader rather than as particles: each
/// path becomes a ribbon the shader holds at a fixed width on the screen and redraws at <see cref="HeldArcLook.Rate"/>, as a
/// sprite redraws its frames, and the look's surface is drawn again a little larger behind itself. While the look swings
/// it surges, as the classic's strike frames do: heavier arcs and a shower of sparks, which are short ribbons in the air
/// around it. All are children of the look.
/// </summary>
public sealed class HeldArcs : IDisposable
{
    // How far the shader may move a vertex from where it lies, for culling.
    private const float DisplacementBound = .1F;
    // Where a strike's arcs number from, past the sparks, so they draw apart from the arcs always shown.
    private const int SurgeGroups = 4096;
    // Which of the arcs surge: one in this many.
    private const int SurgeEvery = 4;

    private readonly RenderResource arcShader, rimShader;
    private readonly Material arcMaterial, sparkMaterial, rimMaterial;
    private readonly MeshResource arcMesh, sparkMesh, rimMesh;
    private readonly Appearance arcs, sparks, rim;
    private readonly ulong arcsObject, sparksObject, rimObject;

    /// <param name="engine">The Engine.</param>
    /// <param name="shader">The shader file, which draws the rim under its <c>RIM</c> keyword.</param>
    /// <param name="shape">What it follows.</param>
    /// <param name="look">How it draws.</param>
    /// <param name="arcsObject">The published object the arcs are drawn on.</param>
    /// <param name="sparksObject">The published object the sparks are drawn on.</param>
    /// <param name="rimObject">The published object the rim is drawn on.</param>
    public HeldArcs(IEngineContext engine, ContentReference shader, HeldArcShape shape, HeldArcLook look, ulong arcsObject, ulong sparksObject, ulong rimObject)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(shape);
        ArgumentNullException.ThrowIfNull(look);
        this.arcsObject = arcsObject;
        this.sparksObject = sparksObject;
        this.rimObject = rimObject;
        arcShader = engine.Graphics.OpenResourceFromContent(new(shader, TextureFilter.Linear, TextureWrap.Clamp, TextureColorSpace.Srgb, "")).Handle;
        rimShader = engine.Graphics.OpenResourceFromContent(new(shader, TextureFilter.Linear, TextureWrap.Clamp, TextureColorSpace.Srgb, "RIM")).Handle;
        Vector4 edge = new(look.Edge, 0), core = new(look.Core, 0), hot = new(look.Hot, look.HotShare);
        arcMaterial = engine.Graphics.CreateMaterial(Shaded(new(arcShader, new Vector4(look.Width, look.Wander, look.Rate, look.Share), edge, core, hot, default, default, DisplacementBound)));
        sparkMaterial = engine.Graphics.CreateMaterial(Shaded(new(arcShader, new Vector4(look.SparkWidth, look.Wander, look.Rate, look.SparkShare), edge, core, hot, default, default, DisplacementBound)));
        rimMaterial = engine.Graphics.CreateMaterial(Shaded(new(rimShader, new Vector4(look.RimWidth, look.RimBehind, look.Rate, 0), new Vector4(look.Rim, 0), default, default, default, default, DisplacementBound)));
        arcMesh = engine.Graphics.CreateMeshResource(Ribbons(shape.Paths, arcMaterial));
        // A strike surges: the sparks, and some of the arcs again at the sparks' weight, over those always shown.
        sparkMesh = engine.Graphics.CreateMeshResource(Ribbons([.. shape.Sparks, .. shape.Paths.Where(path => path.Group % SurgeEvery == 0).Select(path => path with { Group = path.Group + SurgeGroups })], sparkMaterial));
        rimMesh = engine.Graphics.CreateMeshResource(new(shape.Hull.Points.ToArray(), shape.Hull.Normals.ToArray(), default, default, shape.Hull.Corners.ToArray(),
            new MeshGroup[] { new(0, 0, (uint)shape.Hull.Corners.Count) }, new MeshMaterialBinding[] { new(0, rimMaterial) }));
        arcs = engine.Graphics.CreateMeshAppearance(arcMesh);
        sparks = engine.Graphics.CreateMeshAppearance(sparkMesh);
        rim = engine.Graphics.CreateMeshAppearance(rimMesh);
    }

    /// <summary>The objects it draws on, for binding them to a joint of the look.</summary>
    public ulong[] Objects => [arcsObject, sparksObject, rimObject];

    /// <summary>Adds the arcs and rim, children of the look published as <paramref name="look"/>, and the sparks while it <paramref name="swings"/>.</summary>
    public void AppendFacts(List<AppearanceFact> facts, ulong look, bool shown, bool swings)
    {
        ArgumentNullException.ThrowIfNull(facts);
        facts.Add(Fact(rimObject, rim, look, shown));
        facts.Add(Fact(arcsObject, arcs, look, shown));
        facts.Add(Fact(sparksObject, sparks, look, shown && swings));
    }

    private static AppearanceFact Fact(ulong objectId, Appearance appearance, ulong look, bool shown) =>
        new(objectId, true, look, new Transform(Vector3.Zero, Quaternion.Identity, Vector3.One), appearance, shown, RenderLayer.Viewmodel, ShadowCasting.None);

    public void Dispose()
    {
        arcs.Dispose();
        sparks.Dispose();
        rim.Dispose();
        arcMesh.Dispose();
        sparkMesh.Dispose();
        rimMesh.Dispose();
        arcMaterial.Dispose();
        sparkMaterial.Dispose();
        rimMaterial.Dispose();
        arcShader.Dispose();
        rimShader.Dispose();
    }

    private static MaterialRequest Shaded(MaterialShader shader) =>
        new(new Color(1, 1, 1, 1), default(RenderResourceReference), 1, new Color(1, 1, 1, 1), Vector3.Zero, 0, true) { Shader = shader, Unlit = true };

    /// <summary>
    /// Each path as a strip of two vertices a point, which the shader spreads across the view. A vertex's normal runs along
    /// the path, uv.x is its side, and uv.y its arc's number plus how far along the path it lies.
    /// </summary>
    public static MeshResourceCreateRequest Ribbons(IReadOnlyList<HeldArcPath> paths, Material material)
    {
        List<Vector3> positions = [], normals = [];
        List<Vector2> uvs = [];
        List<uint> indices = [];
        foreach (HeldArcPath path in paths)
        {
            IReadOnlyList<Vector3> points = path.Points;
            for (int at = 0; at < points.Count; at++)
            {
                uint first = (uint)positions.Count;
                // A path that doubles straight back has no direction at the turn: the shader only needs one that isn't zero.
                Vector3 span = points[Math.Min(at + 1, points.Count - 1)] - points[Math.Max(at - 1, 0)];
                Vector3 along = span == Vector3.Zero ? Vector3.UnitY : Vector3.Normalize(span);
                float share = at / (float)points.Count;
                foreach (float side in new[] { -1F, 1F })
                {
                    positions.Add(points[at]);
                    normals.Add(along);
                    uvs.Add(new(side, path.Group + share));
                }
                if (at > 0) indices.AddRange([first - 2, first - 1, first, first, first - 1, first + 1]);
            }
        }
        return new(positions.ToArray(), normals.ToArray(), uvs.ToArray(), default, indices.ToArray(), new MeshGroup[] { new(0, 0, (uint)indices.Count) }, new MeshMaterialBinding[] { new(0, material) });
    }
}
