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
/// <param name="LinkRadius">A link's capsule radius, in metres.</param>
/// <param name="LinkMass">A link's mass, in kilograms.</param>
/// <param name="WeightLength">The weight's capsule, from its joint along the joint's +Y, in metres.</param>
/// <param name="WeightRadius">The weight's capsule radius, in metres.</param>
/// <param name="WeightMass">The weight's mass, in kilograms.</param>
/// <param name="Swing">How far a link turns from the one above it, in radians.</param>
/// <param name="Twist">How far a link twists either way about the one above it, in radians.</param>
/// <param name="LinearDamping">Slows the chain's movement, so a swing dies away.</param>
/// <param name="AngularDamping">Slows the links' turning.</param>
public sealed record HeldChainDefinition(string Handle, IReadOnlyList<string> Links, string Weight, float LinkRadius, float LinkMass,
    float WeightLength, float WeightRadius, float WeightMass, float Swing, float Twist, float LinearDamping, float AngularDamping);

/// <summary>
/// The chain of one drawn held model, swung by how the hand moves it: a ragdoll whose handle follows the model's pose and
/// whose links and weight hang from it, in a Dynamics world of its own. The model is drawn in view space, so that world
/// is too: the chain falls toward the bottom of the view, touches nothing, and turning the view does not swing it. It
/// runs on the world seconds it is given, so a held world holds it, and is presentation, never saved.
/// </summary>
public sealed class HeldChain : IDisposable
{
    private const float StepsPerSecond = 120;
    private static readonly Vector3 Gravity = new(0, -9.81F, 0);

    private readonly IEngineContext engine;
    private readonly HeldChainDefinition definition;
    private readonly ReadOnlyMemory<AnimationJointInfo> rig;
    private readonly AnimationInstance instance;
    private readonly DynamicsWorld world;
    private DynamicsRagdoll? ragdoll;
    private bool posed;

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
        world = engine.Dynamics.CreateWorld(new DynamicsWorldConfig(Gravity));
    }

    /// <summary>Once the model has been published: the Engine has a pose to spawn the chain at from the next call.</summary>
    public void Published() => posed = true;

    /// <summary>World seconds admitted: hangs the chain once it can, and swings it by what the hand did.</summary>
    public void Step(float seconds)
    {
        if (!posed) return;
        ragdoll ??= Hang();
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
        List<DynamicsRagdollBone> bones = [DynamicsRagdollBone.Capsule(Joint(definition.Handle), Joint(definition.Links[0]), definition.LinkRadius, definition.LinkMass).FollowingPose()];
        for (int link = 0; link < definition.Links.Count; link++)
        {
            string next = link + 1 < definition.Links.Count ? definition.Links[link + 1] : definition.Weight;
            bones.Add(DynamicsRagdollBone.Capsule(Joint(definition.Links[link]), Joint(next), definition.LinkRadius, definition.LinkMass));
        }
        bones.Add(DynamicsRagdollBone.CapsuleAlong(Joint(definition.Weight), definition.WeightLength, definition.WeightRadius, definition.WeightMass));
        DynamicsJointLimits limits = DynamicsJointLimits.Cone(definition.Swing, -definition.Twist, definition.Twist, 0);
        DynamicsRagdollLink[] links = [.. Enumerable.Range(1, bones.Count - 1).Select(bone => new DynamicsRagdollLink((uint)bone - 1, (uint)bone, Vector3.UnitY, limits))];
        return engine.Dynamics.CreateRagdoll(new(world, instance, bones.ToArray(), links, 1, 0, 0, 0, definition.LinearDamping, definition.AngularDamping, 1));
    }
}
