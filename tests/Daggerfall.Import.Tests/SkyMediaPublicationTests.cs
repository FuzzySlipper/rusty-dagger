using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalization;
using Daggerfall.Import.Publication;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Text.Json;
using Xunit;

namespace Daggerfall.Import.Tests;

/// <summary>Locks the source-format facts and publication policy used by the weather media closure.</summary>
public sealed class SkyMediaPublicationTests
{
    private static readonly Lazy<SkyMediaPublication> RealPublication = new(CreateRealPublication);

    [CorpusFact]
    public void DecodesTheCompleteThirtyTwoFileDaySkyCorpus()
    {
        foreach (int index in Enumerable.Range(0, SkyFileDecoder.FileCount))
        {
            string name = $"SKY{index:00}.DAT";
            SkyFileDocument document = SkyFileDecoder.Decode(index, File.ReadAllBytes(TestData.Corpus(name)), $"arena2/{name}");
            Assert.Equal(SkyFileDecoder.PaletteCount, document.Palettes.Count);
            Assert.Equal(SkyFileDecoder.RecordCount * SkyFileDecoder.FramesPerRecord, document.Frames.Count);
            Assert.Equal(SkyFileDecoder.FrameBytes, document.Frame(0, 0).IndexedPixels.Length);
        }
    }

    [CorpusFact("SKY00.DAT")]
    public void DecodesEverySkyPaletteAndBothSourceRecords()
    {
        byte[] bytes = File.ReadAllBytes(TestData.Corpus("SKY00.DAT"));
        SkyFileDocument document = SkyFileDecoder.Decode(0, bytes, "arena2/SKY00.DAT");

        Assert.Equal(SkyFileDecoder.FileBytes, bytes.Length);
        Assert.Equal(SkyFileDecoder.PaletteCount, document.Palettes.Count);
        Assert.Equal(SkyFileDecoder.RecordCount * SkyFileDecoder.FramesPerRecord, document.Frames.Count);
        Assert.Equal(bytes.AsSpan(SkyFileDecoder.ImageDataOffset, SkyFileDecoder.FrameBytes).ToArray(),
            document.Frame(0, 0).IndexedPixels.ToArray());
        Assert.Equal(bytes.AsSpan(SkyFileDecoder.ImageDataOffset + (SkyFileDecoder.FramesPerRecord * SkyFileDecoder.FrameBytes), SkyFileDecoder.FrameBytes).ToArray(),
            document.Frame(1, 0).IndexedPixels.ToArray());
        Assert.Equal(0, document.Frame(0, 0).Record);
        Assert.Equal(1, document.Frame(1, 0).Record);
        Assert.Equal(31, document.Frame(0, 31).Frame);
        Assert.Equal(31, document.Frame(1, 31).Frame);
    }

    [CorpusFact("NITE00I0.IMG", "NIGHTSKY.COL")]
    public void ReadsTheNightSourceShapeAndDonorNightPalette()
    {
        byte[] imageBytes = File.ReadAllBytes(TestData.Corpus("NITE00I0.IMG"));
        IndexedImg image = ImgDecoder.DecodeHeaderless(imageBytes, "arena2/NITE00I0.IMG");
        NightSkyPaletteSource palette = new("arena2/NIGHTSKY.COL", File.ReadAllBytes(TestData.Corpus("NIGHTSKY.COL")));

        Assert.Equal((512, 219), (image.Width, image.Height));
        Assert.Equal(SkyMediaPublication.NightSourceBytes, imageBytes.Length);
        Assert.Equal(776, palette.Bytes.Length);
        Assert.Equal(256, palette.Palette.Colors.Length);
        Assert.Equal("arena2/NIGHTSKY.COL", palette.SourcePath);
        Assert.Contains(palette.Palette.Colors.Span.ToArray(), color => color.Red > 63 || color.Green > 63 || color.Blue > 63);
    }

    [Fact]
    public void KeepsTheDonorFrameCurveAndRealizedAfternoonSwapExplicit()
    {
        Assert.Equal(
            [
                (0F, 0F, 1.807229F), (0.083F, 0.15F, 1.9096386F), (0.166F, 0.317F, 1.2799762F),
                (0.5F, 0.5F, 0F), (0.834F, 0.683F, 1.2799761F), (0.917F, 0.85F, 1.9096383F),
                (1F, 1F, 1.8072286F),
            ],
            SkyMediaPublication.DaylightFrameCurve.Select(knot => (knot.Time, knot.Value, knot.Tangent)));
    }

    [CorpusFact]
    public void DayPanoramaPreservesDonorHorizonAndYawZeroCardinalMapping()
    {
        SkyMediaPublication publication = RealPublication.Value;
        byte[] normal = DecodePng(publication.Artifacts.Single(artifact => artifact.RelativePath == "media/sky/resources/sky-00-frame-00.png").Bytes.ToArray());
        byte[] swapped = DecodePng(publication.Artifacts.Single(artifact => artifact.RelativePath == "media/sky/resources/sky-00-frame-00-swapped.png").Bytes.ToArray());
        SkyFileDocument source = SkyFileDecoder.Decode(0, File.ReadAllBytes(TestData.Corpus("SKY00.DAT")), "arena2/SKY00.DAT");
        Arena2Palette palette = source.Palettes[0];

        // Engine sky.wgsl samples yaw-zero (-Z) at u=.25. The donor's west source column
        // zero must therefore be at x=256; source bottom is at the equirectangular horizon.
        Assert.Equal(SourceRed(source, palette, record: 1, row: 0), Channel(normal, 256, 0));
        Assert.Equal(SourceRed(source, palette, record: 1, row: SkyFileDecoder.FrameHeight - 1), Channel(normal, 256, 255));
        Assert.Equal(SourceRed(source, palette, record: 1, row: SkyFileDecoder.FrameHeight - 1), Channel(normal, 256, 256));
        Assert.Equal(SourceRed(source, palette, record: 1, row: SkyFileDecoder.FrameHeight - 1), Channel(normal, 256, SkyMediaPublication.PanoramaHeight - 1));
        Assert.Equal(SourceRed(source, palette, record: 0, row: 0), Channel(normal, 768, 0));

        // The realized afternoon swap changes which source half owns each cardinal phase.
        Assert.Equal(SourceRed(source, palette, record: 0, row: 0), Channel(swapped, 256, 0));
        Assert.Equal(SourceRed(source, palette, record: 1, row: 0), Channel(swapped, 768, 0));
    }

    [CorpusFact]
    public void NightPanoramaPreservesDonorHorizonAndQuarterTurn()
    {
        SkyMediaPublication publication = RealPublication.Value;
        byte[] panorama = DecodePng(publication.Artifacts.Single(artifact => artifact.RelativePath == "media/sky/resources/night-00.png").Bytes.ToArray());
        IndexedImg source = ImgDecoder.DecodeHeaderless(File.ReadAllBytes(TestData.Corpus("NITE00I0.IMG")), "arena2/NITE00I0.IMG");
        Arena2Palette palette = PaletteDecoder.Decode(File.ReadAllBytes(TestData.Corpus("NIGHTSKY.COL")), "arena2/NIGHTSKY.COL");
        Assert.Equal(SourceRed(source, palette, row: 0), Channel(panorama, 256, 0));
        Assert.Equal(SourceGreen(source, palette, row: SkyMediaPublication.NightHeight - 1), ChannelGreen(panorama, 256, 255));
        Assert.Equal(SourceGreen(source, palette, row: SkyMediaPublication.NightHeight - 1), ChannelGreen(panorama, 256, 256));
        Assert.Equal(SourceGreen(source, palette, row: SkyMediaPublication.NightHeight - 1), ChannelGreen(panorama, 256, SkyMediaPublication.PanoramaHeight - 1));
    }

    [CorpusFact("NITE00I0.IMG", "NITE01I0.IMG", "NITE02I0.IMG", "NITE03I0.IMG", "NIGHTSKY.COL")]
    public void NightResourcesUseTheDonorPaletteAndStarMutationForEverySource()
    {
        SkyMediaPublication publication = RealPublication.Value;
        Arena2Palette palette = PaletteDecoder.Decode(File.ReadAllBytes(TestData.Corpus("NIGHTSKY.COL")), "arena2/NIGHTSKY.COL");
        int changedPixels = 0;
        foreach (int nightIndex in Enumerable.Range(0, 4))
        {
            string sourceName = $"NITE{nightIndex:00}I0.IMG";
            IndexedImg source = ImgDecoder.DecodeHeaderless(File.ReadAllBytes(TestData.Corpus(sourceName)), $"arena2/{sourceName}");
            byte[] expected = ExpectedNightPanorama(source, palette, out int sourceStars);
            byte[] actual = DecodePng(publication.Artifacts.Single(artifact => artifact.RelativePath == $"media/sky/resources/night-{nightIndex:00}.png").Bytes.ToArray());
            Assert.Equal(expected, actual);
            Assert.Equal("arena2/NIGHTSKY.COL", publication.Manifest.NightResources.Single(resource => resource.NightIndex == nightIndex).PaletteSourcePath);
            Assert.Equal(776, publication.Manifest.NightResources.Single(resource => resource.NightIndex == nightIndex).PaletteSourceByteLength);
            Assert.Equal(0, publication.Manifest.NightResources.Single(resource => resource.NightIndex == nightIndex).StarPolicy.RandomSeed);
            Assert.Equal(0.004F, publication.Manifest.NightResources.Single(resource => resource.NightIndex == nightIndex).StarPolicy.Chance);
            Assert.True(sourceStars > 0, $"{sourceName} did not exercise the donor star mutation.");
            changedPixels += sourceStars;
        }

        Assert.True(changedPixels > 0);
    }

    [CorpusFact]
    public void WritesRuntimeContentHashesAsCanonicalDigestStrings()
    {
        byte[] manifest = RealPublication.Value.Artifacts
            .Single(artifact => artifact.RelativePath == SkyMediaPublication.ManifestRelativePath).Bytes.ToArray();
        using JsonDocument document = JsonDocument.Parse(manifest);
        foreach (string section in new[] { "resources", "nightResources", "weatherParticles" })
        {
            Assert.All(document.RootElement.GetProperty(section).EnumerateArray(), resource =>
                Assert.Equal(JsonValueKind.String, resource.GetProperty("contentHash").ValueKind));
        }
    }

    private static SkyMediaPublication CreateRealPublication()
    {
        SkyMediaSource[] day = Enumerable.Range(0, SkyFileDecoder.FileCount)
            .Select(index => new SkyMediaSource(index, $"arena2/SKY{index:00}.DAT", File.ReadAllBytes(TestData.Corpus($"SKY{index:00}.DAT"))))
            .ToArray();
        NightSkyMediaSource[] night = Enumerable.Range(0, 4)
            .Select(index => new NightSkyMediaSource(index, $"arena2/NITE{index:00}I0.IMG", File.ReadAllBytes(TestData.Corpus($"NITE{index:00}I0.IMG"))))
            .ToArray();
        NightSkyPaletteSource palette = new("arena2/NIGHTSKY.COL", File.ReadAllBytes(TestData.Corpus("NIGHTSKY.COL")));
        return SkyMediaPublication.Create(day, night, palette);
    }

    private static byte Channel(byte[] pixels, int x, int y) => pixels[((y * SkyMediaPublication.PanoramaWidth) + x) * 4];

    private static byte ChannelGreen(byte[] pixels, int x, int y) => pixels[(((y * SkyMediaPublication.PanoramaWidth) + x) * 4) + 1];

    private static byte SourceRed(SkyFileDocument source, Arena2Palette palette, int record, int row)
    {
        byte index = source.Frame(record, 0).IndexedPixels.Span[row * SkyFileDecoder.FrameWidth];
        return palette.Colors.Span[index].Red;
    }

    private static byte SourceRed(IndexedImg source, Arena2Palette palette, int row)
    {
        byte index = source.Pixels.Span[row * SkyMediaPublication.NightWidth];
        return palette.Colors.Span[index].Red;
    }

    private static byte SourceGreen(IndexedImg source, Arena2Palette palette, int row)
    {
        byte index = source.Pixels.Span[row * SkyMediaPublication.NightWidth];
        return palette.Colors.Span[index].Green;
    }

    private static byte[] ExpectedNightPanorama(IndexedImg source, Arena2Palette palette, out int starCount)
    {
        byte[] indexed = source.Pixels.ToArray();
        Random random = new(0);
        byte[] starColors = [16, 32, 74, 105, 112, 120];
        starCount = 0;
        for (int index = 0; index < indexed.Length; index++)
        {
            int sourceIndex = indexed[index];
            if (sourceIndex > 16 && sourceIndex < 32 && random.NextDouble() < 0.004)
            {
                indexed[index] = starColors[random.Next(starColors.Length)];
                starCount++;
            }
        }

        for (int row = 0; row < SkyMediaPublication.NightHeight; row++)
        {
            int seam = row * SkyMediaPublication.NightWidth + SkyMediaPublication.NightWidth - 2;
            indexed[seam + 1] = indexed[seam];
        }

        byte[] expected = new byte[SkyMediaPublication.PanoramaWidth * SkyMediaPublication.PanoramaHeight * 4];
        for (int y = 0; y < SkyMediaPublication.PanoramaHeight; y++)
        {
            int sourceY = y < SkyMediaPublication.PanoramaHeight / 2
                ? Math.Min(SkyMediaPublication.NightHeight - 1,
                    (int)((long)y * SkyMediaPublication.NightHeight / (SkyMediaPublication.PanoramaHeight / 2)))
                : SkyMediaPublication.NightHeight - 1;
            for (int x = 0; x < SkyMediaPublication.PanoramaWidth; x++)
            {
                int sourceX = ((x + SkyMediaPublication.PanoramaWidth / 4) % SkyMediaPublication.PanoramaWidth)
                    % SkyMediaPublication.NightWidth;
                Rgb24 color = palette.Colors.Span[indexed[sourceY * SkyMediaPublication.NightWidth + sourceX]];
                int offset = ((y * SkyMediaPublication.PanoramaWidth) + x) * 4;
                expected[offset] = color.Red;
                expected[offset + 1] = color.Green;
                expected[offset + 2] = color.Blue;
                expected[offset + 3] = byte.MaxValue;
            }
        }

        return expected;
    }

    private static byte[] DecodePng(byte[] png)
    {
        int position = 8;
        int width = 0;
        int height = 0;
        using MemoryStream compressed = new();
        while (position < png.Length)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(position, 4));
            string type = System.Text.Encoding.ASCII.GetString(png, position + 4, 4);
            ReadOnlySpan<byte> chunk = png.AsSpan(position + 8, length);
            if (type == "IHDR")
            {
                width = BinaryPrimitives.ReadInt32BigEndian(chunk[..4]);
                height = BinaryPrimitives.ReadInt32BigEndian(chunk.Slice(4, 4));
            }
            else if (type == "IDAT")
            {
                compressed.Write(chunk);
            }
            else if (type == "IEND")
            {
                break;
            }

            position += 12 + length;
        }

        compressed.Position = 0;
        using ZLibStream inflate = new(compressed, CompressionMode.Decompress);
        using MemoryStream inflated = new();
        inflate.CopyTo(inflated);
        byte[] raw = inflated.ToArray();
        int stride = width * 4;
        byte[] pixels = new byte[stride * height];
        byte[] previous = new byte[stride];
        for (int row = 0; row < height; row++)
        {
            int filter = raw[row * (stride + 1)];
            Span<byte> current = pixels.AsSpan(row * stride, stride);
            raw.AsSpan((row * (stride + 1)) + 1, stride).CopyTo(current);
            for (int index = 0; index < stride; index++)
            {
                int left = index >= 4 ? current[index - 4] : 0;
                int up = previous[index];
                int upLeft = index >= 4 ? previous[index - 4] : 0;
                int predictor = filter switch
                {
                    1 => left,
                    2 => up,
                    3 => (left + up) / 2,
                    4 => Paeth(left, up, upLeft),
                    _ => 0,
                };
                current[index] = (byte)(current[index] + predictor);
            }

            current.CopyTo(previous);
        }

        Assert.Equal(SkyMediaPublication.PanoramaWidth, width);
        Assert.Equal(SkyMediaPublication.PanoramaHeight, height);
        return pixels;
    }

    private static int Paeth(int left, int up, int upLeft)
    {
        int estimate = left + up - upLeft;
        int leftDistance = Math.Abs(estimate - left);
        int upDistance = Math.Abs(estimate - up);
        int upLeftDistance = Math.Abs(estimate - upLeft);
        return leftDistance <= upDistance && leftDistance <= upLeftDistance ? left : upDistance <= upLeftDistance ? up : upLeft;
    }
}
