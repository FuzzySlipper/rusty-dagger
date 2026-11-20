using WorldRpg.Kit;
using WorldRpg.Kit.Presentation;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>Quit from the menus, with no session.</summary>
public sealed class DaggerfallQuitTests
{
    [Fact]
    public void Quit_shows_only_where_the_product_can_quit_and_a_game_played_since_it_was_kept_is_unsaved()
    {
        PresentationState presentation = new("Ready", 5d);
        DaggerfallQuit quit = new(presentation);
        Assert.Null(quit.View(ProductMode.Playing, 10));
        quit.Request();
        Assert.False(quit.Take());
        Assert.Equal("Close the window to quit.", presentation.LastOutcome);

        quit.ReportAvailable(true);
        quit.Kept(10);
        Assert.Equal(new DaggerfallQuitView(false), quit.View(ProductMode.Playing, 10));
        Assert.Equal(new DaggerfallQuitView(true), quit.View(ProductMode.Playing, 10.5));
        Assert.Equal(new DaggerfallQuitView(true), quit.View(ProductMode.Modal, 10.5));
        Assert.Equal(new DaggerfallQuitView(true), quit.View(ProductMode.Paused, 10.5));
        // Nothing is in progress at the title, and a dead player's game cannot be saved.
        Assert.Equal(new DaggerfallQuitView(false), quit.View(ProductMode.Title, 10.5));
        Assert.Equal(new DaggerfallQuitView(false), quit.View(ProductMode.Dead, 10.5));
        quit.Kept(10.5);
        Assert.Equal(new DaggerfallQuitView(false), quit.View(ProductMode.Playing, 10.5));

        quit.Request();
        Assert.True(quit.Take());
        Assert.False(quit.Take());
    }
}
