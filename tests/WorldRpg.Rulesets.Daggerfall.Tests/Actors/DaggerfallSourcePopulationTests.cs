using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Ai;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The source population's walk step, built without the session. Its admission of a town's placements by day is drawn
/// through the session in DaggerfallPopulationSessionTests.
/// </summary>
public sealed class DaggerfallSourcePopulationTests
{
    private sealed class Stage : IDisposable
    {
        internal Stage()
        {
            Civilians = new();
            DaggerfallSiteProfile indoors = Civilians.At(DaggerfallWorldProfileKind.Dungeon, "indoors");
            Population = new DaggerfallSourcePopulation(Civilians.Npcs, Civilians.Actors, Player, TestPayload.Definitions,
                DaggerfallCivilianWanderTuning.Classic, Civilians.Civilians, () => Indoors ? indoors : Civilians.Profile, () => Civilians.Profile.ProfileKey, () => Civilians.Appearance,
                () => true, () => true, point => point, point => point, _ => Civilians.Site, () => Day, () => false, Held.Contains,
                (actor, policy, step, deltaSeconds) => Walked.Add((actor.DurableId, policy, step, deltaSeconds)));
        }

        internal CivilianStage Civilians { get; }
        internal PlayerControlState Player { get; } = new(new WorldPoint(0f, 0f, 0f), 0f, 0f);
        internal bool Day { get; set; } = true;
        internal bool Indoors { get; set; }
        internal HashSet<long> Held { get; } = [];
        internal List<(long Id, WanderPolicy Policy, ulong Step, float DeltaSeconds)> Walked { get; } = [];
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
}
