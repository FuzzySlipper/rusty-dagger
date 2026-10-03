using System.Text;
using System.Text.Json.Nodes;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class QuestNpcAdmissionTests
{
    [Fact]
    public void Ordinary_home_queue_materializes_one_noncombat_person_and_restores_actual_projection_and_name()
    {
        var definitions = Definitions();
        using var fixture = new SanguineRoseSessionTests.Fixture(definitions: definitions);
        var session = fixture.Session;
        int combatants = session.State.Actors.All.Count();
        var started = session.State.Quests.Start(new("npc", "npc.txt", "npc", DaggerfallQuestLifecycle.Active, null, [], []));
        var person = started.Resources.Single();
        Assert.Equal(DaggerfallQuestResourceBindingKind.Pending, person.Binding.Kind);
        Assert.True(started.Placements.Single().AutomaticHome);
        Assert.Null(started.Placements.Single().Applied);
        var appearance = person.SelectedPerson!.Appearance!.Value;
        var faction = definitions.Factions.Factions[person.SelectedPerson.FactionId];
        Assert.Equal(faction.FlatVisuals[0].Archive, appearance.BillboardArchive);
        Assert.Equal(faction.FlatVisuals[0].Record, appearance.BillboardRecord);
        Assert.True(fixture.Inputs.BillboardSprites.ContainsKey((appearance.BillboardArchive, appearance.BillboardRecord)));
        Assert.NotEmpty(fixture.Inputs.QuestMarkers);
        // The ordinary admitted update consumes Home requests; no separate host or task-action stub.
        fixture.Update();
        Assert.True(session.State.Quests.TryGet(started.InstanceId, out var admitted));
        long id = admitted!.Resources.Single().Binding.ActorIds.Single();
        Assert.NotNull(admitted.Placements.Single().Applied);
        var npc = session.State.Npcs.Require(id);
        Assert.Equal(person.SelectedPerson.DisplayName, npc.DisplayName);
        Assert.Equal(fixture.Inputs.ProfileKey, npc.Profile);
        var entity = session.State.Actors.Entities.Resolve(ActorsState.Identity(id));
        Assert.True(session.State.Actors.Store.Has<DaggerfallNpcBody>(entity));
        Assert.False(session.State.Actors.Store.Has<ActorBody>(entity));
        Assert.False(session.State.Actors.Store.Has<StatsComponent>(entity));
        Assert.False(session.State.Actors.Store.Has<InventoryComponent>(entity));
        Assert.Equal(combatants, session.State.Actors.All.Count());
        var body = session.State.Actors.Store.Get<DaggerfallNpcBody>(entity);
        var save = DaggerfallSavePayload.Read(session.CaptureSave());
        var quest = save.Quests.Instances.Single();
        var forged = save with { Quests = save.Quests with { Instances = [quest with
            { Resources = quest.Resources.Select(value => value with { Binding = DaggerfallQuestResourceBinding.Actors(2000) }).ToArray() }] } };
        var encoded = DaggerfallSavePayload.Encode(forged);
        Assert.Contains("NPC identity", Assert.Throws<ArgumentException>(() => DaggerfallSession.Restore(fixture.Engine.Context, fixture.Composition, encoded)).Message);
        foreach (bool placedProfile in new[] { false, true })
        {
            var alias = save with { Npcs = save.Npcs with { Entries = save.Npcs.Entries.Select(value => value.DurableId == id
                ? value with { DurableId = 2000, Profile = placedProfile ? value.Profile : null } : value).ToArray() } };
            Assert.Contains("aliases", Assert.Throws<ArgumentException>(() => DaggerfallSession.Restore(fixture.Engine.Context, fixture.Composition,
                DaggerfallSavePayload.Encode(alias))).Message);
        }
        var wrongKind = save with { Npcs = save.Npcs with { Entries = save.Npcs.Entries.Select(value => value.DurableId == id
            ? value with { Kind = (int)DaggerfallNpcKind.Static } : value).ToArray() } };
        Assert.Contains("Questor", Assert.Throws<ArgumentException>(() => wrongKind.ResolveRestore(definitions, fixture.Inputs, null, DaggerfallTuning.Defaults)).Message);
        // An explicitly selected existing giver may be static; hidden people retain their
        // durable binding and text while the canonical registry suppresses projection.
        var explicitGiver = wrongKind with { Quests = save.Quests with { Instances = [quest with
            { Resources = quest.Resources.Select(value => value with { SelectedPerson = value.SelectedPerson! with { QuestorId = id } }).ToArray() }] } };
        _ = explicitGiver.ResolveRestore(definitions, fixture.Inputs, null, DaggerfallTuning.Defaults);
        var hidden = save with { Npcs = save.Npcs with { Entries = save.Npcs.Entries.Select(value => value.DurableId == id
            ? value with { Presence = (int)DaggerfallNpcPresence.Hidden } : value).ToArray() } };
        _ = hidden.ResolveRestore(definitions, fixture.Inputs, null, DaggerfallTuning.Defaults);
        var changedGiver = explicitGiver with { Quests = explicitGiver.Quests with { Instances = [explicitGiver.Quests.Instances.Single() with
            { Resources = explicitGiver.Quests.Instances.Single().Resources.Select(value => value with
                { SelectedPerson = value.SelectedPerson! with { QuestorId = id + 1 } }).ToArray() }] } };
        Assert.Contains("selected Person meaning", Assert.Throws<ArgumentException>(() => changedGiver.ResolveRestore(definitions, fixture.Inputs, null, DaggerfallTuning.Defaults)).Message);
        session.State.Quests.AdmitPlacements(fixture.Inputs, session);
        session.ReconcileNpcProjection();
        Assert.Single(session.State.Npcs.All, value => value.Kind == DaggerfallNpcKind.Questor);
        Assert.Equal(entity, session.State.Actors.Entities.Resolve(ActorsState.Identity(id)));
        using var restored = fixture.Restore();
        Assert.Equal(npc.DisplayName, restored.State.Npcs.Require(id).DisplayName);
        Assert.Equal(npc.Profile, restored.State.Npcs.Require(id).Profile);
        var restoredEntity = restored.State.Actors.Entities.Resolve(ActorsState.Identity(id));
        Assert.Equal(body.Pose, restored.State.Actors.Store.Get<DaggerfallNpcBody>(restoredEntity).Pose);
        Assert.Equal(combatants, restored.State.Actors.All.Count());
        Assert.NotNull(restored.State.Quests.Capture().Instances.Single().Placements.Single().Applied);
        var dialogue = new DaggerfallDialogueService(restored.State.Npcs, restored.State.Actors, restored.State.Social,
            restored.State.SkillUses, restored.State.Actors.Player.Stats, definitions, RandomMinimum.Create(),
            () => restored.Site.ActiveSite, () => restored.State.Character.Identity, _ => { }, _ => { });
        var target = Assert.Single(dialogue.NpcTargets());
        Assert.Equal(restoredEntity, target.Entity);
        Assert.True(dialogue.ActivateNpc(new(Modules.Interaction.DaggerfallActivationMode.Talk, target)).Applied);
        // Actual site unloading and encoded inactive restore preserve identity and receipts;
        // re-entry rebuilds the existing Appearance projection without repeating placement.
        var profiles = new DaggerfallSiteProfiles([fixture.Inputs, fixture.Castle]);
        restored.AdmitSiteProfiles(profiles);
        Assert.True(restored.TryTransitionTo(fixture.Castle.ProfileKey));
        Assert.False(restored.State.Actors.Entities.TryResolve(ActorsState.Identity(id), out _));
        using var inactive = DaggerfallSession.Restore(fixture.Engine.Context,
            fixture.Composition with { Profiles = profiles }, restored.CaptureSave());
        Assert.False(inactive.State.Actors.Entities.TryResolve(ActorsState.Identity(id), out _));
        Assert.True(inactive.TryTransitionTo(fixture.Inputs.ProfileKey));
        var returnedEntity = inactive.State.Actors.Entities.Resolve(ActorsState.Identity(id));
        Assert.Equal(body.Pose, inactive.State.Actors.Store.Get<DaggerfallNpcBody>(returnedEntity).Pose);
        Assert.Equal(npc.DisplayName, inactive.State.Npcs.Require(id).DisplayName);
        Assert.Single(inactive.State.Npcs.All, value => value.Kind == DaggerfallNpcKind.Questor);
        Assert.Single(inactive.State.Quests.Capture().Instances.Single().Placements);
        Assert.NotNull(inactive.State.Quests.Capture().Instances.Single().Placements.Single().Applied);
    }

    [Fact]
    public void Explicit_placement_replaces_unadmitted_home_request()
    {
        var definitions = Definitions();
        using var fixture = new SanguineRoseSessionTests.Fixture(definitions: definitions);
        var quests = fixture.Session.State.Quests;
        var started = quests.Start(new("npc", "npc.txt", "npc", DaggerfallQuestLifecycle.Active, null, [], []));
        string symbol = started.Resources.Single().Symbol;
        quests.RequestPlacement(started.InstanceId, "explicit", symbol, symbol + ".home");
        var request = quests.Capture().Instances.Single().Placements.Single();
        Assert.Equal("explicit", request.Id);
        Assert.False(request.AutomaticHome);
        fixture.Update();
        Assert.Single(fixture.Session.State.Npcs.All, value => value.Kind == DaggerfallNpcKind.Questor);
    }

    [Fact]
    public void Explicit_civilian_giver_relocates_from_an_inactive_profile_without_duplicate_social_or_actor_identity()
    {
        var definitions = Definitions(explicitGiver: true);
        using var fixture = new SanguineRoseSessionTests.Fixture(definitions: definitions);
        var session = fixture.Session;
        var state = session.State;
        var faction = definitions.Factions.Factions.Values.First(value => value.FlatVisuals.Count > 0
            && fixture.Inputs.BillboardSprites.ContainsKey((value.FlatVisuals[0].Archive, value.FlatVisuals[0].Record)));
        var flat = faction.FlatVisuals[0];
        var site = definitions.Locations.Records.Single(value => value.Id == fixture.Inputs.Site);
        long id = state.Npcs.RegisterCivilian(new(site.Region, site.Name, ""), new("breton", "Male", flat.Archive, flat.Record, 17, faction.Id), "quest giver", ["talk"]);
        state.Npcs.SetDisplayName(id, "Existing Giver");
        session.MaterializeNpcActor(id, new(new(2, 3, 4), .25f));
        state.Npcs.Place(id, fixture.Inputs.ProfileKey, new(2, 3, 4));
        state.Actors.Get(id).Stats.GetTrack(Rusty.Engine.Mechanics.TrackId.Parse("health")).SetCurrent(2, clamp: true);
        double health = state.Actors.Get(id).Stats.GetTrack(Rusty.Engine.Mechanics.TrackId.Parse("health")).Current;
        var started = state.Quests.Start(new("giver", "npc.txt", "npc", DaggerfallQuestLifecycle.Active, null, [], []) { QuestorId = id });
        var person = started.Resources.Single();
        Assert.Equal(id, person.SelectedPerson!.QuestorId);
        Assert.Equal("Existing Giver", person.Text!.Name);
        // Fixture composition shares the actual published NPC sprite catalog, while retaining
        // the destination's own normalized world, geometry and source quest markers.
        var source = QuestWorldAdmissionTests.WithMarker(fixture.Castle);
        var castle = new DaggerfallSiteProfile(source.Project, source.SpatialArtifact, source.StaticMesh, source.WorldAppearance,
            source.InitialLook, source.Materials, source.ActorSprites, source.MobileSprites, source.Audio, source.ClassicPresentation,
            source.Site, source.Doors, source.ProfileKind, source.ProfileKey.LogicalId, source.Portals, source.Anchors.Values.ToArray(),
            source.Lights, source.GroundContainerSprite, source.DungeonMap, source.DungeonActions, source.DungeonActionModels,
            source.InteriorBuilding, source.Music, source.AudioBundle, source.QuestMarkers, fixture.Inputs.BillboardSprites);
        Assert.NotEmpty(castle.QuestMarkers);
        var target = definitions.Locations.Records.Single(value => value.Id == castle.Site);
        var destination = DaggerfallQuestResourceBinding.Place(new(target.Region, target.Index)) with
            { PlaceSelection = new(castle.ProfileKind, target.MapId, null, 0) };
        state.Quests.SetResource(started.InstanceId, person with { SelectedPerson = person.SelectedPerson with
            { Home = new(destination, new(Name: target.Name)) } });
        state.Quests.RequestPlacement(started.InstanceId, "relocate-giver", person.Symbol, person.Symbol + ".home");
        var profiles = new DaggerfallSiteProfiles([fixture.Inputs, castle]);
        session.AdmitSiteProfiles(profiles);
        Assert.True(session.TryTransitionTo(castle.ProfileKey));
        Assert.False(state.Actors.TryGet(id, out _));
        using var restored = fixture.Restore(profiles);
        restored.State.Quests.AdmitPlacements(castle, restored);
        restored.State.Quests.AdmitPlacements(castle, restored);
        Assert.Equal(health, restored.State.Actors.Get(id).Stats.GetTrack(Rusty.Engine.Mechanics.TrackId.Parse("health")).Current);
        Assert.Equal(castle.ProfileKey, restored.State.Npcs.Require(id).Profile);
        Assert.Equal(state.Npcs.Require(id).Site, restored.State.Npcs.Require(id).Site); // stable social origin
        Assert.Single(restored.State.Npcs.All, value => value.DurableId == id);
        Assert.Equal(id, restored.State.Quests.Capture().Instances.Single().Resources.Single().Binding.ActorIds.Single());
        Assert.True(restored.TryTransitionTo(fixture.Inputs.ProfileKey));
        Assert.False(restored.State.Actors.TryGet(id, out _));
        Assert.Empty(restored.MaterializeNpcActors(default));
        using var inactive = DaggerfallSession.Restore(fixture.Engine.Context, fixture.Composition with { Profiles = profiles }, restored.CaptureSave());
        Assert.True(inactive.TryTransitionTo(castle.ProfileKey));
        Assert.Equal(health, inactive.State.Actors.Get(id).Stats.GetTrack(Rusty.Engine.Mechanics.TrackId.Parse("health")).Current);
        Assert.Single(DaggerfallSavePayload.Read(inactive.CaptureSave()).DynamicActors, value => value.EntityId == id);
    }

    private static DaggerfallDefinitions Definitions(bool explicitGiver = false)
    {
        var root = JsonNode.Parse(TestPayload.CombinedText)!.AsObject();
        var declarations = root["questSources"]!["resources"]!["declarations"]!.AsArray();
        var person = declarations.First(value => value!["kind"]!.GetValue<string>() == "person"
            && (explicitGiver ? value["person"]!["group"]?.GetValue<string>() == "Questor"
                : value["person"]!["named"] is not null && value["person"]!["atHome"]!.GetValue<bool>()))!.DeepClone();
        person["quest"] = "npc"; person["sourceFile"] = "npc.txt";
        person["person"]!["gender"] = "female";
        declarations.Add(person);
        root["questSources"]!["quests"]!.AsArray().Add(JsonNode.Parse("""
            {"name":"npc","displayName":"","sourceFile":"npc.txt","disposition":"compiled","messages":[],"blocks":[],"diagnostics":[]}
            """));
        return DaggerfallBaseContent.Read(Encoding.UTF8.GetBytes(root.ToJsonString()));
    }
}
