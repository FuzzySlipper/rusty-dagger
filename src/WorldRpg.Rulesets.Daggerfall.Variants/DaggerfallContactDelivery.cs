using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Facts;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.Policies;
using Combatant = WorldRpg.Rulesets.Daggerfall.Modules.Combat.DaggerCombatRules.Combatant;
using PreparedPhysicalAttack = WorldRpg.Rulesets.Daggerfall.Modules.Combat.DaggerCombatRules.DaggerfallPreparedAttack;

namespace WorldRpg.Rulesets.Daggerfall.Variants;

/// <summary>
/// Physical delivery under the contact + mitigation variant. There is no to-hit roll: a blow that
/// physically connects has hit, and the target's mitigation decides what it takes.
/// <list type="bullet">
/// <item>A melee swing, targeted or not, waits for its hit frame and reaches every actor body its swing
/// volume touches there, which may be an actor it was not aimed at, or several.</item>
/// <item>An arrow flies segment by segment each admitted step from the shooter's eye, and harms the first
/// actor body it meets; world geometry stops it. An enemy archer leads a moving target and holds a shot
/// another body or the world would stop.</item>
/// <item>An enemy swing's damage lands no sooner than its published wind-up.</item>
/// </list>
/// What the classic miss carried has a defined meaning here: Dodging is deflection and records a use on each
/// blow that connects with the player; Critical Strike, with a held enchantment's attack-chance shift, is a
/// chance of bonus damage; backstab keeps its chance of tripled damage from behind; a monster's natural
/// attack lands the slots that clear the player's reflexes; armour absorbs; and a whiff runs no Strikes payload.
/// </summary>
internal sealed class DaggerfallContactDelivery(IDaggerfallPhysicalHarmOperations ops, DaggerfallContactTuning tuning) : IDaggerfallPhysicalDelivery
{
    private const long PlayerId = DaggerfallActorIdentity.PlayerEntityId;
    /// <summary>The variant's own critical-strike draw, keyed beside classic's combat salts without sharing one.</summary>
    internal const int CriticalSalt = 70;
    private readonly List<DeferredAttackImpact> _released = [];
    private readonly List<Shot> _shots = [];
    // Enemy blows whose hit frame came before their wind-up ended, each with the step it may land on.
    private readonly List<(DeferredAttackImpact Impact, ulong DueStep)> _windingUp = [];

    public bool UntargetedSwingsWaitForImpact => true;
    public bool ImpactsOutliveTheirTarget => true;

    public bool HasPlayerFlight => _released.Any(shot => shot.Request.AttackerId == PlayerId) || _shots.Any(shot => shot.Release.Request.AttackerId == PlayerId);

    /// <summary>
    /// Nothing is decided before contact: admission's costs are spent, and the blow is resolved where it
    /// connects. An enemy's melee swing publishes the wind-up its damage cannot land before.
    /// </summary>
    public bool TryPrepare(DaggerfallAdmittedAttack admitted, FactBuffer<IProductFact> facts, out PreparedPhysicalAttack prepared)
    {
        prepared = admitted.Prepared(new AttackOutcome(Hit: true, Allowed: true, Damage: 0));
        AttackRequest request = admitted.Request;
        if (request.AttackerId == PlayerId || request.TargetId is not long target) return true;
        if (admitted.Ranged) return true;
        if (request.Delayed && tuning.EnemyWindUpSeconds > 0d)
            facts.Append(new EnemyAttackTelegraphFact(request.AttackerId, target, tuning.EnemyWindUpSeconds, request.Generation, request.SimulationStep));
        return true;
    }

    /// <summary>
    /// A swing still winding up belongs to the attack that was interrupted: it is dropped and never lands. A
    /// released arrow is already flying and keeps its flight.
    /// </summary>
    public void Interrupt(long attacker, ulong generation) =>
        _windingUp.RemoveAll(held => held.Impact.Request.AttackerId == attacker && held.Impact.Request.Generation == generation);

    /// <summary>An enemy archer holds a shot that another body or the world would stop before its target.</summary>
    public bool HoldsFire(long archer, long target) => !LineOfFireClear(archer, target);

    /// <summary>Whether a shot from the archer's eye at its target's would meet the target first.</summary>
    private bool LineOfFireClear(long archer, long target)
    {
        if (ops.StrikePose(archer) is not DaggerfallStrikePose from || ops.StrikePose(target) is not DaggerfallStrikePose to) return false;
        SegmentContact first = ops.Contact.Segment(archer, from.Eye, to.Eye, ops.ContactEnvironment());
        return first.Kind == ContactKind.None || first.Kind == ContactKind.Actor && first.ActorId == target;
    }

    public void Apply(AttackRequest request, PreparedPhysicalAttack attack, FactBuffer<IProductFact> facts)
    {
        DaggerfallAdmittedAttack admission = attack.Admission
            ?? throw new InvalidOperationException("A contact-delivered attack carries its admission to the impact that resolves it.");
        if (admission.Ranged)
        {
            // A shot with no animated release (an explicit attack) flies its whole range at once.
            if (Launch(new DeferredAttackImpact(request, attack)) is not Shot shot || !Fly(shot, shot.Remaining, facts)) Whiff(request, attack, facts);
            return;
        }
        if (ops.StrikePose(request.AttackerId) is not DaggerfallStrikePose pose) { Whiff(request, attack, facts); return; }
        double reach = admission.Attack.Reach ?? ops.ReachOf(request.AttackerId) ?? tuning.DefaultReach;
        // The swing sweeps a box in front of the attacker's eye, as deep as the attack's reach.
        (SpatialShape swing, SpatialPose at) = DaggerfallContactVolume.Ahead(pose.Eye, pose.Forward, tuning.SwingWidth, tuning.SwingHeight, tuning.SwingDrop, reach);
        VolumeContact touched = ops.Contact.Volume(request.AttackerId, swing, at, pose.Eye, ops.ContactEnvironment());
        if (touched.Actors.Count == 0) { Whiff(request, attack, facts); return; }
        foreach (ActorVolumeContact contact in touched.Actors) Connect(request, attack, contact.ActorId, facts);
    }

    public bool DeferImpact(DeferredAttackImpact impact, FactBuffer<IProductFact> facts)
    {
        if (!impact.Request.Delayed) return false;
        if (impact.Attack is PreparedPhysicalAttack { IsRanged: true }) { _released.Add(impact); return true; }
        // An enemy blow lands no sooner than its published wind-up, however early its strike's frame comes.
        if (impact.Request.AttackerId == PlayerId || tuning.EnemyWindUpSeconds <= 0d) return false;
        ulong windUp = (ulong)Math.Ceiling(tuning.EnemyWindUpSeconds / impact.Request.FixedDeltaSeconds - 1e-9);
        _windingUp.Add((impact, checked(impact.Request.SimulationStep + windUp)));
        return true;
    }

    /// <summary>Launches this step's releases from the shooter's current eye, then flies every arrow one step.</summary>
    public void AdvanceFlight(ulong generation, ulong simulationStep, double fixedDeltaSeconds,
        IReadOnlyDictionary<long, WorldPoint> positions, FactBuffer<IProductFact> facts)
    {
        if (!double.IsFinite(fixedDeltaSeconds) || fixedDeltaSeconds <= 0d) throw new ArgumentOutOfRangeException(nameof(fixedDeltaSeconds));
        foreach ((DeferredAttackImpact impact, ulong due) in _windingUp.ToArray())
        {
            if (impact.Request.Generation == generation && due > simulationStep) continue;
            _windingUp.Remove((impact, due));
            if (impact.Request.Generation == generation && ops.IsLiveCombatant(impact.Request.AttackerId))
                ops.Execution.ApplyDeferredImpact(impact, facts);
        }
        foreach (DeferredAttackImpact release in _released)
            if (release.Request.Generation == generation && Launch(release, simulationStep) is Shot shot) _shots.Add(shot);
        _released.Clear();
        double step = tuning.ProjectileSpeed * fixedDeltaSeconds;
        foreach (Shot shot in _shots.ToArray())
        {
            if (shot.Release.Request.Generation != generation) { _shots.Remove(shot); continue; }
            if (Fly(shot, Math.Min(step, shot.Remaining), facts)) _shots.Remove(shot);
            else if (shot.Remaining <= 0d)
            {
                _shots.Remove(shot);
                Whiff(shot.Release.Request, (PreparedPhysicalAttack)shot.Release.Attack, facts);
            }
        }
    }

    public IReadOnlyList<DaggerfallRangedFlightView> ReadFlights(ulong generation, ulong simulationStep) =>
        [.. _shots.Where(shot => shot.Release.Request.Generation == generation)
            .OrderBy(shot => shot.Release.Request.AttackerId).ThenBy(shot => shot.ReleaseStep)
            .Select(shot => new DaggerfallRangedFlightView(
                new RangedShotIdentity(generation, shot.Release.Request.AttackerId, shot.Release.Request.TargetId ?? 0, shot.ReleaseStep),
                WorldPoint.From(shot.Position), shot.Direction))];

    public void ClearFlight()
    {
        _windingUp.Clear();
        _released.Clear();
        _shots.Clear();
    }

    public void RebaseFlight(Vector3 delta)
    {
        foreach (Shot shot in _shots) shot.Position += delta;
    }

    /// <summary>An arrow's start: the shooter's eye, aimed along the player's look or at a named target's eye.</summary>
    private Shot? Launch(DeferredAttackImpact release, ulong? releaseStep = null)
    {
        AttackRequest request = release.Request;
        if (!ops.IsLiveCombatant(request.AttackerId) || ops.StrikePose(request.AttackerId) is not DaggerfallStrikePose pose) return null;
        Vector3 aim = request.AttackerId != PlayerId && request.TargetId is long target && ops.StrikePose(target) is DaggerfallStrikePose targeted
            ? Lead(pose.Eye, targeted.Eye, ops.VelocityOf(target)) - pose.Eye : pose.Forward;
        if (!float.IsFinite(aim.LengthSquared()) || aim.LengthSquared() <= 1e-6f) return null;
        var attack = (PreparedPhysicalAttack)release.Attack;
        double range = attack.Admission?.Attack.Reach ?? ops.ReachOf(request.AttackerId) ?? tuning.DefaultRange;
        return new Shot(release, pose.Eye, Vector3.Normalize(aim), range, releaseStep ?? request.SimulationStep);
    }

    /// <summary>
    /// Where an enemy archer aims to meet a moving target: its eye now, carried along its velocity for the
    /// arrow's flight time there, by the tuned share of a full lead.
    /// </summary>
    private Vector3 Lead(Vector3 from, Vector3 target, Vector3 velocity)
    {
        double flight = Vector3.Distance(from, target) / tuning.ProjectileSpeed;
        Vector3 led = target + velocity * (float)(flight * tuning.ArcherLeadFraction);
        return float.IsFinite(led.LengthSquared()) ? led : target;
    }

    /// <summary>Flies one segment; true when the shot ended by meeting an actor or the world.</summary>
    private bool Fly(Shot shot, double distance, FactBuffer<IProductFact> facts)
    {
        if (distance <= 0d) return false;
        Vector3 to = shot.Position + shot.Direction * (float)distance;
        AttackRequest request = shot.Release.Request;
        SegmentContact contact = ops.Contact.Segment(request.AttackerId, shot.Position, to, ops.ContactEnvironment());
        shot.Position = to;
        shot.Remaining -= distance;
        switch (contact.Kind)
        {
            case ContactKind.Actor:
                Connect(request, (PreparedPhysicalAttack)shot.Release.Attack, contact.ActorId!.Value, facts);
                return true;
            case ContactKind.World:
                facts.Append(new RangedShotBlockedFact(request.AttackerId, request.TargetId ?? 0, request.Generation, request.SimulationStep));
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// One blow that connected with <paramref name="targetId"/>: contact, negation before the roll, the
    /// damage roll with backstab and critical bonus (or the monster's landed slots), mitigation, then the
    /// shared application.
    /// </summary>
    private void Connect(AttackRequest request, PreparedPhysicalAttack attack, long targetId, FactBuffer<IProductFact> facts)
    {
        DaggerfallAdmittedAttack admission = attack.Admission!;
        if (!ops.TryResolve(request.AttackerId, out Combatant attacker) || !ops.TryResolve(targetId, out Combatant target)) return;
        bool enemy = admission.EnemyAttack;
        // Dodging is the player's deflection under contact, so every enemy blow that reaches the player uses it.
        if (enemy && targetId == PlayerId)
            ops.RecordSkillUse(new DaggerfallSkillUse(DaggerfallMechanicsIds.Dodging.Value, DaggerfallSkillUseReason.DodgingEnemyAttack, DaggerfallSkillUseOutcome.Attempted));
        CombatParticipants participants = ops.Participants(attacker.Id, targetId, admission.Action ?? attacker.Definition.ActionId ?? "attack");
        if (!ops.Rules.Contact(participants, true).Contact) return;
        // Every connect trains the player's weapon skill, whatever the target then does to the blow; the
        // shared application credits a blow that lands, so only a negated connect is credited here.
        string? playerSkill = attacker.Id == PlayerId ? ops.PlayerWeaponSkill() : null;
        void CreditNegatedConnect() { if (playerSkill is not null) ops.RecordPlayerWeaponHit(playerSkill); }
        void Negated(HarmNegation how) => facts.Append(new AttackNegatedFact(attacker.Id, targetId, how, enemy, request.Generation, request.SimulationStep) { Feedback = attack.Feedback });
        ExplicitMeleeRequest key = new(attacker.Id, targetId, request.Generation, request.SimulationStep, request.FixedDeltaSeconds);
        DaggerfallAttackDefinition weapon = admission.Attack;
        int body = DaggerfallFormulaPolicy.CalculateStruckBodyPart(ops.Draw(key, attacker.Id, targetId, CombatRandomKey.BodySalt, 0, 19, enemy));
        DaggerfallPhysicalHarm harm = new(attacker.Id, targetId, target.Definition, weapon.Skill, weapon.Material, body, request.Generation, request.SimulationStep);
        HarmChannelId channel = DaggerfallHarmChannels.ForWeaponSkill(weapon.Skill);
        NegationEvent negation = ops.Rules.Negate(participants, channel, value => ops.Mitigation.Negate(value, harm));
        if (negation.Negation == HarmNegation.Immune && !enemy)
            facts.Append(new AttackRejectedFact(AttackRejection.InsufficientWeaponMaterial));
        if (negation.Negated)
        {
            CreditNegatedConnect();
            // Immunity is reported by the material refusal above; a deflection is a negation the player sees and hears.
            if (negation.Negation != HarmNegation.Immune) Negated(negation.Negation);
            return;
        }

        DaggerfallMonsterHit[] slots = [];
        Action<DamageEvent> roll;
        if (attacker.Definition.Kind == DaggerfallActorKinds.Monster && weapon.Skill == DaggerfallMechanicsIds.HandToHand.Value)
        {
            (int total, slots) = MonsterSlots(key, attacker, target, enemy);
            roll = value => value.Damage = Math.Max(0, total);
        }
        else
        {
            bool backstab = ops.BackstabOpportunity(attacker, target);
            if (backstab)
                ops.RecordSkillUse(new DaggerfallSkillUse("backstabbing", DaggerfallSkillUseReason.BackstabbingOpportunity, DaggerfallSkillUseOutcome.Accepted));
            int backstabChance = DaggerfallFormulaPolicy.CalculateBackstabChance(DaggerCombatRules.ReadStat(attacker, new DaggerfallStatId("backstabbing")), backstab);
            int damageModifier = attacker.Id == PlayerId ? ops.PlayerAttackModifiers(weapon, admission.Gesture).Damage : 0;
            Action<DamageEvent> rolled = ops.DamageRoll(key, attacker, target, weapon, enemy, damageModifier, backstabChance);
            roll = value =>
            {
                rolled(value);
                if (ops.Draw(key, attacker.Id, targetId, CriticalSalt, 1, 100, enemy) <= CriticalChance(attacker))
                    value.Damage = (int)Math.Round(value.Damage * tuning.CriticalDamageMultiplier, MidpointRounding.AwayFromZero);
            };
        }
        DamageEvent damage = ops.Rules.Damage(participants, roll);
        MitigationEvent mitigation = ops.Rules.Mitigate(participants, channel, damage.Damage, value => ops.Mitigation.Mitigate(value, harm));
        if (mitigation.Negation != HarmNegation.None) { CreditNegatedConnect(); Negated(mitigation.Negation); return; }
        PreparedPhysicalAttack connected = attack with
        {
            Outcome = new AttackOutcome(Hit: true, Allowed: true, Damage: mitigation.Damage),
            Detail = new DaggerCombatRules.DaggerfallAttackDetail(body, 0, 0),
            MonsterHits = slots,
            WeaponPoison = admission.WeaponPoison,
            Harm = harm,
            Mitigated = new DaggerfallHarmMitigation(mitigation.Deflected, mitigation.Absorbed),
        };
        ops.ApplyConnectedHarm(request with { TargetId = targetId }, targetId, connected, facts);
    }

    /// <summary>
    /// Critical Strike as a percent chance of bonus damage: the attacker's skill, and for the player the
    /// attack-chance shift its held enchantments give.
    /// </summary>
    private double CriticalChance(Combatant attacker) =>
        DaggerCombatRules.ReadStat(attacker, new DaggerfallStatId("critical-strike")) * tuning.CriticalChancePerSkillPoint
        + (attacker.Id == PlayerId ? ops.AttackChanceModifier() * tuning.CriticalChancePerAttackChancePoint : 0d);

    /// <summary>
    /// A monster's natural attack under contact: each authored slot lands when it clears the player's
    /// reflexes (the defender's reaction, kept from classic), with its own damage, the career's enemy-type
    /// bonus on a positive slot, and the slot's consequence, in classic's per-slot draw order.
    /// </summary>
    private (int Total, DaggerfallMonsterHit[] Slots) MonsterSlots(ExplicitMeleeRequest key, Combatant attacker, Combatant target, bool enemy)
    {
        if (!ops.TryResolve(PlayerId, out Combatant player))
            throw new InvalidOperationException("A monster's natural attacks measure the player's reflexes, and no player combatant is available to measure.");
        int reflexChance = DaggerfallFormulaPolicy.MonsterAttackReflexChance(DaggerCombatRules.ReadStat(player, DaggerfallMechanicsIds.Reflexes));
        int enemyType = ops.EnemyTypeBonus(attacker, target);
        List<DaggerfallMonsterHit> landed = [];
        int total = 0;
        for (int slot = 0; slot < 3; slot++)
        {
            int minimum = slot < attacker.Definition.Attacks.Count ? attacker.Definition.Attacks[slot].MinimumDamage : 0;
            int reflex = ops.Draw(key, attacker.Id, target.Id, CombatRandomKey.MonsterReflexSaltBase + slot, 1, 100, enemy);
            if (reflex > reflexChance || minimum <= 0) continue;
            int slotDamage = ops.Draw(key, attacker.Id, target.Id, CombatRandomKey.MonsterDamageSaltBase + slot, minimum, attacker.Definition.Attacks[slot].MaximumDamage, enemy);
            total = checked(total + slotDamage);
            if (slotDamage <= 0) continue;
            total = checked(total + enemyType);
            int drawSlot = slot;
            landed.Add(new(slot, slotDamage, DaggerfallMonsterHitPolicy.Select(attacker.Definition.MobileId, target.Id == PlayerId,
                (low, high) => ops.Draw(key, attacker.Id, target.Id, CombatRandomKey.MonsterConsequenceSaltBase + drawSlot, low, high, enemy))));
        }
        return (total, [.. landed]);
    }

    /// <summary>A blow that touched no one: no Strikes payload runs, and the swing reports that it met nothing.</summary>
    private static void Whiff(AttackRequest request, PreparedPhysicalAttack attack, FactBuffer<IProductFact> facts) =>
        facts.Append(new AttackWhiffedFact(request.AttackerId, request.TargetId, request.AttackerId != PlayerId, request.Generation, request.SimulationStep) { Feedback = attack.Feedback });

    private sealed class Shot(DeferredAttackImpact release, Vector3 position, Vector3 direction, double remaining, ulong releaseStep)
    {
        internal DeferredAttackImpact Release { get; } = release;
        internal Vector3 Position { get; set; } = position;
        internal Vector3 Direction { get; } = direction;
        internal double Remaining { get; set; } = remaining;
        internal ulong ReleaseStep { get; } = releaseStep;
    }
}
