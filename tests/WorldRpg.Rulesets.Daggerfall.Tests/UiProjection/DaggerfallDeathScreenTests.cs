using WorldRpg.Kit;
using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary><see cref="DaggerfallDeathScreen"/> built without the session: the outcome report recorded as a plain input.</summary>
public sealed class DaggerfallDeathScreenTests
{
    private readonly PresentationState _presentation = new("Ready", 5d);
    private readonly List<string> _reported = [];

    private DaggerfallDeathScreen Screen() => new(_presentation, _reported.Add);

    [Fact]
    public void A_choice_becomes_one_request_the_Host_takes_once()
    {
        DaggerfallDeathScreen screen = Screen();
        Assert.True(screen.Enter());
        Assert.False(screen.Enter());

        screen.Choose(new DaggerfallPlayerUiAction(DaggerfallDeathPresentation.QuitAction));
        Assert.Equal(new PlayerDefeatOutcomeRequest(PlayerDefeatOutcome.QuitToTitle), screen.TakeOutcomeRequest());
        Assert.Null(screen.TakeOutcomeRequest());
        // The selection is latched until the Host's outcome reopens it.
        screen.Choose(new DaggerfallPlayerUiAction(DaggerfallDeathPresentation.NewGameAction));
        Assert.Null(screen.TakeOutcomeRequest());

        screen.Reopen("Load failed.");
        Assert.Equal("Load failed.", _presentation.LastOutcome);
        screen.Choose(new DaggerfallPlayerUiAction(DaggerfallDeathPresentation.NewGameAction));
        Assert.Equal(new PlayerDefeatOutcomeRequest(PlayerDefeatOutcome.NewGame), screen.TakeOutcomeRequest());
    }

    [Fact]
    public void Loading_asks_for_a_slot_and_needs_one_available()
    {
        DaggerfallDeathScreen screen = Screen();
        screen.Enter();

        screen.Choose(new DaggerfallPlayerUiAction(DaggerfallDeathPresentation.LoadGameAction));
        Assert.Equal(["Choose a saved game to load."], _reported);
        screen.Choose(new DaggerfallPlayerUiAction(DaggerfallDeathPresentation.LoadGameAction, Key: "slot-1"));
        Assert.Null(screen.TakeOutcomeRequest());

        screen.SetLoadAvailable(true);
        screen.Choose(new DaggerfallPlayerUiAction(DaggerfallDeathPresentation.LoadGameAction, Key: "slot-1"));
        Assert.Equal(new PlayerDefeatOutcomeRequest(PlayerDefeatOutcome.Load, "slot-1"), screen.TakeOutcomeRequest());
    }

    [Fact]
    public void A_save_outcome_reopens_the_choices_only_while_dead_and_play_clears_the_screen()
    {
        DaggerfallDeathScreen screen = Screen();
        screen.Enter();
        screen.Choose(new DaggerfallPlayerUiAction(DaggerfallDeathPresentation.QuitAction));
        screen.ReopenAfterSaveOutcome(dead: false);
        Assert.NotNull(screen.View.Selected);
        screen.ReopenAfterSaveOutcome(dead: true);
        Assert.Null(screen.View.Selected);

        screen.Clear();
        Assert.False(screen.View.Active);
    }
}
