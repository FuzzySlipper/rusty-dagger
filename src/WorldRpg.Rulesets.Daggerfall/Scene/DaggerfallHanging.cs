using System.Diagnostics;
using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>
/// A hanging mesh the active site draws in place of a placed flat: its decoration's id, its record (<c>ARCHIVE-RECORD</c>),
/// what it is, the appearance and object it is drawn as, its model, the bodies its rig names and how high it hangs from in
/// its model's metres, where it stands (its foot, turned and scaled), and the flat's own position.
/// </summary>
internal sealed record DaggerfallHangingPiece(string Id, string Name, DaggerfallHangingKind Kind, Appearance Look, ulong ObjectId,
    RenderResource Model, DaggerfallHangingRig Rig, float Height, Transform Pose, Vector3 Position);

/// <summary>An actor that pushes hanging pieces aside: its feet and the upright capsule it walks in.</summary>
internal readonly record struct DaggerfallHangingActor(long Id, Vector3 Feet, float Radius, float Height);

/// <summary>
/// Makes the active site's hanging roots, chains, cage and lanterns swing aside when the player or an enemy walks through
/// them. Each hangs as a ragdoll from a body that follows its pose at its sprite's top, as
/// <see cref="WorldRpg.Kit.Presentation.HeldChain"/> hangs the flail's chain, in one Dynamics world bound to the site's
/// collision, and each actor near one walks in a kinematic capsule that pushes its bodies. A piece simulates only while an
/// actor is within the tuning's radius; it then settles, eases back to rest and draws at rest until one comes again. A
/// lantern carries the site's light beside it and flickers it. It is presentation: nothing of it is saved, and while
/// classic art is drawn the sprites stand in and nothing simulates.
/// </summary>
internal sealed class DaggerfallHanging : IDisposable
{
    private const uint HangingGroup = 1u << 1;
    private const uint ActorGroup = 1u << 2;
    // An anchor with no single body below it (two strands hang from it) is a stub this long.
    private const float AnchorStub = .02f;
    // Margin above an actor's reach before a released piece counts as left alone, in metres.
    private const float ReleaseMargin = 1f;
    // An actor standing this close to a piece may be leaning on it, so it keeps it awake moving or not; farther off, only
    // an actor that has moved within the release time does, so monsters idling in a root-hung room leave the roots at rest.
    private const float Touch = 1f;
    // How far an actor must move in an update to count as moving, in metres.
    private const float Stirs = .01f;
    // How far the clearance fit may lower a piece from what it hangs from, and how far it may shorten it.
    private static readonly float[] Lowerings = [0f, .03f, .06f, .1f, .15f];
    // A sprite placed on the edge of a beam or a ceiling's recess hangs its strand along that face: a few centimetres
    // aside clear it, unseen, before it is shortened.
    private static readonly Vector3[] Nudges = [Vector3.Zero, new(.06f, 0, 0), new(-.06f, 0, 0), new(0, 0, .06f), new(0, 0, -.06f),
        new(.12f, 0, 0), new(-.12f, 0, 0), new(0, 0, .12f), new(0, 0, -.12f)];
    private static readonly float[] Shortenings = [1f, .9f, .8f, .7f, .6f];

    private readonly IDynamicsService dynamics;
    private readonly IAnimationService animation;
    private readonly SpatialMovementSystem spatial;
    private readonly DaggerfallSiteLighting lighting;
    private readonly DaggerfallHangingTuning tuning;
    private readonly Action<string> report;
    private readonly List<Piece> pieces = [];
    private readonly Dictionary<long, DynamicsBody> actorBodies = [];
    private DynamicsWorld? world;
    // What the latest updates' steps and lights cost, in milliseconds, for Describe.
    private readonly Queue<(double Step, double Lamps)> costs = new();
    private double seconds;
    // Where the first actor, the player, stood at the latest step, for Describe to say which pieces hang nearest.
    private Vector3? player;
    // Where each actor stood at the latest step and when it last moved, and the actors that have moved lately.
    private readonly Dictionary<long, (Vector3 Feet, double Moved)> stood = [];
    private readonly List<DaggerfallHangingActor> stirring = [];
    private bool paused;

    // Lanterns farther than this from every actor keep a steady light: nobody near sees it flicker.
    private const float FlickerReach = 20f;
    // The importer names a light flat's own light after the flat: billboard/… lights as light-flat/….
    private const string Billboard = "billboard/";
    private const string LightFlat = "light-flat/";
    // How far a lantern's light must move before its light follows, in metres.
    private const float LampStill = .02f;
    // How often a lantern at rest changes its light's brightness, in seconds: a flame's flicker reads the same at this rate.
    private const double RestingFlickerStep = 1 / 20d;
    // How far above or below a piece an actor may stand and still be level with it: a jump's height, short of the floor
    // above or below, whose pieces an actor beneath or above them cannot reach.
    private const float LevelReach = 1f;

    private sealed class Piece(DaggerfallHangingPiece placed)
    {
        internal DaggerfallHangingPiece Placed { get; } = placed;
        internal DaggerfallHangingRig Rig => Placed.Rig;
        /// <summary>From its foot to the point it hangs from, in its model's metres.</summary>
        internal float Height => Placed.Height;
        /// <summary>Where it hangs now: its placed pose, lowered or shortened to clear what it hangs from and what is below.</summary>
        internal Transform Pose { get; set; } = placed.Pose;
        internal bool Fitted { get; set; }
        internal bool Unfit { get; set; }
        internal AnimationInstance? Instance { get; set; }
        internal DynamicsRagdoll? Ragdoll { get; set; }
        internal double LastNear { get; set; }
        internal float Blend { get; set; } = 1;
        /// <summary>The site's light this lantern carries, placed beside it.</summary>
        internal DaggerfallSiteLight? Lamp { get; set; }
        internal Vector3 LampRest { get; set; }
        /// <summary>Where its light was last placed, while it is carried, and when.</summary>
        internal Vector3? LampAt { get; set; }
        internal double LampSet { get; set; } = double.NegativeInfinity;
        internal float Phase { get; set; }
    }

    /// <param name="lamps">The site's authored lights, among which each lantern finds its own, named after it.</param>
    /// <param name="report">Says, once each, which pieces cannot hang clear of what is around them and so stay at rest.</param>
    internal DaggerfallHanging(IDynamicsService dynamics, IAnimationService animation, SpatialMovementSystem spatial, DaggerfallSiteAppearance site,
        DaggerfallSiteLighting lighting, IReadOnlyList<DaggerfallSiteLight> lamps, DaggerfallHangingTuning tuning, Action<string> report)
    {
        this.dynamics = dynamics;
        this.animation = animation;
        this.spatial = spatial;
        this.lighting = lighting;
        this.tuning = tuning.Validate();
        this.report = report;
        Site = site;
        site.DecorationsRetiring = LetGo;
        Dictionary<string, DaggerfallSiteLight> lampsById = lamps.ToDictionary(lamp => lamp.Id, StringComparer.Ordinal);
        foreach (DaggerfallHangingPiece placed in site.HangingPieces())
        {
            Piece piece = new(placed) { Phase = StableHash.Fnv1a64(placed.Id) % 1000 / 1000f };
            if (placed.Kind == DaggerfallHangingKind.Lantern && placed.Rig.Light is uint light
                && placed.Id.StartsWith(Billboard, StringComparison.Ordinal)
                && lampsById.TryGetValue(string.Concat(LightFlat, placed.Id.AsSpan(Billboard.Length)), out DaggerfallSiteLight? lamp))
            {
                piece.Lamp = lamp;
                piece.LampRest = RestJoints(piece).Span[(int)light].World.Translation;
            }
            pieces.Add(piece);
        }
    }

    /// <summary>The site appearance whose pieces hang here.</summary>
    internal DaggerfallSiteAppearance Site { get; }

    /// <summary>Whether the site has any hanging piece at all.</summary>
    internal bool Hangs => pieces.Count > 0;

    /// <summary>
    /// How many pieces hang and swing here, their bodies, the actors pushing them, and what stepping them and carrying the
    /// lanterns' lights cost over the last updates, for a developer judging what a room of them costs.
    /// </summary>
    internal string Describe()
    {
        Piece[] swinging = [.. pieces.Where(piece => piece.Ragdoll is not null)];
        string cost = costs.Count == 0 ? "no steps yet" : $"over {costs.Count} updates a step took {costs.Average(cost => cost.Step):0.000} ms on average "
            + $"(longest {costs.Max(cost => cost.Step):0.000}) and the lights {costs.Average(cost => cost.Lamps):0.000} ms";
        return $"{pieces.Count} hang ({string.Join(", ", pieces.GroupBy(piece => piece.Placed.Kind).Select(group => $"{group.Count()} {group.Key}"))}), "
            + $"{pieces.Count(piece => piece.Unfit)} unfit; {swinging.Length} swing with {swinging.Sum(piece => piece.Rig.Bones.Count)} bodies, "
            + $"pushed by {actorBodies.Count} actors; {cost}." + Nearest();
    }

    private string Nearest()
    {
        if (player is not Vector3 at) return string.Empty;
        return " Nearest the player: " + string.Join("; ", pieces.OrderBy(piece => Vector3.Distance(piece.Placed.Position, at)).Take(3)
            .Select(piece => $"{piece.Placed.Name} at <{piece.Placed.Position.X:0.0}, {piece.Placed.Position.Y:0.0}, {piece.Placed.Position.Z:0.0}> "
                + $"{Vector3.Distance(piece.Placed.Position, at):0.0} m{(piece.Ragdoll is null ? piece.Unfit ? ", unfit" : "" : ", swinging")}")) + ".";
    }

    /// <summary>
    /// World seconds admitted: wakes the pieces an actor has come near and releases those left alone, moves each actor's
    /// capsule to where it walks, steps the world, and carries and flickers the lanterns' lights.
    /// </summary>
    /// <param name="actors">The actors that push the pieces aside, the player first.</param>
    /// <param name="refreshed">Whether refreshed art is drawn; while classic art is, the sprites stand in and nothing hangs.</param>
    internal void Step(float elapsed, ReadOnlySpan<DaggerfallHangingActor> actors, bool refreshed)
    {
        if (!refreshed)
        {
            if (!paused) Pause();
            return;
        }
        paused = false;
        seconds += elapsed;
        player = actors.Length > 0 ? actors[0].Feet : null;
        ReadOnlySpan<DaggerfallHangingActor> moving = Stirring(actors);
        foreach (Piece piece in pieces)
        {
            bool near = Near(piece, moving, tuning.ActivationRadius) || Near(piece, actors, Touch);
            if (near || piece.Ragdoll is not null && Near(piece, moving, tuning.ActivationRadius + ReleaseMargin)) piece.LastNear = seconds;
            if (near && piece.Ragdoll is null && !piece.Unfit) Wake(piece);
            else if (piece.Ragdoll is { } ragdoll && seconds - piece.LastNear > tuning.ReleaseSeconds) Release(piece, ragdoll, elapsed);
            else if (piece.Ragdoll is { } easing && piece.Blend < 1)
            {
                // An actor came back while it eased toward rest: it swings fully again.
                piece.Blend = 1;
                dynamics.SetRagdollBlend(new(easing, 1));
            }
            else if (!near && piece.Ragdoll is null && piece.Instance is { } posed)
            {
                // The actor left before the Engine posed it to hang: it rests again.
                posed.Dispose();
                piece.Instance = null;
            }
        }
        if (world is not null) MoveActors(actors);
        long start = Stopwatch.GetTimestamp();
        if (world is not null && elapsed > 0 && pieces.Any(piece => piece.Ragdoll is not null))
        {
            uint steps = (uint)Math.Max(1, MathF.Ceiling(elapsed * tuning.StepsPerSecond));
            // The world keeps the collision it was last bound to: bound again, it meets a door opened or a platform moved
            // since, and the Engine skips the bind while nothing has changed.
            dynamics.BindWorldCollision(new(world, spatial.Session));
            dynamics.Step(new(world, elapsed / steps, steps, ReadOnlyMemory<DynamicsAction>.Empty));
        }
        double stepped = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        start = Stopwatch.GetTimestamp();
        CarryLamps(actors);
        costs.Enqueue((stepped, Stopwatch.GetElapsedTime(start).TotalMilliseconds));
        while (costs.Count > 600) costs.Dequeue();
    }

    public void Dispose()
    {
        if (Site.DecorationsRetiring == LetGo) Site.DecorationsRetiring = null;
        Pause();
        world?.Dispose();
        world = null;
    }

    /// <summary>
    /// Whether the site retired the decorations its pieces were made from, as a crossing that suspends the site does: it
    /// has let go of them and is to be made again from the decorations the site admits next.
    /// </summary>
    internal bool Stale { get; private set; }

    private void LetGo()
    {
        Pause();
        Stale = true;
    }

    /// <summary>
    /// The actors that have moved within the release time. One first seen stands still until it moves, so a site's monsters
    /// do not wake its pieces as it loads.
    /// </summary>
    private ReadOnlySpan<DaggerfallHangingActor> Stirring(ReadOnlySpan<DaggerfallHangingActor> actors)
    {
        stirring.Clear();
        foreach (DaggerfallHangingActor actor in actors)
        {
            double moved = !stood.TryGetValue(actor.Id, out var was) ? double.NegativeInfinity
                : Vector3.Distance(was.Feet, actor.Feet) > Stirs ? seconds : was.Moved;
            stood[actor.Id] = (actor.Feet, moved);
            if (seconds - moved <= tuning.ReleaseSeconds) stirring.Add(actor);
        }
        if (stood.Count > actors.Length)
        {
            HashSet<long> here = [];
            foreach (DaggerfallHangingActor actor in actors) here.Add(actor.Id);
            foreach (long gone in stood.Keys.Where(id => !here.Contains(id)).ToArray()) stood.Remove(gone);
        }
        return System.Runtime.InteropServices.CollectionsMarshal.AsSpan(stirring);
    }

    /// <summary>Whether an actor stands within <paramref name="radius"/> of a piece across the floor, and level with some of it.</summary>
    private static bool Near(Piece piece, ReadOnlySpan<DaggerfallHangingActor> actors, float radius)
    {
        Vector3 foot = piece.Pose.Translation;
        float top = foot.Y + piece.Height * piece.Pose.Scale.Y;
        foreach (DaggerfallHangingActor actor in actors)
            if (Vector2.Distance(new(actor.Feet.X, actor.Feet.Z), new(foot.X, foot.Z)) <= radius
                && actor.Feet.Y <= top + LevelReach && actor.Feet.Y + actor.Height >= foot.Y - LevelReach)
                return true;
        return false;
    }

    /// <summary>
    /// Hangs a piece's ragdoll: on its first wake its bodies are fitted clear of what is around them, then from the next call,
    /// once the Engine has posed it, it simulates from that pose.
    /// </summary>
    private void Wake(Piece piece)
    {
        if (!piece.Fitted)
        {
            Fit(piece);
            if (piece.Unfit) return;
        }
        if (piece.Instance is null)
        {
            piece.Instance = animation.CreateInstance(new(piece.Placed.Look, piece.Placed.ObjectId));
            return;
        }
        world ??= CreateWorld();
        piece.Ragdoll = Hang(piece);
        piece.Blend = 1;
        // Swinging, it casts no shadow: a moving caster draws every shadowing light near it again each frame.
        Site.SwingHanging(piece.Placed.Id, true);
    }

    private DynamicsWorld CreateWorld()
    {
        DynamicsWorld created = dynamics.CreateWorld(new DynamicsWorldConfig(new Vector3(0, -9.81f, 0)));
        dynamics.BindWorldCollision(new(created, spatial.Session));
        return created;
    }

    /// <summary>Eases a piece left alone back into its rest pose, then lets it rest.</summary>
    private void Release(Piece piece, DynamicsRagdoll ragdoll, float elapsed)
    {
        piece.Blend = tuning.BlendOutSeconds > 0 ? Math.Max(0, piece.Blend - elapsed / tuning.BlendOutSeconds) : 0;
        if (piece.Blend > 0)
        {
            dynamics.SetRagdollBlend(new(ragdoll, piece.Blend));
            return;
        }
        ragdoll.Dispose();
        piece.Ragdoll = null;
        piece.Instance?.Dispose();
        piece.Instance = null;
        Site.SwingHanging(piece.Placed.Id, false);
        // Its light goes home now, whoever is near, so it casts what it cast before it swung.
        if (piece.Lamp is { } lamp) lighting.PlaceLamp(lamp, lamp.Position.ToVector(), 1);
        piece.LampAt = null;
    }

    /// <summary>Stops every piece where it is, back at rest, drops the actors' capsules and gives the lanterns their lights back.</summary>
    private void Pause()
    {
        paused = true;
        foreach (Piece piece in pieces)
        {
            piece.Ragdoll?.Dispose();
            piece.Ragdoll = null;
            piece.Instance?.Dispose();
            piece.Instance = null;
            Site.SwingHanging(piece.Placed.Id, false);
            if (piece.Lamp is { } lamp) lighting.PlaceLamp(lamp, lamp.Position.ToVector(), 1);
            piece.LampAt = null;
        }
        foreach (DynamicsBody body in actorBodies.Values) body.Dispose();
        actorBodies.Clear();
    }

    /// <summary>
    /// Each actor near a swinging piece walks in a kinematic capsule there, which pushes the pieces' bodies and is pushed
    /// by nothing; an actor no longer near one drops its capsule.
    /// </summary>
    private void MoveActors(ReadOnlySpan<DaggerfallHangingActor> actors)
    {
        HashSet<long> moved = [];
        foreach (DaggerfallHangingActor actor in actors)
        {
            if (!pieces.Any(piece => piece.Ragdoll is not null && Near(piece, [actor], tuning.ActivationRadius + ReleaseMargin))) continue;
            float half = Math.Max(0, actor.Height / 2 - actor.Radius);
            Transform centre = new(actor.Feet + Vector3.UnitY * (actor.Height / 2), Quaternion.Identity, Vector3.One);
            if (!actorBodies.TryGetValue(actor.Id, out DynamicsBody? body))
                actorBodies.Add(actor.Id, body = dynamics.CreateCapsuleBody(new(world!, new DynamicsCapsuleBodyConfig(centre, half, actor.Radius,
                    new DynamicsBodyProperties(80, default, Vector3.Zero, Vector3.Zero, default, 0, 0, 0, .6f, 0, ActorGroup, HangingGroup, true, false, false)))));
            dynamics.MoveBodyKinematic(new(body, centre));
            moved.Add(actor.Id);
        }
        foreach (long gone in actorBodies.Keys.Where(id => !moved.Contains(id)).ToArray())
        {
            actorBodies[gone].Dispose();
            actorBodies.Remove(gone);
        }
    }

    /// <summary>A lantern's light sits in its glass, wherever that swings, and flickers about its level while an actor is near.</summary>
    private void CarryLamps(ReadOnlySpan<DaggerfallHangingActor> actors)
    {
        foreach (Piece piece in pieces)
        {
            if (piece.Lamp is not { } lamp || !Near(piece, actors, FlickerReach)) continue;
            // At rest only its brightness changes, and a site of many lights makes each change dear: it flickers less often.
            if (piece.Ragdoll is null && seconds - piece.LampSet < RestingFlickerStep) continue;
            piece.LampSet = seconds;
            // The light keeps where it was authored beside the lantern and moves as the lantern's light joint does, so at
            // rest it is where it was authored.
            Vector3 at = lamp.Position.ToVector();
            if (piece.Instance is { } instance && piece.Ragdoll is not null && piece.Rig.Light is uint light)
            {
                AnimationJointPoseResult pose = animation.ReadJointPose(instance);
                if (pose.Reported) at += pose.Joints.Span[(int)light].World.Translation - piece.LampRest;
                // A settled lantern still stirs by millimetres: its light stays put until it moves, so a light casting
                // shadows is not drawn again every frame for nothing.
                if (piece.LampAt is { } placed && Vector3.Distance(placed, at) < LampStill) at = placed;
                piece.LampAt = at;
            }
            else piece.LampAt = null;
            double t = (seconds * tuning.LampFlickerRate + piece.Phase) * 2 * Math.PI;
            float flicker = 1 + tuning.LampFlicker * (float)((Math.Sin(t) + .6 * Math.Sin(2.3 * t + 1.7) + .3 * Math.Sin(5.1 * t + .4)) / 1.9);
            lighting.PlaceLamp(lamp, at, flicker);
        }
    }

    /// <summary>
    /// Fits a piece clear of what it hangs from and what is below it, before it first swings: each body's capsule at rest
    /// must meet nothing of the site. It hangs from the underside of what is above it, a beam the sprite's top reached
    /// into; it is lowered a little more from there or moved a few centimetres aside, and failing that shortened from it;
    /// a piece that cannot be fitted stays at rest, said once.
    /// </summary>
    private void Fit(Piece piece)
    {
        piece.Fitted = true;
        float tall = piece.Height * piece.Placed.Pose.Scale.Y;
        float under = Underside(piece, tall);
        foreach (float shorten in Shortenings)
            foreach (Vector3 nudge in Nudges)
                foreach (float lower in Lowerings)
                {
                    // Lowered from the hang point and shortened toward it, so its foot rises while its top stays put.
                    Transform pose = piece.Placed.Pose with
                    {
                        Translation = piece.Placed.Pose.Translation + nudge + Vector3.UnitY * (tall * (1 - shorten) - under - lower),
                        Scale = piece.Placed.Pose.Scale * shorten,
                    };
                    if (!Clear(piece, pose, shorten)) continue;
                    if (pose != piece.Placed.Pose) Site.PlaceHanging(piece.Placed.Id, pose);
                    piece.Pose = pose;
                    if (piece.Rig.Light is uint light) piece.LampRest = RestJoints(piece).Span[(int)light].World.Translation;
                    return;
                }
        piece.Unfit = true;
        report($"Hanging {piece.Placed.Name} at {piece.Placed.Position} cannot hang clear of what is around it, lowered, moved aside or shortened; it stays at rest.");
    }

    /// <summary>
    /// How far below its placed hang point the underside of what it hangs from lies: a ray up its hanging line, from
    /// halfway down it to the hang point, meets a beam or a low ceiling the sprite's top reached into. Nothing met, it
    /// hangs where it was placed.
    /// </summary>
    private float Underside(Piece piece, float tall)
    {
        Vector3 top = piece.Placed.Pose.Translation + Vector3.UnitY * tall;
        SpatialHit above = spatial.CastRay(top - Vector3.UnitY * (tall / 2), Vector3.UnitY, tall / 2);
        return above.Present ? top.Y - above.Point.Y : 0;
    }

    private bool Clear(Piece piece, Transform pose, float shorten)
    {
        ReadOnlySpan<JointPose> joints = RestJoints(piece, pose).Span;
        DaggerfallHangingBodyTuning bodies = tuning.Of(piece.Placed.Kind);
        foreach (DaggerfallHangingBone bone in piece.Rig.Bones)
        {
            if (bone.Anchor || bone.Reach is not uint reach) continue;
            Vector3 from = joints[(int)bone.Joint].World.Translation, to = joints[(int)reach].World.Translation;
            float radius = (bone.End ? bodies.EndRadius : bodies.Radius) * shorten;
            // At rest a body hangs straight down, so an upright capsule over its span is its shape.
            float half = Math.Abs(from.Y - to.Y) / 2;
            if (spatial.OverlapCapsule((from + to) / 2, half, radius, ReadOnlyMemory<SpatialEntityCollider>.Empty).Present) return false;
        }
        return true;
    }

    private ReadOnlyMemory<JointPose> RestJoints(Piece piece, Transform? pose = null) =>
        animation.SampleModelJointPose(new(piece.Placed.Model, "", 0, "", 0, 0, false, pose ?? piece.Pose));

    /// <summary>
    /// The ragdoll of a piece: its anchors follow its pose, and each body below is a capsule to the next joint, linked by a
    /// cone to the body it hangs from.
    /// </summary>
    private DynamicsRagdoll Hang(Piece piece)
    {
        DaggerfallHangingBodyTuning bodies = tuning.Of(piece.Placed.Kind);
        float scale = piece.Pose.Scale.Y;
        DynamicsRagdollBone[] bones = [.. piece.Rig.Bones.Select(bone => bone switch
        {
            { Anchor: true, Reach: uint reach } => DynamicsRagdollBone.Capsule(bone.Joint, reach, bodies.Radius * scale, bodies.Mass).FollowingPose(),
            { Anchor: true } => DynamicsRagdollBone.CapsuleAlong(bone.Joint, AnchorStub, bodies.Radius * scale, bodies.Mass).FollowingPose(),
            { End: true } => DynamicsRagdollBone.Capsule(bone.Joint, bone.Reach!.Value, bodies.EndRadius * scale, bodies.EndMass),
            _ => DynamicsRagdollBone.Capsule(bone.Joint, bone.Reach!.Value, bodies.Radius * scale, bodies.Mass),
        })];
        DynamicsJointLimits cone = DynamicsJointLimits.Cone(bodies.Swing, -bodies.Twist, bodies.Twist, bodies.JointDamping);
        DynamicsRagdollLink[] links = [.. piece.Rig.Links.Select(link => new DynamicsRagdollLink((uint)link.Parent, (uint)link.Child, Vector3.UnitY, cone))];
        // A hanging piece meets the actors and the site, never another piece: rooms of them stay cheap.
        return dynamics.CreateRagdoll(new(world!, piece.Instance!, bones, links, HangingGroup, ActorGroup, .6f, 0,
            bodies.LinearDamping, bodies.AngularDamping, 1));
    }
}
