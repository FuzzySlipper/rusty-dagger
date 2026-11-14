using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Crime;
using WorldRpg.Rulesets.Daggerfall.Facts;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>The crime owner's own rules, built from its inputs without a session.</summary>
public sealed class DaggerfallCrimesTests
{
    private const long Player = DaggerfallActorIdentity.PlayerEntityId;

    [Fact]
    public void An_attack_on_a_civilian_is_an_assault_charged_once_per_operation()
    {
        using SocietyStage stage = new();
        long civilian = stage.Civilian();
        List<DaggerfallCrimeIncidentSave> reported = [];
        stage.Crime.IncidentRecorded += reported.Add;

        stage.Crimes.ObserveAttack(Player, civilian, "strike:1");
        stage.Crimes.ObserveAttack(Player, civilian, "strike:1");

        DaggerfallCrimeIncidentSave incident = Assert.Single(reported);
        Assert.Equal(DaggerfallCrimeKind.Assault, incident.Crime);
        Assert.Equal(DaggerfallCrimeTargetKind.Civilian, incident.TargetKind);
        Assert.Equal(DaggerfallCrimeGuildCredit.None, incident.GuildCredit);
        Assert.Equal(stage.Site.Id.Region, incident.Region);
        // The player stands nowhere, so no one was asked whether they saw it.
        Assert.Equal(DaggerfallCrimeWitnessQuery.NotQueried, incident.Witnesses.Query);
        Assert.True(stage.Crime.HasAttempt("strike:1"));
    }

    [Fact]
    public void Killing_a_civilian_or_the_watch_is_murder_with_its_guild_credit_and_a_mobile_guard_is_only_assaulted()
    {
        using SocietyStage stage = new();
        long civilian = stage.Civilian();
        long watch = stage.CityWatch();
        long guard = stage.Civilian("guard");
        foreach (long id in new[] { civilian, watch, guard }) stage.Defeat(id);
        List<DaggerfallCrimeIncidentSave> reported = [];
        stage.Crime.IncidentRecorded += reported.Add;

        stage.Crimes.ObserveAttack(Player, civilian, "kill:civilian");
        stage.Crimes.ObserveAttack(Player, watch, "kill:watch");
        stage.Crimes.ObserveAttack(Player, guard, "kill:guard");

        Assert.Equal([(DaggerfallCrimeKind.Murder, DaggerfallCrimeTargetKind.Civilian, DaggerfallCrimeGuildCredit.CivilianMurder),
            (DaggerfallCrimeKind.Murder, DaggerfallCrimeTargetKind.Guard, DaggerfallCrimeGuildCredit.GuardMurder),
            (DaggerfallCrimeKind.Assault, DaggerfallCrimeTargetKind.Guard, DaggerfallCrimeGuildCredit.None)],
            reported.Select(incident => (incident.Crime, incident.TargetKind, incident.GuildCredit)));
    }

    [Fact]
    public void Only_the_players_attacks_on_people_are_charged()
    {
        using SocietyStage stage = new();
        long civilian = stage.Civilian();
        long watch = stage.CityWatch();
        int reported = 0;
        stage.Crime.IncidentRecorded += _ => reported++;

        stage.Crimes.ObserveAttack(watch, civilian, "watch-strikes");
        Assert.Equal(0, reported);
        Assert.True(stage.Crimes.IsCityWatch(watch));
        Assert.False(stage.Crimes.IsCityWatch(civilian));
    }

    [Fact]
    public void A_damaging_watch_hit_turns_a_quests_scripted_crime_into_its_charge()
    {
        using SocietyStage stage = new();
        long watch = stage.CityWatch();
        stage.Crime.SetScriptedCrime(DaggerfallCrimeKind.Theft);
        List<DaggerfallCrimeIncidentSave> reported = [];
        stage.Crime.IncidentRecorded += reported.Add;

        stage.Crimes.ObserveHit(new AttackHitFact(watch, Player, 4, 4, 0, true, 7, 3));

        DaggerfallCrimeIncidentSave charge = Assert.Single(reported);
        Assert.Equal($"quest-charge:7:3:{watch}", charge.OperationId);
        Assert.Equal(DaggerfallCrimeKind.Theft, charge.Crime);
        Assert.True(charge.Reported);
        Assert.Null(stage.Crime.ScriptedCrime);
    }

    [Fact]
    public void A_civilian_can_be_robbed_once_and_a_failed_attempt_is_reported()
    {
        using SocietyStage lucky = new(roll: 1);
        long mark = lucky.Civilian();
        Assert.True(lucky.Crimes.IsPickpocketTarget(mark));
        Assert.Equal("You found nothing valuable.", lucky.Crimes.Pickpocket(mark).Message);
        Assert.Equal(DaggerfallCrimeAttemptOutcome.SucceededWithoutTakingProperty, Assert.Single(lucky.Crime.Attempts).Outcome);
        Assert.Equal("You already tried to pickpocket this person.", lucky.Crimes.Pickpocket(mark).Message);
        Assert.Empty(lucky.Granted);

        using SocietyStage clumsy = new(roll: 100);
        long other = clumsy.Civilian();
        List<DaggerfallCrimeIncidentSave> reported = [];
        clumsy.Crime.IncidentRecorded += reported.Add;
        Assert.Equal("Your pickpocket attempt failed.", clumsy.Crimes.Pickpocket(other).Message);
        DaggerfallCrimeIncidentSave incident = Assert.Single(reported);
        Assert.Equal((DaggerfallCrimeKind.Pickpocketing, DaggerfallCrimeStage.Attempted), (incident.Crime, incident.Stage));
        Assert.Empty(clumsy.Provoked);
    }

    [Fact]
    public void A_failed_theft_from_the_watch_provokes_it_and_charges_nothing()
    {
        using SocietyStage stage = new(roll: 100);
        long watch = stage.CityWatch();
        int reported = 0;
        stage.Crime.IncidentRecorded += _ => reported++;

        Assert.Equal("Your pickpocket attempt failed.", stage.Crimes.Pickpocket(watch).Message);
        Assert.Equal([watch], stage.Provoked);
        Assert.Equal(0, reported);
    }
}
