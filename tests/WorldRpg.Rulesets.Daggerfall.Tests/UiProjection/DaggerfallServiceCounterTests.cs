using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// <see cref="DaggerfallServiceCounter"/> built without the session, over <see cref="DialogueStage"/>'s conversation and
/// site: the interior building and active profile plain inputs.
/// </summary>
public sealed class DaggerfallServiceCounterTests
{
    [Fact]
    public void The_counter_is_the_person_talked_to_with_the_service_asked_for()
    {
        using DialogueStage stage = new();
        DaggerfallActivationPresentation activation = new();
        DaggerfallDialogueService? conversation = null;
        DaggerfallServiceCounter counter = new(() => conversation, () => null, stage.Society.Sites, () => stage.Society.Active, activation);
        Assert.Null(counter.Summoner());
        Assert.Null(counter.Provider(service: "daedra-summoning"));

        long summoner = stage.Society.Civilian("baker", "talk", "daedra-summoning");
        conversation = stage.Conversation();
        Assert.True(conversation.ActivateNpc(new(DaggerfallActivationMode.Talk, DialogueStage.Target(conversation, summoner))).Applied);
        activation.SetDialogue(stage.View);

        DaggerfallServiceProvider provider = Assert.IsType<DaggerfallServiceProvider>(counter.Summoner(stage.View!.Revision));
        Assert.Equal("daedra-summoning", provider.Service);
        Assert.Null(counter.Summoner("stale"));
        // Outside a building no one offers a counter service, whatever they offer.
        Assert.Null(counter.Provider(service: "daedra-summoning"));
        Assert.Equal(stage.View.Revision, counter.Page.Revision);
    }
}
