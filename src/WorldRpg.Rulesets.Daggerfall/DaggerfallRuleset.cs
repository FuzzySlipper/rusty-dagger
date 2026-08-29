using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

public sealed class DaggerfallRuleset : IGameRuleset
{
    public static readonly RulesetId Identity = new("daggerfall");
    internal static readonly ContentPackId BasePack = new("daggerfall.base");
    internal static readonly ContentPackId PrivateersHoldPack = new("daggerfall.privateers-hold");

    public RulesetId Id => Identity;

    public IGameSession CreateSession(GameSessionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Composition.Ruleset != Identity)
            throw new InvalidOperationException($"Daggerfall cannot interpret ruleset '{context.Composition.Ruleset.Value}'.");
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(context.Composition.RequireContentPack(BasePack).Payload);
        ContentPack pack = context.Composition.RequireContentPack(PrivateersHoldPack);
        return new DaggerfallSession(
            context.Engine,
            definitions,
            PrivateersHoldContent.Read(context.Composition.Content, pack.Payload, definitions),
            DaggerfallTuning.Read(context.Composition.Tuning.Payload.Span));
    }
}
