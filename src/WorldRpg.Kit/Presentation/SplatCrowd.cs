using Rusty.Engine;
using WorldRpg.Kit.World;

namespace WorldRpg.Kit.Presentation;

/// <summary>
/// A Gaussian-splat character walkers are drawn as: its animated appearance (in metres, standing on y = 0 and facing +z),
/// which every walker draws as an instance of; the walks and idles a walker may play as it, each walker playing one of
/// each (<see cref="Gait"/>); the crossfade between the two; its long skirt on joints of its own, which the legs push
/// open, or null; and its eyes, which blink, or null.
/// </summary>
public sealed record SplatFigure(string Name, Appearance Appearance, IReadOnlyList<SplatWalk> Walks, IReadOnlyList<string> Idles, float CrossfadeSeconds,
    SplatSkirtRig? Skirt = null, SplatEyes? Eyes = null)
{
    /// <summary>
    /// The gait a walker of stable <paramref name="seed"/> has as this figure: one of its walks and one of its idles, a
    /// rate within <paramref name="jitter"/> either side of 1 that both play at and it walks at, and the share of the way
    /// through either clip it starts at, each drawn from the seed apart from the others. The same seed has the same gait in every run, so a walker keeps it over a save.
    /// </summary>
    public SplatGait Gait(int seed, float jitter)
    {
        if (!(jitter is >= 0 and < 1)) throw new ArgumentOutOfRangeException(nameof(jitter), jitter, "A gait's rate varies by 0 to under 1 either side of 1.");
        if (Walks.Count == 0 || Idles.Count == 0) throw new InvalidOperationException($"Gsplat figure {Name} has {Walks.Count} walks and {Idles.Count} idles; a walker needs one of each.");
        return new(Walks[(int)(Draw("walk") % (ulong)Walks.Count)], Idles[(int)(Draw("idle") % (ulong)Idles.Count)],
            1F + jitter * (Draw("rate") % 10001 / 5000F - 1F), Draw("phase") % 10000 / 10000F);

        // Mixed, since the hash's low bits would follow the seed's own (the seed % 4 that picks a townsperson's art among four).
        ulong Draw(string what) => StableHash.Mixed($"splat-gait/{what}/{seed}");
    }
}

/// <summary>
/// A walk a <see cref="SplatFigure"/> may walk: its clip, the share of the walkers' speed a walker walking it goes at, and
/// the rate the clip plays at so its stride keeps that pace.
/// </summary>
public sealed record SplatWalk(string Clip, float Pace, float Rate);

/// <summary>
/// How one walker walks and stands as its figure: its <paramref name="Walk"/> and <paramref name="Idle"/>, the
/// <paramref name="Rate"/> both play at and it walks at, about 1, and the <paramref name="Phase"/> (0 to under 1, a share
/// of the clip) both start at, so no two walkers move as one, even those that set off together.
/// </summary>
public readonly record struct SplatGait(SplatWalk Walk, string Idle, float Rate, float Phase)
{
    /// <summary>The share of the walkers' speed this walker goes at; its walk's stride keeps it.</summary>
    public float Pace => Walk.Pace * Rate;

    /// <summary>The rate its walk plays at.</summary>
    public float WalkRate => Walk.Rate * Rate;
}

/// <summary>
/// Draws walkers as <see cref="SplatFigure"/>s, at their own size: one assigned to a walker (<see cref="Assign"/>),
/// which it draws as that figure whether or not the crowd uses one, else the crowd's figure, else the walker's own
/// (<see cref="UseOwn"/>). Each walker shown in a snapshot is an animation instance of the figure's appearance, playing
/// the walk of its gait while it walks and the idle otherwise, each from its gait's phase, its gait drawn from the walker's stable seed
/// (<paramref name="seeds"/>, by actor) and the crowd's <paramref name="jitter"/>. The figure's
/// appearance and clips belong to whoever made it. An object holds one instance, so a walker that changes figure has
/// its instance replaced, and one a snapshot leaves out has its instance released before that snapshot is published:
/// the Engine refuses a pose for an object that now shows a sprite. A figure with a skirt has each walker's skirt
/// driven on its own sheet: each settle moves it on by the time <see cref="Advance"/> gave since the last, from where the
/// walker's clips put its legs as that settle is drawn, and places the skirt joints over the clips. While the crowd
/// blinks (<see cref="Blink"/>), a figure with eyes has each walker blink on its own clock, keyed by its seed: each settle
/// moves the clock on by the same time and tints the eyes toward the lids by how shut they are, telling the Engine only
/// when that changes.
/// </summary>
public sealed class SplatCrowd(Func<IAnimationService> engine, Func<long, int> seeds, float jitter) : IWalkerCrowd, IDisposable
{
    private readonly Dictionary<ulong, Walker> shown = [];
    private readonly HashSet<ulong> seen = [];
    private readonly Dictionary<long, SplatFigure> assigned = [];
    private static readonly Transform Unplaced = new(System.Numerics.Vector3.Zero, System.Numerics.Quaternion.Identity, System.Numerics.Vector3.One);
    private float seconds;
    private bool disposed;

    /// <summary>How walkers with eyes blink, or null while they don't.</summary>
    public SplatBlinks? Blinks { get; private set; }

    /// <summary>The figure walkers are drawn as, or null while they show their own art.</summary>
    public SplatFigure? Figure { get; private set; }

    /// <summary>How many walkers the last settled snapshot drew as the figure.</summary>
    public int Count => shown.Count;

    /// <summary>Each walker's own figure, by actor, drawn where the crowd uses none; null (or a null figure) shows its own art.</summary>
    public Func<long, SplatFigure?>? Own { get; private set; }

    /// <summary>The walkers drawn as a figure of their own, by actor.</summary>
    public IReadOnlyDictionary<long, SplatFigure> Assigned => assigned;

    /// <summary>The figures the walkers shown in the last snapshot draw as.</summary>
    public IEnumerable<SplatFigure> Drawn => shown.Values.Select(walker => walker.Figure).Distinct();

    /// <summary>The gait walker object <paramref name="id"/> has as the figure the last snapshot showed it as, or null when it showed none.</summary>
    public SplatGait? Gait(ulong id) => shown.TryGetValue(id, out Walker? walker) ? walker.Gait : null;

    /// <summary>Draws walker <paramref name="actor"/> as <paramref name="figure"/> from the next snapshot, or (null) as the crowd draws the rest.</summary>
    public void Assign(long actor, SplatFigure? figure)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (figure is null) assigned.Remove(actor);
        else assigned[actor] = figure;
    }

    /// <summary>Draws walkers as <paramref name="figure"/> from the next snapshot, or (null) as their own art again.</summary>
    public void Use(SplatFigure? figure)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        Figure = figure;
    }

    /// <summary>Draws each walker as its own figure from the next snapshot where the crowd uses none, or (null) as its own art again.</summary>
    public void UseOwn(Func<long, SplatFigure?>? figures)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        Own = figures;
    }

    /// <summary>
    /// Has walkers with eyes blink as <paramref name="blinks"/> from the next settle, or (null) keep their eyes open: each
    /// walker's clock starts again, and lids shut now open at that settle.
    /// </summary>
    public void Blink(SplatBlinks? blinks)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        Blinks = blinks;
        foreach (Walker walker in shown.Values) walker.Blink = null;
    }

    /// <summary>Counts <paramref name="elapsed"/> seconds of drawn time toward the next settle, which moves the skirts and blinks on by them.</summary>
    public void Advance(float elapsed)
    {
        if (!float.IsFinite(elapsed) || elapsed < 0) throw new ArgumentOutOfRangeException(nameof(elapsed), elapsed, "Drawn time advances by 0 or more seconds.");
        seconds += elapsed;
    }

    public Appearance? Show(ulong id, long actor, float heightMetres, bool walking)
    {
        if (disposed || (assigned.TryGetValue(actor, out SplatFigure? given) ? given : Figure ?? Own?.Invoke(actor)) is not { } figure) return null;
        seen.Add(id);
        if (!shown.TryGetValue(id, out Walker? walker))
            shown[id] = walker = new(figure, seeds(actor), figure.Gait(seeds(actor), jitter), Engine.CreateInstance(new(figure.Appearance, id)));
        else if (walker.Figure != figure)
        {
            walker.Instance = Engine.ReplaceInstance(walker.Instance, new(figure.Appearance, id));
            walker.Figure = figure;
            walker.Gait = figure.Gait(walker.Seed, jitter);
            walker.Played = null;
            walker.Skirt = null;
            // The new instance draws its eyes open.
            walker.Blink = null;
            walker.Closure = 0;
        }
        walker.Wanted = walking ? walker.Gait.Walk.Clip : walker.Gait.Idle;
        return figure.Appearance;
    }

    public void Publishing()
    {
        if (disposed) return;
        List<Exception>? failures = null;
        foreach (ulong id in shown.Keys.Where(id => !seen.Contains(id)).ToArray())
        {
            Release(shown[id].Instance, ref failures);
            shown.Remove(id);
        }
        seen.Clear();
        if (failures is { Count: > 0 }) throw new AggregateException(failures);
    }

    public void Settle()
    {
        if (disposed) return;
        foreach (Walker walker in shown.Values)
        {
            if (walker.Played == walker.Wanted) continue;
            // A new instance starts its clip at once; a walker that stops or sets off crossfades into the other. Either starts
            // at the walker's own phase, so walkers shown or setting off together are not in step.
            bool fades = walker.Played is not null;
            float speed = walker.Wanted == walker.Gait.Walk.Clip ? walker.Gait.WalkRate : walker.Gait.Rate;
            Engine.SetPlayback(new(walker.Instance, AnimationPlaybackKind.Play, walker.Wanted, AnimationLoopMode.Repeat, speed, 1F,
                !fades, fades ? walker.Figure.CrossfadeSeconds : 0F, fades, walker.Gait.Phase));
            walker.Played = walker.Wanted;
        }
        float elapsed = seconds;
        seconds = 0;
        DriveSkirts(elapsed);
        DriveBlinks(elapsed);
    }

    /// <summary>
    /// Moves every shown walker's blink on by <paramref name="elapsed"/> and tints its eyes by how shut they are, where that
    /// changed. A walker's clock starts at its first settle while the crowd blinks; without blinks its eyes open.
    /// </summary>
    private void DriveBlinks(float elapsed)
    {
        foreach (Walker walker in shown.Values)
        {
            if (walker.Figure.Eyes is not { } eyes) continue;
            float closure = 0;
            if (Blinks is { } blinks)
            {
                bool started = walker.Blink is not null;
                walker.Blink ??= blinks.Clock(walker.Seed);
                closure = walker.Blink.Step(started ? elapsed : 0);
            }
            if (closure == walker.Closure) continue;
            Engine.SetSplatGroupTint(new(walker.Instance, eyes.Group, eyes.Lid, closure));
            walker.Closure = closure;
        }
    }

    /// <summary>
    /// Moves every shown skirt on by <paramref name="elapsed"/>, the time since the last settle. The legs are read from the Engine and the joints placed
    /// on this update; the sheets between, which read nothing else, step side by side on the thread pool.
    /// </summary>
    private void DriveSkirts(float elapsed)
    {
        List<(Walker Walker, JointPose[] Pose)> driven = [];
        foreach (Walker walker in shown.Values)
        {
            if (walker.Figure.Skirt is not { } skirt || walker.Skirt is not null && elapsed == 0) continue;
            walker.Skirt ??= skirt.Sheet();
            driven.Add((walker, Engine.SampleJointPose(AnimationPoseSampleRequest.OfCurrentPlayback(walker.Instance, Unplaced, applyPoseControls: false)).ToArray()));
        }
        JointOverride[][] placed = new JointOverride[driven.Count][];
        if (driven.Count > 1) Parallel.For(0, driven.Count, index => placed[index] = driven[index].Walker.Skirt!.Step(elapsed, driven[index].Pose));
        else if (driven.Count == 1) placed[0] = driven[0].Walker.Skirt!.Step(elapsed, driven[0].Pose);
        for (int index = 0; index < driven.Count; index++)
            Engine.SetPose(new(driven[index].Walker.Instance, ReadOnlyMemory<TwoBoneIk>.Empty, placed[index]));
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        List<Exception>? failures = null;
        foreach (Walker walker in shown.Values) Release(walker.Instance, ref failures);
        shown.Clear();
        if (failures is { Count: > 0 }) throw new AggregateException(failures);
    }

    private IAnimationService Engine => engine();

    private static void Release(IDisposable value, ref List<Exception>? failures)
    {
        try { value.Dispose(); }
        catch (Exception exception) { (failures ??= []).Add(exception); }
    }

    private sealed class Walker(SplatFigure figure, int seed, SplatGait gait, AnimationInstance instance)
    {
        internal SplatFigure Figure { get; set; } = figure;
        /// <summary>The walker's stable seed, which keys its gait and its blinks.</summary>
        internal int Seed { get; } = seed;
        internal SplatGait Gait { get; set; } = gait;
        internal AnimationInstance Instance { get; set; } = instance;
        /// <summary>The clip the latest snapshot asked for, and the one played; null before the first play.</summary>
        internal string Wanted { get; set; } = string.Empty;
        internal string? Played { get; set; }
        /// <summary>The walker's skirt, from its first settle as a figure with one.</summary>
        internal SplatSkirtSheet? Skirt { get; set; }
        /// <summary>The walker's blinks, from its first settle while the crowd blinks a figure with eyes.</summary>
        internal SplatBlinkClock? Blink { get; set; }
        /// <summary>How shut its eyes were last drawn: 0 open, 1 shut.</summary>
        internal float Closure { get; set; }
    }
}
