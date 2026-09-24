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

internal readonly record struct DaggerfallAdrenalineRush(bool Enabled, bool Improved);

/// <summary>
/// The swing state of the player's weapon at the moment an attack is admitted, the classic
/// WeaponStates the donor reads off the on-screen FPSWeapon. Classic steers these states from the
/// mouse: vertical motion strikes up or down, horizontal motion angles the down strike left or
/// right, and the last direction holds until the next deliberate movement, which the session's
/// look tracker reproduces. None covers hand-to-hand attacks and every attacker without a drawn
/// weapon, where the donor's swing modifier guard never fires.
/// </summary>
internal enum DaggerfallSwingDirection
{
    None,
    StrikeUp,
    StrikeDown,
    StrikeDownLeft,
    StrikeDownRight,
    StrikeLeft,
    StrikeRight,
}

internal static class CombatRandomKey
{
    internal const ulong Seed = 0;
    internal const string PlayerScope = "dagger.combat.v1";
    internal const string EnemyScope = "dagger.combat.ai.v1";
    internal const int HitSalt = 1;
    internal const int DamageSalt = 2;
    internal const int BodySalt = 3;
    internal const int WeaponConditionSalt = 4;
    internal const int ArmorConditionSalt = 5;
    internal const int CriticalStrikeSalt = 6;
    internal const int BackstabRollSalt = 7;
    // A monster works its authored attack slots in order, and each slot gates itself on a reflex
    // roll, a critical/hit roll, and its own damage roll. Each draw takes its slot number as
    // an offset from the kind base so slot rolls stay keyed independently.
    internal const int MonsterReflexSaltBase = 10;
    internal const int MonsterHitSaltBase = 20;
    internal const int MonsterDamageSaltBase = 30;
    internal const int MonsterCriticalSaltBase = 50;
    internal const string MediaAttackAlternateScope = "daggerfall.media.attack-alternate.v1";
    internal const int MediaAttackAlternateSalt = 41;
    internal const string MediaHitCueScope = "daggerfall.media.hit-cue.v1";
    internal const int MediaHitCueSalt = 42;

    internal static string For(ulong generation, ulong step, long attacker, long target, int salt) => $"generation:{generation}:step:{step}:attacker:{attacker}:target:{target}:salt:{salt}";
    internal static string InitialHealth(long entityId, string actor) => $"spawn:actor:{entityId}:{actor}:health";
    internal static string ClassHealth(long entityId, string actor, int rollIndex) => $"spawn:actor:{entityId}:{actor}:class-health:{rollIndex}";
}
