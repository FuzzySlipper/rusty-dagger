using WorldRpg.Kit.Facts;
using WorldRpg.Rulesets.Daggerfall.Modules.Behavior;

namespace WorldRpg.Rulesets.Daggerfall.Facts;

internal interface IProductFact : IWorldRpgFact;
internal enum DaggerfallDamageCause { PhysicalAttack, Fall, Hazard, Effect }
/// <summary>One accepted live health application. Calculated damage and actual health lost intentionally differ at bounds or contributions.</summary>
internal sealed record DamageAppliedFact(long SourceActorId, long TargetActorId, DaggerfallDamageCause Cause,
    int CalculatedDamage, double ActualHealthLost, int StruckBody, ulong OriginatingGeneration, ulong OriginatingSimulationStep) : IProductFact;
internal sealed record ActorDamagedFact(long ActorId, long SourceActorId, DaggerfallDamageCause Cause,
    int CalculatedDamage, double ActualHealthLost) : IProductFact;
internal sealed record ActorDiedFact(long ActorId, long KillerId, DaggerfallDamageCause Cause,
    int CalculatedDamage, double ActualHealthLost, ulong OriginatingGeneration, ulong OriginatingSequence) : IProductFact;
/// <summary>A monster's accepted fatigue consequence remains distinct from health damage and its bounded live loss is observable.</summary>
internal sealed record FatigueAppliedFact(long SourceActorId, long TargetActorId,
    int CalculatedFatigueLoss, double ActualFatigueLost, ulong OriginatingGeneration, ulong OriginatingSimulationStep) : IProductFact;
/// <summary>One accepted dungeon action magicka drain on a canonical actor track.</summary>
internal sealed record DungeonMagickaDrainedFact(long TargetActorId, string ActionId, double ActualMagickaLost,
    ulong OriginatingGeneration, ulong OriginatingSimulationStep) : IProductFact;
internal enum AttackRejection { MissingPlayerPosition, NoTargetInReach, UnknownExplicitCombatant, TargetDefeated, Cooldown, NoAttackPolicy, InsufficientStamina, StaminaSpendNotAccepted, InsufficientWeaponMaterial, EmptyQuiver }
internal sealed record AttackRejectedFact(AttackRejection Reason, long? ActorId = null) : IProductFact;
/// <summary>One player melee swing passed cooldown and stamina admission, independently of its target outcome.</summary>
internal sealed record PlayerAttackStartedFact(ulong OriginatingGeneration, ulong OriginatingSimulationStep) : IProductFact;
/// <summary>
/// One enemy melee swing began. The attack's outcome is already decided, so the
/// presentation can play the matching strike, but nothing has been applied yet:
/// the damage lands when the authored damage frame is reached.
/// </summary>
internal sealed record EnemyAttackStartedFact(long AttackerId, long TargetId, bool WillHit, ulong OriginatingGeneration, ulong OriginatingSimulationStep) : IProductFact;
internal sealed record AttackMissedFact(long AttackerId, long TargetId, int Roll, int Chance, bool EnemyAttack, ulong OriginatingGeneration, ulong OriginatingSimulationStep) : IProductFact;
/// <summary>Physical contact is distinct from accepted health loss; body follows the donor table.</summary>
internal sealed record AttackHitFact(long AttackerId, long TargetId, int CalculatedDamage, double ActualHealthLost,
    int StruckBody, bool EnemyAttack, ulong OriginatingGeneration, ulong OriginatingSimulationStep) : IProductFact;
internal sealed record LootAwardedFact(long ActorId, string ItemId, ulong Quantity, ulong OriginatingSequence) : IProductFact;
/// <summary>Explicit interaction emptied the defeated actor's durable loot container.</summary>
internal sealed record CorpseLootedFact(long ActorId) : IProductFact;
/// <summary>One explicit interaction confirmed that a defeated actor left no generated loot.</summary>
internal sealed record CorpseSearchedEmptyFact(long ActorId) : IProductFact;
internal sealed record ExperienceAwardedFact(long ActorId, int Amount) : IProductFact;
/// <summary>Ruleset-owned state change; presentation maps it to normalized actor media without depending on behavior internals.</summary>
internal sealed record EnemyBehaviorTransitionFact(long ActorId, EnemyBehaviorState Previous, EnemyBehaviorState Current, ulong OriginatingGeneration, ulong OriginatingSimulationStep) : IProductFact;
