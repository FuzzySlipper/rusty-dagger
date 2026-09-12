using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Rusty.Engine.Mechanics;
using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>Reads the normalized, immutable daggerfall.base payload.</summary>
internal static class DaggerfallBaseContent
{
    internal const int SchemaVersion = 2;
    private const int MaximumAuthoredDamage = 100_000;
    private const int MaximumAuthoredArmor = 1_000;
    private const int MaximumAuthoredLootGold = 1_000_000;

    internal static DaggerfallDefinitions Read(ReadOnlyMemory<byte> payload)
    {
        DaggerfallContentDiagnostics diagnostics = new();
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            JsonElement root = Object(document.RootElement, "root", diagnostics);
            RejectDuplicateProperties(root, "root", diagnostics);
            if (Text(root, "ruleset", diagnostics) != DaggerfallRuleset.Identity.Value) diagnostics.Add("Base payload must identify ruleset 'daggerfall'.");
            if (Integer(root, "schemaVersion", diagnostics) != SchemaVersion) diagnostics.Add($"Base payload schemaVersion must be {SchemaVersion}.");
            DaggerfallVocabulary vocabulary = ReadVocabulary(root, diagnostics);
            Dictionary<string, DaggerfallActionDefinition> actions = ReadActions(root, diagnostics);
            Dictionary<DaggerfallItemId, DaggerfallItemDefinition> items = ReadItems(root, diagnostics);
            Dictionary<DaggerfallEquipmentSlotId, DaggerfallEquipmentSlotDefinition> equipmentSlots = ReadEquipmentSlots(root, diagnostics);
            Dictionary<string, int> armorValues = ReadIntegerMap(root, "armorValuesByMaterial", diagnostics);
            Dictionary<string, DaggerfallLootTableDefinition> lootTables = ReadLootTables(root, diagnostics);
            Dictionary<DaggerfallActorId, DaggerfallActorDefinition> actors = ReadActors(root, vocabulary, actions, items, diagnostics);
            List<DaggerfallHudResourceDefinition> hud = ReadHud(root, diagnostics);
            IReadOnlyList<DaggerfallDeferredLootCategoryPool> lootCategoryPools = ReadLootCategoryPools(root, diagnostics);
            IReadOnlyList<DaggerfallDonorErratum> donorErrata = ReadDonorErrata(root, diagnostics);
            DaggerfallItemTemplateLedger itemTemplates = ReadItemTemplateLedger(root, items.Count, diagnostics);
            DaggerfallCatalogSet catalogs = ReadCatalogs(root, vocabulary, actors, items, diagnostics);
            ValidateReferences(vocabulary, actors, items, equipmentSlots, armorValues, actions, lootTables, hud, diagnostics);
            ValidateCatalog(vocabulary, actors, items, equipmentSlots, armorValues, actions, lootTables, lootCategoryPools, donorErrata, diagnostics);
            diagnostics.ThrowIfAny();
            return new DaggerfallDefinitions(catalogs, vocabulary, new ReadOnlyDictionary<DaggerfallActorId, DaggerfallActorDefinition>(actors), new ReadOnlyDictionary<DaggerfallItemId, DaggerfallItemDefinition>(items), new ReadOnlyDictionary<DaggerfallEquipmentSlotId, DaggerfallEquipmentSlotDefinition>(equipmentSlots), new ReadOnlyDictionary<string, int>(armorValues), new ReadOnlyDictionary<string, DaggerfallActionDefinition>(actions), new ReadOnlyDictionary<string, DaggerfallLootTableDefinition>(lootTables), System.Array.AsReadOnly(hud.ToArray()), lootCategoryPools, donorErrata, itemTemplates);
        }
        catch (JsonException exception)
        {
            diagnostics.Add($"Base payload is not valid JSON: {exception.Message}");
            throw diagnostics.Exception();
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or FormatException or OverflowException && exception is not DaggerfallContentException)
        {
            diagnostics.Add($"Base payload is malformed: {exception.Message}");
            throw diagnostics.Exception();
        }
    }

    /// <summary>A stable semantic catalog digest for donor regression tests; it intentionally ignores JSON whitespace and object member ordering.</summary>
    internal static string Fingerprint(DaggerfallDefinitions definitions)
    {
        StringBuilder value = new();
        void Add(params object?[] fields) => value.AppendJoin('|', fields.Select(FingerprintField)).Append('\n');
        Add("vocabulary", string.Join(',', definitions.Vocabulary.Attributes.Select(id => id.Value)), string.Join(',', definitions.Vocabulary.Skills.Select(id => id.Value)), string.Join(',', definitions.Vocabulary.Tracks.Select(id => id.Value)), string.Join(',', definitions.Vocabulary.ArmorParts), string.Join(',', definitions.Vocabulary.Progression.Select(id => id.Value)));
        foreach (DaggerfallActorDefinition actor in definitions.Actors.Values.OrderBy(actor => actor.Id.Value))
        {
            Add("actor", actor.Id.Value, actor.Kind, actor.MobileId, actor.HitPointsPerLevel, actor.Armor, actor.Rewards.ExperienceReward, actor.Team, actor.MinimumMaterial, actor.LootTableKey, actor.Level, actor.Weight, actor.ActionId, actor.Health.Minimum, actor.Health.Maximum, actor.GroundOnSpawn, actor.Presentation.PreferredRestState, string.Join(',', actor.Presentation.EffectiveFramesPerSecond.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}={FingerprintField(pair.Value)}")));
            Add("actor-stats", actor.Id.Value, string.Join(',', actor.Stats.Values.OrderBy(pair => pair.Key.Value).Select(pair => $"{pair.Key.Value}={FingerprintField(pair.Value)}")));
            Add("attacks", actor.Id.Value, string.Join(',', actor.Attacks.Select(range => $"{FingerprintField(range.MinimumDamage)}-{FingerprintField(range.MaximumDamage)}")));
            Add("loadout", actor.Id.Value, string.Join(',', actor.Loadout.Select(entry => $"{entry.ItemId.Value}:{FingerprintField(entry.Quantity)}:{FingerprintField(entry.UniqueEntityId)}:{entry.EquipSlot?.Value}")));
        }
        foreach (DaggerfallItemDefinition item in definitions.Items.Values.OrderBy(item => item.Id.Value))
            Add("item", item.Id.Value, item.Kind, item.MaximumQuantity, item.Weight, item.Value, item.Weapon?.MinimumDamage, item.Weapon?.MaximumDamage, item.Weapon?.Material, item.Weapon?.Skill, item.Weapon?.Handedness, item.Armor?.Material, item.Armor?.Part, item.Shield?.Armor, item.Equipment?.RequiredSlots, item.Equipment?.ExclusiveGroup, item.Equipment is null ? "" : string.Join(',', item.Equipment.Classifications.Order()));
        foreach (DaggerfallEquipmentSlotDefinition slot in definitions.EquipmentSlots.Values.OrderBy(slot => slot.Id.Value)) Add("slot", slot.Id.Value, string.Join(',', slot.AllowedClassifications.Order()));
        foreach ((string material, int armor) in definitions.ArmorValuesByMaterial.OrderBy(pair => pair.Key)) Add("material", material, armor);
        foreach (DaggerfallActionDefinition action in definitions.Actions.Values.OrderBy(action => action.Id)) Add("action", action.Id, action.Interpretation, action.Skill, action.AttackRangeIndex, action.MinimumDamage, action.MaximumDamage, action.StaminaCost, action.Reach, action.CooldownSeconds, action.DamageBonus, string.Join(',', action.Tags));
        foreach (DaggerfallLootTableDefinition loot in definitions.LootTables.Values.OrderBy(loot => loot.Key)) Add("loot", loot.Key, loot.MinimumGold, loot.MaximumGold, string.Join(',', loot.Categories.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}={FingerprintField(pair.Value)}")));
        foreach (DaggerfallHudResourceDefinition hud in definitions.HudResources.OrderBy(resource => resource.Id)) Add("hud", hud.Id, hud.Label, hud.Track.Value);
        foreach (DaggerfallDeferredLootCategoryPool pool in definitions.LootCategoryPools.OrderBy(pool => pool.Id)) Add("pool", pool.Id, pool.Status, pool.Reason);
        foreach (DaggerfallDonorErratum erratum in definitions.DonorErrata.OrderBy(erratum => erratum.Id)) Add("errata", erratum.Id);
        // The published catalogs are content a consumer resolves keys through, so the
        // fingerprint covers them: a key, an index, a decoded career field or a
        // provenance citation that changes is a semantic change to the pack.
        foreach (DaggerfallCatalogKey key in definitions.Catalogs.Attributes) Add("catalog-attribute", key.Id, key.Index, key.Source.SourceRecordId, key.Source.Path);
        foreach (DaggerfallCatalogKey key in definitions.Catalogs.Skills) Add("catalog-skill", key.Id, key.Index, key.Source.SourceRecordId, key.Source.Path);
        foreach (DaggerfallCatalogKey key in definitions.Catalogs.Resistances) Add("catalog-resistance", key.Id, key.Index, key.Source.SourceRecordId, key.Source.Path);
        foreach (DaggerfallRaceDefinition race in definitions.Catalogs.Races.OrderBy(race => race.Id, StringComparer.Ordinal)) Add("catalog-race", race.Id, race.DonorRaceId, race.Source.SourceRecordId, race.Source.Path);
        foreach (DaggerfallCareerDefinition career in definitions.Catalogs.Careers.OrderBy(career => career.Id, StringComparer.Ordinal))
        {
            Add("catalog-career", career.Id, career.Name, string.Join(',', career.PrimarySkills), string.Join(',', career.MajorSkills), string.Join(',', career.MinorSkills), string.Join(',', career.Attributes), career.HitPointsPerLevel, FingerprintField(career.AdvancementMultiplier), string.Join(',', career.ResistanceElements), string.Join(',', career.ImmunityElements), string.Join(',', career.FlagBytes.Select(flag => $"{flag.Name}={flag.Value}")), career.Source.SourceRecordId, career.Source.Path);
        }

        foreach (string collision in definitions.Catalogs.CareerNameCollisions) Add("catalog-career-name-collision", collision);
        foreach (string source in definitions.Catalogs.SourceRecords) Add("catalog-source-record", source);
        foreach (DaggerfallCatalogReference enemy in definitions.Catalogs.Enemies.OrderBy(enemy => enemy.Id, StringComparer.Ordinal)) Add("catalog-enemy", enemy.Id, enemy.Source.SourceRecordId, enemy.Source.Path);
        foreach (DaggerfallCatalogReference item in definitions.Catalogs.ItemTemplates.OrderBy(item => item.Id, StringComparer.Ordinal)) Add("catalog-item-template", item.Id, item.Source.SourceRecordId, item.Source.Path);
        foreach (DaggerfallPendingCatalogDefinition pending in definitions.Catalogs.Pending.OrderBy(pending => pending.Id, StringComparer.Ordinal)) Add("catalog-pending", pending.Id, pending.OwnerTask, pending.Reason);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.ToString())));
    }

    private static string FingerprintField(object? field) => field switch { null => "<null>", IFormattable number => number.ToString(null, CultureInfo.InvariantCulture), _ => field.ToString() ?? string.Empty };

    private static Dictionary<DaggerfallActorId, DaggerfallActorDefinition> ReadActors(JsonElement root, DaggerfallVocabulary vocabulary, IReadOnlyDictionary<string, DaggerfallActionDefinition> actions, IReadOnlyDictionary<DaggerfallItemId, DaggerfallItemDefinition> items, DaggerfallContentDiagnostics diagnostics)
    {
        Dictionary<DaggerfallActorId, DaggerfallActorDefinition> actors = [];
        foreach (JsonElement value in Array(root, "actors", diagnostics))
        {
            JsonElement actor = Object(value, "actor", diagnostics);
            DaggerfallActorId id = new(Text(actor, "id", diagnostics));
            DaggerfallStatBases stats = ReadStats(actor, vocabulary, diagnostics);
            DaggerfallVitalRange health = ReadRange(Object(Property(actor, "health", diagnostics), "actor.health", diagnostics), "actor health", diagnostics);
            DaggerfallRewardPolicy rewards = new(OptionalInteger(actor, "xpReward", diagnostics) ?? 0);
            IReadOnlyList<DaggerfallAttackRange> attacks = ReadAttackRanges(actor, diagnostics);
            string? actionId = OptionalText(actor, "action", diagnostics);
            if (!ValidId(id.Value)) diagnostics.Add($"Actor id '{id.Value}' is invalid.");
            int armor = Integer(actor, "armor", diagnostics);
            int? mobileId = OptionalInteger(actor, "mobileId", diagnostics);
            if (armor is < -MaximumAuthoredArmor or > MaximumAuthoredArmor) diagnostics.Add($"Actor '{id.Value}' armor is outside the supported range.");
            if (mobileId is < 0 or > 100_000) diagnostics.Add($"Actor '{id.Value}' mobileId is outside the supported range.");
            if (actionId is not null && !actions.ContainsKey(actionId)) diagnostics.Add($"Actor '{id.Value}' refers to missing action '{actionId}'.");
            DaggerfallActorPresentationDefinition presentation = ReadActorPresentation(actor, id, diagnostics);
            bool groundOnSpawn = false;
            if (actor.TryGetProperty("groundOnSpawn", out JsonElement grounding))
            {
                if (grounding.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) diagnostics.Add($"Actor '{id.Value}' groundOnSpawn must be boolean.");
                else groundOnSpawn = grounding.GetBoolean();
            }
            DaggerfallActorDefinition definition = new(id, Text(actor, "kind", diagnostics), stats, health, new(DaggerfallMechanicsIds.Health, id.Value == "player" ? DaggerfallMechanicsIds.Stamina : null), rewards, armor, mobileId, OptionalInteger(actor, "hitPointsPerLevel", diagnostics), attacks, OptionalText(actor, "team", diagnostics), OptionalText(actor, "minMetalToHit", diagnostics), OptionalText(actor, "lootTableKey", diagnostics), OptionalInteger(actor, "level", diagnostics), OptionalInteger(actor, "weight", diagnostics), actionId, ReadLoadout(actor, items, diagnostics), presentation, groundOnSpawn);
            if (!actors.TryAdd(id, definition)) diagnostics.Add($"Duplicate actor definition '{id.Value}'.");
        }
        if (actors.Count == 0) diagnostics.Add("Base payload must define at least one actor.");
        return actors;
    }

    private static DaggerfallActorPresentationDefinition ReadActorPresentation(JsonElement actor, DaggerfallActorId actorId, DaggerfallContentDiagnostics diagnostics)
    {
        if (!actor.TryGetProperty("presentation", out JsonElement value) || value.ValueKind == JsonValueKind.Null) return DaggerfallActorPresentationDefinition.None;

        JsonElement presentation = Object(value, "actor.presentation", diagnostics);
        RejectDuplicateProperties(presentation, "actor.presentation", diagnostics);
        string? preferredRestState = OptionalText(presentation, "preferredRestState", diagnostics);
        if (preferredRestState is not null && !ValidPresentationStateId(preferredRestState)) diagnostics.Add($"Actor '{actorId.Value}' preferredRestState must be a non-empty state identifier.");

        Dictionary<string, float> effectiveFramesPerSecond = new(StringComparer.Ordinal);
        if (presentation.TryGetProperty("effectiveFramesPerSecond", out JsonElement overridesValue) && overridesValue.ValueKind != JsonValueKind.Null)
        {
            JsonElement overrides = Object(overridesValue, "actor.presentation.effectiveFramesPerSecond", diagnostics);
            RejectDuplicateProperties(overrides, "actor.presentation.effectiveFramesPerSecond", diagnostics);
            if (overrides.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty entry in overrides.EnumerateObject())
                {
                    float framesPerSecond = entry.Value.ValueKind == JsonValueKind.Number && entry.Value.TryGetSingle(out float parsed) ? parsed : 0F;
                    if (!ValidPresentationStateId(entry.Name) || !float.IsFinite(framesPerSecond) || framesPerSecond <= 0F)
                    {
                        diagnostics.Add($"Actor '{actorId.Value}' effective playback override '{entry.Name}' must use a non-empty state identifier and a finite positive framesPerSecond.");
                        continue;
                    }
                    if (!effectiveFramesPerSecond.TryAdd(entry.Name, framesPerSecond)) diagnostics.Add($"Actor '{actorId.Value}' repeats effective playback override '{entry.Name}'.");
                }
            }
        }

        return new(preferredRestState, new ReadOnlyDictionary<string, float>(effectiveFramesPerSecond));
    }

    private static bool ValidPresentationStateId(string value) => !string.IsNullOrWhiteSpace(value);

    private static DaggerfallStatBases ReadStats(JsonElement actor, DaggerfallVocabulary vocabulary, DaggerfallContentDiagnostics diagnostics)
    {
        Dictionary<DaggerfallStatId, int> values = vocabulary.ActorStats.ToDictionary(id => id, _ => 0);
        ReadStatMap(Object(Property(actor, "attributes", diagnostics), "actor.attributes", diagnostics), vocabulary.Attributes, values, diagnostics);
        ReadStatMap(Object(Property(actor, "skills", diagnostics), "actor.skills", diagnostics), vocabulary.Skills, values, diagnostics);
        if (values[DaggerfallMechanicsIds.Strength] is < 0 or > 10_000 || values[DaggerfallMechanicsIds.Endurance] is < 0 or > 10_000 || values[DaggerfallMechanicsIds.Intelligence] is < 0 or > 10_000) diagnostics.Add("Actor attributes are outside the supported range.");
        return new(values);
    }

    private static DaggerfallVitalRange ReadRange(JsonElement value, string name, DaggerfallContentDiagnostics diagnostics)
    {
        int minimum = Integer(value, "minimum", diagnostics), maximum = Integer(value, "maximum", diagnostics);
        if (minimum < 0 || maximum < minimum || maximum > 1_000_000) diagnostics.Add($"{name} range [{minimum}, {maximum}] is invalid.");
        return new(minimum, maximum);
    }

    /// <summary>
    /// Reads the item-template ledger and requires that every one of the 288 classic targets
    /// carries a provenance and a disposition, that the summary agrees with the entries, and
    /// that no target claims a native fact while the native source is absent.
    /// </summary>
    private static DaggerfallItemTemplateLedger ReadItemTemplateLedger(JsonElement root, int publishedItems, DaggerfallContentDiagnostics diagnostics)
    {
        if (!root.TryGetProperty("itemTemplateLedger", out JsonElement value) || value.ValueKind != JsonValueKind.Object)
        {
            diagnostics.Add("Base payload must carry an itemTemplateLedger section: every native item template target needs a provenance and a disposition before any catalog publication claims one.");
            return new DaggerfallItemTemplateLedger(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, 0, string.Empty, false, 0, 0, 0, 0, []);
        }

        JsonElement ledger = Object(value, "itemTemplateLedger", diagnostics);
        JsonElement target = Object(Property(ledger, "target", diagnostics), "itemTemplateLedger.target", diagnostics);
        JsonElement baseline = Object(Property(ledger, "baseline", diagnostics), "itemTemplateLedger.baseline", diagnostics);
        JsonElement published = Object(Property(ledger, "publishedItems", diagnostics), "itemTemplateLedger.publishedItems", diagnostics);
        JsonElement summary = Object(Property(ledger, "summary", diagnostics), "itemTemplateLedger.summary", diagnostics);
        string status = Text(target, "status", diagnostics);
        bool decodedTemplates = published.TryGetProperty("nativeDecoding", out JsonElement nativeDecodingValue) && nativeDecodingValue.ValueKind == JsonValueKind.True;

        List<DaggerfallItemTemplateTarget> targets = [];
        HashSet<int> indices = [];
        int referenced = 0;
        foreach (JsonElement entry in Array(ledger, "targets", diagnostics))
        {
            JsonElement item = Object(entry, "itemTemplateLedger.targets[]", diagnostics);
            int index = Integer(item, "index", diagnostics);
            string provenance = Text(item, "provenance", diagnostics);
            string disposition = Text(item, "disposition", diagnostics);
            string[] groups = [.. Array(item, "donorGroups", diagnostics).Select(group => group.ValueKind == JsonValueKind.String ? group.GetString() ?? string.Empty : string.Empty)];
            if (index < 0 || index >= DaggerfallItemTemplateLedger.TargetCount)
            {
                diagnostics.Add($"Item template target index {index} is outside the classic 0..{DaggerfallItemTemplateLedger.TargetCount - 1} space.");
            }
            else if (!indices.Add(index))
            {
                diagnostics.Add($"Item template target index {index} is declared more than once.");
            }

            if (string.IsNullOrWhiteSpace(provenance))
            {
                diagnostics.Add($"Item template target {index} has no provenance.");
            }

            if (string.IsNullOrWhiteSpace(disposition))
            {
                diagnostics.Add($"Item template target {index} has no disposition.");
            }

            if (groups.Length != 0)
            {
                referenced++;
            }

            // A target can only be resolved by reading the native source: an absent source
            // and a resolved target cannot both be true.
            if (string.Equals(status, DaggerfallItemTemplateLedger.AbsentStatus, StringComparison.Ordinal)
                && !string.Equals(disposition, DaggerfallItemTemplateLedger.UnresolvedDisposition, StringComparison.Ordinal))
            {
                diagnostics.Add($"Item template target {index} is '{disposition}' while the native item template source is '{status}'.");
            }

            targets.Add(new DaggerfallItemTemplateTarget(index, groups, provenance, disposition));
        }

        int unreferenced = targets.Count - referenced;
        Require(summary, ledger, "targets", targets.Count, diagnostics);
        Require(summary, ledger, "referencedByDonorGroups", referenced, diagnostics);
        Require(summary, ledger, "unreferencedByAnyGroup", unreferenced, diagnostics);
        Require(summary, ledger, "nativeTemplatesDecoded", decodedTemplates ? targets.Count(target => target.Disposition != DaggerfallItemTemplateLedger.UnresolvedDisposition) : 0, diagnostics);
        if (!ReferenceEquals(indices, null) && indices.Count != DaggerfallItemTemplateLedger.TargetCount)
        {
            diagnostics.Add($"The item template ledger declares {indices.Count} distinct target indices where the classic space has {DaggerfallItemTemplateLedger.TargetCount}.");
        }

        int declaredPublished = Integer(published, "count", diagnostics);
        if (declaredPublished != publishedItems)
        {
            diagnostics.Add($"The item template ledger records {declaredPublished} published items where the payload defines {publishedItems}.");
        }

        if (string.Equals(status, DaggerfallItemTemplateLedger.AbsentStatus, StringComparison.Ordinal) && decodedTemplates)
        {
            diagnostics.Add($"The item template ledger claims native decoding while its source status is '{status}'.");
        }

        return new DaggerfallItemTemplateLedger(
            Text(target, "recordId", diagnostics),
            Text(target, "path", diagnostics),
            status,
            Text(target, "reason", diagnostics),
            Text(baseline, "rule", diagnostics),
            Text(baseline, "path", diagnostics),
            declaredPublished,
            Text(published, "valueProvenance", diagnostics),
            decodedTemplates,
            Integer(summary, "targets", diagnostics),
            Integer(summary, "referencedByDonorGroups", diagnostics),
            Integer(summary, "unreferencedByAnyGroup", diagnostics),
            Integer(summary, "nativeTemplatesDecoded", diagnostics),
            targets);
    }

    /// <summary>Requires a declared summary count to match what the entries actually say.</summary>
    private static void Require(JsonElement summary, JsonElement ledger, string name, int observed, DaggerfallContentDiagnostics diagnostics)
    {
        int declared = Integer(summary, name, diagnostics);
        if (declared != observed)
        {
            diagnostics.Add($"The item template ledger summary declares {name} {declared} where its entries say {observed}.");
        }
    }

    private static Dictionary<DaggerfallItemId, DaggerfallItemDefinition> ReadItems(JsonElement root, DaggerfallContentDiagnostics diagnostics)
    {
        Dictionary<DaggerfallItemId, DaggerfallItemDefinition> items = [];
        foreach (JsonElement value in Array(root, "items", diagnostics))
        {
            JsonElement item = Object(value, "item", diagnostics);
            DaggerfallItemId id = new(Text(item, "id", diagnostics));
            int maximum = Integer(item, "maximumQuantity", diagnostics);
            int weight = Integer(item, "weight", diagnostics), itemValue = Integer(item, "value", diagnostics);
            DaggerfallWeaponDefinition? weapon = item.TryGetProperty("weapon", out JsonElement weaponValue) && weaponValue.ValueKind != JsonValueKind.Null ? ReadWeapon(Object(weaponValue, "item.weapon", diagnostics), itemValue, weight, diagnostics) : null;
            DaggerfallArmorDefinition? armor = item.TryGetProperty("armor", out JsonElement armorValue) && armorValue.ValueKind != JsonValueKind.Null ? new(Text(Object(armorValue, "item.armor", diagnostics), "material", diagnostics), Text(Object(armorValue, "item.armor", diagnostics), "part", diagnostics)) : null;
            DaggerfallShieldDefinition? shield = item.TryGetProperty("shield", out JsonElement shieldValue) && shieldValue.ValueKind != JsonValueKind.Null ? new(Integer(Object(shieldValue, "item.shield", diagnostics), "armor", diagnostics)) : null;
            DaggerfallItemKind kind = Text(item, "kind", diagnostics) switch { "fungible" => DaggerfallItemKind.Fungible, "unique" => DaggerfallItemKind.Unique, _ => InvalidItemKind(diagnostics) };
            DaggerfallEquipmentDefinition? equipment = item.TryGetProperty("equipment", out JsonElement equipmentValue) && equipmentValue.ValueKind != JsonValueKind.Null ? ReadEquipment(Object(equipmentValue, "item.equipment", diagnostics), diagnostics) : null;
            if (!ValidId(id.Value)) diagnostics.Add($"Item id '{id.Value}' is invalid.");
            if (maximum < 1 || (ulong)maximum > ManagedInventoryLimits.MaximumStackQuantity) diagnostics.Add($"Item '{id.Value}' maximumQuantity is outside the Engine stack range.");
            if (kind == DaggerfallItemKind.Unique && maximum != 1) diagnostics.Add($"Unique item '{id.Value}' maximumQuantity must be exactly 1.");
            if (equipment is not null && kind != DaggerfallItemKind.Unique) diagnostics.Add($"Equipable item '{id.Value}' must be unique.");
            if (weight is < 0 or > 1_000_000 || itemValue is < 0 or > 10_000_000) diagnostics.Add($"Item '{id.Value}' value or weight is outside the supported range.");
            if (!items.TryAdd(id, new(id, kind, maximum > 0 ? checked((ulong)maximum) : 1, weight, itemValue, weapon, armor, shield, equipment))) diagnostics.Add($"Duplicate item definition '{id.Value}'.");
        }
        if (items.Count == 0) diagnostics.Add("Base payload must define at least one item.");
        return items;
    }

    private static DaggerfallEquipmentDefinition ReadEquipment(JsonElement value, DaggerfallContentDiagnostics diagnostics)
    {
        string[] classifications = Array(value, "classifications", diagnostics).Select(entry => entry.ValueKind == JsonValueKind.String ? entry.GetString() ?? string.Empty : InvalidClassification(diagnostics)).ToArray();
        int requiredSlots = Integer(value, "requiredSlots", diagnostics);
        string? exclusiveGroup = value.TryGetProperty("exclusiveGroup", out JsonElement group) && group.ValueKind != JsonValueKind.Null ? Text(value, "exclusiveGroup", diagnostics) : null;
        if (classifications.Length < 1 || classifications.Length > ManagedInventoryLimits.MaximumClassificationsPerItem || classifications.Any(classification => !ValidId(classification)) || classifications.Distinct(StringComparer.Ordinal).Count() != classifications.Length)
            diagnostics.Add("Equipment classifications must be distinct stable identifiers.");
        if (requiredSlots < 1 || requiredSlots > ManagedInventoryLimits.MaximumEquipmentSlotsPerItem) diagnostics.Add($"Equipment requiredSlots must be between 1 and {ManagedInventoryLimits.MaximumEquipmentSlotsPerItem}.");
        if (exclusiveGroup is not null && !ValidId(exclusiveGroup)) diagnostics.Add("Equipment exclusiveGroup must be a stable identifier when present.");
        return new(System.Array.AsReadOnly(classifications), requiredSlots > 0 && requiredSlots <= ManagedInventoryLimits.MaximumEquipmentSlotsPerItem ? checked((ushort)requiredSlots) : (ushort)1, exclusiveGroup);
    }

    private static Dictionary<DaggerfallEquipmentSlotId, DaggerfallEquipmentSlotDefinition> ReadEquipmentSlots(JsonElement root, DaggerfallContentDiagnostics diagnostics)
    {
        Dictionary<DaggerfallEquipmentSlotId, DaggerfallEquipmentSlotDefinition> slots = [];
        foreach (JsonElement value in Array(root, "equipmentSlots", diagnostics))
        {
            JsonElement slot = Object(value, "equipment slot", diagnostics);
            DaggerfallEquipmentSlotId id = new(Text(slot, "id", diagnostics));
            string[] classifications = Array(slot, "allowedClassifications", diagnostics).Select(entry => entry.ValueKind == JsonValueKind.String ? entry.GetString() ?? string.Empty : InvalidClassification(diagnostics)).ToArray();
            if (!ValidId(id.Value) || classifications.Length > ManagedInventoryLimits.MaximumClassificationsPerItem || classifications.Any(classification => !ValidId(classification)) || classifications.Distinct(StringComparer.Ordinal).Count() != classifications.Length)
                diagnostics.Add($"Equipment slot '{id.Value}' must have distinct stable allowed classifications.");
            if (!slots.TryAdd(id, new(id, System.Array.AsReadOnly(classifications)))) diagnostics.Add($"Duplicate equipment slot '{id.Value}'.");
        }
        return slots;
    }

    private static DaggerfallWeaponDefinition ReadWeapon(JsonElement value, int itemValue, int weight, DaggerfallContentDiagnostics diagnostics)
    {
        int minimum = Integer(value, "minimumDamage", diagnostics), maximum = Integer(value, "maximumDamage", diagnostics);
        string material = Text(value, "material", diagnostics), skill = Text(value, "skill", diagnostics), handedness = Text(value, "handedness", diagnostics);
        if (minimum < 0 || maximum < minimum || maximum > MaximumAuthoredDamage || itemValue is < 0 or > 10_000_000 || weight is < 0 or > 1_000_000 || !ValidId(material) || !ValidId(skill) || handedness is not ("either" or "both")) diagnostics.Add("Weapon values are outside their supported range.");
        return new(minimum, maximum, material, skill, handedness, itemValue, weight);
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

    private static IReadOnlyList<DaggerfallDeferredLootCategoryPool> ReadLootCategoryPools(JsonElement root, DaggerfallContentDiagnostics diagnostics)
    {
        List<DaggerfallDeferredLootCategoryPool> pools = [];
        foreach (JsonElement entry in Array(root, "lootCategoryPools", diagnostics))
        {
            JsonElement pool = Object(entry, "loot category pool", diagnostics);
            string id = Text(pool, "id", diagnostics);
            string status = Text(pool, "status", diagnostics);
            string reason = Text(pool, "reason", diagnostics);
            if (!ValidId(id) || status != "deferred") diagnostics.Add($"Loot category pool '{id}' must be a deferred stable category.");
            pools.Add(new(id, status, reason));
        }
        if (pools.Select(pool => pool.Id).Distinct(StringComparer.Ordinal).Count() != pools.Count) diagnostics.Add("Loot category pools must not repeat an id.");
        return System.Array.AsReadOnly(pools.ToArray());
    }

    private static IReadOnlyList<DaggerfallDonorErratum> ReadDonorErrata(JsonElement root, DaggerfallContentDiagnostics diagnostics)
    {
        List<DaggerfallDonorErratum> errata = [];
        foreach (JsonElement entry in Array(root, "donorErrata", diagnostics))
        {
            if (entry.ValueKind != JsonValueKind.String || entry.GetString() is not { Length: > 0 } id || !ValidId(id))
            {
                diagnostics.Add("Donor errata must be stable identifiers.");
                continue;
            }
            errata.Add(new(id));
        }
        if (errata.Select(erratum => erratum.Id).Distinct(StringComparer.Ordinal).Count() != errata.Count) diagnostics.Add("Donor errata must not repeat an id.");
        return System.Array.AsReadOnly(errata.ToArray());
    }

    internal static void RejectDuplicateProperties(JsonElement value, string path, DaggerfallContentDiagnostics diagnostics)
    {
        if (value.ValueKind == JsonValueKind.Array)
        {
            int index = 0;
            foreach (JsonElement entry in value.EnumerateArray()) RejectDuplicateProperties(entry, $"{path}[{index++}]", diagnostics);
            return;
        }
        if (value.ValueKind != JsonValueKind.Object) return;
        HashSet<string> names = new(StringComparer.Ordinal);
        foreach (JsonProperty property in value.EnumerateObject())
        {
            if (!names.Add(property.Name)) diagnostics.Add($"JSON object '{path}' repeats property '{property.Name}'.");
            RejectDuplicateProperties(property.Value, $"{path}.{property.Name}", diagnostics);
        }
    }

    private static void ValidateReferences(DaggerfallVocabulary vocabulary, IReadOnlyDictionary<DaggerfallActorId, DaggerfallActorDefinition> actors, IReadOnlyDictionary<DaggerfallItemId, DaggerfallItemDefinition> items, IReadOnlyDictionary<DaggerfallEquipmentSlotId, DaggerfallEquipmentSlotDefinition> equipmentSlots, IReadOnlyDictionary<string, int> armorValues, IReadOnlyDictionary<string, DaggerfallActionDefinition> actions, IReadOnlyDictionary<string, DaggerfallLootTableDefinition> lootTables, IReadOnlyList<DaggerfallHudResourceDefinition> hud, DaggerfallContentDiagnostics diagnostics)
    {
        foreach (DaggerfallActorDefinition actor in actors.Values)
        {
            if (actor.Kind is not ("player" or "monster" or "enemy-class")) diagnostics.Add($"Actor '{actor.Id.Value}' has unsupported kind '{actor.Kind}'.");
            if (actor.Kind == "player" && (actor.Id.Value != "player" || actor.MobileId is not null)) diagnostics.Add("Only actor 'player' may have kind player and it cannot have a mobile id.");
            if (actor.Kind == "enemy-class" && (actor.Id.Value, actor.MobileId) is not (("thief", 138) or ("archer", 141))) diagnostics.Add("Enemy classes must be thief mobile 138 or archer mobile 141.");
            if (actor.Kind == "monster" && (actor.MobileId is null || actor.MobileId is 39 or < 0 or > 42)) diagnostics.Add($"Monster '{actor.Id.Value}' has an unsupported mobile id.");
            if (actor.Level is < 1 or > 100 || actor.Weight is < 0 or > 100_000 || actor.Armor is < -MaximumAuthoredArmor or > MaximumAuthoredArmor || actor.Rewards.ExperienceReward is < 0 or > 1_000_000) diagnostics.Add($"Actor '{actor.Id.Value}' has an out-of-range level, weight, armor, or xp reward.");
            if (actor.Team is { } team && !ValidId(team)) diagnostics.Add($"Actor '{actor.Id.Value}' has an invalid team.");
            if (actor.MinimumMaterial is { } material && !DaggerfallFormulaPolicy.ClassicWeaponMaterialRanks.ContainsKey(material)) diagnostics.Add($"Actor '{actor.Id.Value}' refers to unknown minimum weapon material '{material}'.");
            if (actor.LootTableKey is { } lootTable && !lootTables.ContainsKey(lootTable)) diagnostics.Add($"Actor '{actor.Id.Value}' refers to missing loot table '{lootTable}'.");
            if (actor.ActionId is { } actionId && !actions.ContainsKey(actionId)) diagnostics.Add($"Actor '{actor.Id.Value}' refers to missing action '{actionId}'.");
            if (actor.Attacks.Any(range => range.MinimumDamage < 0 || range.MaximumDamage < range.MinimumDamage || range.MaximumDamage > MaximumAuthoredDamage)) diagnostics.Add($"Actor '{actor.Id.Value}' has an invalid attack range.");
            if (actor.HitPointsPerLevel is < 1 or > 100) diagnostics.Add($"Actor '{actor.Id.Value}' hitPointsPerLevel is outside the retained progression metadata range.");
            if (actor.Kind == "player" && actor.HitPointsPerLevel is null) diagnostics.Add("Player must retain hitPointsPerLevel metadata for the later progression/formula task.");
            if (actor.Kind == "monster" && actor.Attacks.Count == 0) diagnostics.Add($"Monster '{actor.Id.Value}' must declare at least one attack range.");
            if (actor.ActionId is { } associatedActionId && actions.TryGetValue(associatedActionId, out DaggerfallActionDefinition? action))
            {
                if (actor.Kind == "player" && action.Interpretation != "player-equipped-melee") diagnostics.Add("Player action association must use player-equipped melee.");
                if (actor.Kind != "player" && action.Interpretation != "fixed-melee") diagnostics.Add($"Actor '{actor.Id.Value}' action association is incompatible with its authored attacks.");
                if (action.AttackRangeIndex is int index && (index < 0 || index >= actor.Attacks.Count)) diagnostics.Add($"Action '{action.Id}' references missing attack range {index} on actor '{actor.Id.Value}'.");
                if (action.AttackRangeIndex is null && action.MinimumDamage is null && actor.Kind != "player") diagnostics.Add($"Fixed action '{action.Id}' needs either an actor attack range reference or direct damage.");
            }
            ValidateLoadout(actor, items, equipmentSlots, diagnostics);
        }
        HashSet<int> mobiles = [];
        foreach (DaggerfallActorDefinition actor in actors.Values.Where(actor => actor.Kind == "monster"))
            if (actor.MobileId is int mobile && !mobiles.Add(mobile)) diagnostics.Add($"Mobile '{mobile}' is assigned by more than one monster.");
        foreach (DaggerfallHudResourceDefinition resource in hud)
            if (!ValidId(resource.Id) || string.IsNullOrWhiteSpace(resource.Label) || !vocabulary.Tracks.Contains(resource.Track) || resource.Track != DaggerfallMechanicsIds.Health && resource.Track != DaggerfallMechanicsIds.Stamina && resource.Track != DaggerfallMechanicsIds.Magicka) diagnostics.Add($"HUD resource '{resource.Id}' refers to an unsupported track or is malformed.");
        foreach (string required in new[] { "player", "rat", "skeletal-warrior" })
            if (!actors.ContainsKey(new DaggerfallActorId(required))) diagnostics.Add($"Base payload is missing required actor '{required}'.");
        if (!items.ContainsKey(new DaggerfallItemId("iron-longsword"))) diagnostics.Add("Base payload is missing required item 'iron-longsword'.");
        foreach (DaggerfallItemDefinition item in items.Values)
        {
            if (item.Weapon is not null && (!DaggerfallFormulaPolicy.ClassicWeaponMaterialRanks.ContainsKey(item.Weapon.Material) || !vocabulary.Skills.Any(skill => skill.Value == item.Weapon.Skill))) diagnostics.Add($"Weapon '{item.Id.Value}' refers to an unknown classic weapon material or skill.");
            if (item.Armor is not null && (!armorValues.ContainsKey(item.Armor.Material) || !vocabulary.ArmorParts.Contains(item.Armor.Part))) diagnostics.Add($"Armor '{item.Id.Value}' refers to an unknown material or armor part.");
            if (item.Shield is { Armor: < 0 or > MaximumAuthoredArmor }) diagnostics.Add($"Shield '{item.Id.Value}' armor is outside the Daggerfall policy range.");
            if (item.Weapon is not null && item.Armor is not null || item.Weapon is not null && item.Shield is not null || item.Armor is not null && item.Shield is not null) diagnostics.Add($"Item '{item.Id.Value}' cannot be weapon, armor, and shield simultaneously.");
            if (item.Equipment is not null && !equipmentSlots.Values.Any(slot => slot.AllowedClassifications.Any(item.Equipment.Classifications.Contains))) diagnostics.Add($"Equipable item '{item.Id.Value}' has no compatible equipment slot.");
        }
        foreach (DaggerfallActionDefinition action in actions.Values)
        {
            if (action.Skill != "equipped" && !vocabulary.Skills.Any(skill => skill.Value == action.Skill)) diagnostics.Add($"Action '{action.Id}' refers to unknown skill '{action.Skill}'.");
            if (action.Tags.Distinct(StringComparer.Ordinal).Count() != action.Tags.Count) diagnostics.Add($"Action '{action.Id}' repeats a tag.");
            if (action.CooldownSeconds is not double cooldown || cooldown <= 0d) diagnostics.Add($"Action '{action.Id}' must define a positive cooldown.");
            bool directRange = action.MinimumDamage is not null || action.MaximumDamage is not null;
            if (action.Interpretation == "fixed-melee" && (action.AttackRangeIndex is not null) == directRange) diagnostics.Add($"Fixed action '{action.Id}' must use exactly one direct damage range or actor attackRangeIndex.");
            if (action.Interpretation == "player-equipped-melee" && (action.AttackRangeIndex is not null || directRange || action.StaminaCost is not > 0)) diagnostics.Add($"Player-equipped action '{action.Id}' must own a positive stamina cost and use equipped weapon damage.");
            if (action.Interpretation == "fixed-melee" && action.StaminaCost is not null) diagnostics.Add($"Fixed action '{action.Id}' must not declare player stamina cost.");
        }
    }

    private static void ValidateLoadout(DaggerfallActorDefinition actor, IReadOnlyDictionary<DaggerfallItemId, DaggerfallItemDefinition> items, IReadOnlyDictionary<DaggerfallEquipmentSlotId, DaggerfallEquipmentSlotDefinition> slots, DaggerfallContentDiagnostics diagnostics)
    {
        HashSet<DaggerfallItemId> fungibleItems = [];
        HashSet<ulong> uniqueEntityIds = [];
        HashSet<DaggerfallEquipmentSlotId> equippedSlots = [];
        foreach (DaggerfallLoadoutEntry entry in actor.Loadout)
        {
            if (!items.TryGetValue(entry.ItemId, out DaggerfallItemDefinition? item)) { diagnostics.Add($"Actor '{actor.Id.Value}' loadout refers to missing item '{entry.ItemId.Value}'."); continue; }
            if (item.IsFungible)
            {
                if (entry.UniqueEntityId is not null || entry.EquipSlot is not null || entry.Quantity == 0 || entry.Quantity > item.MaximumQuantity || !fungibleItems.Add(entry.ItemId)) diagnostics.Add($"Actor '{actor.Id.Value}' fungible loadout '{entry.ItemId.Value}' has invalid quantity, entity, equipment, or duplicate id.");
                continue;
            }
            if (entry.Quantity != 1 || entry.UniqueEntityId is not ulong entity || entity == 0 || !uniqueEntityIds.Add(entity)) diagnostics.Add($"Actor '{actor.Id.Value}' unique loadout '{entry.ItemId.Value}' must have one distinct non-zero entity id.");
            if (entry.EquipSlot is not DaggerfallEquipmentSlotId slot) continue;
            if (item.Equipment is null) diagnostics.Add($"Actor '{actor.Id.Value}' equips non-equipable item '{entry.ItemId.Value}'.");
            else if (item.Equipment.RequiredSlots != 1) diagnostics.Add($"Actor '{actor.Id.Value}' loadout item '{entry.ItemId.Value}' requires {item.Equipment.RequiredSlots} slots but this canonical payload supplies one slot.");
            else if (!slots.TryGetValue(slot, out DaggerfallEquipmentSlotDefinition? slotDefinition)) diagnostics.Add($"Actor '{actor.Id.Value}' refers to unknown equipment slot '{slot.Value}'.");
            else if (slotDefinition.AllowedClassifications.Count != 0 && !item.Equipment.Classifications.Any(slotDefinition.AllowedClassifications.Contains)) diagnostics.Add($"Actor '{actor.Id.Value}' item '{entry.ItemId.Value}' is incompatible with slot '{slot.Value}'.");
            if (!equippedSlots.Add(slot)) diagnostics.Add($"Actor '{actor.Id.Value}' equips more than one item in slot '{slot.Value}'.");
        }
    }

    private static DaggerfallVocabulary ReadVocabulary(JsonElement root, DaggerfallContentDiagnostics diagnostics)
    {
        JsonElement value = Object(Property(root, "vocabulary", diagnostics), "vocabulary", diagnostics);
        IReadOnlyList<DaggerfallStatId> attributes = ReadIds(value, "attributes", diagnostics).Select(id => new DaggerfallStatId(id)).ToArray();
        IReadOnlyList<DaggerfallStatId> skills = ReadIds(value, "skills", diagnostics).Select(id => new DaggerfallStatId(id)).ToArray();
        IReadOnlyList<DaggerfallTrackId> tracks = ReadIds(value, "tracks", diagnostics).Select(id => new DaggerfallTrackId(id)).ToArray();
        IReadOnlyList<string> armorParts = ReadIds(value, "armorParts", diagnostics);
        IReadOnlyList<DaggerfallStatId> progression = ReadIds(value, "progression", diagnostics).Select(id => new DaggerfallStatId(id)).ToArray();
        if (attributes.Count != 9 || skills.Count != 35 || tracks.Count != 3 || armorParts.Count != 7 || progression.Count != 2) diagnostics.Add("Daggerfall vocabulary has an unexpected cardinality.");
        if (attributes.Concat(skills).Select(id => id.Value).Distinct(StringComparer.Ordinal).Count() != attributes.Count + skills.Count) diagnostics.Add("Daggerfall stat and skill ids must be unique.");
        return new(attributes, skills, tracks, armorParts, progression);
    }

    /// <summary>
    /// Reads the normalized reference catalogs and rejects what a consumer could not
    /// resolve: duplicate or non-contiguous keys, a career naming a skill or element the
    /// catalogs do not carry, and a reference to an actor or item the pack does not
    /// define. Runtime code never opens a source file; the provenance it carries is a
    /// citation the import tool already reconciled against the documented inventory.
    /// </summary>
    private static DaggerfallCatalogSet ReadCatalogs(
        JsonElement root,
        DaggerfallVocabulary vocabulary,
        IReadOnlyDictionary<DaggerfallActorId, DaggerfallActorDefinition> actors,
        IReadOnlyDictionary<DaggerfallItemId, DaggerfallItemDefinition> items,
        DaggerfallContentDiagnostics diagnostics)
    {
        JsonElement value = Object(Property(root, "catalogs", diagnostics), "catalogs", diagnostics);
        if (Integer(value, "schemaVersion", diagnostics) != CatalogSchemaVersion)
        {
            diagnostics.Add($"Published catalogs must declare schemaVersion {CatalogSchemaVersion}.");
        }

        IReadOnlyList<string> sources = ReadTexts(value, "sources", diagnostics);
        IReadOnlyList<DaggerfallCatalogKey> attributes = ReadCatalogKeys(value, "attributes", sources, diagnostics);
        IReadOnlyList<DaggerfallCatalogKey> skills = ReadCatalogKeys(value, "skills", sources, diagnostics);
        IReadOnlyList<DaggerfallCatalogKey> resistances = ReadCatalogKeys(value, "resistances", sources, diagnostics);
        string[] attributeKeys = [.. attributes.Select(key => key.Id)];
        string[] skillKeys = [.. skills.Select(key => key.Id)];
        string[] elementKeys = [.. resistances.Select(key => key.Id)];
        if (!attributeKeys.SequenceEqual(vocabulary.Attributes.Take(attributeKeys.Length).Select(id => id.Value), StringComparer.Ordinal))
        {
            diagnostics.Add("The catalog attributes must be the vocabulary's attributes in index order.");
        }

        if (!skillKeys.SequenceEqual(vocabulary.Skills.Select(id => id.Value), StringComparer.Ordinal))
        {
            diagnostics.Add("The catalog skills must be the vocabulary's skills in index order.");
        }

        List<DaggerfallRaceDefinition> races = [];
        foreach (JsonElement race in Array(value, "races", diagnostics))
        {
            DaggerfallRaceDefinition definition = new(
                Text(race, "id", diagnostics),
                Integer(race, "donorRaceId", diagnostics),
                ReadCitation(race, sources, diagnostics));
            if (definition.DonorRaceId <= 0)
            {
                diagnostics.Add($"Race '{definition.Id}' must carry the donor's positive race value.");
            }

            races.Add(definition);
        }

        RequireDistinct(races, race => race.Id, "race", diagnostics);
        RequireDistinct(races, race => race.DonorRaceId.ToString(CultureInfo.InvariantCulture), "race donor value", diagnostics);

        List<DaggerfallCareerDefinition> careers = [];
        foreach (JsonElement career in Array(value, "careers", diagnostics))
        {
            string id = Text(career, "id", diagnostics);
            string name = Text(career, "name", diagnostics);
            IReadOnlyList<string> primary = ReadIds(career, "primarySkills", diagnostics);
            IReadOnlyList<string> major = ReadIds(career, "majorSkills", diagnostics);
            IReadOnlyList<string> minor = ReadIds(career, "minorSkills", diagnostics);
            IReadOnlyList<string> careerAttributes = ReadIds(career, "attributes", diagnostics);
            IReadOnlyList<string> resistant = ReadIds(career, "resistanceElements", diagnostics);
            IReadOnlyList<string> immune = ReadIds(career, "immunityElements", diagnostics);
            int hitPoints = Integer(career, "hitPointsPerLevel", diagnostics);
            float multiplier = Number(career, "advancementMultiplier", diagnostics);
            int resistanceFlags = FlagByte(career, "resistanceFlags", diagnostics);
            int immunityFlags = FlagByte(career, "immunityFlags", diagnostics);
            int lowToleranceFlags = FlagByte(career, "lowToleranceFlags", diagnostics);
            int criticalWeaknessFlags = FlagByte(career, "criticalWeaknessFlags", diagnostics);
            DaggerfallCareerDefinition definition = new(
                id, name, primary, major, minor, careerAttributes, hitPoints, multiplier, resistant, immune,
                resistanceFlags, immunityFlags, lowToleranceFlags, criticalWeaknessFlags, ReadCitation(career, sources, diagnostics));
            foreach (string skill in definition.SkillReferences)
            {
                if (!skillKeys.Contains(skill, StringComparer.Ordinal))
                {
                    diagnostics.Add($"Career '{id}' names skill '{skill}', which the catalog does not carry.");
                }
            }

            foreach (string attribute in careerAttributes)
            {
                if (!attributeKeys.Contains(attribute, StringComparer.Ordinal))
                {
                    diagnostics.Add($"Career '{id}' names attribute '{attribute}', which the catalog does not carry.");
                }
            }

            foreach (string element in resistant.Concat(immune))
            {
                if (!elementKeys.Contains(element, StringComparer.Ordinal))
                {
                    diagnostics.Add($"Career '{id}' names element '{element}', which the catalog does not carry.");
                }
            }

            if (string.IsNullOrWhiteSpace(name) || hitPoints <= 0 || !(multiplier > 0f))
            {
                diagnostics.Add($"Career '{id}' must carry a name, positive hit points per level and a positive advancement multiplier.");
            }

            // The published element lists must be what the carrier's flag bytes say. A
            // list that disagrees with its bytes is content a consumer would act on
            // while the source says otherwise.
            RequireElements(id, "resistanceElements", resistant, resistanceFlags, diagnostics);
            RequireElements(id, "immunityElements", immune, immunityFlags, diagnostics);
            if (primary.Count is < 1 or > 3 || major.Count is < 1 or > 3 || minor.Count is < 1 or > 6)
            {
                diagnostics.Add($"Career '{id}' must name one to three primary, one to three major and one to six minor skills.");
            }

            if (careerAttributes.Count != ClassicAttributeValueCount)
            {
                diagnostics.Add($"Career '{id}' must carry {ClassicAttributeValueCount} attribute keys.");
            }

            string[] trained = [.. definition.SkillReferences];
            if (trained.Distinct(StringComparer.Ordinal).Count() != trained.Length)
            {
                diagnostics.Add($"Career '{id}' must name each skill once across its primary, major and minor lists.");
            }

            careers.Add(definition);
        }

        RequireDistinct(careers, career => career.Id, "career", diagnostics);

        IReadOnlyList<string> collisions = ReadTexts(value, "careerNameCollisions", diagnostics);
        string[] actualCollisions = [.. careers
            .GroupBy(career => career.Name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .Order(StringComparer.Ordinal)];
        if (!collisions.Order(StringComparer.Ordinal).SequenceEqual(actualCollisions, StringComparer.Ordinal))
        {
            diagnostics.Add("The career name collisions the catalogs record must be the names more than one career carries.");
        }

        IReadOnlyList<DaggerfallCatalogReference> enemies = ReadCatalogReferences(value, "enemies", sources, diagnostics);
        foreach (DaggerfallCatalogReference enemy in enemies)
        {
            if (!actors.ContainsKey(new DaggerfallActorId(enemy.Id)))
            {
                diagnostics.Add($"The catalog references enemy '{enemy.Id}', which the pack does not define.");
            }
        }

        IReadOnlyList<DaggerfallCatalogReference> itemTemplates = ReadCatalogReferences(value, "itemTemplates", sources, diagnostics);
        foreach (DaggerfallCatalogReference item in itemTemplates)
        {
            if (!items.ContainsKey(new DaggerfallItemId(item.Id)))
            {
                diagnostics.Add($"The catalog references item template '{item.Id}', which the pack does not define.");
            }
        }

        List<DaggerfallPendingCatalogDefinition> pending = [];
        foreach (JsonElement entry in Array(value, "pending", diagnostics))
        {
            DaggerfallPendingCatalogDefinition definition = new(
                Text(entry, "id", diagnostics),
                Integer(entry, "ownerTask", diagnostics),
                Text(entry, "reason", diagnostics));
            if (definition.OwnerTask <= 0)
            {
                diagnostics.Add($"Pending catalog '{definition.Id}' must name the task that supplies it.");
            }

            pending.Add(definition);
        }

        RequireDistinct(pending, entry => entry.Id, "pending catalog", diagnostics);
        return new DaggerfallCatalogSet(attributes, skills, resistances, races, careers, collisions, enemies, itemTemplates, pending, sources);
    }

    /// <summary>The classic effect flag each element key answers to, in key order.</summary>
    private static readonly int[] CatalogElementFlagMasks = [8, 16, 4 | 64, 32, 2];

    /// <summary>Marks a flag byte the reader already refused, so nothing explains it twice.</summary>
    private const int InvalidFlagByte = -1;

    /// <summary>The attribute keys one career record carries values for.</summary>
    private const int ClassicAttributeValueCount = 8;

    /// <summary>
    /// Reads one classic effect-flag byte. The element lists are an interpretation of
    /// these bytes, so the byte is required and range-checked rather than inferred.
    /// </summary>
    private static int FlagByte(JsonElement value, string property, DaggerfallContentDiagnostics diagnostics)
    {
        int result = Integer(value, property, diagnostics);
        if (result is < 0 or > 255)
        {
            diagnostics.Add($"'{property}' must be one byte.");
            return InvalidFlagByte;
        }

        return result;
    }

    /// <summary>
    /// The published element list must be the interpretation of the flag byte it came
    /// from, so a list that disagrees with its own bytes is refused rather than handed to
    /// a consumer that would act on it.
    /// </summary>
    private static void RequireElements(string id, string property, IReadOnlyList<string> elements, int flags, DaggerfallContentDiagnostics diagnostics)
    {
        if (flags == InvalidFlagByte)
        {
            // The byte itself was already refused; deriving an explanation from a
            // substitute value would report a second fact that is not in the payload.
            return;
        }

        List<string> expected = [];
        for (int index = 0; index < DaggerfallCatalogSet.ElementKeys.Length; index++)
        {
            if ((flags & CatalogElementFlagMasks[index]) != 0)
            {
                expected.Add(DaggerfallCatalogSet.ElementKeys[index]);
            }
        }

        if (!elements.SequenceEqual(expected, StringComparer.Ordinal))
        {
            diagnostics.Add($"Career '{id}' publishes {property} [{string.Join(", ", elements)}] where its flags {flags} say [{string.Join(", ", expected)}].");
        }
    }

    /// <summary>One indexed key catalog: distinct ids, distinct contiguous indices, valid citations.</summary>
    private static IReadOnlyList<DaggerfallCatalogKey> ReadCatalogKeys(JsonElement value, string property, IReadOnlyList<string> sources, DaggerfallContentDiagnostics diagnostics)
    {
        List<DaggerfallCatalogKey> keys = [];
        foreach (JsonElement entry in Array(value, property, diagnostics))
        {
            keys.Add(new DaggerfallCatalogKey(Text(entry, "id", diagnostics), Integer(entry, "index", diagnostics), ReadCitation(entry, sources, diagnostics)));
        }

        if (keys.Select(key => key.Id).Distinct(StringComparer.Ordinal).Count() != keys.Count
            || keys.Select(key => key.Index).Distinct().Count() != keys.Count
            || !keys.Select(key => key.Index).Order().SequenceEqual(Enumerable.Range(0, keys.Count)))
        {
            diagnostics.Add($"'{property}' must carry distinct ids with contiguous indices from zero.");
        }

        return System.Array.AsReadOnly(keys.ToArray());
    }

    private static IReadOnlyList<DaggerfallCatalogReference> ReadCatalogReferences(JsonElement value, string property, IReadOnlyList<string> sources, DaggerfallContentDiagnostics diagnostics)
    {
        List<DaggerfallCatalogReference> references = [];
        foreach (JsonElement entry in Array(value, property, diagnostics))
        {
            references.Add(new DaggerfallCatalogReference(Text(entry, "id", diagnostics), ReadCitation(entry, sources, diagnostics)));
        }

        if (references.Select(reference => reference.Id).Distinct(StringComparer.Ordinal).Count() != references.Count)
        {
            diagnostics.Add($"'{property}' must carry distinct ids.");
        }

        return System.Array.AsReadOnly(references.ToArray());
    }

    /// <summary>
    /// A citation: the documented record id and the path it was read from. The import
    /// tool refuses an id the inventory does not carry, so the runtime checks the shape
    /// and that both halves are present rather than pretending to own the inventory.
    /// </summary>
    private static DaggerfallCatalogCitation ReadCitation(JsonElement value, IReadOnlyList<string> sources, DaggerfallContentDiagnostics diagnostics)
    {
        JsonElement source = Object(Property(value, "source", diagnostics), "source", diagnostics);
        string recordId = Text(source, "recordId", diagnostics);
        string path = Text(source, "path", diagnostics);
        if (!recordId.StartsWith("CNT-", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(path))
        {
            diagnostics.Add($"Catalog source '{recordId}' must name a documented inventory record and the path it came from.");
        }
        else if (!sources.Contains(recordId, StringComparer.Ordinal))
        {
            // The pack states the inventory records it drew from, so a citation outside
            // that set is a defect a consumer can see without owning the inventory.
            diagnostics.Add($"Catalog source '{recordId}' is not one of the {sources.Count} records the pack says it drew from.");
        }

        return new DaggerfallCatalogCitation(recordId, path);
    }

    private static void RequireDistinct<T>(IReadOnlyList<T> values, Func<T, string> key, string kind, DaggerfallContentDiagnostics diagnostics)
    {
        if (values.Select(key).Distinct(StringComparer.Ordinal).Count() != values.Count)
        {
            diagnostics.Add($"'{kind}' entries must carry distinct {kind} identities.");
        }
    }

    /// <summary>
    /// Reads display text, which need not be an Engine-compatible id: a career name such
    /// as "Knight" is published data a consumer displays rather than resolves.
    /// </summary>
    private static IReadOnlyList<string> ReadTexts(JsonElement value, string property, DaggerfallContentDiagnostics diagnostics)
    {
        string[] values = [.. Array(value, property, diagnostics)
            .Select(entry => entry.ValueKind == JsonValueKind.String ? entry.GetString() ?? string.Empty : string.Empty)];
        if (values.Any(string.IsNullOrWhiteSpace) || values.Distinct(StringComparer.Ordinal).Count() != values.Length)
        {
            diagnostics.Add($"'{property}' must contain distinct non-empty text values.");
        }

        return System.Array.AsReadOnly(values);
    }

    private static IReadOnlyList<string> ReadIds(JsonElement value, string property, DaggerfallContentDiagnostics diagnostics)
    {
        string[] values = Array(value, property, diagnostics).Select(entry => entry.ValueKind == JsonValueKind.String ? entry.GetString() ?? string.Empty : string.Empty).ToArray();
        if (values.Any(id => !ValidId(id)) || values.Distinct(StringComparer.Ordinal).Count() != values.Length) diagnostics.Add($"'{property}' must contain distinct Engine-compatible ids.");
        return System.Array.AsReadOnly(values);
    }

    private static void ReadStatMap(JsonElement value, IReadOnlyList<DaggerfallStatId> allowed, Dictionary<DaggerfallStatId, int> target, DaggerfallContentDiagnostics diagnostics)
    {
        if (value.ValueKind != JsonValueKind.Object) { diagnostics.Add("Actor stats must be objects."); return; }
        foreach (JsonProperty property in value.EnumerateObject())
        {
            DaggerfallStatId id = new(property.Name);
            if (!allowed.Contains(id) || property.Value.ValueKind != JsonValueKind.Number || !property.Value.TryGetInt32(out int stat) || stat is < 0 or > 10_000) diagnostics.Add($"Actor stat '{property.Name}' is unsupported or outside the supported range.");
            else target[id] = stat;
        }
    }

    private static IReadOnlyList<DaggerfallAttackRange> ReadAttackRanges(JsonElement actor, DaggerfallContentDiagnostics diagnostics)
    {
        if (!actor.TryGetProperty("attacks", out JsonElement value) || value.ValueKind != JsonValueKind.Array) { diagnostics.Add("Actor attacks must be an array."); return []; }
        List<DaggerfallAttackRange> ranges = [];
        foreach (JsonElement entry in value.EnumerateArray())
        {
            JsonElement range = Object(entry, "attack range", diagnostics);
            int minimum = Integer(range, "minimumDamage", diagnostics), maximum = Integer(range, "maximumDamage", diagnostics);
            if (minimum < 0 || maximum < minimum || maximum > MaximumAuthoredDamage) diagnostics.Add("Attack range is invalid.");
            ranges.Add(new(minimum, maximum));
        }
        return System.Array.AsReadOnly(ranges.ToArray());
    }


    private static IReadOnlyList<DaggerfallLoadoutEntry> ReadLoadout(JsonElement actor, IReadOnlyDictionary<DaggerfallItemId, DaggerfallItemDefinition> items, DaggerfallContentDiagnostics diagnostics)
    {
        if (!actor.TryGetProperty("loadout", out JsonElement value)) return [];
        List<DaggerfallLoadoutEntry> values = [];
        foreach (JsonElement entry in Array(actor, "loadout", diagnostics))
        {
            JsonElement item = Object(entry, "actor loadout", diagnostics);
            string id = Text(item, "item", diagnostics);
            DaggerfallItemId itemId = new(id);
            bool hasEntity = item.TryGetProperty("entityId", out JsonElement entityValue) && entityValue.ValueKind != JsonValueKind.Null;
            bool hasQuantity = item.TryGetProperty("quantity", out JsonElement quantityValue) && quantityValue.ValueKind != JsonValueKind.Null;
            ulong parsedEntity = 0;
            ulong parsedQuantity = 0;
            bool validEntity = !hasEntity || entityValue.ValueKind == JsonValueKind.Number && entityValue.TryGetUInt64(out parsedEntity) && parsedEntity > 0;
            bool validQuantity = !hasQuantity || quantityValue.ValueKind == JsonValueKind.Number && quantityValue.TryGetUInt64(out parsedQuantity);
            ulong? entity = hasEntity && validEntity ? parsedEntity : null;
            ulong quantity = hasQuantity ? validQuantity ? parsedQuantity : 0 : 1;
            DaggerfallEquipmentSlotId? slot = item.TryGetProperty("equipSlot", out JsonElement slotValue) && slotValue.ValueKind != JsonValueKind.Null ? new(Text(item, "equipSlot", diagnostics)) : null;
            if (!items.TryGetValue(itemId, out DaggerfallItemDefinition? definition)) diagnostics.Add($"Actor loadout refers to missing item '{id}'.");
            else if (definition.IsFungible && (!hasQuantity || !validQuantity || quantity == 0 || hasEntity || slot is not null)) diagnostics.Add($"Fungible loadout item '{id}' requires a positive unsigned quantity and cannot have an entity or equipment slot.");
            else if (!definition.IsFungible && (!hasEntity || !validEntity || entity is null || hasQuantity)) diagnostics.Add($"Unique loadout item '{id}' requires a non-zero unsigned entity id and cannot have a quantity.");
            values.Add(new(itemId, quantity, entity, slot));
        }
        return System.Array.AsReadOnly(values.ToArray());
    }

    private static Dictionary<string, int> ReadIntegerMap(JsonElement root, string property, DaggerfallContentDiagnostics diagnostics)
    {
        JsonElement value = Object(Property(root, property, diagnostics), property, diagnostics); Dictionary<string, int> result = new(StringComparer.Ordinal);
        foreach (JsonProperty entry in value.EnumerateObject()) if (!ValidId(entry.Name) || !entry.Value.TryGetInt32(out int number) || number < 0 || !result.TryAdd(entry.Name, number)) diagnostics.Add($"'{property}' contains an invalid value.");
        return result;
    }

    private static Dictionary<string, DaggerfallActionDefinition> ReadActions(JsonElement root, DaggerfallContentDiagnostics diagnostics)
    {
        Dictionary<string, DaggerfallActionDefinition> result = new(StringComparer.Ordinal);
        foreach (JsonElement entry in Array(root, "actions", diagnostics))
        {
            JsonElement action = Object(entry, "action", diagnostics);
            string id = Text(action, "id", diagnostics);
            string interpretation = Text(action, "interpretation", diagnostics);
            string skill = Text(action, "skill", diagnostics);
            string[] tags = Array(action, "tags", diagnostics).Select(x => x.GetString() ?? string.Empty).ToArray();
            double? reach = OptionalNumber(action, "reach", diagnostics);
            double? cooldown = OptionalNumber(action, "cooldownSeconds", diagnostics);
            int? stamina = OptionalInteger(action, "staminaCost", diagnostics);
            int? attackRangeIndex = OptionalInteger(action, "attackRangeIndex", diagnostics);
            int damageBonus = OptionalInteger(action, "damageBonus", diagnostics) ?? 0;
            bool hasMinimum = action.TryGetProperty("minimumDamage", out JsonElement minimumValue) && minimumValue.ValueKind != JsonValueKind.Null;
            bool hasMaximum = action.TryGetProperty("maximumDamage", out JsonElement maximumValue) && maximumValue.ValueKind != JsonValueKind.Null;
            int? minimum = hasMinimum && minimumValue.TryGetInt32(out int parsedMinimum) ? parsedMinimum : null;
            int? maximum = hasMaximum && maximumValue.TryGetInt32(out int parsedMaximum) ? parsedMaximum : null;
            bool directRange = hasMinimum || hasMaximum;
            bool validDamageShape = interpretation == "player-equipped-melee"
                ? attackRangeIndex is null && !directRange
                : (attackRangeIndex is not null) != directRange;
            bool valid = ValidId(id)
                && interpretation is "player-equipped-melee" or "fixed-melee"
                && tags.Length > 0 && tags.All(ValidId)
                && (skill == "equipped" || ValidId(skill))
                && reach is null or >= 0 and <= 100
                && cooldown is > 0 and <= 60
                && stamina is null or > 0 and <= 10_000
                && attackRangeIndex is null or >= 0 and <= 16
                && damageBonus is >= -MaximumAuthoredDamage and <= MaximumAuthoredDamage
                && (!directRange || minimum is not null && maximum is not null && minimum >= 0 && maximum >= minimum && maximum <= MaximumAuthoredDamage)
                && validDamageShape;
            if (!valid || !result.TryAdd(id, new(id, System.Array.AsReadOnly(tags), interpretation, skill, attackRangeIndex, minimum, maximum, stamina, reach, cooldown, damageBonus))) diagnostics.Add($"Action '{id}' is invalid or duplicated.");
        }
        return result;
    }

    private static Dictionary<string, DaggerfallLootTableDefinition> ReadLootTables(JsonElement root, DaggerfallContentDiagnostics diagnostics)
    {
        string[] categories = ["plant1", "plant2", "creature1", "creature2", "creature3", "misc1", "misc2", "armor", "weapons", "magic", "clothing", "books", "religious"];
        Dictionary<string, DaggerfallLootTableDefinition> result = new(StringComparer.Ordinal);
        foreach (JsonElement entry in Array(root, "lootTables", diagnostics)) { JsonElement table = Object(entry, "loot table", diagnostics); string key = Text(table, "key", diagnostics); JsonElement gold = Object(Property(table, "gold", diagnostics), "loot gold", diagnostics); int minimum = Integer(gold, "minimum", diagnostics), maximum = Integer(gold, "maximum", diagnostics); Dictionary<string, int> values = ReadIntegerMap(table, "categories", diagnostics); if (key.Length != 1 || (key != "-" && (key[0] < 'A' || key[0] > 'U')) || minimum < 0 || maximum < minimum || maximum > MaximumAuthoredLootGold || values.Keys.Any(id => !categories.Contains(id)) || values.Values.Any(value => value is < 0 or > 100) || !result.TryAdd(key, new(key, minimum, maximum, new ReadOnlyDictionary<string, int>(values)))) diagnostics.Add($"Loot table '{key}' is invalid or duplicated."); }
        return result;
    }

    private static void ValidateCatalog(DaggerfallVocabulary vocabulary, IReadOnlyDictionary<DaggerfallActorId, DaggerfallActorDefinition> actors, IReadOnlyDictionary<DaggerfallItemId, DaggerfallItemDefinition> items, IReadOnlyDictionary<DaggerfallEquipmentSlotId, DaggerfallEquipmentSlotDefinition> slots, IReadOnlyDictionary<string, int> armorValues, IReadOnlyDictionary<string, DaggerfallActionDefinition> actions, IReadOnlyDictionary<string, DaggerfallLootTableDefinition> loot, IReadOnlyList<DaggerfallDeferredLootCategoryPool> pools, IReadOnlyList<DaggerfallDonorErratum> errata, DaggerfallContentDiagnostics diagnostics)
    {
        int[] expectedMobiles = [.. Enumerable.Range(0, 39), .. Enumerable.Range(40, 3)];
        int[] actualMobiles = actors.Values.Where(actor => actor.Kind == "monster").Select(actor => actor.MobileId ?? -1).Order().ToArray();
        if (actors.Count != 45 || actors.Values.Count(actor => actor.Kind == "monster") != 42 || !actualMobiles.SequenceEqual(expectedMobiles) || !actors.TryGetValue(new("thief"), out DaggerfallActorDefinition? thief) || thief.MobileId != 138 || !actors.TryGetValue(new("archer"), out DaggerfallActorDefinition? archer) || archer.MobileId != 141) diagnostics.Add("Daggerfall actor roster must contain exactly mobiles 0..38, 40..42, thief 138, archer 141, and player without a mobile id.");
        if (items.Count != 31 || slots.Count != 25 || actions.Count != 5 || loot.Count != 22 || armorValues.Count != 12) diagnostics.Add("Daggerfall catalog cardinality does not match the adopted donor snapshot.");
        if (armorValues.Values.Any(value => value > MaximumAuthoredArmor)) diagnostics.Add("Armor values by material exceed the Daggerfall policy bound.");
        if (!actors.TryGetValue(new("player"), out DaggerfallActorDefinition? player) || player.Loadout.Count == 0) diagnostics.Add("Daggerfall player loadout is required.");
        if (!loot.ContainsKey("-") || Enumerable.Range('A', 21).Select(value => ((char)value).ToString()).Any(key => !loot.ContainsKey(key))) diagnostics.Add("Daggerfall loot keys must be '-' and A through U.");
        Dictionary<string, (string Interpretation, string Skill)> expectedActions = new(StringComparer.Ordinal)
        {
            ["melee-attack"] = ("player-equipped-melee", "equipped"),
            ["power-attack"] = ("player-equipped-melee", "equipped"),
            ["rat-bite"] = ("fixed-melee", "hand-to-hand"),
            ["skeleton-strike"] = ("fixed-melee", "long-blade"),
            ["thief-strike"] = ("fixed-melee", "short-blade"),
        };
        if (actions.Count != expectedActions.Count || actions.Any(pair => !expectedActions.TryGetValue(pair.Key, out (string Interpretation, string Skill) expected) || pair.Value.Interpretation != expected.Interpretation || pair.Value.Skill != expected.Skill || !pair.Value.Tags.SequenceEqual(["attack", "melee"]))) diagnostics.Add("Actions must be the exact five adopted ids, interpretations, skills, and tags.");
        if (!actions.TryGetValue("melee-attack", out DaggerfallActionDefinition? melee) || melee.StaminaCost != 5 || melee.MinimumDamage is not null || melee.MaximumDamage is not null || melee.AttackRangeIndex is not null
            || !actions.TryGetValue("power-attack", out DaggerfallActionDefinition? power) || power.StaminaCost != 25 || power.DamageBonus != 4 || power.MinimumDamage is not null || power.MaximumDamage is not null || power.AttackRangeIndex is not null
            || !actions.TryGetValue("rat-bite", out DaggerfallActionDefinition? ratAction) || ratAction.AttackRangeIndex != 0 || ratAction.MinimumDamage is not null || ratAction.MaximumDamage is not null
            || !actions.TryGetValue("skeleton-strike", out DaggerfallActionDefinition? skeletonAction) || skeletonAction.AttackRangeIndex != 0 || skeletonAction.MinimumDamage is not null || skeletonAction.MaximumDamage is not null
            || !actions.TryGetValue("thief-strike", out DaggerfallActionDefinition? thiefAction) || thiefAction.AttackRangeIndex is not null || thiefAction.MinimumDamage != 2 || thiefAction.MaximumDamage != 8) diagnostics.Add("Action damage and stamina ownership does not match the adopted actor/action catalog.");
        if (!actors.TryGetValue(new("player"), out DaggerfallActorDefinition? playerActionOwner) || playerActionOwner.ActionId != "melee-attack"
            || !actors.TryGetValue(new("rat"), out DaggerfallActorDefinition? ratActionOwner) || ratActionOwner.ActionId != "rat-bite"
            || !actors.TryGetValue(new("skeletal-warrior"), out DaggerfallActorDefinition? skeletonActionOwner) || skeletonActionOwner.ActionId != "skeleton-strike"
            || !actors.TryGetValue(new("thief"), out DaggerfallActorDefinition? thiefActionOwner) || thiefActionOwner.ActionId != "thief-strike") diagnostics.Add("The four adopted actor/action associations must remain explicit.");
        if (actions.Values.Any(action => action.Id != "power-attack" && action.DamageBonus != 0)) diagnostics.Add("Only the authored power-attack may carry an action damage bonus.");
        string[] categories = ["plant1", "plant2", "creature1", "creature2", "creature3", "misc1", "misc2", "armor", "weapons", "magic", "clothing", "books", "religious"];
        if (pools.Count != categories.Length || !pools.Select(pool => pool.Id).Order().SequenceEqual(categories.Order()) || pools.Any(pool => pool.Status != "deferred" || string.IsNullOrWhiteSpace(pool.Reason))) diagnostics.Add("Deferred loot category pools must be the exact adopted category set with a reason.");
        string[] expectedErrata = ["mobile-39-horse-is-explicitly-absent", "chain2-material-alias-is-not-authored", "bows-retain-donor-both-hands-policy", "loot-matrix-uses-fall-exe-errata"];
        if (!errata.Select(erratum => erratum.Id).Order().SequenceEqual(expectedErrata.Order())) diagnostics.Add("Donor errata must name mobile 39, Chain2 omission, bow two-hand policy, and loot errata exactly.");
    }

    internal static JsonElement Property(JsonElement value, string property, DaggerfallContentDiagnostics diagnostics)
    {
        if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty(property, out JsonElement result)) return result;
        diagnostics.Add($"Required property '{property}' is missing.");
        return default;
    }
    /// <summary>The published catalog shape this reader understands.</summary>
    private const int CatalogSchemaVersion = 1;

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
    internal static string? OptionalText(JsonElement value, string property, DaggerfallContentDiagnostics diagnostics)
    {
        if (!value.TryGetProperty(property, out JsonElement result) || result.ValueKind == JsonValueKind.Null) return null;
        if (result.ValueKind == JsonValueKind.String && result.GetString() is { Length: > 0 } text) return text;
        diagnostics.Add($"'{property}' must be a non-empty string or null.");
        return null;
    }
    internal static double? OptionalNumber(JsonElement value, string property, DaggerfallContentDiagnostics diagnostics)
    {
        if (!value.TryGetProperty(property, out JsonElement result) || result.ValueKind == JsonValueKind.Null) return null;
        if (result.ValueKind == JsonValueKind.Number && result.TryGetDouble(out double number) && double.IsFinite(number)) return number;
        diagnostics.Add($"'{property}' must be a finite number or null.");
        return null;
    }
    internal static float Number(JsonElement value, string property, DaggerfallContentDiagnostics diagnostics)
    {
        JsonElement result = Property(value, property, diagnostics);
        if (result.ValueKind == JsonValueKind.Number && result.TryGetSingle(out float number) && float.IsFinite(number)) return number;
        diagnostics.Add($"'{property}' must be a finite number.");
        return 0f;
    }
    /// <summary>Uses the Engine's public Mechanics identity parser at every Engine-bound boundary.</summary>
    internal static bool ValidId(string value) => StatId.TryParse(value, out _);
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
