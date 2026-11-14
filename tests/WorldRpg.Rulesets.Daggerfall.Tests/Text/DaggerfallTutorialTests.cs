using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>The tutorial over a hand-made definition, with no session.</summary>
public sealed class DaggerfallTutorialTests
{
    private static readonly DaggerfallTutorialDefinition Definition = new("Take the tutorial?",
        [Page("Moving", 0), Page("Looking", 5), Page("Using", 3)]);

    private static DaggerfallTutorialPage Page(string title, double afterSeconds) => new(new(title, []), afterSeconds);

    [Fact]
    public void A_game_without_a_tutorial_offers_none_and_cannot_start_one()
    {
        DaggerfallTutorial none = new(null, null);
        Assert.Null(none.Offer);
        Assert.False(none.Start());
        Assert.False(none.Running);
        Assert.Null(new DaggerfallTutorial(new("Take it?", []), null).Offer);
        Assert.Throws<InvalidOperationException>(() => new DaggerfallTutorial(null, new(0, true, 0)));
    }

    [Fact]
    public void Starting_shows_the_first_page_and_a_tutorial_under_way_is_not_started_again()
    {
        DaggerfallTutorial tutorial = new(Definition, null);
        Assert.Equal("Take the tutorial?", tutorial.Offer);
        Assert.Null(tutorial.View);

        Assert.True(tutorial.Start());
        Assert.True(tutorial.Shown);
        Assert.Equal(new DaggerfallTutorialView(0, 3, Definition.Pages[0].Page), tutorial.View);
        Assert.False(tutorial.Start());
        Assert.Equal(0, tutorial.View!.Index);
    }

    [Fact]
    public void Reading_on_waits_the_next_pages_seconds_of_play_and_a_stale_press_skips_nothing()
    {
        DaggerfallTutorial tutorial = new(Definition, null);
        tutorial.Start();
        Assert.False(tutorial.Continue(1));
        Assert.Equal(0, tutorial.View!.Index);

        Assert.True(tutorial.Continue(0));
        Assert.False(tutorial.Shown);
        Assert.True(tutorial.Running);
        Assert.Equal(new DaggerfallTutorialProgress(1, false, 5), tutorial.Progress);
        // A press while no page is up answers nothing.
        Assert.False(tutorial.Continue(1));

        tutorial.Age(4);
        Assert.Equal(new DaggerfallTutorialProgress(1, false, 1), tutorial.Progress);
        Assert.Null(tutorial.View);
        tutorial.Age(2);
        Assert.Equal(new DaggerfallTutorialProgress(1, true, 0), tutorial.Progress);
        Assert.Equal(1, tutorial.View!.Index);
        // A page that is up holds; play does not age it.
        tutorial.Age(10);
        Assert.Equal(new DaggerfallTutorialProgress(1, true, 0), tutorial.Progress);
    }

    [Fact]
    public void A_next_page_with_no_wait_comes_up_at_once_and_reading_past_the_last_page_ends_the_tutorial()
    {
        DaggerfallTutorial tutorial = new(new("Take it?", [Page("One", 0), Page("Two", 0)]), null);
        tutorial.Start();
        Assert.True(tutorial.Continue(0));
        Assert.Equal(1, tutorial.View?.Index);
        Assert.True(tutorial.Continue(1));
        Assert.False(tutorial.Running);
        Assert.Null(tutorial.Progress);
    }

    [Fact]
    public void Ending_stops_the_tutorial_and_Help_can_start_it_again_from_the_beginning()
    {
        DaggerfallTutorial tutorial = new(Definition, new(2, false, 3));
        Assert.False(tutorial.Shown);
        Assert.True(tutorial.End());
        Assert.False(tutorial.Running);
        Assert.False(tutorial.End());
        Assert.True(tutorial.Start());
        Assert.Equal(0, tutorial.View?.Index);
    }

    [Fact]
    public void Saved_progress_resumes_and_a_page_or_wait_the_tutorial_cannot_have_is_refused()
    {
        Assert.Equal(new DaggerfallTutorialProgress(1, false, 2.5), new DaggerfallTutorial(Definition, new(1, false, 2.5)).Progress);
        Assert.Throws<ArgumentOutOfRangeException>(() => new DaggerfallTutorial(Definition, new(3, true, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DaggerfallTutorial(Definition, new(-1, true, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DaggerfallTutorial(Definition, new(1, false, double.NaN)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DaggerfallTutorial(Definition, new(1, false, -1)));
        Assert.Throws<ArgumentException>(() => new DaggerfallTutorial(Definition, new(1, true, 2)));
    }
}
