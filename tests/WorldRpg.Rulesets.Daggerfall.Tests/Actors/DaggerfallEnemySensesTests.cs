using Rusty.Engine.Entities;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Behavior;
using WorldRpg.Rulesets.Daggerfall.Modules.Encounters;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>Hostility, the perception and motor inputs and an enemy near the player, built without the session.</summary>
public sealed class DaggerfallEnemySensesTests
{
    private const long Foe = 10, Ally = 11, Pacified = 12, Far = 13;

    private sealed class Stage : IDisposable
    {
        internal Stage()
        {
            DaggerfallDefinitions definitions = TestPayload.Definitions;
            Monster = definitions.Actors.Values.First(actor => actor.Kind == DaggerfallActorKinds.Monster && actor.MobileId is not null);
            DaggerfallActorDefinition player = definitions.RequireActor(new("player"));
            DaggerfallMechanicsState mechanics = new();
            Actors.CreatePlayer(DaggerfallActorIdentity.PlayerEntityId, new EntityTypeId("player"),
                mechanics.CreateStats(player, DaggerfallPlayerVitals.Initial(player.Stats, definitions.Catalogs.RequireCareer("class00"))), "health",
                DaggerActorFactory.PlayerCapabilities);
            foreach ((long id, float z) in new[] { (Foe, 5f), (Ally, 5f), (Pacified, 5f), (Far, 40f) })
            {
                DaggerActorFactory.CreateNonPlayerActor(Actors, id, Monster, mechanics.CreateStats(Monster, DaggerActorFactory.InitialVitals(Random.Service, Monster, id)),
                    new ActorPose(new WorldPoint(0f, 0f, z), 0f));
                Definitions[id] = Monster;
            }
            Senses = new DaggerfallEnemySenses(Actors, Player, definitions, Definitions, id => id == Ally ? "player-ally" : null, id => id == Pacified,
                () => LastPerception, new DaggerfallEffectLifecycle(Actors, new DaggerfallEffectCatalog([])),
                new DaggerfallLocomotionPolicy(DaggerfallLocomotionTuning.Classic, new()), () => WeaponDrawn, () => false, () => 600,
                Random.Service, () => Profile, new DaggerfallEncounterRuntime(definitions, Random.Service));
        }

        internal ActorsState Actors { get; } = new();
        internal DaggerfallActorDefinition Monster { get; }
        internal Dictionary<long, DaggerfallActorDefinition> Definitions { get; } = [];
        internal PlayerControlState Player { get; } = new(new WorldPoint(0f, 0f, 0f), 0f, 0f);
        internal KeyedRandomFake Random { get; } = KeyedRandomFake.Create(0);
        internal Dictionary<long, DaggerfallEnemyPerceptionDecision> LastPerception { get; } = [];
        internal bool WeaponDrawn { get; set; } = true;
        internal DaggerfallWorldProfileKey Profile { get; set; } = new(new DaggerfallSiteId(0, 0), DaggerfallWorldProfileKind.Exterior, "test");
        internal DaggerfallEnemySenses Senses { get; }

        public void Dispose() => Actors.Dispose();
    }

    [Fact]
    public void A_monster_is_hostile_unless_it_fights_for_the_player_or_is_pacified()
    {
        using Stage stage = new();
        Assert.True(stage.Senses.IsHostile(Foe, stage.Monster));
        Assert.False(stage.Senses.IsHostile(Ally, stage.Monster));
        Assert.False(stage.Senses.IsHostile(Pacified, stage.Monster));
        Assert.False(stage.Senses.IsHostile(Foe, stage.Monster with { Kind = DaggerfallActorKinds.Civilian }));
    }

    [Fact]
    public void The_motor_reads_levels_a_turned_back_and_a_sheathed_weapon()
    {
        using Stage stage = new();
        // At yaw zero the player looks along -Z, away from the foe at +Z, so their back is turned to it.
        stage.Player.YawRadians = MathF.PI;
        DaggerfallEnemyMotorContext facing = stage.Senses.MotorContext(Foe, DaggerfallActorIdentity.PlayerEntityId);
        Assert.Equal(new DaggerfallEnemyMotorContext(stage.Monster.Level ?? 1, stage.Actors.Player.Progression.Level, false, false, false), facing);

        stage.Player.YawRadians = 0f;
        stage.WeaponDrawn = false;
        DaggerfallEnemyMotorContext behind = stage.Senses.MotorContext(Foe, DaggerfallActorIdentity.PlayerEntityId);
        Assert.True(behind.TargetBackTurned);
        Assert.True(behind.TargetBowOrSheathed);
    }

    [Fact]
    public void Perception_rolls_are_keyed_to_the_enemy_and_the_minute_and_read_the_profile_and_the_weapon()
    {
        using Stage stage = new();
        stage.WeaponDrawn = false;
        DaggerfallEnemyPerceptionContext context = stage.Senses.PerceptionContext(Foe);
        Assert.Equal(600, context.GameMinute);
        Assert.False(context.TargetInsideDungeonCastle);
        Assert.True(context.TargetWeaponSheathed);
        Assert.True(context.EnemyHostile);
        Assert.False(context.TargetPacified);

        _ = context.RollPercent(100);
        _ = context.RollPercent(100);
        Assert.Equal(["actor:10:minute:600:roll:0", "actor:10:minute:600:roll:1"],
            stage.Random.Requests.Where(draw => draw.Scope == "daggerfall.enemy-perception.v1").Select(draw => draw.Key));

        stage.Profile = new(new DaggerfallSiteId(0, 0), DaggerfallWorldProfileKind.Dungeon, "test");
        Assert.True(stage.Senses.PerceptionContext(Foe).TargetInsideDungeonCastle);
        Assert.Throws<InvalidOperationException>(() => stage.Senses.PerceptionContext(404));
    }

    [Fact]
    public void An_enemy_is_near_within_twelve_metres_or_in_sight_but_never_an_ally_or_a_pacified_one()
    {
        using Stage stage = new();
        Assert.True(stage.Senses.EnemyNearby());

        stage.Actors.Get(Foe).ApplyPose(new ActorPose(new WorldPoint(0f, 0f, 30f), 0f));
        Assert.False(stage.Senses.EnemyNearby());

        stage.LastPerception[Far] = new(InSight: true, false, false, true, false, false, null, []);
        Assert.True(stage.Senses.EnemyNearby());
    }
}
