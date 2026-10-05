using WorldRpg.Kit.Actors;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class QuestDialogueLinkTests
{
    [Theory]
    [InlineData("location _location_")]
    [InlineData("person _person_")]
    [InlineData("item _gift_")]
    [InlineData("location _location_ person _person_")]
    [InlineData("location _location_ item _gift_")]
    [InlineData("person _person_ item _gift_")]
    [InlineData("location _location_ person _person_ item _gift_")]
    public void Every_source_subject_combination_compiles(string subjects)
    {
        foreach (string verb in new[] { "add dialog", "dialog link" })
        {
            var source = new DaggerfallQuestSourceDefinition("dialog", "", "dialog.txt", DaggerfallQuestDisposition.Compiled, [],
                [new("headless", 1, [$"{verb} for {subjects}"], null)], []);
            Assert.Empty(DaggerfallQuestTaskCompiler.Assess(source));
        }
    }

    [Fact]
    public void Actual_talk_subjects_hide_reveal_render_live_macros_restore_and_cleanup_per_quest()
    {
        var definitions = QuestWorldAdmissionTests.Definitions(person: true, personQuestor: true, personInfo: "100", personRumor: "101", itemInfo: "102",
            actions: ["dialog link for location _location_ person _person_ item _gift_"],
            taskBlocks: [["_reveal_ task:", "add dialog for location _location_ person _person_ item _gift_"]],
            messages: ["Information about _person_.", "Rumors about _person_.", "Information about _gift_."]);
        using var f = new SanguineRoseSessionTests.Fixture(definitions: definitions);
        long npc = QuestNpcOverlayTests.Giver(f, definitions);
        Start(f, "first", npc); Start(f, "second", npc); Advance(f.Session);
        Assert.Empty(f.Session.State.Quests.DialogueTopics(npc));
        SetTask(f.Session, "first", "reveal"); Advance(f.Session);
        Assert.Equal(3, f.Session.State.Quests.DialogueTopics(npc).Count);
        var resource = Quest(f.Session, "first").Resources.Single(r => r.Symbol == "person");
        Assert.Equal(new[] { "location", "gift" }.Order(), resource.DialogueLinks.Order());
        f.Session.State.Quests.SetResource("first", resource with { Text = resource.Text! with { Name = "Current Person" } });
        Open(f.Session, npc);
        var view = f.Session.ActivationView.Dialogue!;
        Assert.Contains(view.Topics, topic => topic.Id == "direction:quest:first:location");
        string topic = "quest-info:first:person";
        Assert.True(f.Session.Dialogue.ApplyAction(new("dialogue-topic", Revision: view.Revision, Topic: topic)).Applied);
        Assert.Equal("Information about Current Person.", f.Session.ActivationView.Dialogue!.Reply);
        Assert.Empty(f.Session.State.Quests.DialogueRumors());
        Assert.Contains(f.Session.State.Quests.Messages.Deliveries, d => d.InstanceId == "first" && d.MessageId == 100 && d.Delivery == DaggerfallQuestMessageDelivery.Popup);
        Assert.True(f.Session.Dialogue.ApplyAction(new("dialogue-topic", Revision: view.Revision, Topic: "quest-rumor:first:person")).Applied);
        Assert.Equal("first", Assert.Single(f.Session.State.Quests.DialogueRumors()).InstanceId);
        using var restored = f.Restore();
        Assert.Equal(3, restored.State.Quests.DialogueTopics(npc).Count);
        Open(restored, npc);
        Assert.True(restored.Dialogue.ApplyAction(new("dialogue-topic", Revision: restored.ActivationView.Dialogue!.Revision, Topic: topic)).Applied);
        Assert.Equal("Information about Current Person.", restored.ActivationView.Dialogue!.Reply);
        SetTask(restored, "second", "reveal"); Advance(restored);
        restored.State.Quests.Complete("first", "done");
        Assert.All(restored.State.Quests.DialogueTopics(npc), value => Assert.Equal("second", value.InstanceId));
        Assert.Empty(Quest(restored, "first").Resources.SelectMany(value => value.DialogueLinks));
        Assert.False(restored.Dialogue.ApplyAction(new("dialogue-topic", Revision: restored.ActivationView.Dialogue!.Revision, Topic: topic)).Applied);
    }

    [Fact]
    public void Displaying_primary_resource_names_reveals_only_that_quests_named_subjects()
    {
        var definitions = QuestWorldAdmissionTests.Definitions(person: true, personQuestor: true, personInfo: "100", itemInfo: "100",
            actions: ["dialog link for location _location_ person _person_ item _gift_", "say 100"],
            messages: ["Ask _person_ about __gift_ and _missing_.", "Journal names _gift_."]);
        using var f = new SanguineRoseSessionTests.Fixture(definitions: definitions);
        long npc = QuestNpcOverlayTests.Giver(f, definitions);
        Start(f, "first", npc); Start(f, "second", npc); Advance(f.Session);
        Assert.Empty(f.Session.State.Quests.DialogueTopics(npc));
        var first = f.Session.State.Quests.Messages.Deliveries.First(value => value.InstanceId == "first");
        f.Session.State.Quests.Messages.Dismiss("second", f.Session.State.Quests.Messages.Deliveries.Last().EntryId);
        var rendered = f.Session.State.Quests.ReadPresentation(q => new(DaggerfallQuestMessageContext.Empty.Text,
            q.Resources.ToDictionary(value => value.Symbol, value => value.Text ?? new())));
        Assert.Contains(rendered.Deliveries.Single().Diagnostics, message => message.Contains("missing"));
        Assert.Equal("first", Assert.Single(f.Session.State.Quests.DialogueTopics(npc)).InstanceId);
        Assert.False(Quest(f.Session, "first").Resources.Single(value => value.Symbol == "gift").DialogueVisible);
        Assert.False(Quest(f.Session, "second").Resources.Single(value => value.Symbol == "person").DialogueVisible);
        var saved = f.Session.State.Quests.Capture();
        f.Session.State.Quests.Restore(saved with { Messages = saved.Messages with { Journal = [new("first", 1, 101)] } });
        f.Session.State.Quests.ReadPresentation(q => new(DaggerfallQuestMessageContext.Empty.Text,
            q.Resources.ToDictionary(value => value.Symbol, value => value.Text ?? new())));
        Assert.True(Quest(f.Session, "first").Resources.Single(value => value.Symbol == "gift").DialogueVisible);
        using var restored = f.Restore();
        Assert.All(restored.State.Quests.DialogueTopics(npc), topic => Assert.Equal("first", topic.InstanceId));
    }

    [Fact]
    public void Rumor_mill_joins_ordinary_news_in_delivery_order_and_expands_at_display_time()
    {
        var definitions = QuestWorldAdmissionTests.Definitions(person: true, personQuestor: true, noAmbientRumors: true,
            actions: ["rumor mill 100"], messages: ["The rumor concerns _person_."]);
        using var f = new SanguineRoseSessionTests.Fixture(definitions: definitions);
        long npc = QuestNpcOverlayTests.Giver(f, definitions);
        Start(f, "first", npc); Start(f, "second", npc); Advance(f.Session);
        var quests = f.Session.State.Quests;
        Assert.Equal(new[] { "first", "second" }, quests.DialogueRumors().Select(value => value.InstanceId));
        foreach (string id in new[] { "first", "second" })
        {
            var person = Quest(f.Session, id).Resources.Single(r => r.Symbol == "person");
            quests.SetResource(id, person with { Text = person.Text! with { Name = "New Name" } });
        }
        Open(f.Session, npc);
        Assert.True(f.Session.Dialogue.ApplyAction(new("dialogue-topic", Revision: f.Session.ActivationView.Dialogue!.Revision, Topic: "news")).Applied);
        Assert.Equal("The rumor concerns New Name.", f.Session.ActivationView.Dialogue!.Reply);
        using var restored = f.Restore();
        Assert.Equal(new[] { "first", "second" }, restored.State.Quests.DialogueRumors().Select(value => value.InstanceId));
        restored.State.Quests.Complete("first", "done");
        Assert.Equal("second", Assert.Single(restored.State.Quests.DialogueRumors()).InstanceId);
        restored.State.Quests.Complete("second", "done");
        Assert.Empty(restored.State.Quests.DialogueRumors());
    }

    [Fact]
    public void Missing_message_and_stale_link_report_diagnostics_without_running_later_actions()
    {
        var definitions = QuestWorldAdmissionTests.Definitions(person: true, personQuestor: true, personInfo: "999",
            actions: ["add dialog for person _missing_", "rumor mill 100"], messages: ["Not reached."]);
        using var f = new SanguineRoseSessionTests.Fixture(definitions: definitions);
        long npc = QuestNpcOverlayTests.Giver(f, definitions);
        Start(f, "first", npc); Advance(f.Session);
        Assert.Contains("missing", Quest(f.Session, "first").Tasks.First().OperationState[0].UnavailableReason);
        Assert.Empty(f.Session.State.Quests.DialogueRumors());
        Open(f.Session, npc);
        Assert.True(f.Session.Dialogue.ApplyAction(new("dialogue-topic", Revision: f.Session.ActivationView.Dialogue!.Revision, Topic: "quest-info:first:person")).Applied);
        Assert.NotEmpty(f.Session.ActivationView.Dialogue!.Diagnostics);
        using var restored = f.Restore();
        Assert.NotNull(Quest(restored, "first").Tasks.First().OperationState[0].UnavailableReason);
    }

    private static DaggerfallQuestInstanceSave Quest(DaggerfallSession session, string id) => session.State.Quests.Capture().Instances.Single(value => value.InstanceId == id);
    private static void Advance(DaggerfallSession session) => session.State.Quests.Advance(session.State.Variables, DaggerfallCalendar.Start);
    private static void SetTask(DaggerfallSession session, string id, string task)
    {
        var save = session.State.Quests.Capture();
        session.State.Quests.Restore(save with { Instances = save.Instances.Select(value => value.InstanceId != id ? value : value with
            { Tasks = value.Tasks.Select(state => state.Symbol == task ? state with { IsSet = true } : state).ToArray() }).ToArray() });
    }
    private static void Open(DaggerfallSession session, long npc)
    {
        var target = session.Dialogue.NpcTargets().Single(value => value.Identity == ActorsState.Identity(npc));
        Assert.True(session.Dialogue.ActivateNpc(new(DaggerfallActivationMode.Talk, target)).Applied);
    }
    private static void Start(SanguineRoseSessionTests.Fixture f, string id, long npc)
    {
        var site = TestPayload.Definitions.Locations.Records.Single(value => value.Id == f.Inputs.Site);
        f.Session.State.Quests.Start(new(id, "world-test.txt", "world-test", DaggerfallQuestLifecycle.Active, null,
            [new("location", DaggerfallQuestResourceBinding.Place(new(site.Region, site.Index)) with { PlaceSelection = new(f.Inputs.ProfileKind, site.MapId) })], []) { QuestorId = npc });
    }
}
