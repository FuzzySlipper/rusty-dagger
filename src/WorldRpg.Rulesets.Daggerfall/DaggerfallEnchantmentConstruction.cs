using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed record DaggerfallMadeEnchantmentQuote(DaggerfallMadeEnchantment Enchantment, int Capacity, int Power, int Gold);

/// <summary>Classic item-maker selection rules over published settings, including soul-forced children.</summary>
internal static class DaggerfallEnchantmentConstruction
{
    internal static DaggerfallMadeEnchantmentQuote Quote(DaggerfallDefinitions definitions, DaggerfallItemInstanceMetadata item,
        string name, IEnumerable<string> selected)
    {
        var definition = definitions.RequireItem(new(item.ItemId));
        if (item.HasEnchantment || item.PotionRecipeKey is not null || definition.Template?.Index == 131
            || definition.Template is null || !definition.Template.Groups.Any(group => group is "Weapons" or "Armor" or "Gems" or "MensClothing" or "WomensClothing" or "Jewellery"))
            throw new ArgumentException("Choose an ordinary non-potion item other than arrows.");
        var made = new DaggerfallMadeEnchantment(name.Trim(), Expand(definitions.Magic, selected), definition.Value);
        made.Validate(definitions.Magic);
        var settings = made.Settings.Select(value => definitions.Magic.EnchantmentSettings[value.Key]).ToArray();
        if (definition.Weapon is null && made.Settings.Where(value => value.Parent is null).Select(value => definitions.Magic.EnchantmentSettings[value.Key]).Any(value => value.Type is 2 or 4 or 20))
            throw new ArgumentException("The selected enchantment requires a weapon.");
        if (settings.Where(value => value.Type == 15).Any(value => !definitions.Actors.Values.Any(actor => actor.Kind == DaggerfallActorKinds.Monster && actor.MobileId == value.Param)))
            throw new ArgumentException("The selected soul has no published creature definition.");
        int power = made.Settings.Where(value => value.Parent is null).Sum(value => definitions.Magic.EnchantmentSettings[value.Key].Cost);
        int capacity = DaggerfallMagicCostPolicy.ItemEnchantmentPower(definition, item);
        if (power > capacity) throw new ArgumentException($"The item has {capacity} enchantment power; these settings require {power}.");
        return new(made, capacity, power, checked(settings.Where(value => value.Cost > 0).Sum(value => value.Cost) * 10));
    }

    internal static DaggerfallMadeSetting[] Expand(DaggerfallMagicCatalogSet magic, IEnumerable<string> selected)
    {
        List<DaggerfallMadeSetting> settings = [];
        foreach (string key in selected)
        {
            if (!magic.EnchantmentSettings.TryGetValue(key, out var definition))
                throw new ArgumentException($"Unknown enchantment setting '{key}'.");
            int parent = settings.Count;
            settings.Add(new(key));
            foreach (string child in definition.ForcedSettings ?? []) settings.Add(new(child, parent));
        }
        return settings.ToArray();
    }

    internal static void ValidateSettings(DaggerfallMagicCatalogSet magic, DaggerfallMadeSetting[] selected)
    {
        for (int index = 0; index < selected.Length; index++)
        {
            var current = magic.EnchantmentSettings[selected[index].Key];
            if (selected[index].Parent is null)
            {
                string[] actual = selected.Where(value => value.Parent == index).Select(value => value.Key).ToArray();
                if (!actual.SequenceEqual(current.ForcedSettings ?? []))
                    throw new ArgumentException("Soul-bound items must retain their complete forced enchantments.");
            }
            for (int previous = 0; previous < index; previous++)
            {
                var other = magic.EnchantmentSettings[selected[previous].Key];
                bool same = current.Type == other.Type;
                bool duplicate = same && (current.Type is 11 or 12 or 15 or 23 or 24
                    || current.Param == other.Param && current.Type is not (16 or 17));
                bool opposed = (current.Type, other.Type) is (11, 23) or (23, 11) or (12, 24) or (24, 12)
                    || current.Param == other.Param && (current.Type, other.Type) is (4, 20) or (20, 4) or (14, 25) or (25, 14);
                bool all = same && current.Type is 14 or 25 && (current.Param == 5 || other.Param == 5);
                if (duplicate || opposed || all) throw new ArgumentException($"'{current.DisplayName}' conflicts with '{other.DisplayName}'.");
            }
        }
    }
}
