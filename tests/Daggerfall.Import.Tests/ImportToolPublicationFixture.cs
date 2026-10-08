using System.Text;
using System.Text.Json;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Audio;
using Daggerfall.Import.Normalization;
using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;
using WorldRpg.SpriteAuthoring;

namespace Daggerfall.Import.Tests;

internal sealed partial class ImportToolFixture
{
    private void ExpectedOther(string name, Dictionary<string, byte[]> result, Dictionary<string, string> sections, Dictionary<string, string> records)
    {
        void Section<T>(string id, T value) => sections.Add(id, JsonSerializer.Serialize(value, PublishedJson.Section));
        void Artifacts(string root, IEnumerable<ImportPublicationArtifact> artifacts)
        { foreach (var artifact in artifacts) result.Add(Path.Combine(root, artifact.RelativePath), artifact.Bytes.ToArray()); }
        switch (name)
        {
            case "text":
                var questionnaires = Enumerable.Range(0, 18).Select(index =>
                    (File.ReadAllText(Path.Combine(Arena2, $"BIOG{index:D2}T0.TXT")), Label($"BIOG{index:D2}T0.TXT"), index, 0)).ToArray();
                var books = Directory.EnumerateFiles(Path.Combine(Arena2, "books"), "BOK*.TXT").Order(StringComparer.Ordinal)
                    .Select(file => (int.Parse(Path.GetFileNameWithoutExtension(file)[3..], System.Globalization.CultureInfo.InvariantCulture), Label("books/" + Path.GetFileName(file)), File.ReadAllBytes(file))).ToArray();
                var text = DaggerfallTextBuilder.BuildAll(Source("TEXT.RSC"), Label("TEXT.RSC"), Source("NAMEGEN.DAT"), Label("NAMEGEN.DAT"), Source("RUMOR.DAT"), Label("RUMOR.DAT"),
                    Source(BioDatReader.FileName), Label(BioDatReader.FileName), questionnaires, Source("BIOG00I0.IMG"), books, Rows, "en");
                Section("text", text.Item1); Section("names", text.Item2); Section("rumors", text.Item3); Section("biographies", text.Item4); Section("books", text.Item5); break;
            case "music-media":
                var music = ClassicMusicPublication.Create(ClassicMusicCatalogue.All, ClassicMusicCatalogue.All.Select(cue => new ClassicMusicSource(cue, File.ReadAllBytes(Repo("local/Sound/" + cue.SourceFile)))).ToArray());
                string musicRoot = At("output/" + ClassicMusicPublication.Group);
                Artifacts(musicRoot, music.Artifacts);
                result.Add(Path.Combine(musicRoot, "media/music/music-inventory.json"), ArtifactInventory.Write(ClassicMusicPublication.Generator, music.Artifacts.Select(artifact => ArtifactInventory.Entry($"{ClassicMusicPublication.Group}/{artifact.RelativePath}", artifact.Bytes.Span, artifact.MediaId)))); break;
            case "classic-media":
                var classic = ClassicMediaGroup.Create(Arena2SiteSources.ForClassicMedia(Arena2).ClassicMediaInputs, ClassicProfile());
                Artifacts(At("output/worldrpg"), classic.Artifacts);
                result.Add(At("output/worldrpg/" + ClassicMediaGroup.InventoryRelativePath), classic.WriteInventory("worldrpg", Directory.EnumerateFiles(Arena2).Select(Path.GetFileName).OfType<string>())); break;
            case "sky-media":
                var day = Enumerable.Range(0, SkyFileDecoder.FileCount)
                    .Select(index => new SkyMediaSource(index, Label($"SKY{index:00}.DAT"), Source($"SKY{index:00}.DAT"))).ToArray();
                var night = Enumerable.Range(0, 4)
                    .Select(index => new NightSkyMediaSource(index, Label($"NITE{index:00}I0.IMG"), Source($"NITE{index:00}I0.IMG"))).ToArray();
                var sky = SkyMediaPublication.Create(day, night, new NightSkyPaletteSource(Label("NIGHTSKY.COL"), Source("NIGHTSKY.COL")));
                Artifacts(At("output/worldrpg"), sky.Artifacts); break;
            case "character-presentation":
                var characters = CharacterPresentationGroup.Create(Directory.EnumerateFiles(Arena2).Select(Path.GetFileName).OfType<string>()
                    .Where(CharacterMediaInventory.IsDocumentedFamily).Order(StringComparer.Ordinal).Select(file => (file, (ReadOnlyMemory<byte>)Source(file))).ToArray(),
                    Directory.EnumerateFiles(Arena2, "*.COL").Order(StringComparer.Ordinal).ToDictionary(file => Path.GetFileName(file), file => PaletteDecoder.Decode(File.ReadAllBytes(file), Label(Path.GetFileName(file))), StringComparer.Ordinal),
                    File.ReadAllBytes(Inventory), File.ReadAllText(Pack), Path.GetFileName(Path.TrimEndingDirectorySeparator(Arena2)));
                foreach (var artifact in characters.Pass.Artifacts) result.Add(At("output/worldrpg/" + artifact.RelativePath), artifact.Bytes);
                result.Add(At("output/worldrpg/" + CharacterMediaPublisher.IndexRelativePath), CharacterMediaPublisher.WriteIndex(characters.Pass, characters.References, "worldrpg"));
                Section("characterPresentation", characters.Presentation); break;
            case "cinematic-media":
                var cinematicPack = Read<DaggerfallCinematicPack>(Pack, "cinematics");
                var updated = cinematicPack.Cinematics.ToArray();
                string scratch = Path.Combine(Path.GetTempPath(), "daggerfall-tool-expected-" + Guid.NewGuid().ToString("N"));
                try
                {
                    for (int i = 0; i < updated.Length; i++)
                    {
                        if (updated[i].Kind != CinematicKind) continue;
                        var artifact = CinematicMediaPublisher.Publish(Path.Combine(Arena2, updated[i].FileName), updated[i], scratch, "worldrpg/media/cinematics");
                        updated[i] = updated[i] with { Artifact = artifact };
                    }
                    foreach (string file in Directory.EnumerateFiles(scratch, "*", SearchOption.AllDirectories)) result.Add(At("output/worldrpg/media/cinematics/" + Path.GetRelativePath(scratch, file)), File.ReadAllBytes(file));
                }
                finally { if (Directory.Exists(scratch)) Directory.Delete(scratch, true); }
                Section("cinematics", cinematicPack with { Cinematics = updated }); break;
            case "quests": ExpectedQuestSections(sections, records); break;
            case "fighters-quest-corpus":
                result.Add(At("output"), FightersGuildQuestCorpusPublication.Serialize(FightersGuildQuestCorpusPublication.Create(Read<DaggerfallQuestCatalog>(Pack, "questCatalog"), Read<DaggerfallQuestPack>(Pack, "questSources"), Read<DaggerfallQuestOriginalSourceSet>(Records, "questOriginalSources")))); break;
            case "classic-quest-corpora":
                foreach (var spec in ClassicQuestCorpusPublication.Specifications)
                    result.Add(At($"output/daggerfall.quests.{spec.Id}.json"), ClassicQuestCorpusPublication.Serialize(ClassicQuestCorpusPublication.Create(spec.Id, Read<DaggerfallQuestCatalog>(Pack, "questCatalog"), Read<DaggerfallQuestPack>(Pack, "questSources"), Read<DaggerfallQuestOriginalSourceSet>(Records, "questOriginalSources"))));
                // The same verb publishes the named story and cure selections beside the offer corpora.
                foreach (string id in NamedQuestCorpusPublication.Selections.Keys)
                    result.Add(At($"output/daggerfall.quests.{id}.json"), NamedQuestCorpusPublication.Serialize(NamedQuestCorpusPublication.Create(id, Read<DaggerfallQuestPack>(Pack, "questSources"), Read<DaggerfallQuestOriginalSourceSet>(Records, "questOriginalSources"))));
                break;
            case "source-manifest":
                var manifest = ImportPublicationManifestSerializer.Deserialize(File.ReadAllBytes(Path.Combine(Publication, ImportPublicationManifestSerializer.ManifestRelativePath)));
                result.Add(At("report.json"), SourceManifestSerializer.Serialize(SourceManifestPublication.ForPublication(manifest.Sources.Select(source => source.Path), Arena2, Path.GetFileName(Inventory), File.ReadAllBytes(Inventory)))); break;
            case "source-coverage":
                var files = new[] { "content/worldrpg", "import-records" }.SelectMany(folder => Directory.EnumerateFiles(Repo(folder), "*.json", SearchOption.AllDirectories)).ToArray();
                var inputs = SourceCoverageInputReader.Read(TestData.RepositoryRoot, files, Rows);
                var imported = inputs.Consumers.Where(c => !c.ContextOnly && c.SourcePath.StartsWith("arena2/", StringComparison.Ordinal)).Select(c => c.SourcePath["arena2/".Length..]).Distinct().ToArray();
                var request = new SourceManifestRequest("arena2", "data/content-source-manifest.csv", Arena2, imported, [], Rows.Where(row => row.Disposition == "excluded").Select(row => row.PathOrPattern.StartsWith("arena2/", StringComparison.Ordinal) ? row.PathOrPattern["arena2/".Length..] : row.PathOrPattern).ToArray());
                var coverage = SourceCoverageReconciler.Build(SourceManifestBuilder.Scan(request, File.ReadAllBytes(Inventory)), inputs.Consumers, inputs.Quests, inputs.Unsupported, Rows);
                result.Add(At("report.json"), SourceCoverageReconciler.Serialize(coverage)); break;
            case "sprite-inspection": result.Add(At("report.json"), SpriteInspectionDocumentSerializer.Serialize(Sprites.ToInspectionDocument())); break;
            case "sprite-overlay-write": result.Add(At("writer-authoring/sprites/test.json"), File.ReadAllBytes(OverlayFile)); break;
            case "write":
            case "rmb-spatial":
                var siteMedia = new Arena2SiteMedia(Arena2SitePublication.RuntimeActorResources(File.ReadAllText(Pack)), ClassicProfile(), [], [])
                {
                    RuntimeNpcResources = Arena2SitePublication.RuntimeNpcResources(File.ReadAllText(Pack)),
                    RuntimeNatureResources = Arena2SitePublication.RuntimeNatureResources(),
                    RuntimeTerrainResources = Arena2SitePublication.RuntimeTerrainResources(),
                };
                ImportPublicationPlan plan = name == "write"
                    ? Arena2SitePublication.Dungeon(Arena2SiteSources.ForSite(Arena2), 17, "Privateer's Hold", siteMedia)
                    : Arena2SitePublication.Rmb(Arena2SiteSources.ForSite(Arena2), RmbRegion, RmbLocation, Interior ? new RmbBuildingSelection(1, 1, 0) : null, siteMedia).Item1;
                plan = plan.WithInvocation(new ImportInvocation(["daggerfall-import-tool", .. Arguments(name)], []));
                Artifacts(At("output"), plan.Artifacts);
                result.Add(At("site.sources.json"), SourceManifestSerializer.Serialize(SourceManifestPublication.ForPublication(plan.Manifest.Sources.Select(source => source.Path), Arena2, Path.GetFileName(Inventory), File.ReadAllBytes(Inventory)))); break;
            default: throw new ArgumentException($"No expected builder output for {name}.");
        }
    }

    private Arena2ClassicMediaProfile ClassicProfile() => AuthoredUiAssetSet.Read(File.ReadAllBytes(Repo("data/ui-authored-assets.json")), file => File.ReadAllBytes(Repo("data/ui-original/" + file)));

    private void ExpectedQuestSections(Dictionary<string, string> sections, Dictionary<string, string> records)
    {
        string Tables(string name) => Donor("Assets/StreamingAssets/Tables/" + name);
        string TableLabel(string name) => PublishedSourcePath.Donor("Assets/StreamingAssets/Tables/" + name);
        byte[] Table(string name) => File.ReadAllBytes(Tables(name));
        var tables = new DaggerfallQuestTables(
            DaggerfallQuestTableReader.Read(Table("Quests-GlobalVars.txt"), TableLabel("Quests-GlobalVars.txt"), globals: true),
            DaggerfallQuestTableReader.Read(Table("Quests-StaticMessages.txt"), TableLabel("Quests-StaticMessages.txt")),
            DaggerfallQuestPlaceReader.Read(Table("Quests-Places.txt"), TableLabel("Quests-Places.txt")),
            DaggerfallQuestTableReader.Read(Table("Quests-Sounds.txt"), TableLabel("Quests-Sounds.txt")),
            DaggerfallQuestTableReader.Read(Table("Quests-Diseases.txt"), TableLabel("Quests-Diseases.txt")),
            DaggerfallQuestTableReader.Read(Table("Quests-Spells.txt"), TableLabel("Quests-Spells.txt")),
            DaggerfallQuestActorItemTableReader.Read(Table("Quests-Items.txt"), TableLabel("Quests-Items.txt"), Table("Quests-Factions.txt"), TableLabel("Quests-Factions.txt"), Table("Quests-Foes.txt"), TableLabel("Quests-Foes.txt")));
        var files = Directory.EnumerateFiles(Donor("Assets/StreamingAssets/Quests"), "*.txt").Order(StringComparer.Ordinal).ToArray();
        List<QuestSourceDocument> documents = [];
        List<(string FileName, string QuestName, int Line, string Reason)> failures = [];
        foreach (string file in files)
        {
            try { documents.Add(QuestSourceReader.Read(File.ReadAllText(file), Path.GetFileName(file), tables.StaticMessages.Lookup, tables.Globals.Lookup)); }
            catch (Arena2FormatException exception) { failures.Add((Path.GetFileName(file), Path.GetFileNameWithoutExtension(file), exception.Offset, exception.Message)); }
        }
        var pack = DaggerfallQuestPackBuilder.Build(documents, failures, PublishedSourcePath.Donor("Assets/StreamingAssets/Quests"), new byte[files.Sum(file => new FileInfo(file).Length)], Rows);
        var catalog = DaggerfallQuestCatalogReader.Read(Table("QuestList-Classic.txt"), TableLabel("QuestList-Classic.txt"), files);
        var originals = DaggerfallQuestOriginalSourceBuilder.Build(Arena2, QuestSourceInventory.Enumerate(Directory.EnumerateFiles(Arena2).Where(file => file.EndsWith(".QBN", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".QRC", StringComparison.OrdinalIgnoreCase)).Select(Path.GetFileName).OfType<string>().ToArray(), Path.GetFileName(Path.TrimEndingDirectorySeparator(Arena2))), pack);
        sections.Add("questCatalog", JsonSerializer.Serialize(catalog, PublishedJson.Section));
        sections.Add("questTables", JsonSerializer.Serialize(tables, PublishedJson.Section));
        sections.Add("questSources", JsonSerializer.Serialize(pack, PublishedJson.Section));
        records.Add("questOriginalSources", JsonSerializer.Serialize(originals, PublishedJson.Section));
    }
}
