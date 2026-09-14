using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Persistence;
using WorldRpg.Kit;

namespace WorldRpg.Host;

/// <summary>Reference host lifecycle and explicit built-in ruleset selection.</summary>
public sealed class WorldRpgProduct : IEngineProduct
{
    /// <summary>How many recent mode decisions are kept for diagnosis.</summary>
    public const int ModeHistoryLimit = 16;

    /// <summary>
    /// The action a client sends to leave the entry screen.
    /// </summary>
    /// <remarks>
    /// The client and the product have to agree on this word, so it is stated once here and the DOM's own
    /// table is checked against it rather than the two being written out separately and drifting.
    /// </remarks>
    public const string EntryScreenAction = "begin";

    private readonly ProductCreateContext _context;
    private readonly ResolvedGameComposition _composition;
    private readonly IGameRuleset _ruleset;
    private readonly List<ProductModeChange> _modeHistory = [];
    private IGameSession _session;
    private readonly ResolvedCompositionIdentity _compositionIdentity;
    private bool _started;
    private bool _shutdown;
    private readonly bool _resumed;
    private ProductMode _mode = ProductMode.Playing;

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
        try { _session.PublishInitial(); }
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
        return store.Save(key, new GameSaveEnvelope(SaveCompositionIdentity.From(_compositionIdentity), saveable.CaptureSave()), guard, expectedRevision);
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
        catch (WorldRpgSaveSchemaException error)
        {
            return new(null, 0, [new("storage-schema", error.Message)]);
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
        List<WorldRpgSaveDiagnostic> diagnostics = loaded.State.Composition.CheckCompatible(composition.Identity)
            .Select(value => new WorldRpgSaveDiagnostic(value.Code, value.Message)).ToList();
        if (loaded.State.Payload.Ruleset != composition.Ruleset)
            diagnostics.Add(new("payload-ruleset", "The saved payload ruleset does not match the selected bundle ruleset."));
        if (diagnostics.Count > 0) return new(null, loaded.Revision, diagnostics);
        if (selected is not ISaveableGameRuleset saveable)
            return new(null, loaded.Revision, [new("unsupported", "The selected compiled ruleset does not support save resume.")]);
        try
        {
            IGameSession session = saveable.CreateSession(new GameSessionContext(context.Engine, composition), loaded.State.Payload);
            // A restore that had to report something still resumes: its notices ride the
            // one resume channel as non-blocking entries, exactly as composition
            // diagnostics already do.
            IReadOnlyList<SaveRestoreNotice> notices = session is IRestoringGameSession restoring ? restoring.RestoreNotices : [];
            return new(
                new WorldRpgProduct(context, selected, composition, session),
                loaded.Revision,
                [.. notices.Select(value => new WorldRpgSaveDiagnostic(value.Code, value.Message, IsBlocking: false))]);
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
    /// this tells it which mode it is starting in and publishes once, which is the projection a client
    /// that just attached reads. The Engine calls this once through the generated product exports, so the
    /// entry screen is what a client sees before anything has happened in the world; a product resumed
    /// from a save starts in ordinary play instead, because the screen exists to begin a run rather than
    /// to begin one already in progress.
    /// <para>
    /// The mode is set here rather than applied through <see cref="Apply"/>, which republishes: this
    /// publication is the projection a client reads, and a second one would be a mode change for a mode
    /// the product has not left yet. The decision is still recorded, so a caller that needs to know why
    /// the product is where it is reads the history it always did.
    /// </para>
    /// </remarks>
    public void Start()
    {
        if (_shutdown || _started) return;
        _started = true;
        ProductMode start = _resumed ? ProductMode.Playing : ProductMode.Title;
        ProductMode from = _mode;
        _mode = start;
        if (_session is IModeAwareGameSession aware) aware.ApplyProductMode(start);
        Record(new(from, start,
            from == start ? ProductModeChangeOutcome.AlreadyInMode : ProductModeChangeOutcome.Applied,
            _resumed ? "the resumed product started in the world it restored" : "the product started at its entry screen"));
        _session.PublishInitial();
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
    public ProductModeChange Begin() => Apply(ProductMode.Playing, "the entry screen asked for ordinary play");

    /// <summary>Republishes the current session projection when the Engine attaches a new presentation client.</summary>
    public void Attach()
    {
        if (_shutdown) return;
        _session.PublishInitial();
    }

    /// <summary>Pauses ordinary play. A modal that owns input is cancelled by the pause.</summary>
    public void Pause() => Apply(ProductMode.Paused, "the host paused the product");

    /// <summary>Resumes ordinary play from a pause.</summary>
    public void Resume() => Apply(ProductMode.Playing, "the host resumed the product");

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
        if (_shutdown)
        {
            Record(new(_mode, _mode, ProductModeChangeOutcome.Refused, "the product is shut down"));
            return;
        }

        IGameSession replacement = _ruleset.CreateSession(new GameSessionContext(_context.Engine, _composition));
        IGameSession previous = _session;
        try
        {
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

    public void Shutdown()
    {
        if (_shutdown) return;
        _session.Dispose();
        _shutdown = true;
    }

    public void Dispose() => Shutdown();

    public ProductUpdateResult Update(ProductUpdate update)
    {
        // A pause admits no update at all, so neither input nor world time reaches the session.
        // A modal or a death still forwards the update, because the presentation that shows them
        // has to keep publishing; the session decides what the mode means for its own world.
        if (!_started || _shutdown || _mode == ProductMode.Paused) return ProductUpdateResult.None;
        bool begin = _mode == ProductMode.Title && RequestsEntryScreenAction(update.Input);

        // Settle what the session asked for before it runs again: a resumed save whose player is
        // already dead asks for death on the first look, and that must land before the world takes
        // a step rather than after it.
        AdoptSessionRequest();
        ProductUpdateResult result = _session.Update(update);
        AdoptSessionRequest();

        // The entry screen's own action leaves that mode after the session has run, not before: the
        // session is still in the entry-screen mode for that update, so it interprets no gameplay input
        // and takes no world step, and the action it cannot use is dropped by the same gate every other
        // action is. Applying the mode first would put the session into ordinary play in time to act on
        // the very slice that only asked for play, and to report the request it cannot interpret as an
        // unrecognized action. The transition publishes the presentation it changed.
        if (begin) Apply(ProductMode.Playing, "the entry screen asked for ordinary play");
        return result;
    }

    /// <summary>
    /// Whether an admitted input slice carries the entry screen's own action.
    /// </summary>
    /// <remarks>
    /// This is the same wire the ruleset reads player actions from, and the same contract string, because
    /// the Engine delivers one kind of semantic action rather than one per consumer. The product reads one
    /// action name here and passes the slice through unchanged, so the ruleset still sees everything the
    /// entry screen sent and decides for itself that it means nothing in the mode it is in.
    /// </remarks>
    private static bool RequestsEntryScreenAction(ReadOnlySpan<ProductInputEvent> input)
    {
        foreach (ProductInputEvent inputEvent in input)
        {
            if (inputEvent.ValueKind != InputValueKind.ProductPayload
                || !inputEvent.PayloadContract.Span.SequenceEqual(EntryScreenActionContract)) continue;
            if (IsEntryScreenAction(inputEvent.PayloadData.Span)) return true;
        }

        return false;
    }

    /// <summary>The payload contract a UI semantic action arrives under.</summary>
    private static ReadOnlySpan<byte> EntryScreenActionContract => "dagger.ui.action.v1"u8;

    /// <summary>
    /// Whether an action payload names the entry screen's action, without interpreting the rest of it.
    /// </summary>
    /// <remarks>
    /// A payload that does not parse, names another action, or carries fields this does not expect is not
    /// the entry screen's action, so the product leaves the mode alone and the ruleset reports it - which
    /// is where a malformed action already lands today.
    /// </remarks>
    private static bool IsEntryScreenAction(ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty || payload.Length > 1024) return false;
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload.ToArray());
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            bool named = false;
            bool begin = false;
            foreach (JsonProperty property in root.EnumerateObject())
            {
                if (property.Name != "action" || property.Value.ValueKind != JsonValueKind.String) continue;
                if (named) return false;
                named = true;
                begin = property.Value.ValueEquals(EntryScreenAction);
            }

            return named && begin && root.EnumerateObject().Count() == 1;
        }
        catch (JsonException) { return false; }
    }

    /// <summary>
    /// Applies a mode the ruleset asked for. The session asks because it can open an interaction
    /// the product cannot see; the product still decides, so a refused request leaves the mode
    /// where it was and says so in <see cref="ModeHistory"/>.
    /// </summary>
    private void AdoptSessionRequest()
    {
        if (_session is not IModeAwareGameSession aware || aware.PendingModeRequest is not { } requested) return;
        Apply(requested, "the ruleset asked for this mode");
    }

    /// <summary>
    /// Applies one requested mode under the product's precedence, which is the whole table:
    /// death outranks an open modal, which outranks a pause; a pause cancels an open modal; a
    /// resume does not close one, because only the interaction that owns input ends it; a modal
    /// needs ordinary play, so a paused product must resume first; a modal or a death needs a
    /// session that can apply it; death is left only by a session replacement; the entry screen
    /// leaves only for ordinary play, because it is the product's own gate rather than a state a
    /// caller can step over; and a request that names the current mode is already in it rather than
    /// an error.
    /// </summary>
    private ProductModeChange Apply(ProductMode requested, string reason, bool closesModal = false)
    {
        if (_shutdown) return Record(new(_mode, _mode, ProductModeChangeOutcome.Refused, "the product is shut down"));
        if (!_started) return Record(new(_mode, _mode, ProductModeChangeOutcome.Refused, "the product has not started"));
        if (requested == _mode) return Record(new(_mode, _mode, ProductModeChangeOutcome.AlreadyInMode, reason));
        if (_mode == ProductMode.Title && requested != ProductMode.Playing)
        {
            // The entry screen owns the product until a client begins: a pause, a modal, a death or a
            // second entry screen would each be a caller stepping over the one decision the screen exists
            // to represent, and a death especially would be a death of a world that never started.
            return Record(new(_mode, _mode, ProductModeChangeOutcome.Refused, "the entry screen leaves only for ordinary play; begin first"));
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
