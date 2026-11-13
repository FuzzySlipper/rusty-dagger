namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>A selected setting and its optional soul-binding parent, using published setting identities.</summary>
internal sealed record DaggerfallMadeSetting(string Key, int? Parent = null);

/// <summary>
/// The actual item-maker payload on the existing item; no generated item definition or catalog identity. Its shape is
/// the item's; whether its settings are published and agree is Magic's (<c>DaggerfallEnchantmentConstruction.Validate</c>).
/// </summary>
internal sealed record DaggerfallMadeEnchantment(string Name, DaggerfallMadeSetting[] Settings, int Value)
{
    internal void ValidateShape()
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 64 || Value < 0 || Settings is null || Settings.Length is < 1 or > 10
            || Settings.Any(value => value is null || string.IsNullOrWhiteSpace(value.Key)))
            throw new ArgumentException("Made enchantments require a name, value and one to ten settings.");
        for (int index = 0; index < Settings.Length; index++)
            if (Settings[index].Parent is int parent && (parent < 0 || parent >= index || Settings[parent].Parent is not null))
                throw new ArgumentException("Forced enchantment settings require an earlier selected parent.");
    }
}
