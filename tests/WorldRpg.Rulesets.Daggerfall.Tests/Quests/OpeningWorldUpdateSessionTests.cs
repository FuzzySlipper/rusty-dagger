using System.Text.Json.Nodes;
using Rusty.Engine;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// A new game whose opening quest updates the world as it starts: the start place is built with the variant the quest's
/// startup actually selects, as the quest runner runs it, so an update a false condition or a waiting prompt holds back
/// leaves the place, and the save, at its base.
/// </summary>
public sealed class OpeningWorldUpdateSessionTests
{
    private static readonly DaggerfallSiteId Charing = new(17, 4);
    private static readonly DaggerfallWorldProfileKey Exterior = DaggerfallWorldProfileIds.Exterior(Charing);
    private const string Update = "worldupdate location at 4 in region 17 variant closed";

    private static readonly SharedFixture<AssembledWorld> Shared = new(() =>
    {
        JsonArray records = TestPayload.QuestRecords();
        records.Add(Quest("GATED", Block("variable", 1, "variable _enabled_"), Block("headless", 2, "when _enabled_", Update)));
        records.Add(Quest("ASKED", Block("headless", 1, "prompt 10 yes _yes_ no _no_", Update),
            Block("task", 4, "_yes_ task:", "end quest"), Block("task", 6, "_no_ task:", "end quest")));
        return new AssembledWorld(TestPayload.WithQuests(records));
    });

    [Theory]
    [InlineData("GATED.txt")]
    [InlineData("ASKED.txt")]
    public void An_opening_world_update_the_quest_does_not_reach_leaves_the_start_place_and_the_save_at_base(string quest)
    {
        DaggerfallSession game = NewGame(Shared.Value, quest);
        Assert.Same(Shared.Value.Profiles.Require(Exterior), game.Sites.Projection.Inputs);
        QuestDiseaseTests.Advance(game);
        Assert.Empty(DaggerfallSavePayload.Read(game.CaptureSave()).WorldVariants);
        Assert.Same(Shared.Value.Profiles.Require(Exterior), game.Sites.Projection.Inputs);
    }

    [Fact]
    public void An_opening_world_update_the_quest_takes_as_it_starts_is_built_into_the_start_place()
    {
        DaggerfallSession game = NewGame(Shared.Value, "MKTCLOSE.txt");
        DaggerfallSiteProfile built = game.Sites.Projection.Inputs;
        Assert.NotSame(Shared.Value.Profiles.Require(Exterior), built);
        Assert.Contains(built.Geometry.Meshes, mesh => mesh.ModelId == "41239");
        QuestDiseaseTests.Advance(game);
        Assert.Equal(new DaggerfallWorldVariantSave(Charing.Region, Charing.Index, "closed"),
            Assert.Single(DaggerfallSavePayload.Read(game.CaptureSave()).WorldVariants));
        Assert.Same(built, game.Sites.Projection.Inputs);
    }

    /// <summary>A quick-started new game in Charing's market lane whose one opening quest is <paramref name="quest"/>.</summary>
    private static DaggerfallSession NewGame(AssembledWorld world, string quest)
    {
        DaggerfallSiteProfile exterior = world.Profiles.Require(Exterior);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, exterior);
        PopulateContent(content, Assembly.Value.Assemble(Exterior, "closed"));
        foreach (DaggerfallSiteProfile near in world.NearbyExteriors(Charing)) AssembledWorldRun.Admit(content, near);
        SpatialFake spatial = SpatialFake.Create(exterior.Geometry.Spatial[0].Sha256, releases);
        spatial.KeepPosition = true;
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), PerceptionFake.Create().Service);
        DaggerfallSessionComposition composition = new(world.Definitions, exterior, DaggerfallTuning.Defaults, world.Identity)
        {
            Profiles = world.Profiles,
            Blocks = world.Blocks,
            StartPose = new("start", new WorldPoint(312.4F, 0F, -362.8F), 1.5708F, 0F),
            NewGameQuests = [quest],
            QuestAdmission = new(DaggerfallQuestContent.Admit(world.Definitions.QuestSources, new DaggerfallQuestLanguage()).Receipts),
        };
        using DaggerfallSession title = DaggerfallSession.StartNew(engine.Context, composition);
        NewGameSessionTests.Commit(title);
        return Assert.IsType<DaggerfallSession>(title.CreateNewGame());
    }

    private static JsonObject Quest(string name, params JsonObject[] blocks) => new()
    {
        ["name"] = name, ["displayName"] = name, ["sourceFile"] = name + ".txt",
        ["messages"] = new JsonArray(new JsonObject { ["id"] = 10, ["lines"] = new JsonArray("Close the market?") }),
        ["blocks"] = new JsonArray([.. blocks]),
    };

    private static JsonObject Block(string kind, int firstLine, params string[] lines) =>
        new() { ["kind"] = kind, ["firstLine"] = firstLine, ["lines"] = new JsonArray([.. lines.Select(line => (JsonNode)line)]) };
}
