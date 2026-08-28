using Rusty.Engine;
using RustyDagger.Game.Daggerfall;

namespace RustyDagger.Game;

/// <summary>Thin Engine lifecycle and admitted-update entrypoint for the Daggerfall composition.</summary>
public sealed class DaggerGame : IEngineProduct
{
    private readonly DaggerfallComposition _daggerfall;
    private bool _started;
    private bool _paused;
    private bool _shutdown;

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
        if (!_started || _paused || _shutdown) return;
        _daggerfall.Update(update.Facts, update.Input);
    }
}
