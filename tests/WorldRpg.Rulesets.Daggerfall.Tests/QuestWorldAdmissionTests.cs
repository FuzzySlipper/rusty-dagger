using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class QuestWorldAdmissionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Queued_Item_and_Foe_placement_uses_real_owners_once_and_retains_identity_after_encoded_restore(bool stackable)
    {
        var definitions = Definitions(stackable);
        using var fixture = new SanguineRoseSessionTests.Fixture(definitions: definitions, prepareInputs: WithMarker);
        var session = fixture.Session;
        var inputs = fixture.Inputs;
        var site = definitions.Locations.Records.Single(value => value.Id == inputs.Site);
        var binding = DaggerfallQuestResourceBinding.Place(new(site.Region, site.Index)) with
            { PlaceSelection = new(inputs.ProfileKind, site.MapId, null, 0) };
        var instance = session.State.Quests.Start(new("world-test", "world-test.txt", "world-test", DaggerfallQuestLifecycle.Active, null,
            [new("location", binding)], []));
        string item = instance.Resources.Single(value => value.SelectedItem is not null).Symbol;
        string foe = instance.Resources.Single(value => value.SelectedFoe is not null).Symbol;
        session.State.Quests.RequestPlacement(instance.InstanceId, "place-item", item, "location");
        session.State.Quests.RequestPlacement(instance.InstanceId, "place-foe", foe, "location");
        var before = DaggerfallSavePayload.Read(session.CaptureSave());
        Assert.All(before.Quests.Instances.Single().Placements, operation => Assert.Null(operation.Applied));
        int actorCount = session.State.Actors.All.Count();
        int itemCount = session.State.ItemInstances.UniqueItems.Count();
        int stackCount = session.State.ItemInstances.StackItems.Count();
        using (var pending = fixture.Restore())
        {
            Assert.All(pending.State.Quests.Capture().Instances.Single().Placements, operation => Assert.Null(operation.Applied));
            pending.State.Quests.AdmitPlacements(inputs, pending);
            pending.State.Quests.AdmitPlacements(inputs, pending);
            Assert.Equal(actorCount + 1, pending.State.Actors.All.Count());
            Assert.All(pending.State.Quests.Capture().Instances.Single().Placements, operation => Assert.NotNull(operation.Applied));
        }
        session.State.Quests.AdmitPlacements(inputs, session);
        Assert.True(session.State.Quests.TryGet(instance.InstanceId, out var placed));
        var foeBinding = placed!.Resources.Single(value => value.Symbol == foe).Binding;
        var itemBinding = placed.Resources.Single(value => value.Symbol == item).Binding;
        Assert.Equal(actorCount + 1, session.State.Actors.All.Count());
        Assert.Equal(itemCount + (stackable ? 0 : 1), session.State.ItemInstances.UniqueItems.Count());
        Assert.Equal(stackCount + (stackable ? 1 : 0), session.State.ItemInstances.StackItems.Count());
        Assert.True(session.State.Actors.TryGet(foeBinding.ActorIds.Single(), out _));
        if (stackable) Assert.Equal("ground", itemBinding.Stacks.Single().Owner.Scope);
        else Assert.Equal("ground", session.State.ItemInstances.RequireUnique(itemBinding.UniqueItemIds.Single()).Owner.Scope);
        Assert.All(placed.Placements, operation => Assert.Equal(inputs.QuestMarkers.Single().Id, operation.Applied!.MarkerId));
        session.State.Quests.AdmitPlacements(inputs, session);
        Assert.Equal(actorCount + 1, session.State.Actors.All.Count());
        Assert.Equal(itemCount + (stackable ? 0 : 1), session.State.ItemInstances.UniqueItems.Count());
        Assert.Equal(stackCount + (stackable ? 1 : 0), session.State.ItemInstances.StackItems.Count());
        using var restored = fixture.Restore();
        restored.State.Quests.AdmitPlacements(inputs, restored);
        Assert.Equal(actorCount + 1, restored.State.Actors.All.Count());
        Assert.Equal(itemCount + (stackable ? 0 : 1), restored.State.ItemInstances.UniqueItems.Count());
        Assert.Equal(stackCount + (stackable ? 1 : 0), restored.State.ItemInstances.StackItems.Count());
        Assert.True(restored.State.Quests.TryGet(instance.InstanceId, out var saved));
        Assert.Equal(foeBinding.ActorIds, saved!.Resources.Single(value => value.Symbol == foe).Binding.ActorIds);
        Assert.Equal(itemBinding.UniqueItemIds, saved.Resources.Single(value => value.Symbol == item).Binding.UniqueItemIds);
        Assert.Equal(itemBinding.Stacks, saved.Resources.Single(value => value.Symbol == item).Binding.Stacks);
    }

    [Fact]
    public void Ended_quest_discards_unadmitted_operations_and_conflicting_ids_report_the_collision()
    {
        var definitions = Definitions();
        using var fixture = new SanguineRoseSessionTests.Fixture(definitions: definitions, prepareInputs: WithMarker);
        var inputs = fixture.Inputs;
        var site = definitions.Locations.Records.Single(value => value.Id == inputs.Site);
        var instance = fixture.Session.State.Quests.Start(new("ending", "world-test.txt", "world-test", DaggerfallQuestLifecycle.Active, null,
            [new("location", DaggerfallQuestResourceBinding.Place(new(site.Region, site.Index)) with
                { PlaceSelection = new(inputs.ProfileKind, site.MapId, null, 0) })], []));
        string item = instance.Resources.Single(value => value.SelectedItem is not null).Symbol;
        string foe = instance.Resources.Single(value => value.SelectedFoe is not null).Symbol;
        var quests = fixture.Session.State.Quests;
        quests.RequestPlacement(instance.InstanceId, "operation", item, "location");
        quests.RequestPlacement(instance.InstanceId, "operation", item, "location");
        Assert.Contains("different operation", Assert.Throws<ArgumentException>(() =>
            quests.RequestPlacement(instance.InstanceId, "operation", foe, "location")).Message);
        Assert.Empty(quests.Complete(instance.InstanceId, "done").Placements);
        int actorCount = fixture.Session.State.Actors.All.Count();
        quests.AdmitPlacements(inputs, fixture.Session);
        Assert.Equal(actorCount, fixture.Session.State.Actors.All.Count());
    }

    private static DaggerfallDefinitions Definitions(bool stackable = false)
    {
        var root = JsonNode.Parse(TestPayload.CombinedText)!.AsObject();
        var declarations = root["questSources"]!["resources"]!["declarations"]!.AsArray();
        var foe = declarations.First(value => value!["kind"]!.GetValue<string>() == "foe"
            && value["targetSourceSpelling"]!.GetValue<string>() == "Giant_rat")!.DeepClone();
        var item = declarations.Single(value => value!["sourceFile"]!.GetValue<string>() == (stackable ? "R0C11Y28.txt" : "S0000502.txt")
            && value["symbol"]!["canonicalId"]!.GetValue<string>() == (stackable ? "i.09" : "reward"))!.DeepClone();
        var place = declarations.First(value => value!["kind"]!.GetValue<string>() == "place")!.DeepClone();
        foreach (var row in new[] { foe, item, place })
        { row["sourceFile"] = "world-test.txt"; row["quest"] = "world-test"; declarations.Add(row); }
        place["symbol"]!["canonicalId"] = "location"; place["symbol"]!["sourceSpelling"] = "_location_";
        root["questSources"]!["quests"]!.AsArray().Add(JsonNode.Parse("""
            {"name":"world-test","displayName":"","sourceFile":"world-test.txt","disposition":"compiled","messages":[],"blocks":[],"diagnostics":[]}
            """));
        return DaggerfallBaseContent.Read(Encoding.UTF8.GetBytes(root.ToJsonString()));
    }

    private static DaggerfallSiteProfile WithMarker(DaggerfallSiteProfile source)
    {
        var site = TestPayload.Definitions.Locations.Records.Single(value => value.Id == source.Site);
        var blocks = DaggerfallBlocksContent.Read(File.ReadAllBytes(Path.Combine(TestData.RepositoryRoot, "content/worldrpg/payloads/daggerfall.blocks.json")));
        var block = site.DungeonBlocks.First(value => blocks.QuestMarkers.TryGetValue(new(value.SourceKey, null), out var markers)
            && markers.Any(marker => marker.Kind == DaggerfallSiteMarkerKind.QuestSpawn));
        var marker = blocks.QuestMarkers[new(block.SourceKey, null)].First(value => value.Kind == DaggerfallSiteMarkerKind.QuestSpawn)
            with { BlockX = block.X, BlockZ = block.Z };
        return new(source.Project, source.SpatialArtifact,
        source.StaticMesh, source.WorldAppearance, source.InitialLook, source.Materials, source.ActorSprites, source.MobileSprites,
        source.Audio, source.ClassicPresentation, source.Site, source.Doors, source.ProfileKind, source.ProfileKey.LogicalId,
        source.Portals, source.Anchors.Values.ToArray(), source.Lights, source.GroundContainerSprite, source.DungeonMap,
        source.DungeonActions, source.DungeonActionModels, source.InteriorBuilding, source.Music, source.AudioBundle,
        [marker]);
    }
}
