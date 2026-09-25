using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Combat;
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
        using DaggerCombatFixture fixture = new(sourceId);

        fixture.Rules.Apply(Attack(), Prepared(), fixture.Facts);

        List<IProductFact> delivered = fixture.Deliver();
        FatigueAppliedFact fatigue = Assert.Single(delivered.OfType<FatigueAppliedFact>());
        DamageAppliedFact health = Assert.Single(delivered.OfType<DamageAppliedFact>());
        Assert.Equal((2, DaggerfallActorIdentity.PlayerEntityId, 256, 256d),
            (fatigue.SourceActorId, fatigue.TargetActorId, fatigue.CalculatedFatigueLoss, fatigue.ActualFatigueLost));
        Assert.Equal((2, 2d), (health.CalculatedDamage, health.ActualHealthLost));
        Assert.Equal(344d, fixture.Stamina);
        Assert.Equal(98d, fixture.Health);

        DaggerfallStaminaRecoveryModule recovery = new(new DaggerfallStaminaRecoveryTuning(5d, 0d));
        recovery.Update(fixture.Actors.Player.Stats, 1d);
        Assert.Equal(349d, fixture.Stamina);
        DaggerfallStatsSave saved = DaggerfallStatsSaveBoundary.Capture(fixture.Actors.Player.Stats, fixture.Actors.Player.Actor.Entity);
        DaggerfallRestoredStats restored = DaggerfallStatsSaveBoundary.Restore(saved, new EntityId(DaggerfallActorIdentity.PlayerEntityId));
        Assert.Equal(349d, restored.Component.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Stamina.Value)).Current);
    }

    [Fact]
    public void Fatigue_loss_reports_the_bounded_live_loss_when_the_target_is_nearly_exhausted()
    {
        using DaggerCombatFixture fixture = new("nymph", playerStamina: 20d);

        fixture.Rules.Apply(Attack(), Prepared(), fixture.Facts);

        FatigueAppliedFact fatigue = Assert.Single(fixture.Deliver().OfType<FatigueAppliedFact>());
        Assert.Equal((256, 20d), (fatigue.CalculatedFatigueLoss, fatigue.ActualFatigueLost));
        Assert.Equal(0d, fixture.Stamina);
    }

    private static AttackRequest Attack() =>
        new(2, DaggerfallActorIdentity.PlayerEntityId, 7, 13, .125d, Delayed: true);

    private static PreparedAttack Prepared() =>
        new(.5d, new AttackOutcome(Hit: true, Allowed: true, Body: 0, Damage: 2, Roll: 1, Chance: 100));
}
