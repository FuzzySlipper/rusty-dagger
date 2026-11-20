using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Ai;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Behavior;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>Which compiled tactic decides for an actor, by the tactic its mobile's record names.</summary>
public sealed class DaggerfallEnemyTacticsTests
{
    private sealed class Named(string name, bool weighsTendencies = false) : IDaggerfallEnemyTactic
    {
        public override string ToString() => name;
        public bool WeighsTendencies => weighsTendencies;
        public PursuitPolicy Move(DaggerfallEnemyMoveObservation observation, List<DaggerfallDecisionGate>? gates) => throw new NotSupportedException();
        public DaggerfallEnemyActionChoice Act(DaggerfallEnemyActionObservation observation, List<DaggerfallDecisionGate>? gates) => throw new NotSupportedException();
        public void Admitted(ActorState actor, DaggerfallEnemyActionChoice choice, DaggerfallEnemyAdmission admission) { }
    }

    private static readonly Named Legacy = new(DaggerfallMobileDefinition.LegacyTactic), Beast = new("beast"), Intelligent = new("intelligent", weighsTendencies: true);

    /// <summary>The tactics the shipped mobiles name, so a test can change one record and leave the rest valid.</summary>
    private static Dictionary<string, IDaggerfallEnemyTactic> Compiled() => new() { ["legacy"] = Legacy, ["beast"] = Beast, ["intelligent"] = Intelligent };

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
        DaggerfallEnemyTactics tactics = new(Compiled(), withBeast, actors.GetValueOrDefault);

        Assert.Same(Beast, tactics.For(1));
        Assert.Same(Legacy, tactics.For(2));
        Assert.Same(Legacy, tactics.For(3));
        Assert.Same(Legacy, tactics.For(404));
    }

    [Fact]
    public void A_mobile_naming_a_tactic_the_ruleset_does_not_compile_is_refused()
    {
        DaggerfallMobileCatalogSet mobiles = TestPayload.Definitions.Mobiles;
        DaggerfallMobileCatalogSet withWolfpack = mobiles with
        {
            Mobiles = new Dictionary<int, DaggerfallMobileDefinition>(mobiles.Mobiles) { [0] = mobiles.Mobiles[0] with { Tactic = "wolfpack" } },
        };

        InvalidOperationException unknown = Assert.Throws<InvalidOperationException>(() => new DaggerfallEnemyTactics(Compiled(), withWolfpack, _ => null));
        Assert.Contains("Mobile 0 ('Rat') names tactic 'wolfpack'", unknown.Message, StringComparison.Ordinal);
        Dictionary<string, IDaggerfallEnemyTactic> noLegacy = Compiled();
        noLegacy.Remove("legacy");
        Assert.Throws<InvalidOperationException>(() => new DaggerfallEnemyTactics(noLegacy, mobiles, _ => null));
    }

    [Fact]
    public void Tendencies_authored_for_a_tactic_that_does_not_weigh_them_are_refused()
    {
        DaggerfallMobileCatalogSet mobiles = TestPayload.Definitions.Mobiles;
        DaggerfallTacticTendencies bold = new(1d, 0d);
        DaggerfallMobileCatalogSet With(string tactic) => mobiles with
        {
            Mobiles = new Dictionary<int, DaggerfallMobileDefinition>(mobiles.Mobiles) { [0] = mobiles.Mobiles[0] with { Tactic = tactic, Tendencies = bold } },
        };

        // The beast preset's tendencies are fixed, and the legacy tactic weighs none: either would drop them silently.
        foreach (string tactic in new[] { "beast", "legacy" })
            Assert.Contains("authors tendencies", Assert.Throws<InvalidOperationException>(() => new DaggerfallEnemyTactics(Compiled(), With(tactic), _ => null)).Message,
                StringComparison.Ordinal);
        _ = new DaggerfallEnemyTactics(Compiled(), With("intelligent"), _ => null);
    }
}
