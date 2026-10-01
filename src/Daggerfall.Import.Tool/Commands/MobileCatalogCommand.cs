using Daggerfall.Import.Arena2;
using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Tool.Commands;

/// <summary>
/// Publishes the donor's static mobile table into the imported payload as one normalized record per mobile.
/// The donor's table is the parameter authority and the pack is the identity authority; a mobile the pack
/// does not publish keeps its parameters and states its disposition rather than disappearing.
/// </summary>
internal static class MobileCatalogCommand
{
    public static ToolCommand Command { get; } = new("mobile-catalog",
        [CommandOption.Required("--donor", "ENEMY_BASICS.cs"), Options.Authored, Options.Pack, CommandOption.Required("--archive", "MONSTER.BSA"), Options.Update], Run);

    private static int Run(CommandArguments args)
    {
        string donorFile = args["--donor"];
        string archive = args["--archive"];
        if (!File.Exists(donorFile)) throw new FileNotFoundException($"The donor's static mobile table is required to publish mobile parameters and is not at '{donorFile}'.", donorFile);
        if (!File.Exists(archive)) throw new FileNotFoundException($"A mobile's career attack and tolerance flags live in its MONSTER.BSA configuration record and is not at '{archive}'.", archive);
        // Attack and resistance flags live in each mobile's ENEMY###.CFG career record.
        MonsterArchiveInventory enemyConfigurations = MonsterArchiveInventory.Enumerate(File.ReadAllBytes(archive), Path.GetFileName(archive));
        // The published actors are authored, so the identities the catalog joins come from the authored payload.
        Arena2MobileCatalogPublication publication = Arena2MobileCatalogDocument.Build(
            File.ReadAllText(donorFile), PayloadFiles.ReadAuthoredText(args["--authored"]), PublishedSourcePath.Donor("Assets/Scripts/Utility/EnemyBasics.cs"), enemyConfigurations);
        Console.WriteLine($"mobile catalog: {publication.Mobiles} donor mobiles, {publication.Published} published, {publication.HumanMobiles} human mobiles, {publication.Unpublished} unpublished");
        if (!args.Switch("--update")) return Options.ReportOnly("this catalog");
        PayloadFiles.WriteSections(args["--pack"], new Dictionary<string, string>(StringComparer.Ordinal) { ["mobiles"] = publication.Json });
        return 0;
    }
}
