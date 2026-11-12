using Daggerfall.Import.Arena2;
using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Tool.Commands;

/// <summary>
/// Publishes the donor's static mobile table into the imported payload as one normalized record per mobile,
/// every mobile with its parameters and identity. Which authored actor a mobile is, and so its disposition, is
/// joined by the ruleset when it loads, so the import never reads the authored payload.
/// </summary>
internal static class MobileCatalogCommand
{
    public static ToolCommand Command { get; } = new("mobile-catalog",
        [CommandOption.Required("--donor", "ENEMY_BASICS.cs"), Options.Pack, CommandOption.Required("--archive", "MONSTER.BSA"), Options.Update], Run);

    private static int Run(CommandArguments args)
    {
        string donorFile = args["--donor"];
        string archive = args["--archive"];
        if (!File.Exists(donorFile)) throw new FileNotFoundException($"The donor's static mobile table is required to publish mobile parameters and is not at '{donorFile}'.", donorFile);
        if (!File.Exists(archive)) throw new FileNotFoundException($"A mobile's career attack and tolerance flags live in its MONSTER.BSA configuration record and is not at '{archive}'.", archive);
        // Attack and resistance flags live in each mobile's ENEMY###.CFG career record.
        byte[] archiveBytes = File.ReadAllBytes(archive);
        MonsterArchiveInventory enemyConfigurations = MonsterArchiveInventory.Enumerate(archiveBytes, Path.GetFileName(archive));
        Arena2MobileCatalogPublication publication = Arena2MobileCatalogDocument.Build(
            File.ReadAllText(donorFile), PublishedSourcePath.Donor("Assets/Scripts/Utility/EnemyBasics.cs"), enemyConfigurations,
            PublishedSource.Of(Options.Arena2Label("MONSTER.BSA"), archiveBytes));
        Console.WriteLine($"mobile catalog: {publication.Mobiles} donor mobiles");
        if (!args.Switch("--update")) return Options.ReportOnly("this catalog");
        PayloadFiles.WriteSections(args["--pack"], new Dictionary<string, string>(StringComparer.Ordinal) { ["mobiles"] = publication.Json });
        return 0;
    }
}
