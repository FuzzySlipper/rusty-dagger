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
/// <param name="Sources">The source identity this section was read from.</param>
public sealed record DaggerfallLocations(
    int SchemaVersion,
    IReadOnlyList<DaggerfallLocationMap> Locations,
    IReadOnlyList<DaggerfallDungeonRecord> Dungeons,
    IReadOnlyList<DaggerfallRegionGap> RegionsWithoutTables,
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

        HashSet<(int Region, int Index)> locations = [.. Locations.Select(location => (location.Region, location.Index))];
        foreach (DaggerfallRegionGap gap in RegionsWithoutTables)
        {
            if (gap.EmptyTables.Count == 0)
            {
                throw new InvalidOperationException($"Region {gap.Region} is recorded as having no usable tables but names none of them.");
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
        List<DaggerfallRegionGap> withoutTables = [];
        foreach (MapsRegionGroup group in MapsDecoder.DecodeRegionGroups(archive))
        {
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
                if (dungeon.State != MapsDungeonState.Read) continue;
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
            [archive.Source]);
        published.Validate();
        return published;
    }
}
