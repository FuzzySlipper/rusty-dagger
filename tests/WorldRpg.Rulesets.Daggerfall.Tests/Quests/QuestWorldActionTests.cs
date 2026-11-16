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
        Assert.Equal(DaggerfallQuestItemExits.Refused, f.Message);
        Assert.True(f.Session.TryTransitionTo(castle.ProfileKey)); f.Update(); Drop(f, entity); f.Update();
        Assert.Equal("ground", f.Session.State.ItemInstances.RequireUnique(id).Owner.Scope);
        Assert.True(Quest(f.Session).Tasks.Single(t => t.Symbol == "dropped").IsSet);
        Assert.Single(f.Session.State.Quests.Messages.Deliveries);
        using var restored = f.Restore(profiles); Advance(restored); Advance(restored);
        Assert.True(Quest(restored).Tasks.Single(t => t.Symbol == "dropped").IsSet);
        Assert.Single(restored.State.Quests.Messages.Deliveries);
    }

    [Fact]
    public void A_quest_item_no_drop_task_watches_cannot_be_dropped_anywhere()
    {
        var definitions = QuestWorldAdmissionTests.Definitions(secondPlace: true, actions: ["get item _gift_"]);
        using var f = new SanguineRoseSessionTests.Fixture(definitions: definitions, prepareInputs: QuestWorldAdmissionTests.WithMarker);
        var castle = QuestWorldAdmissionTests.WithMarker(f.Castle);
        var profiles = new DaggerfallSiteProfiles([f.Inputs, castle]); f.Session.AdmitSiteProfiles(profiles);
        Start(f, castle); f.Update();
        ulong id = Quest(f.Session).Resources.Single(r => r.Symbol == "gift").Binding.UniqueItemIds.Single();
        ulong entity = f.Session.State.Actors.Entities.Resolve(new(DurableIdentityKind.Item, id)).Value;
        Drop(f, entity);
        Assert.Equal(DaggerfallItemOwner.Player, f.Session.State.ItemInstances.RequireUnique(id).Owner);
        Assert.Equal(DaggerfallQuestItemExits.Refused, f.Message);
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

    /// <summary>
    /// A world update selects a location's variant, which the save keeps through unload and restore, and a dash clears.
    /// No variant content is published, so the location keeps showing its base content throughout.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Location_variant_is_kept_through_unload_and_restore_then_dash_removes_it(bool named)
    {
        var site = ReadInputs(TestData.RepositoryRoot).Site!.Value;
        string selector = named ? $"locationnew named {TestPayload.Definitions.Locations.Records.Single(value => value.Id == site).Name} in region {site.Region}"
            : $"location at {site.Index} in region {site.Region}";
        var definitions = QuestWorldAdmissionTests.Definitions(actions: [$"worldupdate {selector} variant lit"],
            taskBlocks: [["_reset_ task:", $"worldupdate {selector} variant -"]]);
        using var f = new SanguineRoseSessionTests.Fixture(definitions: definitions, prepareInputs: QuestWorldAdmissionTests.WithMarker);
        var profiles = new DaggerfallSiteProfiles([f.Inputs, f.Castle]); f.Session.AdmitSiteProfiles(profiles.ForSession());
        Start(f); Advance(f.Session);
        var saved = DaggerfallSavePayload.Read(f.Session.CaptureSave());
        Assert.Equal(new DaggerfallWorldVariantSave(site.Region, site.Index, "lit"), Assert.Single(saved.WorldVariants));
        Assert.Throws<ArgumentNullException>(() => (saved with { WorldVariants = null! }).Validate());
        Assert.Throws<ArgumentException>(() => (saved with { WorldVariants = [new(0, 0, "-")] }).Validate());
        Assert.Throws<ArgumentException>(() => (saved with { WorldVariants = [saved.WorldVariants[0], saved.WorldVariants[0]] }).Validate());
        Assert.True(f.Session.TryTransitionTo(f.Castle.ProfileKey));
        Assert.True(f.Session.TryTransitionTo(f.Inputs.ProfileKey));
        Assert.Same(profiles.Require(f.Inputs.ProfileKey), f.Session.Sites.Projection.Inputs);
        using var restored = f.Restore(profiles); Advance(restored);
        Assert.Single(DaggerfallSavePayload.Read(restored.CaptureSave()).WorldVariants);
        var savedQuests = restored.State.Quests.Capture();
        restored.State.Quests.Restore(savedQuests with { Instances = savedQuests.Instances.Select(instance => instance with
            { Tasks = instance.Tasks.Select(task => task.Symbol == "reset" ? task with { IsSet = true } : task).ToArray() }).ToArray() });
        Advance(restored);
        Assert.Empty(DaggerfallSavePayload.Read(restored.CaptureSave()).WorldVariants);
    }

    [Fact]
    public void A_world_update_without_published_content_completes_and_keeps_base_content()
    {
        var definitions = QuestWorldAdmissionTests.Definitions(actions: ["worldupdate location at 1 in region 17 variant absent", "say 100"], messages: ["Runs after fallback."]);
        using var f = new SanguineRoseSessionTests.Fixture(definitions: definitions);
        var profiles = new DaggerfallSiteProfiles([f.Inputs, f.Castle]); f.Session.AdmitSiteProfiles(profiles.ForSession());
        Start(f); Advance(f.Session);
        var task = Quest(f.Session).Tasks.First();
        Assert.True(task.OperationCompleted[0], task.OperationState[0].UnavailableReason);
        Assert.NotEmpty(f.Session.State.Quests.Messages.Deliveries);
        Assert.Single(DaggerfallSavePayload.Read(f.Session.CaptureSave()).WorldVariants);
        using var restored = f.Restore(profiles);
        Assert.Single(DaggerfallSavePayload.Read(restored.CaptureSave()).WorldVariants);
    }

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
