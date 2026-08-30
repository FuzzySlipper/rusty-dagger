using WorldRpg.Rulesets.Daggerfall;
using WorldRpg.Rulesets.Daggerfall.Facts;
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
        buffer.Append(new ActorDiedFact(2000, 1, 16, 1, 7));
        buffer.Deliver(fact => { delivered.Add(fact.GetType().Name); buffer.Append(new LootAwardedFact(2000, "gold-piece", 2, 7)); });
        Assert.Equal(["ActorDiedFact"], delivered);
        buffer.Deliver(fact => delivered.Add(fact.GetType().Name));
        Assert.Equal(["ActorDiedFact", "LootAwardedFact"], delivered);
    }

    [Fact]
    public void Loot_rng_labels_are_step_scoped_and_stable()
    {
        Assert.Equal("generation:1:step:7:loot:2000:H", LootRandomKey.For(1, 7, 2000, "H"));
    }
}
