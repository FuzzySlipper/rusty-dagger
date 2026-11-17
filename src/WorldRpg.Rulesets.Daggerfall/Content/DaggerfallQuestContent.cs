using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>One selected quests pack as the reader takes it: its payload and the quest records in its <c>quests/</c> folder.</summary>
internal sealed record DaggerfallQuestPackFiles(string PackId, ReadOnlyMemory<byte> Payload, IReadOnlyList<DaggerfallQuestRecordFile> Records);

/// <summary>One quest record file, by its content path.</summary>
internal sealed record DaggerfallQuestRecordFile(string Path, ReadOnlyMemory<byte> Bytes);

/// <summary>
/// Reads the quests packs a bundle selects. A pack's payload may carry the quest catalog, the quest tables and
/// corpora, and its <c>quests/</c> folder one record per quest; the catalog, the tables and each quest have one home
/// among the selected packs, so a scenario pack adds quests and corpora beside the classic one. What a record's lines
/// mean is decided here, at load: whether it may run, the resources it declares and, with the quest language,
/// whether its program runs (<see cref="Admit"/>). Nothing about a record is computed elsewhere, so an edit to one
/// is played as written.
/// </summary>
internal static class DaggerfallQuestContent
{
    /// <summary>The folder beside a quests pack's payload that holds its quest records.</summary>
    internal const string RecordsFolder = "quests";

    /// <summary>A category offered through the ordinary guild and populace offer paths.</summary>
    internal const string Ordinary = "ordinary";
    /// <summary>A catalog entry never offered, which may not run.</summary>
    internal const string NotOffered = "notOffered";
    /// <summary>A catalog entry reachable only through an explicit Daedric summoning identity.</summary>
    internal const string SummonOnly = "summonOnly";
    /// <summary>A quest started by name: by a new game, another quest, or an affliction's cure.</summary>
    internal const string Named = "named";

    internal static DaggerfallQuestSourceSet Read(IReadOnlyList<DaggerfallQuestPackFiles> packs)
    {
        ArgumentNullException.ThrowIfNull(packs);
        if (packs.Count == 0) return DaggerfallQuestSourceSet.None;
        DaggerfallContentDiagnostics diagnostics = new();
        (string Pack, DaggerfallQuestCatalogDocument Value)? catalog = null;
        (string Pack, DaggerfallQuestTablesDocument Value)? tables = null;
        List<DaggerfallQuestCorpusDefinition> corpora = [];
        List<(string Pack, string Path, DaggerfallQuestRecordDocument Record)> records = [];
        foreach (DaggerfallQuestPackFiles pack in packs)
        {
            if (Parse(pack.Payload, DaggerfallQuestJson.Default.DaggerfallQuestPackDocument, $"Quests pack '{pack.PackId}' payload", diagnostics) is not { } payload) continue;
            if (payload.QuestCatalog is { } rows) catalog = OneHome(catalog, pack.PackId, new DaggerfallQuestCatalogDocument(rows), "questCatalog", diagnostics);
            if (payload.QuestTables is { } carried) tables = OneHome(tables, pack.PackId, carried, "questTables", diagnostics);
            corpora.AddRange(payload.QuestCorpora ?? []);
            foreach (DaggerfallQuestRecordFile file in pack.Records)
                if (Parse(file.Bytes, DaggerfallQuestJson.Default.DaggerfallQuestRecordDocument, $"Quest record '{file.Path}'", diagnostics) is { } record)
                    records.Add((pack.PackId, file.Path, record));
        }
        if (catalog is null) diagnostics.Add("No selected quests pack carries questCatalog, which every quest offer reads.");
        if (tables is null) diagnostics.Add("No selected quests pack carries questTables, which every quest resolves its globals, places and actors through.");
        diagnostics.ThrowIfAny();

        Dictionary<string, DaggerfallQuestSourceDefinition> quests = ReadRecords(records, diagnostics);
        DaggerfallQuestCatalog questCatalog = ReadCatalog(catalog!.Value.Value.Rows, diagnostics);
        DaggerfallQuestTables questTables = ReadTables(tables!.Value.Value, diagnostics);
        CheckCorpora(corpora, questCatalog, quests, diagnostics);
        diagnostics.ThrowIfAny();
        return new(new ReadOnlyDictionary<string, DaggerfallQuestSourceDefinition>(quests), questTables, questCatalog, corpora.AsReadOnly());
    }

    /// <summary>
    /// Whether each record's program runs: a diagnosed record does not; a compiled one does when the quest language
    /// supports every action and clock it states and no corpus withholds it. Withheld and summoned quests are selected
    /// by their corpus categories.
    /// </summary>
    internal static DaggerfallQuestAdmission Admit(DaggerfallQuestSourceSet sources, IDaggerfallQuestLanguage language)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(language);
        Dictionary<string, string> availability = sources.Corpora.SelectMany(corpus => corpus.Categories)
            .SelectMany(category => category.Names.Select(name => (name, category.Availability)))
            .ToDictionary(entry => entry.name, entry => entry.Availability, StringComparer.Ordinal);
        List<DaggerfallQuestRuntimeReceipt> receipts = [];
        List<(string Availability, DaggerfallQuestRuntimeReceipt Receipt)> withheld = [];
        foreach (DaggerfallQuestSourceDefinition source in sources.Quests.Values.OrderBy(source => source.SourceFile, StringComparer.Ordinal))
        {
            List<DaggerfallQuestDiagnosticDefinition> diagnostics = [.. source.Diagnostics];
            if (source.Disposition == DaggerfallQuestDisposition.Compiled)
            {
                diagnostics.AddRange(language.Assess(source));
                try { language.CompileClocks(source); }
                catch (ArgumentException exception) { diagnostics.Add(new(1, source.SourceFile, exception.Message)); }
            }
            string offered = availability.GetValueOrDefault(source.Name, Named);
            if (offered == NotOffered) diagnostics.Add(new(1, source.SourceFile, "Disabled classic quest entries are not ordinary offers."));
            DaggerfallQuestRuntimeReceipt receipt = new(source.Name, source.SourceFile, diagnostics.Count == 0, diagnostics);
            receipts.Add(receipt);
            if (offered is NotOffered or SummonOnly) withheld.Add((offered, receipt));
        }
        return new(receipts.AsReadOnly(), DaggerfallDisabledQuestSelection.From(withheld));
    }

    private static T? Parse<T>(ReadOnlyMemory<byte> bytes, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type, string what, DaggerfallContentDiagnostics diagnostics) where T : class
    {
        try { return JsonSerializer.Deserialize(bytes.Span, type) ?? throw new JsonException("The document is null."); }
        catch (JsonException exception)
        {
            diagnostics.Add($"{what} is malformed: {exception.Message}");
            return null;
        }
    }

    /// <summary>A section one selected pack carries: a second would leave one of the two unread.</summary>
    private static (string Pack, T Value)? OneHome<T>((string Pack, T Value)? held, string pack, T value, string section, DaggerfallContentDiagnostics diagnostics)
    {
        if (held is { } earlier)
        {
            diagnostics.Add($"Quests packs '{earlier.Pack}' and '{pack}' both carry {section}; it has one home.");
            return held;
        }
        return (pack, value);
    }

    private static Dictionary<string, DaggerfallQuestSourceDefinition> ReadRecords(
        List<(string Pack, string Path, DaggerfallQuestRecordDocument Record)> records, DaggerfallContentDiagnostics diagnostics)
    {
        Dictionary<string, string> paths = new(StringComparer.Ordinal);
        foreach ((string pack, string path, DaggerfallQuestRecordDocument record) in records)
        {
            string where = $"Quest record '{path}'";
            if (string.IsNullOrWhiteSpace(record.Name) || string.IsNullOrWhiteSpace(record.SourceFile))
                diagnostics.Add($"{where} states no name or source file.");
            else if (!paths.TryAdd(record.SourceFile, path))
                diagnostics.Add($"{where} and '{paths[record.SourceFile]}' are both '{record.SourceFile}'; a quest has one record.");
            if (record.Messages.GroupBy(message => message.Id).FirstOrDefault(group => group.Count() > 1) is { } repeated)
                diagnostics.Add($"{where} states message {repeated.Key} more than once.");
            // Each line label names one line: saved per-action state and headless tasks are keyed by it.
            HashSet<int> labels = [];
            foreach (DaggerfallQuestBlockDefinition block in record.Blocks)
            {
                if (string.IsNullOrWhiteSpace(block.Kind) || block.FirstLine <= 0 || block.Lines.Count == 0)
                    diagnostics.Add($"{where} has a block with no kind, a first line below 1, or no lines.");
                else if (Enumerable.Range(block.FirstLine, block.Lines.Count).FirstOrDefault(label => !labels.Add(label)) is > 0 and var shared)
                    diagnostics.Add($"{where}'s {block.Kind} block at line {block.FirstLine} labels line {shared}, which another block labels too.");
            }
        }

        // A name two records share resolves to neither: both are diagnosed and neither runs.
        Dictionary<string, int> names = records.GroupBy(entry => entry.Record.Name, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        Dictionary<string, DaggerfallQuestSourceDefinition> quests = new(StringComparer.Ordinal);
        foreach ((_, _, DaggerfallQuestRecordDocument record) in records)
        {
            bool shared = names[record.Name] > 1;
            quests.TryAdd(record.SourceFile, new(record.Name, record.DisplayName ?? string.Empty, record.SourceFile,
                shared ? DaggerfallQuestDisposition.Diagnosed : DaggerfallQuestDisposition.Compiled,
                record.Messages, record.Blocks,
                shared ? [new(1, record.SourceFile, $"Quest name '{record.Name}' resolves to {names[record.Name]} sources.")] : []));
        }
        return quests;
    }

    private static DaggerfallQuestCatalog ReadCatalog(IReadOnlyList<DaggerfallQuestCatalogRow> rows, DaggerfallContentDiagnostics diagnostics)
    {
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        foreach (DaggerfallQuestCatalogRow row in rows)
            if (!names.Add(row.Name)) diagnostics.Add($"Classic quest '{row.Name}' appears twice in the catalog.");
        return new(rows);
    }

    private static DaggerfallQuestTables ReadTables(DaggerfallQuestTablesDocument tables, DaggerfallContentDiagnostics diagnostics)
    {
        HashSet<string> places = new(StringComparer.OrdinalIgnoreCase);
        foreach (DaggerfallQuestPlace place in tables.Places)
            if (!places.Add(place.Name)) diagnostics.Add($"Quest place '{place.Name}' is duplicated.");
        foreach (DaggerfallQuestPlace place in tables.Places)
            if (!places.Contains(place.CanonicalName)) diagnostics.Add($"Quest place '{place.Name}' has missing canonical identity '{place.CanonicalName}'.");
        return new(Table("globals", tables.Globals, true), Table("staticMessages", tables.StaticMessages, false), new(tables.Places),
            Table("sounds", tables.Sounds, false), Table("diseases", tables.Diseases, false), Table("spells", tables.Spells, false),
            new(new(tables.ActorItemTables.Items), new(tables.ActorItemTables.Factions), new(tables.ActorItemTables.Foes)), RegionTemples());

        IReadOnlyList<int> RegionTemples()
        {
            foreach (int temple in tables.RegionTemples)
                if (temple < 0) diagnostics.Add($"Quest table 'regionTemples' names the invalid temple faction {temple}.");
            return [.. tables.RegionTemples];
        }

        DaggerfallQuestTable Table(string key, IReadOnlyList<DaggerfallQuestTableRow> rows, bool globals)
        {
            Dictionary<string, int> aliases = new(StringComparer.OrdinalIgnoreCase);
            foreach (DaggerfallQuestTableRow row in rows)
            {
                if (row.Id < 0 || (globals && row.Id >= DaggerfallVariableAddress.GlobalCount))
                    diagnostics.Add($"Quest table '{key}' gives '{row.Name}' the invalid ID {row.Id}.");
                if (aliases.TryGetValue(row.Name, out int previous) && previous != row.Id)
                    diagnostics.Add($"Quest table '{key}' alias '{row.Name}' resolves to both {previous} and {row.Id}.");
                aliases[row.Name] = row.Id;
            }
            if (rows.Count == 0 || (globals && rows.Select(row => row.Id).Distinct().Count() != DaggerfallVariableAddress.GlobalCount))
                diagnostics.Add($"Quest table '{key}' is empty or omits global slots.");
            return new(rows);
        }
    }

    /// <summary>
    /// A corpus says how its quests are offered, so each name it states must be a quest the packs carry, named once
    /// across every corpus. A category of a catalog group names present rows of that group and activity in catalog
    /// order, and the categories claiming one group together name every present row of it: no row is left out of
    /// its group's offers by accident.
    /// </summary>
    private static void CheckCorpora(List<DaggerfallQuestCorpusDefinition> corpora, DaggerfallQuestCatalog catalog,
        Dictionary<string, DaggerfallQuestSourceDefinition> quests, DaggerfallContentDiagnostics diagnostics)
    {
        HashSet<string> present = [.. quests.Values.Select(quest => quest.Name)];
        Dictionary<string, int> catalogOrder = new(StringComparer.Ordinal);
        foreach ((DaggerfallQuestCatalogRow row, int index) in catalog.Rows.Select((row, index) => (row, index))) catalogOrder.TryAdd(row.Name, index);
        HashSet<string> corpusIds = new(StringComparer.Ordinal), named = new(StringComparer.Ordinal);
        foreach (DaggerfallQuestCorpusDefinition corpus in corpora)
        {
            if (!corpusIds.Add(corpus.Id)) diagnostics.Add($"Quest corpus '{corpus.Id}' is stated twice.");
            HashSet<string> categoryIds = new(StringComparer.Ordinal);
            foreach (DaggerfallQuestCategoryDefinition category in corpus.Categories)
            {
                string where = $"Quest corpus '{corpus.Id}' category '{category.Id}'";
                if (!categoryIds.Add(category.Id)) diagnostics.Add($"{where} is stated twice.");
                // The availability is what the offer paths act on, so one the ruleset cannot interpret would be
                // offered or withheld by accident rather than by the corpus's statement.
                if (category.Availability is not (Ordinary or NotOffered or SummonOnly or Named)
                    || (category.Availability == Named) != (category.CatalogGroup is null))
                    diagnostics.Add($"{where} states availability '{category.Availability}', which takes {(category.Availability == Named ? "no catalog group" : "a catalog group")}.");
                int previous = -1;
                foreach (string name in category.Names)
                {
                    if (!named.Add(name)) diagnostics.Add($"{where} names '{name}', which another category names too.");
                    if (!present.Contains(name)) diagnostics.Add($"{where} names '{name}', which no quest record carries.");
                    if (category.CatalogGroup is not { } group) continue;
                    DaggerfallQuestCatalogRow? row = catalogOrder.TryGetValue(name, out int index) ? catalog.Rows[index] : null;
                    if (row is null || row.Group != group || row.Active != category.Active || index <= previous)
                        diagnostics.Add($"{where} names '{name}', which is not the next {(category.Active ? "active" : "inactive")} {group} row of the catalog.");
                    previous = index;
                }
            }
        }
        foreach (IGrouping<(string Group, bool Active), DaggerfallQuestCategoryDefinition> claimed in corpora.SelectMany(corpus => corpus.Categories)
            .Where(category => category.CatalogGroup is not null).GroupBy(category => (category.CatalogGroup!, category.Active)))
        {
            string[] rows = [.. catalog.Rows.Where(row => row.Group == claimed.Key.Group && row.Active == claimed.Key.Active && present.Contains(row.Name)).Select(row => row.Name).Order(StringComparer.Ordinal)];
            string[] partitioned = [.. claimed.SelectMany(category => category.Names).Order(StringComparer.Ordinal)];
            if (!rows.SequenceEqual(partitioned, StringComparer.Ordinal))
                diagnostics.Add($"The quest corpora's {(claimed.Key.Active ? "active" : "inactive")} {claimed.Key.Group} categories leave out {string.Join(", ", rows.Except(partitioned))} of the catalog's present rows.");
        }
    }
}

/// <summary>Whether each quest record's program runs, and the quests withheld from ordinary offers.</summary>
internal sealed record DaggerfallQuestAdmission(IReadOnlyList<DaggerfallQuestRuntimeReceipt> Receipts, DaggerfallDisabledQuestSelection DisabledSelection);

/// <summary>Whether one quest record's program runs, with the diagnostics that keep it from running.</summary>
internal sealed record DaggerfallQuestRuntimeReceipt(string Name, string SourceFile, bool Runnable, IReadOnlyList<DaggerfallQuestDiagnosticDefinition> Diagnostics);

/// <summary>Admitted quest readiness, consumed by the quest instance owner at start and restore.</summary>
internal sealed class DaggerfallQuestRuntimeAdmission
{
    private readonly IReadOnlyDictionary<string, DaggerfallQuestRuntimeReceipt> _receipts;

    internal DaggerfallQuestRuntimeAdmission(IReadOnlyList<DaggerfallQuestRuntimeReceipt> receipts)
    {
        ArgumentNullException.ThrowIfNull(receipts);
        _receipts = receipts.ToDictionary(receipt => receipt.SourceFile, StringComparer.Ordinal);
        if (_receipts.Count != receipts.Count) throw new ArgumentException("Quest runtime admission repeats a source file.", nameof(receipts));
    }

    internal void RequireRunnable(string sourceFile)
    {
        if (!_receipts.TryGetValue(sourceFile, out DaggerfallQuestRuntimeReceipt? receipt) || receipt.Runnable) return;
        string diagnostics = string.Join("; ", receipt.Diagnostics.Select(diagnostic => $"line {diagnostic.Line}: {diagnostic.Text} ({diagnostic.Reason})"));
        throw new ArgumentException($"Quest source '{sourceFile}' has no admitted runnable program: {diagnostics}");
    }

    /// <summary>Answers whether an admitted receipt can enter an ordinary offer.</summary>
    internal bool IsRunnable(string sourceFile) =>
        !_receipts.TryGetValue(sourceFile, out DaggerfallQuestRuntimeReceipt? receipt) || receipt.Runnable;
}

/// <summary>A quests pack's payload; its <c>worldVariants</c> are read by <see cref="DaggerfallWorldVariantContent"/>.</summary>
internal sealed record DaggerfallQuestPackDocument(
    IReadOnlyList<DaggerfallQuestCatalogRow>? QuestCatalog = null,
    DaggerfallQuestTablesDocument? QuestTables = null,
    IReadOnlyList<DaggerfallQuestCorpusDefinition>? QuestCorpora = null,
    JsonElement? WorldVariants = null);

internal sealed record DaggerfallQuestCatalogDocument(IReadOnlyList<DaggerfallQuestCatalogRow> Rows);

internal sealed record DaggerfallQuestTablesDocument(IReadOnlyList<DaggerfallQuestTableRow> Globals, IReadOnlyList<DaggerfallQuestTableRow> StaticMessages,
    IReadOnlyList<DaggerfallQuestPlace> Places, IReadOnlyList<DaggerfallQuestTableRow> Sounds, IReadOnlyList<DaggerfallQuestTableRow> Diseases,
    IReadOnlyList<DaggerfallQuestTableRow> Spells, DaggerfallQuestActorItemTablesDocument ActorItemTables, IReadOnlyList<int> RegionTemples);

internal sealed record DaggerfallQuestActorItemTablesDocument(IReadOnlyList<DaggerfallQuestItemTableRow> Items,
    IReadOnlyList<DaggerfallQuestFactionTableRow> Factions, IReadOnlyList<DaggerfallQuestFoeTableRow> Foes);

internal sealed record DaggerfallQuestRecordDocument(string Name, string SourceFile, IReadOnlyList<DaggerfallQuestMessageDefinition> Messages,
    IReadOnlyList<DaggerfallQuestBlockDefinition> Blocks, string? DisplayName = null);

/// <summary>
/// The quests pack's documents, read strictly: a member the reader does not know, or one it needs and finds missing,
/// refuses the document, since a hand edit's misspelt field would otherwise be dropped without a word.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(DaggerfallQuestPackDocument))]
[JsonSerializable(typeof(DaggerfallQuestRecordDocument))]
internal partial class DaggerfallQuestJson : JsonSerializerContext;
