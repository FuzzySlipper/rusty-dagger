using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Kit.Combat;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed partial class DaggerfallEquipmentWearTests
{
    [Fact]
    public void Mace_artifact_cost_break_does_not_skip_the_victims_ordinary_armor_wear()
    {
        using WearFixture fixture = new(molagBalStrike: (_, _, _, _, _, _, _) => (1, 0));
        fixture.EquipEnemyMace(9510, 10);
        fixture.CombatRules.RegisterAction("enemy-class-equipped-melee", new AcceptedMaceDamage());
        int armor = fixture.Condition(1003);
        fixture.Script(body: 9, critical: 50, hit: 1, damage: 6, razorSave: 100);
        var facts = fixture.RunEnemyAttack().ToList();
        Assert.Equal(13, AppliedDamage(facts)); Assert.Equal(armor - 1, fixture.Condition(1003));
        Assert.Equal(0, fixture.Condition(9510));
        Assert.Single(facts.OfType<AttackHitFact>());
        var transfer = Assert.Single(facts.OfType<ArtifactResourceTransferredFact>());
        Assert.Equal(9510UL, transfer.SourceItemId);
        Assert.True(facts.FindIndex(fact => fact is EquipmentWornFact wear && wear.DurableItemId == 1003)
            < facts.IndexOf(transfer));
    }
    private sealed class AcceptedMaceDamage : ICombatContribution
    {
        public void Damage(DamageEvent interaction) => interaction.Damage = 13;
    }
}
