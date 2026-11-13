using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Presentation;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.World;
using Rusty.Engine.Entities;
using WorldRpg.Kit;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>What a monster's landed hit carries, built without the session.</summary>
public sealed class DaggerfallMonsterHitsTests
{
    private const long Monster = 2;

    private sealed class Stage : IDisposable
    {
        internal Stage()
        {
            DaggerfallDefinitions definitions = TestPayload.Definitions;
            DaggerfallActorDefinition player = definitions.RequireActor(new DaggerfallActorId("player"));
            StatsComponent stats = new DaggerfallMechanicsState().CreateStats(player,
                DaggerfallPlayerVitals.Initial(player.Stats, definitions.Catalogs.RequireCareer("class00")));
            Actors.CreatePlayer(DaggerfallActorIdentity.PlayerEntityId, new EntityTypeId("player"), stats, "health", DaggerActorFactory.PlayerCapabilities);
            Actors.CreateActor(Monster, new EntityTypeId("rat"), DaggerCombatFixture.Stats(100d, 600d),
                new ActorPose(new WorldPoint(1f, 0f, 0f), 0f), "health", DaggerActorFactory.NonPlayerCapabilities);
            Actors.Player.Progression.AdvanceTo(500, 2);
            DaggerfallCharacterState character = new(definitions, stats, player);
            IRandomService random = RandomMaximum.Create();
            DaggerfallWorldTime time = new(DaggerfallCalendar.FromAbsoluteSeconds(100L * DaggerfallCalendar.SecondsPerDay), 0d, 12d);
            Effects = new(Actors, new DaggerfallEffectCatalog(DaggerfallDiseasePolicy.Definitions(random,
                () => time.Calendar.DayNumber, () => character.Career)));
            Hits = new DaggerfallMonsterHits(new DaggerfallDiseases(Effects, Actors, random, time, character, new CombatResolution(), Presentation, definitions),
                new DaggerfallTransformationInfections(Effects, () => time.Calendar.DayNumber, null, false, _ => { }), Effects,
                (attacker, target) => Paralyzed.Add((attacker, target)), () => 17);
        }

        internal ActorsState Actors { get; } = new();
        internal DaggerfallEffectLifecycle Effects { get; }
        internal PresentationState Presentation { get; } = new("Ready", 5d);
        internal List<(long Attacker, long Target)> Paralyzed { get; } = [];
        internal DaggerfallMonsterHits Hits { get; }

        public void Dispose() => Actors.Dispose();
    }

    private static DaggerfallMonsterHitExposure Hit(DaggerfallMonsterHitConsequence consequence) =>
        new(Monster, DaggerfallActorIdentity.PlayerEntityId, 7, 13, new DaggerfallMonsterHit(0, 3, consequence));

    [Fact]
    public void A_diseased_hit_gives_the_player_a_disease_and_nothing_else()
    {
        using Stage stage = new();
        stage.Hits.Deliver(Hit(DaggerfallMonsterHitConsequence.Plague));
        Assert.Single(stage.Effects.Active);
        Assert.Empty(stage.Paralyzed);
    }

    [Fact]
    public void A_paralyzing_hit_paralyzes_from_the_attacker_and_a_clean_hit_carries_nothing()
    {
        using Stage stage = new();
        stage.Hits.Deliver(Hit(DaggerfallMonsterHitConsequence.Paralysis));
        Assert.Equal((Monster, DaggerfallActorIdentity.PlayerEntityId), Assert.Single(stage.Paralyzed));

        stage.Hits.Deliver(Hit(DaggerfallMonsterHitConsequence.None));
        Assert.Single(stage.Paralyzed);
        Assert.Empty(stage.Effects.Active);
    }
}
