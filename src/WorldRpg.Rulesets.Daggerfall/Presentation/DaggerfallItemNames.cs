using System.Globalization;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>One player-facing name for carried, equipped, traded, created and looted items.</summary>
internal static class DaggerfallItemNames
{
    internal static string BaseName(DaggerfallDefinitions definitions, DaggerfallItemDefinition item) =>
        definitions.SourceTemplateFor(item) is { } template
            ? template.Name : item.Template?.Name ?? DaggerfallInventoryPresentation.Label(item.Id.Value);

    internal static string Label(DaggerfallDefinitions definitions, DaggerfallItemDefinition item, DaggerfallItemInstanceMetadata? metadata)
    {
        string name = BaseName(definitions, item);
        if (metadata is null) return item.Template?.Name ?? name;
        if (!metadata.Identified) return name;
        if (metadata.PotionRecipeKey is int recipe)
            return $"{(item.Template?.Index == 278 ? "Recipe" : "Potion of")} {definitions.Magic.PotionRecipes[recipe].Name}";
        if (metadata.BookId is int book && definitions.Books.Books[book] is { Disposition: DaggerfallBookDisposition.Read, Title.Length: > 0 } readable)
            return readable.Title;
        if (metadata.MadeEnchantment is { } made) name = made.Name;
        if (metadata.Identified && metadata.Enchantment is { } id && definitions.Magic.MagicItems.TryGetValue(id, out var magic))
        {
            if (magic.Type != 0) return magic.Name;
            name = definitions.TextPresentation.ResolveRaw(magic.Name, new(DaggerfallTextKind.Internal, "item-name"),
                DaggerfallTextContext.Empty with { Item = DaggerfallTextContexts.Item(definitions, item, metadata, name) }).Text;
        }
        var native = definitions.SourceTemplateFor(item);
        bool weapon = native?.Groups.Contains("Weapons") == true && native.Index != 131;
        bool armor = native?.Groups.Contains("Armor") == true && native.Index != 107 && item.Shield is null;
        if (!weapon && !armor) return name;
        string material = string.Concat(definitions.Text.Require(new(DaggerfallTextKind.Internal, metadata.Material)).TextRuns);
        string format = string.Concat(definitions.Text.Require(new(DaggerfallTextKind.Internal,
            weapon ? "longWeaponNameFormatString" : "longArmorNameFormatString")).TextRuns);
        return string.Format(CultureInfo.InvariantCulture, format, material, name);
    }
}
