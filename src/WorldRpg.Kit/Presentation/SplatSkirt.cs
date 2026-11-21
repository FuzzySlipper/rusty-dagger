using System.Numerics;
using Rusty.Engine;

namespace WorldRpg.Kit.Presentation;

/// <summary>The joints a skirt's legs push it out with: each leg's thigh, knee and ankle, by name.</summary>
public sealed record SplatSkirtLegs(string LeftUpLeg, string LeftLeg, string LeftFoot, string RightUpLeg, string RightLeg, string RightFoot)
{
    /// <summary>The six joints, left leg then right, each from the hip down.</summary>
    public IReadOnlyList<string> Names => [LeftUpLeg, LeftLeg, LeftFoot, RightUpLeg, RightLeg, RightFoot];
}

/// <summary>
/// A long skirt on joints of its own under <paramref name="Hips"/>, which the legs push open as they swing, so the skirt
/// never stretches between them. Its rest layout is in the character's bind space, in metres, about the vertical line
/// through <paramref name="Centre"/> (x, z) between the hip joints.
/// </summary>
public abstract record SplatSkirt(string Hips, SplatSkirtLegs Legs, Vector2 Centre)
{
    /// <summary>Refuses a layout whose parts disagree in size or hold a value that is not finite.</summary>
    public abstract SplatSkirt Validate();
}

/// <summary>
/// A skirt on two tiers of joints, driven by a cloth sheet: <paramref name="Columns"/> columns round the skirt, from the
/// front (+z) round through +x, of <paramref name="Rows"/> points from the hips to the hem (<paramref name="Grid"/>, column
/// by column, top first). Bone column i of the sheet is <paramref name="BoneColumns"/>[i]: its upper joint hangs at the
/// top of that column and its lower joint at row <see cref="KneeRow"/>, the knee of the cloth. A slit skirt parts at the
/// front below the hips.
/// </summary>
public sealed record SplatClothSkirt(string Hips, SplatSkirtLegs Legs, Vector2 Centre, int Columns, int Rows, IReadOnlyList<Vector3> Grid,
    IReadOnlyList<int> BoneColumns, IReadOnlyList<string> Upper, IReadOnlyList<string> Lower, bool Slit) : SplatSkirt(Hips, Legs, Centre)
{
    /// <summary>The row the lower joints hang at.</summary>
    public int KneeRow => Rows / 2;

    public override SplatSkirt Validate()
    {
        if (Columns < 3 || Rows < 3 || Grid.Count != Columns * Rows || Grid.Any(point => !float.IsFinite(point.X + point.Y + point.Z))
            || BoneColumns.Count == 0 || BoneColumns.Count != Upper.Count || BoneColumns.Count != Lower.Count
            || BoneColumns.Any(column => column < 0 || column >= Columns) || !float.IsFinite(Centre.X + Centre.Y))
            throw new ArgumentException($"A cloth skirt has 3 or more columns and rows, a grid point for each, and an upper and lower joint on each of its bone columns; " +
                $"this one has {Columns} x {Rows}, {Grid.Count} points and {BoneColumns.Count} bone columns for {Upper.Count} upper and {Lower.Count} lower joints.");
        return this;
    }
}

/// <summary>
/// A skirt on one tier of joints, a ring turned open by the legs: joint k hangs from <paramref name="Pivots"/>[k] at the
/// top, toward <paramref name="Theta"/>[k] (radians from the front, +z, toward +x), and the skirt reaches
/// <paramref name="Reach"/>[k][i] from the centre line in that direction at <paramref name="Heights"/>[i], top to hem.
/// </summary>
public sealed record SplatRingSkirt(string Hips, SplatSkirtLegs Legs, Vector2 Centre, float Top, float Hem, IReadOnlyList<string> Joints,
    IReadOnlyList<float> Theta, IReadOnlyList<Vector3> Pivots, IReadOnlyList<float> Heights, IReadOnlyList<IReadOnlyList<float>> Reach)
    : SplatSkirt(Hips, Legs, Centre)
{
    public override SplatSkirt Validate()
    {
        int n = Joints.Count;
        if (n < 3 || Theta.Count != n || Pivots.Count != n || Reach.Count != n || Heights.Count < 2 || Reach.Any(column => column.Count != Heights.Count)
            || !(Top > Hem) || Heights.Zip(Heights.Skip(1)).Any(pair => !(pair.First > pair.Second))
            || Theta.Concat(Heights).Concat(Reach.SelectMany(column => column)).Any(value => !float.IsFinite(value))
            || Pivots.Any(point => !float.IsFinite(point.X + point.Y + point.Z)) || !float.IsFinite(Centre.X + Centre.Y))
            throw new ArgumentException($"A ring skirt has 3 or more joints, each with a direction, a pivot and a reach at every height, its heights falling from " +
                $"above its hem; this one has {n} joints, {Theta.Count} directions, {Pivots.Count} pivots and {Heights.Count} heights.");
        return this;
    }
}

/// <summary>
/// A <see cref="SplatSkirt"/> fitted to a character's rig: the indices of the joints it reads and places, and where they
/// stand at rest. Every walker of the character drives a <see cref="Sheet"/> of its own from it.
/// </summary>
public sealed class SplatSkirtRig
{
    private SplatSkirtRig(SplatSkirt skirt, uint hips, uint[] legs, Transform hipsRest)
    {
        Skirt = skirt;
        Hips = hips;
        Legs = legs;
        HipsRest = hipsRest;
    }

    public SplatSkirt Skirt { get; }
    internal uint Hips { get; }
    internal uint[] Legs { get; }
    internal Transform HipsRest { get; }
    internal uint[] Placed { get; private init; } = [];
    internal Transform[] PlacedRest { get; private init; } = [];
    internal ClothLayout? Cloth { get; private init; }
    internal RingLayout? Ring { get; private init; }

    /// <summary>
    /// <paramref name="skirt"/> on the rig of <paramref name="joints"/> (<c>ReadJoints</c>), whose rest pose is
    /// <paramref name="rest"/> (a model sample of the empty clip): refused when the rig lacks a joint it names.
    /// </summary>
    public static SplatSkirtRig Resolve(SplatSkirt skirt, ReadOnlySpan<AnimationJointInfo> joints, ReadOnlySpan<JointPose> rest)
    {
        skirt.Validate();
        if (rest.Length != joints.Length)
            throw new ArgumentException($"A skirt's rest pose has a joint for each of the rig's {joints.Length}, not {rest.Length}.", nameof(rest));
        uint hips = AnimationJoints.IndexOf(joints, skirt.Hips);
        IReadOnlyList<string> names = skirt.Legs.Names;
        uint[] legs = new uint[names.Count];
        Vector3[] legsRest = new Vector3[names.Count];
        for (int leg = 0; leg < names.Count; leg++)
        {
            legs[leg] = IndexOf(joints, names[leg]);
            legsRest[leg] = rest[(int)legs[leg]].Model.Translation;
        }
        Transform hipsRest = rest[(int)hips].Model;
        return skirt switch
        {
            SplatClothSkirt cloth => ResolveCloth(cloth, joints, rest, hips, legs, hipsRest),
            SplatRingSkirt ring => ResolveRing(ring, joints, rest, hips, legs, hipsRest, legsRest),
            _ => throw new ArgumentException($"No sheet drives a skirt of type {skirt.GetType().Name}.", nameof(skirt)),
        };
    }

    /// <summary>A walker's sheet, at rest until its first step.</summary>
    public SplatSkirtSheet Sheet() => Cloth is { } cloth ? new SplatClothSheet(this, cloth) : new SplatRingSheet(this, Ring!);

    private static SplatSkirtRig ResolveCloth(SplatClothSkirt cloth, ReadOnlySpan<AnimationJointInfo> joints, ReadOnlySpan<JointPose> rest,
        uint hips, uint[] legs, Transform hipsRest)
    {
        List<uint> placed = [];
        for (int i = 0; i < cloth.BoneColumns.Count; i++)
        {
            placed.Add(IndexOf(joints, cloth.Upper[i]));
            placed.Add(IndexOf(joints, cloth.Lower[i]));
        }
        Transform[] placedRest = new Transform[placed.Count];
        for (int i = 0; i < placed.Count; i++) placedRest[i] = rest[(int)placed[i]].Model;
        return new(cloth, hips, legs, hipsRest) { Placed = [.. placed], PlacedRest = placedRest, Cloth = new ClothLayout(cloth) };
    }

    private static SplatSkirtRig ResolveRing(SplatRingSkirt ring, ReadOnlySpan<AnimationJointInfo> joints, ReadOnlySpan<JointPose> rest,
        uint hips, uint[] legs, Transform hipsRest, Vector3[] legsRest)
    {
        uint[] placed = new uint[ring.Joints.Count];
        Transform[] placedRest = new Transform[placed.Length];
        for (int k = 0; k < placed.Length; k++)
        {
            placed[k] = IndexOf(joints, ring.Joints[k]);
            placedRest[k] = rest[(int)placed[k]].Model;
        }
        // At rest a leg asks for no more room than it had then.
        return new(ring, hips, legs, hipsRest) { Placed = placed, PlacedRest = placedRest, Ring = new RingLayout(ring, legsRest) };
    }

    private static uint IndexOf(ReadOnlySpan<AnimationJointInfo> joints, string name) => AnimationJoints.IndexOf(joints, name);
}

/// <summary>
/// One walker's skirt: each step it reads where the legs and hips are now, moves the skirt on by the time since its last
/// step, and gives the skirt joints' placements in the character's own space.
/// </summary>
public abstract class SplatSkirtSheet
{
    /// <summary>A step longer than this starts the skirt again from rest about the hips, as after a pause.</summary>
    public const float LongestStepSeconds = .25F;

    private protected SplatSkirtSheet(SplatSkirtRig rig) => Rig = rig;

    private protected SplatSkirtRig Rig { get; }

    /// <summary>
    /// Moves the skirt on by <paramref name="seconds"/> to <paramref name="pose"/>, the character's joints in
    /// <c>ReadJoints</c> order as its clips put them, and returns the skirt joints' placements (full-weight
    /// <see cref="PoseSpace.Model"/> overrides). The first step, and one after a long gap, hang it at rest about the hips.
    /// </summary>
    public JointOverride[] Step(float seconds, ReadOnlySpan<JointPose> pose)
    {
        if (!float.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds), seconds, "A skirt steps by 0 or more seconds.");
        Transform hips = pose[(int)Rig.Hips].Model;
        Span<Vector3> legs = stackalloc Vector3[6];
        for (int leg = 0; leg < 6; leg++) legs[leg] = pose[(int)Rig.Legs[leg]].Model.Translation;
        (Quaternion turn, Vector3 shift) = Skin.Of(Rig.HipsRest, hips);
        bool start = !Started || seconds > LongestStepSeconds;
        Advance(start ? 0 : seconds, start, turn, shift, legs);
        Started = true;
        return Placements(turn, shift);
    }

    private bool Started { get; set; }

    private protected abstract void Advance(float seconds, bool start, Quaternion turn, Vector3 shift, ReadOnlySpan<Vector3> legs);

    private protected abstract JointOverride[] Placements(Quaternion turn, Vector3 shift);
}

/// <summary>The hips' skin: the move that takes the bind space to where the hips are now, as a turn then a shift.</summary>
internal static class Skin
{
    internal static (Quaternion Turn, Vector3 Shift) Of(Transform rest, Transform now)
    {
        Quaternion turn = Quaternion.Normalize(now.Rotation * Quaternion.Conjugate(rest.Rotation));
        return (turn, now.Translation - Vector3.Transform(rest.Translation, turn));
    }
}

/// <summary>
/// The two-tier skirt's cloth: asset-pipeline's bench sheet (<c>skirt_bones.py drive</c>, Patch's approved settings). The
/// top row rides the hips; the rest is pulled gently back to its rest shape about them, falls under gravity, holds
/// together by stiff edges down each column and softer ones round it, with shear and bend edges, and is pushed out of
/// capsules round the thighs and shins.
/// </summary>
/// <summary>An edge of the sheet between points <paramref name="A"/> and <paramref name="B"/>, each moved by its weight (0 on the top row).</summary>
internal readonly struct ClothEdge(int a, int b, float length, float compliance, float weightA, float weightB)
{
    // Fields, not properties: the sheet's inner loop reads them thousands of times a step.
    internal readonly int A = a, B = b;
    internal readonly float Length = length, Compliance = compliance, WeightA = weightA, WeightB = weightB;
}

internal sealed class ClothLayout
{
    /// <summary>The bench's 8 substeps a frame of a 30 fps clip.</summary>
    internal const float SubstepsPerSecond = 240F;
    internal const float Damping = .92F, HomingPerSecond = 18F, Gravity = 9.81F, Relaxation = .45F;
    internal const int Iterations = 5;
    private const float Warp = 1e-5F, WarpBend = 2e-3F, Weft = 3e-4F, Shear = 1e-3F, WeftBend = 4e-3F;

    internal ClothLayout(SplatClothSkirt skirt)
    {
        Columns = skirt.Columns;
        Rows = skirt.Rows;
        Grid = [.. skirt.Grid];
        List<(int A, int B, float Length, float Compliance)> edges = [];
        int nu = Columns, nv = Rows;
        void Edge(int k1, int j1, int k2, int j2, float compliance)
        {
            float length = Vector3.Distance(Grid[k1 * nv + j1], Grid[k2 * nv + j2]);
            if (length > 1e-5F) edges.Add((k1 * nv + j1, k2 * nv + j2, length, compliance));
        }
        for (int k = 0; k < nu; k++)
            for (int j = 0; j < nv; j++)
            {
                if (j + 1 < nv) Edge(k, j, k, j + 1, Warp);
                if (j + 2 < nv) Edge(k, j, k, j + 2, WarpBend);
                int next = (k + 1) % nu;
                // A slit skirt's two front columns hold together only at the hips.
                if (!(skirt.Slit && k == nu - 1 && j >= 2))
                {
                    Edge(k, j, next, j, Weft);
                    if (j + 1 < nv)
                    {
                        Edge(k, j, next, j + 1, Shear);
                        Edge(next, j, k, j + 1, Shear);
                    }
                }
                if (!(skirt.Slit && (k == nu - 2 || k == nu - 1) && j >= 2)) Edge(k, j, (k + 2) % nu, j, WeftBend);
            }
        // The top row rides the hips: its points have no weight in the sheet.
        Edges = [.. edges.Select(edge => new ClothEdge(edge.A, edge.B, edge.Length, edge.Compliance, edge.A % nv == 0 ? 0 : 1, edge.B % nv == 0 ? 0 : 1))];
        Vector3 centre = new(skirt.Centre.X, 0, skirt.Centre.Y);
        int knee = skirt.KneeRow;
        BoneColumns = [.. skirt.BoneColumns];
        Outward = new Vector3[BoneColumns.Length];
        UpperRest = new Quaternion[BoneColumns.Length];
        LowerRest = new Quaternion[BoneColumns.Length];
        for (int i = 0; i < BoneColumns.Length; i++)
        {
            int k = BoneColumns[i];
            Vector3 top = Grid[k * nv], mid = Grid[k * nv + knee], hem = Grid[k * nv + nv - 1];
            Outward[i] = (top - centre) with { Y = 0 };
            UpperRest[i] = Frame(mid - top, Outward[i]);
            LowerRest[i] = Frame(hem - mid, Outward[i]);
        }
        Knee = knee;
    }

    internal int Columns { get; }
    internal int Rows { get; }
    internal int Knee { get; }
    internal Vector3[] Grid { get; }
    internal ClothEdge[] Edges { get; }
    internal int[] BoneColumns { get; }
    /// <summary>Each bone column's outward direction at rest, level.</summary>
    internal Vector3[] Outward { get; }
    internal Quaternion[] UpperRest { get; }
    internal Quaternion[] LowerRest { get; }

    /// <summary>The turn whose +y is up along <paramref name="down"/> (its reverse) and whose +z is <paramref name="outward"/> made square to it.</summary>
    internal static Quaternion Frame(Vector3 down, Vector3 outward)
    {
        Vector3 y = -down / MathF.Max(down.Length(), 1e-9F);
        Vector3 z = outward - Vector3.Dot(outward, y) * y;
        if (z.Length() < 1e-6F) z = Vector3.UnitZ - y.Z * y;
        z /= MathF.Max(z.Length(), 1e-9F);
        Vector3 x = Vector3.Cross(y, z);
        x /= MathF.Max(x.Length(), 1e-9F);
        return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(new Matrix4x4(
            x.X, x.Y, x.Z, 0,
            y.X, y.Y, y.Z, 0,
            z.X, z.Y, z.Z, 0,
            0, 0, 0, 1)));
    }
}

/// <summary>A walker's two-tier skirt: the cloth sheet of <see cref="ClothLayout"/>.</summary>
public sealed class SplatClothSheet : SplatSkirtSheet
{
    /// <summary>The leg capsules (thigh and shin) the sheet keeps out of, and the gap it keeps from them.</summary>
    public const float ThighRadius = .082F, ShinRadius = .064F, Gap = .018F;

    private readonly ClothLayout layout;
    private readonly Vector3[] position, velocity, before, moved, home;
    private readonly float[] stiffness;
    private readonly Vector3[] lastLegs = new Vector3[6];
    private Quaternion lastTurn = Quaternion.Identity;
    private Vector3 lastShift;

    internal SplatClothSheet(SplatSkirtRig rig, ClothLayout layout) : base(rig)
    {
        this.layout = layout;
        position = new Vector3[layout.Grid.Length];
        velocity = new Vector3[layout.Grid.Length];
        before = new Vector3[layout.Grid.Length];
        moved = new Vector3[layout.Grid.Length];
        home = new Vector3[layout.Grid.Length];
        stiffness = new float[layout.Edges.Length];
    }

    /// <summary>The sheet's points, column by column, top first, in the character's own space.</summary>
    public ReadOnlySpan<Vector3> Points => position;

    private protected override void Advance(float seconds, bool start, Quaternion turn, Vector3 shift, ReadOnlySpan<Vector3> legs)
    {
        int nv = layout.Rows;
        if (start)
        {
            for (int i = 0; i < position.Length; i++) position[i] = shift + Vector3.Transform(layout.Grid[i], turn);
            Array.Clear(velocity);
        }
        else if (seconds > 0)
        {
            int substeps = Math.Max(1, (int)MathF.Ceiling(seconds * ClothLayout.SubstepsPerSecond));
            float dt = seconds / substeps, softness = 1F / (dt * dt);
            ClothEdge[] edges = layout.Edges;
            for (int e = 0; e < edges.Length; e++)
                stiffness[e] = ClothLayout.Relaxation / MathF.Max(edges[e].WeightA + edges[e].WeightB + edges[e].Compliance * softness, 1e-12F);
            Vector3 fall = new(0, -ClothLayout.Gravity * dt, 0);
            Span<Vector3> capsules = stackalloc Vector3[6];
            for (int step = 1; step <= substeps; step++)
            {
                float along = (float)step / substeps;
                Quaternion t = Quaternion.Slerp(lastTurn, turn, along);
                Vector3 s = Vector3.Lerp(lastShift, shift, along);
                for (int i = 0; i < home.Length; i++) home[i] = s + Vector3.Transform(layout.Grid[i], t);
                for (int leg = 0; leg < 6; leg++) capsules[leg] = Vector3.Lerp(lastLegs[leg], legs[leg], along);
                Array.Copy(position, before, position.Length);
                for (int i = 0; i < position.Length; i++)
                {
                    if (i % nv == 0) { position[i] = home[i]; continue; }
                    velocity[i] = velocity[i] * ClothLayout.Damping + fall + (home[i] - position[i]) * (ClothLayout.HomingPerSecond * dt);
                    position[i] += velocity[i] * dt;
                }
                for (int iteration = 0; iteration < ClothLayout.Iterations; iteration++)
                {
                    Array.Clear(moved);
                    for (int e = 0; e < edges.Length; e++)
                    {
                        ref readonly ClothEdge edge = ref edges[e];
                        Vector3 d = position[edge.B] - position[edge.A];
                        float distance = d.Length();
                        Vector3 correction = d * ((distance - edge.Length) * stiffness[e] / MathF.Max(distance, 1e-9F));
                        moved[edge.A] += correction * edge.WeightA;
                        moved[edge.B] -= correction * edge.WeightB;
                    }
                    for (int i = 0; i < position.Length; i++) position[i] += moved[i];
                    for (int i = 0; i < position.Length; i += nv) position[i] = home[i];
                }
                for (int side = 0; side < 2; side++)
                {
                    Vector3 hip = capsules[side * 3], knee = capsules[side * 3 + 1], ankle = capsules[side * 3 + 2];
                    PushOut(hip, knee, ThighRadius + Gap);
                    PushOut(knee, ankle, ShinRadius + Gap);
                }
                for (int i = 0; i < position.Length; i += nv) position[i] = home[i];
                for (int i = 0; i < position.Length; i++) velocity[i] = (position[i] - before[i]) / dt;
            }
        }
        lastTurn = turn;
        lastShift = shift;
        legs.CopyTo(lastLegs);
    }

    private protected override JointOverride[] Placements(Quaternion turn, Vector3 shift)
    {
        int nv = layout.Rows;
        JointOverride[] placements = new JointOverride[layout.BoneColumns.Length * 2];
        for (int i = 0; i < layout.BoneColumns.Length; i++)
        {
            int k = layout.BoneColumns[i];
            Vector3 top = position[k * nv], mid = position[k * nv + layout.Knee], hem = position[k * nv + nv - 1];
            Vector3 outward = Vector3.Transform(layout.Outward[i], turn);
            Quaternion upper = ClothLayout.Frame(mid - top, outward) * Quaternion.Conjugate(layout.UpperRest[i]);
            Quaternion lower = ClothLayout.Frame(hem - mid, outward) * Quaternion.Conjugate(layout.LowerRest[i]);
            placements[2 * i] = JointOverride.Place(Rig.Placed[2 * i], PoseSpace.Model, top, Quaternion.Normalize(upper * Rig.PlacedRest[2 * i].Rotation), 1F);
            placements[2 * i + 1] = JointOverride.Place(Rig.Placed[2 * i + 1], PoseSpace.Model, mid, Quaternion.Normalize(lower * Rig.PlacedRest[2 * i + 1].Rotation), 1F);
        }
        return placements;
    }

    /// <summary>Pushes every point out of the capsule from <paramref name="a"/> to <paramref name="b"/>.</summary>
    private void PushOut(Vector3 a, Vector3 b, float radius)
    {
        Vector3 ab = b - a;
        float length = ab.LengthSquared();
        for (int i = 0; i < position.Length; i++)
        {
            float t = length < 1e-12F ? 0 : Math.Clamp(Vector3.Dot(position[i] - a, ab) / length, 0, 1);
            Vector3 nearest = a + t * ab, d = position[i] - nearest;
            float distance = d.Length();
            if (distance < radius) position[i] = nearest + d / MathF.Max(distance, 1e-9F) * radius;
        }
    }
}

/// <summary>
/// The one-tier skirt's ring: asset-pipeline's bench drive for rigid columns (<c>skirt_bones.py drive</c>, one tier). In the
/// hips' frame each column turns outward about its pivot just far enough that the thighs and shins stay inside the skirt
/// from its top to its hem, asking no more room than a leg had at rest. A column falls back no faster than
/// <see cref="ReleasePerSecond"/>, so the skirt settles after a stride, and turns at least <see cref="Spread"/> of its
/// neighbours' turn, so a knee pushing the front out pulls the sides along like cloth.
/// </summary>
internal sealed class RingLayout
{
    internal const float Gap = .02F, ThighRadius = .085F, ShinRadius = .065F;
    private const float MostTurn = SplatRingSheet.MostTurn;
    /// <summary>Points sampled along each thigh and shin.</summary>
    internal const int Samples = 12;

    internal RingLayout(SplatRingSkirt skirt, Vector3[] legsAtRest)
    {
        int n = skirt.Joints.Count;
        Centre = new(skirt.Centre.X, 0, skirt.Centre.Y);
        Top = skirt.Top;
        Hem = skirt.Hem;
        Direction = [.. skirt.Theta.Select(theta => new Vector3(MathF.Sin(theta), 0, MathF.Cos(theta)))];
        Axis = [.. Direction.Select(d => new Vector3(-d.Z, 0, d.X))];
        Pivots = [.. skirt.Pivots];
        // Interpolated over rising heights, as numpy's interp does.
        Heights = [.. skirt.Heights.Reverse()];
        Reach = [.. skirt.Reach.Select(column => column.Reverse().ToArray())];
        TopReach = [.. skirt.Reach.Select(column => column[0])];
        Vector3[] rest = LegSamples(legsAtRest, out float[] radius);
        Room = new float[rest.Length, n];
        for (int i = 0; i < rest.Length; i++)
            for (int k = 0; k < n; k++)
                Room[i, k] = MathF.Min(radius[i] + Gap, ReachAt(k, rest[i].Y) - Vector3.Dot(rest[i] - Centre, Direction[k]));
    }

    internal Vector3 Centre { get; }
    internal float Top { get; }
    internal float Hem { get; }
    internal Vector3[] Direction { get; }
    internal Vector3[] Axis { get; }
    internal Vector3[] Pivots { get; }
    internal float[] Heights { get; }
    internal float[][] Reach { get; }
    internal float[] TopReach { get; }
    /// <summary>The room each leg sample had inside each column at rest.</summary>
    internal float[,] Room { get; }

    internal float ReachAt(int column, float height)
    {
        float[] heights = Heights, reach = Reach[column];
        if (height <= heights[0]) return reach[0];
        if (height >= heights[^1]) return reach[^1];
        int above = 1;
        while (heights[above] < height) above++;
        float along = (height - heights[above - 1]) / (heights[above] - heights[above - 1]);
        return reach[above - 1] + along * (reach[above] - reach[above - 1]);
    }

    /// <summary>Points along both thighs and shins (hip, knee, ankle per leg), with the radius of the capsule each lies in.</summary>
    internal static Vector3[] LegSamples(ReadOnlySpan<Vector3> legs, out float[] radius)
    {
        Vector3[] points = new Vector3[4 * Samples];
        radius = new float[points.Length];
        int at = 0;
        for (int side = 0; side < 2; side++)
            for (int bone = 0; bone < 2; bone++)
            {
                Vector3 from = legs[side * 3 + bone], to = legs[side * 3 + bone + 1];
                for (int s = 0; s < Samples; s++, at++)
                {
                    points[at] = Vector3.Lerp(from, to, (float)s / (Samples - 1));
                    radius[at] = bone == 0 ? ThighRadius : ShinRadius;
                }
            }
        return points;
    }

    /// <summary>How far each column must turn outward to keep these leg points inside the skirt.</summary>
    internal void Need(ReadOnlySpan<Vector3> points, Span<float> turn)
    {
        for (int k = 0; k < turn.Length; k++)
        {
            float most = float.NegativeInfinity;
            for (int i = 0; i < points.Length; i++)
            {
                float y = points[i].Y;
                if (y > Top - .02F || y < Hem - .05F) continue;
                float dr = ReachAt(k, y) - TopReach[k], h = Top - y;
                float want = Vector3.Dot(points[i] - Centre, Direction[k]) + Room[i, k] - TopReach[k];
                if (!(want > dr)) continue;
                float rho = MathF.Sqrt(dr * dr + h * h), phi = MathF.Atan2(dr, h);
                most = MathF.Max(most, MathF.Asin(Math.Clamp(want / MathF.Max(rho, 1e-6F), -1, 1)) - phi);
            }
            turn[k] = float.IsNegativeInfinity(most) ? 0 : MathF.Max(MathF.Min(most, MostTurn), 0);
        }
    }
}

/// <summary>A walker's one-tier skirt: the ring of <see cref="RingLayout"/>.</summary>
public sealed class SplatRingSheet : SplatSkirtSheet
{
    /// <summary>
    /// A column turns out at most <see cref="MostTurn"/> radians, falls back no faster than <see cref="ReleasePerSecond"/>,
    /// and turns at least <see cref="Spread"/> of its neighbours' turn.
    /// </summary>
    public const float MostTurn = .9F, ReleasePerSecond = 2.5F, Spread = .7F;

    private readonly RingLayout layout;
    private readonly float[] held, turns;

    internal SplatRingSheet(SplatSkirtRig rig, RingLayout layout) : base(rig)
    {
        this.layout = layout;
        held = new float[layout.Direction.Length];
        turns = new float[layout.Direction.Length];
    }

    /// <summary>Each column's outward turn, in radians, after the last step.</summary>
    public IReadOnlyList<float> Turns => turns;

    private protected override void Advance(float seconds, bool start, Quaternion turn, Vector3 shift, ReadOnlySpan<Vector3> legs)
    {
        Span<Vector3> frame = stackalloc Vector3[6];
        Quaternion back = Quaternion.Conjugate(turn);
        for (int leg = 0; leg < 6; leg++) frame[leg] = Vector3.Transform(legs[leg] - shift, back);
        Span<float> need = stackalloc float[held.Length];
        layout.Need(RingLayout.LegSamples(frame, out _), need);
        for (int k = 0; k < held.Length; k++) held[k] = start ? need[k] : MathF.Max(need[k], held[k] - ReleasePerSecond * seconds);
        held.CopyTo(turns, 0);
        Span<float> spread = stackalloc float[held.Length];
        for (int round = 0; round < held.Length / 2; round++)
        {
            for (int k = 0; k < held.Length; k++)
                spread[k] = MathF.Max(turns[k], Spread * MathF.Max(turns[(k + held.Length - 1) % held.Length], turns[(k + 1) % held.Length]));
            spread.CopyTo(turns);
        }
    }

    private protected override JointOverride[] Placements(Quaternion turn, Vector3 shift)
    {
        JointOverride[] placements = new JointOverride[turns.Length];
        for (int k = 0; k < turns.Length; k++)
        {
            Quaternion open = Quaternion.CreateFromAxisAngle(layout.Axis[k], turns[k]);
            Vector3 pivot = layout.Pivots[k], rest = Rig.PlacedRest[k].Translation;
            Vector3 at = shift + Vector3.Transform(pivot + Vector3.Transform(rest - pivot, open), turn);
            placements[k] = JointOverride.Place(Rig.Placed[k], PoseSpace.Model, at, Quaternion.Normalize(turn * open * Rig.PlacedRest[k].Rotation), 1F);
        }
        return placements;
    }
}
