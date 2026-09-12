using Rusty.Engine;
using Rusty.Engine.Persistence;
using WorldRpg.Kit;

namespace WorldRpg.Host;

/// <summary>Reference host lifecycle and explicit built-in ruleset selection.</summary>
public sealed class WorldRpgProduct : IEngineProduct
{
    private readonly IGameSession _session;
    private readonly ResolvedCompositionIdentity _compositionIdentity;
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

    private WorldRpgProduct((IGameSession Session, ResolvedCompositionIdentity Identity) created)
    {
        _session = created.Session;
        _compositionIdentity = created.Identity;
        try { _session.PublishInitial(); }
        catch
        {
            _session.Dispose();
            throw;
        }
    }

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
                new WorldRpgProduct((session, composition.Identity)),
                loaded.Revision,
                [.. notices.Select(value => new WorldRpgSaveDiagnostic(value.Code, value.Message, IsBlocking: false))]);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            return new(null, loaded.Revision, [new("payload", $"The validated save payload was rejected: {error.Message}")]);
        }
    }

    private static (IGameSession Session, ResolvedCompositionIdentity Identity) CreateSession(ProductCreateContext context, IGameRuleset? ruleset, GameBundleId bundle)
    {
        (ResolvedGameComposition composition, IGameRuleset selected) = ResolveSelection(context, ruleset, bundle);
        return (selected.CreateSession(new GameSessionContext(context.Engine, composition)), composition.Identity);
    }

    private static (ResolvedGameComposition Composition, IGameRuleset Selected) ResolveSelection(ProductCreateContext context, IGameRuleset? ruleset, GameBundleId bundle)
    {
        ResolvedGameComposition composition = GameCompositionResolver.Resolve(context.Content, bundle).RequireComposition();
        IGameRuleset selected = ruleset ?? BuiltInRulesets.Resolve(composition.Ruleset);
        if (selected.Id != composition.Ruleset)
            throw new InvalidOperationException($"Selected ruleset '{selected.Id.Value}' does not match bundle ruleset '{composition.Ruleset.Value}'.");
        return (composition, selected);
    }

    public void Start()
    {
        if (_shutdown) return;
        _started = true;
        _paused = false;
        _session.PublishInitial();
    }

    /// <summary>Republishes the current session projection when the Engine attaches a new presentation client.</summary>
    public void Attach()
    {
        if (_shutdown) return;
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

    public ProductUpdateResult Update(ProductUpdate update)
    {
        if (!_started || _paused || _shutdown) return ProductUpdateResult.None;
        return _session.Update(update);
    }
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
