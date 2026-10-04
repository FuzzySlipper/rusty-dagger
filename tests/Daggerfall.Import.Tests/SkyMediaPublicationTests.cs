using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalization;
using Xunit;

namespace Daggerfall.Import.Tests;

/// <summary>Locks the source-format facts and publication policy used by the weather media closure.</summary>
public sealed class SkyMediaPublicationTests
{
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

    [CorpusFact("NITE00I0.IMG", "PAL.PAL")]
    public void ReadsTheNightSourceShapeAndSharedFullPalette()
    {
        byte[] imageBytes = File.ReadAllBytes(TestData.Corpus("NITE00I0.IMG"));
        IndexedImg image = ImgDecoder.DecodeHeaderless(imageBytes, "arena2/NITE00I0.IMG");
        Arena2Palette palette = PaletteDecoder.Decode(File.ReadAllBytes(TestData.Corpus("PAL.PAL")), "arena2/PAL.PAL");

        Assert.Equal((512, 219), (image.Width, image.Height));
        Assert.Equal(SkyMediaPublication.NightSourceBytes, imageBytes.Length);
        Assert.Equal(776, File.ReadAllBytes(TestData.Corpus("PAL.PAL")).Length);
        Assert.Equal(256, palette.Colors.Length);
        Assert.Contains(palette.Colors.Span.ToArray(), color => color.Red > 63 || color.Green > 63 || color.Blue > 63);
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
}
