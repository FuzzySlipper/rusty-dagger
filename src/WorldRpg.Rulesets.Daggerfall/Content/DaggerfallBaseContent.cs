using System.Buffers;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Rusty.Engine.Mechanics;
using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>
/// Reads the base definitions from two payloads: the authored daggerfall.base payload (ruleset identity,
/// vocabulary, actors, items, loot, encounters) and the daggerfall.imported payload the import tool
/// generates from the operator's Arena2 files (catalogs, world records, text). The quests are read
/// from the quests packs (<see cref="DaggerfallQuestContent"/>) and joined with <see cref="DaggerfallDefinitions.WithQuestSources"/>.
/// </summary>
internal static partial class DaggerfallBaseContent
{
    /// <summary>The ruleset every Daggerfall pack names, and the identity the ruleset reports.</summary>
    internal const string RulesetName = "daggerfall";

    private const int MaximumAuthoredDamage = 100_000;
    private const int MaximumAuthoredArmor = 1_000;
    private const int MaximumAuthoredLootGold = 1_000_000;

    /// <summary>
    /// Reads the authored and imported base payloads as one set of sections, parsed once where they lie, and the
    /// published location details and terrain samples from <paramref name="data"/> when they are first used.
    /// </summary>
    internal static DaggerfallDefinitions Read(ReadOnlyMemory<byte> authored, ReadOnlyMemory<byte> imported, DaggerfallData? data)
    {
        DaggerfallContentDiagnostics diagnostics = new();
        ReadOnlySequence<byte> joined = Join(authored, imported, diagnostics);
        try
        {
            using JsonDocument document = JsonDocument.Parse(joined);
            return Read(document, data, diagnostics);
        }
        catch (JsonException exception)
        {
            diagnostics.Add($"Base payload is not valid JSON: {exception.Message}");
            throw diagnostics.Exception();
        }
    }

    /// <summary>The authored and imported payloads joined into one root as <see cref="Read(ReadOnlyMemory{byte}, ReadOnlyMemory{byte}, DaggerfallData?)"/> reads them.</summary>
    internal static byte[] Combine(ReadOnlyMemory<byte> authored, ReadOnlyMemory<byte> imported) =>
        Join(authored, imported, new DaggerfallContentDiagnostics()).ToArray();

    /// <summary>
    /// Joins the authored and imported payloads' root sections into one root without copying either: the authored
    /// object up to its closing brace, a comma, then the imported object after its opening brace. Every section has
    /// one owner: a section both payloads carry is refused rather than resolved by precedence, because either copy
    /// winning would silently discard the other.
    /// </summary>
    private static ReadOnlySequence<byte> Join(ReadOnlyMemory<byte> authored, ReadOnlyMemory<byte> imported, DaggerfallContentDiagnostics diagnostics)
    {
        authored = Trimmed(authored);
        imported = Trimmed(imported);
        try
        {
            string[] authoredSections = SectionNames(authored, "authored base payload", diagnostics);
            string[] importedSections = SectionNames(imported, "imported base payload", diagnostics);
            diagnostics.ThrowIfAny();
            foreach (string section in importedSections.Intersect(authoredSections, StringComparer.Ordinal))
                diagnostics.Add($"Section '{section}' is in both the authored and the imported base payload; each section has one owner.");
            diagnostics.ThrowIfAny();
            if (importedSections.Length == 0) return new ReadOnlySequence<byte>(authored);
            if (authoredSections.Length == 0) return new ReadOnlySequence<byte>(imported);
        }
        catch (JsonException exception)
        {
            diagnostics.Add($"Base payload is not valid JSON: {exception.Message}");
            throw diagnostics.Exception();
        }

        PayloadSegment first = new(authored[..^1], 0);
        PayloadSegment last = first.Append(","u8.ToArray()).Append(imported[1..]);
        return new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);

        static ReadOnlyMemory<byte> Trimmed(ReadOnlyMemory<byte> payload)
        {
            ReadOnlySpan<byte> span = payload.Span;
            int start = span.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]) ? 3 : 0;
            while (start < span.Length && span[start] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n') start++;
            int end = span.Length;
            while (end > start && span[end - 1] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n') end--;
            return payload[start..end];
        }
    }

    /// <summary>A payload's top-level section names, read without materializing the sections.</summary>
    private static string[] SectionNames(ReadOnlyMemory<byte> payload, string what, DaggerfallContentDiagnostics diagnostics)
    {
        Utf8JsonReader reader = new(payload.Span);
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
        {
            diagnostics.Add($"The {what} must be a JSON object.");
            return [];
        }

        List<string> names = [];
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            names.Add(reader.GetString()!);
            reader.Read();
            reader.Skip();
        }

        if (reader.TokenType != JsonTokenType.EndObject || reader.Read())
            diagnostics.Add($"The {what} is not one JSON object.");
        return [.. names];
    }

    /// <summary>One piece of a joined payload.</summary>
    private sealed class PayloadSegment : ReadOnlySequenceSegment<byte>
    {
        internal PayloadSegment(ReadOnlyMemory<byte> memory, long runningIndex)
        {
            Memory = memory;
            RunningIndex = runningIndex;
        }

        internal PayloadSegment Append(ReadOnlyMemory<byte> memory)
        {
            PayloadSegment next = new(memory, RunningIndex + Memory.Length);
            Next = next;
            return next;
        }
    }

    /// <summary>Reads one payload carrying every section, and the published location details and terrain samples from <paramref name="data"/>.</summary>
    internal static DaggerfallDefinitions Read(ReadOnlyMemory<byte> payload, DaggerfallData? data = null)
    {
        DaggerfallContentDiagnostics diagnostics = new();
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            return Read(document, data, diagnostics);
        }
        catch (JsonException exception)
        {
            diagnostics.Add($"Base payload is not valid JSON: {exception.Message}");
            throw diagnostics.Exception();
        }
    }

    private static DaggerfallDefinitions Read(JsonDocument document, DaggerfallData? data, DaggerfallContentDiagnostics diagnostics)
    {
        try
        {
            JsonElement root = Object(document.RootElement, "root", diagnostics);
            RejectDuplicateProperties(root, "root", diagnostics);
            if (Text(root, "ruleset", diagnostics) != RulesetName) diagnostics.Add("Base payload must identify ruleset 'daggerfall'.");
            DaggerfallVocabulary vocabulary = ReadVocabulary(root, diagnostics);
            Dictionary<string, DaggerfallActionDefinition> actions = ReadActions(root, diagnostics);
            Dictionary<DaggerfallItemId, DaggerfallItemDefinition> items = ReadItems(root, diagnostics);
            Dictionary<DaggerfallEquipmentSlotId, DaggerfallEquipmentSlotDefinition> equipmentSlots = ReadEquipmentSlots(root, diagnostics);
            Dictionary<string, int> armorValues = ReadIntegerMap(root, "armorValuesByMaterial", diagnostics);
            Dictionary<string, DaggerfallLootTableDefinition> lootTables = ReadLootTables(root, diagnostics);
            Dictionary<DaggerfallActorId, DaggerfallActorDefinition> actors = ReadActors(root, vocabulary, actions, items, diagnostics);
            List<DaggerfallHudResourceDefinition> hud = ReadHud(root, diagnostics);
            DaggerfallCatalogSet catalogs = ReadCatalogs(root, vocabulary, diagnostics);
            DaggerfallMobileCatalogSet mobiles = ReadMobileCatalog(root, actors, diagnostics);
            DaggerfallEncounterActors.AddMissing(actors, catalogs, mobiles, vocabulary);
            DaggerfallEncounterSet encounters = ReadEncounters(root, catalogs, mobiles, diagnostics);
            ValidateActorIdentities(actors, catalogs, mobiles, diagnostics);
            DaggerfallItemTemplateSet itemTemplatesCatalog = ReadItemTemplates(root, diagnostics);
            DaggerfallCharacterPresentationSet characterPresentation = ReadCharacterPresentation(root, catalogs, itemTemplatesCatalog, diagnostics);
            DaggerfallMagicCatalogSet magic = ReadMagicCatalog(root, diagnostics);
            DaggerfallEnemySpells enemySpells = ReadEnemySpells(root, mobiles, magic, diagnostics);
            DaggerfallLocationSet locations = ReadLocations(root, data, diagnostics);
            (DaggerfallTextSet text, DaggerfallNameTablesSet names, DaggerfallRumorCatalogSet rumors, DaggerfallDialogueWorldRules dialogueWorldRules, DaggerfallBiographiesSet biographies, DaggerfallBooksSet books) = ReadTextAndKeyedSections(root, diagnostics);
            DaggerfallBuildingNameInputs buildingNames = ReadBuildingNameInputs(root, locations.Regions, diagnostics);
            DaggerfallWorldGridsSet grids = ReadWorldGrids(root, diagnostics);
            DaggerfallFactionsSet factions = ReadFactions(root, locations.Regions, diagnostics);
            DaggerfallTerrainSet terrain = ReadTerrain(root, data, diagnostics);
            foreach (var recipe in magic.PotionRecipes.Values) recipe.Validate(itemTemplatesCatalog);
            DaggerfallCinematicSet cinematics = ReadCinematics(root, diagnostics);
            ValidateReferences(vocabulary, actors, items, equipmentSlots, armorValues, actions, lootTables, catalogs, mobiles, hud, diagnostics);
            ValidateCatalog(actors, armorValues, actions, lootTables, diagnostics);
            foreach (string problem in DaggerfallEnchantmentSettings.Validate(magic.EnchantmentSettings.Values))
                diagnostics.Add(problem);
            DaggerfallAssembledSiteDefinition? assembledSites = ReadAssembledSites(root, diagnostics);
            DaggerfallNewGameDefinition newGame = ReadNewGame(root, catalogs, itemTemplatesCatalog, DaggerfallTemplateItemDefinitions.Create(itemTemplatesCatalog, items, magic), equipmentSlots, magic, diagnostics);
            JsonElement rangedSetup = Property(root, "enemyRanged", diagnostics);
            DaggerfallEnemyRangedDefinition enemyRanged = new(Integer(rangedSetup, "bowTemplate", diagnostics),
                new(Text(rangedSetup, "arrowItem", diagnostics)), Integer(rangedSetup, "startingArrows", diagnostics),
                Number(rangedSetup, "minimumDistance", diagnostics));
            var templateItems = DaggerfallTemplateItemDefinitions.Create(itemTemplatesCatalog, items, magic);
            if (!templateItems.TryGetValue(new($"template-{enemyRanged.BowTemplate}-iron"), out var rangedBow) || rangedBow.Weapon?.IsBow != true
                || !items.TryGetValue(enemyRanged.ArrowItem, out var arrows) || !arrows.IsFungible
                || enemyRanged.StartingArrows <= 0 || enemyRanged.MinimumDistance <= 0
                || !actions.TryGetValue("archer-shot", out var rangedAction) || rangedAction.Interpretation != "enemy-equipped-ranged"
                || enemyRanged.MinimumDistance >= rangedAction.Reach)
                diagnostics.Add("Enemy ranged setup requires an admitted bow, fungible arrows, positive quiver count and valid range band.");
            diagnostics.ThrowIfAny();
            DaggerfallCreatureFactionSet creatureFactions = root.TryGetProperty("creatureFactions", out JsonElement creatureFactionsEl)
                ? DaggerfallCreatureFactionSet.Read(creatureFactionsEl, diagnostics)
                : DaggerfallCreatureFactionSet.ClassicDefault;
            diagnostics.ThrowIfAny();
            return new DaggerfallDefinitions(catalogs, vocabulary, new ReadOnlyDictionary<DaggerfallActorId, DaggerfallActorDefinition>(actors), new ReadOnlyDictionary<DaggerfallItemId, DaggerfallItemDefinition>(items), new ReadOnlyDictionary<DaggerfallEquipmentSlotId, DaggerfallEquipmentSlotDefinition>(equipmentSlots), new ReadOnlyDictionary<string, int>(armorValues), new ReadOnlyDictionary<string, DaggerfallActionDefinition>(actions), new ReadOnlyDictionary<string, DaggerfallLootTableDefinition>(lootTables), System.Array.AsReadOnly(hud.ToArray()), characterPresentation, locations, text, magic, mobiles, names, rumors, biographies, grids, books, factions, terrain, itemTemplatesCatalog, DaggerfallQuestSourceSet.None, cinematics, encounters)
            {
                CreatureFactions = creatureFactions,
                BuildingNames = buildingNames,
                EnemySpells = enemySpells,
                EnemyRanged = enemyRanged,
                DialogueWorldRules = dialogueWorldRules,
                NewGame = newGame,
                AssembledSites = assembledSites,
                ClassicSelection = DaggerfallClassicSelection.Read(root, diagnostics),
                QuestJournalTitles = ReadQuestJournalTitles(root, diagnostics),
                Help = ReadHelp(root, diagnostics),
                Tutorial = ReadTutorial(root, diagnostics),
                WorldNames = ReadWorldNames(root, diagnostics),
            };
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

    /// <summary>
    /// The authored presentation every location assembled from its blocks shares: its world appearance and
    /// the navigation grid its block artifacts are placed on. A payload without it admits no location: an
    /// assembly that needs it refuses by name.
    /// </summary>
    private static DaggerfallAssembledSiteDefinition? ReadAssembledSites(JsonElement root, DaggerfallContentDiagnostics diagnostics)
    {
        if (!root.TryGetProperty("assembledSites", out JsonElement section)) return null;
        JsonElement value = Object(section, "assembledSites", diagnostics);
        RejectDuplicateProperties(value, "assembledSites", diagnostics);
        AuthoredWorldAppearance appearance = DaggerfallSiteContent.ReadWorldAppearance(Object(Property(value, "appearance", diagnostics), "assembledSites.appearance", diagnostics), diagnostics);
        long grid = Long(value, "navigationGridId", diagnostics);
        if (grid <= 0) diagnostics.Add("assembledSites.navigationGridId must be a positive navigation grid.");
        float reach = Number(value, "doorReach", diagnostics);
        float building = Number(value, "buildingExitLanding", diagnostics);
        float dungeon = Number(value, "dungeonExitLanding", diagnostics);
        if (!float.IsFinite(reach) || reach <= 0F) diagnostics.Add("assembledSites.doorReach must be a positive distance.");
        if (!float.IsFinite(building) || building < 0F || !float.IsFinite(dungeon) || dungeon < 0F)
            diagnostics.Add("assembledSites.buildingExitLanding and dungeonExitLanding must be non-negative distances.");
        return new DaggerfallAssembledSiteDefinition(appearance, (ulong)Math.Max(grid, 1), reach, building, dungeon);
    }

    /// <summary>
    /// The text section and the sections whose readers resolve their keys against it. No other reader or
    /// cross-section check consults any of them, or the sets they produce.
    /// </summary>
    internal static readonly IReadOnlyList<string> TextSectionNames = ["text", "internalText", "names", "rumors", "dialogueWorldRules", "biographies", "books"];

    /// <summary>
    /// Reads the text section and then every section that resolves text keys through it, so each key
    /// check runs against the text this document carries.
    /// </summary>
    private static (DaggerfallTextSet Text, DaggerfallNameTablesSet Names, DaggerfallRumorCatalogSet Rumors, DaggerfallDialogueWorldRules DialogueWorldRules, DaggerfallBiographiesSet Biographies, DaggerfallBooksSet Books) ReadTextAndKeyedSections(JsonElement root, DaggerfallContentDiagnostics diagnostics)
    {
        DaggerfallTextSet text = ReadText(root, diagnostics);
        return (text, ReadNameTables(root, text, diagnostics), ReadRumorCatalog(root, text, diagnostics), ReadDialogueWorldRules(root, text, diagnostics), ReadBiographies(root, text, diagnostics), ReadBooks(root, text, diagnostics));
    }

    /// <summary>
    /// Already-admitted definitions with their text section, and every section whose keys resolve through
    /// it, replaced by the ones this document carries. Those sections are read by
    /// <see cref="ReadTextAndKeyedSections"/> alone — the same readers and key checks
    /// <see cref="Read(ReadOnlyMemory{byte})"/> runs — and nothing else consults them, so this admits
    /// exactly what a full read of a payload carrying them beside the admitted sections would, without
    /// re-reading every other section. The document must carry each text-keyed section the admitted
    /// payload carried: one it omits is read as absent, as a full read would.
    /// </summary>
    internal static DaggerfallDefinitions ReadTextSections(DaggerfallDefinitions admitted, ReadOnlyMemory<byte> sections)
    {
        DaggerfallContentDiagnostics diagnostics = new();
        try
        {
            using JsonDocument document = JsonDocument.Parse(sections);
            JsonElement root = Object(document.RootElement, "root", diagnostics);
            diagnostics.ThrowIfAny();
            RejectDuplicateProperties(root, "root", diagnostics);
            foreach (JsonProperty section in root.EnumerateObject().Where(section => !TextSectionNames.Contains(section.Name, StringComparer.Ordinal)))
                diagnostics.Add($"Section '{section.Name}' is not a text-keyed section; replacing text cannot admit it.");
            (DaggerfallTextSet text, DaggerfallNameTablesSet names, DaggerfallRumorCatalogSet rumors, DaggerfallDialogueWorldRules dialogueWorldRules, DaggerfallBiographiesSet biographies, DaggerfallBooksSet books) = ReadTextAndKeyedSections(root, diagnostics);
            diagnostics.ThrowIfAny();
            return admitted.WithText(text, names, rumors, dialogueWorldRules, biographies, books);
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

    private static DaggerfallEncounterSet ReadEncounters(JsonElement root, DaggerfallCatalogSet catalogs, DaggerfallMobileCatalogSet mobiles, DaggerfallContentDiagnostics diagnostics)
    {
        if (!root.TryGetProperty("encounters", out JsonElement section) || section.ValueKind != JsonValueKind.Object)
        {
            diagnostics.Add("Base payload must publish the complete random encounter tables.");
            return new DaggerfallEncounterSet([]);
        }
        List<IReadOnlyList<int>> tables = [];
        if (!section.TryGetProperty("tables", out JsonElement sourceTables) || sourceTables.ValueKind != JsonValueKind.Array)
        {
            diagnostics.Add("Random encounter tables must be an array.");
            return new DaggerfallEncounterSet([]);
        }
        foreach (JsonElement value in sourceTables.EnumerateArray())
        {
            int[] table = value.ValueKind == JsonValueKind.Array
                ? value.EnumerateArray().Select(entry => entry.ValueKind == JsonValueKind.Number && entry.TryGetInt32(out int mobile) ? mobile : -1).ToArray()
                : [];
            if (table.Length != 20 || table.Any(mobile => !mobiles.Mobiles.ContainsKey(mobile))) diagnostics.Add("Every random encounter table must carry twenty published mobile ids.");
            tables.Add(System.Array.AsReadOnly(table));
        }
        if (tables.Count != 45) diagnostics.Add($"Random encounter corpus must carry forty-five tables, found {tables.Count}.");
        return new DaggerfallEncounterSet(System.Array.AsReadOnly(tables.ToArray()));
    }

    /// <summary>
    /// Reads the published character presentation references, resolving each layer by race, gender
    /// and role rather than by position.
    /// </summary>
    /// <remarks>
    /// The layer names are the published contract, so the reader derives the role from the name and
    /// refuses a name it does not know instead of guessing a role from order. Every layer's source
    /// file must be one the section accounted for, which is what makes a reference to a file the
    /// corpus does not carry fail with that file named rather than resolving to nothing.
    /// </remarks>
    private static DaggerfallCharacterPresentationSet ReadCharacterPresentation(
        JsonElement root,
        DaggerfallCatalogSet catalogs,
        DaggerfallItemTemplateSet templates,
        DaggerfallContentDiagnostics diagnostics)
    {
        if (!root.TryGetProperty("characterPresentation", out JsonElement section) || section.ValueKind != JsonValueKind.Object)
        {
            diagnostics.Add("Base payload must publish a characterPresentation section.");
            return new DaggerfallCharacterPresentationSet(new Dictionary<string, DaggerfallRaceLayers>(StringComparer.Ordinal), [], new Dictionary<string, DaggerfallCareerPortraitDefinition>(StringComparer.Ordinal), [], [], []);
        }

        List<string> files = [];
        foreach (JsonElement file in Array(section, "files", diagnostics))
        {
            string path = Text(file, "path", diagnostics);
            if (path.Length != 0) files.Add(path);
        }
        HashSet<string> accounted = new(files, StringComparer.OrdinalIgnoreCase);
        // The media is published by the classic race value it is drawn for; each catalog race takes the layers of its own
        // value, and media for a value no race carries is drawn for no one.
        Dictionary<int, string> raceByValue = catalogs.Races.ToDictionary(race => race.DonorRaceId, race => race.Id);
        Dictionary<string, List<DaggerfallCharacterLayerDefinition>> byRace = new(StringComparer.Ordinal);
        foreach (JsonElement layer in Array(section, "layers", diagnostics))
        {
            int donorRaceId = Integer(layer, "donorRaceId", diagnostics);
            string name = Text(layer, "layer", diagnostics);
            string mediaId = Text(layer, "mediaId", diagnostics);
            string sourceFile = Text(layer, "sourceFile", diagnostics);
            string palette = Text(layer, "palette", diagnostics);
            _ = Text(layer, "binding", diagnostics);
            string consumer = Text(layer, "consumer", diagnostics);

            string raceId = raceByValue.GetValueOrDefault(donorRaceId, $"race value {donorRaceId}");
            // A layer whose source file the section does not account for is a reference to a file the
            // corpus does not carry, and it says so with the file named.
            if (sourceFile.Length != 0 && !accounted.Contains(sourceFile))
            {
                diagnostics.Add($"Character presentation layer '{name}' for race '{raceId}' names source file '{sourceFile}', which the publication does not account for.");
            }

            if (mediaId.Length != 0 && !mediaId.StartsWith("character.", StringComparison.Ordinal))
            {
                diagnostics.Add($"Character presentation layer '{name}' names media '{mediaId}', which is not a character media identity.");
            }

            if (!TryLayer(name, out DaggerfallCharacterLayerKind kind, out DaggerfallCharacterGender? gender, out int headIndex))
            {
                diagnostics.Add($"Character presentation layer '{name}' for race '{raceId}' is not a layer name this reader knows.");
                continue;
            }

            DaggerfallPaperDollPlacement? placement = Placement(layer, "placement", $"Character presentation layer '{name}' for race '{raceId}'", diagnostics);
            // The doll draws a body or head where the publication placed it; one without a placement has
            // nowhere honest to be drawn.
            if (kind != DaggerfallCharacterLayerKind.Background && placement is null)
                diagnostics.Add($"Character presentation layer '{name}' for race '{raceId}' publishes no paper-doll placement.");
            if (!raceByValue.ContainsKey(donorRaceId)) continue;
            if (!byRace.TryGetValue(raceId, out List<DaggerfallCharacterLayerDefinition>? layers))
            {
                layers = [];
                byRace.Add(raceId, layers);
            }

            layers.Add(new DaggerfallCharacterLayerDefinition(kind, gender, headIndex, mediaId, sourceFile, palette, consumer) { Placement = placement });
        }

        Dictionary<string, DaggerfallRaceLayers> races = new(StringComparer.Ordinal);
        foreach ((string raceId, List<DaggerfallCharacterLayerDefinition> layers) in byRace)
        {
            DaggerfallRaceLayers race = new(raceId, catalogs.RequireRace(raceId).DonorRaceId, [.. layers]);
            races.Add(raceId, race);
            foreach (DaggerfallCharacterGender gender in new[] { DaggerfallCharacterGender.Male, DaggerfallCharacterGender.Female })
            {
                if (race.Heads(gender).Count == 0)
                {
                    diagnostics.Add($"Race '{raceId}' publishes no heads for {gender.ToString().ToLowerInvariant()}.");
                }
            }
        }

        // A race whose value the corpus lacks a layer for, or has no media at all, is drawn for no one rather than with
        // another race's art.
        List<DaggerfallRaceWithoutMedia> without = [];
        foreach (JsonElement entry in Array(section, "racesWithoutMedia", diagnostics))
        {
            int donorRaceId = Integer(entry, "donorRaceId", diagnostics);
            string reason = Text(entry, "reason", diagnostics);
            if (raceByValue.TryGetValue(donorRaceId, out string? raceId)) without.Add(new DaggerfallRaceWithoutMedia(raceId, donorRaceId, reason));
        }
        foreach (DaggerfallRaceDefinition race in catalogs.Races.Where(race => !races.ContainsKey(race.Id)))
            without.Add(new DaggerfallRaceWithoutMedia(race.Id, race.DonorRaceId,
                $"The classic paper-doll media covers no race value {race.DonorRaceId}, so none is claimed for it."));

        // The faction faces are the section's non-racial layers: a social or escort view resolves
        // them by the donor's faction index, so the reader keys them by index rather than by race.
        List<DaggerfallFactionFaceDefinition> faces = [];
        foreach (JsonElement face in Array(section, "faces", diagnostics))
        {
            string mediaId = Text(face, "mediaId", diagnostics);
            string sourceFile = Text(face, "sourceFile", diagnostics);
            string palette = Text(face, "palette", diagnostics);
            int index = Integer(face, "index", diagnostics);
            _ = Text(face, "binding", diagnostics);
            string consumer = Text(face, "consumer", diagnostics);
            if (sourceFile.Length != 0 && !accounted.Contains(sourceFile))
            {
                diagnostics.Add($"Character presentation faction face {index} names source file '{sourceFile}', which the publication does not account for.");
            }

            if (mediaId.Length != 0 && !mediaId.StartsWith("character.faction-face.", StringComparison.Ordinal))
            {
                diagnostics.Add($"Character presentation faction face {index} names media '{mediaId}', which is not a faction face identity.");
            }

            faces.Add(new DaggerfallFactionFaceDefinition(index, mediaId, sourceFile, palette, consumer));
        }

        // A vampire's form is published by race value (vampire.VALUE.GENDER) and taken by the race of that value.
        Dictionary<string, DaggerfallRacialFormMedia> racialForms = [];
        foreach (JsonElement form in Array(section, "racialForms", diagnostics))
        {
            string id = Text(form, "id", diagnostics);
            if (id.Split('.') is ["vampire", string value, string gender])
            {
                if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int donorRaceId) || !raceByValue.TryGetValue(donorRaceId, out string? raceId))
                    continue;
                id = $"vampire.{raceId}.{gender}";
            }
            if (!racialForms.TryAdd(id, new(id, Text(form, "headMediaId", diagnostics), Text(form, "bodyMediaId", diagnostics))
                { HeadPlacement = Placement(form, "headPlacement", $"Racial form '{id}' head", diagnostics) }))
                diagnostics.Add($"Duplicate racial form media '{id}'.");
        }
        List<DaggerfallFactionFaceDefinition> childFaces = [];
        foreach (JsonElement face in Array(section, "childFaces", diagnostics))
        {
            int index = Integer(face, "index", diagnostics);
            string media = Text(face, "mediaId", diagnostics);
            string source = Text(face, "sourceFile", diagnostics);
            if (index is < 0 or > 3 || media != $"character.child-face.{index}" || source != "KIDS00I0.CIF" || !accounted.Contains(source))
                diagnostics.Add($"Character child face {index} has invalid media/source meaning.");
            childFaces.Add(new(index, media, source, Text(face, "palette", diagnostics), Text(face, "consumer", diagnostics)));
        }
        if (childFaces.Select(face => face.Index).Distinct().Count() != childFaces.Count)
            diagnostics.Add("Character child portraits repeat a source index.");

        // A class portrait is published under the class it depicts and given to every career of that name; a space in
        // a career's name is not part of the portrait's. A career none depicts has none rather than another class's.
        Dictionary<string, DaggerfallCareerPortraitDefinition> careers = new(StringComparer.Ordinal);
        foreach (JsonElement portrait in Array(section, "careers", diagnostics))
        {
            string name = Text(portrait, "portrait", diagnostics);
            string mediaId = Text(portrait, "mediaId", diagnostics);
            string sourceFile = Text(portrait, "sourceFile", diagnostics);
            string palette = Text(portrait, "palette", diagnostics);
            int frames = Integer(portrait, "frameCount", diagnostics);
            _ = Text(portrait, "binding", diagnostics);
            string consumer = Text(portrait, "consumer", diagnostics);
            if (sourceFile.Length != 0 && !accounted.Contains(sourceFile))
            {
                diagnostics.Add($"Character presentation portrait '{name}' names source file '{sourceFile}', which the publication does not account for.");
            }

            if (frames <= 0)
            {
                diagnostics.Add($"Character presentation portrait '{name}' declares {frames} frames.");
            }

            foreach (DaggerfallCareerDefinition career in catalogs.Careers.Where(career => string.Equals(career.Name.Replace(" ", string.Empty, StringComparison.Ordinal), name, StringComparison.OrdinalIgnoreCase)))
                careers[career.Id] = new DaggerfallCareerPortraitDefinition(career.Id, mediaId, sourceFile, palette, frames, consumer);
        }

        List<DaggerfallCareerWithoutPortrait> careersWithout = [.. catalogs.Careers.Where(career => !careers.ContainsKey(career.Id))
            .OrderBy(career => career.Id, StringComparer.Ordinal)
            .Select(career => new DaggerfallCareerWithoutPortrait(career.Id,
                $"The corpus supplies no class portrait named for '{career.Name}'; the three it supplies depict the classes they are named for, and none of them is this one."))];

        DaggerfallPaperDollFrame frame = new(110, 184, -8, -7, 125, 198);
        if (section.TryGetProperty("paperDoll", out JsonElement dollFrame) && dollFrame.ValueKind == JsonValueKind.Object)
            frame = new(Integer(dollFrame, "width", diagnostics), Integer(dollFrame, "height", diagnostics),
                Integer(dollFrame, "backgroundX", diagnostics), Integer(dollFrame, "backgroundY", diagnostics),
                Integer(dollFrame, "backgroundWidth", diagnostics), Integer(dollFrame, "backgroundHeight", diagnostics));
        else
            diagnostics.Add("Character presentation publishes no paperDoll frame.");

        return new DaggerfallCharacterPresentationSet(races, [.. faces.OrderBy(face => face.Index)], careers, careersWithout, without, files)
        { ChildFaces = childFaces, RacialForms = racialForms, PaperDoll = frame, PaperDollItems = ReadPaperDollItems(root, diagnostics), ItemIcons = ReadItemIcons(root, templates, diagnostics) };
    }

    /// <summary>
    /// Reads the worn-item images the doll draws, keyed by texture archive and record. A record that names
    /// no image for a dye, or an image outside the doll's media, is reported where it loads.
    /// </summary>
    private static IReadOnlyDictionary<(int Archive, int Record), DaggerfallPaperDollItemImage> ReadPaperDollItems(JsonElement root, DaggerfallContentDiagnostics diagnostics)
    {
        Dictionary<(int Archive, int Record), DaggerfallPaperDollItemImage> items = [];
        if (!root.TryGetProperty("paperDoll", out JsonElement section) || section.ValueKind != JsonValueKind.Object)
        {
            diagnostics.Add("Base payload must publish a paperDoll section.");
            return items;
        }

        foreach (JsonElement item in Array(section, "items", diagnostics))
        {
            int archive = Integer(item, "archive", diagnostics), record = Integer(item, "record", diagnostics);
            string subject = $"Paper-doll image {archive}/{record}";
            Dictionary<string, string> dyes = new(StringComparer.Ordinal);
            if (item.TryGetProperty("dyes", out JsonElement dyeMap) && dyeMap.ValueKind == JsonValueKind.Object)
                foreach (JsonProperty dye in dyeMap.EnumerateObject())
                    if (dye.Value.GetString() is { Length: > 0 } id && id.StartsWith("paper-doll.", StringComparison.Ordinal)) dyes[dye.Name] = id;
                    else diagnostics.Add($"{subject} names no paper-doll image for dye '{dye.Name}'.");
            if (dyes.Count == 0) diagnostics.Add($"{subject} names no image.");
            string? mask = OptionalText(item, "maskMediaId", diagnostics);
            DaggerfallPaperDollPlacement placement = new(Integer(item, "x", diagnostics), Integer(item, "y", diagnostics),
                Integer(item, "width", diagnostics), Integer(item, "height", diagnostics));
            if (!items.TryAdd((archive, record), new(archive, record, dyes, placement, mask)))
                diagnostics.Add($"{subject} is published twice.");
        }

        return items;
    }

    /// <summary>The worn-item groups the doll draws, whose inventory image is the doll's.</summary>
    private static readonly string[] WornGroups = ["Armor", "Weapons", "MensClothing", "WomensClothing"];

    /// <summary>The item groups whose inventory image is the world texture (<c>DaggerfallUnityItem.UseWorldTexture</c>).</summary>
    private static readonly string[] WorldTextureGroups =
    [
        "UselessItems1", "ReligiousItems", "MiscItems", "PlantIngredients1", "PlantIngredients2", "CreatureIngredients1",
        "CreatureIngredients2", "CreatureIngredients3", "MiscellaneousIngredients1", "MiscellaneousIngredients2", "MetalIngredients",
    ];

    /// <summary>
    /// The inventory images of the templates the doll does not draw, by template index and variant
    /// (<c>DaggerfallUnityItem.GetInventoryTextureArchive</c>): the world texture where <c>UseWorldTexture</c> holds or
    /// the template has no player texture, else the player texture record plus the variant. Only the images the import
    /// published count: a record past the end of its archive (the classic Flowers name one) has none, as in the classic.
    /// </summary>
    private static IReadOnlyDictionary<int, IReadOnlyList<string>> ReadItemIcons(JsonElement root, DaggerfallItemTemplateSet templates, DaggerfallContentDiagnostics diagnostics)
    {
        Dictionary<int, IReadOnlyList<string>> icons = [];
        if (!root.TryGetProperty("paperDoll", out JsonElement section) || section.ValueKind != JsonValueKind.Object) return icons;
        HashSet<string> published = [.. Array(section, "icons", diagnostics).Select(id => id.GetString()).OfType<string>()];
        foreach (DaggerfallItemTemplateDefinition template in templates.Templates.Values)
        {
            if (template.PlayerTextureArchive != 0 && template.Groups.Any(group => WornGroups.Contains(group, StringComparer.Ordinal))) continue;
            IEnumerable<string> ids;
            if (template.PlayerTextureArchive == 0 || template.Groups.Any(group => WorldTextureGroups.Contains(group, StringComparer.Ordinal)))
            {
                if (template.WorldTextureArchive == 0 && template.WorldTextureRecord == 0) continue;
                ids = [$"item.{template.WorldTextureArchive}.{template.WorldTextureRecord}"];
            }
            else
            {
                ids = Enumerable.Range(template.PlayerTextureRecord, Math.Max(1, template.Variants)).Select(record => $"item.{template.PlayerTextureArchive}.{record}");
            }

            string[] drawn = [.. ids.Where(published.Contains)];
            if (drawn.Length != 0) icons.Add(template.Index, drawn);
        }

        return icons;
    }

    /// <summary>An optional paper-doll placement; a present one must carry its position and a positive size.</summary>
    private static DaggerfallPaperDollPlacement? Placement(JsonElement owner, string property, string subject, DaggerfallContentDiagnostics diagnostics)
    {
        if (!owner.TryGetProperty(property, out JsonElement value) || value.ValueKind == JsonValueKind.Null) return null;
        DaggerfallPaperDollPlacement placement = new(Integer(value, "x", diagnostics), Integer(value, "y", diagnostics),
            Integer(value, "width", diagnostics), Integer(value, "height", diagnostics));
        if (placement.Width <= 0 || placement.Height <= 0) diagnostics.Add($"{subject} has an empty paper-doll placement.");
        return placement;
    }

    /// <summary>
    /// Reads the role a published layer name encodes: a background, a gendered body, or a numbered head.
    /// </summary>
    private static bool TryLayer(string name, out DaggerfallCharacterLayerKind kind, out DaggerfallCharacterGender? gender, out int headIndex)
    {
        kind = DaggerfallCharacterLayerKind.Background;
        gender = null;
        headIndex = -1;
        if (name == "background") return true;
        string[] parts = name.Split('.');
        if (parts.Length == 3 && parts[0] == "body" && (parts[2] is "unclothed" or "clothed") && TryGender(parts[1], out DaggerfallCharacterGender bodyGender))
        {
            kind = parts[2] == "clothed" ? DaggerfallCharacterLayerKind.BodyClothed : DaggerfallCharacterLayerKind.BodyUnclothed;
            gender = bodyGender;
            return true;
        }

        if (parts.Length == 3 && parts[0] == "head" && TryGender(parts[1], out DaggerfallCharacterGender headGender) && int.TryParse(parts[2], out int index) && index >= 0)
        {
            kind = DaggerfallCharacterLayerKind.Head;
            gender = headGender;
            headIndex = index;
            return true;
        }

        return false;
    }

    private static bool TryGender(string value, out DaggerfallCharacterGender gender)
    {
        gender = DaggerfallCharacterGender.Male;
        if (value == "male") return true;
        if (value == "female")
        {
            gender = DaggerfallCharacterGender.Female;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Reads the published locations, so a section the import wrote is validated where it is loaded
    /// rather than trusted.
    /// </summary>
    /// <remarks>
    /// The site and world consumers this section is published for are later tasks, and the task that
    /// published it deliberately created no runtime archive reader. What this adds is the check that
    /// was missing: a malformed section used to load silently, because an unknown property is
    /// tolerated by design. The rules here are the ones that make a location resolvable - a region
    /// and name, a non-negative map, a dungeon that names a location the section carries and lists
    /// blocks, and a gap that names what it could not read.
    /// </remarks>
    /// <summary>
    /// Reads the location catalog: every location's identity, map pixel, kind and exterior footprint and blocks.
    /// Each exterior's buildings, building references and ground are its published detail, read from the data
    /// container below the section's <c>details</c> root when the location is first used; with no data container
    /// the catalog is still read, and asking a location for its detail says the container was not given.
    /// </summary>
    private static DaggerfallLocationSet ReadLocations(JsonElement root, DaggerfallData? data, DaggerfallContentDiagnostics diagnostics)
    {
        if (!root.TryGetProperty("locations", out JsonElement section) || section.ValueKind != JsonValueKind.Object)
        {
            diagnostics.Add("Base payload must publish a locations section.");
            return new DaggerfallLocationSet([], [], 0, 0, 0);
        }

        string detailsRoot = Text(section, "details", diagnostics);
        DaggerfallSiteDetails? details = data is null || detailsRoot.Length == 0 ? null : new DaggerfallSiteDetails(data, detailsRoot);

        List<DaggerfallSiteRecord> records = [];
        HashSet<(int Region, int Index)> keys = [];
        foreach (JsonElement location in Array(section, "locations", diagnostics))
        {
            int region = Integer(location, "region", diagnostics);
            int index = Integer(location, "index", diagnostics);
            string name = Text(location, "name", diagnostics);
            int mapId = Integer(location, "mapId", diagnostics);
            int longitude = Integer(location, "longitude", diagnostics);
            int latitude = Integer(location, "latitude", diagnostics);
            int dungeonType = Integer(location, "dungeonType", diagnostics);
            bool discovered = Boolean(location, "discovered", diagnostics);
            // The CLIMATE.PAK value at the location's own map pixel, which selects its texture swaps; a
            // world assembly refuses a location without one rather than guessing its climate.
            int? climate = OptionalInteger(location, "climate", diagnostics);

            // Read against the record rather than through the shared helper: across fifteen thousand
            // locations, a diagnostic that says only "'locationType' must be an integer" leaves the
            // reader nowhere to look, so this one names the location and what it actually carried.
            int locationType = 0;
            if (!location.TryGetProperty("locationType", out JsonElement kindValue) || kindValue.ValueKind != JsonValueKind.Number || !kindValue.TryGetInt32(out locationType))
            {
                diagnostics.Add($"Published location {index} of region {region} carries no integer locationType; it carries {DaggerfallSiteKinds.Describe(kindValue)}.");
            }
            if (!keys.Add((region, index)))
            {
                diagnostics.Add($"Published locations carry region {region} index {index} twice, so one of them is unreachable.");
            }

            if (name.Length == 0 || mapId < 0)
            {
                diagnostics.Add($"Published location {index} of region {region} names '{name}' on map {mapId}, which cannot be resolved.");
            }

            // A record whose kind this ruleset cannot name is reported against the record itself: the
            // section is still readable, but a site consumer would answer from a kind nobody published.
            if (DaggerfallSiteKinds.TryResolve(locationType, out DaggerfallSiteKind kind))
            {
                DaggerfallSiteExterior? exterior = ReadSiteExterior(location, region, index, longitude, latitude, details, diagnostics);
                records.Add(new DaggerfallSiteRecord(new DaggerfallSiteId(region, index), name, mapId,
                    longitude, latitude, dungeonType, kind, discovered, exterior) { Climate = climate });
            }
            else
            {
                diagnostics.Add(DaggerfallSiteKinds.UnnameableMessage(locationType, region, index));
            }
        }

        int dungeons = 0;
        foreach (JsonElement dungeon in Array(section, "dungeons", diagnostics))
        {
            int region = Integer(dungeon, "region", diagnostics);
            int index = Integer(dungeon, "index", diagnostics);
            string name = Text(dungeon, "name", diagnostics);
            _ = Integer(dungeon, "exteriorLocationId", diagnostics);
            _ = Integer(dungeon, "dungeonLocationId", diagnostics);
            int blocks = 0;
            foreach (JsonElement block in Array(dungeon, "blocks", diagnostics))
            {
                if (block.ValueKind != JsonValueKind.String || block.GetString() is not { Length: > 0 })
                {
                    diagnostics.Add($"Published dungeon '{name}' names a block with no name.");
                }

                blocks++;
            }

            if (!keys.Contains((region, index)))
            {
                diagnostics.Add($"Published dungeon '{name}' names region {region} location {index}, which no location record carries.");
            }

            if (blocks == 0)
            {
                diagnostics.Add($"Published dungeon '{name}' lists no blocks, so it describes no structure.");
            }

            if (dungeon.TryGetProperty("blockPlacements", out JsonElement placements))
            {
                List<DaggerfallSiteDungeonBlock> sourceBlocks = [];
                foreach (JsonElement placement in placements.EnumerateArray())
                {
                    string sourceKey = Text(placement, "sourceKey", diagnostics);
                    int x = Integer(placement, "x", diagnostics), z = Integer(placement, "z", diagnostics);
                    bool start = placement.TryGetProperty("start", out JsonElement startValue) && startValue.ValueKind != JsonValueKind.Null
                        && Boolean(placement, "start", diagnostics);
                    if (sourceKey.Length == 0 || x is < sbyte.MinValue or > sbyte.MaxValue || z is < sbyte.MinValue or > sbyte.MaxValue)
                        diagnostics.Add($"Published dungeon '{name}' has an invalid source block placement.");
                    sourceBlocks.Add(new(sourceKey, x, z) { Start = start });
                }
                if (sourceBlocks.Count != blocks)
                    diagnostics.Add($"Published dungeon '{name}' has {sourceBlocks.Count} placements for {blocks} source blocks.");
                if (sourceBlocks.Count(block => block.Start) > 1)
                    diagnostics.Add($"Published dungeon '{name}' names more than one start block.");
                Dictionary<int, int> remaps = [];
                if (dungeon.TryGetProperty("textureTable", out JsonElement table) && table.ValueKind == JsonValueKind.Object
                    && table.TryGetProperty("remaps", out _))
                {
                    foreach (JsonElement remap in Array(table, "remaps", diagnostics))
                    {
                        int archive = Integer(remap, "archive", diagnostics), target = Integer(remap, "targetArchive", diagnostics);
                        if (archive < 0 || target < 0 || !remaps.TryAdd(archive, target))
                            diagnostics.Add($"Published dungeon '{name}' has an invalid or repeated texture remap of archive {archive}.");
                    }
                }
                int recordIndex = records.FindIndex(record => record.Id == new DaggerfallSiteId(region, index));
                if (recordIndex >= 0) records[recordIndex] = records[recordIndex] with { DungeonBlocks = sourceBlocks, DungeonTextureRemaps = remaps };
            }

            dungeons++;
        }

        int gaps = 0;
        foreach (JsonElement gap in Array(section, "regionsWithoutTables", diagnostics))
        {
            _ = Integer(gap, "region", diagnostics);
            int empty = 0;
            foreach (JsonElement table in Array(gap, "emptyTables", diagnostics))
            {
                if (table.ValueKind != JsonValueKind.String || table.GetString() is not { Length: > 0 })
                {
                    diagnostics.Add("A published region gap names an empty table with no name.");
                }

                empty++;
            }

            if (empty == 0)
            {
                diagnostics.Add("A published region gap names no empty tables, so nothing says why the region has none.");
            }

            gaps++;
        }

        // Each region's provenance is the four tables the donor reads: a region that records fewer, or
        // records one with no name, would leave a published fact with nowhere to trace it to.
        int regions = 0;
        foreach (JsonElement region in Array(section, "regions", diagnostics))
        {
            _ = Integer(region, "region", diagnostics);
            int tables = 0;
            foreach (JsonElement table in Array(region, "tables", diagnostics))
            {
                string name = Text(table, "name", diagnostics);
                _ = Integer(table, "ordinal", diagnostics);
                _ = Integer(table, "length", diagnostics);
                _ = Integer(table, "declaredRecords", diagnostics);
                if (Text(table, "state", diagnostics).Length == 0)
                {
                    diagnostics.Add($"A published region table '{name}' carries no state, so nothing says whether it read.");
                }

                tables++;
            }

            if (tables != 4)
            {
                diagnostics.Add($"A published region records {tables} tables; a region group carries four.");
            }

            regions++;
        }

        foreach (JsonElement gap in Array(section, "dungeonsWithoutRecords", diagnostics))
        {
            int region = Integer(gap, "region", diagnostics);
            int index = Integer(gap, "index", diagnostics);
            string name = Text(gap, "name", diagnostics);
            if (Text(gap, "reason", diagnostics).Length == 0)
            {
                diagnostics.Add($"Published dungeon gap '{name}' states no reason.");
            }

            if (!keys.Contains((region, index)))
            {
                diagnostics.Add($"Published dungeon gap '{name}' names region {region} location {index}, which no location record carries.");
            }
        }

        // Each exterior's detail is read when it is used, so its presence is what is checked here: a container
        // that lacks one is named now, not when a quest or a walk first reaches that location.
        if (details is not null)
        {
            string[] missing = [.. details.Missing(records.Where(record => record.Exterior is not null).Select(record => record.Id))];
            if (missing.Length != 0)
                diagnostics.Add($"{data!.Source} lacks {missing.Length} location detail files, for example '{string.Join("', '", missing.Take(3))}'; import the Daggerfall data again.");
        }

        return new DaggerfallLocationSet([.. keys], [.. records], dungeons, gaps, regions);
    }

    private static DaggerfallSiteExterior? ReadSiteExterior(JsonElement location, int region, int index,
        int longitude, int latitude, DaggerfallSiteDetails? details, DaggerfallContentDiagnostics diagnostics)
    {
        if (!location.TryGetProperty("exterior", out JsonElement value)) return null;
        JsonElement exterior = Object(value, $"location {region}/{index} exterior", diagnostics);
        int mapPixelX = Integer(exterior, "mapPixelX", diagnostics);
        int mapPixelY = Integer(exterior, "mapPixelY", diagnostics);
        int? sourceLocationId = exterior.TryGetProperty("locationId", out _) ? Integer(exterior, "locationId", diagnostics) : null;
        if (sourceLocationId is < 0 or > ushort.MaxValue) diagnostics.Add($"Location {region}/{index} has invalid source location identity.");
        int port = Integer(exterior, "portTownAndUnknown", diagnostics);
        if (port is < 0 or > 255) diagnostics.Add($"Location {region}/{index} has invalid source port byte {port}.");
        int width = Integer(exterior, "width", diagnostics);
        int height = Integer(exterior, "height", diagnostics);
        int tileOriginX = Integer(exterior, "tileOriginX", diagnostics);
        int tileOriginY = Integer(exterior, "tileOriginY", diagnostics);
        bool custom = Boolean(exterior, "usesCustomLocationPosition", diagnostics);
        int clearance = Integer(exterior, "blendClearance", diagnostics);
        JsonElement rect = Object(Property(exterior, "flattenRect", diagnostics),
            $"location {region}/{index} terrain rectangle", diagnostics);
        int minX = Integer(rect, "minX", diagnostics);
        int maxX = Integer(rect, "maxX", diagnostics);
        int minY = Integer(rect, "minY", diagnostics);
        int maxY = Integer(rect, "maxY", diagnostics);
        if (mapPixelX != longitude / 128 || mapPixelY != 499 - latitude / 128)
            diagnostics.Add($"Published location {region}/{index} terrain map pixel disagrees with its world coordinates.");
        if (width <= 0 || height <= 0 || clearance is not 2 and not 3 || maxX < minX || maxY < minY)
            diagnostics.Add($"Published location {region}/{index} has invalid normalized exterior terrain bounds.");
        return new DaggerfallSiteExterior(mapPixelX, mapPixelY, width, height, tileOriginX, tileOriginY,
            custom, clearance, minX, maxX, minY, maxY)
        {
            SourceLocationId = sourceLocationId,
            PortTownAndUnknown = port,
            Blocks = ReadSiteBlocks(exterior, width, height, diagnostics),
            BuildingTypes = ReadBuildingTypes(exterior, region, index, diagnostics),
            Detail = new DaggerfallSiteDetailSource(details, new DaggerfallSiteId(region, index), width, height),
        };
    }

    private static IReadOnlyList<int> ReadBuildingTypes(JsonElement exterior, int region, int index, DaggerfallContentDiagnostics diagnostics)
    {
        List<int> types = [];
        foreach (JsonElement value in Array(exterior, "buildingTypes", diagnostics))
        {
            if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int type) || type is < 0 or > byte.MaxValue)
            {
                diagnostics.Add($"Location {region}/{index} names a building type that is not a source byte.");
                continue;
            }

            types.Add(type);
        }

        return types.AsReadOnly();
    }

    /// <summary>A location's MAPPITEM building references, from its detail record.</summary>
    internal static IReadOnlyList<DaggerfallSiteBuildingReference> ReadBuildingReferences(JsonElement exterior, DaggerfallContentDiagnostics diagnostics)
    {
        // Authored sites need not publish MAPS fixed-building references.
        if (!exterior.TryGetProperty("buildingReferences", out _)) return [];
        List<DaggerfallSiteBuildingReference> result = [];
        foreach (JsonElement value in Array(exterior, "buildingReferences", diagnostics))
        {
            int locationId = Integer(value, "locationId", diagnostics);
            int sector = Integer(value, "sector", diagnostics);
            int? buildingType = value.TryGetProperty("buildingType", out JsonElement type) ? type.GetInt32() : null;
            if (locationId is < 0 or > ushort.MaxValue || sector is < short.MinValue or > short.MaxValue || buildingType is < 0 or > byte.MaxValue)
                diagnostics.Add("Location building reference carries invalid source values.");
            result.Add(new(locationId, sector, buildingType));
        }
        return result.AsReadOnly();
    }

    private static IReadOnlyList<DaggerfallSiteBlock> ReadSiteBlocks(JsonElement exterior, int width, int height, DaggerfallContentDiagnostics diagnostics)
    {
        List<DaggerfallSiteBlock> blocks = [];
        HashSet<(int, int)> positions = [];
        foreach (JsonElement value in Array(exterior, "blocks", diagnostics))
        {
            DaggerfallSiteBlock block = new(Text(value, "sourceName", diagnostics), Integer(value, "x", diagnostics), Integer(value, "y", diagnostics));
            if (block.X < 0 || block.Y < 0 || block.X >= width || block.Y >= height || !positions.Add((block.X, block.Y)))
                diagnostics.Add($"Location exterior repeats or misplaces block '{block.SourceName}'.");
            blocks.Add(block);
        }
        if (blocks.Count != width * height) diagnostics.Add("Location exterior must publish its complete block grid.");
        return blocks.AsReadOnly();
    }

    /// <summary>A location's placed buildings, from its detail record.</summary>
    internal static IReadOnlyDictionary<DaggerfallSiteBuildingId, DaggerfallSiteBuildingSource> ReadSiteBuildings(JsonElement exterior, int region, int index,
        int width, int height, DaggerfallContentDiagnostics diagnostics)
    {
        List<DaggerfallSiteBuildingSource> result = [];
        HashSet<DaggerfallSiteBuildingId> identities = [];
        foreach (JsonElement value in Array(exterior, "buildings", diagnostics))
        {
            DaggerfallSiteBuildingId id = new(Integer(value, "blockX", diagnostics), Integer(value, "blockY", diagnostics), Integer(value, "buildingIndex", diagnostics));
            DaggerfallRmbBuildingSource source = new(new(Text(value, "sourceKey", diagnostics), id.Index),
                Integer(value, "buildingType", diagnostics), Integer(value, "factionId", diagnostics), Integer(value, "nameSeed", diagnostics));
            int quality = Integer(value, "quality", diagnostics);
            if (id.BlockX < 0 || id.BlockX >= width || id.BlockY < 0 || id.BlockY >= height || id.Index is < 0 or > 255
                || source.BuildingType is < 0 or > 255 || source.FactionId is < 0 or > 65535 || source.NameSeed is < 0 or > 65535 || quality is < 0 or > 20)
                diagnostics.Add($"Location {region}/{index} building '{id}' carries invalid placement or policy fields.");
            if (!identities.Add(id)) diagnostics.Add($"Location {region}/{index} carries building '{id}' twice.");
            string? modelId = value.TryGetProperty("modelId", out JsonElement model) && model.ValueKind == JsonValueKind.String ? model.GetString() : null;
            float? radius = value.TryGetProperty("modelRadius", out JsonElement radiusValue) && radiusValue.ValueKind == JsonValueKind.Number
                && radiusValue.TryGetSingle(out float r) ? r : null;
            if (radius is float invalid && (!float.IsFinite(invalid) || invalid < 0))
                diagnostics.Add($"Location {region}/{index} building '{id}' carries invalid model radius.");
            int? sourceLocationId = value.TryGetProperty("sourceLocationId", out _) ? Integer(value, "sourceLocationId", diagnostics) : null;
            if (sourceLocationId is < 0 or > ushort.MaxValue)
                diagnostics.Add($"Location {region}/{index} building '{id}' carries invalid source location id.");
            result.Add(new(id, source, quality) { SourceLocationId = sourceLocationId, ModelId = modelId, ModelRadius = radius });
        }
        return new ReadOnlyDictionary<DaggerfallSiteBuildingId, DaggerfallSiteBuildingSource>(result.DistinctBy(building => building.Id).ToDictionary(building => building.Id));
    }

    /// <summary>
    /// Checks the race and career an actor names against the published catalogs.
    /// </summary>
    /// <remarks>
    /// An identity nobody publishes would resolve to no art at all, which is the failure the
    /// character presentation set exists to make impossible rather than to discover while a sheet is
    /// being drawn.
    /// </remarks>
    private static void ValidateActorIdentities(
        IReadOnlyDictionary<DaggerfallActorId, DaggerfallActorDefinition> actors,
        DaggerfallCatalogSet catalogs,
        DaggerfallMobileCatalogSet mobiles,
        DaggerfallContentDiagnostics diagnostics)
    {
        foreach (DaggerfallActorDefinition actor in actors.Values)
        {
            if (actor.Race is { } race && !catalogs.Races.Any(candidate => candidate.Id == race))
            {
                diagnostics.Add($"Actor '{actor.Id.Value}' names race '{race}', which the catalogs do not publish.");
            }

            if (actor.Career is { } career && !catalogs.Careers.Any(candidate => candidate.Id == career))
            {
                diagnostics.Add($"Actor '{actor.Id.Value}' names career '{career}', which the catalogs do not publish.");
            }

            // An actor's mobile id is a media reference: it names the donor record whose textures, sounds
            // and corpse the actor wears. A reference the published catalog does not carry would leave the
            // actor with no source record, and one that resolves to a different actor would silently give
            // it another mobile's media.
            if (actor.MobileId is { } mobileId)
            {
                if (!mobiles.Mobiles.TryGetValue(mobileId, out DaggerfallMobileDefinition? mobile))
                {
                    diagnostics.Add($"Actor '{actor.Id.Value}' names mobile {mobileId}, which the published mobile catalog does not carry.");
                }
                else if (mobile.Actor is { } owner && owner != actor.Id.Value)
                {
                    diagnostics.Add($"Actor '{actor.Id.Value}' names mobile {mobileId}, which the published catalog resolves to actor '{owner}'.");
                }
                else if (mobile.Actor is null && actor.Kind == DaggerfallActorKinds.EnemyClass
                    && actor.Id != DaggerfallEncounterActors.ActorFor(mobile))
                {
                    diagnostics.Add($"Enemy class actor '{actor.Id.Value}' does not match the derived actor identity for mobile {mobileId}.");
                }
            }
        }
    }

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
            DaggerfallActorDefinition definition = new(id, Text(actor, "kind", diagnostics), stats, health, new(DaggerfallMechanicsIds.Health, id.Value == "player" ? DaggerfallMechanicsIds.Stamina : null), rewards, armor, mobileId, OptionalInteger(actor, "hitPointsPerLevel", diagnostics), attacks, OptionalText(actor, "team", diagnostics), OptionalText(actor, "minMetalToHit", diagnostics), OptionalText(actor, "lootTableKey", diagnostics), OptionalInteger(actor, "level", diagnostics), OptionalInteger(actor, "weight", diagnostics), actionId, ReadLoadout(actor, items, diagnostics), presentation, OptionalText(actor, "race", diagnostics), OptionalText(actor, "career", diagnostics));
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
    /// Reads the published text section, with the base pack's internal text beside it. Every value resolves by the key its own
    /// source gave it, carries the language of the source it was read from, and states whether it was
    /// readable — so a lookup answers with text, with a reason there is none, or with a miss, and never
    /// with an empty string standing in for either.
    /// </summary>
    private static DaggerfallTextSet ReadText(JsonElement root, DaggerfallContentDiagnostics diagnostics)
    {
        if (!root.TryGetProperty("text", out JsonElement section) || section.ValueKind != JsonValueKind.Object)
        {
            // The section is the whole text contract: without it every key resolves to nothing, so the
            // payload is refused rather than read as a product whose text is uniformly missing. The
            // diagnostic is what the caller sees, since reading aborts on it.
            diagnostics.Add("Base payload publishes no text section; every text lookup resolves to nothing.");
            return new DaggerfallTextSet(new Dictionary<DaggerfallTextKey, DaggerfallTextValue>(), []);
        }

        // Daggerfall Unity's internal strings are a text family of their own in the internalText section; the two
        // sections read as one set, each with its own sources.
        JsonElement[] parts = root.TryGetProperty("internalText", out JsonElement internalText) && internalText.ValueKind == JsonValueKind.Object
            ? [section, internalText]
            : [section];

        // A source is the language and family its values inherit, so two sources claiming one path would
        // leave a value naming it with no one language and no one family.
        Dictionary<string, (DaggerfallTextKind Kind, string Language)> sources = new(StringComparer.Ordinal);
        foreach (JsonElement source in parts.SelectMany(part => Array(part, "sources", diagnostics)))
        {
            DaggerfallTextKind kind = TextKind(source, diagnostics);
            string path = Text(source, "path", diagnostics);
            string language = Text(source, "language", diagnostics);
            if (!sources.TryAdd(path, (kind, language))) diagnostics.Add($"Published text source '{path}' is claimed twice, so a value naming it has no one language and family.");
        }
        if (sources.Count == 0) diagnostics.Add("Published text must name the sources its values were read from.");

        Dictionary<DaggerfallTextKey, DaggerfallTextValue> values = [];
        HashSet<string> grouped = new(StringComparer.Ordinal);
        string previousSource = string.Empty;
        int previousIndex = -1;
        foreach (JsonElement record in parts.SelectMany(part => Array(part, "records", diagnostics)))
        {
            JsonElement keyValue = Object(Property(record, "key", diagnostics), "key", diagnostics);
            DaggerfallTextKind kind = TextKind(keyValue, diagnostics);
            string id = Text(keyValue, "id", diagnostics);
            DaggerfallTextKey key = new(kind, id);
            string source = Text(record, "source", diagnostics);
            int index = Integer(record, "index", diagnostics);
            int subrecords = Integer(record, "subrecords", diagnostics);
            string stateName = Text(record, "state", diagnostics);
            string reason = EmptyAllowedText(record, "reason", diagnostics);

            // A value whose source the section does not declare has no language and no family: the key
            // would resolve and nothing would say what it was read from.
            if (!sources.TryGetValue(source, out (DaggerfallTextKind Kind, string Language) owner))
            {
                diagnostics.Add($"Published text key '{key}' names source '{source}', which the section does not carry.");
                continue;
            }

            if (owner.Kind != kind)
            {
                diagnostics.Add($"Published text key '{key}' belongs to family '{owner.Kind}' by its source and to '{kind}' by its key.");
            }

            if (!values.TryAdd(key, null!))
            {
                diagnostics.Add($"Published text carries '{key}' twice, so one of them is unreachable.");
                continue;
            }

            if (!TryReadName(stateName, out DaggerfallTextState state))
            {
                diagnostics.Add($"Published text key '{key}' states the state '{stateName}', which the contract does not declare.");
            }

            // A value is either read or it is not, and the two say different things: a read value carries its
            // text, and a malformed one's reason is the fact. One claiming both would leave a consumer unable to
            // tell an empty value from a value that was never readable.
            if (state == DaggerfallTextState.Read && reason.Length != 0)
            {
                diagnostics.Add($"Published text key '{key}' is readable and states a reason it is not readable.");
            }

            if (state == DaggerfallTextState.Malformed && (string.IsNullOrWhiteSpace(reason) || subrecords != 0))
            {
                diagnostics.Add($"Published text key '{key}' is malformed in {subrecords} variants, or states no reason.");
            }

            List<string> symbols = [];
            foreach (JsonElement macro in Array(record, "macros", diagnostics))
            {
                if (macro.ValueKind != JsonValueKind.String || macro.GetString() is not { Length: > 0 } symbol)
                {
                    diagnostics.Add($"Published text key '{key}' names a macro with no symbol.");
                    continue;
                }

                if (symbol[0] != '%') diagnostics.Add($"Published text key '{key}' names macro '{symbol}', which does not begin with the macro marker.");
                if (!symbols.Contains(symbol, StringComparer.Ordinal)) symbols.Add(symbol);
            }

            List<DaggerfallTextElement> tokens = [];
            foreach (JsonElement token in Array(record, "tokens", diagnostics))
            {
                DaggerfallTextElement? element = TextElement(token, key, diagnostics);
                if (element is not null) tokens.Add(element);
            }

            if (state == DaggerfallTextState.Malformed && tokens.Count != 0)
            {
                diagnostics.Add($"Published text key '{key}' is malformed and still carries {tokens.Count} tokens.");
            }

            // The variant count is not a separate claim: the separators the value publishes are what
            // divide it, so a count that disagrees with them describes a value no consumer can divide.
            int dividers = 1 + tokens.Count(token => token.Code == DaggerfallTextCode.SubrecordSeparator);
            if (state == DaggerfallTextState.Read && subrecords != dividers)
            {
                diagnostics.Add($"Published text key '{key}' states {subrecords} variants where its separators divide it into {dividers}.");
            }

            // Each source's values are published as a group in its own order, so a caller reads one
            // source's ordinals without interleaving another's.
            if (!StringComparer.Ordinal.Equals(source, previousSource))
            {
                if (!grouped.Add(source)) diagnostics.Add($"Published text source '{source}' is interleaved with another source rather than grouped.");
                previousSource = source;
                previousIndex = -1;
            }

            if (index <= previousIndex) diagnostics.Add($"Published text source '{source}' carries '{key}' at ordinal {index} after ordinal {previousIndex}, so its values are not in source order.");
            previousIndex = index;
            values[key] = new DaggerfallTextValue(key, source, owner.Language, index, subrecords, state, reason, symbols, tokens);
        }

        // The macro index is derived from the values: each symbol any value carries, with how many carry it.
        List<DaggerfallTextMacro> macros = [.. values.Values.SelectMany(value => value.Macros).GroupBy(symbol => symbol, StringComparer.Ordinal)
            .Select(group => new DaggerfallTextMacro(group.Key, group.Count())).OrderBy(macro => macro.Symbol, StringComparer.Ordinal)];
        return new DaggerfallTextSet(values, macros);
    }

    /// <summary>
    /// Reads the exact classic region-to-name-bank mapping. It cannot be inferred from a faction's
    /// race: the donor's MapsFile table is a separate FALL.EXE-derived input used by name generation.
    /// </summary>
    private static DaggerfallBuildingNameInputs ReadBuildingNameInputs(JsonElement root, int regionCount, DaggerfallContentDiagnostics diagnostics)
    {
        if (!root.TryGetProperty("buildingNames", out JsonElement section) || section.ValueKind != JsonValueKind.Object)
        {
            diagnostics.Add("Base payload publishes no buildingNames section; classic building names cannot select a region name bank.");
            return new DaggerfallBuildingNameInputs([]);
        }

        // The bank is chosen by region index, so the table covers exactly the regions the locations publish.
        List<int> banks = [.. Array(section, "regionNameBanks", diagnostics).Select(entry =>
        {
            if (entry.ValueKind == JsonValueKind.Number && entry.TryGetInt32(out int value) && value is 0 or 1) return value;
            diagnostics.Add("Building-name region bank must be Breton (0) or Redguard (1).");
            return -1;
        })];
        if (banks.Count != regionCount) diagnostics.Add($"Building-name inputs publish {banks.Count} regions for the {regionCount} published regions.");
        string[] names = section.TryGetProperty("regionNames", out JsonElement regionNames)
            ? [.. regionNames.EnumerateArray().Select(name => name.GetString() ?? string.Empty)] : [];
        if (names.Length > 0 && (names.Length != regionCount || names.Any(string.IsNullOrWhiteSpace)))
            diagnostics.Add("Building-name region names must name each published region.");
        return new DaggerfallBuildingNameInputs(banks, names);
    }

    /// <summary>
    /// Reads one published token. A run states its characters, an unnamed code states the byte it came
    /// from, and only the two prefixes state a payload: a token whose absent and empty members disagree
    /// with its kind would be applied as a different code than the one it names.
    /// </summary>
    private static DaggerfallTextElement? TextElement(JsonElement token, DaggerfallTextKey key, DaggerfallContentDiagnostics diagnostics)
    {
        string codeName = Text(token, "code", diagnostics);
        if (!TryReadName(codeName, out DaggerfallTextCode code))
        {
            diagnostics.Add($"Published text key '{key}' carries the element kind '{codeName}', which the contract does not declare.");
            return null;
        }

        string? text = OptionalText(token, "text", diagnostics);
        int? value = OptionalInteger(token, "value", diagnostics);
        int? x = OptionalInteger(token, "x", diagnostics);
        bool prefixed = code is DaggerfallTextCode.FontPrefix or DaggerfallTextCode.PositionPrefix;
        if (code == DaggerfallTextCode.Text)
        {
            if (text is null || value is not null || x is not null)
            {
                diagnostics.Add($"Published text key '{key}' carries a text element that holds no text or states a value a run does not have.");
            }
        }
        else if (text is not null || (x is not null) != prefixed || (x is { } payload && payload is < 0 or > 0xff))
        {
            diagnostics.Add($"Published text key '{key}' carries code {code} with text, or with a payload it does not take.");
        }
        else if (code == DaggerfallTextCode.Unknown)
        {
            if (value is not { } byteValue || byteValue is < 0 or > 0xff || Enum.IsDefined((DaggerfallTextCode)byteValue))
            {
                diagnostics.Add($"Published text key '{key}' carries an unnamed code with no byte, or with byte {value} that a name covers.");
            }
        }
        else if (value is not null)
        {
            diagnostics.Add($"Published text key '{key}' states byte {value} for the named code {code}.");
        }

        return new DaggerfallTextElement(code, text, value, x);
    }

    /// <summary>Reads the source family a published key or source names.</summary>
    private static DaggerfallTextKind TextKind(JsonElement value, DaggerfallContentDiagnostics diagnostics)
    {
        string name = Text(value, "kind", diagnostics);
        if (TryReadName(name, out DaggerfallTextKind kind)) return kind;
        diagnostics.Add($"Published text names the source family '{name}', which the contract does not declare.");
        return DaggerfallTextKind.Resource;
    }

    /// <summary>
    /// Reads one name from a closed set the published dialect writes by name.
    /// </summary>
    /// <remarks>
    /// The set is closed and the spelling starts with a letter, because a name that merely parses is not
    /// a value a consumer can act on: <c>Enum.TryParse</c> accepts a numeric spelling too, so a pack
    /// stating <c>"state": "1"</c> would be read as the second member of the enum and one stating
    /// <c>"code": "-1"</c> as a text run. Case is not significant, since the meaning is the name.
    /// </remarks>
    private static bool TryReadName<TEnum>(string name, out TEnum value)
        where TEnum : struct, Enum
    {
        if (name.Length != 0 && char.IsAsciiLetter(name[0]) && Enum.TryParse(name, ignoreCase: true, out value) && Enum.IsDefined(value))
        {
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Reads a string that may legitimately be empty: a readable text value states no reason it is not,
    /// and that is a fact about the value rather than a property the payload failed to carry.
    /// </summary>
    private static string EmptyAllowedText(JsonElement value, string property, DaggerfallContentDiagnostics diagnostics)
    {
        JsonElement result = Property(value, property, diagnostics);
        if (result.ValueKind == JsonValueKind.String) return result.GetString() ?? string.Empty;
        diagnostics.Add($"'{property}' must be a string.");
        return string.Empty;
    }

    /// <summary>
    /// Reads the published mobile catalog and joins it to the authored actors. Each imported record states the
    /// donor's parameters and the identity its donor name gives it; which authored actor carries it, and so its
    /// disposition, is joined here rather than by the import, so an authored actor edit leaves the imported data
    /// current. A consumer resolves a mobile's behaviour, damage and health without opening the donor's table.
    /// </summary>
    private static DaggerfallMobileCatalogSet ReadMobileCatalog(
        JsonElement root,
        IReadOnlyDictionary<DaggerfallActorId, DaggerfallActorDefinition> actors,
        DaggerfallContentDiagnostics diagnostics)
    {
        if (!root.TryGetProperty("mobiles", out JsonElement section) || section.ValueKind != JsonValueKind.Object)
        {
            // The payload must publish the catalog: every published actor's parameters would resolve to
            // nothing without it. The diagnostic is what a caller sees - reading aborts on it - so the
            // return below is never observed.
            diagnostics.Add("Base payload publishes no mobile catalog section; donor mobile parameters resolve to nothing.");
            return new DaggerfallMobileCatalogSet(
                new Dictionary<int, DaggerfallMobileDefinition>(),
                new Dictionary<string, DaggerfallMobileDefinition>(StringComparer.Ordinal));
        }

        Dictionary<int, DaggerfallMobileDefinition> mobiles = [];
        Dictionary<string, DaggerfallMobileDefinition> byActor = new(StringComparer.Ordinal);
        JsonElement[] records = [.. Array(section, "mobiles", diagnostics)];
        // The donor names some mobiles more than once; an authored actor keeps each addressable by numbering
        // it after its donor id, and the plain identity is the entry it repeats.
        Dictionary<string, int> identityCounts = records.Select(mobile => OptionalText(mobile, "identity"))
            .GroupBy(identity => identity, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        foreach (JsonElement mobile in records)
        {
            int donorId = Integer(mobile, "donorId", diagnostics);
            string identity = OptionalText(mobile, "identity");
            string numbered = $"{identity}-{donorId.ToString(CultureInfo.InvariantCulture)}";
            string? actor = actors.ContainsKey(new DaggerfallActorId(numbered)) ? numbered
                : actors.ContainsKey(new DaggerfallActorId(identity)) ? identity : null;
            string disposition = actor is not null ? (actor == numbered && identityCounts[identity] > 1 ? "published-variant" : "published")
                : donorId >= DaggerfallEncounterActors.FirstClassMobile ? "human-mobile" : "unpublished";
            JsonElement corpse = Object(Property(mobile, "corpse", diagnostics), "corpse", diagnostics);
            JsonElement sounds = Object(Property(mobile, "sounds", diagnostics), "sounds", diagnostics);
            JsonElement damage = Object(Property(mobile, "damage", diagnostics), "damage", diagnostics);
            JsonElement health = Object(Property(mobile, "health", diagnostics), "health", diagnostics);

            DaggerfallMobileDefinition definition = new(
                donorId,
                OptionalText(mobile, "donorName"),
                identity,
                mobile.TryGetProperty("castsMagic", out JsonElement castsMagic) && castsMagic.ValueKind == JsonValueKind.True,
                mobile.TryGetProperty("canOpenDoors", out JsonElement canOpenDoors) && canOpenDoors.ValueKind == JsonValueKind.True,
                Boolean(mobile, "seesThroughInvisibility", diagnostics),
                actor,
                disposition,
                OptionalText(mobile, "behaviour"),
                OptionalText(mobile, "affinity"),
                Integer(mobile, "maleTexture", diagnostics),
                Integer(mobile, "femaleTexture", diagnostics),
                Integer(corpse, "archive", diagnostics),
                Integer(corpse, "record", diagnostics),
                Boolean(mobile, "hasIdle", diagnostics),
                Boolean(mobile, "hasRangedAttack1", diagnostics),
                Boolean(mobile, "hasRangedAttack2", diagnostics),
                OptionalText(sounds, "move"),
                OptionalText(sounds, "bark"),
                OptionalText(sounds, "attack"),
                mobile.TryGetProperty("lootTableKey", out JsonElement lootTableKey) && lootTableKey.ValueKind == JsonValueKind.String ? lootTableKey.GetString() : null,
                OptionalText(mobile, "minMetalToHit"),
                Integer(damage, "minimum", diagnostics),
                Integer(damage, "maximum", diagnostics),
                Integer(health, "minimum", diagnostics),
                Integer(health, "maximum", diagnostics),
                Integer(mobile, "level", diagnostics),
                Integer(mobile, "armorValue", diagnostics),
                Boolean(mobile, "parrySounds", diagnostics),
                Integer(mobile, "mapChance", diagnostics),
                Integer(mobile, "weight", diagnostics),
                OptionalText(mobile, "team"),
                FlagByte(mobile, "attackModifierFlags", diagnostics))
            {
                SoulPoints = Integer(mobile, "soulPoints", diagnostics),
                NoShadow = Boolean(mobile, "noShadow", diagnostics),
                PrefersRanged = Boolean(mobile, "prefersRanged", diagnostics),
                GlowColor = mobile.TryGetProperty("glowColor", out JsonElement glow) && glow.ValueKind != JsonValueKind.Null
                    ? new System.Numerics.Vector3(Number(glow, "red", diagnostics), Number(glow, "green", diagnostics), Number(glow, "blue", diagnostics)) : null,
                ResistanceFlags = FlagByte(mobile, "resistanceFlags", diagnostics),
                ImmunityFlags = FlagByte(mobile, "immunityFlags", diagnostics),
                LowToleranceFlags = FlagByte(mobile, "lowToleranceFlags", diagnostics),
                CriticalWeaknessFlags = FlagByte(mobile, "criticalWeaknessFlags", diagnostics),
                Tactic = mobile.TryGetProperty("tactic", out JsonElement tactic) ? tactic.ValueKind == JsonValueKind.String ? tactic.GetString()! : "" : DaggerfallMobileDefinition.LegacyTactic,
                Tendencies = mobile.TryGetProperty("tendencies", out JsonElement tendencies)
                    ? new(OptionalNumber(tendencies, "courage", diagnostics) ?? double.NaN, OptionalNumber(tendencies, "discipline", diagnostics) ?? double.NaN) : null,
            };

            if (definition.Tendencies is { IsValid: false })
                diagnostics.Add($"Published mobile {donorId} needs its tendencies' courage and discipline between 0 and 1.");
            if (string.IsNullOrWhiteSpace(definition.Tactic))
                diagnostics.Add($"Published mobile {donorId} names a blank or non-text tactic; leave the field out to fight with '{DaggerfallMobileDefinition.LegacyTactic}'.");
            if (definition.SoulPoints < 0 || definition.GlowColor is { } tint && (tint.X < 0 || tint.Y < 0 || tint.Z < 0))
                diagnostics.Add($"Published mobile {donorId} requires nonnegative soul points and glow channels.");
            // A donor id identifies one mobile; two records claiming it would make a lookup ambiguous.
            if (!mobiles.TryAdd(donorId, definition))
            {
                diagnostics.Add($"Published mobile catalog names donor id {donorId} twice ('{definition.DonorName}' and '{mobiles[donorId].DonorName}'), so a consumer cannot resolve one record for it.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(identity))
            {
                diagnostics.Add($"Published mobile {donorId} ('{definition.DonorName}') carries no identity, so nothing links it to an actor.");
            }

            if (actor is not null && !byActor.TryAdd(actor, definition))
            {
                diagnostics.Add($"Published mobile catalog names actor '{actor}' twice, so a consumer cannot resolve one mobile for it.");
            }
        }

        if (mobiles.Count == 0)
        {
            diagnostics.Add("The published mobile catalog carries no mobile, so nothing resolves through it.");
        }

        return new DaggerfallMobileCatalogSet(mobiles, byActor);
    }

    /// <summary>
    /// Reads the published name tables from the pack alone. A bank keeps its donor identity and
    /// composition, and every fragment key must resolve through the text section: a name part with
    /// no text is a reference the generator cannot honor.
    /// </summary>
    private static DaggerfallNameTablesSet ReadNameTables(JsonElement root, DaggerfallTextSet text, DaggerfallContentDiagnostics diagnostics)
    {
        if (!root.TryGetProperty("names", out JsonElement section) || section.ValueKind != JsonValueKind.Object)
        {
            diagnostics.Add("Base payload publishes no names section; generated names resolve to nothing.");
            return new DaggerfallNameTablesSet([]);
        }

        List<DaggerfallNameBankDefinition> banks = [];
        HashSet<int> seenBanks = [];
        foreach (JsonElement bank in Array(section, "banks", diagnostics))
        {
            int index = Integer(bank, "bank", diagnostics);
            string name = Text(bank, "name", diagnostics);
            string kindName = Text(bank, "kind", diagnostics);
            if (!TryReadName(kindName, out DaggerfallNameBankKind kind))
            {
                diagnostics.Add($"Name bank '{name}' names the composition '{kindName}', which the contract does not declare.");
            }

            if (!seenBanks.Add(index))
            {
                diagnostics.Add($"Name bank {index} is published twice, so one of them is unreachable.");
            }

            List<DaggerfallNameSetDefinition> sets = [];
            HashSet<int> seenSets = [];
            foreach (JsonElement set in Array(bank, "sets", diagnostics))
            {
                int setIndex = Integer(set, "set", diagnostics);
                if (!seenSets.Add(setIndex))
                {
                    diagnostics.Add($"Name bank '{name}' set {setIndex} is published twice, so one of them is unreachable.");
                }

                List<DaggerfallTextKey> keys = [];
                foreach (JsonElement key in Array(set, "keys", diagnostics))
                {
                    DaggerfallTextKey parsed = ParseTextKey(key, DaggerfallTextKind.Name, $"name bank '{name}' set {setIndex}", diagnostics);
                    if (!text.Values.ContainsKey(parsed))
                    {
                        diagnostics.Add($"Name bank '{name}' set {setIndex} names text key '{parsed}', which the section does not carry.");
                    }

                    keys.Add(parsed);
                }

                if (keys.Count == 0)
                {
                    diagnostics.Add($"Name bank '{name}' set {setIndex} carries no fragments.");
                }

                sets.Add(new DaggerfallNameSetDefinition(setIndex, keys));
            }

            if (sets.Count == 0)
            {
                diagnostics.Add($"Name bank '{name}' carries no sets.");
            }

            banks.Add(new DaggerfallNameBankDefinition(index, name, kind, sets));
        }

        if (banks.Count == 0)
        {
            diagnostics.Add("The published name tables carry no bank, so nothing resolves through them.");
        }

        return new DaggerfallNameTablesSet(banks);
    }

    /// <summary>
    /// Reads the published rumor catalog from the pack alone. Each entry keeps the region, type,
    /// faction and quest references dialogue, faction and quest consumers resolve through, and its
    /// text key must resolve through the text section.
    /// </summary>
    private static DaggerfallRumorCatalogSet ReadRumorCatalog(JsonElement root, DaggerfallTextSet text, DaggerfallContentDiagnostics diagnostics)
    {
        if (!root.TryGetProperty("rumors", out JsonElement section) || section.ValueKind != JsonValueKind.Object)
        {
            diagnostics.Add("Base payload publishes no rumors section; rumor references resolve to nothing.");
            return new DaggerfallRumorCatalogSet([]);
        }

        List<DaggerfallRumorDefinition> entries = [];
        HashSet<int> seen = [];
        foreach (JsonElement entry in Array(section, "entries", diagnostics))
        {
            int index = Integer(entry, "index", diagnostics);
            if (!seen.Add(index))
            {
                diagnostics.Add($"Rumor {index} is published twice, so one of them is unreachable.");
            }

            int type = Integer(entry, "type", diagnostics);
            string typeName = OptionalText(entry, "typeName");
            string dispositionName = Text(entry, "typeDisposition", diagnostics);
            if (!TryReadName(dispositionName, out DaggerfallRumorTypeDisposition disposition))
            {
                diagnostics.Add($"Rumor {index} names the type disposition '{dispositionName}', which the contract does not declare.");
            }

            if (disposition == DaggerfallRumorTypeDisposition.Known && typeName.Length == 0)
            {
                diagnostics.Add($"Rumor {index} names a known type with no name.");
            }

            if (disposition == DaggerfallRumorTypeDisposition.Unknown && typeName.Length != 0)
            {
                diagnostics.Add($"Rumor {index} names an unknown type '{typeName}'.");
            }

            int region = Integer(entry, "region", diagnostics);
            if (region is < 0 or > 61)
            {
                diagnostics.Add($"Rumor {index} circulates in region {region} outside the 62 classic regions.");
            }

            DaggerfallTextKey textKey = ParseTextKey(Property(entry, "textKey", diagnostics), DaggerfallTextKind.Rumor, $"rumor {index}", diagnostics);
            if (!text.Values.ContainsKey(textKey))
            {
                diagnostics.Add($"Rumor {index} names text key '{textKey}', which the section does not carry.");
            }

            entries.Add(new DaggerfallRumorDefinition(
                index,
                type,
                typeName,
                disposition,
                region,
                Integer(entry, "flags", diagnostics),
                Boolean(entry, "isQuestRumor", diagnostics),
                Boolean(entry, "isSignMessage", diagnostics),
                Integer(entry, "faction1", diagnostics),
                Integer(entry, "faction2", diagnostics),
                Integer(entry, "questId", diagnostics),
                OptionalText(entry, "questName"),
                Integer(entry, "npcId", diagnostics),
                Integer(entry, "timeLimit", diagnostics),
                textKey));
        }

        if (entries.Count == 0)
        {
            diagnostics.Add("The published rumor catalog carries no rumor, so nothing resolves through it.");
        }

        return new DaggerfallRumorCatalogSet(entries);
    }

    /// <summary>
    /// Reads the authored links for donor-generated regional news. These rows are separate from
    /// RUMOR.DAT: the donor creates them when region conditions change, then TalkManager reads the
    /// resulting non-quest rumor. Keeping the links authored makes that source-backed boundary
    /// explicit instead of pretending those generated rows were imported rumor records.
    /// </summary>
    private static DaggerfallDialogueWorldRules ReadDialogueWorldRules(
        JsonElement root,
        DaggerfallTextSet text,
        DaggerfallContentDiagnostics diagnostics)
    {
        if (!root.TryGetProperty("dialogueWorldRules", out JsonElement section))
            return DaggerfallDialogueWorldRules.Empty;

        section = Object(section, "dialogueWorldRules", diagnostics);

        Dictionary<(int Type, int TextId), int[]> expectedRegionKeys = new()
        {
            // PlayerEntity marks the condition but emits only this row without the sign flag;
            // TalkManager therefore admits it in ordinary conversation. Famine, plague,
            // persecuted-temple and witch-burning rows are sign-only donor resources and stay
            // out of the spoken-news projection until a sign-reading caller owns them.
            [(11, 1410)] = [11], // CrimeWave
        };
        List<DaggerfallDialogueWorldNewsRule> rules = [];
        HashSet<(int Type, int TextId)> seen = [];
        foreach (JsonElement row in Array(section, "news", diagnostics))
        {
            int type = Integer(row, "type", diagnostics);
            int textId = Integer(row, "textId", diagnostics);
            string scopeName = Text(row, "scope", diagnostics);
            if (!TryReadName(scopeName, out DaggerfallVariableScope scope))
                diagnostics.Add($"Dialogue world news {type}/{textId} names unknown variable scope '{scopeName}'.");
            int[] variableKeys = [.. Array(row, "variableKeys", diagnostics)
                .Select(value => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int key) ? key : InvalidWorldVariableKey(diagnostics))];
            bool requiredValue = Boolean(row, "requiredValue", diagnostics);
            if (!seen.Add((type, textId)))
                diagnostics.Add($"Dialogue world news {type}/{textId} is published twice, so one link would be unreachable.");
            if (scope != DaggerfallVariableScope.Region)
                diagnostics.Add($"Dialogue world news {type}/{textId} must use a region variable because PlayerEntity emits it from RegionDataFlags.");
            if (variableKeys.Any(key => key < 0))
                diagnostics.Add($"Dialogue world news {type}/{textId} names a negative variable key.");
            if (!expectedRegionKeys.TryGetValue((type, textId), out int[]? expectedKeys)
                || !expectedKeys.SequenceEqual(variableKeys)
                || !requiredValue)
                diagnostics.Add($"Dialogue world news {type}/{textId} does not match the PlayerEntity region-condition mapping.");

            DaggerfallTextKey textKey = new(DaggerfallTextKind.Resource, textId.ToString(CultureInfo.InvariantCulture));
            if (!text.Values.ContainsKey(textKey))
                diagnostics.Add($"Dialogue world news {type}/{textId} names text key '{textKey}', which the text section does not carry.");

            rules.Add(new(type, textId, scope, variableKeys, requiredValue));
        }

        return new DaggerfallDialogueWorldRules(System.Array.AsReadOnly(rules.ToArray()));
    }

    private static int InvalidWorldVariableKey(DaggerfallContentDiagnostics diagnostics)
    {
        diagnostics.Add("Dialogue world news variable keys must be integers.");
        return -1;
    }

    /// <summary>
    /// Reads the published biographies from the pack alone. Each questionnaire keeps its questions,
    /// answers and effect references with the text keys they resolve through; a structural key with
    /// no text is a reference character creation cannot honor, while a link the publication marks
    /// unresolved is the recorded miss itself and only a claimed resolution must cite a value.
    /// </summary>
    private static DaggerfallBiographiesSet ReadBiographies(JsonElement root, DaggerfallTextSet text, DaggerfallContentDiagnostics diagnostics)
    {
        if (!root.TryGetProperty("biographies", out JsonElement section) || section.ValueKind != JsonValueKind.Object)
        {
            diagnostics.Add("Base payload publishes no biographies section; biography references resolve to nothing.");
            return new DaggerfallBiographiesSet([], 0);
        }

        List<DaggerfallBiographyDefinition> biographies = [];
        HashSet<string> seen = [];
        foreach (JsonElement biography in Array(section, "biographies", diagnostics))
        {
            int classIndex = Integer(biography, "classIndex", diagnostics);
            int biographyIndex = Integer(biography, "biographyIndex", diagnostics);
            if (!seen.Add($"{classIndex}-{biographyIndex}"))
            {
                diagnostics.Add($"Biography {classIndex}-{biographyIndex} is published twice, so one of them is unreachable.");
            }

            DaggerfallTextKey backstoryKey = ParseTextKey(Property(biography, "backstoryKey", diagnostics), DaggerfallTextKind.Resource, $"biography {classIndex}-{biographyIndex} backstory", diagnostics);
            string backstoryDispositionName = Text(biography, "backstoryDisposition", diagnostics);
            if (!TryReadName(backstoryDispositionName, out DaggerfallBiographyLinkDisposition backstoryDisposition))
            {
                diagnostics.Add($"Biography {classIndex}-{biographyIndex} names the link disposition '{backstoryDispositionName}', which the contract does not declare.");
            }

            if (backstoryDisposition == DaggerfallBiographyLinkDisposition.Resolved && !text.Values.ContainsKey(backstoryKey))
            {
                diagnostics.Add($"Biography {classIndex}-{biographyIndex} claims a resolved backstory link to '{backstoryKey}', which the section does not carry.");
            }

            List<DaggerfallBiographyQuestionDefinition> questions = [];
            foreach (JsonElement question in Array(biography, "questions", diagnostics))
            {
                int number = Integer(question, "number", diagnostics);
                List<DaggerfallTextKey> textKeys = [];
                foreach (JsonElement key in Array(question, "textKeys", diagnostics))
                {
                    DaggerfallTextKey parsed = ParseTextKey(key, DaggerfallTextKind.Biography, $"biography {classIndex}-{biographyIndex} question {number}", diagnostics);
                    if (!text.Values.ContainsKey(parsed))
                    {
                        diagnostics.Add($"Biography {classIndex}-{biographyIndex} question {number} names text key '{parsed}', which the section does not carry.");
                    }

                    textKeys.Add(parsed);
                }

                if (textKeys.Count == 0)
                {
                    diagnostics.Add($"Biography {classIndex}-{biographyIndex} question {number} carries no prose keys.");
                }

                List<DaggerfallBiographyAnswerDefinition> answers = [];
                HashSet<string> seenLetters = [];
                foreach (JsonElement answer in Array(question, "answers", diagnostics))
                {
                    string letter = Text(answer, "letter", diagnostics);
                    if (!seenLetters.Add(letter))
                    {
                        diagnostics.Add($"Biography {classIndex}-{biographyIndex} question {number} answers '{letter}' twice, so one of them is unreachable.");
                    }

                    DaggerfallTextKey answerKey = ParseTextKey(Property(answer, "textKey", diagnostics), DaggerfallTextKind.Biography, $"biography {classIndex}-{biographyIndex} question {number} answer '{letter}'", diagnostics);
                    if (!text.Values.ContainsKey(answerKey))
                    {
                        diagnostics.Add($"Biography {classIndex}-{biographyIndex} question {number} answer '{letter}' names text key '{answerKey}', which the section does not carry.");
                    }

                    List<DaggerfallBiographyEffectDefinition> effects = [];
                    foreach (JsonElement effect in Array(answer, "effects", diagnostics))
                    {
                        effects.Add(ReadBiographyEffect(effect, text, classIndex, biographyIndex, number, letter, diagnostics));
                    }

                    answers.Add(new DaggerfallBiographyAnswerDefinition(letter, answerKey, effects));
                }

                if (answers.Count == 0)
                {
                    diagnostics.Add($"Biography {classIndex}-{biographyIndex} question {number} carries no answers.");
                }

                questions.Add(new DaggerfallBiographyQuestionDefinition(number, textKeys, answers));
            }

            if (questions.Count == 0)
            {
                diagnostics.Add($"Biography {classIndex}-{biographyIndex} carries no questions.");
            }

            JsonElement image = Object(Property(biography, "image", diagnostics), $"biography {classIndex}-{biographyIndex} image", diagnostics);
            string mediaId = Text(image, "mediaId", diagnostics);
            string imageSource = Text(image, "source", diagnostics);
            bool published = Boolean(image, "published", diagnostics);
            string reason = EmptyAllowedText(image, "reason", diagnostics);
            if (published == !string.IsNullOrWhiteSpace(reason))
            {
                diagnostics.Add($"Biography {classIndex}-{biographyIndex} image '{mediaId}' states a reason exactly when it does not resolve.");
            }

            List<string> warnings = [];
            foreach (JsonElement warning in Array(biography, "warnings", diagnostics))
            {
                if (warning.ValueKind == JsonValueKind.String && warning.GetString() is { Length: > 0 } stated)
                {
                    warnings.Add(stated);
                }
                else
                {
                    diagnostics.Add($"Biography {classIndex}-{biographyIndex} states an empty warning.");
                }
            }

            biographies.Add(new DaggerfallBiographyDefinition(
                classIndex,
                biographyIndex,
                Integer(biography, "backstoryId", diagnostics),
                Boolean(biography, "backstoryExplicit", diagnostics),
                backstoryKey,
                backstoryDisposition,
                questions,
                new DaggerfallBiographyImageDefinition(mediaId, imageSource, published, reason),
                warnings));
        }

        if (biographies.Count == 0)
        {
            diagnostics.Add("The published biographies carry no questionnaire, so nothing resolves through them.");
        }

        return new DaggerfallBiographiesSet(biographies, Integer(section, "defaultLines", diagnostics));
    }

    private static DaggerfallBiographyEffectDefinition ReadBiographyEffect(
        JsonElement effect, DaggerfallTextSet text, int classIndex, int biographyIndex, int number, string letter, DaggerfallContentDiagnostics diagnostics)
    {
        string line = Text(effect, "text", diagnostics);
        string kindName = Text(effect, "kind", diagnostics);
        if (!TryReadName(kindName, out DaggerfallBiographyEffectKind kind))
        {
            diagnostics.Add($"Biography {classIndex}-{biographyIndex} question {number} answer '{letter}' effect '{line}' names the kind '{kindName}', which the contract does not declare.");
        }

        string first = OptionalText(effect, "first");
        string second = OptionalText(effect, "second");
        string third = OptionalText(effect, "third");
        string macroTarget = OptionalText(effect, "macroTarget");
        DaggerfallTextKey? target = null;
        DaggerfallBiographyLinkDisposition? disposition = null;
        string reason = string.Empty;
        if (kind == DaggerfallBiographyEffectKind.TextMacro)
        {
            target = ParseTextKey(Property(effect, "macroTarget", diagnostics), DaggerfallTextKind.Resource, $"biography {classIndex}-{biographyIndex} question {number} answer '{letter}' effect '{line}'", diagnostics);
            string dispositionName = Text(effect, "macroTargetDisposition", diagnostics);
            if (!TryReadName(dispositionName, out DaggerfallBiographyLinkDisposition parsed))
            {
                diagnostics.Add($"Biography effect '{line}' names the link disposition '{dispositionName}', which the contract does not declare.");
            }
            else
            {
                disposition = parsed;
            }

            reason = EmptyAllowedText(effect, "macroTargetReason", diagnostics);
            if (disposition == DaggerfallBiographyLinkDisposition.Resolved && (target is null || !text.Values.ContainsKey(target.Value)))
            {
                diagnostics.Add($"Biography effect '{line}' claims a resolved link to '{target}', which the section does not carry.");
            }

            if (reason.Length != 0 && disposition != DaggerfallBiographyLinkDisposition.Unresolved)
            {
                diagnostics.Add($"Biography effect '{line}' states a reason without an unresolved link.");
            }
        }
        else if (macroTarget.Length != 0)
        {
            diagnostics.Add($"Biography effect '{line}' carries a macro target without being a text macro.");
        }

        return new DaggerfallBiographyEffectDefinition(line, kind, first, second, third, target, disposition, reason);
    }

    /// <summary>
    /// Reads one text reference in the pack's key spelling and checks it names the expected family.
    /// A reference naming another family resolves elsewhere, so accepting it would answer lookups
    /// from the wrong family's values.
    /// </summary>
    private static DaggerfallTextKey ParseTextKey(JsonElement value, DaggerfallTextKind expected, string what, DaggerfallContentDiagnostics diagnostics)
    {
        DaggerfallTextKey fallback = new(expected, "missing");
        if (value.ValueKind != JsonValueKind.String || value.GetString() is not { } spelled)
        {
            diagnostics.Add($"{what} names a text key that is not a string.");
            return fallback;
        }

        int separator = spelled.IndexOf(':');
        if (separator <= 0 || !TryReadName(spelled[..separator], out DaggerfallTextKind kind) || kind != expected)
        {
            diagnostics.Add($"{what} names text key '{spelled}', which belongs to no '{expected}' family.");
            return fallback;
        }

        string id = spelled[(separator + 1)..];
        if (id.Length == 0 || id.Any(char.IsWhiteSpace))
        {
            diagnostics.Add($"{what} names text key '{spelled}' with no identity.");
            return fallback;
        }

        return new DaggerfallTextKey(kind, id);
    }

    /// <summary>
    /// Reads the published climate and politic grids from the pack alone. Each grid keeps the source
    /// bytes row by row with the sentinel column tiled, and every cell byte the rows carry must be a
    /// value the grid names: a byte no interpretation covers would leave a coordinate lookup
    /// answering from a value nothing accounts for.
    /// </summary>
    private static DaggerfallWorldGridsSet ReadWorldGrids(JsonElement root, DaggerfallContentDiagnostics diagnostics)
    {
        return new DaggerfallWorldGridsSet(ReadClimateGrid(root, diagnostics), ReadPoliticGrid(root, diagnostics));
    }

    private static DaggerfallClimateGridDefinition ReadClimateGrid(JsonElement root, DaggerfallContentDiagnostics diagnostics)
    {
        if (!root.TryGetProperty("climate", out JsonElement section) || section.ValueKind != JsonValueKind.Object)
        {
            diagnostics.Add("Base payload publishes no climate section; climate lookups resolve to nothing.");
            return new DaggerfallClimateGridDefinition(0, 0, [], []);
        }

        byte[] cells = ReadGridCells(section, "rows", 1001, 500, "climate", diagnostics);
        List<DaggerfallClimateValueDefinition> values = [];
        HashSet<int> seen = [];
        foreach (JsonElement value in Array(section, "values", diagnostics))
        {
            int cell = Integer(value, "value", diagnostics);
            if (!seen.Add(cell))
            {
                diagnostics.Add($"Climate grid names value {cell} twice, so one of them is unreachable.");
            }

            string name = OptionalText(value, "name");
            string dispositionName = Text(value, "disposition", diagnostics);
            if (!TryReadName(dispositionName, out DaggerfallClimateDisposition disposition))
            {
                diagnostics.Add($"Climate value {cell} names the disposition '{dispositionName}', which the contract does not declare.");
                continue;
            }

            if (disposition == DaggerfallClimateDisposition.Named && name.Length == 0)
            {
                diagnostics.Add($"Climate value {cell} is named with no name.");
            }

            if (disposition == DaggerfallClimateDisposition.Unresolved && name.Length != 0)
            {
                diagnostics.Add($"Climate value {cell} is unresolved with the name '{name}'.");
            }

            string people = OptionalText(value, "people");
            if (people.Length != 0 && people is not ("Breton" or "Nord" or "Redguard"))
                diagnostics.Add($"Climate value {cell} names unsupported People race '{people}'.");
            string climateBase = OptionalText(value, "climateBase");
            if (climateBase.Length != 0 && climateBase is not ("desert" or "mountain" or "temperate" or "swamp"))
                diagnostics.Add($"Climate value {cell} names unknown climate base '{climateBase}'.");
            values.Add(new DaggerfallClimateValueDefinition(cell, name, disposition) { People = people, ClimateBase = climateBase });
        }

        List<DaggerfallClimateSwap> swaps = [];
        if (section.TryGetProperty("swaps", out _))
        {
            foreach (JsonElement swap in Array(section, "swaps", diagnostics))
            {
                int archive = Integer(swap, "archive", diagnostics);
                string climateBase = Text(swap, "climate", diagnostics);
                string seasonName = Text(swap, "season", diagnostics);
                int first = Integer(swap, "firstRecord", diagnostics);
                int? last = swap.TryGetProperty("lastRecord", out JsonElement lastValue) && lastValue.ValueKind != JsonValueKind.Null
                    ? Integer(swap, "lastRecord", diagnostics) : null;
                int target = Integer(swap, "targetArchive", diagnostics);
                DaggerfallClimateSeason? season = seasonName switch
                {
                    "summer" => DaggerfallClimateSeason.Summer,
                    "winter" => DaggerfallClimateSeason.Winter,
                    "rain" => DaggerfallClimateSeason.Rain,
                    _ => null,
                };
                if (season is null || climateBase is not ("desert" or "mountain" or "temperate" or "swamp")
                    || archive < 0 || target < 0 || first < 0 || last < first)
                {
                    diagnostics.Add($"Climate swap of archive {archive} ({climateBase}, {seasonName}) is malformed.");
                    continue;
                }
                swaps.Add(new DaggerfallClimateSwap(archive, climateBase, season.Value, first, last, target));
            }
        }

        if (values.Count == 0)
        {
            diagnostics.Add("The published climate grid names no cell values.");
        }

        foreach (int carried in cells.Distinct().Order())
        {
            if (!seen.Contains(carried))
            {
                diagnostics.Add($"Climate grid carries value {carried} no interpretation covers.");
            }
        }

        return new DaggerfallClimateGridDefinition(1001, 500, cells, values) { Swaps = swaps };
    }

    private static DaggerfallPoliticGridDefinition ReadPoliticGrid(JsonElement root, DaggerfallContentDiagnostics diagnostics)
    {
        if (!root.TryGetProperty("politic", out JsonElement section) || section.ValueKind != JsonValueKind.Object)
        {
            diagnostics.Add("Base payload publishes no politic section; politic lookups resolve to nothing.");
            return new DaggerfallPoliticGridDefinition(0, 0, [], []);
        }

        byte[] cells = ReadGridCells(section, "rows", 1001, 500, "politic", diagnostics);
        List<DaggerfallPoliticValueDefinition> values = [];
        HashSet<int> seen = [];
        foreach (JsonElement value in Array(section, "values", diagnostics))
        {
            int cell = Integer(value, "value", diagnostics);
            if (!seen.Add(cell))
            {
                diagnostics.Add($"Politic grid names value {cell} twice, so one of them is unreachable.");
            }

            int region = OptionalInteger(value, "region", diagnostics) ?? -1;
            string dispositionName = Text(value, "disposition", diagnostics);
            if (!TryReadName(dispositionName, out DaggerfallPoliticDisposition disposition)
                || disposition == DaggerfallPoliticDisposition.OutOfBounds)
            {
                diagnostics.Add($"Politic value {cell} names the disposition '{dispositionName}', which the contract does not declare.");
                continue;
            }

            if (disposition == DaggerfallPoliticDisposition.Region && (region < 0 || region > 61))
            {
                diagnostics.Add($"Politic value {cell} names region {region} outside the 62 classic regions.");
            }

            if (disposition != DaggerfallPoliticDisposition.Region && region != -1)
            {
                diagnostics.Add($"Politic value {cell} names no region with index {region}.");
            }

            values.Add(new DaggerfallPoliticValueDefinition(cell, region, disposition));
        }

        if (values.Count == 0)
        {
            diagnostics.Add("The published politic grid names no cell values.");
        }

        foreach (int carried in cells.Distinct().Order())
        {
            if (!seen.Contains(carried))
            {
                diagnostics.Add($"Politic grid carries value {carried} no interpretation covers.");
            }
        }

        return new DaggerfallPoliticGridDefinition(1001, 500, cells, values);
    }

    /// <summary>
    /// Reads the published book catalog from the pack alone. Every book the catalog claims readable
    /// names each page by its id among the text section's book records: a page with no record would
    /// leave a reader holding a reference the resolver answers as missing.
    /// </summary>
    private static DaggerfallBooksSet ReadBooks(JsonElement root, DaggerfallTextSet text, DaggerfallContentDiagnostics diagnostics)
    {
        if (!root.TryGetProperty("books", out JsonElement section) || section.ValueKind != JsonValueKind.Object)
        {
            diagnostics.Add("Base payload publishes no books section; book messages resolve to nothing.");
            return new DaggerfallBooksSet(new ReadOnlyDictionary<int, DaggerfallBookDefinition>(new Dictionary<int, DaggerfallBookDefinition>()));
        }

        Dictionary<int, DaggerfallBookDefinition> books = [];
        foreach (JsonElement book in Array(section, "books", diagnostics))
        {
            int bookId = Integer(book, "bookId", diagnostics);
            string fileName = Text(book, "fileName", diagnostics);
            string title = OptionalText(book, "title") ?? string.Empty;
            string author = OptionalText(book, "author") ?? string.Empty;
            bool naughty = book.TryGetProperty("isNaughty", out JsonElement naughtyValue) && naughtyValue.ValueKind == JsonValueKind.True;
            uint filePrice = book.TryGetProperty("filePrice", out JsonElement priceValue) && priceValue.TryGetUInt32(out uint parsed) ? parsed : 0;
            uint runtimePrice = book.TryGetProperty("runtimePrice", out JsonElement runtimePriceValue) && runtimePriceValue.TryGetUInt32(out uint parsedRuntime) ? parsedRuntime : 0;
            int pageCount = Integer(book, "pageCount", diagnostics);
            List<string> pageKeys = [.. Array(book, "pageKeys", diagnostics).Select(key => key.GetString() ?? string.Empty)];
            string dispositionName = Text(book, "disposition", diagnostics);
            if (!TryReadName(dispositionName, out DaggerfallBookDisposition disposition))
            {
                diagnostics.Add($"Book {bookId} names the disposition '{dispositionName}', which the contract does not declare.");
                continue;
            }

            if (disposition == DaggerfallBookDisposition.Read)
            {
                if (pageKeys.Count != pageCount)
                {
                    diagnostics.Add($"Book {bookId} publishes {pageKeys.Count} page keys for {pageCount} pages.");
                }

                foreach (string key in pageKeys)
                {
                    if (!text.Values.ContainsKey(new DaggerfallTextKey(DaggerfallTextKind.Book, key)))
                    {
                        diagnostics.Add($"Book {bookId} cites missing book page '{key}'.");
                    }
                }
            }
            else if (pageKeys.Count != 0)
            {
                diagnostics.Add($"Book {bookId} is {dispositionName} with {pageKeys.Count} page keys.");
            }

            if (disposition == DaggerfallBookDisposition.Read && runtimePrice is < 300 or > 800)
            {
                diagnostics.Add($"Book {bookId} publishes runtime price {runtimePrice}, outside the donor's 300..800 range.");
            }
            else if (disposition != DaggerfallBookDisposition.Read && runtimePrice != 0)
            {
                diagnostics.Add($"Unreadable book {bookId} publishes runtime price {runtimePrice}.");
            }

            if (!books.TryAdd(bookId, new DaggerfallBookDefinition(bookId, fileName, title, author, naughty, filePrice, runtimePrice, pageCount, pageKeys, disposition)))
            {
                diagnostics.Add($"Book catalog names book {bookId} twice, so one of them is unreachable.");
            }
        }

        if (books.Count == 0)
        {
            diagnostics.Add("The published book catalog carries no books.");
        }

        return new DaggerfallBooksSet(new ReadOnlyDictionary<int, DaggerfallBookDefinition>(books));
    }

    /// <summary>
    /// Reads the published faction catalog from the pack alone. Every relation the catalog claims
    /// resolved names a faction the section carries, and every region claim names factions it
    /// carries: a social consumer holding a reference the catalog cannot answer would read policy
    /// from a miss.
    /// </summary>
    private static DaggerfallFactionsSet ReadFactions(JsonElement root, int regionCount, DaggerfallContentDiagnostics diagnostics)
    {
        if (!root.TryGetProperty("factions", out JsonElement section) || section.ValueKind != JsonValueKind.Object)
        {
            diagnostics.Add("Base payload publishes no factions section; faction lookups resolve to nothing.");
            return new DaggerfallFactionsSet(
                new ReadOnlyDictionary<int, DaggerfallFactionDefinition>(new Dictionary<int, DaggerfallFactionDefinition>()),
                new ReadOnlyDictionary<int, DaggerfallRegionFactionDefinition>(new Dictionary<int, DaggerfallRegionFactionDefinition>()),
                new ReadOnlyDictionary<string, int>(new Dictionary<string, int>(StringComparer.Ordinal)));
        }

        Dictionary<int, DaggerfallFactionDefinition> factions = [];
        Dictionary<string, int> names = new(StringComparer.Ordinal);
        foreach (JsonElement faction in Array(section, "factions", diagnostics))
        {
            int id = Integer(faction, "id", diagnostics);
            int filedId = Integer(faction, "filedId", diagnostics);
            string name = Text(faction, "name", diagnostics);
            int parent = Integer(faction, "parent", diagnostics);
            DaggerfallFactionLinkDisposition? parentDisposition = null;
            if (faction.TryGetProperty("parentDisposition", out JsonElement parentValue)
                && parentValue.ValueKind == JsonValueKind.String
                && parentValue.GetString() is string parentName)
            {
                if (!TryReadName(parentName, out DaggerfallFactionLinkDisposition parsed))
                {
                    diagnostics.Add($"Faction {id} names the parent disposition '{parentName}', which the contract does not declare.");
                    continue;
                }

                parentDisposition = parsed;
            }

            if ((parent == 0) != (parentDisposition is null))
            {
                diagnostics.Add($"Faction {id} names parent {parent} with no matching parent disposition.");
            }

            List<int> children = [.. Array(faction, "children", diagnostics).Select(child => child.GetInt32())];
            int type = Integer(faction, "type", diagnostics);
            string typeName = OptionalText(faction, "typeName") ?? string.Empty;
            // Regions outside the classic range name no region: the donor converts filed values
            // without a range check, so vampire clans carry 62..70 and region claims cover 0..61.
            int region = Integer(faction, "region", diagnostics);

            List<int> allies = [.. Array(faction, "allies", diagnostics).Select(ally => ally.GetInt32())];
            List<int> enemies = [.. Array(faction, "enemies", diagnostics).Select(enemy => enemy.GetInt32())];
            List<int> sprites = [.. Array(faction, "sprites", diagnostics).Select(sprite => sprite.GetInt32())];
            string allyName = Text(faction, "allyDisposition", diagnostics);
            string enemyName = Text(faction, "enemyDisposition", diagnostics);
            if (!TryReadName(allyName, out DaggerfallFactionLinkDisposition allyDisposition)
                || !TryReadName(enemyName, out DaggerfallFactionLinkDisposition enemyDisposition))
            {
                diagnostics.Add($"Faction {id} names a relation disposition the contract does not declare.");
                continue;
            }

            DaggerfallFactionSpriteDefinition[] visuals = faction.TryGetProperty("spriteVisuals", out var visualRows)
                ? [.. visualRows.EnumerateArray().Select(value => new DaggerfallFactionSpriteDefinition(
                    Integer(value, "id", diagnostics), Integer(value, "archive", diagnostics), Integer(value, "record", diagnostics)))] : [];
            if (visuals.Any(value => value.Id < 0 || value.Archive < 0 || value.Record is < 0 or > 127)
                || visuals.Length != 0 && !visuals.Select(value => value.Id).SequenceEqual(sprites))
                diagnostics.Add($"Faction {id} has malformed normalized billboard addresses.");

            if (!factions.TryAdd(id, new DaggerfallFactionDefinition(
                id, filedId, name, parent, parentDisposition, children, type, typeName, region,
                Integer(faction, "power", diagnostics), Integer(faction, "flags", diagnostics), Integer(faction, "ruler", diagnostics),
                allies, allyDisposition, enemies, enemyDisposition, sprites,
                Integer(faction, "face", diagnostics), Integer(faction, "race", diagnostics),
                Integer(faction, "socialGroup", diagnostics), OptionalText(faction, "socialGroupName") ?? string.Empty,
                Integer(faction, "guildGroup", diagnostics), OptionalText(faction, "guildGroupName") ?? string.Empty,
                Integer(faction, "reputation", diagnostics), Integer(faction, "summon", diagnostics),
                Integer(faction, "minimumFame", diagnostics), Integer(faction, "maximumFame", diagnostics),
                Integer(faction, "vampire", diagnostics), Integer(faction, "rank", diagnostics)) { SpriteVisuals = visuals }))
            {
                diagnostics.Add($"Faction catalog names faction {id} twice, so one of them is unreachable.");
            }

            names.TryAdd(name, id);
        }

        if (factions.Count == 0)
        {
            diagnostics.Add("The published faction catalog carries no factions.");
        }

        foreach (DaggerfallFactionDefinition faction in factions.Values)
        {
            if (faction.Parent != 0 && !factions.ContainsKey(faction.Parent) && faction.ParentDisposition == DaggerfallFactionLinkDisposition.Resolved)
            {
                diagnostics.Add($"Faction {faction.Id} claims a resolved parent no faction carries.");
            }

            foreach (int ally in faction.Allies)
            {
                if (!factions.ContainsKey(ally) && faction.AllyDisposition == DaggerfallFactionLinkDisposition.Resolved)
                {
                    diagnostics.Add($"Faction {faction.Id} claims a resolved ally no faction carries.");
                }
            }

            foreach (int enemy in faction.Enemies)
            {
                if (!factions.ContainsKey(enemy) && faction.EnemyDisposition == DaggerfallFactionLinkDisposition.Resolved)
                {
                    diagnostics.Add($"Faction {faction.Id} claims a resolved enemy no faction carries.");
                }
            }
        }

        Dictionary<int, DaggerfallRegionFactionDefinition> regions = [];
        foreach (JsonElement claimed in Array(section, "regions", diagnostics))
        {
            int region = Integer(claimed, "region", diagnostics);
            List<int> claimants = [.. Array(claimed, "factionIds", diagnostics).Select(entry => entry.GetInt32())];
            string dispositionName = Text(claimed, "disposition", diagnostics);
            if (!TryReadName(dispositionName, out DaggerfallRegionFactionDisposition disposition))
            {
                diagnostics.Add($"Region {region} names the disposition '{dispositionName}', which the contract does not declare.");
                continue;
            }

            if ((disposition == DaggerfallRegionFactionDisposition.Claimed) != (claimants.Count > 0))
            {
                diagnostics.Add($"Region {region} claims {claimants.Count} factions as {dispositionName}.");
            }

            foreach (int claimant in claimants)
            {
                if (!factions.ContainsKey(claimant))
                {
                    diagnostics.Add($"Region {region} is claimed by faction {claimant} no faction carries.");
                }
            }

            if (!regions.TryAdd(region, new DaggerfallRegionFactionDefinition(region, claimants, disposition)))
            {
                diagnostics.Add($"Faction catalog claims region {region} twice, so one claim is unreachable.");
            }
        }

        // Region claims are read by region index, so the catalog states one for each published region.
        if (regions.Count != regionCount)
        {
            diagnostics.Add($"The published faction catalog claims {regions.Count} regions for the {regionCount} published regions.");
        }

        Dictionary<(int Archive, int Record), string> npcCaptions = [];
        if (section.TryGetProperty("npcCaptions", out var captionRows))
            foreach (var row in captionRows.EnumerateArray())
            {
                int archive = Integer(row, "archive", diagnostics), record = Integer(row, "record", diagnostics);
                string caption = Text(row, "caption", diagnostics);
                if (archive < 0 || record is < 0 or > 127 || string.IsNullOrWhiteSpace(caption)
                    || !npcCaptions.TryAdd((archive, record), caption))
                    diagnostics.Add($"NPC caption has malformed or duplicate sprite address {archive}/{record}.");
            }
        return new DaggerfallFactionsSet(
            new ReadOnlyDictionary<int, DaggerfallFactionDefinition>(factions),
            new ReadOnlyDictionary<int, DaggerfallRegionFactionDefinition>(regions),
            new ReadOnlyDictionary<string, int>(names)) { NpcCaptions = new ReadOnlyDictionary<(int, int), string>(npcCaptions) };
    }

    /// <summary>
    /// Reads the published wilderness terrain from the pack alone. The heightmap tiles rows the
    /// way the grids do; the cell samples travel as one base-64 span. Every heightmap byte the
    /// rows carry is data, so a row count or tiling miss is a refusal rather than a partial map.
    /// </summary>
    /// <summary>
    /// Reads the wilderness terrain: the heightmap from the section, and the cell samples from the raw file the
    /// section names in the data container, read when a terrain surface first needs them.
    /// </summary>
    private static DaggerfallTerrainSet ReadTerrain(JsonElement root, DaggerfallData? data, DaggerfallContentDiagnostics diagnostics)
    {
        const int Width = 1000, Height = 500, CellSamples = 25;
        if (!root.TryGetProperty("terrain", out JsonElement section) || section.ValueKind != JsonValueKind.Object)
        {
            diagnostics.Add("Base payload publishes no terrain section; terrain lookups resolve to nothing.");
            return new DaggerfallTerrainSet(0, 0, [], []);
        }

        byte[] heightmap = ReadGridCells(section, "heightmap", Width, Height, "terrain heightmap", diagnostics);
        if (heightmap.Length != Width * Height)
        {
            diagnostics.Add($"The published terrain heightmap carries {heightmap.Length} bytes for a 1000 by 500 map.");
        }

        string path = Text(section, "samples", diagnostics);
        string? woods = OptionalText(section, "woods", diagnostics);
        if (data is not null && path.Length != 0 && !data.Contains(path))
            diagnostics.Add($"The published terrain names its samples at '{path}', which {data.Source} does not carry.");
        return new DaggerfallTerrainSet(Width, Height, heightmap, () =>
        {
            if (data is null)
                throw new InvalidOperationException($"The terrain samples are read from '{path}' in {DaggerfallData.DataContainerName}, and this terrain was read without it.");
            byte[] samples = data.ReadBytes(path).ToArray();
            return samples.Length == Width * Height * CellSamples
                ? samples
                : throw new DaggerfallContentException([$"The published terrain samples at '{path}' carry {samples.Length} bytes for {Width * Height} cells of {CellSamples}."]);
        }, () =>
        {
            if (data is null || !data.Contains(DaggerfallTerrainRelief.Path)) return null;
            if (DaggerfallTerrainRelief.WoodsMismatch(woods, data.TryReadFile(DaggerfallTerrainRelief.RecordPath, out ReadOnlyMemory<byte> record) ? record : null) is { } mismatch)
                System.Diagnostics.Trace.TraceWarning(mismatch);
            return DaggerfallTerrainRelief.Read(data.ReadBytes(DaggerfallTerrainRelief.Path).Span);
        },
            () => data is not null && data.Contains(DaggerfallTerrainRivers.Path)
                ? DaggerfallTerrainRivers.Read(data.ReadBytes(DaggerfallTerrainRivers.Path).Span) : null);
    }

    /// <summary>
    /// Reads the published item template catalog from the pack alone. Every template the catalog
    /// claims substitute-resolved names a group the donor states; every magic placement names a
    /// group the donor table carries. A template no group claims is a miss the ledger owns, not
    /// a record this section invents.
    /// </summary>
    private static DaggerfallItemTemplateSet ReadItemTemplates(JsonElement root, DaggerfallContentDiagnostics diagnostics)
    {
        if (!root.TryGetProperty("itemTemplates", out JsonElement section) || section.ValueKind != JsonValueKind.Object)
        {
            diagnostics.Add("Base payload publishes no itemTemplates section; template lookups resolve to nothing.");
            return new DaggerfallItemTemplateSet(
                new ReadOnlyDictionary<int, DaggerfallItemTemplateDefinition>(new Dictionary<int, DaggerfallItemTemplateDefinition>()),
                new ReadOnlyDictionary<int, DaggerfallMagicTemplateDefinition>(new Dictionary<int, DaggerfallMagicTemplateDefinition>()));
        }

        Dictionary<int, DaggerfallItemTemplateDefinition> templates = [];
        foreach (JsonElement template in Array(section, "templates", diagnostics))
        {
            int index = Integer(template, "index", diagnostics);
            List<string> groups = [.. Array(template, "groups", diagnostics).Select(group => group.GetString() ?? string.Empty)];
            if (!templates.TryAdd(index, new DaggerfallItemTemplateDefinition(
                index, Text(template, "name", diagnostics), groups,
                Number(template, "baseWeight", diagnostics), Integer(template, "hitPoints", diagnostics),
                Integer(template, "capacityOrTarget", diagnostics), Integer(template, "basePrice", diagnostics),
                Integer(template, "enchantmentPoints", diagnostics), Integer(template, "rarity", diagnostics),
                Integer(template, "variants", diagnostics), Boolean(template, "isBluntWeapon", diagnostics),
                Boolean(template, "isLiquid", diagnostics), Boolean(template, "isOneHanded", diagnostics),
                Boolean(template, "isIngredient", diagnostics), Boolean(template, "stackable", diagnostics),
                Integer(template, "worldTextureArchive", diagnostics), Integer(template, "worldTextureRecord", diagnostics),
                Integer(template, "playerTextureArchive", diagnostics), Integer(template, "playerTextureRecord", diagnostics),
                OptionalText(template, "weaponMediaId") ?? string.Empty, Integer(template, "drawOrderOrEffect", diagnostics))
            {
                ClassicModel = OptionalInteger(template, "classicModel", diagnostics),
                RefreshedArt = OptionalText(template, "refreshedArt", diagnostics),
            }))
            {
                diagnostics.Add($"Item template catalog names template {index} twice, so one of them is unreachable.");
            }
        }

        // A template modelled on another reads the classic's per-template tables by that one's index, which must be a
        // template of its own.
        foreach (DaggerfallItemTemplateDefinition template in templates.Values)
            if (template.ClassicModel is int model && (model == template.Index || !templates.TryGetValue(model, out var classic) || classic.ClassicModel is not null))
                diagnostics.Add($"Item template {template.Index} is modelled on template {model}, which is not a template of its own.");

        Dictionary<int, DaggerfallMagicTemplateDefinition> magic = [];
        foreach (JsonElement entry in Array(section, "magic", diagnostics))
        {
            int index = Integer(entry, "index", diagnostics);
            List<DaggerfallTemplateEnchantmentDefinition> enchantments = [.. Array(entry, "enchantments", diagnostics)
                .Select(enchantment => new DaggerfallTemplateEnchantmentDefinition(Text(enchantment, "type", diagnostics), Integer(enchantment, "param", diagnostics)))];
            if (!magic.TryAdd(index, new DaggerfallMagicTemplateDefinition(
                index, Text(entry, "name", diagnostics), Text(entry, "type", diagnostics),
                Integer(entry, "group", diagnostics), OptionalText(entry, "groupName") ?? string.Empty,
                Integer(entry, "groupIndex", diagnostics), enchantments,
                Integer(entry, "uses", diagnostics), Integer(entry, "value", diagnostics), Integer(entry, "material", diagnostics))))
            {
                diagnostics.Add($"Item template catalog names magic template {index} twice, so one of them is unreachable.");
            }
        }

        Dictionary<int, DaggerfallItemGroupDefinition> groupDefinitions = [];
        if (section.TryGetProperty("groupTable", out JsonElement groupTable) && groupTable.ValueKind == JsonValueKind.Object)
            foreach (JsonElement group in Array(groupTable, "groups", diagnostics))
            {
                int id = Integer(group, "id", diagnostics);
                int[] values = [.. Array(group, "values", diagnostics).Select(value => value.GetInt32())];
                bool templateIndices = Boolean(group, "templateIndices", diagnostics);
                if (id is < 0 or > 28 || values.Length == 0 || (templateIndices && values.Any(value => !templates.ContainsKey(value)))
                    || !values.SequenceEqual(values.OrderBy(value => unchecked((uint)value)))
                    || !groupDefinitions.TryAdd(id, new(id, Text(group, "name", diagnostics), templateIndices, values)))
                    diagnostics.Add($"Item group {id} has invalid or repeated identity, values or runtime ordinal ordering.");
            }
        return new DaggerfallItemTemplateSet(
            new ReadOnlyDictionary<int, DaggerfallItemTemplateDefinition>(templates),
            new ReadOnlyDictionary<int, DaggerfallMagicTemplateDefinition>(magic))
        { Groups = new ReadOnlyDictionary<int, DaggerfallItemGroupDefinition>(groupDefinitions) };
    }

    /// <summary>
    /// Reads the cinematic identities from the pack alone. A bound cinematic without its caller or
    /// an unresolved one claiming a hook is a contradiction the pack must not state.
    /// </summary>
    private static DaggerfallCinematicSet ReadCinematics(JsonElement root, DaggerfallContentDiagnostics diagnostics)
    {
        if (!root.TryGetProperty("cinematics", out JsonElement section) || section.ValueKind != JsonValueKind.Object)
        {
            diagnostics.Add("Base payload publishes no cinematics section; cinematic lookups resolve to nothing.");
            return new DaggerfallCinematicSet(new ReadOnlyDictionary<string, DaggerfallCinematicDefinition>(new Dictionary<string, DaggerfallCinematicDefinition>(StringComparer.OrdinalIgnoreCase)));
        }

        Dictionary<string, DaggerfallCinematicDefinition> cinematics = new(StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement cinematic in Array(section, "cinematics", diagnostics))
        {
            string file = Text(cinematic, "fileName", diagnostics);
            string kindName = Text(cinematic, "kind", diagnostics);
            string bindingName = Text(cinematic, "binding", diagnostics);
            if (!TryReadName(kindName, out DaggerfallCinematicKind kind))
            {
                diagnostics.Add($"Cinematic '{file}' names the kind '{kindName}', which the contract does not declare.");
                continue;
            }

            if (!TryReadName(bindingName, out DaggerfallCinematicBinding binding))
            {
                diagnostics.Add($"Cinematic '{file}' names the binding '{bindingName}', which the contract does not declare.");
                continue;
            }

            string caller = OptionalText(cinematic, "caller");
            int? faction = OptionalInteger(cinematic, "factionId", diagnostics);
            string quest = OptionalText(cinematic, "quest");
            if (binding == DaggerfallCinematicBinding.Bound && caller.Length == 0)
            {
                diagnostics.Add($"Cinematic '{file}' is bound and names no caller.");
            }

            if (binding == DaggerfallCinematicBinding.Unresolved && (caller.Length != 0 || faction.HasValue || quest.Length != 0))
            {
                diagnostics.Add($"Cinematic '{file}' is unresolved and still claims a hook.");
            }

            if (!cinematics.TryAdd(file, new DaggerfallCinematicDefinition(
                file, kind, Long(cinematic, "byteLength", diagnostics), Text(cinematic, "digest", diagnostics),
                binding, caller, faction, quest) { Artifact = ReadCinematicArtifact(cinematic, file, diagnostics) }))
            {
                diagnostics.Add($"Cinematic '{file}' is published twice, so one of them is unreachable.");
            }
        }

        return new DaggerfallCinematicSet(new ReadOnlyDictionary<string, DaggerfallCinematicDefinition>(cinematics));
    }

    private static DaggerfallCinematicArtifact? ReadCinematicArtifact(JsonElement cinematic, string source,
        DaggerfallContentDiagnostics diagnostics)
    {
        if (!cinematic.TryGetProperty("artifact", out JsonElement value) || value.ValueKind == JsonValueKind.Null) return null;
        value = Object(value, $"cinematic '{source}' artifact", diagnostics);
        DaggerfallCinematicArtifact artifact = new(Text(value, "path", diagnostics), Text(value, "mimeType", diagnostics),
            Long(value, "byteLength", diagnostics), Text(value, "sha256", diagnostics), Integer(value, "width", diagnostics),
            Integer(value, "height", diagnostics), Long(value, "frameCount", diagnostics),
            OptionalNumber(value, "durationSeconds", diagnostics) ?? 0, Boolean(value, "hasAudio", diagnostics));
        string expected = "worldrpg/media/cinematics/" + System.IO.Path.GetFileNameWithoutExtension(source).ToLowerInvariant() + ".webm";
        if (artifact.Path != expected || artifact.MimeType != "video/webm" || artifact.ByteLength <= 0 ||
            artifact.Sha256.Length != 64 || artifact.Sha256.Any(character => !Uri.IsHexDigit(character)) ||
            artifact.Width <= 0 || artifact.Height <= 0 || artifact.FrameCount <= 0 || artifact.DurationSeconds <= 0)
            diagnostics.Add($"Cinematic '{source}' has an invalid packaged WebM artifact; expected '{expected}' with positive dimensions, frames, duration, bytes and SHA-256.");
        return artifact;
    }

    private static byte[] ReadGridCells(JsonElement section, string rowsKey, int width, int height, string what, DaggerfallContentDiagnostics diagnostics)    {
        List<byte> cells = [];
        HashSet<int> seenRows = [];
        foreach (JsonElement row in Array(section, rowsKey, diagnostics))
        {
            int y = Integer(row, "y", diagnostics);
            if (!seenRows.Add(y))
            {
                diagnostics.Add($"{what} grid publishes row {y} twice, so one of them is unreachable.");
            }

            List<byte> line = [];
            foreach (JsonElement run in Array(row, "runs", diagnostics))
            {
                int count = Integer(run, "count", diagnostics);
                int value = Integer(run, "value", diagnostics);
                if (count <= 0 || value is < 0 or > 0xff)
                {
                    diagnostics.Add($"{what} grid row {y} carries a run that covers no columns or repeats no byte.");
                    continue;
                }

                for (int column = 0; column < count; column++)
                {
                    line.Add((byte)value);
                }
            }

            if (line.Count != width)
            {
                diagnostics.Add($"{what} grid row {y} tiles {line.Count} columns for a {width}-column grid.");
            }

            cells.AddRange(line);
        }

        if (seenRows.Count != height)
        {
            diagnostics.Add($"{what} grid carries {seenRows.Count} rows for a {height}-row grid.");
        }

        return [.. cells];
    }

    /// <summary>
    /// Reads the published magical catalogs from the pack alone. A spell keeps the identity the source
    /// gave it, an enchantment that named a spell resolves to that spell's key, and anything the
    /// publication could not carry is reported: a link to a key no spell defines, a spell that claims a
    /// shared identity no other spell shares, and a record the source stated without a spell.
    /// </summary>
    private static DaggerfallMagicCatalogSet ReadMagicCatalog(JsonElement root, DaggerfallContentDiagnostics diagnostics)
    {
        if (!root.TryGetProperty("magic", out JsonElement section) || section.ValueKind != JsonValueKind.Object)
        {
            // The payload must publish the catalog: every spell and every enchantment link would resolve
            // to nothing without it. The diagnostic is what a caller sees - reading aborts on it - so the
            // return below is never observed.
            diagnostics.Add("Base payload publishes no magic catalog section; spells and enchantment links resolve to nothing.");
            return new DaggerfallMagicCatalogSet(
                new Dictionary<string, DaggerfallSpellDefinition>(StringComparer.Ordinal),
                new Dictionary<string, DaggerfallMagicItemDefinition>(StringComparer.Ordinal),
                new Dictionary<(int Type, int SubType), DaggerfallMagicEffectCostDefinition>(),
                new Dictionary<string, DaggerfallEnchantmentSetting>(StringComparer.Ordinal));
        }

        Dictionary<string, DaggerfallSpellDefinition> spells = new(StringComparer.Ordinal);
        Dictionary<int, int> identityUse = [];
        foreach (JsonElement spell in Array(section, "spells", diagnostics))
        {
            string key = Text(spell, "key", diagnostics);
            if (string.IsNullOrWhiteSpace(key) || !spells.TryAdd(key, null!))
            {
                diagnostics.Add($"Published spell key '{key}' is empty or claimed twice, so a consumer cannot resolve it to one spell.");
                continue;
            }

            int identity = Integer(spell, "identity", diagnostics);
            bool identityShared = Boolean(spell, "identityShared", diagnostics);
            identityUse[identity] = identityUse.TryGetValue(identity, out int seen) ? seen + 1 : 1;
            List<DaggerfallSpellEffectDefinition> effects = [];
            foreach (JsonElement effect in Array(spell, "effects", diagnostics))
            {
                effects.Add(ReadSpellEffect(effect, diagnostics));
            }

            spells[key] = new DaggerfallSpellDefinition(
                key, identity, identityShared, Text(spell, "name", diagnostics),
                Integer(spell, "element", diagnostics), Integer(spell, "rangeType", diagnostics),
                Integer(spell, "cost", diagnostics), Integer(spell, "icon", diagnostics), effects)
            {
                IsCustom = spell.TryGetProperty("isCustom", out var custom) && custom.GetBoolean(),
                SpellsForSale = spell.TryGetProperty("spellsForSale", out var forSale) ? forSale.GetBoolean()
                    : !spell.TryGetProperty("isCustom", out var customRow) || !customRow.GetBoolean(),
            };
        }

        foreach ((string key, DaggerfallSpellDefinition spell) in spells)
        {
            bool actuallyShared = !spell.IsCustom && identityUse.TryGetValue(spell.Identity, out int uses) && uses > 1;
            if (spell.IdentityShared != actuallyShared)
            {
                // The flag is what tells a consumer whether an identity is ambiguous; disagreeing with the
                // catalog's own records would make it resolve the wrong spell.
                diagnostics.Add($"Published spell '{key}' reports identity {spell.Identity} as {(spell.IdentityShared ? "shared" : "unique")}, but the catalog carries {(actuallyShared ? "more than one" : "one")} record with it.");
            }
        }

        Dictionary<string, DaggerfallMagicItemDefinition> items = new(StringComparer.Ordinal);
        foreach (JsonElement item in Array(section, "magicItems", diagnostics))
        {
            string key = Text(item, "key", diagnostics);
            if (string.IsNullOrWhiteSpace(key) || !items.TryAdd(key, null!))
            {
                diagnostics.Add($"Published magic-item key '{key}' is empty or claimed twice, so a consumer cannot resolve it to one template.");
                continue;
            }

            List<DaggerfallMagicEnchantmentDefinition> enchantments = [];
            foreach (JsonElement enchantment in Array(item, "enchantments", diagnostics))
            {
                string enchantmentKey = Text(enchantment, "key", diagnostics);
                string? spellKey = enchantment.TryGetProperty("spell", out JsonElement link) && link.ValueKind == JsonValueKind.String ? link.GetString() : null;
                if (spellKey is not null && !spells.ContainsKey(spellKey))
                {
                    diagnostics.Add($"Published enchantment '{enchantmentKey}' names spell '{spellKey}', which the catalog does not define.");
                    spellKey = null;
                }

                int enchantmentType = Integer(enchantment, "type", diagnostics);
                if (enchantmentType is 0 or 1 or 2 && string.IsNullOrWhiteSpace(spellKey))
                    diagnostics.Add($"Published spell trigger '{enchantmentKey}' has no normalized spell link.");
                enchantments.Add(new DaggerfallMagicEnchantmentDefinition(
                    enchantmentKey,
                    enchantmentType,
                    Integer(enchantment, "param", diagnostics),
                    Text(enchantment, "paramMeaning", diagnostics),
                    spellKey,
                    Boolean(enchantment, "spellIdentityShared", diagnostics)));
            }

            items[key] = new DaggerfallMagicItemDefinition(
                key, Long(item, "offset", diagnostics), Text(item, "name", diagnostics),
                Integer(item, "type", diagnostics), Integer(item, "group", diagnostics), Integer(item, "groupIndex", diagnostics),
                Integer(item, "uses", diagnostics), Integer(item, "value", diagnostics), Integer(item, "material", diagnostics), enchantments);
        }

        Dictionary<(int Type, int SubType), DaggerfallMagicEffectCostDefinition> costs = [];
        foreach (JsonElement cost in Array(section, "effectCosts", diagnostics))
        {
            int type = Integer(cost, "type", diagnostics);
            int subtype = Integer(cost, "subType", diagnostics);
            int settingsType = Integer(cost, "settingsType", diagnostics);
            string school = Text(cost, "school", diagnostics);
            JsonElement coefficients = Object(Property(cost, "coefficients", diagnostics), "coefficients", diagnostics);
            DaggerfallMagicEffectCostDefinition value = new(type, subtype, settingsType, school,
                Integer(coefficients, "first", diagnostics), Integer(coefficients, "second", diagnostics),
                Integer(coefficients, "third", diagnostics), Integer(coefficients, "fourth", diagnostics));
            if (type is < 0 or > 50 || subtype is < -1 or > 11 || settingsType is < 1 or > 7
                || school is not ("alteration" or "restoration" or "destruction" or "mysticism" or "thaumaturgy" or "illusion")
                || value.Coefficient0 < 0 || value.Coefficient1 < 0 || value.Coefficient2 < 0 || value.Coefficient3 < 0
                || !costs.TryAdd((type, subtype), value))
                diagnostics.Add($"Magic effect cost row ({type}, {subtype}) is invalid, duplicated, or has unsupported settings/school values.");
        }
        foreach (DaggerfallSpellEffectDefinition effect in spells.Values.SelectMany(spell => spell.Effects))
            if (!costs.ContainsKey((effect.Type, effect.SubType)))
                diagnostics.Add($"Published spell effect '{effect.Key}' has no normalized cost row for ({effect.Type}, {effect.SubType}).");

        if (spells.Count == 0)
        {
            diagnostics.Add("The published magic catalog carries no spell, so nothing resolves through it.");
        }

        Dictionary<string, DaggerfallEnchantmentSetting> settings = new(StringComparer.Ordinal);
        foreach (JsonElement row in Array(section, "enchantmentSettings", diagnostics))
        {
            string key = Text(row, "key", diagnostics);
            List<int> variants = [];
            foreach (JsonElement variant in Array(row, "parameterVariants", diagnostics))
                variants.Add(variant.GetInt32());
            DaggerfallEnchantmentSetting setting = new(key, Integer(row, "type", diagnostics),
                Integer(row, "param", diagnostics), Integer(row, "cost", diagnostics),
                Text(row, "meaning", diagnostics), Text(row, "displayName", diagnostics),
                Text(row, "textKey", diagnostics),
                row.TryGetProperty("parameterTextKey", out JsonElement textKey) && textKey.ValueKind == JsonValueKind.String ? textKey.GetString() : null,
                variants.AsReadOnly(),
                row.TryGetProperty("spell", out var spellLink) && spellLink.ValueKind == JsonValueKind.String ? spellLink.GetString() : null,
                row.TryGetProperty("spellIdentityShared", out var sharedLink) && sharedLink.GetBoolean(),
                row.TryGetProperty("forcedSettings", out var forced) ? forced.EnumerateArray().Select(value => value.GetString()!).ToArray() : []);
            if (setting.SpellKey is { } linkedKey && (!spells.TryGetValue(linkedKey, out var linkedSpell) || linkedSpell.Identity != setting.Param))
                diagnostics.Add($"Enchantment setting '{key}' has an invalid spell identity link.");
            if (!settings.TryAdd(key, setting))
                diagnostics.Add($"Enchantment setting '{key}' is published twice, so a consumer cannot resolve it to one setting.");
        }
        Dictionary<int, DaggerfallPotionRecipeDefinition> recipes = [];
        foreach (JsonElement row in Array(section, "potionRecipes", diagnostics))
        {
            DaggerfallPotionSpellPointRestore? restore = row.TryGetProperty("spellPointRestore", out var special) && special.ValueKind == JsonValueKind.Object
                ? new(Text(special, "effect", diagnostics), Integer(special, "baseLow", diagnostics), Integer(special, "baseHigh", diagnostics),
                    Integer(special, "levelBase", diagnostics), Integer(special, "levelHigh", diagnostics), Integer(special, "perLevel", diagnostics)) : null;
            var recipe = new DaggerfallPotionRecipeDefinition(Integer(row, "key", diagnostics), Integer(row, "classicIndex", diagnostics),
                Text(row, "name", diagnostics), Text(row, "textKey", diagnostics), Integer(row, "price", diagnostics),
                Integer(row, "textureRecord", diagnostics),
                [.. Array(row, "ingredients", diagnostics).Select(value => new DaggerfallPotionIngredient(Integer(value, "template", diagnostics), Text(value, "item", diagnostics), Integer(value, "count", diagnostics)))],
                [.. Array(row, "effects", diagnostics).Select(value => ReadSpellEffect(value, diagnostics))], restore);
            if (!recipes.TryAdd(recipe.Key, recipe)) diagnostics.Add($"Potion recipe identity {recipe.Key} is duplicated.");
        }
        if (recipes.Values.Select(value => value.ClassicIndex).Distinct().Count() != recipes.Count)
            diagnostics.Add("Potion recipes repeat a classic selection index.");
        return new DaggerfallMagicCatalogSet(spells, items, costs, settings) { PotionRecipes = recipes };
    }

    private static DaggerfallSpellEffectDefinition ReadSpellEffect(JsonElement effect, DaggerfallContentDiagnostics diagnostics)
    {
        JsonElement duration = Object(Property(effect, "duration", diagnostics), "duration", diagnostics);
        JsonElement chance = Object(Property(effect, "chance", diagnostics), "chance", diagnostics);
        JsonElement magnitude = Object(Property(effect, "magnitude", diagnostics), "magnitude", diagnostics);
        return new DaggerfallSpellEffectDefinition(
                    Text(effect, "key", diagnostics),
                    Integer(effect, "type", diagnostics),
                    Integer(effect, "subType", diagnostics),
                    Integer(duration, "base", diagnostics), Integer(duration, "mod", diagnostics), Integer(duration, "perLevel", diagnostics),
                    Integer(chance, "base", diagnostics), Integer(chance, "mod", diagnostics), Integer(chance, "perLevel", diagnostics),
                    Integer(magnitude, "baseLow", diagnostics), Integer(magnitude, "baseHigh", diagnostics),
                    Integer(magnitude, "levelBase", diagnostics), Integer(magnitude, "levelHigh", diagnostics), Integer(magnitude, "perLevel", diagnostics));
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
            if (maximum < 1) diagnostics.Add($"Item '{id.Value}' maximumQuantity must be positive.");
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
        if (classifications.Length < 1 || classifications.Any(classification => !ValidId(classification)) || classifications.Distinct(StringComparer.Ordinal).Count() != classifications.Length)
            diagnostics.Add("Equipment classifications must be distinct stable identifiers.");
        if (requiredSlots < 1 || requiredSlots > ushort.MaxValue) diagnostics.Add($"Equipment requiredSlots must be between 1 and {ushort.MaxValue}.");
        if (exclusiveGroup is not null && !ValidId(exclusiveGroup)) diagnostics.Add("Equipment exclusiveGroup must be a stable identifier when present.");
        return new(System.Array.AsReadOnly(classifications), requiredSlots > 0 && requiredSlots <= ushort.MaxValue ? checked((ushort)requiredSlots) : (ushort)1, exclusiveGroup);
    }

    private static Dictionary<DaggerfallEquipmentSlotId, DaggerfallEquipmentSlotDefinition> ReadEquipmentSlots(JsonElement root, DaggerfallContentDiagnostics diagnostics)
    {
        Dictionary<DaggerfallEquipmentSlotId, DaggerfallEquipmentSlotDefinition> slots = [];
        foreach (JsonElement value in Array(root, "equipmentSlots", diagnostics))
        {
            JsonElement slot = Object(value, "equipment slot", diagnostics);
            DaggerfallEquipmentSlotId id = new(Text(slot, "id", diagnostics));
            string[] classifications = Array(slot, "allowedClassifications", diagnostics).Select(entry => entry.ValueKind == JsonValueKind.String ? entry.GetString() ?? string.Empty : InvalidClassification(diagnostics)).ToArray();
            if (!ValidId(id.Value) || classifications.Any(classification => !ValidId(classification)) || classifications.Distinct(StringComparer.Ordinal).Count() != classifications.Length)
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

    private static IReadOnlyDictionary<string, string> ReadQuestJournalTitles(JsonElement root, DaggerfallContentDiagnostics diagnostics)
    {
        Dictionary<string, string> titles = new(StringComparer.Ordinal);
        JsonElement section = Object(Property(root, "questJournalTitles", diagnostics), "questJournalTitles", diagnostics);
        if (section.ValueKind != JsonValueKind.Object) return new ReadOnlyDictionary<string, string>(titles);
        foreach (JsonProperty entry in section.EnumerateObject())
        {
            if (entry.Value.ValueKind != JsonValueKind.String || entry.Value.GetString() is not { } title || string.IsNullOrWhiteSpace(title))
                diagnostics.Add($"Quest journal title for '{entry.Name}' must be non-empty text.");
            else titles.Add(entry.Name, title.Trim());
        }
        return new ReadOnlyDictionary<string, string>(titles);
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

    private static void ValidateReferences(DaggerfallVocabulary vocabulary, IReadOnlyDictionary<DaggerfallActorId, DaggerfallActorDefinition> actors, IReadOnlyDictionary<DaggerfallItemId, DaggerfallItemDefinition> items, IReadOnlyDictionary<DaggerfallEquipmentSlotId, DaggerfallEquipmentSlotDefinition> equipmentSlots, IReadOnlyDictionary<string, int> armorValues, IReadOnlyDictionary<string, DaggerfallActionDefinition> actions, IReadOnlyDictionary<string, DaggerfallLootTableDefinition> lootTables, DaggerfallCatalogSet catalogs, DaggerfallMobileCatalogSet mobiles, IReadOnlyList<DaggerfallHudResourceDefinition> hud, DaggerfallContentDiagnostics diagnostics)
    {
        // Classic class mobiles are numbered from the first class mobile by career record, so the class range
        // is as long as the published career list; every other mobile is a monster below it.
        int classMobileEnd = DaggerfallEncounterActors.FirstClassMobile + catalogs.Careers.Count;
        foreach (DaggerfallActorDefinition actor in actors.Values)
        {

            if (actor.Kind is not ("player" or "monster" or "enemy-class")) diagnostics.Add($"Actor '{actor.Id.Value}' has unsupported kind '{actor.Kind}'.");
            if (actor.Kind == "player" && (actor.Id.Value != "player" || actor.MobileId is not null)) diagnostics.Add("Only actor 'player' may have kind player and it cannot have a mobile id.");
            if (actor.Kind == "enemy-class" && (actor.MobileId is not int classMobile || classMobile < DaggerfallEncounterActors.FirstClassMobile || classMobile >= classMobileEnd || actor.HitPointsPerLevel is null || actor.Career is null)) diagnostics.Add($"Enemy class '{actor.Id.Value}' must name one classic class mobile, career, and health progression.");
            if (actor.Kind == "monster" && (actor.MobileId is null || actor.MobileId is < 0 || actor.MobileId >= DaggerfallEncounterActors.FirstClassMobile)) diagnostics.Add($"Monster '{actor.Id.Value}' has an unsupported mobile id.");
            if (actor.Level is < 1 or > 100 || actor.Weight is < 0 or > 100_000 || actor.Armor is < -MaximumAuthoredArmor or > MaximumAuthoredArmor || actor.Rewards.ExperienceReward is < 0 or > 1_000_000) diagnostics.Add($"Actor '{actor.Id.Value}' has an out-of-range level, weight, armor, or xp reward.");
            if (actor.Team is { } team && !ValidId(team)) diagnostics.Add($"Actor '{actor.Id.Value}' has an invalid team.");
            if (actor.MinimumMaterial is { } material && !DaggerfallItemMaterials.IsWeaponMaterial(material)) diagnostics.Add($"Actor '{actor.Id.Value}' refers to unknown minimum weapon material '{material}'.");
            if (actor.LootTableKey is { } lootTable && !lootTables.ContainsKey(lootTable)) diagnostics.Add($"Actor '{actor.Id.Value}' refers to missing loot table '{lootTable}'.");
            if (actor.ActionId is { } actionId && !actions.ContainsKey(actionId)) diagnostics.Add($"Actor '{actor.Id.Value}' refers to missing action '{actionId}'.");
            if (actor.Attacks.Any(range => range.MinimumDamage < 0 || range.MaximumDamage < range.MinimumDamage || range.MaximumDamage > MaximumAuthoredDamage)) diagnostics.Add($"Actor '{actor.Id.Value}' has an invalid attack range.");
            if (actor.HitPointsPerLevel is < 1 or > 100) diagnostics.Add($"Actor '{actor.Id.Value}' hitPointsPerLevel is outside the retained progression metadata range.");
            if (actor.Kind == "player" && actor.HitPointsPerLevel is null) diagnostics.Add("Player must retain hitPointsPerLevel metadata for the later progression/formula task.");
            if (actor.Kind == "monster" && actor.Attacks.Count == 0) diagnostics.Add($"Monster '{actor.Id.Value}' must declare at least one attack range.");
            if (actor.ActionId is { } associatedActionId && actions.TryGetValue(associatedActionId, out DaggerfallActionDefinition? action))
            {
                if (actor.Kind == "player" && action.Interpretation != "player-equipped-melee") diagnostics.Add("Player action association must use player-equipped melee.");
                if (actor.Kind != "player" && action.Interpretation is not ("fixed-melee" or "fixed-ranged" or "enemy-equipped-melee" or "enemy-equipped-ranged")) diagnostics.Add($"Actor '{actor.Id.Value}' action association is incompatible with its authored attacks.");
                if (action.AttackRangeIndex is int index && (index < 0 || index >= actor.Attacks.Count)) diagnostics.Add($"Action '{action.Id}' references missing attack range {index} on actor '{actor.Id.Value}'.");
                if (action.AttackRangeIndex is null && action.MinimumDamage is null && actor.Kind != "player" && action.Interpretation is not ("enemy-equipped-melee" or "enemy-equipped-ranged")) diagnostics.Add($"Fixed action '{action.Id}' needs either an actor attack range reference or direct damage.");
            }
            ValidateLoadout(actor, items, equipmentSlots, diagnostics);
        }
        foreach (DaggerfallMobileDefinition mobile in mobiles.Mobiles.Values)
        {
            if (mobile.LootTableKey is { } lootTable && !lootTables.ContainsKey(lootTable))
                diagnostics.Add($"Mobile '{mobile.DonorId}' refers to missing loot table '{lootTable}'.");
        }
        HashSet<int> monsterMobileIds = [];
        foreach (DaggerfallActorDefinition actor in actors.Values.Where(actor => actor.Kind == "monster"))
            if (actor.MobileId is int mobile && !monsterMobileIds.Add(mobile)) diagnostics.Add($"Mobile '{mobile}' is assigned by more than one monster.");
        foreach (DaggerfallHudResourceDefinition resource in hud)
            if (!ValidId(resource.Id) || string.IsNullOrWhiteSpace(resource.Label) || !vocabulary.Tracks.Contains(resource.Track) || resource.Track != DaggerfallMechanicsIds.Health && resource.Track != DaggerfallMechanicsIds.Fatigue && resource.Track != DaggerfallMechanicsIds.Stamina && resource.Track != DaggerfallMechanicsIds.Magicka) diagnostics.Add($"HUD resource '{resource.Id}' refers to an unsupported track or is malformed.");
        foreach (DaggerfallItemDefinition item in items.Values)
        {
            if (item.Weapon is not null && (!DaggerfallItemMaterials.IsWeaponMaterial(item.Weapon.Material) || !vocabulary.Skills.Any(skill => skill.Value == item.Weapon.Skill))) diagnostics.Add($"Weapon '{item.Id.Value}' refers to an unknown classic weapon material or skill.");
            if (item.Armor is not null && (!armorValues.ContainsKey(item.Armor.Material) || !vocabulary.ArmorParts.Contains(item.Armor.Part))) diagnostics.Add($"Armor '{item.Id.Value}' refers to an unknown material or armor part.");
            if (item.Shield is { Armor: < 0 or > MaximumAuthoredArmor }) diagnostics.Add($"Shield '{item.Id.Value}' armor is outside the Daggerfall policy range.");
            if (item.Weapon is not null && item.Armor is not null || item.Weapon is not null && item.Shield is not null || item.Armor is not null && item.Shield is not null) diagnostics.Add($"Item '{item.Id.Value}' cannot be weapon, armor, and shield simultaneously.");
            if (item.Equipment is not null && !equipmentSlots.Values.Any(slot => slot.AllowedClassifications.Any(item.Equipment.Classifications.Contains))) diagnostics.Add($"Equipable item '{item.Id.Value}' has no compatible equipment slot.");
        }
        foreach (DaggerfallActionDefinition action in actions.Values)
        {
            double? reach = action.Reach;
            if (action.Skill != "equipped" && !vocabulary.Skills.Any(skill => skill.Value == action.Skill)) diagnostics.Add($"Action '{action.Id}' refers to unknown skill '{action.Skill}'.");
            if (action.Tags.Distinct(StringComparer.Ordinal).Count() != action.Tags.Count) diagnostics.Add($"Action '{action.Id}' repeats a tag.");
            // A bow's cadence is the donor's formula over live speed, not an authored number, so exactly
            // one owner states it per interpretation rather than both agreeing by luck.
            if (action.Interpretation != "player-equipped-ranged" && (action.CooldownSeconds is not double cooldown || cooldown <= 0d)) diagnostics.Add($"Action '{action.Id}' must define a positive cooldown.");
            if (action.Interpretation == "player-equipped-ranged" && action.CooldownSeconds is not null) diagnostics.Add($"Action '{action.Id}' takes its cooldown from FORM-04.GetBowCooldownTime and must not author one.");
            bool directRange = action.MinimumDamage is not null || action.MaximumDamage is not null;
            bool fixedReach = action.Interpretation is "fixed-melee" or "fixed-ranged";
            if (fixedReach && (action.AttackRangeIndex is not null) == directRange) diagnostics.Add($"Fixed action '{action.Id}' must use exactly one direct damage range or actor attackRangeIndex.");
            if (action.Interpretation is "player-equipped-melee" or "player-equipped-ranged" && (action.AttackRangeIndex is not null || directRange || action.StaminaCost is not > 0)) diagnostics.Add($"Player-equipped action '{action.Id}' must own a positive stamina cost and use equipped weapon damage.");
            if ((fixedReach || action.Interpretation is "enemy-equipped-melee" or "enemy-equipped-ranged") && action.StaminaCost is not null) diagnostics.Add($"Fixed action '{action.Id}' must not declare player stamina cost.");
            // How far an attack carries is the attack's own property. A fixed action without a reach would
            // have to borrow one from somewhere else, which is how a bow and a dagger ended up reaching the
            // same distance.
            // Every action that swings carries its own reach, the player's included: the melee targeting
            // bound takes the player action's reach as the query's own distance limit and refuses a
            // non-positive one, so an action authoring 0 here loads and then throws on every swing.
            if (reach is not > 0d) diagnostics.Add($"Action '{action.Id}' must declare the positive reach it carries to.");
            if (action.Interpretation is "fixed-melee" or "enemy-equipped-melee" && reach > MaximumMeleeReach) diagnostics.Add($"Melee action '{action.Id}' reaches {reach} beyond any melee distance '{MaximumMeleeReach}'.");
            // A guardrail against authoring nonsense rather than a live disagreement: nothing re-queries
            // at impact time, so the behaviour gate is the admission, and a reach past what an attacker
            // perceives would mean a shot that only ever fires if something else already put the player
            // there.
            if (action.Interpretation is "fixed-ranged" or "player-equipped-ranged" or "enemy-equipped-ranged" && reach > MaximumRangedReach) diagnostics.Add($"Ranged action '{action.Id}' reaches {reach} beyond the admitted classic ranged maximum '{MaximumRangedReach}'.");
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
        if (attributes.Count != 9 || skills.Count != 35 || tracks.Count != 4 || armorParts.Count != 7 || progression.Count != 2) diagnostics.Add("Daggerfall vocabulary has an unexpected cardinality.");
        if (attributes.Concat(skills).Select(id => id.Value).Distinct(StringComparer.Ordinal).Count() != attributes.Count + skills.Count) diagnostics.Add("Daggerfall stat and skill ids must be unique.");
        return new(attributes, skills, tracks, armorParts, progression);
    }

    /// <summary>
    /// Reads the imported catalogs and joins them to the authored vocabulary here, not in the import: the
    /// import publishes the classic attribute and skill indices, and the vocabulary names them in index order,
    /// so an authored edit to the vocabulary reaches the game without making the imported data stale.
    /// </summary>
    private static DaggerfallCatalogSet ReadCatalogs(JsonElement root, DaggerfallVocabulary vocabulary, DaggerfallContentDiagnostics diagnostics)
    {
        JsonElement value = Object(Property(root, "catalogs", diagnostics), "catalogs", diagnostics);
        // A career names its attributes and skills by classic index: the vocabulary's first eight attributes, in
        // order, and every skill it names.
        if (vocabulary.Attributes.Count < ClassicAttributeValueCount)
            diagnostics.Add($"The vocabulary names {vocabulary.Attributes.Count} attributes where a career's classic values need {ClassicAttributeValueCount}.");
        IReadOnlyList<DaggerfallCatalogKey> attributes = [.. vocabulary.Attributes.Take(ClassicAttributeValueCount).Select((id, index) => new DaggerfallCatalogKey(id.Value, index))];
        IReadOnlyList<DaggerfallCatalogKey> skills = [.. vocabulary.Skills.Select((id, index) => new DaggerfallCatalogKey(id.Value, index))];
        IReadOnlyList<DaggerfallCatalogKey> resistances = ReadCatalogKeys(value, "resistances", diagnostics);
        string[] attributeKeys = [.. attributes.Select(key => key.Id)];
        string[] elementKeys = [.. resistances.Select(key => key.Id)];

        List<DaggerfallRaceDefinition> races = [];
        foreach (JsonElement race in Array(value, "races", diagnostics))
        {
            DaggerfallRaceDefinition definition = new(
                Text(race, "id", diagnostics),
                Integer(race, "donorRaceId", diagnostics))
            {
                ResistanceFlags = Integer(race, "resistanceFlags", diagnostics), ImmunityFlags = Integer(race, "immunityFlags", diagnostics),
                LowToleranceFlags = Integer(race, "lowToleranceFlags", diagnostics), CriticalWeaknessFlags = Integer(race, "criticalWeaknessFlags", diagnostics),
                Townspeople = race.TryGetProperty("townspeople", out JsonElement townspeople)
                    ? new Dictionary<string, IReadOnlyList<int>>(StringComparer.Ordinal)
                    {
                        ["Male"] = [.. Array(townspeople, "male", diagnostics).Select(archive => archive.GetInt32())],
                        ["Female"] = [.. Array(townspeople, "female", diagnostics).Select(archive => archive.GetInt32())],
                    }
                    : new Dictionary<string, IReadOnlyList<int>>(),
            };
            if (((definition.ResistanceFlags | definition.ImmunityFlags | definition.LowToleranceFlags | definition.CriticalWeaknessFlags) & ~127) != 0)
                diagnostics.Add($"Race '{definition.Id}' carries unknown effect flag bits.");
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
            IReadOnlyList<string> primary = ReadSkillIndices(career, "primarySkills", id, skills, diagnostics);
            IReadOnlyList<string> major = ReadSkillIndices(career, "majorSkills", id, skills, diagnostics);
            IReadOnlyList<string> minor = ReadSkillIndices(career, "minorSkills", id, skills, diagnostics);
            IReadOnlyList<string> careerAttributes = attributeKeys;
            IReadOnlyList<int> attributeValues = Array(career, "attributeValues", diagnostics).Select(value =>
            {
                if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int parsed)) return parsed;
                diagnostics.Add("'attributeValues' must contain integers.");
                return 0;
            }).ToArray();
            IReadOnlyList<string> resistant = ReadIds(career, "resistanceElements", diagnostics);
            IReadOnlyList<string> immune = ReadIds(career, "immunityElements", diagnostics);
            int hitPoints = Integer(career, "hitPointsPerLevel", diagnostics);
            int spellPointMultiplierMilli = Integer(career, "spellPointMultiplierMilli", diagnostics);
            float multiplier = Number(career, "advancementMultiplier", diagnostics);
            int resistanceFlags = FlagByte(career, "resistanceFlags", diagnostics);
            int immunityFlags = FlagByte(career, "immunityFlags", diagnostics);
            int lowToleranceFlags = FlagByte(career, "lowToleranceFlags", diagnostics);
            int criticalWeaknessFlags = FlagByte(career, "criticalWeaknessFlags", diagnostics);
            int attackModifierFlags = FlagByte(career, "attackModifierFlags", diagnostics);
            DaggerfallCareerSpecials specials = new(
                FlagByte(career, "specialAbilityFlags", diagnostics), FlagByte(career, "darknessPoweredMagery", diagnostics),
                FlagByte(career, "lightPoweredMagery", diagnostics), FlagByte(career, "rapidHealingFlags", diagnostics),
                FlagByte(career, "regenerationFlags", diagnostics), FlagByte(career, "spellAbsorptionFlags", diagnostics));
            // A value no consumer interprets would be an ability the career claims and never grants.
            if (!specials.IsClassic && specials.Fields.All(field => field.Value != InvalidFlagByte))
                diagnostics.Add($"Career '{id}' carries special-ability values [{string.Join(", ", specials.Fields.Select(field => $"{field.Name}={field.Value}"))}] outside the classic bits and single conditions.");
            IReadOnlyList<string> expertProficiencies = ReadSkillIndices(career, "expertProficiencies", id, skills, diagnostics);
            IReadOnlyList<string> forbiddenEquipment = ReadIds(career, "forbiddenEquipment", diagnostics);
            DaggerfallCareerDefinition definition = new(
                id, name, primary, major, minor, careerAttributes, attributeValues, hitPoints, spellPointMultiplierMilli, multiplier, resistant, immune,
                resistanceFlags, immunityFlags, lowToleranceFlags, criticalWeaknessFlags, attackModifierFlags, specials, expertProficiencies, forbiddenEquipment);
            foreach (string element in resistant.Concat(immune))
            {
                if (!elementKeys.Contains(element, StringComparer.Ordinal))
                {
                    diagnostics.Add($"Career '{id}' names element '{element}', which the catalog does not carry.");
                }
            }

            if (string.IsNullOrWhiteSpace(name) || hitPoints <= 0
                || spellPointMultiplierMilli is not (500 or 1000 or 1500 or 1750 or 2000 or 3000)
                || !(multiplier > 0f))
            {
                diagnostics.Add($"Career '{id}' must carry a name, positive hit points per level, one classic spell-point multiplier and a positive advancement multiplier.");
            }

            // The published element lists must be what the carrier's flag bytes say. A
            // list that disagrees with its bytes is content a consumer would act on
            // while the source says otherwise.
            RequireElements(id, "resistanceElements", resistant, resistanceFlags, diagnostics);
            RequireElements(id, "immunityElements", immune, immunityFlags, diagnostics);
            if (forbiddenEquipment.Distinct(StringComparer.Ordinal).Count() != forbiddenEquipment.Count
                || forbiddenEquipment.Any(restriction => !ValidEquipmentRestriction(restriction)))
            {
                diagnostics.Add($"Career '{id}' must carry distinct supported forbidden-equipment restrictions.");
            }
            if (primary.Count is < 1 or > 3 || major.Count is < 1 or > 3 || minor.Count is < 1 or > 6)
            {
                diagnostics.Add($"Career '{id}' must name one to three primary, one to three major and one to six minor skills.");
            }

            if (careerAttributes.Count != ClassicAttributeValueCount)
            {
                diagnostics.Add($"Career '{id}' must carry {ClassicAttributeValueCount} attribute keys.");
            }
            if (attributeValues.Count != ClassicAttributeValueCount || attributeValues.Any(value => value < 0))
            {
                diagnostics.Add($"Career '{id}' must carry {ClassicAttributeValueCount} non-negative attribute values.");
            }

            string[] trained = [.. definition.SkillReferences];
            if (trained.Distinct(StringComparer.Ordinal).Count() != trained.Length)
            {
                diagnostics.Add($"Career '{id}' must name each skill once across its primary, major and minor lists.");
            }

            careers.Add(definition);
        }

        RequireDistinct(careers, career => career.Id, "career", diagnostics);

        DaggerfallClassQuestionnaire? questionnaire = null;
        if (value.TryGetProperty("classQuestionnaire", out JsonElement questionsValue) && questionsValue.ValueKind != JsonValueKind.Null)
        {
            var questions = Array(questionsValue, "questions", diagnostics).Select(question => new DaggerfallClassQuestion(
                Integer(question, "number", diagnostics), Text(question, "text", diagnostics),
                Array(question, "answers", diagnostics).Select(answer => new DaggerfallClassAnswer(Text(answer, "text", diagnostics), Integer(answer, "archetype", diagnostics))).ToArray())).ToArray();
            var recommendations = Array(questionsValue, "recommendations", diagnostics).Select(row => new DaggerfallClassRecommendation(
                Integer(row, "warrior", diagnostics), Integer(row, "rogue", diagnostics), Integer(row, "mage", diagnostics), Text(row, "careerId", diagnostics))).ToArray();
            if (!questions.Select(question => question.Number).SequenceEqual(Enumerable.Range(1, 40))
                || questions.Any(question => string.IsNullOrWhiteSpace(question.Text) || question.Answers.Count != 3
                    || !question.Answers.Select(answer => answer.Archetype).Order().SequenceEqual(new[] { 0, 1, 2 })
                    || question.Answers.Any(answer => string.IsNullOrWhiteSpace(answer.Text))))
                diagnostics.Add("Class questionnaire must carry forty ordered questions with three classified answers each.");
            if (recommendations.Length != 66 || recommendations.Select(row => (row.Warrior, row.Rogue, row.Mage)).Distinct().Count() != 66
                || recommendations.Any(row => row.Warrior < 0 || row.Rogue < 0 || row.Mage < 0
                || row.Warrior + row.Rogue + row.Mage != 10 || !careers.Any(career => career.Id == row.CareerId)))
                diagnostics.Add("Class questionnaire recommendations must resolve all ten-answer results to published careers.");
            questionnaire = new(questions, recommendations);
        }
        return new DaggerfallCatalogSet(attributes, skills, resistances, races, careers, questionnaire);
    }

    private static bool ValidEquipmentRestriction(string value)
    {
        string[] pair = value.Split(':', 2, StringSplitOptions.TrimEntries);
        return pair.Length == 2 && (pair[0], pair[1]) switch
        {
            ("forbidden-material", "iron" or "steel" or "silver" or "elven" or "dwarven" or "mithril" or "adamantium" or "ebony" or "orcish" or "daedric") => true,
            ("forbidden-shield", "buckler" or "round-shield" or "kite-shield" or "tower-shield") => true,
            ("forbidden-armor", "leather" or "chain" or "plate") => true,
            ("forbidden-weapon", "short-blade" or "long-blade" or "hand-to-hand" or "axe" or "blunt-weapon" or "archery") => true,
            _ => false,
        };
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

    /// <summary>One indexed key catalog: distinct ids, distinct contiguous indices.</summary>
    private static IReadOnlyList<DaggerfallCatalogKey> ReadCatalogKeys(JsonElement value, string property, DaggerfallContentDiagnostics diagnostics)
    {
        List<DaggerfallCatalogKey> keys = [];
        foreach (JsonElement entry in Array(value, property, diagnostics))
        {
            keys.Add(new DaggerfallCatalogKey(Text(entry, "id", diagnostics), Integer(entry, "index", diagnostics)));
        }

        if (keys.Select(key => key.Id).Distinct(StringComparer.Ordinal).Count() != keys.Count
            || keys.Select(key => key.Index).Distinct().Count() != keys.Count
            || !keys.Select(key => key.Index).Order().SequenceEqual(Enumerable.Range(0, keys.Count)))
        {
            diagnostics.Add($"'{property}' must carry distinct ids with contiguous indices from zero.");
        }

        return System.Array.AsReadOnly(keys.ToArray());
    }

    /// <summary>A career's skills, published as classic skill indices and named through the catalog's skill keys.</summary>
    private static IReadOnlyList<string> ReadSkillIndices(JsonElement career, string property, string careerId, IReadOnlyList<DaggerfallCatalogKey> skills,
        DaggerfallContentDiagnostics diagnostics)
    {
        List<string> names = [];
        foreach (JsonElement entry in Array(career, property, diagnostics))
        {
            if (entry.ValueKind == JsonValueKind.Number && entry.TryGetInt32(out int index) && index >= 0 && index < skills.Count)
                names.Add(skills[index].Id);
            else
                diagnostics.Add($"Career '{careerId}' names skill {entry} in '{property}', which is not an index of the catalog's classic skills.");
        }

        if (names.Distinct(StringComparer.Ordinal).Count() != names.Count) diagnostics.Add($"Career '{careerId}' names a skill twice in '{property}'.");
        return System.Array.AsReadOnly(names.ToArray());
    }

    private static void RequireDistinct<T>(IReadOnlyList<T> values, Func<T, string> key, string kind, DaggerfallContentDiagnostics diagnostics)
    {
        if (values.Select(key).Distinct(StringComparer.Ordinal).Count() != values.Count)
        {
            diagnostics.Add($"'{kind}' entries must carry distinct {kind} identities.");
        }
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
            bool validDamageShape = interpretation is "player-equipped-melee" or "enemy-equipped-melee" or "enemy-equipped-ranged" or "player-equipped-ranged"
                ? attackRangeIndex is null && !directRange
                : (attackRangeIndex is not null) != directRange;
            bool valid = ValidId(id)
                && interpretation is "player-equipped-melee" or "player-equipped-ranged" or "fixed-melee" or "fixed-ranged" or "enemy-equipped-melee" or "enemy-equipped-ranged"
                && tags.Length > 0 && tags.All(ValidId)
                && (skill == "equipped" || ValidId(skill))
                && reach is null or >= 0 and <= 100
                // A bow's cadence is FORM-04.GetBowCooldownTime over live speed, so the ranged player
                // action must not carry an authored cooldown the admission policy would ignore.
                && (interpretation == "player-equipped-ranged" ? cooldown is null : cooldown is > 0 and <= 60)
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
        string[] categories = LootCategories;
        Dictionary<string, DaggerfallLootTableDefinition> result = new(StringComparer.Ordinal);
        foreach (JsonElement entry in Array(root, "lootTables", diagnostics)) { JsonElement table = Object(entry, "loot table", diagnostics); string key = Text(table, "key", diagnostics); JsonElement gold = Object(Property(table, "gold", diagnostics), "loot gold", diagnostics); int minimum = Integer(gold, "minimum", diagnostics), maximum = Integer(gold, "maximum", diagnostics); Dictionary<string, int> values = ReadIntegerMap(table, "categories", diagnostics); if (key.Length != 1 || (key != "-" && (key[0] < 'A' || key[0] > 'U')) || minimum < 0 || maximum < minimum || maximum > MaximumAuthoredLootGold || values.Keys.Any(id => !categories.Contains(id)) || values.Values.Any(value => value is < 0 or > 100) || !result.TryAdd(key, new(key, minimum, maximum, new ReadOnlyDictionary<string, int>(values)))) diagnostics.Add($"Loot table '{key}' is invalid or duplicated."); }
        return result;
    }

    /// <summary>The donor's loot categories: the keys a loot table's category chances may name.</summary>
    private static readonly string[] LootCategories = ["plant1", "plant2", "creature1", "creature2", "creature3", "misc1", "misc2", "armor", "weapons", "magic", "clothing", "books", "religious"];

    /// <summary>
    /// The catalog's structural invariants. How many actors, items, slots, actions or loot tables a pack
    /// carries, and which ids it authors, are content; what is checked here is only what the compiled
    /// ruleset reaches for by identity, so a pack that lacks it would fail at the first use instead.
    /// </summary>
    private static void ValidateCatalog(IReadOnlyDictionary<DaggerfallActorId, DaggerfallActorDefinition> actors, IReadOnlyDictionary<string, int> armorValues, IReadOnlyDictionary<string, DaggerfallActionDefinition> actions, IReadOnlyDictionary<string, DaggerfallLootTableDefinition> loot, DaggerfallContentDiagnostics diagnostics)
    {
        if (armorValues.Values.Any(value => value > MaximumAuthoredArmor)) diagnostics.Add("Armor values by material exceed the Daggerfall policy bound.");
        // The session composes the player from this actor, so a pack without it has no one to play.
        if (!actors.ContainsKey(new(DaggerfallActorKinds.Player))) diagnostics.Add("Base payload is missing the 'player' actor the session composes the player from.");
        // Every classic class encounter the ruleset materializes fights through this action.
        if (!actions.TryGetValue(DaggerfallEncounterActors.ClassEquippedMeleeAction, out DaggerfallActionDefinition? classMelee) || classMelee.Interpretation != "enemy-equipped-melee")
            diagnostics.Add($"Base payload must author '{DaggerfallEncounterActors.ClassEquippedMeleeAction}' as enemy-equipped melee; classic class encounters fight through it.");
        // Dungeon treasure resolves through the ruleset's dungeon-type map, which names these tables.
        foreach (string key in DaggerfallDungeonLootTables.DungeonTableKeysInUse.Where(key => !loot.ContainsKey(key)))
            diagnostics.Add($"Base payload is missing loot table '{key}', which dungeon treasure selects.");
    }

    internal static JsonElement Property(JsonElement value, string property, DaggerfallContentDiagnostics diagnostics)
    {
        if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty(property, out JsonElement result)) return result;
        diagnostics.Add($"Required property '{property}' is missing.");
        return default;
    }
    /// <summary>The published catalog shape this reader understands.</summary>
    /// <summary>
    /// The furthest a melee action may reach, which is the tunable melee query bound's own ceiling.
    /// </summary>
    /// <remarks>
    /// A melee swing and the query that admits it have to agree: an action reaching past what the query
    /// resolves would be an attack the ruleset admits and the Engine then reports as out of range, which
    /// reads to a player as a miss rather than as the authoring mistake it is.
    /// </remarks>
    private const double MaximumMeleeReach = 2.25d;

    /// <summary>
    /// EnemyAttack's maximum ranged distance (2048 classic units). Enemy sight extends beyond it.
    /// </summary>
    private const double MaximumRangedReach = 51.2d;

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
    internal static long Long(JsonElement value, string property, DaggerfallContentDiagnostics diagnostics)
    {
        JsonElement result = Property(value, property, diagnostics);
        if (result.ValueKind == JsonValueKind.Number && result.TryGetInt64(out long integer)) return integer;
        diagnostics.Add($"'{property}' must be an integer.");
        return 0;
    }

    /// <summary>
    /// Reads a property that may legitimately be absent or empty. A donor field the mobile's own entry
    /// does not state is empty in the published record, and that is a source fact rather than a defect.
    /// </summary>
    internal static string OptionalText(JsonElement value, string property)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(property, out JsonElement result) || result.ValueKind != JsonValueKind.String) return string.Empty;
        return result.GetString() ?? string.Empty;
    }

    internal static bool Boolean(JsonElement value, string property, DaggerfallContentDiagnostics diagnostics)
    {
        JsonElement result = Property(value, property, diagnostics);
        if (result.ValueKind is JsonValueKind.True or JsonValueKind.False) return result.GetBoolean();
        diagnostics.Add($"'{property}' must be a boolean.");
        return false;
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
    internal bool HasErrors => _values.Count > 0;
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
