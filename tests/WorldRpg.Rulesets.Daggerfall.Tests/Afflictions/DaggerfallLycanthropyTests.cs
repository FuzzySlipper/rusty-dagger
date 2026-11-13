using Rusty.Engine.Mechanics;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>Lycanthropy built without the session (<see cref="CurseFixture"/>).</summary>
public sealed class DaggerfallLycanthropyTests
{
    [Fact]
    public void An_infection_becomes_the_curse_heals_the_player_and_tells_them()
    {
        using CurseFixture fixture = new();
        fixture.Health.SetCurrent(5);
        DaggerfallInfectionConsumption became = fixture.Lycanthropy.Become(CurseFixture.Transition(DaggerfallInfectionKind.Wereboar));
        Assert.True(became.Applied);
        Assert.Equal(DaggerfallRacialKind.Wereboar, fixture.Overrides.Current!.State.Kind);
        Assert.Equal(fixture.Health.Maximum.Value, fixture.Health.Current);
        Assert.Equal("You are now a wereboar.", fixture.Presentation.LastOutcome);

        DaggerfallInfectionConsumption again = fixture.Lycanthropy.Become(CurseFixture.Transition(DaggerfallInfectionKind.Werewolf) with { Instance = "second" });
        Assert.False(again.Applied);
        Assert.Equal("A racial override is already active.", again.Unavailable);
    }

    [Fact]
    public void Beast_form_comes_once_a_day_unless_forced_or_the_ring_is_worn()
    {
        using CurseFixture fixture = new();
        Assert.False(fixture.Lycanthropy.Morph());
        fixture.Lycanthropy.Become(CurseFixture.Transition(DaggerfallInfectionKind.Werewolf));

        Assert.True(fixture.Lycanthropy.Morph());
        Assert.True(fixture.Overrides.Current!.State.BeastForm);
        Assert.Equal(1, fixture.HandsEmptied);
        Assert.Equal("You transform into a werewolf.", fixture.Presentation.LastOutcome);
        Assert.True(fixture.Lycanthropy.Morph());
        Assert.False(fixture.Overrides.Current!.State.BeastForm);
        Assert.Equal("You return to your human form.", fixture.Presentation.LastOutcome);

        Assert.False(fixture.Lycanthropy.Morph());
        Assert.Equal("You can only change into beast form once per day.", fixture.Presentation.LastOutcome);
        fixture.RingWorn = true;
        Assert.True(fixture.Lycanthropy.Morph());
        Assert.Equal(2, fixture.HandsEmptied);
    }

    [Fact]
    public void A_round_ending_under_a_full_moon_forces_the_change_unless_the_ring_is_worn()
    {
        using CurseFixture fixture = new();
        fixture.Lycanthropy.Become(CurseFixture.Transition(DaggerfallInfectionKind.Werewolf));
        DaggerfallCalendar day = fixture.Time.Calendar;
        while (day.IsFullMoon) day = DaggerfallCalendar.FromAbsoluteSeconds(day.ToAbsoluteSeconds() + DaggerfallCalendar.SecondsPerDay);
        DaggerfallCalendar fullMoon = day;
        while (!fullMoon.IsFullMoon) fullMoon = DaggerfallCalendar.FromAbsoluteSeconds(fullMoon.ToAbsoluteSeconds() + DaggerfallCalendar.SecondsPerDay);

        fixture.Lycanthropy.AdvanceRound(day);
        Assert.False(fixture.Overrides.Current!.State.BeastForm);
        fixture.RingWorn = true;
        fixture.Lycanthropy.AdvanceRound(fullMoon);
        Assert.False(fixture.Overrides.Current!.State.BeastForm);
        fixture.RingWorn = false;
        fixture.Lycanthropy.AdvanceRound(fullMoon);
        Assert.True(fixture.Overrides.Current!.State.BeastForm);
        Assert.Equal(fullMoon.MinuteNumber, fixture.Overrides.Current!.State.LastMorphMinute);
    }

    [Fact]
    public void Hunger_lowers_maximum_health_once_its_period_passes_without_a_kill()
    {
        using CurseFixture fixture = new();
        fixture.Lycanthropy.Become(CurseFixture.Transition(DaggerfallInfectionKind.Werewolf));
        Stat maximum = fixture.Actors.Player.Stats.GetStat(StatId.Parse("health-maximum"));
        double full = maximum.Value;
        fixture.Lycanthropy.Refresh();
        Assert.Equal(full, maximum.Value);

        DaggerfallLycanthropyTuning tuning = DaggerfallTuning.Defaults.Lycanthropy;
        fixture.Pass((tuning.HungerPeriodMinutes + 600L) * 60);
        fixture.Lycanthropy.Refresh();
        Assert.Equal(Math.Max(tuning.MinimumHealth, full - Math.Round(600 * tuning.HealthLossPerMinute, MidpointRounding.ToEven)), maximum.Value);
        Assert.Equal("Your lycanthropic hunger weakens you. You need to hunt an innocent.", fixture.Presentation.LastOutcome);

        fixture.RingWorn = true;
        fixture.Lycanthropy.Refresh();
        Assert.Equal(full, maximum.Value);
    }

    [Fact]
    public void The_cure_ends_beast_form_and_passes_an_hour_through_the_time_it_is_handed()
    {
        using CurseFixture fixture = new();
        Assert.False(fixture.Lycanthropy.Cure());
        Assert.Equal("Lycanthropy cure has no active curse to remove.", fixture.Presentation.LastOutcome);
        Assert.Empty(fixture.Elapsed);

        fixture.Lycanthropy.Become(CurseFixture.Transition(DaggerfallInfectionKind.Werewolf));
        fixture.Lycanthropy.Morph();
        Assert.True(fixture.Lycanthropy.Cure());
        Assert.Null(fixture.Overrides.Current);
        Assert.Equal([60L], fixture.Elapsed);
        Assert.Empty(fixture.QuestTime);
        Assert.Equal("Your lycanthropy is cured.", fixture.Presentation.LastOutcome);

        fixture.Lycanthropy.Become(CurseFixture.Transition(DaggerfallInfectionKind.Werewolf) with { Instance = "again" });
        Assert.True(fixture.Lycanthropy.Cure(fromQuest: true));
        Assert.Equal([60L], fixture.QuestTime);
    }

    [Fact]
    public void The_cure_quest_is_offered_at_each_84_day_boundary_unless_one_is_running()
    {
        using CurseFixture fixture = new();
        fixture.Lycanthropy.Become(CurseFixture.Transition(DaggerfallInfectionKind.Werewolf));
        DaggerfallCalendar before = fixture.Time.Calendar;
        fixture.Pass(200L * DaggerfallCalendar.SecondsPerDay);
        fixture.Lycanthropy.AdvanceQuestOpportunities(before, fixture.Time.Calendar);
        Assert.Equal(2, fixture.Quests.Offered.Count);
        Assert.All(fixture.Quests.Offered, offer => Assert.Equal("$CUREWER.txt", offer.Source));

        fixture.Quests.ActiveSources.Add("$CUREWER.txt");
        Assert.False(fixture.Lycanthropy.StartCureQuestOpportunity(fixture.Time.Calendar.MinuteNumber));
        fixture.Quests.ActiveSources.Clear();
        fixture.Quests.Refuse = true;
        Assert.False(fixture.Lycanthropy.StartCureQuestOpportunity(fixture.Time.Calendar.MinuteNumber));
        Assert.Equal("The cure for lycanthropy cannot be offered right now.", fixture.Presentation.LastOutcome);
    }
}
