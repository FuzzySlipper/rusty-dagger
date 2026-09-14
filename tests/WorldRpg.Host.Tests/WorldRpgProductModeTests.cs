using System.Reflection;
using Rusty.Engine;
using Rusty.Engine.Persistence;
using WorldRpg.Host;
using WorldRpg.Kit;
using Xunit;

namespace WorldRpg.Host.Tests;

/// <summary>
/// The product's mode machine: ordinary play, pause, a modal interaction and death, with the
/// precedence between them, the cancel behaviour, and the session replacement that is the only
/// way out of death.
/// </summary>
public sealed class WorldRpgProductModeTests
{
    [Fact]
    public void A_modal_owns_input_until_it_is_closed_and_a_pause_cancels_it()
    {
        using WorldRpgProduct product = Product();
        StartInPlay(product);

        Assert.Equal(ProductMode.Playing, product.Mode);
        Assert.True(product.EnterModal().Changed);
        Assert.Equal(ProductMode.Modal, product.Mode);

        // Resuming is not closing: only the interaction that owns input ends it.
        product.Resume();
        Assert.Equal(ProductMode.Modal, product.Mode);
        Assert.Equal(ProductModeChangeOutcome.Refused, product.ModeHistory[^1].Outcome);

        // Closing returns to ordinary play, and closing again is simply already done.
        Assert.True(product.ExitModal().Changed);
        Assert.Equal(ProductMode.Playing, product.Mode);
        Assert.Equal(ProductModeChangeOutcome.AlreadyInMode, product.ExitModal().Outcome);

        // A pause cancels a modal rather than hiding behind it: the player asked for the world to
        // stop, and a modal that kept input would be a mode they cannot see.
        product.EnterModal();
        product.Pause();
        Assert.Equal(ProductMode.Paused, product.Mode);

        // And the ordinary path, which every other test reaches through a modal: pausing play.
        // `Pause` is a lifecycle method and returns nothing, so the decision is read from history.
        product.Resume();
        product.Pause();
        Assert.True(product.ModeHistory[^1].Changed);
        Assert.Equal(ProductModeChangeOutcome.Applied, product.ModeHistory[^1].Outcome);
        Assert.Equal(ProductMode.Paused, product.Mode);

        // A modal needs ordinary play, and resuming is the way back to it.
        Assert.Equal(ProductModeChangeOutcome.Refused, product.EnterModal().Outcome);
        Assert.True(product.ModeHistory[^1].Reason.Contains("resume the paused product", StringComparison.Ordinal));
        product.Resume();
        Assert.Equal(ProductMode.Playing, product.Mode);
    }

    [Fact]
    public void Death_outranks_every_other_mode_and_only_a_replacement_leaves_it()
    {
        ModeRecordingRuleset ruleset = new();
        using WorldRpgProduct product = Product(ruleset);
        StartInPlay(product);
        product.EnterModal();

        Assert.True(product.MarkDead().Changed);
        Assert.Equal(ProductMode.Dead, product.Mode);
        // The lifecycle methods the Engine calls return nothing, so the decision is read from
        // the history rather than from the call.
        product.Resume();
        Assert.Equal(ProductModeChangeOutcome.Refused, product.ModeHistory[^1].Outcome);
        product.Pause();
        Assert.Equal(ProductModeChangeOutcome.Refused, product.ModeHistory[^1].Outcome);
        Assert.Equal(ProductModeChangeOutcome.Refused, product.EnterModal().Outcome);
        Assert.Equal(ProductModeChangeOutcome.Refused, product.ExitModal().Outcome);

        int sessionsBefore = ruleset.Created;
        product.Restart();

        // The replacement is a new session from the same composition, it starts in ordinary play,
        // and the session it replaced was disposed rather than left holding the world.
        Assert.Equal(ProductMode.Playing, product.Mode);
        Assert.Equal(sessionsBefore + 1, ruleset.Created);
        Assert.True(ruleset.Replaced!.Disposed);
        Assert.True(product.ModeHistory[^1].Changed);
        Assert.Equal(ProductMode.Playing, ruleset.LastApplied);

        // The replacement is built from the composition the product resolved, not from a default or
        // a second resolution: same identity fingerprint and bundle as the session it replaced.
        Assert.NotNull(ruleset.ReplacedComposition);
        Assert.Equal(ruleset.FirstComposition!.Identity.Fingerprint, ruleset.ReplacedComposition!.Identity.Fingerprint);
        Assert.Equal(ruleset.FirstComposition.Identity.Bundle, ruleset.ReplacedComposition.Identity.Bundle);
    }

    [Fact]
    public void A_session_asks_for_a_mode_and_the_product_decides_at_the_next_update()
    {
        ModeRecordingRuleset ruleset = new() { Request = ProductMode.Modal };
        using WorldRpgProduct product = Product(ruleset);
        StartInPlay(product);

        // The ruleset can open an interaction the product cannot see, so it asks; the product
        // adopts the request and tells the session what it decided.
        product.Update(Update(1));
        Assert.Equal(ProductMode.Modal, product.Mode);
        Assert.Equal(ProductMode.Modal, ruleset.LastApplied);

        // A pause admits no update at all, so neither input nor world time reaches the session.
        product.Pause();
        int seen = ruleset.Updates;
        product.Update(Update(2));
        Assert.Equal(seen, ruleset.Updates);

        // Death refuses the session's next request instead of letting it reopen a modal.
        product.MarkDead();
        ruleset.Request = ProductMode.Modal;
        product.Update(Update(3));
        Assert.Equal(ProductMode.Dead, product.Mode);
        Assert.Equal(ProductModeChangeOutcome.Refused, product.ModeHistory[^1].Outcome);
    }

    [Fact]
    public void A_session_that_cannot_apply_a_mode_is_never_told_the_world_is_held()
    {
        // Pausing is this product's own doing, because it drops the update. A modal and a death
        // still forward one, so a session that cannot apply the mode would run them as ordinary
        // play while ModeHistory claimed the world was held.
        PlainRuleset ruleset = new();
        using WorldRpgProduct product = new(Context(), ruleset, new GameBundleId("test.bundle"));
        StartInPlay(product);

        Assert.Equal(ProductModeChangeOutcome.Refused, product.EnterModal().Outcome);
        Assert.Equal(ProductMode.Playing, product.Mode);
        Assert.Equal(ProductModeChangeOutcome.Refused, product.MarkDead().Outcome);
        Assert.Equal(ProductMode.Playing, product.Mode);
        Assert.Contains("IModeAwareGameSession", product.ModeHistory[^1].Reason, StringComparison.Ordinal);

        // Pausing still holds the world, because the product admits no update at all.
        product.Pause();
        int seen = ruleset.Session.Updates;
        product.Update(Update(1));
        Assert.Equal(seen, ruleset.Session.Updates);
    }

    [Fact]
    public void A_resumed_session_that_is_already_dead_is_adopted_before_the_world_steps()
    {
        // A restored save can carry a dead player. The session asks for death on the first look,
        // and that request has to settle before the world takes a step rather than after it.
        ModeRecordingRuleset ruleset = new() { Request = ProductMode.Dead };
        using WorldRpgProduct product = Product(ruleset);
        StartInPlay(product);

        product.Update(Update(1));

        Assert.Equal(ProductMode.Dead, product.Mode);
        Assert.Equal(1, ruleset.Updates);
        Assert.Equal(ProductMode.Dead, ruleset.LastApplied);
    }

    [Fact]
    public void Replacing_a_session_is_recorded_even_before_the_product_started()
    {
        ModeRecordingRuleset ruleset = new();
        using WorldRpgProduct product = Product(ruleset);

        product.Restart();

        // The session was replaced, but the mode did not move: Changed answers the mode question.
        Assert.Equal("the product replaced its session", product.ModeHistory[^1].Reason);
        Assert.Equal(ProductModeChangeOutcome.AlreadyInMode, product.ModeHistory[^1].Outcome);
        Assert.False(product.ModeHistory[^1].Changed);
        Assert.Equal(ProductMode.Playing, product.Mode);
    }

    [Fact]
    public void The_entry_screen_holds_the_world_until_a_client_asks_to_begin()
    {
        ModeRecordingRuleset ruleset = new();
        using WorldRpgProduct product = Product(ruleset);

        product.Start();

        // The product a launcher starts shows its entry screen, and the session is told the world is held
        // rather than being left to step behind it: input and time reach nothing while the mode holds.
        Assert.Equal(ProductMode.Title, product.Mode);
        Assert.Equal(ProductMode.Title, ruleset.LastApplied);
        Assert.Equal(ProductModeChangeOutcome.Applied, product.ModeHistory[^1].Outcome);

        // The entry screen's own action is the client's one way to leave it, and the product applies it
        // before the session runs, so the update that asked to begin does not also take a world step.
        product.Update(Semantic("""{"action":"begin"}"""));
        Assert.True(product.Mode == ProductMode.Playing, $"mode={product.Mode} history={product.ModeHistory[^1]}");
        Assert.Equal(ProductMode.Playing, product.Mode);
        Assert.Equal(ProductMode.Playing, ruleset.LastApplied);
        Assert.Equal(1, ruleset.Updates);

        // An action the entry screen does not answer leaves the mode where it is.
        using WorldRpgProduct second = Product();
        second.Start();
        second.Update(Semantic("""{"action":"inventory"}"""));
        Assert.Equal(ProductMode.Title, second.Mode);

        // A payload that names the action twice is not a shape this reads, so the mode stays and the
        // refusal is the ruleset's to report rather than the product's to guess at.
        second.Update(Semantic("""{"action":"begin","action":"begin"}"""));
        Assert.Equal(ProductMode.Title, second.Mode);
        // Asking to begin from a mode that is not the entry screen is refused and recorded, so a caller
        // that skipped the entry screen is visible rather than silently put into play.
        second.Update(Semantic("""{"action":"begin"}"""));
        Assert.Equal(ProductMode.Playing, second.Mode);
        Assert.Equal(ProductModeChangeOutcome.AlreadyInMode, second.Begin().Outcome);
    }

    /// <summary>One admitted input slice carrying a UI semantic action, the way the Engine delivers one.</summary>
    private static ProductUpdate Semantic(string json)
    {
        ProductInputEvent[] slice = [Ui(json)];
        return new(OuterUpdate(1), slice);
    }

    /// <summary>One product-payload input event carrying a semantic action to the product.</summary>
    private static ProductInputEvent Ui(string json) =>
        new ProductInputEvent(InputEventKind.DirectDigital, InputEdge.None, InputDevice.None, InputChannel.None,
            InputAxis.None, KeyboardControl.None, PointerButton.None, ControllerButton.None, ControllerAxis.None,
            InputClearReason.None, InputValueKind.ProductPayload, InputPhase.DirectUi, InputProvenance.DirectUi,
            default, default, default, 0F, 0F, ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty,
            ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty) with
        {
            PayloadContract = "dagger.ui.action.v1"u8.ToArray(),
            PayloadData = System.Text.Encoding.UTF8.GetBytes(json),
        };

    private static ProductUpdateFacts OuterUpdate(ulong step) =>
        new(ProductUpdateMode.Realtime, ProductLifecycleState.Running, step, step, step, step, 60, 1, 0, 1d / 60d);

    /// <summary>
    /// Starts a product and leaves its entry screen, which is the two steps a launcher and a client take.
    /// A test about ordinary play cares about what happens after both, and a test about the entry screen
    /// calls them separately.
    /// </summary>
    private static void StartInPlay(WorldRpgProduct product)
    {
        product.Start();
        product.Begin();
    }

    private static ProductUpdate Update(ulong step) =>
        new(new ProductUpdateFacts(ProductUpdateMode.Realtime, ProductLifecycleState.Running, step, step, step, step, 60, 1, 0, 1d / 60d), ReadOnlySpan<ProductInputEvent>.Empty);

    private static WorldRpgProduct Product(ModeRecordingRuleset? ruleset = null) =>
        new(Context(), ruleset ?? new ModeRecordingRuleset(), new GameBundleId("test.bundle"));

    private static ProductCreateContext Context() => new(Engine(), Content(), EmptyInput());

    private sealed class ModeRecordingRuleset : IGameRuleset
    {
        public RulesetId Id => new("test");

        internal ProductMode? Request { get; set; }

        internal int Created { get; private set; }

        internal ResolvedGameComposition? FirstComposition { get; private set; }

        internal ResolvedGameComposition? ReplacedComposition { get; private set; }

        internal int Updates { get; private set; }

        internal ModeRecordingSession? Replaced { get; private set; }

        internal ProductMode? LastApplied => _current?.LastApplied;

        private ModeRecordingSession? _current;

        public IGameSession CreateSession(GameSessionContext context)
        {
            Created++;
            FirstComposition ??= context.Composition;
            if (Created > 1) ReplacedComposition = context.Composition;
            Replaced = _current;
            _current = new ModeRecordingSession(this);
            return _current;
        }

        internal void Count() => Updates++;
    }

    private sealed class ModeRecordingSession(ModeRecordingRuleset owner) : IGameSession, IModeAwareGameSession
    {
        internal ProductMode? LastApplied { get; private set; }

        internal bool Disposed { get; private set; }

        public ProductMode? PendingModeRequest => owner.Request;

        public void ApplyProductMode(ProductMode mode) => LastApplied = mode;

        public void PublishInitial()
        {
        }

        public ProductUpdateResult Update(ProductUpdate update)
        {
            owner.Count();
            return ProductUpdateResult.None;
        }

        public void Dispose() => Disposed = true;
    }

    /// <summary>A ruleset whose session does not implement the mode seam at all.</summary>
    private sealed class PlainRuleset : IGameRuleset
    {
        public RulesetId Id => new("test");

        internal PlainSession Session { get; } = new();

        public IGameSession CreateSession(GameSessionContext context) => Session;
    }

    private sealed class PlainSession : IGameSession
    {
        internal int Updates { get; private set; }

        public void PublishInitial()
        {
        }

        public ProductUpdateResult Update(ProductUpdate update)
        {
            Updates++;
            return ProductUpdateResult.None;
        }

        public void Dispose()
        {
        }
    }

    private static ProductContent Content() => new(ContentFiles());

    private static ProductContentFile[] ContentFiles() =>
    [
        File("worldrpg/bundles/test.bundle.json", """{"kind":"worldrpg.game-bundle","schemaVersion":1,"id":"test.bundle","version":1,"ruleset":"test","contentPacks":[{"id":"test.pack","version":1}],"tuning":{"id":"test.tuning","version":1}}"""),
        File("worldrpg/content-packs/test.pack.json", """{"kind":"worldrpg.content-pack","schemaVersion":1,"id":"test.pack","version":1,"ruleset":"test","dependencies":[],"payload":"payload/pack.json"}"""),
        File("worldrpg/tuning/test.tuning.json", """{"kind":"worldrpg.tuning-profile","schemaVersion":1,"id":"test.tuning","version":1,"ruleset":"test","payload":"payload/tuning.json"}"""),
        File("payload/pack.json", "{}"),
        File("payload/tuning.json", "{}"),
    ];

    private static ProductContentFile File(string path, string value) =>
        new(System.Text.Encoding.UTF8.GetBytes(path), System.Text.Encoding.UTF8.GetBytes(value));

    private static IEngineContext Engine() => DispatchProxy.Create<IEngineContext, PersistenceContextProxy>();

    private static ProductInputConfiguration EmptyInput() => new(
        new InputBinding(1, 1, 1),
        new InputContext("test"u8.ToArray()),
        Array.Empty<ProductInputDescriptor>(), Array.Empty<ProductInputMapping>());

    /// <summary>A composition-resolution context: these tests never reach a persistence member.</summary>
    private class PersistenceContextProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new NotSupportedException($"Unexpected Engine service member '{targetMethod?.Name}'.");
    }

}
