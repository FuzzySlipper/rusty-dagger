using WorldRpg.Rulesets.Daggerfall;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Kit.Facts;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class ProductFactsTests
{
    [Fact]
    public void LateUpdate_delivers_a_stable_buffer_and_defers_reaction_facts()
    {
        FactBuffer<IProductFact> buffer = new();
        List<string> delivered = [];
        buffer.Append(new ActorDiedFact(2000, 1, DaggerfallDamageCause.PhysicalAttack, 16, 16d, 1, 7));
        buffer.Deliver(fact => { delivered.Add(fact.GetType().Name); buffer.Append(new LootAwardedFact(2000, "gold-piece", 2, 7)); });
        Assert.Equal(["ActorDiedFact"], delivered);
        buffer.Deliver(fact => delivered.Add(fact.GetType().Name));
        Assert.Equal(["ActorDiedFact", "LootAwardedFact"], delivered);
    }

    [Fact]
    public void Damage_display_rounds_the_exact_health_delta_for_classic_text()
    {
        Assert.Equal(1, DaggerfallFormulaPolicy.DisplayDamage(1.75d));
        Assert.Equal(0, DaggerfallFormulaPolicy.DisplayDamage(0.5d));
    }

    [Fact]
    public void Loot_rng_labels_are_step_scoped_and_stable()
    {
        Assert.Equal("generation:1:step:7:loot:2000:H", LootRandomKey.For(1, 7, 2000, "H"));
    }
}
