using WorldRpg.Kit;

namespace WorldRpg.Rulesets.Daggerfall;

public sealed class DaggerfallRuleset : IGameRuleset
{
    public static readonly RulesetId Identity = new("daggerfall");

    public RulesetId Id => Identity;

    public IGameSession CreateSession(GameSessionContext context) => new DaggerfallSession(context.Engine, context.Content);
}
