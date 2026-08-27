using System.Text;
using Rusty.Engine;
using RustyDagger.Game.Daggerfall;
using RustyDagger.Game.Modules.PlayerControl;

namespace RustyDagger.Game;

/// <summary>Thin Engine lifecycle and admitted-update entrypoint for the Daggerfall composition.</summary>
public sealed class DaggerGame : IEngineProduct
{
    private readonly DaggerfallComposition _daggerfall;
    private bool _started;
    private bool _paused;
    private bool _shutdown;
    private ulong? _lastObservedNanoseconds;

    public DaggerGame(ProductCreateContext context) => _daggerfall = new DaggerfallComposition(context.Engine, context.Content);

    public void Start()
    {
        if (_shutdown) return;
        _started = true;
        _paused = false;
        _daggerfall.PublishInitial();
    }

    public void Pause() { if (_started && !_shutdown) _paused = true; }
    public void Resume() { if (_started && !_shutdown) _paused = false; }

    public void Shutdown()
    {
        if (_shutdown) return;
        _daggerfall.Dispose();
        _shutdown = true;
    }

    public void Dispose() => Shutdown();

    public void Update(ProductUpdate update)
    {
        if (!_started || _paused || _shutdown || update.Kind is < 1 or > 3) return;
        _daggerfall.Update(ReadUpdate(update));
    }

    private ProductUpdateState ReadUpdate(ProductUpdate update)
    {
        ProductUpdateState productUpdate = new(TimeDelta(update.Kind, update.Observation));
        foreach (var input in update.Input)
        {
            string label = input.Label.IsEmpty ? string.Empty : Encoding.UTF8.GetString(input.Label.Span);
            productUpdate.Add(new ProductInput(input.Kind, input.Edge, input.X, input.Y, label));
        }
        return productUpdate;
    }

    private float TimeDelta(uint kind, ulong observedTimeOrStep)
    {
        const float FallbackSeconds = 1f / 60f;
        const float MinimumSeconds = .001f;
        const float MaximumSeconds = .08f;
        if (kind != 1) return FallbackSeconds;
        if (_lastObservedNanoseconds is not ulong previous || observedTimeOrStep <= previous)
        {
            _lastObservedNanoseconds = observedTimeOrStep;
            return FallbackSeconds;
        }
        _lastObservedNanoseconds = observedTimeOrStep;
        return Math.Clamp((observedTimeOrStep - previous) / 1_000_000_000f, MinimumSeconds, MaximumSeconds);
    }
}
