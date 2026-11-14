using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.Policies;

/// <summary>
/// The classic item materials and ItemBuilder's material adjustment, which turns a template into the definition of
/// each of its materials: the weapon metals in order, the armour-only leather and chain, and what each scales a
/// template's weight, value and condition by.
/// </summary>
internal static class DaggerfallItemMaterials
{
    internal static readonly string[] WeaponMaterials = ["iron", "steel", "silver", "elven", "dwarven", "mithril", "adamantium", "ebony", "orcish", "daedric"];
    private static readonly int[] WeightMultipliers = [4, 5, 4, 4, 3, 4, 4, 2, 4, 5];
    private static readonly int[] ValueMultipliers = [1, 2, 4, 8, 16, 32, 64, 128, 256, 512];
    private static readonly int[] ConditionMultipliers = [4, 6, 6, 8, 12, 16, 20, 24, 28, 32];

    internal static bool IsWeaponMaterial(string value) => WeaponMaterials.Contains(value, StringComparer.Ordinal);
    internal static bool IsArmorMaterial(string value) => value is "leather" or "chain" || IsWeaponMaterial(value);

    /// <summary>Applies ItemBuilder's material adjustment before an item becomes an Engine definition.</summary>
    internal static DaggerfallMaterializedItemProperties Apply(DaggerfallItemTemplateDefinition template, string material)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentException.ThrowIfNullOrWhiteSpace(material);
        int weight = checked((int)Math.Round(template.BaseWeight * 4, MidpointRounding.AwayFromZero));
        int value = template.BasePrice;
        int condition = template.HitPoints;
        bool weapon = template.Groups.Contains("Weapons", StringComparer.Ordinal) && template.Index != 131;
        bool armor = template.Groups.Contains("Armor", StringComparer.Ordinal);
        if (weapon)
            return ApplyWeaponMaterial(weight, value, condition, material);
        if (!armor)
            return new(weight, value, condition);
        return material switch
        {
            "leather" => new(weight / 2, value, condition),
            "chain" => new(weight, checked(value * 2), condition),
            _ when IsWeaponMaterial(material) => ApplyWeaponMaterial(weight, value, condition, material),
            _ => throw new ArgumentException($"Material '{material}' cannot be applied to armor.", nameof(material)),
        };
    }

    private static DaggerfallMaterializedItemProperties ApplyWeaponMaterial(int weight, int value, int condition, string material)
    {
        int index = Array.IndexOf(WeaponMaterials, material);
        if (index < 0) throw new ArgumentException($"Material '{material}' is not a weapon material.", nameof(material));
        // ItemBuilder first turns kilograms into integral quarter-kilograms, then rounds the scaled
        // result back to quarters. Weight is already in the Engine's quarter-kilogram convention.
        int adjustedWeight = checked((int)Math.Round((double)weight * WeightMultipliers[index] / 4, MidpointRounding.AwayFromZero));
        return new(adjustedWeight, checked(value * 3 * ValueMultipliers[index]), checked(condition * ConditionMultipliers[index] / 4));
    }
}

internal readonly record struct DaggerfallMaterializedItemProperties(int Weight, int Value, int MaximumCondition);
