using Rusty.Engine;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Facts;
using WorldRpg.Kit.Targeting;

namespace WorldRpg.Kit.Combat;

/// <summary>
/// Input/AI convenience entry points over targeting and the one attack lifecycle. When
/// <paramref name="untargetedSwingsWaitForImpact"/> is set, a player swing that found no target still
/// waits for its hit frame, where the ruleset's own contact decides whom it reaches.
/// </summary>
public sealed class AttackCapabilities<TFact>(long playerId, TargetingService targeting, AttackExecution<TFact> execution,
    Func<long, double?> reach, Action<FactBuffer<TFact>> missingPosition, bool untargetedSwingsWaitForImpact = false)
    : IAttackCapabilities<TFact> where TFact : IWorldRpgFact
{
    public double? ReachOf(long actorId) => reach(actorId);
    public bool IsReady(long actorId, ulong generation, ulong step) => execution.IsReady(actorId, generation, step);
    public void TryPlayerMelee(PlayerControlState player, LookReceipt look, ulong generation, ulong step, double delta, FactBuffer<TFact> facts)
    {
        if (player.Position is null) { targeting.Clear(); missingPosition(facts); return; }
        long? target = targeting.Select(player.Position, look.Forward, reach(playerId));
        // A swing that found a target delivers at its animation's hit frame, and the shared pending
        // state is the only owner of that delay. A swing that found nothing resolves inside this update
        // and reports why nothing was in reach, unless the ruleset's contact decides at the hit frame.
        execution.Start(new(playerId, target, generation, step, delta, target is not null || untargetedSwingsWaitForImpact), facts);
    }
    public bool TryBeginEnemyAttack(long attacker, long target, ulong generation, ulong step, double delta, FactBuffer<TFact> facts) =>
        execution.Start(new(attacker, target, generation, step, delta, true), facts);
    public void InterruptPendingAttack(long attacker, ulong generation) => execution.Interrupt(attacker, generation);
}
