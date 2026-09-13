using Daggerfall.Import.Arena2;
using Daggerfall.Import.Publication;
using Xunit;

namespace Daggerfall.Import.Tests;

/// <summary>
/// The residual classification: every CNT-027 path in exactly one bounded family with one
/// disposition, the reader this repository would reuse, and the donor reader that establishes
/// the family — or an explicit statement that the donor reads none of it.
/// </summary>
public sealed class ResidualSourceInventoryTests
{
    [Fact]
    public void Classifies_every_documented_residual_path_exactly_once()
    {
        string[] documented = DocumentedResidualPaths();
        ResidualSourceInventory inventory = ReadInventory();

        // The manifest's own residual set is the check: 183 rows, all supplied, each classified
        // once. A path that reached no record would be the silent omission this task forbids.
        Assert.Equal(183, documented.Length);
        Assert.Equal(183, inventory.Files.Count);
        Assert.Equal(
            documented.Order(StringComparer.Ordinal),
            inventory.Files.Select(file => file.Path).Order(StringComparer.Ordinal));
        Assert.All(inventory.Files, file => Assert.False(string.IsNullOrWhiteSpace(file.Note)));
        Assert.All(inventory.Files, file => Assert.NotEqual(SourceRecordDisposition.None, file.Disposition));
        Assert.All(inventory.Files, file => Assert.Equal(file.Documented, ResidualSourceInventory.DocumentedFamilies.Contains(file.Family, StringComparer.Ordinal)));
    }

    [Fact]
    public void Reconciles_the_supplied_families_with_the_documented_list()
    {
        ResidualSourceInventory inventory = ReadInventory();

        // The task names fourteen families. The corpus supplies sixteen groups, six of which the
        // list does not name, and supplies nothing for five of the named families. Both
        // directions are recorded rather than smoothed into "other".
        Assert.Equal(
            ["000", "001", "BIN", "CFA", "CFG", "PAL", "SAV"],
            inventory.UndocumentedFamilies);
        // Reported in the documented list's own order, so the drift reads the way the task states it.
        Assert.Equal(
            ["GFX", "CEL", "BSS", "DEF", "RSC"],
            inventory.MissingDocumentedFamilies);

        Assert.Equal(68, inventory.Family("IMG").Count());
        Assert.Equal(43, inventory.Family("DAT").Count());
        Assert.Equal(40, inventory.Family("CIF").Count());
        Assert.Equal(6, inventory.Family("RCI").Count());
        Assert.Equal(4, inventory.Family("CFA").Count());
        Assert.Equal(4, inventory.Family("PAL").Count());
        Assert.Equal(3, inventory.Family("COL").Count());
        Assert.Equal(3, inventory.Family("LGT").Count());
        Assert.Equal(3, inventory.Family("RAW").Count());
        Assert.Equal(2, inventory.Family("000").Count());
        Assert.Equal(2, inventory.Family("001").Count());
        Assert.Single(inventory.Family("BIN"));
        Assert.Single(inventory.Family("CFG"));
        Assert.Single(inventory.Family("SAV"));
        Assert.Single(inventory.Family("TBL"));
        Assert.Single(inventory.Family("TDE"));
    }

    [Fact]
    public void Reads_the_families_this_repository_has_readers_for_and_names_the_rest()
    {
        ResidualSourceInventory inventory = ReadInventory();

        // Everything a reader reads is unused rather than pending: the residual publication emits
        // a family only once a named consumer exists, and none does yet.
        Assert.Equal(122, inventory.Unused.Count());
        Assert.All(inventory.Unused, file => Assert.Contains("no consumer claims it yet", file.Note, StringComparison.Ordinal));
        Assert.All(inventory.Unused, file => Assert.NotEqual(string.Empty, file.Reader));

        // Every family this repository has a reader for reads all of its supplied files: the six
        // run-length encoded sprite CIFs and the two images whose records declare an unimplemented
        // compression value all read, because the classic readers either decode compression 2 or
        // never consult the field. A file no reader reads would be malformed here, so an empty set
        // is the assertion that the readers and the corpus agree.
        Assert.Empty(inventory.Malformed);
        Assert.Contains(inventory.Unused, file => file.Path == "FIRE00C6.CIF" && file.Reader == "Arena2CanvasReader");
        Assert.Contains(inventory.Unused, file => file.Path == "FRAM00I0.IMG" && file.Reader == "Arena2CanvasReader");

        // A weapon CIF is a CIF by extension and a weapon file by name, and the weapon reader owns
        // it: sending it to the general canvas probe would report a readable file as refused.
        Assert.All(
            inventory.Family("CIF").Where(file => file.Path.StartsWith("WEAPO", StringComparison.Ordinal)),
            file => Assert.Equal("WeaponCifArchive", file.Reader));

        Assert.Equal(61, inventory.Unresolved.Count());
        Assert.All(inventory.Unresolved, file => Assert.Equal(string.Empty, file.Reader));
        Assert.Contains(inventory.Unresolved, file => file.Path == "MRED00I0.CFA" && file.DonorReader == "CfaFile");
        Assert.Contains(inventory.Unresolved, file => file.Path == "HAZE.000" && file.Note.Contains("No donor reader reaches this family", StringComparison.Ordinal));
        Assert.Contains(inventory.Unresolved, file => file.Path == "PAINT.DAT" && file.DonorReader == "PaintFile");
    }

    [Fact]
    public void Names_what_a_supplied_palette_is_rather_than_what_its_name_suggests()
    {
        ResidualSourceInventory inventory = ReadInventory();

        // MAP.PAL is the donor's six-bit world-map palette and the only .PAL file a call site
        // reaches; OLDMAP.PAL is six-bit too, and the donor's x4 rescale is keyed to the exact
        // name MAP.PAL, so the depth is a property of the bytes rather than of the name.
        Assert.Contains(inventory.Family("PAL"), file => file.Path == "MAP.PAL");
        ResidualSourceRecord old = inventory.Family("PAL").Single(file => file.Path == "OLDMAP.PAL");
        Assert.Equal(SourceRecordDisposition.Unused, old.Disposition);
        Assert.Contains("keyed to the exact name MAP.PAL", old.Note, StringComparison.Ordinal);

        // The classic save archive is a named BSA, but the donor builds that path from the save
        // folder rather than Arena2, so this copy's provenance is recorded.
        ResidualSourceRecord save = inventory.Family("SAV").Single();
        Assert.Equal("BsaArchive", save.Reader);
        Assert.Contains("classic save folder rather than from Arena2", save.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_a_repeated_path_or_an_unknown_family()
    {
        // A path is the identity, so classifying one twice would give one file two verdicts; an
        // extension outside the table would otherwise become an unrecorded "other" bucket.
        Assert.Throws<ArgumentException>(() => ResidualSourceInventory.Enumerate(
            [("BUTN00I0.IMG", new byte[16]), ("BUTN00I0.IMG", new byte[16])], "fixture"));
        Assert.Throws<ArgumentException>(() => ResidualSourceInventory.Enumerate(
            [(null!, new byte[16])], "fixture"));

        Arena2FormatException unknown = Assert.Throws<Arena2FormatException>(() => ResidualSourceInventory.Enumerate(
            [("MYSTERY.XYZ", new byte[16])], "fixture"));
        Assert.Contains("cannot be classified or silently omitted", unknown.Message, StringComparison.Ordinal);
    }

    /// <summary>The residual paths the manifest documents, which are the paths this classification covers.</summary>
    private static string[] DocumentedResidualPaths() =>
    [
        .. SourceManifestBuilder.ReadInventory(File.ReadAllBytes(Path.Combine(RepositoryRoot(), "docs/coverage/content-source-manifest.csv")))
            .Where(row => row.RowType == "file" && StringComparer.Ordinal.Equals(row.FamilyId, "CNT-027"))
            .Select(row => Path.GetFileName(row.PathOrPattern)),
    ];

    private static ResidualSourceInventory ReadInventory()
    {
        string arena2 = Path.Combine(RepositoryRoot(), "local/arena2");
        List<(string Path, ReadOnlyMemory<byte> Bytes)> sources = [];
        foreach (string path in DocumentedResidualPaths())
        {
            sources.Add((path, File.ReadAllBytes(Path.Combine(arena2, path))));
        }

        return ResidualSourceInventory.Enumerate(sources, "local/arena2");
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
