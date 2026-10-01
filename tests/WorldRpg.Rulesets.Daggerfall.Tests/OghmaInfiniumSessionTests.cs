using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class OghmaInfiniumSessionTests
{
    [Fact]
    public void Ordinary_inventory_use_consumes_once_and_resumes_the_attribute_only_reward()
    {
        var inputs = ReadInputs(TestData.RepositoryRoot);
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        EngineContextFake Engine()
        {
            List<string> releases = [];
            ContentFake content = new(releases);
            PopulateContent(content, inputs);
            return EngineContextFake.Create(content, SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases).Service, new AppearanceFake(releases));
        }
        var identity = GameCompositionResolver.Resolve(FullContent(TestData.RepositoryRoot), new GameBundleId("daggerfall.privateers-hold")).RequireComposition().Identity;
        DaggerfallSessionComposition composition = new(definitions, inputs, DaggerfallTuning.Defaults, identity);
        EngineContextFake engine = Engine();
        RulesetSavePayload save;
        ulong durableId;
        int level;
        long health;
        double strength;
        using (DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, composition))
        {
            DaggerfallCreatedItem created = new DaggerfallItemFactory(definitions, engine.Context.Random)
                .Create(new("Magic", "oghma-session", DaggerfallItemOwner.Player, MagicItemKey: "magic-item.0005"));
            var itemIdentity = session.UniqueItemAllocator.AllocateReference();
            durableId = itemIdentity.Value;
            var item = session.State.Equipment.Materialize(itemIdentity, created.Item);
            session.State.ItemInstances.RegisterUnique(durableId, created.Metadata);
            level = session.State.Actors.Player.Progression.Level;
            health = session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).Maximum.ValueInt64;
            strength = session.State.Actors.Player.Stats.GetStat(StatId.Parse("strength")).BaseValue;
            ulong step = 1;
            void Submit(object action) => session.Update(new ProductUpdate(OuterUpdate(++step), [Ui(JsonSerializer.Serialize(action))]));
            session.Update(new ProductUpdate(OuterUpdate(1), []));
            string key = $"unique:{item.EntityId}";
            Submit(new { action = "inventory-use", revision = engine.PublishedNested("inventory", "revision"), item = key });
            Assert.Equal("character", engine.PublishedNested("panelRequest", "panel"));
            Assert.Equal(DaggerfallAttributeAllocationKind.OghmaInfinium, session.State.LevelUps.Pending!.Kind);
            Assert.Equal(30, session.State.LevelUps.Read()!.RemainingPoints);
            Assert.Equal("Oghma Infinium", session.State.LevelUps.Read()!.Title);
            Assert.Contains("\"title\":\"Oghma Infinium\"", JsonSerializer.Serialize(engine.Published()));
            Assert.False(session.State.ItemInstances.ContainsUnique(durableId));
            Assert.DoesNotContain(session.State.Inventory.Read().UniqueItems, value => value.Entity.Value == item.EntityId);
            Assert.Contains(durableId, session.UniqueItemAllocator.RemovedEntityIds);
            Submit(new { action = "inventory-use", revision = engine.PublishedNested("inventory", "revision"), item = key });
            Assert.Equal(30, session.State.LevelUps.Read()!.RemainingPoints);
            for (int point = 0; point < 7; point++) Submit(new { action = "character-level-allocate", attribute = "strength" });
            Assert.Equal(23, session.State.LevelUps.Read()!.RemainingPoints);
            save = session.CaptureSave();
        }
        EngineContextFake resumedEngine = Engine();
        using DaggerfallSession restored = DaggerfallSession.Restore(resumedEngine.Context, composition, save);
        Assert.False(restored.State.ItemInstances.ContainsUnique(durableId));
        Assert.Contains(durableId, restored.UniqueItemAllocator.RemovedEntityIds);
        Assert.Equal(DaggerfallAttributeAllocationKind.OghmaInfinium, restored.State.LevelUps.Pending!.Kind);
        Assert.Equal(23, restored.State.LevelUps.Read()!.RemainingPoints);
        ulong resumedStep = 10;
        void ResumeAction(object action) => restored.Update(new ProductUpdate(OuterUpdate(++resumedStep), [Ui(JsonSerializer.Serialize(action))]));
        for (int point = 0; point < 3; point++) ResumeAction(new { action = "character-level-allocate", attribute = "strength" });
        for (int point = 0; point < 10; point++) ResumeAction(new { action = "character-level-allocate", attribute = "intelligence" });
        for (int point = 0; point < 10; point++) ResumeAction(new { action = "character-level-allocate", attribute = "speed" });
        Assert.True(restored.State.LevelUps.Read()!.CanCommit);
        ResumeAction(new { action = "character-level-commit" });
        Assert.Null(restored.State.LevelUps.Pending);
        Assert.Equal(level, restored.State.Actors.Player.Progression.Level);
        Assert.Equal(health, restored.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health")).Maximum.ValueInt64);
        Assert.Equal(strength + 10, restored.State.Actors.Player.Stats.GetStat(StatId.Parse("strength")).BaseValue);
    }
}
