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
        : this(CreateSession(context, ruleset: null, HostDefaults.DefaultBundle))
    {
    }

    /// <summary>Creates a product through the same selected-bundle seam with an explicit compiled ruleset.</summary>
    public WorldRpgProduct(ProductCreateContext context, IGameRuleset ruleset, GameBundleId bundle)
        : this(CreateSession(context, ruleset, bundle))
    {
    }

    private WorldRpgProduct(IGameSession session)
    {
        _session = session;
        try { _session.PublishInitial(); }
        catch
        {
            _session.Dispose();
            throw;
        }
    }

    private static IGameSession CreateSession(ProductCreateContext context, IGameRuleset? ruleset, GameBundleId bundle)
    {
        ArgumentNullException.ThrowIfNull(context);
        ResolvedGameComposition composition = GameCompositionResolver.Resolve(context.Content, bundle).RequireComposition();
        IGameRuleset selected = ruleset ?? BuiltInRulesets.Resolve(composition.Ruleset);
        if (selected.Id != composition.Ruleset)
            throw new InvalidOperationException($"Selected ruleset '{selected.Id.Value}' does not match bundle ruleset '{composition.Ruleset.Value}'.");
        return selected.CreateSession(new GameSessionContext(context.Engine, composition));
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
