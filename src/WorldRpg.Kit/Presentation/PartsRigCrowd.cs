using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit.Ai;

namespace WorldRpg.Kit.Presentation;

/// <summary>
/// One view of a directional parts-rig figure: its model document in product content, the bearing of a camera that sees
/// it (radians about the up axis from the object's +z toward its +x, so 0 is a camera in front), and whether it is
/// drawn mirrored.
/// </summary>
public sealed record PartsRigFigureView(string Model, float Heading, bool Mirror)
{
    /// <summary>
    /// The view seen by a camera <paramref name="degrees"/> around the figure: 0 in front, 90 at the figure's left (its
    /// +x, as it faces +z), 180 behind and 270 at its right.
    /// </summary>
    public static PartsRigFigureView AtCamera(string model, float degrees, bool mirror)
    {
        float heading = float.DegreesToRadians(degrees) % MathF.Tau;
        return new(model, heading > MathF.PI ? heading - MathF.Tau : heading <= -MathF.PI ? heading + MathF.Tau : heading, mirror);
    }
}

/// <summary>
/// A walking figure drawn as one parts rig per view. <paramref name="Height"/> is its standing height in rig units from
/// the ground line (y = 0 at its feet), by which it is drawn at a walker's own height. It walks <paramref name="Walk"/>
/// and stands <paramref name="Idle"/>, both at their authored speed, crossfading between them; its <paramref name="Layers"/>
/// play over them. Every view holds <paramref name="Clips"/>.
/// </summary>
public sealed record PartsRigFigure(string Name, IReadOnlyList<PartsRigFigureView> Views, float Height, string Walk, string Idle,
    IReadOnlySet<string> Clips, PartsRigLayerStack Layers, float CrossfadeSeconds, TextureFilter Filter, SpriteMaterialDescriptor Material)
{
    /// <summary>The rotation that turns the figure's front (+z) to face an actor's heading (<see cref="Actors.ActorHeading"/>).</summary>
    public static Quaternion Facing(float headingYawRadians) => Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI - headingYawRadians);
}

/// <summary>
/// Draws walkers as one <see cref="PartsRigFigure"/>: each walker shown in a snapshot gets an appearance of the figure at
/// its own height, walks while its wander state moves and stands otherwise. Rigs load once for every figure that uses
/// them and are released when nothing shows them; an appearance a snapshot stopped drawing is released at the next
/// settle after that, when no reader of the scene still holds it.
/// </summary>
public sealed class PartsRigCrowd(Func<IPartsRigsService> engine) : IDisposable
{
    private readonly Dictionary<(string Model, TextureFilter Filter), PartsRig> rigs = [];
    private readonly Dictionary<ulong, Walker> shown = [];
    private readonly HashSet<ulong> seen = [];
    private List<Walker> stopped = [], released = [];
    private bool disposed;

    /// <summary>The figure walkers are drawn as, or null while they show their own art.</summary>
    public PartsRigFigure? Figure { get; private set; }

    /// <summary>How many walkers the last settled snapshot drew as the figure.</summary>
    public int Count => shown.Count;

    /// <summary>Whether a walker in <paramref name="state"/> walks: while it seeks or moves to a waypoint.</summary>
    public static bool Walks(WanderState state) => state is WanderState.Moving or WanderState.Seeking;

    /// <summary>
    /// Draws walkers as <paramref name="figure"/> from the next snapshot, or (null) as their own art again. The figure's
    /// rigs load now, so a figure whose rigs cannot load is refused here and the crowd keeps what it drew.
    /// </summary>
    public void Use(PartsRigFigure? figure)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        foreach (PartsRigFigureView view in figure?.Views ?? []) Rig(view, figure!.Filter);
        Figure = figure;
    }

    /// <summary>
    /// The appearance object <paramref name="id"/> shows in the snapshot being built, drawn
    /// <paramref name="heightMetres"/> tall and walking or standing; null while no figure is used.
    /// </summary>
    public Appearance? Show(ulong id, float heightMetres, bool walking)
    {
        if (disposed || Figure is not { } figure) return null;
        if (!float.IsFinite(heightMetres) || heightMetres <= 0F) throw new ArgumentOutOfRangeException(nameof(heightMetres));
        seen.Add(id);
        if (!shown.TryGetValue(id, out Walker? walker) || walker.Figure != figure || walker.Height != heightMetres)
        {
            if (walker is not null) stopped.Add(walker);
            PartsRigView[] views = [.. figure.Views.Select(view => new PartsRigView(Rig(view, figure.Filter), view.Heading, view.Mirror))];
            walker = new(figure, heightMetres, Engine.CreateAppearance(new PartsRigAppearanceRequest(views, heightMetres / figure.Height,
                SpriteSizeMode.World, BillboardMode.Cylindrical, 0, SpriteDepthPolicy.Default, new Color(1F, 1F, 1F, 1F), figure.Material, 0F)));
            shown[id] = walker;
        }
        walker.Wanted = walking ? figure.Walk : figure.Idle;
        return walker.Appearance;
    }

    /// <summary>
    /// Once the snapshot <see cref="Show"/> built has been accepted: plays each shown walker's clip where it changed (a
    /// new appearance starts its standing layers too), stops drawing walkers that snapshot left out, and releases the
    /// appearances the snapshot before it stopped drawing and the rigs nothing shows.
    /// </summary>
    public void Settle()
    {
        if (disposed) return;
        List<Exception>? failures = null;
        foreach (Walker walker in released) Release(walker.Appearance, ref failures);
        released = stopped;
        stopped = [];
        foreach (ulong id in shown.Keys.Where(id => !seen.Contains(id)).ToArray())
        {
            released.Add(shown[id]);
            shown.Remove(id);
        }
        seen.Clear();
        foreach ((ulong id, Walker walker) in shown)
        {
            if (walker.Played == walker.Wanted) continue;
            walker.Figure.Layers.Play(Engine, id, walker.Wanted, walker.Played, walker.Figure.Clips, walker.Figure.CrossfadeSeconds);
            walker.Played = walker.Wanted;
        }
        HashSet<(string, TextureFilter)> needed = [.. new[] { Figure }.Concat(shown.Values.Concat(released).Select(walker => walker.Figure))
            .OfType<PartsRigFigure>().SelectMany(figure => figure.Views.Select(view => (view.Model, figure.Filter)))];
        foreach ((string, TextureFilter) key in rigs.Keys.Where(key => !needed.Contains(key)).ToArray())
        {
            Release(rigs[key], ref failures);
            rigs.Remove(key);
        }
        if (failures is { Count: > 0 }) throw new AggregateException(failures);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        List<Exception>? failures = null;
        foreach (Walker walker in released.Concat(stopped).Concat(shown.Values)) Release(walker.Appearance, ref failures);
        foreach (PartsRig rig in rigs.Values) Release(rig, ref failures);
        (released, stopped) = ([], []);
        shown.Clear();
        rigs.Clear();
        if (failures is { Count: > 0 }) throw new AggregateException(failures);
    }

    private IPartsRigsService Engine => engine();

    private PartsRig Rig(PartsRigFigureView view, TextureFilter filter)
    {
        if (!rigs.TryGetValue((view.Model, filter), out PartsRig? rig))
            rigs.Add((view.Model, filter), rig = Engine.Load(new PartsRigLoadRequest(view.Model, filter)).Rig);
        return rig;
    }

    private static void Release(IDisposable value, ref List<Exception>? failures)
    {
        try { value.Dispose(); }
        catch (Exception exception) { (failures ??= []).Add(exception); }
    }

    private sealed class Walker(PartsRigFigure figure, float height, Appearance appearance)
    {
        internal PartsRigFigure Figure { get; } = figure;
        internal float Height { get; } = height;
        internal Appearance Appearance { get; } = appearance;
        /// <summary>The body clip the latest snapshot asked for, and the one played; null before the first play.</summary>
        internal string Wanted { get; set; } = string.Empty;
        internal string? Played { get; set; }
    }
}
