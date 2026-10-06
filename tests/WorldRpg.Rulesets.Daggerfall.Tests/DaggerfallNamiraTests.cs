using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Combat;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed partial class DaggerfallEquipmentWearTests
{
    [Theory]
    [InlineData("vermin", 0)]
    [InlineData("spriggans", 0)]
    [InlineData("bears", 0)]
    [InlineData("tigers", 0)]
    [InlineData("spiders", 0)]
    [InlineData("scorpions", 0)]
    [InlineData("daedra", 6)]
    [InlineData("undead", 26)]
    [InlineData("criminals", 13)]
    [InlineData("orcs", 13)]
    public void Namira_reflects_the_donor_team_amount_once_without_wearing_the_ring(string team, int expected)
    {
        using WearFixture f = new();
        f.EquipEnemyWeapon("iron-longsword", 9001);
        f.EquipNamira();
        f.ReplaceActor(Enemy, f.Definitions.RequireActor(new DaggerfallActorId("thief")) with { ActionId = "enemy-class-equipped-melee", Team = team });
        f.Script(body: 9, critical: 50, hit: 1, damage: 15);
        var facts = f.RunEnemyAttack();
        Assert.Equal(13, AppliedDamage(facts));
        Assert.Equal(200 - expected, f.EnemyActor.Get<StatsComponent>().GetTrack(TrackId.Parse("health")).Current);
        Assert.Equal(1500, f.Condition(9501));
        if (expected == 0) Assert.Empty(facts.OfType<ArtifactDamageReflectedFact>());
        else
        {
            var reflection = Assert.Single(facts.OfType<ArtifactDamageReflectedFact>());
            Assert.Equal((9501UL, expected, (double)expected), (reflection.SourceItemId, reflection.ReflectedDamage, reflection.ActualHealthLost));
            Assert.Equal(2, facts.OfType<DamageAppliedFact>().Count());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Namira_unequip_and_source_retirement_stop_reflection(bool destroy)
    {
        using WearFixture f = new();
        f.EquipEnemyWeapon("iron-longsword", 9001);
        f.EquipNamira();
        if (destroy) f.DestroyNamira(9501);
        else f.PlayerEquipment.Unequip(f.PlayerItem(9501));
        f.Script(9, 50, 1, 15);
        var facts = f.RunEnemyAttack();
        Assert.Empty(facts.OfType<ArtifactDamageReflectedFact>());
        Assert.Equal(200, f.EnemyActor.Get<StatsComponent>().GetTrack(TrackId.Parse("health")).Current);
    }

    [Fact]
    public void Namira_miss_reflects_nothing_and_two_rings_do_not_double_the_result()
    {
        using WearFixture f = new();
        f.EquipEnemyWeapon("iron-longsword", 9001);
        f.EquipNamira();
        f.EquipNamira(9502, "ring1");
        f.Script(9, 50, 99);
        Assert.Empty(f.RunEnemyAttack().OfType<ArtifactDamageReflectedFact>());
        Assert.Equal(1500, f.Condition(9501));
        f.Script(9, 50, 1, 15);
        Assert.Single(f.RunEnemyAttack(step: 100).OfType<ArtifactDamageReflectedFact>());
        Assert.Equal(1500, f.Condition(9501));
        Assert.Equal(1500, f.Condition(9502));
    }

    [Fact]
    public void Namira_respects_incoming_defenses_and_uses_the_shared_damage_application_for_reflection()
    {
        using WearFixture f = new();
        f.EquipEnemyWeapon("iron-longsword", 9001);
        f.EquipNamira();
        f.Player.Add(new CombatContributions());
        f.Player.Get<CombatContributions>().Rules.Add(new NamiraDefense());
        f.Script(9, 50, 1, 15, wornWeaponRoll: 21, wornArmourRoll: 21);
        var facts = f.RunEnemyAttack();
        var normal = Assert.Single(facts.OfType<AttackHitFact>());
        Assert.Equal(13, normal.CalculatedDamage);
        Assert.Equal(4, normal.ActualHealthLost);
        var reflection = Assert.Single(facts.OfType<ArtifactDamageReflectedFact>());
        Assert.Equal(4, reflection.ReflectedDamage);
        Assert.Equal(2, reflection.ActualHealthLost);
        Assert.Equal(1500, f.Condition(9501));
        Assert.Equal(198, f.EnemyActor.Get<StatsComponent>().GetTrack(TrackId.Parse("health")).Current);
    }

    [Fact]
    public void Namira_lethal_reflection_emits_one_ordered_death_and_leaves_a_worn_ring_whole()
    {
        using WearFixture f = new();
        f.EquipEnemyWeapon("iron-longsword", 9001);
        f.EquipNamira(condition: 5);
        f.EnemyActor.Get<StatsComponent>().GetTrack(TrackId.Parse("health")).SetCurrent(3);
        f.Script(9, 50, 1, 15);
        var facts = f.RunEnemyAttack().ToList();
        var death = Assert.Single(facts.OfType<ActorDiedFact>());
        Assert.Equal((Enemy, DaggerfallActorIdentity.PlayerEntityId, DaggerfallDamageCause.Effect, 3d), (death.ActorId, death.KillerId, death.Cause, death.ActualHealthLost));
        Assert.True(facts.FindIndex(fact => fact is AttackHitFact) < facts.FindIndex(fact => fact is ArtifactDamageReflectedFact));
        Assert.True(facts.FindIndex(fact => fact is ArtifactDamageReflectedFact) < facts.FindIndex(fact => fact is ActorDiedFact));
        Assert.Equal(5, f.Condition(9501));
        Assert.Contains(f.PlayerEquipment.Read().Assignments, assignment => assignment.Slot.Value == "ring0");
        Assert.DoesNotContain(facts.OfType<EquipmentWornFact>(), fact => fact.DurableItemId == 9501);
    }

    private sealed class NamiraDefense : ICombatContribution
    {
        public void Applying(ApplyHitEvent hit) => hit.Damage = hit.Participants.Cause == "artifact.namira" ? 2 : 4;
    }

    [Fact]
    public void Namira_does_not_reflect_into_an_attacker_removed_before_its_strike_lands()
    {
        using WearFixture f = new();
        f.EquipEnemyWeapon("iron-longsword", 9001);
        f.EquipNamira();
        f.Script(9, 50, 1, 15);
        var facts = f.RunEnemyAttack(retireBeforeImpact: true);
        Assert.Empty(facts.OfType<DamageAppliedFact>());
        Assert.Empty(facts.OfType<ArtifactDamageReflectedFact>());
        Assert.Equal(1500, f.Condition(9501));
    }
}
