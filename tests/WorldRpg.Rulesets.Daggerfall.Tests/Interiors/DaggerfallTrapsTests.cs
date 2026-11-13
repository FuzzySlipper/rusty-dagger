using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Combat;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.World;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>Hurt traps as an owner, built without the session: whom one the player sets off reaches, and what a thrown body springs.</summary>
public sealed class DaggerfallTrapsTests
{
    private const long Body = 12;

    private static DaggerfallDungeonActionDefinition Action(string id, DaggerfallDungeonActionFlag flag, string? next = null) =>
        new(id, 1, (uint)DaggerfallDungeonTriggerFlag.Direct, (byte)flag, 0, 0, 2, -1, next, IsFlat: true);

    private sealed class Stage : IDisposable
    {
        internal Stage(float away, params DaggerfallDungeonActionDefinition[] actions)
        {
            Actors.CreatePlayer(DaggerfallActorIdentity.PlayerEntityId, new EntityTypeId("player"), TestSessions.HealthyMechanics(), "health", DaggerActorFactory.PlayerCapabilities);
            Actors.CreateActor(Body, new EntityTypeId("test"), TestSessions.HealthyMechanics(), new ActorPose(new WorldPoint(away, 0f, 0f), 0f), "health", DaggerActorFactory.NonPlayerCapabilities);
            _ = CombatCollaborators.Targeting(Actors, new Dictionary<long, DaggerfallActorDefinition>(), out SpatialMovementSystem spatial, out SpatialFake fake);
            Reactions = new ActorReactions(new ActorNavigationCoordinator(fake.Service, spatial.Session));
            Traps = new DaggerfallTraps(DaggerfallHazardTuning.Default, Actors, Reactions, () => new WorldPoint(0f, 0f, 0f), () => actions,
                _ => actions.Take(1), () => 5, new CombatResolution(), RandomMaximum.Create(), Harm.Add);
        }

        internal ActorsState Actors { get; } = new();
        internal ActorReactions Reactions { get; }
        internal List<DamageResult> Harm { get; } = [];
        internal DaggerfallTraps Traps { get; }

        internal IEnumerable<long> Harmed => Harm.Select(harm => harm.Target.Entity == Actors.Player.Actor.Entity ? DaggerfallActorIdentity.PlayerEntityId : Body);

        public void Dispose() => Actors.Dispose();
    }

    [Fact]
    public void A_hurt_the_player_sets_off_reaches_bodies_in_reach_and_nothing_else_spreads()
    {
        using Stage near = new(1f);
        near.Traps.SpreadHurt(Action("trap", DaggerfallDungeonActionFlag.Hurt22), 1);
        Assert.Contains(Body, near.Harmed);

        using Stage far = new(DaggerfallHazardTuning.Default.TrapRadius + 1f);
        far.Traps.SpreadHurt(Action("trap", DaggerfallDungeonActionFlag.Hurt22), 1);
        Assert.DoesNotContain(Body, far.Harmed);

        using Stage drain = new(1f);
        drain.Traps.SpreadHurt(Action("trap", DaggerfallDungeonActionFlag.DrainMagicka), 1);
        Assert.Empty(drain.Harm);
    }

    [Fact]
    public void A_thrown_body_springs_a_trap_and_its_links_once_a_throw()
    {
        using Stage stage = new(1f, Action("first", DaggerfallDungeonActionFlag.Hurt22, "second"), Action("second", DaggerfallDungeonActionFlag.Hurt23));
        ActorState body = stage.Actors.Get(Body);
        stage.Reactions.Begin(Body, Vector3.Zero, .05d, 9f, ActorNavigationMode.Ground);

        stage.Traps.ForgetLanded();
        stage.Traps.SpringOn(body, Vector3.Zero);
        stage.Traps.SpringOn(body, Vector3.Zero);
        Assert.Equal(new long[] { Body, Body }, stage.Harmed);

        // Still held: the throw has sprung them. Landed and thrown again, it springs them again.
        stage.Traps.ForgetLanded();
        stage.Traps.SpringOn(body, Vector3.Zero);
        Assert.Equal(2, stage.Harm.Count);
        stage.Reactions.Step(stage.Actors, 1f, _ => { });
        Assert.False(stage.Reactions.Holds(Body));
        stage.Traps.ForgetLanded();
        stage.Traps.SpringOn(body, Vector3.Zero);
        Assert.Equal(4, stage.Harm.Count);
    }
}
