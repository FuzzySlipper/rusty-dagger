using WorldRpg.Kit.Combat;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall;

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
