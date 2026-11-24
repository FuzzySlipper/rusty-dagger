using WorldRpg.Kit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The compiled Daggerfall ruleset as the Host tests run it: its sessions are the real ones, untouched, so the Host
/// sees every capability the game's session has. A test reads the session the Host runs from the product
/// (<see cref="TestSessions.RunningSession"/>), since a new game is made by the title's session, not here.
/// </summary>
internal sealed class TestDaggerfallRuleset : ISaveableGameRuleset, IInstalledContentRuleset
{
    // Ordinary integration tests exercise gameplay and persistence, not the Engine video runtime.
    // They name disabled videos explicitly so a missing fake Video service cannot masquerade as a
    // successful production-media path.
    private readonly DaggerfallRuleset _inner;
    internal TestDaggerfallRuleset(bool videosEnabled = false, ulong? gameSeed = null) => _inner = new(videosEnabled, gameSeed);
    public RulesetId Id => _inner.Id;

    /// <summary>The containers the real ruleset reads, which the Host opens for these tests as it does for the product.</summary>
    public IReadOnlyList<InstalledContentRequirement> InstalledContent => _inner.InstalledContent;

    /// <summary>
    /// Every container counts as current here: these facts exercise gameplay over whatever import the checkout
    /// holds, and an importer edit since that import is the Host's stale-data case, which its own tests cover.
    /// </summary>
    public string? CheckInstalledContent(InstalledContent content) => null;

    public IGameSession CreateUnavailableSession(GameUnavailableContext context) => _inner.CreateUnavailableSession(context);
    public IGameSession CreateSession(GameSessionContext context) => _inner.CreateSession(context);
    public IGameSession CreateSession(GameSessionContext context, RulesetSavePayload saved) => _inner.CreateSession(context, saved);
}
