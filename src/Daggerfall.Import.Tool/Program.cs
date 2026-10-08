using System.Text.Json;
using Daggerfall.Import.Tool.Commands;

namespace Daggerfall.Import.Tool;

/// <summary>
/// The import tool: one verb per command, each in its own file under <c>Commands/</c>, all parsed by the one
/// option grammar in <see cref="CommandArguments"/>. <c>scripts/regenerate-content.sh</c> runs them in the
/// importer's dependency order.
/// </summary>
internal static class Program
{
    internal static readonly IReadOnlyDictionary<string, ToolCommand> Commands = new ToolCommand[]
    {
        // Product-wide media.
        MusicMediaCommand.Command,
        ClassicMediaCommand.Command,
        SkyMediaCommand.Command,
        CharacterPresentationCommand.Command,
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
    }.ToDictionary(command => command.Name, StringComparer.Ordinal);

    internal static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || !Commands.TryGetValue(args[0], out ToolCommand? command))
            {
                throw new ArgumentException($"usage: daggerfall-import-tool <{string.Join('|', Commands.Keys)}> [options]; each command prints its own options when they are wrong.");
            }

            return command.Invoke(args);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException or InvalidDataException or UnauthorizedAccessException or FormatException or JsonException)
        {
            Console.Error.WriteLine($"daggerfall-import-tool: {exception.Message}");
            return 1;
        }
    }
}
