using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Audio;
using Daggerfall.Import.Normalization;
using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;
using WorldRpg.SpriteAuthoring;
using Xunit;

namespace Daggerfall.Import.Tests;

/// <summary>Private output roots over the real operator corpus; expected bytes come from Import builders,
/// independently of command dispatch or its write branches. No converted fixture is checked in.</summary>
internal sealed partial class ImportToolFixture : IDisposable
{
    private static readonly DateTime OldTime = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    internal string Root { get; } = Path.Combine(Path.GetTempPath(), "daggerfall-tool-tests-" + Guid.NewGuid().ToString("N"));
    private string Arena2 => TestData.CorpusRoot;
    private string Donor(string path) => TestData.Donor(path);
    private string Repo(string path) => Path.Combine(TestData.RepositoryRoot, path);
    private string At(string path) => Path.Combine(Root, path);
    private string Pack => At("pack.json");
    private string Records => At("records.json");
    private string Inventory => At("content-source-manifest.csv");
    internal string InventoryFile => Inventory;
    internal DaggerfallCinematicKind CinematicKind { get; init; } = DaggerfallCinematicKind.Vid;
    internal bool Interior { get; init; }
    // A real one-block exterior exercises the command without repeatedly deriving a whole town's
    // navigation. The building-selection case uses the separately published Charing interior.
    private int RmbRegion => Interior ? 17 : 0;
    private string RmbLocation => Interior ? "Charing" : "Caarcun Manor";
    private string Authored => At("authored.json");
    private string Publication => Repo("content/worldrpg/imports/privateers-hold");
    internal string OverlayFile => At("authoring/sprites/test.json");
    private SpritePublicationSnapshot Sprites => SpritePublicationReader.Read(Publication);
    private IReadOnlyList<SourceInventoryRow> Rows => SourceManifestBuilder.ReadInventory(File.ReadAllBytes(Inventory));

    internal ImportToolFixture()
    {
        Directory.CreateDirectory(Root);
        Copy("content/worldrpg/payloads/daggerfall.imported.json", Pack);
        Copy("import-records/daggerfall.import-records.json", Records);
        Copy("import-records/daggerfall.blocks.json", At("blocks.json"));
        Copy("content/worldrpg/payloads/daggerfall.blocks.json", At("buildings.json"));
        Copy("content/worldrpg/payloads/daggerfall.base.json", Authored);
        Copy("data/content-source-manifest.csv", Inventory);
        var sprites = Sprites;
        SpriteAuthoredOverlayStore.Write(Publication, At("authoring"), "sprites/test.json",
            new(SpriteAuthoredOverlayDocument.CurrentSchemaVersion, sprites.AuthoringBasisDigest, []), sprites.Catalog, sprites.AuthoringBasisDigest);
        foreach (string file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)) File.SetLastWriteTimeUtc(file, OldTime);
    }

    private void Copy(string source, string target) => File.Copy(Repo(source), target);
    internal void PrepareInventoryDrift()
    {
        string[] lines = File.ReadAllLines(Inventory);
        var sourcePaths = ImportPublicationManifestSerializer.Deserialize(File.ReadAllBytes(Path.Combine(Publication,
            ImportPublicationManifestSerializer.ManifestRelativePath))).Sources.Select(source => source.Path).ToHashSet(StringComparer.Ordinal);
        var ids = Rows.Where(row => row.RowType == "family" || sourcePaths.Contains(row.PathOrPattern)).Select(row => row.Id).ToHashSet(StringComparer.Ordinal);
        string updated = string.Join('\n', new[] { lines[0] }.Concat(lines.Skip(1).Where(line => ids.Contains(line.Split(',')[0])))) + "\n";
        string drift = string.Join('\n', updated.Split('\n').Select(line => line.StartsWith("CNT-001.file.MAPS.BSA,", StringComparison.Ordinal)
            ? line.Replace(",imported,", ",uninspected,", StringComparison.Ordinal) : line));
        File.WriteAllText(Inventory, drift);
        File.SetLastWriteTimeUtc(Inventory, OldTime);
    }
    internal byte[] ExpectedReconciledInventory()
    {
        var manifest = SourceManifestSerializer.Deserialize(Expected("source-manifest").Values.Single());
        string scratch = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(scratch, File.ReadAllBytes(Inventory));
            Assert.True(SourceInventoryReconciler.Reconcile(scratch, manifest.Records, update: true).Updated);
            return File.ReadAllBytes(scratch);
        }
        finally { File.Delete(scratch); }
    }
    private byte[] Source(string file) => File.ReadAllBytes(Path.Combine(Arena2, file));
    private static string Label(string file) => PublishedSourcePath.Arena2(file);

    internal string[] Arguments(string name)
    {
        if (name == "internal-strings")
        {
            // Regeneration invokes this after classic text, before the donor supplement exists.
            // The copied fully generated pack must represent that actual input boundary.
            File.WriteAllBytes(Pack, Expected("text")[Pack]);
            File.SetLastWriteTimeUtc(Pack, OldTime);
        }
        Dictionary<string, string> values = new(StringComparer.Ordinal)
        {
            ["--arena2"] = Arena2, ["--pack"] = Pack, ["--authored"] = Authored, ["--records"] = Records,
            ["--inventory"] = Inventory, ["--out"] = At("output"), ["--group"] = "worldrpg", ["--language"] = "en",
            ["--donor-races"] = Donor("Assets/Scripts/Game/Entities/RaceTemplate.cs"),
            ["--donor-formulas"] = Donor("Assets/Scripts/Game/Formulas/FormulaHelper.cs"),
            ["--donor"] = name == "item-template-ledger" ? Donor("Assets/Scripts/Game/Items") : Donor("Assets/Scripts/Utility/EnemyBasics.cs"),
            ["--archive"] = Path.Combine(Arena2, "MONSTER.BSA"), ["--monster"] = Path.Combine(Arena2, "MONSTER.BSA"),
            ["--document"] = At("blocks.json"), ["--buildings"] = At("buildings.json"), ["--blocks"] = At("blocks.json"),
            ["--quest-text"] = Donor("Assets/StreamingAssets/Quests"), ["--tables"] = Donor("Assets/StreamingAssets/Tables"),
            ["--item-templates"] = Donor("Assets/Resources/ItemTemplates.txt"), ["--magic-templates"] = Donor("Assets/Resources/MagicItemTemplates.txt"),
            ["--item-enums"] = Donor("Assets/Scripts/Game/Items/ItemEnums.cs"), ["--item-helper"] = Donor("Assets/Scripts/Game/Items/ItemHelper.cs"),
            ["--flat-captions"] = Donor("Assets/StreamingAssets/Text/Master Localization CSV Files/Internal_Flats.csv"),
            ["--maps-file"] = Donor("Assets/Scripts/API/MapsFile.cs"), ["--label"] = PublishedSourcePath.Donor("Assets/Scripts/API/MapsFile.cs"),
            ["--source"] = Donor("Assets/StreamingAssets/Text/Master Localization CSV Files/Internal_Strings.csv"),
            ["--output"] = At("report.json"), ["--repository"] = TestData.RepositoryRoot, ["--publication"] = Publication,
            ["--region"] = name == "rmb-spatial" ? RmbRegion.ToString(System.Globalization.CultureInfo.InvariantCulture) : "17",
            ["--location"] = name == "rmb-spatial" ? RmbLocation : "Privateer's Hold",
            ["--source-manifest"] = At("site.sources.json"), ["--texture-table"] = "classic", ["--profile"] = Interior ? "interior" : "exterior",
            ["--ui-authored-assets"] = Repo("data/ui-authored-assets.json"), ["--ui-original"] = Repo("data/ui-original"),
            ["--authoring"] = name == "sprite-overlay-write" ? At("writer-authoring") : At("authoring"),
            ["--overlay"] = "sprites/test.json", ["--input"] = OverlayFile, ["--id"] = Sprites.Catalog.Entries.First().Id,
        };
        if (name == "internal-strings") values["--label"] = PublishedSourcePath.Donor("Assets/StreamingAssets/Text/Master Localization CSV Files/Internal_Strings.csv");
        List<string> args = [name];
        foreach (var option in global::Daggerfall.Import.Tool.Program.Commands[name].Options.Where(option => option.IsRequired))
            args.AddRange([option.Name, values[option.Name]]);
        // Exercise nonempty source sets and all real artifact writers, rather than no-op success paths.
        if (name == "music-media") args.AddRange(["--sound", Repo("local/Sound"), "--require-all"]);
        if (name == "classic-media") args.AddRange(["--ui-authored-assets", values["--ui-authored-assets"], "--ui-original", values["--ui-original"]]);
        if (name == "cinematic-media" && CinematicKind == DaggerfallCinematicKind.Flc) args.AddRange(["--kind", "flc"]);
        if (name == "rmb-spatial" && Interior) args.AddRange(["--block-x", "1", "--block-y", "1", "--building", "0"]);
        return [.. args];
    }

    internal Dictionary<string, (string Digest, DateTime Written)> Snapshot() => Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)
        .ToDictionary(file => file, file => (Digest(File.ReadAllBytes(file)), File.GetLastWriteTimeUtc(file)), StringComparer.Ordinal);
    private static string Digest(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    internal void AssertUnchanged(Dictionary<string, (string Digest, DateTime Written)> before) => Assert.Equal(
        before.OrderBy(pair => pair.Key, StringComparer.Ordinal), Snapshot().OrderBy(pair => pair.Key, StringComparer.Ordinal));
    internal void AssertPublication(Dictionary<string, (string Digest, DateTime Written)> before, Dictionary<string, byte[]> expected)
    {
        var after = Snapshot();
        Assert.Equal(before.Keys.Concat(expected.Keys).Distinct().Order(StringComparer.Ordinal), after.Keys.Order(StringComparer.Ordinal));
        foreach ((string path, byte[] bytes) in expected)
        {
            Assert.Equal(Digest(bytes), after[path].Digest);
            Assert.True(after[path].Written > OldTime, $"The command did not write {path}.");
            if (before.TryGetValue(path, out var prior))
                Assert.NotEqual(prior.Written, after[path].Written);
        }
        foreach (var pair in before.Where(pair => !expected.ContainsKey(pair.Key))) Assert.Equal(pair.Value, after[pair.Key]);
    }

    internal Dictionary<string, byte[]> Expected(string name)
    {
        Dictionary<string, byte[]> result = new(StringComparer.Ordinal);
        Dictionary<string, string> sections = new(StringComparer.Ordinal), recordSections = new(StringComparer.Ordinal);
        void Section<T>(string id, T value) => sections.Add(id, JsonSerializer.Serialize(value, PublishedJson.Section));
        switch (name)
        {
            case "locations": Section("locations", DaggerfallLocationBuilder.Build(BsaArchive.Parse(Source("MAPS.BSA"), Label("MAPS.BSA")), BsaArchive.Parse(Source("BLOCKS.BSA"), Label("BLOCKS.BSA")), BsaArchive.Parse(Source("ARCH3D.BSA"), Label("ARCH3D.BSA")))); break;
            case "magic-catalog": sections.Add("magic", Arena2MagicCatalogDocument.Build(Source("SPELLS.STD"), Source("MAGIC.DEF"), Label("SPELLS.STD"), Label("MAGIC.DEF"), Arena2MagicEffectCostTable.Read(File.ReadAllText(Donor("Assets/Scripts/Game/Formulas/FormulaHelper.cs")))).Json); break;
            case "mobile-catalog": sections.Add("mobiles", Arena2MobileCatalogDocument.Build(File.ReadAllText(Donor("Assets/Scripts/Utility/EnemyBasics.cs")), File.ReadAllText(Authored), PublishedSourcePath.Donor("Assets/Scripts/Utility/EnemyBasics.cs"), MonsterArchiveInventory.Enumerate(Source("MONSTER.BSA"), "MONSTER.BSA")).Json); break;
            case "internal-strings": Section("text", DaggerfallInternalStringsBuilder.Merge(Read<DaggerfallText>(Pack, "text"), File.ReadAllBytes(Donor("Assets/StreamingAssets/Text/Master Localization CSV Files/Internal_Strings.csv")), PublishedSourcePath.Donor("Assets/StreamingAssets/Text/Master Localization CSV Files/Internal_Strings.csv"), "en")); break;
            case "climate":
                Section("climate", DaggerfallWorldGridsBuilder.BuildClimate(Source("CLIMATE.PAK"), Label("CLIMATE.PAK"), Rows));
                Section("politic", DaggerfallWorldGridsBuilder.BuildPolitic(Source("POLITIC.PAK"), Label("POLITIC.PAK"), Rows)); break;
            case "factions": Section("factions", DaggerfallFactionsBuilder.WithNpcCaptions(
                DaggerfallFactionsBuilder.Build(File.ReadAllText(Path.Combine(Arena2, "FACTION.TXT")), Label("FACTION.TXT"), Source("FACTION.TXT"), Rows),
                File.ReadAllBytes(Donor("Assets/StreamingAssets/Text/Master Localization CSV Files/Internal_Flats.csv")),
                PublishedSourcePath.Donor("Assets/StreamingAssets/Text/Master Localization CSV Files/Internal_Flats.csv"))); break;
            case "terrain": Section("terrain", DaggerfallTerrainBuilder.Build(Source("WOODS.WLD"), Label("WOODS.WLD"), Rows)); break;
            case "building-name-inputs": Section("buildingNames", DaggerfallBuildingNameInputsBuilder.Build(File.ReadAllBytes(Donor("Assets/Scripts/API/MapsFile.cs")), PublishedSourcePath.Donor("Assets/Scripts/API/MapsFile.cs"))); break;
            case "blocks":
                var blocks = DaggerfallBlocksBuilder.Build(Source("BLOCKS.BSA"), Label("BLOCKS.BSA"), Rows);
                result.Add(At("blocks.json"), Encoding.UTF8.GetBytes(JsonSerializer.Serialize(blocks, PublishedJson.SectionCompact) + "\n"));
                result.Add(At("buildings.json"), Encoding.UTF8.GetBytes(JsonSerializer.Serialize(DaggerfallBlockBuildingSet.From(blocks), PublishedJson.SectionCompact) + "\n")); break;
            case "geometry":
                var blockInputs = JsonSerializer.Deserialize<DaggerfallBlocks>(File.ReadAllBytes(At("blocks.json")), PublishedJson.SectionRead)!;
                var geometry = DaggerfallGeometryBuilder.Build(Source("ARCH3D.BSA"), Label("ARCH3D.BSA"), Rows,
                    blockInputs.Records.SelectMany(block => (block.Objects?.ModelIds ?? []).Select(id => new DaggerfallGeometryUseSite(id, block.SourceKey))).ToArray());
                recordSections.Add("geometry", JsonSerializer.Serialize(geometry, PublishedJson.Section)); break;
            case "catalogs":
                var authored = JsonNode.Parse(File.ReadAllText(Authored))!;
                var vocabulary = authored["vocabulary"]!;
                var catalogs = DaggerfallCatalogBuilder.Build(Rows,
                    vocabulary["attributes"]!.AsArray().Select(v => v!.GetValue<string>()).ToArray(),
                    vocabulary["skills"]!.AsArray().Select(v => v!.GetValue<string>()).ToArray(),
                    Directory.EnumerateFiles(Arena2, "CLASS*.CFG").Order(StringComparer.Ordinal).Select(file => (Path.GetFileName(file), File.ReadAllBytes(file))).ToArray(),
                    authored["actors"]!.AsArray().Select(v => v!["id"]!.GetValue<string>()).Where(id => id != "player").ToArray(),
                    authored["items"]!.AsArray().Select(v => v!["id"]!.GetValue<string>()).ToArray(), File.ReadAllText(Donor("Assets/Scripts/Game/Entities/RaceTemplate.cs")),
                    Source("CLASSES.DAT"), DaggerfallTextBuilder.Build(Source("TEXT.RSC"), Label("TEXT.RSC"), Rows, "en"));
                sections.Add("catalogs", Encoding.UTF8.GetString(DaggerfallCatalogSerializer.Serialize(catalogs, Rows.Select(row => row.PathOrPattern).ToHashSet(StringComparer.Ordinal)))); break;
            case "item-template-ledger":
                var family = Rows.Single(row => row.RowType == "family" && row.Id == "CNT-011");
                var baseline = ItemTemplateBaseline.FromDonorSources(File.ReadAllText(Donor("Assets/Scripts/Game/Items/ItemEnums.cs")), File.ReadAllText(Donor("Assets/Scripts/Game/Items/ItemHelper.cs")), File.ReadAllText(Donor("Assets/Scripts/API/ItemsFile.cs")), PublishedSourcePath.DonorRoot);
                sections.Add(ItemTemplateLedgerBuilder.SectionName, ItemTemplateLedgerBuilder.Build(baseline, family.Id, family.PathOrPattern, ItemTemplateLedgerBuilder.StatusFor(family.Disposition), JsonNode.Parse(File.ReadAllText(Authored))!["items"]!.AsArray().Count).ToJsonString(PublishedJson.Section)); break;
            case "items":
                var templates = File.ReadAllBytes(Donor("Assets/Resources/ItemTemplates.txt"));
                string label = PublishedSourcePath.Donor("Assets/Resources/ItemTemplates.txt");
                var items = DaggerfallItemTemplatesBuilder.Build(ItemTemplateReader.ReadTemplates(Encoding.UTF8.GetString(templates), label), ItemTemplateReader.ReadMagic(File.ReadAllText(Donor("Assets/Resources/MagicItemTemplates.txt")), PublishedSourcePath.Donor("Assets/Resources/MagicItemTemplates.txt")), label, templates, Rows);
                byte[] groupEnums = File.ReadAllBytes(Donor("Assets/Scripts/Game/Items/ItemEnums.cs"));
                byte[] groupHelper = File.ReadAllBytes(Donor("Assets/Scripts/Game/Items/ItemHelper.cs"));
                items = items with { GroupTable = new(
                    PublishedSource.Of(PublishedSourcePath.Donor("Assets/Scripts/Game/Items/ItemEnums.cs"), groupEnums),
                    PublishedSource.Of(PublishedSourcePath.Donor("Assets/Scripts/Game/Items/ItemHelper.cs"), groupHelper),
                    ItemTemplateBaseline.ReadGroupEnumerations(Encoding.UTF8.GetString(groupEnums), Encoding.UTF8.GetString(groupHelper), "fixture item groups")) };
                Section("itemTemplates", items);
                sections.Add(ItemTemplateLedgerBuilder.SectionName, ItemTemplateLedgerBuilder.WithSubstituteTargets(JsonNode.Parse(File.ReadAllText(Pack))![ItemTemplateLedgerBuilder.SectionName]!.AsObject(), items).ToJsonString(PublishedJson.Section)); break;
            case "videos": Section("cinematics", DaggerfallCinematicPackBuilder.Build(Directory.EnumerateFiles(Arena2).Order(StringComparer.OrdinalIgnoreCase)
                .Where(file => file.EndsWith(".VID", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".FLC", StringComparison.OrdinalIgnoreCase))
                .Select(file => (Path.GetFileName(file), file.EndsWith(".VID", StringComparison.OrdinalIgnoreCase) ? DaggerfallCinematicKind.Vid : DaggerfallCinematicKind.Flc, new FileInfo(file).Length, Digest(File.ReadAllBytes(file)))).ToArray(), PublishedSourcePath.Arena2Root, Rows)); break;
            default: ExpectedOther(name, result, sections, recordSections); break;
        }
        if (sections.Count > 0) result.Add(Pack, Encoding.UTF8.GetBytes(TopLevelJsonSectionRewriter.ReplaceOrAppend(File.ReadAllText(Pack), sections.ToDictionary(pair => pair.Key, pair => pair.Value.TrimEnd(), StringComparer.Ordinal))));
        if (recordSections.Count > 0) result.Add(Records, Encoding.UTF8.GetBytes(TopLevelJsonSectionRewriter.ReplaceOrAppend(File.ReadAllText(Records), recordSections.ToDictionary(pair => pair.Key, pair => pair.Value.TrimEnd(), StringComparer.Ordinal))));
        return result;
    }

    private static T Read<T>(string path, string section) where T : class => JsonNode.Parse(File.ReadAllText(path))![section]!.Deserialize<T>(PublishedJson.SectionRead)!;
    public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
}
