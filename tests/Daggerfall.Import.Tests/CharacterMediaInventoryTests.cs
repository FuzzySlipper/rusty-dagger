using Daggerfall.Import.Arena2;
using Xunit;

namespace Daggerfall.Import.Tests;

/// <summary>
/// The character, face and story-art inventory: every documented family file with its
/// canvas, key and candidate use, the files no decoder reads, and any key two files claim.
/// </summary>
public sealed class CharacterMediaInventoryTests
{
    [Fact]
    public void Reconciles_every_documented_family_count()
    {
        CharacterMediaInventory inventory = ReadInventory();

        // The documented counts are 32;17;9;10;4;9;3;3 for BODY, FACE (including FACES.CIF),
        // CHAR, CUST, NITE, SCBG, CEL and BSS, and the corpus supplies exactly those.
        Assert.Equal(87, inventory.Files.Count);
        Assert.All(CharacterMediaInventory.DocumentedFamilies, family =>
            Assert.Equal(family.Count, inventory.Family(family.Prefix).Count()));
        Assert.Equal(17, inventory.Family("FACE").Count());
        Assert.Contains(inventory.Family("FACE"), file => file.Path == "FACES.CIF");
    }

    [Fact]
    public void Reads_the_families_the_repository_has_decoders_for()
    {
        CharacterMediaInventory inventory = ReadInventory();

        // The five IMG families read through the image decoder: 64 files. The face CIFs are
        // refused by the weapon CIF reader, and the CEL and BSS families have no reader at
        // all, so 23 files are retained with the reason rather than dropped.
        Assert.Equal(60, inventory.Files.Count(file => file.Decode is CharacterMediaDecode.Header or CharacterMediaDecode.Headerless));
        Assert.Equal(27, inventory.Unsupported.Count());
        Assert.Equal(17, inventory.Unsupported.Count(file => file.Family == "FACE"));
        Assert.Equal(6, inventory.Unsupported.Count(file => file.Family is "CEL" or "BSS"));
        // Four supplied IMG files are compressed and the image decoder reads uncompressed
        // records only; they are retained with the decoder's reason.
        Assert.Equal(4, inventory.Unsupported.Count(file => file.Family is "BODY" or "CHAR" or "CUST" or "NITE" or "SCBG"));
        Assert.All(inventory.Unsupported, file => Assert.Equal(CharacterMediaDecode.NotRead, file.Decode));
        Assert.All(inventory.Unsupported, file => Assert.False(string.IsNullOrWhiteSpace(file.Note)));
        Assert.All(inventory.Unsupported, file => Assert.Contains("retained unbound", file.Note, StringComparison.Ordinal));
        Assert.All(inventory.Unsupported, file => Assert.False(string.IsNullOrWhiteSpace(file.UseCandidate)));
    }

    [Fact]
    public void Retains_every_unbound_file_with_its_candidate_use()
    {
        CharacterMediaInventory inventory = ReadInventory();

        Assert.All(inventory.Unbound, file =>
        {
            Assert.Equal(string.Empty, file.Consumer);
            Assert.False(string.IsNullOrWhiteSpace(file.UseCandidate));
            Assert.Contains("candidate use", file.Note, StringComparison.Ordinal);
        });
        Assert.NotEmpty(inventory.Unbound);
    }

    [Fact]
    public void Reports_any_key_two_files_claim()
    {
        CharacterMediaInventory inventory = ReadInventory();

        // FACE00I0.CIF and FACES.CIF are different keys, so the corpus has no clash; the
        // report is what makes a clash visible rather than resolved by ordering.
        Assert.Empty(inventory.DuplicateKeys);

        CharacterMediaInventory clashing = CharacterMediaInventory.Enumerate(
            [("BODY00I0.IMG", ValidImage()), ("body00i0.IMG", ValidImage())],
            new HashSet<string>(StringComparer.Ordinal),
            "fixture");

        (string Key, IReadOnlyList<string> Files) clash = Assert.Single(clashing.DuplicateKeys);
        Assert.Equal("BODY00I0", clash.Key);
        Assert.Equal(2, clash.Files.Count);
    }

    [Fact]
    public void Refuses_a_file_in_no_documented_family()
    {
        Arena2FormatException error = Assert.Throws<Arena2FormatException>(() => CharacterMediaInventory.Enumerate(
            [("NOTAFAMILY.IMG", new byte[16])],
            new HashSet<string>(StringComparer.Ordinal),
            "fixture"));

        Assert.Contains("none of the documented character media families", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Binds_only_the_files_a_consumer_names()
    {
        CharacterMediaInventory inventory = CharacterMediaInventory.Enumerate(
            [("SCBG00I0.IMG", ValidImage())],
            new HashSet<string>(["SCBG00I0.IMG"], StringComparer.Ordinal),
            "fixture");

        CharacterMediaRecord record = Assert.Single(inventory.Files);
        Assert.Equal(CharacterMediaDisposition.Bound, record.Disposition);
        Assert.NotEqual(string.Empty, record.Consumer);
        Assert.Equal("SCBG", record.Family);
    }

    private static byte[] ValidImage() => File.ReadAllBytes(Path.Combine(RepositoryRoot(), "local/arena2/MAIN00I0.IMG"));

    private static CharacterMediaInventory ReadInventory()
    {
        string root = RepositoryRoot();
        string[] extensions = [".IMG", ".CIF", ".CEL", ".BSS"];
        List<(string Path, ReadOnlyMemory<byte> Bytes)> sources = [];
        foreach (string path in Directory.GetFiles(Path.Combine(root, "local/arena2")))
        {
            string name = Path.GetFileName(path);
            string extension = Path.GetExtension(name).ToUpperInvariant();
            if (!extensions.Contains(extension))
            {
                continue;
            }

            bool documented = CharacterMediaInventory.DocumentedFamilies.Any(family =>
                family.Prefix is "CEL" or "BSS"
                    ? extension == $".{family.Prefix}"
                    : name.StartsWith(family.Prefix, StringComparison.OrdinalIgnoreCase));
            if (documented)
            {
                sources.Add((name, File.ReadAllBytes(path)));
            }
        }

        return CharacterMediaInventory.Enumerate(sources, new HashSet<string>(StringComparer.Ordinal), "local/arena2");
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
