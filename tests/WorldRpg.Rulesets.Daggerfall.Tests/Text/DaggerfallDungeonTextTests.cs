using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.DaggerfallDungeonTextActionsTests;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>The text that is up, built over a hand-made text set with no session.</summary>
public sealed class DaggerfallDungeonTextTests
{
    private static DaggerfallDungeonText Owner() => new(Resolver(
        Resource("8601", "A plaque."), Resource("8602", "A second plaque."), Resource("5406", "One speaks."),
        Internal("answers_5406", "one\r\n1"), Resource("7705", "Staff only.")));

    private static DaggerfallDungeonActionDefinition Plaque(string id, byte soundIndex = 1) =>
        Action(DaggerfallDungeonActionFlag.ShowText, soundIndex, id: id);

    private static DaggerfallDungeonActionDefinition Riddle(string id = "riddle") =>
        Action(DaggerfallDungeonActionFlag.ShowTextWithInput, soundIndex: 6, next: "next", id: id);

    private static DaggerfallDungeonTextProjection Show(DaggerfallDungeonText text, DaggerfallDungeonActionDefinition action)
    {
        DaggerfallDungeonTextActionResult result = text.Execute(action, 1, Context("Player"));
        text.Show(result.Projection);
        return Assert.IsType<DaggerfallDungeonTextProjection>(result.Projection);
    }

    [Fact]
    public void Only_the_three_text_families_are_shown()
    {
        Assert.True(DaggerfallDungeonText.Shows(Action(DaggerfallDungeonActionFlag.ShowText, 1)));
        Assert.True(DaggerfallDungeonText.Shows(Action(DaggerfallDungeonActionFlag.ShowTextWithInput, 1)));
        Assert.True(DaggerfallDungeonText.Shows(Action(DaggerfallDungeonActionFlag.DoorText, 1)));
        Assert.False(DaggerfallDungeonText.Shows(Action(DaggerfallDungeonActionFlag.Teleport, 1)));
    }

    [Fact]
    public void Executing_shows_nothing_until_the_result_is_shown_and_a_text_covers_the_one_beneath_until_it_closes()
    {
        DaggerfallDungeonText text = Owner();
        DaggerfallDungeonTextActionResult result = text.Execute(Plaque("first"), 1, Context("Player"));
        Assert.Null(text.Projection);
        text.Show(result.Projection);
        DaggerfallDungeonTextProjection first = Assert.IsType<DaggerfallDungeonTextProjection>(text.Projection);
        DaggerfallDungeonTextProjection second = Show(text, Plaque("second", 2));
        Assert.Same(second, text.Projection);

        // A result with no text leaves what is up.
        text.Show(null);
        Assert.Same(second, text.Projection);

        Assert.True(text.IsUp("second", second.Revision));
        Assert.False(text.IsUp("first", first.Revision));
        Assert.False(text.IsUp("second", second.Revision - 1));
        Assert.Null(text.Close("second", second.Revision));
        Assert.Same(first, text.Projection);
        Assert.Null(text.Close("first", first.Revision));
        Assert.Null(text.Projection);
    }

    [Fact]
    public void Closing_a_riddle_cancels_its_wait_and_answering_brings_back_what_it_covered()
    {
        DaggerfallDungeonText closed = Owner();
        DaggerfallDungeonTextProjection riddle = Show(closed, Riddle());
        Assert.Null(closed.Close("riddle", riddle.Revision));
        Assert.Null(closed.Projection);
        Assert.Null(closed.Capture().Pending);

        DaggerfallDungeonText answered = Owner();
        DaggerfallDungeonTextProjection plaque = Show(answered, Plaque("plaque"));
        DaggerfallDungeonTextProjection asked = Show(answered, Riddle());

        // An answer to an older revision changes nothing and the riddle stays up.
        Assert.Equal(DaggerfallDungeonTextOutcome.StaleSubmission, answered.Answer("riddle", asked.Revision - 1, "one").Outcome);
        Assert.Same(asked, answered.Projection);

        Assert.Equal(DaggerfallDungeonTextOutcome.RejectedAnswer, answered.Answer("riddle", asked.Revision, "two").Outcome);
        Assert.Same(plaque, answered.Projection);
        Assert.Equal(DaggerfallDungeonTextOutcome.NoPendingAnswer, answered.Answer("riddle", asked.Revision, "one").Outcome);
        Assert.Same(plaque, answered.Projection);

        DaggerfallDungeonText accepted = Owner();
        DaggerfallDungeonTextProjection again = Show(accepted, Riddle());
        Assert.Equal(DaggerfallDungeonTextOutcome.AcceptedAnswer, accepted.Answer("riddle", again.Revision, "ONE").Outcome);
        Assert.Null(accepted.Projection);
    }

    [Fact]
    public void A_door_warning_is_known_by_its_action_and_a_greeting_replaces_what_is_up_without_covering_it()
    {
        DaggerfallDungeonText text = Owner();
        Show(text, Action(DaggerfallDungeonActionFlag.DoorText, soundIndex: 1, axis: 6, id: "door"));
        Assert.True(text.ShowsDoorText("door"));
        Assert.False(text.ShowsDoorText("other"));

        DaggerfallDungeonTextProjection greeting = new("greeting", DaggerfallDungeonTextActionKind.BuildingGreeting, 1, "Welcome.",
            [], 1, IsHud: false, ClickAnywhereToClose: true);
        text.Greet(greeting);
        Assert.Same(greeting, text.Projection);
        Assert.False(text.ShowsDoorText("door"));
        text.CloseGreeting();
        Assert.Null(text.Projection);
    }

    [Fact]
    public void A_saved_wait_is_put_back_up_over_the_active_graphs_actions_and_one_whose_action_is_gone_is_refused()
    {
        DaggerfallDungeonText original = Owner();
        DaggerfallDungeonTextProjection asked = Show(original, Riddle());
        DaggerfallDungeonTextSnapshot save = original.Capture();

        DaggerfallDungeonText restored = Owner();
        bool contextRead = false;
        restored.Restore(save, () => [Plaque("plaque"), Riddle()], () => { contextRead = true; return Context("Player"); });
        Assert.True(contextRead);
        DaggerfallDungeonTextProjection resumed = Assert.IsType<DaggerfallDungeonTextProjection>(restored.Projection);
        Assert.Equal((asked.ActionId, asked.Kind, asked.TextId, asked.Text, asked.Revision),
            (resumed.ActionId, resumed.Kind, resumed.TextId, resumed.Text, resumed.Revision));
        Assert.Equal(asked.Answers, resumed.Answers);
        Assert.Equal(DaggerfallDungeonTextOutcome.AcceptedAnswer, restored.Answer("riddle", asked.Revision, "1").Outcome);

        Assert.Throws<ArgumentException>(() => Owner().Restore(save, () => null, () => Context("Player")));
        Assert.Throws<ArgumentException>(() => Owner().Restore(save, () => [Plaque("plaque")], () => Context("Player")));

        // With nothing waiting, the actions and the context are not asked.
        DaggerfallDungeonText idle = Owner();
        idle.Restore(new(3, null), () => throw new InvalidOperationException(), () => throw new InvalidOperationException());
        Assert.Null(idle.Projection);
        Assert.Equal(3UL, idle.Capture().Revision);
    }

    [Fact]
    public void Unloading_cancels_the_wait_and_clears_every_text()
    {
        DaggerfallDungeonText text = Owner();
        Show(text, Plaque("plaque"));
        DaggerfallDungeonTextProjection asked = Show(text, Riddle());
        text.CancelOnUnload();
        Assert.Null(text.Projection);
        Assert.Null(text.Capture().Pending);
        Assert.Equal(DaggerfallDungeonTextOutcome.NoPendingAnswer, text.Answer("riddle", asked.Revision, "one").Outcome);
        Assert.Null(text.Projection);
    }
}
