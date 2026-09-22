using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Facts;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerCombatFatigueConsequencesTests
{
    [Theory]
    [InlineData("nymph")]
    [InlineData("lamia")]
    public void Retained_monster_fatigue_callers_apply_the_donor_formula_after_an_accepted_hit(string sourceId)
    {
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(RepositoryRoot(), "content/worldrpg/payloads/daggerfall.base.json")));
        DaggerfallActorDefinition playerDefinition = definitions.RequireActor(new DaggerfallActorId("player"));
        DaggerfallActorDefinition sourceDefinition = definitions.RequireActor(new DaggerfallActorId(sourceId));
        using ActorsState actors = new();
        actors.CreatePlayer(DaggerfallActorIdentity.PlayerEntityId, new EntityTypeId("player"), Stats(health: 100d, stamina: 600d), "health");
        actors.CreateActor(2, new EntityTypeId(sourceId), Stats(health: 100d, stamina: 600d), new ActorPose(new WorldPoint(1f, 0f, 0f), 0f), "health");
        DaggerCombatRules combat = new(
            null!, actors, null!, _ => null, new DaggerfallItemInstances(), definitions,
            new Dictionary<long, DaggerfallActorDefinition>
            {
                [DaggerfallActorIdentity.PlayerEntityId] = playerDefinition,
                [2] = sourceDefinition,
            },
            null!);
        FactBuffer<IProductFact> facts = new();

        combat.Apply(
            new AttackRequest(2, DaggerfallActorIdentity.PlayerEntityId, 7, 13, .125d, Delayed: true),
            new PreparedAttack(.5d, new AttackOutcome(Hit: true, Allowed: true, Body: 0, Damage: 2, Roll: 1, Chance: 100)),
            facts);

        List<IProductFact> delivered = [];
        facts.Deliver(delivered.Add);
        FatigueAppliedFact fatigue = Assert.Single(delivered.OfType<FatigueAppliedFact>());
        DamageAppliedFact health = Assert.Single(delivered.OfType<DamageAppliedFact>());
        Assert.Equal((2, DaggerfallActorIdentity.PlayerEntityId, 256, 256d),
            (fatigue.SourceActorId, fatigue.TargetActorId, fatigue.CalculatedFatigueLoss, fatigue.ActualFatigueLost));
        Assert.Equal((2, 2d), (health.CalculatedDamage, health.ActualHealthLost));
        Assert.Equal(344d, actors.Player.Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Stamina.Value)).Current);
        Assert.Equal(98d, actors.Player.Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Health.Value)).Current);

        DaggerfallStaminaRecoveryModule recovery = new(new DaggerfallStaminaRecoveryTuning(5d, 0d));
        recovery.Update(actors.Player.Stats, 1d);
        Assert.Equal(349d, actors.Player.Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Stamina.Value)).Current);
        DaggerfallStatsSave saved = DaggerfallStatsSaveBoundary.Capture(actors.Player.Stats, actors.Player.Actor.Entity);
        DaggerfallRestoredStats restored = DaggerfallStatsSaveBoundary.Restore(saved, new EntityId(DaggerfallActorIdentity.PlayerEntityId));
        Assert.Equal(349d, restored.Component.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Stamina.Value)).Current);
    }

    [Fact]
    public void Fatigue_loss_reports_the_bounded_live_loss_when_the_target_is_nearly_exhausted()
    {
        DaggerfallDefinitions definitions = DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(RepositoryRoot(), "content/worldrpg/payloads/daggerfall.base.json")));
        using ActorsState actors = new();
        actors.CreatePlayer(DaggerfallActorIdentity.PlayerEntityId, new EntityTypeId("player"), Stats(health: 100d, stamina: 20d), "health");
        actors.CreateActor(2, new EntityTypeId("nymph"), Stats(health: 100d, stamina: 600d), new ActorPose(new WorldPoint(1f, 0f, 0f), 0f), "health");
        DaggerCombatRules combat = new(
            null!, actors, null!, _ => null, new DaggerfallItemInstances(), definitions,
            new Dictionary<long, DaggerfallActorDefinition>
            {
                [DaggerfallActorIdentity.PlayerEntityId] = definitions.RequireActor(new DaggerfallActorId("player")),
                [2] = definitions.RequireActor(new DaggerfallActorId("nymph")),
            },
            null!);
        FactBuffer<IProductFact> facts = new();

        combat.Apply(new AttackRequest(2, DaggerfallActorIdentity.PlayerEntityId, 7, 13, .125d, Delayed: true),
            new PreparedAttack(.5d, new AttackOutcome(Hit: true, Allowed: true, Body: 0, Damage: 2, Roll: 1, Chance: 100)), facts);

        FatigueAppliedFact fatigue = Assert.Single(Deliver(facts).OfType<FatigueAppliedFact>());
        Assert.Equal((256, 20d), (fatigue.CalculatedFatigueLoss, fatigue.ActualFatigueLost));
        Assert.Equal(0d, actors.Player.Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Stamina.Value)).Current);
    }

    private static StatsComponent Stats(double health, double stamina)
    {
        Stat healthMaximum = new(1_000);
        Stat staminaMaximum = new(1_000);
        StatsComponent stats = new();
        stats.AddStat(StatId.Parse(DaggerfallMechanicsIds.HealthMaximum.Value), healthMaximum);
        stats.AddStat(StatId.Parse(DaggerfallMechanicsIds.StaminaMaximum.Value), staminaMaximum);
        stats.AddTrack(TrackId.Parse(DaggerfallMechanicsIds.Health.Value), new Track(healthMaximum, health, 0d));
        stats.AddTrack(TrackId.Parse(DaggerfallMechanicsIds.Stamina.Value), new Track(staminaMaximum, stamina, 0d));
        return stats;
    }

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md"))) return current.FullName;
        throw new InvalidOperationException("Could not locate the Rusty Dagger repository root.");
    }

    private static List<IProductFact> Deliver(FactBuffer<IProductFact> facts)
    {
        List<IProductFact> delivered = [];
        facts.Deliver(delivered.Add);
        return delivered;
    }
}
