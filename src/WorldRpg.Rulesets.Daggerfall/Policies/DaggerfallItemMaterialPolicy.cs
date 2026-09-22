using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.Policies;

/// <summary>FORM-10 material bands, adapted from the donor's runtime random calls.</summary>
internal static class DaggerfallItemMaterialPolicy
{
    internal static readonly string[] WeaponMaterials = ["iron", "steel", "silver", "elven", "dwarven", "mithril", "adamantium", "ebony", "orcish", "daedric"];
    private static readonly int[] Bands = [64, 128, 10, 21, 13, 8, 5, 3, 2, 5];
    private static readonly int[] WeightMultipliers = [4, 5, 4, 4, 3, 4, 4, 2, 4, 5];
    private static readonly int[] ValueMultipliers = [1, 2, 4, 8, 16, 32, 64, 128, 256, 512];
    private static readonly int[] ConditionMultipliers = [4, 6, 6, 8, 12, 16, 20, 24, 28, 32];

    internal static string RandomMaterial(int level, int random0To255)
    {
        if (random0To255 is < 0 or > 255) throw new ArgumentOutOfRangeException(nameof(random0To255));
        int modifier = level - 10;
        modifier *= modifier >= 0 ? 2 : 4;
        int remaining = Math.Clamp(modifier + random0To255, 0, 256);
        int material = 0;
        while (material < Bands.Length - 1 && Bands[material] < remaining)
            remaining -= Bands[material++];
        return WeaponMaterials[material];
    }

    /// <summary>FORM-10 armor selection: 1–69 leather, 70–89 chain, 90–100 plate material.</summary>
    internal static string RandomArmorMaterial(int level, int roll1To100, int random0To255)
    {
        if (roll1To100 is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(roll1To100));
        return roll1To100 < 70 ? "leather" : roll1To100 < 90 ? "chain" : RandomMaterial(level, random0To255);
    }

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
