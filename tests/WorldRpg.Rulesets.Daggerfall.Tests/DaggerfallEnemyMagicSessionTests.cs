using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Behavior;
using WorldRpg.Rulesets.Daggerfall.Policies;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>Enemy spell decisions exercised through the live session update and common casting path.</summary>
public sealed class DaggerfallEnemyMagicSessionTests
{
    [Fact]
    public void Ranged_enemy_spell_uses_the_common_flight_owner_and_delivers_only_on_impact()
    {
        using Fixture fixture = new();
        DaggerfallSession session = fixture.Session;
        WorldPoint playerPosition = session.State.PlayerControl.Position
            ?? throw new InvalidOperationException("The fixture player has no position.");
        long imp = session.SpawnActor("imp", new ActorPose(playerPosition with { Z = playerPosition.Z - 10f }, 0f), level: 1);
        session.State.Actors.Get(imp).Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Magicka.Value)).SetCurrent(1, clamp: true);
        fixture.Perception.Receipt = Receipt(new PerceptionPair(
            checked((ulong)imp), checked((ulong)DaggerfallActorIdentity.PlayerEntityId), 10d, 1d,
            PerceptionPairKind.Visible, 1d));

        double healthBefore = session.State.Actors.Player.Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Health.Value)).Current;
        fixture.Spatial.FloorHit = _ => default;
        session.Update(new ProductUpdate(OuterUpdate(1), []));

        Assert.True(session.LastEnemySpell.TryGetValue(imp, out DaggerfallEnemySpellEvidence? evidence));
        Assert.NotNull(evidence);
        Assert.Equal("spell.007", evidence.SpellKey);
        Assert.Equal(DaggerfallSpellTarget.SingleTargetAtRange, evidence.Target);
        Assert.Equal(DaggerfallCastOutcome.Released, evidence.Outcome);
        DaggerfallLiveSpell flight = Assert.Single(session.Casting.PendingRangedFlights);
        Assert.False(flight.Delivered);
        Assert.Equal(1d / 60d, flight.DungeonFlightElapsedSeconds, 8);
        Assert.Single(session.ReadDungeonSpellFlights());
        Assert.Equal(healthBefore, session.State.Actors.Player.Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Health.Value)).Current);

        fixture.Spatial.FloorHit = request => request.Direction.Z > .99f
            ? new SpatialHit
            {
                Present = true,
                Kind = SpatialHitKind.Entity,
                Entity = session.State.Actors.Player.Actor.Entity.Value,
                Point = request.Origin + request.Direction * (float)request.MaxDistance,
            }
            : default;
        session.Update(new ProductUpdate(OuterUpdate(2), []));

        Assert.True(flight.Delivered);
        Assert.Empty(session.Casting.PendingRangedFlights);
        Assert.Contains(flight.Results, result => result.Outcome is DaggerfallCastOutcome.Applied or DaggerfallCastOutcome.Resisted);
        Assert.NotEqual(DaggerfallCastOutcome.Released, flight.Results[0].Outcome);
        Assert.True(session.State.Actors.Player.Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Health.Value)).Current <= healthBefore);
    }

    [Fact]
    public void Caster_only_enemy_spell_is_delivered_immediately_and_duplicate_is_suppressed()
    {
        using Fixture fixture = new();
        DaggerfallSession session = fixture.Session;
        WorldPoint playerPosition = session.State.PlayerControl.Position
            ?? throw new InvalidOperationException("The fixture player has no position.");
        long shaman = session.SpawnActor("orc-shaman", new ActorPose(playerPosition with { Z = playerPosition.Z - 1f }, 0f), level: 1);
        session.State.Actors.Get(shaman).Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Magicka.Value)).SetCurrent(1, clamp: true);
        fixture.Perception.Receipt = Receipt(new PerceptionPair(
            checked((ulong)shaman), checked((ulong)DaggerfallActorIdentity.PlayerEntityId), 1d, 1d,
            PerceptionPairKind.Visible, 1d));

        session.Update(new ProductUpdate(OuterUpdate(1), []));

        Assert.True(session.LastEnemySpell.TryGetValue(shaman, out DaggerfallEnemySpellEvidence? evidence));
        Assert.NotNull(evidence);
        Assert.Equal("spell.006", evidence.SpellKey);
        Assert.Equal(DaggerfallSpellTarget.CasterOnly, evidence.Target);
        Assert.Equal(DaggerfallCastOutcome.DeliveryCompleted, evidence.Outcome);
        Assert.Contains(session.State.Effects.Active, effect => effect.Context.Target.Value == checked((ulong)shaman));
        int activeEffects = session.State.Effects.Active.Count;

        session.Update(new ProductUpdate(OuterUpdate(2), []));

        Assert.Equal(activeEffects, session.State.Effects.Active.Count);
        Assert.True(session.LastEnemySpell.TryGetValue(shaman, out DaggerfallEnemySpellEvidence? repeated));
        Assert.Equal(evidence, repeated);
    }

    [Fact]
    public void Human_class_caster_uses_its_level_tier_through_the_same_enemy_decision()
    {
        using Fixture fixture = new();
        DaggerfallSession session = fixture.Session;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        int mobileId = definitions.EnemySpells.ClassCasters.Order().First();
        DaggerfallMobileDefinition mobile = definitions.Mobiles.Mobiles[mobileId];
        string actorKey = DaggerfallEncounterActors.ActorFor(mobile).Value;
        WorldPoint playerPosition = session.State.PlayerControl.Position
            ?? throw new InvalidOperationException("The fixture player has no position.");
        long caster = session.SpawnActor(actorKey, new ActorPose(playerPosition with { Z = playerPosition.Z - 10f }, 0f), level: 1);
        session.State.Actors.Get(caster).Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Magicka.Value)).SetCurrent(1, clamp: true);
        fixture.Perception.Receipt = Receipt(new PerceptionPair(
            checked((ulong)caster), checked((ulong)DaggerfallActorIdentity.PlayerEntityId), 10d, 1d,
            PerceptionPairKind.Visible, 1d));

        session.Update(new ProductUpdate(OuterUpdate(1), []));

        Assert.True(session.LastEnemySpell.TryGetValue(caster, out DaggerfallEnemySpellEvidence? evidence));
        Assert.NotNull(evidence);
        Assert.Equal(definitions.EnemySpells.ClassTiers[0][0], evidence.SpellKey);
        Assert.Equal(DaggerfallSpellTarget.SingleTargetAtRange, evidence.Target);
        Assert.Equal(DaggerfallCastOutcome.Released, evidence.Outcome);
        Assert.Single(session.Casting.PendingRangedFlights);
    }

    [Fact]
    public void Occluded_enemy_cannot_start_a_spell_release()
    {
        using Fixture fixture = new();
        DaggerfallSession session = fixture.Session;
        WorldPoint playerPosition = session.State.PlayerControl.Position
            ?? throw new InvalidOperationException("The fixture player has no position.");
        long imp = session.SpawnActor("imp", new ActorPose(playerPosition with { Z = playerPosition.Z - 10f }, 0f), level: 1);
        session.State.Actors.Get(imp).Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Magicka.Value)).SetCurrent(1, clamp: true);
        fixture.Perception.Receipt = Receipt(new PerceptionPair(
            checked((ulong)imp), checked((ulong)DaggerfallActorIdentity.PlayerEntityId), 10d, 0d,
            PerceptionPairKind.Occluded, 0d));

        session.Update(new ProductUpdate(OuterUpdate(1), []));

        Assert.DoesNotContain(imp, session.LastEnemySpell);
        Assert.Empty(session.Casting.PendingRangedFlights);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly DaggerfallSiteProfile inputs;
        private readonly List<string> releases = [];
        internal DaggerfallSession Session { get; }
        internal SpatialFake Spatial { get; }
        internal PerceptionFake Perception { get; }

        internal Fixture()
        {
            string root = TestData.RepositoryRoot;
            DaggerfallDefinitions definitions = TestPayload.Definitions;
            inputs = ReadInputs(root);
            ContentFake content = new(releases);
            PopulateContent(content, inputs);
            Spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
            Perception = PerceptionFake.Create();
            EngineContextFake engine = EngineContextFake.Create(content, Spatial.Service,
                new AppearanceFake(releases), Perception.Service);
            Session = DaggerfallSession.StartNew(engine.Context,
                new(definitions, inputs, DaggerfallTuning.Defaults));
        }

        public void Dispose() => Session.Dispose();
    }
}
