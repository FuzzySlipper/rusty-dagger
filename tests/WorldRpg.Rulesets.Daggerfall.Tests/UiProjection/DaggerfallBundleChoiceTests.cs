using WorldRpg.Kit;
using WorldRpg.Kit.Presentation;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>The entry screen's choice of game, with no session.</summary>
public sealed class DaggerfallBundleChoiceTests
{
    [Fact]
    public void A_game_chosen_on_the_title_screen_is_only_shown_until_a_game_is_started_under_it()
    {
        PresentationState presentation = new("Ready", 5d);
        DaggerfallBundleChoice choice = new(presentation);
        Assert.Null(choice.Offered);
        GameBundleChoices offered = new(new GameBundleId("daggerfall.classic"), []);
        choice.Report(offered);
        Assert.Same(offered, choice.Offered);
        // With the running game chosen, a start is the running session's own.
        Assert.False(choice.StartUnderChoice(GameBundleStart.Begin));

        // Choosing asks nothing of the Host; the choice is what the title shows.
        choice.Choose("test.second", ProductMode.Title, newGameBegun: false);
        Assert.Null(choice.Take());
        Assert.Equal(new GameBundleId("test.second"), choice.Offered!.Current);
        Assert.True(choice.StartUnderChoice(GameBundleStart.CreateCharacter));
        Assert.Equal(new GameBundleRequest(new GameBundleId("test.second"), GameBundleStart.CreateCharacter), choice.Take());
        Assert.Null(choice.Take());

        // Stepping back to the running game leaves nothing to switch to.
        choice.Choose("daggerfall.classic", ProductMode.Title, newGameBegun: false);
        Assert.Same(offered, choice.Offered);
        Assert.False(choice.StartUnderChoice(GameBundleStart.Begin));

        choice.Choose("test.second", ProductMode.Playing, newGameBegun: false);
        Assert.Same(offered, choice.Offered);
        Assert.Equal("The game can be changed only from the title screen.", presentation.LastOutcome);
        presentation.SetOutcome("Ready");
        choice.Choose("test.second", ProductMode.Title, newGameBegun: true);
        Assert.Same(offered, choice.Offered);
        Assert.Equal("The game can be changed only from the title screen.", presentation.LastOutcome);
    }
}
