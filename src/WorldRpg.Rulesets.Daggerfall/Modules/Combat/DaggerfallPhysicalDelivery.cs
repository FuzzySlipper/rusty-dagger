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
    bool Ranged,
    double DamageMultiplier,
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
    /// <summary>An attacker's attack was interrupted: drop any impact the owner still holds for it that has not landed.</summary>
    void Interrupt(long attacker, ulong generation);

    /// <summary>Whether an enemy archer holds its shot at <paramref name="target"/> now, before any arrow is spent.</summary>
    bool HoldsFire(long archer, long target);

    /// <summary>Prepares an admitted attack's delivery; false refuses it, with the reason already published.</summary>
    bool TryPrepare(DaggerfallAdmittedAttack admitted, FactBuffer<IProductFact> facts, out PreparedPhysicalAttack prepared);

    /// <summary>Delivers a prepared attack at its impact.</summary>
    void Apply(AttackRequest request, PreparedPhysicalAttack attack, FactBuffer<IProductFact> facts);

    /// <summary>Takes a released projectile into the owner's flight; false when the impact is not a flight.</summary>
    bool DeferImpact(DeferredAttackImpact impact, FactBuffer<IProductFact> facts);

    /// <summary>Whether a blow past its hit frame is still held, winding up to land on <paramref name="target"/>.</summary>
    bool HoldsImpactAt(long target);

    /// <summary>Lands every held blow due by this step.</summary>
    void LandDueImpacts(ulong generation, ulong simulationStep, FactBuffer<IProductFact> facts);

    /// <summary>Advances every flight by the update's elapsed seconds and delivers what arrives.</summary>
    void AdvanceFlight(ulong generation, ulong simulationStep, double elapsedSeconds,
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
    int PlayerDamageModifier(DaggerfallAttackDefinition attack);
    bool BackstabOpportunity(Combatant attacker, Combatant target);
    int EnemyTypeBonus(Combatant attacker, Combatant target);
    EquipmentRead EquipmentOf(long owner);
    int AttackChanceModifier();
    void RecordSkillUse(DaggerfallSkillUse use);
    void ApplyMonsterConsequences(AttackRequest request, long target, IReadOnlyList<DaggerfallMonsterHit> slots, FactBuffer<IProductFact> facts);
    void DeliverWeaponPoison(long target, DaggerfallWeaponPoisonSource coating);
    bool TryTransform(AttackRequest request, PreparedAttack attack, FactBuffer<IProductFact> facts, out ActorTransformedFact? transformation);
    /// <summary>Wears the attacker's equipped weapon for a strike of <paramref name="damage"/>, as a hit's wear does.</summary>
    void WearAttackersWeapon(AttackRequest request, long target, int damage, FactBuffer<IProductFact> facts);
    string PlayerWeaponSkill();
    /// <summary>Whom the player's swing swept into, consumed by the impact it decides; null when it met no one.</summary>
    long? TakeSweptTarget();
    void RecordPlayerWeaponHit(string weaponSkill);
    int ItemStrike(long attacker, long target, DaggerfallItemStrikeSource source, int damage);
    bool WieldsRazor(long attacker);
    void ApplyDamage(CombatParticipants participants, long attacker, long target, int damage, int body, bool enemy,
        ulong generation, ulong step, DaggerfallStrikeFeedback feedback, FactBuffer<IProductFact> facts,
        (UniqueInventoryItem Weapon, ulong Identity)? mace, bool emitHitFact = true, int? admittedBaseDamage = null, UniqueInventoryItem? rangedWeapon = null);
    /// <summary>How far an attacker's authored attack carries, or null when it has none.</summary>
    double? ReachOf(long attacker);
    /// <summary>The query-local actor bodies and Engine contact queries delivery reaches targets through.</summary>
    ActorContact Contact { get; }
    /// <summary>Call-local obstacles (doors, for example) contact queries answer with, for the current site.</summary>
    CharacterStepEnvironment? ContactEnvironment();
    /// <summary>Where an actor strikes or looses from and which way it faces, or null for one with no live pose.</summary>
    DaggerfallStrikePose? StrikePose(long actor);
    /// <summary>An actor's current velocity, zero for one whose motion reports none.</summary>
    Vector3 VelocityOf(long actor);
    /// <summary>
    /// Applies a prepared attack that connected with <paramref name="target"/>: its natural consequences and
    /// coating, Wabbajack, the weapon's Strikes payloads, the health application with wear, artifacts and
    /// facts, and the player's weapon skill use.
    /// </summary>
    void ApplyConnectedHarm(AttackRequest request, long target, PreparedPhysicalAttack attack, FactBuffer<IProductFact> facts);
}

/// <summary>Where an actor strikes from (its eye) and the direction it faces or aims.</summary>
internal readonly record struct DaggerfallStrikePose(Vector3 Eye, Vector3 Forward);
