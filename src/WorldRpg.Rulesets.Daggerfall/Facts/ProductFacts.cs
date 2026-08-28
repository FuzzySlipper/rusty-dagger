using WorldRpg.Kit.Facts;

namespace WorldRpg.Rulesets.Daggerfall.Facts;

internal interface IProductFact : IWorldRpgFact;
internal sealed record ActorDamagedFact(long ActorId, int Amount) : IProductFact;
internal sealed record ActorDiedFact(long ActorId, int AppliedDamage, ulong OriginatingSequence) : IProductFact;
internal sealed record PlayerDamagedFact(int Amount) : IProductFact;
internal sealed record PlayerDiedFact(int AppliedDamage) : IProductFact;
internal enum AttackRejection { MissingPlayerPosition, NoTargetInReach }
internal sealed record AttackRejectedFact(AttackRejection Reason) : IProductFact;
internal sealed record AttackMissedFact(long ActorId, int Roll, int Chance, bool EnemyAttack) : IProductFact;
internal sealed record AttackHitFact(long AttackerId, int Damage, bool EnemyAttack) : IProductFact;
internal sealed record LootAwardedFact(long ActorId, string ItemId, int Quantity, ulong OriginatingSequence) : IProductFact;
internal sealed record ExperienceAwardedFact(long ActorId, int Amount) : IProductFact;
