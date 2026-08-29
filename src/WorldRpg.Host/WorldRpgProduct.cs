using Rusty.Engine;
using WorldRpg.Kit;

namespace WorldRpg.Host;

/// <summary>Reference host lifecycle and explicit built-in ruleset selection.</summary>
public sealed class WorldRpgProduct : IEngineProduct
{
    private readonly IGameSession _session;
    private bool _started;
    private bool _paused;
    private bool _shutdown;

    public WorldRpgProduct(ProductCreateContext context)
        : this(context, BuiltInRulesets.Resolve(HostDefaults.DefaultRuleset))
    {
    }

    /// <summary>Creates a product from an explicitly supplied compiled ruleset.</summary>
    public WorldRpgProduct(ProductCreateContext context, IGameRuleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(ruleset);
        _session = ruleset.CreateSession(new GameSessionContext(context.Engine, context.Content));
        try { _session.PublishInitial(); }
        catch
        {
            _session.Dispose();
            throw;
        }
    }

    public void Start()
    {
        if (_shutdown) return;
        _started = true;
        _paused = false;
        _session.PublishInitial();
    }

    public void Pause() { if (_started && !_shutdown) _paused = true; }
    public void Resume() { if (_started && !_shutdown) _paused = false; }

    public void Restart()
    {
        if (_shutdown) return;
        _paused = false;
        _started = true;
        _session.PublishInitial();
    }

    public void Shutdown()
    {
        if (_shutdown) return;
        _session.Dispose();
        _shutdown = true;
    }

    public void Dispose() => Shutdown();

    public ProductTurnRequest Update(ProductUpdate update)
    {
        if (!_started || _paused || _shutdown) return ProductTurnRequest.None;
        return _session.Update(update);
    }
}
