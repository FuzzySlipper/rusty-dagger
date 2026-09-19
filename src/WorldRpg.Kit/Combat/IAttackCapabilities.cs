using Rusty.Engine;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Facts;

namespace WorldRpg.Kit.Combat;

/// <summary>Named attack capability consumed by controls and actor behavior.</summary>
public interface IAttackCapabilities<TFact> where TFact : IWorldRpgFact
{
    double? ReachOf(long actorId);
    bool IsReady(long actorId, ulong generation, ulong step);
    void TryPlayerMelee(PlayerControlState player, LookReceipt look, ulong generation, ulong step, double delta, FactBuffer<TFact> facts);
    bool TryBeginEnemyAttack(long attacker, long target, ulong generation, ulong step, double delta, FactBuffer<TFact> facts);
    void InterruptPendingAttack(long attacker, ulong generation);
}
