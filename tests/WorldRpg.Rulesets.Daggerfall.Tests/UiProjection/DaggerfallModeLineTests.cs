using WorldRpg.Kit;
using WorldRpg.Kit.Presentation;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>The line a mode change says, with no session.</summary>
public sealed class DaggerfallModeLineTests
{
    [Fact]
    public void Returning_to_play_clears_a_modes_own_line_but_keeps_a_result_reported_while_it_was_up()
    {
        PresentationState presentation = new("Ready", 5d);
        DaggerfallModeLine line = new(presentation);

        line.Say(ProductMode.Paused, "Paused.");
        Assert.Equal("Paused.", presentation.LastOutcome);
        line.Say(ProductMode.Playing, string.Empty);
        Assert.Equal(string.Empty, presentation.LastOutcome);

        line.Say(ProductMode.Paused, "Paused.");
        presentation.SetOutcome("You rest for 8 hours.");
        line.Say(ProductMode.Playing, string.Empty);
        Assert.Equal("You rest for 8 hours.", presentation.LastOutcome);
    }

    [Fact]
    public void A_screen_that_says_what_it_is_adds_no_line_and_leaves_the_messages()
    {
        PresentationState presentation = new("Ready", 5d);
        DaggerfallModeLine line = new(presentation);
        presentation.SetOutcome("A result.");

        line.Say(ProductMode.Modal, string.Empty);
        Assert.Equal("A result.", presentation.LastOutcome);
        // The modal set no line of its own, so returning to play leaves the result standing.
        line.Say(ProductMode.Playing, string.Empty);
        Assert.Equal("A result.", presentation.LastOutcome);
    }
}
