using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallGameRandomTests
{
    [Fact]
    public void A_keyed_draw_takes_the_games_seed_with_its_own_and_the_worlds_streams_pass_through_unmixed()
    {
        KeyedRandomFake engine = KeyedRandomFake.Create(0);
        KeyedRngRequest draw = new(7, "daggerfall.encounter.v1", "generation:1:sequence:1:roll", 0, 99);
        ulong Drawn(ulong game)
        {
            new DaggerfallGameRandom(engine.Service, game).DrawKeyed(draw);
            KeyedRngRequest sent = engine.Requests[^1];
            Assert.Equal(draw with { Seed = sent.Seed }, sent);
            return sent.Seed;
        }

        // Another game draws the same key from another seed; the same game draws it from the same one, as a load does.
        Assert.NotEqual(Drawn(1), Drawn(2));
        Assert.Equal(Drawn(1), Drawn(1));
        // A zero game seed leaves the draw's own seed as it was.
        Assert.Equal(7UL, Drawn(0));

        // Fresh games get seeds no other shares.
        Assert.Equal(64, Enumerable.Range(0, 64).Select(_ => DaggerfallGameRandom.NewSeed()).Distinct().Count());

        // The world's streams (terrain by cell) and the classic LCG (building names) are the same in every game.
        IRandomService minimum = RandomMinimum.Create();
        Lcg15Request lcg = new(12345, 100);
        Assert.Equal(minimum.DrawLcg15(lcg), new DaggerfallGameRandom(minimum, 1).DrawLcg15(lcg));
        Assert.Equal(minimum.DrawLcg15(lcg), new DaggerfallGameRandom(minimum, 2).DrawLcg15(lcg));
    }
}
