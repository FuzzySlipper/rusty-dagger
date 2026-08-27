using RustyDagger.Game.Daggerfall;
using RustyDagger.Game.Facts;
using RustyDagger.Game.Modules.Combat;
using Xunit;

namespace Dagger.Game.Tests;

public sealed class ProductFactsTests
{
    [Fact]
    public void LateUpdate_delivers_a_stable_buffer_and_defers_reaction_facts()
    {
        ProductFactBuffer buffer = new();
        List<string> delivered = [];
        buffer.Append(new ActorDiedFact(2000, 16, 7));
        buffer.Deliver(fact => { delivered.Add(fact.GetType().Name); buffer.Append(new LootAwardedFact(2000, "gold-piece", 2, 7)); });
        Assert.Equal(["ActorDiedFact"], delivered);
        buffer.Deliver(fact => delivered.Add(fact.GetType().Name));
        Assert.Equal(["ActorDiedFact", "LootAwardedFact"], delivered);
    }

    [Fact]
    public void Keyed_rng_labels_are_step_scoped_and_stable()
    {
        Assert.Equal("step:7:hit:2000", CombatKeys.PlayerHit(7, 2000));
        Assert.Equal("step:7:damage:2000", CombatKeys.PlayerDamage(7, 2000));
        Assert.Equal("step:7:enemy-hit:2000", CombatKeys.EnemyHit(7, 2000));
        Assert.Equal("step:7:loot:2000:H", LootRandomKey.For(7, 2000, "H"));
    }
}
