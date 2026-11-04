using System.Numerics;
using Rusty.Engine;

namespace WorldRpg.Kit.Presentation;

/// <summary>
/// A chain on a held model: the joint it hangs from and the links and weight below it, all named in the model's rig, as
/// the Engine's ragdoll bones take them.
/// </summary>
/// <param name="Handle">The joint that follows the model's pose, which the chain hangs from.</param>
/// <param name="Links">The link joints in order from the handle, each ending at the next.</param>
/// <param name="Weight">The joint of what the chain swings, past the last link.</param>
/// <param name="Gravity">Which way the chain falls in view space (m/s²): tilted, it hangs out of the way of the view.</param>
/// <param name="HandleRadius">The radius of the handle's capsule, which the chain cannot pass through, in metres.</param>
/// <param name="LinkRadius">A link's capsule radius, in metres.</param>
/// <param name="LinkMass">A link's mass, in kilograms.</param>
/// <param name="WeightRadius">The radius of the sphere on the weight's joint, in metres.</param>
/// <param name="WeightMass">The weight's mass, in kilograms.</param>
/// <param name="Swing">How far a link turns from the one above it, in radians.</param>
/// <param name="Twist">How far a link twists either way about the one above it, in radians.</param>
/// <param name="WeightSwing">How far the weight turns on the last link, in radians.</param>
/// <param name="LinearDamping">Slows the chain's movement, so a swing dies away.</param>
/// <param name="AngularDamping">Slows the links' turning.</param>
/// <param name="HeldDamping">Resists each link's turning while the chain is held (N·m·s per radian), so walking and the
/// idle do not swing it; a strike lets it go.</param>
/// <param name="HeldTorque">The most that resistance can give (N·m).</param>
public sealed record HeldChainDefinition(string Handle, IReadOnlyList<string> Links, string Weight, Vector3 Gravity, float HandleRadius, float LinkRadius,
    float LinkMass, float WeightRadius, float WeightMass, float Swing, float Twist, float WeightSwing, float LinearDamping, float AngularDamping,
    float HeldDamping, float HeldTorque);

/// <summary>
/// The chain of one drawn held model, swung by how the hand moves it: a ragdoll whose handle follows the model's pose and
/// whose links and weight hang from it, in a Dynamics world of its own. The model is drawn in view space, so that world
/// is too: the chain falls the way its definition says within the view, touches only itself and the handle, and turning
/// the view does not swing it. Held, its links turn as if through treacle; let go for a strike, they swing freely. It never sleeps,
/// since it hangs from a bone that follows the pose. It runs on the world seconds it is given, so a held world holds
/// it, and is presentation, never saved.
/// </summary>
public sealed class HeldChain : IDisposable
{
    private const float StepsPerSecond = 120;
    // A sphere is a capsule with next to no length.
    private const float SphereLength = .002F;

    private readonly IEngineContext engine;
    private readonly HeldChainDefinition definition;
    private readonly ReadOnlyMemory<AnimationJointInfo> rig;
    private readonly AnimationInstance instance;
    private readonly DynamicsWorld world;
    private DynamicsRagdoll? ragdoll;
    private bool posed;
    private bool swinging;
    private bool? driven;

    /// <summary>Hangs the chain of <paramref name="definition"/> on the appearance of <paramref name="model"/> published as <paramref name="objectId"/>.</summary>
    /// <exception cref="InvalidDataException">The model's rig lacks a joint the definition names: the chain could not hang from it.</exception>
    public HeldChain(IEngineContext engine, HeldChainDefinition definition, RenderResource model, Appearance look, ulong objectId)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(definition);
        this.engine = engine;
        this.definition = definition;
        rig = engine.Animation.ReadJoints(model);
        string[] rigged = [.. rig.ToArray().Select(joint => joint.Id)];
        string[] missing = [.. new[] { definition.Handle, definition.Weight }.Concat(definition.Links).Where(joint => !rigged.Contains(joint, StringComparer.Ordinal))];
        if (missing.Length > 0) throw new InvalidDataException($"A held chain names joints its model lacks: {string.Join(", ", missing)}.");
        instance = engine.Animation.CreateInstance(new(look, objectId));
        world = engine.Dynamics.CreateWorld(new DynamicsWorldConfig(definition.Gravity));
    }

    /// <summary>Once the model has been published: the Engine has a pose to spawn the chain at from the next call.</summary>
    public void Published() => posed = true;

    /// <summary>Lets the chain swing freely, as a strike does, or holds it sluggish.</summary>
    public void Swing(bool free) => swinging = free;

    /// <summary>World seconds admitted: hangs the chain once it can, and swings it by what the hand did.</summary>
    public void Step(float seconds)
    {
        if (!posed) return;
        ragdoll ??= Hang();
        if (driven != swinging)
        {
            DynamicsRagdollLinkDrive held = swinging ? new(0, 0, 0) : new(0, definition.HeldDamping, definition.HeldTorque);
            engine.Dynamics.SetRagdollDrive(new(ragdoll, Enumerable.Repeat(held, definition.Links.Count + 1).ToArray(), DynamicsRagdollRoot.Kinematic));
            driven = swinging;
        }
        if (seconds <= 0) return;
        uint steps = (uint)Math.Max(1, MathF.Ceiling(seconds * StepsPerSecond));
        engine.Dynamics.Step(new(world, seconds / steps, steps, ReadOnlyMemory<DynamicsAction>.Empty));
    }

    public void Dispose()
    {
        try { ragdoll?.Dispose(); }
        finally
        {
            world.Dispose();
            instance.Dispose();
        }
    }

    private DynamicsRagdoll Hang()
    {
        uint Joint(string name) => AnimationJoints.IndexOf(rig.Span, name);
        List<DynamicsRagdollBone> bones = [DynamicsRagdollBone.Capsule(Joint(definition.Handle), Joint(definition.Links[0]), definition.HandleRadius, definition.LinkMass).FollowingPose()];
        for (int link = 0; link < definition.Links.Count; link++)
        {
            string next = link + 1 < definition.Links.Count ? definition.Links[link + 1] : definition.Weight;
            bones.Add(DynamicsRagdollBone.Capsule(Joint(definition.Links[link]), Joint(next), definition.LinkRadius, definition.LinkMass));
        }
        bones.Add(DynamicsRagdollBone.CapsuleAlong(Joint(definition.Weight), SphereLength, definition.WeightRadius, definition.WeightMass));
        DynamicsJointLimits limits = DynamicsJointLimits.Cone(definition.Swing, -definition.Twist, definition.Twist, 0);
        DynamicsJointLimits weight = DynamicsJointLimits.Cone(definition.WeightSwing, -definition.Twist, definition.Twist, 0);
        DynamicsRagdollLink[] links = [.. Enumerable.Range(1, bones.Count - 1).Select(bone =>
            new DynamicsRagdollLink((uint)bone - 1, (uint)bone, Vector3.UnitY, bone == bones.Count - 1 ? weight : limits))];
        return engine.Dynamics.CreateRagdoll(new(world, instance, bones.ToArray(), links, 1, uint.MaxValue, 0, 0, definition.LinearDamping, definition.AngularDamping, 1));
    }
}
