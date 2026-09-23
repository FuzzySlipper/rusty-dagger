using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallDungeonTextActionsTests
{
    [Fact]
    public void Show_text_uses_sound_index_as_the_type_eleven_resource_id_and_eager_link_signal()
    {
        DaggerfallDungeonTextActions actions = new(Resolver(
            Resource("8607", "Greetings, %pcn.")));
        DaggerfallDungeonActionDefinition action = Action(DaggerfallDungeonActionFlag.ShowText, soundIndex: 7, next: "next");

        DaggerfallDungeonTextActionResult result = actions.Execute(action, activationCount: 1, Context("Nulfaga"));

        Assert.Equal(DaggerfallDungeonTextOutcome.Presented, result.Outcome);
        Assert.NotNull(result.Projection);
        Assert.Equal(DaggerfallDungeonTextActionKind.ShowText, result.Projection!.Kind);
        Assert.Equal(8607, result.Projection.TextId);
        Assert.Equal("Greetings, Nulfaga.", result.Projection.Text);
        Assert.True(result.Projection.ClickAnywhereToClose);
        Assert.True(result.ContinueLinkedAction);
        Assert.Null(result.Pending);
    }

    [Fact]
    public void Input_text_uses_donor_fallback_answers_and_continues_only_once_on_case_insensitive_match()
    {
        DaggerfallDungeonTextActions actions = new(Resolver(Resource("5404", "Name the weapon.")));
        DaggerfallDungeonActionDefinition action = Action(DaggerfallDungeonActionFlag.ShowTextWithInput, soundIndex: 4, next: "next");

        DaggerfallDungeonTextActionResult shown = actions.Execute(action, 1, Context("Player"));

        Assert.Equal(DaggerfallDungeonTextOutcome.AwaitingAnswer, shown.Outcome);
        Assert.False(shown.ContinueLinkedAction);
        Assert.Equal(["bow", "bow arrow", "crossbow", "bows", "crossbows"], shown.Projection!.Answers);
        Assert.Equal(shown.Projection.Revision, shown.Pending!.Revision);

        DaggerfallDungeonTextActionResult accepted = actions.Submit(action.Id, shown.Projection.Revision, "BOW");

        Assert.Equal(DaggerfallDungeonTextOutcome.AcceptedAnswer, accepted.Outcome);
        Assert.True(accepted.ContinueLinkedAction);
        Assert.Null(actions.Pending);
        Assert.Equal(DaggerfallDungeonTextOutcome.NoPendingAnswer,
            actions.Submit(action.Id, shown.Projection.Revision, "bow").Outcome);
    }

    [Fact]
    public void Wrong_answer_and_cancel_close_the_wait_without_following_the_link()
    {
        DaggerfallDungeonTextActions wrong = new(Resolver(
            Resource("5406", "One speaks."), Internal("answers_5406", "one\r\n1")));
        DaggerfallDungeonActionDefinition action = Action(DaggerfallDungeonActionFlag.ShowTextWithInput, soundIndex: 6, next: "next");
        DaggerfallDungeonTextActionResult shown = wrong.Execute(action, 1, Context("Player"));

        DaggerfallDungeonTextActionResult rejected = wrong.Submit(action.Id, shown.Projection!.Revision, "two");

        Assert.Equal(DaggerfallDungeonTextOutcome.RejectedAnswer, rejected.Outcome);
        Assert.False(rejected.ContinueLinkedAction);
        Assert.Null(wrong.Pending);

        DaggerfallDungeonTextActions cancelled = new(Resolver(
            Resource("5406", "One speaks."), Internal("answers_5406", "one\r\n1")));
        DaggerfallDungeonTextActionResult pending = cancelled.Execute(action, 1, Context("Player"));
        DaggerfallDungeonTextActionResult cancel = cancelled.Cancel(action.Id, pending.Projection!.Revision);

        Assert.Equal(DaggerfallDungeonTextOutcome.Cancelled, cancel.Outcome);
        Assert.False(cancel.ContinueLinkedAction);
        Assert.Null(cancelled.Pending);
    }

    [Fact]
    public void Restored_waiting_identity_rejects_stale_input_and_consumes_a_valid_submission_once()
    {
        DaggerfallTextResolver resolver = Resolver(
            Resource("5406", "One speaks."), Internal("answers_5406", "one\r\n1"));
        DaggerfallDungeonActionDefinition action = Action(DaggerfallDungeonActionFlag.ShowTextWithInput, soundIndex: 6, next: "next");
        DaggerfallDungeonTextActions original = new(resolver);
        DaggerfallDungeonTextActionResult pending = original.Execute(action, 1, Context("Player"));
        DaggerfallDungeonTextSnapshot save = original.Capture();

        DaggerfallDungeonTextActions restored = new(resolver);
        restored.Restore(save);
        DaggerfallDungeonTextActionResult resumed = restored.Resume(action, Context("Player"));
        DaggerfallDungeonTextActionResult stale = restored.Submit(action.Id, pending.Projection!.Revision - 1, "one");
        bool remainedPendingAfterStale = restored.Pending is not null;
        DaggerfallDungeonTextActionResult accepted = restored.Submit(action.Id, pending.Projection.Revision, "1");
        DaggerfallDungeonTextActionResult duplicate = restored.Submit(action.Id, pending.Projection.Revision, "1");

        Assert.Equal(DaggerfallDungeonTextOutcome.AwaitingAnswer, resumed.Outcome);
        Assert.Equal(pending.Projection.Revision, resumed.Projection!.Revision);
        Assert.Equal(DaggerfallDungeonTextOutcome.StaleSubmission, stale.Outcome);
        Assert.True(remainedPendingAfterStale);
        Assert.Equal(DaggerfallDungeonTextOutcome.AcceptedAnswer, accepted.Outcome);
        Assert.True(accepted.ContinueLinkedAction);
        Assert.Equal(DaggerfallDungeonTextOutcome.NoPendingAnswer, duplicate.Outcome);
    }

    [Fact]
    public void Door_text_aliases_7701_through_7704_and_checks_trespass_only_after_first_display()
    {
        DaggerfallDungeonTextActions actions = new(Resolver(Resource("7705", "Staff only.")));
        DaggerfallDungeonActionDefinition action = Action(DaggerfallDungeonActionFlag.DoorText, soundIndex: 1, axis: 6, next: "next");

        DaggerfallDungeonTextActionResult first = actions.Execute(action, 1, Context("Player"));
        DaggerfallDungeonTextActionResult second = actions.Execute(action, 2, Context("Player"));

        Assert.Equal(DaggerfallDungeonTextOutcome.Presented, first.Outcome);
        Assert.Equal(7705, first.Projection!.TextId);
        Assert.True(first.Projection.IsHud);
        Assert.True(first.ContinueLinkedAction);
        Assert.False(first.ApplyDoorTrespass);
        Assert.Equal(DaggerfallDungeonTextOutcome.SkippedDoorText, second.Outcome);
        Assert.True(second.ContinueLinkedAction);
        Assert.True(second.ApplyDoorTrespass);
    }

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(6, 6, true)]
    public void Donor_missing_door_text_ids_skip_to_the_door_check(byte soundIndex, byte axis, bool trespass)
    {
        DaggerfallDungeonTextActions actions = new(Resolver());
        DaggerfallDungeonActionDefinition action = Action(DaggerfallDungeonActionFlag.DoorText, soundIndex, axis);

        DaggerfallDungeonTextActionResult result = actions.Execute(action, 1, Context("Player"));

        Assert.Equal(DaggerfallDungeonTextOutcome.SkippedDoorText, result.Outcome);
        Assert.Equal(trespass, result.ApplyDoorTrespass);
        Assert.True(result.ContinueLinkedAction);
        Assert.Null(result.Projection);
    }

    private static DaggerfallDungeonActionDefinition Action(
        DaggerfallDungeonActionFlag flag,
        byte soundIndex,
        byte axis = 0,
        string? next = null) =>
        new("action", 1, 2, (byte)flag, axis, 0, 0, next is null ? -1 : 2, next, SoundIndex: soundIndex);

    private static DaggerfallTextResolver Resolver(params DaggerfallTextValue[] values) =>
        new(new(values.ToDictionary(value => value.Key), [], []));

    private static DaggerfallTextValue Resource(string id, string text) => Value(new(DaggerfallTextKind.Resource, id), text);

    private static DaggerfallTextValue Internal(string id, string text) => Value(new(DaggerfallTextKind.Internal, id), text);

    private static DaggerfallTextValue Value(DaggerfallTextKey key, string text) =>
        new(key, "test", "en", 0, 0, text.Length, 1, DaggerfallTextState.Read, string.Empty, [],
            [new(DaggerfallTextCode.Text, text, null, null)]);

    private static DaggerfallTextContext Context(string player) =>
        new(new(Name: player), new(), new(), new(), new(), new());
}
