using System.Text.Json;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalization;
using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;
using Xunit;

namespace Daggerfall.Import.Tests;

/// <summary>
/// Facts about the missile world visual a site publishes: the mesh the donor draws a flying arrow with,
/// the textures its planes select, and the source record the geometry was decoded from.
/// </summary>
public sealed class ClassicWorldVisualTests
{
    [Fact]
    public void PublishesTheDonorArrowMeshWithItsOwnTexturesAndSourceRecordDigest()
    {
        (GeometryPublication geometry, ClassicWorldVisualRequest request, string arena2) = PublishArrowFromCorpus();
        if (geometry is null) return;
        byte[] archiveBytes = File.ReadAllBytes(Path.Combine(arena2, "ARCH3D.BSA"));
        Arch3dMeshInventory inventory = Arch3dInventoryReader.Read(archiveBytes, "arena2/ARCH3D.BSA");

        // The donor creates model 99800 for an arrow in flight, and the mesh archive carries it as a
        // readable record the arrow prop's block also names. These numbers are pinned rather than read
        // from the visual, so a swap to another mesh fails here.
        GeometryMeshArtifact mesh = Assert.Single(geometry.Meshes, candidate => candidate.MeshId == "99800");
        Assert.Equal(99800, mesh.SourceRecordId);
        Assert.Equal(772, mesh.SourceOrdinal);
        Assert.Equal(15, inventory.Records.Single(record => record.Ordinal == 772).Facts!.DeclaredPoints);
        Assert.Equal(11, inventory.Records.Single(record => record.Ordinal == 772).Facts!.Planes);
        Assert.Equal(36, mesh.Vertices);
        Assert.Equal(14, mesh.Triangles);

        // The request is the geometry publication's own record of the mesh, not a restatement of it.
        Assert.Equal("visual.missile.arrow", request.MediaId);
        Assert.Equal("99800", request.MeshId);
        Assert.Equal(mesh.RelativePath, request.RelativePath);
        Assert.Equal(mesh.ContentDigest, request.ContentDigest.Value);
        Assert.Equal("arena2/ARCH3D.BSA", request.SourceArchive);
        Assert.Equal(772, request.SourceRecordOrdinal);

        // The provenance digest is derived from the record's own bytes: the mesh's planes and the
        // textures they select come from that record, and nothing else in the corpus carries it. The
        // expected digest is computed here from the archive, so it is not the code's own claim.
        Arch3dMeshRecord record = inventory.Records.Single(candidate => candidate.Ordinal == 772);
        byte[] recordBytes = archiveBytes[(int)record.Offset..(int)(record.Offset + record.ByteLength)];
        string recordDigest = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(recordBytes));
        Assert.Equal("26e251e8b5cb89821847de2e7943eee5570627f4132a5720e71b1412e868d8bf", recordDigest);
        Assert.Equal(recordDigest, request.SourceRecordDigest.Value);

        // The arrow's planes select texture archive 1 record 121 and archive 0 record 72. The corpus
        // serves both, and the request names the material slot each texture fills.
        Assert.Equal([(ushort)1, (ushort)0], request.Materials.Select(material => material.Archive));
        Assert.Equal([(ushort)121, (ushort)72], request.Materials.Select(material => material.Record));
        Assert.All(request.Materials, material =>
        {
            Assert.Equal(GeometryMaterialDisposition.Resolved, material.Disposition);
            Assert.Equal($"material/texture-{material.Archive}-{material.Record}", material.MaterialResourceId);
        });
    }

    [Fact]
    public void PublishesTextureBytesThatAreTheRecordsOwnPaletteColours()
    {
        ClassicWorldVisualManifest visual = PublishedArrowDescriptor();

        // Both records are virtual solid-colour records (TEXTURE.000's record is a palette index,
        // TEXTURE.001's is index + 128), so the published PNG must be exactly the palette colour of its
        // record. This pins the decode: a wrong frame, the wrong palette or a transparent alpha fails.
        string root = RepositoryRoot();
        byte[] palette = File.ReadAllBytes(Path.Combine(root, "local/arena2/PAL.PAL"));
        foreach (ClassicWorldVisualTexture texture in visual.Materials)
        {
            byte[] png = File.ReadAllBytes(Path.Combine(root, "content/worldrpg/imports/privateers-hold", texture.RelativePath));
            Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(png)), texture.ContentDigest.Value);
            Assert.Equal(png.LongLength, texture.ByteLength);

            int paletteIndex = texture.Archive == 0 ? texture.Record : texture.Record + 128;
            (int width, int height, byte[] pixels) = DecodePng(png);
            Assert.Equal(32, width);
            Assert.Equal(32, height);
            (byte red, byte green, byte blue) = (palette[8 + (paletteIndex * 3)], palette[8 + (paletteIndex * 3) + 1], palette[8 + (paletteIndex * 3) + 2]);
            Assert.All(Enumerable.Range(0, width * height), pixel => Assert.Equal(
                (red, green, blue, (byte)255),
                (pixels[pixel * 4], pixels[(pixel * 4) + 1], pixels[(pixel * 4) + 2], pixels[(pixel * 4) + 3])));
        }
    }

    [Fact]
    public void RefusesAVisualWhoseMeshTheGeometryPublicationDoesNotCarry()
    {
        byte[] mesh = new byte[64];
        "v2.7"u8.CopyTo(mesh);
        byte[] bytes = new byte[Arena2FormatConstants.BsaHeaderBytes + mesh.Length + Arena2FormatConstants.NumericBsaDirectoryEntryBytes];
        bytes[0] = 1;
        bytes[2] = (byte)(Arena2FormatConstants.NumericBsaDirectoryType & 0xff);
        bytes[3] = (byte)(Arena2FormatConstants.NumericBsaDirectoryType >> 8);
        mesh.CopyTo(bytes, Arena2FormatConstants.BsaHeaderBytes);
        BitConverter.GetBytes(9004u).CopyTo(bytes, Arena2FormatConstants.BsaHeaderBytes + mesh.Length);
        BitConverter.GetBytes(mesh.Length).CopyTo(bytes, Arena2FormatConstants.BsaHeaderBytes + mesh.Length + 4);
        Arch3dMeshInventory inventory = Arch3dInventoryReader.Read(bytes, "arena2/ARCH3D.BSA");
        GeometryPublication geometry = GeometryPublicationBuilder.Create(new GeometryPublicationRequest(
            inventory,
            bytes,
            ReferencedMeshIds: [],
            TextureLeafInventory.Enumerate([], "arena2")));

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() =>
            ClassicWorldVisualRequest.FromGeometry(ClassicMissileVisuals.Published[0], geometry, inventory, bytes));

        Assert.Contains("99800", failure.Message, StringComparison.Ordinal);
        Assert.Contains("does not carry", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesAPublishedDescriptorThatDisagreesWithTheMeshItNames()
    {
        (GeometryPublication geometry, _, _) = PublishArrowFromCorpus();
        if (geometry is null) return;
        ClassicWorldVisualManifest visual = PublishedArrowDescriptor();
        Arena2MediaBundlePublication.ValidateClassicWorldVisuals([visual], geometry);

        InvalidOperationException digest = Assert.Throws<InvalidOperationException>(() => Arena2MediaBundlePublication.ValidateClassicWorldVisuals(
            [visual with { ContentDigest = new ContentDigest(new string('0', 64)) }],
            geometry));
        Assert.Contains("which is not what the geometry publication wrote", digest.Message, StringComparison.Ordinal);

        InvalidOperationException ordinal = Assert.Throws<InvalidOperationException>(() => Arena2MediaBundlePublication.ValidateClassicWorldVisuals(
            [visual with { SourceRecordOrdinal = 771 }],
            geometry));
        Assert.Contains("which is not what the geometry publication wrote", ordinal.Message, StringComparison.Ordinal);

        // The texture keeps a self-consistent identity (record and path agree) so the only thing left
        // to disagree with is the mesh the geometry publication actually wrote.
        InvalidOperationException materials = Assert.Throws<InvalidOperationException>(() => Arena2MediaBundlePublication.ValidateClassicWorldVisuals(
            [visual with
            {
                Materials =
                [
                    visual.Materials[0] with { Record = 120, RelativePath = ClassicWorldVisualRequest.TextureRelativePath(1, 120) },
                    visual.Materials[1],
                ],
            }],
            geometry));
        Assert.Contains("differ from the ones mesh", materials.Message, StringComparison.Ordinal);

        InvalidOperationException archive = Assert.Throws<InvalidOperationException>(() => Arena2MediaBundlePublication.ValidateClassicWorldVisuals(
            [visual with { SourceArchive = "arena2/TEXTURE.000" }],
            geometry));
        Assert.Contains("not the geometry publication's", archive.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesAPublishedDescriptorWhoseTextureIsNotWhereItSaysItIs()
    {
        (GeometryPublication geometry, _, _) = PublishArrowFromCorpus();
        if (geometry is null) return;
        ClassicWorldVisualManifest visual = PublishedArrowDescriptor();

        InvalidOperationException misplaced = Assert.Throws<InvalidOperationException>(() => Arena2MediaBundlePublication.ValidateClassicWorldVisuals(
            [visual with { Materials = [visual.Materials[0] with { RelativePath = "media/world-visuals/texture-1-120.png" }, visual.Materials[1]] }],
            geometry));
        Assert.Contains("which is not where that texture is published", misplaced.Message, StringComparison.Ordinal);

        ArgumentOutOfRangeException unsized = Assert.Throws<ArgumentOutOfRangeException>(() => Arena2MediaBundlePublication.ValidateClassicWorldVisuals(
            [visual with { Materials = [visual.Materials[0] with { ByteLength = 0 }, visual.Materials[1]] }],
            geometry));
        Assert.Contains("with no bytes", unsized.Message, StringComparison.Ordinal);
    }

    private static ClassicWorldVisualManifest PublishedArrowDescriptor()
    {
        string root = RepositoryRoot();
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(
            Path.Combine(root, "content/worldrpg/imports/privateers-hold/media/classic/manifest.json")));
        return Assert.Single(manifest.RootElement.GetProperty("worldVisuals").Deserialize<List<ClassicWorldVisualManifest>>(PublishedJson.SectionRead)!);
    }

    private static (GeometryPublication Geometry, ClassicWorldVisualRequest Request, string Arena2) PublishArrowFromCorpus()
    {
        string arena2 = Path.Combine(RepositoryRoot(), "local/arena2");
        string archivePath = Path.Combine(arena2, "ARCH3D.BSA");
        if (!File.Exists(archivePath)) return (null!, null!, arena2);
        byte[] archiveBytes = File.ReadAllBytes(archivePath);
        Arch3dMeshInventory inventory = Arch3dInventoryReader.Read(archiveBytes, "arena2/ARCH3D.BSA");
        Arch3dMeshRecord record = inventory.Records.Single(candidate =>
            candidate.RecordId == ClassicMissileVisuals.ArrowMeshNumber && candidate.DuplicateOf is null);

        // The leaves come from the mesh's own plane selections, so the test does not restate which
        // textures the arrow draws with as a second authority beside the record that says so.
        TextureLeafInventory textures = TextureLeafInventory.Enumerate(
            [.. record.Facts!.Textures
                .Select(texture => (int)texture.Archive)
                .Distinct()
                .Order()
                .Select(archive => (archive, $"TEXTURE.{archive:000}", (ReadOnlyMemory<byte>)File.ReadAllBytes(Path.Combine(arena2, $"TEXTURE.{archive:000}"))))],
            "arena2");
        GeometryPublication geometry = GeometryPublicationBuilder.Create(new GeometryPublicationRequest(
            inventory,
            archiveBytes,
            ClassicMissileVisuals.MeshIds,
            textures));
        return (geometry, ClassicWorldVisualRequest.FromGeometry(ClassicMissileVisuals.Published[0], geometry, inventory, archiveBytes), arena2);
    }

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md"))) return current.FullName;
        throw new InvalidOperationException("Could not locate the Rusty Dagger repository root.");
    }

    /// <summary>Decodes the repository's own RGBA8 PNG emission without a second image library.</summary>
    private static (int Width, int Height, byte[] Pixels) DecodePng(byte[] png)
    {
        int position = 8;
        int width = 0;
        int height = 0;
        using MemoryStream compressed = new();
        while (position < png.Length)
        {
            int length = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(position, 4));
            string type = System.Text.Encoding.ASCII.GetString(png, position + 4, 4);
            ReadOnlySpan<byte> chunk = png.AsSpan(position + 8, length);
            if (type == "IHDR")
            {
                width = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(chunk[..4]);
                height = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(chunk.Slice(4, 4));
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
        using System.IO.Compression.ZLibStream inflate = new(compressed, System.IO.Compression.CompressionMode.Decompress);
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

        return (width, height, pixels);
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
