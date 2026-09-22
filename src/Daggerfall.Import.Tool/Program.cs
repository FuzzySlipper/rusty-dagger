using Daggerfall.Import.Arena2;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Daggerfall.Import.Normalization;
using Daggerfall.Import.Publication;
using Daggerfall.Import.Normalized;

namespace Daggerfall.Import.Tool;

internal static partial class Program
{
    private const long MaximumIndividualSourceBytes = 128L * 1024L * 1024L;
    private const long MaximumTotalSourceBytes = 512L * 1024L * 1024L;

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length != 0 && args[0].StartsWith("sprite-", StringComparison.Ordinal))
            {
                return RunSpriteCommand(args);
            }

            if (args.Length != 0 && args[0] == "source-manifest")
            {
                return RunSourceManifestCommand(args);
            }

            if (args.Length != 0 && args[0] == "catalogs")
            {
                return RunCatalogCommand(args);
            }

            if (args.Length != 0 && args[0] == "monster-archive")
            {
                return RunMonsterArchiveCommand(args);
            }

            if (args.Length != 0 && args[0] == "quest-sources")
            {
                return RunQuestSourceCommand(args);
            }

            if (args.Length != 0 && args[0] == "item-template-ledger")
            {
                return RunItemTemplateLedgerCommand(args);
            }

            if (args.Length != 0 && args[0] == "texture-leaves")
            {
                return RunTextureLeafCommand(args);
            }

            if (args.Length != 0 && args[0] == "residual-paths")
            {
                return RunResidualPathCommand(args);
            }

            if (args.Length != 0 && args[0] == "character-presentation")
            {
                return RunCharacterPresentationCommand(args);
            }

            if (args.Length != 0 && args[0] == "classic-media")
            {
                return RunClassicMediaCommand(args);
            }

            if (args.Length != 0 && args[0] == "mobile-ledger")
            {
                return RunMobileLedgerCommand(args);
            }

            if (args.Length != 0 && args[0] == "magic-catalog")
            {
                return RunMagicCatalogCommand(args);
            }

            if (args.Length != 0 && args[0] == "mobile-catalog")
            {
                return RunMobileCatalogCommand(args);
            }

            if (args.Length != 0 && args[0] == "locations")
            {
                return RunLocationsCommand(args);
            }

            if (args.Length != 0 && args[0] == "climate")
            {
                return RunClimateCommand(args);
            }

            if (args.Length != 0 && args[0] == "map-art")
            {
                return RunMapArtCommand(args);
            }

            if (args.Length != 0 && args[0] == "factions")
            {
                return RunFactionsCommand(args);
            }

            if (args.Length != 0 && args[0] == "terrain")
            {
                return RunTerrainCommand(args);
            }

            if (args.Length != 0 && args[0] == "items")
            {
                return RunItemsCommand(args);
            }

            if (args.Length != 0 && args[0] == "quests")
            {
                return RunQuestsCommand(args);
            }

            if (args.Length != 0 && args[0] == "cinematic-media") return RunCinematicMediaCommand(args);

            if (args.Length != 0 && args[0] == "videos")
            {
                return RunVideosCommand(args);
            }

            if (args.Length != 0 && args[0] == "geometry")
            {
                return RunGeometryCommand(args);
            }

            if (args.Length != 0 && args[0] == "blocks")
            {
                return RunBlocksCommand(args);
            }

            if (args.Length != 0 && args[0] == "text")
            {
                return RunTextCommand(args);
            }

            if (args.Length != 0 && args[0] == "internal-strings")
            {
                return RunInternalStringsCommand(args);
            }

            if (args.Length != 0 && args[0] == "building-name-inputs")
            {
                return RunBuildingNameInputsCommand(args);
            }

            ToolOptions options = ToolOptions.Parse(args);
            ImportPublicationPlan plan = AttachSourceManifest(BuildPlan(options), options);
            switch (options.Command)
            {
                case ToolCommand.Plan:
                    PrintPlan(plan.Compare(options.OutputDirectory));
                    return 0;
                case ToolCommand.Write:
                    PrintPlan(ImportPublicationWriter.Write(plan, options.OutputDirectory));
                    return 0;
                case ToolCommand.VerifyRealData:
                    VerifyDeterminism(plan, options);
                    return 0;
                default:
                    throw new InvalidOperationException("The import command is not known.");
            }
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException or InvalidDataException or UnauthorizedAccessException or FormatException or System.Text.Json.JsonException)
        {
            Console.Error.WriteLine($"daggerfall-import-tool: {exception.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Enumerates the supplied texture leaves, reports what every documented id holds, and
    /// — when the documented inventory is supplied — checks that the two agree.
    /// </summary>
    private static int RunTextureLeafCommand(IReadOnlyList<string> args)
    {
        bool check = args.Contains("--inventory", StringComparer.Ordinal);
        if (args.Count != (check ? 5 : 3) || args[1] != "--arena2" || (check && args[3] != "--inventory"))
        {
            throw new ArgumentException("usage: daggerfall-import-tool texture-leaves --arena2 SOURCE_DIR [--inventory INVENTORY.csv]");
        }

        string arena2 = args[2];
        List<(int Id, string Path, ReadOnlyMemory<byte> Bytes)> sources = [];
        foreach (string path in Directory.EnumerateFiles(arena2, "TEXTURE.*"))
        {
            string name = Path.GetFileName(path);
            if (!int.TryParse(name["TEXTURE.".Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int leafId))
            {
                throw new InvalidOperationException($"'{name}' does not carry a texture leaf number, so it cannot be enumerated as a leaf.");
            }

            sources.Add((leafId, name, File.ReadAllBytes(path)));
        }

        TextureLeafInventory inventory = TextureLeafInventory.Enumerate(sources, Path.GetFileName(Path.TrimEndingDirectorySeparator(arena2)));
        Console.WriteLine($"texture leaves: {inventory.Decoded.Count()} decoded, {inventory.Malformed.Count()} supplied and unreadable, {inventory.NotSupplied.Count()} not supplied, {inventory.Records} records, {inventory.Frames} frames");
        foreach (TextureLeafRecord leaf in inventory.Malformed)
        {
            Console.WriteLine($"  unreadable {leaf.Path}: {leaf.Note}");
        }

        if (!check)
        {
            return 0;
        }

        IReadOnlyList<SourceInventoryRow> rows = SourceManifestBuilder.ReadInventory(File.ReadAllBytes(args[4]));
        HashSet<string> documented = [.. rows
            .Where(row => row.RowType == "file" && StringComparer.Ordinal.Equals(row.FamilyId, "CNT-018"))
            .Select(row => Path.GetFileName(row.PathOrPattern))];
        string[] supplied = [.. inventory.Leaves.Where(leaf => leaf.Path.Length != 0).Select(leaf => leaf.Path)];
        string[] missing = [.. supplied.Where(path => !documented.Contains(path))];
        string[] extra = [.. documented.Where(path => !supplied.Contains(path, StringComparer.Ordinal))];
        if (missing.Length != 0 || extra.Length != 0)
        {
            throw new InvalidOperationException($"The documented inventory and the supplied corpus disagree; supplied but undocumented: [{string.Join(", ", missing)}], documented but not supplied: [{string.Join(", ", extra)}].");
        }

        Console.WriteLine($"inventory: {documented.Count} documented texture leaves match the corpus, {inventory.NotSupplied.Count()} not supplied");
        return 0;
    }

    /// <summary>
    /// Classifies every residual (CNT-027) source path against the documented inventory: which
    /// bounded family it belongs to, what reads it, and what happened to it.
    /// </summary>
    private static int RunResidualPathCommand(IReadOnlyList<string> args)
    {
        if (args.Count != 5 || args[1] != "--arena2" || args[3] != "--inventory")
        {
            throw new ArgumentException("usage: daggerfall-import-tool residual-paths --arena2 SOURCE_DIR --inventory INVENTORY.csv");
        }

        string arena2 = args[2];
        IReadOnlyList<SourceInventoryRow> rows = SourceManifestBuilder.ReadInventory(File.ReadAllBytes(args[4]));
        Dictionary<string, string> documented = [];
        List<(string Path, ReadOnlyMemory<byte> Bytes)> sources = [];
        foreach (SourceInventoryRow row in rows
            .Where(row => row.RowType == "file" && StringComparer.Ordinal.Equals(row.FamilyId, "CNT-027"))
            .OrderBy(row => row.PathOrPattern, StringComparer.Ordinal))
        {
            string name = Path.GetFileName(row.PathOrPattern);
            string path = Path.Combine(arena2, name);
            if (!File.Exists(path))
            {
                throw new InvalidOperationException($"The documented residual path '{name}' is not supplied by '{arena2}'.");
            }

            // The documented disposition travels with the path: a file the inventory already
            // imports has a consumer this classification cannot see for itself.
            documented[name] = row.Disposition;
            sources.Add((name, File.ReadAllBytes(path)));
        }

        ResidualSourceInventory inventory = ResidualSourceInventory.Enumerate(sources, Path.GetFileName(Path.TrimEndingDirectorySeparator(arena2)), documented);
        Console.WriteLine($"residual paths: {inventory.Files.Count} documented and classified, {inventory.Imported.Count()} already imported, {inventory.Unused.Count()} readable with no consumer, {inventory.Malformed.Count()} refused by their family's reader, {inventory.Unresolved.Count()} with no reader in this repository");
        foreach (IGrouping<string, ResidualSourceRecord> family in inventory.Files.GroupBy(file => file.Family, StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            ResidualSourceRecord first = family.First();
            Console.WriteLine($"  {family.Key,-4} {family.Count(),3}  reader=[{(first.Reader.Length == 0 ? "none" : first.Reader)}] donor=[{(first.DonorReader.Length == 0 ? "none" : first.DonorReader)}]{(first.Documented ? string.Empty : "  (not in the documented family list)")}");
        }

        foreach (ResidualSourceRecord refused in inventory.Malformed)
        {
            Console.WriteLine($"  refused {refused.Path}: {refused.Note}");
        }

        Console.WriteLine($"families: {inventory.UndocumentedFamilies.Count} supplied but undocumented [{string.Join(", ", inventory.UndocumentedFamilies)}], {inventory.MissingDocumentedFamilies.Count} documented but not supplied [{string.Join(", ", inventory.MissingDocumentedFamilies)}]");
        return 0;
    }

    /// <summary>
    /// Publishes the donor's static mobile table into the base pack as one normalized record per mobile.
    /// The donor's table is the parameter authority and the pack is the identity authority; a mobile the
    /// pack does not publish keeps its parameters and states its disposition rather than disappearing.
    /// </summary>
    private static int RunMobileCatalogCommand(IReadOnlyList<string> args)
    {
        bool update = args.Contains("--update", StringComparer.Ordinal);
        if (args.Count != (update ? 6 : 5) || args[1] != "--donor" || args[3] != "--pack")
        {
            throw new ArgumentException("usage: daggerfall-import-tool mobile-catalog --donor ENEMY_BASICS.cs --pack PACK.json [--update]");
        }

        string donorFile = args[2];
        string packFile = args[4];
        if (!File.Exists(donorFile)) throw new FileNotFoundException($"The donor's static mobile table is required to publish mobile parameters and is not at '{donorFile}'.", donorFile);
        Arena2MobileCatalogPublication publication = Arena2MobileCatalogDocument.Build(
            File.ReadAllText(donorFile), File.ReadAllText(packFile), "research/daggerfall-unity/Assets/Scripts/Utility/EnemyBasics.cs");
        Console.WriteLine($"mobile catalog: {publication.Mobiles} donor mobiles, {publication.Published} published, {publication.HumanMobiles} human mobiles, {publication.Unpublished} unpublished");
        if (!update)
        {
            Console.WriteLine("pack: not written (rerun with --update to publish this catalog into it)");
            return 0;
        }

        JsonNode pack = JsonNode.Parse(File.ReadAllText(packFile))!.AsObject();
        pack["mobiles"] = JsonNode.Parse(publication.Json);
        File.WriteAllText(packFile, pack.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
        Console.WriteLine($"pack: mobile catalog updated in {packFile}");
        return 0;
    }

    /// <summary>
    /// Publishes the classic spell and magic-item catalogs into the base pack. Both source files are
    /// required inputs: a catalog built from one of them would look complete while resolving half of what
    /// the game defines, and SPELL.RSC is refused by name because it is not a file this corpus carries.
    /// </summary>
    private static int RunMagicCatalogCommand(IReadOnlyList<string> args)
    {
        bool update = args.Contains("--update", StringComparer.Ordinal);
        if (args.Count != (update ? 6 : 5) || args[1] != "--arena2" || args[3] != "--pack")
        {
            throw new ArgumentException("usage: daggerfall-import-tool magic-catalog --arena2 SOURCE_DIR --pack PACK.json [--update]");
        }

        string arena2 = args[2];
        string packFile = args[4];
        foreach (string refused in Directory.EnumerateFiles(arena2, "*", SearchOption.TopDirectoryOnly))
        {
            if (string.Equals(Path.GetFileName(refused), "SPELL.RSC", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"'{refused}' is a SPELL.RSC input; the classic spell catalog lives in SPELLS.STD and this corpus carries no SPELL.RSC to read.");
            }
        }

        string spellPath = Path.Combine(arena2, "SPELLS.STD");
        string magicPath = Path.Combine(arena2, "MAGIC.DEF");
        if (!File.Exists(spellPath)) throw new FileNotFoundException($"SPELLS.STD is required to build the spell catalog and is not in '{arena2}'.", spellPath);
        if (!File.Exists(magicPath)) throw new FileNotFoundException($"MAGIC.DEF is required to build the magic-item catalog and is not in '{arena2}'.", magicPath);
        Arena2MagicCatalogPublication publication = Arena2MagicCatalogDocument.Build(
            File.ReadAllBytes(spellPath), File.ReadAllBytes(magicPath), "local/arena2/SPELLS.STD", "local/arena2/MAGIC.DEF");
        Console.WriteLine($"magic catalog: {publication.Spells} spells, {publication.MagicItems} magic items, {publication.Enchantments} enchantments, {publication.UnresolvedLinks} unresolved spell links, {publication.Dispositions} dispositions");
        if (!update)
        {
            Console.WriteLine("pack: not written (rerun with --update to publish this catalog into it)");
            return 0;
        }

        // Only the magic property is replaced; every other authored section keeps its own ordering.
        JsonNode pack = JsonNode.Parse(File.ReadAllText(packFile))!.AsObject();
        pack["magic"] = JsonNode.Parse(publication.Json);
        File.WriteAllText(packFile, pack.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
        Console.WriteLine($"pack: magic catalog updated in {packFile}");
        return 0;
    }

    /// <summary>
    /// Reconciles the donor's static mobile table with the published pack and reports every entry
    /// nothing published carries. Coverage of the donor's mobiles is reported, not assumed: an entry
    /// the pack does not publish is named here rather than staying invisible.
    /// </summary>
    private static int RunMobileLedgerCommand(IReadOnlyList<string> args)
    {
        if (args.Count != 5 || args[1] != "--donor" || args[3] != "--pack")
        {
            throw new ArgumentException("usage: daggerfall-import-tool mobile-ledger --donor ENEMY_BASICS.cs --pack PACK.json");
        }

        MobileLedger ledger = MobileLedgerBuilder.Build(File.ReadAllText(args[2]), File.ReadAllText(args[4]));
        Console.WriteLine($"donor mobiles: {ledger.DonorEntries}, published actors: {ledger.PublishedActors}, catalog enemies: {ledger.CatalogEntries}");
        Console.WriteLine($"published {ledger.Entries.Count(entry => entry.Disposition == MobileLedgerDisposition.Published)}, variants {ledger.Entries.Count(entry => entry.Disposition == MobileLedgerDisposition.PublishedVariant)}, human mobiles {ledger.Entries.Count(entry => entry.Disposition == MobileLedgerDisposition.HumanClass)}, unpublished {ledger.Unpublished.Count}");
        foreach (MobileLedgerEntry entry in ledger.Unpublished)
        {
            Console.WriteLine($"unpublished: id {entry.Id} '{entry.Name}' - {entry.Note}");
        }

        foreach (MobileLedgerEntry entry in ledger.Entries.Where(entry => entry.Disposition == MobileLedgerDisposition.PublishedVariant))
        {
            Console.WriteLine($"variant: id {entry.Id} '{entry.Name}' - {entry.Note}");
        }

        return 0;
    }

    /// <summary>
    /// Builds the item-template ledger from the donor's own group enumerations and either
    /// reports the drift against the pack's ledger or publishes the rebuilt section.
    /// </summary>
    private const string ItemTemplateFamily = "CNT-011";

    private static int RunItemTemplateLedgerCommand(IReadOnlyList<string> args)
    {
        bool update = args.Contains("--update", StringComparer.Ordinal);
        if (args.Count != (update ? 8 : 7) || args[1] != "--donor" || args[3] != "--inventory" || args[5] != "--pack")
        {
            throw new ArgumentException("usage: daggerfall-import-tool item-template-ledger --donor ITEMS_DIR --inventory INVENTORY.csv --pack PACK.json [--update]");
        }

        string donor = args[2];
        string packFile = args[6];
        // The target's provenance comes from the documented inventory rather than from a
        // literal here, so a manifest-row change flows into the ledger instead of leaving
        // it silently stale.
        SourceInventoryRow family = SourceManifestBuilder.ReadInventory(File.ReadAllBytes(args[4]))
            .FirstOrDefault(row => row.RowType == "family" && StringComparer.Ordinal.Equals(row.Id, ItemTemplateFamily))
            ?? throw new InvalidOperationException($"The documented inventory does not carry family '{ItemTemplateFamily}'.");
        string targetStatus = ItemTemplateLedgerBuilder.StatusFor(family.Disposition);
        ItemTemplateBaseline baseline = ItemTemplateBaseline.FromDonorSources(
            File.ReadAllText(Path.Combine(donor, "ItemEnums.cs")),
            File.ReadAllText(Path.Combine(donor, "ItemHelper.cs")),
            File.ReadAllText(Path.Combine(donor, "..", "..", "API", "ItemsFile.cs")),
            "donor");
        JsonNode pack = JsonNode.Parse(File.ReadAllText(packFile))!.AsObject();
        int publishedItems = pack["items"]?.AsArray().Count ?? 0;
        JsonObject ledger = ItemTemplateLedgerBuilder.Build(
            baseline,
            family.Id,
            family.PathOrPattern,
            targetStatus,
            publishedItems);
        Console.WriteLine($"target: {family.Id} | {family.PathOrPattern} | documented '{family.Disposition}' -> status '{targetStatus}'");
        Console.WriteLine($"item template ledger: {baseline.Targets.Count} targets, {baseline.Targets.Count(target => target.IsReferenced)} referenced by donor groups, {baseline.Unreferenced.Count()} referenced by none, {baseline.OutOfRangeIndices.Count} outside the classic space");
        Console.WriteLine($"unreferenced indices: [{string.Join(", ", baseline.Unreferenced.Select(target => target.Index))}]");
        Console.WriteLine($"published items: {publishedItems}, value provenance 'catalog-migration', native decoding false");

        string rebuilt = ledger.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        string? existing = pack[ItemTemplateLedgerBuilder.SectionName]?.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        if (!update)
        {
            Console.WriteLine(existing is null
                ? $"pack: no {ItemTemplateLedgerBuilder.SectionName} section (rerun with --update to publish it)"
                : string.Equals(existing, rebuilt, StringComparison.Ordinal)
                    ? $"pack: {ItemTemplateLedgerBuilder.SectionName} matches the donor baseline"
                    : $"pack: {ItemTemplateLedgerBuilder.SectionName} differs from the donor baseline (rerun with --update to publish it)");
            // Report only: the pack's ledger is the published artifact, and a difference is
            // printed for a human to publish rather than failed, since this command needs
            // the donor checkout that a routine build does not carry.
            return 0;
        }

        pack[ItemTemplateLedgerBuilder.SectionName] = JsonNode.Parse(rebuilt);
        File.WriteAllText(packFile, pack.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
        Console.WriteLine($"pack: {ItemTemplateLedgerBuilder.SectionName} updated in {packFile}");
        return 0;
    }

    /// <summary>
    /// Enumerates the classic quest source corpus, decodes both envelopes, and — when the
    /// documented inventory is supplied — checks that every supplied path is one the
    /// inventory carries and that it carries no path the corpus does not.
    /// </summary>
    private static int RunQuestSourceCommand(IReadOnlyList<string> args)
    {
        bool check = args.Contains("--inventory", StringComparer.Ordinal);
        if (args.Count != (check ? 5 : 3) || args[1] != "--arena2" || (check && args[3] != "--inventory"))
        {
            throw new ArgumentException("usage: daggerfall-import-tool quest-sources --arena2 SOURCE_DIR [--inventory INVENTORY.csv]");
        }

        string arena2 = args[2];
        string[] paths = [.. Directory.EnumerateFiles(arena2)
            .Where(path => path.EndsWith(QuestSourceInventory.BinaryExtension, StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(QuestSourceInventory.ResourcesExtension, StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFileName)
            .OfType<string>()];
        // A directory may be supplied with a trailing separator, which has no file name of
        // its own; the source label is the directory the caller named.
        string label = Path.GetFileName(Path.TrimEndingDirectorySeparator(arena2));
        QuestSourceInventory inventory = QuestSourceInventory.Enumerate(paths, string.IsNullOrEmpty(label) ? arena2 : label);
        int marked = 0, unmarked = 0, decoded = 0, badResources = 0, badBinaries = 0;
        long records = 0;
        foreach (QuestSourceFile file in inventory.Files)
        {
            byte[] bytes = File.ReadAllBytes(Path.Combine(arena2, file.Path));
            if (file.Family == QuestSourceFamily.QuestBinary)
            {
                QuestBinaryEnvelope envelope = QuestBinaryEnvelope.Decode(bytes, file.Path);
                marked += envelope.HasTerminalMarker ? 1 : 0;
                unmarked += envelope.HasTerminalMarker ? 0 : 1;
                badBinaries += envelope.Disposition == QuestBinaryEnvelopeDisposition.WellFormed ? 0 : 1;
            }
            else
            {
                QuestResourceEnvelope envelope = QuestResourceEnvelope.Decode(bytes, file.Path);
                decoded += envelope.Disposition == QuestResourceEnvelopeDisposition.Decoded ? 1 : 0;
                badResources += envelope.Disposition == QuestResourceEnvelopeDisposition.Decoded ? 0 : 1;
                records += envelope.Records.Count;
            }
        }

        Console.WriteLine($"quest sources: {inventory.Files.Count} files, {inventory.Binaries.Count()} QBN, {inventory.Resources.Count()} QRC, {inventory.Files.Count(file => file.Pairing == QuestSourcePairing.Paired)} paired, {inventory.BinaryOnly.Count()} binary-only, {inventory.ResourcesOnly.Count()} resources-only");
        Console.WriteLine($"binary envelopes: {marked} with the terminal marker, {unmarked} without, {badBinaries} not well formed");
        Console.WriteLine($"resource envelopes: {decoded} decoded into {records} records, {badResources} not decoded");
        foreach (QuestSourceFile file in inventory.BinaryOnly.Concat(inventory.ResourcesOnly))
        {
            Console.WriteLine($"  unpaired {file.Path} ({file.Pairing})");
        }

        if (!check)
        {
            return 0;
        }

        // The inventory is the authority for which paths exist; this checks the corpus and
        // the document against each other rather than trusting either alone.
        IReadOnlyList<SourceInventoryRow> rows = SourceManifestBuilder.ReadInventory(File.ReadAllBytes(args[4]));
        HashSet<string> documented = [.. rows
            .Where(row => row.RowType == "file" && StringComparer.Ordinal.Equals(row.FamilyId, "CNT-017"))
            .Select(row => Path.GetFileName(row.PathOrPattern))];
        string[] missing = [.. inventory.Files.Select(file => file.Path).Where(path => !documented.Contains(path))];
        string[] extra = [.. documented.Where(path => !inventory.Files.Any(file => StringComparer.Ordinal.Equals(file.Path, path)))];
        if (missing.Length != 0 || extra.Length != 0)
        {
            throw new InvalidOperationException($"The documented inventory and the supplied corpus disagree; supplied but undocumented: [{string.Join(", ", missing)}], documented but not supplied: [{string.Join(", ", extra)}].");
        }

        Console.WriteLine($"inventory: {documented.Count} documented paths match the corpus");
        return 0;
    }

    /// <summary>
    /// Enumerates MONSTER.BSA and reports what every record is, which source mobile it
    /// belongs to, and which records nothing in this build explains.
    /// </summary>
    private static int RunMonsterArchiveCommand(IReadOnlyList<string> args)
    {
        if (args.Count != 3 || args[1] != "--monster")
        {
            throw new ArgumentException("usage: daggerfall-import-tool monster-archive --monster MONSTER.BSA");
        }

        string path = args[2];
        MonsterArchiveInventory inventory = MonsterArchiveInventory.Enumerate(File.ReadAllBytes(path), Path.GetFileName(path));
        int decoded = inventory.Records.Count(record => record.Disposition == MonsterArchiveRecordDisposition.Decoded);
        int malformed = inventory.Records.Count(record => record.Disposition == MonsterArchiveRecordDisposition.Malformed);
        int unrecognized = inventory.Records.Count(record => record.Disposition == MonsterArchiveRecordDisposition.Unrecognized);
        Console.WriteLine($"{inventory.Source}: {inventory.Records.Count} records, {inventory.AnimationScripts.Count()} animation scripts, {inventory.EnemyConfigurations.Count()} enemy configurations, {decoded} decoded, {malformed} malformed, {unrecognized} unrecognized");
        Console.WriteLine($"links: {inventory.Records.Count(record => record.IsLinked)} records resolve to a supported source mobile, {inventory.Unlinked.Count()} stay unlinked");
        foreach (MonsterArchiveRecord record in inventory.Records.Where(record => record.Disposition != MonsterArchiveRecordDisposition.Decoded).Take(8))
        {
            Console.WriteLine($"  {record.Name}: {record.Disposition} - {record.Note}");
        }

        // A report command still fails when the archive cannot be read at all; records it
        // merely cannot explain are reported rather than treated as a failure.
        return 0;
    }

    /// <summary>
    /// Builds the normalized reference catalogs from the supplied careers and the
    /// documented inventory, reports what it found, and writes them into the base pack
    /// when asked. The pack is the published artifact; this is how it is produced.
    /// </summary>
    private static int RunCatalogCommand(IReadOnlyList<string> args)
    {
        bool update = args.Contains("--update", StringComparer.Ordinal);
        if (args.Count != (update ? 8 : 7) || args[1] != "--arena2" || args[3] != "--inventory" || args[5] != "--pack")
        {
            throw new ArgumentException("usage: daggerfall-import-tool catalogs --arena2 SOURCE_DIR --inventory INVENTORY.csv --pack PACK.json [--update]");
        }

        string arena2 = args[2];
        string inventoryFile = args[4];
        string packFile = args[6];
        IReadOnlyList<SourceInventoryRow> inventory = SourceManifestBuilder.ReadInventory(File.ReadAllBytes(inventoryFile));
        (List<string> attributes, List<string> skills) = ReadVocabulary(packFile);
        List<(string FileName, byte[] Bytes)> careers = [.. Directory
            .EnumerateFiles(arena2, "CLASS*.CFG")
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => (Path.GetFileName(path), File.ReadAllBytes(path)))];
        (List<string> enemies, List<string> items) = ReadPackKeys(packFile);
        DaggerfallCatalogs catalogs = DaggerfallCatalogBuilder.Build(inventory, attributes, skills, careers, enemies, items);
        IReadOnlySet<string> inventoryIds = inventory.Select(row => row.Id).ToHashSet(StringComparer.Ordinal);
        byte[] section = DaggerfallCatalogSerializer.Serialize(catalogs, inventoryIds);

        Console.WriteLine($"catalogs: {catalogs.Races.Count} races, {catalogs.Careers.Count} careers, {catalogs.Attributes.Count} attributes, {catalogs.Skills.Count} skills, {catalogs.Resistances.Count} elements, {catalogs.Enemies.Count} enemy references, {catalogs.ItemTemplates.Count} item-template references, {catalogs.Pending.Count} pending namespaces");
        foreach (DaggerfallCareerRecord career in catalogs.Careers)
        {
            Console.WriteLine($"  {career.Id} '{career.Name}' hp/level {career.HitPointsPerLevel} primary {string.Join('/', career.PrimarySkills)} source {career.Source.RecordId}");
        }

        if (!update)
        {
            Console.WriteLine("pack: not written (rerun with --update to publish these catalogs into it)");
            return 0;
        }

        // Only the catalogs property is replaced; every other authored section keeps its
        // own ordering and formatting.
        JsonNode pack = JsonNode.Parse(File.ReadAllText(packFile))!.AsObject();
        pack["catalogs"] = JsonNode.Parse(System.Text.Encoding.UTF8.GetString(section));
        File.WriteAllText(packFile, pack.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
        Console.WriteLine($"pack: catalogs updated in {packFile}");
        return 0;
    }

    /// <summary>
    /// Publishes the character media the corpus supplies and builds the presentation section from it,
    /// writing the artifacts under the content root and the references into the base pack when asked, so
    /// a character or social consumer resolves a race's layers by identity instead of reconstructing
    /// file names.
    /// </summary>
    /// <remarks>
    /// The publication runs before the presentation is built, because the presentation states whether a
    /// reference binds: <c>MediaBinding.Admitted</c> means a published consumer binds the file, so the
    /// files the character sheet resolves are enumerated as bound and the references are rewritten with
    /// that fact rather than every reference staying pending beside its own published artifact.
    /// </remarks>
    private static int RunCharacterPresentationCommand(IReadOnlyList<string> args)
    {
        const string Usage = "usage: daggerfall-import-tool character-presentation --arena2 SOURCE_DIR --inventory INVENTORY.csv --pack PACK.json --out CONTENT_ROOT [--group NAME] [--update]";
        bool update = args.Contains("--update", StringComparer.Ordinal);
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        for (int index = 1; index < args.Count; index++)
        {
            string argument = args[index];
            if (argument == "--update") continue;
            if (!argument.StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Count || !values.TryAdd(argument, args[++index]))
            {
                throw new ArgumentException(Usage);
            }
        }

        string[] accepted = ["--arena2", "--inventory", "--pack", "--out", "--group"];
        if (values.Count != accepted.Length || accepted.Any(key => !values.ContainsKey(key)))
        {
            throw new ArgumentException(Usage);
        }

        string arena2 = values["--arena2"];
        string packFile = values["--pack"];
        // The group is part of the path the artifacts and their index are written under, which is the
        // naming the product's admitted content carries.
        string outRoot = Path.Combine(values["--out"], values["--group"]);
        // The inventory's own family rule, not a prefix filter: a class portrait or story sprite is
        // named by its extension, and a prefix test alone drops those files silently.
        List<(string Path, ReadOnlyMemory<byte> Bytes)> sources = [.. Directory
            .EnumerateFiles(arena2)
            .Select(path => Path.GetFileName(path))
            .Where(CharacterMediaInventory.IsDocumentedFamily)
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name => (name, (ReadOnlyMemory<byte>)File.ReadAllBytes(Path.Combine(arena2, name))))];
        Dictionary<string, Arena2Palette> palettes = new(StringComparer.Ordinal);
        foreach (string file in Directory.EnumerateFiles(arena2, "*.COL").OrderBy(path => path, StringComparer.Ordinal))
        {
            string name = Path.GetFileName(file);
            palettes[name] = PaletteDecoder.Decode(File.ReadAllBytes(file), $"arena2/{name}");
        }

        // The documented inventory is what the corpus is supposed to hold for this family, so it is checked
        // against the corpus in both directions: a documented file the corpus lacks is a source gap, and a
        // corpus file the inventory does not document is one this publication would emit without a record.
        string[] undocumented = [.. CharacterMediaPublisher.ReconcileDocumentedInventory(File.ReadAllBytes(values["--inventory"]), sources.Select(entry => entry.Path))];

        IReadOnlyList<DaggerfallRaceKey> races = ReadPackRaces(packFile);
        IReadOnlyDictionary<string, string> careers = ReadPackCareers(packFile);
        string source = Path.GetFileName(Path.TrimEndingDirectorySeparator(arena2));

        // The publication pass runs first over the corpus as it stands, so the artifacts exist before any
        // reference claims one. The consumer is then derived from the pack's own authored identity: the
        // character sheet resolves the race the player's actor declares and a portrait per career, so
        // those files are the ones a published consumer binds today and every other file stays
        // required-pending with its artifact written and indexed.
        CharacterMediaInventory unbound = CharacterMediaInventory.Enumerate(sources, new HashSet<string>(StringComparer.Ordinal), source);
        IReadOnlySet<string> suppliedPalettes = palettes.Keys.ToHashSet(StringComparer.Ordinal);
        Dictionary<string, ReadOnlyMemory<byte>> corpus = sources.ToDictionary(entry => entry.Path, entry => entry.Bytes, StringComparer.Ordinal);
        CharacterMediaReferenceSet derived = CharacterMediaReferences.Derive(unbound, suppliedPalettes, corpus);
        CharacterMediaPassResult pass = CharacterMediaPublisher.PublishAll(derived, corpus, palettes, unbound);
        IReadOnlySet<string> bound = CharacterMediaReferences.FilesBoundByCharacterSheet(unbound);
        // The references already state the palette each file is painted in - the derivation reads a
        // container's own - so the rewrite only has to state the binding.
        CharacterMediaReferenceSet referenced = CharacterMediaReferences.WithBoundFiles(derived, bound, CharacterMediaReferences.CharacterSheetConsumer);

        // A reference a consumer binds has to resolve to a published canvas: binding a source whose artifact
        // the pass could not emit would publish a reference to nothing, which is the failure the binding
        // exists to make impossible rather than to discover in a session. A source whose format nothing here
        // reads is the same failure, so both halves are checked rather than only the enumerated canvases.
        string[] unboundSources = [.. referenced.Canvases
            .Where(canvas => canvas.Binding == MediaBinding.Admitted && !pass.PublishedMediaIds.Contains(canvas.MediaId))
            .Select(canvas => System.IO.Path.GetFileName(canvas.Path))
            .Concat(referenced.Unavailable
                .Where(file => file.Binding == MediaBinding.Admitted)
                .Select(file => System.IO.Path.GetFileName(file.Path)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)];
        if (unboundSources.Length != 0)
        {
            throw new InvalidOperationException(
                $"{CharacterMediaReferences.CharacterSheetConsumer} binds {unboundSources.Length} supplied file(s) this pass published no canvas from: {string.Join(", ", unboundSources)}.");
        }

        CharacterMediaInventory characters = CharacterMediaInventory.Enumerate(sources, bound, CharacterMediaReferences.CharacterSheetConsumer, source);
        DaggerfallCharacterPresentation presentation = DaggerfallCharacterPresentationBuilder.Build(characters, suppliedPalettes, races, careers, pass.UnpublishableFiles, corpus);
        presentation.Validate(pass.PublishedMediaIds);

        // Every reference, not the layers and faces alone: a career portrait is bound too, and reporting
        // only part of the set understates what the pack claims a consumer draws.
        int pending = presentation.Layers.Count(layer => layer.Binding == MediaBinding.RequiredPending)
            + presentation.Faces.Count(face => face.Binding == MediaBinding.RequiredPending)
            + presentation.Careers.Count(portrait => portrait.Binding == MediaBinding.RequiredPending);
        int admitted = presentation.Layers.Count + presentation.Faces.Count + presentation.Careers.Count - pending;
        Console.WriteLine($"character presentation: {characters.Files.Count} supplied files, {pass.Artifacts.Count} published canvases, {pass.Refusals.Count} refused, {presentation.Layers.Count} layers over {races.Count} races, {presentation.Faces.Count} faction faces, {presentation.Careers.Count} career portraits, {presentation.CareersWithoutPortrait.Count} careers without one");
        Console.WriteLine($"  binding: {admitted} reference(s) bound by {CharacterMediaReferences.CharacterSheetConsumer}, {pending} required-pending; {pass.UnreadableFamilies.Count} unreadable family entry(ies)");
        foreach (string refusal in pass.Refusals.Take(2))
        {
            Console.WriteLine($"  refusal: {refusal}");
        }

        Console.WriteLine($"  inventory: {characters.Files.Count - undocumented.Length} documented supplied file(s) reconciled with the corpus");
        foreach (string file in undocumented)
        {
            Console.WriteLine($"  warning: '{file}' is in the corpus and not in the documented inventory");
        }

        foreach (string race in presentation.Layers.Select(layer => layer.Race).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            Console.WriteLine($"  {race}: {presentation.Layers.Count(layer => layer.Race == race)} layers");
        }

        // The races the catalogs publish and the ones the supplied media covers are two facts: a race in the
        // catalog with no layer section is a stated gap, and a layer section no catalog race claims is art
        // nothing resolves - either way the reader sees it here rather than inferring it from a layer count.
        string[] unresolved = [.. races.Select(race => race.Id).Where(id => !presentation.Layers.Any(layer => layer.Race == id))];
        if (unresolved.Length != 0)
        {
            Console.WriteLine($"  warning: {unresolved.Length} catalog race(s) publish no layers here: {string.Join(", ", unresolved)}");
        }

        foreach (DaggerfallRaceWithoutMedia gap in presentation.RacesWithoutMedia.Take(4))
        {
            Console.WriteLine($"  gap {gap.Race}: {gap.Reason}");
        }

        if (!update)
        {
            Console.WriteLine("content: not written (rerun with --update to publish these artifacts and references)");
            return 0;
        }

        byte[] indexBytes = CharacterMediaPublisher.WriteIndex(pass, referenced, values["--group"]);
        string characterRoot = Path.Combine(outRoot, "media", "character");
        string indexFile = Path.Combine(outRoot, CharacterMediaPublisher.IndexRelativePath.Replace('/', Path.DirectorySeparatorChar));

        // A rebuild writes what this run published; it does not delete what an earlier run published and this
        // one did not, and an artifact whose identity changed would otherwise sit in the tree unindexed while
        // the index looked complete. This refuses rather than deleting, because the tree is the product's
        // content and a file nothing here wrote is not this command's to remove.
        string[] stale = [.. StaleArtifacts(characterRoot, indexBytes, indexFile)];
        if (stale.Length != 0)
        {
            throw new InvalidOperationException(
                $"{stale.Length} file(s) under {characterRoot} are not artifacts this run publishes, so the delivered tree would carry content no index names: {string.Join(", ", stale)}. Remove them, or republish from the corpus that produced them.");
        }

        foreach (CharacterMediaArtifact artifact in pass.Artifacts)
        {
            string path = Path.Combine(outRoot, artifact.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, artifact.Bytes);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(indexFile)!);
        File.WriteAllBytes(indexFile, indexBytes);
        Console.WriteLine($"content: {pass.Artifacts.Count} character artifacts and their index written under {outRoot}");

        JsonNode pack = JsonNode.Parse(File.ReadAllText(packFile))!.AsObject();
        pack["characterPresentation"] = JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(presentation, PublishedJson.Section));
        File.WriteAllText(packFile, pack.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
        Console.WriteLine($"pack: characterPresentation updated in {packFile}");
        return 0;
    }

    /// <summary>
    /// The files already under the character group that the index this run is about to write does not name,
    /// with the index itself excluded.
    /// </summary>
    private static IEnumerable<string> StaleArtifacts(string characterRoot, byte[] indexBytes, string indexFile)
    {
        if (!Directory.Exists(characterRoot)) yield break;
        JsonObject index = JsonNode.Parse(indexBytes)!.AsObject();
        HashSet<string> published = [.. index["artifacts"]!.AsArray()
            .Select(artifact => Path.GetFileName(artifact!["path"]!.GetValue<string>()))];
        published.Add(Path.GetFileName(indexFile));
        foreach (string path in Directory.EnumerateFiles(characterRoot).OrderBy(path => path, StringComparer.Ordinal))
        {
            if (!published.Contains(Path.GetFileName(path))) yield return Path.GetFileName(path);
        }
    }

    /// <summary>
    /// The careers the catalog section publishes, by identity, with the class name a portrait is
    /// matched to: a career's identity is its record position, so its name is what names its art.
    /// </summary>
    /// <summary>
    /// Reads every region's locations and dungeons from MAPS.BSA and writes them into the base pack
    /// when asked, so a site or world consumer resolves a place by the region the source names.
    /// </summary>
    private static int RunLocationsCommand(IReadOnlyList<string> args)
    {
        bool update = args.Contains("--update", StringComparer.Ordinal);
        if (args.Count != (update ? 6 : 5) || args[1] != "--arena2" || args[3] != "--pack")
        {
            throw new ArgumentException("usage: daggerfall-import-tool locations --arena2 SOURCE_DIR --pack PACK.json [--update]");
        }

        string arena2 = args[2];
        string packFile = args[4];
        BsaArchive archive = BsaArchive.Parse(File.ReadAllBytes(Path.Combine(arena2, "MAPS.BSA")), "arena2/MAPS.BSA");
        DaggerfallLocations locations = DaggerfallLocationBuilder.Build(archive);

        Console.WriteLine($"locations: {locations.Locations.Count} locations over {locations.Locations.Select(location => location.Region).Distinct().Count()} regions, {locations.Dungeons.Count} dungeons, {locations.RegionsWithoutTables.Count} regions without usable tables");
        foreach (IGrouping<string, DaggerfallRegionGap> gap in locations.RegionsWithoutTables.GroupBy(gap => string.Join('+', gap.EmptyTables.Select(name => name[..name.IndexOf('.', StringComparison.Ordinal)]))))
        {
            Console.WriteLine($"  {gap.Count()} regions have no {gap.Key}");
        }
        if (!update)
        {
            Console.WriteLine("pack: not written (rerun with --update to publish these locations into it)");
            return 0;
        }

        JsonNode pack = JsonNode.Parse(File.ReadAllText(packFile))!.AsObject();
        pack["locations"] = JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(locations, PublishedJson.Section));
        File.WriteAllText(packFile, pack.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
        Console.WriteLine($"pack: locations updated in {packFile}");
        return 0;
    }

    /// <summary>
    /// Reads the climate and politic grids from their PAK files and writes them into the base pack
    /// when asked, so a terrain, weather or social consumer resolves a map pixel by the coordinates
    /// the source tiles rather than by a policy inferred from a cell value.
    /// </summary>
    private static int RunClimateCommand(IReadOnlyList<string> args)
    {
        const string Usage = "usage: daggerfall-import-tool climate --arena2 SOURCE_DIR --pack PACK.json --inventory CSV [--update]";
        bool update = args.Contains("--update", StringComparer.Ordinal);
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        for (int index = 1; index < args.Count; index++)
        {
            string argument = args[index];
            if (argument == "--update") continue;
            if (!argument.StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Count || !values.TryAdd(argument, args[++index]))
            {
                throw new ArgumentException(Usage);
            }
        }

        string[] accepted = ["--arena2", "--pack", "--inventory"];
        if (values.Count != accepted.Length || accepted.Any(key => !values.ContainsKey(key)))
        {
            throw new ArgumentException(Usage);
        }

        string arena2 = values["--arena2"];
        IReadOnlyList<SourceInventoryRow> inventory = SourceManifestBuilder.ReadInventory(File.ReadAllBytes(values["--inventory"]));
        // The documented inventory decides the logical source identity, so the bytes are read under the
        // paths the repository documents rather than under whatever directory the caller happened to name.
        string climateLabel = Path.Combine(arena2, "CLIMATE.PAK");
        string politicLabel = Path.Combine(arena2, "POLITIC.PAK");
        DaggerfallClimateGrid climate = DaggerfallWorldGridsBuilder.BuildClimate(File.ReadAllBytes(climateLabel), climateLabel, inventory);
        DaggerfallPoliticGrid politic = DaggerfallWorldGridsBuilder.BuildPolitic(File.ReadAllBytes(politicLabel), politicLabel, inventory);

        Console.WriteLine($"climate: {climate.Rows.Count} rows, {climate.Values.Count} distinct values ({climate.Values.Count(value => value.Disposition == DaggerfallClimateDisposition.Named)} named)");
        Console.WriteLine($"politic: {politic.Rows.Count} rows, {politic.Values.Count} distinct values ({politic.Values.Count(value => value.Disposition == DaggerfallPoliticDisposition.Region)} regions, {politic.Values.Count(value => value.Disposition == DaggerfallPoliticDisposition.Ocean)} ocean, {politic.Values.Count(value => value.Disposition == DaggerfallPoliticDisposition.Unresolved)} unresolved)");
        if (!update)
        {
            Console.WriteLine("pack: not written (rerun with --update to publish these grids into it)");
            return 0;
        }

        JsonNode pack = JsonNode.Parse(File.ReadAllText(values["--pack"]))!.AsObject();
        pack["climate"] = JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(climate, PublishedJson.Section));
        pack["politic"] = JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(politic, PublishedJson.Section));
        File.WriteAllText(values["--pack"], pack.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
        Console.WriteLine($"pack: climate and politic updated in {values["--pack"]}");
        return 0;
    }

    /// <summary>
    /// Enumerates the map, automap, travel and town artwork against the documented inventory, so a
    /// region, call-site or palette binding is a stated source fact rather than a filename a
    /// consumer would have to know. Pixels stay in the corpus; only the bindings publish.
    /// </summary>
    private static int RunMapArtCommand(IReadOnlyList<string> args)
    {
        bool check = args.Contains("--inventory", StringComparer.Ordinal);
        if (args.Count != (check ? 5 : 3) || args[1] != "--arena2" || (check && args[3] != "--inventory"))
        {
            throw new ArgumentException("usage: daggerfall-import-tool map-art --arena2 SOURCE_DIR [--inventory INVENTORY.csv]");
        }

        string arena2 = args[2];
        List<(string Name, string Path, byte[] Bytes)> sources = [];
        foreach (string pattern in new[] { "FMAP*.IMG", "AMAP*.IMG", "TMAP*.IMG", "TRAV*.IMG", "TOWN*.IMG", "FMAP_PAL.COL", "MAP.PAL" })
        {
            foreach (string path in Directory.EnumerateFiles(arena2, pattern).Order(StringComparer.Ordinal))
            {
                sources.Add((Path.GetFileName(path), Path.Combine(arena2, Path.GetFileName(path)).Replace(Path.DirectorySeparatorChar, '/'), File.ReadAllBytes(path)));
            }
        }

        MapArtInventory inventory = MapArtInventory.Enumerate(sources, Path.GetFileName(Path.TrimEndingDirectorySeparator(arena2)));
        Console.WriteLine($"map art: {inventory.Records.Count(record => record.Disposition == MapArtDisposition.Decoded)} decoded, {inventory.Records.Count(record => record.Disposition == MapArtDisposition.Unsupported)} unsupported, {inventory.Records.Count(record => record.Disposition == MapArtDisposition.NotSupplied)} not supplied, {inventory.Records.Count(record => record.Disposition is MapArtDisposition.Malformed or MapArtDisposition.Unreadable)} unreadable");
        foreach (MapArtRecord record in inventory.Records.Where(record => record.Disposition is MapArtDisposition.Malformed or MapArtDisposition.Unreadable))
        {
            Console.WriteLine($"  unreadable {record.FileName}: {record.Note}");
        }

        if (!check)
        {
            return 0;
        }

        IReadOnlyList<SourceInventoryRow> rows = SourceManifestBuilder.ReadInventory(File.ReadAllBytes(args[4]));
        HashSet<string> documented = [.. rows
            .Where(row => row.RowType == "file" && (StringComparer.Ordinal.Equals(row.FamilyId, "CNT-022")
                || StringComparer.Ordinal.Equals(row.Id, "CNT-027.file.MAP.PAL")))
            .Select(row => Path.GetFileName(row.PathOrPattern))];
        string[] supplied = [.. inventory.Records.Where(record => record.Disposition != MapArtDisposition.NotSupplied).Select(record => record.FileName)];
        string[] missing = [.. supplied.Where(path => !documented.Contains(path))];
        string[] extra = [.. documented.Where(path => !supplied.Contains(path, StringComparer.Ordinal))];
        if (missing.Length != 0 || extra.Length != 0)
        {
            throw new InvalidOperationException($"The documented inventory and the supplied corpus disagree; supplied but undocumented: [{string.Join(", ", missing)}], documented but not supplied: [{string.Join(", ", extra)}].");
        }

        Console.WriteLine($"inventory: {documented.Count} documented map art files match the corpus, {inventory.Records.Count(record => record.Disposition == MapArtDisposition.NotSupplied)} region slots without screens");
        return 0;
    }

    /// <summary>
    /// Reads the classic text resource into the base pack, so a text consumer resolves a value by the
    /// key the source gives it and reads the macros and variants the value carries rather than a
    /// rendered string this tool would have had to choose.
    /// </summary>
    private static int RunTextCommand(IReadOnlyList<string> args)
    {
        const string Usage = "usage: daggerfall-import-tool text --arena2 SOURCE_DIR --pack PACK.json --inventory CSV --language LANG [--update]";
        bool update = args.Contains("--update", StringComparer.Ordinal);
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        for (int index = 1; index < args.Count; index++)
        {
            string argument = args[index];
            if (argument == "--update") continue;
            if (!argument.StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Count || !values.TryAdd(argument, args[++index]))
            {
                throw new ArgumentException(Usage);
            }
        }

        string[] accepted = ["--arena2", "--pack", "--inventory", "--language"];
        if (values.Count != accepted.Length || accepted.Any(key => !values.ContainsKey(key)))
        {
            throw new ArgumentException(Usage);
        }

        // The documented inventory decides the logical source identity, so the bytes are read under the
        // paths the repository documents rather than under whatever directory the caller happened to name.
        string arena2 = values["--arena2"];
        string source = Path.Combine(arena2, TextResourceReader.FileName);
        List<(string Text, string Label, int ClassIndex, int BiographyIndex)> questionnaires = [];
        for (int classIndex = 0; classIndex <= 17; classIndex++)
        {
            string file = $"BIOG{classIndex:D2}T0.TXT";
            string label = Path.Combine(arena2, file);
            if (!File.Exists(label))
            {
                throw new ArgumentException($"the arena2 directory carries no {file}, so the biography questionnaires are incomplete");
            }

            questionnaires.Add((File.ReadAllText(label), label, classIndex, 0));
        }

        string imageLabel = Path.Combine(arena2, "BIOG00I0.IMG");
        byte[]? imageBytes = File.Exists(imageLabel) ? File.ReadAllBytes(imageLabel) : null;
        if (imageBytes is null)
        {
            Console.WriteLine("biography backdrop: not supplied, so every questionnaire records an unresolved image link");
        }

        (DaggerfallText text, DaggerfallNameTables names, DaggerfallRumorCatalog rumors, DaggerfallBiographies biographies, DaggerfallBooks publishedBooks) = DaggerfallTextBuilder.BuildAll(
            File.ReadAllBytes(source),
            source,
            File.ReadAllBytes(Path.Combine(arena2, NameGenReader.FileName)),
            Path.Combine(arena2, NameGenReader.FileName),
            File.ReadAllBytes(Path.Combine(arena2, RumorReader.FileName)),
            Path.Combine(arena2, RumorReader.FileName),
            File.ReadAllBytes(Path.Combine(arena2, BioDatReader.FileName)),
            Path.Combine(arena2, BioDatReader.FileName),
            questionnaires,
            imageBytes,
            ReadBooks(Path.Combine(arena2, "books")),
            SourceManifestBuilder.ReadInventory(File.ReadAllBytes(values["--inventory"])),
            values["--language"]);

        DaggerfallTextSource publishedSource = text.Sources[0];
        Console.WriteLine($"text: {text.Records.Count} records from {publishedSource.Path} in {publishedSource.Language}, {text.Records.Count(record => record.State == Arena2TextState.Malformed)} malformed, {text.Records.Count(record => record.State == Arena2TextState.Read)} readable");
        foreach (IGrouping<TextMacroDisposition, DaggerfallTextMacro> disposition in text.Macros.GroupBy(macro => macro.Disposition).OrderBy(group => group.Key))
        {
            Console.WriteLine($"  {disposition.Count()} macros {disposition.Key.ToString().ToLowerInvariant()}");
        }

        string[] unrecognised = [.. text.Macros.Where(macro => macro.Disposition == TextMacroDisposition.Unrecognised).Select(macro => macro.Symbol)];
        if (unrecognised.Length != 0)
        {
            Console.WriteLine($"  unrecognised symbols: {string.Join(", ", unrecognised)}");
        }

        Console.WriteLine($"  declared key families: {string.Join(", ", text.PendingKinds.Select(pending => $"{pending.Kind.ToString().ToLowerInvariant()} (task #{pending.OwnerTask})"))}");
        Console.WriteLine($"  names: {names.Banks.Count} banks, {names.Banks.Sum(bank => bank.Sets.Sum(set => set.Parts.Count))} fragments");
        Console.WriteLine($"  rumors: {rumors.Entries.Count} records, {rumors.Entries.Count(entry => entry.TypeDisposition == DaggerfallRumorTypeDisposition.Unknown)} unknown types");
        Console.WriteLine($"  biographies: {biographies.Biographies.Count} questionnaires, {biographies.DefaultLines} default lines");
        Console.WriteLine($"  books: {publishedBooks.Books.Count(book => book.Disposition == DaggerfallBookDisposition.Read)} supplied, {publishedBooks.Books.Count(book => book.Disposition != DaggerfallBookDisposition.Read)} without files or unreadable");
        if (!update)
        {
            Console.WriteLine("pack: not written (rerun with --update to publish this text into it)");
            return 0;
        }

        JsonNode pack = JsonNode.Parse(File.ReadAllText(values["--pack"]))!.AsObject();
        pack["text"] = JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(text, PublishedJson.Section));
        pack["names"] = JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(names, PublishedJson.Section));
        pack["rumors"] = JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(rumors, PublishedJson.Section));
        pack["biographies"] = JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(biographies, PublishedJson.Section));
        pack["books"] = JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(publishedBooks, PublishedJson.Section));
        File.WriteAllText(values["--pack"], pack.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
        Console.WriteLine($"pack: text, names, rumors, biographies and books updated in {values["--pack"]}");
        return 0;
    }

    /// <summary>
    /// Publishes Daggerfall Unity's managed localization table into the existing shared text section.
    /// The table supplements TEXT.RSC; it is deliberately a focused import so it does not need an
    /// Arena2 directory merely to update one donor-owned CSV source.
    /// </summary>
    private static int RunInternalStringsCommand(IReadOnlyList<string> args)
    {
        const string Usage = "usage: daggerfall-import-tool internal-strings --source Internal_Strings.csv --label LOGICAL_PATH --pack PACK.json --language LANG [--update]";
        bool update = args.Contains("--update", StringComparer.Ordinal);
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        for (int index = 1; index < args.Count; index++)
        {
            string argument = args[index];
            if (argument == "--update") continue;
            if (!argument.StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Count || !values.TryAdd(argument, args[++index]))
            {
                throw new ArgumentException(Usage);
            }
        }

        string[] accepted = ["--source", "--label", "--pack", "--language"];
        if (values.Count != accepted.Length || accepted.Any(key => !values.ContainsKey(key)))
        {
            throw new ArgumentException(Usage);
        }

        string existingJson = File.ReadAllText(values["--pack"]);
        JsonNode pack = JsonNode.Parse(existingJson)!.AsObject();
        DaggerfallText existing = JsonSerializer.Deserialize<DaggerfallText>(
            pack["text"]?.ToJsonString() ?? throw new InvalidOperationException("The target pack carries no text section to extend."),
            PublishedJson.SectionRead) ?? throw new InvalidOperationException("The target pack's text section could not be read.");
        DaggerfallText merged = DaggerfallInternalStringsBuilder.Merge(
            existing,
            File.ReadAllBytes(values["--source"]),
            values["--label"],
            values["--language"]);
        Console.WriteLine($"internal strings: {merged.Records.Count(record => record.Key.Kind == DaggerfallTextKind.Internal)} records from {values["--label"]}");
        if (!update)
        {
            Console.WriteLine("pack: not written (rerun with --update to publish internal strings into it)");
            return 0;
        }

        string updated = TopLevelJsonSectionRewriter.ReplaceOrAppend(existingJson, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["text"] = JsonSerializer.Serialize(merged, PublishedJson.Section),
        });
        File.WriteAllText(values["--pack"], updated);
        Console.WriteLine($"pack: internal strings updated in {values["--pack"]}");
        return 0;
    }

    /// <summary>Publishes the donor MapsFile regionRaces table used by classic %ef building-name expansion.</summary>
    private static int RunBuildingNameInputsCommand(IReadOnlyList<string> args)
    {
        const string Usage = "usage: daggerfall-import-tool building-name-inputs --maps-file MapsFile.cs --label LOGICAL_PATH --pack PACK.json [--update]";
        bool update = args.Contains("--update", StringComparer.Ordinal);
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        for (int index = 1; index < args.Count; index++)
        {
            string argument = args[index];
            if (argument == "--update") continue;
            if (!argument.StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Count || !values.TryAdd(argument, args[++index]))
            {
                throw new ArgumentException(Usage);
            }
        }

        string[] accepted = ["--maps-file", "--label", "--pack"];
        if (values.Count != accepted.Length || accepted.Any(key => !values.ContainsKey(key)))
        {
            throw new ArgumentException(Usage);
        }

        DaggerfallBuildingNameInputs inputs = DaggerfallBuildingNameInputsBuilder.Build(
            File.ReadAllBytes(values["--maps-file"]),
            values["--label"]);
        Console.WriteLine($"building-name inputs: {inputs.RegionNameBanks.Count} regions from {inputs.Source.Path}");
        if (!update)
        {
            Console.WriteLine("pack: not written (rerun with --update to publish building-name inputs into it)");
            return 0;
        }

        string existing = File.ReadAllText(values["--pack"]);
        string updated = TopLevelJsonSectionRewriter.ReplaceOrAppend(existing, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["buildingNames"] = JsonSerializer.Serialize(inputs, PublishedJson.Section),
        });
        File.WriteAllText(values["--pack"], updated);
        Console.WriteLine($"pack: building-name inputs updated in {values["--pack"]}");
        return 0;
    }

    /// <summary>
    /// Reads the supplied book files: identity from the file stem, bytes under the logical path the
    /// repository documents. A stem that carries no identity is refused rather than published under
    /// a guessed one.
    /// </summary>
    private static IReadOnlyList<(int BookId, string Label, byte[] Bytes)> ReadBooks(string directory)
    {
        List<(int BookId, string Label, byte[] Bytes)> books = [];
        foreach (string path in Directory.EnumerateFiles(directory, "BOK*.TXT").Order(StringComparer.Ordinal))
        {
            string stem = Path.GetFileNameWithoutExtension(path);
            if (stem.Length != 8 || !int.TryParse(stem[3..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int bookId))
            {
                throw new InvalidOperationException($"'{stem}' does not carry a book identity, so it cannot be enumerated as a book.");
            }

            books.Add((bookId, Path.Combine(directory, Path.GetFileName(path)).Replace(Path.DirectorySeparatorChar, '/'), File.ReadAllBytes(path)));
        }

        return books;
    }

    /// <summary>
    /// Reads the classic faction file into the base pack when asked, so a region, temple, guild or
    /// court resolves to the filed relations and bindings the source states rather than to policy
    /// inferred from them. Reputation, rank and service behavior stay out: the catalog publishes
    /// filed values, and the social consumers that read them own what those values do.
    /// </summary>
    private static int RunFactionsCommand(IReadOnlyList<string> args)
    {
        const string Usage = "usage: daggerfall-import-tool factions --arena2 SOURCE_DIR --pack PACK.json --inventory CSV [--update]";
        bool update = args.Contains("--update", StringComparer.Ordinal);
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        for (int index = 1; index < args.Count; index++)
        {
            string argument = args[index];
            if (argument == "--update") continue;
            if (!argument.StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Count || !values.TryAdd(argument, args[++index]))
            {
                throw new ArgumentException(Usage);
            }
        }

        string[] accepted = ["--arena2", "--pack", "--inventory"];
        if (values.Count != accepted.Length || accepted.Any(key => !values.ContainsKey(key)))
        {
            throw new ArgumentException(Usage);
        }

        string arena2 = values["--arena2"];
        IReadOnlyList<SourceInventoryRow> inventory = SourceManifestBuilder.ReadInventory(File.ReadAllBytes(values["--inventory"]));
        string label = Path.Combine(arena2, "FACTION.TXT");
        byte[] bytes = File.ReadAllBytes(label);
        DaggerfallFactions factions = DaggerfallFactionsBuilder.Build(File.ReadAllText(label), label, bytes, inventory);

        Console.WriteLine($"factions: {factions.Factions.Count} records, {factions.Regions.Count(region => region.Disposition == DaggerfallRegionFactionDisposition.Claimed)} claimed regions, {factions.DuplicateNames.Count} duplicated names");
        foreach (DaggerfallFactionNameAlias alias in factions.DuplicateNames)
        {
            Console.WriteLine($"  {alias.Name}: [{string.Join(", ", alias.Ids)}] resolves to {alias.ResolvedId}");
        }

        if (!update)
        {
            Console.WriteLine("pack: not written (rerun with --update to publish these factions into it)");
            return 0;
        }

        JsonNode pack = JsonNode.Parse(File.ReadAllText(values["--pack"]))!.AsObject();
        pack["factions"] = JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(factions, PublishedJson.Section));
        File.WriteAllText(values["--pack"], pack.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
        Console.WriteLine($"pack: factions updated in {values["--pack"]}");
        return 0;
    }

    /// <summary>
    /// Reads the classic wilderness file into the base pack when asked, so an exterior consumer
    /// resolves a map pixel by the coordinates the source tiles rather than by a second spatial
    /// system. The heightmap tiles rows; the cell samples travel as one span; the prefix bytes no
    /// reader consumes stay documented domains rather than republished bytes.
    /// </summary>
    private static int RunTerrainCommand(IReadOnlyList<string> args)
    {
        const string Usage = "usage: daggerfall-import-tool terrain --arena2 SOURCE_DIR --pack PACK.json --inventory CSV [--update]";
        bool update = args.Contains("--update", StringComparer.Ordinal);
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        for (int index = 1; index < args.Count; index++)
        {
            string argument = args[index];
            if (argument == "--update") continue;
            if (!argument.StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Count || !values.TryAdd(argument, args[++index]))
            {
                throw new ArgumentException(Usage);
            }
        }

        string[] accepted = ["--arena2", "--pack", "--inventory"];
        if (values.Count != accepted.Length || accepted.Any(key => !values.ContainsKey(key)))
        {
            throw new ArgumentException(Usage);
        }

        string arena2 = values["--arena2"];
        IReadOnlyList<SourceInventoryRow> inventory = SourceManifestBuilder.ReadInventory(File.ReadAllBytes(values["--inventory"]));
        string label = Path.Combine(arena2, "WOODS.WLD");
        DaggerfallTerrain terrain = DaggerfallTerrainBuilder.Build(File.ReadAllBytes(label), label, inventory);

        Console.WriteLine($"terrain: {terrain.Heightmap.Count} heightmap rows, {terrain.CellCount} cells from {terrain.CellBase} stride {terrain.CellStride}, {terrain.Prefix.Count} prefix domains");
        if (!update)
        {
            Console.WriteLine("pack: not written (rerun with --update to publish this terrain into it)");
            return 0;
        }

        JsonNode pack = JsonNode.Parse(File.ReadAllText(values["--pack"]))!.AsObject();
        pack["terrain"] = JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(terrain, PublishedJson.Section));
        File.WriteAllText(values["--pack"], pack.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
        Console.WriteLine($"pack: terrain updated in {values["--pack"]}");
        return 0;
    }

    /// <summary>
    /// Reads donor-shaped quest text into the base pack when asked, so a quest source resolves
    /// to its messages and finite top-level QBN blocks rather than to a binary blob. Action
    /// bodies remain ordered source lines for later action compilation; an unclaimed top-level
    /// line diagnoses the source.
    /// </summary>
    private static int RunQuestsCommand(IReadOnlyList<string> args)
    {
        const string Usage = "usage: daggerfall-import-tool quests --arena2 SOURCE_DIR --quest-text SOURCE_DIR --tables TABLE_DIR --pack PACK.json --inventory CSV [--update]";
        bool update = args.Contains("--update", StringComparer.Ordinal);
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        for (int index = 1; index < args.Count; index++)
        {
            string argument = args[index];
            if (argument == "--update") continue;
            if (!argument.StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Count || !values.TryAdd(argument, args[++index]))
            {
                throw new ArgumentException(Usage);
            }
        }

        string[] accepted = ["--arena2", "--quest-text", "--tables", "--pack", "--inventory"];
        if (values.Count != accepted.Length || accepted.Any(key => !values.ContainsKey(key)))
        {
            throw new ArgumentException(Usage);
        }

        IReadOnlyList<SourceInventoryRow> inventory = SourceManifestBuilder.ReadInventory(File.ReadAllBytes(values["--inventory"]));
        DaggerfallQuestTables tables = new(
            DaggerfallQuestTableReader.Read(File.ReadAllBytes(Path.Combine(values["--tables"], "Quests-GlobalVars.txt")), "Tables/Quests-GlobalVars.txt", globals: true),
            DaggerfallQuestTableReader.Read(File.ReadAllBytes(Path.Combine(values["--tables"], "Quests-StaticMessages.txt")), "Tables/Quests-StaticMessages.txt"),
            DaggerfallQuestPlaceReader.Read(File.ReadAllBytes(Path.Combine(values["--tables"], "Quests-Places.txt")), "Tables/Quests-Places.txt"),
            DaggerfallQuestTableReader.Read(File.ReadAllBytes(Path.Combine(values["--tables"], "Quests-Sounds.txt")), "Tables/Quests-Sounds.txt"),
            DaggerfallQuestTableReader.Read(File.ReadAllBytes(Path.Combine(values["--tables"], "Quests-Diseases.txt")), "Tables/Quests-Diseases.txt"),
            DaggerfallQuestTableReader.Read(File.ReadAllBytes(Path.Combine(values["--tables"], "Quests-Spells.txt")), "Tables/Quests-Spells.txt"),
            DaggerfallQuestActorItemTableReader.Read(
                File.ReadAllBytes(Path.Combine(values["--tables"], "Quests-Items.txt")), "Tables/Quests-Items.txt",
                File.ReadAllBytes(Path.Combine(values["--tables"], "Quests-Factions.txt")), "Tables/Quests-Factions.txt",
                File.ReadAllBytes(Path.Combine(values["--tables"], "Quests-Foes.txt")), "Tables/Quests-Foes.txt"));
        IReadOnlyDictionary<string, int> messageIds = tables.StaticMessages.Lookup;
        IReadOnlyDictionary<string, int> globalKeys = tables.Globals.Lookup;
        List<QuestSourceDocument> documents = [];
        List<(string FileName, string QuestName, int Line, string Reason)> failures = [];
        long totalBytes = 0;
        foreach (string path in Directory.EnumerateFiles(values["--quest-text"], "*.txt").Order(StringComparer.Ordinal))
        {
            string fileName = Path.GetFileName(path);
            string text = File.ReadAllText(path);
            totalBytes += new FileInfo(path).Length;
            try
            {
                documents.Add(QuestSourceReader.Read(text, fileName, messageIds, globalKeys));
            }
            catch (Arena2FormatException exception)
            {
                failures.Add((fileName, Path.GetFileNameWithoutExtension(path), exception.Offset, exception.Message));
            }
        }

        DaggerfallQuestPack pack = DaggerfallQuestPackBuilder.Build(documents, failures, "donor/StreamingAssets/Quests", new byte[totalBytes], inventory);
        DaggerfallQuestCatalog catalog = DaggerfallQuestCatalogReader.Read(
            File.ReadAllBytes(Path.Combine(values["--tables"], "QuestList-Classic.txt")), "Tables/QuestList-Classic.txt",
            Directory.EnumerateFiles(values["--quest-text"], "*.txt"));
        string[] originalPaths = [.. Directory.EnumerateFiles(values["--arena2"])
            .Where(path => path.EndsWith(QuestSourceInventory.BinaryExtension, StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(QuestSourceInventory.ResourcesExtension, StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFileName)
            .OfType<string>()];
        string originalLabel = Path.GetFileName(Path.TrimEndingDirectorySeparator(values["--arena2"]));
        QuestSourceInventory originalInventory = QuestSourceInventory.Enumerate(originalPaths,
            string.IsNullOrEmpty(originalLabel) ? values["--arena2"] : originalLabel);
        DaggerfallQuestOriginalSourceSet originals = DaggerfallQuestOriginalSourceBuilder.Build(values["--arena2"], originalInventory, pack);
        Console.WriteLine($"classic catalog: {catalog.Rows.Count(row => row.Active)} active, {catalog.Rows.Count(row => !row.Active)} disabled, {catalog.Rows.Count(row => row.SourceDisposition == "missing")} missing sources");
        Console.WriteLine($"quests: {pack.Quests.Count} sources, {pack.Quests.Count(quest => quest.Disposition == DaggerfallQuestDisposition.Compiled)} compiled");
        Console.WriteLine($"original quest sources: {originals.Quests.Count} stems, {originals.Quests.Count(quest => quest.Selection == DaggerfallQuestOriginalSourceSelection.RewrittenText)} rewritten text selections, {originals.Quests.Count(quest => quest.Selection != DaggerfallQuestOriginalSourceSelection.RewrittenText)} not enabled");
        if (!update)
        {
            Console.WriteLine("pack: not written (rerun with --update to publish these sources into it)");
            return 0;
        }

        string existing = File.ReadAllText(values["--pack"]);
        string updated = TopLevelJsonSectionRewriter.ReplaceOrAppend(existing, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["questCatalog"] = System.Text.Json.JsonSerializer.Serialize(catalog, PublishedJson.Section),
            ["questTables"] = System.Text.Json.JsonSerializer.Serialize(tables, PublishedJson.Section),
            ["questSources"] = System.Text.Json.JsonSerializer.Serialize(pack, PublishedJson.Section),
            ["questOriginalSources"] = System.Text.Json.JsonSerializer.Serialize(originals, PublishedJson.Section),
        });
        File.WriteAllText(values["--pack"], updated);
        Console.WriteLine($"pack: quest sources updated in {values["--pack"]}");
        return 0;
    }

    /// <summary>
    /// Records cinematic source identities without shipping media: digests tell files apart and
    /// donor callers bind the opening three VIDs and the sixteen Daedric FLCs, while the rest stay
    /// unresolved rather than assuming an ending mapping.
    /// </summary>
    private static int RunVideosCommand(IReadOnlyList<string> args)
    {
        const string Usage = "usage: daggerfall-import-tool videos --arena2 SOURCE_DIR --pack PACK.json --inventory CSV [--update]";
        bool update = args.Contains("--update", StringComparer.Ordinal);
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        for (int index = 1; index < args.Count; index++)
        {
            string argument = args[index];
            if (argument == "--update") continue;
            if (!argument.StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Count || !values.TryAdd(argument, args[++index]))
            {
                throw new ArgumentException(Usage);
            }
        }

        string[] accepted = ["--arena2", "--pack", "--inventory"];
        if (values.Count != accepted.Length || accepted.Any(key => !values.ContainsKey(key)))
        {
            throw new ArgumentException(Usage);
        }

        IReadOnlyList<SourceInventoryRow> inventory = SourceManifestBuilder.ReadInventory(File.ReadAllBytes(values["--inventory"]));
        List<(string FileName, DaggerfallCinematicKind Kind, long ByteLength, string Digest)> files = [];
        foreach (string path in Directory.EnumerateFiles(values["--arena2"]).Order(StringComparer.OrdinalIgnoreCase))
        {
            string fileName = Path.GetFileName(path);
            DaggerfallCinematicKind? kind = fileName.EndsWith(".VID", StringComparison.OrdinalIgnoreCase)
                ? DaggerfallCinematicKind.Vid
                : fileName.EndsWith(".FLC", StringComparison.OrdinalIgnoreCase) ? DaggerfallCinematicKind.Flc : null;
            if (kind is null) continue;
            byte[] bytes = File.ReadAllBytes(path);
            files.Add((fileName, kind.Value, bytes.LongLength, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))));
        }

        DaggerfallCinematicPack pack = DaggerfallCinematicPackBuilder.Build(files, "local/arena2", inventory);
        Console.WriteLine($"videos: {pack.Cinematics.Count} cinematics, {pack.Cinematics.Count(record => record.Binding == DaggerfallCinematicBinding.Bound)} bound");
        if (!update)
        {
            Console.WriteLine("pack: not written (rerun with --update to publish these identities into it)");
            return 0;
        }

        JsonNode node = JsonNode.Parse(File.ReadAllText(values["--pack"]))!.AsObject();
        node["cinematics"] = JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(pack, PublishedJson.Section));
        File.WriteAllText(values["--pack"], node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
        Console.WriteLine($"pack: cinematic identities updated in {values["--pack"]}");
        return 0;
    }

    /// <summary>
    /// Reads the donor's exported template tables into the base pack when asked, so a native
    /// template index resolves to the substitute record the donor states rather than to a
    /// placeholder. Every record carries substitute provenance; the ledger targets resolve to it.
    /// </summary>
    private static int RunItemsCommand(IReadOnlyList<string> args)
    {
        const string Usage = "usage: daggerfall-import-tool items --arena2 SOURCE_DIR --pack PACK.json --inventory CSV --item-templates FILE --magic-templates FILE [--update]";
        bool update = args.Contains("--update", StringComparer.Ordinal);
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        for (int index = 1; index < args.Count; index++)
        {
            string argument = args[index];
            if (argument == "--update") continue;
            if (!argument.StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Count || !values.TryAdd(argument, args[++index]))
            {
                throw new ArgumentException(Usage);
            }
        }

        string[] accepted = ["--arena2", "--pack", "--inventory", "--item-templates", "--magic-templates"];
        if (values.Count != accepted.Length || accepted.Any(key => !values.ContainsKey(key)))
        {
            throw new ArgumentException(Usage);
        }

        IReadOnlyList<SourceInventoryRow> inventory = SourceManifestBuilder.ReadInventory(File.ReadAllBytes(values["--inventory"]));
        // The substitute tables are read by file name, not by directory: the provenance label
        // names the donor export, so a file that is not that export is refused rather than
        // published under its name.
        if (!StringComparer.Ordinal.Equals(Path.GetFileName(values["--item-templates"]), "ItemTemplates.txt")
            || !StringComparer.Ordinal.Equals(Path.GetFileName(values["--magic-templates"]), "MagicItemTemplates.txt"))
        {
            throw new ArgumentException("The item substitute tables must be the donor's ItemTemplates.txt and MagicItemTemplates.txt exports.");
        }

        string label = "donor/Assets/Resources/ItemTemplates.txt";
        string magicLabel = "donor/Assets/Resources/MagicItemTemplates.txt";
        IReadOnlyList<SubstituteItemTemplate> substitutes = ItemTemplateReader.ReadTemplates(File.ReadAllText(values["--item-templates"]), label);
        IReadOnlyList<SubstituteMagicTemplate> magic = ItemTemplateReader.ReadMagic(File.ReadAllText(values["--magic-templates"]), magicLabel);
        byte[] bytes = File.ReadAllBytes(values["--item-templates"]);
        DaggerfallItemTemplates catalog = DaggerfallItemTemplatesBuilder.Build(substitutes, magic, label, bytes, inventory);

        Console.WriteLine($"items: {catalog.Templates.Count} templates, {catalog.Magic.Count} magic templates");
        if (!update)
        {
            Console.WriteLine("pack: not written (rerun with --update to publish these templates into it)");
            return 0;
        }

        JsonNode pack = JsonNode.Parse(File.ReadAllText(values["--pack"]))!.AsObject();
        pack["itemTemplates"] = JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(catalog, PublishedJson.Section));
        ResolveLedgerTargets(pack, catalog);
        File.WriteAllText(values["--pack"], pack.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
        Console.WriteLine($"pack: item templates updated in {values["--pack"]}");
        return 0;
    }

    private static void ResolveLedgerTargets(JsonNode pack, DaggerfallItemTemplates catalog)
    {
        if (pack["itemTemplateLedger"]?["targets"] is not JsonArray targets) return;
        Dictionary<int, DaggerfallItemTemplate> resolved = catalog.Templates.ToDictionary(template => template.Index);
        foreach (JsonNode? target in targets)
        {
            if (target is null) continue;
            int index = target["index"]!.GetValue<int>();
            if (resolved.TryGetValue(index, out DaggerfallItemTemplate? _))
            {
                // The disposition moves to the substitute table; the provenance stays, because
                // it records how the target's groups were attributed, which the substitute
                // read does not change.
                target["disposition"] = "substitute";
            }
        }
    }

    /// <summary>
    /// Enumerates the classic mesh archive into the base pack, so a mesh identity is published whether or
    /// not anything references it and the blocks that do reference one can be answered.
    /// </summary>
    /// <remarks>
    /// The use sites come from the pack's own block section: which blocks name a mesh is a fact the block
    /// inventory publishes, and reading it there keeps one owner for it. Running this before blocks exists
    /// is refused rather than publishing an inventory whose use sites are silently empty.
    /// </remarks>
    private static int RunGeometryCommand(IReadOnlyList<string> args)
    {
        const string Usage = "usage: daggerfall-import-tool geometry --arena2 SOURCE_DIR --pack PACK.json --inventory CSV [--update]";
        bool update = args.Contains("--update", StringComparer.Ordinal);
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        for (int index = 1; index < args.Count; index++)
        {
            string argument = args[index];
            if (argument == "--update") continue;
            if (!argument.StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Count || !values.TryAdd(argument, args[++index]))
            {
                throw new ArgumentException(Usage);
            }
        }

        string[] accepted = ["--arena2", "--pack", "--inventory"];
        if (values.Count != accepted.Length || accepted.Any(key => !values.ContainsKey(key)))
        {
            throw new ArgumentException(Usage);
        }

        JsonNode pack = JsonNode.Parse(File.ReadAllText(values["--pack"]))!.AsObject();
        if (pack["blocks"] is not JsonObject blocks)
        {
            throw new ArgumentException("the pack carries no blocks section, which is where mesh use sites come from: run the blocks command first");
        }

        // The section is read through its own contract rather than by matching member names here. A pack
        // whose block section this build cannot read has to refuse: walking members by name would fold a
        // shape it does not recognize into a geometry section where every mesh is unused, which is the
        // opposite of the closure set this inventory exists to publish.
        DaggerfallBlocks publishedBlocks = blocks.Deserialize<DaggerfallBlocks>(PublishedJson.SectionRead)
            ?? throw new ArgumentException("the pack's blocks section could not be read");
        publishedBlocks.Validate();

        List<DaggerfallGeometryUseSite> useSites = [];
        foreach (DaggerfallBlockRecord block in publishedBlocks.Records)
        {
            foreach (string model in block.Objects?.ModelIds ?? [])
            {
                useSites.Add(new DaggerfallGeometryUseSite(model, block.SourceKey));
            }
        }

        // The documented inventory decides the logical source identity, so the bytes are read under the
        // path the repository documents rather than under whatever directory the caller happened to name.
        string source = Path.Combine(values["--arena2"], Arch3dInventoryReader.FileName);
        DaggerfallGeometry geometry = DaggerfallGeometryBuilder.Build(
            File.ReadAllBytes(source),
            source,
            SourceManifestBuilder.ReadInventory(File.ReadAllBytes(values["--inventory"])),
            useSites);

        DaggerfallGeometrySource publishedSource = geometry.Sources[0];
        Console.WriteLine($"geometry: {geometry.Records.Count} records from {publishedSource.Path}, declared {publishedSource.DeclaredLength}, {geometry.Records.Count(record => record.State == DaggerfallGeometryState.Malformed)} malformed, {geometry.Records.Sum(record => record.Facts?.Planes ?? 0)} planes");
        foreach (IGrouping<DaggerfallGeometryDisposition, DaggerfallGeometryRecord> disposition in geometry.Records.GroupBy(record => record.Disposition).OrderBy(group => group.Key))
        {
            Console.WriteLine($"  {disposition.Count()} {disposition.Key.ToString().ToLowerInvariant()}");
        }

        foreach (IGrouping<string, DaggerfallGeometryRecord> version in geometry.Records.Where(record => record.Facts is not null).GroupBy(record => record.Facts!.Version).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            Console.WriteLine($"  {version.Count()} records state {version.Key}");
        }

        Console.WriteLine($"  {geometry.Records.Count(record => record.DuplicateOf is not null)} records reuse an earlier number, {geometry.Records.Count(record => record.PayloadDuplicateOf is not null)} repeat an earlier record's bytes");
        Console.WriteLine($"  unresolved use sites: {(geometry.UnresolvedUseSites.Count == 0 ? "none" : string.Join(", ", geometry.UnresolvedUseSites.Select(unresolved => unresolved.MeshId))) }");
        if (!update)
        {
            Console.WriteLine("pack: not written (rerun with --update to publish this inventory into it)");
            return 0;
        }

        pack["geometry"] = JsonNode.Parse(JsonSerializer.Serialize(geometry, PublishedJson.Section));
        File.WriteAllText(values["--pack"], pack.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
        Console.WriteLine($"pack: geometry updated in {values["--pack"]}");
        return 0;
    }

    /// <summary>
    /// Enumerates the classic block archive into the base pack, so the tasks that publish dungeons,
    /// exteriors and geometry start from one inventory of what the corpus carries rather than decoding
    /// the archive again and disagreeing about what a name means.
    /// </summary>
    private static int RunBlocksCommand(IReadOnlyList<string> args)
    {
        const string Usage = "usage: daggerfall-import-tool blocks --arena2 SOURCE_DIR --pack PACK.json --inventory CSV [--update]";
        bool update = args.Contains("--update", StringComparer.Ordinal);
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        for (int index = 1; index < args.Count; index++)
        {
            string argument = args[index];
            if (argument == "--update") continue;
            if (!argument.StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Count || !values.TryAdd(argument, args[++index]))
            {
                throw new ArgumentException(Usage);
            }
        }

        string[] accepted = ["--arena2", "--pack", "--inventory"];
        if (values.Count != accepted.Length || accepted.Any(key => !values.ContainsKey(key)))
        {
            throw new ArgumentException(Usage);
        }

        // The documented inventory decides the logical source identity, so the bytes are read under the
        // path the repository documents rather than under whatever directory the caller happened to name.
        string source = Path.Combine(values["--arena2"], BlockRecordInventoryReader.FileName);
        DaggerfallBlocks blocks = DaggerfallBlocksBuilder.Build(
            File.ReadAllBytes(source),
            source,
            SourceManifestBuilder.ReadInventory(File.ReadAllBytes(values["--inventory"])));

        DaggerfallBlockSource publishedSource = blocks.Sources[0];
        Console.WriteLine($"blocks: {blocks.Records.Count} records from {publishedSource.Path}, declared {publishedSource.DeclaredLength}, {blocks.Records.Count(record => record.State == DaggerfallBlockState.Malformed)} malformed");
        foreach (IGrouping<DaggerfallBlockKind, DaggerfallBlockRecord> kind in blocks.Records.GroupBy(record => record.Kind).OrderBy(group => group.Key))
        {
            Console.WriteLine($"  {kind.Count()} {kind.Key.ToString().ToLowerInvariant()}");
        }

        Console.WriteLine($"  rdb types: {string.Join(", ", blocks.Records.Where(record => record.Kind == DaggerfallBlockKind.Rdb).GroupBy(record => record.RdbName!.Type).OrderBy(group => group.Key).Select(group => $"{group.Key.ToString().ToLowerInvariant()} {group.Count()}"))}");
        string[] unresolved = [.. blocks.Records.Where(record => record.State == DaggerfallBlockState.Malformed).Select(record => record.SourceKey)];
        if (unresolved.Length != 0)
        {
            Console.WriteLine($"  malformed records: {string.Join(", ", unresolved)}");
        }

        if (!update)
        {
            Console.WriteLine("pack: not written (rerun with --update to publish this inventory into it)");
            return 0;
        }

        // Placements ride in their own payload beside the pack: 621k records would triple the
        // base pack, while the base section keeps the counts existing consumers read. The full
        // document is validated before either file is written, so the two never disagree.
        DaggerfallBlocks stripped = StripPlacements(blocks);
        JsonNode pack = JsonNode.Parse(File.ReadAllText(values["--pack"]))!.AsObject();
        pack["blocks"] = JsonNode.Parse(JsonSerializer.Serialize(stripped, PublishedJson.Section));
        File.WriteAllText(values["--pack"], pack.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
        Console.WriteLine($"pack: blocks updated in {values["--pack"]}");
        string payloadPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(values["--pack"]))!, "daggerfall.blocks.json");
        File.WriteAllText(payloadPath, JsonSerializer.Serialize(blocks, PublishedJson.SectionCompact) + "\n");
        Console.WriteLine($"pack: block placements updated in {payloadPath}");
        return 0;
    }

    private static DaggerfallBlocks StripPlacements(DaggerfallBlocks blocks)
    {
        List<DaggerfallBlockRecord> records = [];
        foreach (DaggerfallBlockRecord record in blocks.Records)
        {
            DaggerfallBlockObjects? objects = record.Objects is null ? null : record.Objects with
            {
                ModelPlacements = [],
                FlatPlacements = [],
                LightPlacements = [],
                DoorPlacements = [],
            };
            DaggerfallBlockRmbHeader? rmb = record.Rmb;
            if (rmb is not null)
            {
                rmb = rmb with
                {
                    Buildings = [.. rmb.Buildings.Select(building => building with
                    {
                        ExteriorPlacements = EmptyHalf(),
                        InteriorPlacements = EmptyHalf(),
                    })],
                };
            }

            records.Add(record with { Objects = objects, Rmb = rmb, RmbPlacements = null });
        }

        return blocks with { Records = records };
    }

    private static DaggerfallBlockHalfPlacements EmptyHalf() => new([], [], [], [], []);

    /// <summary>
    /// Publishes the classic media into a content root, so the artifacts are admitted content a
    /// consumer reads by name rather than bytes that only exist inside this process.
    /// </summary>
    /// <remarks>
    /// The inventory is written beside the artifacts from the publication's own artifact list. It is
    /// generated rather than maintained by hand: every earlier hand-kept index in this repository has
    /// drifted from what it indexed, and the point of writing this one is that a rebuild rewrites it.
    /// </remarks>
    private static int RunClassicMediaCommand(IReadOnlyList<string> args)
    {
        const string Usage = "usage: daggerfall-import-tool classic-media --arena2 SOURCE_DIR --out CONTENT_ROOT [--group NAME] [--ui-authored-assets FILE --ui-original DIR] [--update]";
        bool update = args.Contains("--update", StringComparer.Ordinal);
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        for (int index = 1; index < args.Count; index++)
        {
            string argument = args[index];
            if (argument == "--update") continue;
            if (!argument.StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Count || !values.TryAdd(argument, args[++index]))
            {
                throw new ArgumentException(Usage);
            }
        }

        bool authored = values.ContainsKey("--ui-authored-assets") && values.ContainsKey("--ui-original");
        string[] accepted = authored
            ? ["--arena2", "--out", "--group", "--ui-authored-assets", "--ui-original"]
            : ["--arena2", "--out", "--group"];
        if (values.Count != accepted.Length || accepted.Any(key => !values.ContainsKey(key)))
        {
            throw new ArgumentException(Usage);
        }

        string group = values["--group"];
        string arena2 = values["--arena2"];

        // Paths are content-root relative, which is the naming the product's admitted content carries:
        // the group is part of the path, so a consumer holds one name for an artifact and the inventory
        // the generator writes uses that same name rather than the group-relative one it used to.
        string outRoot = Path.Combine(values["--out"], group);
        Console.WriteLine($"group: {group} under {values["--out"]}");
        AdmittedArena2Sources sources = new(arena2);
        LoadClassicMediaSources(sources);
        // The authored UI art is part of the same group the DOM reads, so its identity, bytes and
        // inventory entry come from the one publication rather than a copy staged beside the UI.
        Arena2ClassicMediaPublication publication = authored
            ? Arena2ClassicMediaPublication.Create(sources.ClassicMediaInputs, LoadClassicMediaProfile(values["--ui-authored-assets"], values["--ui-original"]))
            : Arena2ClassicMediaPublication.Create(sources.ClassicMediaInputs);

        // The catalog is published beside the clips it describes and its admitted entries are the
        // publication's own audio manifests, so "a published artifact carries this clip" is a
        // reference to emitted bytes rather than a second list the catalog keeps in agreement.
        DaggerfallSoundCatalog catalog = DaggerfallSoundCatalogBuilder.Build(
            SoundArchive.Parse(sources.ClassicMediaInputs.DaggerSound, Arena2ClassicMediaPublication.DaggerSoundSourcePath),
            publication.SoundAdmissions);
        List<ImportPublicationArtifact> published = [.. publication.Artifacts, new ImportPublicationArtifact(DaggerfallSoundCatalogJson.RelativePath, DaggerfallSoundCatalogJson.Write(catalog))];
        Console.WriteLine($"classic media: {publication.Artifacts.Count} artifacts, {publication.Sources.Count} sources, {publication.UiImages.Count} UI images");
        int admitted = catalog.Clips.Count(clip => clip.Disposition == DaggerfallSoundClipDisposition.Admitted);
        int readable = catalog.Clips.Count(clip => clip.Disposition == DaggerfallSoundClipDisposition.ReadableNoConsumer);
        Console.WriteLine($"sound catalog: {catalog.Clips.Count} clips, {admitted} admitted, {readable} readable with no consumer, {catalog.Clips.Count - admitted - readable} unsupported");
        if (!update)
        {
            Console.WriteLine("content: not written (rerun with --update to publish these artifacts)");
            return 0;
        }

        // The publication's artifact paths already begin with 'media/', so the root is the content
        // directory itself: prefixing another media/ published everything one level too deep.
        foreach (ImportPublicationArtifact artifact in published)
        {
            string path = Path.Combine(outRoot, artifact.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, artifact.Bytes.ToArray());
        }

        // The inventory names each artifact with its byte length and digest, so a consumer can tell
        // whether the content it admitted is the content this publication produced. An artifact that
        // carries a media identity states it here, which is how a consumer resolves a published name
        // to bytes through generated data rather than a list kept beside it.
        // A published UI image states the semantic screen it fills, so a consumer binds "the book
        // reader" rather than a file name it would have to know.
        Dictionary<string, string> slots = publication.UiImages
            .GroupBy(image => image.MediaId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Single().Slot.ToString(), StringComparer.Ordinal);
        // A screen drawn with the palette inside its own file states that as a conversion fact with its
        // donor anchor: the bytes are the file's trailing palette scaled by four, which is why dropping
        // the scale darkens every colour in the artifact.
        Dictionary<string, bool> ownPalettes = publication.UiImages
            .GroupBy(image => image.MediaId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Single().OwnEmbeddedPalette, StringComparer.Ordinal);
        JsonObject InventoryEntry(ImportPublicationArtifact artifact)
        {
            JsonObject entry = new()
            {
                ["path"] = $"{group}/{artifact.RelativePath}",
                ["byteLength"] = artifact.Bytes.Length,
                ["sha256"] = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(artifact.Bytes.Span)),
            };
            if (artifact.MediaId is { } mediaId)
            {
                entry["mediaId"] = mediaId;
                if (slots.TryGetValue(mediaId, out string? slot)) entry["slot"] = char.ToLowerInvariant(slot[0]) + slot[1..];
                if (ownPalettes.TryGetValue(mediaId, out bool own) && own)
                {
                    entry["palette"] = new JsonObject
                    {
                        ["source"] = "embedded-in-source-file",
                        ["channelScale"] = 4,
                        ["donorAnchor"] = "ImgFile.ReadPalette",
                    };
                }
            }

            return entry;
        }

        // A family that carries no published canvas is stated rather than left as an absence, and the
        // reason says which half is missing: these two containers are read here, so the gap is a missing
        // publisher rather than a missing decoder, and the donor anchor says where the original
        // behaviour lives.
        JsonArray unreadable = [];
        foreach ((string family, string kind, string reason, string anchor) in new[]
        {
            // The classic media group publishes no canvas from these two families. The character group does
            // publish the class portraits from the CEL files, through its own command, so the reason says
            // which group is speaking rather than claiming the bytes have no canvas anywhere.
            (".CEL", "class-question animation", "no publisher in this group: the FLC container and its frames are read, and the character group publishes the class portraits from them, but nothing here emits an artifact from these files and nothing plays them back, so this group carries no canvas for them", "Assets/Scripts/API/FlcFile.cs"),
            (".BSS", "compass sprite bank", "no publisher in this group: the BSS container is decoded and its frames are published through the character group, but this classic-media group emits no artifact from these files", "Assets/Scripts/API/BssFile.cs"),
            // No family entry for the CIF files: most of the corpus's CIFs are weapon, armour and painting
            // grammars this repository reads and publishes, and the face grammar it refuses is refused by
            // name when a face is read rather than being a family that carries no artifact at all.
        })
        {
            string[] files = [.. Directory.EnumerateFiles(arena2, "*" + family, SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .Where(name => name is not null)
                .Select(name => name!)
                .Order(StringComparer.OrdinalIgnoreCase)];
            if (files.Length == 0) continue;
            unreadable.Add(new JsonObject
            {
                ["family"] = family,
                ["kind"] = kind,
                ["files"] = new JsonArray([.. files.Select(file => JsonValue.Create(file))]),
                ["reason"] = reason,
                ["donorAnchor"] = anchor,
            });
        }

        JsonObject inventory = new()
        {
            ["generator"] = "daggerfall-import-tool classic-media",
            ["unreadableFamilies"] = unreadable,
            ["artifacts"] = new JsonArray([.. published
                .OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal)
                .Select(artifact => (JsonNode)InventoryEntry(artifact))]),
        };
        File.WriteAllText(Path.Combine(outRoot, "media", "classic-media-inventory.json"), inventory.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
        Console.WriteLine($"content: {published.Count} artifacts and their inventory written under {outRoot}");
        return 0;
    }

    private static IReadOnlyDictionary<string, string> ReadPackCareers(string packFile)
    {
        JsonNode root = JsonNode.Parse(File.ReadAllText(packFile))!.AsObject();
        return root["catalogs"]!["careers"]!.AsArray().ToDictionary(
            value => value!.AsObject()["id"]!.GetValue<string>(),
            value => value!.AsObject()["name"]!.GetValue<string>(),
            StringComparer.Ordinal);
    }

    /// <summary>The races the catalog section publishes, which are what the layers are keyed by.</summary>
    private static IReadOnlyList<DaggerfallRaceKey> ReadPackRaces(string packFile)
    {
        JsonNode root = JsonNode.Parse(File.ReadAllText(packFile))!.AsObject();
        return [.. root["catalogs"]!["races"]!.AsArray().Select(value =>
        {
            JsonObject race = value!.AsObject();
            JsonObject source = race["source"]!.AsObject();
            return new DaggerfallRaceKey(
                race["id"]!.GetValue<string>(),
                race["donorRaceId"]!.GetValue<int>(),
                new DaggerfallCatalogSource(source["recordId"]!.GetValue<string>(), source["path"]!.GetValue<string>()));
        })];
    }

    private static (List<string> Attributes, List<string> Skills) ReadVocabulary(string packFile)
    {
        JsonNode vocabulary = JsonNode.Parse(File.ReadAllText(packFile))!.AsObject()["vocabulary"]!;
        return (
            [.. vocabulary["attributes"]!.AsArray().Select(value => value!.GetValue<string>())],
            [.. vocabulary["skills"]!.AsArray().Select(value => value!.GetValue<string>())]);
    }

    private static (List<string> Enemies, List<string> Items) ReadPackKeys(string packFile)
    {
        JsonNode root = JsonNode.Parse(File.ReadAllText(packFile))!.AsObject();
        List<string> ids(string property) =>
            [.. root[property]!.AsArray().Select(value => value!.AsObject()["id"]!.GetValue<string>())];
        // The player is an actor identity but not an enemy a catalog references.
        return ([.. ids("actors").Where(id => id != "player")], ids("items"));
    }

    /// <summary>
    /// Reconciles the documented inventory against a real source tree and writes the
    /// machine-readable source manifest later normalizers consume. Which sources count
    /// as imported comes from a published manifest, so the record states what a
    /// consumer actually read rather than a second guess at it.
    /// </summary>
    private static int RunSourceManifestCommand(IReadOnlyList<string> args)
    {
        bool update = args.Contains("--update-inventory", StringComparer.Ordinal);
        if (args.Count != (update ? 10 : 9) || args[1] != "--arena2" || args[3] != "--inventory" || args[5] != "--output" || args[7] != "--publication")
        {
            throw new ArgumentException("usage: daggerfall-import-tool source-manifest --arena2 SOURCE_DIR --inventory INVENTORY.csv --output DIR --publication PUBLISHED_DIR [--update-inventory]");
        }

        string arena2 = args[2];
        string inventoryFile = args[4];
        string output = args[6];
        string publication = args[8];
        byte[] inventoryBytes = File.ReadAllBytes(inventoryFile);
        IReadOnlyList<SourceInventoryRow> inventory = SourceManifestBuilder.ReadInventory(inventoryBytes);
        CanonicalImportManifest published = ImportPublicationManifestSerializer.Deserialize(
            File.ReadAllBytes(Path.Combine(publication, ImportPublicationManifestSerializer.ManifestRelativePath)));
        SourceManifest complete = SourceManifestBuilder.Scan(
            new SourceManifestRequest("local/arena2", Path.GetFileName(inventoryFile), arena2,
                ImportedNames(published.Sources.Select(source => source.SourcePath)), [], ExcludedNames(inventory)),
            inventoryBytes);
        byte[] bytes = SourceManifestSerializer.Serialize(complete);
        string manifestPath = Path.Combine(output, SourceManifestSerializer.ManifestRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        File.WriteAllBytes(manifestPath, bytes);

        SourceManifest readback = SourceManifestSerializer.Deserialize(bytes);
        SourceManifestFamilyCount total = SourceManifestFamilyCount.From("total", "summary", readback.Records);
        Console.WriteLine($"source manifest: {total.Discovered} records across {readback.Families.Count(family => family.Discovered > 0)} supplied families");
        Console.WriteLine($"  imported {total.Imported}, unused {total.Unused}, source-gap {total.SourceGap}, required-pending {total.RequiredPending}, unresolved {total.Unresolved}, excluded {total.Excluded}, duplicate {total.Duplicate}, malformed {total.Malformed}");
        // Every family is printed, including the ones with no records: a documented
        // source gap or an excluded family is exactly what a reader needs to see, and
        // filtering by record count hides precisely those.
        foreach (SourceManifestFamilyCount family in readback.Families)
        {
            Console.WriteLine($"  {family.FamilyId} [{family.DocumentedDisposition}]: {family.Discovered} = {family.Imported} imported, {family.Unused} unused, {family.SourceGap} source-gap, {family.RequiredPending} pending, {family.Unresolved} unresolved, {family.Excluded} excluded, {family.Malformed} malformed");
        }

        Console.WriteLine($"manifest: {manifestPath}");
        Console.WriteLine($"digest: {ContentDigest.Compute(bytes).Value}");
        SourceInventoryReconciliation reconciliation = SourceInventoryReconciler.Reconcile(inventoryFile, readback.Records, update);
        foreach (string line in reconciliation.Drift)
        {
            Console.WriteLine($"inventory drift: {line}");
        }

        foreach (string line in reconciliation.Unreconciled.Take(20))
        {
            Console.WriteLine($"inventory unresolved: {line}");
        }

        // The status line is chosen from what reconciliation actually did, so it cannot
        // announce an update that was refused. A requested update that did not happen is
        // a failure the caller has to see, not a success with a caveat.
        string status = reconciliation switch
        {
            { IsClean: true } => "inventory: documented dispositions match the supplied tree",
            { Updated: true } => $"inventory: documented dispositions updated: {reconciliation.Drift.Count}",
            { UpdateBlocked: true } => $"inventory: the inventory file was NOT rewritten — unresolved documented rows: {reconciliation.Unreconciled.Count}, disagreements remaining: {reconciliation.Drift.Count}",
            { Unreconciled.Count: > 0 } => $"inventory: unresolved documented rows: {reconciliation.Unreconciled.Count}, disagreements: {reconciliation.Drift.Count}; --update-inventory cannot resolve an unresolved row",
            _ => $"inventory: disagreements to record: {reconciliation.Drift.Count} (rerun with --update-inventory)",
        };
        Console.Error.WriteLine(status);
        return reconciliation.UpdateBlocked ? 1 : 0;

    }

    /// <summary>
    /// The names a consumer's source set can claim by. A published source path keeps
    /// the directory it was read from, so both the path relative to the corpus root and
    /// the leaf are offered; the manifest decides which of them identifies one supplied
    /// file, rather than the caller discarding the path here.
    /// </summary>
    private static HashSet<string> ImportedNames(IEnumerable<string> sourcePaths) =>
        ClaimNames(sourcePaths);

    private static HashSet<string> ExcludedNames(IReadOnlyList<SourceInventoryRow> inventory) => ClaimNames(inventory
        .Where(row => StringComparer.Ordinal.Equals(row.Disposition, "excluded"))
        .Select(row => row.PathOrPattern));

    private static HashSet<string> ClaimNames(IEnumerable<string> sourcePaths)
    {
        HashSet<string> names = new(StringComparer.Ordinal);
        foreach (string sourcePath in sourcePaths)
        {
            names.Add(sourcePath);
            names.Add(sourcePath.Split('/')[^1]);
            int corpus = sourcePath.IndexOf("arena2/", StringComparison.Ordinal);
            if (corpus >= 0)
            {
                names.Add(sourcePath[(corpus + "arena2/".Length)..]);
            }
        }

        return names;
    }

    /// <summary>
    /// Adds the source manifest to a publication closure, so the record identities and
    /// dispositions a later normalizer cites travel with the published tree instead of
    /// existing only in an operator's working directory.
    /// </summary>
    private static ImportPublicationPlan AttachSourceManifest(ImportPublicationPlan plan, ToolOptions options)
    {
        if (options.InventoryFile is null)
        {
            return plan;
        }

        byte[] inventoryBytes = File.ReadAllBytes(options.InventoryFile);
        SourceManifest complete = SourceManifestBuilder.Scan(
            new SourceManifestRequest("local/arena2", Path.GetFileName(options.InventoryFile), options.Arena2Directory,
                ImportedNames(plan.Manifest.Sources.Select(source => source.SourcePath)), [], ExcludedNames(SourceManifestBuilder.ReadInventory(inventoryBytes))),
            inventoryBytes);
        // A publication that carries a source manifest is its first consumer: every
        // source it read has to appear as imported, or the artifact would claim
        // something the closure itself contradicts.
        HashSet<string> recorded = complete.Records
            .Where(record => record.Disposition == SourceRecordDisposition.Imported)
            .Select(record => record.SourcePath.Split('/')[^1])
            .ToHashSet(StringComparer.Ordinal);
        foreach (string source in plan.Manifest.Sources.Select(source => source.SourcePath.Split('/')[^1]))
        {
            if (complete.Records.Any(record => StringComparer.Ordinal.Equals(record.SourcePath.Split('/')[^1], source)) && !recorded.Contains(source))
            {
                throw new InvalidOperationException($"The closure read '{source}', but the source manifest it carries does not record it as imported.");
            }
        }

        SourceInventoryReconciliation reconciliation = SourceInventoryReconciler.Reconcile(options.InventoryFile!, complete.Records, update: false);
        foreach (string line in reconciliation.Drift)
        {
            Console.Error.WriteLine($"inventory drift: {line}");
        }

        foreach (string line in reconciliation.Unreconciled)
        {
            Console.Error.WriteLine($"inventory unresolved: {line}");
        }

        byte[] bytes = SourceManifestSerializer.Serialize(complete);
        ImportProvenance provenance = plan.Manifest.Sources.Count == 0
            ? throw new InvalidOperationException("A publication with no sources cannot carry a source manifest.")
            : new ImportProvenance(ImportProvenance.CurrentSchemaVersion, plan.Manifest.ImporterId, plan.Manifest.ImporterVersion,
                plan.Manifest.Sources.Select(source => new LogicalSourceRecord(
                    LogicalSourceRecord.CurrentSchemaVersion, source.SourcePath, source.ContentHash, source.ByteLen, Daggerfall.Import.Normalized.NormalizedImportDocument.CurrentSchemaVersion)).ToArray());
        // The plan's own manifest artifact is regenerated by Create, so it must not be
        // carried over as an input artifact.
        return ImportPublicationPlan.Create(provenance,
        [
            .. plan.Artifacts.Where(artifact => artifact.RelativePath != ImportPublicationManifestSerializer.ManifestRelativePath),
            new ImportPublicationArtifact(SourceManifestSerializer.ManifestRelativePath, bytes),
        ]);
    }

    private static int RunSpriteCommand(IReadOnlyList<string> args)
    {
        SpriteToolOptions options = SpriteToolOptions.Parse(args);
        if (options.Command == SpriteToolCommand.OverlayDiscard)
        {
            Console.WriteLine(SpriteAuthoredOverlayStore.Discard(options.PublicationDirectory, options.AuthoringDirectory!, options.OverlayPath!)
                ? "sprite overlay moved to its .discarded recovery path"
                : "sprite overlay was not present");
            return 0;
        }

        SpritePublicationSnapshot publication = SpritePublicationReader.Read(options.PublicationDirectory);
        switch (options.Command)
        {
            case SpriteToolCommand.List:
            {
                IEnumerable<SpriteInspectionEntry> entries = publication.Catalog.Entries;
                if (options.Kind is not null)
                {
                    entries = entries.Where(entry => entry.Kind == options.Kind.Value);
                }

                PrintJson(entries.OrderBy(entry => entry.Id, StringComparer.Ordinal).ToArray());
                return 0;
            }
            case SpriteToolCommand.Show:
                PrintJson(publication.Catalog.Require(options.Id!));
                return 0;
            case SpriteToolCommand.OverlayValidate:
            {
                SpriteAuthoredOverlayStore.ValidateRootSeparation(options.PublicationDirectory, options.AuthoringDirectory!);
                SpriteAuthoredOverlayDocument overlay = ReadOverlay(options.AuthoringDirectory!, options.OverlayPath!);
                SpriteAuthoredOverlayStore.Validate(overlay, publication.Catalog, publication.AuthoringBasisDigest);
                Console.WriteLine("sprite overlay is valid");
                return 0;
            }
            case SpriteToolCommand.OverlayWrite:
            {
                SpriteAuthoredOverlayDocument overlay = ReadExternalOverlay(options.InputPath!);
                SpriteAuthoredOverlayStore.Write(options.PublicationDirectory, options.AuthoringDirectory!, options.OverlayPath!, overlay, publication.Catalog, publication.AuthoringBasisDigest);
                Console.WriteLine("sprite overlay written; later regeneration may pass this typed document through SpriteAuthoredOverlayStore.ToMediaOverlays.");
                return 0;
            }
            default:
                throw new InvalidOperationException("The sprite command is not known.");
        }
    }

    private static SpriteAuthoredOverlayDocument ReadOverlay(string authoringDirectory, string relativePath)
    {
        string path = SpriteAuthoredOverlayStore.ResolveRelativePath(authoringDirectory, relativePath);
        return ReadExternalOverlay(path);
    }

    private static SpriteAuthoredOverlayDocument ReadExternalOverlay(string path)
    {
        FileInfo file = new(Path.GetFullPath(path));
        if (!file.Exists || file.Length is <= 0 or > 1024 * 1024)
        {
            throw new FormatException("The sprite overlay input is missing or outside its byte quota.");
        }

        byte[] bytes = File.ReadAllBytes(file.FullName);
        if (bytes.LongLength != file.Length)
        {
            throw new IOException("The sprite overlay input changed while it was being read.");
        }

        return SpriteAuthoredOverlayStore.Read(bytes);
    }

    private static readonly JsonSerializerOptions SpriteJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private static void PrintJson<T>(T value) => Console.WriteLine(JsonSerializer.Serialize(value, SpriteJsonOptions));

    private static ImportPublicationPlan BuildPlan(ToolOptions options)
    {
        if (options.SpriteAuthoringDirectory is null)
        {
            return BuildPlanCore(options, [], []);
        }

        SpriteAuthoredOverlayStore.ValidateRootSeparation(options.OutputDirectory, options.SpriteAuthoringDirectory);
        ImportPublicationPlan currentInputs = BuildPlanCore(options, [], []);
        SpritePublicationSnapshot current = SpritePublicationReader.FromPlan(currentInputs);
        SpriteAuthoredOverlayDocument overlay = ReadOverlay(options.SpriteAuthoringDirectory, options.SpriteOverlayPath!);
        IReadOnlyList<AuthoredMediaOverlay> values = SpriteAuthoredOverlayStore.ToMediaOverlays(overlay, current.Catalog, current.AuthoringBasisDigest);

        HashSet<string> dungeonIds = current.Catalog.Entries
            .Where(entry => entry.Kind is SpriteInspectionKind.DungeonBillboard or SpriteInspectionKind.DungeonActor or SpriteInspectionKind.DungeonCorpse)
            .Select(entry => entry.Id)
            .ToHashSet(StringComparer.Ordinal);
        return BuildPlanCore(options, values.Where(value => dungeonIds.Contains(value.Id)).ToArray(), values.Where(value => !dungeonIds.Contains(value.Id)).ToArray());
    }

    private static ImportPublicationPlan BuildPlanCore(ToolOptions options, IReadOnlyList<AuthoredMediaOverlay> dungeonOverlays, IReadOnlyList<AuthoredMediaOverlay> classicOverlays)
    {
        AdmittedArena2Sources sources = new(options.Arena2Directory);
        LoadRequiredDungeonSources(sources);
        LoadClassicMediaSources(sources);
        // Discovery rides typed missing-source data, never diagnostic wording. Each retry loads at
        // least one previously unrequested source; a repeated request means the requirement does
        // not resolve, so the loop always terminates instead of spinning.
        HashSet<string> resolvedOnDemand = new(StringComparer.Ordinal);
        while (true)
        {
            try
            {
                DungeonNormalizationRequest request = new(
                    new DungeonLogicalSourceSet(sources.DungeonSources),
                    options.Region,
                    options.Location,
                    options.TextureTableMode,
                    DungeonNormalizationQuotas.Default with { MaximumSourceBytes = MaximumTotalSourceBytes });
                DungeonNormalizationResult result = DungeonNormalizer.Normalize(request);

                Arena2DungeonMediaPublication dungeonMedia = Arena2DungeonMediaPublication.Create(
                    Arena2DungeonMediaRequest.Create(result.Document, new Arena2DungeonMediaSourceSet(sources.DungeonMediaSources)) with { AuthoredOverlays = dungeonOverlays });
                Arena2ClassicMediaPublication classicMedia = Arena2ClassicMediaPublication.Create(
                    sources.ClassicMediaInputs,
                    options.ClassicMediaProfile with { AuthoredOverlays = classicOverlays },
                    new Arena2ClassicMediaPublicationOptions(MaximumSourceBytes: MaximumIndividualSourceBytes));
                DungeonLogicalSource archSource = sources.DungeonSources.Single(source => source.Label.EndsWith("ARCH3D.BSA", StringComparison.Ordinal));
                GeometryPublication geometry = GeometryPublicationBuilder.Create(new GeometryPublicationRequest(
                    Arch3dInventoryReader.Read(archSource.Bytes.ToArray(), archSource.Label),
                    archSource.Bytes,
                    result.ReferencedMeshIds,
                    sources.TextureLeaves()));
                return Arena2MediaBundlePublication.Create(result, dungeonMedia, classicMedia, geometry).Plan;
            }
            catch (MissingArena2SourceException missing)
            {
                LoadOnDemand(sources, resolvedOnDemand, missing.SourceName);
            }
            catch (MissingDungeonMediaTexturesException mismatch)
                when (mismatch.UnneededTextureNames.Count == 0 && mismatch.MissingTextureNames.Count != 0)
            {
                foreach (string textureName in mismatch.MissingTextureNames)
                {
                    LoadOnDemand(sources, resolvedOnDemand, textureName);
                }
            }
        }
    }

    /// <summary>Loads one on-demand source, refusing to retry a requirement that never resolves.</summary>
    private static void LoadOnDemand(AdmittedArena2Sources sources, HashSet<string> resolvedOnDemand, string sourceName)
    {
        sources.LoadDungeon(sourceName);
        if (!resolvedOnDemand.Add(sourceName))
            throw new InvalidOperationException($"Dungeon source '{sourceName}' is still required after loading it on demand.");
    }

    private static void LoadRequiredDungeonSources(AdmittedArena2Sources sources)
    {
        foreach (string name in DungeonSourceNames)
        {
            sources.LoadDungeon(name);
        }
    }

    private static void LoadClassicMediaSources(AdmittedArena2Sources sources)
    {
        foreach (string name in ClassicMediaSourceNames)
        {
            sources.LoadClassic(name);
        }
    }

    private static DungeonLogicalSource ReadSource(string arena2Directory, string fileName)
    {
        if (!IsAdmittedSourceName(fileName))
        {
            throw new InvalidOperationException($"'{fileName}' is not an admitted Arena2 source name.");
        }

        string sourcePath = Path.Combine(arena2Directory, fileName);
        FileInfo info = new(sourcePath);
        if (!info.Exists)
        {
            throw new FileNotFoundException($"Required Arena2 source '{fileName}' was not found.", sourcePath);
        }

        if (info.Length is <= 0 or > MaximumIndividualSourceBytes)
        {
            throw new InvalidOperationException($"Arena2 source '{fileName}' is outside the permitted byte range.");
        }

        byte[] bytes = File.ReadAllBytes(sourcePath);
        if (bytes.LongLength != info.Length)
        {
            throw new IOException($"Arena2 source '{fileName}' changed while it was being read.");
        }

        return new DungeonLogicalSource($"arena2/{fileName}", bytes);
    }

    private static bool IsAdmittedSourceName(string value) => DungeonSourceNames.Contains(value, StringComparer.Ordinal)
        || ClassicMediaSourceNames.Contains(value, StringComparer.Ordinal)
        || IsTextureLeaf(value);

    private static bool IsTextureLeaf(string value) => value.Length == "TEXTURE.000".Length
        && value.StartsWith("TEXTURE.", StringComparison.Ordinal)
        && value[8..].All(char.IsAsciiDigit);

    /// <summary>
    /// Reads the tracked authored UI input through two explicit caller paths.
    /// The importer itself receives only portable labels and copied bytes, so
    /// it never gains filesystem authority or discovers a directory.
    /// </summary>
    private static Arena2ClassicMediaProfile LoadClassicMediaProfile(string authoredManifestPath, string originalDirectory)
    {
        byte[] manifestBytes = ReadBoundedExternalFile(authoredManifestPath, "authored UI manifest");
        AuthoredUiAssetFileSet manifest = JsonSerializer.Deserialize<AuthoredUiAssetFileSet>(manifestBytes, AuthoredUiJsonOptions)
            ?? throw new FormatException("The authored UI manifest is empty.");
        if (manifest.SchemaVersion != AuthoredUiAssetFileSet.CurrentSchemaVersion || manifest.Assets is null || manifest.Assets.Count == 0)
        {
            throw new FormatException("The authored UI manifest schema or asset list is not supported.");
        }

        string originalsRoot = Path.GetFullPath(originalDirectory);
        if (!Directory.Exists(originalsRoot))
        {
            throw new DirectoryNotFoundException($"The authored UI original directory '{originalsRoot}' was not found.");
        }

        HashSet<string> ids = new(StringComparer.Ordinal);
        HashSet<string> outputFiles = new(StringComparer.Ordinal);
        HashSet<string> sourceFiles = new(StringComparer.Ordinal);
        long totalBytes = manifestBytes.LongLength;
        List<ClassicAuthoredUiAsset> assets = new(manifest.Assets.Count);
        foreach (AuthoredUiAssetFile asset in manifest.Assets.OrderBy(asset => asset.Id, StringComparer.Ordinal))
        {
            ArgumentNullException.ThrowIfNull(asset);
            RequirePortableLeaf(asset.File, nameof(asset.File));
            RequirePortableLeaf(asset.SourceFile, nameof(asset.SourceFile));
            if (string.IsNullOrWhiteSpace(asset.Id) || asset.Id.Any(char.IsControl)
                || string.IsNullOrWhiteSpace(asset.Generator) || string.IsNullOrWhiteSpace(asset.Prompt)
                || asset.Generator.Any(char.IsControl) || asset.Prompt.Any(char.IsControl)
                || !ids.Add(asset.Id) || !outputFiles.Add(asset.File) || !sourceFiles.Add(asset.SourceFile))
            {
                throw new FormatException("Authored UI asset IDs, output files, and source files must be unique plain values.");
            }

            string sourcePath = Path.Combine(originalsRoot, asset.SourceFile);
            if (!StringComparer.Ordinal.Equals(Path.GetFullPath(sourcePath), Path.Combine(originalsRoot, asset.SourceFile)))
            {
                throw new FormatException("An authored UI source file escaped its explicit original directory.");
            }

            byte[] bytes = ReadBoundedExternalFile(sourcePath, $"authored UI source '{asset.SourceFile}'");
            totalBytes = checked(totalBytes + bytes.LongLength);
            if (totalBytes > MaximumTotalSourceBytes)
            {
                throw new InvalidOperationException("The authored UI input exceeds the total source byte quota.");
            }

            assets.Add(new ClassicAuthoredUiAsset(
                asset.Id,
                $"media/ui/authored/{asset.File}",
                $"ui-original/{asset.SourceFile}",
                bytes,
                asset.Generator,
                asset.Prompt));
        }

        return new Arena2ClassicMediaProfile(
            AuthoredUiManifest: new ClassicAuthoredUiManifestInput("ui-authored-assets.json", manifestBytes),
            AuthoredUiAssets: assets);
    }

    private static byte[] ReadBoundedExternalFile(string path, string subject)
    {
        string fullPath = Path.GetFullPath(path);
        FileInfo info = new(fullPath);
        if (!info.Exists)
        {
            throw new FileNotFoundException($"Required {subject} '{fullPath}' was not found.", fullPath);
        }

        if (info.Length is <= 0 or > MaximumIndividualSourceBytes)
        {
            throw new InvalidOperationException($"{subject} is outside the permitted byte range.");
        }

        byte[] bytes = File.ReadAllBytes(fullPath);
        if (bytes.LongLength != info.Length)
        {
            throw new IOException($"{subject} changed while it was being read.");
        }

        return bytes;
    }

    private static void RequirePortableLeaf(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !StringComparer.Ordinal.Equals(value, Path.GetFileName(value))
            || value is "." or ".."
            || value.Contains('/') || value.Contains('\\')
            || value.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0)
        {
            throw new FormatException($"{name} must be one portable file leaf.");
        }
    }

    private static readonly JsonSerializerOptions AuthoredUiJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private sealed record AuthoredUiAssetFileSet(int SchemaVersion, IReadOnlyList<AuthoredUiAssetFile> Assets)
    {
        public const int CurrentSchemaVersion = 1;
    }

    private sealed record AuthoredUiAssetFile(string Id, string File, string SourceFile, string Generator, string Prompt);

    private static void VerifyDeterminism(ImportPublicationPlan plan, ToolOptions options)
    {
        string parent = Path.GetTempPath();
        string root = Path.Combine(parent, $"daggerfall-import-verify-{Guid.NewGuid():N}");
        string first = Path.Combine(root, "first");
        string second = Path.Combine(root, "second");
        try
        {
            ImportPublicationWriter.Write(plan, first);
            ImportPublicationWriter.Write(BuildPlan(options), second);
            IReadOnlyDictionary<string, ContentDigest> firstHashes = HashClosure(first);
            IReadOnlyDictionary<string, ContentDigest> secondHashes = HashClosure(second);
            if (firstHashes.Count != secondHashes.Count || firstHashes.Any(entry => !secondHashes.TryGetValue(entry.Key, out ContentDigest hash) || hash != entry.Value))
            {
                throw new InvalidOperationException("Repeated real-data import did not produce the same publication closure.");
            }

            Console.WriteLine($"verified deterministic publication ({firstHashes.Count} artifacts)");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static IReadOnlyDictionary<string, ContentDigest> HashClosure(string directory) => Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
        .OrderBy(path => path, StringComparer.Ordinal)
        .ToDictionary(
            path => Path.GetRelativePath(directory, path).Replace(Path.DirectorySeparatorChar, '/'),
            path => ContentDigest.Compute(File.ReadAllBytes(path)),
            StringComparer.Ordinal);

    private static void PrintPlan(ImportPublicationComparison comparison)
    {
        if (comparison.IsNoOp)
        {
            Console.WriteLine("publication is current");
            return;
        }

        Console.WriteLine($"publication changes: missing={comparison.Missing.Count}, changed={comparison.Changed.Count}, unexpected={comparison.Unexpected.Count}");
        // Naming the artifacts is the point of the comparison: an operator who sees a
        // count cannot tell whether the source manifest or the world moved.
        foreach ((string kind, IReadOnlyList<string> paths) in new[]
        {
            ("missing", comparison.Missing),
            ("changed", comparison.Changed),
            ("unexpected", comparison.Unexpected),
        })
        {
            foreach (string path in paths.Take(20))
            {
                Console.WriteLine($"  {kind}: {path}");
            }

            if (paths.Count > 20)
            {
                Console.WriteLine($"  {kind}: ... and {paths.Count - 20} more");
            }
        }
    }

    private enum ToolCommand
    {
        Plan,
        Write,
        VerifyRealData,
        SourceManifest,
    }

    private enum SpriteToolCommand
    {
        List,
        Show,
        OverlayValidate,
        OverlayWrite,
        OverlayDiscard,
    }

    private sealed record SpriteToolOptions(
        SpriteToolCommand Command,
        string PublicationDirectory,
        string? AuthoringDirectory,
        string? Id,
        string? OverlayPath,
        string? InputPath,
        SpriteInspectionKind? Kind)
    {
        public static SpriteToolOptions Parse(IReadOnlyList<string> args)
        {
            if (args.Count == 0)
            {
                throw new ArgumentException(Usage());
            }

            SpriteToolCommand command = args[0] switch
            {
                "sprite-list" => SpriteToolCommand.List,
                "sprite-show" => SpriteToolCommand.Show,
                "sprite-overlay-validate" => SpriteToolCommand.OverlayValidate,
                "sprite-overlay-write" => SpriteToolCommand.OverlayWrite,
                "sprite-overlay-discard" => SpriteToolCommand.OverlayDiscard,
                _ => throw new ArgumentException(Usage()),
            };
            Dictionary<string, string> values = [];
            for (int index = 1; index < args.Count; index += 2)
            {
                if (index + 1 >= args.Count || !args[index].StartsWith("--", StringComparison.Ordinal) || !values.TryAdd(args[index], args[index + 1]))
                {
                    throw new ArgumentException(Usage());
                }
            }

            values.TryGetValue("--publication", out string? publication);
            if (string.IsNullOrWhiteSpace(publication))
            {
                throw new ArgumentException(Usage());
            }

            string[] required = command switch
            {
                SpriteToolCommand.List => ["--publication"],
                SpriteToolCommand.Show => ["--publication", "--id"],
                SpriteToolCommand.OverlayValidate => ["--publication", "--authoring", "--overlay"],
                SpriteToolCommand.OverlayWrite => ["--publication", "--authoring", "--overlay", "--input"],
                SpriteToolCommand.OverlayDiscard => ["--publication", "--authoring", "--overlay"],
                _ => throw new InvalidOperationException(),
            };
            bool listWithKind = command == SpriteToolCommand.List && values.ContainsKey("--kind");
            if (values.Count != required.Length + (listWithKind ? 1 : 0) || required.Any(key => !values.ContainsKey(key)))
            {
                throw new ArgumentException(Usage());
            }

            SpriteInspectionKind? kind = null;
            if (listWithKind && (!Enum.TryParse(values["--kind"], ignoreCase: true, out SpriteInspectionKind parsedKind) || !Enum.IsDefined(parsedKind)))
            {
                throw new ArgumentException("--kind must name a known sprite inspection kind.");
            }
            else if (listWithKind)
            {
                kind = Enum.Parse<SpriteInspectionKind>(values["--kind"], ignoreCase: true);
            }

            if (values.TryGetValue("--overlay", out string? overlay))
            {
                SpriteAuthoredOverlayStore.ValidateOverlayRelativePath(overlay);
            }

            if (values.TryGetValue("--id", out string? id) && (string.IsNullOrWhiteSpace(id) || id.Any(char.IsWhiteSpace)))
            {
                throw new ArgumentException("--id must be a non-empty whitespace-free logical ID.");
            }

            if (values.TryGetValue("--authoring", out string? authoring) && string.IsNullOrWhiteSpace(authoring))
            {
                throw new ArgumentException("--authoring must name a non-empty source directory.");
            }

            return new(command, Path.GetFullPath(publication), authoring is null ? null : Path.GetFullPath(authoring), values.GetValueOrDefault("--id"), values.GetValueOrDefault("--overlay"), values.GetValueOrDefault("--input"), kind);
        }

        private static string Usage() => "usage: daggerfall-import-tool sprite-list --publication GENERATED_DIR [--kind KIND] | sprite-show --publication GENERATED_DIR --id ID | sprite-overlay-validate --publication GENERATED_DIR --authoring SOURCE_DIR --overlay sprites/RELATIVE.json | sprite-overlay-write --publication GENERATED_DIR --authoring SOURCE_DIR --overlay sprites/RELATIVE.json --input FILE | sprite-overlay-discard --publication GENERATED_DIR --authoring SOURCE_DIR --overlay sprites/RELATIVE.json";
    }

    private sealed class AdmittedArena2Sources
    {
        private readonly string arena2Directory;
        private readonly Dictionary<string, DungeonLogicalSource> loaded = new(StringComparer.Ordinal);
        private readonly HashSet<string> dungeonSourceNames = new(StringComparer.Ordinal);
        private long totalBytes;

        public AdmittedArena2Sources(string arena2Directory)
        {
            this.arena2Directory = arena2Directory;
        }

        public IReadOnlyList<DungeonLogicalSource> DungeonSources => dungeonSourceNames
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name => loaded[name])
            .ToArray();

        /// <summary>
        /// Only PAL.PAL and the exact dynamically selected dungeon texture
        /// closure enter this source set. Classic-only TEXTURE archives never
        /// reach the dungeon media exact-closure validator.
        /// </summary>
        /// <summary>
        /// Every texture leaf the corpus supplies, read so a material reference resolves against what is
        /// actually there rather than against whatever this pass happened to load.
        /// </summary>
        public TextureLeafInventory TextureLeaves()
        {
            List<(int Id, string Path, ReadOnlyMemory<byte> Bytes)> leaves = [];
            foreach (string path in Directory.EnumerateFiles(arena2Directory, "TEXTURE.*"))
            {
                string name = Path.GetFileName(path);
                if (!int.TryParse(name["TEXTURE.".Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int leafId))
                {
                    // A supplied name that carries no leaf number would leave a material looking unsupplied
                    // while the corpus supplies it, so it is refused exactly as the leaf command refuses it.
                    throw new InvalidOperationException($"'{name}' does not carry a texture leaf number, so it cannot be enumerated as a leaf.");
                }

                leaves.Add((leafId, name, File.ReadAllBytes(path)));
            }

            return TextureLeafInventory.Enumerate(leaves, Path.GetFileName(Path.TrimEndingDirectorySeparator(arena2Directory)));
        }

        public IReadOnlyList<Arena2DungeonMediaSource> DungeonMediaSources => dungeonSourceNames
            .Where(name => name is "PAL.PAL" || IsTextureLeaf(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name => new Arena2DungeonMediaSource(loaded[name].Label, loaded[name].Bytes.Span))
            .ToArray();

        public Arena2ClassicMediaInputs ClassicMediaInputs => new(
            Require("WEAPON01.CIF").Bytes.ToArray(),
            Require("WEAPON02.CIF").Bytes.ToArray(),
            Require("WEAPON04.CIF").Bytes.ToArray(),
            Require("WEAPON05.CIF").Bytes.ToArray(),
            Require("WEAPON06.CIF").Bytes.ToArray(),
            Require("WEAPON07.CIF").Bytes.ToArray(),
            Require("WEAPON08.CIF").Bytes.ToArray(),
            Require("WEAPON09.CIF").Bytes.ToArray(),
            Require("WEAPON10.CIF").Bytes.ToArray(),
            Require("ART_PAL.COL").Bytes.ToArray(),
            Require("TEXTURE.380").Bytes.ToArray(),
            Require("PAL.PAL").Bytes.ToArray(),
            Require("DAGGER.SND").Bytes.ToArray(),
            Require("MAIN00I0.IMG").Bytes.ToArray(),
            Require("MAIN03I0.IMG").Bytes.ToArray(),
            Require("MAIN04I0.IMG").Bytes.ToArray(),
            Require("MAIN05I0.IMG").Bytes.ToArray(),
            Require("INVE00I0.IMG").Bytes.ToArray(),
            Require("INFO00I0.IMG").Bytes.ToArray(),
            Require("DIE_00I0.IMG").Bytes.ToArray(),
            Require("CHGN00I0.IMG").Bytes.ToArray(),
            Require("PICK02I0.IMG").Bytes.ToArray(),
            Require("PICK03I0.IMG").Bytes.ToArray(),
            Require("PRIS00I0.IMG").Bytes.ToArray(),
            Require("TITL00I0.IMG").Bytes.ToArray(),
            Require("BOOK00I0.IMG").Bytes.ToArray(),
            Require("REST00I0.IMG").Bytes.ToArray(),
            Require("SHOP00I0.IMG").Bytes.ToArray(),
            Require("GILD00I0.IMG").Bytes.ToArray(),
            Require("BANK00I0.IMG").Bytes.ToArray(),
            Require("REST01I0.IMG").Bytes.ToArray(),
            Require("REST02I0.IMG").Bytes.ToArray(),
            Require("INVE08I0.IMG").Bytes.ToArray(),
            Require("INVE10I0.IMG").Bytes.ToArray(),
            Require("INVE11I0.IMG").Bytes.ToArray(),
            Require("INVE12I0.IMG").Bytes.ToArray(),
            Require("INVE14I0.IMG").Bytes.ToArray(),
            Require("GILD01I0.IMG").Bytes.ToArray(),
            Require("TEXTURE.207").Bytes.ToArray(),
            Require("TEXTURE.216").Bytes.ToArray(),
            Require("TEXTURE.234").Bytes.ToArray(),
            Require("TEXTURE.245").Bytes.ToArray(),
            Require("FONT0003.FNT").Bytes.ToArray(),
            Require("WEAPON00.CIF").Bytes.ToArray(),
            Require("WEAPON03.CIF").Bytes.ToArray(),
            Require("WEAPON11.CIF").Bytes.ToArray(),
            Require("FONT0000.FNT").Bytes.ToArray(),
            Require("FONT0001.FNT").Bytes.ToArray(),
            Require("FONT0002.FNT").Bytes.ToArray(),
            Require("FONT0004.FNT").Bytes.ToArray(),
            ReadMapMedia(),
            Require("FMAP_PAL.COL").Bytes.ToArray(),
            Require("MAP.PAL").Bytes.ToArray());

        public void LoadDungeon(string fileName)
        {
            if (!DungeonSourceNames.Contains(fileName, StringComparer.Ordinal) && !IsTextureLeaf(fileName))
            {
                throw new InvalidOperationException($"'{fileName}' is not an admitted Arena2 dungeon source name.");
            }

            Load(fileName);
            dungeonSourceNames.Add(fileName);
        }

        public void LoadClassic(string fileName)
        {
            if (!ClassicMediaSourceNames.Contains(fileName, StringComparer.Ordinal))
            {
                throw new InvalidOperationException($"'{fileName}' is not in the fixed classic media source set.");
            }

            Load(fileName);
        }

        private DungeonLogicalSource Require(string fileName) => loaded.TryGetValue(fileName, out DungeonLogicalSource? source)
            ? source
            : throw new InvalidOperationException($"The admitted Arena2 source '{fileName}' was not loaded.");

        /// <summary>
        /// Reads the supplied map art files in inventory order: the documented set the map
        /// publication renders, with the stubs and missing screens left to the inventory's
        /// dispositions rather than to absent bytes.
        /// </summary>
        private IReadOnlyList<MapMediaInput> ReadMapMedia()
        {
            List<MapMediaInput> files = [];
            foreach (string name in ClassicMediaSourceNames.Where(name =>
                name.EndsWith(".IMG", StringComparison.Ordinal)
                && (name.StartsWith("FMAP", StringComparison.Ordinal) || name.StartsWith("AMAP", StringComparison.Ordinal)
                || name.StartsWith("TMAP", StringComparison.Ordinal) || name.StartsWith("TRAV", StringComparison.Ordinal)
                || name.StartsWith("TOWN", StringComparison.Ordinal))).Order(StringComparer.Ordinal))
            {
                files.Add(new MapMediaInput(name, Require(name).Bytes.ToArray()));
            }

            return files;
        }

        private void Load(string fileName)
        {
            if (loaded.ContainsKey(fileName))
            {
                return;
            }

            DungeonLogicalSource source = ReadSource(arena2Directory, fileName);
            long nextTotal = checked(totalBytes + source.Bytes.Length);
            if (nextTotal > MaximumTotalSourceBytes)
            {
                throw new InvalidOperationException("The admitted Arena2 source closure exceeds the total byte quota.");
            }

            loaded.Add(fileName, source);
            totalBytes = nextTotal;
        }
    }

    private static readonly string[] DungeonSourceNames =
    [
        "MAPS.BSA",
        "BLOCKS.BSA",
        "ARCH3D.BSA",
        "CLIMATE.PAK",
        "PAL.PAL",
    ];

    private static readonly string[] ClassicMediaSourceNames =
    [
        "ART_PAL.COL",
        "WEAPON00.CIF",
        "WEAPON01.CIF",
        "WEAPON02.CIF",
        "WEAPON03.CIF",
        "WEAPON04.CIF",
        "WEAPON05.CIF",
        "WEAPON06.CIF",
        "WEAPON07.CIF",
        "WEAPON08.CIF",
        "WEAPON09.CIF",
        "WEAPON10.CIF",
        "WEAPON11.CIF",
        "DAGGER.SND",
        "MAIN00I0.IMG",
        "MAIN03I0.IMG",
        "MAIN04I0.IMG",
        "MAIN05I0.IMG",
        "INVE00I0.IMG",
        "INFO00I0.IMG",
        "DIE_00I0.IMG",
        "CHGN00I0.IMG",
        "PICK02I0.IMG",
        "PICK03I0.IMG",
        "PRIS00I0.IMG",
        "TITL00I0.IMG",
        "BOOK00I0.IMG",
        "REST00I0.IMG",
        "SHOP00I0.IMG",
        "GILD00I0.IMG",
        "BANK00I0.IMG",
        "REST01I0.IMG",
        "REST02I0.IMG",
        "INVE08I0.IMG",
        "INVE10I0.IMG",
        "INVE11I0.IMG",
        "INVE12I0.IMG",
        "INVE14I0.IMG",
        "GILD01I0.IMG",
        "FONT0003.FNT",
        "FONT0000.FNT",
        "FONT0001.FNT",
        "FONT0002.FNT",
        "FONT0004.FNT",
        "FMAP_PAL.COL",
        "MAP.PAL",
        "FMAP0I00.IMG",
        "FMAP0I01.IMG",
        "FMAP0I05.IMG",
        "FMAP0I09.IMG",
        "FMAP0I11.IMG",
        "FMAP0I16.IMG",
        "FMAP0I17.IMG",
        "FMAP0I18.IMG",
        "FMAP0I19.IMG",
        "FMAP0I20.IMG",
        "FMAP0I21.IMG",
        "FMAP0I22.IMG",
        "FMAP0I23.IMG",
        "FMAP0I26.IMG",
        "FMAP0I32.IMG",
        "FMAP0I33.IMG",
        "FMAP0I34.IMG",
        "FMAP0I35.IMG",
        "FMAP0I36.IMG",
        "FMAP0I37.IMG",
        "FMAP0I38.IMG",
        "FMAP0I39.IMG",
        "FMAP0I40.IMG",
        "FMAP0I41.IMG",
        "FMAP0I42.IMG",
        "FMAP0I43.IMG",
        "FMAP0I44.IMG",
        "FMAP0I45.IMG",
        "FMAP0I46.IMG",
        "FMAP0I47.IMG",
        "FMAP0I48.IMG",
        "FMAP0I49.IMG",
        "FMAP0I50.IMG",
        "FMAP0I51.IMG",
        "FMAP0I52.IMG",
        "FMAP0I53.IMG",
        "FMAP0I54.IMG",
        "FMAP0I55.IMG",
        "FMAP0I56.IMG",
        "FMAP0I57.IMG",
        "FMAP0I58.IMG",
        "FMAP0I59.IMG",
        "FMAP0I60.IMG",
        "FMAP0I61.IMG",
        "FMAPAI00.IMG",
        "FMAPBI00.IMG",
        "FMAPAI01.IMG",
        "FMAPBI01.IMG",
        "FMAPCI01.IMG",
        "FMAPDI01.IMG",
        "FMAPAI16.IMG",
        "FMAPBI16.IMG",
        "FMAPCI16.IMG",
        "FMAPDI16.IMG",
        "AMAP00I0.IMG",
        "AMAP01I0.IMG",
        "TMAP00I0.IMG",
        "TOWN00I0.IMG",
        "TRAV00I0.IMG",
        "TRAV01I0.IMG",
        "TRAV01I1.IMG",
        "TRAV02I0.IMG",
        "TRAV0I00.IMG",
        "TRAV0I01.IMG",
        "TRAV0I03.IMG",
        "TRAV0I04.IMG",
        "TRAVAI05.IMG",
        "TRAVBI05.IMG",
        "TRAVCI05.IMG",
        "TRAVDI05.IMG",
        "TEXTURE.380",
        "TEXTURE.207",
        "TEXTURE.216",
        "TEXTURE.234",
        "TEXTURE.245",
        "PAL.PAL",
    ];

    private sealed record ToolOptions(
        ToolCommand Command,
        string Arena2Directory,
        string OutputDirectory,
        int Region,
        string Location,
        DungeonTextureTableMode TextureTableMode,
        Arena2ClassicMediaProfile ClassicMediaProfile,
        string? SpriteAuthoringDirectory,
        string? SpriteOverlayPath,
        string? InventoryFile)
    {
        public static ToolOptions Parse(IReadOnlyList<string> args)
        {
            if (args.Count == 0 || args[0] is "--help" or "-h")
            {
                throw new ArgumentException(Usage());
            }

            ToolCommand command = args[0] switch
            {
                "plan" => ToolCommand.Plan,
                "write" => ToolCommand.Write,
                "verify-real-data" => ToolCommand.VerifyRealData,
                _ => throw new ArgumentException(Usage()),
            };
            Dictionary<string, string> values = new(StringComparer.Ordinal);
            for (int index = 1; index < args.Count; index += 2)
            {
                if (index + 1 >= args.Count || !args[index].StartsWith("--", StringComparison.Ordinal) || !values.TryAdd(args[index], args[index + 1]))
                {
                    throw new ArgumentException(Usage());
                }
            }

            string[] required = ["--arena2", "--output", "--region", "--location", "--texture-table", "--ui-authored-assets", "--ui-original"];
            bool hasSpriteAuthoring = values.ContainsKey("--sprite-authoring") || values.ContainsKey("--sprite-overlay");
            if (hasSpriteAuthoring && (!values.ContainsKey("--sprite-authoring") || !values.ContainsKey("--sprite-overlay")))
            {
                throw new ArgumentException("--sprite-authoring and --sprite-overlay must be supplied together.");
            }

            List<string> accepted = [.. required];
            if (hasSpriteAuthoring)
            {
                accepted.AddRange(["--sprite-authoring", "--sprite-overlay"]);
            }

            if (values.ContainsKey("--inventory"))
            {
                accepted.Add("--inventory");
            }

            EnsureExactKeys(values, accepted);
            if (!int.TryParse(values["--region"], NumberStyles.None, CultureInfo.InvariantCulture, out int region) || region is < 0 or > 999)
            {
                throw new ArgumentException("--region must be an integer in 0..999.");
            }

            if (string.IsNullOrWhiteSpace(values["--location"]))
            {
                throw new ArgumentException("--location must be non-empty.");
            }

            DungeonTextureTableMode table = values["--texture-table"] switch
            {
                "classic" => DungeonTextureTableMode.Classic,
                "default" => DungeonTextureTableMode.Default,
                _ => throw new ArgumentException("--texture-table must be classic or default."),
            };
            string? spriteAuthoring = hasSpriteAuthoring ? Path.GetFullPath(values["--sprite-authoring"]) : null;
            string? spriteOverlay = hasSpriteAuthoring ? values["--sprite-overlay"] : null;
            if (spriteAuthoring is not null)
            {
                SpriteAuthoredOverlayStore.ValidateOverlayRelativePath(spriteOverlay!);
            }

            return new(
                command,
                Path.GetFullPath(values["--arena2"]),
                Path.GetFullPath(values["--output"]),
                region,
                values["--location"],
                table,
                LoadClassicMediaProfile(values["--ui-authored-assets"], values["--ui-original"]),
                spriteAuthoring,
                spriteOverlay,
                values.TryGetValue("--inventory", out string? inventory) ? Path.GetFullPath(inventory) : null);
        }

        private static void EnsureExactKeys(IReadOnlyDictionary<string, string> values, IReadOnlyList<string> keys)
        {
            if (values.Count != keys.Count || keys.Any(key => !values.ContainsKey(key)))
            {
                throw new ArgumentException(Usage());
            }
        }

        private static string Usage() => "usage: daggerfall-import-tool <plan|write|verify-real-data> --arena2 DIR --output DIR --region 0..999 --location NAME --texture-table classic|default --ui-authored-assets FILE --ui-original DIR [--sprite-authoring SOURCE_DIR --sprite-overlay sprites/RELATIVE.json] [--inventory INVENTORY.csv]";
    }
}
