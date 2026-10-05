using System.Numerics;
using System.Text.Json.Nodes;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class QuestWorldActionTests
{
    [Theory]
    [InlineData("dropped _gift_ at _location_")]
    [InlineData("dropped _gift_ at _location_ saying 100")]
    [InlineData("reveal _location_")]
    [InlineData("reveal _location_ readmap")]
    [InlineData("teleport pc to _location_")]
    [InlineData("transfer pc inside _location_ marker 2")]
    [InlineData("worldupdate location at 1 in region 17 variant restored")]
    [InlineData("worldupdate locationnew named A New Town in region 17 variant restored")]
    [InlineData("worldupdate block S0000000.RMB at 1 in region 17 variant restored")]
    [InlineData("worldupdate blockAll S0000000.RMB variant -")]
    [InlineData("worldupdate building S0000000.RMB 1 at 1 in region 17 variant restored")]
    [InlineData("worldupdate buildingAll S0000000.RMB 1 variant restored")]
    public void Retained_source_forms_compile(string action)
    {
        var source = new DaggerfallQuestSourceDefinition("world", "", "world.txt", DaggerfallQuestDisposition.Compiled, [], [new("headless", 1, [action], null)], []);
        Assert.NotEqual(DaggerfallQuestTaskOperationKind.Unsupported, Assert.Single(Assert.Single(DaggerfallQuestTaskCompiler.Compile(source).Tasks).Operations).Kind);
    }

    [Fact]
    public void Actual_drop_checks_bound_item_and_place_then_latches_once_through_restore()
    {
        var definitions = QuestWorldAdmissionTests.Definitions(secondPlace: true, actions: ["get item _gift_"], messages: ["Delivered."],
            taskBlocks: [["_dropped_ task:", "dropped _gift_ at _destination_ saying 100"]]);
        using var f = new SanguineRoseSessionTests.Fixture(definitions: definitions, prepareInputs: QuestWorldAdmissionTests.WithMarker);
        var castle = QuestWorldAdmissionTests.WithMarker(f.Castle);
        var profiles = new DaggerfallSiteProfiles([f.Inputs, castle]); f.Session.AdmitSiteProfiles(profiles);
        Start(f, castle); f.Update();
        f.Submit(new { action = "inventory-drop", revision = f.Engine.PublishedNested("inventory", "revision"), item = $"unique:{f.Item.EntityId}", amount = 1 });
        Assert.False(Quest(f.Session).Tasks.Single(t => t.Symbol == "dropped").IsSet);
        ulong id = Quest(f.Session).Resources.Single(r => r.Symbol == "gift").Binding.UniqueItemIds.Single();
        ulong entity = f.Session.State.Actors.Entities.Resolve(new(DurableIdentityKind.Item, id)).Value;
        Drop(f, entity);
        Assert.Equal(DaggerfallItemOwner.Player, f.Session.State.ItemInstances.RequireUnique(id).Owner);
        Assert.Contains("designated place", f.Message);
        Assert.True(f.Session.TryTransitionTo(castle.ProfileKey)); f.Update(); Drop(f, entity); f.Update();
        Assert.Equal("ground", f.Session.State.ItemInstances.RequireUnique(id).Owner.Scope);
        Assert.True(Quest(f.Session).Tasks.Single(t => t.Symbol == "dropped").IsSet);
        Assert.Single(f.Session.State.Quests.Messages.Deliveries);
        using var restored = f.Restore(profiles); Advance(restored); Advance(restored);
        Assert.True(Quest(restored).Tasks.Single(t => t.Symbol == "dropped").IsSet);
        Assert.Single(restored.State.Quests.Messages.Deliveries);
    }

    [Theory]
    [InlineData("teleport pc to _destination_")]
    [InlineData("transfer pc inside _destination_ marker 99")]
    public void Reveal_and_teleport_use_real_site_and_marker_and_survive_restore(string transfer)
    {
        var definitions = QuestWorldAdmissionTests.Definitions(secondPlace: true, actions: ["reveal _destination_ readmap", transfer]);
        using var f = new SanguineRoseSessionTests.Fixture(definitions: definitions, prepareInputs: QuestWorldAdmissionTests.WithMarker);
        var castle = QuestWorldAdmissionTests.WithMarker(f.Castle);
        var profiles = new DaggerfallSiteProfiles([f.Inputs, castle]); f.Session.AdmitSiteProfiles(profiles);
        Start(f, castle); Advance(f.Session);
        Assert.Equal(castle.ProfileKey, f.Session.Sites.ActiveProfile);
        Assert.Equal(castle.QuestMarkers[0].Position, f.Session.State.PlayerControl.Position);
        Assert.True(f.Session.Site.IsDiscovered(castle.Site!.Value));
        Assert.Single(DaggerfallSavePayload.Read(f.Session.CaptureSave()).Notebook.Notes);
        using var restored = f.Restore(profiles); Advance(restored);
        Assert.Equal(castle.ProfileKey, restored.Sites.ActiveProfile);
        Assert.Equal(castle.QuestMarkers[0].Position, restored.State.PlayerControl.Position);
        Assert.True(restored.Site.IsDiscovered(castle.Site!.Value));
        Assert.Single(DaggerfallSavePayload.Read(restored.CaptureSave()).Notebook.Notes);
    }

    [Fact]
    public void Location_variant_changes_admitted_lights_after_unload_and_restore_then_dash_removes_it()
    {
        var site = ReadInputs(TestData.RepositoryRoot).Site!.Value;
        var definitions = QuestWorldAdmissionTests.Definitions(actions: [$"worldupdate location at {site.Index} in region {site.Region} variant lit"],
            taskBlocks: [["_reset_ task:", $"worldupdate location at {site.Index} in region {site.Region} variant -"]]);
        using var f = new SanguineRoseSessionTests.Fixture(definitions: definitions, prepareInputs: QuestWorldAdmissionTests.WithMarker);
        var variant = Variant(f.Inputs);
        var profiles = new DaggerfallSiteProfiles([f.Inputs, f.Castle, variant]); f.Session.AdmitSiteProfiles(profiles.ForSession());
        Start(f); Advance(f.Session);
        Assert.Single(DaggerfallSavePayload.Read(f.Session.CaptureSave()).WorldVariants);
        var saved = DaggerfallSavePayload.Read(f.Session.CaptureSave());
        Assert.Throws<ArgumentNullException>(() => (saved with { WorldVariants = null! }).Validate());
        Assert.Throws<ArgumentException>(() => (saved with { WorldVariants = [new(0, 0, "-")] }).Validate());
        Assert.Throws<ArgumentException>(() => (saved with { WorldVariants = [saved.WorldVariants[0], saved.WorldVariants[0]] }).Validate());
        Assert.Null(profiles.Require(f.Inputs.ProfileKey).VariantName); // The shared catalog stays immutable across sessions.
        int before = f.Appearance.LightRequests.Count;
        Assert.True(f.Session.TryTransitionTo(f.Castle.ProfileKey));
        Assert.True(f.Session.TryTransitionTo(f.Inputs.ProfileKey));
        Assert.Equal("lit", f.Session.Sites.Projection.Inputs.VariantName);
        Assert.Equal(17f, Assert.Single(f.Session.Sites.Projection.Inputs.Lights).Intensity);
        Assert.Contains(f.Appearance.LightRequests.Skip(before), light => light.Descriptor.Intensity == 17f);
        using var restored = f.Restore(profiles); Advance(restored);
        Assert.Equal("lit", restored.Sites.Projection.Inputs.VariantName);
        Assert.Equal(17f, Assert.Single(restored.Sites.Projection.Inputs.Lights).Intensity);
        var savedQuests = restored.State.Quests.Capture();
        restored.State.Quests.Restore(savedQuests with { Instances = savedQuests.Instances.Select(instance => instance with
            { Tasks = instance.Tasks.Select(task => task.Symbol == "reset" ? task with { IsSet = true } : task).ToArray() }).ToArray() });
        Advance(restored);
        Assert.Empty(DaggerfallSavePayload.Read(restored.CaptureSave()).WorldVariants);
        Assert.True(restored.TryTransitionTo(f.Castle.ProfileKey)); Assert.True(restored.TryTransitionTo(f.Inputs.ProfileKey));
        Assert.Null(restored.Sites.Projection.Inputs.VariantName);
    }

    [Theory]
    [InlineData("worldupdate blockAll S0000000.RMB variant altered")]
    [InlineData("worldupdate location at 1 in region 17 variant absent")]
    public void Unavailable_world_updates_report_owning_action_and_do_not_complete_following_actions(string action)
    {
        var definitions = QuestWorldAdmissionTests.Definitions(actions: [action, "say 100"], messages: ["Must not run."]);
        using var f = new SanguineRoseSessionTests.Fixture(definitions: definitions);
        Start(f); Advance(f.Session);
        var task = Quest(f.Session).Tasks.First();
        Assert.False(task.OperationCompleted[0]); Assert.NotNull(task.OperationState[0].UnavailableReason);
        Assert.Empty(f.Session.State.Quests.Messages.Deliveries);
        using var restored = f.Restore(); Assert.NotNull(Quest(restored).Tasks.First().OperationState[0].UnavailableReason);
    }

    [Fact]
    public void Authored_site_variant_fields_use_normal_content_admission_and_reject_missing_catalog_entries()
    {
        string root = TestData.RepositoryRoot;
        var payload = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "content/worldrpg/payloads/daggerfall.privateers-hold.json")))!;
        var original = ReadInputs(root);
        payload["world"]!["variant"] = "authored";
        payload["world"]!["variantOf"] = original.ProfileKey.LogicalId;
        var variant = DaggerfallSiteContent.Read(ImportContent(root), System.Text.Encoding.UTF8.GetBytes(payload.ToJsonString()), TestPayload.Definitions);
        var catalog = new DaggerfallSiteProfiles([original, variant]);
        var session = catalog.ForSession([new(original.Site!.Value.Region, original.Site.Value.Index, "authored")]);
        Assert.Equal("authored", session.Require(original.ProfileKey).VariantName);
        Assert.Null(catalog.Require(original.ProfileKey).VariantName);
        Assert.Throws<NotSupportedException>(() => catalog.ForSession([new(original.Site.Value.Region, original.Site.Value.Index, "missing")]));
        Assert.Throws<ArgumentException>(() => new DaggerfallSiteProfiles([variant]));
    }

    [Fact]
    public void Location_variant_rejects_missing_retained_quest_markers()
    {
        var original = ReadInputs(TestData.RepositoryRoot);
        Assert.NotEmpty(original.QuestMarkers);
        Assert.Throws<NotSupportedException>(() => new DaggerfallSiteProfiles([original, Variant(original, [])]));
    }

    private static DaggerfallSiteProfile Variant(DaggerfallSiteProfile source, IReadOnlyList<DaggerfallSiteMarker>? markers = null) => new(source.Project, source.SpatialArtifact,
        source.StaticMesh, source.WorldAppearance, source.InitialLook, source.Materials, source.ActorSprites, source.MobileSprites,
        source.Audio, source.ClassicPresentation, source.Site, source.Doors, source.ProfileKind, source.ProfileKey.LogicalId,
        source.Portals, source.Anchors.Values.ToArray(), [new("variant-light", new(1, 2, 3), 8, 17, Vector3.One)], source.GroundContainerSprite, source.DungeonMap,
        source.DungeonActions, source.DungeonActionModels, source.InteriorBuilding, source.Music, source.AudioBundle, markers ?? source.QuestMarkers, source.BillboardSprites,
        source.StaticNpcs, source.WaterVolumes, source.TerrainTextures, source.Population)
        { VariantName = "lit", VariantBaseLogicalId = source.ProfileKey.LogicalId, AmbientZones = source.AmbientZones, PropertyContainers = source.PropertyContainers };
    private static void Drop(SanguineRoseSessionTests.Fixture f, ulong entity) => f.Submit(new { action = "inventory-drop", revision = f.Engine.PublishedNested("inventory", "revision"), item = $"unique:{entity}", amount = 1 });
    private static DaggerfallQuestInstanceSave Quest(DaggerfallSession s) => s.State.Quests.Capture().Instances.Single();
    private static void Advance(DaggerfallSession s) => s.State.Quests.Advance(s.State.Variables, DaggerfallCalendar.Start);
    private static void Start(SanguineRoseSessionTests.Fixture f, DaggerfallSiteProfile? destination = null)
    {
        DaggerfallQuestResourceState Binding(string symbol, DaggerfallSiteProfile profile)
        {
            var site = TestPayload.Definitions.Locations.Records.Single(value => value.Id == profile.Site);
            return new(symbol, DaggerfallQuestResourceBinding.Place(new(site.Region, site.Index)) with { PlaceSelection = new(profile.ProfileKind, site.MapId) });
        }
        f.Session.State.Quests.Start(new("world-actions", "world-test.txt", "world-test", DaggerfallQuestLifecycle.Active, null,
            destination is null ? [Binding("location", f.Inputs)] : [Binding("location", f.Inputs), Binding("destination", destination)], []));
    }
}
