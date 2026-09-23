using System.Globalization;
using System.Text;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalized;

namespace Daggerfall.Import.Normalization;

/// <summary>Disposition of one source or cross-file reference in the dungeon corpus.</summary>
public enum DungeonCorpusClosureDisposition
{
    Valid,
    Unresolved,
    Malformed,
    Duplicate,
    Unused,
}

/// <summary>Source relationship represented by one closure entry.</summary>
public enum DungeonCorpusClosureKind
{
    MapTable,
    Location,
    DungeonLink,
    ExteriorBlockLink,
    DungeonBlockLink,
    Block,
    MeshLink,
    Mesh,
    TextureLink,
    TextureRecord,
    TextureLeaf,
    ActionLink,
}

/// <summary>One source ID or source link and its whole-corpus disposition.</summary>
public sealed record DungeonCorpusClosureEntry(
    string SourceId,
    DungeonCorpusClosureKind Kind,
    DungeonCorpusClosureDisposition Disposition,
    string Source,
    string Reason)
{
    public void Validate()
    {
        NormalizedImportDocument.RequireLogicalId(SourceId, nameof(SourceId));
        NormalizedImportDocument.RequireLogicalPath(Source, nameof(Source));
        if (string.IsNullOrWhiteSpace(Reason))
            throw new InvalidOperationException($"Closure entry '{SourceId}' carries no reason.");
    }
}

/// <summary>Validated whole-corpus closure for MAPS, BLOCKS, ARCH3D, texture leaves, and links.</summary>
public sealed record DungeonCorpusClosureReport(
    int RegionCount,
    int LocationCount,
    int DungeonLocationCount,
    int ExteriorLocationCount,
    int ReferencedRmbCount,
    int ReferencedRdbCount,
    IReadOnlyList<DungeonCorpusClosureEntry> Entries)
{
    public IReadOnlyDictionary<DungeonCorpusClosureDisposition, int> DispositionCounts => Entries
        .GroupBy(entry => entry.Disposition)
        .ToDictionary(group => group.Key, group => group.Count());

    public IReadOnlyDictionary<DungeonCorpusClosureKind, int> KindCounts => Entries
        .GroupBy(entry => entry.Kind)
        .ToDictionary(group => group.Key, group => group.Count());

    public int Count(DungeonCorpusClosureDisposition disposition) => Entries.Count(entry => entry.Disposition == disposition);

    public void Validate()
    {
        if (RegionCount != 62)
            throw new InvalidOperationException($"The closure reports {RegionCount} regions; classic MAPS.BSA declares 62.");
        if (LocationCount < 0 || DungeonLocationCount < 0 || ExteriorLocationCount < 0)
            throw new InvalidOperationException("The closure carries a negative MAPS count.");
        if (ReferencedRmbCount < 0 || ReferencedRdbCount < 0)
            throw new InvalidOperationException("The closure carries a negative block-reference count.");
        ArgumentNullException.ThrowIfNull(Entries);

        HashSet<string> ids = new(StringComparer.Ordinal);
        foreach (DungeonCorpusClosureEntry entry in Entries)
        {
            entry.Validate();
            if (!ids.Add(entry.SourceId))
                throw new InvalidOperationException($"The closure repeats source ID '{entry.SourceId}'.");
        }
    }

    /// <summary>Writes a deterministic Markdown report without source payload bytes.</summary>
    public string ToMarkdown()
    {
        Validate();
        StringBuilder output = new();
        output.AppendLine("# Daggerfall world corpus closure");
        output.AppendLine();
        output.AppendLine("Generated from the supplied Arena2 corpus by `DungeonCorpusClosureBuilder`. Source payloads are not copied here.");
        output.AppendLine();
        output.AppendLine($"- Regions: {RegionCount}");
        output.AppendLine($"- Locations: {LocationCount}");
        output.AppendLine($"- Dungeon locations: {DungeonLocationCount}");
        output.AppendLine($"- Exterior locations inspected: {ExteriorLocationCount}");
        output.AppendLine($"- Referenced RMB records: {ReferencedRmbCount}");
        output.AppendLine($"- Referenced RDB records: {ReferencedRdbCount}");
        output.AppendLine();
        output.AppendLine("## Disposition summary");
        output.AppendLine();
        output.AppendLine("| Disposition | Entries |");
        output.AppendLine("| --- | ---: |");
        foreach (DungeonCorpusClosureDisposition disposition in Enum.GetValues<DungeonCorpusClosureDisposition>())
            output.AppendLine($"| {disposition} | {Count(disposition)} |");
        output.AppendLine();
        output.AppendLine("## Kind summary");
        output.AppendLine();
        output.AppendLine("| Kind | Entries |");
        output.AppendLine("| --- | ---: |");
        foreach (DungeonCorpusClosureKind kind in Enum.GetValues<DungeonCorpusClosureKind>())
            output.AppendLine($"| {kind} | {KindCounts.GetValueOrDefault(kind)} |");
        output.AppendLine();
        output.AppendLine("## Source ID examples");
        output.AppendLine();
        output.AppendLine("The complete deterministic manifest is [`dungeon-corpus-closure.tsv`](dungeon-corpus-closure.tsv); this page keeps bounded examples readable while the API and TSV retain every source ID.");
        output.AppendLine();
        output.AppendLine("| Disposition | Kind | Source ID | Source | Reason |");
        output.AppendLine("| --- | --- | --- | --- | --- |");
        foreach (DungeonCorpusClosureEntry entry in Entries
            .GroupBy(entry => entry.Disposition)
            .SelectMany(group => group.OrderBy(entry => entry.Kind).ThenBy(entry => entry.SourceId, StringComparer.Ordinal).Take(12))
            .OrderBy(entry => entry.Disposition)
            .ThenBy(entry => entry.Kind)
            .ThenBy(entry => entry.SourceId, StringComparer.Ordinal))
        {
            output.Append('|').Append(entry.Disposition).Append('|').Append(entry.Kind).Append('|')
                .Append(Escape(entry.SourceId)).Append('|').Append(Escape(entry.Source)).Append('|')
                .Append(Escape(entry.Reason)).AppendLine("|");
        }

        return output.ToString();
    }

    /// <summary>Writes every closure entry as a deterministic tab-separated manifest.</summary>
    public string ToTsv()
    {
        Validate();
        StringBuilder output = new();
        output.AppendLine("disposition\tkind\tsource_id\tsource\treason");
        foreach (DungeonCorpusClosureEntry entry in Entries.OrderBy(entry => entry.SourceId, StringComparer.Ordinal))
        {
            output.Append(entry.Disposition).Append('\t').Append(entry.Kind).Append('\t')
                .Append(Tsv(entry.SourceId)).Append('\t').Append(Tsv(entry.Source)).Append('\t')
                .Append(Tsv(entry.Reason)).AppendLine();
        }

        return output.ToString();
    }

    private static string Escape(string value) => value.Replace("|", "\\|", StringComparison.Ordinal).Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);

    private static string Tsv(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\t", "\\t", StringComparison.Ordinal).Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);
}

/// <summary>Input source set for the whole-corpus closure pass.</summary>
public sealed record DungeonCorpusClosureRequest(DungeonLogicalSourceSet Sources)
{
    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Sources);
        _ = Sources.Require("MAPS.BSA");
        _ = Sources.Require("BLOCKS.BSA");
        _ = Sources.Require("ARCH3D.BSA");
    }
}

/// <summary>
/// Reads every MAPS region and every block reachable from its dungeon/exterior records, then follows
/// model, texture, and action links through the existing Arena2 inventories.
/// </summary>
public static class DungeonCorpusClosureBuilder
{
    private const int RegionCount = 62;
    private const string MapsFileName = "MAPS.BSA";
    private const string BlocksFileName = "BLOCKS.BSA";
    private const string MeshFileName = "ARCH3D.BSA";

    public static DungeonCorpusClosureReport Build(DungeonCorpusClosureRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Validate();

        DungeonLogicalSource mapsSource = request.Sources.Require(MapsFileName);
        DungeonLogicalSource blocksSource = request.Sources.Require(BlocksFileName);
        DungeonLogicalSource meshSource = request.Sources.Require(MeshFileName);
        BsaArchive maps = BsaArchive.Parse(mapsSource.Bytes.Span, mapsSource.Label);
        BlockRecordInventory blocks = BlockRecordInventoryReader.Read(blocksSource.Bytes.ToArray(), blocksSource.Label);
        Arch3dMeshInventory meshes = Arch3dInventoryReader.Read(meshSource.Bytes.ToArray(), meshSource.Label);
        TextureLeafInventory textures = ReadTextures(request.Sources);
        PakMap? climate = null;
        string? climateFailure = null;
        if (request.Sources.TryGet("CLIMATE.PAK", out DungeonLogicalSource? climateSource) && climateSource is not null)
        {
            try
            {
                climate = PakDecoder.Decode(climateSource.Bytes.Span, climateSource.Label);
            }
            catch (Arena2FormatException error)
            {
                climateFailure = error.Message;
            }
        }

        List<DungeonCorpusClosureEntry> entries = [];
        List<BlockReference> blockReferences = [];
        List<MeshReference> meshReferences = [];
        List<TextureReference> textureReferences = [];
        List<ActionReference> actionReferences = [];
        IReadOnlyDictionary<int, MapsRegionGroup> groups = MapsDecoder.DecodeRegionGroups(maps)
            .ToDictionary(group => group.Region);
        int locationCount = 0;
        int dungeonLocationCount = 0;
        int exteriorLocationCount = 0;

        for (int region = 0; region < RegionCount; region++)
        {
            if (!groups.TryGetValue(region, out MapsRegionGroup? group))
            {
                foreach (string table in MapsDecoder.RegionTables)
                    Add(entries, $"maps/region/{region}/table/{table}", DungeonCorpusClosureKind.MapTable,
                        DungeonCorpusClosureDisposition.Unresolved, mapsSource.Label,
                        $"MAPS.BSA has no {table}.{region:000} record.");
                continue;
            }

            foreach (MapsRegionTable table in group.Tables)
            {
                (DungeonCorpusClosureDisposition disposition, string reason) = table.State switch
                {
                    MapsTableState.Read => (DungeonCorpusClosureDisposition.Valid, table.Reason),
                    MapsTableState.Malformed => (DungeonCorpusClosureDisposition.Malformed, table.Reason),
                    _ => (DungeonCorpusClosureDisposition.Unresolved, table.Reason),
                };
                Add(entries, $"maps/region/{region}/table/{table.Name}", DungeonCorpusClosureKind.MapTable, disposition, mapsSource.Label, reason);
            }

            if (group.Tables.Any(table => table.State != MapsTableState.Read))
                continue;

            IReadOnlyList<MapsLocationRecord> locations;
            try
            {
                locations = MapsDecoder.DecodeRegionLocations(maps, region);
            }
            catch (Arena2FormatException error)
            {
                Add(entries, $"maps/region/{region}/locations", DungeonCorpusClosureKind.Location,
                    DungeonCorpusClosureDisposition.Malformed, mapsSource.Label, error.Message);
                continue;
            }

            foreach (MapsLocationRecord location in locations)
            {
                locationCount++;
                string locationId = $"maps/region/{region}/location/{location.Index}";
                Add(entries, locationId, DungeonCorpusClosureKind.Location,
                    string.IsNullOrWhiteSpace(location.Name) || location.MapId < 0
                        ? DungeonCorpusClosureDisposition.Malformed
                        : DungeonCorpusClosureDisposition.Valid,
                    mapsSource.Label,
                    string.IsNullOrWhiteSpace(location.Name) ? "The MAPS location has no name." : $"MAPS location '{location.Name}' is readable.");

                try
                {
                    MapsExteriorLayout exterior = MapsDecoder.DecodeExteriorLayout(maps, region, location.Index);
                    exteriorLocationCount++;
                    Add(entries, $"{locationId}/exterior", DungeonCorpusClosureKind.ExteriorBlockLink,
                        DungeonCorpusClosureDisposition.Valid, mapsSource.Label,
                        $"MAPPITEM location index {exterior.LocationIndex} resolves {exterior.Blocks.Count} RMB block references.");
                    ushort? groundTextureArchive = GroundTextureArchive(climate, location, climateFailure);
                    foreach ((MapsExteriorBlock block, int index) in exterior.Blocks.Select((block, index) => (block, index)))
                    {
                        string sourceId = $"{locationId}/exterior-block/{index}";
                        blockReferences.Add(new(sourceId, block.SourceName, DungeonCorpusClosureKind.ExteriorBlockLink, groundTextureArchive));
                    }
                }
                catch (Arena2FormatException error)
                {
                    Add(entries, $"{locationId}/exterior", DungeonCorpusClosureKind.ExteriorBlockLink,
                        DungeonCorpusClosureDisposition.Malformed, mapsSource.Label, error.Message);
                }
            }

            IReadOnlyList<MapsDungeonLocation> dungeons;
            try
            {
                dungeons = MapsDecoder.DecodeRegionDungeons(maps, region);
            }
            catch (Arena2FormatException error)
            {
                Add(entries, $"maps/region/{region}/dungeons", DungeonCorpusClosureKind.DungeonLink,
                    DungeonCorpusClosureDisposition.Malformed, mapsSource.Label, error.Message);
                continue;
            }

            foreach (MapsDungeonLocation dungeon in dungeons)
            {
                string locationId = $"maps/region/{region}/location/{dungeon.Index}/dungeon";
                if (dungeon.State == MapsDungeonState.Read)
                {
                    dungeonLocationCount++;
                    Add(entries, locationId, DungeonCorpusClosureKind.DungeonLink,
                        DungeonCorpusClosureDisposition.Valid, mapsSource.Label,
                        $"MAPDITEM resolves {dungeon.Blocks.Count} RDB block references.");
                    foreach ((MapsDungeonBlock block, int index) in dungeon.Blocks.Select((block, index) => (block, index)))
                    {
                        string sourceId = $"{locationId}/block/{index}";
                        blockReferences.Add(new(sourceId, block.SourceName, DungeonCorpusClosureKind.DungeonBlockLink));
                    }
                }
                else
                {
                    Add(entries, locationId, DungeonCorpusClosureKind.DungeonLink,
                        dungeon.State == MapsDungeonState.Malformed
                            ? DungeonCorpusClosureDisposition.Malformed
                            : DungeonCorpusClosureDisposition.Unresolved,
                        mapsSource.Label, dungeon.Reason);
                }
            }
        }

        IReadOnlyDictionary<string, BlockRecord> blocksByName = blocks.Records
            .Where(record => record.SourceKey is not null)
            .ToDictionary(record => record.SourceKey, StringComparer.Ordinal);
        HashSet<string> referencedRmb = blockReferences.Where(reference => reference.Kind == DungeonCorpusClosureKind.ExteriorBlockLink)
            .Select(reference => reference.BlockName).ToHashSet(StringComparer.Ordinal);
        HashSet<string> referencedRdb = blockReferences.Where(reference => reference.Kind == DungeonCorpusClosureKind.DungeonBlockLink)
            .Select(reference => reference.BlockName).ToHashSet(StringComparer.Ordinal);

        foreach (BlockRecord record in blocks.Records)
        {
            bool referenced = referencedRmb.Contains(record.SourceKey) || referencedRdb.Contains(record.SourceKey);
            DungeonCorpusClosureDisposition disposition = record.State == BlockRecordState.Malformed
                ? DungeonCorpusClosureDisposition.Malformed
                : referenced && record.Kind is BlockRecordKind.Rmb or BlockRecordKind.Rdb
                    ? DungeonCorpusClosureDisposition.Valid
                    : DungeonCorpusClosureDisposition.Unused;
            string reason = record.State == BlockRecordState.Malformed
                ? record.Reason
                : referenced
                    ? $"{record.Kind} block is reachable from MAPS.BSA."
                    : "The block is supplied but no readable MAPS location references it.";
            Add(entries, $"block/{record.SourceKey}", DungeonCorpusClosureKind.Block, disposition, blocks.Source, reason);
        }

        HashSet<string> processedBlocks = new(StringComparer.Ordinal);
        HashSet<string> emittedBlockLinks = new(StringComparer.Ordinal);
        foreach (BlockReference reference in blockReferences)
        {
            if (!blocksByName.TryGetValue(reference.BlockName, out BlockRecord? record))
            {
                Add(entries, reference.SourceId, reference.Kind, DungeonCorpusClosureDisposition.Unresolved, blocks.Source,
                    $"MAPS references block '{reference.BlockName}', but BLOCKS.BSA does not supply it.");
                continue;
            }

            string blockLinkId = reference.Kind == DungeonCorpusClosureKind.ExteriorBlockLink
                ? $"rmb-link/{reference.BlockName}"
                : $"rdb-link/{reference.BlockName}";
            if (emittedBlockLinks.Add(blockLinkId))
                Add(entries, blockLinkId, reference.Kind,
                    record.State == BlockRecordState.Malformed ? DungeonCorpusClosureDisposition.Malformed : DungeonCorpusClosureDisposition.Valid,
                    blocks.Source,
                    record.State == BlockRecordState.Malformed ? record.Reason : $"BLOCKS.BSA resolves '{reference.BlockName}'.");
            if (record.State != BlockRecordState.Read)
                continue;

            if (!processedBlocks.Add(record.SourceKey))
                continue;

            try
            {
                ReadBlockLinks(record, blocksSource.Bytes, blocks.Source, meshReferences, textureReferences, actionReferences);
                if (record.Kind == BlockRecordKind.Rmb)
                {
                    foreach (ushort archive in blockReferences
                        .Where(reference => reference.BlockName == record.SourceKey && reference.GroundTextureArchive is not null)
                        .Select(reference => reference.GroundTextureArchive!.Value)
                        .Distinct())
                        ReadRmbGroundTextureLinks(record, archive, textureReferences);

                    if (!blockReferences.Any(reference => reference.BlockName == record.SourceKey && reference.GroundTextureArchive is not null))
                    {
                        string reason = climateFailure is not null
                            ? $"CLIMATE.PAK could not be decoded, so the RMB ground texture archive is unresolved: {climateFailure}"
                            : "CLIMATE.PAK was not supplied, so the RMB ground texture archive is unresolved.";
                        Add(entries, $"block/{record.SourceKey}/ground-texture", DungeonCorpusClosureKind.TextureLink,
                            DungeonCorpusClosureDisposition.Unresolved, blocks.Source, reason);
                    }
                }
            }
            catch (Arena2FormatException error)
            {
                Add(entries, $"block/{record.SourceKey}/placements", DungeonCorpusClosureKind.Block,
                    DungeonCorpusClosureDisposition.Malformed, blocks.Source, error.Message);
            }
        }

        // A model or texture can be placed many times in one reachable block. The closure is about
        // source links, so retain one deterministic source ID per block/model or block/texture pair;
        // the placement readers remain responsible for preserving each occurrence in normalized output.
        meshReferences = [.. meshReferences.GroupBy(reference => reference.SourceId, StringComparer.Ordinal).Select(group => group.First())];
        textureReferences = [.. textureReferences.GroupBy(reference => reference.SourceId, StringComparer.Ordinal).Select(group => group.First())];

        AddMeshEntries(entries, meshReferences, meshes, meshes.Source);
        AddTextureEntries(entries, textureReferences, meshReferences, meshes, textures, textures.Source);
        AddActionEntries(entries, actionReferences, blocks.Source);

        DungeonCorpusClosureReport report = new(
            RegionCount,
            locationCount,
            dungeonLocationCount,
            exteriorLocationCount,
            referencedRmb.Count,
            referencedRdb.Count,
            [.. entries.OrderBy(entry => entry.SourceId, StringComparer.Ordinal)]);
        report.Validate();
        return report;
    }

    private static TextureLeafInventory ReadTextures(DungeonLogicalSourceSet sources)
    {
        List<(int Id, string Path, ReadOnlyMemory<byte> Bytes)> leaves = [];
        foreach (DungeonLogicalSource source in sources.Sources)
        {
            string leaf = source.Label[(source.Label.LastIndexOf('/') + 1)..];
            if (!leaf.StartsWith("TEXTURE.", StringComparison.OrdinalIgnoreCase)
                || !int.TryParse(leaf["TEXTURE.".Length..], NumberStyles.None, CultureInfo.InvariantCulture, out int id))
                continue;
            leaves.Add((id, leaf, source.Bytes));
        }

        return TextureLeafInventory.Enumerate(leaves, "Arena2 TEXTURE leaf corpus");
    }

    private static ushort? GroundTextureArchive(PakMap? climate, MapsLocationRecord location, string? climateFailure)
    {
        if (climate is null || climateFailure is not null)
            return null;
        (int mapX, int climateY) = MapsDecoder.ToMapPixel(location.Longitude, location.Latitude);
        int climateX = mapX + 1;
        return climate.TryGetPixel(climateX, climateY, out byte value) ? GroundTextureArchive(value) : null;
    }

    private static ushort GroundTextureArchive(byte climate) => climate switch
    {
        223 or 227 or 228 => 402,
        224 or 225 or 229 => 2,
        226 or 230 => 102,
        _ => 302,
    };

    private static void ReadBlockLinks(
        BlockRecord record,
        ReadOnlyMemory<byte> blocksBytes,
        string blocksSource,
        ICollection<MeshReference> meshReferences,
        ICollection<TextureReference> textureReferences,
        ICollection<ActionReference> actionReferences)
    {
        if (record.Kind == BlockRecordKind.Rdb)
        {
            RdbBlockSource block = RdbDecoder.Decode(blocksBytes.Span[(int)record.Offset..(int)(record.Offset + record.ByteLength)], blocksSource);
            HashSet<int> actionOffsets = [];
            foreach ((RdbModelSource model, int index) in block.Models.Select((model, index) => (model, index)))
            {
                meshReferences.Add(new($"mesh-link/{model.ModelId}", model.ModelId));
                if (model.Action is not null && model.ObjectOffset > 0)
                {
                    actionOffsets.Add(model.ObjectOffset);
                    actionReferences.Add(new($"block/{record.SourceKey}/action/model/{index}", model.ObjectOffset, model.Action.NextObjectOffset));
                }
            }

            foreach ((RdbFlatSource flat, int index) in block.Flats.Select((flat, index) => (flat, index)))
            {
                textureReferences.Add(new($"texture-link/{flat.TextureArchive}/{flat.TextureRecord}", flat.TextureArchive, flat.TextureRecord));
                if (flat.ObjectOffset > 0 && (flat.Action != 0 || flat.Flags != 0 || flat.NextObjectOffset >= 0))
                {
                    actionOffsets.Add(flat.ObjectOffset);
                    actionReferences.Add(new($"block/{record.SourceKey}/action/flat/{index}", flat.ObjectOffset, flat.NextObjectOffset));
                }
            }

            // The target set is evaluated after all source nodes have been seen. The temporary target
            // set is carried by the action references' source block and rechecked in AddActionEntries.
            foreach (ActionReference action in actionReferences.Where(action => action.SourceId.StartsWith($"block/{record.SourceKey}/", StringComparison.Ordinal)))
                action.TargetIsAction = action.NextObjectOffset > 0 && actionOffsets.Contains(action.NextObjectOffset);
            return;
        }

        if (record.Kind != BlockRecordKind.Rmb || record.RmbPlacements is null)
            return;

        int buildingIndex = 0;
        foreach (RmbBuildingPlacements building in record.RmbPlacements.Buildings)
        {
            ReadRmbHalf(record.SourceKey, $"building/{buildingIndex}/exterior", building.Exterior, meshReferences, textureReferences);
            ReadRmbHalf(record.SourceKey, $"building/{buildingIndex}/interior", building.Interior, meshReferences, textureReferences);
            buildingIndex++;
        }

        foreach ((RmbModelPlacement model, int index) in record.RmbPlacements.MiscModels.Select((model, index) => (model, index)))
            meshReferences.Add(new($"mesh-link/{model.ModelId}", model.ModelId));
        foreach ((RmbFlatPlacement flat, int index) in record.RmbPlacements.MiscFlats.Select((flat, index) => (flat, index)))
            textureReferences.Add(new($"texture-link/{flat.TextureArchive}/{flat.TextureRecord}", checked((ushort)flat.TextureArchive), checked((ushort)flat.TextureRecord)));

    }

    private static void ReadRmbGroundTextureLinks(BlockRecord record, ushort archive, ICollection<TextureReference> textureReferences)
    {
        if (record.RmbHeader is null)
            return;
        foreach (RmbGroundTile tile in record.RmbHeader.GroundTiles)
        {
            // MeshReader maps source records 56..63 to the grass fallback at record 2.
            ushort textureRecord = tile.TextureRecord < 56 ? tile.TextureRecord : (ushort)2;
            string note = tile.TextureRecord < 56
                ? "RMB ground tile selects this source record."
                : $"RMB ground tile source record {tile.TextureRecord} uses the donor grass fallback record 2.";
            textureReferences.Add(new($"texture-link/{archive}/{textureRecord}", archive, textureRecord, note));
        }
    }

    private static void ReadRmbHalf(
        string block,
        string half,
        RmbHalfPlacements placements,
        ICollection<MeshReference> meshReferences,
        ICollection<TextureReference> textureReferences)
    {
        foreach ((RmbModelPlacement model, int index) in placements.Models.Select((model, index) => (model, index)))
            meshReferences.Add(new($"mesh-link/{model.ModelId}", model.ModelId));
        foreach ((RmbFlatPlacement flat, int index) in placements.Flats.Select((flat, index) => (flat, index)))
            textureReferences.Add(new($"texture-link/{flat.TextureArchive}/{flat.TextureRecord}", checked((ushort)flat.TextureArchive), checked((ushort)flat.TextureRecord)));
        foreach ((RmbPeoplePlacement person, int index) in placements.People.Select((person, index) => (person, index)))
            textureReferences.Add(new($"texture-link/{person.TextureArchive}/{person.TextureRecord}", checked((ushort)person.TextureArchive), checked((ushort)person.TextureRecord)));
    }

    private static void AddMeshEntries(
        ICollection<DungeonCorpusClosureEntry> entries,
        IReadOnlyCollection<MeshReference> references,
        Arch3dMeshInventory meshes,
        string source)
    {
        Dictionary<uint, Arch3dMeshRecord> firstById = meshes.Records
            .Where(record => record.DuplicateOf is null)
            .ToDictionary(record => record.RecordId);
        HashSet<string> referencedIds = [];
        foreach (MeshReference reference in references)
        {
            if (!uint.TryParse(reference.ModelId, NumberStyles.None, CultureInfo.InvariantCulture, out uint modelId))
            {
                Add(entries, reference.SourceId, DungeonCorpusClosureKind.MeshLink, DungeonCorpusClosureDisposition.Unresolved, source,
                    $"BLOCKS model reference '{reference.ModelId}' is not a numeric ARCH3D record ID.");
                continue;
            }

            referencedIds.Add(modelId.ToString(CultureInfo.InvariantCulture));
            if (!firstById.TryGetValue(modelId, out Arch3dMeshRecord? mesh))
            {
                Add(entries, reference.SourceId, DungeonCorpusClosureKind.MeshLink, DungeonCorpusClosureDisposition.Unresolved, source,
                    $"BLOCKS references ARCH3D mesh {modelId}, which is not supplied.");
                continue;
            }

            Add(entries, reference.SourceId, DungeonCorpusClosureKind.MeshLink,
                mesh.State == Arch3dRecordState.Malformed ? DungeonCorpusClosureDisposition.Malformed : DungeonCorpusClosureDisposition.Valid,
                source,
                mesh.State == Arch3dRecordState.Malformed ? mesh.Reason : $"ARCH3D mesh {modelId} resolves through its first directory record.");
        }

        foreach (Arch3dMeshRecord mesh in meshes.Records)
        {
            DungeonCorpusClosureDisposition disposition = mesh.DuplicateOf is not null
                ? DungeonCorpusClosureDisposition.Duplicate
                : mesh.State == Arch3dRecordState.Malformed
                    ? DungeonCorpusClosureDisposition.Malformed
                    : referencedIds.Contains(mesh.RecordId.ToString(CultureInfo.InvariantCulture))
                        ? DungeonCorpusClosureDisposition.Valid
                        : DungeonCorpusClosureDisposition.Unused;
            string reason = mesh.DuplicateOf is not null
                ? $"ARCH3D numeric ID {mesh.RecordId} resolves to earlier directory record {mesh.DuplicateOf.Value}."
                : mesh.State == Arch3dRecordState.Malformed
                    ? mesh.Reason
                    : disposition == DungeonCorpusClosureDisposition.Valid
                        ? "ARCH3D mesh is reachable from a referenced block."
                        : "ARCH3D mesh is supplied but no referenced block reaches its numeric ID.";
            Add(entries, $"mesh/{mesh.RecordId}/record/{mesh.Ordinal}", DungeonCorpusClosureKind.Mesh, disposition, source, reason);
        }
    }

    private static void AddTextureEntries(
        ICollection<DungeonCorpusClosureEntry> entries,
        IReadOnlyCollection<TextureReference> references,
        IReadOnlyCollection<MeshReference> meshReferences,
        Arch3dMeshInventory meshes,
        TextureLeafInventory textures,
        string source)
    {
        Dictionary<uint, Arch3dMeshRecord> firstById = meshes.Records
            .Where(record => record.DuplicateOf is null)
            .ToDictionary(record => record.RecordId);
        List<TextureReference> allReferences = [.. references];
        foreach (MeshReference reference in meshReferences)
        {
            if (!uint.TryParse(reference.ModelId, NumberStyles.None, CultureInfo.InvariantCulture, out uint modelId)
                || !firstById.TryGetValue(modelId, out Arch3dMeshRecord? mesh)
                || mesh.Facts is null)
                continue;
            foreach ((Arch3dTextureReference texture, int index) in mesh.Facts.Textures.Select((texture, index) => (texture, index)))
                allReferences.Add(new($"texture-link/{texture.Archive}/{texture.Record}", texture.Archive, texture.Record));
        }

        allReferences = [.. allReferences.GroupBy(reference => reference.SourceId, StringComparer.Ordinal).Select(group => group.First())];

        HashSet<(ushort Archive, ushort Record)> usedRecords = allReferences
            .Select(reference => (reference.Archive, reference.Record))
            .ToHashSet();
        foreach (TextureLeafRecord leaf in textures.Leaves)
        {
            DungeonCorpusClosureDisposition disposition = leaf.Disposition switch
            {
                TextureLeafDisposition.Decoded when usedRecords.Any(reference => reference.Archive == leaf.Id) => DungeonCorpusClosureDisposition.Valid,
                TextureLeafDisposition.Decoded => DungeonCorpusClosureDisposition.Unused,
                TextureLeafDisposition.Malformed => DungeonCorpusClosureDisposition.Malformed,
                _ => DungeonCorpusClosureDisposition.Unresolved,
            };
            string reason = leaf.Disposition switch
            {
                TextureLeafDisposition.Decoded when disposition == DungeonCorpusClosureDisposition.Valid => "Texture leaf is reached by a block or mesh texture link.",
                TextureLeafDisposition.Decoded => "Texture leaf is supplied but no referenced block or mesh reaches it.",
                _ => leaf.Note,
            };
            Add(entries, $"texture-leaf/{leaf.Id:000}", DungeonCorpusClosureKind.TextureLeaf, disposition, source, reason);
            foreach (TextureRecordFacts facts in leaf.RecordFacts)
            {
                bool used = usedRecords.Contains(((ushort)leaf.Id, checked((ushort)facts.RecordIndex)));
                DungeonCorpusClosureDisposition recordDisposition = facts.UnreadableReason.Length != 0
                    ? DungeonCorpusClosureDisposition.Malformed
                    : used ? DungeonCorpusClosureDisposition.Valid : DungeonCorpusClosureDisposition.Unused;
                Add(entries, $"texture/{leaf.Id:000}/record/{facts.RecordIndex}", DungeonCorpusClosureKind.TextureRecord,
                    recordDisposition, source,
                    facts.UnreadableReason.Length != 0 ? facts.UnreadableReason : used ? "Texture record is linked by a block or mesh." : "Texture record is supplied but unused by the reachable closure.");
            }
        }

        foreach (TextureReference reference in allReferences)
        {
            string sourceId = reference.SourceId;
            if (!textures.TryGet(reference.Archive, out TextureLeafRecord? leaf) || leaf!.Disposition == TextureLeafDisposition.NotSupplied)
            {
                Add(entries, sourceId, DungeonCorpusClosureKind.TextureLink, DungeonCorpusClosureDisposition.Unresolved, source,
                    $"Texture archive {reference.Archive} is not supplied.");
                continue;
            }

            if (leaf.Disposition == TextureLeafDisposition.Malformed)
            {
                Add(entries, sourceId, DungeonCorpusClosureKind.TextureLink, DungeonCorpusClosureDisposition.Malformed, source,
                    $"Texture archive {reference.Archive} is malformed: {leaf.Note}");
                continue;
            }

            if (!textures.TryGetRecord(reference.Archive, reference.Record, out TextureRecordFacts? facts) || facts is null)
            {
                Add(entries, sourceId, DungeonCorpusClosureKind.TextureLink, DungeonCorpusClosureDisposition.Unresolved, source,
                    $"Texture archive {reference.Archive} has no record {reference.Record}.");
                continue;
            }

            Add(entries, sourceId, DungeonCorpusClosureKind.TextureLink,
                facts.UnreadableReason.Length == 0 ? DungeonCorpusClosureDisposition.Valid : DungeonCorpusClosureDisposition.Malformed,
                source,
                facts.UnreadableReason.Length == 0
                    ? reference.Note ?? $"Texture {reference.Archive}:{reference.Record} resolves."
                    : facts.UnreadableReason);
        }
    }

    private static void AddActionEntries(ICollection<DungeonCorpusClosureEntry> entries, IReadOnlyCollection<ActionReference> references, string source)
    {
        foreach (ActionReference reference in references)
        {
            DungeonCorpusClosureDisposition disposition = reference.NextObjectOffset > 0 && !reference.TargetIsAction
                ? DungeonCorpusClosureDisposition.Unresolved
                : DungeonCorpusClosureDisposition.Valid;
            Add(entries, reference.SourceId, DungeonCorpusClosureKind.ActionLink, disposition, source,
                reference.NextObjectOffset > 0
                    ? reference.TargetIsAction ? $"Action target offset {reference.NextObjectOffset} resolves to another action node." : $"Action target offset {reference.NextObjectOffset} has no action node in this block."
                    : "Action carries a terminal or null source-link sentinel.");
        }
    }

    private static void Add(
        ICollection<DungeonCorpusClosureEntry> entries,
        string sourceId,
        DungeonCorpusClosureKind kind,
        DungeonCorpusClosureDisposition disposition,
        string source,
        string reason)
    {
        entries.Add(new DungeonCorpusClosureEntry(sourceId, kind, disposition, source, string.IsNullOrWhiteSpace(reason) ? "No source reason was supplied." : reason));
    }

    private sealed record BlockReference(string SourceId, string BlockName, DungeonCorpusClosureKind Kind, ushort? GroundTextureArchive = null);

    private sealed record MeshReference(string SourceId, string ModelId);

    private sealed record TextureReference(string SourceId, ushort Archive, ushort Record, string? Note = null);

    private sealed class ActionReference(string sourceId, int objectOffset, int nextObjectOffset)
    {
        internal string SourceId { get; } = sourceId;
        internal int ObjectOffset { get; } = objectOffset;
        internal int NextObjectOffset { get; } = nextObjectOffset;
        internal bool TargetIsAction { get; set; }
    }
}
