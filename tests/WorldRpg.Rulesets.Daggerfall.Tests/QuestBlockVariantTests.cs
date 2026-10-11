using System.Text;
using System.Text.Json.Nodes;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class QuestBlockVariantTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Quest_block_and_building_forms_resolve_real_assembled_content_and_restore(bool building, bool global)
    {
        var target = building ? new DaggerfallSiteId(17, 4) : new DaggerfallSiteId(17, 9);
        var location = TestPayload.Definitions.Locations.Records.Single(value => value.Id == target);
        var selectedBuilding = building ? location.Exterior!.Buildings.Values.First(value => value.Source.Id.SourceKey == "MAGEAA14.RMB" && value.Id.Index == 0) : null;
        string block = building ? selectedBuilding!.Source.Id.SourceKey : "W0000020.RDB";
        string selector = (building ? "building" : "block") + (global ? "All" : "") + " " + block
            + (building ? " 0" : "") + (global ? "" : $" at {target.Index} in region {target.Region}");
        var definitions = QuestWorldAdmissionTests.Definitions(actions: [$"worldupdate {selector} variant lit"],
            taskBlocks: [["_reset_ task:", $"worldupdate {selector} variant -"]]);
        using var f = new SanguineRoseSessionTests.Fixture(definitions: definitions, prepareInputs: QuestWorldAdmissionTests.WithMarker);
        var catalog = Catalog(definitions, block, building ? 0 : null, f.Inputs, f.Castle);
        f.Session.AdmitSiteProfiles(catalog.ForSession());
        var key = building ? DaggerfallWorldProfileIds.Interior(target, selectedBuilding!.Id) : DaggerfallWorldProfileIds.Dungeon(target);
        Start(f);
        var task = f.Session.State.Quests.Capture().Instances.Single().Tasks.First();
        Assert.True(task.OperationCompleted[0], task.OperationState[0].UnavailableReason);
        Assert.Contains(f.Session.Sites.RequireProfiles().Require(key).Lights, value => value.Id.Contains("variant-light") && value.Intensity == 17f);
        if (building) Assert.Contains(f.Session.Sites.RequireProfiles().Require(DaggerfallWorldProfileIds.Exterior(target)).Lights,
            value => value.Id.Contains("variant-light") && value.Intensity == 17f);
        var resolved = f.Session.Sites.RequireProfiles().Require(key);
        PopulateContent(Assert.IsType<ContentFake>(f.Engine.Context.Content), resolved);
        Assert.True(f.Session.TryTransitionTo(key));
        Assert.Contains(f.Appearance.LightRequests, value => value.Descriptor.Intensity == 17f);
        Assert.True(f.Session.TryTransitionTo(f.Inputs.ProfileKey));
        var saved = DaggerfallSavePayload.Read(f.Session.CaptureSave());
        var choice = Assert.Single(saved.WorldVariants);
        Assert.Equal(building ? "building" : "block", choice.Kind);
        Assert.Equal(global ? -1 : target.Region, choice.Region);
        using var restored = f.Restore(catalog);
        Assert.Contains(restored.Sites.RequireProfiles().Require(key).Lights, value => value.Id.Contains("variant-light"));
        var quests = restored.State.Quests.Capture();
        restored.State.Quests.Restore(quests with { Instances = quests.Instances.Select(instance => instance with
            { Tasks = instance.Tasks.Select(task => task.Symbol == "reset" ? task with { IsSet = true } : task).ToArray() }).ToArray() });
        restored.State.Quests.Advance(restored.State.Variables, DaggerfallCalendar.Start);
        Assert.Empty(DaggerfallSavePayload.Read(restored.CaptureSave()).WorldVariants);
        Assert.DoesNotContain(restored.Sites.RequireProfiles().Require(key).Lights, value => value.Id.Contains("variant-light"));
        Assert.DoesNotContain(catalog.Require(key).Lights, value => value.Id.Contains("variant-light"));
    }

    [Fact]
    public void Local_selection_overrides_global_and_dash_restores_the_global_choice()
    {
        var catalog = Catalog(TestPayload.Definitions, "W0000020.RDB", null, ReadInputs(TestData.RepositoryRoot));
        var owner = catalog.ForSession();
        var site = new DaggerfallSiteId(17, 9);
        var key = DaggerfallWorldProfileIds.Dungeon(site);
        owner.SetBlockVariant(null, "w0000020.rdb", null, "lit");
        owner.SetBlockVariant(site, "W0000020.RDB", null, "dim");
        Assert.Contains(owner.Require(key).Lights, value => value.Id.Contains("variant-light") && value.Intensity == 2f);
        owner = catalog.ForSession(owner.CaptureVariants());
        owner.SetBlockVariant(site, "W0000020.RDB", null, "-");
        Assert.Contains(owner.Require(key).Lights, value => value.Id.Contains("variant-light") && value.Intensity == 17f);
        Assert.Throws<NotSupportedException>(() => owner.SetBlockVariant(site, "W0000020.RDB", null, "absent"));
        Assert.Equal("lit", Assert.Single(owner.CaptureVariants()).Variant);
    }

    private static DaggerfallSiteProfiles Catalog(DaggerfallDefinitions definitions, string source, int? building, params DaggerfallSiteProfile[] authored)
    {
        string root = TestData.RepositoryRoot;
        ProductContent mediaContent = FullContent(root);
        var media = DaggerfallWorldMedia.Read(mediaContent);
        var product = new Lazy<DaggerfallProductMedia>(() => DaggerfallSiteContent.ReadProductMedia(mediaContent, media, definitions));
        var meshes = new Lazy<IReadOnlyDictionary<string, DaggerfallWorldMesh>>(() => DaggerfallLocationAssembly.ReadMeshIndex(mediaContent, media));
        string directory = Path.Combine(root, "content", DaggerfallWorldBlocks.Root);
        var index = JsonNode.Parse(File.ReadAllBytes(Path.Combine(directory, "blocks.json")))!.AsObject();
        var variants = new JsonArray();
        var service = new BundleContentFake();
        foreach (string path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(directory, path).Replace('\\', '/');
            if (relative != "blocks.json") service.AddFile(DaggerfallWorldBlocks.BundleId, relative, path);
        }
        foreach (var original in index["blocks"]!.AsArray().Where(value => value!["sourceKey"]!.GetValue<string>() == source
            && (building is null || value!["kind"]!.GetValue<string>() == "rmbExterior" || value!["buildingIndex"]?.GetValue<int>() == building)))
            foreach (var (name, intensity) in new[] { ("lit", 17), ("dim", 2) })
            {
                var entry = original!.DeepClone().AsObject();
                string originalPath = entry["document"]!.GetValue<string>();
                var block = JsonNode.Parse(File.ReadAllBytes(Path.Combine(directory, originalPath)))!.AsObject();
                block["lights"]!.AsArray().Add(new JsonObject { ["id"] = $"light/{source.ToLowerInvariant().Replace('.', '-')}/variant-light", ["position"] = new JsonObject { ["x"] = 1, ["y"] = 2, ["z"] = -1 },
                    ["range"] = 10, ["intensity"] = intensity, ["color"] = new JsonObject { ["x"] = 1, ["y"] = 1, ["z"] = 1 } });
                string path = $"variants/{name}/{originalPath}";
                entry["document"] = path;
                if (building is null) entry["variant"] = name;
                else entry["buildingVariants"] = new JsonArray(new JsonObject { ["building"] = building, ["variant"] = name });
                variants.Add(entry);
                service.Add(DaggerfallWorldBlocks.BundleId, path, Encoding.UTF8.GetBytes(block.ToJsonString()));
            }
        index["variants"] = variants;
        service.Add(DaggerfallWorldBlocks.BundleId, "blocks.json", Encoding.UTF8.GetBytes(index.ToJsonString()));
        var assembly = new DaggerfallLocationAssembly(definitions, new DaggerfallWorldBlocks(new ProductContent(Array.Empty<ProductContentFile>(), service)), () => product.Value, () => meshes.Value);
        return DaggerfallSiteProfiles.Resolve(authored.Select(DaggerfallAuthoredSite.Of), assembly, null);
    }

    private static void Start(SanguineRoseSessionTests.Fixture f)
    {
        var site = f.Inputs.Site!.Value;
        f.Session.State.Quests.Start(new("world-actions", "world-test.txt", "world-test", DaggerfallQuestLifecycle.Active, null,
            [new("location", DaggerfallQuestResourceBinding.Place(new(site.Region, site.Index)))], []));
        f.Session.State.Quests.Advance(f.Session.State.Variables, DaggerfallCalendar.Start);
    }
}
