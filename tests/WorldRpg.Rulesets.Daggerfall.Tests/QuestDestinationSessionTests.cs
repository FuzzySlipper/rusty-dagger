using System.Numerics;
using System.Text.Json.Nodes;
using Rusty.Engine;
using Rusty.Engine.Entities;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// A quest's person, foe and item queued for a place no pack publishes wait, across a save taken before the
/// player gets there, and appear at the place's quest markers when its assembled profile loads: a remote
/// building and a remote dungeon chosen from the catalog by the source Place rules, reached by ordinary travel
/// and entry, and a permanent main-quest place reached by quest teleport.
/// </summary>
public sealed class QuestDestinationSessionTests
{
    private static readonly SharedFixture<AssembledWorld> Shared = new(() => new AssembledWorld(Definitions()));

    [Fact]
    public void Remote_building_realizes_its_queued_person_foe_and_item_when_entered_after_restore()
    {
        AssembledWorld world = Shared.Value;
        using AssembledWorldRun start = world.Start(AssembledWorld.PrivateersHold);
        DaggerfallQuestResourceBinding place = Begin(start, "remote-building");
        Assert.Equal(DaggerfallWorldProfileKind.Interior, place.PlaceSelection!.Kind);
        DaggerfallSiteId site = place.Places[0].Require();
        DaggerfallSiteBuildingId building = new(place.Building!.BlockX, place.Building.BlockY, place.Building.Index);
        Assert.True(world.IsUnpublished(site));
        DaggerfallWorldProfileKey exterior = DaggerfallWorldProfileIds.Exterior(site), interior = DaggerfallWorldProfileIds.Interior(site, building);

        using AssembledWorldRun run = world.Restore(start.Session.CaptureSave(), site, building);
        AssertPending(run);
        run.Travel(world.Definitions.Locations.Records.Single(record => record.Id == site));
        Assert.Equal(exterior, run.Session.Sites.ActiveProfile);
        run.Step();
        AssertPending(run);

        // In through the building's own door, which its block locks: the player picks it first.
        DaggerfallRdbDoorDefinition door = world.Profiles.Require(exterior).Doors.Single(candidate => candidate.ExteriorBuilding == building);
        run.Session.Doors.Unlock(door.Id, DaggerfallDoorOperationSource.Player);
        DaggerfallDoorView live = run.Session.Sites.Projection.Doors.Read(door.Id);
        Assert.False(live.IsLocked);
        run.Use(live.Pose.Translation, live.Entity);
        Assert.Equal(interior, run.Session.Sites.ActiveProfile);
        run.Step();
        AssertRealized(run, world.Profiles.Require(interior));
    }

    [Fact]
    public void Remote_dungeon_realizes_its_queued_person_foe_and_item_when_entered_after_restore()
    {
        AssembledWorld world = Shared.Value;
        using AssembledWorldRun start = world.Start(AssembledWorld.PrivateersHold);
        DaggerfallQuestResourceBinding place = Begin(start, "remote-dungeon");
        Assert.Equal(DaggerfallWorldProfileKind.Dungeon, place.PlaceSelection!.Kind);
        DaggerfallSiteId site = place.Places[0].Require();
        Assert.True(world.IsUnpublished(site));
        DaggerfallWorldProfileKey exterior = DaggerfallWorldProfileIds.Exterior(site), dungeon = DaggerfallWorldProfileIds.Dungeon(site);

        using AssembledWorldRun run = world.Restore(start.Session.CaptureSave(), site);
        AssertPending(run);
        run.Travel(world.Definitions.Locations.Records.Single(record => record.Id == site));
        Assert.Equal(exterior, run.Session.Sites.ActiveProfile);
        run.Step();
        AssertPending(run);

        // In through the location's source dungeon entrance.
        DaggerfallSitePortal entrance = world.Profiles.Require(exterior).Portals.First(portal => portal.DestinationLogicalProfile == dungeon.LogicalId);
        (DaggerfallSitePortal Portal, DurableIdentityReference _, EntityId Entity) live = run.Session.Sites.Projection.Portals.All.Single(value => value.Portal.Id == entrance.Id);
        run.Use(live.Portal.Position.ToVector(), live.Entity);
        Assert.Equal(dungeon, run.Session.Sites.ActiveProfile);
        run.Step();
        AssertRealized(run, world.Profiles.Require(dungeon));
    }

    [Fact]
    public void Permanent_castle_is_reached_by_quest_teleport_and_realizes_its_queued_person_foe_and_item_after_restore()
    {
        AssembledWorld world = Shared.Value;
        using AssembledWorldRun start = world.Start(AssembledWorld.PrivateersHold);
        DaggerfallQuestResourceBinding place = Begin(start, "permanent");
        DaggerfallSiteId site = place.Places[0].Require();
        // Place _daggerfall_ permanent DaggerfallCastle2 (S0000008): the castle beneath the city of Daggerfall.
        Assert.Equal("Daggerfall", world.Definitions.Locations.Records.Single(record => record.Id == site).Name);
        Assert.Equal(DaggerfallWorldProfileKind.Dungeon, place.PlaceSelection!.Kind);
        Assert.True(world.IsUnpublished(site));
        DaggerfallWorldProfileKey dungeon = DaggerfallWorldProfileIds.Dungeon(site);

        using AssembledWorldRun run = world.Restore(start.Session.CaptureSave(), site);
        AssertPending(run);
        // The quest's teleport runs in the first admitted update and lands on the castle's first spawn marker;
        // the queued resources are realized as the castle loads.
        run.Step();
        Assert.Equal(dungeon, run.Session.Sites.ActiveProfile);
        DaggerfallSiteProfile castle = world.Profiles.Require(dungeon);
        // The first spawn marker of the castle's own block layout, which is not the first in the profile's grid order.
        DaggerfallSiteMarker first = DaggerfallQuestPlacements.SourceOrder(castle,
            world.Definitions.Locations.Records.Single(record => record.Id == site), DaggerfallSiteMarkerKind.QuestSpawn)[0];
        Assert.NotEqual(castle.QuestMarkers.First(marker => marker.Kind == DaggerfallSiteMarkerKind.QuestSpawn), first);
        Assert.Equal(first.Position, run.Session.State.PlayerControl.Position);
        run.Step();
        AssertRealized(run, castle);
    }

    /// <summary>Starts the quest, letting the source Place rules choose its destination, and queues its person, foe and item there.</summary>
    private static DaggerfallQuestResourceBinding Begin(AssembledWorldRun run, string quest)
    {
        var instance = run.Session.State.Quests.Start(new(quest, quest + ".txt", quest, DaggerfallQuestLifecycle.Active, null, [], []));
        foreach (string symbol in new[] { "enemy", "gift", "person" })
            run.Session.State.Quests.RequestPlacement(instance.InstanceId, "place-" + symbol, symbol, "destination");
        return instance.Resources.Single(resource => resource.Symbol == "destination").Binding;
    }

    private static void AssertPending(AssembledWorldRun run)
    {
        DaggerfallQuestInstanceSave quest = Assert.Single(run.Session.State.Quests.Capture().Instances);
        Assert.Equal(3, quest.Placements.Length);
        Assert.All(quest.Placements, placement => Assert.Null(placement.Applied));
    }

    /// <summary>
    /// Each queued resource stands at the place's quest marker, in the world owner its kind belongs to. The foe,
    /// queued first, chooses a spawn marker; the item and the person, given no marker index, join the marker the
    /// place already selected (GetSiteMarker).
    /// </summary>
    private static void AssertRealized(AssembledWorldRun run, DaggerfallSiteProfile profile)
    {
        DaggerfallQuestInstanceSave quest = Assert.Single(run.Session.State.Quests.Capture().Instances);
        Assert.All(quest.Placements, placement =>
        {
            Assert.Equal(profile.ProfileKey, placement.Applied!.Profile);
            Assert.Contains(profile.QuestMarkers, marker => marker.Id == placement.Applied.MarkerId);
        });
        DaggerfallSiteMarker At(string symbol) => profile.QuestMarkers.Single(marker => marker.Id == quest.Placements.Single(value => value.ResourceSymbol == symbol).Applied!.MarkerId);
        DaggerfallQuestResourceBinding Bound(string symbol) => quest.Resources.Single(resource => resource.Symbol == symbol).Binding;

        Assert.Equal(DaggerfallSiteMarkerKind.QuestSpawn, At("enemy").Kind);
        Assert.Equal(At("enemy"), At("gift"));
        Assert.Equal(At("enemy"), At("person"));

        Vector3 foeMarker = run.Session.Sites.ProfileToLocal(At("enemy").Position).ToVector();
        Assert.True(run.Session.State.Actors.TryGet(Assert.Single(Bound("enemy").ActorIds), out var foe));
        Assert.True(Vector2.Distance(new(foe!.Position.X, foe.Position.Z), new(foeMarker.X, foeMarker.Z)) < 1e-3F);
        Assert.Equal("ground", run.Session.State.ItemInstances.RequireUnique(Assert.Single(Bound("gift").UniqueItemIds)).Owner.Scope);
        Assert.Equal(DaggerfallNpcPresence.Active, run.Session.State.Npcs.Require(Assert.Single(Bound("person").ActorIds)).Presence);
    }

    /// <summary>
    /// The shipped definitions with three quests, each holding a source Place declaration (a remote tavern from
    /// C0B00Y01, a remote dungeon from K0C00Y07, and Daggerfall's castle from S0000008, whose quest teleports the
    /// player there) and a source person, foe and item to place at it.
    /// </summary>
    private static DaggerfallDefinitions Definitions()
    {
        JsonObject root = TestPayload.Sections("questSources");
        JsonArray declarations = root["questSources"]!["resources"]!["declarations"]!.AsArray();
        JsonArray quests = root["questSources"]!["quests"]!.AsArray();
        JsonNode Find(Func<JsonNode, bool> match) => declarations.First(value => match(value!))!;
        string Text(JsonNode node, string property) => node[property]!.GetValue<string>();
        JsonNode foe = Find(value => Text(value, "kind") == "foe" && Text(value, "targetSourceSpelling") == "Giant_rat");
        JsonNode item = Find(value => Text(value, "sourceFile") == "S0000502.txt" && value["symbol"]!["canonicalId"]!.GetValue<string>() == "reward");
        JsonNode person = Find(value => Text(value, "kind") == "person" && value["person"]!["named"] is not null && value["person"]!["atHome"]!.GetValue<bool>());
        foreach ((string quest, string file, string source, string? action) in new[]
        {
            ("remote-building", "C0B00Y01.txt", "Place _tavern_ remote tavern", (string?)null),
            ("remote-dungeon", "K0C00Y07.txt", "Place _mondung_ remote dungeon", null),
            ("permanent", "S0000008.txt", "Place _daggerfall_ permanent DaggerfallCastle2", "teleport pc to _destination_"),
        })
        {
            JsonNode place = Find(value => Text(value, "sourceFile") == file && Text(value, "sourceText") == source);
            foreach ((JsonNode declaration, string symbol) in new[] { (place, "destination"), (foe, "enemy"), (item, "gift"), (person, "person") })
            {
                JsonNode added = declaration.DeepClone();
                added["quest"] = quest;
                added["sourceFile"] = quest + ".txt";
                added["symbol"]!["canonicalId"] = symbol;
                added["symbol"]!["sourceSpelling"] = $"_{symbol}_";
                if (symbol == "person") { added["person"]!["atHome"] = false; added["person"]!["gender"] = "female"; }
                declarations.Add(added);
            }
            JsonArray blocks = action is null ? [] : [new JsonObject { ["kind"] = "headless", ["firstLine"] = 1, ["lines"] = new JsonArray(action), ["global"] = null }];
            quests.Add(new JsonObject
            {
                ["name"] = quest, ["displayName"] = "", ["sourceFile"] = quest + ".txt", ["disposition"] = "compiled",
                ["messages"] = new JsonArray(), ["blocks"] = blocks, ["diagnostics"] = new JsonArray(),
            });
        }
        return TestPayload.WithQuestSections(root);
    }
}
