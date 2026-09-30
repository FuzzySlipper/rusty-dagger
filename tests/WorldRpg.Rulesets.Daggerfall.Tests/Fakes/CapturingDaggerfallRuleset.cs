using WorldRpg.Kit;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>Delegates every session to the compiled Daggerfall ruleset while exposing the real session a test created.</summary>
internal sealed class CapturingDaggerfallRuleset : ISaveableGameRuleset
{
    // Ordinary integration tests exercise gameplay and persistence, not the Engine video runtime.
    // They name disabled videos explicitly so a missing fake Video service cannot masquerade as a
    // successful production-media path.
    private readonly DaggerfallRuleset _inner;
    internal CapturingDaggerfallRuleset(bool videosEnabled = false) => _inner = new(videosEnabled);
    internal DaggerfallSession? Session { get; private set; }
    public RulesetId Id => _inner.Id;

    public IGameSession CreateSession(GameSessionContext context) => Capture(_inner.CreateSession(context));
    public IGameSession CreateSession(GameSessionContext context, RulesetSavePayload saved) => Capture(_inner.CreateSession(context, saved));

    internal DaggerfallSession RequireSession() => Session ?? throw new InvalidOperationException("The delegated Daggerfall ruleset did not create a session.");
    private IGameSession Capture(IGameSession session)
    {
        Session = Assert.IsType<DaggerfallSession>(session);
        return session;
    }
}
