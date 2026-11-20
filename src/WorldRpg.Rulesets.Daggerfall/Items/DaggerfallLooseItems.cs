using System.Diagnostics;
using System.Numerics;
using Rusty.Engine;

namespace WorldRpg.Rulesets.Daggerfall.Modules.Loot;

/// <summary>A loose item this update: its pile, where its foot stands, and its billboard's width and height, or null before it is first drawn.</summary>
internal readonly record struct DaggerfallLooseItem(long Id, Vector3 Foot, Vector2? Size);

/// <summary>The player's hand as loose items meet it: where the eye is, which way it looks, and the player's Strength.</summary>
internal readonly record struct DaggerfallLooseItemHand(Vector3 Eye, Vector3 Forward, int Strength);

/// <summary>One loose item's body as a developer reads it.</summary>
internal readonly record struct DaggerfallLooseItemReadout(long Id, Vector3 Foot, Quaternion Turn, Vector3 Velocity, bool Sleeping, float Kilograms, Vector3 Size, bool Carried);

/// <summary>
/// Loose items (dropped, or placed by a quest) as Engine Dynamics bodies near the player. Each is an upright box as wide and
/// deep as its billboard is wide and as tall as it is tall, its rotation locked so the billboard always stands over it,
/// weighing what its pile weighs, with the tuning's high friction and damping, so a pile stays put rather than creeping. A
/// dropped item falls and settles; one the player comes near is made asleep where it rests. Bodies sleep by the Engine's
/// own thresholds, the same for every body, until it lets an item's be set (rusty-engine #10945). The player may carry one
/// in front of the eye, driven toward where it is held by impulses on its own body, so it still collides and pushes the
/// others, and drop or throw it. Nothing here is saved: the piles keep where each came to rest, and a body is made again,
/// asleep, from that.
/// </summary>
internal sealed class DaggerfallLooseItems(IDynamicsService dynamics, DaggerfallLooseItemTuning tuning) : IDisposable
{
    private const uint ItemGroup = 1u << 3;
    // A body beyond the radius by this much more, and asleep, is let go, so one at the edge does not come and go.
    private const float RetireMargin = 2f;
    private const float MinimumHalfExtent = .03f;
    // How far a body must move before its pile moves with it, in metres.
    private const float Stirs = .001f;
    // A body this far below where it last rested has fallen through the floor: it goes back there, asleep.
    private const float LostFall = 30f;
    // A pile this far from its body was moved by its owner (a quest placing it again): its body is made again there.
    private const float Placed = .05f;
    private static readonly AxisLocks Upright = new(false, false, false, true, true, true);

    private readonly DaggerfallLooseItemTuning tuning = tuning.Validate();
    private readonly Dictionary<long, Body> bodies = [];
    // Items let go of while they moved, and those just dropped with the toss they leave the hand with: each body is made awake.
    private readonly HashSet<long> woken = [];
    private readonly Dictionary<long, Vector3> dropped = [];
    private readonly List<DynamicsAction> actions = [];
    private readonly Queue<double> costs = new();
    private DynamicsWorld? world;
    private Vector3 held;
    private Vector3? player;

    private sealed class Body(DynamicsBody handle, Vector3 half, float mass, Vector2 size, Vector3 centre, bool sleeping)
    {
        internal DynamicsBody Handle { get; } = handle;
        internal Vector3 Half { get; } = half;
        internal float Mass { get; } = mass;
        /// <summary>The billboard size it was made for.</summary>
        internal Vector2 Size { get; } = size;
        internal Vector3 Centre { get; set; } = centre;
        internal Quaternion Turn { get; set; } = Quaternion.Identity;
        internal Vector3 Velocity { get; set; }
        internal bool Sleeping { get; set; } = sleeping;
        /// <summary>Whether, carried, it has come within the break distance of where it is held.</summary>
        internal bool Reached { get; set; }
        /// <summary>Where it last rested.</summary>
        internal Vector3 Rest { get; set; } = centre;
    }

    /// <summary>The pile the player carries, or null.</summary>
    internal long? Carried { get; private set; }

    /// <summary>Why the carried item was last let go of by itself, once, or null.</summary>
    internal string? TakeNotice()
    {
        string? notice = Notice;
        Notice = null;
        return notice;
    }

    private string? Notice { get; set; }

    /// <summary>Whether the pile has a body now.</summary>
    internal bool Holds(long id) => bodies.ContainsKey(id);

    /// <summary>A pile just dropped from the inventory: its body is made awake, leaving the hand at <paramref name="velocity"/>.</summary>
    internal void Drop(long id, Vector3 velocity) => dropped[id] = velocity;

    /// <summary>
    /// Takes hold of a pile's item, or says why not: it has no body, it is beyond the tuning's reach of the eye, or it
    /// weighs more than the player's Strength can lift.
    /// </summary>
    internal string? Grab(long id, DaggerfallLooseItemHand hand)
    {
        if (!bodies.TryGetValue(id, out Body? body)) return "That cannot be moved.";
        if (Vector3.Distance(hand.Eye, body.Centre) > tuning.Reach) return "That is out of reach.";
        if (body.Mass > tuning.CarryKilogramsPerStrength * hand.Strength) return "That is too heavy to lift.";
        Carried = id;
        body.Reached = false;
        return null;
    }

    /// <summary>Lets the carried item go: it falls as it was moving.</summary>
    internal void Release() => Carried = null;

    /// <summary>
    /// Throws the carried item along the look: an impulse of the tuning's per point of Strength, so a heavier item flies
    /// slower, and none faster than the tuning's throw speed. False when nothing is carried.
    /// </summary>
    internal bool Throw(DaggerfallLooseItemHand hand)
    {
        if (Carried is not long id || !bodies.TryGetValue(id, out Body? body)) return false;
        float impulse = Math.Min(tuning.ThrowImpulsePerStrength * hand.Strength, body.Mass * tuning.MaxThrowSpeed);
        actions.Add(new(body.Handle, Vector3.Zero, Vector3.Zero, Vector3.Normalize(hand.Forward) * impulse, Vector3.Zero, true));
        Carried = null;
        return true;
    }

    /// <summary>
    /// Drops every body, as the site changes or the world's origin moves under them; the piles keep where they are, and
    /// each body is made again there next update, awake if it was moving.
    /// </summary>
    internal void LetGo()
    {
        foreach ((long id, Body body) in bodies)
        {
            if (!body.Sleeping) woken.Add(id);
            body.Handle.Dispose();
        }
        bodies.Clear();
        actions.Clear();
        Carried = null;
        world?.Dispose();
        world = null;
    }

    /// <summary>
    /// World seconds admitted: lets go of the bodies whose piles went or lie far off, makes bodies for the items near the
    /// player where the collision under them stands, drives the carried item, steps the bodies while any moves, and tells
    /// <paramref name="moved"/> where each moved pile's foot now stands.
    /// </summary>
    /// <param name="collision">The site's collision, which the bodies land on and strike.</param>
    /// <param name="items">The loose items in the active site.</param>
    /// <param name="feet">Where the player's feet stand.</param>
    /// <param name="kilograms">What a pile weighs, read as its body is made.</param>
    /// <param name="collides">Whether the collision about a point is in the scene, so a body made there has a floor to land on.</param>
    internal void Step(float elapsed, SpatialSession collision, IReadOnlyList<DaggerfallLooseItem> items, Vector3 feet,
        DaggerfallLooseItemHand hand, Func<long, float> kilograms, Func<Vector3, bool> collides, Action<long, Vector3> moved)
    {
        player = feet;
        Dictionary<long, DaggerfallLooseItem> here = items.ToDictionary(item => item.Id);
        foreach ((long id, Body body) in bodies.ToArray())
        {
            if (!here.TryGetValue(id, out DaggerfallLooseItem item)) Remove(id);
            else if (item.Size != body.Size)
            {
                // The art drawn changed, and the body is made again at its billboard's new size.
                if (!body.Sleeping) woken.Add(id);
                Remove(id);
            }
            else if (id != Carried && body.Sleeping && Vector3.Distance(item.Foot, feet) > tuning.ActivationRadius + RetireMargin) Remove(id);
            else if (Vector3.Distance(item.Foot, body.Centre - Vector3.UnitY * body.Half.Y) > Placed) Remove(id);
        }
        foreach (long gone in dropped.Keys.Concat(woken).Where(id => !here.ContainsKey(id)).ToArray())
        {
            dropped.Remove(gone);
            woken.Remove(gone);
        }
        foreach (DaggerfallLooseItem item in items)
            if (item.Size is Vector2 size && !bodies.ContainsKey(item.Id) && Vector3.Distance(item.Foot, feet) <= tuning.ActivationRadius
                && collides(item.Foot + Vector3.UnitY * Half(size).Y))
                Create(item.Id, item.Foot, size, kilograms(item.Id));
        if (Carried is long carried && !bodies.ContainsKey(carried)) Carried = null;
        if (world is null || elapsed <= 0) return;
        if (Carried is long holding) Drive(bodies[holding], hand, elapsed);
        if (actions.Count == 0 && bodies.Values.All(body => body.Sleeping)) return;

        long start = Stopwatch.GetTimestamp();
        uint steps = (uint)Math.Max(1, MathF.Ceiling(elapsed * tuning.StepsPerSecond));
        long[] ids = [.. bodies.Keys];
        // Bound again each step, the world meets a door opened since; the Engine skips the bind while nothing changed.
        dynamics.BindWorldCollision(new(world, collision));
        DynamicsStepAndReadResult result = dynamics.StepAndRead(new(world, elapsed / steps, steps, actions.ToArray(),
            ids.Select(id => bodies[id].Handle).ToArray()));
        actions.Clear();
        ReadOnlySpan<DynamicsBodyFact> facts = result.Bodies.Span;
        for (int index = 0; index < ids.Length; index++) Settle(ids[index], bodies[ids[index]], facts[index].Readout, moved);
        costs.Enqueue(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        while (costs.Count > 600) costs.Dequeue();
    }

    private static Vector3 Half(Vector2 size) =>
        new(Math.Max(size.X / 2, MinimumHalfExtent), Math.Max(size.Y / 2, MinimumHalfExtent), Math.Max(size.X / 2, MinimumHalfExtent));

    private void Create(long id, Vector3 foot, Vector2 size, float kilograms)
    {
        world ??= dynamics.CreateWorld(new DynamicsWorldConfig(new Vector3(0, -9.81f, 0)));
        Vector3 half = Half(size);
        Vector3 centre = foot + Vector3.UnitY * half.Y;
        float mass = Math.Max(kilograms, tuning.MinimumKilograms);
        bool awake = dropped.Remove(id, out Vector3 velocity) | woken.Remove(id);
        DynamicsBody handle = dynamics.CreateCuboidBody(new(world, new DynamicsCuboidBodyConfig(new Transform(centre, Quaternion.Identity, Vector3.One), half,
            new DynamicsBodyProperties(mass, default, velocity, Vector3.Zero, Upright, tuning.LinearDamping, 0, 1, tuning.Friction, tuning.Restitution,
                ItemGroup, uint.MaxValue, true, !awake, true))));
        bodies.Add(id, new Body(handle, half, mass, size, centre, !awake) { Velocity = velocity });
    }

    private void Remove(long id)
    {
        bodies.Remove(id, out Body? body);
        body!.Handle.Dispose();
        if (Carried == id) Carried = null;
    }

    /// <summary>Moves the carried item toward where it is held, closing the gap at the tuning's pace, its weight borne.</summary>
    private void Drive(Body body, DaggerfallLooseItemHand hand, float elapsed)
    {
        held = hand.Eye + Vector3.Normalize(hand.Forward) * tuning.HoldDistance;
        Vector3 want = (held - body.Centre) / tuning.FollowSeconds;
        if (want.Length() > tuning.CarrySpeed) want = Vector3.Normalize(want) * tuning.CarrySpeed;
        Vector3 impulse = body.Mass * (want - body.Velocity) + body.Mass * 9.81f * elapsed * Vector3.UnitY;
        actions.Add(new(body.Handle, Vector3.Zero, Vector3.Zero, impulse, Vector3.Zero, true));
        body.Sleeping = false;
    }

    private void Settle(long id, Body body, DynamicsReadout readout, Action<long, Vector3> moved)
    {
        Vector3 centre = readout.Transform.Translation;
        body.Turn = readout.Transform.Rotation;
        body.Velocity = readout.LinearVelocity;
        body.Sleeping = readout.Sleeping;
        if (centre.Y < body.Rest.Y - LostFall)
        {
            // Fallen through the floor, it would be lost below: it goes back where it last rested.
            centre = body.Rest;
            dynamics.Reset(new(body.Handle, new Transform(centre, Quaternion.Identity, Vector3.One), Vector3.Zero, Vector3.Zero, true));
            (body.Sleeping, body.Velocity) = (true, Vector3.Zero);
            if (Carried == id) Carried = null;
        }
        if (Vector3.Distance(centre, body.Centre) > Stirs)
        {
            body.Centre = centre;
            moved(id, centre - Vector3.UnitY * body.Half.Y);
        }
        if (Carried == id)
        {
            // It is let go once it lags where it is held, having reached it: caught on something the hand moved past.
            bool within = Vector3.Distance(centre, held) <= tuning.BreakDistance;
            if (body.Reached && !within)
            {
                Carried = null;
                Notice = "It slipped from your grasp.";
            }
            body.Reached |= within;
        }
        else if (body.Sleeping) body.Rest = body.Centre;
    }

    /// <summary>Every body now, nearest the player first.</summary>
    internal IReadOnlyList<DaggerfallLooseItemReadout> Read() => [.. bodies
        .OrderBy(entry => player is Vector3 at ? Vector3.Distance(entry.Value.Centre, at) : 0).ThenBy(entry => entry.Key)
        .Select(entry => new DaggerfallLooseItemReadout(entry.Key, entry.Value.Centre - Vector3.UnitY * entry.Value.Half.Y, entry.Value.Turn, entry.Value.Velocity,
            entry.Value.Sleeping, entry.Value.Mass, entry.Value.Half * 2, Carried == entry.Key))];

    /// <summary>How many loose items have bodies, how many are awake, what stepping them costs, and the nearest few.</summary>
    internal string Describe()
    {
        IReadOnlyList<DaggerfallLooseItemReadout> read = Read();
        string cost = costs.Count == 0 ? "no steps yet" : $"over the last {costs.Count} stepped updates a step took {costs.Average():0.000} ms on average "
            + $"(longest {costs.Max():0.000})";
        string nearest = string.Join("; ", read.Take(5).Select(item => $"pile {item.Id} {(item.Carried ? "carried" : item.Sleeping ? "asleep" : "awake")} at "
            + $"<{item.Foot.X:0.00}, {item.Foot.Y:0.00}, {item.Foot.Z:0.00}> moving {item.Velocity.Length():0.00} m/s, {item.Kilograms:0.##} kg, "
            + $"{item.Size.X:0.00} by {item.Size.Y:0.00} m"));
        return $"{read.Count} loose items have bodies, {read.Count(item => !item.Sleeping)} awake{(Carried is long id ? $", pile {id} carried" : "")}; {cost}."
            + (nearest.Length == 0 ? "" : $" Nearest the player: {nearest}.");
    }

    public void Dispose() => LetGo();
}
