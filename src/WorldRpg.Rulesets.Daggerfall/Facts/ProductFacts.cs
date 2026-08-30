using WorldRpg.Kit.Facts;

namespace WorldRpg.Rulesets.Daggerfall.Facts;

internal interface IProductFact : IWorldRpgFact;
internal sealed record ActorDamagedFact(long ActorId, int Amount) : IProductFact;
internal sealed record ActorDiedFact(long ActorId, long KillerId, int AppliedDamage, ulong OriginatingGeneration, ulong OriginatingSequence) : IProductFact;
internal enum AttackRejection { MissingPlayerPosition, NoTargetInReach, UnknownExplicitCombatant, TargetDefeated, Cooldown, NoAttackPolicy, InsufficientStamina, StaminaSpendNotAccepted, InsufficientWeaponMaterial }
internal sealed record AttackRejectedFact(AttackRejection Reason) : IProductFact;
internal sealed record AttackMissedFact(long AttackerId, long TargetId, int Roll, int Chance, bool EnemyAttack) : IProductFact;
/// <summary>Struck body follows the donor table; scalar current armor deliberately ignores it.</summary>
internal sealed record AttackHitFact(long AttackerId, long TargetId, int Damage, int StruckBody, bool EnemyAttack) : IProductFact;
internal sealed record LootAwardedFact(long ActorId, string ItemId, int Quantity, ulong OriginatingSequence) : IProductFact;
internal sealed record ExperienceAwardedFact(long ActorId, int Amount) : IProductFact;
