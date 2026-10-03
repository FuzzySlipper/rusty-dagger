using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WorldRpg.Kit.Actors;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class QuestNpcOverlayTests
{
    [Theory]
    [InlineData("add _person_ as questor", DaggerfallQuestTaskOperationKind.AddQuestor)]
    [InlineData("drop _person_ as questor", DaggerfallQuestTaskOperationKind.DropQuestor)]
    [InlineData("add _person_ face", DaggerfallQuestTaskOperationKind.AddFace)]
    [InlineData("add _person_ face saying 100", DaggerfallQuestTaskOperationKind.AddFace)]
    [InlineData("add foe _enemy_ face saying 100", DaggerfallQuestTaskOperationKind.AddFace)]
    [InlineData("drop _person_ face", DaggerfallQuestTaskOperationKind.DropFace)]
    [InlineData("drop foe _enemy_ face", DaggerfallQuestTaskOperationKind.DropFace)]
    [InlineData("mute npc _person_", DaggerfallQuestTaskOperationKind.MuteNpc)]
    internal void Source_variants_compile_to_owned_actions(string action, DaggerfallQuestTaskOperationKind kind)
    {
        var source = new DaggerfallQuestSourceDefinition("npc", "", "npc.txt", DaggerfallQuestDisposition.Compiled, [], [new("headless", 1, [action], null)], []);
        Assert.Equal(kind, Assert.Single(Assert.Single(DaggerfallQuestTaskCompiler.Compile(source).Tasks).Operations).Kind);
        Assert.Empty(DaggerfallQuestTaskCompiler.Assess(source));
    }

    [Fact]
    public void Two_quests_share_the_actual_npc_without_overwriting_base_state_and_cleanup_only_their_own_overlays()
    {
        var definitions = Definitions(["add _contact_ as questor", "add _contact_ face", "mute npc _contact_"]);
        using var f = new SanguineRoseSessionTests.Fixture(definitions: definitions);
        long id = Giver(f, definitions);
        var original = f.Session.State.Npcs.Require(id);
        var dialogue = f.Session.Dialogue;
        var target = Assert.Single(dialogue.NpcTargets(), value => value.Identity == ActorsState.Identity(id));
        Assert.True(dialogue.ActivateNpc(new(DaggerfallActivationMode.Talk, target)).Applied);
        Start(f, "first", id); Start(f, "second", id);
        f.Update();
        var quests = f.Session.State.Quests;
        Assert.Null(dialogue.CurrentNpc());
        Assert.Null(f.Session.ActivationView.Dialogue);
        Assert.True(quests.IsNpcMuted(id));
        Assert.Equal(2, quests.QuestContacts(id).Count);
        Assert.DoesNotContain(dialogue.NpcTargets(), value => value.Identity == ActorsState.Identity(id));
        AssertBaseNpc(original, f.Session.State.Npcs.Require(id));
        var faces = quests.ReadPresentation(_ => DaggerfallQuestMessageContext.Empty).EscortFaces;
        Assert.Equal(2, faces.Count);
        Assert.All(faces, value => Assert.StartsWith("character.head.", value.MediaId));
        using var restored = f.Restore();
        Assert.Equal(faces, restored.State.Quests.ReadPresentation(_ => DaggerfallQuestMessageContext.Empty).EscortFaces);
        Assert.Equal(2, restored.State.Quests.QuestContacts(id).Count);
        restored.State.Quests.Complete("first", "done");
        Assert.True(restored.State.Quests.IsNpcMuted(id));
        Assert.Equal("second", Assert.Single(restored.State.Quests.QuestContacts(id)).InstanceId);
        Assert.Equal("second", Assert.Single(restored.State.Quests.ReadPresentation(_ => DaggerfallQuestMessageContext.Empty).EscortFaces).InstanceId);
        restored.State.Quests.Complete("second", "done");
        Assert.False(restored.State.Quests.IsNpcMuted(id));
        Assert.Empty(restored.State.Quests.QuestContacts(id));
        Assert.Empty(restored.State.Quests.ReadPresentation(_ => DaggerfallQuestMessageContext.Empty).EscortFaces);
        Assert.Equal(original.Appearance, restored.State.Npcs.Require(id).Appearance);
        Assert.Equal(original.Kind, restored.State.Npcs.Require(id).Kind);
        Assert.Contains(restored.Dialogue.NpcTargets(), value => value.Identity == ActorsState.Identity(id));
    }

    [Fact]
    public void Normal_drop_actions_remove_only_resource_owned_relation_and_portrait()
    {
        var definitions = Definitions(["add _contact_ as questor", "add _contact_ face", "drop _contact_ as questor", "drop _contact_ face"]);
        using var f = new SanguineRoseSessionTests.Fixture(definitions: definitions);
        long id = Giver(f, definitions);
        var original = f.Session.State.Npcs.Require(id);
        Start(f, "drop", id); f.Update();
        Assert.Empty(f.Session.State.Quests.QuestContacts(id));
        Assert.Empty(f.Session.State.Quests.ReadPresentation(_ => DaggerfallQuestMessageContext.Empty).EscortFaces);
        AssertBaseNpc(original, f.Session.State.Npcs.Require(id));
        using var restored = f.Restore();
        Assert.Empty(restored.State.Quests.QuestContacts(id));
    }

    [Fact]
    public void Source_clear_rearms_mute_but_face_action_does_not_repeat()
    {
        var definitions = Definitions(["add _contact_ face", "mute npc _contact_", "clear headless.1"], rearm: true);
        using var f = new SanguineRoseSessionTests.Fixture(definitions: definitions);
        long id = Giver(f, definitions); Start(f, "rearm", id); f.Update();
        Assert.False(f.Session.State.Quests.IsNpcMuted(id));
        var quest = f.Session.State.Quests.Capture().Instances.Single();
        var resource = quest.Resources.Single();
        Assert.NotNull(resource.EscortFaceMedia);
        f.Session.State.Quests.SetResource(quest.InstanceId, resource with { EscortFaceMedia = null, EscortFaceOrder = 0 });
        f.Update();
        Assert.Null(f.Session.State.Quests.Capture().Instances.Single().Resources.Single().EscortFaceMedia);
        Assert.False(f.Session.State.Quests.IsNpcMuted(id));
    }

    [Theory]
    [InlineData("Male")]
    [InlineData("Female")]
    public void Child_source_face_resolves_only_its_gender_portraits_and_survives_encoded_restore(string gender)
    {
        var definitions = Definitions(["add _contact_ face"]);
        using var f = new SanguineRoseSessionTests.Fixture(definitions: definitions);
        long id = Giver(f, definitions, 514, gender); Start(f, "child", id); f.Update();
        var face = Assert.Single(f.Session.State.Quests.ReadPresentation(_ => DaggerfallQuestMessageContext.Empty).EscortFaces);
        var media = definitions.CharacterPresentation.ChildFaces.Single(value => value.MediaId == face.MediaId);
        Assert.Equal(gender == "Female" ? 1 : 0, media.Index % 2);
        using var restored = f.Restore();
        Assert.Equal(face, Assert.Single(restored.State.Quests.ReadPresentation(_ => DaggerfallQuestMessageContext.Empty).EscortFaces));
    }

    [Fact]
    public void Foe_source_portrait_selection_is_saved_once_and_drop_uses_the_same_resource()
    {
        var definitions = QuestWorldAdmissionTests.Definitions(actions: ["add foe _enemy_ face"]);
        using var f = new SanguineRoseSessionTests.Fixture(definitions: definitions);
        var site = definitions.Locations.Records.Single(value => value.Id == f.Inputs.Site);
        f.Session.State.Quests.Start(new("foe", "world-test.txt", "world-test", DaggerfallQuestLifecycle.Active, null,
            [new("location", DaggerfallQuestResourceBinding.Place(new(site.Region, site.Index)) with { PlaceSelection = new(f.Inputs.ProfileKind, site.MapId, null, 0) })], []));
        f.Update();
        var face = Assert.Single(f.Session.State.Quests.ReadPresentation(_ => DaggerfallQuestMessageContext.Empty).EscortFaces);
        Assert.StartsWith("character.head.male.00.", face.MediaId);
        using var restored = f.Restore();
        restored.Update(new Rusty.Engine.ProductUpdate(TestSessions.OuterUpdate(1), []));
        Assert.Equal(face, Assert.Single(restored.State.Quests.ReadPresentation(_ => DaggerfallQuestMessageContext.Empty).EscortFaces));
        restored.State.Quests.Complete("foe", "done");
        Assert.Empty(restored.State.Quests.ReadPresentation(_ => DaggerfallQuestMessageContext.Empty).EscortFaces);
    }

    [Fact]
    public void Optional_saying_is_published_once_with_source_identity_before_adding_the_face()
    {
        var definitions = Definitions(["add _contact_ face saying 100"]);
        using var f = new SanguineRoseSessionTests.Fixture(definitions: definitions);
        long id = Giver(f, definitions); Start(f, "saying", id); f.Update(); f.Update();
        var view = f.Session.State.Quests.ReadPresentation(_ => DaggerfallQuestMessageContext.Empty);
        var saying = Assert.Single(view.Deliveries);
        Assert.Equal("saying", saying.InstanceId); Assert.Equal(100, saying.MessageId);
        Assert.Equal("A companion joins you.", saying.Text);
        Assert.Single(view.EscortFaces);
        using var restored = f.Restore();
        Assert.Single(restored.State.Quests.ReadPresentation(_ => DaggerfallQuestMessageContext.Empty).Deliveries);
    }

    [Fact]
    public void Encoded_current_overlay_rejects_a_published_face_of_the_wrong_gender()
    {
        var definitions = Definitions(["add _contact_ face"]);
        using var f = new SanguineRoseSessionTests.Fixture(definitions: definitions);
        long id = Giver(f, definitions); Start(f, "invalid", id); f.Update();
        var save = f.Session.State.Quests.Capture();
        var quest = save.Instances.Single();
        var resource = quest.Resources.Single();
        var malformed = save with { Instances = [quest with { Resources = [resource with { EscortFaceMedia = "character.head.female.00.0" }] }] };
        string json = JsonSerializer.Serialize(malformed, typeof(DaggerfallQuestInstancesSave), DaggerfallSaveJsonContext.Default);
        var decoded = (DaggerfallQuestInstancesSave)JsonSerializer.Deserialize(json, typeof(DaggerfallQuestInstancesSave), DaggerfallSaveJsonContext.Default)!;
        var owner = new DaggerfallQuestInstances(definitions, RandomMinimum.Create());
        Assert.Contains("incompatible selected escort face", Assert.Throws<ArgumentException>(() => owner.Restore(decoded)).Message);
    }

    [Fact]
    public void Portrait_order_and_three_visible_slots_survive_restore_and_reveal_the_waiting_fourth_face()
    {
        var definitions = Definitions(["add _contact_ face"]);
        using var f = new SanguineRoseSessionTests.Fixture(definitions: definitions);
        long id = Giver(f, definitions);
        foreach (string instance in new[] { "z-first", "a-second", "m-third", "n-fourth" }) { Start(f, instance, id); f.Update(); }
        var expected = new[] { "z-first", "a-second", "m-third" };
        Assert.Equal(expected, f.Session.State.Quests.ReadPresentation(_ => DaggerfallQuestMessageContext.Empty).EscortFaces.Select(value => value.InstanceId));
        Assert.Equal(4, f.Session.State.Quests.Capture().Instances.Count(value => value.Resources.Single().EscortFaceMedia is not null));
        using var restored = f.Restore();
        Assert.Equal(expected, restored.State.Quests.ReadPresentation(_ => DaggerfallQuestMessageContext.Empty).EscortFaces.Select(value => value.InstanceId));
        restored.State.Quests.Complete("z-first", "done");
        Assert.Equal(new[] { "a-second", "m-third", "n-fourth" }, restored.State.Quests.ReadPresentation(_ => DaggerfallQuestMessageContext.Empty).EscortFaces.Select(value => value.InstanceId));
    }

    private static void AssertBaseNpc(DaggerfallNpc expected, DaggerfallNpc actual)
    {
        Assert.Equal(expected.DurableId, actual.DurableId);
        Assert.Equal(expected.Kind, actual.Kind);
        Assert.Equal(expected.Appearance, actual.Appearance);
        Assert.Equal(expected.DisplayName, actual.DisplayName);
        Assert.Equal(expected.Site, actual.Site);
        Assert.Equal(expected.Services, actual.Services);
    }

    private static void Start(SanguineRoseSessionTests.Fixture f, string instance, long id) =>
        f.Session.State.Quests.Start(new(instance, "overlay.txt", "overlay", DaggerfallQuestLifecycle.Active, null, [], []) { QuestorId = id });

    private static long Giver(SanguineRoseSessionTests.Fixture f, DaggerfallDefinitions definitions, int? factionId = null, string gender = "Male")
    {
        var faction = definitions.Factions.Factions.Values.First(value => (factionId is null ? value.Type != 0 && value.Type != 4 && value.Id != 514 : value.Id == factionId) && value.FlatVisuals.Count > 0
            && f.Inputs.BillboardSprites.ContainsKey((value.FlatVisuals[0].Archive, value.FlatVisuals[0].Record)));
        var flat = faction.FlatVisuals[0];
        var site = definitions.Locations.Records.Single(value => value.Id == f.Inputs.Site);
        long id = f.Session.State.Npcs.RegisterCivilian(new(site.Region, site.Name, ""), new("breton", gender, flat.Archive, flat.Record, 17, faction.Id), "quest giver", ["talk"]);
        f.Session.State.Npcs.SetDisplayName(id, "Existing Giver");
        f.Session.MaterializeNpcActor(id, new(new(2, 3, 4), .25f));
        f.Session.State.Npcs.Place(id, f.Inputs.ProfileKey, new(2, 3, 4));
        return id;
    }

    private static DaggerfallDefinitions Definitions(string[] actions, bool rearm = false)
    {
        var root = JsonNode.Parse(TestPayload.CombinedText)!.AsObject();
        var declarations = root["questSources"]!["resources"]!["declarations"]!.AsArray();
        var person = declarations.First(value => value!["kind"]!.GetValue<string>() == "person" && value["person"]!["group"]?.GetValue<string>() == "Questor")!.DeepClone();
        person["quest"] = "overlay"; person["sourceFile"] = "overlay.txt";
        person["symbol"]!["canonicalId"] = "contact"; person["symbol"]!["sourceSpelling"] = "_contact_";
        declarations.Add(person);
        var blocks = new JsonArray();
        if (rearm)
        {
            blocks.Add(JsonNode.Parse("""{"kind":"variable","firstLine":99,"lines":["variable _stop_"],"global":null}"""));
            blocks.Add(JsonNode.Parse("""{"kind":"task","firstLine":100,"lines":["until _stop_ performed:","start task headless.1"],"global":null}"""));
        }
        blocks.Add(new JsonObject { ["kind"] = "headless", ["firstLine"] = 1, ["lines"] = JsonSerializer.SerializeToNode(actions), ["global"] = null });
        root["questSources"]!["quests"]!.AsArray().Add(new JsonObject { ["name"] = "overlay", ["displayName"] = "", ["sourceFile"] = "overlay.txt", ["disposition"] = "compiled", ["messages"] = new JsonArray(new JsonObject { ["id"] = 100, ["firstLine"] = 1, ["lines"] = new JsonArray("A companion joins you.") }), ["blocks"] = blocks, ["diagnostics"] = new JsonArray() });
        return DaggerfallBaseContent.Read(Encoding.UTF8.GetBytes(root.ToJsonString()));
    }
}
