using WorldRpg.Kit;
using WorldRpg.Kit.Presentation;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>The entry screen's choice of game, with no session.</summary>
public sealed class DaggerfallBundleChoiceTests
{
    [Fact]
    public void The_game_is_chosen_once_on_the_title_screen_before_a_new_game_begins()
    {
        PresentationState presentation = new("Ready", 5d);
        DaggerfallBundleChoice choice = new(presentation);
        Assert.Null(choice.Offered);
        GameBundleChoices offered = new(new GameBundleId("daggerfall.classic"), []);
        choice.Report(offered);
        Assert.Same(offered, choice.Offered);

        choice.Choose("test.second", ProductMode.Title, newGameBegun: false);
        Assert.Equal(new GameBundleId("test.second"), choice.Take());
        Assert.Null(choice.Take());

        choice.Choose("test.second", ProductMode.Playing, newGameBegun: false);
        Assert.Null(choice.Take());
        Assert.Equal("The game can be changed only from the title screen.", presentation.LastOutcome);
        presentation.SetOutcome("Ready");
        choice.Choose("test.second", ProductMode.Title, newGameBegun: true);
        Assert.Null(choice.Take());
        Assert.Equal("The game can be changed only from the title screen.", presentation.LastOutcome);
    }
}
