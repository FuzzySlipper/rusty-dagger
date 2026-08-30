using Daggerfall.Import.Arena2;
using Xunit;

namespace Daggerfall.Import.Tests;

public sealed class Arena2MediaTests
{
    [Fact]
    public void TextureArchiveDecodesPaddedUncompressedAndVirtualSolidFrames()
    {
        TextureArchive texture = TextureArchive.Parse(CreateUncompressedTexture(), "texture.123");
        TextureRecordInfo info = texture.GetRecordInfo(0);
        Assert.Equal((short)2, info.Width);
        Assert.Equal((short)2, info.Height);
        Assert.Equal([1, 2, 3, 4], texture.DecodeFrame(0, 0).Pixels.ToArray());

        TextureArchive solid = TextureArchive.Parse(CreateTextureHeader(2), "texture.001", TextureSolidPalette.SecondHalf);
        Assert.Equal([129, 129, 129, 129], solid.DecodeFrame(1, 0).Pixels.Span[..4].ToArray());
        Assert.Throws<ArgumentOutOfRangeException>(() => solid.DecodeFrame(2, 0));
    }

    [Theory]
    [InlineData("arena2/TEXTURE.000", 0, 0)]
    [InlineData("arena2/TEXTURE.000", 127, 127)]
    [InlineData("arena2/TEXTURE.001", 0, 128)]
    [InlineData("arena2/TEXTURE.001", 127, 255)]
    public void TextureArchiveInfersClassicVirtualSolidPaletteFromFinalLogicalSourceSegment(string source, int recordOrdinal, byte expectedPaletteIndex)
    {
        TextureArchive texture = TextureArchive.Parse(CreateTextureHeader(128), source);

        Assert.Equal(128, texture.RecordCount);
        IndexedTextureFrame frame = texture.DecodeFrame(recordOrdinal, 0);
        Assert.Equal((ushort)32, frame.Width);
        Assert.Equal((ushort)32, frame.Height);
        Assert.Equal(1024, frame.Pixels.Length);
        Assert.All(frame.Pixels.ToArray(), pixel => Assert.Equal(expectedPaletteIndex, pixel));
    }

    [Fact]
    public void TextureArchiveRejectsVirtualSolidRecordCountsAboveClassicBound()
    {
        Assert.Throws<Arena2FormatException>(() => TextureArchive.Parse(CreateTextureHeader(129), "arena2/TEXTURE.000"));
        Assert.Throws<Arena2FormatException>(() => TextureArchive.Parse(CreateTextureHeader(129), "texture.999", TextureSolidPalette.FirstHalf));
    }

    [Theory]
    [InlineData(0x0900)]
    [InlineData(0x0100)]
    [InlineData(0x0101)]
    public void TextureArchiveTreatsNonRleCompressionTagsAsPaddedUncompressedRows(ushort compression)
    {
        TextureArchive texture = TextureArchive.Parse(CreateUncompressedTexture(compression), $"texture-{compression:X4}");

        Assert.Equal([1, 2, 3, 4], texture.DecodeFrame(0, 0).Pixels.ToArray());
    }

    [Fact]
    public void TextureArchiveDecodesRowRleAndRejectsTruncatedRows()
    {
        TextureArchive texture = TextureArchive.Parse(CreateRleTexture(), "texture-rle");
        Assert.Equal([7, 7, 8, 9], texture.DecodeFrame(0, 0).Pixels.ToArray());

        byte[] truncated = CreateRleTexture();
        Array.Resize(ref truncated, 92);
        TextureArchive malformed = TextureArchive.Parse(truncated, "texture-rle-truncated");
        Assert.Throws<Arena2FormatException>(() => malformed.DecodeFrame(0, 0));
    }

    [Fact]
    public void TextureArchiveRejectsZeroProgressMultiFrameRuns()
    {
        byte[] bytes = CreateMultiFrameTextureWithZeroProgressRun();
        TextureArchive texture = TextureArchive.Parse(bytes, "texture-zero-progress");
        Assert.Throws<Arena2FormatException>(() => texture.DecodeFrame(0, 0));
    }

    [Fact]
    public void WeaponCifDecodesImageAndAnimationFramesAndFailsClosed()
    {
        WeaponCifArchive archive = WeaponCifArchive.Parse(CreateWeaponCif(), "weapon.cif");
        Assert.Equal([1, 2, 3, 4], archive.DecodeFrame(0, 0).Pixels.ToArray());
        Assert.Equal([5, 6, 7, 8], archive.DecodeFrame(1, 0).Pixels.ToArray());
        Assert.Equal([9, 10, 11, 12], archive.DecodeFrame(1, 1).Pixels.ToArray());

        byte[] truncated = CreateWeaponCif();
        Array.Resize(ref truncated, truncated.Length - 1);
        Assert.Throws<Arena2FormatException>(() => WeaponCifArchive.Parse(truncated, "weapon-truncated"));
    }

    [Fact]
    public void NumericSoundArchivePreservesOrdinalAndIdAndWrapsOfflineWave()
    {
        SoundArchive sounds = SoundArchive.Parse(CreateNumericBsa((42U, [4]), (61000U, [5, 6])), "dagger.snd");
        Arena2PcmClip clip = sounds.GetClip(1);
        Assert.Equal(1, clip.Ordinal);
        Assert.Equal(61000U, clip.NumericId);
        Assert.Equal([5, 6], clip.PcmUnsigned8.ToArray());

        byte[] wave = sounds.CreateWave(1);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wave, 0, 4));
        Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(wave, 8, 4));
        Assert.Equal(46, wave.Length);
        Assert.Throws<Arena2FormatException>(() => SoundArchive.Parse(CreateNamedBsa(("SOUND", [1])), "not-numeric"));
    }

    private static byte[] CreateTextureHeader(short recordCount)
    {
        byte[] bytes = new byte[26];
        BitConverter.GetBytes(recordCount).CopyTo(bytes, 0);
        return bytes;
    }

    private static byte[] CreateUncompressedTexture(ushort compression = 0)
    {
        const int recordOffset = 46;
        const int dataOffset = 28;
        byte[] bytes = new byte[recordOffset + dataOffset + 258];
        BitConverter.GetBytes((short)1).CopyTo(bytes, 0);
        BitConverter.GetBytes(recordOffset).CopyTo(bytes, 28);
        WriteTextureRecord(bytes, recordOffset, 2, 2, compression, dataOffset, 1);
        bytes[recordOffset + dataOffset] = 1;
        bytes[recordOffset + dataOffset + 1] = 2;
        bytes[recordOffset + dataOffset + 256] = 3;
        bytes[recordOffset + dataOffset + 257] = 4;
        return bytes;
    }

    private static byte[] CreateRleTexture()
    {
        const int recordOffset = 46;
        const int dataOffset = 28;
        const int rowOneOffset = 36;
        const int rowTwoOffset = 41;
        byte[] bytes = new byte[recordOffset + 50];
        BitConverter.GetBytes((short)1).CopyTo(bytes, 0);
        BitConverter.GetBytes(recordOffset).CopyTo(bytes, 28);
        WriteTextureRecord(bytes, recordOffset, 2, 2, 0x0108, dataOffset, 1);
        WriteInt16(bytes, recordOffset + dataOffset, rowOneOffset);
        WriteUInt16(bytes, recordOffset + dataOffset + 2, 0x8000);
        WriteInt16(bytes, recordOffset + dataOffset + 4, rowTwoOffset);
        WriteUInt16(bytes, recordOffset + dataOffset + 6, 0x8000);
        WriteUInt16(bytes, recordOffset + rowOneOffset, 2);
        WriteInt16(bytes, recordOffset + rowOneOffset + 2, -2);
        bytes[recordOffset + rowOneOffset + 4] = 7;
        WriteUInt16(bytes, recordOffset + rowTwoOffset, 2);
        WriteInt16(bytes, recordOffset + rowTwoOffset + 2, 2);
        bytes[recordOffset + rowTwoOffset + 4] = 8;
        bytes[recordOffset + rowTwoOffset + 5] = 9;
        return bytes;
    }

    private static byte[] CreateMultiFrameTextureWithZeroProgressRun()
    {
        const int recordOffset = 46;
        const int dataOffset = 28;
        const int frameOffset = 4;
        byte[] bytes = new byte[recordOffset + dataOffset + frameOffset + 6];
        BitConverter.GetBytes((short)1).CopyTo(bytes, 0);
        BitConverter.GetBytes(recordOffset).CopyTo(bytes, 28);
        WriteTextureRecord(bytes, recordOffset, 1, 1, 0, dataOffset, 2);
        BitConverter.GetBytes(frameOffset).CopyTo(bytes, recordOffset + dataOffset);
        WriteInt16(bytes, recordOffset + dataOffset + frameOffset, 1);
        WriteInt16(bytes, recordOffset + dataOffset + frameOffset + 2, 1);
        return bytes;
    }

    private static void WriteTextureRecord(byte[] bytes, int offset, short width, short height, ushort compression, int dataOffset, ushort frameCount)
    {
        WriteInt16(bytes, offset + 4, width);
        WriteInt16(bytes, offset + 6, height);
        WriteUInt16(bytes, offset + 8, compression);
        BitConverter.GetBytes((uint)dataOffset).CopyTo(bytes, offset + 14);
        WriteUInt16(bytes, offset + 20, frameCount);
    }

    private static byte[] CreateWeaponCif()
    {
        List<byte> bytes = [];
        AppendInt16(bytes, 0);
        AppendInt16(bytes, 0);
        AppendInt16(bytes, 2);
        AppendInt16(bytes, 2);
        AppendUInt16(bytes, 0);
        AppendUInt16(bytes, 4);
        bytes.AddRange([1, 2, 3, 4]);
        AppendUInt16(bytes, 2);
        AppendUInt16(bytes, 2);
        AppendUInt16(bytes, 2);
        AppendInt16(bytes, 0);
        AppendInt16(bytes, 0);
        AppendInt16(bytes, 0);
        AppendUInt16(bytes, 76);
        AppendUInt16(bytes, 81);
        for (int index = 2; index < 31; index++)
        {
            AppendUInt16(bytes, 0);
        }

        AppendUInt16(bytes, 86);
        bytes.AddRange([3, 5, 6, 7, 8, 3, 9, 10, 11, 12]);
        return bytes.ToArray();
    }

    private static byte[] CreateNamedBsa(params (string Name, byte[] Payload)[] records)
    {
        List<byte> result = [];
        AppendInt16(result, checked((short)records.Length));
        AppendUInt16(result, Arena2FormatConstants.NamedBsaDirectoryType);
        foreach ((_, byte[] payload) in records)
        {
            result.AddRange(payload);
        }

        foreach ((string name, byte[] payload) in records)
        {
            result.AddRange(System.Text.Encoding.ASCII.GetBytes(name));
            result.AddRange(new byte[14 - name.Length]);
            result.AddRange(BitConverter.GetBytes(payload.Length));
        }

        return result.ToArray();
    }

    private static byte[] CreateNumericBsa(params (uint Id, byte[] Payload)[] records)
    {
        List<byte> result = [];
        AppendInt16(result, checked((short)records.Length));
        AppendUInt16(result, Arena2FormatConstants.NumericBsaDirectoryType);
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

    private static void WriteInt16(byte[] bytes, int offset, short value) => BitConverter.GetBytes(value).CopyTo(bytes, offset);
    private static void WriteUInt16(byte[] bytes, int offset, ushort value) => BitConverter.GetBytes(value).CopyTo(bytes, offset);
    private static void AppendInt16(List<byte> bytes, short value) => bytes.AddRange(BitConverter.GetBytes(value));
    private static void AppendUInt16(List<byte> bytes, ushort value) => bytes.AddRange(BitConverter.GetBytes(value));
}
