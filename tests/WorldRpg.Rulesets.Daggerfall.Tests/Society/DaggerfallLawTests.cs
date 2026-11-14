using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Crime;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>The law's own rules, from a reported crime to release, built from its inputs without a session.</summary>
public sealed class DaggerfallLawTests
{
    [Fact]
    public void A_reported_crime_costs_regional_standing_and_opens_a_pursuit_after_the_watchs_delay()
    {
        using SocietyStage stage = new(roll: 7);
        int region = stage.Site.Id.Region;

        stage.Crime.RecordIncident(stage.Reported("theft:1"));

        Assert.Equal(-DaggerfallCrimePolicy.RegionalReputationLoss(DaggerfallCrimeKind.Theft), stage.Social.RegionalReputation(region));
        DaggerfallLegalResponseSave response = Assert.IsType<DaggerfallLegalResponseSave>(stage.Law.Current);
        Assert.Equal(DaggerfallLegalPhase.Pursuit, response.Phase);
        Assert.Equal(["theft:1"], response.Charges);
        Assert.Equal(Math.Clamp(7, DaggerfallTuning.Defaults.Law.MinimumResponseSeconds, DaggerfallTuning.Defaults.Law.MaximumResponseSeconds),
            response.ResponseDelaySeconds);
        Assert.Equal(stage.Active, response.Profile.Require());
        Assert.False(stage.Law.ModalOpen);
        Assert.Null(stage.Law.View);
    }

    [Fact]
    public void An_unwitnessed_unreported_crime_opens_nothing_and_a_later_charge_joins_the_pursuit()
    {
        using SocietyStage stage = new();
        stage.Crime.RecordIncident(stage.Reported("quiet") with { Reported = false });
        Assert.Null(stage.Law.Current);

        stage.Crime.RecordIncident(stage.Reported("first"));
        stage.Crime.RecordIncident(stage.Reported("second", DaggerfallCrimeKind.Assault));
        Assert.Equal(["first", "second"], stage.Law.Current!.Charges);
    }

    [Fact]
    public void A_surrender_goes_to_court_a_guilty_plea_is_sentenced_and_the_served_sentence_releases_the_player()
    {
        // The highest draws keep the accusation from turning severe, so a guilty plea is convicted, not banished.
        using SocietyStage stage = new(roll: 100);
        int region = stage.Site.Id.Region;
        stage.Gold = 1000;
        stage.Crime.RecordIncident(stage.Reported("theft:1"));
        int before = stage.Social.RegionalReputation(region);

        stage.Law.Arrest(stage.Law.Current!);
        Assert.True(stage.Law.ModalOpen);
        DaggerfallLegalView arrest = stage.Law.View!;
        Assert.Equal("arrest", arrest.Phase);
        Assert.Equal(["Theft"], arrest.Charges);

        DaggerfallLegalChoiceResult yielded = stage.Law.Choose(arrest.Revision, "yield")!;
        Assert.Equal(DaggerfallLegalStep.Surrendered, yielded.Step);
        Assert.Equal(stage.Active, stage.Law.BeginCourt(yielded.Response));
        DaggerfallLegalView court = stage.Law.View!;
        Assert.Equal("court", court.Phase);
        Assert.Null(stage.Law.Choose("stale", "guilty"));

        Assert.Null(stage.Law.Choose(court.Revision, "guilty"));
        DaggerfallLegalView judgment = stage.Law.View!;
        Assert.Equal("sentence", judgment.Phase);
        DaggerfallLegalChoiceResult accepted = stage.Law.Choose(judgment.Revision, "accept")!;
        Assert.Equal(DaggerfallLegalStep.SentenceAccepted, accepted.Step);

        DaggerfallCourtSentence sentence = stage.Law.FitSentence(accepted.Response);
        Assert.Equal(DaggerfallCourtOutcome.Convicted, sentence.Outcome);
        DaggerfallLegalResponseSave imprisoned = stage.Law.Imprison(accepted.Response, sentence);
        long term = sentence.PrisonDays * 86400L + 240 * 60;
        Assert.Equal(term, imprisoned.PrisonSecondsRemaining);

        // A consequence that stops the sentence's time keeps the player in prison with the rest still owed.
        stage.PassLimit = 60;
        Assert.Null(stage.Law.Serve(imprisoned));
        Assert.Equal(term - 60, stage.Law.Current!.PrisonSecondsRemaining);
        Assert.Equal("prison", stage.Law.View!.Phase);

        stage.PassLimit = long.MaxValue;
        DaggerfallLegalResponseSave released = stage.Law.Serve(stage.Law.Current!)!;
        Assert.Equal([term, term - 60], stage.Passed);
        Assert.Equal(sentence.Outcome, released.Sentence!.Outcome);
        Assert.Null(stage.Crime.Response(region));
        Assert.Equal(before + DaggerfallCrimePolicy.RegionalReputationLoss(DaggerfallCrimeKind.Theft) / 2 - 1, stage.Social.RegionalReputation(region));
        Assert.False(stage.Law.ModalOpen);
    }

    [Fact]
    public void Resisting_arrest_turns_the_cases_guards_hostile_and_says_so()
    {
        using SocietyStage stage = new();
        long watch = stage.CityWatch();
        stage.Crime.RecordIncident(stage.Reported("theft:1"));
        stage.Crime.SetResponse(stage.Law.Current! with { Guards = [watch] });
        Assert.True(stage.Law.IsGuard(watch));
        stage.Law.Arrest(stage.Law.Current!);
        Assert.False(stage.Law.IsGuard(watch));

        DaggerfallLegalChoiceResult fled = stage.Law.Choose(stage.Law.View!.Revision, "escape")!;

        Assert.Equal((DaggerfallLegalStep.Resisted, "You flee. The charges remain in this region."), (fled.Step, fled.Line));
        Assert.Equal(DaggerfallLegalPhase.Resisting, stage.Law.Current!.Phase);
        Assert.Equal([watch], stage.Hostile);
    }

    [Fact]
    public void A_pursuit_left_at_another_profile_escapes_and_resumes_after_the_shortest_delay()
    {
        using SocietyStage stage = new(roll: 1);
        stage.Crime.RecordIncident(stage.Reported("theft:1"));
        stage.Active = DaggerfallWorldProfileIds.Dungeon(stage.Site.Id);
        stage.Law.ReconcileSite();
        Assert.Equal(DaggerfallLegalPhase.Escaped, stage.Law.Current!.Phase);

        // In a dungeon the watch does not pursue.
        Assert.Null(stage.Law.Update(1));
        Assert.Equal(DaggerfallLegalPhase.Escaped, stage.Law.Current!.Phase);

        stage.Active = DaggerfallWorldProfileIds.Exterior(stage.Site.Id);
        Assert.Null(stage.Law.Update(0.5));
        DaggerfallLegalResponseSave resumed = stage.Law.Current!;
        Assert.Equal(DaggerfallLegalPhase.Pursuit, resumed.Phase);
        Assert.Equal(DaggerfallTuning.Defaults.Law.MinimumResponseSeconds - 0.5, resumed.ResponseDelaySeconds);
        Assert.Equal(stage.Active, resumed.Profile.Require());
    }

    [Fact]
    public void A_wanted_player_is_reported_by_the_standing_law_as_minutes_pass()
    {
        using SocietyStage stage = new(roll: 1);
        int region = stage.Site.Id.Region;
        stage.Social.ChangeRegionalReputation(region, -20);

        stage.Law.CheckStanding(100, 103);

        DaggerfallCrimeIncidentSave charge = Assert.Single(stage.Crime.Incidents);
        Assert.Equal(($"regional-wanted:{region}:101", DaggerfallCrimeKind.CriminalConspiracy), (charge.OperationId, charge.Crime));
        Assert.Equal(DaggerfallLegalPhase.Pursuit, stage.Law.Current!.Phase);
        // A conspiracy costs no further standing.
        Assert.Equal(-20, stage.Social.RegionalReputation(region));
    }

    [Fact]
    public void A_charge_has_its_courts_name()
    {
        Assert.Equal("Loan default", DaggerfallLaw.CrimeLabel(DaggerfallCrimeKind.LoanDefault));
        Assert.Equal("Theft", DaggerfallLaw.CrimeLabel(DaggerfallCrimeKind.Theft));
    }
}
