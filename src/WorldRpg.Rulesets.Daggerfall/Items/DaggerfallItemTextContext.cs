using System.Globalization;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;
using static WorldRpg.Rulesets.Daggerfall.Presentation.DaggerfallTextContexts;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>An item's values formatted for the donor's item text macros.</summary>
internal static class DaggerfallItemTextContext
{
    internal static DaggerfallTextItemContext For(DaggerfallDefinitions definitions, DaggerfallItemDefinition definition,
        DaggerfallItemInstanceMetadata metadata, string name, ulong quantity = 1, int? value = null)
    {
        int percentage = DaggerfallFormulaPolicy.ConditionPercentage(metadata.CurrentCondition, metadata.MaximumCondition);
        string condition = metadata.MaximumCondition > 0 && metadata.CurrentCondition <= metadata.MaximumCondition
            ? Internal(definitions, percentage switch
                { <= 1 => "Broken", <= 5 => "Useless", <= 15 => "Battered", <= 40 => "Worn", <= 60 => "Used",
                    <= 75 => "SlightlyUsed", <= 91 => "AlmostNew", _ => "New" })
            : Number(metadata.CurrentCondition);
        double weight = DaggerfallEncumbrancePolicy.UnitKilograms(definition, metadata) * quantity;
        int materialDamage = DaggerfallFormulaPolicy.WeaponMaterialDamageModifier(metadata.Material,
            DaggerfallFormulaPolicy.ClassicWeaponMaterialModifiers);
        string? damage = definition.Weapon is { } weapon
            ? $"{weapon.MinimumDamage + materialDamage} - {weapon.MaximumDamage + materialDamage}" : null;
        int? armor = definition.Shield?.Armor ?? (definition.Armor is not null
            ? definitions.ArmorValuesByMaterial[metadata.Material] : null);
        string? author = metadata.BookId is int book ? definitions.Books.Books[book].Author : null;
        string? potion = metadata.PotionRecipeKey is int recipe ? definitions.Magic.PotionRecipes[recipe].Name : null;
        if (potion is not null && definition.Template?.Index != 278)
            potion = Internal(definitions, "potionOf").Replace("%po", potion, StringComparison.Ordinal);
        string? soul = metadata.CapturedSoulMobileId is int mobile
            ? DaggerfallIdLabel.For(definitions.Actors.Values.First(actor => actor.MobileId == mobile).Id.Value) : Internal(definitions, "Nothing");
        return new(ItemName: metadata.BookId is int bookId ? definitions.Books.Books[bookId].Title : name, BookAuthor: author, Amount: quantity.ToString(CultureInfo.InvariantCulture),
            Worth: checked((decimal)(value ?? definition.Value) * quantity).ToString(CultureInfo.InvariantCulture),
            Material: definition.Weapon is not null || definition.Armor is not null ? Internal(definitions, metadata.Material) : null,
            Condition: condition, Weight: weight.ToString(weight % 1 == 0 ? "F0" : "F2", CultureInfo.InvariantCulture),
            WeaponDamage: damage, ArmourModifier: armor is int protection ? Signed(protection) : null,
            Potion: potion, HeldSoul: soul);
    }
}
