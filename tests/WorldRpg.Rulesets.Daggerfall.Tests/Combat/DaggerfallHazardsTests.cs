using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.World;
using Rusty.Engine.Entities;
using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>The fires as an owner, built without the session: who burns, and where a thrown body meets the traps.</summary>
public sealed class DaggerfallHazardsTests
{
    private const double Step = 1d / 60d;
    private const long Thrown = 12;

    private sealed class Stage : IDisposable
    {
        internal Stage(Vector3 player, Vector3 body)
        {
            DaggerfallSiteProfile inputs = CombatCollaborators.Profile.Value;
            Actors.CreatePlayer(DaggerfallActorIdentity.PlayerEntityId, new EntityTypeId("player"), TestSessions.HealthyMechanics(), "health", DaggerActorFactory.PlayerCapabilities);
            Actors.CreateActor(Thrown, new EntityTypeId("test"), TestSessions.HealthyMechanics(), new ActorPose(WorldPoint.From(body), 0f), "health", DaggerActorFactory.NonPlayerCapabilities);
            _ = CombatCollaborators.Targeting(Actors, new Dictionary<long, DaggerfallActorDefinition>(), out SpatialMovementSystem spatial, out SpatialFake fake);
            Reactions = new ActorReactions(new ActorNavigationCoordinator(fake.Service, spatial.Session));
            Hazards = new DaggerfallHazards(DaggerfallHazardTuning.Default, Actors, Reactions, () => WorldPoint.From(player), point => point, () => inputs,
                new DaggerfallVitalityConsequences(new CombatResolution()), Presentation, Harm.Add, () => Forgotten++,
                (actor, at) => Sprung.Add((actor.DurableId, at)));
        }

        internal ActorsState Actors { get; } = new();
        internal ActorReactions Reactions { get; }
        internal PresentationState Presentation { get; } = new("Ready", 5d);
        internal List<DamageResult> Harm { get; } = [];
        internal List<(long Actor, Vector3 At)> Sprung { get; } = [];
        internal int Forgotten { get; private set; }
        internal DaggerfallHazards Hazards { get; }

        internal void Run(int steps) { for (int step = 0; step < steps; step++) Hazards.Step(Step); }

        public void Dispose() => Actors.Dispose();
    }

    private static Vector3 Campfire => CombatCollaborators.Profile.Value.Decorations.First(flat => flat.Source == (210, 1)).Position.ToVector();
    private static readonly Vector3 Away = new(40f, 0f, 40f);

    [Fact]
    public void A_player_in_a_fire_burns_and_is_told_and_one_away_from_it_is_not()
    {
        using Stage burning = new(Campfire, Away);
        burning.Run(180);
        Assert.True(burning.Harm.Sum(harm => harm.ActualHealthLost) > 0);
        Assert.All(burning.Harm, harm => Assert.Equal(burning.Actors.Player.Actor.Entity, harm.Target.Entity));
        Assert.Equal("The fire burns you.", burning.Presentation.LastOutcome);

        using Stage clear = new(Away, Away);
        clear.Run(180);
        Assert.Empty(clear.Harm);
        Assert.Equal("Ready", clear.Presentation.LastOutcome);
    }

    [Fact]
    public void Only_a_held_body_catches_fire_and_meets_the_traps_and_each_step_forgets_the_landed_first()
    {
        using Stage stage = new(Away, Campfire);
        stage.Run(60);
        Assert.Empty(stage.Harm);
        Assert.Empty(stage.Sprung);
        Assert.Equal(60, stage.Forgotten);

        stage.Reactions.Begin(Thrown, Vector3.Zero, 3d, 9f, ActorNavigationMode.Ground);
        stage.Run(1);
        Assert.Equal((Thrown, Campfire), Assert.Single(stage.Sprung));
        stage.Run(59);
        Assert.Equal(60, stage.Sprung.Count);
        Assert.True(stage.Harm.Sum(harm => harm.ActualHealthLost) > 0);
        Assert.All(stage.Harm, harm => Assert.Equal(stage.Actors.Get(Thrown).Actor.Entity, harm.Target.Entity));
    }
}
