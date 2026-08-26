using System.Text;
using Rusty.Engine.Native;

namespace RustyDagger.Product;

/// <summary>Owns Dagger lifecycle and persistent game state.</summary>
public sealed unsafe class DaggerRuntime
{
    private readonly GameplayInput _input = new();
    private bool _started;
    private bool _paused;
    private bool _shutdown;
    private ulong? _lastObservedNanoseconds;

    public DaggerRuntime(DaggerGameState state)
    {
        State = state;
    }

    public DaggerGameState State { get; }

    public int Start()
    {
        if (_shutdown) return 4;
        _started = true;
        _paused = false;
        return 1;
    }

    public int Pause()
    {
        if (!_started || _shutdown) return 4;
        _paused = true;
        return 1;
    }

    public int Resume()
    {
        if (!_started || _shutdown) return 4;
        _paused = false;
        return 1;
    }

    public int Shutdown()
    {
        _shutdown = true;
        return 1;
    }

    public int Turn(NativeTurnArgs* args)
    {
        if (!_started || _paused || _shutdown) return 4;
        if (args is null || args->kind is < 1 or > 3 || (args->event_count != 0 && args->events is null)) return 5;

        var turn = ReadTurn(args);
        State.AdvanceTime(turn.DeltaSeconds);
        _input.Apply(State.Player, turn);
        if (turn.AttackRequested) CombatService.TryMelee(State);
        State.Turns++;
        return 1;
    }

    private GameplayTurn ReadTurn(NativeTurnArgs* args)
    {
        var turn = new GameplayTurn(TimeDelta(args->kind, args->observed_time_or_step));
        for (nuint index = 0; index < args->event_count; index++)
        {
            var input = args->events[index];
            if (input.label_len != 0 && input.label is null) throw new ArgumentException("input label pointer was null");
            var label = input.label_len == 0
                ? string.Empty
                : Encoding.UTF8.GetString(new ReadOnlySpan<byte>(input.label, checked((int)input.label_len)));
            turn.Add(new ProductInput(input.kind, input.edge, input.x, input.y, label));
        }
        return turn;
    }

    private float TimeDelta(uint kind, ulong observedTimeOrStep)
    {
        if (kind != 1) return 1f / 60f;
        if (_lastObservedNanoseconds is not ulong previous || observedTimeOrStep <= previous)
        {
            _lastObservedNanoseconds = observedTimeOrStep;
            return 1f / 60f;
        }
        _lastObservedNanoseconds = observedTimeOrStep;
        return Math.Clamp((observedTimeOrStep - previous) / 1_000_000_000f, .001f, .08f);
    }

}
