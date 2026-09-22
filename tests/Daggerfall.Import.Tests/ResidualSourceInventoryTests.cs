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

        // Current publication closure includes palettes and menu images from this residual set.
        // A readable source without an admitted consumer remains unused; a published source must
        // retain that consumer disposition rather than being reset by the residual classifier.
        Assert.Equal(
            ["ART_PAL.COL", "CHGN00I0.IMG", "DIE_00I0.IMG", "MAP.PAL", "PAL.PAL", "PICK02I0.IMG", "PICK03I0.IMG", "PRIS00I0.IMG", "TITL00I0.IMG"],
            inventory.Imported.Select(file => file.Path).Order(StringComparer.Ordinal));
        Assert.All(inventory.Imported, file => Assert.Contains("a consumer claims it", file.Note, StringComparison.Ordinal));

        Assert.Equal(113, inventory.Unused.Count());
        Assert.All(inventory.Unused, file => Assert.Contains("no consumer named here claims it", file.Note, StringComparison.Ordinal));
        Assert.All(inventory.Unused, file => Assert.NotEqual(string.Empty, file.Reader));

        // Every family this repository has a reader for reads all of its supplied files: the six
        // five run-length encoded sprite CIFs and the two images whose records declare an unimplemented
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
        // A path with no reader is not a path a reader refused: saying "refused it:" with an empty
        // reader is the kind of text that reads as a fact and is not one.
        Assert.All(inventory.Unresolved, file => Assert.DoesNotContain("refused it", file.Note, StringComparison.Ordinal));
        Assert.All(inventory.Unresolved, file => Assert.Contains("no reader for it", file.Note, StringComparison.Ordinal));
        Assert.Contains(inventory.Unresolved, file => file.Path == "MRED00I0.CFA" && file.DonorReader == "CfaFile");
        Assert.Contains(inventory.Unresolved, file => file.Path == "HAZE.000" && file.DonorReader == string.Empty);
        Assert.Contains(inventory.Unresolved, file => file.Path == "PAINT.DAT" && file.DonorReader == "PaintFile");
        // The donor reads the sky animations from Arena2 through SkyFile, which the DAT family's
        // own verdict cannot say for the thirty-two files that use it.
        Assert.All(
            inventory.Family("DAT").Where(file => file.Path.StartsWith("SKY", StringComparison.Ordinal) && file.Path.EndsWith(".DAT", StringComparison.Ordinal) && file.Path != "SKYPAL.DAT"),
            file => Assert.Equal("SkyFile", file.DonorReader));
        Assert.Equal(string.Empty, inventory.Family("DAT").Single(file => file.Path == "SKYPAL.DAT").DonorReader);
        Assert.Equal(string.Empty, inventory.Family("PAL").Single(file => file.Path == "OLDPAL.PAL").DonorReader);

        // Clause 6 asks for a named representative of every family whose reader exists.
        Assert.Contains(inventory.Family("RCI"), file => file.Path == "BUTTONS.RCI" && file.Reader == "Arena2CanvasReader");
        Assert.Contains(inventory.Family("COL"), file => file.Path == "DANKBMAP.COL" && file.Reader == "PaletteDecoder");
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

        // What a reader left unread travels with the record: TFAC00I0.RCI reads its 503 cells and
        // its seven-byte tail is the reason this classification exists, so dropping the disclosure
        // on the reading path would lose the fact exactly where it was found.
        ResidualSourceRecord bank = inventory.Family("RCI").Single(file => file.Path == "TFAC00I0.RCI");
        Assert.Equal(SourceRecordDisposition.Unused, bank.Disposition);
        Assert.Contains("7 byte(s)", bank.Note, StringComparison.Ordinal);
        Assert.Contains("belong to no canvas", bank.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void The_closure_report_decides_every_path_from_its_classification()
    {
        ResidualSourceInventory inventory = ReadInventory();
        ResidualPublicationClosure closure = ResidualPublicationClosure.From(inventory);

        // Nothing is published without a consumer and nothing is dropped silently: the nine paths the
        // manifest imports are published, the readable remainder is unpublished for want of a
        // consumer, and the unreadable remainder is unpublished for want of a reader.
        Assert.Equal(183, closure.Decisions.Count);
        Assert.Equal(
            ["ART_PAL.COL", "CHGN00I0.IMG", "DIE_00I0.IMG", "MAP.PAL", "PAL.PAL", "PICK02I0.IMG", "PICK03I0.IMG", "PRIS00I0.IMG", "TITL00I0.IMG"],
            closure.Published.Select(decision => decision.Path).Order(StringComparer.Ordinal));
        Assert.Equal(113, closure.WithoutConsumer.Count());
        Assert.Equal(61, closure.WithoutReader.Count());
        Assert.All(closure.Published, decision => Assert.Contains("a consumer claims it", decision.Reason, StringComparison.Ordinal));
        Assert.All(closure.WithoutConsumer, decision => Assert.Contains("no consumer names it", decision.Reason, StringComparison.Ordinal));
        Assert.All(closure.WithoutReader, decision => Assert.Contains("nothing to publish", decision.Reason, StringComparison.Ordinal));

        // The report carries the classification's own facts rather than re-deriving them.
        Assert.All(closure.Decisions, decision =>
        {
            ResidualSourceRecord file = inventory.Files.Single(candidate => candidate.Path == decision.Path);
            Assert.Equal(file.Family, decision.Family);
            Assert.Equal(file.Reader, decision.Reader);
            Assert.Equal(file.Disposition, decision.Classification);
        });

        // A family that reads but is claimed by nobody stays visible with its reason, which is the
        // whole point of the report: Silent omission is the failure this prevents.
        Assert.Contains(closure.WithoutConsumer, decision => decision.Path == "FRAM00I0.IMG" && decision.Family == "IMG");
    }

    [Fact]
    public void Classifies_the_documented_families_the_corpus_does_not_supply()
    {
        // Five families the task names have no residual file today. They are classified rather
        // than refused, so a future file of one of those shapes lands in its documented family
        // instead of being reported as an unknown extension.
        ResidualSourceInventory inventory = ResidualSourceInventory.Enumerate(
            [("X.GFX", new byte[16]), ("X.DEF", new byte[16]), ("X.RSC", new byte[16]), ("X.CEL", new byte[16]), ("X.BSS", new byte[16])],
            "fixture");

        Assert.All(inventory.Files, file => Assert.True(file.Documented));
        Assert.Equal("Arena2CanvasReader", inventory.Family("GFX").Single().Reader);
        Assert.Equal("MagicItemsFile", inventory.Family("DEF").Single().DonorReader);
        Assert.Equal("TextFile", inventory.Family("RSC").Single().DonorReader);
        Assert.Equal("FlcFile", inventory.Family("CEL").Single().DonorReader);
        Assert.Equal("Arena2CanvasReader", inventory.Family("CEL").Single().Reader);
        Assert.Equal("BssFile", inventory.Family("BSS").Single().DonorReader);
        Assert.Equal("Arena2CanvasReader", inventory.Family("BSS").Single().Reader);
        Assert.Empty(inventory.UndocumentedFamilies);
    }

    [Fact]
    public void Reports_a_documented_disposition_it_does_not_know_rather_than_guessing()
    {
        // The manifest's vocabulary is the product's; a token this classification cannot read
        // leaves the reader-based verdict standing and says so, rather than being ignored.
        ResidualSourceInventory inventory = ResidualSourceInventory.Enumerate(
            [("BUTN00I0.IMG", File.ReadAllBytes(Path.Combine(RepositoryRoot(), "local/arena2/BUTN00I0.IMG")))],
            "fixture",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["BUTN00I0.IMG"] = "not-a-disposition" });

        ResidualSourceRecord record = Assert.Single(inventory.Files);
        Assert.Equal(SourceRecordDisposition.Unused, record.Disposition);
        Assert.Contains("does not know ('not-a-disposition')", record.Note, StringComparison.Ordinal);

        // An imported path this repository cannot read keeps the reader's verdict *and* the import
        // disclosure: losing the consumer because the reader failed would hide a fact the manifest
        // establishes. No corpus path is in this state, so the fixture is the only place it shows.
        ResidualSourceInventory unreadable = ResidualSourceInventory.Enumerate(
            [("MYSTERY.IMG", new byte[8])],
            "fixture",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["MYSTERY.IMG"] = "imported" });

        ResidualSourceRecord refused = Assert.Single(unreadable.Files);
        Assert.Equal(SourceRecordDisposition.Malformed, refused.Disposition);
        Assert.Contains("already imports it", refused.Note, StringComparison.Ordinal);
        Assert.Contains("refused it", refused.Note, StringComparison.Ordinal);

        // The closure keeps both facts too: this path is not simply "no reader", because a consumer
        // claims it and the gap between the claim and what this repository reads is the point.
        ResidualPublicationDecision decision = Assert.Single(ResidualPublicationClosure.From(unreadable).Decisions);
        Assert.Equal(ResidualPublicationOutcome.UnreadableButClaimed, decision.Outcome);
        Assert.Contains("imports this path while no reader", decision.Reason, StringComparison.Ordinal);
        Assert.Contains("refused it", decision.Reason, StringComparison.Ordinal);
        Assert.Single(ResidualPublicationClosure.From(unreadable).ClaimedButUnreadable);
    }

    [Fact]
    public void A_claimed_path_no_reader_covers_is_reported_as_claimed_rather_than_readerless()
    {
        // The classification can reach an imported path two ways: a reader refused it, or no reader
        // covers its family at all. Both keep the consumer's claim, and the closure has to escalate
        // both - the note's wording is not the fact.
        ResidualSourceInventory readerless = ResidualSourceInventory.Enumerate(
            [("NOTELESS.TBL", new byte[16])],
            "fixture",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["NOTELESS.TBL"] = "imported" });

        ResidualSourceRecord covered = Assert.Single(readerless.Files);
        Assert.Equal(SourceRecordDisposition.Unresolved, covered.Disposition);
        Assert.True(covered.ClaimedByInventory);

        ResidualPublicationDecision decision = Assert.Single(ResidualPublicationClosure.From(readerless).Decisions);
        Assert.Equal(ResidualPublicationOutcome.UnreadableButClaimed, decision.Outcome);
        Assert.Contains("no reader here covers it", decision.Reason, StringComparison.Ordinal);

        // A path nobody claims stays in the plain readerless bucket.
        ResidualSourceInventory unclaimed = ResidualSourceInventory.Enumerate([("NOTELESS.TBL", new byte[16])], "fixture");
        Assert.False(Assert.Single(unclaimed.Files).ClaimedByInventory);
        Assert.Equal(ResidualPublicationOutcome.UnpublishedNoReader, Assert.Single(ResidualPublicationClosure.From(unclaimed).Decisions).Outcome);
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
    private static string[] DocumentedResidualPaths() => [.. DocumentedResidualRows().Select(row => Path.GetFileName(row.PathOrPattern))];

    private static IReadOnlyList<SourceInventoryRow> DocumentedResidualRows() =>
    [
        .. SourceManifestBuilder.ReadInventory(File.ReadAllBytes(Path.Combine(RepositoryRoot(), "docs/coverage/content-source-manifest.csv")))
            .Where(row => row.RowType == "file" && StringComparer.Ordinal.Equals(row.FamilyId, "CNT-027")),
    ];

    private static ResidualSourceInventory ReadInventory()
    {
        string arena2 = Path.Combine(RepositoryRoot(), "local/arena2");
        List<(string Path, ReadOnlyMemory<byte> Bytes)> sources = [];
        Dictionary<string, string> documented = [];
        foreach (SourceInventoryRow row in DocumentedResidualRows())
        {
            string name = Path.GetFileName(row.PathOrPattern);
            documented[name] = row.Disposition;
            sources.Add((name, File.ReadAllBytes(Path.Combine(arena2, name))));
        }

        return ResidualSourceInventory.Enumerate(sources, "local/arena2", documented);
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
