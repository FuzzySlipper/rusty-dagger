using Rusty.Engine;
using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall;

namespace WorldRpg.Host;

/// <summary>Reference host lifecycle and explicit built-in ruleset selection.</summary>
public sealed class WorldRpgProduct : IEngineProduct
{
    private readonly IGameSession _session;
    private bool _started;
    private bool _paused;
    private bool _shutdown;

    public WorldRpgProduct(ProductCreateContext context)
    {
        IGameRuleset ruleset = BuiltInRulesets.Resolve(HostDefaults.DefaultRuleset);
        _session = ruleset.CreateSession(new GameSessionContext(context.Engine, context.Content));
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

    public void Shutdown()
    {
        if (_shutdown) return;
        _session.Dispose();
        _shutdown = true;
    }

    public void Dispose() => Shutdown();

    public void Update(ProductUpdate update)
    {
        if (!_started || _paused || _shutdown) return;
        _session.Update(update);
    }
}

internal static class HostDefaults
{
    internal static readonly RulesetId DefaultRuleset = DaggerfallRuleset.Identity;
}

internal static class BuiltInRulesets
{
    internal static IGameRuleset Resolve(RulesetId id)
    {
        if (id == DaggerfallRuleset.Identity) return new DaggerfallRuleset();
        throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown built-in ruleset.");
    }
}
