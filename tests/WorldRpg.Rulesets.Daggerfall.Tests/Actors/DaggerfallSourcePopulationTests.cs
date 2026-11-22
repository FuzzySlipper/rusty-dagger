using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Ai;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The source population's admission and walk step, built without the session: who a town's townsfolk are (its climate's
/// race, the placement's gender and seed) through night and dawn, and their walk. Their drawing is in
/// DaggerfallPopulationSessionTests and DaggerfallPeopleSwitchTests.
/// </summary>
public sealed class DaggerfallSourcePopulationTests
{
    private sealed class Stage : IDisposable
    {
        internal Stage(DaggerfallSiteRecord? site = null, IReadOnlyList<DaggerfallPopulationPlacement>? population = null)
        {
            Civilians = new(site, population);
            DaggerfallSiteProfile indoors = Civilians.At(DaggerfallWorldProfileKind.Dungeon, "indoors");
            Population = new DaggerfallSourcePopulation(Civilians.Npcs, Civilians.Actors, Player, TestPayload.Definitions,
                DaggerfallCivilianWanderTuning.Classic, Civilians.Civilians, () => Indoors ? indoors : Civilians.Profile, () => Civilians.Profile.ProfileKey, () => Civilians.Appearance,
                () => true, () => true, point => point, point => point, _ => Civilians.Site, () => Day, () => false, Held.Contains,
                (actor, policy, step, deltaSeconds) => Walked.Add((actor.DurableId, policy, step, deltaSeconds)), Reports.Add);
        }

        internal CivilianStage Civilians { get; }
        internal PlayerControlState Player { get; } = new(new WorldPoint(0f, 0f, 0f), 0f, 0f);
        internal bool Day { get; set; } = true;
        internal bool Indoors { get; set; }
        internal HashSet<long> Held { get; } = [];
        internal List<(long Id, WanderPolicy Policy, ulong Step, float DeltaSeconds)> Walked { get; } = [];
        internal List<string> Reports { get; } = [];
        internal DaggerfallNpc[] Townsfolk => [.. Civilians.Npcs.All.Where(DaggerfallCivilians.IsSourcePopulation)];
        internal DaggerfallSourcePopulation Population { get; }

        internal long Person(bool sourcePopulation = true)
        {
            long id = Civilians.Register(sourcePopulation);
            Civilians.Npcs.Place(id, Civilians.Profile.ProfileKey, new WorldPoint(2f, 0f, 2f));
            Civilians.Civilians.Materialize(id, new ActorPose(new WorldPoint(2f, 0f, 2f), 0f));
            return id;
        }

        public void Dispose() => Civilians.Dispose();
    }

    [Fact]
    public void An_admitted_town_person_walks_by_day_with_the_tuned_wander()
    {
        using Stage stage = new();
        long person = stage.Person();
        stage.Population.Step(7, .25f);
        (long id, WanderPolicy policy, ulong step, float deltaSeconds) = Assert.Single(stage.Walked);
        Assert.Equal((person, 7UL, .25f), (id, step, deltaSeconds));
        Assert.Equal(DaggerfallCivilianWanderTuning.Classic.MovementSpeedUnitsPerSecond, policy.MovementSpeedUnitsPerSecond);
        Assert.Equal(ActorNavigationMode.Ground, policy.NavigationMode);
        // Townspeople ease their turns and their setting off and stopping.
        Assert.Equal(float.DegreesToRadians(DaggerfallCivilianWanderTuning.Classic.TurnDegreesPerSecond), policy.TurnRadiansPerSecond);
        Assert.Equal(DaggerfallCivilianWanderTuning.Classic.EaseSeconds, policy.EaseSeconds);
        Assert.True(float.IsFinite(policy.TurnRadiansPerSecond) && policy.EaseSeconds > 0f);
    }

    [Fact]
    public void Night_a_held_person_and_one_not_of_the_source_population_do_not_walk()
    {
        using Stage stage = new();
        long person = stage.Person();
        long other = stage.Person(sourcePopulation: false);

        stage.Population.Step(1, .25f);
        Assert.Equal(person, Assert.Single(stage.Walked).Id);
        Assert.Equal(WanderState.Unloaded, stage.Civilians.Actors.Get(other).Wander.State);

        stage.Held.Add(person);
        stage.Population.Step(2, .25f);
        Assert.Single(stage.Walked);
        Assert.NotEqual(WanderState.Unloaded, stage.Civilians.Actors.Get(person).Wander.State);

        stage.Held.Clear();
        stage.Day = false;
        stage.Population.Step(3, .25f);
        Assert.Single(stage.Walked);
        Assert.Equal(WanderState.Unloaded, stage.Civilians.Actors.Get(person).Wander.State);
    }

    [Fact]
    public void Nobody_walks_indoors()
    {
        using Stage stage = new();
        long person = stage.Person();
        stage.Indoors = true;
        stage.Population.Step(1, .25f);
        Assert.Empty(stage.Walked);
        Assert.Equal(WanderState.Unloaded, stage.Civilians.Actors.Get(person).Wander.State);
    }

    /// <summary>A real town by region and name.</summary>
    private static DaggerfallSiteRecord Town(int region, string name) =>
        TestPayload.Definitions.Locations.Records.Single(site => site.Id.Region == region && site.Name == name && site.Exterior is not null);

    /// <summary>
    /// Eight placements a few metres from the player wearing the stage's billboard, every other one flagged female, each
    /// with its own name seed.
    /// </summary>
    private static DaggerfallPopulationPlacement[] Placements(string town) => [.. Enumerable.Range(0, 8).Select(index =>
        new DaggerfallPopulationPlacement($"population/{town}/{index}", new WorldPoint(index, 0F, 3F), 210, 4, 0,
            (byte)(index % 2 == 1 ? 0x20 : 0), (ushort)(1000 + index * 37)))];

    [Theory]
    [InlineData(17, "Daggerfall", "breton")]
    [InlineData(0, "Alik'ra", "redguard")]
    [InlineData(16, "Wrothgaria", "nord")]
    public void A_town_dresses_every_townsperson_as_its_climates_race_by_their_flag_and_seed(int region, string name, string race)
    {
        DaggerfallSiteRecord town = Town(region, name);
        DaggerfallPopulationPlacement[] placements = Placements(name);
        using Stage stage = new(town, placements);
        // The race is the People of the climate cell the town lies in, as the donor's PopulationManager takes it.
        Assert.Equal(race, TestPayload.Definitions.Grids.Climate.GetCell(town.MapPixelX + 1, town.MapPixelY).People, StringComparer.OrdinalIgnoreCase);

        stage.Population.Reconcile();

        DaggerfallRaceDefinition dressed = TestPayload.Definitions.Catalogs.RequireRace(race);
        Assert.Equal(placements.Length, stage.Townsfolk.Length);
        foreach (DaggerfallPopulationPlacement placement in placements)
        {
            DaggerfallNpc npc = Assert.Single(stage.Townsfolk, npc => npc.StableKey == placement.Id);
            string gender = (placement.Flags & 0x20) != 0 ? "Female" : "Male";
            Assert.Equal((race, gender, (int)placement.NameSeed), (npc.Appearance.Race, npc.Appearance.Gender, npc.Appearance.NameSeed));
            // Each wears one of its race's townsperson archives for its gender, the one its seed picks.
            int archive = Assert.IsType<int>(stage.Population.Townsperson(npc.DurableId));
            Assert.Contains(archive, dressed.Townspeople[gender]);
            Assert.Equal(dressed.Townsperson(gender, placement.NameSeed), archive);
        }
        Assert.Empty(stage.Reports);
    }

    [Fact]
    public void A_site_whose_climate_names_no_race_has_no_townsfolk_and_says_so_once()
    {
        // Past the climate grid's edge no cell names a race; no race is made up for the site.
        DaggerfallSiteRecord town = Town(17, "Daggerfall") with { Longitude = 5000 * 128 };
        using Stage stage = new(town, Placements("raceless"));

        stage.Population.Reconcile();
        stage.Population.Reconcile();

        Assert.Empty(stage.Townsfolk);
        Assert.Empty(stage.Civilians.Materialized);
        string report = Assert.Single(stage.Reports);
        Assert.Contains("Daggerfall", report);
        Assert.Contains("names no race", report);
    }

    [Fact]
    public void Night_hides_the_townsfolk_and_dawn_brings_back_the_same_people_in_the_same_look()
    {
        using Stage stage = new(Town(20, "Sentinel"), Placements("Sentinel"));
        stage.Population.Reconcile();
        var byDay = stage.Townsfolk.ToDictionary(npc => npc.StableKey, npc => (npc.DurableId, npc.Appearance, stage.Population.Townsperson(npc.DurableId)));
        Assert.NotEmpty(byDay);

        stage.Day = false;
        stage.Population.Reconcile();
        Assert.All(stage.Townsfolk, npc => Assert.Equal(DaggerfallNpcPresence.Hidden, npc.Presence));

        stage.Day = true;
        stage.Population.Reconcile();
        Assert.Equal(byDay, stage.Townsfolk.ToDictionary(npc => npc.StableKey, npc => (npc.DurableId, npc.Appearance, stage.Population.Townsperson(npc.DurableId))));
        Assert.All(stage.Townsfolk, npc => Assert.Equal(DaggerfallNpcPresence.Active, npc.Presence));
    }
}
