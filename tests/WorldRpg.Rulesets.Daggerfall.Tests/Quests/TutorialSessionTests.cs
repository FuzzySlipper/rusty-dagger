using Rusty.Engine;
using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class TutorialSessionTests
{
    private static DaggerfallTutorialDefinition Definition => TestPayload.Definitions.Tutorial
        ?? throw new InvalidOperationException("The authored help has no tutorial.");

    [Fact]
    public void A_new_game_starts_no_tutorial_and_Help_offers_one_in_the_games_own_words()
    {
        using DaggerfallSession session = FreshSession();
        Assert.False(session.Tutorial.Running);
        Assert.Null(session.Tutorial.View);
        Assert.DoesNotContain(session.State.Quests.Capture().Instances, quest => quest.SourceFile == "_TUTOR__.txt");
        Assert.False(string.IsNullOrWhiteSpace(session.Tutorial.Offer));
        // The donor tutorial named another program and its default keys; this one names the player's own controls.
        Assert.All(Definition.Pages.SelectMany(page => page.Page.Paragraphs).SelectMany(paragraph => paragraph),
            segment => Assert.DoesNotContain("Unity", segment.Text ?? string.Empty, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(Definition.Pages.SelectMany(page => page.Page.Paragraphs).SelectMany(paragraph => paragraph), segment => segment.Action is not null);
    }

    [Fact]
    public void Taking_the_tutorial_holds_the_world_on_each_page_and_the_next_waits_its_seconds_of_play()
    {
        using DaggerfallSession session = FreshSession();
        Assert.True(Definition.Pages.Count >= 3);
        double wait = Definition.Pages[1].AfterSeconds;
        Assert.True(wait > 1);

        session.Update(new ProductUpdate(OuterUpdate(1), [Ui("""{"action":"tutorial-start"}""")]));
        Assert.Equal(0, session.Tutorial.View?.Index);
        Assert.Equal(ProductMode.Modal, session.PendingModeRequest);
        session.ApplyProductMode(ProductMode.Modal);

        // A press made on an earlier page does not skip the page that is up.
        session.Update(new ProductUpdate(OuterUpdate(2), [Ui("""{"action":"tutorial-continue","page":1}""")]));
        Assert.Equal(0, session.Tutorial.View?.Index);

        session.Update(new ProductUpdate(OuterUpdate(3), [Ui("""{"action":"tutorial-continue","page":0}""")]));
        Assert.Null(session.Tutorial.View);
        Assert.True(session.Tutorial.Running);
        Assert.Equal(ProductMode.Playing, session.PendingModeRequest);
        session.ApplyProductMode(ProductMode.Playing);

        // Play runs until the next page's seconds have passed, and then it comes up.
        session.Update(new ProductUpdate(OuterUpdate(4) with { FixedDeltaSeconds = 1, AdmittedStepCount = checked((uint)Math.Floor(wait - 1)) }, []));
        Assert.Null(session.Tutorial.View);
        session.Update(new ProductUpdate(OuterUpdate(100) with { FixedDeltaSeconds = 1, AdmittedStepCount = 1 }, []));
        Assert.Equal(1, session.Tutorial.View?.Index);
        Assert.Equal(ProductMode.Modal, session.PendingModeRequest);

        session.Update(new ProductUpdate(OuterUpdate(101), [Ui("""{"action":"tutorial-end"}""")]));
        Assert.False(session.Tutorial.Running);
        // Help can start it again from the beginning.
        session.Update(new ProductUpdate(OuterUpdate(102), [Ui("""{"action":"tutorial-start"}""")]));
        Assert.Equal(0, session.Tutorial.View?.Index);
    }

    [Fact]
    public void Reading_past_the_last_page_ends_the_tutorial()
    {
        DaggerfallTutorial tutorial = new(Definition, new(Definition.Pages.Count - 1, true, 0));
        Assert.True(tutorial.Continue(Definition.Pages.Count - 1));
        Assert.False(tutorial.Running);
    }

    [Fact]
    public void A_save_part_way_through_the_tutorial_restores_to_the_same_page_and_wait()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        RulesetSavePayload payload;
        using (DaggerfallSession session = FreshSession())
        {
            session.Update(new ProductUpdate(OuterUpdate(1), [Ui("""{"action":"tutorial-start"}""")]));
            session.Update(new ProductUpdate(OuterUpdate(2), [Ui("""{"action":"tutorial-continue","page":0}""")]));
            payload = session.CaptureSave();
        }
        DaggerfallTutorialProgress saved = Assert.IsType<DaggerfallTutorialProgress>(DaggerfallSavePayload.Read(payload).Tutorial);
        // The update that read on also admitted its step of play, which the wait has already counted.
        Assert.Equal((1, false), (saved.Page, saved.Shown));
        Assert.InRange(saved.SecondsToNext, Definition.Pages[1].AfterSeconds - 1, Definition.Pages[1].AfterSeconds);

        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        ResolvedCompositionIdentity identity = Resolve(FullContent(root), new GameBundleId("daggerfall.classic")).RequireComposition().Identity;
        using DaggerfallSession resumed = DaggerfallSession.Restore(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults, identity), payload);
        Assert.Equal(saved, resumed.Tutorial.Progress);

        // A saved page the tutorial does not have is refused rather than shown as some other page.
        Assert.Throws<ArgumentOutOfRangeException>(() => new DaggerfallTutorial(Definition, new(Definition.Pages.Count, true, 0)));
    }
}
