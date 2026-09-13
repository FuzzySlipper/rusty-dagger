using Daggerfall.Import.Arena2;
using Xunit;

namespace Daggerfall.Import.Tests;

/// <summary>The region groups MAPS.BSA carries and whether each group's four tables agree.</summary>
public sealed class MapsRegionGroupTests
{
    [Fact]
    public void Every_region_group_carries_the_four_tables_with_one_map_entry_per_location()
    {
        BsaArchive archive = BsaArchive.Parse(File.ReadAllBytes(Corpus("MAPS.BSA")), "arena2/MAPS.BSA");
        IReadOnlyList<MapsRegionGroup> groups = MapsDecoder.DecodeRegionGroups(archive);

        // The corpus's own region count, discovered from the records rather than assumed.
        Assert.Equal(62, groups.Count);
        Assert.Equal(Enumerable.Range(0, 62), groups.Select(group => group.Region));

        foreach (MapsRegionGroup group in groups)
        {
            // The four tables the donor reads, and every region group carries all four.
            Assert.Equal(MapsDecoder.RegionTables, group.Tables.Select(table => table.Name[..table.Name.LastIndexOf('.')]));
            Assert.All(group.Tables, table => Assert.NotEqual(MapsTableState.Missing, table.State));
            Assert.All(group.Tables, table => Assert.True(table.Ordinal >= 0));
            Assert.NotEqual(MapsTableState.Malformed, group.Tables.Single(table => table.Name.StartsWith("MAPDITEM", StringComparison.Ordinal)).State);

            // A table with no bytes means the donor discards the region, so those regions are reported
            // as empty with the donor's own rule rather than as zero-location regions.
            bool empty = group.Tables.Any(table => table.Length == 0);
            if (empty)
            {
                Assert.Contains(group.Tables, table => table.State == MapsTableState.Empty && table.Reason.Contains("discards the region", StringComparison.Ordinal));
                continue;
            }

            // Otherwise a map table entry is one location, so the region's declared location count and
            // the map table's own length are a real cross-check rather than two independent claims.
            MapsRegionTable names = group.Tables.Single(table => table.Name.StartsWith("MAPNAMES", StringComparison.Ordinal));
            MapsRegionTable maps = group.Tables.Single(table => table.Name.StartsWith("MAPTABLE", StringComparison.Ordinal));
            Assert.True(names.State == MapsTableState.Read, $"region {group.Region} MAPNAMES: {names.Reason}");
            Assert.True(maps.State == MapsTableState.Read, $"region {group.Region} MAPTABLE: {maps.Reason}");
            Assert.True(maps.DeclaredRecords >= names.DeclaredRecords, $"region {group.Region}: {maps.Reason}");
        }
    }

    [Fact]
    public void A_table_that_disagrees_with_its_region_is_reported_rather_than_used()
    {
        // A region whose map table is one entry short of its names is malformed with both values
        // named; a region missing its exterior table reports the absence instead of dropping it.
        byte[] maps = MapNames(3);
        byte[] short_ = new byte[(3 * 17) - 1];
        byte[] archiveBytes = Archive(
            ("MAPNAMES.000", maps),
            ("MAPTABLE.000", short_),
            ("MAPDITEM.000", new byte[4]));
        IReadOnlyList<MapsRegionGroup> groups = MapsDecoder.DecodeRegionGroups(BsaArchive.Parse(archiveBytes, "fixture"));

        MapsRegionGroup group = Assert.Single(groups);
        MapsRegionTable missing = group.Tables.Single(table => table.Name.StartsWith("MAPPITEM", StringComparison.Ordinal));
        Assert.Equal(MapsTableState.Missing, missing.State);
        Assert.Contains("does not carry this table", missing.Reason, StringComparison.Ordinal);

        MapsRegionTable mapsTable = group.Tables.Single(table => table.Name.StartsWith("MAPTABLE", StringComparison.Ordinal));
        Assert.Equal(MapsTableState.Malformed, mapsTable.State);
        Assert.Contains("not a whole number", mapsTable.Reason, StringComparison.Ordinal);

        // And a whole number of entries that still disagrees with the names is caught by the count.
        IReadOnlyList<MapsRegionGroup> mismatched = MapsDecoder.DecodeRegionGroups(BsaArchive.Parse(
            Archive(("MAPNAMES.000", MapNames(3)), ("MAPTABLE.000", new byte[2 * 17])), "fixture"));
        MapsRegionTable disagreeing = mismatched.Single().Tables.Single(table => table.Name.StartsWith("MAPTABLE", StringComparison.Ordinal));
        Assert.Equal(MapsTableState.Malformed, disagreeing.State);
        Assert.Contains("cannot be addressed", disagreeing.Reason, StringComparison.Ordinal);

        // A zero-length table is the donor's own unreadable-region case, not a corrupt file.
        IReadOnlyList<MapsRegionGroup> blank = MapsDecoder.DecodeRegionGroups(BsaArchive.Parse(
            Archive(("MAPNAMES.000", MapNames(3)), ("MAPTABLE.000", new byte[3 * 17]), ("MAPDITEM.000", [])), "fixture"));
        MapsRegionTable empty = blank.Single().Tables.Single(table => table.Name.StartsWith("MAPDITEM", StringComparison.Ordinal));
        Assert.Equal(MapsTableState.Empty, empty.State);
        Assert.Contains("discards the region", empty.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Reads_every_location_a_region_describes_with_its_map_and_position()
    {
        BsaArchive archive = BsaArchive.Parse(File.ReadAllBytes(Corpus("MAPS.BSA")), "arena2/MAPS.BSA");
        IReadOnlyList<MapsRegionGroup> groups = MapsDecoder.DecodeRegionGroups(archive);

        int total = 0;
        foreach (MapsRegionGroup group in groups)
        {
            // A region that the donor discards for a zero-length table has nothing to read; every
            // other region yields one record per name, indexed from zero and preserving its region.
            if (group.Tables.Any(table => table.Length == 0)) continue;
            IReadOnlyList<MapsLocationRecord> locations = MapsDecoder.DecodeRegionLocations(archive, group.Region);
            MapsRegionTable names = group.Tables.Single(table => table.Name.StartsWith("MAPNAMES", StringComparison.Ordinal));
            Assert.Equal(names.DeclaredRecords, locations.Count);
            Assert.Equal(Enumerable.Range(0, locations.Count), locations.Select(location => location.Index));
            Assert.All(locations, location => Assert.Equal(group.Region, location.Region));
            Assert.All(locations, location => Assert.False(string.IsNullOrWhiteSpace(location.Name)));
            Assert.All(locations, location => Assert.True(location.MapId >= 0));
            Assert.All(locations, location => Assert.InRange(location.Longitude, 0, 0x1F_FFFF >> 8));
            Assert.All(locations, location => Assert.InRange(location.Latitude, 0, 0x00FF_FFFF >> 8));
            total += locations.Count;

            // The same read twice is the same records: the decode is deterministic.
            Assert.Equal(locations, MapsDecoder.DecodeRegionLocations(archive, group.Region));
        }

        // The corpus's sixty-two regions describe fifteen thousand two hundred and fifty-one locations
        // between them, measured from the tables rather than assumed: the number moves if the source
        // does, and a test that asserts a guess would only ever fail.
        Assert.Equal(15251, total);
    }

    private static byte[] MapNames(int count)
    {
        byte[] bytes = new byte[sizeof(uint) + (count * 32)];
        BitConverter.GetBytes((uint)count).CopyTo(bytes, 0);
        for (int index = 0; index < count; index++)
        {
            System.Text.Encoding.ASCII.GetBytes($"Location {index}").CopyTo(bytes, sizeof(uint) + (index * 32));
        }

        return bytes;
    }

    /// <summary>
    /// A named-record BSA as this repository parses it: a count and directory type, the payloads in
    /// order, then a directory of 14-byte names and their lengths at the end of the file.
    /// </summary>
    private static byte[] Archive(params (string Name, byte[] Payload)[] records)
    {
        const int nameBytes = 14;
        const int entryBytes = nameBytes + sizeof(int);
        List<byte> bytes = [];
        bytes.AddRange(BitConverter.GetBytes((short)records.Length));
        bytes.AddRange(BitConverter.GetBytes(Arena2FormatConstants.NamedBsaDirectoryType));
        foreach ((string _, byte[] payload) in records)
        {
            bytes.AddRange(payload);
        }

        foreach ((string name, byte[] payload) in records)
        {
            byte[] nameField = new byte[nameBytes];
            System.Text.Encoding.ASCII.GetBytes(name).CopyTo(nameField, 0);
            bytes.AddRange(nameField);
            bytes.AddRange(BitConverter.GetBytes(payload.Length));
        }

        _ = entryBytes;
        return [.. bytes];
    }

    private static string Corpus(string name) => Path.Combine(RepositoryRoot(), "local/arena2", name);

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("repository root not found");
    }
}
