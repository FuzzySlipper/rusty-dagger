using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Ai;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Behavior;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>Which compiled tactic decides for an actor, by the tactic its mobile's record names.</summary>
public sealed class DaggerfallEnemyTacticsTests
{
    private sealed class Named(string name) : IDaggerfallEnemyTactic
    {
        public override string ToString() => name;
        public PursuitPolicy Move(DaggerfallEnemyMoveObservation observation, List<DaggerfallDecisionGate>? gates) => throw new NotSupportedException();
        public DaggerfallEnemyActionChoice Act(DaggerfallEnemyActionObservation observation, List<DaggerfallDecisionGate>? gates) => throw new NotSupportedException();
        public void Admitted(ActorState actor, DaggerfallEnemyActionChoice choice, DaggerfallEnemyAdmission admission) { }
    }

    private static readonly Named Legacy = new(DaggerfallMobileDefinition.LegacyTactic), Beast = new("beast");

    [Fact]
    public void An_actor_fights_with_its_mobiles_tactic_and_one_no_mobile_describes_with_the_legacy_tactic()
    {
        DaggerfallMobileCatalogSet mobiles = TestPayload.Definitions.Mobiles;
        DaggerfallMobileDefinition rat = mobiles.Mobiles[0] with { Tactic = "beast" };
        DaggerfallMobileCatalogSet withBeast = mobiles with { Mobiles = new Dictionary<int, DaggerfallMobileDefinition>(mobiles.Mobiles) { [0] = rat } };
        Dictionary<long, DaggerfallActorDefinition> actors = new()
        {
            [1] = TestPayload.Definitions.Actors[new DaggerfallActorId("rat")],
            [2] = TestPayload.Definitions.Actors[new DaggerfallActorId("imp")],
            [3] = TestPayload.Definitions.Actors[new DaggerfallActorId("rat")] with { MobileId = null },
        };
        DaggerfallEnemyTactics tactics = new(new Dictionary<string, IDaggerfallEnemyTactic> { ["legacy"] = Legacy, ["beast"] = Beast }, withBeast,
            actors.GetValueOrDefault);

        Assert.Same(Beast, tactics.For(1));
        Assert.Same(Legacy, tactics.For(2));
        Assert.Same(Legacy, tactics.For(3));
        Assert.Same(Legacy, tactics.For(404));
    }

    [Fact]
    public void A_mobile_naming_a_tactic_the_ruleset_does_not_compile_is_refused()
    {
        DaggerfallMobileCatalogSet mobiles = TestPayload.Definitions.Mobiles;
        DaggerfallMobileCatalogSet withBeast = mobiles with
        {
            Mobiles = new Dictionary<int, DaggerfallMobileDefinition>(mobiles.Mobiles) { [0] = mobiles.Mobiles[0] with { Tactic = "beast" } },
        };

        InvalidOperationException unknown = Assert.Throws<InvalidOperationException>(() =>
            new DaggerfallEnemyTactics(new Dictionary<string, IDaggerfallEnemyTactic> { ["legacy"] = Legacy }, withBeast, _ => null));
        Assert.Contains("Mobile 0 ('Rat') names tactic 'beast'", unknown.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() =>
            new DaggerfallEnemyTactics(new Dictionary<string, IDaggerfallEnemyTactic> { ["beast"] = Beast }, mobiles, _ => null));
    }
}
