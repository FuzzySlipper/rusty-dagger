using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Arena2;

/// <summary>One residual source path: its family, what could read it, and its disposition.</summary>
/// <param name="Path">The supplied file name, which is the identity the manifest cites.</param>
/// <param name="Family">The bounded format family the path belongs to.</param>
/// <param name="Reader">The reader this repository could reuse, or empty when it has none.</param>
/// <param name="DonorReader">The donor's own reader, or empty when the donor reads nothing here.</param>
/// <param name="Documented">Whether the task's documented family list names this family.</param>
/// <param name="Disposition">What happened to the path, from the manifest's own vocabulary.</param>
/// <param name="Note">Why the path carries that disposition, and what would change it.</param>
public sealed record ResidualSourceRecord(
    string Path,
    string Family,
    string Reader,
    string DonorReader,
    bool Documented,
    bool ClaimedByInventory,
    SourceRecordDisposition Disposition,
    string Note);

/// <summary>
/// Classifies every residual (<c>CNT-027</c>) source path by exact path and bounded format family,
/// with the reader this repository could reuse and one disposition each.
/// </summary>
/// <remarks>
/// The family table is closed: a path whose extension is not in it is refused rather than dropped
/// into an "other" bucket, because a residual file that reaches no family is exactly the file this
/// classification exists to notice. The table's donor column is evidence rather than an assumption:
/// a family the donor does not read says so, and a family the donor reads through a class this
/// repository does not have is reported as unresolved with that class named.
/// <para>
/// Readability is probed, not assumed. A family whose documented reader is present is read here;
/// a file that reader refuses is malformed with the reader's own message, which distinguishes
/// "these bytes are broken" from "this repository cannot read this shape yet" only as far as the
/// message goes — hence the reader is named in every note.
/// </para>
/// </remarks>
public sealed class ResidualSourceInventory
{
    /// <summary>The families the task documents, in the order it documents them.</summary>
    public static readonly string[] DocumentedFamilies =
    [
        "CIF", "IMG", "GFX", "CEL", "BSS", "RCI", "COL", "LGT", "RAW", "DAT", "DEF", "TDE", "RSC", "TBL",
    ];

    /// <summary>What the family table says about one bounded family.</summary>
    /// <param name="Reader">The reader this repository reuses, or empty.</param>
    /// <param name="DonorReader">The donor's reader, or empty when the donor reads none of it.</param>
    /// <param name="Purpose">What the family holds, in one sentence.</param>
    private sealed record FamilyEntry(string Reader, string DonorReader, string Purpose);

    private static readonly Dictionary<string, FamilyEntry> Families = new(StringComparer.Ordinal)
    {
        ["IMG"] = new("Arena2CanvasReader", "ImgFile", "single-image UI artwork and full-screen backdrops, read as one record or as a length-established headerless canvas"),
        ["CIF"] = new("Arena2CanvasReader", "CifRciFile", "multi-record sprite and UI graphics read as a contiguous sequence of IMG records"),
        ["RCI"] = new("Arena2CanvasReader", "CifRciFile.ReadRci", "headerless banks of equal fixed-size cells whose shape the classic reader selects by file name"),
        ["CFA"] = new("", "CfaFile", "run-length encoded multi-frame animations: the horse and cart, and the two moons"),
        ["PAL"] = new("PaletteDecoder", "", "256-colour palettes, either 768 raw bytes or 776 bytes behind an eight-byte header, of which the donor's palette reader accepts any by length while a call site reaches only MAP.PAL"),
        ["COL"] = new("PaletteDecoder", "DFPalette", "256-colour palettes with the eight-byte header the classic art uses"),
        ["DAT"] = new("", "", "assorted tables, of which the donor reads the thirty-two SKY##.DAT sky animations through SkyFile and PAINT.DAT through PaintFile; it reaches nothing else in this family"),
        ["LGT"] = new("", "", "shading tables whose meaning is inferred from their bytes rather than established by any donor reader"),
        ["RAW"] = new("", "", "raw sprite, mask and palette bytes with no donor reader and no header to establish a shape"),
        ["000"] = new("", "", "one byte per pixel overlays with no header, no record table and no palette"),
        ["001"] = new("", "", "one byte per pixel overlays with no header, no record table and no palette"),
        ["BIN"] = new("", "", "a single file the donor never names"),
        ["CFG"] = new("", "FlatsFile", "the flat billboard catalogue, a UTF-8 table of captions, genders and face indices"),
        ["SAV"] = new("BsaArchive", "SaveGames", "a classic save archive of per-region map-discovery bits"),
        ["TBL"] = new("", "", "a single 256-byte table the donor never names"),
        ["TDE"] = new("", "", "notebook and journal text with no donor reader"),
        // The task documents these five families and the residual corpus supplies none of them,
        // which the drift lists report. They are classified rather than refused so a future
        // residual file of one of these shapes lands in its documented family.
        ["GFX"] = new("Arena2CanvasReader", "GfxFile", "classic GFX frame containers, of which the shipped corpus keeps its two under the UI media family"),
        ["CEL"] = new("", "FlcFile", "classic animation frames the donor reads with its FLC reader"),
        ["BSS"] = new("", "BssFile", "compass sprite banks, which is what the donor opens with its BSS reader"),
        ["DEF"] = new("", "MagicItemsFile", "the magic item definition table the donor reads as MAGIC.DEF"),
        ["RSC"] = new("", "TextFile", "packed text records the donor reads as TEXT.RSC"),
    };

    /// <summary>
    /// What one exact path adds to its family's verdict, where the donor's own evidence is finer
    /// than the family. Every entry here was read out of the donor rather than inferred.
    /// </summary>
    private static readonly Dictionary<string, (string? DonorReader, string? Note)> PathOverrides = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PAINT.DAT"] = ("PaintFile", "The donor reads this through PaintFile: 180 forty-byte records, one per random painting item variant."),
        ["SKYPAL.DAT"] = ("", "The donor's sky reader accepts any name starting SKY and ending .DAT by its own guard, but no call site can build this one, so nothing opens it."),
        ["MPOP.RCI"] = ("CifRciFile.ReadRci", "Seventeen-pixel cells; the donor names this file in its RCI shape table but no 1.1.1 call site opens it."),
        ["SPOP.RCI"] = ("CifRciFile.ReadRci", "Twenty-two-pixel cells; the donor opens this one for the parchment border (DaggerfallUI.cs names it as parchmentBorderRCIFile)."),
        ["NOTE.RCI"] = ("CifRciFile.ReadRci", "Forty-four by nine cells; named in the donor's RCI shape table with no 1.1.1 call site."),
        ["CHLD00I0.RCI"] = ("CifRciFile.ReadRci", "Sixty-four pixel cells; named in the donor's RCI shape table with no 1.1.1 call site."),
        ["MAPSAVE.SAV"] = ("SaveGames", "A named BSA archive of 62 records, but the donor builds this path from the classic save folder rather than from Arena2, so this copy is source-tree residue rather than a game asset."),
        ["MAP.PAL"] = ("DFPalette", "The one palette file a donor call site reaches, as the world-map palette, and the one name its x4 rescale is keyed to."),
        ["OLDMAP.PAL"] = ("", "Six-bit like MAP.PAL, but no call site reaches this file and the donor's x4 rescale is keyed to the exact name MAP.PAL, so palette depth is a property of the bytes and not of the name."),
        ["CNFG05I0.CIF"] = ("", "Sibling of the CNFG images the donor's controls window names, and a residual CIF no donor call site reaches."),
        ["PNTER.CIF"] = ("", "A residual CIF the donor never names anywhere, so nothing here establishes what it holds beyond its format."),
    };

    private ResidualSourceInventory(string source, IReadOnlyList<ResidualSourceRecord> files)
    {
        Source = source;
        Files = files;
    }

    /// <summary>Logical source identity supplied to <see cref="Enumerate"/>.</summary>
    public string Source { get; }

    /// <summary>Every supplied path, ordered by path.</summary>
    public IReadOnlyList<ResidualSourceRecord> Files { get; }

    /// <summary>The paths the documented inventory already imports, so a consumer claims them.</summary>
    public IEnumerable<ResidualSourceRecord> Imported => Files.Where(file => file.Disposition == SourceRecordDisposition.Imported);

    /// <summary>The paths a reader in this repository reads, with no consumer claiming them yet.</summary>
    public IEnumerable<ResidualSourceRecord> Unused => Files.Where(file => file.Disposition == SourceRecordDisposition.Unused);

    /// <summary>The paths whose family has a reader here that refused the bytes.</summary>
    public IEnumerable<ResidualSourceRecord> Malformed => Files.Where(file => file.Disposition == SourceRecordDisposition.Malformed);

    /// <summary>The paths whose family has no reader in this repository.</summary>
    public IEnumerable<ResidualSourceRecord> Unresolved => Files.Where(file => file.Disposition == SourceRecordDisposition.Unresolved);

    /// <summary>Families the corpus supplies that the task's documented list does not name.</summary>
    public IReadOnlyList<string> UndocumentedFamilies =>
        [.. Files.Select(file => file.Family).Distinct(StringComparer.Ordinal).Where(family => !DocumentedFamilies.Contains(family, StringComparer.Ordinal)).Order(StringComparer.Ordinal)];

    /// <summary>Families the task's documented list names that the corpus does not supply.</summary>
    public IReadOnlyList<string> MissingDocumentedFamilies =>
        [.. DocumentedFamilies.Where(family => !Files.Any(file => StringComparer.Ordinal.Equals(file.Family, family)))];

    /// <summary>The supplied paths of one family, ordered by path.</summary>
    public IEnumerable<ResidualSourceRecord> Family(string family) => Files.Where(file => StringComparer.Ordinal.Equals(file.Family, family));

    /// <summary>Classifies every supplied residual path.</summary>
    /// <param name="sources">File name and bytes for every residual path.</param>
    /// <param name="source">Logical source identity for error messages.</param>
    /// <param name="documented">
    /// The disposition the documented inventory already records for a path, when the caller has
    /// it. A path the inventory already imports keeps that disposition: calling a consumed file
    /// unused because this pass cannot see the consumer would contradict the artifact that can.
    /// </param>
    public static ResidualSourceInventory Enumerate(
        IEnumerable<(string Path, ReadOnlyMemory<byte> Bytes)> sources,
        string source,
        IReadOnlyDictionary<string, string>? documented = null)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        List<ResidualSourceRecord> files = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach ((string path, ReadOnlyMemory<byte> bytes) in sources.OrderBy(entry => entry.Path, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A residual source was supplied without a path, which is the identity the classification records.", nameof(sources));
            }

            if (!seen.Add(path))
            {
                throw new ArgumentException($"'{path}' was supplied more than once, so it would be classified twice.", nameof(sources));
            }

            string name = System.IO.Path.GetFileName(path);
            string family = System.IO.Path.GetExtension(name).TrimStart('.').ToUpperInvariant();
            if (!Families.TryGetValue(family, out FamilyEntry? entry))
            {
                throw new Arena2FormatException(source, 0, $"'{name}' has extension '{family}', which is in none of the {Families.Count} bounded residual families, so it cannot be classified or silently omitted");
            }

            (string? overrideDonor, string? overrideNote) = Override(name, entry.DonorReader);
            string donorReader = overrideDonor ?? string.Empty;
            string reader = ReaderFor(entry.Reader, name);
            ProbeOutcome probe = reader.Length == 0 ? new ProbeOutcome(false, string.Empty) : Probe(reader, bytes.Span, path);
            string? documentedToken = documented is not null && documented.TryGetValue(path, out string? token) ? token : null;
            SourceRecordDisposition disposition = Decide(reader, probe, documentedToken, out string documentedNote);
            files.Add(new ResidualSourceRecord(
                path,
                family,
                reader,
                donorReader,
                DocumentedFamilies.Contains(family, StringComparer.Ordinal),
                // The consumer's claim is a fact of the manifest rather than a reading of the note: a
                // caller asking "does something want this?" must not have to parse English, and a
                // reworded note must not be able to demote a claimed path silently.
                string.Equals(documentedToken, nameof(SourceRecordDisposition.Imported), StringComparison.OrdinalIgnoreCase),
                disposition,
                Note(entry, reader, donorReader, probe, disposition, documentedNote, overrideNote)));
        }

        return new ResidualSourceInventory(source, files);
    }

    /// <summary>
    /// What one path's donor evidence is: an exact override, the donor's own sky name guard, or
    /// the family's own verdict.
    /// </summary>
    private static (string? DonorReader, string? Note) Override(string name, string familyDonor) =>
        PathOverrides.TryGetValue(name, out (string? DonorReader, string? Note) exact) ? exact
        // The donor builds its sky names as SKY{index:00}.DAT and its reader accepts any name that
        // starts SKY and ends .DAT, so the pattern is the donor's rather than an approximation.
        : name.StartsWith("SKY", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".DAT", StringComparison.OrdinalIgnoreCase)
            ? ("SkyFile", "One of the sky animations the donor reads from Arena2 through SkyFile, as thirty-two 776-byte palettes at the start and sixty-four frames of 512x220 from offset 549120.")
            : (familyDonor, null);

    /// <summary>
    /// The disposition one path carries, and what the documented inventory said about it.
    /// </summary>
    private static SourceRecordDisposition Decide(string reader, ProbeOutcome probe, string? documentedToken, out string documentedNote)
    {
        documentedNote = string.Empty;
        SourceRecordDisposition? documented = null;
        if (documentedToken is not null)
        {
            if (Enum.TryParse(documentedToken, ignoreCase: true, out SourceRecordDisposition parsed) && parsed != SourceRecordDisposition.None)
            {
                documented = parsed;
            }
            else
            {
                documentedNote = $" The documented inventory carries a disposition this classification does not know ('{documentedToken}'), so the reader decides here.";
            }
        }

        // An imported path has a consumer whether or not this pass can name it, so a readable one
        // keeps that disposition rather than being called unused. A path this repository cannot
        // read keeps the reader's verdict, and the note carries the import either way: "no reader"
        // and "a consumer claims it" are both true, and the second one must not be lost just
        // because the first is the disposition.
        bool claimed = documented == SourceRecordDisposition.Imported;
        if (claimed)
        {
            documentedNote = " The documented inventory already imports it, so a consumer claims it.";
        }

        if (reader.Length == 0) return SourceRecordDisposition.Unresolved;
        if (!probe.Read) return SourceRecordDisposition.Malformed;
        if (documented == SourceRecordDisposition.Imported) return SourceRecordDisposition.Imported;
        if (documented is { } value)
        {
            documentedNote = $" The documented inventory dispositions it '{documentedToken}'.";
            return value;
        }

        return SourceRecordDisposition.Unused;
    }

    /// <summary>
    /// The reader this repository would reuse for one path: the family's reader, except that a
    /// weapon CIF belongs to the weapon CIF reader rather than to the general canvas probe, which
    /// says so instead of reading it. The test mirrors the donor's own substring test
    /// (<c>CifRciFile.ReadRecords</c> dispatches on <c>fn.Contains("WEAPO")</c>); comparing
    /// case-insensitively is this repository's superset, which cannot misfire on the corpus's
    /// uppercase names.
    /// </summary>
    private static string ReaderFor(string familyReader, string name) =>
        familyReader == "Arena2CanvasReader" && name.Contains("WEAPO", StringComparison.OrdinalIgnoreCase)
            ? "WeaponCifArchive"
            : familyReader;

    /// <summary>What a probe established: whether a reader read the file, and what it left unsaid.</summary>
    /// <param name="Read">Whether the family's reader read the bytes.</param>
    /// <param name="Message">The refusal when it did not, or what it left unread when it did.</param>
    private readonly record struct ProbeOutcome(bool Read, string Message)
    {
        internal static ProbeOutcome Read_(string disclosure) => new(true, disclosure);

        internal static ProbeOutcome Refused(string failure) => new(false, failure);
    }

    private static string Note(
        FamilyEntry entry,
        string reader,
        string donorReader,
        ProbeOutcome probe,
        SourceRecordDisposition disposition,
        string documentedNote,
        string? pathNote)
    {
        string donor = donorReader.Length == 0
            ? "No donor reader reaches this file, so what it holds is a source fact without an established meaning."
            : $"The donor reads it through {donorReader}.";
        string verdict = reader.Length == 0
            ? "This repository has no reader for it, so nothing here determines its contents."
            : disposition == SourceRecordDisposition.Malformed
                ? $"{reader} refused it: {probe.Message}"
                : disposition == SourceRecordDisposition.Imported
                    ? $"Read by {reader}, and the documented inventory already imports it, so a consumer claims it."
                    // What a reader left unread still travels with the record: a file that reads is
                    // not a file whose remainder may be dropped.
                    : $"Read by {reader}; no consumer named here claims it.{(probe.Message.Length == 0 ? string.Empty : $" {probe.Message}")}";
        string purpose = $"Holds {entry.Purpose}.";
        return pathNote is null ? $"{purpose} {donor} {verdict}{documentedNote}" : $"{purpose} {pathNote} {donor} {verdict}{documentedNote}";
    }

    /// <summary>Reads a path with the reader its family names, returning what it read or refused.</summary>
    private static ProbeOutcome Probe(string reader, ReadOnlySpan<byte> bytes, string path)
    {
        try
        {
            switch (reader)
            {
                case "Arena2CanvasReader":
                    Arena2CanvasSet canvases = Arena2CanvasReader.Read(bytes, path);
                    return canvases.Read ? ProbeOutcome.Read_(canvases.Reason) : ProbeOutcome.Refused(canvases.Reason);
                case "WeaponCifArchive":
                    WeaponCifArchive.Parse(bytes, path);
                    return ProbeOutcome.Read_(string.Empty);
                case "PaletteDecoder":
                    PaletteDecoder.Decode(bytes, path);
                    return ProbeOutcome.Read_(string.Empty);
                case "BsaArchive":
                    BsaArchive.Parse(bytes, path);
                    return ProbeOutcome.Read_(string.Empty);
                default:
                    throw new InvalidOperationException($"Family reader '{reader}' has no probe, so its families would be classified without reading them.");
            }
        }
        catch (Arena2FormatException failure)
        {
            return ProbeOutcome.Refused(failure.Message);
        }
    }
}
