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
        [CommandOption.Required("--donor", "ENEMY_BASICS.cs"), Options.Authored, Options.Pack, CommandOption.Optional("--archive", "MONSTER.BSA"), Options.Update], Run);

    private static int Run(CommandArguments args)
    {
        string donorFile = args["--donor"];
        string? archive = args.Optional("--archive");
        if (!File.Exists(donorFile)) throw new FileNotFoundException($"The donor's static mobile table is required to publish mobile parameters and is not at '{donorFile}'.", donorFile);
        if (archive is not null && !File.Exists(archive)) throw new FileNotFoundException($"A mobile's career attack-modifier byte lives in its MONSTER.BSA configuration record and is not at '{archive}'.", archive);
        // The attack-modifier byte is not in the donor's table; it lives in each mobile's ENEMY###.CFG
        // career record. Without the archive the catalog publishes zero flags for every mobile.
        MonsterArchiveInventory? enemyConfigurations = archive is null
            ? null
            : MonsterArchiveInventory.Enumerate(File.ReadAllBytes(archive), Path.GetFileName(archive));
        // The published actors are authored, so the identities the catalog joins come from the authored payload.
        Arena2MobileCatalogPublication publication = Arena2MobileCatalogDocument.Build(
            File.ReadAllText(donorFile), PayloadFiles.ReadAuthoredText(args["--authored"]), PublishedSourcePath.Donor("Assets/Scripts/Utility/EnemyBasics.cs"), enemyConfigurations);
        Console.WriteLine($"mobile catalog: {publication.Mobiles} donor mobiles, {publication.Published} published, {publication.HumanMobiles} human mobiles, {publication.Unpublished} unpublished");
        if (!args.Switch("--update")) return Options.ReportOnly("this catalog");
        PayloadFiles.WriteSections(args["--pack"], new Dictionary<string, string>(StringComparer.Ordinal) { ["mobiles"] = publication.Json });
        return 0;
    }
}
