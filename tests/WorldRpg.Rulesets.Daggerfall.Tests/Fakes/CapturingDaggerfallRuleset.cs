using Rusty.Engine;
using WorldRpg.Kit;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>Delegates every session to the compiled Daggerfall ruleset while exposing the real session a test created.</summary>
internal sealed class CapturingDaggerfallRuleset : ISaveableGameRuleset, IRulesVariantCatalog, IInstalledContentRuleset
{
    // Ordinary integration tests exercise gameplay and persistence, not the Engine video runtime.
    // They name disabled videos explicitly so a missing fake Video service cannot masquerade as a
    // successful production-media path.
    private readonly DaggerfallRuleset _inner;
    internal CapturingDaggerfallRuleset(bool videosEnabled = false, IReadOnlyList<DaggerfallRulesVariant>? variants = null) =>
        _inner = new(variants ?? [], videosEnabled);
    public IReadOnlyList<RulesVariantManifest> CompiledVariants => _inner.CompiledVariants;
    internal DaggerfallSession? Session { get; private set; }
    public RulesetId Id => _inner.Id;

    /// <summary>The containers the real ruleset reads, which the Host opens for these tests as it does for the product.</summary>
    public IReadOnlyList<InstalledContentRequirement> InstalledContent => _inner.InstalledContent;

    /// <summary>
    /// Every container counts as current here: these facts exercise gameplay over whatever import the checkout
    /// holds, and an importer edit since that import is the Host's stale-data case, which its own tests cover.
    /// </summary>
    public string? CheckInstalledContent(InstalledContent content) => null;

    public IGameSession CreateUnavailableSession(GameUnavailableContext context) => _inner.CreateUnavailableSession(context);

    public IGameSession CreateSession(GameSessionContext context) => Capture(_inner.CreateSession(context));
    public IGameSession CreateSession(GameSessionContext context, RulesetSavePayload saved) => Capture(_inner.CreateSession(context, saved));

    internal DaggerfallSession RequireSession() => Session ?? throw new InvalidOperationException("The delegated Daggerfall ruleset did not create a session.");
    private IGameSession Capture(IGameSession session)
    {
        Session = Assert.IsType<DaggerfallSession>(session);
        return new CapturingSession(Session, this);
    }
    private sealed class CapturingSession(DaggerfallSession inner, CapturingDaggerfallRuleset owner)
        : IGameSession, ICharacterCreationSession, IEntryScreenSession, IEntryScreenStartupSession,
        IModeAwareGameSession, ISaveableGameSession, ISaveRequestingGameSession, IPlayerPreferencesSession,
        IPlayerDefeatOutcomeSession, IPlaytestGameSession, IGameBundleChoiceSession
    {
        public bool RequiresCharacterInitialization => inner.RequiresCharacterInitialization;
        public bool HasCommittedCharacter => inner.HasCommittedCharacter;
        public void OpenCharacterCreation() => inner.OpenCharacterCreation();
        public IGameSession CreateNewGame() => owner.Capture(inner.CreateNewGame());
        public void PublishInitial() => inner.PublishInitial();
        public ProductUpdateResult Update(ProductUpdate update) => inner.Update(update);
        public void Dispose() => inner.Dispose();
        public bool RequestsBegin(ReadOnlySpan<Rusty.Engine.ProductInputEvent> input) => inner.RequestsBegin(input);
        public EntryScreenStartupResult StartEntry() => inner.StartEntry();
        public bool TakeEntryReadyForPlay() => inner.TakeEntryReadyForPlay();
        public void ApplyProductMode(ProductMode mode) => inner.ApplyProductMode(mode);
        public ProductMode? PendingModeRequest => inner.PendingModeRequest;
        public bool PendingModeRequestClosesModal => inner.PendingModeRequestClosesModal;
        public RulesetSavePayload CaptureSave() => inner.CaptureSave();
        public SaveSlotRequest? TakeSaveSlotRequest() => inner.TakeSaveSlotRequest();
        public void ReportSaveOutcome(string message) => inner.ReportSaveOutcome(message);
        public void ReportSaveSlots(IReadOnlyList<SaveSlotSummary> slots, string? diagnostic) => inner.ReportSaveSlots(slots, diagnostic);
        public string CapturePlayerPreferences() => inner.CapturePlayerPreferences();
        public void ApplyPlayerPreferences(string? serialized) => inner.ApplyPlayerPreferences(serialized);
        public string? TakePlayerPreferencesSave() => inner.TakePlayerPreferencesSave();
        public void ReportPlayerPreferencesOutcome(string message) => inner.ReportPlayerPreferencesOutcome(message);
        public PlayerDefeatOutcomeRequest? TakePlayerDefeatOutcomeRequest() => inner.TakePlayerDefeatOutcomeRequest();
        public void ReportPlayerDefeatOutcome(string message) => inner.ReportPlayerDefeatOutcome(message);
        public void ReportBundles(GameBundleChoices choices) => inner.ReportBundles(choices);
        public GameBundleId? TakeBundleRequest() => inner.TakeBundleRequest();
        public void ReportBundleOutcome(string message) => inner.ReportBundleOutcome(message);
        public IReadOnlyList<string> PlaytestActions => inner.PlaytestActions;
        public Rusty.Engine.Debugging.DebugCommandResult ReadPlaytestObservation() => inner.ReadPlaytestObservation();
        public Rusty.Engine.Debugging.PlaytestAction InspectPlaytestAction(string id) => inner.InspectPlaytestAction(id);
        public Rusty.Engine.Debugging.DebugCommandResult InspectPlaytestLook(double yaw, double pitch) => inner.InspectPlaytestLook(yaw, pitch);
        public Rusty.Engine.Debugging.DebugCommandResult ReadPlaytestTargets() => inner.ReadPlaytestTargets();
    }

}
