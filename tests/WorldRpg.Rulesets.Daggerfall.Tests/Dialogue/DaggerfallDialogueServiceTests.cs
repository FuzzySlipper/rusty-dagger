using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>The conversation built without the session, over <see cref="DialogueStage"/>.</summary>
public sealed class DaggerfallDialogueServiceTests
{
    [Fact]
    public void Talking_opens_the_conversation_and_a_toned_question_is_answered_with_one_skill_use()
    {
        using DialogueStage stage = new();
        long baker = stage.Talker();
        DaggerfallDialogueService conversation = stage.Conversation();

        DaggerfallActivationOutcome opened = conversation.ActivateNpc(new(DaggerfallActivationMode.Talk, DialogueStage.Target(conversation, baker)));
        Assert.True(opened.Applied, opened.Message);
        DaggerfallDialogueView opening = Assert.IsType<DaggerfallDialogueView>(stage.View);
        Assert.NotEmpty(opening.Greeting);
        Assert.Equal(stage.Society.Npcs.Require(baker).DisplayName ?? "baker", opening.TargetLabel);
        Assert.Contains(opening.Topics, topic => topic.Id == "directions");
        Assert.Contains(opening.Topics, topic => topic.Id == "news");
        Assert.Equal(baker, conversation.CurrentNpc(opening.Revision)?.DurableId);

        Assert.True(conversation.ChooseTone(opening.Revision, "polite").Applied);
        Assert.Equal("polite", stage.View!.Tone);
        Assert.False(conversation.ChooseTone(opening.Revision, "rude").Applied);
        Assert.True(conversation.Ask(opening.Revision, "directions", null, null).Applied);
        Assert.False(string.IsNullOrWhiteSpace(stage.View!.Question));
        Assert.False(string.IsNullOrWhiteSpace(stage.View.Reply));
        Assert.Equal(1, stage.Society.Actors.Player.Progression.SkillUses["etiquette"]);

        // The same tone again reuses its one skill use.
        Assert.True(conversation.Ask(opening.Revision, "directions", null, null).Applied);
        Assert.Equal(1, stage.Society.Actors.Player.Progression.SkillUses["etiquette"]);
        Assert.False(conversation.Ask(opening.Revision, "gossip", null, null).Applied);
        Assert.Equal("That topic is not available.", stage.Outcomes[^1]);
    }

    [Fact]
    public void Ending_closes_the_page_and_a_choice_at_an_old_revision_is_refused()
    {
        using DialogueStage stage = new();
        long baker = stage.Talker();
        DaggerfallDialogueService conversation = stage.Conversation();
        Assert.True(conversation.ActivateNpc(new(DaggerfallActivationMode.Talk, DialogueStage.Target(conversation, baker))).Applied);
        string revision = stage.View!.Revision;

        Assert.False(conversation.End("0").Applied);
        Assert.Equal("That conversation has already ended.", stage.Outcomes[^1]);
        Assert.True(conversation.End(revision).Applied);
        Assert.Null(stage.View);
        Assert.Null(conversation.CurrentNpc());
        Assert.False(conversation.Ask(revision, "news", null, null).Applied);
        Assert.Equal("That conversation choice is no longer current.", stage.Outcomes[^1]);
    }

    [Fact]
    public void A_person_who_has_moved_ends_the_conversation_when_a_choice_is_made()
    {
        using DialogueStage stage = new();
        long baker = stage.Talker();
        DaggerfallDialogueService conversation = stage.Conversation();
        Assert.True(conversation.ActivateNpc(new(DaggerfallActivationMode.Talk, DialogueStage.Target(conversation, baker))).Applied);
        string revision = stage.View!.Revision;

        ActorState actor = stage.Society.Actors.Get(baker);
        actor.ApplyPose(new ActorPose(new WorldPoint(actor.Position.X + 1f, actor.Position.Y, actor.Position.Z), actor.HeadingYawRadians));

        Assert.False(conversation.Ask(revision, "news", null, null).Applied);
        Assert.Equal("That person has moved or is no longer available.", stage.Outcomes[^1]);
        Assert.Null(stage.View);
    }

    [Fact]
    public void A_quest_topic_is_listed_under_the_quests_heading_and_answered_by_the_quest()
    {
        using DialogueStage stage = new();
        long baker = stage.Talker();
        DaggerfallDialogueService conversation = stage.Conversation(
            questTopics: id => id == baker ? [new("quest-info:q:person", "Ask about Person")] : [],
            resolveQuestTopic: (_, topic) => ($"answer to {topic}", []));
        Assert.True(conversation.ActivateNpc(new(DaggerfallActivationMode.Talk, DialogueStage.Target(conversation, baker))).Applied);
        string revision = stage.View!.Revision;

        DaggerfallDialogueTopicOption quest = Assert.Single(stage.View.Topics, topic => topic.Id == "quest-info:q:person");
        Assert.Equal(DaggerfallDialogueTopicGroups.Quests, quest.Group);
        Assert.True(conversation.Ask(revision, quest.Id, null, null).Applied);
        Assert.Equal("answer to quest-info:q:person", stage.View!.Reply);
        Assert.False(conversation.Ask(revision, "quest-info:q:other", null, null).Applied);
        Assert.Equal("That quest topic is no longer available.", stage.Outcomes[^1]);
    }

    [Fact]
    public void A_known_undisclosed_place_is_disclosed_once_it_is_answered()
    {
        using DialogueStage stage = new();
        long baker = stage.Talker();
        string target = "direction:site:0:1";
        bool known = false;
        List<string> disclosed = [];
        DaggerfallDialogueService conversation = stage.Conversation(
            resolveDirection: id => id == target ? new DaggerfallDialogueDestination(target, "Far place", "on the map", known) { QuestLocality = true } : null,
            disclose: id => { disclosed.Add(id); known = true; return true; });
        Assert.True(conversation.ActivateNpc(new(DaggerfallActivationMode.Talk, DialogueStage.Target(conversation, baker))).Applied);
        string revision = stage.View!.Revision;
        Assert.False(conversation.Ask(revision, "direction:site:9:9", null, null).Applied);
        Assert.Equal("That place is no longer in the directory.", stage.Outcomes[^1]);

        // A quest-local place is known without a roll, so the answer discloses it, and once only.
        Assert.True(conversation.Ask(revision, target, null, null).Applied);
        Assert.True(conversation.Ask(revision, target, null, null).Applied);
        Assert.Equal([target], disclosed);
    }
}
