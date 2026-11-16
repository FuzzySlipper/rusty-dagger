using Rusty.Engine;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>
/// The bodies a hanging mesh's rig names, in the order a ragdoll takes them (authoring/tools/hanging/hanging.py builds the
/// rigs). A joint named <c>anchor…</c> follows the placed pose: the point the piece hangs from. Every other joint with
/// exactly one child is a swinging body, a capsule from it to that child; a joint with no child ends a strand. A
/// lantern's <c>light</c> joint marks where its light sits and is never a body.
/// </summary>
/// <param name="Bones">The bodies, anchors first in rig order: each its joint, the joint its capsule reaches, and whether it follows the pose.</param>
/// <param name="Links">Each swinging body and the body it hangs from, as indices into <paramref name="Bones"/>.</param>
/// <param name="Light">The <c>light</c> joint, when the rig has one.</param>
internal sealed record DaggerfallHangingRig(IReadOnlyList<DaggerfallHangingBone> Bones, IReadOnlyList<(int Parent, int Child)> Links, uint? Light)
{
    internal const string Anchor = "anchor";
    internal const string LightJoint = "light";

    /// <summary>
    /// How high a hanging model's first anchor stands above its origin, the foot it is drawn from, in its own metres: the
    /// height it is drawn at its sprite's, since it hangs from the sprite's top whatever its width.
    /// </summary>
    /// <exception cref="InvalidDataException">The model names no anchor to hang from.</exception>
    internal static float Height(IAnimationService animation, RenderResource model, string mesh)
    {
        ReadOnlySpan<AnimationJointInfo> joints = animation.ReadJoints(model).Span;
        for (int joint = 0; joint < joints.Length; joint++)
            if (joints[joint].Id.StartsWith(Anchor, StringComparison.Ordinal))
                return animation.SampleModelJointPose(new(model, "", 0, "", 0, 0, false, new Transform(System.Numerics.Vector3.Zero, System.Numerics.Quaternion.Identity, System.Numerics.Vector3.One)))
                    .Span[joint].World.Translation.Y;
        throw new InvalidDataException($"Hanging mesh '{mesh}' names no joint '{Anchor}…' to hang from.");
    }

    /// <exception cref="InvalidDataException">The rig names no anchor, no body, or a body that branches: it could not hang.</exception>
    internal static DaggerfallHangingRig Read(ReadOnlySpan<AnimationJointInfo> joints, string mesh)
    {
        List<uint>[] children = [.. Enumerable.Range(0, joints.Length).Select(_ => new List<uint>())];
        uint? light = null;
        for (uint joint = 0; joint < joints.Length; joint++)
        {
            if (joints[(int)joint].Id == LightJoint) { light = joint; continue; }
            if (joints[(int)joint].Parent < joints.Length) children[joints[(int)joint].Parent].Add(joint);
        }
        List<DaggerfallHangingBone> bones = [];
        Dictionary<uint, int> boneOf = [];
        // Anchors first: a ragdoll's bones hang from those listed before them.
        for (uint joint = 0; joint < joints.Length; joint++)
        {
            if (!joints[(int)joint].Id.StartsWith(Anchor, StringComparison.Ordinal)) continue;
            boneOf[joint] = bones.Count;
            bones.Add(new(joint, children[joint].Count == 1 ? children[joint][0] : null, true, false));
        }
        if (bones.Count == 0) throw new InvalidDataException($"Hanging mesh '{mesh}' names no joint '{Anchor}…' to hang from.");
        List<(int, int)> links = [];
        Queue<uint> below = new(boneOf.Keys.OrderBy(joint => joint).SelectMany(joint => children[joint]));
        while (below.TryDequeue(out uint joint))
        {
            List<uint> next = children[joint];
            if (next.Count == 0) continue;
            if (next.Count > 1) throw new InvalidDataException($"Hanging mesh '{mesh}' branches below joint '{joints[(int)joint].Id}': a body reaches one joint.");
            uint parent = joints[(int)joint].Parent;
            while (!boneOf.ContainsKey(parent)) parent = joints[(int)parent].Parent;
            boneOf[joint] = bones.Count;
            links.Add((boneOf[parent], bones.Count));
            bones.Add(new(joint, next[0], false, children[next[0]].Count == 0));
            below.Enqueue(next[0]);
        }
        if (links.Count == 0) throw new InvalidDataException($"Hanging mesh '{mesh}' names no body below its anchor.");
        return new(bones, links, light);
    }
}

/// <summary>One body of a hanging rig.</summary>
/// <param name="Joint">The joint it hangs from.</param>
/// <param name="Reach">The joint its capsule reaches, if it reaches one (an anchor with two strands below it reaches none).</param>
/// <param name="Anchor">Whether it follows the placed pose rather than swinging.</param>
/// <param name="End">Whether it ends its strand: the root's tip, a chain's ring, the cage, the lantern.</param>
internal readonly record struct DaggerfallHangingBone(uint Joint, uint? Reach, bool Anchor, bool End);
