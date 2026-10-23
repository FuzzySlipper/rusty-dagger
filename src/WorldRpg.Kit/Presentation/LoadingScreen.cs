namespace WorldRpg.Kit.Presentation;

/// <summary>One labelled step of slow work, run whole inside one admitted update.</summary>
/// <param name="Label">What the player reads while the step runs, such as "Reading Daggerfall".</param>
/// <param name="Run">The work itself.</param>
public sealed record LoadingPhase(string Label, Action Run);

/// <summary>
/// What a loading screen shows: the work's title, the phase now running and how far the work has come.
/// </summary>
/// <param name="Title">The work as a whole, such as "Travelling to Daggerfall".</param>
/// <param name="Label">The phase now running.</param>
/// <param name="Step">Phases already finished.</param>
/// <param name="Steps">Phases in all.</param>
/// <param name="Fraction">
/// The share of phases finished, or null when the work is a single phase and nothing is measured, so the
/// screen shows a moving bar rather than a made-up percentage.
/// </param>
public sealed record LoadingState(string Title, string Label, int Step, int Steps, double? Fraction);

/// <summary>
/// Slow work acknowledged at once and run one phase per admitted update. Starting the work publishes the
/// loading state and runs nothing, so the update that accepted the request reaches the page before the first
/// slow phase blocks a later one; each later update the host gives it runs one phase and publishes the next.
/// After the last phase the screen stays up for one more update, so the first frame of what was loaded is
/// drawn before play shows. No thread, timer or second loop is involved: the host's own admitted updates
/// drive it, and while it runs the host gives them to nothing else.
/// </summary>
/// <remarks>
/// The host owns the one screen across session replacement, and a session given it stages its own slow work on
/// it. The state travels in the session's own projection (a product UI reads one stream), as the
/// <c>loading</c> field <see cref="Write"/> builds: each change tells the host, which has the current session
/// publish again. A phase that throws ends the work: the screen clears and the work's failure handler reports it.
/// </remarks>
public sealed class LoadingScreen
{
    private readonly Action _changed;
    private string? _title;
    private IReadOnlyList<LoadingPhase> _phases = [];
    private Action<Exception>? _failed;
    private int _next;

    /// <param name="changed">Told after every change of <see cref="Current"/>, so the presentation carrying it publishes it.</param>
    public LoadingScreen(Action changed)
    {
        _changed = changed ?? throw new ArgumentNullException(nameof(changed));
    }

    /// <summary>What the screen shows now, or null when nothing is loading.</summary>
    public LoadingState? Current { get; private set; }

    /// <summary>Whether work is staged; the host gives its updates to <see cref="Advance"/> until it is not.</summary>
    public bool IsLoading => _title is not null;

    /// <summary>
    /// Accepts slow work: publishes its first phase and runs nothing. Refused (false) while other work is
    /// still loading, because one screen shows one piece of work.
    /// </summary>
    public bool Start(string title, IReadOnlyList<LoadingPhase> phases, Action<Exception> failed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(phases);
        ArgumentNullException.ThrowIfNull(failed);
        if (phases.Count == 0) throw new ArgumentException("Loading work needs at least one phase.", nameof(phases));
        if (IsLoading) return false;
        (_title, _phases, _failed, _next) = (title, [.. phases], failed, 0);
        Show(phases[0].Label);
        return true;
    }

    /// <summary>
    /// Gives this admitted update to the staged work: runs its next phase and publishes what follows, or, once
    /// every phase has run, clears the screen. True when the update was spent on loading; false when nothing was
    /// staged or the screen just cleared, so the caller runs the update as usual.
    /// </summary>
    public bool Advance()
    {
        if (!IsLoading) return false;
        if (_next == _phases.Count)
        {
            Clear();
            return false;
        }
        LoadingPhase phase = _phases[_next];
        try
        {
            phase.Run();
        }
        catch (Exception error)
        {
            Action<Exception> failed = _failed!;
            Clear();
            failed(error);
            return true;
        }
        _next++;
        // The finished work keeps its last label at a full bar for the update that draws what it loaded.
        Show(_next < _phases.Count ? _phases[_next].Label : phase.Label);
        return true;
    }

    /// <summary>
    /// Runs work at once, phase after phase, for a caller with no update to acknowledge in: a direct product
    /// API or a developer command. A phase that throws stops the rest and reaches the caller.
    /// </summary>
    public static void RunNow(IReadOnlyList<LoadingPhase> phases)
    {
        ArgumentNullException.ThrowIfNull(phases);
        foreach (LoadingPhase phase in phases) phase.Run();
    }

    private void Show(string label)
    {
        int steps = _phases.Count;
        Current = new(_title!, label, _next, steps, steps > 1 ? (double)_next / steps : null);
        _changed();
    }

    private void Clear()
    {
        (_title, _phases, _failed, _next) = (null, [], null, 0);
        Current = null;
        _changed();
    }

    /// <summary>The <c>loading</c> value a projection carries: the state's fields, or null when nothing is loading.</summary>
    public uint Write(UiValueBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return Current is not { } state ? builder.Null() : builder.Object(
            ("title", builder.String(state.Title)),
            ("label", builder.String(state.Label)),
            ("step", builder.Number(state.Step)),
            ("steps", builder.Number(state.Steps)),
            ("fraction", state.Fraction is { } fraction ? builder.Number(fraction) : builder.Null()));
    }
}
