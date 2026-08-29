using System.Reflection;
using Rusty.Engine;
using WorldRpg.Host;
using WorldRpg.Kit;
using WorldRpg.Kit.Presentation;
using Xunit;

namespace WorldRpg.Rulesets.Canary;

public sealed class CanaryRulesetTests
{
    [Fact]
    public void Host_runs_one_admitted_update_and_disposes_the_canary_session()
    {
        CanaryRuleset ruleset = new();
        ProductCreateContext context = new(EngineContextDouble.Create(), new ProductContent(ReadOnlyMemory<ProductContentFile>.Empty), EmptyInputConfiguration());
        ProductUpdate update = new(AdmittedRealtimeFacts(), ReadOnlySpan<ProductInputEvent>.Empty);

        using (WorldRpgProduct product = new(context, ruleset))
        {
            product.Start();

            Assert.Equal(ProductTurnRequest.None, product.Update(update));
        }

        CanarySession session = Assert.IsType<CanarySession>(ruleset.CreatedSession);
        Assert.Equal(2, session.InitialPublishCount);
        Assert.Equal((uint)1, session.AppliedStepCount);
        Assert.NotEqual(0, session.Hud.Nodes.Length);
        Assert.True(session.IsDisposed);
    }

    [Fact]
    public void Scenario_stays_deliberately_incompatible_with_the_reference_ruleset()
    {
        CanaryScenario scenario = CanaryScenario.SingleRoom;

        Assert.Equal(["might", "finesse"], scenario.Statistics);
        Assert.Equal(["vitality", "focus"], scenario.Resources);
        Assert.Equal("weapon", scenario.WeaponSlot);
        Assert.Equal("reach", scenario.CombatStyle);
        Assert.Equal("observatory", scenario.Room);
        Assert.Equal("warden", scenario.Actor);
        Assert.False(scenario.HasCurrency);
    }

    private static ProductUpdateFacts AdmittedRealtimeFacts() => new(
        ProductTurnKind.Realtime,
        ProductLifecycleState.Running,
        1,
        1,
        1,
        1,
        60,
        1,
        0,
        .125d);

    private static ProductInputConfiguration EmptyInputConfiguration() => new(
        default,
        default,
        ReadOnlyMemory<ProductInputDescriptor>.Empty,
        ReadOnlyMemory<ProductInputMapping>.Empty);

    private class EngineContextDouble : DispatchProxy
    {
        internal static IEngineContext Create() => DispatchProxy.Create<IEngineContext, EngineContextDouble>();

        protected override object? Invoke(MethodInfo? method, object?[]? args) =>
            throw new NotSupportedException($"Canary session does not use Engine service {method?.Name}.");
    }
}

public sealed class CanaryRuleset : IGameRuleset
{
    public static readonly RulesetId Identity = new("canary");

    public RulesetId Id => Identity;
    internal IGameSession? CreatedSession { get; private set; }

    public IGameSession CreateSession(GameSessionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        CanarySession session = new(CanaryScenario.SingleRoom);
        CreatedSession = session;
        return session;
    }
}

internal sealed class CanarySession : IGameSession
{
    private bool _disposed;

    internal CanarySession(CanaryScenario scenario)
    {
        Scenario = scenario;
        UiValueBuilder values = new();
        uint vitality = values.String(scenario.Resources[0]);
        uint focus = values.String(scenario.Resources[1]);
        uint resources = values.Array(vitality, focus);
        Hud = values.Build(values.Object(("resources", resources)));
    }

    internal CanaryScenario Scenario { get; }
    internal UiValue Hud { get; }
    internal int InitialPublishCount { get; private set; }
    internal uint AppliedStepCount { get; private set; }
    internal bool IsDisposed => _disposed;

    public void PublishInitial()
    {
        ThrowIfDisposed();
        InitialPublishCount++;
    }

    public ProductTurnRequest Update(ProductUpdate update)
    {
        ThrowIfDisposed();
        if (update.Facts.LifecycleState == ProductLifecycleState.Running
            && update.Facts.Mode == ProductTurnKind.Realtime
            && update.Facts.AdmittedStepCount > 0)
        {
            AppliedStepCount = checked(AppliedStepCount + update.Facts.AdmittedStepCount);
        }

        return ProductTurnRequest.None;
    }

    public void Dispose() => _disposed = true;

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(CanarySession));
    }
}

internal sealed record CanaryScenario(
    IReadOnlyList<string> Statistics,
    IReadOnlyList<string> Resources,
    bool HasCurrency,
    string WeaponSlot,
    string CombatStyle,
    string Room,
    string Actor)
{
    internal static readonly CanaryScenario SingleRoom = new(
        ["might", "finesse"],
        ["vitality", "focus"],
        HasCurrency: false,
        WeaponSlot: "weapon",
        CombatStyle: "reach",
        Room: "observatory",
        Actor: "warden");
}
