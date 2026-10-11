using Rusty.Engine;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Actors;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;
using WorldRpg.Rulesets.Daggerfall.Modules.Behavior;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class FixedDungeonEnemySessionTests
{
    [Fact]
    public void Published_site_binds_fixed_markers_once_and_preserves_scenario_identity()
    {
        var profile = ReadInputs(TestData.RepositoryRoot);
        Assert.Equal(42, profile.Project.Actors.Count);
        Assert.Equal("skeletal-warrior", profile.Project.Actors[2000].ActorId.Value);
        using var session = FreshSession();
        foreach (var placement in profile.Project.Actors.Values)
        {
            var memory = session.State.Actors.Get(placement.EntityId).Actor.Get<DaggerfallEnemyPerceptionMemory>();
            Assert.Equal(placement.SpawnDistance, memory.SpawnDistance);
            Assert.Equal(placement.Passive, memory.Passive);
        }
    }

    [Fact]
    public void Published_block_fixed_passive_enemy_stays_idle_and_restores_its_disposition()
    {
        var world = new AssembledWorld();
        var definitions = world.Definitions;
        var location = definitions.Locations.Records.First(record => record.DungeonBlocks.Any(block => block.SourceKey == "S0000081.RDB"));
        var profile = world.Profiles.Require(DaggerfallWorldProfileIds.Dungeon(location.Id));
        var placement = profile.Project.Actors.Values.First(actor => actor.Passive);
        var female = profile.Project.Actors.Values.First(actor => actor.Gender == "female");
        Assert.Contains("-female.png", profile.ActorSprites[female.EntityId].TexturePath, StringComparison.Ordinal);
        List<string> releases = [];
        var content = new ContentFake(releases);
        PopulateContent(content, profile);
        var spatial = SpatialFake.Create(world.Source.SpatialArtifact.Sha256, releases);
        var perception = PerceptionFake.Create();
        perception.Receipt = Receipt(new PerceptionPair((ulong)placement.EntityId, 1, 1, 1, PerceptionPairKind.Visible, 1));
        var engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), perception.Service);
        var configuration = new DaggerfallSessionComposition(definitions, profile, DaggerfallTuning.Defaults);
        using var session = DaggerfallSession.StartNew(engine.Context, configuration);
        var memory = session.State.Actors.Get(placement.EntityId).Actor.Get<DaggerfallEnemyPerceptionMemory>();
        Assert.True(memory.Passive);
        Assert.Equal(placement.SpawnDistance, memory.SpawnDistance);
        session.Update(new ProductUpdate(OuterUpdate(1), []));
        Assert.True(memory.Pacified);
        Assert.DoesNotContain(session.LastEnemyBehavior.Values, evidence => evidence.ActorId == placement.EntityId
            && evidence.State is EnemyBehaviorState.Attack or EnemyBehaviorState.Chase);
        var save = session.CaptureSave();
        session.Dispose();
        using var restored = DaggerfallSession.Restore(engine.Context, configuration, save);
        var restoredMemory = restored.State.Actors.Get(placement.EntityId).Actor.Get<DaggerfallEnemyPerceptionMemory>();
        Assert.True(restoredMemory.Passive);
        Assert.True(restoredMemory.Pacified);
        Assert.Equal(placement.SpawnDistance, restoredMemory.SpawnDistance);
        restoredMemory.SetForcedHostile(true);
        Assert.False(restoredMemory.Pacified);
        var provokedSave = restored.CaptureSave();
        restored.Dispose();
        using var provoked = DaggerfallSession.Restore(engine.Context, configuration, provokedSave);
        Assert.False(provoked.State.Actors.Get(placement.EntityId).Actor.Get<DaggerfallEnemyPerceptionMemory>().Pacified);
    }

    [Fact]
    public void Gender_selects_the_female_atlas_and_keeps_the_corpse()
    {
        var profile = ReadInputs(TestData.RepositoryRoot);
        var male = profile.MobileSprites[141];
        var female = male.ForGender("female");
        Assert.NotEqual(male.TexturePath, female.TexturePath);
        Assert.Contains("-female.png", female.TexturePath, StringComparison.Ordinal);
        Assert.Equal(male.Corpse, female.Corpse);
        foreach (var actor in profile.Project.Actors.Values.Where(actor => actor.Gender == "female"))
            Assert.Contains("-female.png", profile.ActorSprites[actor.EntityId].TexturePath, StringComparison.Ordinal);
    }

    [Fact]
    public void Unflagged_human_spawns_take_both_genders_and_keep_them_across_save()
    {
        using var f = new SanguineRoseSessionTests.Fixture(random: GenderRandom.Create());
        // Random human enemies draw from every human class mobile; each publishes its female atlas.
        var humans = f.Inputs.MobileSprites.Where(entry => entry.Key is >= 128 and <= 145).ToArray();
        Assert.Equal(18, humans.Length);
        Assert.All(humans, entry => Assert.NotNull(entry.Value.Female));
        long[] ids = Enumerable.Range(0, 16).Select(i => f.Session.SpawnActor("archer", new(new(i, 0, -80), 0), level: 5)).ToArray();
        var genders = ids.ToDictionary(id => id, id => Gender(f.Session, id));
        Assert.Equal(["female", "male"], genders.Values.Distinct().Order());
        AssertAtlases(f.Session, genders);
        using var restored = f.Restore();
        Assert.All(ids, id => Assert.Equal(genders[id], Gender(restored, id)));
        AssertAtlases(restored, genders);

        static string Gender(DaggerfallSession session, long id) => session.State.Actors.Get(id).Actor.Get<DaggerfallActorGender>().Value;
        static void AssertAtlases(DaggerfallSession session, Dictionary<long, string> genders)
        {
            var field = typeof(DaggerfallSiteAppearance).GetField("actors", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var visuals = (Dictionary<long, DaggerfallSiteAppearance.ActorVisual>)field.GetValue(session.Sites.Projection.Appearance)!;
            foreach ((long id, string gender) in genders)
                Assert.Equal(gender == "female", visuals[id].Sprite.TexturePath.Contains("-female.png", StringComparison.Ordinal));
        }
    }

    /// <summary>Minimum gameplay rolls, with each spawn's gender draw hashed from its key like a varying seed.</summary>
    private class GenderRandom : RandomMinimum
    {
        internal new static IRandomService Create() => System.Reflection.DispatchProxy.Create<IRandomService, GenderRandom>();
        protected override object? Invoke(System.Reflection.MethodInfo? method, object?[]? arguments)
        {
            if (arguments is not [KeyedRngRequest { Key: var key } request] || !key.StartsWith("enemy-gender:", StringComparison.Ordinal))
                return base.Invoke(method, arguments);
            uint hash = 2166136261;
            foreach (char ch in key) hash = unchecked((hash ^ ch) * 16777619);
            return new KeyedRngReceipt(request.Minimum + hash % 2);
        }
    }

    [Fact]
    public void Classic_spawn_distance_uses_hysteresis_and_the_asymmetric_vertical_band()
    {
        DaggerfallEnemyPerceptionMemory memory = new() { SpawnDistance = 1 };
        Assert.False(DaggerfallClassicSpawnRange.Update(memory, new(10, 0, 0), default, true));
        Assert.True(DaggerfallClassicSpawnRange.Update(memory, new(9, 0, 0), default, true));
        Assert.True(DaggerfallClassicSpawnRange.Update(memory, new(20, 0, 0), default, true));
        Assert.False(DaggerfallClassicSpawnRange.Update(memory, new(26, 0, 0), default, true));
        memory.SpawnDistance = 5;
        Assert.True(DaggerfallClassicSpawnRange.Update(memory, new(0, -10, 0), default, true));
        memory.WithinClassicSpawnRange = false;
        Assert.False(DaggerfallClassicSpawnRange.Update(memory, new(0, 10, 0), default, true));
    }
}
