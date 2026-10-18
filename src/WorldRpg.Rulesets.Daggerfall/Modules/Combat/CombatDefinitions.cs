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

/// <summary>
/// The harm channels classic Daggerfall resolves through the Kit harm pipeline. The magic element
/// channels follow the classic resistance elements, whose tolerance and saving-throw rules stay in
/// <see cref="DaggerfallMagicAdmissionPolicy"/>.
/// </summary>
internal static class DaggerfallHarmChannels
{
    /// <summary>Weapon, natural and unarmed blows and physical hazards. Classic's only physical mitigation is the target's material requirement.</summary>
    internal static readonly HarmChannelId Physical = new("physical");
    /// <summary>Physical harm by how it lands: a ruleset that mitigates them differently resolves blows on these.</summary>
    internal static readonly HarmChannelId Edged = new("edged");
    internal static readonly HarmChannelId Blunt = new("blunt");
    internal static readonly HarmChannelId Piercing = new("piercing");

    /// <summary>How a weapon skill's blow lands: blades cut, archery pierces, and blunt weapons and fists bludgeon.</summary>
    internal static HarmChannelId ForWeaponSkill(string skill) => skill switch
    {
        DaggerfallSkills.Archery => Piercing,
        DaggerfallSkills.BluntWeapon or DaggerfallSkills.HandToHand => Blunt,
        _ => Edged,
    };

    /// <summary>Whether a channel is one of the physical ones.</summary>
    internal static bool IsPhysical(HarmChannelId channel) => channel == Physical || channel == Edged || channel == Blunt || channel == Piercing;
    internal static readonly HarmChannelId Fire = new("fire");
    internal static readonly HarmChannelId Frost = new("frost");
    internal static readonly HarmChannelId Shock = new("shock");
    internal static readonly HarmChannelId Magic = new("magic");
    /// <summary>Poison and disease share the classic DiseaseOrPoison resistance element.</summary>
    internal static readonly HarmChannelId DiseaseOrPoison = new("disease-or-poison");
    /// <summary>A spell effect whose bundle names no element.</summary>
    internal static readonly HarmChannelId Unaligned = new("unaligned");

    internal static HarmChannelId For(DaggerfallMagicResistanceElement element) => element switch
    {
        DaggerfallMagicResistanceElement.Fire => Fire,
        DaggerfallMagicResistanceElement.Frost => Frost,
        DaggerfallMagicResistanceElement.Shock => Shock,
        DaggerfallMagicResistanceElement.Magic => Magic,
        DaggerfallMagicResistanceElement.DiseaseOrPoison => DiseaseOrPoison,
        DaggerfallMagicResistanceElement.None => Unaligned,
        _ => throw new ArgumentOutOfRangeException(nameof(element)),
    };

    /// <summary>
    /// The channel of an admitted spell effect from the bundle element it records. An effect that allows
    /// every element takes its bundle's element, as admission did.
    /// </summary>
    private const DaggerfallMagicAllowedElements EveryElement = DaggerfallMagicAllowedElements.Fire | DaggerfallMagicAllowedElements.Cold
        | DaggerfallMagicAllowedElements.Poison | DaggerfallMagicAllowedElements.Shock | DaggerfallMagicAllowedElements.Magic;

    internal static HarmChannelId ForBundleElement(string? element) =>
        Enum.TryParse(element, ignoreCase: false, out DaggerfallMagicBundleElement parsed) && Enum.IsDefined(parsed)
            ? For(DaggerfallMagicAdmissionPolicy.GetElementType(new(true, false, false, EveryElement, parsed)))
            : Unaligned;
}

internal readonly record struct DaggerfallAdrenalineRush(bool Enabled, bool Improved);

/// <summary>
/// The swing state of the player's weapon at the moment an attack is admitted, the classic
/// WeaponStates the donor reads off the on-screen FPSWeapon. Classic steers these states from the
/// mouse: vertical motion strikes up or down, horizontal motion angles the down strike left or
/// right, and the last direction holds until the next deliberate movement, which the session's
/// look tracker reproduces. The same direction names the strike animation the viewmodel plays.
/// None is an attack without a gesture (the presentation then plays the donor's click-attack
/// choice); the swing modifiers additionally require a weapon, as the donor's guard does.
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
    internal const int MonsterConsequenceSaltBase = 60;
    internal const string MediaAttackAlternateScope = "daggerfall.media.attack-alternate.v1";
    internal const int MediaAttackAlternateSalt = 41;
    internal const string MediaHitCueScope = "daggerfall.media.hit-cue.v1";
    internal const int MediaHitCueSalt = 42;
    internal const int RazorSavingThrowSalt = 43;
    internal const int MolagBalSavingThrowSalt = 44;
    internal const int MolagBalStrengthSalt = 45;

    internal static string For(ulong generation, ulong step, long attacker, long target, int salt) => $"generation:{generation}:step:{step}:attacker:{attacker}:target:{target}:salt:{salt}";
    internal static string InitialHealth(long entityId, string actor) => $"spawn:actor:{entityId}:{actor}:health";
    internal static string ClassHealth(long entityId, string actor, int rollIndex) => $"spawn:actor:{entityId}:{actor}:class-health:{rollIndex}";
}
