using Rusty.Engine;

namespace WorldRpg.Kit.Presentation;

/// <summary>
/// A Gaussian-splat character walkers are drawn as: its animated appearance (in metres, standing on y = 0 and facing +z),
/// which every walker draws as an instance of; the ids of its <paramref name="Walk"/> and <paramref name="Idle"/> clips;
/// the rate its walk plays at so its stride keeps pace with a walker; and the crossfade between the two.
/// </summary>
public sealed record SplatFigure(string Name, Appearance Appearance, string Walk, string Idle, float WalkRate, float CrossfadeSeconds);

/// <summary>
/// Draws walkers as one <see cref="SplatFigure"/>, at its own size: each walker shown in a snapshot is an animation
/// instance of the figure's appearance, playing its walk while it walks and its idle otherwise. The figure's
/// appearance and clips belong to whoever made it. An object holds one instance, so a walker that changes figure has
/// its instance replaced, and one a snapshot leaves out has its instance released before that snapshot is published:
/// the Engine refuses a pose for an object that now shows a sprite.
/// </summary>
public sealed class SplatCrowd(Func<IAnimationService> engine) : IWalkerCrowd, IDisposable
{
    private readonly Dictionary<ulong, Walker> shown = [];
    private readonly HashSet<ulong> seen = [];
    private bool disposed;

    /// <summary>The figure walkers are drawn as, or null while they show their own art.</summary>
    public SplatFigure? Figure { get; private set; }

    /// <summary>How many walkers the last settled snapshot drew as the figure.</summary>
    public int Count => shown.Count;

    /// <summary>Draws walkers as <paramref name="figure"/> from the next snapshot, or (null) as their own art again.</summary>
    public void Use(SplatFigure? figure)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        Figure = figure;
    }

    public Appearance? Show(ulong id, float heightMetres, bool walking)
    {
        if (disposed || Figure is not { } figure) return null;
        seen.Add(id);
        if (!shown.TryGetValue(id, out Walker? walker))
            shown[id] = walker = new(figure, Engine.CreateInstance(new(figure.Appearance, id)));
        else if (walker.Figure != figure)
        {
            walker.Instance = Engine.ReplaceInstance(walker.Instance, new(figure.Appearance, id));
            walker.Figure = figure;
            walker.Played = null;
        }
        walker.Wanted = walking ? figure.Walk : figure.Idle;
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
            // A new instance starts its clip at once; a walker that stops or sets off crossfades into the other.
            bool fades = walker.Played is not null;
            float speed = walker.Wanted == walker.Figure.Walk ? walker.Figure.WalkRate : 1F;
            Engine.SetPlayback(new(walker.Instance, AnimationPlaybackKind.Play, walker.Wanted, AnimationLoopMode.Repeat, speed, 1F,
                !fades, fades ? walker.Figure.CrossfadeSeconds : 0F, fades, 0F));
            walker.Played = walker.Wanted;
        }
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

    private sealed class Walker(SplatFigure figure, AnimationInstance instance)
    {
        internal SplatFigure Figure { get; set; } = figure;
        internal AnimationInstance Instance { get; set; } = instance;
        /// <summary>The clip the latest snapshot asked for, and the one played; null before the first play.</summary>
        internal string Wanted { get; set; } = string.Empty;
        internal string? Played { get; set; }
    }
}
