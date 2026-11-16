using System.Text.Json;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Content;

internal sealed record DaggerfallInitialItem(int Template, string? Material, ulong Quantity);

/// <summary>
/// Where a new game places the player: a location profile, and optionally an authored pose in it, which the new
/// character keeps wherever it is. Without one the title stands at the location's start and the new character where
/// the location receives one (a dungeon's Enter marker, where the site crossing stands it).
/// </summary>
internal sealed record DaggerfallNewGameStart(string Profile, DaggerfallSiteAnchor? Pose);
internal sealed record DaggerfallInitialCareer(string Career, DaggerfallInitialItem[] Items, string[] Spells);
internal sealed record DaggerfallNewGameDefinition(int Gold, int SpellbookTemplate, int MaleShirtTemplate,
    int MalePantsTemplate, int FemaleShirtTemplate, int FemalePantsTemplate,
    DaggerfallInitialCareer[] Careers, DaggerfallInitialItem[] CustomItems, string[] CustomMagicSpells)
{
    /// <summary>Where a new game starts.</summary>
    internal required DaggerfallNewGameStart Start { get; init; }

    /// <summary>The quest sources a new game starts (the donor's tutorial and introduction), when admitted.</summary>
    internal string[] Quests { get; init; } = [];

    /// <summary>The hour of the first day a new game begins at; the corpus calendar starts at midnight.</summary>
    internal int StartHour { get; init; }
    /// <summary>Begin starts play at once: a character is quick-started and the opening cinematics are skipped.</summary>
    internal bool QuickEntry { get; init; }
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
                if (template == 131 ? material is not null : material is null || !DaggerfallItemMaterials.IsWeaponMaterial(material))
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
        DaggerfallNewGameStart start = ReadStart(section, "newGame", diagnostics);
        string[] quests = [.. Array(section, "quests", diagnostics).Select(value => value.GetString() ?? string.Empty)];
        if (quests.Any(string.IsNullOrWhiteSpace) || quests.Distinct(StringComparer.Ordinal).Count() != quests.Length)
            diagnostics.Add("New-game quests must name distinct quest sources.");
        return new(gold, Template("spellbookTemplate", "MiscItems"), Template("maleShirtTemplate", "MensClothing", "chest-clothes"), Template("malePantsTemplate", "MensClothing", "legs-clothes"),
            Template("femaleShirtTemplate", "WomensClothing", "chest-clothes"), Template("femalePantsTemplate", "WomensClothing", "legs-clothes"), careers,
            Items(section, "customItems"), Spells(section, "customMagicSpells")) { Start = start, Quests = quests };
    }

    /// <summary>
    /// A new-game start pack's payload over the base new-game definition: it names its own start,
    /// starting gold, opening quests and optionally the hour play begins, and keeps every career loadout
    /// and starting gear field.
    /// </summary>
    internal static DaggerfallNewGameDefinition ReadNewGameStart(ReadOnlyMemory<byte> payload, DaggerfallNewGameDefinition baseline, string packId)
    {
        DaggerfallContentDiagnostics diagnostics = new();
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            JsonElement root = Object(document.RootElement, packId, diagnostics);
            diagnostics.ThrowIfAny();
            RejectDuplicateProperties(root, packId, diagnostics);
            if (Text(root, "ruleset", diagnostics) != RulesetName) diagnostics.Add($"New-game start pack '{packId}' must identify ruleset 'daggerfall'.");
            DaggerfallNewGameStart start = ReadStart(root, $"new-game start pack '{packId}'", diagnostics);
            int gold = Integer(root, "gold", diagnostics);
            if (gold < 0) diagnostics.Add($"New-game start pack '{packId}' cannot start with negative gold.");
            string[] quests = [.. Array(root, "quests", diagnostics).Select(value => value.GetString() ?? string.Empty)];
            if (quests.Any(string.IsNullOrWhiteSpace) || quests.Distinct(StringComparer.Ordinal).Count() != quests.Length)
                diagnostics.Add($"New-game start pack '{packId}' must name distinct quest sources.");
            int startHour = OptionalInteger(root, "startHour", diagnostics) ?? baseline.StartHour;
            if (startHour is < 0 or >= DaggerfallSkyTime.HoursPerDay) diagnostics.Add($"New-game start pack '{packId}' names start hour {startHour}, outside the day.");
            bool quickEntry = root.TryGetProperty("quickEntry", out JsonElement quick) && quick.GetBoolean();
            diagnostics.ThrowIfAny();
            return baseline with { Gold = gold, Start = start, Quests = quests, StartHour = startHour, QuickEntry = quickEntry };
        }
        catch (JsonException error)
        {
            throw new InvalidOperationException($"New-game start pack '{packId}' is not valid JSON: {error.Message}", error);
        }
    }

    /// <summary>
    /// A new game's <c>start</c>: the location profile id, and optionally a <c>position</c> in it with the
    /// <c>yawRadians</c> faced there, given together.
    /// </summary>
    private static DaggerfallNewGameStart ReadStart(JsonElement owner, string what, DaggerfallContentDiagnostics diagnostics)
    {
        JsonElement start = Object(Property(owner, "start", diagnostics), $"{what} start", diagnostics);
        if (start.ValueKind != JsonValueKind.Object) return new(string.Empty, null);
        RejectDuplicateProperties(start, $"{what} start", diagnostics);
        string profile = Text(start, "profile", diagnostics);
        bool positioned = start.TryGetProperty("position", out JsonElement point);
        if (positioned != start.TryGetProperty("yawRadians", out _))
        {
            diagnostics.Add($"The {what} start gives a position and a yaw together, or neither.");
            return new(profile, null);
        }
        if (!positioned) return new(profile, null);
        float[] values = point.ValueKind == JsonValueKind.Array && point.GetArrayLength() == 3
            ? [.. point.EnumerateArray().Select(value => value.ValueKind == JsonValueKind.Number ? value.GetSingle() : float.NaN)]
            : [float.NaN, float.NaN, float.NaN];
        if (values.Any(value => !float.IsFinite(value))) diagnostics.Add($"The {what} start position must be three finite numbers.");
        float yaw = Number(start, "yawRadians", diagnostics);
        return new(profile, new("new-game", new WorldPoint(values[0], values[1], values[2]), yaw, 0F));
    }
}
