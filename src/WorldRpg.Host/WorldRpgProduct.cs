using Rusty.Engine;
using Rusty.Engine.Persistence;
using WorldRpg.Kit;

namespace WorldRpg.Host;

/// <summary>Reference host lifecycle and explicit built-in ruleset selection.</summary>
public sealed class WorldRpgProduct : IEngineProduct
{
    /// <summary>How many recent mode decisions are kept for diagnosis.</summary>
    public const int ModeHistoryLimit = 16;

    /// <summary>Engine persistence scope for ordinary menu saves.</summary>
    private const string SaveStoreScope = "worldrpg.saves";

    /// <summary>Engine persistence scope for current ruleset-owned player preferences.</summary>
    private const string PlayerPreferencesScope = "worldrpg.preferences";

    /// <summary>The one Host-owned catalog and payload owner used by ordinary menu save actions.</summary>
    private WorldRpgSaveSlots SaveSlots => _saveSlots ??= new WorldRpgSaveSlots(_context.Engine, SaveStoreScope);

    private readonly ProductCreateContext _context;
    private readonly ResolvedGameComposition _composition;
    private readonly IGameRuleset _ruleset;
    private readonly List<ProductModeChange> _modeHistory = [];
    private WorldRpgSaveSlots? _saveSlots;
    private ProductStateStore<string>? _playerPreferences;
    private IGameSession _session;
    private readonly ResolvedCompositionIdentity _compositionIdentity;
    private bool _started;
    private bool _shutdown;
    private readonly bool _resumed;
    private ProductMode _mode = ProductMode.Playing;

    private ProductStateStore<string> PlayerPreferences => _playerPreferences ??= new ProductStateStore<string>(
        _context.Engine,
        PlayerPreferencesScope,
        new JsonProductStateCodec<string>(WorldRpgSaveJsonContext.Default.String));

    public WorldRpgProduct(ProductCreateContext context)
        : this(context, HostDefaults.DefaultBundle, ruleset: null)
    {
    }

    /// <summary>Creates a product through the same selected-bundle seam with an explicit compiled ruleset.</summary>
    public WorldRpgProduct(ProductCreateContext context, IGameRuleset ruleset, GameBundleId bundle)
        : this(context, bundle, ruleset)
    {
    }

    private WorldRpgProduct(ProductCreateContext context, GameBundleId bundle, IGameRuleset? ruleset)
    {
        ArgumentNullException.ThrowIfNull(context);
        (ResolvedGameComposition composition, IGameRuleset selected) = ResolveSelection(context, ruleset, bundle);
        _context = context;
        _composition = composition;
        _ruleset = selected;
        _compositionIdentity = composition.Identity;
        _session = selected.CreateSession(new GameSessionContext(context.Engine, composition));
        try
        {
            ApplyPlayerPreferences(_session);
            RefreshSaveSlots();
            _session.PublishInitial();
        }
        catch
        {
            _session.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Adopts a session a resume already built and validated, keeping the composition that a
    /// later session replacement rebuilds from.
    /// </summary>
    private WorldRpgProduct(ProductCreateContext context, IGameRuleset ruleset, ResolvedGameComposition composition, IGameSession session)
    {
        _context = context;
        _composition = composition;
        _ruleset = ruleset;
        _compositionIdentity = composition.Identity;
        _session = session;
        ApplyPlayerPreferences(_session);
        // A resumed product is already past the entry screen: the world has been played, so starting it
        // again behind a screen that offers to begin would offer to begin a run that is already running.
        _resumed = true;
    }

    /// <summary>The mode the product runs its session under.</summary>
    public ProductMode Mode => _mode;

    /// <summary>
    /// Recent mode decisions, oldest first and capped at <see cref="ModeHistoryLimit"/>. The
    /// lifecycle methods the Engine calls return nothing, so a caller that needs to know why a
    /// transition did nothing reads the decision here.
    /// </summary>
    public IReadOnlyList<ProductModeChange> ModeHistory => _modeHistory;

    /// <summary>Captures this compiled ruleset state into an Engine-persisted envelope.</summary>
    public PersistenceSaveReceipt Save(WorldRpgSaveStore store, string key, PersistenceRevisionGuard guard = PersistenceRevisionGuard.Any, ulong expectedRevision = 0)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (_shutdown) throw new ObjectDisposedException(nameof(WorldRpgProduct));
        if (_session is not ISaveableGameSession saveable)
            throw new InvalidOperationException("The selected compiled ruleset does not support save capture.");
        return store.Save(key, new GameSaveEnvelope(saveable.CaptureSave()), guard, expectedRevision);
    }

    /// <summary>Loads and admits a save before any ruleset session is constructed or Engine state is mutated.</summary>
    public static WorldRpgResumeResult TryResume(ProductCreateContext context, WorldRpgSaveStore store, string key, IGameRuleset? ruleset = null, GameBundleId? bundle = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(store);
        ProductStateLoad<GameSaveEnvelope> loaded;
        try
        {
            loaded = store.Load(key);
        }
        catch (WorldRpgSaveFormatException error)
        {
            return new(null, 0, [new("corrupt", error.Message)]);
        }
        catch (OverflowException)
        {
            return new(null, 0, [new("corrupt", "The persisted WorldRpg save payload length is invalid.")]);
        }
        if (!loaded.Present || loaded.State is null) return new(null, loaded.Revision, [new("missing", "No saved state exists for the requested key.")]);
        GameBundleId requestedBundle = bundle ?? HostDefaults.DefaultBundle;
        GameCompositionResolution resolution = GameCompositionResolver.Resolve(context.Content, requestedBundle);
        if (!resolution.IsResolved)
        {
            return new(null, loaded.Revision, resolution.Diagnostics
                .Select(value => new WorldRpgSaveDiagnostic("selection", value.Message)).ToArray());
        }
        ResolvedGameComposition composition = resolution.RequireComposition();
        IGameRuleset selected;
        try
        {
            selected = ruleset ?? BuiltInRulesets.Resolve(composition.Ruleset);
        }
        catch (ArgumentOutOfRangeException error) when (ruleset is null && error.ParamName == "id")
        {
            return new(null, loaded.Revision, [new("selection", $"No built-in compiled ruleset is available for '{composition.Ruleset.Value}'.")]);
        }
        if (selected.Id != composition.Ruleset)
        {
            return new(null, loaded.Revision, [new("selection", $"Selected ruleset '{selected.Id.Value}' does not match bundle ruleset '{composition.Ruleset.Value}'.")]);
        }
        if (loaded.State.Payload.Ruleset != composition.Ruleset)
            return new(null, loaded.Revision, [new("payload-ruleset", "The saved payload ruleset does not match the selected bundle ruleset.")]);
        if (selected is not ISaveableGameRuleset saveable)
            return new(null, loaded.Revision, [new("unsupported", "The selected compiled ruleset does not support save resume.")]);
        try
        {
            IGameSession session = saveable.CreateSession(new GameSessionContext(context.Engine, composition), loaded.State.Payload);
            return new(
                new WorldRpgProduct(context, selected, composition, session),
                loaded.Revision,
                []);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            return new(null, loaded.Revision, [new("payload", $"The validated save payload was rejected: {error.Message}")]);
        }
    }

    private static (ResolvedGameComposition Composition, IGameRuleset Selected) ResolveSelection(ProductCreateContext context, IGameRuleset? ruleset, GameBundleId bundle)
    {
        ResolvedGameComposition composition = GameCompositionResolver.Resolve(context.Content, bundle).RequireComposition();
        IGameRuleset selected = ruleset ?? BuiltInRulesets.Resolve(composition.Ruleset);
        if (selected.Id != composition.Ruleset)
            throw new InvalidOperationException($"Selected ruleset '{selected.Id.Value}' does not match bundle ruleset '{composition.Ruleset.Value}'.");
        return (composition, selected);
    }

    /// <summary>
    /// Starts the product at its entry screen, which is the mode a run begins in.
    /// </summary>
    /// <remarks>
    /// The world does not start here: the session holds input and time while the entry screen is up, so
    /// this tells it which mode it is starting in and exposes that as a projection. The mode change is the
    /// publication - telling the session and then asking it to publish again would expose two UI
    /// projections for one Start, which is one more than the Engine's own product exercise holds a product
    /// to. A resumed product is already in ordinary play, so its mode change is the already-in-mode case
    /// and publishes nothing, leaving the create-time projection as the one a client attaches to.
    /// <para>
    /// The Engine calls this once through the generated product exports, so the entry screen is what a
    /// client sees before anything has happened in the world.
    /// </para>
    /// </remarks>
    public void Start()
    {
        Guarded(StartCore);
    }

    private void StartCore()
    {
        if (_shutdown || _started) return;
        _started = true;
        Apply(_resumed ? ProductMode.Playing : ProductMode.Title,
            _resumed ? "the resumed product started in the world it restored" : "the product started at its entry screen");
    }

    /// <summary>
    /// Leaves the entry screen for ordinary play, which is the product's own decision to make.
    /// </summary>
    /// <remarks>
    /// A client asks by sending the semantic action the Engine delivers, which <see cref="Update"/>
    /// answers; this is the same transition for a caller that already holds the product, such as the
    /// launcher or a test. Asking while the product is not at its entry screen is refused and recorded
    /// rather than silently ignored.
    /// </remarks>
    public ProductModeChange Begin()
    {
        if (_mode != ProductMode.Title)
            return Apply(ProductMode.Playing, "the entry screen asked for ordinary play", closesEntryScreen: true);

        if (_session is not IEntryScreenStartupSession startup)
            return Apply(ProductMode.Playing, "the entry screen asked for ordinary play", closesEntryScreen: true);

        return startup.StartEntry() switch
        {
            EntryScreenStartupResult.ReadyForPlay => Apply(ProductMode.Playing, "the entry screen completed its startup", closesEntryScreen: true),
            EntryScreenStartupResult.Waiting => Record(new(_mode, _mode, ProductModeChangeOutcome.AlreadyInMode, "the entry screen started its opening sequence")),
            EntryScreenStartupResult.Failed => Record(new(_mode, _mode, ProductModeChangeOutcome.Refused, "the entry screen could not start its opening sequence")),
            _ => throw new InvalidOperationException("The entry screen returned an unknown startup result."),
        };
    }

    /// <summary>Republishes the current session projection when the Engine attaches a new presentation client.</summary>
    public void Attach()
    {
        Guarded(AttachCore);
    }

    private void AttachCore()
    {
        if (_shutdown) return;
        RefreshSaveSlots();
        _session.PublishInitial();
    }

    /// <summary>Pauses ordinary play. A modal that owns input is cancelled by the pause.</summary>
    public void Pause() => Guarded(() => Apply(ProductMode.Paused, "the host paused the product"));

    private void Guarded(Action body)
    {
        try { body(); }
        catch (Exception failure) { PublishCallbackFailure(failure); throw; }
    }

    /// <summary>Resumes ordinary play from a pause.</summary>
    public void Resume() => Guarded(() => Apply(ProductMode.Playing, "the host resumed the product"));

    /// <summary>Gives input to a modal interaction.</summary>
    public ProductModeChange EnterModal() => Apply(ProductMode.Modal, "the product opened a modal interaction");

    /// <summary>Returns input to ordinary play when a modal interaction ends.</summary>
    public ProductModeChange ExitModal() => Apply(ProductMode.Playing, "the product closed its modal interaction", closesModal: true);

    /// <summary>Marks the player dead. Death outranks a pause and an open modal.</summary>
    public ProductModeChange MarkDead() => Apply(ProductMode.Dead, "the ruleset reported the player dead");

    /// <summary>
    /// Replaces the running session with a fresh one from the same composition and returns to
    /// ordinary play. Held input does not survive the replacement, because the replacement starts
    /// with no input interpreter state to carry it.
    /// </summary>
    public void Restart()
    {
        Guarded(RestartCore);
    }

    private void RestartCore()
    {
        if (_shutdown)
        {
            Record(new(_mode, _mode, ProductModeChangeOutcome.Refused, "the product is shut down"));
            return;
        }

        if (_mode == ProductMode.Title)
        {
            // Replacing a session that has not begun would be the same run started twice, and it would put
            // the entry screen behind the world without the client having asked to leave it.
            Record(new(_mode, _mode, ProductModeChangeOutcome.Refused, "the entry screen leaves only for ordinary play; begin first"));
            return;
        }

        IGameSession replacement = _ruleset.CreateSession(new GameSessionContext(_context.Engine, _composition));
        IGameSession previous = _session;
        try
        {
            ApplyPlayerPreferences(replacement);
            replacement.PublishInitial();
        }
        catch
        {
            replacement.Dispose();
            throw;
        }

        _session = replacement;
        previous.Dispose();
        _started = true;
        // The replacement is always the decision here, but the *mode* only moved if it was not
        // already ordinary play: Changed answers the mode question, not the session one.
        ProductMode from = _mode;
        _mode = ProductMode.Playing;
        if (_session is IModeAwareGameSession aware) aware.ApplyProductMode(ProductMode.Playing);
        Record(new(from, ProductMode.Playing, from == ProductMode.Playing ? ProductModeChangeOutcome.AlreadyInMode : ProductModeChangeOutcome.Applied, "the product replaced its session"));
    }

    /// <summary>
    /// Quits to the title: the running session retires and the entry screen owns the product again.
    /// A quit from the title is refused; quitting never deletes saves.
    /// </summary>
    public void QuitToTitle()
    {
        if (_shutdown)
        {
            Record(new(_mode, _mode, ProductModeChangeOutcome.Refused, "the product is shut down"));
            return;
        }

        if (_mode == ProductMode.Title)
        {
            Record(new(_mode, _mode, ProductModeChangeOutcome.Refused, "the product is already at the title"));
            return;
        }

        IGameSession replacement = _ruleset.CreateSession(new GameSessionContext(_context.Engine, _composition));
        IGameSession previous = _session;
        try
        {
            ApplyPlayerPreferences(replacement);
            replacement.PublishInitial();
        }
        catch
        {
            replacement.Dispose();
            throw;
        }

        _session = replacement;
        previous.Dispose();
        ProductMode from = _mode;
        _mode = ProductMode.Title;
        if (_session is IModeAwareGameSession aware) aware.ApplyProductMode(ProductMode.Title);
        Record(new(from, ProductMode.Title, ProductModeChangeOutcome.Applied, "the product quit to the title"));
    }

    public void Shutdown()
    {
        Guarded(ShutdownCore);
    }

    private void ShutdownCore()
    {
        if (_shutdown) return;
        _session.Dispose();
        _saveSlots?.Dispose();
        _playerPreferences?.Dispose();
        _shutdown = true;
    }

    public void Dispose() => Shutdown();

    public ProductUpdateResult Update(ProductUpdate update)
    {
        try
        {
            return UpdateCore(update);
        }
        catch (Exception failure)
        {
            // An exception leaving the product's entry point ends this runtime incarnation, and the
            // Engine's own report of that names no cause. The reason is published here, where the
            // product still owns the diagnostics channel, before the failure escapes.
            PublishCallbackFailure(failure);
            throw;
        }
    }

    private void PublishCallbackFailure(Exception failure)
    {
        try
        {
            _context.Engine.Diagnostics.Publish(new DiagnosticsPublishRequest(
                DiagnosticsSeverity.Error,
                DiagnosticsDisposition.Terminal,
                "daggerfall.product",
                "callback.failed",
                failure.ToString(),
                string.Empty));
        }
        catch (Exception) { /* a failure to report a failure must not replace it */ }
    }

    private ProductUpdateResult UpdateCore(ProductUpdate update)
    {
        // A pause admits no update at all, so neither input nor world time reaches the session.
        // A modal or a death still forwards the update, because the presentation that shows them
        // has to keep publishing; the session decides what the mode means for its own world.
        if (!_started || _shutdown || _mode == ProductMode.Paused) return ProductUpdateResult.None;
        bool begin = _mode == ProductMode.Title
            && _session is IEntryScreenSession entry
            && entry.RequestsBegin(update.Input);

        // Settle what the session asked for before it runs again: a resumed save whose player is
        // already dead asks for death on the first look, and that must land before the world takes
        // a step rather than after it.
        AdoptSessionRequest();
        ProductUpdateResult result = _session.Update(update);
        PersistRequestedPlayerPreferences();
        AdoptSessionRequest();
        HonorPlayerDefeatOutcomeRequests();
        HonorSaveRequests();

        // The entry screen's own action leaves that mode after the session has run, not before: the
        // session is still in the entry-screen mode for that update, so it interprets no gameplay input
        // and takes no world step, and the action it cannot use is dropped by the same gate every other
        // action is. Applying the mode first would put the session into ordinary play in time to act on
        // the very slice that only asked for play, and to report the request it cannot interpret as an
        // unrecognized action. The transition publishes the presentation it changed.
        if (begin) Begin();
        if (_mode == ProductMode.Title
            && _session is IEntryScreenStartupSession startup
            && startup.TakeEntryReadyForPlay())
        {
            Apply(ProductMode.Playing, "the entry screen completed its opening sequence", closesEntryScreen: true);
        }
        return result;
    }

    /// <summary>
    /// Applies a mode the ruleset asked for. The session asks because it can open an interaction
    /// the product cannot see; the product still decides, so a refused request leaves the mode
    /// where it was and says so in <see cref="ModeHistory"/>.
    /// </summary>
    private void AdoptSessionRequest()
    {
        if (_session is not IModeAwareGameSession aware || aware.PendingModeRequest is not { } requested) return;
        Apply(requested, "the ruleset asked for this mode", closesModal: aware.PendingModeRequestClosesModal);
    }

    private void ApplyPlayerPreferences(IGameSession session)
    {
        if (session is not IPlayerPreferencesSession preferences) return;

        try
        {
            ProductStateLoad<string> loaded = PlayerPreferences.Load(_composition.Ruleset.Value);
            if (loaded.Present && loaded.State is null)
                throw new InvalidOperationException("The stored player-preference value is null.");
            preferences.ApplyPlayerPreferences(loaded.Present ? loaded.State : null);
        }
        catch (Exception error)
        {
            ApplyDefaultPlayerPreferences(preferences, $"Saved player preferences are invalid; defaults are active: {error.Message}");
        }
    }

    private static void ApplyDefaultPlayerPreferences(IPlayerPreferencesSession preferences, string diagnostic)
    {
        try
        {
            preferences.ApplyPlayerPreferences(null);
            preferences.ReportPlayerPreferencesOutcome(diagnostic);
        }
        catch (Exception fallbackError)
        {
            preferences.ReportPlayerPreferencesOutcome($"{diagnostic} Default preferences could not be applied: {fallbackError.Message}");
        }
    }

    private void PersistRequestedPlayerPreferences()
    {
        if (_session is not IPlayerPreferencesSession preferences) return;

        string? serialized;
        try
        {
            serialized = preferences.TakePlayerPreferencesSave();
        }
        catch (Exception error)
        {
            preferences.ReportPlayerPreferencesOutcome($"Player preferences are active but were not saved: {error.Message}");
            return;
        }

        if (serialized is null) return;
        try
        {
            PersistenceSaveReceipt receipt = PlayerPreferences.Save(_composition.Ruleset.Value, serialized, PersistenceRevisionGuard.Any);
            if (receipt.Outcome != PersistenceSaveOutcome.Saved)
            {
                preferences.ReportPlayerPreferencesOutcome($"Player preferences are active but were not saved: persistence returned {receipt.Outcome}.");
                return;
            }

            preferences.ReportPlayerPreferencesOutcome("Player preferences saved.");
        }
        catch (Exception error)
        {
            preferences.ReportPlayerPreferencesOutcome($"Player preferences are active but were not saved: {error.Message}");
        }
    }

    /// <summary>
    /// Honors ordinary named save-slot requests. The catalog owns every menu save/load payload;
    /// explicit public store methods remain available for startup and focused callers, never as a
    /// second player-facing menu path.
    /// </summary>
    private void HonorSaveRequests()
    {
        if (_session is not ISaveRequestingGameSession requesting) return;
        if (requesting.TakeSaveSlotRequest() is not { } slotRequest) return;
        if (_mode == ProductMode.Dead)
        {
            requesting.ReportSaveOutcome("Save choices are unavailable after defeat.");
            return;
        }
        HonorSaveSlotRequest(requesting, slotRequest);
    }

    /// <summary>
    /// Routes the ruleset-neutral choices exposed by a defeated session. The session recognizes
    /// Daggerfall's action payload; this Host only decides how a requested replacement or named
    /// save operation is carried out. A defeat choice is valid only while the product owns Dead,
    /// so a stale held action cannot replace a newly adopted session.
    /// </summary>
    private void HonorPlayerDefeatOutcomeRequests()
    {
        if (_session is not IPlayerDefeatOutcomeSession requesting
            || requesting.TakePlayerDefeatOutcomeRequest() is not { } request)
            return;

        if (_mode != ProductMode.Dead)
        {
            requesting.ReportPlayerDefeatOutcome("The defeat choices are no longer available.");
            return;
        }

        switch (request.Outcome)
        {
            case PlayerDefeatOutcome.NewGame:
                Restart();
                if (_session is IPlayerDefeatOutcomeSession replacement)
                    replacement.ReportPlayerDefeatOutcome("New game started.");
                return;
            case PlayerDefeatOutcome.Load:
                if (string.IsNullOrWhiteSpace(request.SaveKey))
                {
                    requesting.ReportPlayerDefeatOutcome("Choose a saved game to load.");
                    return;
                }
                if (_session is ISaveRequestingGameSession saveRequesting)
                {
                    HonorSaveSlotRequest(saveRequesting, new(SaveSlotOperation.Load, request.SaveKey));
                    return;
                }
                requesting.ReportPlayerDefeatOutcome("Load failed: the selected ruleset does not support save slots.");
                return;
            case PlayerDefeatOutcome.QuitToTitle:
                QuitToTitle();
                if (_session is IPlayerDefeatOutcomeSession title)
                    title.ReportPlayerDefeatOutcome("Returned to the title.");
                return;
            default:
                requesting.ReportPlayerDefeatOutcome("The defeat choice was not recognized.");
                return;
        }
    }

    private void HonorSaveSlotRequest(ISaveRequestingGameSession requesting, SaveSlotRequest request)
    {
        try
        {
            switch (request.Operation)
            {
                case SaveSlotOperation.List:
                    RefreshSaveSlots(requesting);
                    return;
                case SaveSlotOperation.Save:
                    SaveNamedSlot(requesting, request);
                    return;
                case SaveSlotOperation.Load:
                    if (string.IsNullOrWhiteSpace(request.Key))
                    {
                        requesting.ReportSaveOutcome("Choose a save slot to load.");
                        return;
                    }
                    LoadNamedSlot(requesting, request.Key);
                    return;
                case SaveSlotOperation.Delete:
                    DeleteNamedSlot(requesting, request);
                    return;
                default:
                    requesting.ReportSaveOutcome("Save slot request was not recognized.");
                    return;
            }
        }
        catch (WorldRpgSaveFormatException error)
        {
            requesting.ReportSaveOutcome($"Save slots unavailable: {error.Message}");
            requesting.ReportSaveSlots([], error.Message);
        }
        catch (Exception error)
        {
            requesting.ReportSaveOutcome($"Save slot operation failed: {error.Message}");
        }
    }

    private void SaveNamedSlot(ISaveRequestingGameSession requesting, SaveSlotRequest request)
    {
        if (_shutdown) throw new ObjectDisposedException(nameof(WorldRpgProduct));
        if (_session is not ISaveableGameSession saveable)
        {
            requesting.ReportSaveOutcome("Save failed: the selected compiled ruleset does not support save capture.");
            return;
        }

        if (string.IsNullOrWhiteSpace(request.Label))
        {
            requesting.ReportSaveOutcome("Name the save slot before saving.");
            return;
        }

        IReadOnlyList<WorldRpgSaveSlotEntry> entries = SaveSlots.List();
        string key;
        if (string.IsNullOrWhiteSpace(request.Key))
        {
            key = NextSaveSlotKey(entries);
        }
        else
        {
            if (!entries.Any(entry => string.Equals(entry.Key, request.Key, StringComparison.Ordinal)))
            {
                requesting.ReportSaveOutcome("The selected save slot no longer exists. Refresh the list and try again.");
                RefreshSaveSlots(requesting);
                return;
            }
            if (!request.Confirm)
            {
                requesting.ReportSaveOutcome($"Confirm overwriting '{request.Key}'.");
                return;
            }
            key = request.Key;
        }

        WorldRpgSaveSlotEntry entry = SaveSlots.SaveSlot(key, request.Label.Trim(), new GameSaveEnvelope(saveable.CaptureSave()));
        requesting.ReportSaveOutcome($"Saved '{entry.Label}' (revision {entry.Revision}).");
        RefreshSaveSlots(requesting);
    }

    private void LoadNamedSlot(ISaveRequestingGameSession requesting, string key)
    {
        (GameSaveEnvelope? envelope, WorldRpgSlotLoadDiagnostic? diagnostic) = SaveSlots.LoadSlot(key, _composition.Ruleset.Value);
        if (diagnostic is not null)
        {
            requesting.ReportSaveOutcome($"Load failed: {diagnostic.Message}");
            RefreshSaveSlots(requesting);
            return;
        }

        LoadSavedGame(requesting, envelope!);
    }

    private void DeleteNamedSlot(ISaveRequestingGameSession requesting, SaveSlotRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Key))
        {
            requesting.ReportSaveOutcome("Choose a save slot to delete.");
            return;
        }
        if (!request.Confirm)
        {
            requesting.ReportSaveOutcome($"Confirm deleting '{request.Key}'.");
            return;
        }

        if (!SaveSlots.DeleteSlot(request.Key))
        {
            requesting.ReportSaveOutcome("The selected save slot no longer exists.");
            RefreshSaveSlots(requesting);
            return;
        }

        requesting.ReportSaveOutcome($"Deleted save slot '{request.Key}'.");
        RefreshSaveSlots(requesting);
    }

    private void RefreshSaveSlots(ISaveRequestingGameSession? requesting = null)
    {
        if (_session is not ISaveRequestingGameSession session) return;
        try
        {
            session.ReportSaveSlots(
                SaveSlots.List().Select(entry => new SaveSlotSummary(entry.Key, entry.Label, entry.SavedAtUtc, entry.Ruleset)).ToArray(),
                null);
        }
        catch (WorldRpgSaveFormatException error)
        {
            session.ReportSaveSlots([], error.Message);
            if (requesting is not null) requesting.ReportSaveOutcome($"Save slots unavailable: {error.Message}");
        }
    }

    private static string NextSaveSlotKey(IReadOnlyList<WorldRpgSaveSlotEntry> entries)
    {
        ulong ordinal = 1;
        while (entries.Any(entry => string.Equals(entry.Key, $"slot-{ordinal}", StringComparison.Ordinal))) ordinal++;
        return $"slot-{ordinal}";
    }

    private void LoadSavedGame(ISaveRequestingGameSession requesting, GameSaveEnvelope envelope)
    {
        if (envelope.Payload.Ruleset != _composition.Ruleset)
        {
            requesting.ReportSaveOutcome("Load failed: the saved game is for a different ruleset.");
            return;
        }

        if (_ruleset is not ISaveableGameRuleset saveable)
        {
            requesting.ReportSaveOutcome("Load failed: the selected compiled ruleset does not support save resume.");
            return;
        }

        IGameSession replacement;
        try
        {
            replacement = saveable.CreateSession(new GameSessionContext(_context.Engine, _composition), envelope.Payload);
        }
        catch (Exception error)
        {
            requesting.ReportSaveOutcome($"Load failed: {error.Message}");
            return;
        }

        try
        {
            ApplyPlayerPreferences(replacement);
            replacement.PublishInitial();
        }
        catch (Exception error)
        {
            replacement.Dispose();
            requesting.ReportSaveOutcome($"Load failed: {error.Message}");
            return;
        }

        IGameSession previous = _session;
        _session = replacement;
        try
        {
            previous.Dispose();
            _started = true;
            ProductMode from = _mode;
            _mode = ProductMode.Playing;
            if (_session is IModeAwareGameSession aware) aware.ApplyProductMode(ProductMode.Playing);
            Record(new(from, ProductMode.Playing, from == ProductMode.Playing ? ProductModeChangeOutcome.AlreadyInMode : ProductModeChangeOutcome.Applied, "the product loaded a saved game"));
        }
        catch (Exception error)
        {
            // The replacement is constructed and published; only adopting it can still fail here.
            // Put the current session back so a failing dispose or mode apply cannot strand the
            // product on a half-adopted session, and report instead of throwing out of the update.
            _session = previous;
            try
            {
                replacement.Dispose();
            }
            catch (Exception disposeError)
            {
                requesting.ReportSaveOutcome($"Load failed: {error.Message}; discarding the replacement also failed: {disposeError.Message}");
                return;
            }

            requesting.ReportSaveOutcome($"Load failed: {error.Message}");
            return;
        }

        if (replacement is ISaveRequestingGameSession resumed) resumed.ReportSaveOutcome("Game loaded.");
        RefreshSaveSlots();
    }

    /// <summary>
    /// Applies one requested mode under the product's precedence, which is the whole table:
    /// death outranks an open modal, which outranks a pause; a pause cancels an open modal; a
    /// resume does not close one, because only the interaction that owns input ends it; a modal
    /// needs ordinary play, so a paused product must resume first; a modal or a death needs a
    /// session that can apply it; death is left only by a session replacement; the entry screen
    /// leaves only when the entry screen itself asks to, because it is the product's own gate rather
    /// than a state a caller can step over; and a request that names the current mode is already in
    /// it rather than an error.
    /// </summary>
    /// <param name="requested">The mode being asked for.</param>
    /// <param name="reason">Why, recorded in the history so a caller can see what was decided.</param>
    /// <param name="closesModal">Whether this is the interaction that owns input closing itself.</param>
    /// <param name="closesEntryScreen">
    /// Whether this is the entry screen's own request to begin. Every other route into ordinary play -
    /// a resume, a modal closing, a replacement - leaves the screen behind without the client having
    /// asked to, and reports an event that never happened: a resume of an unpaused product, a modal
    /// closed that was never open, a session resumed that never began.
    /// </param>
    private ProductModeChange Apply(ProductMode requested, string reason, bool closesModal = false, bool closesEntryScreen = false)
    {
        if (_shutdown) return Record(new(_mode, _mode, ProductModeChangeOutcome.Refused, "the product is shut down"));
        if (!_started) return Record(new(_mode, _mode, ProductModeChangeOutcome.Refused, "the product has not started"));
        if (requested == _mode) return Record(new(_mode, _mode, ProductModeChangeOutcome.AlreadyInMode, reason));
        if (_mode == ProductMode.Title && !closesEntryScreen)
        {
            // The entry screen owns the product until a client begins: a pause, a modal, a death, a
            // second entry screen, or any other route into ordinary play would each be a caller stepping
            // over the one decision the screen exists to represent.
            return Record(new(_mode, _mode, ProductModeChangeOutcome.Refused, "the entry screen leaves only when the entry screen asks to; begin first"));
        }

        if (_mode == ProductMode.Dead)
        {
            return Record(new(_mode, _mode, ProductModeChangeOutcome.Refused, "a dead player leaves that mode only by a session replacement, not by a mode change"));
        }

        if (requested == ProductMode.Modal && _mode == ProductMode.Paused)
        {
            // Opening an interaction the player cannot see would hide the pause they asked for.
            return Record(new(_mode, _mode, ProductModeChangeOutcome.Refused, "a modal interaction needs ordinary play; resume the paused product first"));
        }

        if (requested is ProductMode.Modal or ProductMode.Dead && _session is not IModeAwareGameSession)
        {
            // This product holds a paused world by dropping updates itself, but a modal and a death
            // still forward one so the presentation keeps publishing. A session that cannot apply
            // the mode would interpret those updates as ordinary play, so entering the mode would
            // claim the world is held while it advances.
            return Record(new(_mode, _mode, ProductModeChangeOutcome.Refused, "the running session cannot apply a mode; it does not implement IModeAwareGameSession"));
        }

        if (requested == ProductMode.Playing && _mode == ProductMode.Modal && !closesModal)
        {
            // Resuming is not closing: only the interaction that owns input ends it.
            return Record(new(_mode, _mode, ProductModeChangeOutcome.Refused, "a resume does not close a modal interaction; closing it is what returns to ordinary play"));
        }

        return Record(SetMode(requested, reason));
    }

    private ProductModeChange SetMode(ProductMode mode, string reason)
    {
        ProductMode from = _mode;
        _mode = mode;
        if (_session is IModeAwareGameSession aware) aware.ApplyProductMode(mode);
        return new(from, mode, ProductModeChangeOutcome.Applied, reason);
    }

    private ProductModeChange Record(ProductModeChange change)
    {
        // A session that keeps asking for a mode the product refuses would otherwise fill the
        // history with one identical line per update.
        if (_modeHistory.Count == 0 || _modeHistory[^1] != change)
        {
            _modeHistory.Add(change);
            if (_modeHistory.Count > ModeHistoryLimit) _modeHistory.RemoveAt(0);
        }

        return change;
    }
}

/// <summary>What one product mode decision did.</summary>
public enum ProductModeChangeOutcome
{
    /// <summary>The product entered the requested mode.</summary>
    Applied,

    /// <summary>The request named the mode the product already had.</summary>
    AlreadyInMode,

    /// <summary>Precedence refused the request; the product kept the mode it had.</summary>
    Refused,
}

/// <summary>
/// One product mode decision: where the product was, where the request pointed, what happened and
/// why. A change whose outcome is not <see cref="ProductModeChangeOutcome.Applied"/> changed nothing.
/// </summary>
public sealed record ProductModeChange(ProductMode From, ProductMode To, ProductModeChangeOutcome Outcome, string Reason)
{
    /// <summary>Whether the product entered a different mode.</summary>
    public bool Changed => Outcome == ProductModeChangeOutcome.Applied;
}

/// <summary>
/// One thing a resume has to report. Blocking entries are why no product was created;
/// non-blocking entries describe state that was left out of a product that did resume.
/// </summary>
public sealed record WorldRpgSaveDiagnostic(string Code, string Message, bool IsBlocking = true);
public sealed record WorldRpgResumeResult(WorldRpgProduct? Product, ulong Revision, IReadOnlyList<WorldRpgSaveDiagnostic> Diagnostics)
{
    /// <summary>A resume happened and nothing was left out of it.</summary>
    public bool IsComplete => Product is not null && Diagnostics.Count == 0;

    /// <summary>
    /// A resume happened. Non-blocking entries may still describe state that was left
    /// out, which is why <see cref="IsComplete"/> is the stricter question.
    /// </summary>
    public bool IsResumed => Product is not null && Diagnostics.All(value => !value.IsBlocking);
}
