using Daggerfall.Import.Arena2;
using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Tool.Commands;

/// <summary>
/// Publishes our mobile table (taken from Daggerfall Unity's static table) into the imported payload as one normalized record per mobile,
/// every mobile with its parameters and identity. Which authored actor a mobile is, and so its disposition, is
/// joined by the ruleset when it loads, so the import never reads the authored payload.
/// </summary>
internal static class MobileCatalogCommand
{
    public static ToolCommand Command { get; } = new("mobile-catalog",
        [CommandOption.Staged("--mobiles", "MOBILES.json", $"{ImportLayout.Inputs}/mobiles.json"), Options.Pack, CommandOption.Required("--archive", "MONSTER.BSA"), Options.Update], Run);

    private static int Run(CommandArguments args)
    {
        string mobiles = args["--mobiles"];
        string archive = args["--archive"];
        if (!File.Exists(archive)) throw new FileNotFoundException($"A mobile's career attack and tolerance flags live in its MONSTER.BSA configuration record and is not at '{archive}'.", archive);
        // Attack and resistance flags live in each mobile's ENEMY###.CFG career record.
        byte[] archiveBytes = File.ReadAllBytes(archive);
        MonsterArchiveInventory enemyConfigurations = MonsterArchiveInventory.Enumerate(archiveBytes, Path.GetFileName(archive));
        Arena2MobileCatalogPublication publication = Arena2MobileCatalogDocument.Build(
            TakenTable.Read<MobileTable>(File.ReadAllBytes(mobiles), mobiles), enemyConfigurations,
            PublishedSource.Of(Options.Arena2Label("MONSTER.BSA"), archiveBytes));
        Console.WriteLine($"mobile catalog: {publication.Mobiles} mobiles");
        if (!args.Switch("--update")) return Options.ReportOnly("this catalog");
        PayloadFiles.WriteSections(args["--pack"], new Dictionary<string, string>(StringComparer.Ordinal) { ["mobiles"] = publication.Json });
        return 0;
    }
}
