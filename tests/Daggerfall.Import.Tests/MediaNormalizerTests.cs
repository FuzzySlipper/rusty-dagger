using System.IO.Compression;
using Daggerfall.Import.Normalization;
using Daggerfall.Import.Normalized;
using Xunit;

namespace Daggerfall.Import.Tests;

public sealed class MediaNormalizerTests
{
    [Fact]
    public void PngEncoderProducesRepeatableRgba8Bytes()
    {
        byte[] rgba = [255, 0, 0, 255, 0, 255, 0, 128];

        byte[] first = DeterministicPngEncoder.EncodeRgba8(2, 1, rgba);
        byte[] second = DeterministicPngEncoder.EncodeRgba8(2, 1, rgba);

        Assert.Equal(first, second);
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, first[..8]);
        Assert.Equal(rgba, DecodeRgba8(first, 2, 1));
    }

    [Fact]
    public void AtlasNormalizesCropMirrorGridAndBottomAlignment()
    {
        byte[] edgePixel =
        [
            0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 255, 0, 0, 255,
        ];
        byte[] twoPixels = [255, 0, 0, 255, 0, 0, 255, 255];
        NormalizedSpriteAtlas atlas = SpriteAtlasNormalizer.Normalize(
            [new("cropped", 2, 2, edgePixel), new("mirrored", 2, 1, twoPixels, MirrorHorizontally: true)],
            SpriteAtlasOptions.FixedCellGrid(8, 2, 2, bottomAlign: true) with { CropTransparentPixels = true });

        Assert.Equal((4, 2), (atlas.Width, atlas.Height));
        Assert.Collection(atlas.Frames,
            frame =>
            {
                Assert.Equal(("cropped", 0, 0, 1, 1, 1, 2, 2), (frame.Id, frame.FrameIndex, frame.X, frame.Y, frame.Width, frame.Height, frame.SourceWidth, frame.SourceHeight));
                Assert.Equal(1, frame.Y);
            },
            frame =>
            {
                Assert.Equal(("mirrored", 1, 2, 1, 2, 1), (frame.Id, frame.FrameIndex, frame.X, frame.Y, frame.Width, frame.Height));
                Assert.True(frame.Mirrored);
            });
        byte[] decoded = DecodeRgba8(atlas.PngBytes, atlas.Width, atlas.Height);
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, PixelAt(decoded, atlas.Width, 2, 1));
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, PixelAt(decoded, atlas.Width, 3, 1));
    }

    [Fact]
    public void AtlasRejectsMalformedFramesAndDimensionQuotaOverflow()
    {
        Assert.Throws<ArgumentException>(() => SpriteAtlasNormalizer.Normalize(
            [new("bad", 2, 2, [0])], SpriteAtlasOptions.Grid(8)));
        Assert.Throws<InvalidOperationException>(() => SpriteAtlasNormalizer.Normalize(
            [new("one", 3, 1, Opaque(3, 1)), new("two", 3, 1, Opaque(3, 1)), new("three", 3, 1, Opaque(3, 1))],
            SpriteAtlasOptions.Strip(8)));
    }

    [Fact]
    public void ManifestUsesRegeneratedFactsAndOnlyExplicitAuthoredOverlay()
    {
        NormalizedSpriteAtlas atlas = SpriteAtlasNormalizer.Normalize(
            [new("idle", 1, 1, Opaque(1, 1)), new("strike", 1, 1, Opaque(1, 1))], SpriteAtlasOptions.Grid(8));
        GeneratedMediaArtifact generated = new(
            "sprite/weapon",
            NormalizedMediaKind.WeaponSprite,
            "sprites/weapon.png",
            atlas.PngBytes,
            16,
            16,
            atlas,
            "image/png");
        AuthoredMediaOverlay edited = new(
            "sprite/weapon",
            true,
            "Steel dagger",
            new(0.5F, 0F),
            new(1F, 2F),
            10F,
            false,
            [0, 1, 0]);

        NormalizedMediaDescriptor resource = Assert.Single(MediaManifestNormalizer.Normalize([generated], [edited]).Resources);

        Assert.Equal(ContentDigest.Compute(atlas.PngBytes), resource.ContentDigest);
        Assert.Equal(atlas.PngBytes.LongLength, resource.ByteLength);
        Assert.Equal("sprites/weapon.png", resource.RelativePath);
        Assert.Equal((16, 16), (resource.SourceWidth, resource.SourceHeight));
        Assert.Equal(atlas.Frames, resource.Frames);
        Assert.Equal("Steel dagger", resource.DisplayName);
        Assert.Equal(new[] { 0, 1, 0 }, resource.Sequence);
        Assert.Equal("artifact/sprite/weapon", resource.ToArtifactDescriptor().Id);
    }

    [Fact]
    public void ManifestRejectsUnmarkedOverlayUnknownReferencesAndOversizedArtifacts()
    {
        GeneratedMediaArtifact audio = new("audio/hit", NormalizedMediaKind.Audio, "audio/hit.wav", [1, 2], 0, 0, null, "audio/wav");
        AuthoredMediaOverlay unmarked = new("audio/hit", false, DisplayName: "not permitted");
        AuthoredMediaOverlay unknown = new("audio/missing", true, DisplayName: "missing");

        Assert.Throws<InvalidOperationException>(() => MediaManifestNormalizer.Normalize([audio], [unmarked]));
        Assert.Throws<InvalidOperationException>(() => MediaManifestNormalizer.Normalize([audio], [unknown]));
        Assert.Throws<InvalidOperationException>(() => MediaManifestNormalizer.Normalize([audio], maximumArtifactBytes: 1));
    }

    private static byte[] Opaque(int width, int height)
    {
        byte[] rgba = new byte[width * height * 4];
        for (int index = 3; index < rgba.Length; index += 4) rgba[index] = 255;
        return rgba;
    }

    private static byte[] PixelAt(byte[] rgba, int width, int x, int y)
    {
        int offset = (y * width + x) * 4;
        return rgba[offset..(offset + 4)];
    }

    private static byte[] DecodeRgba8(byte[] png, int expectedWidth, int expectedHeight)
    {
        int offset = 8;
        using MemoryStream idat = new();
        while (offset < png.Length)
        {
            int length = ReadInt32BigEndian(png, offset);
            string type = System.Text.Encoding.ASCII.GetString(png, offset + 4, 4);
            if (type == "IHDR")
            {
                Assert.Equal(expectedWidth, ReadInt32BigEndian(png, offset + 8));
                Assert.Equal(expectedHeight, ReadInt32BigEndian(png, offset + 12));
            }
            else if (type == "IDAT")
            {
                idat.Write(png, offset + 8, length);
            }

            offset += length + 12;
        }

        idat.Position = 0;
        using ZLibStream zlib = new(idat, CompressionMode.Decompress);
        using MemoryStream scanlines = new();
        zlib.CopyTo(scanlines);
        byte[] bytes = scanlines.ToArray();
        Assert.Equal(expectedHeight * (expectedWidth * 4 + 1), bytes.Length);
        byte[] rgba = new byte[expectedWidth * expectedHeight * 4];
        for (int row = 0; row < expectedHeight; row++)
        {
            Assert.Equal(0, bytes[row * (expectedWidth * 4 + 1)]);
            bytes.AsSpan(row * (expectedWidth * 4 + 1) + 1, expectedWidth * 4).CopyTo(rgba.AsSpan(row * expectedWidth * 4));
        }

        return rgba;
    }

    private static int ReadInt32BigEndian(byte[] bytes, int offset) =>
        bytes[offset] << 24 | bytes[offset + 1] << 16 | bytes[offset + 2] << 8 | bytes[offset + 3];
}
