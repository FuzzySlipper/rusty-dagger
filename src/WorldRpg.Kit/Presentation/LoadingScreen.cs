namespace WorldRpg.Kit.Presentation;

/// <summary>
/// One labelled step of slow work, run whole inside one admitted update. A step that hands work to something
/// running beside the update (an Engine worker baking collision) also names how to ask after it: the step then
/// stays on the screen, asked once per update, until that work is done.
/// </summary>
/// <param name="Label">What the player reads while the step runs, such as "Reading Daggerfall".</param>
/// <param name="Run">The work itself.</param>
/// <param name="Await">
/// How far the work <paramref name="Run"/> handed off has come, asked straight after it runs and then once per
/// update until it reports done; null when the step is finished once it has run. It may throw to fail the work.
/// </param>
public sealed record LoadingPhase(string Label, Action Run, Func<LoadingProgress>? Await = null);

/// <summary>How far a step's handed-off work has come.</summary>
/// <param name="Done">Whether the work is finished, so the next step may run.</param>
/// <param name="Fraction">
/// The share of the step done, from 0 to 1, or null while nothing measures it (the work is still being
/// prepared), so the screen shows a moving bar.
/// </param>
public readonly record struct LoadingProgress(bool Done, double? Fraction)
{
    /// <summary>The handed-off work is finished.</summary>
    public static LoadingProgress Finished => new(true, 1d);

    /// <summary>The work is under way and this share of it is done.</summary>
    public static LoadingProgress Measured(double fraction) => new(false, Math.Clamp(fraction, 0d, 1d));

    /// <summary>The work is under way and nothing measures it yet.</summary>
    public static LoadingProgress Unmeasured => new(false, null);
}

/// <summary>
/// What a loading screen shows: the work's title, the phase now running and how far the work has come.
/// </summary>
/// <param name="Title">The work as a whole, such as "Travelling to Daggerfall".</param>
/// <param name="Label">The phase now running.</param>
/// <param name="Step">Phases already finished.</param>
/// <param name="Steps">Phases in all.</param>
/// <param name="Fraction">
/// The share of the work finished: the phases finished, plus the measured share of a phase waiting on handed-off
/// work. Null when nothing is measured (a single phase, or a waiting phase whose work is not yet measurable), so
/// the screen shows a moving bar rather than a made-up percentage.
/// </param>
public sealed record LoadingState(string Title, string Label, int Step, int Steps, double? Fraction);

/// <summary>
/// Slow work acknowledged at once and run one phase per admitted update. Starting the work publishes the
/// loading state and runs nothing, so the update that accepted the request reaches the page before the first
/// slow phase blocks a later one; each later update the host gives it runs one phase and publishes the next.
/// A phase that handed work off (<see cref="LoadingPhase.Await"/>) holds the screen, asked once per update and
/// publishing how far it has come, until that work is done.
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
    private bool _waiting;

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
        (_title, _phases, _failed, _next, _waiting) = (title, [.. phases], failed, 0, false);
        Show(phases[0].Label);
        return true;
    }

    /// <summary>
    /// Gives this admitted update to the staged work: runs its next phase, or asks after the work a phase handed
    /// off, and publishes what follows; once every phase has run, clears the screen. True when the update was spent
    /// on loading; false when nothing was staged or the screen just cleared, so the caller runs the update as usual.
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
        LoadingProgress progress;
        try
        {
            if (!_waiting) phase.Run();
            progress = phase.Await?.Invoke() ?? LoadingProgress.Finished;
        }
        catch (Exception error)
        {
            Action<Exception> failed = _failed!;
            Clear();
            failed(error);
            return true;
        }
        if (!progress.Done)
        {
            _waiting = true;
            Show(phase.Label, progress.Fraction);
            return true;
        }
        _waiting = false;
        _next++;
        // The finished work keeps its last label at a full bar for the update that draws what it loaded.
        Show(_next < _phases.Count ? _phases[_next].Label : phase.Label);
        return true;
    }

    /// <summary>
    /// Runs work at once, phase after phase, for a caller with no update to acknowledge in: a direct product
    /// API or a developer command. A phase that throws stops the rest and reaches the caller. No update follows
    /// to ask after handed-off work, so a phase that waits must find its work done straight after it runs.
    /// </summary>
    public static void RunNow(IReadOnlyList<LoadingPhase> phases)
    {
        ArgumentNullException.ThrowIfNull(phases);
        foreach (LoadingPhase phase in phases)
        {
            phase.Run();
            if (phase.Await?.Invoke() is { Done: false })
                throw new InvalidOperationException($"'{phase.Label}' handed off work that needs later updates; stage it on a loading screen.");
        }
    }

    /// <param name="label">The phase to show.</param>
    /// <param name="waiting">The measured share of the current phase's handed-off work; null when it is unmeasured.</param>
    private void Show(string label, double? waiting = null)
    {
        int steps = _phases.Count;
        double? fraction = _waiting
            ? waiting is { } share ? (_next + share) / steps : null
            : steps > 1 ? (double)_next / steps : null;
        Current = new(_title!, label, _next, steps, fraction);
        _changed();
    }

    private void Clear()
    {
        (_title, _phases, _failed, _next, _waiting) = (null, [], null, 0, false);
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
