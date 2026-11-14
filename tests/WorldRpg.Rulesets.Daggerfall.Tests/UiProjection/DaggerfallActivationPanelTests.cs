using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// <see cref="DaggerfallActivationPanel"/> built without the session over <see cref="DialogueStage"/>'s conversation and an
/// activation with no site module composed. The service owners the conversation's page would ask are not reached here:
/// the page is published on the conversation's own callback, not this panel's.
/// </summary>
public sealed class DaggerfallActivationPanelTests
{
    [Fact]
    public void Without_a_site_module_or_a_conversation_the_actions_are_not_taken()
    {
        using DialogueStage stage = new();
        PresentationState presentation = new("Ready", 5d);
        DaggerfallActivationPresentation view = new();
        DaggerfallActivationPanel panel = Panel(stage, view, () => null, presentation);

        Assert.False(panel.ChangeMode(new DaggerfallPlayerUiAction("activation-mode", Mode: "talk")));
        Assert.False(panel.ApplyDialogue(new DaggerfallPlayerUiAction("dialogue-close", "1")));
        Assert.Equal("Ready", presentation.LastOutcome);
        Assert.Equal(new DaggerfallActivationView("grab", "Interaction mode: grab.", false), panel.View);
    }

    [Fact]
    public void A_conversation_action_reads_its_revision_and_asks_the_conversation()
    {
        using DialogueStage stage = new();
        long baker = stage.Talker();
        DaggerfallDialogueService conversation = stage.Conversation();
        Assert.True(conversation.ActivateNpc(new(DaggerfallActivationMode.Talk, DialogueStage.Target(conversation, baker))).Applied);
        DaggerfallActivationPanel panel = Panel(stage, new DaggerfallActivationPresentation(), () => conversation, new PresentationState("Ready", 5d));

        Assert.True(panel.ApplyDialogue(new DaggerfallPlayerUiAction("dialogue-close", stage.View!.Revision)));
        Assert.Null(stage.View);
        Assert.Null(conversation.CurrentNpc());
    }

    private static DaggerfallActivationPanel Panel(DialogueStage stage, DaggerfallActivationPresentation view,
        Func<DaggerfallDialogueService?> dialogue, PresentationState presentation)
    {
        DaggerfallServiceCounter counter = new(dialogue, () => null, stage.Society.Sites, () => stage.Society.Active, view);
        return new(view, new DaggerfallActivation(null!, stage.Society.Actors, stage.Society.Player, () => null!, 1d, 1d), dialogue,
            null!, stage.Society.Effects, null!, null!, () => null, null!, null!, counter, presentation);
    }
}
