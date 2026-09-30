using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalized;

namespace Daggerfall.Import.Tool.Commands;

/// <summary>
/// Reads every region's locations and dungeons from MAPS.BSA, joins each MAPPITEM to its RMB/FLD facts from
/// BLOCKS.BSA, and writes the complete location contract into the imported payload when asked.
/// </summary>
internal static class LocationsCommand
{
    public static ToolCommand Command { get; } = new("locations", [Options.Arena2, Options.Pack, Options.Update], Run);

    private static int Run(CommandArguments args)
    {
        string arena2 = args["--arena2"];
        BsaArchive archive = BsaArchive.Parse(File.ReadAllBytes(Path.Combine(arena2, "MAPS.BSA")), "arena2/MAPS.BSA");
        BsaArchive blocks = BsaArchive.Parse(File.ReadAllBytes(Path.Combine(arena2, "BLOCKS.BSA")), "arena2/BLOCKS.BSA");
        DaggerfallLocations locations = DaggerfallLocationBuilder.Build(archive, blocks);

        Console.WriteLine($"locations: {locations.Locations.Count} locations over {locations.Locations.Select(location => location.Region).Distinct().Count()} regions, {locations.Dungeons.Count} dungeons, {locations.Locations.Count(location => location.Exterior is not null)} exterior metadata records, {locations.RegionsWithoutTables.Count} regions without usable tables");
        foreach (IGrouping<string, DaggerfallRegionGap> gap in locations.RegionsWithoutTables.GroupBy(gap => string.Join('+', gap.EmptyTables.Select(name => name[..name.IndexOf('.', StringComparison.Ordinal)]))))
        {
            Console.WriteLine($"  {gap.Count()} regions have no {gap.Key}");
        }

        if (!args.Switch("--update")) return Options.ReportOnly("these locations");
        PayloadFiles.WriteSection(args["--pack"], "locations", locations);
        return 0;
    }
}
