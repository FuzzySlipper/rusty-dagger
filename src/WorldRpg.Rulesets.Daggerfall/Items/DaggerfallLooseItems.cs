using System.Diagnostics;
using System.Numerics;
using Rusty.Engine;

namespace WorldRpg.Rulesets.Daggerfall.Modules.Loot;

/// <summary>A loose item this update: its pile, where its foot stands, and its billboard's width and height, or null before it is first drawn.</summary>
internal readonly record struct DaggerfallLooseItem(long Id, Vector3 Foot, Vector2? Size);

/// <summary>The player's hand as loose items meet it: where the eye is, which way it looks, and the player's Strength.</summary>
internal readonly record struct DaggerfallLooseItemHand(Vector3 Eye, Vector3 Forward, int Strength);

/// <summary>
/// The site loose items lie in, as the session binds it: its collision and the version of it now, what a pile weighs and
/// the revision of every pile's contents, whether the collision about a point is in the scene (so a body made there has a
/// floor), whether an item's box can move from one point to another without meeting collision, where a moved pile's foot
/// now stands, and how far from its middle the player's body reaches.
/// </summary>
internal sealed record DaggerfallLooseItemSite(SpatialSession Collision, ulong CollisionVersion, Func<long, float> Kilograms, ulong ContentsRevision,
    Func<Vector3, bool> Collides, Func<Vector3, Vector3, Vector3, bool> Reaches, Action<long, Vector3> Moved, float PlayerRadius);

/// <summary>One loose item's body as a developer reads it.</summary>
internal readonly record struct DaggerfallLooseItemReadout(long Id, Vector3 Foot, Quaternion Turn, Vector3 Velocity, bool Sleeping, float Kilograms, Vector3 Size, bool Carried);

/// <summary>
/// Loose items (dropped, or placed by a quest) as Engine Dynamics bodies near the player. Each is an upright box as wide and
/// deep as its billboard is wide and as tall as it is tall, its rotation locked so the billboard always stands over it,
/// weighing what its pile weighs, with the tuning's high friction and damping, so a pile stays put rather than creeping. A
/// dropped item falls and settles; one the player comes near is made asleep where it rests. An item sleeps by the tuning's
/// thresholds, sooner than the Engine's other bodies, so a slow creep stops rather than carrying it off. The player may carry one
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
    // How many rings about the drop point items dropped together may be let go on.
    private const int DropRings = 4;
    // How near another body counts as touching one that is removed, in metres.
    private const float Touching = .05f;
    private static readonly AxisLocks Upright = new(false, false, false, true, true, true);

    private readonly DaggerfallLooseItemTuning tuning = tuning.Validate();
    private readonly Dictionary<long, Body> bodies = [];
    // Items let go of while they moved, and those just dropped with the toss they leave the hand with: each body is made awake.
    private readonly HashSet<long> woken = [];
    private readonly Dictionary<long, Vector3> dropped = [];
    private readonly List<DynamicsAction> actions = [];
    // Bodies that touched one just removed, to be woken so the Engine settles them without it.
    private readonly HashSet<long> unsupported = [];
    // The pile contents the bodies were weighed at, and the collision the world was last stepped against.
    private ulong? weighed;
    private ulong? stepped;
    private readonly Queue<double> costs = new();
    private DynamicsWorld? world;
    private Vector3 held;
    private Vector3? player;

    private sealed class Body(DynamicsBody handle, Vector3 half, float mass, Vector2 size, Vector3 centre, bool sleeping)
    {
        internal DynamicsBody Handle { get; } = handle;
        internal Vector3 Half { get; } = half;
        internal float Mass { get; set; } = mass;
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
        unsupported.Clear();
        stepped = null;
        world?.Dispose();
        world = null;
    }

    /// <summary>
    /// World seconds admitted: lets go of the bodies whose piles went or lie far off, makes bodies for the items near the
    /// player where the collision under them stands, drives the carried item, steps the bodies while any moves, and tells
    /// the site where each moved pile's foot now stands.
    /// </summary>
    /// <param name="items">The loose items in the active site.</param>
    /// <param name="feet">Where the player's feet stand.</param>
    internal void Step(float elapsed, DaggerfallLooseItemSite site, IReadOnlyList<DaggerfallLooseItem> items, Vector3 feet, DaggerfallLooseItemHand hand)
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
            else if (id != Carried && body.Sleeping && Vector3.Distance(item.Foot, feet) > tuning.ActivationRadius + RetireMargin) Remove(id, unsupporting: false);
            else if (Vector3.Distance(item.Foot, body.Centre - Vector3.UnitY * body.Half.Y) > Placed) Remove(id);
        }
        foreach (long gone in dropped.Keys.Concat(woken).Where(id => !here.ContainsKey(id)).ToArray())
        {
            dropped.Remove(gone);
            woken.Remove(gone);
        }
        foreach (DaggerfallLooseItem item in items)
            if (item.Size is Vector2 size && !bodies.ContainsKey(item.Id) && Vector3.Distance(item.Foot, feet) <= tuning.ActivationRadius
                && site.Collides(item.Foot + Vector3.UnitY * Half(size).Y))
                Create(item.Id, item.Foot, size, site);
        if (Carried is long carried && !bodies.ContainsKey(carried)) Carried = null;
        if (weighed != site.ContentsRevision) Weigh(site);
        if (world is null || elapsed <= 0) return;
        if (Carried is long holding) Drive(bodies[holding], hand, elapsed);
        foreach (long id in unsupported)
            if (bodies.TryGetValue(id, out Body? body)) actions.Add(new(body.Handle, Vector3.Zero, Vector3.Zero, Vector3.Zero, Vector3.Zero, true));
        unsupported.Clear();
        // While every body sleeps and nothing touched them or the collision under them, there is nothing to step.
        if (actions.Count == 0 && stepped == site.CollisionVersion && bodies.Values.All(body => body.Sleeping)) return;
        stepped = site.CollisionVersion;

        long start = Stopwatch.GetTimestamp();
        uint steps = (uint)Math.Max(1, MathF.Ceiling(elapsed * tuning.StepsPerSecond));
        long[] ids = [.. bodies.Keys];
        // Bound again each step, the world meets a door opened since; the Engine skips the bind while nothing changed.
        dynamics.BindWorldCollision(new(world, site.Collision));
        DynamicsStepAndReadResult result = dynamics.StepAndRead(new(world, elapsed / steps, steps, actions.ToArray(),
            ids.Select(id => bodies[id].Handle).ToArray()));
        actions.Clear();
        ReadOnlySpan<DynamicsBodyFact> facts = result.Bodies.Span;
        for (int index = 0; index < ids.Length; index++) Settle(ids[index], bodies[ids[index]], facts[index].Readout, site.Moved);
        costs.Enqueue(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        while (costs.Count > 600) costs.Dequeue();
    }

    private static Vector3 Half(Vector2 size) =>
        new(Math.Max(size.X / 2, MinimumHalfExtent), Math.Max(size.Y / 2, MinimumHalfExtent), Math.Max(size.X / 2, MinimumHalfExtent));

    private void Create(long id, Vector3 foot, Vector2 size, DaggerfallLooseItemSite site)
    {
        world ??= dynamics.CreateWorld(new DynamicsWorldConfig(new Vector3(0, -9.81f, 0)));
        Vector3 half = Half(size);
        Vector3 centre = foot + Vector3.UnitY * half.Y;
        if (dropped.ContainsKey(id))
        {
            // Items dropped together are let go side by side, not inside one another; one with no room yet waits for the
            // others to fall clear.
            if (Clear(centre, half, site) is not Vector3 clear) return;
            if (clear != centre) site.Moved(id, clear - Vector3.UnitY * half.Y);
            centre = clear;
        }
        float mass = Math.Max(site.Kilograms(id), tuning.MinimumKilograms);
        bool awake = dropped.Remove(id, out Vector3 velocity) | woken.Remove(id);
        DynamicsBody handle = dynamics.CreateCuboidBody(new(world, new DynamicsCuboidBodyConfig(new Transform(centre, Quaternion.Identity, Vector3.One), half,
            Properties(mass, velocity, !awake))));
        bodies.Add(id, new Body(handle, half, mass, size, centre, !awake) { Velocity = velocity });
    }

    private DynamicsBodyProperties Properties(float mass, Vector3 velocity, bool sleeping) => new(mass, default, velocity, Vector3.Zero, Upright,
        tuning.LinearDamping, 0, 1, tuning.Friction, tuning.Restitution, ItemGroup, uint.MaxValue, true, sleeping, true,
        new DynamicsSleepThresholds(tuning.SleepSpeed, tuning.SleepSeconds));

    /// <summary>Weighs every body's pile again once any pile's contents changed, as part of one is taken.</summary>
    private void Weigh(DaggerfallLooseItemSite site)
    {
        weighed = site.ContentsRevision;
        foreach ((long id, Body body) in bodies)
        {
            float mass = Math.Max(site.Kilograms(id), tuning.MinimumKilograms);
            if (MathF.Abs(mass - body.Mass) < 1e-4f) continue;
            body.Mass = mass;
            dynamics.UpdateBody(new(body.Handle, Properties(mass, body.Velocity, body.Sleeping)));
        }
    }

    /// <summary>
    /// Where a dropped item is let go clear of the bodies already made: the drop point, else the first free place on rings
    /// about it, the first outside the player's body, that the item could move to from the drop point without meeting the
    /// site's collision, or null while there is none.
    /// </summary>
    private Vector3? Clear(Vector3 centre, Vector3 half, DaggerfallLooseItemSite site)
    {
        bool Free(Vector3 at) => !bodies.Values.Any(body => MathF.Abs(body.Centre.X - at.X) < body.Half.X + half.X
            && MathF.Abs(body.Centre.Y - at.Y) < body.Half.Y + half.Y && MathF.Abs(body.Centre.Z - at.Z) < body.Half.Z + half.Z);
        if (Free(centre)) return centre;
        for (int ring = 1; ring <= DropRings; ring++)
            for (int step = 0; step < 8 * ring; step++)
            {
                float angle = step * MathF.PI / (4 * ring), radius = site.PlayerRadius + half.X + (ring - 1) * (2 * half.X + .05f) + .05f;
                Vector3 at = centre + new Vector3(MathF.Cos(angle) * radius, 0, MathF.Sin(angle) * radius);
                if (Free(at) && site.Reaches(centre, at, half)) return at;
            }
        return null;
    }

    /// <summary>
    /// Lets go of a pile's body. One taken or remade (<paramref name="unsupporting"/>) leaves the bodies it touched to be
    /// woken, so one resting on it falls.
    /// </summary>
    private void Remove(long id, bool unsupporting = true)
    {
        bodies.Remove(id, out Body? body);
        body!.Handle.Dispose();
        if (Carried == id) Carried = null;
        if (!unsupporting) return;
        foreach ((long other, Body near) in bodies)
            if (MathF.Abs(near.Centre.X - body.Centre.X) <= near.Half.X + body.Half.X + Touching
                && MathF.Abs(near.Centre.Y - body.Centre.Y) <= near.Half.Y + body.Half.Y + Touching
                && MathF.Abs(near.Centre.Z - body.Centre.Z) <= near.Half.Z + body.Half.Z + Touching)
                unsupported.Add(other);
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

    /// <summary>How many loose items have bodies, how many are awake and how many dropped wait for room, what stepping them costs, and the nearest few.</summary>
    internal string Describe()
    {
        IReadOnlyList<DaggerfallLooseItemReadout> read = Read();
        string cost = costs.Count == 0 ? "no steps yet" : $"over the last {costs.Count} stepped updates a step took {costs.Average():0.000} ms on average "
            + $"(longest {costs.Max():0.000})";
        string nearest = string.Join("; ", read.Take(5).Select(item => $"pile {item.Id} {(item.Carried ? "carried" : item.Sleeping ? "asleep" : "awake")} at "
            + $"<{item.Foot.X:0.00}, {item.Foot.Y:0.00}, {item.Foot.Z:0.00}> moving {item.Velocity.Length():0.00} m/s, {item.Kilograms:0.##} kg, "
            + $"{item.Size.X:0.00} by {item.Size.Y:0.00} m"));
        return $"{read.Count} loose items have bodies, {read.Count(item => !item.Sleeping)} awake{(Carried is long id ? $", pile {id} carried" : "")}"
            + $"{(dropped.Count > 0 ? $", {dropped.Count} dropped waiting for room" : "")}; {cost}."
            + (nearest.Length == 0 ? "" : $" Nearest the player: {nearest}.");
    }

    public void Dispose() => LetGo();
}
