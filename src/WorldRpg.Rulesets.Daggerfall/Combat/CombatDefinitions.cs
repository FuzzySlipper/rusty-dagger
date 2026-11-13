using WorldRpg.Kit.Combat;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;

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

/// <summary>An Adrenaline Rush the player has, improved or not.</summary>
internal readonly record struct DaggerfallAdrenalineRush(bool Improved);

/// <summary>Combat's keyed draws: the enemy scope and the salts that keep each roll of a blow apart. The seed, the player's scope and the key are <see cref="DaggerfallRandomKey"/>.</summary>
internal static class CombatRandomKey
{
    internal const string EnemyScope = "dagger.combat.ai.v1";
    internal const int DamageSalt = 2;
    internal const int BodySalt = 3;
    internal const int WeaponConditionSalt = 4;
    internal const int ArmorConditionSalt = 5;
    // A monster works its authored attack slots in order, and each slot gates itself on a reflex
    // roll and its own damage roll. Each draw takes its slot number as
    // an offset from the kind base so slot rolls stay keyed independently.
    internal const int MonsterReflexSaltBase = 10;
    internal const int MonsterDamageSaltBase = 30;
    internal const int MonsterConsequenceSaltBase = 60;
    // 41 and 42 are Scene's media draws (DaggerfallSiteAppearance).
    internal const int RazorSavingThrowSalt = 43;
    internal const int MolagBalSavingThrowSalt = 44;
    internal const int MolagBalStrengthSalt = 45;

    internal static string InitialHealth(long entityId, string actor) => $"spawn:actor:{entityId}:{actor}:health";
    internal static string ClassHealth(long entityId, string actor, int rollIndex) => $"spawn:actor:{entityId}:{actor}:class-health:{rollIndex}";
}
