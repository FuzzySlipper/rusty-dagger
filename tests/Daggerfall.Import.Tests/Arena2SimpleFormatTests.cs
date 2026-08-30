using Daggerfall.Import.Arena2;
using Xunit;

namespace Daggerfall.Import.Tests;

public sealed class Arena2SimpleFormatTests
{
    [Fact]
    public void BsaPreservesNamedAndNumericDirectoryIdentities()
    {
        BsaArchive named = BsaArchive.Parse(CreateNamedBsa(("ONE.DAT", [1, 2]), ("TWO.DAT", [3])), "named-fixture");
        Assert.True(named.TryGetByName("TWO.DAT", out BsaRecord? namedRecord));
        Assert.Null(namedRecord!.NumericId);
        Assert.Equal(1, namedRecord.Ordinal);
        Assert.Equal([3], named.GetPayload(namedRecord).ToArray());
        Assert.False(named.TryGetByNumericId(42, out _));

        BsaArchive numeric = BsaArchive.Parse(CreateNumericBsa((42U, [4]), (61000U, [5, 6])), "numeric-fixture");
        Assert.True(numeric.TryGetByNumericId(61000, out BsaRecord? numericRecord));
        Assert.Null(numericRecord!.Name);
        Assert.Equal(1, numericRecord.Ordinal);
        Assert.Equal([5, 6], numeric.GetPayload(numericRecord).ToArray());
        Assert.False(numeric.TryGetByName("61000", out _));
    }

    [Fact]
    public void BsaMalformedHeadersAndPayloadBoundariesFailWithTypedErrors()
    {
        Arena2FormatException header = Assert.Throws<Arena2FormatException>(() => BsaArchive.Parse([], "truncated-bsa"));
        Assert.Equal("truncated-bsa", header.SourceName);

        byte[] malformed = CreateNumericBsa((42U, [1, 2]));
        malformed[^4] = 3;
        Assert.Throws<Arena2FormatException>(() => BsaArchive.Parse(malformed, "boundary-bsa"));
    }

    [Fact]
    public void ImgRequiresHeaderUnlessCallerExplicitlySelectsHeaderlessCanvas()
    {
        byte[] headered = [0, 0, 154, 0, 2, 0, 2, 0, 0, 0, 4, 0, 4, 3, 2, 1];
        IndexedImg image = ImgDecoder.Decode(headered, "headered.img");
        Assert.Equal((short)154, image.YOffset);
        Assert.Equal([4, 3, 2, 1], image.Pixels.ToArray());

        byte[] headerless = new byte[Arena2FormatConstants.HeaderlessUiImgBytes];
        headerless[0] = 7;
        Assert.Throws<Arena2FormatException>(() => ImgDecoder.Decode(headerless, "headerless.img"));
        IndexedImg canvas = ImgDecoder.DecodeHeaderlessUiCanvas(headerless, "ui-canvas.img");
        Assert.True(canvas.IsHeaderless);
        Assert.Equal((ushort)320, canvas.Width);
        Assert.Equal((byte)7, canvas.Pixels.Span[0]);
    }

    [Fact]
    public void FntUsesTheDonorGlyphBitOrientation()
    {
        FntFont font = FntDecoder.Decode(CreateFnt(), "font.fnt");
        FntGlyph glyph = font.Glyphs[0];
        Assert.True(glyph.IsSet(7, 0));
        Assert.True(glyph.IsSet(15, 0));
        Assert.False(glyph.IsSet(0, 0));
    }

    [Fact]
    public void PaletteMakesIndexZeroTransparencyAnExplicitCallerChoice()
    {
        byte[] bytes = new byte[Arena2FormatConstants.PaletteRgbBytes];
        bytes[0] = 1;
        bytes[1] = 2;
        bytes[2] = 3;
        bytes[3] = 4;
        bytes[4] = 5;
        bytes[5] = 6;
        Arena2Palette palette = PaletteDecoder.Decode(bytes, "pal.pal");

        Assert.Equal(new Rgba32(1, 2, 3, 255), palette.ToRgba([0], PaletteAlphaMode.Opaque)[0]);
        Assert.Equal(new Rgba32(1, 2, 3, 0), palette.ToRgba([0], PaletteAlphaMode.IndexZeroTransparent)[0]);
        Assert.Equal(new Rgba32(4, 5, 6, 255), palette.ToRgba([1], PaletteAlphaMode.IndexZeroTransparent)[0]);
    }

    private static byte[] CreateNamedBsa(params (string Name, byte[] Payload)[] records)
    {
        List<byte> result = [];
        result.AddRange(BitConverter.GetBytes((short)records.Length));
        result.AddRange(BitConverter.GetBytes(Arena2FormatConstants.NamedBsaDirectoryType));
        foreach ((_, byte[] payload) in records)
        {
            result.AddRange(payload);
        }

        foreach ((string name, byte[] payload) in records)
        {
            byte[] nameBytes = System.Text.Encoding.ASCII.GetBytes(name);
            result.AddRange(nameBytes);
            result.AddRange(new byte[14 - nameBytes.Length]);
            result.AddRange(BitConverter.GetBytes(payload.Length));
        }

        return result.ToArray();
    }

    private static byte[] CreateNumericBsa(params (uint Id, byte[] Payload)[] records)
    {
        List<byte> result = [];
        result.AddRange(BitConverter.GetBytes((short)records.Length));
        result.AddRange(BitConverter.GetBytes(Arena2FormatConstants.NumericBsaDirectoryType));
        foreach ((_, byte[] payload) in records)
        {
            result.AddRange(payload);
        }

        foreach ((uint id, byte[] payload) in records)
        {
            result.AddRange(BitConverter.GetBytes(id));
            result.AddRange(BitConverter.GetBytes(payload.Length));
        }

        return result.ToArray();
    }

    private static byte[] CreateFnt()
    {
        int bitmapStart = 4 + Arena2FormatConstants.FntGlyphTableBytes;
        byte[] result = new byte[bitmapStart + (Arena2FormatConstants.FntGlyphCount * Arena2FormatConstants.FntGlyphBytes)];
        BitConverter.GetBytes((ushort)14).CopyTo(result, 0);
        BitConverter.GetBytes((ushort)11).CopyTo(result, 2);
        for (int index = 0; index < Arena2FormatConstants.FntGlyphCount; index++)
        {
            int entryOffset = 4 + (index * 4);
            int bitmapOffset = bitmapStart + (index * Arena2FormatConstants.FntGlyphBytes);
            BitConverter.GetBytes((ushort)bitmapOffset).CopyTo(result, entryOffset);
            BitConverter.GetBytes((ushort)14).CopyTo(result, entryOffset + 2);
        }

        result[bitmapStart] = 1;
        result[bitmapStart + 1] = 1;
        return result;
    }
}
