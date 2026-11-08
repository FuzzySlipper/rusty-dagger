using System.Numerics;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.World;

/// <summary>
/// A tree grown in the round from a flora sprite's measures (<see cref="DaggerfallTreeArt"/>): a trunk and limbs as low-sided
/// tubes wearing bark, and leaf cards wearing pieces of the sprite's own painted crown. A conifer rings its trunk to the top
/// with whorls of drooping branches, shorter toward the tip, each carrying cards laid along it; a broadleaf's trunk forks
/// into limbs and twigs whose ends carry the cards in a rounded crown. Bark holds still in the wind (vertex alpha 0) and
/// the cards flutter, more the farther out they stand. Seeded, so a seed always grows the same tree.
/// </summary>
internal static class DaggerfallTreeMesh
{
    private const int TrunkSides = 6, LimbSides = 4;
    /// <summary>How far the trunk's foot sinks into the ground, as a share of the tree's height, so slopes never show it.</summary>
    private const float Sink = .03f;

    private sealed record Limb(Vector3[] Points, float[] Radii, int Sides);

    /// <param name="spriteHeight">The sprite's world height, which every measure is a share of.</param>
    /// <param name="bark">The bark's material, drawn at slot 0.</param>
    /// <param name="leaves">The leaves' material, drawn at slot 1.</param>
    internal static MeshResourceCreateRequest Build(DaggerfallTreeArt art, float spriteHeight, int seed, Material bark, Material leaves)
    {
        Random rng = new(seed);
        float s = spriteHeight, foot = art.Foot * s, top = foot + (art.Height * s), crownBase = foot + (art.CrownBase * s);
        float radius = art.CrownRadius * s, trunk = art.TrunkRadius * s, card = art.CardSize * s;
        List<Limb> limbs = [];
        List<(Vector3 At, Vector3 Along, float Size, float Sway)> cards = [];
        if (art.Form == "conifer") Conifer(rng, limbs, cards, foot, top, crownBase, radius, trunk, card);
        else Broadleaf(rng, limbs, cards, foot, top, crownBase, radius, trunk, card);

        List<Vector3> positions = [], normals = [];
        List<Vector2> uvs = [];
        List<Color> colours = [];
        List<uint> indices = [];
        foreach (Limb limb in limbs) Tube(limb, art.BarkSize * s, top, positions, normals, uvs, colours, indices);
        uint barkCount = (uint)indices.Count;
        Vector3 centre = new(0, (crownBase + top) / 2, 0);
        foreach ((Vector3 at, Vector3 along, float size, float sway) in cards)
            Card(rng, at, along, size, sway, centre, art.LeafCells, positions, normals, uvs, colours, indices);
        return new MeshResourceCreateRequest(positions.ToArray(), normals.ToArray(), uvs.ToArray(), colours.ToArray(), indices.ToArray(),
            new MeshGroup[] { new(0, 0, barkCount), new(1, barkCount, (uint)indices.Count - barkCount) },
            new MeshMaterialBinding[] { new(0, bark), new(1, leaves) }, ReadOnlyMemory<Vector4>.Empty);
    }

    /// <summary>A trunk to the tip, ringed by whorls of drooping branches that shorten toward it, each carrying cards along it.</summary>
    private static void Conifer(Random rng, List<Limb> limbs, List<(Vector3, Vector3, float, float)> cards, float foot, float top, float crownBase,
        float radius, float trunk, float card)
    {
        Limb stem = Grow(rng, new Vector3(0, foot - (Sink * (top - foot)), 0), Lean(rng, .03f), top - foot, trunk, .25f, 6, .04f, 0, TrunkSides);
        limbs.Add(stem);
        int whorls = 7;
        float azimuth = (float)(rng.NextDouble() * MathF.Tau);
        for (int w = 0; w < whorls; w++)
        {
            float t = (crownBase - foot + ((top - crownBase) * (w + .3f) / whorls)) / (top - foot);
            (Vector3 at, _, float r) = At(stem, t);
            float share = 1 - ((at.Y - crownBase) / (top - crownBase));
            int count = 4 + rng.Next(2);
            for (int k = 0; k < count; k++)
            {
                azimuth += (MathF.Tau / count) + (float)((rng.NextDouble() - .5) * .6);
                // Inside the tier's cards, which carry the branch's look; the limb only shows through their gaps.
                float length = radius * (.2f + (.55f * share)) * (float)(.85 + (rng.NextDouble() * .3));
                Vector3 out_ = Vector3.Normalize(new Vector3(MathF.Cos(azimuth), -.15f + (float)(rng.NextDouble() * .25), MathF.Sin(azimuth)));
                limbs.Add(Grow(rng, at, out_, length, r * .22f, .3f, 2, .08f, -.12f, LimbSides));
            }
        }

        // The cards lie in tiers on a cone, each tier drooping from its whorl, so the crown keeps the sprite's stacked shape.
        int tiers = whorls, perTier = CardCount(radius, top - crownBase, card) / tiers;
        for (int w = 0; w < tiers; w++)
        {
            float y = crownBase + ((top - crownBase) * (w + .5f) / tiers);
            float share = 1 - ((y - crownBase) / (top - crownBase));
            float reach = radius * (.2f + (.85f * share));
            int count = Math.Max(3, (int)(perTier * (.3f + share)));
            for (int c = 0; c < count; c++)
            {
                float a = (float)(rng.NextDouble() * MathF.Tau), out_ = reach * MathF.Sqrt(.35f + (.65f * (float)rng.NextDouble()));
                Vector3 at = new(MathF.Cos(a) * out_, y - (out_ / Math.Max(reach, 1e-3f) * (top - crownBase) / tiers * .5f), MathF.Sin(a) * out_);
                Vector3 outward = Vector3.Normalize(new Vector3(MathF.Cos(a), .2f, MathF.Sin(a)));
                cards.Add((at, outward, card * (float)(.85 + (rng.NextDouble() * .3)), .3f + (.6f * out_ / Math.Max(reach, 1e-3f))));
            }
        }

        cards.Add((new Vector3(stem.Points[^1].X, top - (card * .35f), stem.Points[^1].Z), Vector3.UnitY, card * .7f, .6f));
    }

    /// <summary>A trunk forking below the crown into limbs, each into twigs, their ends carrying the crown's cards.</summary>
    private static void Broadleaf(Random rng, List<Limb> limbs, List<(Vector3, Vector3, float, float)> cards, float foot, float top, float crownBase,
        float radius, float trunk, float card)
    {
        float fork = crownBase + ((top - crownBase) * .2f);
        Limb stem = Grow(rng, new Vector3(0, foot - (Sink * (top - foot)), 0), Lean(rng, .06f), fork - foot, trunk, .7f, 4, .06f, 0, TrunkSides);
        limbs.Add(stem);
        Vector3 crotch = stem.Points[^1];
        float crownHeight = top - crotch.Y;
        int count = 4 + rng.Next(3);
        float azimuth = (float)(rng.NextDouble() * MathF.Tau);
        for (int k = 0; k < count; k++)
        {
            azimuth += (MathF.Tau / count) + (float)((rng.NextDouble() - .5) * .5);
            float splay = (25 + (float)(rng.NextDouble() * 35)) * MathF.PI / 180;
            Vector3 out_ = Vector3.Normalize(new Vector3(MathF.Cos(azimuth) * MathF.Sin(splay), MathF.Cos(splay), MathF.Sin(azimuth) * MathF.Sin(splay)));
            // Ending inside the crown's shell, so the cards hide its tip.
            float length = MathF.Sqrt((radius * radius * .3f) + (crownHeight * crownHeight * .2f)) * (float)(.8 + (rng.NextDouble() * .3));
            Limb limb = Grow(rng, crotch, out_, length, trunk * .5f, .35f, 3, .1f, .05f, LimbSides);
            limbs.Add(limb);
            int twigs = 2 + rng.Next(2);
            for (int g = 0; g < twigs; g++)
            {
                (Vector3 at, Vector3 axis, float r) = At(limb, .45f + (.5f * (g + .5f) / twigs));
                Vector3 side = Vector3.Normalize(Vector3.Cross(axis, Vector3.UnitY) * (rng.Next(2) * 2 - 1) + (axis * .4f) + (Vector3.UnitY * .3f));
                Limb twig = Grow(rng, at, side, length * .45f, r * .6f, .3f, 2, .15f, .05f, LimbSides);
                limbs.Add(twig);
            }
        }

        // The cards make a rounded crown about the limbs: most on its outer shell, facing out, a few inside so it reads full.
        Vector3 middle = new(0, crotch.Y + (crownHeight * .55f), 0);
        Vector3 radii = new(radius, crownHeight * .5f, radius);
        int cardCount = CardCount(radius, crownHeight, card);
        for (int c = 0; c < cardCount; c++)
        {
            Vector3 direction = RandomUnit(rng);
            // A crown is flatter beneath than above.
            if (direction.Y < 0) direction.Y *= .6f;
            float depth = MathF.Pow((float)rng.NextDouble(), .35f);
            Vector3 at = middle + (direction * radii * (.35f + (.65f * depth)));
            cards.Add((at, Vector3.Normalize(direction + (RandomUnit(rng) * .5f)), card * (float)(.85 + (rng.NextDouble() * .3)), .3f + (.6f * depth)));
        }
    }

    /// <summary>How many cards cover a crown this wide and tall, each this big, a few deep.</summary>
    private static int CardCount(float radius, float height, float card) =>
        Math.Clamp((int)(7 * MathF.PI * radius * Math.Max(height, radius) / Math.Max(card * card, 1e-3f)), 60, 600);

    private static Vector3 Lean(Random rng, float amount) => Vector3.Normalize(Vector3.UnitY + (RandomUnit(rng) * amount));

    /// <summary>A limb from <paramref name="origin"/>: segments wandering by <paramref name="wobble"/> and rising by <paramref name="rise"/>, tapering to <paramref name="tip"/> of its radius.</summary>
    private static Limb Grow(Random rng, Vector3 origin, Vector3 direction, float length, float radius, float tip, int segments, float wobble, float rise, int sides)
    {
        Vector3[] points = new Vector3[segments + 1];
        float[] radii = new float[segments + 1];
        points[0] = origin;
        Vector3 d = direction;
        for (int i = 1; i <= segments; i++)
        {
            d = Vector3.Normalize(d + (RandomUnit(rng) * wobble) + (Vector3.UnitY * rise));
            points[i] = points[i - 1] + (d * length / segments);
        }

        for (int i = 0; i <= segments; i++) radii[i] = radius * (1 - ((1 - tip) * i / segments));
        if (sides == TrunkSides) radii[0] *= 1.35f;
        return new(points, radii, sides);
    }

    /// <summary>The point, axis and radius a share <paramref name="t"/> along a limb.</summary>
    private static (Vector3 Point, Vector3 Axis, float Radius) At(Limb limb, float t)
    {
        float[] lengths = new float[limb.Points.Length];
        for (int i = 1; i < lengths.Length; i++) lengths[i] = lengths[i - 1] + Vector3.Distance(limb.Points[i - 1], limb.Points[i]);
        float target = Math.Clamp(t, 0, 1) * lengths[^1];
        int k = 0;
        while (k < lengths.Length - 2 && lengths[k + 1] < target) k++;
        float span = lengths[k + 1] - lengths[k], f = span > 0 ? (target - lengths[k]) / span : 0;
        return (Vector3.Lerp(limb.Points[k], limb.Points[k + 1], f), Vector3.Normalize(limb.Points[k + 1] - limb.Points[k]),
            limb.Radii[k] + ((limb.Radii[k + 1] - limb.Radii[k]) * f));
    }

    /// <summary>A low-sided tube along a limb, its rings carried along without twisting, bark tiled to its size; darker at the foot.</summary>
    private static void Tube(Limb limb, Vector2 barkTile, float top, List<Vector3> positions, List<Vector3> normals, List<Vector2> uvs, List<Color> colours, List<uint> indices)
    {
        uint first = (uint)positions.Count;
        int sides = limb.Sides;
        Vector3 axis = Vector3.Normalize(limb.Points[1] - limb.Points[0]);
        (Vector3 u, Vector3 v) = Perpendicular(axis);
        int around = Math.Max(1, (int)MathF.Round(MathF.Tau * limb.Radii[0] / Math.Max(barkTile.X, 1e-3f)));
        float along = 0;
        for (int i = 0; i < limb.Points.Length; i++)
        {
            if (i > 0)
            {
                along += Vector3.Distance(limb.Points[i - 1], limb.Points[i]);
                Vector3 next = Vector3.Normalize(limb.Points[i] - limb.Points[i - 1]);
                if (i < limb.Points.Length - 1) next = Vector3.Normalize(next + Vector3.Normalize(limb.Points[i + 1] - limb.Points[i]));
                u = Vector3.Normalize(u - (next * Vector3.Dot(u, next)));
                v = Vector3.Cross(next, u);
            }

            float tone = .6f + (.4f * Math.Clamp(limb.Points[i].Y / Math.Max(top * .15f, 1e-3f), 0, 1));
            for (int j = 0; j <= sides; j++)
            {
                float a = MathF.Tau * j / sides;
                Vector3 n = (u * MathF.Cos(a)) + (v * MathF.Sin(a));
                positions.Add(limb.Points[i] + (n * limb.Radii[i]));
                normals.Add(n);
                uvs.Add(new Vector2((float)around * j / sides, along / Math.Max(barkTile.Y, 1e-3f)));
                colours.Add(new Color(tone, tone, tone, 0));
            }
        }

        for (int i = 0; i < limb.Points.Length - 1; i++)
        for (int j = 0; j < sides; j++)
        {
            uint a = first + (uint)((i * (sides + 1)) + j), b = first + (uint)(((i + 1) * (sides + 1)) + j);
            indices.AddRange([a, b, b + 1, a, b + 1, a + 1]);
        }
    }

    /// <summary>
    /// A leaf card at <paramref name="at"/>, laid along <paramref name="along"/>, showing one piece of the crown atlas, both
    /// windings with a normal leaning out of the crown and up, so the crown takes the sky's light and its back is not dark.
    /// </summary>
    private static void Card(Random rng, Vector3 at, Vector3 along, float size, float sway, Vector3 centre, int cells,
        List<Vector3> positions, List<Vector3> normals, List<Vector2> uvs, List<Color> colours, List<uint> indices)
    {
        Vector3 outward = at - centre;
        outward = outward.LengthSquared() > 1e-6f ? Vector3.Normalize(outward) : Vector3.UnitY;
        Vector3 facing = Vector3.Normalize(RandomUnit(rng) + (outward * .6f));
        Vector3 length = Vector3.Normalize(along + (RandomUnit(rng) * .35f));
        Vector3 across = Vector3.Cross(facing, length);
        if (across.LengthSquared() < 1e-6f) across = Perpendicular(facing).U;
        across = Vector3.Normalize(across);
        length = Vector3.Cross(across, facing);
        Vector3 shade = Vector3.Normalize((outward * .6f) + (facing * .4f) + (Vector3.UnitY * .4f));
        Vector3 middle = at + (RandomUnit(rng) * size * .15f);
        int cell = rng.Next(cells * cells);
        float u0 = (float)(cell % cells) / cells, v0 = (float)(cell / cells) / cells, u1 = u0 + (1f / cells), v1 = v0 + (1f / cells);
        float tone = (float)(.88 + (rng.NextDouble() * .12));
        uint first = (uint)positions.Count;
        Vector3 halfA = across * size / 2, halfB = length * size / 2;
        foreach ((Vector3 corner, Vector2 uv) in new[] { (middle - halfA - halfB, new Vector2(u0, v1)), (middle + halfA - halfB, new Vector2(u1, v1)),
                     (middle + halfA + halfB, new Vector2(u1, v0)), (middle - halfA + halfB, new Vector2(u0, v0)) })
        {
            positions.Add(corner);
            normals.Add(shade);
            uvs.Add(uv);
            colours.Add(new Color(tone, tone, tone, sway));
        }

        indices.AddRange([first, first + 1, first + 2, first, first + 2, first + 3, first, first + 2, first + 1, first, first + 3, first + 2]);
    }

    private static (Vector3 U, Vector3 V) Perpendicular(Vector3 axis)
    {
        Vector3 helper = MathF.Abs(axis.X) < .9f ? Vector3.UnitX : Vector3.UnitZ;
        Vector3 u = Vector3.Normalize(Vector3.Cross(axis, helper));
        return (u, Vector3.Cross(axis, u));
    }

    private static Vector3 RandomUnit(Random rng)
    {
        while (true)
        {
            Vector3 v = new((float)((rng.NextDouble() * 2) - 1), (float)((rng.NextDouble() * 2) - 1), (float)((rng.NextDouble() * 2) - 1));
            float l = v.LengthSquared();
            if (l is > .01f and <= 1) return v / MathF.Sqrt(l);
        }
    }
}
