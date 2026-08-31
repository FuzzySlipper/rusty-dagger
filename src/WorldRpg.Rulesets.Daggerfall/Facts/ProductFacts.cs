using WorldRpg.Kit.Facts;
using WorldRpg.Rulesets.Daggerfall.Modules.Behavior;

namespace WorldRpg.Rulesets.Daggerfall.Facts;

internal interface IProductFact : IWorldRpgFact;
internal sealed record ActorDamagedFact(long ActorId, int Amount) : IProductFact;
internal sealed record ActorDiedFact(long ActorId, long KillerId, int AppliedDamage, ulong OriginatingGeneration, ulong OriginatingSequence) : IProductFact;
internal enum AttackRejection { MissingPlayerPosition, NoTargetInReach, UnknownExplicitCombatant, TargetDefeated, Cooldown, NoAttackPolicy, InsufficientStamina, StaminaSpendNotAccepted, InsufficientWeaponMaterial }
internal sealed record AttackRejectedFact(AttackRejection Reason) : IProductFact;
internal sealed record AttackMissedFact(long AttackerId, long TargetId, int Roll, int Chance, bool EnemyAttack, ulong OriginatingGeneration, ulong OriginatingSimulationStep) : IProductFact;
/// <summary>Struck body follows the donor table; scalar current armor deliberately ignores it.</summary>
internal sealed record AttackHitFact(long AttackerId, long TargetId, int Damage, int StruckBody, bool EnemyAttack, ulong OriginatingGeneration, ulong OriginatingSimulationStep) : IProductFact;
internal sealed record LootAwardedFact(long ActorId, string ItemId, ulong Quantity, ulong OriginatingSequence) : IProductFact;
/// <summary>Explicit interaction emptied the defeated actor's durable loot container.</summary>
internal sealed record CorpseLootedFact(long ActorId) : IProductFact;
/// <summary>One explicit interaction confirmed that a defeated actor left no generated loot.</summary>
internal sealed record CorpseSearchedEmptyFact(long ActorId) : IProductFact;
internal sealed record ExperienceAwardedFact(long ActorId, int Amount) : IProductFact;
/// <summary>Ruleset-owned state change; presentation maps it to normalized actor media without depending on behavior internals.</summary>
internal sealed record EnemyBehaviorTransitionFact(long ActorId, EnemyBehaviorState Previous, EnemyBehaviorState Current, ulong OriginatingGeneration, ulong OriginatingSimulationStep) : IProductFact;
