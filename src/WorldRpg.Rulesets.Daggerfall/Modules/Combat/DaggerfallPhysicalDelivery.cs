using System.Numerics;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Facts;
using WorldRpg.Kit.Inventory;
using UniqueInventoryItem = WorldRpg.Kit.Inventory.UniqueInventoryItem;
using EquipmentRead = WorldRpg.Kit.Inventory.EquipmentRead;
using EquipmentAssignment = WorldRpg.Kit.Inventory.EquipmentAssignment;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Policies;
using Combatant = WorldRpg.Rulesets.Daggerfall.Modules.Combat.DaggerCombatRules.Combatant;
using PreparedPhysicalAttack = WorldRpg.Rulesets.Daggerfall.Modules.Combat.DaggerCombatRules.DaggerfallPreparedAttack;

namespace WorldRpg.Rulesets.Daggerfall.Modules.Combat;

/// <summary>
/// One attack as admission accepted it, before delivery: the attacker and its attack, the costs already
/// spent, and the sources captured at the admitted boundary. Every delivery owner starts from this.
/// </summary>
internal sealed record DaggerfallAdmittedAttack(
    AttackRequest Request,
    Combatant Attacker,
    DaggerfallAttackDefinition Attack,
    DaggerfallSwingDirection Gesture,
    bool Ranged,
    string? Action,
    DaggerfallStrikeFeedback Feedback,
    ulong? WabbajackSource,
    (UniqueInventoryItem Weapon, ulong Identity)? MolagBalSource,
    DaggerfallWeaponPoisonSource? WeaponPoison,
    DaggerfallItemStrikeSource? StrikeSource,
    UniqueInventoryItem? RangedWeapon)
{
    internal bool EnemyAttack => Request.AttackerId != DaggerfallActorIdentity.PlayerEntityId;

    /// <summary>The prepared attack carrying this admission's sources, with the delivery owner's outcome.</summary>
    internal PreparedPhysicalAttack Prepared(AttackOutcome outcome) => new(Attack.CooldownSeconds, outcome, Feedback, WabbajackSource,
        MolagBalSource: MolagBalSource, ItemStrikeSource: StrikeSource, IsRanged: Ranged, RangedWeapon: RangedWeapon, Action: Action, Admission: this);
}

/// <summary>
/// The physical delivery seam (<see cref="DaggerfallRulesSeams.PhysicalDelivery"/>): when an admitted
/// physical attack's harm is decided and whom it reaches. The owner resolves harm through the shared
/// <see cref="IDaggerfallPhysicalHarmOperations"/> and the selected physical mitigation owner, so
/// contributions, consequences and facts are the same under every owner.
/// </summary>
internal interface IDaggerfallPhysicalDelivery
{
    /// <summary>Whether a swing that found no target still waits for its hit frame, where contact decides whom it reaches.</summary>
    bool UntargetedSwingsWaitForImpact { get; }

    /// <summary>Prepares an admitted attack's delivery; false refuses it, with the reason already published.</summary>
    bool TryPrepare(DaggerfallAdmittedAttack admitted, FactBuffer<IProductFact> facts, out PreparedPhysicalAttack prepared);

    /// <summary>Delivers a prepared attack at its impact.</summary>
    void Apply(AttackRequest request, PreparedPhysicalAttack attack, FactBuffer<IProductFact> facts);

    /// <summary>Takes a released projectile into the owner's flight; false when the impact is not a flight.</summary>
    bool DeferImpact(DeferredAttackImpact impact, FactBuffer<IProductFact> facts);

    /// <summary>Advances every flight one admitted step and delivers what arrives.</summary>
    void AdvanceFlight(ulong generation, ulong simulationStep, double fixedDeltaSeconds,
        IReadOnlyDictionary<long, WorldPoint> positions, FactBuffer<IProductFact> facts);

    /// <summary>The current flight poses the world appearance draws.</summary>
    IReadOnlyList<DaggerfallRangedFlightView> ReadFlights(ulong generation, ulong simulationStep);

    bool HasPlayerFlight { get; }

    /// <summary>A site transition retires every flight.</summary>
    void ClearFlight();

    void RebaseFlight(Vector3 delta);
}

/// <summary>
/// The shared physical harm machinery every delivery owner resolves and applies harm through: actors,
/// draws, the damage roll, consequences, artifacts, wear, skill uses and facts.
/// </summary>
internal interface IDaggerfallPhysicalHarmOperations
{
    CombatResolution Rules { get; }
    IDaggerfallPhysicalMitigation Mitigation { get; }
    AttackExecution<IProductFact> Execution { get; }
    DaggerfallDefinitions Catalog { get; }
    bool TryResolve(long id, out Combatant combatant);
    bool IsDefeated(long id);
    /// <summary>Whether an actor is present, gameplay-active, defined and alive.</summary>
    bool IsLiveCombatant(long id);
    /// <summary>The attacker's authored default action, which names its harm when the request names none.</summary>
    string? DefaultAction(long attacker);
    CombatParticipants Participants(long attacker, long target, string action);
    int Draw(ExplicitMeleeRequest request, long attacker, long target, int salt, int minimum, int maximum, bool enemy);
    Action<DamageEvent> DamageRoll(ExplicitMeleeRequest request, Combatant attacker, Combatant target,
        DaggerfallAttackDefinition attack, bool enemy, int attackDamageMod, int backstabChance);
    (int ToHit, int Damage) PlayerAttackModifiers(DaggerfallAttackDefinition attack, DaggerfallSwingDirection gesture);
    bool BackstabOpportunity(Combatant attacker, Combatant target);
    int EnemyTypeBonus(Combatant attacker, Combatant target);
    EquipmentRead EquipmentOf(long owner);
    DaggerfallAdrenalineRush AdrenalineRush(long actor);
    int AttackChanceModifier();
    int ArmorValueModifier();
    int PlayerBiographyAvoidHit();
    void RecordSkillUse(DaggerfallSkillUse use);
    void ApplyMonsterConsequences(AttackRequest request, long target, IReadOnlyList<DaggerfallMonsterHit> slots, FactBuffer<IProductFact> facts);
    void DeliverWeaponPoison(long target, DaggerfallWeaponPoisonSource coating);
    bool TryTransform(AttackRequest request, PreparedAttack attack, FactBuffer<IProductFact> facts, out ActorTransformedFact? transformation);
    /// <summary>Wears the attacker's equipped weapon for a strike of <paramref name="damage"/>, as a hit's wear does.</summary>
    void WearAttackersWeapon(AttackRequest request, long target, int damage, FactBuffer<IProductFact> facts);
    string PlayerWeaponSkill();
    void RecordPlayerWeaponHit(string weaponSkill);
    int ItemStrike(long attacker, long target, DaggerfallItemStrikeSource source, int damage);
    bool WieldsRazor(long attacker);
    void ApplyDamage(CombatParticipants participants, long attacker, long target, int damage, int body, bool enemy,
        ulong generation, ulong step, DaggerfallStrikeFeedback feedback, FactBuffer<IProductFact> facts,
        (UniqueInventoryItem Weapon, ulong Identity)? mace, bool emitHitFact = true, int? admittedBaseDamage = null, UniqueInventoryItem? rangedWeapon = null);
    bool CoverBlocksShot(WorldPoint origin, WorldPoint aim);
    /// <summary>How far an attacker's authored attack carries, or null when it has none.</summary>
    double? ReachOf(long attacker);
    /// <summary>The query-local actor bodies and Engine contact queries delivery reaches targets through.</summary>
    ActorContact Contact { get; }
    /// <summary>Call-local obstacles (doors, for example) contact queries answer with, for the current site.</summary>
    CharacterStepEnvironment? ContactEnvironment();
    /// <summary>Where an actor strikes or looses from and which way it faces, or null for one with no live pose.</summary>
    DaggerfallStrikePose? StrikePose(long actor);
    /// <summary>
    /// Applies a prepared attack that connected with <paramref name="target"/>: its natural consequences and
    /// coating, Wabbajack, the weapon's Strikes payloads, the health application with wear, artifacts and
    /// facts, and the player's weapon skill use.
    /// </summary>
    void ApplyConnectedHarm(AttackRequest request, long target, PreparedPhysicalAttack attack, FactBuffer<IProductFact> facts);
}

/// <summary>Where an actor strikes from (its eye) and the direction it faces or aims.</summary>
internal readonly record struct DaggerfallStrikePose(Vector3 Eye, Vector3 Forward);

/// <summary>
/// Classic Daggerfall physical delivery. A targeted attack decides at admission: the d100 to-hit roll is
/// the pre-contact admission and the admitted target is contact, and the hit frame only carries the
/// decision out. A released shot is admitted and its damage calculated at release; its target-locked
/// flight decides contact (cover on the release line, or the target beyond the dodge radius at arrival).
/// A swing with no target resolves inside its admitting update and reports nothing in reach. At impact a
/// miss still counts Dodging for an enemy attempt at the player and runs the player's Strikes payloads.
/// </summary>
internal sealed class DaggerfallClassicPhysicalDelivery(IDaggerfallPhysicalHarmOperations ops) : IDaggerfallPhysicalDelivery
{
    private const long PlayerId = DaggerfallActorIdentity.PlayerEntityId;
    // DFU DaggerfallMissile moves at 25m/s. The accepted ruleset approximation uses a 0.45m
    // target-position dodge radius and asks the caller's cover query about the release line; it does
    // not collide with an intervening actor's body, which the erratum records.
    private const double ArrowSpeedMetersPerSecond = 25d;
    private const float ArrowDodgeRadiusMeters = .45f;
    private readonly List<DeferredAttackImpact> _releasedRangedShots = [];
    private readonly Dictionary<RangedShotIdentity, InFlightRangedShot> _inFlightRangedShots = [];

    public bool UntargetedSwingsWaitForImpact => false;

    public bool HasPlayerFlight => _releasedRangedShots.Any(shot => shot.Request.AttackerId == PlayerId)
        || _inFlightRangedShots.Values.Any(shot => shot.Release.Request.AttackerId == PlayerId);

    public bool TryPrepare(DaggerfallAdmittedAttack admitted, FactBuffer<IProductFact> facts, out PreparedPhysicalAttack prepared)
    {
        prepared = null!;
        AttackRequest request = admitted.Request;
        Combatant attacker = admitted.Attacker;
        DaggerfallAttackDefinition attack = admitted.Attack;
        bool enemyAttack = admitted.EnemyAttack;
        if (request.TargetId is not long targetId) { prepared = admitted.Prepared(default); return true; }
        if (!ops.TryResolve(targetId, out Combatant target))
        { facts.Append(new AttackRejectedFact(AttackRejection.UnknownExplicitCombatant, request.AttackerId)); return false; }
        ExplicitMeleeRequest explicitRequest = new(request.AttackerId, targetId, request.Generation, request.SimulationStep, request.FixedDeltaSeconds);
        CombatParticipants participants = ops.Participants(attacker.Id, targetId, admitted.Action ?? attacker.Definition.ActionId ?? "attack");
        bool backstabOpportunity = ops.BackstabOpportunity(attacker, target);
        if (backstabOpportunity)
            ops.RecordSkillUse(new DaggerfallSkillUse("backstabbing", DaggerfallSkillUseReason.BackstabbingOpportunity, DaggerfallSkillUseOutcome.Accepted));
        // The donor computes the swing, proficiency and racial attack modifiers once and rides both
        // the hit roll and the damage roll with them; the career's enemy-type bonus and the backstab
        // chance cross the same two rolls at their own points. Resolve them once here and carry them
        // to both resolutions so one admitted attack shares one set of modifier values.
        var modifiers = attacker.Id == PlayerId ? ops.PlayerAttackModifiers(attack, admitted.Gesture) : default;
        int backstabChance = DaggerfallFormulaPolicy.CalculateBackstabChance(DaggerCombatRules.ReadStat(attacker, new DaggerfallStatId("backstabbing")), backstabOpportunity);
        int body = DaggerfallFormulaPolicy.CalculateStruckBodyPart(ops.Draw(explicitRequest, attacker.Id, target.Id, CombatRandomKey.BodySalt, 0, 19, enemyAttack));
        if (attacker.Definition.Kind == DaggerfallActorKinds.Monster && attack.Skill == DaggerfallMechanicsIds.HandToHand.Value)
        {
            var (monsterOutcome, detail) = MonsterAttackSet(participants, explicitRequest, attacker, target, attack, body, enemyAttack, out var slots);
            prepared = admitted.Prepared(monsterOutcome) with { MonsterHits = slots, Detail = detail };
            return true;
        }
        DaggerfallPhysicalHarm harm = new(attacker.Id, target.Id, target.Definition, attack.Skill, attack.Material, body, request.Generation, request.SimulationStep);
        Action<TryHitEvent> hitRoll = HitRoll(explicitRequest, attacker, target, attack, body, enemyAttack, modifiers.ToHit, backstabChance);
        Action<DamageEvent> damageRoll = ops.DamageRoll(explicitRequest, attacker, target, attack, enemyAttack, modifiers.Damage, backstabChance);
        AttackOutcome outcome;
        TryHitEvent admission;
        bool contactInFlight = admitted.Ranged && request.Delayed;
        if (contactInFlight)
        {
            // A target-locked shot is admitted and its harm calculated at release (the provisional
            // DEC-11 bow rule); its flight decides contact and mitigation when it arrives.
            admission = ops.Rules.TryHit(participants, hitRoll);
            NegationEvent? negation = admission.Hit ? ops.Rules.Negate(participants, DaggerfallHarmChannels.Physical, value => ops.Mitigation.Negate(value, harm)) : null;
            DamageEvent? damage = negation is { Negated: false } ? ops.Rules.Damage(participants, damageRoll) : null;
            outcome = new(admission.Hit, negation?.Negated != true, damage?.Damage ?? 0);
        }
        else
        {
            // A swing's delivery is its admitted target: the to-hit roll admits contact, and the harm
            // is mitigated and prepared for the hit frame.
            HarmResolution resolution = ops.Rules.Resolve(participants, DaggerfallHarmChannels.Physical, hitRoll, damageRoll,
                negate: value => ops.Mitigation.Negate(value, harm), mitigate: value => ops.Mitigation.Mitigate(value, harm));
            admission = resolution.Admission!;
            outcome = new(resolution.Connected, !resolution.NegatedBeforeCalculation, resolution.Damage);
        }
        prepared = admitted.Prepared(outcome) with
        {
            WeaponPoison = admitted.WeaponPoison,
            Detail = new(body, admission.Roll, admission.Chance),
            ContactInFlight = contactInFlight,
            Harm = harm,
        };
        return true;
    }

    public void Apply(AttackRequest request, PreparedPhysicalAttack attack, FactBuffer<IProductFact> facts)
    {
        if (request.TargetId is not long target)
        { facts.Append(new AttackRejectedFact(AttackRejection.NoTargetInReach)); return; }
        AttackOutcome outcome = attack.Outcome;
        DaggerfallStrikeFeedback feedback = attack.Feedback;
        DaggerCombatRules.DaggerfallAttackDetail detail = attack.Detail;
        bool enemyAttack = request.AttackerId != PlayerId;
        // This is the one admitted resolution boundary an enemy attempt reaches. The donor tallies
        // Dodging before damage, so a resolved miss contributes too; a rejected or unknown attack
        // never reaches this method and therefore cannot manufacture a use.
        if (request.AttackerId != PlayerId && target == PlayerId)
            ops.RecordSkillUse(new DaggerfallSkillUse(DaggerfallMechanicsIds.Dodging.Value, DaggerfallSkillUseReason.DodgingEnemyAttack, DaggerfallSkillUseOutcome.Attempted));
        if (outcome.Hit && !outcome.Allowed)
        { facts.Append(new AttackRejectedFact(AttackRejection.InsufficientWeaponMaterial)); return; }
        if (outcome.Hit) { ops.ApplyConnectedHarm(request, target, attack, facts); return; }
        // A miss: WeaponManager invokes Strikes even when contact misses, and Wabbajack's replacement
        // still happens.
        if (ops.TryTransform(request, attack, facts, out ActorTransformedFact? transformation))
        {
            if (ops.IsDefeated(target))
                facts.Append(new ActorDiedFact(target, request.AttackerId, DaggerfallDamageCause.Effect, 0, 0, request.Generation, request.SimulationStep));
            return;
        }
        int strikeDamage = 0;
        // An enemy's weapon runs its Strikes payloads against the player only on a damaging blow;
        // the player's own weapon, and one enemy striking another, run them on every swing.
        bool strikes = !(request.AttackerId != PlayerId && target == PlayerId);
        if (strikes && attack.ItemStrikeSource is { } sourceItem)
            strikeDamage = ops.ItemStrike(request.AttackerId, target, sourceItem, strikeDamage);
        facts.Append(new AttackMissedFact(request.AttackerId, target, detail.Roll, detail.Chance, enemyAttack, request.Generation, request.SimulationStep) { Feedback = feedback });
        if (transformation is not null) facts.Append(transformation);
        // The player's weapon runs its Strikes payloads on a miss; Mehrunes Razor's terminal
        // strike has no damage condition, so its saving throw is offered even when nothing landed.
        if (strikeDamage > 0 || request.AttackerId == PlayerId && ops.WieldsRazor(request.AttackerId))
            ops.ApplyDamage(ops.Participants(request.AttackerId, target, request.Action ?? "attack"), request.AttackerId, target,
                strikeDamage, detail.Body, enemyAttack, request.Generation, request.SimulationStep, feedback, facts, null, emitHitFact: false, admittedBaseDamage: 0);
    }

    /// <summary>
    /// The authored release frame consumes the pending attack, while a fixed-ranged attack stays
    /// in this owner's transient queue until the session's admitted update advances it.
    /// The target's current position can dodge the release aim. Admitted static geometry between the
    /// release and the aim is asked of the caller's own Engine query: a shot that meets cover lands
    /// nowhere.
    /// </summary>
    public bool DeferImpact(DeferredAttackImpact impact, FactBuffer<IProductFact> facts)
    {
        if (!impact.Request.Delayed || impact.Attack is not PreparedPhysicalAttack { IsRanged: true } released) return false;
        _releasedRangedShots.Add(impact with { Attack = released with { ReleasedProjectile = true } });
        return true;
    }

    /// <summary>Launches releases at their actual current admitted step, then resolves arrived shots.</summary>
    public void AdvanceFlight(ulong generation, ulong simulationStep, double fixedDeltaSeconds,
        IReadOnlyDictionary<long, WorldPoint> positions, FactBuffer<IProductFact> facts)
    {
        if (!double.IsFinite(fixedDeltaSeconds) || fixedDeltaSeconds <= 0d) throw new ArgumentOutOfRangeException(nameof(fixedDeltaSeconds));

        foreach (DeferredAttackImpact release in _releasedRangedShots)
        {
            if (release.Request.Generation != generation || !ops.IsLiveCombatant(release.Request.AttackerId)
                || release.Request.TargetId is not long targetId || !ops.IsLiveCombatant(targetId)
                || !positions.TryGetValue(release.Request.AttackerId, out WorldPoint origin)
                || !positions.TryGetValue(targetId, out WorldPoint aim)) continue;

            // Cover is asked once, on the line the shot was released along: a missile that meets a wall
            // dies there, so neither the shooter's roll nor the target's later movement decides it.
            if (ops.CoverBlocksShot(origin, aim))
            {
                if (!DeliverRangedShot(release, targetId, contact: false, facts))
                    facts.Append(new RangedShotBlockedFact(release.Request.AttackerId, targetId, generation, simulationStep));
                continue;
            }

            ulong arrival = checked(simulationStep + RequiredFlightSteps(origin, aim, fixedDeltaSeconds));
            RangedShotIdentity identity = new(generation, release.Request.AttackerId, targetId, simulationStep);
            _inFlightRangedShots[identity] = new(release, origin, aim, arrival);
        }
        _releasedRangedShots.Clear();

        foreach ((RangedShotIdentity identity, InFlightRangedShot shot) in _inFlightRangedShots.ToArray())
        {
            if (identity.Generation != generation || !ops.IsLiveCombatant(shot.Release.Request.AttackerId)
                || shot.Release.Request.TargetId is not long targetId || !ops.IsLiveCombatant(targetId))
            {
                _inFlightRangedShots.Remove(identity);
                continue;
            }
            if (simulationStep < shot.ArriveAtStep) continue;
            _inFlightRangedShots.Remove(identity);
            if (!positions.TryGetValue(targetId, out WorldPoint currentTarget)) continue;

            bool dodged = Vector3.Distance(currentTarget.ToVector(), shot.Aim.ToVector()) > ArrowDodgeRadiusMeters;
            if (!DeliverRangedShot(shot.Release, targetId, contact: !dodged, facts))
            {
                var released = (PreparedPhysicalAttack)shot.Release.Attack;
                facts.Append(new AttackMissedFact(shot.Release.Request.AttackerId, targetId, released.Detail.Roll, released.Detail.Chance,
                    EnemyAttack: shot.Release.Request.AttackerId != PlayerId, shot.Release.Request.Generation, shot.Release.Request.SimulationStep) { Feedback = released.Feedback });
            }
        }
    }

    /// <summary>
    /// The harm pipeline's contact for a shot the flight has finished with: the flight's own answer
    /// (cover met, or the target dodged beyond the aim) is the delivery owner's base contact, then
    /// physical mitigation runs on the harm prepared at release and the impact applies. Returns false
    /// when nothing connected, including a shot whose admission already missed.
    /// </summary>
    private bool DeliverRangedShot(DeferredAttackImpact release, long targetId, bool contact, FactBuffer<IProductFact> facts)
    {
        var shot = (PreparedPhysicalAttack)release.Attack;
        if (!shot.Outcome.Hit) return false;
        if (!shot.ContactInFlight)
        {
            if (!contact) return false;
            ops.Execution.ApplyDeferredImpact(release, facts);
            return true;
        }
        long attackerId = release.Request.AttackerId;
        CombatParticipants participants = ops.Participants(attackerId, targetId, shot.Action ?? ops.DefaultAction(attackerId) ?? "attack");
        if (!ops.Rules.Contact(participants, contact).Contact) return false;
        AttackOutcome outcome = shot.Outcome.Allowed
            ? shot.Outcome with { Damage = ops.Rules.Mitigate(participants, DaggerfallHarmChannels.Physical, shot.Outcome.Damage,
                value => ops.Mitigation.Mitigate(value, shot.Harm ?? throw new InvalidOperationException("A shot whose flight decides contact carries its prepared harm."))).Damage }
            : shot.Outcome;
        ops.Execution.ApplyDeferredImpact(release with { Attack = shot with { Outcome = outcome, ContactInFlight = false } }, facts);
        return true;
    }

    public IReadOnlyList<DaggerfallRangedFlightView> ReadFlights(ulong generation, ulong simulationStep) =>
        [.. _inFlightRangedShots
            .Where(pair => pair.Key.Generation == generation && simulationStep < pair.Value.ArriveAtStep)
            .OrderBy(pair => pair.Key.Generation)
            .ThenBy(pair => pair.Key.AttackerId)
            .ThenBy(pair => pair.Key.TargetId)
            .ThenBy(pair => pair.Key.ReleaseStep)
            .Select(pair =>
            {
                InFlightRangedShot shot = pair.Value;
                float progress = simulationStep <= pair.Key.ReleaseStep ? 0f
                    : (float)((double)(simulationStep - pair.Key.ReleaseStep)
                        / (shot.ArriveAtStep - pair.Key.ReleaseStep));
                Vector3 origin = shot.Origin.ToVector();
                Vector3 aim = shot.Aim.ToVector();
                Vector3 line = aim - origin;
                return new DaggerfallRangedFlightView(pair.Key, WorldPoint.From(Vector3.Lerp(origin, aim, progress)),
                    line.LengthSquared() > .000001f ? Vector3.Normalize(line) : Vector3.UnitZ);
            })];

    public void ClearFlight()
    {
        _releasedRangedShots.Clear();
        _inFlightRangedShots.Clear();
    }

    public void RebaseFlight(Vector3 delta)
    {
        foreach ((RangedShotIdentity identity, InFlightRangedShot shot) in _inFlightRangedShots.ToArray())
            _inFlightRangedShots[identity] = shot with
            {
                Origin = DaggerfallExteriorSessionOrigin.Shift(shot.Origin, delta),
                Aim = DaggerfallExteriorSessionOrigin.Shift(shot.Aim, delta),
            };
    }

    private static ulong RequiredFlightSteps(WorldPoint origin, WorldPoint target, double fixedDeltaSeconds)
    {
        double distance = Vector3.Distance(origin.ToVector(), target.ToVector());
        return checked((ulong)Math.Max(1d, Math.Ceiling((distance / ArrowSpeedMetersPerSecond) / fixedDeltaSeconds)));
    }

    private readonly record struct InFlightRangedShot(DeferredAttackImpact Release, WorldPoint Origin, WorldPoint Aim, ulong ArriveAtStep);

    /// <summary>The classic d100 to-hit roll: the harm pipeline's pre-contact admission.</summary>
    private Action<TryHitEvent> HitRoll(ExplicitMeleeRequest request, Combatant attacker, Combatant target,
        DaggerfallAttackDefinition attack, int body, bool enemy, int attackToHitMod, int backstabChance,
        int hitSalt = CombatRandomKey.HitSalt, int criticalSalt = CombatRandomKey.CriticalStrikeSalt) => hit =>
    {
        hit.Chance = HitChance(request, attacker, target, attack, body, enemy, attackToHitMod, backstabChance, criticalSalt);
        hit.Roll = ops.Draw(request, attacker.Id, target.Id, hitSalt, 1, 100, enemy);
        // Daggerfall hits when the d100 roll does not exceed the chance; a contribution may override it.
        hit.Hit = hit.Roll <= hit.Chance;
    };

    /// <summary>
    /// A monster's natural attacks for one attempt: the classic loop works through up to three
    /// authored attack slots, and each slot must clear the player's reflexes chance and the attack's
    /// own hit chance before it rolls damage, with the career's enemy-type bonus carried only by a
    /// slot that landed positive damage.
    /// </summary>
    /// <remarks>
    /// The donor rolls the reflexes check even for slots whose minimum damage is zero; keyed draws are
    /// addressed per (generation, step, attacker, target, salt) rather than by draw order, so skipping
    /// those slots cannot shift any other roll. Every eligible slot resolves through the shared hit
    /// rules independently. Contact, negation, damage contributions, mitigation and application see the
    /// combined attack once.
    /// </remarks>
    private (AttackOutcome Outcome, DaggerCombatRules.DaggerfallAttackDetail Detail) MonsterAttackSet(CombatParticipants participants, ExplicitMeleeRequest request,
        Combatant attacker, Combatant target, DaggerfallAttackDefinition attack, int body, bool enemy, out DaggerfallMonsterHit[] hits)
    {
        List<DaggerfallMonsterHit> accepted = [];
        int enemyType = ops.EnemyTypeBonus(attacker, target);
        if (!ops.TryResolve(PlayerId, out Combatant player))
            throw new InvalidOperationException("A monster's natural attacks measure the player's reflexes, and no player combatant is available to measure.");
        int reflexChance = DaggerfallFormulaPolicy.MonsterAttackReflexChance(DaggerCombatRules.ReadStat(player, DaggerfallMechanicsIds.Reflexes));
        int total = 0;
        bool landed = false;
        TryHitEvent? representative = null;
        DaggerfallPhysicalHarm physical = new(attacker.Id, target.Id, target.Definition, attack.Skill, attack.Material, body, request.Generation, request.SimulationStep);
        // Negation precedes calculation: the first landed slot asks once whether the target turns this
        // physical attack away, and a negated attack rolls no slot damage or consequence. Classic
        // negates nothing here, so the non-negated draw order is the donor's per-slot order.
        NegationEvent? negation = null;
        NegationEvent Negation() => negation ??= ops.Rules.Negate(participants, DaggerfallHarmChannels.Physical, value => ops.Mitigation.Negate(value, physical));
        // Classic tests all three slots; normalized definitions may omit trailing zero ranges.
        for (int slot = 0; slot < 3; slot++)
        {
            int slotMinimum = slot < attacker.Definition.Attacks.Count ? attacker.Definition.Attacks[slot].MinimumDamage : 0;
            int reflex = ops.Draw(request, attacker.Id, target.Id, CombatRandomKey.MonsterReflexSaltBase + slot, 1, 100, enemy);
            if (reflex > reflexChance || slotMinimum <= 0) continue;
            TryHitEvent hit = ops.Rules.TryHit(participants, HitRoll(request, attacker, target, attack, body, enemy, 0, 0,
                CombatRandomKey.MonsterHitSaltBase + slot, CombatRandomKey.MonsterCriticalSaltBase + slot));
            // Retain a landed slot for the shared outcome, or the last miss when none landed.
            if (hit.Hit || !landed) representative = hit;
            if (!hit.Hit) continue;
            landed = true;
            if (Negation().Negated) continue;
            int slotDamage = ops.Draw(request, attacker.Id, target.Id, CombatRandomKey.MonsterDamageSaltBase + slot, slotMinimum, attacker.Definition.Attacks[slot].MaximumDamage, enemy);
            total = checked(total + slotDamage);
            if (slotDamage > 0)
            {
                total = checked(total + enemyType);
                var consequence = DaggerfallMonsterHitPolicy.Select(attacker.Definition.MobileId, target.Id == PlayerId,
                    (low, high) => ops.Draw(request, attacker.Id, target.Id, CombatRandomKey.MonsterConsequenceSaltBase + slot, low, high, enemy));
                accepted.Add(new(slot, slotDamage, consequence));
            }
        }

        // The slots are the attempt's admissions and any landed slot is contact. The landed slots' draws
        // are the attack's calculation, which damage contributions and mitigation then see once.
        DaggerCombatRules.DaggerfallAttackDetail detail = new(body, representative?.Roll ?? 0, representative?.Chance ?? 0);
        hits = [];
        if (!ops.Rules.Contact(participants, landed).Contact) return (new(false, true, 0), detail);
        if (Negation().Negated) return (new(true, false, 0), detail);
        DamageEvent damage = ops.Rules.Damage(participants, value => value.Damage = Math.Max(0, total));
        MitigationEvent mitigation = ops.Rules.Mitigate(participants, DaggerfallHarmChannels.Physical, damage.Damage, value => ops.Mitigation.Mitigate(value, physical));
        hits = accepted.ToArray();
        return (new(true, true, mitigation.Damage), detail);
    }

    private int HitChance(ExplicitMeleeRequest request, Combatant attacker, Combatant target, DaggerfallAttackDefinition attack, int body, bool enemy, int attackToHitMod, int backstabChance, int criticalSalt)
    {
        // WeaponManager.WeaponDamage handles mobile civilians before the enemy-only hit
        // formula. Their lightweight actor definition does not carry enemy combat skills.
        if (target.Definition.Kind == DaggerfallActorKinds.Civilian) return 100;
        int critical = DaggerCombatRules.ReadStat(attacker, new DaggerfallStatId("critical-strike"));
        bool criticalSucceeded = ops.Draw(request, attacker.Id, target.Id, criticalSalt, 1, 100, enemy) <= critical;
        DaggerfallAdrenalineRush attackerRush = ops.AdrenalineRush(attacker.Id), targetRush = ops.AdrenalineRush(target.Id);
        // The donor assembles the skill value, the player's swing/proficiency/racial modifiers and the
        // backstab chance into one chance-to-hit modifier, then adds the weapon material inside its
        // weapon adjustment seam; an unarmed attempt never reaches that seam.
        int chanceToHitMod = checked(DaggerCombatRules.ReadStat(attacker, new DaggerfallStatId(attack.Skill)) + attackToHitMod + backstabChance);
        if (attack.Material is not null)
            chanceToHitMod = DaggerfallFormulaPolicy.AdjustWeaponHitChanceMod(checked(
                chanceToHitMod + DaggerfallFormulaPolicy.CalculateWeaponToHit(attack.Material, DaggerfallFormulaPolicy.ClassicWeaponMaterialModifiers)));
        if (attacker.Id == PlayerId) chanceToHitMod = checked(chanceToHitMod + ops.AttackChanceModifier());
        return DaggerfallFormulaPolicy.CalculateSuccessfulHitChance(
            chanceToHitMod,
            ArmorToHit(target, body),
            DaggerfallFormulaPolicy.CalculateAdrenalineRushToHit(attackerRush.Enabled, attackerRush.Improved, attacker.Health.Current, attacker.Health.Maximum.Value, targetRush.Enabled, targetRush.Improved, target.Health.Current, target.Health.Maximum.Value),
            DaggerfallFormulaPolicy.CalculateStatsToHit(DaggerCombatRules.ReadStat(attacker, DaggerfallMechanicsIds.Luck), DaggerCombatRules.ReadStat(target, DaggerfallMechanicsIds.Luck), DaggerCombatRules.ReadStat(attacker, DaggerfallMechanicsIds.Agility), DaggerCombatRules.ReadStat(target, DaggerfallMechanicsIds.Agility)),
            DaggerfallFormulaPolicy.CalculateSkillsToHit(DaggerCombatRules.ReadStat(target, DaggerfallMechanicsIds.Dodging), critical, criticalSucceeded),
            DaggerfallFormulaPolicy.CalculateAdjustmentsToHit(target.Definition.Kind == DaggerfallActorKinds.Monster, target.Id == PlayerId ? ops.PlayerBiographyAvoidHit() : 0));
    }

    private int ArmorToHit(Combatant target, int body)
    {
        int equipmentBonus = 0;
        string bodySlot = DaggerCombatRules.BodySlot(body);
        foreach (EquipmentAssignment assignment in ops.EquipmentOf(target.Id).Assignments)
        {
            DaggerfallItemDefinition item = ops.Catalog.RequireItem(new DaggerfallItemId(assignment.Item.Definition.Value));
            if (item.Armor is { } worn && assignment.Slot.Value == bodySlot)
                equipmentBonus = checked(equipmentBonus + (ops.Catalog.ArmorValuesByMaterial[worn.Material] * 5));
            else if (item.Shield is { } shield && assignment.Slot.Value == "left-hand" && DaggerCombatRules.ShieldCovers(item, body))
                equipmentBonus = checked(equipmentBonus + (shield.Armor * 5));
        }

        // A worn StrengthensArmor enchantment shifts the defender's whole armor value, which is why it
        // applies once here rather than to any one piece the way a material's rating does. The donor
        // shifts the value down to make armour stronger, and this ruleset accumulates the value's
        // complement — ArmorToHit below turns the bonus back into the value the hit chance adds — so
        // the shift is subtracted from the bonus rather than added to it.
        if (target.Id == PlayerId) equipmentBonus = checked(equipmentBonus - ops.ArmorValueModifier());

        int equipmentArmor = checked(100 - equipmentBonus);
        return target.Definition.Kind switch
        {
            DaggerfallActorKinds.Monster => Math.Min(target.Definition.Armor, equipmentArmor),
            DaggerfallActorKinds.EnemyClass => Math.Min(60, equipmentArmor),
            _ => equipmentArmor,
        };
    }
}
