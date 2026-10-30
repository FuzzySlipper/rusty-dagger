using System.Text.Json;
using Daggerfall.Import.Tool.Commands;

namespace Daggerfall.Import.Tool;

/// <summary>
/// The import tool: one verb per command, each in its own file under <c>Commands/</c>, all parsed by the one
/// option grammar in <see cref="CommandArguments"/>. <c>import</c> runs them in the importer's dependency order
/// and packs the data containers; <c>pack</c> repacks them after one command was rerun.
/// </summary>
internal static class Program
{
    internal static readonly IReadOnlyDictionary<string, ToolCommand> Commands = new ToolCommand[]
    {
        // The whole import into the data containers, and repacking them.
        ImportCommand.Command,
        PackCommand.Command,
        // Product-wide media.
        MusicTracksCommand.Command,
        ClassicMediaCommand.Command,
        SkyMediaCommand.Command,
        CharacterPresentationCommand.Command,
        PaperDollMediaCommand.Command,
        CinematicMediaCommand.Command,
        // The imported payload's sections, the block document and the import records.
        CatalogsCommand.Command,
        ItemTemplateLedgerCommand.Command,
        LocationsCommand.Command,
        MagicCatalogCommand.Command,
        MobileCatalogCommand.Command,
        TextCommand.Command,
        InternalStringsCommand.Command,
        BlocksCommand.Command,
        GeometryCommand.Command,
        ClimateCommand.Command,
        FactionsCommand.Command,
        TerrainCommand.Command,
        ItemsCommand.Command,
        QuestsCommand.Command,
        VideosCommand.Command,
        BuildingNameInputsCommand.Command,
        // Quest corpus payloads.
        FightersQuestCorpusCommand.Command,
        ClassicQuestCorporaCommand.Command,
        // The product-wide world media the site closures reference, then the site closures.
        WorldMediaCommand.Command,
        WorldBlocksCommand.Command,
        DungeonSiteCommand.Write,
        DungeonSiteCommand.Plan,
        DungeonSiteCommand.VerifyRealData,
        RmbSpatialCommand.Command,
        // Reports and reconciliation against the documented inventory.
        SourceManifestCommand.Command,
        SourceCoverageCommand.Command,
        MobileLedgerCommand.Command,
        MonsterArchiveCommand.Command,
        QuestSourcesCommand.Command,
        TextureLeavesCommand.Command,
        ResidualPathsCommand.Command,
        MapArtCommand.Command,
        // Sprite inspection and authored overlays.
        SpriteListCommand.Command,
        SpriteShowCommand.Command,
        SpriteInspectionCommand.Command,
        SpriteOverlayValidateCommand.Command,
        SpriteOverlayWriteCommand.Command,
        SpriteOverlayDiscardCommand.Command,
        // Local image-generation reference sets (never product content).
        ReferenceExportCommands.Items,
        ReferenceExportCommands.Spells,
        ReferenceExportCommands.PaperDoll,
        ReferenceExportCommands.Effects,
    }.ToDictionary(command => command.Name, StringComparer.Ordinal);

    internal static int Main(string[] args)
    {
        string[] command = Release(args);
        int status = Run(command);
        // A player who dropped a folder onto the tool on Windows gets a console of its own, which closes on exit.
        // The game runs the tool with its command spelled out and no console, so it never waits there.
        if (command != args && OperatingSystem.IsWindows() && !Console.IsInputRedirected)
        {
            Console.WriteLine("Press Enter to close.");
            Console.ReadLine();
        }

        return status;
    }

    /// <summary>
    /// In a release, a folder named first is an import of it, so a player can run the tool on their Daggerfall
    /// folder, or drop the folder onto it, without naming the command.
    /// </summary>
    internal static string[] Release(string[] args) =>
        ImportLayout.ReleaseDirectory is not null && args.Length != 0 && !Commands.ContainsKey(args[0]) && !args[0].StartsWith('-')
            ? ["import", "--arena2", args[0], .. args[1..]]
            : args;

    private static int Run(string[] args)
    {
        try
        {
            if (args.Length == 0 || !Commands.TryGetValue(args[0], out ToolCommand? command))
            {
                throw new ArgumentException(ImportLayout.ReleaseDirectory is null
                    ? $"usage: daggerfall-import-tool <{string.Join('|', Commands.Keys)}> [options]; each command prints its own options when they are wrong."
                    : "usage: Daggerfall.Import.Tool <the folder Daggerfall is installed in, or its ARENA2 folder> [--music <a folder of the game's music recordings>]");
            }

            return command.Invoke(args);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException or InvalidDataException or UnauthorizedAccessException or FormatException or JsonException or Rusty.Engine.EngineCallException)
        {
            Console.Error.WriteLine($"daggerfall-import-tool: {exception.Message}");
            return 1;
        }
    }
}
