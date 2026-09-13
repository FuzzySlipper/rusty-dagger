using Daggerfall.Import.Arena2;

namespace Daggerfall.Import.Normalized;

/// <summary>One location a region describes: its name, its map and where it sits.</summary>
/// <param name="Region">The source region index, preserved from the table name.</param>
/// <param name="Index">The location's ordinal in the region's names table.</param>
/// <param name="Name">The exact name the names table carries.</param>
/// <param name="MapId">The map the location draws.</param>
/// <param name="Longitude">The location's authored longitude.</param>
/// <param name="Latitude">The location's authored latitude.</param>
/// <param name="DungeonType">The location's dungeon type byte, zero when it has none.</param>
/// <param name="LocationType">The location's type, from the map table's bitfield.</param>
/// <param name="Discovered">Whether the location is discovered.</param>
public sealed record DaggerfallLocationMap(
    int Region,
    int Index,
    string Name,
    int MapId,
    int Longitude,
    int Latitude,
    byte DungeonType,
    int LocationType,
    bool Discovered);

/// <summary>
/// A region group the donor discards because a table has no bytes, with the tables that are empty.
/// </summary>
/// <param name="Region">The region index the table names carry.</param>
/// <param name="EmptyTables">The tables that carry no bytes, named.</param>
/// <param name="Reason">Why the region has no locations to publish.</param>
public sealed record DaggerfallRegionGap(int Region, IReadOnlyList<string> EmptyTables, string Reason);

/// <summary>
/// A dungeon location the publication could not read, kept explicit rather than dropped.
/// </summary>
/// <param name="Region">The source region index.</param>
/// <param name="Index">The location's ordinal in the region's names table.</param>
/// <param name="Name">The location's exact name.</param>
/// <param name="Reason">Why its records could not be read.</param>
public sealed record DaggerfallDungeonGap(int Region, int Index, string Name, string Reason);

/// <summary>
/// One table a region group carries: its name, its ordinal in the group, and what it declared.
/// </summary>
/// <param name="Name">The table's name as the record carries it, which is where the region index lives.</param>
/// <param name="Ordinal">The table's ordinal in the donor's own read order.</param>
/// <param name="Length">How many payload bytes it carries.</param>
/// <param name="DeclaredRecords">How many records it declares, which its length must account for.</param>
/// <param name="State">Whether it read, disagreed, or carries no bytes.</param>
public sealed record DaggerfallRegionTable(string Name, int Ordinal, int Length, int DeclaredRecords, string State);

/// <summary>
/// One region's table provenance: which tables it has and what each of them declared.
/// </summary>
/// <param name="Region">The source region index.</param>
/// <param name="Tables">Its tables, in the donor's read order.</param>
public sealed record DaggerfallRegionProvenance(int Region, IReadOnlyList<DaggerfallRegionTable> Tables);

/// <summary>One dungeon: the exterior location it pairs with, its identity, and its blocks.</summary>
/// <param name="Region">The source region index.</param>
/// <param name="Index">The location's ordinal in the region's names table.</param>
/// <param name="Name">The location's exact name.</param>
/// <param name="ExteriorLocationId">The exterior location the dungeon pairs with.</param>
/// <param name="DungeonLocationId">The dungeon record's own identity.</param>
/// <param name="Blocks">The block names the dungeon is built from, in record order.</param>
public sealed record DaggerfallDungeonRecord(
    int Region,
    int Index,
    string Name,
    uint ExteriorLocationId,
    uint DungeonLocationId,
    IReadOnlyList<string> Blocks);

/// <summary>
/// The published locations of every region, for the site and world consumers that place things on
/// them.
/// </summary>
/// <param name="SchemaVersion">Shape version of this section.</param>
/// <param name="Locations">Every location the corpus's regions describe.</param>
/// <param name="Dungeons">Every dungeon among them, with its block references.</param>
/// <param name="RegionsWithoutTables">Region groups the donor discards, with the tables that are empty named.</param>
/// <param name="DungeonsWithoutRecords">Dungeon locations whose records disagreed, with the reason.</param>
/// <param name="Regions">Every region's table provenance, so a fact can be traced to the table it came from.</param>
/// <param name="Sources">The source identity this section was read from.</param>
public sealed record DaggerfallLocations(
    int SchemaVersion,
    IReadOnlyList<DaggerfallLocationMap> Locations,
    IReadOnlyList<DaggerfallDungeonRecord> Dungeons,
    IReadOnlyList<DaggerfallRegionGap> RegionsWithoutTables,
    IReadOnlyList<DaggerfallDungeonGap> DungeonsWithoutRecords,
    IReadOnlyList<DaggerfallRegionProvenance> Regions,
    IReadOnlyList<string> Sources)
{
    public const int CurrentSchemaVersion = 1;

    public void Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion)
        {
            throw new InvalidOperationException($"Locations schema must be {CurrentSchemaVersion} but is {SchemaVersion}.");
        }

        // A location whose region or name is missing would resolve to nothing, and a dungeon whose
        // blocks are empty would claim a structure it does not describe.
        foreach (DaggerfallLocationMap location in Locations)
        {
            if (location.Region < 0) throw new InvalidOperationException($"Location '{location.Name}' carries region {location.Region}.");
            if (string.IsNullOrWhiteSpace(location.Name)) throw new InvalidOperationException($"Region {location.Region} location {location.Index} carries no name.");
            if (location.MapId < 0) throw new InvalidOperationException($"Location '{location.Name}' carries map {location.MapId}.");
        }

        HashSet<(int Region, int Index)> locations = [];
        foreach (DaggerfallLocationMap location in Locations)
        {
            // Two records claiming one region and index would collapse in the set below and quietly
            // leave one of them unreachable, so the collision is refused where it is created.
            if (!locations.Add((location.Region, location.Index)))
            {
                throw new InvalidOperationException($"Region {location.Region} carries two locations at index {location.Index}, so one of them would be unreachable.");
            }
        }

        // A region's provenance is the four tables the donor reads, named, so a later consumer traces a
        // value to its source instead of inferring it from an ordinal.
        foreach (DaggerfallRegionProvenance region in Regions)
        {
            if (region.Tables.Count != 4)
            {
                throw new InvalidOperationException($"Region {region.Region} records {region.Tables.Count} tables; a region group carries four.");
            }

            foreach (DaggerfallRegionTable table in region.Tables)
            {
                if (string.IsNullOrWhiteSpace(table.Name))
                {
                    throw new InvalidOperationException($"Region {region.Region} records a table with no name, so nothing says where its records came from.");
                }
            }
        }

        foreach (DaggerfallRegionGap gap in RegionsWithoutTables)
        {
            if (gap.EmptyTables.Count == 0)
            {
                throw new InvalidOperationException($"Region {gap.Region} is recorded as having no usable tables but names none of them.");
            }
        }

        foreach (DaggerfallDungeonGap gap in DungeonsWithoutRecords)
        {
            // A dungeon the publication could not read is a gap like a region's empty tables: dropping
            // it silently would leave a place that exists in the source and nowhere in the pack.
            if (!locations.Contains((gap.Region, gap.Index)))
            {
                throw new InvalidOperationException($"Dungeon gap '{gap.Name}' in region {gap.Region} names location {gap.Index}, which no location record carries.");
            }

            if (string.IsNullOrWhiteSpace(gap.Reason))
            {
                throw new InvalidOperationException($"Dungeon gap '{gap.Name}' carries no reason, so nothing says why it is missing.");
            }
        }

        foreach (DaggerfallDungeonRecord dungeon in Dungeons)
        {
            if (!locations.Contains((dungeon.Region, dungeon.Index)))
            {
                throw new InvalidOperationException($"Dungeon '{dungeon.Name}' in region {dungeon.Region} names location {dungeon.Index}, which no location record carries.");
            }

            if (dungeon.Blocks.Count == 0)
            {
                throw new InvalidOperationException($"Dungeon '{dungeon.Name}' carries no blocks, so it describes no structure.");
            }

            foreach (string block in dungeon.Blocks)
            {
                if (string.IsNullOrWhiteSpace(block))
                {
                    throw new InvalidOperationException($"Dungeon '{dungeon.Name}' names a block with no name, so it describes no structure.");
                }
            }
        }
    }
}

/// <summary>
/// Builds the published locations from the MAPS.BSA archive.
/// </summary>
/// <remarks>
/// This is the offline read: a region group the donor discards because one of its tables has no
/// bytes is recorded as having no tables rather than as a region with no locations, and a location
/// whose dungeon type is set but which no dungeon record links is published as a location with none,
/// which is what the donor's own lookup concludes.
/// </remarks>
public static class DaggerfallLocationBuilder
{
    public static DaggerfallLocations Build(BsaArchive archive)
    {
        ArgumentNullException.ThrowIfNull(archive);
        List<DaggerfallLocationMap> locations = [];
        List<DaggerfallDungeonRecord> dungeons = [];
        List<DaggerfallDungeonGap> dungeonGaps = [];
        List<DaggerfallRegionGap> withoutTables = [];
        List<DaggerfallRegionProvenance> regions = [];
        foreach (MapsRegionGroup group in MapsDecoder.DecodeRegionGroups(archive))
        {
            // Every region's tables are recorded whatever they hold, including the ones with no bytes:
            // a location's region and index locate its table, and this says what that table declared.
            regions.Add(new DaggerfallRegionProvenance(group.Region,
            [
                .. group.Tables.Select(table => new DaggerfallRegionTable(
                    table.Name,
                    table.Ordinal,
                    table.Length,
                    table.DeclaredRecords,
                    table.State.ToString())),
            ]));

            if (group.Tables.Any(table => table.Length == 0))
            {
                // Which tables are empty is the diagnosable fact; that a region has none to publish is
                // only its consequence.
                withoutTables.Add(new DaggerfallRegionGap(
                    group.Region,
                    [.. group.Tables.Where(table => table.Length == 0).Select(table => table.Name)],
                    "The donor's MapsFile discards a region whose tables are not all readable, so this region has no locations to publish."));
                continue;
            }

            foreach (MapsLocationRecord location in MapsDecoder.DecodeRegionLocations(archive, group.Region))
            {
                locations.Add(new DaggerfallLocationMap(
                    location.Region,
                    location.Index,
                    location.Name,
                    location.MapId,
                    location.Longitude,
                    location.Latitude,
                    location.DungeonType,
                    location.LocationType,
                    location.Discovered));
            }

            foreach (MapsDungeonLocation dungeon in MapsDecoder.DecodeRegionDungeons(archive, group.Region))
            {
                if (dungeon.State != MapsDungeonState.Read)
                {
                    // A no-dungeon location is an ordinary outcome and is not a gap; a record that
                    // disagreed is, and it is published with its reason.
                    if (dungeon.State == MapsDungeonState.Malformed)
                    {
                        dungeonGaps.Add(new DaggerfallDungeonGap(dungeon.Region, dungeon.Index, dungeon.Name, dungeon.Reason));
                    }

                    continue;
                }
                dungeons.Add(new DaggerfallDungeonRecord(
                    dungeon.Region,
                    dungeon.Index,
                    dungeon.Name,
                    dungeon.ExteriorLocationId,
                    dungeon.DungeonLocationId,
                    [.. dungeon.Blocks.Select(block => block.SourceName)]));
            }
        }

        DaggerfallLocations published = new(
            DaggerfallLocations.CurrentSchemaVersion,
            [.. locations.OrderBy(location => location.Region).ThenBy(location => location.Index)],
            [.. dungeons.OrderBy(dungeon => dungeon.Region).ThenBy(dungeon => dungeon.Index)],
            [.. withoutTables.OrderBy(gap => gap.Region)],
            [.. dungeonGaps.OrderBy(gap => gap.Region).ThenBy(gap => gap.Index)],
            [.. regions.OrderBy(region => region.Region)],
            [archive.Source]);
        published.Validate();
        return published;
    }
}
