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
/// spent, and the sources captured at the admitted boundary. Delivery starts from this.
/// </summary>
internal sealed record DaggerfallAdmittedAttack(
    AttackRequest Request,
    Combatant Attacker,
    DaggerfallAttackDefinition Attack,
    bool Ranged,
    double DamageMultiplier,
    bool MayCritical,
    string? Action,
    DaggerfallStrikeFeedback Feedback,
    ulong? WabbajackSource,
    (UniqueInventoryItem Weapon, ulong Identity)? MolagBalSource,
    DaggerfallWeaponPoisonSource? WeaponPoison,
    DaggerfallItemStrikeSource? StrikeSource,
    UniqueInventoryItem? RangedWeapon)
{
    internal bool EnemyAttack => Request.AttackerId != DaggerfallActorIdentity.PlayerEntityId;

    /// <summary>The attack a player's blow is, by id, which sets its force; null for an enemy's.</summary>
    internal string? PlayerAttack { get; init; }

    /// <summary>Whether this player's attack started aimed at a civilian, so it harms any it meets.</summary>
    internal bool HarmsCivilians { get; init; }

    /// <summary>The prepared attack carrying this admission's sources, with delivery's outcome.</summary>
    internal PreparedPhysicalAttack Prepared(AttackOutcome outcome) => new(Attack.CooldownSeconds, outcome, Feedback, WabbajackSource,
        MolagBalSource: MolagBalSource, ItemStrikeSource: StrikeSource, IsRanged: Ranged, RangedWeapon: RangedWeapon, Action: Action, Admission: this);
}

/// <summary>
/// The shared physical harm machinery <see cref="DaggerfallContactDelivery"/> resolves and applies harm through:
/// actors, draws, the damage roll, consequences, artifacts, wear, skill uses and facts.
/// </summary>
internal interface IDaggerfallPhysicalHarmOperations
{
    CombatResolution Rules { get; }
    DaggerfallContactMitigation Mitigation { get; }
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
        DaggerfallAttackDefinition attack, bool enemy, int attackDamageMod);
    int PlayerDamageModifier(DaggerfallAttackDefinition attack);
    /// <summary>Whether a target hasn't noticed the attacker, so a blow is a backstab.</summary>
    bool TargetUnaware(long target);
    /// <summary>A blow makes its target aware of who struck it.</summary>
    void AlertTarget(long target);
    /// <summary>What a backstab multiplies a blow by, from the attacker's Backstabbing.</summary>
    double BackstabMultiplier(Combatant attacker);
    /// <summary>Whether a player's attack goes through this body as if it weren't there.</summary>
    bool PassesThrough(long target, bool harmsCivilians);
    /// <summary>A player's blow rocks the body it landed on, by its force against that body's weight.</summary>
    void Strike(AttackRequest request, long target, string attack, FactBuffer<IProductFact> facts);
    /// <summary>An unarmed blow drains the target's fatigue as well as its health.</summary>
    void DrainFatigue(AttackRequest request, long target, int damage, FactBuffer<IProductFact> facts);
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
