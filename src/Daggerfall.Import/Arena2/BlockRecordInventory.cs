namespace Daggerfall.Import.Arena2;

/// <summary>What an archive record is, by the extension of its name, as the donor classifies it.</summary>
public enum BlockRecordKind
{
    /// <summary>The name carries no extension the donor recognises.</summary>
    Unknown,

    /// <summary>A city or exterior block.</summary>
    Rmb,

    /// <summary>A dungeon block.</summary>
    Rdb,

    /// <summary>A dungeon block index, which the donor reads as unknown bytes and ignores.</summary>
    Rdi,
}

/// <summary>Whether a record's own bytes could be read.</summary>
public enum BlockRecordState
{
    /// <summary>The record was read, and what is published about it describes its bytes.</summary>
    Read,

    /// <summary>The record could not be read, and the reason says why.</summary>
    Malformed,
}

/// <summary>What the donor does with a record of its kind, which is not the same as whether it is readable.</summary>
public enum BlockRecordDisposition
{
    /// <summary>The record's own header was read and summarized.</summary>
    Summarized,

    /// <summary>The donor reads the record as unknown bytes and ignores it.</summary>
    DonorUnsupported,

    /// <summary>The donor has no block type for the record's name.</summary>
    UnknownKind,
}

/// <summary>A dungeon block's kind, from the first letter of its name as the donor derives it.</summary>
public enum BlockRdbType
{
    /// <summary>No letter the donor maps to a dungeon kind.</summary>
    Unknown,

    /// <summary>Border block used to seal a dungeon.</summary>
    Border,

    /// <summary>Normal block.</summary>
    Normal,

    /// <summary>Flooded block.</summary>
    Wet,

    /// <summary>Used in the main quest.</summary>
    Quest,

    /// <summary>Crypt block.</summary>
    Mausoleum,
}

/// <summary>A texture archive and record an RMB or RDB block's flat objects name.</summary>
/// <param name="Archive">The texture archive the flat selects from.</param>
/// <param name="Record">The record within that archive.</param>
public sealed record BlockTextureReference(ushort Archive, ushort Record);

/// <summary>
/// What an RMB block's name says about how the donor composes such a name, when the name can be taken
/// apart at all.
/// </summary>
/// <param name="Prefix">The four-character prefix the donor's own table carries.</param>
/// <param name="TableIndices">
/// Every index that prefix occupies in the donor's table. Two of the table's entries are the same
/// <c>TEMP</c> prefix — the two temple kinds — so a name beginning with it has more than one candidate
/// index and cannot be resolved by its name alone.
/// </param>
/// <param name="Letter1">The letter the donor places first after the prefix.</param>
/// <param name="Letter2">The letter the donor places second after the prefix.</param>
/// <param name="NumberText">The bytes the donor fills with the block's number, verbatim.</param>
/// <param name="Number">That text as a number, or null when it is not one, as a temple name is not.</param>
/// <param name="DonorShape">Whether the name has one of the two shapes the donor's own composer produces.</param>
public sealed record BlockRmbName(
    string Prefix,
    IReadOnlyList<int> TableIndices,
    char Letter1,
    char Letter2,
    string NumberText,
    int? Number,
    bool DonorShape);

/// <summary>A dungeon block's name taken apart.</summary>
/// <param name="Letter">The first letter of the name, which selects the type.</param>
/// <param name="NumberText">The digits after it, verbatim, which the source pads to a fixed width.</param>
/// <param name="Number">That text as a number, or null when the name carries none.</param>
/// <param name="Type">What the donor's letter table calls that letter.</param>
public sealed record BlockRdbName(char Letter, string NumberText, int? Number, BlockRdbType Type);

/// <summary>
/// What a readable block's own header says it places: how many of each object it declares, and which
/// models and textures those objects name.
/// </summary>
/// <param name="Models">How many 3D object records the block places.</param>
/// <param name="Flats">How many flat object records the block places.</param>
/// <param name="Lights">How many light records the block places.</param>
/// <param name="Doors">How many of its models carry an action-door tag.</param>
/// <param name="StartMarkers">How many of its flats are the classic start marker.</param>
/// <param name="EnterMarkers">How many of its flats are the classic enter marker.</param>
/// <param name="TreasureMarkers">How many of its flats are the classic random-treasure marker.</param>
/// <param name="FixedMobiles">How many of its flats are the classic fixed-mobile marker.</param>
/// <param name="ModelIds">The distinct models the block's objects name, in first-use order.</param>
/// <param name="Textures">The distinct archive records its flats select, in first-use order.</param>
public sealed record BlockObjectSummary(
    int Models,
    int Flats,
    int Lights,
    int Doors,
    int StartMarkers,
    int EnterMarkers,
    int TreasureMarkers,
    int FixedMobiles,
    IReadOnlyList<string> ModelIds,
    IReadOnlyList<BlockTextureReference> Textures);

/// <summary>One record of the block archive, classified and summarized without decoding a placement.</summary>
/// <param name="Ordinal">The record's position in the archive's directory, which is its stable identity.</param>
/// <param name="SourceKey">The exact name the archive stores it under.</param>
/// <param name="Kind">What the name says it is.</param>
/// <param name="Disposition">What the donor does with a record of that kind.</param>
/// <param name="Offset">The byte its bytes begin at.</param>
/// <param name="ByteLength">The bytes it spans.</param>
/// <param name="State">Whether its own header could be read.</param>
/// <param name="Reason">Why it could not, empty when it could.</param>
/// <param name="RmbName">Its name taken apart, when the name has a shape the donor composes.</param>
/// <param name="RdbName">Its name taken apart as a dungeon block, when it is one.</param>
/// <param name="RmbHeader">What its header declares, when it is a city block that could be read.</param>
/// <param name="Objects">What it places, when it is a dungeon block that could be read.</param>
public sealed record BlockRecord(
    int Ordinal,
    string SourceKey,
    BlockRecordKind Kind,
    BlockRecordDisposition Disposition,
    long Offset,
    int ByteLength,
    BlockRecordState State,
    string Reason,
    BlockRmbName? RmbName,
    BlockRdbName? RdbName,
    RmbBlockSummary? RmbHeader,
    BlockObjectSummary? Objects);

/// <summary>Every record a block archive declares, in the order its directory lists them.</summary>
/// <param name="Source">Logical source identity supplied to the reader.</param>
/// <param name="DeclaredRecords">The count the archive's own header declares.</param>
/// <param name="Records">The records, in directory order.</param>
public sealed record BlockRecordInventory(string Source, int DeclaredRecords, IReadOnlyList<BlockRecord> Records);

/// <summary>
/// Enumerates a named block archive: every record it carries, what its name says it is, and what its
/// own header says it places.
/// </summary>
/// <remarks>
/// <para>
/// Classification follows the donor rather than a table invented here. The kind comes from the name's
/// extension (<c>BlocksFile.GetBlockType</c>), a dungeon block's type from the first letter of its name
/// (<c>GetRdbType</c>, which has no case for <c>L</c> even though the coverage inventory lists one), and
/// a city block's prefix from the donor's own <c>rmbBlockPrefixes</c> table, in which the two temple
/// kinds share the prefix <c>TEMP</c>. Every index a prefix occupies is retained, because a name alone
/// cannot say which of the two a block came from: that is the identity the donor's own composer loses,
/// and an inventory that collapsed it would be reporting one kind where the source has two.
/// </para>
/// <para>
/// A record is summarized from its own header, and a header that cannot be read is published as
/// malformed with its reason rather than dropped: the archive key exists either way, and a lookup that
/// answered "no such block" would report a source fact as an absence. Nothing here decodes a placement,
/// a ground tile or an automap; those belong to the tasks that publish dungeon and exterior assemblies.
/// </para>
/// </remarks>
public static class BlockRecordInventoryReader
{
    /// <summary>Where the documented inventory places the block archive.</summary>
    public const string FileName = "BLOCKS.BSA";

    /// <summary>
    /// The donor's own city-block prefixes, transcribed with both of its temple entries in place so a
    /// name can be matched to every index that claims it.
    /// </summary>
    public static readonly IReadOnlyList<string> RmbBlockPrefixes =
    [
        "TVRN", "GENR", "RESI", "WEAP", "ARMR", "ALCH", "BANK", "BOOK",
        "CLOT", "FURN", "GEMS", "LIBR", "PAWN", "TEMP", "TEMP", "PALA",
        "FARM", "DUNG", "CAST", "MANR", "SHRI", "RUIN", "SHCK", "GRVE",
        "FILL", "KRAV", "KDRA", "KOWL", "KMOO", "KCAN", "KFLA", "KHOR",
        "KROS", "KWHE", "KSCA", "KHAW", "MAGE", "THIE", "DARK", "FIGH",
        "CUST", "WALL", "MARK", "SHIP", "WITC",
    ];

    /// <summary>The letters the donor places second in an RMB name.</summary>
    public static readonly IReadOnlyList<char> RmbLetters2 = ['A', 'L', 'M', 'S'];

    /// <summary>Reads every record the supplied archive declares.</summary>
    public static BlockRecordInventory Read(byte[] bytes, string label)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        BsaArchive archive = BsaArchive.Parse(bytes, label);
        List<BlockRecord> records = new(archive.Records.Count);
        foreach (BsaRecord record in archive.Records)
        {
            records.Add(ReadRecord(bytes, archive, record));
        }

        return new BlockRecordInventory(label, archive.Records.Count, records);
    }

    private static BlockRecord ReadRecord(byte[] bytes, BsaArchive archive, BsaRecord record)
    {
        // A named archive is what carries block names at all; the numeric variant names nothing, so a
        // record from it has no kind to classify and is published as such rather than guessed at.
        string key = record.Name ?? throw new Arena2FormatException(archive.Source, record.Offset, $"block archive record {record.Ordinal} carries no name, so nothing says what kind of block it is");
        BlockRecordKind kind = Kind(key);
        long offset = record.Offset;
        int byteLength = record.Length;
        return kind switch
        {
            BlockRecordKind.Rmb => ReadRmb(bytes, archive.Source, record.Ordinal, key, offset, byteLength),
            BlockRecordKind.Rdb => ReadRdb(bytes, archive.Source, record.Ordinal, key, offset, byteLength),
            BlockRecordKind.Rdi => new BlockRecord(
                record.Ordinal, key, kind, BlockRecordDisposition.DonorUnsupported, offset, byteLength,
                BlockRecordState.Read, string.Empty, null, null, null, null),
            _ => new BlockRecord(
                record.Ordinal, key, kind, BlockRecordDisposition.UnknownKind, offset, byteLength,
                BlockRecordState.Read, string.Empty, null, null, null, null),
        };
    }

    /// <summary>Determines a record's kind from the extension of its own name, as the donor does.</summary>
    public static BlockRecordKind Kind(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name.EndsWith(".RMB", StringComparison.Ordinal))
        {
            return BlockRecordKind.Rmb;
        }

        return name.EndsWith(".RDB", StringComparison.Ordinal) ? BlockRecordKind.Rdb
            : name.EndsWith(".RDI", StringComparison.Ordinal) ? BlockRecordKind.Rdi
            : BlockRecordKind.Unknown;
    }

    /// <summary>Determines a dungeon block's type from the first letter of its name, as the donor does.</summary>
    public static BlockRdbType RdbType(char letter) => letter switch
    {
        'B' => BlockRdbType.Border,
        'W' => BlockRdbType.Wet,
        'S' => BlockRdbType.Quest,
        'M' => BlockRdbType.Mausoleum,
        'N' => BlockRdbType.Normal,
        _ => BlockRdbType.Unknown,
    };

    /// <summary>
    /// Takes a city block's name apart the way the donor's own composer builds one, retaining every
    /// index the prefix occupies, or reports that the name has no such shape.
    /// </summary>
    public static BlockRmbName? RmbName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        string stem = name.EndsWith(".RMB", StringComparison.Ordinal) ? name[..^4] : name;
        if (stem.Length < 4)
        {
            return null;
        }

        string prefix = stem[..4];
        int[] indices = [.. RmbBlockPrefixes.Select((candidate, index) => (candidate, index)).Where(pair => StringComparer.Ordinal.Equals(pair.candidate, prefix)).Select(pair => pair.index)];
        if (indices.Length == 0)
        {
            return null;
        }

        // The donor composes prefix + letter1 + letter2 + numbers, so a name shorter than that cannot be
        // one it produced, and a name it could not have produced is published with its own identity only.
        string remainder = stem[4..];
        if (remainder.Length < 3)
        {
            return null;
        }

        string numberText = remainder[2..];
        char letter2 = remainder[1];
        bool digits = numberText.Length != 0 && numberText.All(char.IsAsciiDigit);
        bool temple = numberText.Length >= 2 && char.IsAsciiLetterUpper(numberText[0]) && numberText[1..].All(char.IsAsciiDigit);
        return new BlockRmbName(
            prefix,
            indices,
            remainder[0],
            letter2,
            numberText,
            digits ? int.Parse(numberText, System.Globalization.CultureInfo.InvariantCulture) : null,
            RmbLetters2.Contains(letter2) && (digits || (temple && numberText.Length > 1)));
    }

    /// <summary>Takes a dungeon block's name apart into the letter that selects its type and its number.</summary>
    public static BlockRdbName RdbName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        string stem = name.EndsWith(".RDB", StringComparison.Ordinal) ? name[..^4] : name;
        char letter = stem.Length != 0 ? stem[0] : '\0';
        string digits = stem.Length > 1 ? stem[1..] : string.Empty;
        return new BlockRdbName(
            letter,
            digits,
            digits.Length != 0 && digits.All(char.IsAsciiDigit) ? int.Parse(digits, System.Globalization.CultureInfo.InvariantCulture) : null,
            RdbType(letter));
    }

    private static BlockRecord ReadRmb(byte[] bytes, string source, int ordinal, string key, long offset, int byteLength)
    {
        BlockRmbName? name = RmbName(key);
        if (!RmbBlockSummaryReader.TryRead(bytes, source, (int)offset, byteLength, out RmbBlockSummary? summary, out string reason))
        {
            return new BlockRecord(ordinal, key, BlockRecordKind.Rmb, BlockRecordDisposition.Summarized, offset, byteLength, BlockRecordState.Malformed, reason, name, null, null, null);
        }

        return new BlockRecord(ordinal, key, BlockRecordKind.Rmb, BlockRecordDisposition.Summarized, offset, byteLength, BlockRecordState.Read, string.Empty, name, null, summary, null);
    }

    private static BlockRecord ReadRdb(byte[] bytes, string source, int ordinal, string key, long offset, int byteLength)
    {
        BlockRdbName name = RdbName(key);
        try
        {
            RdbBlockSource block = RdbDecoder.Decode(bytes.AsSpan((int)offset, byteLength), source);
            HashSet<string> models = new(StringComparer.Ordinal);
            List<string> modelIds = [];
            int doors = 0;
            foreach (RdbModelSource model in block.Models)
            {
                if (models.Add(model.ModelId))
                {
                    modelIds.Add(model.ModelId);
                }

                if (RdbSourceClassification.HasActionDoorTag(model))
                {
                    doors++;
                }
            }

            HashSet<(ushort Archive, ushort Record)> textures = [];
            List<BlockTextureReference> references = [];
            int startMarkers = 0;
            int enterMarkers = 0;
            int treasureMarkers = 0;
            int fixedMobiles = 0;
            foreach (RdbFlatSource flat in block.Flats)
            {
                if (textures.Add((flat.TextureArchive, flat.TextureRecord)))
                {
                    references.Add(new BlockTextureReference(flat.TextureArchive, flat.TextureRecord));
                }

                if (RdbSourceClassification.IsStartMarker(flat)) startMarkers++;
                if (RdbSourceClassification.IsEnterMarker(flat)) enterMarkers++;
                if (RdbSourceClassification.IsRandomTreasureMarker(flat)) treasureMarkers++;
                if (RdbSourceClassification.IsFixedMobileMarker(flat)) fixedMobiles++;
            }

            BlockObjectSummary objects = new(
                block.Models.Count, block.Flats.Count, block.Lights.Count, doors,
                startMarkers, enterMarkers, treasureMarkers, fixedMobiles, modelIds, references);
            return new BlockRecord(ordinal, key, BlockRecordKind.Rdb, BlockRecordDisposition.Summarized, offset, byteLength, BlockRecordState.Read, string.Empty, null, name, null, objects);
        }
        catch (Arena2FormatException error)
        {
            return new BlockRecord(ordinal, key, BlockRecordKind.Rdb, BlockRecordDisposition.Summarized, offset, byteLength, BlockRecordState.Malformed, error.Message, null, name, null, null);
        }
    }
}
