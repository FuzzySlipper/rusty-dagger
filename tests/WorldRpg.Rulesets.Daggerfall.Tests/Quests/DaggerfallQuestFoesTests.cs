using System.Numerics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Combat;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.Travel;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// A quest's foes built without the session over <see cref="SocietyStage"/>'s actors, their definitions and placement
/// (whose perception sees no one), quests of their own and a vitality owner; the roster's spawning and removal, enemy
/// behaviour, the kill's facts, detached retirement and the retained kill are delegates the test watches.
/// </summary>
public sealed class DaggerfallQuestFoesTests
{
    private sealed class Watched
    {
        internal readonly List<DamageResult> Kills = [];
        internal readonly List<long> Removed = [];
        internal readonly List<(long Id, bool Allies)> Hostile = [];
        internal readonly List<long> Retired = [];
        internal readonly List<long> RetainedKills = [];
    }

    private static DaggerfallQuestFoes Foes(SocietyStage stage, Watched watched) =>
        new(new DaggerfallQuestInstances(stage.Definitions, RandomMinimum.Create()), stage.Actors, stage.Player, new DaggerfallTravelState(),
            stage.ActorDefinitions, stage.Placement, new DaggerfallVitalityConsequences(new CombatResolution()),
            DaggerfallTuning.Defaults.QuestSpawning, DaggerfallTuning.Defaults.EnemyBehavior,
            () => throw new InvalidOperationException("No quest foe spawns in this stage."),
            (_, _) => throw new InvalidOperationException("No quest foe spawns in this stage."),
            watched.Removed.Add, (id, allies) => watched.Hostile.Add((id, allies)), _ => false, watched.Kills.Add,
            id => { watched.Retired.Add(id); return false; }, id => { watched.RetainedKills.Add(id); return false; });

    private static long Monster(SocietyStage stage) =>
        stage.Actor(stage.Definitions.Actors.Values.First(definition => definition.Kind == DaggerfallActorKinds.Monster));

    [Fact]
    public void A_quests_kill_of_a_live_foe_is_harm_handed_back_and_one_not_live_goes_to_the_site_that_keeps_it()
    {
        using SocietyStage stage = new();
        long foe = Monster(stage);
        Watched watched = new();
        DaggerfallQuestFoes foes = Foes(stage, watched);

        Assert.Equal(stage.ActorDefinitions[foe].Team, foes.Team(foe));
        Assert.Null(foes.Team(987654321));
        Assert.False(foes.Restrained(foe));

        Assert.True(foes.Command(foe, remove: false));
        DamageResult kill = Assert.Single(watched.Kills);
        Assert.True(kill.Defeated);
        Assert.Equal(stage.Actors.Get(foe).Actor.Entity, kill.Target.Entity);

        Assert.False(foes.Command(987654321, remove: false));
        Assert.False(foes.Command(987654321, remove: true));
        Assert.Equal([987654321L], watched.RetainedKills);
        Assert.Equal([987654321L], watched.Retired);
        Assert.True(foes.Command(foe, remove: true));
        Assert.Equal([foe], watched.Removed);
    }

    [Fact]
    public void A_quests_command_over_all_enemies_reaches_the_monsters_and_not_the_townsfolk()
    {
        using SocietyStage stage = new();
        long monster = Monster(stage);
        stage.Civilian();
        Watched watched = new();

        Foes(stage, watched).CommandAllEnemies(clear: false);
        Foes(stage, watched).CommandAllEnemies(clear: true);

        Assert.Equal([(monster, true)], watched.Hostile);
        Assert.Equal([monster], watched.Removed);
    }

    [Fact]
    public void An_enemy_pursues_the_player_and_no_foe_spawns_for_a_defeated_player()
    {
        using SocietyStage stage = new();
        long foe = Monster(stage);
        stage.Player.MoveTo(new Vector3(4, 0, 4));
        Watched watched = new();

        Assert.Equal(DaggerfallActorIdentity.PlayerEntityId, Foes(stage, watched).SelectTarget(foe)?.DurableId);

        stage.Defeat(DaggerfallActorIdentity.PlayerEntityId);
        Assert.Null(Foes(stage, watched).Spawn("spawn/defeated", new("nymph", 1, false), send: false));
    }
}
