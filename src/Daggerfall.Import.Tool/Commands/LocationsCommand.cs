using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalized;

namespace Daggerfall.Import.Tool.Commands;

/// <summary>
/// Reads every region's locations and dungeons from MAPS.BSA, joins each MAPPITEM to its RMB/FLD facts from
/// BLOCKS.BSA, reads each location's climate from CLIMATE.PAK (and its dungeon's classic texture table from that
/// climate), and writes the location contract when asked: the locations section of the imported payload, and each
/// exterior location's detail record and ground file under the content root (<see cref="DaggerfallLocationDetails"/>).
/// </summary>
internal static class LocationsCommand
{
    public static ToolCommand Command { get; } = new("locations", [Options.Arena2, Options.Pack, Options.ContentRoot, Options.Update], Run);

    private static int Run(CommandArguments args)
    {
        string arena2 = args["--arena2"];
        BsaArchive archive = BsaArchive.Parse(File.ReadAllBytes(Path.Combine(arena2, "MAPS.BSA")), "arena2/MAPS.BSA");
        BsaArchive blocks = BsaArchive.Parse(File.ReadAllBytes(Path.Combine(arena2, "BLOCKS.BSA")), "arena2/BLOCKS.BSA");
        BsaArchive models = BsaArchive.Parse(File.ReadAllBytes(Path.Combine(arena2, "ARCH3D.BSA")), "arena2/ARCH3D.BSA");
        PakMap climate = PakDecoder.Decode(File.ReadAllBytes(Path.Combine(arena2, "CLIMATE.PAK")), "arena2/CLIMATE.PAK");
        DaggerfallLocations locations = DaggerfallLocationBuilder.Build(archive, blocks, models, climate);

        Console.WriteLine($"locations: {locations.Locations.Count} locations over {locations.Locations.Select(location => location.Region).Distinct().Count()} regions, {locations.Dungeons.Count} dungeons, {locations.Locations.Count(location => location.Exterior is not null)} exterior metadata records, {locations.RegionsWithoutTables.Count} regions without usable tables");
        foreach (IGrouping<string, DaggerfallRegionGap> gap in locations.RegionsWithoutTables.GroupBy(gap => string.Join('+', gap.EmptyTables.Select(name => name[..name.IndexOf('.', StringComparison.Ordinal)]))))
        {
            Console.WriteLine($"  {gap.Count()} regions have no {gap.Key}");
        }

        foreach (DaggerfallLocationMap location in locations.Locations.Where(location => location.Exterior?.MissingCityBuildings.Count > 0))
            Console.WriteLine($"  warning: location {location.Region}/{location.Index}: {string.Join("; ", location.Exterior!.MissingCityBuildings)}");

        if (!args.Switch("--update")) return Options.ReportOnly("these locations");
        // The directory holds only what this run writes, so a location a later corpus no longer carries leaves no detail behind.
        string root = Path.GetFullPath(args[Options.ContentRoot.Name]);
        string details = Path.Combine(root, locations.Details);
        if (Directory.Exists(details)) Directory.Delete(details, recursive: true);
        int files = 0;
        foreach ((string path, byte[] bytes) in DaggerfallLocationDetails.Files(locations))
        {
            PayloadFiles.WriteFile(Path.Combine(root, path), bytes);
            files++;
        }

        Console.WriteLine($"locations: {files} detail and ground files written under {details}");
        PayloadFiles.WriteSection(args["--pack"], "locations", locations);
        return 0;
    }
}
