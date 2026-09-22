using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Combat;
using WorldRpg.Kit.World;
using Xunit;

namespace WorldRpg.Kit.Tests;

public sealed class CombatResolutionTests
{
    [Fact]
    public void Participant_chance_bonus_changes_hit_without_reapplying_stat_modifiers()
    {
        using ActorsState actors = Actors();
        CombatContributions contributions = new();
        contributions.Rules.Add(new ChanceContribution());
        actors.Get(3).Actor.Add(contributions);
        CombatResolution resolution = new();
        TryHitEvent hit = resolution.TryHit(new(actors.Get(2).Actor, actors.Get(3).Actor, "melee"), value =>
        { value.Chance = 40; value.Roll = 50; });
        Assert.True(hit.Hit);
        Assert.Equal(60, hit.Chance);
    }
    private sealed class ChanceContribution : ICombatContribution
    {
        public void Hit(TryHitEvent interaction) => interaction.Chance += 20;
    }

    [Fact]
    public void Fixed_policy_and_participant_ward_keep_calculated_damage_distinct_from_applied_loss()
    {
        using ActorsState actors = Actors();
        ActorState attacker = actors.Get(2);
        ActorState target = actors.Get(3);
        CombatContributions ward = new();
        ward.Rules.Add(new ApplyingContribution(interaction => interaction.Damage -= 4));
        target.Actor.Add(ward);
        CombatResolution resolution = new();
        resolution.RegisterAction("fixed", new DamageContribution(interaction => interaction.Damage += 2));
        CombatParticipants participants = new(attacker.Actor, target.Actor, "fixed");

        TryHitEvent hit = resolution.TryHit(participants, interaction =>
        {
            interaction.Chance = 100;
            interaction.Roll = 1;
            interaction.Hit = true;
        });
        DamageEvent damage = resolution.Damage(participants, interaction =>
        {
            interaction.Body = 7;
            interaction.Damage = 10;
        });
        ApplyHitEvent applied = resolution.Apply(participants, damage.Damage, damage.Body, interaction =>
        {
            Track health = interaction.Participants.TargetStats.GetTrack(TrackId.Parse("health"));
            double before = health.Current;
            health.SetCurrent(before - interaction.Damage, clamp: true);
            interaction.ActualHealthLost = before - health.Current;
            interaction.Defeated = before > health.Minimum && health.Current <= health.Minimum;
        });

        Assert.True(hit.Hit);
        Assert.Equal(12, damage.Damage);
        Assert.Equal(8d, applied.ActualHealthLost);
        Assert.Equal(92, target.Stats.GetTrack(TrackId.Parse("health")).ValueInt);
        Assert.Equal(7, applied.Body);
    }

    [Fact]
    public void Canonical_health_application_retains_source_cause_actual_loss_and_one_defeat_transition()
    {
        using ActorsState actors = Actors();
        ActorState attacker = actors.Get(2);
        ActorState target = actors.Get(3);
        Track health = target.Stats.GetTrack(TrackId.Parse("health"));
        health.SetCurrent(3.75, clamp: true);
        CombatParticipants participants = new(attacker.Actor, target.Actor, "falling");
        CombatResolution resolution = new();

        ApplyHitEvent lethal = resolution.ApplyToHealth(participants, 10, 0, health);
        ApplyHitEvent repeated = resolution.ApplyToHealth(participants, 10, 0, health);

        Assert.Equal((10, 3.75d, true), (lethal.Result.CalculatedDamage, lethal.Result.ActualHealthLost, lethal.Result.Defeated));
        Assert.Same(attacker.Actor, lethal.Result.Source);
        Assert.Same(target.Actor, lethal.Result.Target);
        Assert.Equal("falling", lethal.Result.Cause);
        Assert.Equal((0d, false), (repeated.ActualHealthLost, repeated.Defeated));
        Assert.Equal(0, health.ValueInt);
    }

    [Fact]
    public void Canonical_health_application_preserves_fractional_current_values_and_the_minimum_bound()
    {
        using ActorsState actors = Actors();
        Track health = actors.Get(3).Stats.GetTrack(TrackId.Parse("health"));
        CombatParticipants participants = new(actors.Get(2).Actor, actors.Get(3).Actor, "hazard");
        CombatResolution resolution = new();

        health.SetCurrent(10.75, clamp: true);
        ApplyHitEvent ordinary = resolution.ApplyToHealth(participants, 1, 0, health);
        Assert.Equal(9.75d, health.Current);
        Assert.Equal(1d, ordinary.ActualHealthLost);
        Assert.False(ordinary.Defeated);

        health.SetCurrent(0.5d, clamp: true);
        ApplyHitEvent bounded = resolution.ApplyToHealth(participants, 1, 0, health);
        Assert.Equal(0d, health.Current);
        Assert.Equal(0.5d, bounded.ActualHealthLost);
        Assert.True(bounded.Defeated);
    }

    [Fact]
    public void A_source_that_is_also_the_target_contributes_once()
    {
        using ActorsState actors = Actors();
        ActorState target = actors.Get(3);
        CombatContributions ward = new();
        ward.Rules.Add(new ApplyingContribution(interaction => interaction.Damage -= 2));
        target.Actor.Add(ward);
        Track health = target.Stats.GetTrack(TrackId.Parse("health"));

        ApplyHitEvent applied = new CombatResolution().ApplyToHealth(new(target.Actor, target.Actor, "effect"), 5, 0, health);

        Assert.Equal(3d, applied.ActualHealthLost);
        Assert.Equal(97d, health.Current);
    }

    [Fact]
    public void A_multislot_equipped_item_contributes_once()
    {
        using ActorsState actors = Actors();
        ActorState attacker = actors.Get(2);
        ActorState target = actors.Get(3);
        InventoryStore inventory = new();
        inventory.RegisterInventory(new InventoryState(attacker.Actor.Entity));
        inventory.RegisterEquipment(new EquipmentState(attacker.Actor.Entity));
        EquipmentComponent equipment = new(inventory, attacker.Actor.Entity);
        attacker.Actor.Add(equipment);
        EntityId item = actors.Entities.Create(new DurableIdentityReference(DurableIdentityKind.Item, 99), new EntityTypeId("two-handed-ward"));
        CombatContributions contribution = new();
        contribution.Rules.Add(new DamageContribution(interaction => interaction.Damage += 5));
        actors.Store.Add(item, contribution);
        ItemDefinition definition = new(ItemDefinitionId.Parse("two-handed-ward"), ItemKind.Unique, 1,
            classifications: [ItemClassificationId.Parse("ward")], equipment: new ItemEquipmentPolicy(2));
        inventory.MaterializeUnique(new ItemState(item, definition), attacker.Actor.Entity);
        EquipmentSlotDefinition[] slots =
        [
            new(EquipmentSlotId.Parse("left"), [ItemClassificationId.Parse("ward")]),
            new(EquipmentSlotId.Parse("right"), [ItemClassificationId.Parse("ward")]),
        ];
        equipment.Equip(item, slots);
        CombatResolution resolution = new();

        DamageEvent damage = resolution.Damage(new CombatParticipants(attacker.Actor, target.Actor, "attack"), interaction => interaction.Damage = 10);

        Assert.Equal(15, damage.Damage);
    }

    private static ActorsState Actors()
    {
        ActorsState actors = new();
        actors.CreatePlayer(1, new EntityTypeId("player"), Stats(100), "health");
        actors.CreateActor(2, new EntityTypeId("attacker"), Stats(100), new ActorPose(new WorldRpg.Kit.Controls.WorldPoint(0, 0, 0), 0), "health");
        actors.CreateActor(3, new EntityTypeId("target"), Stats(100), new ActorPose(new WorldRpg.Kit.Controls.WorldPoint(1, 0, 0), 0), "health");
        return actors;
    }

    private static StatsComponent Stats(int health)
    {
        Stat maximum = new(health);
        StatsComponent stats = new();
        stats.AddStat(StatId.Parse("health-maximum"), maximum);
        stats.AddTrack(TrackId.Parse("health"), new Track(maximum, health));
        return stats;
    }

    private sealed class DamageContribution(Action<DamageEvent> apply) : ICombatContribution
    {
        public void Damage(DamageEvent interaction) => apply(interaction);
    }

    private sealed class ApplyingContribution(Action<ApplyHitEvent> apply) : ICombatContribution
    {
        public void Applying(ApplyHitEvent interaction) => apply(interaction);
    }
}
