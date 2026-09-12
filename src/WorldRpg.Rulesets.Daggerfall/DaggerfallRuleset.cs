using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

public sealed class DaggerfallRuleset : ISaveableGameRuleset
{
    public static readonly RulesetId Identity = new("daggerfall");
    internal static readonly ContentPackId BasePack = new("daggerfall.base");
    internal static readonly ContentPackId PrivateersHoldPack = new("daggerfall.privateers-hold");

    public RulesetId Id => Identity;

    public IGameSession CreateSession(GameSessionContext context)
        => CreateSessionCore(context, saved: null);

    public IGameSession CreateSession(GameSessionContext context, RulesetSavePayload saved)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(saved);
        if (context.Composition.Ruleset != Identity)
            throw new InvalidOperationException($"Daggerfall cannot interpret ruleset '{context.Composition.Ruleset.Value}'.");
        // Decode all detached ruleset data before session construction creates
        // any Engine-owned state.
        DaggerfallSaveRead read = DaggerfallSavePayload.Read(saved);
        // Resolve only detached content definitions before admitting a fresh
        // session; malformed actor/inventory/corpse references never reach
        // Engine-backed construction.
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(context.Composition.RequireContentPack(BasePack).Payload);
        ContentPack pack = context.Composition.RequireContentPack(PrivateersHoldPack);
        PrivateersHoldInputs inputs = PrivateersHoldContent.Read(context.Composition.Content, pack.Payload, definitions);
        DaggerfallTuning tuning = DaggerfallTuning.Read(context.Composition.Tuning.Payload.Span);
        // Reference resolution reports what the selected content cannot explain rather
        // than refusing an otherwise restorable save; only an uninterpretable payload
        // reaches the caller as an error.
        DaggerfallRestorePlan plan = read.Payload.ResolveRestore(definitions, inputs, tuning, context.Engine.Random);
        return new DaggerfallSession(
            context.Engine,
            context.CompositionIdentity,
            definitions,
            inputs,
            tuning,
            plan.Payload,
            [.. read.Notices, .. plan.Notices]);
    }

    private static IGameSession CreateSessionCore(GameSessionContext context, DaggerfallSavePayload? saved)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Composition.Ruleset != Identity)
            throw new InvalidOperationException($"Daggerfall cannot interpret ruleset '{context.Composition.Ruleset.Value}'.");
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(context.Composition.RequireContentPack(BasePack).Payload);
        ContentPack pack = context.Composition.RequireContentPack(PrivateersHoldPack);
        return new DaggerfallSession(
            context.Engine,
            context.CompositionIdentity,
            definitions,
            PrivateersHoldContent.Read(context.Composition.Content, pack.Payload, definitions),
            DaggerfallTuning.Read(context.Composition.Tuning.Payload.Span),
            saved,
            restoreNotices: []);
    }
}
