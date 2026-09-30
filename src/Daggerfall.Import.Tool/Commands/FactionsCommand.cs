using Daggerfall.Import.Normalized;

namespace Daggerfall.Import.Tool.Commands;

/// <summary>
/// Reads the classic faction file into the imported payload when asked, so a region, temple, guild or court
/// resolves to the filed relations and bindings the source states rather than to policy inferred from them.
/// Reputation, rank and service behavior stay out: the catalog publishes filed values, and the social
/// consumers that read them own what those values do.
/// </summary>
internal static class FactionsCommand
{
    public static ToolCommand Command { get; } = new("factions", [Options.Arena2, Options.Pack, Options.Inventory, Options.Update], Run);

    private static int Run(CommandArguments args)
    {
        string path = Path.Combine(args["--arena2"], "FACTION.TXT");
        DaggerfallFactions factions = DaggerfallFactionsBuilder.Build(File.ReadAllText(path), Options.Arena2Label("FACTION.TXT"), File.ReadAllBytes(path), Options.ReadInventory(args));
        Console.WriteLine($"factions: {factions.Factions.Count} records, {factions.Regions.Count(region => region.Disposition == DaggerfallRegionFactionDisposition.Claimed)} claimed regions, {factions.DuplicateNames.Count} duplicated names");
        foreach (DaggerfallFactionNameAlias alias in factions.DuplicateNames)
        {
            Console.WriteLine($"  {alias.Name}: [{string.Join(", ", alias.Ids)}] resolves to {alias.ResolvedId}");
        }

        if (!args.Switch("--update")) return Options.ReportOnly("these factions");
        PayloadFiles.WriteSection(args["--pack"], "factions", factions);
        return 0;
    }
}
