using System.Collections.ObjectModel;
using System.Text.Json;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>Reads the normalized, immutable daggerfall.base payload.</summary>
internal static class DaggerfallBaseContent
{
    internal const int SchemaVersion = 1;

    internal static DaggerfallDefinitions Read(ReadOnlyMemory<byte> payload)
    {
        DaggerfallContentDiagnostics diagnostics = new();
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            JsonElement root = Object(document.RootElement, "root", diagnostics);
            if (Text(root, "ruleset", diagnostics) != DaggerfallRuleset.Identity.Value) diagnostics.Add("Base payload must identify ruleset 'daggerfall'.");
            if (Integer(root, "schemaVersion", diagnostics) != SchemaVersion) diagnostics.Add($"Base payload schemaVersion must be {SchemaVersion}.");
            Dictionary<DaggerfallActorId, DaggerfallActorDefinition> actors = ReadActors(root, diagnostics);
            Dictionary<DaggerfallItemId, DaggerfallItemDefinition> items = ReadItems(root, diagnostics);
            Dictionary<DaggerfallEquipmentSlotId, DaggerfallEquipmentSlotDefinition> equipmentSlots = ReadEquipmentSlots(root, diagnostics);
            List<DaggerfallHudResourceDefinition> hud = ReadHud(root, diagnostics);
            ValidateReferences(actors, items, equipmentSlots, hud, diagnostics);
            diagnostics.ThrowIfAny();
            return new DaggerfallDefinitions(new ReadOnlyDictionary<DaggerfallActorId, DaggerfallActorDefinition>(actors), new ReadOnlyDictionary<DaggerfallItemId, DaggerfallItemDefinition>(items), new ReadOnlyDictionary<DaggerfallEquipmentSlotId, DaggerfallEquipmentSlotDefinition>(equipmentSlots), System.Array.AsReadOnly(hud.ToArray()));
        }
        catch (JsonException exception)
        {
            diagnostics.Add($"Base payload is not valid JSON: {exception.Message}");
            throw diagnostics.Exception();
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException or OverflowException && exception is not DaggerfallContentException)
        {
            diagnostics.Add($"Base payload is malformed: {exception.Message}");
            throw diagnostics.Exception();
        }
    }

    private static Dictionary<DaggerfallActorId, DaggerfallActorDefinition> ReadActors(JsonElement root, DaggerfallContentDiagnostics diagnostics)
    {
        Dictionary<DaggerfallActorId, DaggerfallActorDefinition> actors = [];
        foreach (JsonElement value in Array(root, "actors", diagnostics))
        {
            JsonElement actor = Object(value, "actor", diagnostics);
            DaggerfallActorId id = new(Text(actor, "id", diagnostics));
            DaggerfallStatBases stats = ReadStats(Object(Property(actor, "stats", diagnostics), "actor.stats", diagnostics), diagnostics);
            DaggerfallVitalRange health = ReadRange(Object(Property(actor, "health", diagnostics), "actor.health", diagnostics), "actor health", diagnostics);
            DaggerfallRewardPolicy rewards = ReadRewards(Object(Property(actor, "rewards", diagnostics), "actor.rewards", diagnostics), diagnostics);
            DaggerfallAttackDefinition? attack = actor.TryGetProperty("attack", out JsonElement attackValue) && attackValue.ValueKind != JsonValueKind.Null ? ReadAttack(Object(attackValue, "actor.attack", diagnostics), diagnostics) : null;
            if (!ValidId(id.Value)) diagnostics.Add($"Actor id '{id.Value}' is invalid.");
            int armor = Integer(actor, "armor", diagnostics);
            int? mobileId = OptionalInteger(actor, "mobileId", diagnostics);
            if (armor is < -1000 or > 1000) diagnostics.Add($"Actor '{id.Value}' armor is outside the supported range.");
            if (mobileId is < 0 or > 100_000) diagnostics.Add($"Actor '{id.Value}' mobileId is outside the supported range.");
            DaggerfallActorDefinition definition = new(id, stats, health, new(DaggerfallMechanicsIds.Health, id.Value == "player" ? DaggerfallMechanicsIds.Stamina : null), rewards, armor, mobileId, attack);
            if (!actors.TryAdd(id, definition)) diagnostics.Add($"Duplicate actor definition '{id.Value}'.");
        }
        if (actors.Count == 0) diagnostics.Add("Base payload must define at least one actor.");
        return actors;
    }

    private static DaggerfallStatBases ReadStats(JsonElement value, DaggerfallContentDiagnostics diagnostics)
    {
        DaggerfallStatBases stats = new(Integer(value, "strength", diagnostics), Integer(value, "intelligence", diagnostics), Integer(value, "willpower", diagnostics), Integer(value, "agility", diagnostics), Integer(value, "endurance", diagnostics), Integer(value, "personality", diagnostics), Integer(value, "speed", diagnostics), Integer(value, "luck", diagnostics), Integer(value, "reflexes", diagnostics), Integer(value, "longBlade", diagnostics), Integer(value, "handToHand", diagnostics), Integer(value, "dodging", diagnostics));
        foreach (int stat in new[] { stats.Strength, stats.Intelligence, stats.Willpower, stats.Agility, stats.Endurance, stats.Personality, stats.Speed, stats.Luck, stats.LongBlade, stats.HandToHand, stats.Dodging })
            if (stat is < 0 or > 10_000) diagnostics.Add("Actor stat values must be between 0 and 10000.");
        if (stats.Reflexes is < 0 or > 10) diagnostics.Add("Actor reflexes must be between 0 and 10.");
        return stats;
    }

    private static DaggerfallVitalRange ReadRange(JsonElement value, string name, DaggerfallContentDiagnostics diagnostics)
    {
        int minimum = Integer(value, "minimum", diagnostics), maximum = Integer(value, "maximum", diagnostics);
        if (minimum < 0 || maximum < minimum || maximum > 1_000_000) diagnostics.Add($"{name} range [{minimum}, {maximum}] is invalid.");
        return new(minimum, maximum);
    }

    private static DaggerfallAttackDefinition ReadAttack(JsonElement value, DaggerfallContentDiagnostics diagnostics)
    {
        int minimum = Integer(value, "minimumDamage", diagnostics), maximum = Integer(value, "maximumDamage", diagnostics);
        string skill = Text(value, "skill", diagnostics);
        double cooldown = Number(value, "cooldownSeconds", diagnostics);
        if (minimum < 0 || maximum < minimum || maximum > 100_000 || skill is not ("long-blade" or "hand-to-hand") || !double.IsFinite(cooldown) || cooldown <= 0d || cooldown > 60d)
            diagnostics.Add($"Attack values for skill '{skill}' are outside the supported range.");
        return new(skill, minimum, maximum, cooldown);
    }

    private static DaggerfallRewardPolicy ReadRewards(JsonElement value, DaggerfallContentDiagnostics diagnostics)
    {
        int experience = Integer(value, "experience", diagnostics);
        if (experience is < 0 or > 1_000_000) diagnostics.Add("Reward experience is outside the supported range.");
        if (!value.TryGetProperty("loot", out JsonElement loot) || loot.ValueKind == JsonValueKind.Null) return new(experience);
        JsonElement lootObject = Object(loot, "actor.rewards.loot", diagnostics);
        int minimum = Integer(lootObject, "minimumQuantity", diagnostics), maximum = Integer(lootObject, "maximumQuantity", diagnostics);
        if (minimum < 0 || maximum < minimum || maximum > 100_000) diagnostics.Add($"Loot range [{minimum}, {maximum}] is invalid.");
        return new(experience, new(Text(lootObject, "table", diagnostics), new(Text(lootObject, "item", diagnostics)), minimum, maximum));
    }

    private static Dictionary<DaggerfallItemId, DaggerfallItemDefinition> ReadItems(JsonElement root, DaggerfallContentDiagnostics diagnostics)
    {
        Dictionary<DaggerfallItemId, DaggerfallItemDefinition> items = [];
        foreach (JsonElement value in Array(root, "items", diagnostics))
        {
            JsonElement item = Object(value, "item", diagnostics);
            DaggerfallItemId id = new(Text(item, "id", diagnostics));
            int maximum = Integer(item, "maximumQuantity", diagnostics);
            DaggerfallWeaponDefinition? weapon = item.TryGetProperty("weapon", out JsonElement weaponValue) && weaponValue.ValueKind != JsonValueKind.Null ? ReadWeapon(Object(weaponValue, "item.weapon", diagnostics), diagnostics) : null;
            DaggerfallItemKind kind = Text(item, "kind", diagnostics) switch { "fungible" => DaggerfallItemKind.Fungible, "unique" => DaggerfallItemKind.Unique, _ => InvalidItemKind(diagnostics) };
            DaggerfallEquipmentDefinition? equipment = item.TryGetProperty("equipment", out JsonElement equipmentValue) && equipmentValue.ValueKind != JsonValueKind.Null ? ReadEquipment(Object(equipmentValue, "item.equipment", diagnostics), diagnostics) : null;
            if (!ValidId(id.Value)) diagnostics.Add($"Item id '{id.Value}' is invalid.");
            if (maximum is < 1 or > 1_000_000) diagnostics.Add($"Item '{id.Value}' maximumQuantity is outside the supported range.");
            if (kind == DaggerfallItemKind.Unique && maximum != 1) diagnostics.Add($"Unique item '{id.Value}' maximumQuantity must be exactly 1.");
            if (equipment is not null && kind != DaggerfallItemKind.Unique) diagnostics.Add($"Equipable item '{id.Value}' must be unique.");
            if (!items.TryAdd(id, new(id, kind, maximum > 0 ? checked((ulong)maximum) : 1, weapon, equipment))) diagnostics.Add($"Duplicate item definition '{id.Value}'.");
        }
        if (items.Count > 256) diagnostics.Add("Base payload defines more than the Engine limit of 256 items.");
        if (items.Count == 0) diagnostics.Add("Base payload must define at least one item.");
        return items;
    }

    private static DaggerfallEquipmentDefinition ReadEquipment(JsonElement value, DaggerfallContentDiagnostics diagnostics)
    {
        string[] classifications = Array(value, "classifications", diagnostics).Select(entry => entry.ValueKind == JsonValueKind.String ? entry.GetString() ?? string.Empty : InvalidClassification(diagnostics)).ToArray();
        int requiredSlots = Integer(value, "requiredSlots", diagnostics);
        string? exclusiveGroup = value.TryGetProperty("exclusiveGroup", out JsonElement group) && group.ValueKind != JsonValueKind.Null ? Text(value, "exclusiveGroup", diagnostics) : null;
        if (classifications.Length is < 1 or > 16 || classifications.Any(classification => !ValidId(classification)) || classifications.Distinct(StringComparer.Ordinal).Count() != classifications.Length)
            diagnostics.Add("Equipment classifications must be distinct stable identifiers.");
        if (requiredSlots is < 1 or > 8) diagnostics.Add("Equipment requiredSlots must be between 1 and 8.");
        if (exclusiveGroup is not null && !ValidId(exclusiveGroup)) diagnostics.Add("Equipment exclusiveGroup must be a stable identifier when present.");
        return new(System.Array.AsReadOnly(classifications), requiredSlots is > 0 and <= 8 ? checked((ushort)requiredSlots) : (ushort)1, exclusiveGroup);
    }

    private static Dictionary<DaggerfallEquipmentSlotId, DaggerfallEquipmentSlotDefinition> ReadEquipmentSlots(JsonElement root, DaggerfallContentDiagnostics diagnostics)
    {
        Dictionary<DaggerfallEquipmentSlotId, DaggerfallEquipmentSlotDefinition> slots = [];
        foreach (JsonElement value in Array(root, "equipmentSlots", diagnostics))
        {
            JsonElement slot = Object(value, "equipment slot", diagnostics);
            DaggerfallEquipmentSlotId id = new(Text(slot, "id", diagnostics));
            string[] classifications = Array(slot, "allowedClassifications", diagnostics).Select(entry => entry.ValueKind == JsonValueKind.String ? entry.GetString() ?? string.Empty : InvalidClassification(diagnostics)).ToArray();
            if (!ValidId(id.Value) || classifications.Length is < 1 or > 16 || classifications.Any(classification => !ValidId(classification)) || classifications.Distinct(StringComparer.Ordinal).Count() != classifications.Length)
                diagnostics.Add($"Equipment slot '{id.Value}' must have distinct stable allowed classifications.");
            if (!slots.TryAdd(id, new(id, System.Array.AsReadOnly(classifications)))) diagnostics.Add($"Duplicate equipment slot '{id.Value}'.");
        }
        if (slots.Count > 64) diagnostics.Add("Base payload defines more than the Engine limit of 64 equipment slots.");
        return slots;
    }

    private static DaggerfallWeaponDefinition ReadWeapon(JsonElement value, DaggerfallContentDiagnostics diagnostics)
    {
        int minimum = Integer(value, "minimumDamage", diagnostics), maximum = Integer(value, "maximumDamage", diagnostics), itemValue = Integer(value, "value", diagnostics), weight = Integer(value, "weight", diagnostics);
        if (minimum < 0 || maximum < minimum || maximum > 100_000 || itemValue is < 0 or > 10_000_000 || weight is < 0 or > 1_000_000 || Text(value, "skill", diagnostics) != "long-blade" || Text(value, "handedness", diagnostics) is not ("either" or "both")) diagnostics.Add("Weapon values are outside their supported range.");
        return new(minimum, maximum, Text(value, "skill", diagnostics), Text(value, "handedness", diagnostics), itemValue, weight);
    }

    private static List<DaggerfallHudResourceDefinition> ReadHud(JsonElement root, DaggerfallContentDiagnostics diagnostics)
    {
        List<DaggerfallHudResourceDefinition> resources = [];
        HashSet<string> ids = new(StringComparer.Ordinal);
        foreach (JsonElement value in Array(root, "hudResources", diagnostics))
        {
            JsonElement resource = Object(value, "hud resource", diagnostics);
            string id = Text(resource, "id", diagnostics);
            if (!ids.Add(id)) diagnostics.Add($"Duplicate HUD resource '{id}'.");
            resources.Add(new(id, Text(resource, "label", diagnostics), new(Text(resource, "track", diagnostics))));
        }
        if (resources.Count == 0) diagnostics.Add("Base payload must define HUD resources.");
        return resources;
    }

    private static void ValidateReferences(IReadOnlyDictionary<DaggerfallActorId, DaggerfallActorDefinition> actors, IReadOnlyDictionary<DaggerfallItemId, DaggerfallItemDefinition> items, IReadOnlyDictionary<DaggerfallEquipmentSlotId, DaggerfallEquipmentSlotDefinition> equipmentSlots, IReadOnlyList<DaggerfallHudResourceDefinition> hud, DaggerfallContentDiagnostics diagnostics)
    {
        foreach (DaggerfallActorDefinition actor in actors.Values)
            if (actor.Rewards.Loot is LootDefinition loot)
            {
                if (!items.TryGetValue(loot.ItemId, out DaggerfallItemDefinition? item)) diagnostics.Add($"Actor '{actor.Id.Value}' loot refers to missing item '{loot.ItemId.Value}'.");
                else if (!item.IsFungible) diagnostics.Add($"Actor '{actor.Id.Value}' loot refers to unique item '{loot.ItemId.Value}' without a materialization policy.");
                else if ((ulong)loot.MinimumQuantity > item.MaximumQuantity || (ulong)loot.MaximumQuantity > item.MaximumQuantity) diagnostics.Add($"Actor '{actor.Id.Value}' loot range [{loot.MinimumQuantity}, {loot.MaximumQuantity}] exceeds fungible item '{loot.ItemId.Value}' maximumQuantity {item.MaximumQuantity}.");
            }
        foreach (DaggerfallHudResourceDefinition resource in hud)
            if (resource.Track != DaggerfallMechanicsIds.Health && resource.Track != DaggerfallMechanicsIds.Stamina && resource.Track != DaggerfallMechanicsIds.Magicka) diagnostics.Add($"HUD resource '{resource.Id}' refers to unsupported track '{resource.Track.Value}'.");
        foreach (string required in new[] { "player", "rat", "skeletal-warrior" })
            if (!actors.ContainsKey(new DaggerfallActorId(required))) diagnostics.Add($"Base payload is missing required actor '{required}'.");
        if (!items.ContainsKey(new DaggerfallItemId("iron-longsword"))) diagnostics.Add("Base payload is missing required item 'iron-longsword'.");
        foreach (DaggerfallItemDefinition item in items.Values)
            if (item.Equipment is not null && !equipmentSlots.Values.Any(slot => slot.AllowedClassifications.Any(item.Equipment.Classifications.Contains)))
                diagnostics.Add($"Equipable item '{item.Id.Value}' has no compatible equipment slot.");
    }

    internal static JsonElement Property(JsonElement value, string property, DaggerfallContentDiagnostics diagnostics)
    {
        if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty(property, out JsonElement result)) return result;
        diagnostics.Add($"Required property '{property}' is missing.");
        return default;
    }
    internal static JsonElement Object(JsonElement value, string name, DaggerfallContentDiagnostics diagnostics)
    {
        if (value.ValueKind == JsonValueKind.Object) return value;
        diagnostics.Add($"'{name}' must be an object.");
        return default;
    }
    internal static IEnumerable<JsonElement> Array(JsonElement value, string property, DaggerfallContentDiagnostics diagnostics)
    {
        JsonElement result = Property(value, property, diagnostics);
        if (result.ValueKind == JsonValueKind.Array) return result.EnumerateArray().ToArray();
        diagnostics.Add($"'{property}' must be an array.");
        return [];
    }
    internal static string Text(JsonElement value, string property, DaggerfallContentDiagnostics diagnostics)
    {
        JsonElement result = Property(value, property, diagnostics);
        if (result.ValueKind == JsonValueKind.String && result.GetString() is { Length: > 0 } text) return text;
        diagnostics.Add($"'{property}' must be a non-empty string.");
        return string.Empty;
    }
    internal static int Integer(JsonElement value, string property, DaggerfallContentDiagnostics diagnostics)
    {
        JsonElement result = Property(value, property, diagnostics);
        if (result.ValueKind == JsonValueKind.Number && result.TryGetInt32(out int integer)) return integer;
        diagnostics.Add($"'{property}' must be an integer.");
        return 0;
    }
    internal static int? OptionalInteger(JsonElement value, string property, DaggerfallContentDiagnostics diagnostics)
    {
        if (!value.TryGetProperty(property, out JsonElement result) || result.ValueKind == JsonValueKind.Null) return null;
        if (result.ValueKind == JsonValueKind.Number && result.TryGetInt32(out int integer)) return integer;
        diagnostics.Add($"'{property}' must be an integer or null.");
        return null;
    }
    internal static float Number(JsonElement value, string property, DaggerfallContentDiagnostics diagnostics)
    {
        JsonElement result = Property(value, property, diagnostics);
        if (result.ValueKind == JsonValueKind.Number && result.TryGetSingle(out float number) && float.IsFinite(number)) return number;
        diagnostics.Add($"'{property}' must be a finite number.");
        return 0f;
    }
    /// <summary>Matches the Engine Mechanics ASCII identity grammar at every Engine-bound boundary.</summary>
    internal static bool ValidId(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 96 && value[0] is >= 'a' and <= 'z' && value.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_' or '.');
    private static DaggerfallItemKind InvalidItemKind(DaggerfallContentDiagnostics diagnostics) { diagnostics.Add("Item kind must be fungible or unique."); return DaggerfallItemKind.Fungible; }
    private static string InvalidClassification(DaggerfallContentDiagnostics diagnostics) { diagnostics.Add("Equipment classifications must be strings."); return string.Empty; }
}

internal sealed class DaggerfallContentDiagnostics
{
    private const int Limit = 16;
    private readonly List<string> _values = [];
    internal void Add(string value)
    {
        if (_values.Count < Limit) _values.Add(value);
        else if (_values.Count == Limit) _values.Add("Daggerfall content diagnostics were truncated.");
    }
    internal void ThrowIfAny()
    {
        if (_values.Count > 0) throw Exception();
    }
    internal DaggerfallContentException Exception() => new(_values);
}

internal sealed class DaggerfallContentException(IEnumerable<string> diagnostics) : InvalidOperationException(string.Join(" ", diagnostics))
{
    internal IReadOnlyList<string> Diagnostics { get; } = System.Array.AsReadOnly(diagnostics.ToArray());
}
