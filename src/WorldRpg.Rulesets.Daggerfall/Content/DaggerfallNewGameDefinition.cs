using System.Text.Json;
using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall.Content;

internal sealed record DaggerfallInitialItem(int Template, string? Material, ulong Quantity);
internal sealed record DaggerfallInitialCareer(string Career, DaggerfallInitialItem[] Items, string[] Spells);
internal sealed record DaggerfallNewGameDefinition(int Gold, int SpellbookTemplate, int MaleShirtTemplate,
    int MalePantsTemplate, int FemaleShirtTemplate, int FemalePantsTemplate,
    DaggerfallInitialCareer[] Careers, DaggerfallInitialItem[] CustomItems, string[] CustomMagicSpells)
{
    /// <summary>The site content pack a new game starts at; a bundle must select it.</summary>
    internal string StartSitePack { get; init; } = string.Empty;

    /// <summary>The quest sources a new game starts (the donor's tutorial and introduction), when admitted.</summary>
    internal string[] Quests { get; init; } = [];
}

internal static partial class DaggerfallBaseContent
{
    internal static DaggerfallNewGameDefinition ReadNewGame(JsonElement root, DaggerfallCatalogSet catalogs,
        DaggerfallItemTemplateSet templates, IReadOnlyDictionary<DaggerfallItemId, DaggerfallItemDefinition> definitions,
        IReadOnlyDictionary<DaggerfallEquipmentSlotId, DaggerfallEquipmentSlotDefinition> slots,
        DaggerfallMagicCatalogSet magic, DaggerfallContentDiagnostics diagnostics)
    {
        var section = Object(Property(root, "newGame", diagnostics), "newGame", diagnostics);
        DaggerfallInitialItem[] Items(JsonElement owner, string field) => Array(owner, field, diagnostics).Select(item =>
        {
            int template = Integer(item, "template", diagnostics);
            if (!templates.Templates.TryGetValue(template, out var definition)) diagnostics.Add($"Starting item names missing template {template}.");
            int quantity = Integer(item, "quantity", diagnostics);
            if (quantity <= 0) diagnostics.Add("Starting item quantity must be positive.");
            var materialValue = Property(item, "material", diagnostics);
            string? material = materialValue.ValueKind == JsonValueKind.Null ? null : Text(item, "material", diagnostics);
            if (definition is not null)
            {
                if (!definition.Groups.Contains("Weapons", StringComparer.Ordinal)) diagnostics.Add($"Starting weapon template {template} is not a weapon.");
                if (template == 131 ? material is not null : material is null || !DaggerfallItemMaterialPolicy.IsWeaponMaterial(material))
                    diagnostics.Add($"Starting weapon template {template} has invalid material '{material}'.");
                if (!definition.Stackable && quantity != 1) diagnostics.Add($"Unique starting template {template} must have quantity one.");
                string key = material is null ? $"template-{template}" : $"template-{template}-{material}";
                if (!definitions.TryGetValue(new(key), out var itemDefinition)) diagnostics.Add($"Starting item '{key}' is not published.");
                else if ((ulong)Math.Max(0, quantity) > itemDefinition.MaximumQuantity) diagnostics.Add($"Starting item '{key}' quantity exceeds its maximum.");
            }
            return new DaggerfallInitialItem(template, material, (ulong)Math.Max(0, quantity));
        }).ToArray();
        string[] Spells(JsonElement owner, string field) => Array(owner, field, diagnostics).Select(value =>
        {
            string key = value.GetString() ?? string.Empty;
            if (!magic.Spells.ContainsKey(key)) diagnostics.Add($"Starting spell '{key}' is not published.");
            return key;
        }).ToArray();
        var careers = Array(section, "careers", diagnostics).Select(value => new DaggerfallInitialCareer(
            Text(value, "career", diagnostics), Items(value, "items"), Spells(value, "spells"))).ToArray();
        foreach (var career in careers)
            if (careers.Count(value => value.Career == career.Career) != 1)
                diagnostics.Add($"Career '{career.Career}' must have exactly one starting loadout.");
        foreach (var career in careers)
            if (!catalogs.TryGetCareer(career.Career, out _)) diagnostics.Add($"Starting loadout names missing career '{career.Career}'.");
        int Template(string field, string category, string? slot = null)
        {
            int id = Integer(section, field, diagnostics);
            if (!templates.Templates.TryGetValue(id, out var template)) diagnostics.Add($"Starting gear field '{field}' names missing template {id}.");
            else
            {
                if (!template.Groups.Contains(category, StringComparer.Ordinal) || template.Stackable)
                    diagnostics.Add($"Starting gear field '{field}' must name a unique {category} template.");
                if (!definitions.TryGetValue(new($"template-{id}"), out var definition)) diagnostics.Add($"Starting gear field '{field}' is not published.");
                else if (slot is not null && (definition.Equipment is null || !slots.TryGetValue(new(slot), out var equipmentSlot) ||
                    !definition.Equipment.Classifications.Any(equipmentSlot.AllowedClassifications.Contains)))
                    diagnostics.Add($"Starting gear field '{field}' cannot occupy '{slot}'.");
            }
            return id;
        }
        int gold = Integer(section, "gold", diagnostics);
        if (gold < 0) diagnostics.Add("Starting gold cannot be negative.");
        string startSite = Text(section, "startSite", diagnostics);
        if (string.IsNullOrWhiteSpace(startSite)) diagnostics.Add("A new game requires a start site content pack.");
        string[] quests = [.. Array(section, "quests", diagnostics).Select(value => value.GetString() ?? string.Empty)];
        if (quests.Any(string.IsNullOrWhiteSpace) || quests.Distinct(StringComparer.Ordinal).Count() != quests.Length)
            diagnostics.Add("New-game quests must name distinct quest sources.");
        return new(gold, Template("spellbookTemplate", "MiscItems"), Template("maleShirtTemplate", "MensClothing", "chest-clothes"), Template("malePantsTemplate", "MensClothing", "legs-clothes"),
            Template("femaleShirtTemplate", "WomensClothing", "chest-clothes"), Template("femalePantsTemplate", "WomensClothing", "legs-clothes"), careers,
            Items(section, "customItems"), Spells(section, "customMagicSpells")) { StartSitePack = startSite, Quests = quests };
    }
}
