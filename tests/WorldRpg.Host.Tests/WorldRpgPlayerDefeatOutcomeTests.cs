using System.Reflection;
using Rusty.Engine;
using WorldRpg.Host;
using WorldRpg.Kit;
using Xunit;

namespace WorldRpg.Host.Tests;

public sealed class WorldRpgPlayerDefeatOutcomeTests
{
    [Fact]
    public void New_game_replaces_a_dead_session_and_returns_to_play()
    {
        OutcomeRuleset ruleset = new();
        using WorldRpgProduct product = new(Context(), ruleset, new GameBundleId("test.bundle"));
        product.Start();
        product.Begin();
        OutcomeSession defeated = ruleset.Current!;
        Assert.True(product.MarkDead().Changed);

        defeated.Request = new(PlayerDefeatOutcome.NewGame);
        product.Update(Update(1));

        // The choice puts the loading screen up in its own update; the new game is built on the updates after.
        Assert.Equal("Starting a new game", product.Loading?.Title);
        Assert.Equal(ProductMode.Dead, product.Mode);
        Assert.False(defeated.Disposed);
        RunLoading(product);

        Assert.Equal(ProductMode.Playing, product.Mode);
        Assert.Equal(2, ruleset.Created);
        Assert.True(defeated.Disposed);
        Assert.Equal("New game started.", ruleset.Current!.Outcome);
        Assert.Equal(ProductMode.Playing, ruleset.Current.AppliedMode);
    }

    [Fact]
    public void Quit_from_defeat_replaces_the_session_at_the_title()
    {
        OutcomeRuleset ruleset = new();
        using WorldRpgProduct product = new(Context(), ruleset, new GameBundleId("test.bundle"));
        product.Start();
        product.Begin();
        OutcomeSession defeated = ruleset.Current!;
        product.MarkDead();

        defeated.Request = new(PlayerDefeatOutcome.QuitToTitle);
        product.Update(Update(1));

        // Return to menu shows the loading screen at once and stays on it until the title is ready.
        Assert.Equal("Returning to the title", product.Loading?.Title);
        Assert.Equal(ProductMode.Dead, product.Mode);
        Assert.Equal(1, ruleset.Created);
        Assert.False(defeated.Disposed);
        product.Update(Update(2));
        Assert.Equal("Leaving the world", product.Loading?.Label);
        Assert.Equal(2, ruleset.Created);
        Assert.False(defeated.Disposed);
        RunLoading(product);

        Assert.Equal(ProductMode.Title, product.Mode);
        Assert.Equal(2, ruleset.Created);
        Assert.True(defeated.Disposed);
        Assert.Equal("Returned to the title.", ruleset.Current!.Outcome);
        Assert.Equal(ProductMode.Title, ruleset.Current.AppliedMode);
    }

    [Fact]
    public void Load_without_a_selected_slot_keeps_the_product_dead_and_reports_the_choice()
    {
        OutcomeRuleset ruleset = new();
        using WorldRpgProduct product = new(Context(), ruleset, new GameBundleId("test.bundle"));
        product.Start();
        product.Begin();
        OutcomeSession defeated = ruleset.Current!;
        product.MarkDead();

        defeated.Request = new(PlayerDefeatOutcome.Load);
        product.Update(Update(1));

        Assert.Equal(ProductMode.Dead, product.Mode);
        Assert.Equal(1, ruleset.Created);
        Assert.Same(defeated, ruleset.Current);
        Assert.Equal("Choose a saved game to load.", defeated.Outcome);
    }

    [Fact]
    public void A_stale_defeat_choice_is_reported_without_replacing_a_live_session()
    {
        OutcomeRuleset ruleset = new();
        using WorldRpgProduct product = new(Context(), ruleset, new GameBundleId("test.bundle"));
        product.Start();
        product.Begin();
        OutcomeSession current = ruleset.Current!;
        current.Request = new(PlayerDefeatOutcome.NewGame);

        product.Update(Update(1));

        Assert.Equal(ProductMode.Playing, product.Mode);
        Assert.Equal(1, ruleset.Created);
        Assert.Same(current, ruleset.Current);
        Assert.Equal("The defeat choices are no longer available.", current.Outcome);
    }

    /// <summary>Gives the product updates until its loading screen clears.</summary>
    private static void RunLoading(WorldRpgProduct product)
    {
        for (ulong step = 10; product.Loading is not null; step++)
        {
            Assert.True(step < 20, "The loading screen did not clear.");
            product.Update(Update(step));
        }
    }

    private static ProductUpdate Update(ulong step) => new(
        new ProductUpdateFacts(ProductLifecycleState.Running,
            step, step, step, step, 60, 1, 0, 1d / 60d), ReadOnlySpan<ProductInputEvent>.Empty);

    private static ProductCreateContext Context() => new(
        DispatchProxy.Create<IEngineContext, EngineProxy>(),
        new ProductContent(ContentFiles()),
        new ProductInputConfiguration(new InputBinding(1, 1, 1), new InputContext("test"u8.ToArray()),
            Array.Empty<ProductInputDescriptor>(), Array.Empty<ProductInputMapping>()));

    private static ProductContentFile[] ContentFiles() =>
    [
        File("worldrpg/bundles/test.bundle.json", "{\"kind\":\"worldrpg.game-bundle\",\"id\":\"test.bundle\",\"ruleset\":\"test\",\"contentPacks\":[{\"id\":\"test.pack\"}],\"tuning\":{\"id\":\"test.tuning\"}}"),
        File("worldrpg/packs/test/pack.json", "{\"kind\":\"worldrpg.content-pack\",\"id\":\"test.pack\",\"ruleset\":\"test\",\"role\":\"test.content\",\"dependencies\":[],\"payload\":\"payload/content.json\"}"),
        File("worldrpg/tuning/test/tuning.json", "{\"kind\":\"worldrpg.tuning-profile\",\"id\":\"test.tuning\",\"ruleset\":\"test\",\"payload\":\"payload/values.json\"}"),
        File("payload/content.json", "{}"),
        File("payload/values.json", "{}"),
    ];

    private static ProductContentFile File(string path, string value) =>
        new(System.Text.Encoding.UTF8.GetBytes(path), System.Text.Encoding.UTF8.GetBytes(value));

    private sealed class OutcomeRuleset : IGameRuleset
    {
        public RulesetId Id => new("test");
        internal int Created { get; private set; }
        internal OutcomeSession Current { get; private set; } = null!;

        public IGameSession CreateSession(GameSessionContext context)
        {
            Created++;
            Current = new OutcomeSession();
            return Current;
        }
    }

    private sealed class OutcomeSession : IPlayerDefeatOutcomeSession, IModeAwareGameSession
    {
        internal PlayerDefeatOutcomeRequest? Request { get; set; }
        internal string? Outcome { get; private set; }
        internal ProductMode? AppliedMode { get; private set; }
        internal bool Disposed { get; private set; }

        public ProductMode? PendingModeRequest => null;
        public bool PendingModeRequestClosesModal => false;
        public void ApplyProductMode(ProductMode mode) => AppliedMode = mode;
        public PlayerDefeatOutcomeRequest? TakePlayerDefeatOutcomeRequest()
        {
            PlayerDefeatOutcomeRequest? request = Request;
            Request = null;
            return request;
        }
        public void ReportPlayerDefeatOutcome(string message) => Outcome = message;
        public void PublishInitial() { }
        public ProductUpdateResult Update(ProductUpdate update) => ProductUpdateResult.None;
        public void Dispose() => Disposed = true;
    }

    private class EngineProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new NotSupportedException($"Unexpected Engine service member '{targetMethod?.Name}'.");
    }
}
