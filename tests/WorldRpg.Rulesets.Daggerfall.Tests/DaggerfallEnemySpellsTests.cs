using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallEnemySpellsTests
{
    [Fact]
    public void Every_authored_list_resolves_classic_identity_and_retained_caster_stats_restore_without_refill()
    {
        using ConditionSessionFixture fixture = new();
        var definitions = fixture.Definitions;
        Assert.Equal(new[] { 1,18,21,23,25,26,27,28,29,30,31,32,33 }, definitions.EnemySpells.MobileLists.Keys.Order());
        Assert.Equal(7, definitions.EnemySpells.ClassTiers.Length);
        Assert.Equal(new[] { 7,10,29,44 }, definitions.EnemySpells.MobileLists[1].Select(key => definitions.Magic.Spells[key].Identity));
        Assert.Equal("spell.021", definitions.EnemySpells.MobileLists[21].Single(key => definitions.Magic.Spells[key].Identity == 22));
        List<long> casters = [];
        foreach (var mobile in definitions.Mobiles.Mobiles.Values.Where(mobile => definitions.EnemySpells.IsCaster(mobile.DonorId)))
        {
            var definition = definitions.RequireActor(DaggerfallEncounterActors.ActorFor(mobile));
            long id = fixture.Session.SpawnActor(definition.Id.Value, new(new WorldPoint(10,0,10), 0), 20);
            var actor = fixture.Session.State.Actors.Get(id);
            var magicka = actor.Stats.GetTrack(TrackId.Parse("magicka"));
            Assert.Equal(300, magicka.Maximum.Value);
            Assert.Equal(300, magicka.Current);
            foreach (string school in DaggerfallEnemySpells.Schools) Assert.Equal(80, actor.Stats.GetStat(StatId.Parse(school)).Value);
            magicka.SetCurrent(1);
            casters.Add(id);
        }
        using var restored = fixture.Restore(fixture.Session.CaptureSave());
        foreach (long id in casters)
        {
            Assert.Equal(20, restored.DefinitionsByActor[id].Level);
            Assert.Equal(1, restored.State.Actors.Get(id).Stats.GetTrack(TrackId.Parse("magicka")).Current);
            Assert.Equal(300, restored.State.Actors.Get(id).Stats.GetTrack(TrackId.Parse("magicka")).Maximum.Value);
        }
    }

    [Theory]
    [InlineData(1,0)] [InlineData(2,0)] [InlineData(3,1)] [InlineData(5,1)] [InlineData(6,2)]
    [InlineData(9,3)] [InlineData(12,4)] [InlineData(15,5)] [InlineData(18,6)] [InlineData(30,6)]
    public void Class_tiers_use_exact_admitted_level_before_skill_cap(int level, int tier)
    {
        var d = TestPayload.Definitions;
        int mobile = d.EnemySpells.ClassCasters.First();
        var actor = d.RequireActor(DaggerfallEncounterActors.ActorFor(d.Mobiles.Mobiles[mobile]));
        actor = DaggerfallEncounterActors.AtLevel(actor, d.Vocabulary, level);
        Assert.Equal(d.EnemySpells.ClassTiers[tier], d.EnemySpells.For(actor));
    }
}
