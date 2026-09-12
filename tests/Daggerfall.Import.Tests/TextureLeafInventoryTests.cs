using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalization;
using Xunit;

namespace Daggerfall.Import.Tests;

/// <summary>
/// The texture-leaf inventory: every documented leaf id with its disposition, the archives
/// that parse, the five supplied leaves that do not, and the forty the corpus does not
/// carry. Frames are validated where they are decoded rather than copied into a table.
/// </summary>
public sealed class TextureLeafInventoryTests
{
    [Fact]
    public void Enumerates_every_documented_leaf_with_its_disposition()
    {
        TextureLeafInventory inventory = ReadInventory();

        Assert.Equal(512, inventory.Leaves.Count);
        Assert.Equal(Enumerable.Range(0, 512), inventory.Leaves.Select(leaf => leaf.Id));
        Assert.Equal(472, inventory.Leaves.Count(leaf => leaf.Disposition != TextureLeafDisposition.NotSupplied));
        Assert.Equal(40, inventory.NotSupplied.Count());
        Assert.Equal(2, inventory.Malformed.Count());
        Assert.Equal(470, inventory.Decoded.Count());
    }

    [Fact]
    public void Retains_every_absent_leaf_id_as_a_source_fact()
    {
        TextureLeafInventory inventory = ReadInventory();

        // The documented ranges say 472 present and 40 absent; the ids are the fact, so the
        // enumeration names them rather than deriving them from a count.
        Assert.Equal(
            [21, 32, 34, 51, 52, 78, 187, 188, 189, 191, 192, 193, 196, 219, 220, 221, 222, 223, 224, 225, 226, 227, 228, 229, 230, 231, 232, 243, 244, 294, 367, 373, 421, 441, 471, 472, 496, 497, 498, 499],
            inventory.NotSupplied.Select(leaf => leaf.Id));
        Assert.All(inventory.NotSupplied, leaf => Assert.Equal(string.Empty, leaf.Path));
    }

    [Fact]
    public void Retains_a_supplied_leaf_that_does_not_parse_with_the_decoders_reason()
    {
        TextureLeafInventory inventory = ReadInventory();

        // Two supplied archives are 46-byte stubs whose record header runs past the file.
        // Each keeps the decoder's own diagnostic, so an operator sees why rather than only
        // that something is missing.
        Assert.Equal([215, 217], inventory.Malformed.Select(leaf => leaf.Id));
        Assert.All(inventory.Malformed, leaf => Assert.False(string.IsNullOrWhiteSpace(leaf.Note)));
        Assert.All(inventory.Malformed, leaf => Assert.Contains("exceeds source length", leaf.Note, StringComparison.Ordinal));
        Assert.All(inventory.Malformed, leaf => Assert.Equal(46, new FileInfo(Path.Combine(RepositoryRoot(), "local/arena2", leaf.Path)).Length));
    }

    [Fact]
    public void Counts_the_records_and_frames_the_arable_leaves_carry()
    {
        TextureLeafInventory inventory = ReadInventory();

        // Record and frame totals are measurements of the corpus, not transcribed numbers.
        Assert.Equal(6718, inventory.Records);
        Assert.Equal(11211, inventory.Frames);
        Assert.True(inventory.TryGet(2, out TextureLeafRecord? leaf));
        Assert.Equal(56, leaf!.Records);
        Assert.Equal(TextureLeafDisposition.Decoded, leaf.Disposition);
    }

    [Fact]
    public void Validates_frame_bounds_and_palette_indices()
    {
        string root = RepositoryRoot();
        TextureArchive archive = TextureArchive.Parse(File.ReadAllBytes(Path.Combine(root, "local/arena2/TEXTURE.002")), "TEXTURE.002");
        Arena2Palette palette = PaletteDecoder.Decode(File.ReadAllBytes(Path.Combine(root, "local/arena2/PAL.PAL")), "PAL.PAL");

        for (int record = 0; record < archive.RecordCount; record++)
        {
            TextureRecordInfo info = archive.GetRecordInfo(record);
            for (int frame = 0; frame < info.FrameCount; frame++)
            {
                IndexedTextureFrame decoded = archive.DecodeFrame(record, frame);
                // The decoded frame's own bounds are the check: a frame that did not match
                // its record header would not produce exactly width x height indices.
                Assert.Equal((int)info.Width, (int)decoded.Width);
                Assert.Equal((int)info.Height, (int)decoded.Height);
                Assert.Equal(decoded.Width * decoded.Height, decoded.Pixels.Length);
                // Every index must address the palette the frame is drawn with.
                foreach (byte index in decoded.Pixels.Span)
                {
                    Assert.InRange(index, 0, palette.Colors.Length - 1);
                }
            }
        }

        // A frame outside the record's bounds is refused by the decoder rather than clipped.
        Assert.True(archive.GetRecordInfo(0).FrameCount > 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => archive.DecodeFrame(0, archive.GetRecordInfo(0).FrameCount));
    }

    [Fact]
    public void Encodes_a_frame_to_identical_png_bytes_every_time()
    {
        string root = RepositoryRoot();
        TextureArchive archive = TextureArchive.Parse(File.ReadAllBytes(Path.Combine(root, "local/arena2/TEXTURE.002")), "TEXTURE.002");
        Arena2Palette palette = PaletteDecoder.Decode(File.ReadAllBytes(Path.Combine(root, "local/arena2/PAL.PAL")), "PAL.PAL");
        IndexedTextureFrame frame = archive.DecodeFrame(0, 0);

        byte[] first = Encode(frame, palette);
        byte[] second = Encode(frame, palette);

        Assert.Equal(first, second);
        Assert.True(first.Length > 8);
        Assert.Equal([0x89, 0x50, 0x4e, 0x47], first[..4]);
    }

    [Fact]
    public void The_documented_inventory_carries_exactly_the_supplied_texture_leaves()
    {
        // The inventory is the authority for which leaves exist, so the corpus and the
        // document are checked against each other in both directions.
        TextureLeafInventory inventory = ReadInventory();
        HashSet<string> documented = [.. Daggerfall.Import.Publication.SourceManifestBuilder
            .ReadInventory(File.ReadAllBytes(Path.Combine(RepositoryRoot(), "docs/coverage/content-source-manifest.csv")))
            .Where(row => row.RowType == "file" && StringComparer.Ordinal.Equals(row.FamilyId, "CNT-018"))
            .Select(row => Path.GetFileName(row.PathOrPattern))];
        string[] supplied = [.. inventory.Leaves.Where(leaf => leaf.Path.Length != 0).Select(leaf => leaf.Path)];

        Assert.Equal(472, documented.Count);
        Assert.DoesNotContain(supplied, path => !documented.Contains(path));
        Assert.DoesNotContain(documented, path => !supplied.Contains(path, StringComparer.Ordinal));
    }

    [Fact]
    public void Refuses_a_reference_to_a_leaf_the_corpus_does_not_carry()
    {
        TextureLeafInventory inventory = ReadInventory();

        // A consumer that cannot name a disposition for a missing leaf fails here rather
        // than publishing a reference to media the corpus does not have.
        InvalidOperationException missing = Assert.Throws<InvalidOperationException>(() => inventory.Require(21, "dungeon geometry"));
        Assert.Contains("dungeon geometry", missing.Message, StringComparison.Ordinal);
        Assert.Contains("21", missing.Message, StringComparison.Ordinal);

        InvalidOperationException malformed = Assert.Throws<InvalidOperationException>(() => inventory.Require(215, "dungeon geometry"));
        Assert.Contains("does not parse", malformed.Message, StringComparison.Ordinal);

        Assert.Equal(2, inventory.Require(2, "dungeon geometry").Id);
    }

    private static byte[] Encode(IndexedTextureFrame frame, Arena2Palette palette)
    {
        byte[] rgba = new byte[frame.Pixels.Length * 4];
        for (int index = 0; index < frame.Pixels.Length; index++)
        {
            Rgb24 color = palette.Colors.Span[frame.Pixels.Span[index]];
            rgba[(index * 4) + 0] = color.Red;
            rgba[(index * 4) + 1] = color.Green;
            rgba[(index * 4) + 2] = color.Blue;
            rgba[(index * 4) + 3] = 0xff;
        }

        return DeterministicPngEncoder.EncodeRgba8(frame.Width, frame.Height, rgba);
    }

    private static TextureLeafInventory ReadInventory()
    {
        string root = RepositoryRoot();
        List<(int Id, string Path, ReadOnlyMemory<byte> Bytes)> sources = [];
        foreach (string path in Directory.GetFiles(Path.Combine(root, "local/arena2"), "TEXTURE.*"))
        {
            string name = Path.GetFileName(path);
            sources.Add((int.Parse(name["TEXTURE.".Length..], System.Globalization.CultureInfo.InvariantCulture), name, File.ReadAllBytes(path)));
        }

        // No explicit solid palette: the decoder infers the palette each archive uses, and
        // forcing one would override that inference and change what parses.
        return TextureLeafInventory.Enumerate(sources, "local/arena2");
    }

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? current = new(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md")))
            {
                return current.FullName;
            }
        }

        throw new InvalidOperationException("Could not locate the Rusty Dagger repository root.");
    }
}
