using System.Globalization;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Normalized;

/// <summary>What an archive record is, by the extension of its name.</summary>
public enum DaggerfallBlockKind
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

/// <summary>What the donor does with a record of its kind.</summary>
public enum DaggerfallBlockDisposition
{
    /// <summary>The record's own header was read and summarized.</summary>
    Summarized,

    /// <summary>The donor reads the record as unknown bytes and ignores it.</summary>
    DonorUnsupported,

    /// <summary>The donor has no block type for the record's name.</summary>
    UnknownKind,
}

/// <summary>A dungeon block's kind, from the first letter of its name.</summary>
public enum DaggerfallBlockRdbType
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

/// <summary>Whether a record's own bytes could be read.</summary>
public enum DaggerfallBlockState
{
    /// <summary>The record was read, and what is published about it describes its bytes.</summary>
    Read,

    /// <summary>The record could not be read, and the reason says why.</summary>
    Malformed,
}

/// <summary>A city block's name taken apart the way the donor composes one.</summary>
/// <param name="Prefix">The four-character prefix the donor's table carries.</param>
/// <param name="TableIndices">Every index that prefix occupies in the donor's table, which for two of them is two indices.</param>
/// <param name="Letter1">The letter the donor places first after the prefix.</param>
/// <param name="Letter2">The letter the donor places second after the prefix.</param>
/// <param name="NumberText">The bytes the donor fills with the block's number, verbatim.</param>
/// <param name="Number">That text as a number, or null when it is not one.</param>
/// <param name="DonorShape">Whether the name has one of the two shapes the donor's composer produces.</param>
public sealed record DaggerfallBlockRmbName(
    string Prefix,
    IReadOnlyList<int> TableIndices,
    string Letter1,
    string Letter2,
    string NumberText,
    int? Number,
    bool DonorShape);

/// <summary>A dungeon block's name taken apart.</summary>
/// <param name="Letter">The first letter of the name, which selects the type.</param>
/// <param name="NumberText">The digits after it, verbatim, which the source pads to a fixed width.</param>
/// <param name="Number">That text as a number, or null when the name carries none.</param>
/// <param name="Type">What the donor's letter table calls that letter.</param>
public sealed record DaggerfallBlockRdbName(string Letter, string NumberText, int? Number, DaggerfallBlockRdbType Type);

/// <summary>A texture archive and record a block's flat objects select.</summary>
/// <param name="Archive">The texture archive.</param>
/// <param name="Record">The record within it.</param>
public sealed record DaggerfallBlockTexture(ushort Archive, ushort Record);

/// <summary>What a readable block's own header says it places.</summary>
/// <param name="Models">How many 3D object records it places.</param>
/// <param name="Flats">How many flat object records it places.</param>
/// <param name="Lights">How many light records it places.</param>
/// <param name="Doors">How many of its models carry an action-door tag.</param>
/// <param name="StartMarkers">How many of its flats are the classic start marker.</param>
/// <param name="EnterMarkers">How many of its flats are the classic enter marker.</param>
/// <param name="TreasureMarkers">How many of its flats are the classic random-treasure marker.</param>
/// <param name="FixedMobiles">How many of its flats are the classic fixed-mobile marker.</param>
/// <param name="ModelIds">The distinct models its objects name, in first-use order.</param>
/// <param name="Textures">The distinct archive records its flats select, in first-use order.</param>
public sealed record DaggerfallBlockObjects(
    int Models,
    int Flats,
    int Lights,
    int Doors,
    int StartMarkers,
    int EnterMarkers,
    int TreasureMarkers,
    int FixedMobiles,
    IReadOnlyList<string> ModelIds,
    IReadOnlyList<DaggerfallBlockTexture> Textures);

/// <summary>One building slot of a city block header.</summary>
/// <param name="Index">The slot's ordinal, which is the sub-record's ordinal in the block.</param>
/// <param name="ByteLength">The bytes the donor steps over for this sub-record, padding included.</param>
/// <param name="BuildingType">The building type byte the slot carries.</param>
/// <param name="FactionId">The faction the slot names, or zero when it names none.</param>
/// <param name="Quality">The quality byte the slot carries.</param>
/// <param name="NameSeed">The seed the building's generated name derives from.</param>
/// <param name="Objects">How many 3D object records the sub-record declares.</param>
/// <param name="Flats">How many flat object records the sub-record declares.</param>
/// <param name="Sections">How many section-3 records the sub-record declares.</param>
/// <param name="People">How many people records the sub-record declares.</param>
/// <param name="Doors">How many door records the sub-record declares.</param>
public sealed record DaggerfallBlockBuilding(
    int Index,
    int ByteLength,
    int BuildingType,
    int FactionId,
    int Quality,
    int NameSeed,
    int Objects,
    int Flats,
    int Sections,
    int People,
    int Doors);

/// <summary>What a city block's own header declares, without a single placement decoded.</summary>
/// <param name="Name">The name the block states for itself.</param>
/// <param name="OtherNames">How many of the header's other-name slots carry a name.</param>
/// <param name="DeclaredBlocks">How many building sub-records the header declares.</param>
/// <param name="Misc3dObjects">How many 3D objects the block places outside any sub-record.</param>
/// <param name="MiscFlatObjects">How many flat objects the block places outside any sub-record.</param>
/// <param name="Buildings">One entry per declared sub-record, in the header's own order.</param>
/// <param name="TrailingBytes">Bytes the record carries past everything the header accounts for.</param>
public sealed record DaggerfallBlockRmbHeader(
    string Name,
    int OtherNames,
    int DeclaredBlocks,
    int Misc3dObjects,
    int MiscFlatObjects,
    IReadOnlyList<DaggerfallBlockBuilding> Buildings,
    int TrailingBytes);

/// <summary>One record of the block archive, classified and summarized.</summary>
/// <param name="Ordinal">The record's position in the archive directory, which is its stable identity.</param>
/// <param name="SourceKey">The exact name the archive stores it under.</param>
/// <param name="Source">The logical path of the source that carries it.</param>
/// <param name="Kind">What the name says it is.</param>
/// <param name="Disposition">What the donor does with a record of that kind.</param>
/// <param name="Offset">The byte its bytes begin at.</param>
/// <param name="ByteLength">The bytes it spans.</param>
/// <param name="State">Whether its own header could be read.</param>
/// <param name="Reason">Why it could not, empty when it could.</param>
/// <param name="RmbName">Its name taken apart, when the name has a shape the donor composes.</param>
/// <param name="RdbName">Its name taken apart as a dungeon block, when it is one.</param>
/// <param name="Rmb">What its header declares, when it is a city block that could be read.</param>
/// <param name="Objects">What it places, when it is a dungeon block that could be read.</param>
public sealed record DaggerfallBlockRecord(
    int Ordinal,
    string SourceKey,
    string Source,
    DaggerfallBlockKind Kind,
    DaggerfallBlockDisposition Disposition,
    long Offset,
    int ByteLength,
    DaggerfallBlockState State,
    string Reason,
    DaggerfallBlockRmbName? RmbName,
    DaggerfallBlockRdbName? RdbName,
    DaggerfallBlockRmbHeader? Rmb,
    DaggerfallBlockObjects? Objects);

/// <summary>One archive the published blocks were read from.</summary>
/// <param name="RecordId">The inventory's identity for the family.</param>
/// <param name="Path">The logical path the inventory documents it at.</param>
/// <param name="ByteLength">The bytes the archive occupies.</param>
/// <param name="DeclaredLength">The record count the archive's own header declares.</param>
/// <param name="Records">How many records the publication carries for it.</param>
public sealed record DaggerfallBlockSource(string RecordId, string Path, long ByteLength, int DeclaredLength, int Records);

/// <summary>
/// The published block inventory: every record the supplied archive declares, what its name says it is,
/// and what its own header says it places.
/// </summary>
/// <param name="SchemaVersion">Shape version of this section.</param>
/// <param name="Sources">Every archive the records were read from.</param>
/// <param name="Records">Every record the archives carry, in directory order.</param>
public sealed record DaggerfallBlocks(
    int SchemaVersion,
    IReadOnlyList<DaggerfallBlockSource> Sources,
    IReadOnlyList<DaggerfallBlockRecord> Records)
{
    /// <summary>Shape version this publication writes.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>The bytes of a city block header, up to and including the block's own name.</summary>
    public const int RmbHeaderBytes = 6776;

    /// <summary>Bytes one 3D object record occupies.</summary>
    public const int RmbModelRecordBytes = 66;

    /// <summary>Bytes one flat object record occupies.</summary>
    public const int RmbFlatRecordBytes = 17;

    /// <summary>The bytes of a city block sub-record's own header.</summary>
    public const int RmbBuildingHeaderBytes = 5;

    /// <summary>Checks that every claim this publication makes about the corpus holds together.</summary>
    public void Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion)
        {
            throw new InvalidOperationException($"Block schema must be {CurrentSchemaVersion} but is {SchemaVersion}.");
        }

        ArgumentNullException.ThrowIfNull(Sources);
        ArgumentNullException.ThrowIfNull(Records);
        if (Sources.Count == 0)
        {
            throw new InvalidOperationException("A published block section must name the archives it read.");
        }

        NormalizedImportDocument.ValidateUnique(Sources, source => source.Path, "block source");
        Dictionary<string, DaggerfallBlockSource> byPath = Sources.ToDictionary(source => source.Path, StringComparer.Ordinal);
        Dictionary<string, int> perSource = new(StringComparer.Ordinal);
        HashSet<string> keys = new(StringComparer.Ordinal);
        string previousSource = string.Empty;
        int previousOrdinal = -1;
        foreach (DaggerfallBlockRecord record in Records)
        {
            // Each source's records are published as a group in its own order, so a consumer reads one
            // archive's ordinals without another's interleaving them.
            if (!StringComparer.Ordinal.Equals(record.Source, previousSource))
            {
                if (perSource.ContainsKey(record.Source))
                {
                    throw new InvalidOperationException($"Published block source '{record.Source}' is interleaved with another source rather than grouped.");
                }

                previousSource = record.Source;
                previousOrdinal = -1;
            }

            // A record's identity is its own archive's directory ordinal, so an ordinal that repeats or
            // skips within its source would leave two records claiming one identity or one identity
            // addressing nothing.
            if (record.Ordinal != previousOrdinal + 1)
            {
                throw new InvalidOperationException($"Published block '{record.SourceKey}' carries ordinal {record.Ordinal} where the section is ordered by ordinal and the previous record of '{record.Source}' was {previousOrdinal}.");
            }

            previousOrdinal = record.Ordinal;
            record.Validate();
            if (!byPath.TryGetValue(record.Source, out DaggerfallBlockSource? source))
            {
                throw new InvalidOperationException($"Published block '{record.SourceKey}' names source '{record.Source}', which the section does not carry.");
            }

            if (!keys.Add(record.SourceKey))
            {
                throw new InvalidOperationException($"Published blocks carry '{record.SourceKey}' twice, so one of them is unreachable.");
            }

            perSource[record.Source] = perSource.GetValueOrDefault(record.Source) + 1;
        }

        foreach (DaggerfallBlockSource source in Sources)
        {
            int published = perSource.GetValueOrDefault(source.Path);
            if (source.DeclaredLength != published || source.Records != published)
            {
                throw new InvalidOperationException($"Block source '{source.Path}' declares {source.DeclaredLength} records and publishes {source.Records}, where the section carries {published}.");
            }
        }
    }
}

/// <summary>One record of the block inventory checked against what its own name and bytes claim.</summary>
internal static class DaggerfallBlockValidation
{
    /// <summary>Checks one record's own claims.</summary>
    internal static void Validate(this DaggerfallBlockRecord record)
    {
        NormalizedImportDocument.RequireLogicalId(record.SourceKey, nameof(record.SourceKey));
        NormalizedImportDocument.RequireLogicalPath(record.Source, nameof(record.Source));
        if (record.Ordinal < 0 || record.Offset < 0 || record.ByteLength < 1)
        {
            throw new InvalidOperationException($"Published block '{record.SourceKey}' carries ordinal {record.Ordinal}, offset {record.Offset} and length {record.ByteLength}, which cannot all describe a record.");
        }

        // The kind is a function of the name's extension and the disposition a function of the kind, so
        // both are recomputed: a record whose name and kind disagreed would be filed under a kind no
        // consumer could reproduce from the archive key it publishes.
        if ((BlockRecordKind)record.Kind != BlockRecordInventoryReader.Kind(record.SourceKey))
        {
            throw new InvalidOperationException($"Published block '{record.SourceKey}' states the kind {record.Kind}, which its own name does not carry.");
        }

        DaggerfallBlockDisposition expected = record.Kind switch
        {
            DaggerfallBlockKind.Rmb or DaggerfallBlockKind.Rdb => DaggerfallBlockDisposition.Summarized,
            DaggerfallBlockKind.Rdi => DaggerfallBlockDisposition.DonorUnsupported,
            _ => DaggerfallBlockDisposition.UnknownKind,
        };
        if (record.Disposition != expected)
        {
            throw new InvalidOperationException($"Published block '{record.SourceKey}' states the disposition {record.Disposition} where its kind {record.Kind} is {expected}.");
        }

        if (record.State == DaggerfallBlockState.Read)
        {
            if (record.Reason.Length != 0)
            {
                throw new InvalidOperationException($"Published block '{record.SourceKey}' is readable and still states a reason it is not.");
            }
        }
        else if (record.State == DaggerfallBlockState.Malformed)
        {
            if (string.IsNullOrWhiteSpace(record.Reason))
            {
                throw new InvalidOperationException($"Published block '{record.SourceKey}' is malformed and states no reason, so nothing says why nothing could be read.");
            }
        }
        else
        {
            throw new ArgumentOutOfRangeException(nameof(record), record.State, $"Published block '{record.SourceKey}' carries a state the contract does not declare.");
        }

        // What a record's header declares is published only when it could be read, and only for the kind
        // whose header it is.
        if (record.State == DaggerfallBlockState.Read)
        {
            switch (record.Kind)
            {
                case DaggerfallBlockKind.Rmb when record.Rmb is null:
                    throw new InvalidOperationException($"Published block '{record.SourceKey}' is a readable city block with no header summary.");
                case DaggerfallBlockKind.Rdb when record.Objects is null:
                    throw new InvalidOperationException($"Published block '{record.SourceKey}' is a readable dungeon block with no object summary.");
                case DaggerfallBlockKind.Rmb or DaggerfallBlockKind.Rdb when record.Rmb is not null && record.Objects is not null:
                    throw new InvalidOperationException($"Published block '{record.SourceKey}' carries both a city and a dungeon summary.");
                default:
                    break;
            }
        }
        else if (record.Rmb is not null || record.Objects is not null)
        {
            throw new InvalidOperationException($"Published block '{record.SourceKey}' is malformed and still summarizes bytes it could not read.");
        }

        record.RmbName?.Validate(record.SourceKey);
        record.RdbName?.Validate(record.SourceKey);
        if (record.Kind == DaggerfallBlockKind.Rmb)
        {
            record.Rmb?.Validate(record);
        }

        if (record.Kind == DaggerfallBlockKind.Rdb)
        {
            if (record.RdbName is null)
            {
                throw new InvalidOperationException($"Published block '{record.SourceKey}' is a dungeon block with no name classification.");
            }

            record.Objects?.Validate(record.SourceKey);
        }
        else if (record.RdbName is not null)
        {
            throw new InvalidOperationException($"Published block '{record.SourceKey}' is not a dungeon block and still carries a dungeon classification.");
        }
    }

    private static void Validate(this DaggerfallBlockRmbName name, string sourceKey)
    {
        int[] expected = [.. BlockRecordInventoryReader.RmbBlockPrefixes.Select((prefix, index) => (prefix, index)).Where(pair => StringComparer.Ordinal.Equals(pair.prefix, name.Prefix)).Select(pair => pair.index)];
        if (expected.Length == 0)
        {
            throw new InvalidOperationException($"Published block '{sourceKey}' names the prefix '{name.Prefix}', which the donor's table does not carry.");
        }

        // Every index that claims the prefix is retained: the donor's table carries two temple entries
        // under one prefix, so a name alone cannot say which of them a block came from.
        if (!name.TableIndices.SequenceEqual(expected))
        {
            throw new InvalidOperationException($"Published block '{sourceKey}' names the prefix indices [{string.Join(", ", name.TableIndices)}] where '{name.Prefix}' occupies [{string.Join(", ", expected)}].");
        }

        // The classification is a decomposition, so it has to reassemble the name it came from.
        string stem = sourceKey.EndsWith(".RMB", StringComparison.Ordinal) ? sourceKey[..^4] : sourceKey;
        if (!StringComparer.Ordinal.Equals(name.Prefix + name.Letter1 + name.Letter2 + name.NumberText, stem))
        {
            throw new InvalidOperationException($"Published block '{sourceKey}' is taken apart as '{name.Prefix}' + '{name.Letter1}' + '{name.Letter2}' + '{name.NumberText}', which does not reassemble its name.");
        }

        if (name.Letter1.Length != 1 || name.Letter2.Length != 1)
        {
            throw new InvalidOperationException($"Published block '{sourceKey}' states the letters '{name.Letter1}' and '{name.Letter2}', which are not one character each.");
        }

        if (name.Number is int number && (!name.NumberText.All(char.IsAsciiDigit) || number != int.Parse(name.NumberText, CultureInfo.InvariantCulture)))
        {
            throw new InvalidOperationException($"Published block '{sourceKey}' states the number {number} where its own text '{name.NumberText}' says otherwise.");
        }
    }

    private static void Validate(this DaggerfallBlockRdbName name, string sourceKey)
    {
        string stem = sourceKey.EndsWith(".RDB", StringComparison.Ordinal) ? sourceKey[..^4] : sourceKey;
        if (name.Letter.Length != 1 || stem.Length == 0 || stem[0] != name.Letter[0])
        {
            throw new InvalidOperationException($"Published block '{sourceKey}' states the letter '{name.Letter}', which does not begin its name.");
        }

        if ((BlockRdbType)name.Type != BlockRecordInventoryReader.RdbType(name.Letter[0]))
        {
            throw new InvalidOperationException($"Published block '{sourceKey}' states the dungeon type {name.Type}, which the letter '{name.Letter}' does not select.");
        }

        // The number is published verbatim as well as parsed, because the source pads it to a fixed width
        // and the name is what a lookup uses: a decomposition has to reassemble the name it came from.
        if (!StringComparer.Ordinal.Equals(name.Letter + name.NumberText, stem))
        {
            throw new InvalidOperationException($"Published block '{sourceKey}' is taken apart as '{name.Letter}' + '{name.NumberText}', which does not reassemble its name.");
        }

        if (name.Number is int number && (!name.NumberText.All(char.IsAsciiDigit) || number != int.Parse(name.NumberText, CultureInfo.InvariantCulture)))
        {
            throw new InvalidOperationException($"Published block '{sourceKey}' states the number {number} where its own text '{name.NumberText}' says otherwise.");
        }
    }

    private static void Validate(this DaggerfallBlockRmbHeader header, DaggerfallBlockRecord record)
    {
        if (header.DeclaredBlocks != header.Buildings.Count)
        {
            throw new InvalidOperationException($"Published block '{record.SourceKey}' declares {header.DeclaredBlocks} sub-records and carries {header.Buildings.Count}.");
        }

        if (header.OtherNames is < 0 or > 32 || header.Misc3dObjects < 0 || header.MiscFlatObjects < 0 || header.TrailingBytes < 0)
        {
            throw new InvalidOperationException($"Published block '{record.SourceKey}' states negative or impossible header counts.");
        }

        // The header's own arithmetic is republished so it can be checked without the source: the declared
        // sub-records, the objects that follow them, and whatever the record carries past both have to add
        // up to the record's own length.
        int accounted = DaggerfallBlocks.RmbHeaderBytes + header.TrailingBytes
            + (header.Misc3dObjects * DaggerfallBlocks.RmbModelRecordBytes)
            + (header.MiscFlatObjects * DaggerfallBlocks.RmbFlatRecordBytes)
            + header.Buildings.Sum(building => building.ByteLength);
        if (accounted != record.ByteLength)
        {
            throw new InvalidOperationException($"Published block '{record.SourceKey}' accounts for {accounted} bytes where the record spans {record.ByteLength}.");
        }

        for (int index = 0; index < header.Buildings.Count; index++)
        {
            DaggerfallBlockBuilding building = header.Buildings[index];
            if (building.Index != index)
            {
                throw new InvalidOperationException($"Published block '{record.SourceKey}' carries building {building.Index} at position {index}, so its sub-records are not in header order.");
            }

            if (building.ByteLength < DaggerfallBlocks.RmbBuildingHeaderBytes)
            {
                throw new InvalidOperationException($"Published block '{record.SourceKey}' gives building {index} {building.ByteLength} bytes, fewer than the {DaggerfallBlocks.RmbBuildingHeaderBytes}-byte header it has to carry.");
            }
        }
    }

    private static void Validate(this DaggerfallBlockObjects objects, string sourceKey)
    {
        if (objects.Models < 0 || objects.Flats < 0 || objects.Lights < 0 || objects.Doors < 0
            || objects.StartMarkers < 0 || objects.EnterMarkers < 0 || objects.TreasureMarkers < 0 || objects.FixedMobiles < 0)
        {
            throw new InvalidOperationException($"Published block '{sourceKey}' states a negative object count.");
        }

        if (objects.Doors > objects.Models)
        {
            throw new InvalidOperationException($"Published block '{sourceKey}' states {objects.Doors} door models of {objects.Models} models.");
        }

        int markers = objects.StartMarkers + objects.EnterMarkers + objects.TreasureMarkers + objects.FixedMobiles;
        if (markers > objects.Flats)
        {
            throw new InvalidOperationException($"Published block '{sourceKey}' states {markers} marker flats of {objects.Flats} flats.");
        }

        if (objects.ModelIds.Count > objects.Models)
        {
            throw new InvalidOperationException($"Published block '{sourceKey}' names {objects.ModelIds.Count} distinct models of the {objects.Models} it places.");
        }

        if (objects.Textures.Count > objects.Flats)
        {
            throw new InvalidOperationException($"Published block '{sourceKey}' names {objects.Textures.Count} distinct textures of the {objects.Flats} it places.");
        }

        NormalizedImportDocument.ValidateUnique(objects.ModelIds, model => model, $"model of '{sourceKey}'");
        NormalizedImportDocument.ValidateUnique(objects.Textures, texture => $"{texture.Archive}:{texture.Record}", $"texture of '{sourceKey}'");
    }
}

/// <summary>
/// Builds the published block inventory from the supplied archive and the documented inventory.
/// </summary>
/// <remarks>
/// The inventory decides the source's identity: the caller's label has to be the path the documented
/// record places the archive at, so a publication cannot cite blocks to a file the repository does not
/// document. Nothing here decodes a placement, a ground tile or an automap: this is the inventory the
/// dungeon, exterior and geometry tasks name when they publish what these records contain.
/// </remarks>
public static class DaggerfallBlocksBuilder
{
    /// <summary>The inventory family the block archive is documented under.</summary>
    public const string BlocksFamily = "CNT-005";

    /// <summary>Reads the supplied archive and publishes every record it declares.</summary>
    public static DaggerfallBlocks Build(byte[] bytes, string label, IReadOnlyList<SourceInventoryRow> inventory)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(inventory);
        SourceInventoryRow family = SourceInventoryRow.RequireFamily(inventory, BlocksFamily);
        if (!StringComparer.Ordinal.Equals(family.PathOrPattern, label))
        {
            throw new InvalidOperationException($"The block archive was read as '{label}', but the documented inventory places {BlocksFamily} at '{family.PathOrPattern}'.");
        }

        BlockRecordInventory catalog = BlockRecordInventoryReader.Read(bytes, label);
        List<DaggerfallBlockRecord> records = [];
        foreach (BlockRecord record in catalog.Records)
        {
            records.Add(new DaggerfallBlockRecord(
                record.Ordinal,
                record.SourceKey,
                label,
                (DaggerfallBlockKind)record.Kind,
                (DaggerfallBlockDisposition)record.Disposition,
                record.Offset,
                record.ByteLength,
                (DaggerfallBlockState)record.State,
                record.Reason,
                Publish(record.RmbName),
                Publish(record.RdbName),
                Publish(record.RmbHeader),
                Publish(record.Objects)));
        }

        DaggerfallBlocks published = new(
            DaggerfallBlocks.CurrentSchemaVersion,
            [new DaggerfallBlockSource(family.Id, label, bytes.LongLength, catalog.DeclaredRecords, catalog.Records.Count)],
            records);
        published.Validate();
        return published;
    }

    /// <summary>
    /// The archive's declared record count is what the header states, which the publication repeats so a
    /// consumer can see the count the archive claims beside the records it actually carries.
    /// </summary>
    private static DaggerfallBlockRmbName? Publish(BlockRmbName? name) =>
        name is null ? null : new DaggerfallBlockRmbName(name.Prefix, name.TableIndices, name.Letter1.ToString(), name.Letter2.ToString(), name.NumberText, name.Number, name.DonorShape);

    private static DaggerfallBlockRdbName? Publish(BlockRdbName? name) =>
        name is null ? null : new DaggerfallBlockRdbName(name.Letter.ToString(), name.NumberText, name.Number, (DaggerfallBlockRdbType)name.Type);

    private static DaggerfallBlockRmbHeader? Publish(RmbBlockSummary? summary) =>
        summary is null ? null : new DaggerfallBlockRmbHeader(
            summary.Name,
            summary.OtherNames,
            summary.DeclaredBlocks,
            summary.Misc3dObjects,
            summary.MiscFlatObjects,
            [.. summary.Buildings.Select(building => new DaggerfallBlockBuilding(
                building.Index, building.ByteLength, building.BuildingType, building.FactionId, building.Quality, building.NameSeed,
                building.Objects, building.Flats, building.Sections, building.People, building.Doors))],
            summary.TrailingBytes);

    private static DaggerfallBlockObjects? Publish(BlockObjectSummary? objects) =>
        objects is null ? null : new DaggerfallBlockObjects(
            objects.Models, objects.Flats, objects.Lights, objects.Doors,
            objects.StartMarkers, objects.EnterMarkers, objects.TreasureMarkers, objects.FixedMobiles,
            objects.ModelIds,
            [.. objects.Textures.Select(texture => new DaggerfallBlockTexture(texture.Archive, texture.Record))]);
}
