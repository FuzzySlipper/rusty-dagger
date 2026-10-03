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

    private static DaggerfallDefinitions Definitions()
    {
        var root = JsonNode.Parse(TestPayload.CombinedText)!.AsObject();
        var declarations = root["questSources"]!["resources"]!["declarations"]!.AsArray();
        var person = declarations.First(value => value!["kind"]!.GetValue<string>() == "person"
            && value["person"]!["named"] is not null && value["person"]!["atHome"]!.GetValue<bool>())!.DeepClone();
        person["quest"] = "npc"; person["sourceFile"] = "npc.txt";
        person["person"]!["gender"] = "female";
        declarations.Add(person);
        root["questSources"]!["quests"]!.AsArray().Add(JsonNode.Parse("""
            {"name":"npc","displayName":"","sourceFile":"npc.txt","disposition":"compiled","messages":[],"blocks":[],"diagnostics":[]}
            """));
        return DaggerfallBaseContent.Read(Encoding.UTF8.GetBytes(root.ToJsonString()));
    }
}
