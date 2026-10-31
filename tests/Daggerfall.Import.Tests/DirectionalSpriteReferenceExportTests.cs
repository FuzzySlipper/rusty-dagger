using System.Text.Json;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalization;
using Daggerfall.Import.Tool.ReferenceExports;
using Xunit;

namespace Daggerfall.Import.Tests;

public sealed class DirectionalSpriteReferenceExportTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "daggerfall-directional-export-" + Guid.NewGuid().ToString("N"));

    [CorpusFact("ART_PAL.COL", "TEXTURE.283", "TEXTURE.299", "TEXTURE.381", "TEXTURE.382", "TEXTURE.383", "TEXTURE.384",
        "TEXTURE.385", "TEXTURE.386", "TEXTURE.387", "TEXTURE.388", "TEXTURE.389", "TEXTURE.390", "TEXTURE.391", "TEXTURE.392",
        "TEXTURE.393", "TEXTURE.394", "TEXTURE.395", "TEXTURE.396", "TEXTURE.397", "TEXTURE.398", "TEXTURE.399", "TEXTURE.451",
        "TEXTURE.452", "TEXTURE.453", "TEXTURE.454", "TEXTURE.455", "TEXTURE.456", "TEXTURE.457", "TEXTURE.473", "TEXTURE.491",
        "TEXTURE.492", "TEXTURE.493", "TEXTURE.494", "TEXTURE.495")]
    public void Remaining_sets_preserve_all_source_pixels_and_spectral_alpha_and_account_for_existing_coverage()
    {
        string baseline = Path.Combine(root, "baseline");
        Directory.CreateDirectory(baseline);
        int[] covered = [.. Enumerable.Range(255, 43).Where(archive => archive != 294), 399, .. Enumerable.Range(475, 16)];
        string baselineFile = Path.Combine(baseline, "manifest.json");
        string baselineJson = JsonSerializer.Serialize(new
        {
            mobiles = covered.Select(archive => new { sourceArchive = archive, directory = $"covered-{archive}" }),
        });
        File.WriteAllText(baselineFile, baselineJson);
        string output = Path.Combine(root, "remaining");
        DirectionalSpriteReferenceExport.Run(TestData.CorpusRoot, baseline, output);
        Assert.Equal(baselineJson, File.ReadAllText(baselineFile));

        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(output, "manifest.json")));
        JsonElement document = manifest.RootElement;
        // The original Town/Farm People files are 46-byte stubs with no image record.
        // The audit must report those supplied but unreadable files and continue.
        Assert.Collection(document.GetProperty("warnings").EnumerateArray(),
            warning => Assert.Contains("TEXTURE.215 could not be audited", warning.GetString(), StringComparison.Ordinal),
            warning => Assert.Contains("TEXTURE.217 could not be audited", warning.GetString(), StringComparison.Ordinal));
        JsonElement summary = document.GetProperty("summary");
        Assert.Equal(31, summary.GetProperty("exportedArchives").GetInt32());
        Assert.Equal(24, summary.GetProperty("townspeople").GetInt32());
        Assert.Equal(7, summary.GetProperty("supplemental").GetInt32());
        Assert.Equal(155, summary.GetProperty("referenceDirections").GetInt32());
        Assert.Equal(1257, summary.GetProperty("originalAnimationFrames").GetInt32());
        Assert.Empty(summary.GetProperty("unclassifiedCandidates").EnumerateArray());
        Assert.Equal([215, 217], summary.GetProperty("unreadableArchives").EnumerateArray().Select(value => value.GetInt32()));
        JsonElement relationships = document.GetProperty("relationships");
        Assert.Equal(399, Assert.Single(relationships.GetProperty("overlaps").EnumerateArray()).GetProperty("archive").GetInt32());
        JsonElement alias = Assert.Single(relationships.GetProperty("aliases").EnumerateArray());
        Assert.Equal(299, alias.GetProperty("archive").GetInt32());
        Assert.Equal(283, alias.GetProperty("identicalToArchive").GetInt32());

        Arena2Palette palette = PaletteDecoder.Decode(File.ReadAllBytes(TestData.Corpus("ART_PAL.COL")), "ART_PAL.COL");
        Dictionary<int, TextureArchive> textures = [];
        int checkedImages = 0;
        foreach (JsonElement entry in document.GetProperty("entries").EnumerateArray())
        {
            int archive = entry.GetProperty("sourceArchive").GetInt32();
            Assert.DoesNotContain(archive, covered);
            string directory = Path.Combine(output, entry.GetProperty("directory").GetString()!);
            TextureArchive texture = TextureArchive.Parse(File.ReadAllBytes(TestData.Corpus($"TEXTURE.{archive:000}")), $"TEXTURE.{archive:000}");
            textures[archive] = texture;
            ReferenceImage[] images = entry.GetProperty("images").Deserialize<ReferenceImage[]>(ReferenceExport.Json)!;
            Assert.Equal(5 + Enumerable.Range(0, texture.RecordCount).Sum(record => texture.GetRecordInfo(record).FrameCount), images.Length);
            Assert.Equal(5, entry.GetProperty("frames").GetArrayLength());
            foreach (JsonElement reference in entry.GetProperty("frames").EnumerateArray())
            {
                Assert.False(reference.GetProperty("mirrored").GetBoolean());
                Assert.Equal(reference.GetProperty("orientation").GetInt32(), reference.GetProperty("sourceRecord").GetInt32());
                Assert.Equal(0, reference.GetProperty("sourceFrame").GetInt32());
                Assert.True(File.Exists(Path.Combine(output, reference.GetProperty("file").GetString()!)));
            }
            if (entry.GetProperty("family").GetString() == "townspeople")
            {
                Assert.Equal(20, images.Count(image => image.Role.StartsWith("walk/", StringComparison.Ordinal)));
                Assert.Equal(2, images.Count(image => image.Role == "idle/front"));
                Assert.All(images.Where(image => image.Role == "idle/front"), image => Assert.Equal(5, image.Record));
            }
            foreach (ReferenceImage image in images)
            {
                IndexedTextureFrame frame = texture.DecodeFrame(image.Record, image.Frame);
                TextureRecordInfo info = texture.GetRecordInfo(image.Record);
                byte[] expected = archive == 473
                    ? Arena2SpectralTextures.ToRgbaBytes(frame.Pixels.Span, palette, texture.Source)
                    : frame.ToRgba(palette, PaletteAlphaMode.IndexZeroTransparent);
                DeterministicPngImage png = DeterministicPngReader.ReadRgba8(File.ReadAllBytes(Path.Combine(directory, image.File)), image.File);
                Assert.Equal(((int)frame.Width, (int)frame.Height, info.XOffset, info.YOffset),
                    (png.Width, png.Height, image.XOffset, image.YOffset));
                Assert.True(expected.AsSpan().SequenceEqual(png.Rgba), $"Source pixels differ: {archive}/{image.File}");
                if (archive == 473) Assert.Contains((byte)180, png.Rgba.Where((_, index) => index % 4 == 3));
                checkedImages++;
            }
        }
        Assert.Equal(1412, checkedImages);
        Assert.Equal(checkedImages, document.GetProperty("imageCount").GetInt32());
        Assert.Equal(checkedImages, Directory.EnumerateFiles(output, "*.png", SearchOption.AllDirectories).Count());
        // Mounted source frames really have different phase counts; no four-frame assumption
        // may silently truncate the eighth/sixteenth phases in these archives.
        Assert.Equal(16, textures[495].GetRecordInfo(1).FrameCount);
        Assert.True(File.Exists(Path.Combine(output, "supplemental", "texture-495-female-light-horse-rider", "frames", "record-001", "frame-015.png")));
    }

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}
