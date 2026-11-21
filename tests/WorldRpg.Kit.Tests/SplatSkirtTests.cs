using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit.Presentation;
using Xunit;

namespace WorldRpg.Kit.Tests;

/// <summary>
/// A long skirt on joints of its own, pushed open by the legs: the two-tier cloth sheet and the one-tier ring, ported
/// from asset-pipeline's bench drive, on a small made-up rig.
/// </summary>
public sealed class SplatSkirtTests
{
    private const float Step = 1F / 60;
    private static readonly SplatSkirtLegs Legs = new("LeftUpLeg", "LeftLeg", "LeftFoot", "RightUpLeg", "RightLeg", "RightFoot");
    private static readonly Vector3 HipsAt = new(0, 1, 0);
    private static readonly Vector3[] RestLegs = [new(.09F, .95F, 0), new(.09F, .5F, 0), new(.09F, .08F, 0), new(-.09F, .95F, 0), new(-.09F, .5F, 0), new(-.09F, .08F, 0)];

    [Fact]
    public void A_cloth_skirt_at_rest_hangs_where_its_joints_were_bound()
    {
        (AnimationJointInfo[] joints, JointPose[] rest) = ClothRig(out SplatClothSkirt skirt);
        SplatSkirtSheet sheet = SplatSkirtRig.Resolve(skirt, joints, rest).Sheet();

        JointOverride[] placed = [];
        for (int step = 0; step < 60; step++) placed = sheet.Step(step == 0 ? 0 : Step, rest);

        // Each column's two joints are placed in the character's space, the upper at its top and the lower at the knee
        // of the cloth, turned as they were bound: the sheet hangs within a centimetre of its rest.
        Assert.Equal(2 * skirt.BoneColumns.Count, placed.Length);
        foreach (JointOverride joint in placed)
        {
            Assert.Equal((PoseSpace.Model, 1F), (joint.Space, joint.Weight));
            JointPose bound = rest[(int)joint.Joint];
            Assert.True(Vector3.Distance(bound.Model.Translation, joint.Translation) < .01F, $"joint {joint.Joint} hangs at {joint.Translation}, bound at {bound.Model.Translation}");
            Assert.True(Angle(bound.Model.Rotation, joint.Rotation) < .05F, $"joint {joint.Joint} is turned {Angle(bound.Model.Rotation, joint.Rotation)} rad from its binding");
        }
    }

    [Fact]
    public void A_cloth_skirt_rides_the_hips_and_a_knee_swung_forward_pushes_its_front_out_clear_of_the_leg()
    {
        (AnimationJointInfo[] joints, JointPose[] rest) = ClothRig(out SplatClothSkirt skirt);
        SplatClothSheet sheet = (SplatClothSheet)SplatSkirtRig.Resolve(skirt, joints, rest).Sheet();
        sheet.Step(0, rest);

        // The hips step aside: the top row goes with them at once.
        JointPose[] aside = Posed(rest, hips: HipsAt + new Vector3(.1F, 0, 0), legs: [.. RestLegs.Select(leg => leg + new Vector3(.1F, 0, 0))]);
        sheet.Step(Step, aside);
        for (int k = 0; k < skirt.Columns; k++) AssertNear(skirt.Grid[k * skirt.Rows] + new Vector3(.1F, 0, 0), sheet.Points[k * skirt.Rows]);

        // The left leg swings forward through where the front of the skirt hung.
        Vector3[] stride = [.. RestLegs];
        stride[1] = new(.09F, .6F, .35F);
        stride[2] = new(.09F, .2F, .5F);
        // It swings there over half a second, as a stride does, and holds.
        for (int step = 1; step <= 60; step++)
            sheet.Step(Step, Posed(rest, HipsAt, [.. RestLegs.Zip(stride, (from, to) => Vector3.Lerp(from, to, MathF.Min(step / 30F, 1)))]));
        float hem = Enumerable.Range(0, skirt.Columns).Max(k => sheet.Points[k * skirt.Rows + skirt.Rows - 1].Z);
        Assert.True(hem > .4F, $"the hem reaches only z {hem}, from .3, round a shin that reaches z .5");
        foreach (Vector3 point in sheet.Points)
        {
            float clear = MathF.Min(Distance(point, stride[0], stride[1]) - SplatClothSheet.ThighRadius, Distance(point, stride[1], stride[2]) - SplatClothSheet.ShinRadius);
            Assert.True(clear > -.005F, $"the sheet at {point} is {-clear} m inside the leg");
        }
    }

    [Fact]
    public void A_long_pause_hangs_the_cloth_at_rest_about_the_hips_again()
    {
        (AnimationJointInfo[] joints, JointPose[] rest) = ClothRig(out SplatClothSkirt skirt);
        SplatClothSheet sheet = (SplatClothSheet)SplatSkirtRig.Resolve(skirt, joints, rest).Sheet();
        sheet.Step(0, rest);
        Vector3[] stride = [.. RestLegs];
        stride[1] = new(.09F, .55F, .3F);
        stride[2] = new(.09F, .15F, .38F);
        for (int step = 0; step < 10; step++) sheet.Step(Step, Posed(rest, HipsAt, stride));

        sheet.Step(SplatSkirtSheet.LongestStepSeconds + .1F, rest);
        for (int i = 0; i < skirt.Grid.Count; i++) AssertNear(skirt.Grid[i], sheet.Points[i]);
        Assert.Throws<ArgumentOutOfRangeException>(() => sheet.Step(-1, rest));
    }

    [Fact]
    public void A_ring_skirt_turns_its_front_out_for_a_knee_falls_back_slowly_and_pulls_its_neighbours_along()
    {
        (AnimationJointInfo[] joints, JointPose[] rest) = RingRig(out SplatRingSkirt skirt);
        SplatRingSheet sheet = (SplatRingSheet)SplatSkirtRig.Resolve(skirt, joints, rest).Sheet();

        // At rest a leg asks for no more room than it had: no column turns, and each joint is placed as bound.
        JointOverride[] placed = sheet.Step(0, rest);
        Assert.All(sheet.Turns, turn => Assert.Equal(0F, turn));
        foreach (JointOverride joint in placed)
        {
            AssertNear(rest[(int)joint.Joint].Model.Translation, joint.Translation);
            Assert.True(Angle(rest[(int)joint.Joint].Model.Rotation, joint.Rotation) < 1e-3F);
        }

        Vector3[] stride = [.. RestLegs];
        stride[1] = new(.05F, .55F, .3F);
        stride[2] = new(.05F, .15F, .4F);
        placed = sheet.Step(Step, Posed(rest, HipsAt, stride));
        float front = sheet.Turns[0];
        Assert.InRange(front, .1F, SplatRingSheet.MostTurn);
        // The front column swings its hem forward, and its neighbours follow at least most of the way.
        Assert.True(Vector3.Transform(-Vector3.UnitY, placed[0].Rotation).Z > .05F);
        Assert.True(sheet.Turns[1] >= SplatRingSheet.Spread * front - 1e-5F && sheet.Turns[^1] >= SplatRingSheet.Spread * front - 1e-5F);

        // The leg back, the front falls back no faster than the release.
        sheet.Step(Step, rest);
        Assert.Equal(front - SplatRingSheet.ReleasePerSecond * Step, sheet.Turns[0], 4);
    }

    [Fact]
    public void A_skirt_is_refused_on_a_rig_without_a_joint_it_names_or_with_a_grid_of_the_wrong_size()
    {
        (AnimationJointInfo[] joints, JointPose[] rest) = ClothRig(out SplatClothSkirt skirt);
        Assert.Throws<ArgumentException>(() => SplatSkirtRig.Resolve(skirt with { Upper = [.. skirt.Upper.SkipLast(1), "Skirt9_U"] }, joints, rest));
        Assert.Throws<ArgumentException>(() => SplatSkirtRig.Resolve(skirt with { Grid = [.. skirt.Grid.Skip(1)] }, joints, rest));
    }

    /// <summary>A cone of cloth 16 columns round and 7 rows down, 25 cm out at the hips and 30 at the hem, a bone column on every other.</summary>
    private static (AnimationJointInfo[], JointPose[]) ClothRig(out SplatClothSkirt skirt)
    {
        const int columns = 16, rows = 7;
        List<Vector3> grid = [];
        for (int k = 0; k < columns; k++)
            for (int j = 0; j < rows; j++)
            {
                float theta = MathF.Tau * k / columns, along = j / (rows - 1F), radius = .25F + .05F * along;
                grid.Add(new(radius * MathF.Sin(theta), .95F - .75F * along, radius * MathF.Cos(theta)));
            }
        int[] bones = [0, 2, 4, 6, 8, 10, 12, 14];
        string[] upper = [.. bones.Select((_, i) => $"Skirt{i}_U")], lower = [.. bones.Select((_, i) => $"Skirt{i}_L")];
        skirt = new("Hips", Legs, Vector2.Zero, columns, rows, grid, bones, upper, lower, Slit: false);
        List<(string Name, Vector3 At)> named = [.. Body()];
        for (int i = 0; i < bones.Length; i++)
        {
            named.Add((upper[i], grid[bones[i] * rows]));
            named.Add((lower[i], grid[bones[i] * rows + rows / 2]));
        }
        return Rig(named);
    }

    /// <summary>A ring of 8 columns from 25 cm out at the hips to 30 at the hem.</summary>
    private static (AnimationJointInfo[], JointPose[]) RingRig(out SplatRingSkirt skirt)
    {
        const int columns = 8;
        float[] theta = [.. Enumerable.Range(0, columns).Select(k => MathF.Tau * k / columns)];
        float[] heights = [.. Enumerable.Range(0, 16).Select(i => .95F - .75F * i / 15)];
        IReadOnlyList<float>[] reach = [.. theta.Select(_ => (IReadOnlyList<float>)[.. heights.Select(y => .25F + .05F * (.95F - y) / .75F)])];
        Vector3[] pivots = [.. theta.Select(t => new Vector3(.25F * MathF.Sin(t), .95F, .25F * MathF.Cos(t)))];
        string[] names = [.. theta.Select((_, k) => $"Skirt{k}")];
        skirt = new("Hips", Legs, Vector2.Zero, .95F, .2F, names, theta, pivots, heights, reach);
        return Rig([.. Body(), .. names.Zip(pivots)]);
    }

    private static IEnumerable<(string, Vector3)> Body() =>
        [("Hips", HipsAt), .. Legs.Names.Zip(RestLegs)];

    private static (AnimationJointInfo[], JointPose[]) Rig(List<(string Name, Vector3 At)> named) =>
        ([.. named.Select(joint => new AnimationJointInfo(joint.Name, joint.Name == "Hips" ? uint.MaxValue : 0))],
         [.. named.Select(joint => Pose(joint.At))]);

    /// <summary>The rest pose with the hips and the six leg joints moved.</summary>
    private static JointPose[] Posed(JointPose[] rest, Vector3 hips, Vector3[] legs)
    {
        JointPose[] pose = [.. rest];
        pose[0] = Pose(hips);
        for (int leg = 0; leg < 6; leg++) pose[1 + leg] = Pose(legs[leg]);
        return pose;
    }

    private static JointPose Pose(Vector3 at)
    {
        Transform placed = new(at, Quaternion.Identity, Vector3.One);
        return new(placed, placed);
    }

    private static float Distance(Vector3 point, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float t = Math.Clamp(Vector3.Dot(point - a, ab) / ab.LengthSquared(), 0, 1);
        return Vector3.Distance(point, a + t * ab);
    }

    private static float Angle(Quaternion a, Quaternion b) => 2 * MathF.Acos(Math.Clamp(MathF.Abs(Quaternion.Dot(a, b)), 0, 1));

    private static void AssertNear(Vector3 expected, Vector3 actual) =>
        Assert.True(Vector3.Distance(expected, actual) < 1e-3F, $"expected {expected}, got {actual}");
}
