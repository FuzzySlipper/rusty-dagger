using System.Text;
using Rusty.Engine;

namespace RustyDagger.Game;

/// <summary>Owns Dagger lifecycle and persistent game state.</summary>
public sealed class DaggerGame : IEngineProduct
{
    private readonly IEngineContext _engine;
    private readonly GameplayInput _input = new();
    private readonly SpatialGameplayService _spatial;
    private readonly DaggerPresentation _presentation;
    private bool _started;
    private bool _paused;
    private bool _shutdown;
    private ulong? _lastObservedNanoseconds;

    public DaggerGame(ProductCreateContext context)
    {
        _engine = context.Engine;
        var content = PrivateersHoldContent.Read(context.Content);
        State = DaggerGameState.CreatePrivateersHold(content.Project);
        _spatial = new SpatialGameplayService(_engine, content);
        _presentation = new DaggerPresentation(_engine, content);
    }

    public DaggerGameState State { get; }

    public void Start()
    {
        if (_shutdown) return;
        _started = true;
        _paused = false;
        _presentation.Publish(State);
    }

    public void Pause()
    {
        if (!_started || _shutdown) return;
        _paused = true;
    }

    public void Resume()
    {
        if (!_started || _shutdown) return;
        _paused = false;
    }

    public void Shutdown()
    {
        if (_shutdown) return;
        _spatial.Dispose();
        _shutdown = true;
    }

    public void Update(ProductUpdate update)
    {
        if (!_started || _paused || _shutdown || update.Kind is < 1 or > 3) return;

        var gameplayUpdate = ReadUpdate(update);
        State.AdvanceTime(gameplayUpdate.DeltaSeconds);
        _input.Apply(State, gameplayUpdate, _engine);
        _spatial.Step(State, gameplayUpdate);
        if (gameplayUpdate.AttackRequested) CombatService.TryMelee(State, _engine.Random);
        EnemyCombatService.TryActiveEncounterAttack(State, _engine.Random);
        State.Updates++;
        _presentation.Publish(State);
    }

    public void Dispose() => Shutdown();

    private GameplayUpdate ReadUpdate(ProductUpdate update)
    {
        var gameplayUpdate = new GameplayUpdate(TimeDelta(update.Kind, update.Observation));
        foreach (var input in update.Input)
        {
            var label = input.Label.IsEmpty
                ? string.Empty
                : Encoding.UTF8.GetString(input.Label.Span);
            gameplayUpdate.Add(new ProductInput(input.Kind, input.Edge, input.X, input.Y, label));
        }
        return gameplayUpdate;
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
