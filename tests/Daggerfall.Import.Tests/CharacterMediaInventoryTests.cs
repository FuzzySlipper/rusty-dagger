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

        // Every supplied file but six reads: the face CIFs are sequences of IMG records and
        // FACES.CIF is the fixed-cell grid the classic reader names, so the six files retained
        // with a reason are the three CEL and three BSS files whose readers this repository
        // does not have.
        Assert.Equal(81, inventory.Files.Count(file => file.Decode != Arena2CanvasKind.Unread));
        Assert.Equal(6, inventory.Unsupported.Count());
        Assert.Equal(3, inventory.Unsupported.Count(file => file.Family == "CEL"));
        Assert.Equal(3, inventory.Unsupported.Count(file => file.Family == "BSS"));
        Assert.All(inventory.Unsupported, file => Assert.Equal(Arena2CanvasKind.Unread, file.Decode));
        Assert.All(inventory.Unsupported, file => Assert.False(string.IsNullOrWhiteSpace(file.Note)));
        Assert.All(inventory.Unsupported, file => Assert.Contains("retained unbound", file.Note, StringComparison.Ordinal));
        Assert.All(inventory.Unsupported, file => Assert.False(string.IsNullOrWhiteSpace(file.UseCandidate)));
    }

    [Fact]
    public void Enumerates_the_canvases_inside_a_file_rather_than_one_canvas_per_file()
    {
        CharacterMediaInventory inventory = ReadInventory();

        // Each face CIF is ten records and FACES.CIF is 61 grid cells, so the face family
        // supplies 221 canvases in 17 files: a file count would report 17 and hide 204.
        Assert.Equal(221, inventory.Family("FACE").Sum(file => file.CanvasCount));
        Assert.All(inventory.Family("FACE"), file =>
            Assert.Equal(file.Path == "FACES.CIF" ? 61 : 10, file.CanvasCount));

        // The night and rest art is a documented headerless shape of 512x219, not a compressed
        // IMG record: the four files read at the shape their length establishes.
        Assert.All(inventory.Family("NITE"), file =>
        {
            Assert.Equal(Arena2CanvasKind.HeaderlessCanvas, file.Decode);
            Assert.Equal((512, 219), (file.Canvases[0].Width, file.Canvases[0].Height));
        });
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
            "fixture consumer",
            "fixture");

        (string Key, IReadOnlyList<string> Files) clash = Assert.Single(clashing.DuplicateKeys);
        Assert.Equal("BODY00I0", clash.Key);
        // Which files clash, not just how many: a count alone passes for the wrong pair.
        Assert.Equal(["BODY00I0.IMG", "body00i0.IMG"], clash.Files);
    }

    [Fact]
    public void Names_the_reader_a_format_needs_rather_than_calling_the_source_bad()
    {
        CharacterMediaInventory inventory = ReadInventory();

        // A CEL is not a damaged file: the classic reader reads it with an FLC animation reader
        // this repository does not have, and the note names that reader.
        CharacterMediaRecord portrait = inventory.Family("CEL").First();
        Assert.Contains("FLC animation reader", portrait.Note, StringComparison.Ordinal);
        Assert.Contains("does not have", portrait.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void Keeps_a_binding_whether_or_not_the_file_reads()
    {
        // Binding and decodability are separate facts: a consumer that binds an unread file
        // still binds it, and the record says both.
        CharacterMediaInventory inventory = CharacterMediaInventory.Enumerate(
            [("MAGE.CEL", File.ReadAllBytes(Path.Combine(RepositoryRoot(), "local/arena2/MAGE.CEL")))],
            new HashSet<string>(["MAGE.CEL"], StringComparer.Ordinal),
            "the fixture consumer",
            "fixture");

        CharacterMediaRecord record = Assert.Single(inventory.Files);
        Assert.Equal(MediaBinding.Admitted, record.Binding);
        Assert.Equal(Arena2CanvasKind.Unread, record.Decode);
        Assert.Equal("the fixture consumer", record.Consumer);
    }

    [Fact]
    public void The_documented_inventory_carries_the_families_it_claims()
    {
        // The family counts come from the manifest's CNT-021 row; this checks the corpus against
        // the documented vector in both directions rather than trusting either side, which is
        // what keeps this table from being a second, unchecked source.
        string line = File.ReadLines(Path.Combine(RepositoryRoot(), "docs/coverage/content-source-manifest.csv"))
            .Single(value => value.StartsWith("CNT-021,family,", StringComparison.Ordinal));
        int[] documented = [.. line.Split(',')[5].Split(';', StringSplitOptions.TrimEntries).Select(int.Parse)];

        CharacterMediaInventory inventory = ReadInventory();
        int[] measured = [.. CharacterMediaInventory.DocumentedFamilies.Select(entry => inventory.Family(entry.Prefix).Count())];
        Assert.Equal(CharacterMediaInventory.DocumentedFamilies.Length, documented.Length);
        Assert.Equal(documented, measured);
    }

    [Fact]
    public void Derives_each_canvas_palette_and_refuses_one_that_is_not_supplied()
    {
        CharacterMediaInventory inventory = ReadInventory();
        HashSet<string> supplied = [CharacterMediaReferences.ArtPalette, CharacterMediaReferences.NightskyPalette];
        CharacterMediaReferenceSet set = CharacterMediaReferences.Derive(inventory, supplied);
        IReadOnlyList<CharacterCanvasReference> references = set.Canvases;

        // One reference per readable canvas, and none for a file nothing reads: the six unread
        // files supply no canvas to publish rather than one invented for them.
        Assert.Equal(inventory.Files.Where(file => file.Decode != Arena2CanvasKind.Unread).Sum(file => file.CanvasCount), references.Count);
        Assert.DoesNotContain(references, reference => inventory.Files.Single(file => file.Path == reference.Path).Decode == Arena2CanvasKind.Unread);

        // ... and they are not omitted either: every unread file is accounted for with the donor
        // reader its format would need, three CEL and three BSS, and every canvas carries the
        // binding the inventory recorded so a published reference says who claims it.
        Assert.Equal(6, set.Unavailable.Count);
        Assert.Equal(3, set.Unavailable.Count(entry => entry.Family == "CEL"));
        Assert.Equal(3, set.Unavailable.Count(entry => entry.Family == "BSS"));
        Assert.All(set.Unavailable, entry => Assert.Contains(entry.Family == "CEL" ? "FlcFile.cs" : "BssFile.cs", entry.Reason, StringComparison.Ordinal));
        Assert.All(set.Unavailable, entry => Assert.Equal(inventory.Files.Single(file => file.Path == entry.Path).Binding, entry.Binding));
        Assert.All(references, reference => Assert.Equal(inventory.Files.Single(file => file.Path == reference.Path).Binding, reference.Binding));

        // The classic reader's palette rule: NITE files read with NIGHTSKY.COL, everything else with
        // ART_PAL.COL, and the four NITE files in this corpus prove the rule is applied rather than
        // asserted for a family the corpus does not carry.
        Assert.All(references, reference => Assert.Equal(CharacterMediaReferences.PaletteFor(reference.Path), reference.Palette));
        Assert.Contains(references, reference => reference.Palette == CharacterMediaReferences.NightskyPalette && reference.Path.StartsWith("NITE", StringComparison.Ordinal));
        Assert.Contains(references, reference => reference.Palette == CharacterMediaReferences.ArtPalette && reference.Path.StartsWith("BODY", StringComparison.Ordinal));

        // Paper-doll companions follow the donor naming: a male body of race 0 belongs with its
        // clothed variant, its head and its background, and a female head of race 0 with her bodies
        // and the same race's background.
        CharacterCanvasReference maleBody = references.First(reference => reference.Path == "BODY00I0.IMG");
        Assert.Equal(["BODY00I1.IMG", "FACE00I0.CIF", "SCBG00I0.IMG"], maleBody.Companions);
        CharacterCanvasReference femaleHead = references.First(reference => reference.Path == "FACE10I0.CIF");
        Assert.Equal(["BODY10I0.IMG", "BODY10I1.IMG", "SCBG00I0.IMG"], femaleHead.Companions);

        // A family with no paper-doll rule claims no companion rather than guessing one.
        CharacterCanvasReference other = references.First(reference => reference.Path.StartsWith("CUST", StringComparison.Ordinal));
        Assert.Empty(other.Companions);
        Assert.Contains("no paper-doll companion rule", other.Reason, StringComparison.Ordinal);

        // Every canvas has one stable identity, derived from the file rather than from position, and
        // the concrete paper-doll names follow the donor's layers rather than the file's spelling.
        Assert.Equal(references.Count, references.Select(reference => reference.MediaId).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal("character.body-unclothed.male.00.0", references.First(reference => reference.Path == "BODY00I0.IMG").MediaId);
        Assert.Equal("character.body-clothed.female.00.0", references.First(reference => reference.Path == "BODY10I1.IMG").MediaId);
        Assert.Equal("character.head.female.00.3", references.First(reference => reference.Path == "FACE10I0.CIF" && reference.CanvasIndex == 3).MediaId);
        Assert.All(references, reference => Assert.StartsWith("character.", reference.MediaId, StringComparison.Ordinal));
        Assert.DoesNotContain(references, reference => reference.MediaId.Contains(' ', StringComparison.Ordinal));

        // A canvas whose palette is not supplied is refused by name, not painted with a default.
        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(
            () => CharacterMediaReferences.Derive(inventory, new HashSet<string>(StringComparer.Ordinal) { CharacterMediaReferences.ArtPalette }));
        Assert.Contains("NIGHTSKY.COL", refused.Message, StringComparison.Ordinal);
        Assert.Contains("does not supply", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Reports_the_same_records_whatever_order_the_sources_arrive_in()
    {
        // The consumer/disposition report is deterministic: the same files in any input
        // order produce the same sequence.
        List<(string Path, ReadOnlyMemory<byte> Bytes)> sources =
        [
            ("SCBG00I0.IMG", ValidImage()), ("MAGE.CEL", new byte[16]), ("BODY00I0.IMG", ValidImage()),
        ];
        CharacterMediaInventory forward = CharacterMediaInventory.Enumerate(sources, new HashSet<string>(StringComparer.Ordinal), "none", "fixture");
        CharacterMediaInventory reversed = CharacterMediaInventory.Enumerate([.. Enumerable.Reverse(sources)], new HashSet<string>(StringComparer.Ordinal), "none", "fixture");

        Assert.Equal(forward.Files.Select(file => file.Path), reversed.Files.Select(file => file.Path));
        Assert.Equal(forward.Files.Select(file => file.Binding), reversed.Files.Select(file => file.Binding));
    }

    [Fact]
    public void Refuses_a_path_supplied_twice_or_not_at_all()
    {
        // A path is the identity and the consumer binds against it, so a missing or repeated
        // path is refused rather than producing two records for one file or a null.
        Assert.Throws<ArgumentException>(() => CharacterMediaInventory.Enumerate(
            [(null!, ValidImage())], new HashSet<string>(StringComparer.Ordinal), "none", "fixture"));
        Assert.Throws<ArgumentException>(() => CharacterMediaInventory.Enumerate(
            [("BODY00I0.IMG", ValidImage()), ("BODY00I0.IMG", ValidImage())], new HashSet<string>(StringComparer.Ordinal), "none", "fixture"));
    }

    [Fact]
    public void Refuses_a_file_in_no_documented_family()
    {
        Arena2FormatException error = Assert.Throws<Arena2FormatException>(() => CharacterMediaInventory.Enumerate(
            [("NOTAFAMILY.IMG", new byte[16])],
            new HashSet<string>(StringComparer.Ordinal),
            "fixture consumer",
            "fixture"));

        Assert.Contains("none of the documented character media families", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Binds_only_the_files_a_consumer_names()
    {
        CharacterMediaInventory inventory = CharacterMediaInventory.Enumerate(
            [("SCBG00I0.IMG", ValidImage())],
            new HashSet<string>(["SCBG00I0.IMG"], StringComparer.Ordinal),
            "the fixture consumer",
            "fixture");

        CharacterMediaRecord record = Assert.Single(inventory.Files);
        Assert.Equal(MediaBinding.Admitted, record.Binding);
        Assert.Equal("the fixture consumer", record.Consumer);
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

        return CharacterMediaInventory.Enumerate(sources, new HashSet<string>(StringComparer.Ordinal), "no published consumer yet", "local/arena2");
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
