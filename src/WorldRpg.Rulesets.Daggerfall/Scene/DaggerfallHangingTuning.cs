using System.Text.Json;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// How the hanging roots, chains, cage and lanterns swing (<see cref="Presentation.DaggerfallHanging"/>). A hanging piece
/// simulates only while an actor moves within <paramref name="ActivationRadius"/> of it or stands against it, so a room
/// full of them costs only what the actors walk among; elsewhere, and around monsters standing idle, each draws at rest.
/// </summary>
/// <param name="ActivationRadius">How near a moving actor, in metres across the floor, wakes a piece to swing.</param>
/// <param name="ReleaseSeconds">How long a piece keeps swinging once no actor has moved within the radius and a metre more, so it settles before it rests; an actor still for this long no longer counts as moving.</param>
/// <param name="BlendOutSeconds">How long a released piece takes to ease back into its rest pose.</param>
/// <param name="StepsPerSecond">How often the swinging pieces step, in steps a second of world time.</param>
/// <param name="LampFlicker">How far a lantern's light rises and falls about its level as it flickers, as a share of it (0 holds it steady).</param>
/// <param name="LampFlickerRate">How fast a lantern's light flickers, in cycles a second.</param>
internal sealed record DaggerfallHangingTuning(float ActivationRadius, float ReleaseSeconds, float BlendOutSeconds, int StepsPerSecond,
    float LampFlicker, float LampFlickerRate, DaggerfallHangingBodyTuning Root, DaggerfallHangingBodyTuning Chain,
    DaggerfallHangingBodyTuning Cage, DaggerfallHangingBodyTuning Lantern)
{
    internal static DaggerfallHangingTuning Default { get; } = new(4f, 2f, .5f, 60, .12f, 7f,
        Root: new(.035f, .25f, .035f, .25f, .45f, .2f, .08f, .5f, 2f),
        Chain: new(.045f, .6f, .07f, 1.2f, 1.1f, .4f, .02f, .15f, .4f),
        Cage: new(.03f, .4f, .45f, 18f, .8f, .3f, .05f, .15f, .6f),
        Lantern: new(.02f, .12f, .12f, 2f, .6f, .3f, .02f, .1f, .3f));

    /// <summary>The bodies and joints of a piece of <paramref name="kind"/>.</summary>
    internal DaggerfallHangingBodyTuning Of(DaggerfallHangingKind kind) => kind switch
    {
        DaggerfallHangingKind.Root => Root,
        DaggerfallHangingKind.Chain => Chain,
        DaggerfallHangingKind.Cage => Cage,
        _ => Lantern,
    };

    internal DaggerfallHangingTuning Validate()
    {
        if (!float.IsFinite(ActivationRadius) || ActivationRadius <= 0) throw new ArgumentException("A hanging piece wakes within a radius above 0.");
        if (!float.IsFinite(ReleaseSeconds) || ReleaseSeconds < 0 || !float.IsFinite(BlendOutSeconds) || BlendOutSeconds < 0)
            throw new ArgumentException("A hanging piece's release and blend-out last 0 seconds or more.");
        if (StepsPerSecond is < 10 or > 480) throw new ArgumentException("Hanging pieces step 10 to 480 times a second.");
        if (!float.IsFinite(LampFlicker) || LampFlicker is < 0 or >= 1 || !float.IsFinite(LampFlickerRate) || LampFlickerRate < 0)
            throw new ArgumentException("A lantern's light flickers by a share from 0 up to 1, at 0 or more cycles a second.");
        foreach (DaggerfallHangingBodyTuning bodies in (DaggerfallHangingBodyTuning[])[Root, Chain, Cage, Lantern]) bodies.Validate();
        return this;
    }

    internal static DaggerfallHangingTuning Read(JsonElement e) => new DaggerfallHangingTuning(
        e.GetProperty("activationRadius").GetSingle(), e.GetProperty("releaseSeconds").GetSingle(), e.GetProperty("blendOutSeconds").GetSingle(),
        e.GetProperty("stepsPerSecond").GetInt32(), e.GetProperty("lampFlicker").GetSingle(), e.GetProperty("lampFlickerRate").GetSingle(),
        DaggerfallHangingBodyTuning.Read(e.GetProperty("root")), DaggerfallHangingBodyTuning.Read(e.GetProperty("chain")),
        DaggerfallHangingBodyTuning.Read(e.GetProperty("cage")), DaggerfallHangingBodyTuning.Read(e.GetProperty("lantern"))).Validate();
}

/// <summary>
/// How one kind of hanging piece's bodies weigh and turn. Each body is a capsule from its joint to the next; the last, what
/// the piece ends in (a root's tip, a chain's ring, the cage, the lantern), takes its own size and weight.
/// </summary>
/// <param name="Radius">A body's capsule radius, in metres.</param>
/// <param name="Mass">A body's mass, in kilograms.</param>
/// <param name="EndRadius">The last body's capsule radius, in metres.</param>
/// <param name="EndMass">The last body's mass, in kilograms.</param>
/// <param name="Swing">How far a body turns from the one above it, in radians.</param>
/// <param name="Twist">How far a body twists either way about the one above it, in radians.</param>
/// <param name="JointDamping">How each joint resists turning (N·m·s per radian).</param>
/// <param name="LinearDamping">How quickly a swing dies away.</param>
/// <param name="AngularDamping">How quickly the bodies' turning slows.</param>
internal sealed record DaggerfallHangingBodyTuning(float Radius, float Mass, float EndRadius, float EndMass, float Swing, float Twist,
    float JointDamping, float LinearDamping, float AngularDamping)
{
    internal DaggerfallHangingBodyTuning Validate()
    {
        float[] positive = [Radius, Mass, EndRadius, EndMass, Swing];
        float[] none = [Twist, JointDamping, LinearDamping, AngularDamping];
        if (positive.Any(value => !float.IsFinite(value) || value <= 0) || none.Any(value => !float.IsFinite(value) || value < 0))
            throw new ArgumentException("A hanging body has a size, weight and swing above 0, and twist and damping of 0 or more.");
        return this;
    }

    internal static DaggerfallHangingBodyTuning Read(JsonElement e) => new DaggerfallHangingBodyTuning(
        e.GetProperty("radius").GetSingle(), e.GetProperty("mass").GetSingle(), e.GetProperty("endRadius").GetSingle(),
        e.GetProperty("endMass").GetSingle(), e.GetProperty("swing").GetSingle(), e.GetProperty("twist").GetSingle(),
        e.GetProperty("jointDamping").GetSingle(), e.GetProperty("linearDamping").GetSingle(), e.GetProperty("angularDamping").GetSingle()).Validate();
}
