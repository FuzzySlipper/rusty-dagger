using Daggerfall.Import.Arena2;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Daggerfall.Import.Normalization;
using Daggerfall.Import.Publication;
using Daggerfall.Import.Normalized;

namespace Daggerfall.Import.Tool;

internal static class Program
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
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException or FormatException or System.Text.Json.JsonException)
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
    /// Builds the character presentation section from the character media the corpus supplies and
    /// writes it into the base pack when asked, so a character or social consumer resolves a race's
    /// layers by identity instead of reconstructing file names.
    /// </summary>
    private static int RunCharacterPresentationCommand(IReadOnlyList<string> args)
    {
        bool update = args.Contains("--update", StringComparer.Ordinal);
        if (args.Count != (update ? 8 : 7) || args[1] != "--arena2" || args[3] != "--inventory" || args[5] != "--pack")
        {
            throw new ArgumentException("usage: daggerfall-import-tool character-presentation --arena2 SOURCE_DIR --inventory INVENTORY.csv --pack PACK.json [--update]");
        }

        string arena2 = args[2];
        string packFile = args[6];
        // The inventory's own family rule, not a prefix filter: a class portrait or story sprite is
        // named by its extension, and a prefix test alone drops those files silently.
        List<(string Path, ReadOnlyMemory<byte> Bytes)> sources = [.. Directory
            .EnumerateFiles(arena2)
            .Select(path => Path.GetFileName(path))
            .Where(CharacterMediaInventory.IsDocumentedFamily)
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name => (name, (ReadOnlyMemory<byte>)File.ReadAllBytes(Path.Combine(arena2, name))))];
        HashSet<string> palettes = [.. Directory.EnumerateFiles(arena2, "*.COL").Select(path => Path.GetFileName(path))];

        // Nothing binds character media yet, so every file is required-pending rather than admitted:
        // the references say so per layer instead of implying a consumer that does not exist.
        CharacterMediaInventory characters = CharacterMediaInventory.Enumerate(sources, new HashSet<string>(StringComparer.Ordinal), "no consumer binds it yet", Path.GetFileName(Path.TrimEndingDirectorySeparator(arena2)));
        IReadOnlyList<DaggerfallRaceKey> races = ReadPackRaces(packFile);
        IReadOnlyDictionary<string, string> careers = ReadPackCareers(packFile);
        DaggerfallCharacterPresentation presentation = DaggerfallCharacterPresentationBuilder.Build(characters, palettes, races, careers);
        HashSet<string> mediaIds = [.. CharacterMediaReferences.Derive(characters, palettes).Canvases.Select(reference => reference.MediaId)];
        presentation.Validate(mediaIds);

        Console.WriteLine($"character presentation: {characters.Files.Count} supplied files, {mediaIds.Count} published canvases, {presentation.Layers.Count} layers over {races.Count} races, {presentation.Faces.Count} faction faces, {presentation.Careers.Count} career portraits, {presentation.CareersWithoutPortrait.Count} careers without one");
        foreach (string race in presentation.Layers.Select(layer => layer.Race).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            Console.WriteLine($"  {race}: {presentation.Layers.Count(layer => layer.Race == race)} layers");
        }

        foreach (DaggerfallRaceWithoutMedia gap in presentation.RacesWithoutMedia.Take(4))
        {
            Console.WriteLine($"  gap {gap.Race}: {gap.Reason}");
        }

        if (!update)
        {
            Console.WriteLine("pack: not written (rerun with --update to publish these references into it)");
            return 0;
        }

        JsonNode pack = JsonNode.Parse(File.ReadAllText(packFile))!.AsObject();
        pack["characterPresentation"] = JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(presentation, PublishedJson.Section));
        File.WriteAllText(packFile, pack.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
        Console.WriteLine($"pack: characterPresentation updated in {packFile}");
        return 0;
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

        // A family this repository cannot read is stated rather than left as an absence: a consumer that
        // finds no artifact for one of these files must be able to tell "not published" from "not
        // readable", and the donor anchor says where the original behaviour lives.
        JsonArray unreadable = [];
        foreach ((string family, string reason, string anchor) in new[]
        {
            (".CEL", "no decoder in this repository: the classic animation is colour-cycled and the donor reads it through its own FLC reader", "Assets/Scripts/API/FlcFile.cs"),
            (".BSS", "no decoder in this repository: the donor reads these sound-bank streams through its own BSS reader", "Assets/Scripts/API/BssFile.cs"),
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
                ["files"] = new JsonArray([.. files.Select(file => JsonValue.Create(file))]),
                ["reason"] = reason,
                ["donorAnchor"] = anchor,
            });
        }

        JsonObject inventory = new()
        {
            ["schemaVersion"] = 1,
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
                return Arena2MediaBundlePublication.Create(result, dungeonMedia, classicMedia).Plan;
            }
            catch (InvalidOperationException exception) when (TryRequiredTexture(exception.Message, out string? textureName))
            {
                sources.LoadDungeon(textureName!);
            }
            catch (InvalidOperationException exception) when (TryRequiredDungeonMediaTexture(exception.Message, out string? textureName))
            {
                sources.LoadDungeon(textureName!);
            }
            catch (InvalidOperationException exception) when (TryMissingDungeonMediaTextures(exception.Message, out IReadOnlyList<string>? textureNames))
            {
                foreach (string textureName in textureNames!)
                {
                    sources.LoadDungeon(textureName);
                }
            }
        }
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

    private static bool TryRequiredTexture(string message, out string? textureName)
    {
        const string prefix = "Dungeon normalization requires logical source '";
        textureName = null;
        if (!message.StartsWith(prefix, StringComparison.Ordinal) || !message.EndsWith("'.", StringComparison.Ordinal))
        {
            return false;
        }

        string candidate = message[prefix.Length..^2];
        if (!IsTextureLeaf(candidate))
        {
            return false;
        }

        textureName = candidate;
        return true;
    }

    private static bool TryRequiredDungeonMediaTexture(string message, out string? textureName)
    {
        const string prefix = "Arena2 dungeon media requires ";
        textureName = null;
        if (!message.StartsWith(prefix, StringComparison.Ordinal) || !message.EndsWith(".", StringComparison.Ordinal))
        {
            return false;
        }

        string candidate = message[prefix.Length..^1];
        if (!IsTextureLeaf(candidate))
        {
            return false;
        }

        textureName = candidate;
        return true;
    }

    private static bool TryMissingDungeonMediaTextures(string message, out IReadOnlyList<string>? textureNames)
    {
        const string prefix = "Arena2 dungeon media texture closure does not match normalized references. Missing: [";
        const string separator = "]. Unneeded: [";
        textureNames = null;
        if (!message.StartsWith(prefix, StringComparison.Ordinal) || !message.EndsWith("].", StringComparison.Ordinal))
        {
            return false;
        }

        int separatorIndex = message.IndexOf(separator, prefix.Length, StringComparison.Ordinal);
        if (separatorIndex < 0)
        {
            return false;
        }

        string unneeded = message[(separatorIndex + separator.Length)..^2];
        if (unneeded.Length != 0)
        {
            return false;
        }

        string missing = message[prefix.Length..separatorIndex];
        string[] names = missing.Length == 0 ? [] : missing.Split(", ", StringSplitOptions.None);
        if (names.Length == 0 || names.Any(name => !IsTextureLeaf(name)))
        {
            return false;
        }

        textureNames = names.OrderBy(name => name, StringComparer.Ordinal).ToArray();
        return true;
    }

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
            Require("FONT0003.FNT").Bytes.ToArray());

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
        "WEAPON01.CIF",
        "WEAPON02.CIF",
        "WEAPON04.CIF",
        "WEAPON05.CIF",
        "WEAPON06.CIF",
        "WEAPON07.CIF",
        "WEAPON08.CIF",
        "WEAPON09.CIF",
        "WEAPON10.CIF",
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
