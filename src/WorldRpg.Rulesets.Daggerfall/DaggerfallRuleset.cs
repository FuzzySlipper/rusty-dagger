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
        DaggerfallSavePayload payload = DaggerfallSavePayload.Decode(saved);
        // Resolve only detached content definitions before admitting a fresh
        // session; malformed actor/inventory/corpse references never reach
        // Engine-backed construction.
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(context.Composition.RequireContentPack(BasePack).Payload);
        ContentPack pack = context.Composition.RequireContentPack(PrivateersHoldPack);
        PrivateersHoldInputs inputs = PrivateersHoldContent.Read(context.Composition.Content, pack.Payload, definitions);
        DaggerfallTuning tuning = DaggerfallTuning.Read(context.Composition.Tuning.Payload.Span);
        payload.ValidateForRestore(definitions, inputs, tuning, context.Engine.Random);
        return new DaggerfallSession(
            context.Engine,
            context.CompositionIdentity,
            definitions,
            inputs,
            tuning,
            payload);
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
            saved);
    }
}
