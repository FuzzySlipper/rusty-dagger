using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>Vampirism built without the session (<see cref="CurseFixture"/>).</summary>
public sealed class DaggerfallVampirismTests
{
    private static int ProvinceRegion() => Enumerable.Range(0, 62).First(region =>
        TestPayload.Definitions.Factions.Factions.Values.Count(value => value.Region == region && value.Type == 7) == 1);

    private static int RegionWithoutProvince() => Enumerable.Range(0, 62).First(region =>
        TestPayload.Definitions.Factions.Factions.Values.Count(value => value.Region == region && value.Type == 7) != 1);

    [Fact]
    public void The_clan_is_the_infection_regions_and_a_region_without_one_says_why()
    {
        using CurseFixture fixture = new();
        int region = ProvinceRegion();
        Assert.True(fixture.Vampirism.TryClan(region, out int clan, out string? unavailable));
        Assert.Equal(DaggerfallVampirismPolicy.GetVampireClan(TestPayload.Definitions.Factions, region).Id, clan);
        Assert.Null(unavailable);

        Assert.False(fixture.Vampirism.TryClan(RegionWithoutProvince(), out _, out unavailable));
        Assert.StartsWith("Vampire clan is unavailable: ", unavailable);
    }

    [Fact]
    public void The_awakening_draws_its_cemetery_and_passes_two_weeks_to_the_hour_after_dusk()
    {
        using CurseFixture fixture = new(roll: 2);
        Assert.Equal(2, fixture.Vampirism.CemeteryIndex("infection", 5));
        Assert.Equal("infection:cemetery", fixture.Random.Requests[^1].Key);
        Assert.Equal(4, fixture.Random.Requests[^1].Maximum);

        int hour = fixture.Time.Calendar.Hour;
        Assert.Equal(14L * DaggerfallCalendar.SecondsPerDay + (DaggerfallSkyTime.DuskHour + 1 - hour) * 3600L,
            fixture.Vampirism.AwakeningSeconds);
    }

    [Fact]
    public void Becoming_a_vampire_heals_and_the_hunger_returns_after_the_satiation_period()
    {
        using CurseFixture fixture = new();
        fixture.Health.SetCurrent(5);
        Assert.True(fixture.Vampirism.Become(CurseFixture.Transition(DaggerfallInfectionKind.Vampire), 153));
        Assert.Equal(153, fixture.Overrides.Current!.State.Vampire!.Clan);
        Assert.Equal(fixture.Health.Maximum.Value, fixture.Health.Current);
        Assert.False(fixture.Vampirism.Become(CurseFixture.Transition(DaggerfallInfectionKind.Vampire) with { Instance = "second" }, 153));

        Assert.False(fixture.Vampirism.MustFeed);
        fixture.Pass((DaggerfallTuning.Defaults.Vampirism.SatiationMinutes + 1L) * 60);
        Assert.True(fixture.Vampirism.MustFeed);
    }

    [Fact]
    public void The_clan_offers_its_first_quest_then_its_ordinary_work_and_the_cure_its_own()
    {
        using CurseFixture fixture = new();
        fixture.Vampirism.Become(CurseFixture.Transition(DaggerfallInfectionKind.Vampire), 153);
        long minute = fixture.Time.Calendar.MinuteNumber;

        Assert.True(fixture.Vampirism.StartQuestOpportunity(minute, cure: false));
        Assert.Equal(("P0A01L00.txt", 0), (fixture.Quests.Offered[^1].Source, fixture.Quests.Offered[^1].Faction));
        Assert.True(fixture.Overrides.Current!.State.Vampire!.InitialQuestStarted);

        Assert.False(fixture.Vampirism.StartQuestOpportunity(minute + 1, cure: false));
        fixture.Quests.Work.Add("P0B00L01");
        Assert.True(fixture.Vampirism.StartQuestOpportunity(minute + 2, cure: false));
        Assert.Equal(("P0B00L01.txt", 153), (fixture.Quests.Offered[^1].Source, fixture.Quests.Offered[^1].Faction));
        Assert.False(fixture.Vampirism.StartQuestOpportunity(minute + 2, cure: false));

        Assert.True(fixture.Vampirism.StartQuestOpportunity(minute, cure: true));
        Assert.Equal(("$CUREVAM.txt", 0), (fixture.Quests.Offered[^1].Source, fixture.Quests.Offered[^1].Faction));
        fixture.Quests.Refuse = true;
        Assert.False(fixture.Vampirism.StartQuestOpportunity(minute + 3, cure: true));
        Assert.Equal("The cure for vampirism cannot be offered right now.", fixture.Presentation.LastOutcome);
    }

    [Fact]
    public void The_cure_passes_an_hour_and_ending_vampirism_fails_the_clans_work()
    {
        using CurseFixture fixture = new();
        Assert.False(fixture.Vampirism.Cure());
        fixture.Vampirism.Become(CurseFixture.Transition(DaggerfallInfectionKind.Vampire), 153);
        Assert.True(fixture.Vampirism.Cure(fromQuest: true));
        Assert.Null(fixture.Overrides.Current);
        Assert.Equal([60L], fixture.QuestTime);
        Assert.Empty(fixture.Elapsed);
        Assert.Equal("Your vampirism is cured.", fixture.Presentation.LastOutcome);
        Assert.Equal([("P0", "Vampirism ended")], fixture.Quests.Failed);
    }
}
