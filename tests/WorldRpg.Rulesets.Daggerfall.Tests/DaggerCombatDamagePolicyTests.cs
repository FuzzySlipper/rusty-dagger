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
/// and the AI attack capability actually enter: <see cref="DaggerCombatRules.TryPrepare"/>. Every
/// scenario drives the one admitted resolution, and the scripted random records each keyed draw's
/// requested range in order, so the ranges are the evidence that the donor's draw discipline —
/// gates that skip their roll, reflex-gated slots, backstab rolls only above level one — survived
/// into the product rather than being approximated by a flat roll.
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
        fixture.Script(body: 3, critical: 1, hit: 1, damage: 7);

        PreparedResolution result = fixture.Run(new AttackRequest(2, DaggerfallActorIdentity.PlayerEntityId, 5, 9, .125d, Delayed: true));

        Assert.True(result.Admitted);
        Assert.True(result.Outcome.Hit);
        Assert.True(result.Outcome.Allowed);
        Assert.Equal(6, result.Outcome.Damage); // 7 (weapon roll) - 1 (iron) + 0 (strength 50)
        Assert.Equal((2, 16), Assert.Single(result.Ranges, range => range != (0, 19) && range != (1, 100)));
    }

    [Fact]
    public void A_variants_physical_mitigation_owner_replaces_classic_mitigation_for_the_prepared_attack()
    {
        // The same armed brigand swing as above, under a whole-owner mitigation seam that absorbs two and
        // does not apply classic's material requirement.
        RecordingMitigation absorbing = new(absorb: 2);
        using DamagePolicyFixture fixture = new(mitigation: absorbing);
        fixture.NpcStrength(50);
        fixture.EquipNpcWeapon(2, "iron-longsword", 9001);
        fixture.Script(body: 3, critical: 1, hit: 1, damage: 7);

        PreparedResolution result = fixture.Run(new AttackRequest(2, DaggerfallActorIdentity.PlayerEntityId, 5, 9, .125d, Delayed: true));

        Assert.Equal((true, true, 4), (result.Outcome.Hit, result.Outcome.Allowed, result.Outcome.Damage));
        DaggerfallPhysicalHarm harm = Assert.Single(absorbing.Mitigated);
        Assert.Equal((2L, DaggerfallActorIdentity.PlayerEntityId, "long-blade", "iron"), (harm.AttackerId, harm.TargetId, harm.Skill, harm.WeaponMaterial));
        Assert.Equal(harm, Assert.Single(absorbing.Negated));

        using DamagePolicyFixture negated = new(mitigation: new RecordingMitigation(negate: true));
        negated.EquipNpcWeapon(2, "iron-longsword", 9001);
        negated.Script(body: 3, critical: 1, hit: 1);
        PreparedResolution turned = negated.Run(new AttackRequest(2, DaggerfallActorIdentity.PlayerEntityId, 5, 9, .125d, Delayed: true));
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
        Assert.True(announced.WaitsWithoutTarget);
        Assert.Null(announced.TargetId);
        Assert.True(announced.FrameSeconds > 0d);
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

    [Fact]
    public void Classic_delivery_resolves_an_untargeted_swing_at_admission_and_reports_nothing_in_reach()
    {
        using DamagePolicyFixture fixture = new();
        AttackRequest swing = new(DaggerfallActorIdentity.PlayerEntityId, null, 3, 5, .125d, Delayed: false);
        IReadOnlyList<IProductFact> started = fixture.StartSwing(swing, out bool admitted);
        Assert.True(admitted);
        Assert.False(Assert.Single(started.OfType<PlayerAttackStartedFact>()).WaitsWithoutTarget);
        Assert.Contains(new AttackRejectedFact(AttackRejection.NoTargetInReach), started);
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
        public bool UntargetedSwingsWaitForImpact => true;
        public bool ImpactsOutliveTheirTarget => false;
        public bool HoldsFire(long archer, long target) => false;
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
        // Each slot independently passes reflex, critical and hit rolls before drawing damage.
        fixture.ScriptMonster(0, (50, 1, 4, 100), (50, 1, 5, 100), (50, 1, 6, 100));

        PreparedResolution result = fixture.Run(new AttackRequest(2, DaggerfallActorIdentity.PlayerEntityId, 5, 9, .125d, Delayed: true));

        Assert.True(result.Outcome.Hit);
        Assert.True(result.Outcome.Allowed);
        Assert.Equal(15, result.Outcome.Damage);
        Assert.Equal(
        [
            (0, 19),
            (1, 100), (1, 100), (1, 100), (1, 8), (1, 100),
            (1, 100), (1, 100), (1, 100), (1, 8), (1, 100),
            (1, 100), (1, 100), (1, 100), (1, 10), (1, 100),
        ], result.Ranges);
    }

    [Fact]
    public void A_slot_the_targets_reflexes_evade_never_reaches_its_hit_or_damage_roll()
    {
        using DamagePolicyFixture fixture = new();
        fixture.ReplaceActor(2, Definitions.RequireActor(new DaggerfallActorId("rat")) with
        {
            Attacks = [new DaggerfallAttackRange(1, 8), new(1, 8), new(1, 10)],
        });
        // Slot 1's reflex roll (51) beats the 50 gate; the recorded sequence jumps from that roll
        // straight to slot 2's reflex roll with no (1, 8) damage draw in between.
        fixture.ScriptMonster(0, (50, 1, 4, 100), (51, null, null, null), (50, 1, 6, 100));

        PreparedResolution result = fixture.Run(new AttackRequest(2, DaggerfallActorIdentity.PlayerEntityId, 5, 9, .125d, Delayed: true));

        Assert.Equal(10, result.Outcome.Damage);
        // Slot 0's damage (1, 8) is the only (1, 8) draw: slot 1 went from its reflex roll straight
        // to slot 2's, and slot 2's later (1, 10) keeps the ranges distinguishable.
        Assert.Equal(
        [
            (0, 19),
            (1, 100), (1, 100), (1, 100), (1, 8), (1, 100),
            (1, 100),
            (1, 100), (1, 100), (1, 100), (1, 10), (1, 100),
        ], result.Ranges);
    }

    [Fact]
    public void A_slot_that_misses_the_rerolled_gate_or_has_no_authored_minimum_adds_no_damage_and_no_career_bonus()
    {
        using DamagePolicyFixture fixture = new();
        // The vampire's classic enemy configuration carries the humanoid bonus (bit 0x04), so each
        // landed natural-attack slot carries the attacker's level (19) on top of its damage.
        fixture.ReplaceActor(2, Definitions.RequireActor(new DaggerfallActorId("vampire")) with
        {
            ActionId = "monster-strike",
            Attacks = [new DaggerfallAttackRange(1, 3), new(0, 0), new(1, 3)],
        });
        // Slot 1 has no authored minimum and stops after its reflex draw; slot 2's rerolled hit
        // gate (98, above the clamped chance ceiling) refuses it after its reflex roll passed.
        fixture.ScriptMonster(0, (50, 1, 3, 1_000_000), (50, null, null, null), (50, 98, null, null));

        PreparedResolution result = fixture.Run(new AttackRequest(2, DaggerfallActorIdentity.PlayerEntityId, 5, 9, .125d, Delayed: true));

        Assert.Equal(22, result.Outcome.Damage); // (3 + 19) from slot 0 alone
        Assert.Equal(
        [
            (0, 19),
            (1, 100), (1, 100), (1, 100), (1, 3), (0, 1_000_000),
            (1, 100), // The zero-minimum slot still draws reflexes.
            (1, 100), (1, 100), (1, 100),
        ], result.Ranges);
    }

    [Fact]
    public void A_weapon_below_the_target_material_immunity_hits_but_deals_no_damage()
    {
        // The donor returns before its damage roll when the weapon cannot scratch an immune target;
        // the product draws the keyed hit first and stops before the damage roll, so an immune
        // attempt never fabricates a damage draw and never reports a scratch.
        using DamagePolicyFixture fixture = new();
        fixture.ReplaceActor(2, Definitions.RequireActor(new DaggerfallActorId("rat")) with { MinimumMaterial = "silver" });
        fixture.Script(body: 0, critical: 1, hit: 1);

        PreparedResolution result = fixture.Run(new AttackRequest(DaggerfallActorIdentity.PlayerEntityId, 2, 1, 1, .125d, Delayed: false));

        Assert.True(result.Admitted);
        Assert.True(result.Outcome.Hit);
        Assert.False(result.Outcome.Allowed);
        Assert.Equal(0, result.Outcome.Damage);
        Assert.DoesNotContain(result.Ranges, range => range != (0, 19) && range != (1, 100));
    }

    [Fact]
    public void A_backstab_from_behind_triples_the_resolved_damage_only_when_its_roll_succeeds()
    {
        using DamagePolicyFixture fixture = new();
        fixture.PutPlayerBehindTarget(2);
        fixture.PlayerBackstabbingSkill(20);
        fixture.Script(body: 0, critical: 1, hit: 1, damage: 5, backstabRoll: 10);

        PreparedResolution result = fixture.Run(new AttackRequest(DaggerfallActorIdentity.PlayerEntityId, 2, 1, 1, .125d, Delayed: false));

        Assert.True(result.Outcome.Hit);
        Assert.Equal(12, result.Outcome.Damage); // (5 - 1 iron) * 3
        Assert.Equal((1, 100), result.Ranges[^1]);
    }

    [Fact]
    public void A_backstab_facing_the_player_never_rolls_and_never_triples()
    {
        using DamagePolicyFixture fixture = new();
        fixture.PutPlayerBehindTarget(2, facingAway: false);
        fixture.PlayerBackstabbingSkill(20);
        fixture.Script(body: 0, critical: 1, hit: 1, damage: 5);

        PreparedResolution result = fixture.Run(new AttackRequest(DaggerfallActorIdentity.PlayerEntityId, 2, 1, 1, .125d, Delayed: false));

        Assert.Equal(4, result.Outcome.Damage); // 5 - 1 iron, never tripled
        Assert.Equal(4, result.Ranges.Count);   // body, critical, hit, damage — no backstab roll
    }

    [Fact]
    public void A_backstab_of_one_never_rolls_even_from_behind()
    {
        // The donor rolls backstab only above level one; a chance of one would triple every hit
        // without a roll, so the roll and the tripling share the same eligibility gate.
        using DamagePolicyFixture fixture = new();
        fixture.PutPlayerBehindTarget(2);
        fixture.PlayerBackstabbingSkill(1);
        fixture.Script(body: 0, critical: 1, hit: 1, damage: 5);

        PreparedResolution result = fixture.Run(new AttackRequest(DaggerfallActorIdentity.PlayerEntityId, 2, 1, 1, .125d, Delayed: false));

        Assert.Equal(4, result.Outcome.Damage);
        Assert.Equal(4, result.Ranges.Count);
    }

    [Fact]
    public void A_weak_swing_below_the_damage_floor_resolves_to_zero_damage()
    {
        using DamagePolicyFixture fixture = new();
        fixture.PlayerStrength(30); // DamageModifier (30 - 50) / 5 = -4
        fixture.Script(body: 0, critical: 1, hit: 1, damage: 2);

        PreparedResolution result = fixture.Run(new AttackRequest(DaggerfallActorIdentity.PlayerEntityId, 2, 1, 1, .125d, Delayed: false));

        Assert.True(result.Outcome.Hit);
        Assert.True(result.Outcome.Allowed);
        Assert.Equal(0, result.Outcome.Damage); // 2 - 1 iron - 4 strength floors at zero
    }

    [Fact]
    public void A_monsters_first_miss_does_not_cancel_later_hits()
    {
        using DamagePolicyFixture fixture = new();
        fixture.ReplaceActor(2, Definitions.RequireActor(new DaggerfallActorId("rat")) with
        {
            Attacks = [new DaggerfallAttackRange(1, 8), new(1, 8), new(1, 10)],
        });
        fixture.ScriptMonster(0, (1, 100, null, null), (1, 1, 5, 100), (1, 1, 6, 100));
        PreparedResolution result = fixture.Run(new AttackRequest(2, DaggerfallActorIdentity.PlayerEntityId, 5, 9, .125d, Delayed: true));

        Assert.True(result.Outcome.Hit);
        Assert.Equal(11, result.Outcome.Damage);
        Assert.Equal(1, result.Detail.Roll);
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
        fixture.Contribute(new PhysicalImmunity(order));
        // Every slot clears its reflex and hit gates; an immune target leaves nothing else to draw.
        fixture.ScriptMonster(0, (50, 1, null, null), (50, 1, null, null), (50, 1, null, null));

        PreparedResolution result = fixture.Run(new AttackRequest(2, DaggerfallActorIdentity.PlayerEntityId, 5, 9, .125d, Delayed: true));

        Assert.Equal((true, false, 0), (result.Outcome.Hit, result.Outcome.Allowed, result.Outcome.Damage));
        Assert.All(result.Ranges, range => Assert.True(range is (0, 19) or (1, 100), $"An immune target drew {range}."));
        Assert.Equal(["hit", "negate", "hit", "hit", "contact"], order);
    }

    private sealed class PhysicalImmunity(List<string> order) : ICombatContribution
    {
        public void Hit(TryHitEvent interaction) => order.Add("hit");
        public void Contact(ContactEvent interaction) => order.Add("contact");
        public void Negate(NegationEvent interaction)
        {
            order.Add("negate");
            if (interaction.Channel == DaggerfallHarmChannels.Physical) interaction.Negation = HarmNegation.Immune;
        }
        public void Damage(DamageEvent interaction) => order.Add("damage");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_monster_whose_slots_all_fail_reports_a_miss_without_damage(bool evaded)
    {
        using DamagePolicyFixture fixture = new();
        fixture.ReplaceActor(2, Definitions.RequireActor(new DaggerfallActorId("rat")) with
        {
            Attacks = [new DaggerfallAttackRange(1, 8), new(1, 8), new(1, 10)],
        });
        var slot = (Reflex: (int?)(evaded ? 100 : 1), Hit: evaded ? null : (int?)100, Damage: (int?)null, Consequence: (int?)null);
        fixture.ScriptMonster(0, slot, slot, slot);
        CountingContribution contribution = new();
        fixture.Contribute(contribution);
        PreparedResolution result = fixture.Run(new AttackRequest(2, DaggerfallActorIdentity.PlayerEntityId, 5, 9, .125d, Delayed: true));

        Assert.True(result.Admitted);
        Assert.False(result.Outcome.Hit);
        Assert.Equal(0, result.Outcome.Damage);
        Assert.Equal(evaded ? 0 : 3, contribution.HitCount);
        Assert.Equal(0, contribution.DamageCount);
    }

    [Fact]
    public void Each_monster_slot_uses_hit_contributions_and_the_combined_damage_is_resolved_once()
    {
        using DamagePolicyFixture fixture = new();
        fixture.ReplaceActor(2, Definitions.RequireActor(new DaggerfallActorId("rat")) with
        {
            Attacks = [new DaggerfallAttackRange(1, 8), new(1, 8), new(1, 10)],
        });
        CountingContribution contribution = new(rejectFirst: true);
        fixture.Contribute(contribution);
        fixture.ScriptMonster(0, (1, 1, null, null), (1, 1, 5, 100), (1, 1, 6, 100));
        PreparedResolution result = fixture.Run(new AttackRequest(2, DaggerfallActorIdentity.PlayerEntityId, 5, 9, .125d, Delayed: true));

        Assert.True(result.Outcome.Hit);
        Assert.Equal(11, result.Outcome.Damage);
        Assert.Equal(3, contribution.HitCount);
        Assert.Equal(1, contribution.DamageCount);
    }

    [Theory]
    [InlineData("redguard", false, 0)]
    [InlineData("dark-elf", false, 0)]
    [InlineData("redguard", true, 4)]
    [InlineData("dark-elf", true, 3)]
    public void Racial_weapon_bonuses_require_an_equipped_weapon(string race, bool armed, int bonus)
    {
        PreparedResolution Attack(string selectedRace)
        {
            using DamagePolicyFixture fixture = new(armed);
            fixture.PlayerRace(selectedRace, 12);
            fixture.Script(body: 0, critical: 100, hit: 1, damage: 3);
            return fixture.Run(new AttackRequest(DaggerfallActorIdentity.PlayerEntityId, 2, 1, 1, .125d, Delayed: false));
        }

        PreparedResolution baseline = Attack("breton");
        PreparedResolution racial = Attack(race);
        Assert.True(racial.Outcome.Hit);
        Assert.Equal(baseline.Detail.Chance + bonus, racial.Detail.Chance);
        Assert.Equal(baseline.Outcome.Damage + bonus, racial.Outcome.Damage);
    }

    [Fact]
    public void A_targeted_player_swing_admits_its_timing_and_lands_only_when_the_hit_frame_arrives()
    {
        // The donor runs its melee damage when the swing animation reaches FPSWeapon's hit frame, so a
        // targeted swing hands its impact to the shared pending state and publishes the tick time the
        // viewmodel has to play at.
        using DamagePolicyFixture fixture = new();
        fixture.Script(body: 9, critical: 50, hit: 1, damage: 10);
        AttackRequest request = new(DaggerfallActorIdentity.PlayerEntityId, 2, 5, 9, .125d, Delayed: true);

        IReadOnlyList<IProductFact> admitted = fixture.StartSwing(request, out bool started);

        Assert.True(started);
        PlayerAttackStartedFact opening = Assert.Single(admitted.OfType<PlayerAttackStartedFact>());
        Assert.Equal(2L, opening.TargetId);
        // Stated from the donor's own arithmetic rather than read back from the formula the rules
        // call: 3 * (115 - 50) / 980 seconds per frame.
        Assert.Equal(3d * (115 - 50) / 980d, opening.FrameSeconds, 6);
        Assert.DoesNotContain(admitted, fact => fact is AttackHitFact or AttackMissedFact);

        IReadOnlyList<IProductFact> impact = fixture.DeliverImpact(request);

        // One admitted swing delivers one impact, however many animation frames follow it.
        Assert.Equal(2L, Assert.Single(impact.OfType<AttackHitFact>()).TargetId);
        Assert.Empty(fixture.DeliverImpact(request));
    }

    [Theory]
    [InlineData("StrikeUp")]
    [InlineData("StrikeDown")]
    [InlineData("StrikeDownLeft")]
    [InlineData("StrikeDownRight")]
    [InlineData("StrikeLeft")]
    [InlineData("StrikeRight")]
    public void Bow_modifiers_are_neutral_and_do_not_consume_the_melee_gesture(string direction)
    {
        DaggerfallSwingDirection gesture = Enum.Parse<DaggerfallSwingDirection>(direction);
        PreparedResolution Shot(DaggerfallSwingDirection drawn)
        {
            using DamagePolicyFixture fixture = new(armed: false);
            fixture.EquipPlayerBow("iron-short-bow", 3001);
            fixture.GivePlayerArrows(1);
            fixture.Gesture = drawn;
            fixture.Script(body: 9, critical: 50, hit: 1, damage: 10);
            var result = fixture.Run(new AttackRequest(1, 2, 1, 1, .125d, Delayed: true), out var facts);
            Assert.Equal(drawn, fixture.Gesture);
            Assert.Equal(DaggerfallSwingDirection.StrikeDown, Assert.Single(facts.OfType<PlayerAttackStartedFact>()).Swing);
            return result;
        }
        PreparedResolution neutral = Shot(DaggerfallSwingDirection.None);
        PreparedResolution swung = Shot(gesture);
        Assert.Equal(neutral.Detail.Chance, swung.Detail.Chance);
        Assert.Equal(neutral.Outcome.Damage, swung.Outcome.Damage);
    }

    [Theory]
    [InlineData("StrikeUp", 10, -4)]
    [InlineData("StrikeDown", -10, 4)]
    [InlineData("StrikeLeft", 0, 0)]
    public void A_player_swing_publishes_the_direction_it_rolls_with(string direction, int toHit, int damage)
    {
        DaggerfallSwingDirection gesture = Enum.Parse<DaggerfallSwingDirection>(direction);
        // The gesture is read once per swing: the strike the viewmodel plays and the swing modifiers the
        // rolls carry describe the same swing, and the swing consumes it.
        PreparedResolution Attack(DaggerfallSwingDirection drawn, out PlayerAttackStartedFact started)
        {
            using DamagePolicyFixture fixture = new();
            fixture.Gesture = drawn;
            fixture.Script(body: 0, critical: 100, hit: 1, damage: 10);
            PreparedResolution result = fixture.Run(new AttackRequest(DaggerfallActorIdentity.PlayerEntityId, 2, 1, 1, .125d, Delayed: true),
                out IReadOnlyList<IProductFact> published);
            Assert.Equal(DaggerfallSwingDirection.None, fixture.Gesture);
            started = Assert.Single(published.OfType<PlayerAttackStartedFact>());
            return result;
        }

        PreparedResolution baseline = Attack(DaggerfallSwingDirection.None, out PlayerAttackStartedFact straight);
        PreparedResolution swung = Attack(gesture, out PlayerAttackStartedFact drawnSwing);

        Assert.Equal(DaggerfallSwingDirection.None, straight.Swing);
        Assert.Equal(gesture, drawnSwing.Swing);
        Assert.Equal(DaggerfallFormulaPolicy.MeleeWeaponHitFrame, drawnSwing.HitFrame);
        Assert.Equal(baseline.Detail.Chance + toHit, swung.Detail.Chance);
        Assert.Equal(baseline.Outcome.Damage + damage, swung.Outcome.Damage);
    }

    [Fact]
    public void A_swing_whose_animation_never_reaches_its_hit_frame_delivers_nothing()
    {
        using DamagePolicyFixture fixture = new();
        fixture.Script(body: 9, critical: 50, hit: 1, damage: 10);
        AttackRequest request = new(DaggerfallActorIdentity.PlayerEntityId, 2, 5, 9, .125d, Delayed: true);
        _ = fixture.StartSwing(request, out bool started);
        Assert.True(started);

        IReadOnlyList<IProductFact> impact = fixture.DeliverImpact(request, expired: true);

        Assert.DoesNotContain(impact, fact => fact is AttackHitFact or AttackMissedFact or EquipmentWornFact);
        // A delivered or expired swing leaves nothing pending, so the next admitted swing is ready.
        Assert.True(fixture.IsSwingReady(DaggerfallActorIdentity.PlayerEntityId, 5, 40));
    }

    [Fact]
    public void An_unaimed_player_swing_resolves_immediately_and_reports_nothing_in_reach()
    {
        // No target means no animation frame to wait for: the swing resolves in its own update, spends
        // its stamina once, and says what the melee query found.
        using DamagePolicyFixture fixture = new();
        AttackRequest request = new(DaggerfallActorIdentity.PlayerEntityId, null, 5, 9, .125d, Delayed: false);

        IReadOnlyList<IProductFact> facts = fixture.StartSwing(request, out bool started);

        Assert.True(started);
        Assert.Equal(new PlayerAttackStartedFact(5, 9) { Feedback = new(true, "sound.347") },
            Assert.Single(facts.OfType<PlayerAttackStartedFact>()));
        Assert.Contains(new AttackRejectedFact(AttackRejection.NoTargetInReach), facts);
    }

    [Fact]
    public void A_cancelled_swing_never_lands_and_keeps_the_cadence_it_already_spent()
    {
        // One admitted swing latches its cooldown at admission. When the swing is cancelled before its
        // impact frame — leaving reach, losing sight, dying or unloading — only the damage is cancelled;
        // the charge stands, and the next swing waits for the authored cadence like any other.
        using DamagePolicyFixture fixture = new();
        fixture.EquipNpcWeapon(2, "iron-longsword", 9001);
        fixture.Script(body: 3, critical: 1, hit: 1, damage: 7);
        AttackRequest request = new(2, DaggerfallActorIdentity.PlayerEntityId, 5, 9, .125d, Delayed: true);

        IReadOnlyList<IProductFact> admitted = fixture.StartSwing(request, out bool started);
        Assert.True(started);
        Assert.DoesNotContain(admitted, fact => fact is AttackHitFact or AttackMissedFact);
        Assert.False(fixture.IsSwingReady(2, 5, 500));

        fixture.CancelSwing(2, 5);

        Assert.False(fixture.IsSwingReady(2, 5, 9));      // the cancellation costs the charge
        Assert.True(fixture.IsSwingReady(2, 5, 500));      // and the charge elapses on its authored cadence
        Assert.Empty(fixture.DeliverImpact(request).Where(fact => fact is AttackHitFact or AttackMissedFact));
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
        fixture.Script(body: 9, critical: 50, hit: 1, damage: 10);
        AttackRequest request = new(DaggerfallActorIdentity.PlayerEntityId, 2, 5, 9, .125d, Delayed: true);

        IReadOnlyList<IProductFact> admitted = fixture.StartSwing(request, out bool started);

        Assert.True(started);
        PlayerAttackStartedFact opening = Assert.Single(admitted.OfType<PlayerAttackStartedFact>());
        Assert.Equal(2L, opening.TargetId);
        Assert.Equal(5, opening.HitFrame);   // the donor's bow animation releases on frame 5
        // WeaponManager forces a bow's attack direction to Down, which FPSWeapon plays as StrikeDown.
        Assert.Equal(DaggerfallSwingDirection.StrikeDown, opening.Swing);
        Assert.Equal(2UL, fixture.PlayerArrows());
        // (10 * (100 - 50) + 800) / 980 seconds, latched in 0.125s steps.
        Assert.Equal((ulong)Math.Ceiling((10d * (100 - 50) + 800) / 980d / .125d), fixture.CooldownRemaining(DaggerfallActorIdentity.PlayerEntityId, 5, 9));
        Assert.DoesNotContain(admitted, fact => fact is AttackHitFact or AttackMissedFact);

        // The release is queued for flight, and the arrival is what lands the hit.
        Assert.Empty(fixture.DeliverImpact(request));
        IReadOnlyList<IProductFact> arrived = fixture.AdvanceFlight(5, 10);
        Assert.Single(arrived.OfType<AttackHitFact>());
    }

    [Fact]
    public void A_strengthened_armor_value_makes_the_worn_player_harder_to_hit()
    {
        // StrengthensArmor shifts the wearer's armor value down by five, and the classic hit chance
        // adds that value: a lower value must mean a lower chance to hit, not a higher one.
        using DamagePolicyFixture plain = new(armed: true);
        plain.EquipNpcWeapon(2, "iron-longsword", 9001);
        plain.Script(body: 5, critical: 50, hit: 1, damage: 7);
        PreparedResolution unarmoured = plain.Run(new AttackRequest(2, DaggerfallActorIdentity.PlayerEntityId, 5, 9, .125d, Delayed: true));

        using DamagePolicyFixture strengthened = new(armed: true, armorValueShift: -5);
        strengthened.EquipNpcWeapon(2, "iron-longsword", 9001);
        strengthened.Script(body: 5, critical: 50, hit: 1, damage: 7);
        PreparedResolution armoured = strengthened.Run(new AttackRequest(2, DaggerfallActorIdentity.PlayerEntityId, 5, 9, .125d, Delayed: true));

        Assert.True(unarmoured.Admitted && armoured.Admitted);
        Assert.Equal(unarmoured.Detail.Chance - 5, armoured.Detail.Chance);
        Assert.Equal(unarmoured.Outcome.Hit, armoured.Outcome.Hit);
    }

    [Fact]
    public void Bad_reactions_reduce_player_attack_chance_without_penalizing_the_enemy()
    {
        using DamagePolicyFixture plain = new();
        using DamagePolicyFixture penalized = new(attackChanceShift: -10);
        AttackRequest playerAttack = new(DaggerfallActorIdentity.PlayerEntityId, 2, 5, 9, .125d, Delayed: true);
        plain.Script(body: 5, critical: 50, hit: 1, damage: 7);
        penalized.Script(body: 5, critical: 50, hit: 1, damage: 7);
        Assert.Equal(plain.Run(playerAttack).Detail.Chance - 10, penalized.Run(playerAttack).Detail.Chance);
        plain.EquipNpcWeapon(2, "iron-longsword", 9001);
        penalized.EquipNpcWeapon(2, "iron-longsword", 9001);
        AttackRequest enemyAttack = new(2, DaggerfallActorIdentity.PlayerEntityId, 5, 9, .125d, Delayed: true);
        plain.Script(body: 5, critical: 50, hit: 1, damage: 7);
        penalized.Script(body: 5, critical: 50, hit: 1, damage: 7);
        Assert.Equal(plain.Run(enemyAttack).Detail.Chance, penalized.Run(enemyAttack).Detail.Chance);
    }

    [Fact]
    public void A_shot_that_meets_cover_lands_nothing_and_says_so()
    {
        // Static geometry on the release line stops the missile before the target: neither the roll
        // nor the aim decides anything, so nothing about the target is damaged or reported as a miss.
        using DamagePolicyFixture fixture = new(armed: false, coverBlocks: (origin, aim) => origin != aim);
        fixture.EquipPlayerBow("iron-short-bow", 3001);
        fixture.GivePlayerArrows(3);
        fixture.Script(body: 9, critical: 50, hit: 1, damage: 10);
        AttackRequest request = new(DaggerfallActorIdentity.PlayerEntityId, 2, 5, 9, .125d, Delayed: true);
        fixture.StartSwing(request, out bool started);
        Assert.True(started);

        Assert.Empty(fixture.DeliverImpact(request));
        IReadOnlyList<IProductFact> arrived = fixture.AdvanceFlight(5, 10);

        RangedShotBlockedFact blocked = Assert.Single(arrived.OfType<RangedShotBlockedFact>());
        Assert.Equal(DaggerfallActorIdentity.PlayerEntityId, blocked.AttackerId);
        Assert.Equal(2L, blocked.TargetId);
        Assert.Empty(arrived.OfType<AttackHitFact>());
        Assert.Empty(arrived.OfType<AttackMissedFact>());
    }

    [Fact]
    public void A_player_bow_shot_with_an_empty_quiver_is_refused_before_it_is_admitted()
    {
        using DamagePolicyFixture fixture = new(armed: false);
        fixture.EquipPlayerBow("iron-short-bow", 3001);
        fixture.Script(body: 9, critical: 50, hit: 1, damage: 10);

        IReadOnlyList<IProductFact> facts = fixture.StartSwing(
            new AttackRequest(DaggerfallActorIdentity.PlayerEntityId, 2, 5, 9, .125d, Delayed: true), out bool started);

        Assert.False(started);
        Assert.Contains(new AttackRejectedFact(AttackRejection.EmptyQuiver, DaggerfallActorIdentity.PlayerEntityId), facts);
        Assert.DoesNotContain(facts, fact => fact is PlayerAttackStartedFact);
        Assert.True(fixture.IsSwingReady(DaggerfallActorIdentity.PlayerEntityId, 5, 500));
    }

    [Fact]
    public void A_held_bow_carries_the_authored_ranged_reach_and_a_swing_keeps_its_own()
    {
        using DamagePolicyFixture fixture = new(armed: false);

        Assert.Equal(2.25d, fixture.ReachOf(DaggerfallActorIdentity.PlayerEntityId));

        fixture.EquipPlayerBow("iron-short-bow", 3001);

        Assert.Equal(10d, fixture.ReachOf(DaggerfallActorIdentity.PlayerEntityId));
    }

    private sealed class CountingContribution(bool rejectFirst = false) : ICombatContribution
    {
        internal int HitCount { get; private set; }
        internal int DamageCount { get; private set; }
        public void Hit(TryHitEvent interaction)
        {
            HitCount++;
            if (rejectFirst && HitCount == 1) interaction.Hit = false;
        }
        public void Damage(DamageEvent interaction) => DamageCount++;
    }

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
        /// <summary>The gesture the next swing reads; reading it consumes it, as the session's tracker does.</summary>
        internal DaggerfallSwingDirection Gesture { get; set; }
        private DaggerfallSwingDirection TakeGesture()
        {
            DaggerfallSwingDirection gesture = Gesture;
            Gesture = DaggerfallSwingDirection.None;
            return gesture;
        }

        internal DamagePolicyFixture(bool armed = true, Func<WorldPoint, WorldPoint, bool>? coverBlocks = null, int armorValueShift = 0, int attackChanceShift = 0,
            IDaggerfallPhysicalMitigation? mitigation = null, Func<IDaggerfallPhysicalHarmOperations, IDaggerfallPhysicalDelivery>? delivery = null,
            IRandomService? random = null, DaggerfallRulesVariant? variant = null)
        {
            DaggerfallDefinitions definitions = Definitions;
            DaggerfallActorDefinition playerDefinition = definitions.RequireActor(new DaggerfallActorId("player"));
            _random = random ?? (IRandomService)(object)_scripted;
            ArmorValueShift = armorValueShift;
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
            Owners = variant?.Compose(VariantContext(variant));
            _combat = new DaggerCombatRules(_random, _actors, _playerEquipment,
                id => _actorInventories.TryGetValue(id, out MechanicsInventoryCoordinator? quiver) ? quiver : null,
                _itemInstances, definitions, _authored, CombatCollaborators.Targeting(_actors, _authored, out _spatial, out SpatialFake spatialFake),
                skillUses: SkillUses.Add, playerBiographyAvoidHit: () => 0,
                actorEquipment: id => _actorEquipment.TryGetValue(id, out MechanicsEquipmentCoordinator? coordinator) ? coordinator : _playerEquipment,
                itemCondition: _itemCondition, rules: new CombatResolution(), adrenalineRush: _ => default,
                playerPosition: () => _playerPosition, character: () => _character, playerSwing: TakeGesture,
                coverBlocksShot: coverBlocks ?? ((_, _) => false), armorValueModifier: () => ArmorValueShift,
                deliverWeaponPoison: (_, _) => throw new InvalidOperationException("This fixture coats no weapon."),
                attackChanceModifier: () => attackChanceShift,
                transformActor: (_, target, _, _, _) => new DaggerfallWabbajackResult(DaggerfallWabbajackOutcome.InvalidTarget, target),
                magicDefense: _ => DaggerfallMagicDefense.None, physicalAttacksBlocked: _ => false,
                molagBalStrike: (_, _, _, _, _, _, _) => throw new InvalidOperationException("This fixture arms no Mace of Molag Bal."),
                itemStrike: (_, _, _, damage) => damage,
                monsterHit: _ => { }, actorGameplayActive: _ => true, actorTeam: id => _authored.GetValueOrDefault(id)?.Team,
                mitigation: Owners?.PhysicalMitigation ?? mitigation,
                delivery: ops => Delivery = (Owners?.PhysicalDelivery ?? delivery)?.Invoke(ops) ?? new DaggerfallClassicPhysicalDelivery(ops),
                contact: Contact = new ActorContact(_actors, _spatial, () => new PlayerControlState(_playerPosition ?? new WorldPoint(0f, 0f, 0f), 0f, 0f),
                    DaggerfallSession.ActorContactBody, _ => true),
                strikePose: StrikePose,
                actorVelocity: id => id == DaggerfallActorIdentity.PlayerEntityId ? PlayerVelocity : Vector3.Zero);
            Spatial = spatialFake;
        }

        internal void Script(int body, int critical, int hit, int? damage = null, int? backstabRoll = null)
        {
            List<int> draws = [body, critical, hit];
            if (damage is int weaponDamage) draws.Add(weaponDamage);
            if (backstabRoll is int backstab) draws.Add(backstab);
            _scripted.Feed(draws);
        }

        internal void ScriptMonster(int body, params (int? Reflex, int? Hit, int? Damage, int? Consequence)[] slots)
        {
            List<int> draws = [body];
            foreach (var slot in slots)
            {
                if (slot.Reflex is int reflex) draws.Add(reflex);
                if (slot.Hit is int hit) draws.AddRange([1, hit]); // critical, then hit
                if (slot.Damage is int damage) draws.Add(damage);
                if (slot.Consequence is int consequence) draws.Add(consequence);
            }
            _scripted.Feed(draws);
        }

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

        /// <summary>The physical delivery owner the rules composed.</summary>
        internal IDaggerfallPhysicalDelivery Delivery { get; private set; } = null!;

        /// <summary>The player's controller velocity, which an archer may lead.</summary>
        internal Vector3 PlayerVelocity { get; set; }

        /// <summary>The player's worn armor-value shift, as held enchantments would give it.</summary>
        internal int ArmorValueShift { get; set; }

        /// <summary>The variant's composed owners, when the fixture runs under one.</summary>
        internal DaggerfallSeamOwners? Owners { get; }

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

        /// <summary>The player strikes from its eye facing +X, toward the brigand; a site actor from near the top of its body along its heading.</summary>
        private DaggerfallStrikePose? StrikePose(long actor) => actor == DaggerfallActorIdentity.PlayerEntityId
            ? new((_playerPosition ?? new WorldPoint(0f, 0f, 0f)).ToVector() + new Vector3(0f, 1.65f, 0f), Vector3.UnitX)
            : _actors.TryGet(actor, out ActorState site) ? new(site.Position.ToVector() + new Vector3(0f, 1.62f, 0f), ActorHeading.Forward(site.HeadingYawRadians)) : null;

        internal DaggerfallSeamContext VariantContext(DaggerfallRulesVariant variant) => DaggerfallContactVariantTests.Context(variant, SeamInputs);

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
                _combat.AdvanceRangedFlight(generation, checked(step + (ulong)index), .125d, positions, facts);
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

        internal void PutPlayerBehindTarget(long targetEntityId, bool facingAway = true)
        {
            _playerPosition = new WorldPoint(0f, 0f, 0f);
            _actors.Get(targetEntityId).ApplyPose(new ActorPose(new WorldPoint(0f, 0f, -1f), facingAway ? 0f : MathF.PI));
        }

        internal PreparedResolution Run(AttackRequest request) => Run(request, out _);

        internal PreparedResolution Run(AttackRequest request, out IReadOnlyList<IProductFact> published)
        {
            FactBuffer<IProductFact> facts = new();
            bool admitted = _combat.TryPrepare(request, facts, out PreparedAttack prepared);
            published = Delivered(facts);
            return new PreparedResolution(admitted, prepared.Outcome,
                (prepared as DaggerCombatRules.DaggerfallPreparedAttack)?.Detail ?? default, _scripted.Ranges);
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

    internal sealed record PreparedResolution(bool Admitted, AttackOutcome Outcome, DaggerCombatRules.DaggerfallAttackDetail Detail,
        IReadOnlyList<(int Minimum, int Maximum)> Ranges);

    private class ScriptedRandom : DispatchProxy
    {
        private readonly List<int> _values = [];
        private readonly List<(int Minimum, int Maximum)> _ranges = [];
        private int _next;

        internal void Feed(IEnumerable<int> values) => _values.AddRange(values);

        internal IReadOnlyList<(int Minimum, int Maximum)> Ranges => _ranges;

        protected override object? Invoke(MethodInfo? method, object?[]? arguments)
        {
            if (method?.Name != nameof(IRandomService.DrawKeyed)) throw new NotSupportedException(method?.Name);
            KeyedRngRequest request = (KeyedRngRequest)arguments![0]!;
            _ranges.Add(((int)request.Minimum, (int)request.Maximum));
            if (_next >= _values.Count) throw new InvalidOperationException($"The scripted attack reached draw {_next + 1} with no scripted value.");
            int value = _values[_next++];
            if (value < request.Minimum || value > request.Maximum)
                throw new InvalidOperationException($"Scripted draw {value} is outside [{request.Minimum}, {request.Maximum}].");
            return new KeyedRngReceipt(value);
        }
    }

}
