using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.Modules.Combat;

/// <summary>
/// An explicit, exact-id request supplied by a future targeting owner or focused tests.
/// This seam performs no target acquisition, proximity, sight, encounter, or AI work.
/// </summary>
internal readonly record struct ExplicitMeleeRequest(long AttackerId, long TargetId, ulong Generation, ulong SimulationStep, double FixedDeltaSeconds, DaggerfallActionId? Action = null)
{
    internal ExplicitMeleeRequest Validate()
    {
        if (AttackerId <= 0 || TargetId <= 0 || AttackerId == TargetId) throw new ArgumentOutOfRangeException(nameof(AttackerId));
        if (!double.IsFinite(FixedDeltaSeconds) || FixedDeltaSeconds <= 0d) throw new ArgumentOutOfRangeException(nameof(FixedDeltaSeconds));
        if (Action is DaggerfallActionId action && string.IsNullOrWhiteSpace(action.Value)) throw new ArgumentException("Action identity cannot be empty.", nameof(Action));
        return this;
    }
}

internal static class CombatRandomKey
{
    internal const ulong Seed = 0;
    internal const string PlayerScope = "dagger.combat.v1";
    internal const string EnemyScope = "dagger.combat.ai.v1";
    internal const int HitSalt = 1;
    internal const int DamageSalt = 2;
    internal const int BodySalt = 3;

    internal static string For(ulong generation, ulong step, long attacker, long target, int salt) => $"generation:{generation}:step:{step}:attacker:{attacker}:target:{target}:salt:{salt}";
    internal static string InitialHealth(long entityId, string actor) => $"spawn:actor:{entityId}:{actor}:health";
}
