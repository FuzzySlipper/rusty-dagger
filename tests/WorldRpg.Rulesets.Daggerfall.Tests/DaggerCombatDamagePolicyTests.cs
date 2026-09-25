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
    public void An_unarmed_monster_fights_with_its_authored_attack_set_and_every_slot_rolls_its_own_gate()
    {
        using DamagePolicyFixture fixture = new();
        fixture.ReplaceActor(2, Definitions.RequireActor(new DaggerfallActorId("rat")) with
        {
            Attacks = [new DaggerfallAttackRange(1, 8), new(1, 8), new(1, 10)],
        });
        // Each slot independently passes reflex, critical and hit rolls before drawing damage.
        fixture.ScriptMonster(0, (50, 1, 4), (50, 1, 5), (50, 1, 6));

        PreparedResolution result = fixture.Run(new AttackRequest(2, DaggerfallActorIdentity.PlayerEntityId, 5, 9, .125d, Delayed: true));

        Assert.True(result.Outcome.Hit);
        Assert.True(result.Outcome.Allowed);
        Assert.Equal(15, result.Outcome.Damage);
        Assert.Equal(
        [
            (0, 19),
            (1, 100), (1, 100), (1, 100), (1, 8),
            (1, 100), (1, 100), (1, 100), (1, 8),
            (1, 100), (1, 100), (1, 100), (1, 10),
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
        fixture.ScriptMonster(0, (50, 1, 4), (51, null, null), (50, 1, 6));

        PreparedResolution result = fixture.Run(new AttackRequest(2, DaggerfallActorIdentity.PlayerEntityId, 5, 9, .125d, Delayed: true));

        Assert.Equal(10, result.Outcome.Damage);
        // Slot 0's damage (1, 8) is the only (1, 8) draw: slot 1 went from its reflex roll straight
        // to slot 2's, and slot 2's later (1, 10) keeps the ranges distinguishable.
        Assert.Equal(
        [
            (0, 19),
            (1, 100), (1, 100), (1, 100), (1, 8),
            (1, 100),
            (1, 100), (1, 100), (1, 100), (1, 10),
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
        // Slot 1 has no authored minimum and is skipped before any draw; slot 2's rerolled hit
        // gate (98, above the clamped chance ceiling) refuses it after its reflex roll passed.
        fixture.ScriptMonster(0, (50, 1, 3), (null, null, null), (50, 98, null));

        PreparedResolution result = fixture.Run(new AttackRequest(2, DaggerfallActorIdentity.PlayerEntityId, 5, 9, .125d, Delayed: true));

        Assert.Equal(22, result.Outcome.Damage); // (3 + 19) from slot 0 alone
        Assert.Equal(
        [
            (0, 19),
            (1, 100), (1, 100), (1, 100), (1, 3),
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
        fixture.ScriptMonster(0, (1, 100, null), (1, 1, 5), (1, 1, 6));
        PreparedResolution result = fixture.Run(new AttackRequest(2, DaggerfallActorIdentity.PlayerEntityId, 5, 9, .125d, Delayed: true));

        Assert.True(result.Outcome.Hit);
        Assert.Equal(11, result.Outcome.Damage);
        Assert.Equal(1, result.Outcome.Roll);
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
        var slot = (Reflex: (int?)(evaded ? 100 : 1), Hit: evaded ? null : (int?)100, Damage: (int?)null);
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
        fixture.ScriptMonster(0, (1, 1, null), (1, 1, 5), (1, 1, 6));
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
        AttackOutcome Attack(string selectedRace)
        {
            using DamagePolicyFixture fixture = new(armed);
            fixture.PlayerRace(selectedRace, 12);
            fixture.Script(body: 0, critical: 100, hit: 1, damage: 3);
            return fixture.Run(new AttackRequest(DaggerfallActorIdentity.PlayerEntityId, 2, 1, 1, .125d, Delayed: false)).Outcome;
        }

        AttackOutcome baseline = Attack("breton");
        AttackOutcome racial = Attack(race);
        Assert.True(racial.Hit);
        Assert.Equal(baseline.Chance + bonus, racial.Chance);
        Assert.Equal(baseline.Damage + bonus, racial.Damage);
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
        Assert.Equal(new PlayerAttackStartedFact(5, 9), Assert.Single(facts.OfType<PlayerAttackStartedFact>()));
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
        Assert.Equal(unarmoured.Outcome.Chance - 5, armoured.Outcome.Chance);
        Assert.Equal(unarmoured.Outcome.Hit, armoured.Outcome.Hit);
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
    private sealed class DamagePolicyFixture : IDisposable
    {
        private readonly ActorsState _actors;
        private readonly DaggerfallItemInstances _itemInstances = new();
        private readonly Dictionary<long, DaggerfallActorDefinition> _authored = [];
        private readonly Dictionary<long, MechanicsEquipmentCoordinator> _actorEquipment = [];
        private readonly Dictionary<long, MechanicsInventoryCoordinator> _actorInventories = [];
        private readonly MechanicsEquipmentCoordinator _playerEquipment;
        private readonly ScriptedRandom _scripted = (ScriptedRandom)(object)DispatchProxy.Create<IRandomService, ScriptedRandom>();
        private readonly IRandomService _random;
        private readonly DaggerCombatRules _combat;
        private WorldPoint? _playerPosition;

        private DaggerfallCharacterState? _character;

        internal DamagePolicyFixture(bool armed = true, Func<WorldPoint, WorldPoint, bool>? coverBlocks = null, int armorValueShift = 0)
        {
            DaggerfallDefinitions definitions = Definitions;
            DaggerfallActorDefinition playerDefinition = definitions.RequireActor(new DaggerfallActorId("player"));
            _random = (IRandomService)(object)_scripted;
            _actors = new ActorsState();
            PlayerActorState player = _actors.CreatePlayer(DaggerfallActorIdentity.PlayerEntityId, new EntityTypeId(playerDefinition.Id.Value),
                new DaggerfallMechanicsState().CreateStats(playerDefinition, DaggerfallPlayerVitals.Initial(playerDefinition.Stats, definitions.Catalogs.RequireCareer("class00"))), "health");
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
                new DaggerfallMechanicsState().CreateStats(brigand, new DaggerfallVitalValues(200, 100, 0)), new ActorPose(new WorldPoint(1f, 0f, 0f), 0f), "health");
            _authored[DaggerfallActorIdentity.PlayerEntityId] = playerDefinition;
            _authored[2] = brigand;
            _actorEquipment[2] = BuildEquipment(enemy.Actor.Entity, 2, enemy.Actor);
            _combat = new DaggerCombatRules(_random, _actors, _playerEquipment,
                id => _actorInventories.TryGetValue(id, out MechanicsInventoryCoordinator? quiver) ? quiver : null,
                _itemInstances, definitions, _authored, null!,
                actorEquipment: id => _actorEquipment.TryGetValue(id, out MechanicsEquipmentCoordinator? coordinator) ? coordinator : _playerEquipment,
                playerPosition: () => _playerPosition, character: () => _character, coverBlocksShot: coverBlocks,
                armorValueModifier: () => armorValueShift);
        }

        internal void Script(int body, int critical, int hit, int? damage = null, int? backstabRoll = null)
        {
            List<int> draws = [body, critical, hit];
            if (damage is int weaponDamage) draws.Add(weaponDamage);
            if (backstabRoll is int backstab) draws.Add(backstab);
            _scripted.Feed(draws);
        }

        internal void ScriptMonster(int body, params (int? Reflex, int? Hit, int? Damage)[] slots)
        {
            List<int> draws = [body];
            foreach (var slot in slots)
            {
                if (slot.Reflex is int reflex) draws.Add(reflex);
                if (slot.Hit is int hit) draws.AddRange([1, hit]); // critical, then hit
                if (slot.Damage is int damage) draws.Add(damage);
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

        internal void GivePlayerArrows(ulong quantity) =>
            _actorInventories[DaggerfallActorIdentity.PlayerEntityId].Grant(
                new InventoryGrant(new InventoryItemId("arrow"), InventoryStackId.Parse("test.quiver"), quantity));

        internal ulong PlayerArrows() => _actorInventories[DaggerfallActorIdentity.PlayerEntityId].Read().Stacks
            .Where(stack => stack.Definition.Value == "arrow").Aggregate(0UL, (total, stack) => total + stack.Quantity);

        internal double? ReachOf(long actorId) => _combat.ReachOf(actorId);

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

        internal PreparedResolution Run(AttackRequest request)
        {
            FactBuffer<IProductFact> facts = new();
            bool admitted = _combat.TryPrepare(request, facts, out PreparedAttack prepared);
            return new PreparedResolution(admitted, prepared.Outcome, _scripted.Ranges);
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
            _combat.Execution.ApplyImpacts([new AttackImpactNotice(request.AttackerId, request.TargetId!.Value,
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

        public void Dispose() => _actors.Dispose();

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

    private sealed record PreparedResolution(bool Admitted, AttackOutcome Outcome, IReadOnlyList<(int Minimum, int Maximum)> Ranges);

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
