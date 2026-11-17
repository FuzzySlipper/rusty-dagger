using System.Text.Json;
using System.Text;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalization;
using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;

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
            case "classic-media":
                var classic = ClassicMediaGroup.Create(Arena2SiteSources.ForClassicMedia(Arena2).ClassicMediaInputs, ClassicProfile());
                // The inventory lists the authored UI art, which the command does not write: it is tracked product content.
                Artifacts(At("output/worldrpg"), classic.GeneratedArtifacts);
                result.Add(At("output/worldrpg/" + ClassicMediaGroup.InventoryRelativePath), classic.WriteInventory("worldrpg", Directory.EnumerateFiles(Arena2).Select(Path.GetFileName).OfType<string>())); break;
            case "sky-media":
                var day = Enumerable.Range(0, SkyFileDecoder.FileCount)
                    .Select(index => new SkyMediaSource(index, Label($"SKY{index:00}.DAT"), Source($"SKY{index:00}.DAT"))).ToArray();
                var night = Enumerable.Range(0, 4)
                    .Select(index => new NightSkyMediaSource(index, Label($"NITE{index:00}I0.IMG"), Source($"NITE{index:00}I0.IMG"))).ToArray();
                var moons = new[] { ("masser", "MOON01I0.CFA"), ("secunda", "MOON00I0.CFA") }
                    .Select(moon => new SkyMoonSource(moon.Item1, Label(moon.Item2), Source(moon.Item2))).ToArray();
                var sky = SkyMediaPublication.Create(day, night, new NightSkyPaletteSource(Label("NIGHTSKY.COL"), Source("NIGHTSKY.COL")),
                    moons, PaletteDecoder.Decode(Source("ART_PAL.COL"), Label("ART_PAL.COL")));
                Artifacts(At("output/worldrpg"), sky.Artifacts); break;
            case "character-presentation":
                var characters = CharacterPresentationGroup.Create(Directory.EnumerateFiles(Arena2).Select(Path.GetFileName).OfType<string>()
                    .Where(CharacterMediaInventory.IsDocumentedFamily).Order(StringComparer.Ordinal).Select(file => (file, (ReadOnlyMemory<byte>)Source(file))).ToArray(),
                    Directory.EnumerateFiles(Arena2, "*.COL").Order(StringComparer.Ordinal).ToDictionary(file => Path.GetFileName(file), file => PaletteDecoder.Decode(File.ReadAllBytes(file), Label(Path.GetFileName(file))), StringComparer.Ordinal),
                    File.ReadAllBytes(Inventory), Path.GetFileName(Path.TrimEndingDirectorySeparator(Arena2)));
                foreach (var artifact in characters.Pass.Artifacts) result.Add(At("output/worldrpg/" + artifact.RelativePath), artifact.Bytes);
                result.Add(At("output/worldrpg/" + CharacterMediaPublisher.IndexRelativePath), CharacterMediaPublisher.WriteIndex(characters.Pass, characters.References, "worldrpg"));
                Section("characterPresentation", characters.Presentation); break;
            case "paper-doll-media":
                var dollArchives = PaperDollItemPublication.Archives.Concat(PaperDollItemPublication.IconArchives).Where(number => File.Exists(Path.Combine(Arena2, $"TEXTURE.{number:000}")))
                    .ToDictionary(number => number, number => (ReadOnlyMemory<byte>)Source($"TEXTURE.{number:000}"));
                var doll = PaperDollItemPublication.Publish(dollArchives, PaletteDecoder.Decode(Source(CharacterMediaReferences.ArtPalette), $"arena2/{CharacterMediaReferences.ArtPalette}"));
                foreach (var artifact in doll.Artifacts) result.Add(At("output/worldrpg/" + artifact.RelativePath), artifact.Bytes);
                result.Add(At("output/worldrpg/" + PaperDollItemPublication.IndexRelativePath), PaperDollItemPublication.WriteIndex(doll, "worldrpg"));
                sections.Add("paperDoll", PaperDollItemPublication.Section(doll).ToJsonString(PublishedJson.Section)); break;
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
            case "source-coverage":
                // The staging tree's files replace the repository content's at the same path; records keep their own label.
                result.Add(At("report.json"), SourceCoverageReconciler.Serialize(Coverage())); break;
            default: throw new ArgumentException($"No expected builder output for {name}.");
        }
    }

    private Arena2ClassicMediaProfile ClassicProfile() => AuthoredUiAssetSet.Read(File.ReadAllBytes(Repo("content/worldrpg/packs/daggerfall.art-replacements/authored-ui/ui-authored-assets.json")), file => File.ReadAllBytes(Path.Combine(AuthoredUiArt, file)));
}
