using System.Numerics;
using System.Reflection;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Facts;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.Policies;
using Xunit;
using SlotId = WorldRpg.Kit.Inventory.EquipmentSlotId;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The weapon, unarmed and enemy attack-set damage policy at the rules boundary the player swing
/// and the AI attack capability actually enter: an admitted attack whose blow connects with its
/// target at the hit frame. The scripted random records each keyed draw's requested range and salt
/// in order, so they are the evidence that the draw discipline — gates that skip their roll,
/// reflex-gated slots, backstab rolls only above level one — survived into the product rather than
/// being approximated by a flat roll.
/// </summary>
public sealed class DaggerCombatDamagePolicyTests
{
    [Fact]
    public void An_armed_class_enemy_strikes_with_its_equipped_weapon_even_when_its_fists_would_hit_harder()
    {
        // DEC-03: classic equipped-enemy semantics. A high-skill brigand's unarmed swing would
        // roll harder than its iron longsword (11-21 versus 2-16); DFU's stronger-unarmed fallback
        // must not land, so the damage draw stays inside the weapon's authored range.
        using DamagePolicyFixture fixture = new();
        fixture.NpcHandToHandSkill(100);
        fixture.NpcStrength(50);
        fixture.EquipNpcWeapon(2, "iron-longsword", 9001);
        fixture.Script(body: BareFeet, damage: 7);

        BlowResolution result = fixture.Run(new AttackRequest(2, DaggerfallActorIdentity.PlayerEntityId, 5, 9, .125d, Delayed: true));

        Assert.True(result.Admitted);
        Assert.True(result.Outcome.Hit);
        Assert.True(result.Outcome.Allowed);
        Assert.Equal(6, result.Outcome.Damage); // 7 (weapon roll) - 1 (iron) + 0 (strength 50)
        Assert.Equal((2, 16), Assert.Single(result.Ranges, range => range != (0, 19) && range != (1, 100)));
    }

    [Fact]
    public void A_variants_physical_mitigation_owner_replaces_the_default_mitigation_for_the_blow()
    {
        // The same armed brigand swing as above, under a whole-owner mitigation seam that absorbs two and
        // neither deflects nor applies the default material requirement.
        RecordingMitigation absorbing = new(absorb: 2);
        using DamagePolicyFixture fixture = new(mitigation: absorbing);
        fixture.NpcStrength(50);
        fixture.EquipNpcWeapon(2, "iron-longsword", 9001);
        fixture.Script(body: BareFeet, deflect: null, damage: 7);

        BlowResolution result = fixture.Run(new AttackRequest(2, DaggerfallActorIdentity.PlayerEntityId, 5, 9, .125d, Delayed: true));

        Assert.Equal((true, true, 4), (result.Outcome.Hit, result.Outcome.Allowed, result.Outcome.Damage));
        DaggerfallPhysicalHarm harm = Assert.Single(absorbing.Mitigated);
        Assert.Equal((2L, DaggerfallActorIdentity.PlayerEntityId, "long-blade", "iron"), (harm.AttackerId, harm.TargetId, harm.Skill, harm.WeaponMaterial));
        Assert.Equal(harm, Assert.Single(absorbing.Negated));

        using DamagePolicyFixture negated = new(mitigation: new RecordingMitigation(negate: true));
        negated.EquipNpcWeapon(2, "iron-longsword", 9001);
        negated.Script(body: BareFeet, deflect: null);
        BlowResolution turned = negated.Run(new AttackRequest(2, DaggerfallActorIdentity.PlayerEntityId, 5, 9, .125d, Delayed: true));
        Assert.Equal((true, false, 0), (turned.Outcome.Hit, turned.Outcome.Allowed, turned.Outcome.Damage));
        Assert.DoesNotContain(turned.Ranges, range => range == (2, 16)); // A negated harm is never rolled.
    }

    [Fact]
    public void A_delivery_owner_deciding_contact_at_the_hit_frame_receives_an_untargeted_swing_there()
    {
        RecordingDelivery delivery = new();
        using DamagePolicyFixture fixture = new(delivery: delivery.Bind);
        AttackRequest swing = new(DaggerfallActorIdentity.PlayerEntityId, null, 3, 5, .125d, Delayed: true);

        IReadOnlyList<IProductFact> started = fixture.StartSwing(swing, out bool admitted);

        Assert.True(admitted);
        PlayerAttackStartedFact announced = Assert.Single(started.OfType<PlayerAttackStartedFact>());
        Assert.Null(announced.TargetId);
        DaggerfallAdmittedAttack prepared = Assert.Single(delivery.Prepared);
        Assert.Equal(swing, prepared.Request);
        Assert.False(prepared.Ranged);
        Assert.False(prepared.EnemyAttack);
        Assert.Empty(delivery.Applied);
        Assert.DoesNotContain(started, fact => fact is AttackRejectedFact);

        fixture.DeliverImpact(swing);
        Assert.Equal([swing], delivery.Applied);
        Assert.NotNull(delivery.Operations); // Bind asserts the owner is built once, over the shared operations.
    }

    private sealed class RecordingDelivery : IDaggerfallPhysicalDelivery
    {
        internal IDaggerfallPhysicalHarmOperations? Operations { get; private set; }
        internal List<DaggerfallAdmittedAttack> Prepared { get; } = [];
        internal List<AttackRequest> Applied { get; } = [];
        internal IDaggerfallPhysicalDelivery Bind(IDaggerfallPhysicalHarmOperations operations)
        {
            Assert.Null(Operations);
            Operations = operations;
            return this;
        }
        public bool HoldsFire(long archer, long target) => false;
        public void Interrupt(long attacker, ulong generation) { }
        public bool TryPrepare(DaggerfallAdmittedAttack admitted, FactBuffer<IProductFact> facts, out DaggerCombatRules.DaggerfallPreparedAttack prepared)
        {
            Prepared.Add(admitted);
            prepared = admitted.Prepared(new AttackOutcome(false, true, 0));
            return true;
        }
        public void Apply(AttackRequest request, DaggerCombatRules.DaggerfallPreparedAttack attack, FactBuffer<IProductFact> facts) => Applied.Add(request);
        public bool DeferImpact(DeferredAttackImpact impact, FactBuffer<IProductFact> facts) => false;
        public void AdvanceFlight(ulong generation, ulong simulationStep, double fixedDeltaSeconds,
            IReadOnlyDictionary<long, WorldPoint> positions, FactBuffer<IProductFact> facts) { }
        public IReadOnlyList<DaggerfallRangedFlightView> ReadFlights(ulong generation, ulong simulationStep) => [];
        public bool HasPlayerFlight => false;
        public bool HoldsImpactAt(long target) => false;
        public void LandDueImpacts(ulong generation, ulong simulationStep, FactBuffer<IProductFact> facts) { }
        public void ClearFlight() { }
        public void RebaseFlight(System.Numerics.Vector3 delta) { }
    }

    private sealed class RecordingMitigation(int absorb = 0, bool negate = false) : IDaggerfallPhysicalMitigation
    {
        internal List<DaggerfallPhysicalHarm> Negated { get; } = [];
        internal List<DaggerfallPhysicalHarm> Mitigated { get; } = [];
        public void Negate(NegationEvent negation, DaggerfallPhysicalHarm harm)
        {
            Negated.Add(harm);
            if (negate) negation.Negation = HarmNegation.Deflected;
        }
        public void Mitigate(MitigationEvent mitigation, DaggerfallPhysicalHarm harm)
        {
            Mitigated.Add(harm);
            mitigation.Absorbed += absorb;
        }
    }

    [Fact]
    public void An_unarmed_monster_fights_with_its_authored_attack_set_and_every_slot_rolls_its_own_gate()
    {
        using DamagePolicyFixture fixture = new();
        fixture.ReplaceActor(2, Definitions.RequireActor(new DaggerfallActorId("rat")) with
        {
            Attacks = [new DaggerfallAttackRange(1, 8), new(1, 8), new(1, 10)],
        });
        // Each slot independently passes its reflex gate before drawing damage and its consequence.
        fixture.ScriptMonster(0, (50, 4, 100), (50, 5, 100), (50, 6, 100));

        BlowResolution result = fixture.Run(new AttackRequest(2, DaggerfallActorIdentity.PlayerEntityId, 5, 9, .125d, Delayed: true));

        Assert.True(result.Outcome.Hit);
        Assert.True(result.Outcome.Allowed);
        Assert.Equal(15, result.Outcome.Damage);
        Assert.Equal(
        [
            (0, 19),
            (1, 100), (1, 8), (1, 100),
            (1, 100), (1, 8), (1, 100),
            (1, 100), (1, 10), (1, 100),
            (1, 100), // A natural blow is blunt: its deflection is drawn on the rolled damage.
        ], result.Ranges);
    }

    [Fact]
    public void A_slot_the_targets_reflexes_evade_never_reaches_its_damage_roll()
    {
        using DamagePolicyFixture fixture = new();
        fixture.ReplaceActor(2, Definitions.RequireActor(new DaggerfallActorId("rat")) with
        {
            Attacks = [new DaggerfallAttackRange(1, 8), new(1, 8), new(1, 10)],
        });
        // Slot 1's reflex roll (51) beats the 50 gate; the recorded sequence jumps from that roll
        // straight to slot 2's reflex roll with no (1, 8) damage draw in between.
        fixture.ScriptMonster(0, (50, 4, 100), (51, null, null), (50, 6, 100));

        BlowResolution result = fixture.Run(new AttackRequest(2, DaggerfallActorIdentity.PlayerEntityId, 5, 9, .125d, Delayed: true));

        Assert.Equal(10, result.Outcome.Damage);
        // Slot 0's damage (1, 8) is the only (1, 8) draw: slot 1 went from its reflex roll straight
        // to slot 2's, and slot 2's later (1, 10) keeps the ranges distinguishable.
        Assert.Equal(
        [
            (0, 19),
            (1, 100), (1, 8), (1, 100),
            (1, 100),
            (1, 100), (1, 10), (1, 100),
            (1, 100),
        ], result.Ranges);
    }

    [Fact]
    public void A_slot_its_reflexes_evade_or_with_no_authored_minimum_adds_no_damage_and_no_career_bonus()
    {
        using DamagePolicyFixture fixture = new();
        // The vampire's classic enemy configuration carries the humanoid bonus (bit 0x04), so each
        // landed natural-attack slot carries the attacker's level (19) on top of its damage.
        fixture.ReplaceActor(2, Definitions.RequireActor(new DaggerfallActorId("vampire")) with
        {
            ActionId = "monster-strike",
            Attacks = [new DaggerfallAttackRange(1, 3), new(0, 0), new(1, 3)],
        });
        // Slot 1 has no authored minimum and stops after its reflex draw; slot 2's reflex roll (51)
        // evades it before any damage.
        fixture.ScriptMonster(0, (50, 3, 1_000_000), (50, null, null), (51, null, null));

        BlowResolution result = fixture.Run(new AttackRequest(2, DaggerfallActorIdentity.PlayerEntityId, 5, 9, .125d, Delayed: true));

        Assert.Equal(22, result.Outcome.Damage); // (3 + 19) from slot 0 alone
        Assert.Equal(
        [
            (0, 19),
            (1, 100), (1, 3), (0, 1_000_000),
            (1, 100), // The zero-minimum slot still draws reflexes.
            (1, 100),
            (1, 100),
        ], result.Ranges);
    }

    [Fact]
    public void A_weapon_below_the_target_material_immunity_connects_but_deals_no_damage()
    {
        // The material requirement is settled before deflection and the damage roll, so an immune
        // connect never fabricates a damage draw and never reports a scratch.
        using DamagePolicyFixture fixture = new();
        fixture.ReplaceActor(2, Definitions.RequireActor(new DaggerfallActorId("rat")) with { MinimumMaterial = "silver" });
        fixture.Script(body: 0, deflect: null);

        BlowResolution result = fixture.Run(new AttackRequest(DaggerfallActorIdentity.PlayerEntityId, 2, 1, 1, .125d, Delayed: false), out IReadOnlyList<IProductFact> facts);

        Assert.True(result.Admitted);
        Assert.True(result.Outcome.Hit);
        Assert.False(result.Outcome.Allowed);
        Assert.Equal(0, result.Outcome.Damage);
        Assert.Contains(new AttackRejectedFact(AttackRejection.InsufficientWeaponMaterial), facts);
        Assert.Equal([(0, 19)], result.Ranges);
    }

    [Theory]
    [InlineData(0, 20)]   // 13 * 1.5
    [InlineData(100, 39)] // 13 * 3
    public void An_unaware_target_always_takes_the_backstab_its_skill_scales(int skill, int damage)
    {
        using DamagePolicyFixture fixture = new();
        fixture.PlayerStrength(50);
        fixture.PlayerBackstabbingSkill(skill);
        fixture.Unaware.Add(2);
        fixture.Script(body: 0, damage: 11);

        BlowResolution result = fixture.Run(new AttackRequest(DaggerfallActorIdentity.PlayerEntityId, 2, 1, 1, .125d, Delayed: false));

        Assert.Equal(damage, result.Outcome.Damage);
        Assert.Contains(fixture.SkillUses, use => use.Skill == "backstabbing");
        Assert.Contains(2L, fixture.Alerted);
    }

    [Fact]
    public void A_target_that_noticed_the_player_takes_no_backstab_from_any_side()
    {
        using DamagePolicyFixture fixture = new();
        fixture.PlayerStrength(50);
        fixture.PlayerBackstabbingSkill(100);
        fixture.Script(body: 0, damage: 11);

        BlowResolution result = fixture.Run(new AttackRequest(DaggerfallActorIdentity.PlayerEntityId, 2, 1, 1, .125d, Delayed: false));

        Assert.Equal(13, result.Outcome.Damage);
        Assert.DoesNotContain(fixture.SkillUses, use => use.Skill == "backstabbing");
    }

    [Fact]
    public void A_deflected_blow_alerts_its_target_and_trains_no_backstabbing()
    {
        using DamagePolicyFixture fixture = new();
        fixture.Unaware.Add(2);
        fixture.Script(body: 0, deflect: 1);

        BlowResolution result = fixture.Run(new AttackRequest(DaggerfallActorIdentity.PlayerEntityId, 2, 1, 1, .125d, Delayed: false));

        Assert.Equal(0, result.Outcome.Damage);
        Assert.Contains(2L, fixture.Alerted);
        Assert.DoesNotContain(fixture.SkillUses, use => use.Skill == "backstabbing");
    }

    [Fact]
    public void A_weak_swing_below_the_damage_floor_resolves_to_zero_damage()
    {
        using DamagePolicyFixture fixture = new();
        fixture.PlayerStrength(30); // DamageModifier (30 - 50) / 5 = -4
        fixture.Script(body: 0, damage: 2);

        BlowResolution result = fixture.Run(new AttackRequest(DaggerfallActorIdentity.PlayerEntityId, 2, 1, 1, .125d, Delayed: false));

        Assert.True(result.Outcome.Hit);
        Assert.True(result.Outcome.Allowed);
        Assert.Equal(0, result.Outcome.Damage); // 2 - 1 iron - 4 strength floors at zero
    }

    [Fact]
    public void A_monsters_first_evaded_slot_does_not_cancel_later_slots()
    {
        using DamagePolicyFixture fixture = new();
        fixture.ReplaceActor(2, Definitions.RequireActor(new DaggerfallActorId("rat")) with
        {
            Attacks = [new DaggerfallAttackRange(1, 8), new(1, 8), new(1, 10)],
        });
        fixture.ScriptMonster(0, (100, null, null), (1, 5, 100), (1, 6, 100));
        BlowResolution result = fixture.Run(new AttackRequest(2, DaggerfallActorIdentity.PlayerEntityId, 5, 9, .125d, Delayed: true));

        Assert.True(result.Outcome.Hit);
        Assert.Equal(11, result.Outcome.Damage);
        Assert.Equal(0, result.Body);
    }

    [Fact]
    public void A_monster_attack_the_target_negates_rolls_no_slot_damage_or_consequence()
    {
        using DamagePolicyFixture fixture = new();
        fixture.ReplaceActor(2, Definitions.RequireActor(new DaggerfallActorId("rat")) with
        {
            Attacks = [new DaggerfallAttackRange(1, 8), new(1, 8), new(1, 10)],
        });
        List<string> order = [];
        fixture.Contribute(new BluntImmunity(order));
        // Contact and the body draw come first; an immune target leaves nothing else to draw.
        fixture.Script(body: 0, deflect: null);

        BlowResolution result = fixture.Run(new AttackRequest(2, DaggerfallActorIdentity.PlayerEntityId, 5, 9, .125d, Delayed: true));

        Assert.Equal((false, 0), (result.Outcome.Allowed, result.Outcome.Damage));
        Assert.Equal([(0, 19)], result.Ranges);
        Assert.Equal(["contact", "negate"], order);
    }

    private sealed class BluntImmunity(List<string> order) : ICombatContribution
    {
        public void Contact(ContactEvent interaction) => order.Add("contact");
        public void Negate(NegationEvent interaction)
        {
            order.Add("negate");
            if (interaction.Channel == DaggerfallHarmChannels.Blunt) interaction.Negation = HarmNegation.Immune;
        }
        public void Damage(DamageEvent interaction) => order.Add("damage");
    }

    [Fact]
    public void A_monster_whose_slots_are_all_evaded_connects_for_no_damage()
    {
        using DamagePolicyFixture fixture = new();
        fixture.ReplaceActor(2, Definitions.RequireActor(new DaggerfallActorId("rat")) with
        {
            Attacks = [new DaggerfallAttackRange(1, 8), new(1, 8), new(1, 10)],
        });
        fixture.ScriptMonster(0, (100, null, null), (100, null, null), (100, null, null));
        CountingContribution contribution = new();
        fixture.Contribute(contribution);
        BlowResolution result = fixture.Run(new AttackRequest(2, DaggerfallActorIdentity.PlayerEntityId, 5, 9, .125d, Delayed: true));

        Assert.True(result.Admitted);
        Assert.True(result.Outcome.Hit);
        Assert.Equal(0, result.Outcome.Damage);
        Assert.DoesNotContain(result.Ranges, range => range is (1, 8) or (1, 10));
    }

    [Fact]
    public void A_monsters_combined_slot_damage_is_resolved_once()
    {
        using DamagePolicyFixture fixture = new();
        fixture.ReplaceActor(2, Definitions.RequireActor(new DaggerfallActorId("rat")) with
        {
            Attacks = [new DaggerfallAttackRange(1, 8), new(1, 8), new(1, 10)],
        });
        CountingContribution contribution = new();
        fixture.Contribute(contribution);
        fixture.ScriptMonster(0, (100, null, null), (1, 5, 100), (1, 6, 100));
        BlowResolution result = fixture.Run(new AttackRequest(2, DaggerfallActorIdentity.PlayerEntityId, 5, 9, .125d, Delayed: true));

        Assert.True(result.Outcome.Hit);
        Assert.Equal(11, result.Outcome.Damage);
        Assert.Equal(1, contribution.DamageCount);
    }

    [Theory]
    [InlineData("redguard", false, 0)]
    [InlineData("dark-elf", false, 0)]
    [InlineData("redguard", true, 4)]
    [InlineData("dark-elf", true, 3)]
    public void Racial_weapon_bonuses_require_an_equipped_weapon(string race, bool armed, int bonus)
    {
        BlowResolution Attack(string selectedRace)
        {
            using DamagePolicyFixture fixture = new(armed);
            fixture.PlayerRace(selectedRace, 12);
            // An armed blow is edged and draws deflection before damage; a fist is blunt and draws it after.
            if (armed) fixture.Script(body: 0, damage: 3);
            else fixture.Script(body: 0, deflect: null, damage: 3, afterDamage: 100);
            return fixture.Run(new AttackRequest(DaggerfallActorIdentity.PlayerEntityId, 2, 1, 1, .125d, Delayed: false));
        }

        BlowResolution baseline = Attack("breton");
        BlowResolution racial = Attack(race);
        Assert.True(racial.Outcome.Hit);
        Assert.Equal(baseline.Outcome.Damage + bonus, racial.Outcome.Damage);
    }

    [Fact]
    public void A_targeted_player_swing_lands_only_when_its_impact_arrives()
    {
        // A targeted swing hands its impact to the shared pending state; the attack clock delivers it.
        using DamagePolicyFixture fixture = new();
        fixture.Script(body: 9, damage: 10);
        AttackRequest request = new(DaggerfallActorIdentity.PlayerEntityId, 2, 5, 9, .125d, Delayed: true);

        IReadOnlyList<IProductFact> admitted = fixture.StartSwing(request, out bool started);
        fixture.Swept(2);

        Assert.True(started);
        PlayerAttackStartedFact opening = Assert.Single(admitted.OfType<PlayerAttackStartedFact>());
        Assert.Equal(2L, opening.TargetId);
        Assert.DoesNotContain(admitted, fact => fact is AttackHitFact or AttackWhiffedFact);

        IReadOnlyList<IProductFact> impact = fixture.DeliverImpact(request);

        // One admitted swing delivers one impact, however many animation frames follow it.
        Assert.Equal(2L, Assert.Single(impact.OfType<AttackHitFact>()).TargetId);
        Assert.Empty(fixture.DeliverImpact(request));
    }

    [Fact]
    public void A_swing_whose_animation_never_reaches_its_hit_frame_delivers_nothing()
    {
        using DamagePolicyFixture fixture = new();
        AttackRequest request = new(DaggerfallActorIdentity.PlayerEntityId, 2, 5, 9, .125d, Delayed: true);
        _ = fixture.StartSwing(request, out bool started);
        fixture.Swept(2);
        Assert.True(started);

        IReadOnlyList<IProductFact> impact = fixture.DeliverImpact(request, expired: true);

        Assert.DoesNotContain(impact, fact => fact is AttackHitFact or AttackWhiffedFact or EquipmentWornFact);
        // A delivered or expired swing leaves nothing pending, so the next admitted swing is ready.
        Assert.True(fixture.IsSwingReady(DaggerfallActorIdentity.PlayerEntityId, 5, 40));
    }

    [Fact]
    public void A_swing_cancelled_after_its_sweep_met_someone_leaves_nothing_for_the_next_swing_to_land_on()
    {
        using DamagePolicyFixture fixture = new();
        fixture.StartSwing(new AttackRequest(DaggerfallActorIdentity.PlayerEntityId, null, 5, 9, .125d, Delayed: true), out _);
        fixture.Swept(2);
        fixture.CancelSwing(DaggerfallActorIdentity.PlayerEntityId, 5);

        // The next swing's sweep meets nothing, wherever the last one's body now stands.
        AttackRequest next = new(DaggerfallActorIdentity.PlayerEntityId, null, 5, 500, .125d, Delayed: true);
        fixture.StartSwing(next, out bool started);
        Assert.True(started);
        fixture.Script(body: 0, damage: 5);
        IReadOnlyList<IProductFact> impact = fixture.DeliverImpact(next);

        Assert.Empty(impact.OfType<AttackHitFact>());
        Assert.Single(impact.OfType<AttackWhiffedFact>());
    }

    [Fact]
    public void An_undelayed_unaimed_swing_resolves_in_its_own_update_and_reports_it_met_nothing()
    {
        // With no animation to wait for, the swing's volume is queried in its own update: it spends its
        // stamina once and says what it met.
        using DamagePolicyFixture fixture = new();
        AttackRequest request = new(DaggerfallActorIdentity.PlayerEntityId, null, 5, 9, .125d, Delayed: false);

        IReadOnlyList<IProductFact> facts = fixture.StartSwing(request, out bool started);

        Assert.True(started);
        Assert.Equal(new PlayerAttackStartedFact(5, 9) { Feedback = new(true, "sound.347") },
            Assert.Single(facts.OfType<PlayerAttackStartedFact>()));
        Assert.Single(facts.OfType<AttackWhiffedFact>());
    }

    [Fact]
    public void A_cancelled_swing_never_lands_and_keeps_the_cadence_it_already_spent()
    {
        // One admitted swing latches its cooldown at admission. When the swing is cancelled before its
        // impact frame — leaving reach, losing sight, dying or unloading — only the damage is cancelled;
        // the charge stands, and the next swing waits for the authored cadence like any other.
        using DamagePolicyFixture fixture = new();
        fixture.EquipNpcWeapon(2, "iron-longsword", 9001);
        fixture.Touches(DaggerfallActorIdentity.PlayerEntityId);
        AttackRequest request = new(2, DaggerfallActorIdentity.PlayerEntityId, 5, 9, .125d, Delayed: true);

        IReadOnlyList<IProductFact> admitted = fixture.StartSwing(request, out bool started);
        Assert.True(started);
        Assert.DoesNotContain(admitted, fact => fact is AttackHitFact or AttackWhiffedFact);
        Assert.False(fixture.IsSwingReady(2, 5, 500));

        fixture.CancelSwing(2, 5);

        Assert.False(fixture.IsSwingReady(2, 5, 9));      // the cancellation costs the charge
        Assert.True(fixture.IsSwingReady(2, 5, 500));      // and the charge elapses on its authored cadence
        Assert.Empty(fixture.DeliverImpact(request).Where(fact => fact is AttackHitFact or AttackWhiffedFact));
    }

    [Fact]
    public void A_player_bow_shot_draws_one_arrow_and_pays_the_donors_bow_cooldown()
    {
        // The bow's cadence is FORM-04.GetBowCooldownTime over live speed rather than an authored
        // number, the shot is refused before admission when the quiver is empty, and the released
        // arrow travels through the same ranged delivery the archer uses.
        using DamagePolicyFixture fixture = new(armed: false);
        fixture.EquipPlayerBow("iron-short-bow", 3001);
        fixture.GivePlayerArrows(3);
        fixture.Script(body: 9, damage: 10);
        fixture.ArrowsMeet(2);
        AttackRequest request = new(DaggerfallActorIdentity.PlayerEntityId, 2, 5, 9, .125d, Delayed: true);

        IReadOnlyList<IProductFact> admitted = fixture.StartSwing(request, out bool started);

        Assert.True(started);
        PlayerAttackStartedFact opening = Assert.Single(admitted.OfType<PlayerAttackStartedFact>());
        Assert.Equal(2L, opening.TargetId);
        Assert.Equal(2UL, fixture.PlayerArrows());
        // (10 * (100 - 50) + 800) / 980 seconds, latched in 0.125s steps.
        Assert.Equal((ulong)Math.Ceiling((10d * (100 - 50) + 800) / 980d / .125d), fixture.CooldownRemaining(DaggerfallActorIdentity.PlayerEntityId, 5, 9));
        Assert.DoesNotContain(admitted, fact => fact is AttackHitFact or AttackWhiffedFact);

        // The release is queued for flight, and the arrival is what lands the hit.
        Assert.Empty(fixture.DeliverImpact(request));
        IReadOnlyList<IProductFact> arrived = fixture.AdvanceFlight(5, 10);
        Assert.Single(arrived.OfType<AttackHitFact>());
    }

    [Fact]
    public void Bad_reactions_reduce_the_players_critical_chance()
    {
        // A held enchantment's attack-chance shift moves the player's Critical Strike chance. At skill 50
        // the chance is 30, so a draw of 30 is critical until a shift of -10 lowers the chance to 20.
        int Blow(int shift)
        {
            using DamagePolicyFixture fixture = new(attackChanceShift: shift);
            fixture.PlayerStrength(50);
            fixture.PlayerCriticalStrikeSkill(50);
            fixture.SelectPlayerAttack(DaggerfallAttackIds.PowerOut);
            fixture.Script(body: 0, damage: 11, critical: 30);
            return fixture.Run(new AttackRequest(DaggerfallActorIdentity.PlayerEntityId, 2, 5, 9, .125d, Delayed: true)).Outcome.Damage;
        }

        Assert.Equal(52, Blow(0));   // 13 * 2 power * 2 critical
        Assert.Equal(26, Blow(-10));
    }

    [Theory]
    [InlineData("fastA", false, 13)]
    [InlineData("fastB", true, 26)]
    public void Only_a_chain_finisher_or_a_power_release_can_be_critical(string attack, bool finisher, int damage)
    {
        using DamagePolicyFixture fixture = new();
        fixture.PlayerStrength(50);
        fixture.PlayerCriticalStrikeSkill(100);
        fixture.SelectPlayerAttack(attack, finisher);
        fixture.Script(body: 0, damage: 11, critical: finisher ? 1 : null);

        BlowResolution result = fixture.Run(new AttackRequest(DaggerfallActorIdentity.PlayerEntityId, 2, 5, 9, .125d, Delayed: true));

        Assert.Equal(damage, result.Outcome.Damage);
        Assert.Equal(finisher, result.Salts.Contains(DaggerfallContactDelivery.CriticalSalt));
    }

    [Fact]
    public void Luck_above_average_raises_the_critical_chance()
    {
        int Blow(int luck)
        {
            using DamagePolicyFixture fixture = new();
            fixture.PlayerStrength(50);
            fixture.PlayerStat(DaggerfallMechanicsIds.Luck.Value, luck);
            fixture.PlayerCriticalStrikeSkill(50);
            fixture.SelectPlayerAttack("fastB", finisher: true);
            fixture.Script(body: 0, damage: 11, critical: 32);
            return fixture.Run(new AttackRequest(DaggerfallActorIdentity.PlayerEntityId, 2, 5, 9, .125d, Delayed: true)).Outcome.Damage;
        }

        Assert.Equal(13, Blow(50));
        Assert.Equal(26, Blow(70));
    }

    [Fact]
    public void An_enemys_blow_is_never_critical()
    {
        using DamagePolicyFixture fixture = new();
        fixture.NpcCriticalStrikeSkill(100);
        fixture.NpcStrength(50);
        fixture.EquipNpcWeapon(2, "iron-longsword", 9001);
        fixture.Script(body: BareFeet, damage: 11);

        BlowResolution result = fixture.Run(new AttackRequest(2, DaggerfallActorIdentity.PlayerEntityId, 5, 9, .125d, Delayed: true));

        Assert.DoesNotContain(DaggerfallContactDelivery.CriticalSalt, result.Salts);
    }

    [Fact]
    public void A_winded_character_swings_weaker_but_has_no_power_release_or_kick()
    {
        DaggerfallCombatantTuning tuning = DaggerfallAttackTuning.Default.Combatant;
        foreach (string refused in new[] { DaggerfallAttackIds.PowerOut, DaggerfallAttackIds.Kick })
        {
            using DamagePolicyFixture fixture = new();
            fixture.SetPlayerStamina(tuning.WindedStamina);
            fixture.SelectPlayerAttack(refused);
            _ = fixture.StartSwing(new AttackRequest(DaggerfallActorIdentity.PlayerEntityId, 2, 1, 1, .125d, Delayed: false), out bool admitted);
            Assert.False(admitted);
        }

        using DamagePolicyFixture winded = new();
        winded.PlayerStrength(50);
        winded.SetPlayerStamina(tuning.WindedStamina);
        winded.Script(body: 0, damage: 11);
        Assert.Equal(7, winded.Run(new AttackRequest(DaggerfallActorIdentity.PlayerEntityId, 2, 1, 1, .125d, Delayed: false)).Outcome.Damage); // 13 * .5
    }

    [Fact]
    public void Fighting_never_empties_the_stamina_pool()
    {
        using DamagePolicyFixture fixture = new();
        fixture.SetPlayerStamina(10d);
        fixture.Script(body: 0, damage: 11);

        _ = fixture.Run(new AttackRequest(DaggerfallActorIdentity.PlayerEntityId, 2, 1, 1, .125d, Delayed: false));

        Assert.Equal(1d, fixture.PlayerStamina);
    }

    [Theory]
    [InlineData(false, 16)] // 13 * 1.25
    [InlineData(true, 20)]  // 13 * 1.5
    public void An_adrenaline_rush_wakes_below_an_eighth_of_full_health(bool improved, int damage)
    {
        using DamagePolicyFixture hurt = new();
        hurt.PlayerStrength(50);
        hurt.Rush = new DaggerfallAdrenalineRush(improved);
        hurt.SetPlayerHealth(.1d);
        hurt.Script(body: 0, damage: 11);
        Assert.Equal(damage, hurt.Run(new AttackRequest(DaggerfallActorIdentity.PlayerEntityId, 2, 1, 1, .125d, Delayed: false)).Outcome.Damage);

        using DamagePolicyFixture healthy = new();
        healthy.PlayerStrength(50);
        healthy.Rush = new DaggerfallAdrenalineRush(improved);
        healthy.Script(body: 0, damage: 11);
        Assert.Equal(13, healthy.Run(new AttackRequest(DaggerfallActorIdentity.PlayerEntityId, 2, 1, 1, .125d, Delayed: false)).Outcome.Damage);
    }

    [Fact]
    public void A_kick_trains_hand_to_hand_and_drains_the_targets_stamina_whatever_the_weapon()
    {
        using DamagePolicyFixture fixture = new();
        fixture.SelectPlayerAttack(DaggerfallAttackIds.Kick);
        fixture.Script(body: 0, deflect: null, damage: DaggerfallAttackTuning.Default.KickDamage, afterDamage: 100);
        double before = fixture.Actors.Get(2).Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Stamina.Value)).Current;

        BlowResolution result = fixture.Run(new AttackRequest(DaggerfallActorIdentity.PlayerEntityId, 2, 1, 1, .125d, Delayed: false));

        Assert.True(result.Outcome.Hit);
        Assert.Contains(fixture.SkillUses, use => use.Skill == DaggerfallMechanicsIds.HandToHand.Value);
        Assert.True(fixture.Actors.Get(2).Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Stamina.Value)).Current < before);
    }

    [Theory]
    [InlineData(10, 2)]
    [InlineData(35, 3)]
    [InlineData(70, 4)]
    public void Weapon_skill_sets_how_long_a_chain_runs(int skill, int length) =>
        Assert.Equal(length, DaggerfallAttackTuning.Default.Combatant.ChainLength(skill));

    [Fact]
    public void A_shot_that_meets_cover_lands_nothing_and_says_so()
    {
        // Static geometry on the arrow's line stops it before the target: nothing about the target is
        // damaged or reported as having met nothing.
        using DamagePolicyFixture fixture = new(armed: false);
        fixture.EquipPlayerBow("iron-short-bow", 3001);
        fixture.GivePlayerArrows(3);
        fixture.Spatial.FloorHit = request => new SpatialHit(true, SpatialHitKind.StaticMesh, 0, 1, 1, 1, 0, 0, 0, default,
            request.Origin + request.Direction, -request.Direction, 1d, 0d, 0d, false, true);
        AttackRequest request = new(DaggerfallActorIdentity.PlayerEntityId, 2, 5, 9, .125d, Delayed: true);
        fixture.StartSwing(request, out bool started);
        Assert.True(started);

        Assert.Empty(fixture.DeliverImpact(request));
        IReadOnlyList<IProductFact> arrived = fixture.AdvanceFlight(5, 10);

        RangedShotBlockedFact blocked = Assert.Single(arrived.OfType<RangedShotBlockedFact>());
        Assert.Equal(DaggerfallActorIdentity.PlayerEntityId, blocked.AttackerId);
        Assert.Equal(2L, blocked.TargetId);
        Assert.Empty(arrived.OfType<AttackHitFact>());
        Assert.Empty(arrived.OfType<AttackWhiffedFact>());
    }

    [Fact]
    public void A_player_bow_shot_with_an_empty_quiver_is_refused_before_it_is_admitted()
    {
        using DamagePolicyFixture fixture = new(armed: false);
        fixture.EquipPlayerBow("iron-short-bow", 3001);

        IReadOnlyList<IProductFact> facts = fixture.StartSwing(
            new AttackRequest(DaggerfallActorIdentity.PlayerEntityId, 2, 5, 9, .125d, Delayed: true), out bool started);

        Assert.False(started);
        Assert.Contains(new AttackRejectedFact(AttackRejection.EmptyQuiver, DaggerfallActorIdentity.PlayerEntityId), facts);
        Assert.DoesNotContain(facts, fact => fact is PlayerAttackStartedFact);
        Assert.True(fixture.IsSwingReady(DaggerfallActorIdentity.PlayerEntityId, 5, 500));
    }

    [Fact]
    public void A_power_release_doubles_the_rolled_damage_and_the_stamina_it_costs()
    {
        (int Damage, double Stamina) Blow(string attack)
        {
            using DamagePolicyFixture fixture = new();
            fixture.PlayerStrength(50);
            fixture.SelectPlayerAttack(attack);
            fixture.Script(body: 0, damage: 11, critical: attack == DaggerfallAttackIds.PowerOut ? 100 : null);
            double before = fixture.PlayerStamina;
            BlowResolution result = fixture.Run(new AttackRequest(DaggerfallActorIdentity.PlayerEntityId, 2, 1, 1, .125d, Delayed: false));
            return (result.Outcome.Damage, before - fixture.PlayerStamina);
        }

        Assert.Equal((13, 64d), Blow("fastA")); // 11 - 1 iron + 3 for skill 60
        Assert.Equal((26, 128d), Blow(DaggerfallAttackIds.PowerOut));
    }

    [Fact]
    public void A_kick_is_a_blunt_hand_to_hand_blow_of_its_own_damage_and_cost_whatever_the_weapon()
    {
        using DamagePolicyFixture fixture = new();
        fixture.SelectPlayerAttack(DaggerfallAttackIds.Kick);
        // Blunt deflects after the roll, and the kick's one-point range is drawn like any other.
        fixture.Script(body: 0, deflect: null, damage: DaggerfallAttackTuning.Default.KickDamage, afterDamage: 100);
        double before = fixture.PlayerStamina;

        BlowResolution result = fixture.Run(new AttackRequest(DaggerfallActorIdentity.PlayerEntityId, 2, 1, 1, .125d, Delayed: false));

        Assert.True(result.Outcome.Hit);
        Assert.Contains((DaggerfallAttackTuning.Default.KickDamage, DaggerfallAttackTuning.Default.KickDamage), result.Ranges);
        Assert.Equal(DaggerfallAttackTuning.Default.KickStaminaCost, before - fixture.PlayerStamina);
        Assert.Contains(fixture.SkillUses, use => use.Skill == "long-blade" || use.Skill == "hand-to-hand");
    }

    [Fact]
    public void A_kick_with_a_bow_in_hand_lands_by_contact_and_spends_no_arrow()
    {
        using DamagePolicyFixture fixture = new(armed: false);
        fixture.EquipPlayerBow("iron-short-bow", 3001);
        fixture.GivePlayerArrows(3);
        fixture.SelectPlayerAttack(DaggerfallAttackIds.Kick);
        fixture.Touches(2);
        fixture.Script(body: 0, deflect: null, damage: DaggerfallAttackTuning.Default.KickDamage, afterDamage: 100);

        fixture.Run(new AttackRequest(DaggerfallActorIdentity.PlayerEntityId, 2, 1, 1, .125d, Delayed: false), out IReadOnlyList<IProductFact> facts);

        Assert.Equal(3UL, fixture.PlayerArrows());
        Assert.Single(facts.OfType<AttackHitFact>());
    }

    [Fact]
    public void A_kick_with_a_bow_in_hand_works_with_an_empty_quiver()
    {
        using DamagePolicyFixture fixture = new(armed: false);
        fixture.EquipPlayerBow("iron-short-bow", 3001);
        fixture.SelectPlayerAttack(DaggerfallAttackIds.Kick);

        IReadOnlyList<IProductFact> facts = fixture.StartSwing(new AttackRequest(DaggerfallActorIdentity.PlayerEntityId, 2, 5, 9, .125d, Delayed: true), out bool started);

        Assert.True(started);
        Assert.DoesNotContain(facts, fact => fact is AttackRejectedFact { Reason: AttackRejection.EmptyQuiver });
    }

    [Fact]
    public void A_held_bow_carries_the_authored_ranged_reach_and_a_swing_keeps_its_own()
    {
        using DamagePolicyFixture fixture = new(armed: false);

        Assert.Equal(2.25d, fixture.ReachOf(DaggerfallActorIdentity.PlayerEntityId));

        fixture.EquipPlayerBow("iron-short-bow", 3001);

        Assert.Equal(10d, fixture.ReachOf(DaggerfallActorIdentity.PlayerEntityId));
    }

    private sealed class CountingContribution : ICombatContribution
    {
        internal int DamageCount { get; private set; }
        public void Damage(DamageEvent interaction) => DamageCount++;
    }

    /// <summary>The body roll that strikes the feet, which the payload player wears nothing on.</summary>
    private const int BareFeet = 19;

    private static DaggerfallDefinitions Definitions => _definitions ??= TestPayload.Definitions;
    private static DaggerfallDefinitions? _definitions;

    /// <summary>
    /// One staged attack arena: the payload player with their iron loadout, a brigand enemy (entity 2)
    /// wearing the equipped-enemy action, and a scripted keyed random that records every requested
    /// draw range in order.
    /// </summary>
    internal sealed class DamagePolicyFixture : IDisposable
    {
        private readonly ActorsState _actors;
        private readonly DaggerfallItemInstances _itemInstances = new();
        private readonly Dictionary<long, DaggerfallActorDefinition> _authored = [];
        private readonly Dictionary<long, MechanicsEquipmentCoordinator> _actorEquipment = [];
        private readonly Dictionary<long, MechanicsInventoryCoordinator> _actorInventories = [];
        private readonly SpatialMovementSystem _spatial;
        private readonly DaggerfallItemConditionService _itemCondition;
        private readonly MechanicsEquipmentCoordinator _playerEquipment;
        private readonly ScriptedRandom _scripted = (ScriptedRandom)(object)DispatchProxy.Create<IRandomService, ScriptedRandom>();
        private readonly IRandomService _random;
        private readonly DaggerCombatRules _combat;
        private WorldPoint? _playerPosition;

        private DaggerfallCharacterState? _character;

        internal DamagePolicyFixture(bool armed = true, int attackChanceShift = 0,
            IDaggerfallPhysicalMitigation? mitigation = null, Func<IDaggerfallPhysicalHarmOperations, IDaggerfallPhysicalDelivery>? delivery = null,
            IRandomService? random = null)
        {
            DaggerfallDefinitions definitions = Definitions;
            DaggerfallActorDefinition playerDefinition = definitions.RequireActor(new DaggerfallActorId("player"));
            _random = random ?? (IRandomService)(object)_scripted;
            _actors = new ActorsState();
            PlayerActorState player = _actors.CreatePlayer(DaggerfallActorIdentity.PlayerEntityId, new EntityTypeId(playerDefinition.Id.Value),
                new DaggerfallMechanicsState().CreateStats(playerDefinition, DaggerfallPlayerVitals.Initial(playerDefinition.Stats, definitions.Catalogs.RequireCareer("class00"))), "health", DaggerActorFactory.PlayerCapabilities);
            _playerEquipment = BuildEquipment(player.Actor.Entity, DaggerfallActorIdentity.PlayerEntityId, player.Actor);
            foreach (DaggerfallLoadoutEntry entry in playerDefinition.Loadout.Where(entry => entry.UniqueEntityId is not null))
            {
                _itemInstances.RegisterDefaultUnique(entry.UniqueEntityId!.Value, definitions.RequireItem(entry.ItemId), DaggerfallItemOwner.Player);
                WorldRpg.Kit.Inventory.UniqueInventoryItem item = _playerEquipment.Materialize(
                    new DurableIdentityReference(DurableIdentityKind.Item, entry.UniqueEntityId.Value), new InventoryItemId(entry.ItemId.Value));
                if (entry.EquipSlot is DaggerfallEquipmentSlotId slot && (armed || slot.Value != "right-hand" && slot.Value != "left-hand"))
                    _playerEquipment.Equip(item, [new SlotId(slot.Value)]);
            }

            // The default stage: an enemy-class brigand (entity 2) one metre from the player with
            // the equipped-enemy action and nothing worn until a test hands it a weapon.
            DaggerfallActorDefinition brigand = definitions.RequireActor(new DaggerfallActorId("thief")) with { ActionId = "enemy-class-equipped-melee" };
            ActorState enemy = _actors.CreateActor(2, new EntityTypeId("brigand"),
                new DaggerfallMechanicsState().CreateStats(brigand, new DaggerfallVitalValues(200, 100, 0)), new ActorPose(new WorldPoint(1f, 0f, 0f), 0f), "health", DaggerActorFactory.NonPlayerCapabilities);
            _authored[DaggerfallActorIdentity.PlayerEntityId] = playerDefinition;
            _authored[2] = brigand;
            _actorEquipment[2] = BuildEquipment(enemy.Actor.Entity, 2, enemy.Actor);
            _itemCondition = new DaggerfallItemConditionService(definitions, _itemInstances,
                new DaggerfallEquipmentMoves(_actorInventories[DaggerfallActorIdentity.PlayerEntityId], _playerEquipment, definitions, itemInstances: _itemInstances));
            DaggerfallComposedSeams seams = DaggerfallSeamSelection.Default.Build(SeamInputs, DaggerfallContactTuning.Default);
            _combat = new DaggerCombatRules(_random, _actors, _playerEquipment,
                id => _actorInventories.TryGetValue(id, out MechanicsInventoryCoordinator? quiver) ? quiver : null,
                _itemInstances, definitions, _authored, CombatCollaborators.Targeting(_actors, _authored, out _spatial, out SpatialFake spatialFake),
                skillUses: SkillUses.Add,
                actorEquipment: id => _actorEquipment.TryGetValue(id, out MechanicsEquipmentCoordinator? coordinator) ? coordinator : _playerEquipment,
                itemCondition: _itemCondition, rules: new CombatResolution(),
                playerPosition: () => _playerPosition, character: () => _character,
                deliverWeaponPoison: (_, _) => throw new InvalidOperationException("This fixture coats no weapon."),
                attackChanceModifier: () => attackChanceShift,
                transformActor: (_, target, _, _, _) => new DaggerfallWabbajackResult(DaggerfallWabbajackOutcome.InvalidTarget, target),
                magicDefense: _ => DaggerfallMagicDefense.None, physicalAttacksBlocked: _ => false,
                molagBalStrike: (_, _, _, _, _, _, _) => throw new InvalidOperationException("This fixture arms no Mace of Molag Bal."),
                itemStrike: (_, _, _, damage) => damage,
                monsterHit: _ => { }, actorGameplayActive: _ => true, actorTeam: id => _authored.GetValueOrDefault(id)?.Team,
                mitigation: mitigation ?? seams.PhysicalMitigation,
                delivery: ops => Delivery = (delivery ?? seams.PhysicalDelivery)(ops),
                attackTuning: DaggerfallAttackTuning.Default,
                targetUnaware: Unaware.Contains, alertTarget: id => Alerted.Add(id), playerRush: () => Rush,
                contact: Contact = CombatCollaborators.Contact(_actors, _spatial, () => _playerPosition),
                strikePose: actor => CombatCollaborators.StrikePose(_actors, actor, _playerPosition),
                actorVelocity: id => id == DaggerfallActorIdentity.PlayerEntityId ? PlayerVelocity : Vector3.Zero);
            Spatial = spatialFake;
        }

        /// <summary>
        /// Scripts one connecting blow's draws in order: the body, the deflection a target with a full
        /// channel draws before damage (the top of the percentile, which turns nothing aside), then the
        /// damage roll with the critical draw a finishing blow makes, and whatever a partial channel draws after it.
        /// </summary>
        internal void Script(int body, int? deflect = 100, int? damage = null, int? critical = null, int? afterDamage = null)
        {
            List<int> draws = [body];
            if (deflect is int deflection) draws.Add(deflection);
            if (damage is int weaponDamage)
            {
                draws.Add(weaponDamage);
                if (critical is int strike) draws.Add(strike);
            }
            if (afterDamage is int after) draws.Add(after);
            _scripted.Feed(draws);
        }

        /// <summary>Scripts a monster's natural blow: the body, each slot's gates, then the blunt deflection on what they rolled.</summary>
        internal void ScriptMonster(int body, params (int? Reflex, int? Damage, int? Consequence)[] slots)
        {
            List<int> draws = [body];
            foreach (var slot in slots)
            {
                if (slot.Reflex is int reflex) draws.Add(reflex);
                if (slot.Damage is int damage) draws.Add(damage);
                if (slot.Consequence is int consequence) draws.Add(consequence);
            }
            draws.Add(100);
            _scripted.Feed(draws);
        }

        /// <summary>The swing and touch volumes meet the given actor's body.</summary>
        internal void Touches(long actorId) => Spatial.OverlapContactsResult = _ => TestSessions.Touching(EntityOf(actorId));

        /// <summary>The player's sweep met the given actor, whom the swing's impact takes.</summary>
        internal void Swept(long actorId) => _combat.SetSweptTarget(actorId);

        /// <summary>Every arrow segment meets the given actor's body.</summary>
        internal void ArrowsMeet(long actorId)
        {
            ulong entity = EntityOf(actorId);
            Spatial.FloorHit = request => new SpatialHit(true, SpatialHitKind.Entity, entity, 0, 0, 0, 0, 0, 0, default,
                request.Origin + request.Direction * .7f, -request.Direction, .7d, 0d, 0d, false, true);
        }

        private ulong EntityOf(long actorId) => (actorId == DaggerfallActorIdentity.PlayerEntityId ? _actors.Player.Actor : _actors.Get(actorId).Actor).Entity.Value;

        internal void PlayerRace(string race, int level)
        {
            _character = new(Definitions, _actors.Player.Stats, Definitions.RequireActor(new DaggerfallActorId("player")));
            _character.BeginChoices();
            _character.ReplacePending(new DaggerfallCharacterCreationChoices("Review", race,
                DaggerfallCharacterGender.Male, 0, DaggerfallCharacterReflexes.Average, "class00"));
            _character.CommitChoices();
            _actors.Player.Progression.AdvanceTo(500, level);
            foreach (string skill in new[] { "long-blade", "hand-to-hand" })
                _actors.Player.Stats.GetStat(StatId.Parse(skill)).BaseValue = 20;
            PlayerStrength(50);
        }

        internal void Contribute(ICombatContribution contribution) => _combat.Rules.RegisterAction("monster-strike", contribution);

        internal void PlayerStrength(int strength) => _actors.Player.Stats.GetStat(StatId.Parse(DaggerfallMechanicsIds.Strength.Value)).BaseValue = strength;

        internal void PlayerBackstabbingSkill(int skill) => _actors.Player.Stats.GetStat(StatId.Parse("backstabbing")).BaseValue = skill;

        internal void PlayerCriticalStrikeSkill(int skill) => _actors.Player.Stats.GetStat(StatId.Parse("critical-strike")).BaseValue = skill;

        internal void NpcCriticalStrikeSkill(int skill) => _actors.Get(2).Stats.GetStat(StatId.Parse("critical-strike")).BaseValue = skill;

        internal void NpcHandToHandSkill(int skill) => _actors.Get(2).Stats.GetStat(StatId.Parse(DaggerfallMechanicsIds.HandToHand.Value)).BaseValue = skill;

        internal void NpcStrength(int strength) => _actors.Get(2).Stats.GetStat(StatId.Parse(DaggerfallMechanicsIds.Strength.Value)).BaseValue = strength;

        /// <summary>Hands the player a bow, which is the weapon the attack input then looses with. A bow
        /// is held in both hands, so the fixture hands it the two slots the item definition requires.</summary>
        internal void EquipPlayerBow(string itemId, ulong uniqueId)
        {
            _itemInstances.RegisterDefaultUnique(uniqueId, Definitions.RequireItem(new DaggerfallItemId(itemId)), DaggerfallItemOwner.Player);
            _playerEquipment.Equip(
                _playerEquipment.Materialize(new DurableIdentityReference(DurableIdentityKind.Item, uniqueId), new InventoryItemId(itemId)),
                [new SlotId("right-hand"), new SlotId("left-hand")]);
        }

        internal void GivePlayerArrows(ulong quantity)
        {
            InventoryStackId stack = InventoryStackId.Parse("test.quiver");
            _actorInventories[DaggerfallActorIdentity.PlayerEntityId].Grant(
                new InventoryGrant(new InventoryItemId("arrow"), stack, quantity));
            _itemInstances.RegisterDefaultStack(DaggerfallItemOwner.Player,
                new InventoryStack(stack, ItemDefinitionId.Parse("arrow"), quantity),
                Definitions.RequireItem(new DaggerfallItemId("arrow")));
        }

        internal ulong PlayerArrows() => _actorInventories[DaggerfallActorIdentity.PlayerEntityId].Read().Stacks
            .Where(stack => stack.Definition.Value == "arrow").Aggregate(0UL, (total, stack) => total + stack.Quantity);

        internal double? ReachOf(long actorId) => _combat.ReachOf(actorId);

        /// <summary>Names the attack the next player admission carries, as the attack clock does.</summary>
        internal void SelectPlayerAttack(string attack, bool finisher = false) => _combat.SelectPlayerAttack(attack, finisher);

        internal HashSet<long> Unaware { get; } = [];
        internal List<long> Alerted { get; } = [];
        internal DaggerfallAdrenalineRush? Rush { get; set; }

        internal void PlayerStat(string stat, int value) => _actors.Player.Stats.GetStat(StatId.Parse(stat)).BaseValue = value;
        internal void SetPlayerStamina(double value) => _actors.Player.Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Stamina.Value)).SetCurrent(value);
        internal void SetPlayerHealth(double share)
        {
            Track health = _actors.Player.Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Health.Value));
            health.SetCurrent(health.Maximum.Value * share);
        }

        internal double PlayerStamina => _actors.Player.Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Stamina.Value)).Current;

        /// <summary>Interrupts an attacker's attack, as leaving attack state or losing its target does.</summary>
        internal void Interrupt(long attackerId, ulong generation) => _combat.Execution.Interrupt(attackerId, generation);

        /// <summary>The physical delivery owner the rules composed.</summary>
        internal IDaggerfallPhysicalDelivery Delivery { get; private set; } = null!;

        /// <summary>The player's controller velocity, which an archer may lead.</summary>
        internal Vector3 PlayerVelocity { get; set; }

        /// <summary>The player's worn armor-value shift, as held enchantments would give it.</summary>
        internal int ArmorValueShift { get; set; }

        /// <summary>The spatial fake answering contact queries.</summary>
        internal SpatialFake Spatial { get; }

        /// <summary>Actor contact over the fixture's actor bodies and spatial fake.</summary>
        internal ActorContact Contact { get; private set; }

        /// <summary>The spatial reads a touch delivery owner queries, over this fixture.</summary>
        internal DaggerfallTouchOperations TouchOperations => new(Contact, _spatial, () => null);

        /// <summary>Stands another brigand at <paramref name="at"/>, for a touch or swing that reaches several actors.</summary>
        internal ActorState SpawnBrigand(long id, WorldPoint at)
        {
            DaggerfallActorDefinition brigand = Definitions.RequireActor(new DaggerfallActorId("thief")) with { ActionId = "enemy-class-equipped-melee" };
            _authored[id] = brigand;
            ActorState spawned = _actors.CreateActor(id, new EntityTypeId("brigand"),
                new DaggerfallMechanicsState().CreateStats(brigand, new DaggerfallVitalValues(200, 100, 0)), new ActorPose(at, 0f), "health", DaggerActorFactory.NonPlayerCapabilities);
            _actorEquipment[id] = BuildEquipment(spawned.Actor.Entity, id, spawned.Actor);
            return spawned;
        }

        /// <summary>Skill uses the rules recorded, in order.</summary>
        internal List<DaggerfallSkillUse> SkillUses { get; } = [];

        internal ActorsState Actors => _actors;

        /// <summary>The session reads seam owners are built over, from this fixture.</summary>
        internal DaggerfallSeamInputs SeamInputs => new(Definitions, () => _character, _random,
            id => id == DaggerfallActorIdentity.PlayerEntityId ? _playerEquipment.Read() : _actorEquipment[id].Read(),
            id => id == DaggerfallActorIdentity.PlayerEntityId ? _actors.Player.Stats : _actors.TryGet(id, out ActorState actor) ? actor.Stats : null,
            () => ArmorValueShift);

        internal ulong CooldownRemaining(long actorId, ulong generation, ulong step) =>
            _combat.Execution.CaptureCooldowns(generation, step).Single(cooldown => cooldown.AttackerId == actorId).RemainingSteps;

        /// <summary>Advances the ruleset's own ranged delivery until the released shot has arrived.</summary>
        internal IReadOnlyList<IProductFact> AdvanceFlight(ulong generation, ulong step, int steps = 4)
        {
            FactBuffer<IProductFact> facts = new();
            Dictionary<long, WorldPoint> positions = new()
            {
                [DaggerfallActorIdentity.PlayerEntityId] = new WorldPoint(0f, 0f, 1f),
                [2] = _actors.Get(2).Position,
            };
            for (int index = 0; index < steps; index++)
            {
                _combat.LandDueImpacts(generation, checked(step + (ulong)index), facts);
                _combat.AdvanceRangedFlight(generation, checked(step + (ulong)index), .125d, positions, facts);
            }
            return Delivered(facts);
        }

        internal void EquipNpcWeapon(long actorId, string itemId, ulong uniqueId)
        {
            _itemInstances.RegisterDefaultUnique(uniqueId, Definitions.RequireItem(new DaggerfallItemId(itemId)), DaggerfallItemOwner.Actor(actorId));
            _actorEquipment[actorId].Equip(
                _actorEquipment[actorId].Materialize(new DurableIdentityReference(DurableIdentityKind.Item, uniqueId), new InventoryItemId(itemId)),
                [new SlotId("right-hand")]);
        }

        internal void ReplaceActor(long entityId, DaggerfallActorDefinition definition) => _authored[entityId] = definition;

        internal BlowResolution Run(AttackRequest request) => Run(request, out _);

        /// <summary>Admits one attack whose volume touches its target, and carries it through its hit frame and any wind-up.</summary>
        internal BlowResolution Run(AttackRequest request, out IReadOnlyList<IProductFact> published)
        {
            Touches(request.TargetId!.Value);
            List<IProductFact> facts = [.. StartSwing(request, out bool admitted)];
            if (request.AttackerId == DaggerfallActorIdentity.PlayerEntityId) Swept(request.TargetId.Value);
            if (request.Delayed)
            {
                facts.AddRange(DeliverImpact(request));
                facts.AddRange(AdvanceFlight(request.Generation, request.SimulationStep, steps: 5));
            }
            published = facts;
            AttackHitFact? hit = facts.OfType<AttackHitFact>().SingleOrDefault();
            bool connected = hit is not null || facts.Any(fact => fact is AttackNegatedFact or AttackRejectedFact { Reason: AttackRejection.InsufficientWeaponMaterial });
            return new BlowResolution(admitted, new AttackOutcome(connected, hit is not null, hit?.CalculatedDamage ?? 0), hit?.StruckBody ?? 0,
                _scripted.Ranges, _scripted.Salts);
        }

        /// <summary>Admits one swing through the shared attack lifecycle and delivers what it published.</summary>
        internal IReadOnlyList<IProductFact> StartSwing(AttackRequest request, out bool admitted)
        {
            FactBuffer<IProductFact> facts = new();
            admitted = _combat.Execution.Start(request, facts);
            return Delivered(facts);
        }

        /// <summary>Delivers the swing's animation hit frame, or its expiry when the animation never reached one.</summary>
        internal IReadOnlyList<IProductFact> DeliverImpact(AttackRequest request, bool expired = false)
        {
            FactBuffer<IProductFact> facts = new();
            _combat.Execution.ApplyImpacts([new AttackImpactNotice(request.AttackerId, request.TargetId ?? 0,
                request.Generation, request.SimulationStep, expired)], request.Generation, facts);
            return Delivered(facts);
        }

        internal bool IsSwingReady(long attackerId, ulong generation, ulong step) =>
            _combat.Execution.IsReady(attackerId, generation, step);

        /// <summary>The one cancellation owner: the pending swing is dropped and its cooldown stands.</summary>
        internal void CancelSwing(long attackerId, ulong generation) => _combat.Execution.Interrupt(attackerId, generation);

        private static IReadOnlyList<IProductFact> Delivered(FactBuffer<IProductFact> facts)
        {
            List<IProductFact> collected = [];
            facts.Deliver(collected.Add);
            return collected;
        }

        public void Dispose()
        {
            _spatial.Dispose();
            _actors.Dispose();
        }

        private MechanicsEquipmentCoordinator BuildEquipment(EntityId owner, long durableId, Actor actor)
        {
            InventoryStore world = new();
            world.RegisterInventory(new InventoryState(owner));
            world.RegisterEquipment(new EquipmentState(owner));
            InventoryComponent inventory = new(world, owner);
            EquipmentComponent equipment = new(world, owner);
            actor.Add(inventory);
            actor.Add(equipment);
            var items = Definitions.Items.Values.Concat(Definitions.TemplateItems.Values).ToDictionary(item => new InventoryItemId(item.Id.Value), DaggerActorFactory.ToManagedItem);
            var slots = Definitions.EquipmentSlots.Values.ToDictionary(slot => new SlotId(slot.Id.Value), DaggerActorFactory.ToManagedSlot);
            // Ranged attacks draw from the same coordinator the actor's inventory component owns.
            _actorInventories[durableId] = new MechanicsInventoryCoordinator(inventory, _actors.Entities, items);
            return new MechanicsEquipmentCoordinator(inventory, equipment, _actors.Entities, items, slots);
        }
    }

    internal sealed record BlowResolution(bool Admitted, AttackOutcome Outcome, int Body,
        IReadOnlyList<(int Minimum, int Maximum)> Ranges, IReadOnlyList<int> Salts);

    private class ScriptedRandom : DispatchProxy
    {
        private readonly List<int> _values = [];
        private readonly List<(int Minimum, int Maximum)> _ranges = [];
        private readonly List<int> _salts = [];
        private int _next;

        internal void Feed(IEnumerable<int> values) => _values.AddRange(values);

        internal IReadOnlyList<(int Minimum, int Maximum)> Ranges => _ranges;

        internal IReadOnlyList<int> Salts => _salts;

        protected override object? Invoke(MethodInfo? method, object?[]? arguments)
        {
            if (method?.Name != nameof(IRandomService.DrawKeyed)) throw new NotSupportedException(method?.Name);
            KeyedRngRequest request = (KeyedRngRequest)arguments![0]!;
            int salt = int.Parse(request.Key[(request.Key.LastIndexOf(':') + 1)..]);
            // Wear follows the applied hit and is not what these facts script: its top draw wears nothing extra.
            if (salt is CombatRandomKey.WeaponConditionSalt or CombatRandomKey.ArmorConditionSalt) return new KeyedRngReceipt(request.Maximum);
            _ranges.Add(((int)request.Minimum, (int)request.Maximum));
            _salts.Add(salt);
            if (_next >= _values.Count) throw new InvalidOperationException($"The scripted attack reached draw {_next + 1} with no scripted value.");
            int value = _values[_next++];
            if (value < request.Minimum || value > request.Maximum)
                throw new InvalidOperationException($"Scripted draw {value} is outside [{request.Minimum}, {request.Maximum}].");
            return new KeyedRngReceipt(value);
        }
    }

}
