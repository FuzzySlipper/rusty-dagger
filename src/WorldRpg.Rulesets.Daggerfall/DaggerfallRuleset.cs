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
        => CreateSessionCore(context);

    public IGameSession CreateSession(GameSessionContext context, RulesetSavePayload saved)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(saved);
        if (context.Composition.Ruleset != Identity)
            throw new InvalidOperationException($"Daggerfall cannot interpret ruleset '{context.Composition.Ruleset.Value}'.");
        // Decode all detached ruleset data before session construction creates
        // any Engine-owned state.

        // Resolve only detached content definitions before admitting a fresh
        // session; malformed actor/inventory/corpse references never reach
        // Engine-backed construction.
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(context.Composition.RequireContentPack(BasePack).Payload);
        ContentPack pack = context.Composition.RequireContentPack(PrivateersHoldPack);
        PrivateersHoldInputs inputs = PrivateersHoldContent.Read(context.Composition.Content, pack.Payload, definitions);
        DaggerfallTuning tuning = DaggerfallTuning.Read(context.Composition.Tuning.Payload.Span);
        // Reading and reference resolution happen before any Engine-owned state exists.
        // What the selected content cannot explain is reported rather than refused; only
        // an uninterpretable payload reaches the caller as an error.
        return DaggerfallSession.Restore(
            context.Engine,
            context.CompositionIdentity,
            definitions,
            inputs,
            tuning,
            saved,
            context.Engine.Random);
    }

    /// <summary>A fresh session: no saved state exists, so nothing is resolved or reported.</summary>
    private static IGameSession CreateSessionCore(GameSessionContext context)
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
            DaggerfallTuning.Read(context.Composition.Tuning.Payload.Span));
    }
}
