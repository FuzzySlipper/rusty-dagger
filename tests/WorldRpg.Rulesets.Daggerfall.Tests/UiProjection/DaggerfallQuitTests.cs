using WorldRpg.Kit;
using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Presentation;
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

        // While the menu holds the world no play time passes, yet an action can still change the game: a note written.
        quit.Applied(DaggerfallUiActionKind.Menu);
        quit.Applied(DaggerfallUiActionKind.NotebookPage);
        quit.Applied(DaggerfallUiActionKind.ControlsVolume);
        Assert.Equal(new DaggerfallQuitView(false), quit.View(ProductMode.Modal, 10.5));
        quit.Applied(DaggerfallUiActionKind.NotebookAdd);
        Assert.Equal(new DaggerfallQuitView(true), quit.View(ProductMode.Modal, 10.5));
        quit.Kept(10.5);
        Assert.Equal(new DaggerfallQuitView(false), quit.View(ProductMode.Modal, 10.5));
        // The expedition map's places are only read; its Find pins the best match, which a save keeps.
        quit.Applied(DaggerfallUiActionKind.ExpeditionPlaces);
        Assert.Equal(new DaggerfallQuitView(false), quit.View(ProductMode.Modal, 10.5));
        quit.Applied(DaggerfallUiActionKind.ExpeditionFind);
        Assert.Equal(new DaggerfallQuitView(true), quit.View(ProductMode.Modal, 10.5));

        quit.Request();
        Assert.True(quit.Take());
        Assert.False(quit.Take());
    }
}
