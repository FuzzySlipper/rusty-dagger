namespace WorldRpg.Rulesets.Daggerfall.Facts;

internal interface IProductFact;
internal sealed record ActorDamagedFact(long ActorId, int Amount) : IProductFact;
internal sealed record ActorDiedFact(long ActorId, int AppliedDamage, ulong OriginatingSequence) : IProductFact;
internal sealed record PlayerDamagedFact(int Amount) : IProductFact;
internal sealed record PlayerDiedFact(int AppliedDamage) : IProductFact;
internal enum AttackRejection { WeaponRecovering, TooExhausted, MissingPlayerPosition, NoTargetInReach }
internal sealed record AttackRejectedFact(AttackRejection Reason) : IProductFact;
internal sealed record AttackMissedFact(long ActorId, int Roll, int Chance, bool EnemyAttack) : IProductFact;
internal sealed record AttackHitFact(long AttackerId, int Damage, bool EnemyAttack) : IProductFact;
internal sealed record LootAwardedFact(long ActorId, string ItemId, int Quantity, ulong OriginatingSequence) : IProductFact;
internal sealed record ExperienceAwardedFact(long ActorId, int Amount) : IProductFact;

/// <summary>Stable LateUpdate delivery; facts appended while delivering wait for the next admitted update.</summary>
internal sealed class ProductFactBuffer
{
    private List<IProductFact> _pending = [];
    internal void Append(IProductFact fact) { ArgumentNullException.ThrowIfNull(fact); _pending.Add(fact); }
    internal void Deliver(Action<IProductFact> react)
    {
        ArgumentNullException.ThrowIfNull(react);
        List<IProductFact> stable = _pending;
        _pending = [];
        foreach (IProductFact fact in stable) react(fact);
    }
}
