using System.Numerics;
using Rusty.Engine;

namespace WorldRpg.Rulesets.Daggerfall.Travel;

/// <summary>
/// The travel map's markers, made once in code: an ornate standard with a waving swallowtail pennant for where the party
/// heads (and for figures without sprites); a faceted ruby-and-gold map pin; dark-rimmed terrain-hugging dashes for the
/// sight ring and route trail; hollow tactical rings, directional heading chevrons, and a soft radial ground shadow for
/// the party and overland bands (#11115). Each stands on its origin, one unit wide, and shades crisp and flat.
/// </summary>
internal sealed class DaggerfallOverlandModels : IDisposable
{
    private readonly List<IDisposable> _owned = [];
    private readonly IEngineContext _engine;
    private readonly RenderResourceReference _shadowTexture;
    private readonly Material _shadowMaterial;

    internal DaggerfallOverlandModels(IEngineContext engine, RenderResourceReference shadowTexture = default)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _shadowTexture = shadowTexture;

        Material gold = Solid(.88f, .70f, .26f);
        Material wood = Solid(.30f, .20f, .12f);
        Material ruby = LitAccent(.84f, .16f, .18f, new(.75f, .10f, .12f), .35f);
        Material darkInk = Glow(.11f, .09f, .07f);
        Material darkIron = Glow(.11f, .11f, .14f);
        Material darkCrimson = Glow(.15f, .05f, .05f);
        Material darkAmber = Glow(.16f, .08f, .04f);

        Pole = Mesh(StandardStaff(), wood, StandardTrim(), gold);
        Pin = Mesh(PinHardware(), gold, PinGem(), ruby);
        Flags = new Dictionary<DaggerfallOverlandBanner, Appearance>
        {
            [DaggerfallOverlandBanner.Party] = Flag(Solid(.16f, .36f, .82f), gold),
            [DaggerfallOverlandBanner.Destination] = Flag(Solid(.88f, .68f, .22f), Solid(.66f, .16f, .14f)),
            [DaggerfallOverlandBanner.Bandits] = Flag(Solid(.66f, .13f, .11f), gold),
            [DaggerfallOverlandBanner.Travellers] = Flag(Solid(.58f, .58f, .64f), gold),
            [DaggerfallOverlandBanner.Beasts] = Flag(Solid(.52f, .36f, .18f), gold),
            [DaggerfallOverlandBanner.Undead] = Flag(Solid(.56f, .72f, .54f), gold),
            [DaggerfallOverlandBanner.Monsters] = Flag(Solid(.46f, .20f, .58f), gold),
            [DaggerfallOverlandBanner.Caravan] = Flag(Solid(.85f, .62f, .22f), gold),
            [DaggerfallOverlandBanner.Courier] = Flag(Solid(.30f, .68f, .80f), gold),
            [DaggerfallOverlandBanner.Patrol] = Flag(Solid(.28f, .44f, .70f), gold),
            [DaggerfallOverlandBanner.Hostile] = Flag(Solid(.78f, .15f, .11f), gold),
            [DaggerfallOverlandBanner.Clash] = Flag(Solid(.90f, .42f, .12f), gold),
        };
        Dots = new Dictionary<DaggerfallOverlandDot, Appearance>
        {
            // Sight ring: sleek dark-rimmed champagne-gold arc dash, readable on both winter snow and night terrain.
            [DaggerfallOverlandDot.Sight] = Mesh(
                Bar(.28f, .50f, 0f, .045f), darkInk,
                Bar(.16f, .44f, .050f, .080f), Glow(.95f, .86f, .52f)),
            // Route trail: high-contrast dark-rimmed burnished-gold directional chevron pointing forward (+Z).
            [DaggerfallOverlandDot.Route] = Mesh(
                Chevron(.42f, -.48f, .66f, 0f, .058f), darkInk,
                Chevron(.25f, -.34f, .48f, .062f, .104f), Glow(1f, .80f, .22f)),
            // Spotted / chasing band: hollow dark-rimmed crimson pursuit reticle (not a solid flat red pancake).
            [DaggerfallOverlandDot.Spotted] = Mesh(
                RingWithNotches(.31f, .53f, 0f, .040f, inward: true, notchReach: .18f, sides: 24), darkCrimson,
                RingWithNotches(.36f, .47f, .045f, .078f, inward: true, notchReach: .23f, sides: 24), Glow(.98f, .20f, .18f)),
            // Active clash: hollow dark-rimmed amber skirmish starburst ring.
            [DaggerfallOverlandDot.Clash] = Mesh(
                RingWithNotches(.30f, .52f, 0f, .040f, inward: false, notchReach: .72f, sides: 24), darkAmber,
                RingWithNotches(.35f, .46f, .045f, .078f, inward: false, notchReach: .64f, sides: 24), Glow(1f, .58f, .14f)),
        };

        PartyRing = Mesh(
            Ring(.30f, .51f, 0f, .038f, 24), darkIron,
            Ring(.35f, .46f, .042f, .072f, 24), Glow(.32f, .68f, 1f));
        BandRing = Mesh(
            Ring(.31f, .48f, 0f, .034f, 20), darkInk,
            Ring(.36f, .44f, .038f, .064f, 20), Glow(.80f, .72f, .52f));
        DestinationRing = Mesh(
            Ring(.24f, .44f, 0f, .038f, 20), darkInk,
            Ring(.29f, .39f, .042f, .070f, 20), Glow(.98f, .78f, .24f));
        PinRing = Mesh(
            Ring(.18f, .36f, 0f, .034f, 16), darkCrimson,
            Ring(.22f, .31f, .038f, .064f, 16), Glow(.94f, .30f, .28f));

        PartyHeading = Mesh(
            Chevron(.24f, -.22f, .40f, 0f, .042f), darkIron,
            Chevron(.14f, -.13f, .28f, .046f, .078f), Glow(.98f, .84f, .32f));
        HostileHeading = Mesh(
            Chevron(.22f, -.20f, .36f, 0f, .040f), darkCrimson,
            Chevron(.13f, -.12f, .25f, .044f, .074f), Glow(.98f, .26f, .22f));
        NeutralHeading = Mesh(
            Chevron(.20f, -.18f, .32f, 0f, .036f), darkInk,
            Chevron(.12f, -.10f, .22f, .040f, .066f), Glow(.92f, .84f, .58f));

        _shadowMaterial = Own(_engine.Graphics.CreateMaterial(ShadowMaterialRequest(winter: false, night: 0f, _shadowTexture)));
        TokenShadow = Mesh(_shadowTexture != default ? ShadowQuad() : Disk(.48f, .012f, 16), _shadowMaterial);
    }

    internal Appearance Pole { get; }
    internal Appearance Pin { get; }
    internal Appearance PartyRing { get; }
    internal Appearance BandRing { get; }
    internal Appearance DestinationRing { get; }
    internal Appearance PinRing { get; }
    internal Appearance PartyHeading { get; }
    internal Appearance HostileHeading { get; }
    internal Appearance NeutralHeading { get; }
    internal Appearance TokenShadow { get; }
    internal IReadOnlyDictionary<DaggerfallOverlandBanner, Appearance> Flags { get; }
    internal IReadOnlyDictionary<DaggerfallOverlandDot, Appearance> Dots { get; }

    /// <summary>Adapts the token contact shadow opacity and tint to winter snow and night lighting.</summary>
    internal void UpdateTheme(bool winter, float night) =>
        _engine.Graphics.UpdateMaterial(new(_shadowMaterial, ShadowMaterialRequest(winter, night, _shadowTexture)));

    public void Dispose()
    {
        for (int index = _owned.Count - 1; index >= 0; index--) _owned[index].Dispose();
        _owned.Clear();
    }

    /// <summary>Maps an expedition compass heading (degrees clockwise from north) to a horizontal unit vector (+X east, -Z north).</summary>
    internal static Vector3 HeadingForward(double degreesClockwiseFromNorth)
    {
        double radians = degreesClockwiseFromNorth * Math.PI / 180.0;
        return new((float)Math.Sin(radians), 0f, (float)-Math.Cos(radians));
    }

    /// <summary>
    /// Builds a right-handed orientation whose local +Y matches <paramref name="surfaceNormal"/> and whose local +Z
    /// aligns with <paramref name="forwardHint"/> projected onto the terrain tangent plane.
    /// </summary>
    internal static Quaternion AlignToSurface(Vector3 surfaceNormal, Vector3 forwardHint)
    {
        Vector3 up = surfaceNormal.LengthSquared() > 1e-6f ? Vector3.Normalize(surfaceNormal) : Vector3.UnitY;
        Vector3 fwd = forwardHint - (up * Vector3.Dot(forwardHint, up));
        if (fwd.LengthSquared() < 1e-6f)
        {
            Vector3 fallback = MathF.Abs(up.Z) < .9f ? Vector3.UnitZ : Vector3.UnitX;
            fwd = fallback - (up * Vector3.Dot(fallback, up));
        }
        fwd = Vector3.Normalize(fwd);
        Vector3 right = Vector3.Normalize(Vector3.Cross(up, fwd));
        return BasisQuaternion(right, up, fwd);
    }

    /// <summary>
    /// Builds a right-handed orientation whose local +Z points directly along the 3D chord from
    /// <paramref name="chordStart"/> to <paramref name="chordEnd"/> while banking cleanly to <paramref name="surfaceNormal"/>.
    /// </summary>
    internal static Quaternion AlignChordToSurface(Vector3 surfaceNormal, Vector3 chordStart, Vector3 chordEnd)
    {
        Vector3 chord = chordEnd - chordStart;
        if (chord.LengthSquared() < 1e-6f) return AlignToSurface(surfaceNormal, Vector3.UnitZ);
        Vector3 fwd = Vector3.Normalize(chord);
        Vector3 refUp = surfaceNormal.LengthSquared() > 1e-6f ? Vector3.Normalize(surfaceNormal) : Vector3.UnitY;
        Vector3 right = Vector3.Cross(refUp, fwd);
        if (right.LengthSquared() < 1e-6f) right = Vector3.Cross(Vector3.UnitY, fwd);
        if (right.LengthSquared() < 1e-6f) right = Vector3.UnitX;
        right = Vector3.Normalize(right);
        Vector3 up = Vector3.Normalize(Vector3.Cross(fwd, right));
        return BasisQuaternion(right, up, fwd);
    }

    /// <summary>
    /// Subtle vertical march stride lift (in map metres) for a moving party or overland band figure; returns 0 when
    /// standing still, paused or camped.
    /// </summary>
    internal static float MarchBobOffset(double clockSeconds, int seed, float tokenSize, bool moving)
    {
        if (!moving || tokenSize <= 0f || !double.IsFinite(clockSeconds)) return 0f;
        float wave = MathF.Abs(MathF.Sin((float)((clockSeconds * 5.6) + (seed * 1.37))));
        return MathF.Pow(wave, .85f) * (tokenSize * .11f);
    }

    /// <summary>Gentle breathing scale factor for active threat/clash rings.</summary>
    internal static float RingPulseScale(double clockSeconds, float rate, float amplitude) =>
        double.IsFinite(clockSeconds) ? 1f + (MathF.Sin((float)(clockSeconds * rate)) * amplitude) : 1f;

    /// <summary>Soft ground contact shadow material for party, band, destination and pin anchors.</summary>
    internal static MaterialRequest ShadowMaterialRequest(bool winter, float night, RenderResourceReference shadowTexture)
    {
        bool textured = shadowTexture != default;
        float dark = Math.Clamp(night, 0f, 1f);
        float dayAlpha = textured ? (winter ? .52f : .66f) : (winter ? .34f : .44f);
        float nightAlpha = textured ? .36f : .26f;
        float alpha = dayAlpha + ((nightAlpha - dayAlpha) * dark);
        Color tint = winter ? new Color(.035f, .05f, .085f, alpha) : new Color(.014f, .016f, .022f, alpha);
        return new MaterialRequest(tint, shadowTexture, 1f, new Color(1, 1, 1, 1), Vector3.Zero, 0, false, MaterialAlphaMode.Blend, 0) with
        {
            Unlit = true,
        };
    }

    private static Quaternion BasisQuaternion(Vector3 right, Vector3 up, Vector3 forward)
    {
        Matrix4x4 basis = new(
            right.X, right.Y, right.Z, 0f,
            up.X, up.Y, up.Z, 0f,
            forward.X, forward.Y, forward.Z, 0f,
            0f, 0f, 0f, 1f);
        return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(basis));
    }

    private Material Solid(float r, float g, float b) => Own(_engine.Graphics.CreateMaterial(
        new MaterialRequest(Color.FromSrgb(r, g, b), default(RenderResourceReference), .85f, new Color(1, 1, 1, 1), Vector3.Zero, 0, false) with { FlatShading = true }));

    private Material LitAccent(float r, float g, float b, Vector3 emissionColor, float emissionIntensity) => Own(_engine.Graphics.CreateMaterial(
        new MaterialRequest(Color.FromSrgb(r, g, b), default(RenderResourceReference), .35f, new Color(1, 1, 1, 1), Vector3.Zero, 0, false) with
        {
            FlatShading = true,
            EmissionColor = emissionColor,
            EmissionIntensity = emissionIntensity,
        }));

    /// <summary>A colour drawn as it is, unlit, so the ring, route and alerts read by night as by day.</summary>
    private Material Glow(float r, float g, float b) => Own(_engine.Graphics.CreateMaterial(
        new MaterialRequest(Color.FromSrgb(r, g, b), default(RenderResourceReference), 1, new Color(1, 1, 1, 1), Vector3.Zero, 0, false) with { Unlit = true }));

    private Appearance Flag(Material cloth, Material trim) => Mesh(PennantCloth(), cloth, PennantTrim(), trim);

    private Appearance Mesh(Shape first, Material firstMaterial, Shape? second = null, Material? secondMaterial = null)
    {
        Shape all = second is { } more && more.Indices.Length > 0 ? first.Then(more) : first;
        MeshGroup[] groups = second is { Indices.Length: > 0 }
            ? [new(0, 0, (uint)first.Indices.Length), new(1, (uint)first.Indices.Length, (uint)second.Value.Indices.Length)]
            : [new(0, 0, (uint)first.Indices.Length)];
        MeshMaterialBinding[] bindings = second is { Indices.Length: > 0 } ? [new(0, firstMaterial), new(1, secondMaterial!)] : [new(0, firstMaterial)];
        MeshResource mesh = Own(_engine.Graphics.CreateMeshResource(new MeshResourceCreateRequest(all.Positions, all.Normals,
            all.Uvs, all.Indices, groups, bindings)));
        return Own(_engine.Graphics.CreateMeshAppearance(mesh));
    }

    private T Own<T>(T value) where T : IDisposable
    {
        _owned.Add(value);
        return value;
    }

    /// <summary>Triangles, each with its own corners so it shades flat.</summary>
    private readonly record struct Shape(Vector3[] Positions, Vector3[] Normals, Vector2[] Uvs, uint[] Indices)
    {
        internal static Shape Of(IEnumerable<(Vector3 A, Vector3 B, Vector3 C)> triangles)
        {
            List<Vector3> positions = [], normals = [];
            foreach ((Vector3 a, Vector3 b, Vector3 c) in triangles)
            {
                Vector3 cross = Vector3.Cross(b - a, c - a);
                Vector3 normal = cross.LengthSquared() > 1e-10f ? Vector3.Normalize(cross) : Vector3.UnitY;
                positions.AddRange([a, b, c]);
                normals.AddRange([normal, normal, normal]);
            }
            return new([.. positions], [.. normals], new Vector2[positions.Count], [.. Enumerable.Range(0, positions.Count).Select(index => (uint)index)]);
        }

        internal static Shape TexturedQuad(Vector3 a, Vector2 ua, Vector3 b, Vector2 ub, Vector3 c, Vector2 uc, Vector3 d, Vector2 ud)
        {
            Vector3 normal = Vector3.Normalize(Vector3.Cross(b - a, c - a));
            return new(
                [a, b, c, a, c, d],
                [normal, normal, normal, normal, normal, normal],
                [ua, ub, uc, ua, uc, ud],
                [0, 1, 2, 3, 4, 5]);
        }

        internal Shape Then(Shape next)
        {
            uint offset = (uint)Positions.Length;
            return new([.. Positions, .. next.Positions], [.. Normals, .. next.Normals], [.. Uvs, .. next.Uvs],
                [.. Indices, .. next.Indices.Select(index => index + offset)]);
        }
    }

    private static IEnumerable<(Vector3, Vector3, Vector3)> Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d) => [(a, b, c), (a, c, d)];

    /// <summary>A flat upward-facing textured quad [-0.5..0.5] for soft radial contact shadows.</summary>
    private static Shape ShadowQuad() => Shape.TexturedQuad(
        new(-.5f, .012f, .5f), new(0f, 1f),
        new(.5f, .012f, .5f), new(1f, 1f),
        new(.5f, .012f, -.5f), new(1f, 0f),
        new(-.5f, .012f, -.5f), new(0f, 0f));

    /// <summary>A flat disc facing up (fallback when no radial shadow texture is present).</summary>
    private static Shape Disk(float radius, float y, int sides)
    {
        List<(Vector3, Vector3, Vector3)> triangles = [];
        Vector3 centre = new(0f, y, 0f);
        for (int side = 0; side < sides; side++)
        {
            double from = side * Math.Tau / sides, to = (side + 1) * Math.Tau / sides;
            triangles.Add((centre, new((float)(Math.Cos(to) * radius), y, (float)(Math.Sin(to) * radius)),
                new((float)(Math.Cos(from) * radius), y, (float)(Math.Sin(from) * radius))));
        }
        return Shape.Of(triangles);
    }

    /// <summary>A beveled capsule-bar aligned along local +Z from -halfLen to +halfLen (sight-ring arc dash).</summary>
    private static Shape Bar(float halfWidth, float halfLen, float yLow, float yHigh)
    {
        float tip = halfLen * .78f;
        Vector2[] poly =
        [
            new(0f, halfLen),
            new(halfWidth, tip),
            new(halfWidth, -tip),
            new(0f, -halfLen),
            new(-halfWidth, -tip),
            new(-halfWidth, tip),
        ];
        return ExtrudeConvexPolygon(poly, yLow, yHigh);
    }

    /// <summary>A crisp directional chevron/arrowhead plaque pointing forward along local +Z (route dashes & heading cues).</summary>
    private static Shape Chevron(float halfWidth, float tailZ, float tipZ, float yLow, float yHigh)
    {
        float shoulderZ = tailZ + ((tipZ - tailZ) * .30f);
        float notchZ = tailZ + ((tipZ - tailZ) * .20f);
        Vector2[] poly =
        [
            new(0f, tipZ),
            new(halfWidth, shoulderZ),
            new(halfWidth * .78f, tailZ),
            new(0f, notchZ),
            new(-halfWidth * .78f, tailZ),
            new(-halfWidth, shoulderZ),
        ];
        return ExtrudeChevron(poly, yLow, yHigh);
    }

    /// <summary>A flat beveled hollow ring (annulus) on the XZ plane with outer skirt faces for oblique readability.</summary>
    private static Shape Ring(float innerRadius, float outerRadius, float yLow, float yHigh, int sides)
    {
        List<(Vector3, Vector3, Vector3)> tris = [];
        for (int s = 0; s < sides; s++)
        {
            double a0 = s * Math.Tau / sides, a1 = (s + 1) * Math.Tau / sides;
            float c0 = (float)Math.Cos(a0), s0 = (float)Math.Sin(a0);
            float c1 = (float)Math.Cos(a1), s1 = (float)Math.Sin(a1);
            Vector3 i0 = new(c0 * innerRadius, yHigh, s0 * innerRadius);
            Vector3 i1 = new(c1 * innerRadius, yHigh, s1 * innerRadius);
            Vector3 o0 = new(c0 * outerRadius, yHigh, s0 * outerRadius);
            Vector3 o1 = new(c1 * outerRadius, yHigh, s1 * outerRadius);
            Vector3 ob0 = new(c0 * outerRadius, yLow, s0 * outerRadius);
            Vector3 ob1 = new(c1 * outerRadius, yLow, s1 * outerRadius);
            // Top annulus face (+Y normal) and outer rim wall (outward normal).
            tris.AddRange(Quad(i0, i1, o1, o0));
            tris.AddRange(Quad(ob0, o0, o1, ob1));
        }
        return Shape.Of(tris);
    }

    /// <summary>A hollow tactical ring accented with 4 crisp directional notches (inward pursuit ticks or outward clash spikes).</summary>
    private static Shape RingWithNotches(float innerRadius, float outerRadius, float yLow, float yHigh, bool inward, float notchReach, int sides)
    {
        Shape baseRing = Ring(innerRadius, outerRadius, yLow, yHigh, sides);
        List<(Vector3, Vector3, Vector3)> spikes = [];
        float baseOffset = inward ? 0f : MathF.PI / 4f;
        float halfAngle = inward ? .11f : .14f;
        float anchorR = inward ? innerRadius + .02f : outerRadius - .02f;
        for (int k = 0; k < 4; k++)
        {
            float a = baseOffset + (k * MathF.PI * .5f);
            Vector2 pLeft = new(MathF.Cos(a - halfAngle) * anchorR, MathF.Sin(a - halfAngle) * anchorR);
            Vector2 pRight = new(MathF.Cos(a + halfAngle) * anchorR, MathF.Sin(a + halfAngle) * anchorR);
            Vector2 pTip = new(MathF.Cos(a) * notchReach, MathF.Sin(a) * notchReach);
            // Winding produces +Y upward top normal and outward side walls for both inward and outward spikes.
            Vector2 t0 = inward ? pLeft : pRight;
            Vector2 t1 = inward ? pRight : pLeft;
            Vector3 v0 = new(t0.X, yHigh, t0.Y);
            Vector3 v1 = new(t1.X, yHigh, t1.Y);
            Vector3 v2 = new(pTip.X, yHigh, pTip.Y);
            Vector3 b0 = new(t0.X, yLow, t0.Y);
            Vector3 b1 = new(t1.X, yLow, t1.Y);
            Vector3 b2 = new(pTip.X, yLow, pTip.Y);
            spikes.Add((v0, v2, v1));
            spikes.AddRange(Quad(b0, b2, v2, v0));
            spikes.AddRange(Quad(b2, b1, v1, v2));
        }
        return baseRing.Then(Shape.Of(spikes));
    }

    /// <summary>A tapered wooden standard staff from pedestal collar to finial base.</summary>
    private static Shape StandardStaff() => Frustum(0f, .07f, 1.00f, .036f, .024f, 6);

    /// <summary>Burnished brass base pedestal, mid collar, and spearhead finial for a travel standard.</summary>
    private static Shape StandardTrim()
    {
        Shape baseFoot = Frustum(0f, 0f, .085f, .115f, .048f, 8);
        Shape collar = Frustum(0f, .52f, .56f, .046f, .042f, 6);
        Shape finialLower = Frustum(0f, .98f, 1.06f, .032f, .078f, 6);
        Shape finialTip = Frustum(0f, 1.06f, 1.22f, .078f, .003f, 6);
        return baseFoot.Then(collar).Then(finialLower).Then(finialTip);
    }

    /// <summary>A 3-segment gently rippling swallowtail pennant fluttering from the standard's hoist.</summary>
    private static Shape PennantCloth()
    {
        // Control stations along X with subtle wave in Z and graceful taper into a forked swallowtail tip.
        float[] xs = [.04f, .24f, .46f, .68f];
        float[] zs = [0f, .042f, -.032f, .022f];
        float[] topY = [.97f, .95f, .91f, .88f];
        float[] botY = [.57f, .59f, .62f, .65f];
        const float halfThick = .012f;

        List<(Vector3, Vector3, Vector3)> tris = [];
        for (int i = 0; i < 2; i++)
        {
            Vector3 f0 = new(xs[i], botY[i], zs[i] + halfThick);
            Vector3 f1 = new(xs[i + 1], botY[i + 1], zs[i + 1] + halfThick);
            Vector3 f2 = new(xs[i + 1], topY[i + 1], zs[i + 1] + halfThick);
            Vector3 f3 = new(xs[i], topY[i], zs[i] + halfThick);
            Vector3 b0 = new(xs[i], botY[i], zs[i] - halfThick);
            Vector3 b1 = new(xs[i + 1], botY[i + 1], zs[i + 1] - halfThick);
            Vector3 b2 = new(xs[i + 1], topY[i + 1], zs[i + 1] - halfThick);
            Vector3 b3 = new(xs[i], topY[i], zs[i] - halfThick);
            tris.AddRange(Quad(f0, f1, f2, f3));
            tris.AddRange(Quad(b1, b0, b3, b2));
            tris.AddRange(Quad(f3, f2, b2, b3));
            tris.AddRange(Quad(b0, b1, f1, f0));
        }

        // Forked swallowtail tip between station 2 and station 3 (notched back at x = 0.54, midY = 0.765).
        float midX = .54f, midY = .765f, midZ = -.006f;
        Vector3 topBaseF = new(xs[2], topY[2], zs[2] + halfThick), topBaseB = new(xs[2], topY[2], zs[2] - halfThick);
        Vector3 botBaseF = new(xs[2], botY[2], zs[2] + halfThick), botBaseB = new(xs[2], botY[2], zs[2] - halfThick);
        Vector3 midBaseF = new(xs[2], midY, zs[2] + halfThick), midBaseB = new(xs[2], midY, zs[2] - halfThick);
        Vector3 notchF = new(midX, midY, midZ + halfThick), notchB = new(midX, midY, midZ - halfThick);
        Vector3 topTipF = new(xs[3], topY[3], zs[3] + halfThick), topTipB = new(xs[3], topY[3], zs[3] - halfThick);
        Vector3 botTipF = new(xs[3], botY[3], zs[3] + halfThick), botTipB = new(xs[3], botY[3], zs[3] - halfThick);

        // Upper swallowtail tongue.
        tris.AddRange(Quad(midBaseF, notchF, topTipF, topBaseF));
        tris.AddRange(Quad(notchB, midBaseB, topBaseB, topTipB));
        tris.AddRange(Quad(topBaseF, topTipF, topTipB, topBaseB));
        tris.AddRange(Quad(notchF, topTipF, topTipB, notchB));
        // Lower swallowtail tongue.
        tris.AddRange(Quad(botBaseF, botTipF, notchF, midBaseF));
        tris.AddRange(Quad(botTipB, botBaseB, midBaseB, notchB));
        tris.AddRange(Quad(botBaseB, botTipB, botTipF, botBaseF));
        tris.AddRange(Quad(botTipF, notchF, notchB, botTipB));

        return Shape.Of(tris);
    }

    /// <summary>Heraldic hoist sleeve and vertical trim band along the pennant's pole edge.</summary>
    private static Shape PennantTrim() => Frustum(0f, .56f, .98f, .045f, .045f, 6);

    /// <summary>Burnished brass needle, flared collar, and crown tip for the ornate cartographer's map pin.</summary>
    private static Shape PinHardware()
    {
        Shape needle = Frustum(0f, 0f, .46f, .016f, .052f, 6);
        Shape collar = Frustum(0f, .44f, .52f, .135f, .095f, 8);
        Shape crown = Frustum(0f, .94f, 1.08f, .092f, .012f, 8);
        return needle.Then(collar).Then(crown);
    }

    /// <summary>Faceted 8-sided ruby jewel head seated inside the pin's brass collar and crown.</summary>
    private static Shape PinGem()
    {
        Shape lower = Frustum(0f, .51f, .73f, .095f, .225f, 8);
        Shape upper = Frustum(0f, .73f, .95f, .225f, .090f, 8);
        return lower.Then(upper);
    }

    private static Shape Frustum(float xOffset, float y0, float y1, float r0, float r1, int sides)
    {
        List<(Vector3, Vector3, Vector3)> tris = [];
        for (int s = 0; s < sides; s++)
        {
            double a0 = s * Math.Tau / sides, a1 = (s + 1) * Math.Tau / sides;
            float c0 = (float)Math.Cos(a0), s0 = (float)Math.Sin(a0);
            float c1 = (float)Math.Cos(a1), s1 = (float)Math.Sin(a1);
            Vector3 b0 = new(xOffset + (c0 * r0), y0, s0 * r0);
            Vector3 b1 = new(xOffset + (c1 * r0), y0, s1 * r0);
            Vector3 t0 = new(xOffset + (c0 * r1), y1, s0 * r1);
            Vector3 t1 = new(xOffset + (c1 * r1), y1, s1 * r1);
            // Outward side faces and upward top cap.
            tris.AddRange(Quad(b0, t0, t1, b1));
            if (r1 > .02f) tris.Add((new(xOffset, y1, 0f), t1, t0));
        }
        return Shape.Of(tris);
    }

    /// <summary>Extrudes a convex XZ polygon (clockwise in XZ looking down from +Y) into a flat beveled prism.</summary>
    private static Shape ExtrudeConvexPolygon(Vector2[] poly, float yLow, float yHigh)
    {
        List<(Vector3, Vector3, Vector3)> tris = [];
        Vector3 centre = new(0f, yHigh, 0f);
        for (int i = 0; i < poly.Length; i++)
        {
            Vector2 a = poly[i], b = poly[(i + 1) % poly.Length];
            Vector3 ta = new(a.X, yHigh, a.Y), tb = new(b.X, yHigh, b.Y);
            Vector3 ba = new(a.X, yLow, a.Y), bb = new(b.X, yLow, b.Y);
            // Top face (+Y normal) and outward side wall.
            tris.Add((centre, ta, tb));
            tris.AddRange(Quad(ba, bb, tb, ta));
        }
        return Shape.Of(tris);
    }

    /// <summary>Extrudes the 6-vertex concave chevron [tip, rightShoulder, rightTail, notch, leftTail, leftShoulder] cleanly.</summary>
    private static Shape ExtrudeChevron(Vector2[] poly, float yLow, float yHigh)
    {
        List<(Vector3, Vector3, Vector3)> tris = [];
        Vector3 T(int i) => new(poly[i].X, yHigh, poly[i].Y);
        Vector3 B(int i) => new(poly[i].X, yLow, poly[i].Y);
        // Split cleanly into 4 top triangles around the center line (tip=0, notch=3) with +Y normals.
        tris.Add((T(0), T(1), T(3)));
        tris.Add((T(1), T(2), T(3)));
        tris.Add((T(0), T(3), T(5)));
        tris.Add((T(5), T(3), T(4)));
        for (int i = 0; i < poly.Length; i++)
        {
            int next = (i + 1) % poly.Length;
            tris.AddRange(Quad(B(i), B(next), T(next), T(i)));
        }
        return Shape.Of(tris);
    }
}

/// <summary>Whose standard a flag is: the party's, where it heads, or a band's by its kind.</summary>
internal enum DaggerfallOverlandBanner { Party, Destination, Bandits, Travellers, Beasts, Undead, Monsters, Caravan, Courier, Patrol, Hostile, Clash }

/// <summary>What a dot on the map marks: the edge of the party's sight, the way it goes, a band that has seen it, or an active clash.</summary>
internal enum DaggerfallOverlandDot { Sight, Route, Spotted, Clash }
