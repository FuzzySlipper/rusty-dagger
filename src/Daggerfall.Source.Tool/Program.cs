using Daggerfall.Source.Tool.Commands;

namespace Daggerfall.Source.Tool;

/// <summary>
/// The source tool: reference sets for image generation, reports that reconcile the classic sources with the
/// documented inventory, and the art pack's ARCH3D remodel meshes. Nothing here feeds the data containers; the
/// import and its steps are <c>Daggerfall.Import.Tool</c>'s.
/// </summary>
internal static class Program
{
    internal const string Tool = "daggerfall-source-tool";

    internal static readonly IReadOnlyDictionary<string, ToolCommand> Commands = new ToolCommand[]
    {
        // Reports and reconciliation against the documented inventory.
        SourceInventoryCommand.Command,
        MonsterArchiveCommand.Command,
        TextureLeavesCommand.Command,
        ResidualPathsCommand.Command,
        MapArtCommand.Command,
        // The art pack's ARCH3D remodels, written and held to the classic's volume at promotion.
        ModelMeshCommand.Command,
        // Local image-generation reference sets (never product content).
        ReferenceExportCommands.Items,
        ReferenceExportCommands.Spells,
        ReferenceExportCommands.PaperDoll,
        ReferenceExportCommands.Effects,
        ReferenceExportCommands.Surfaces,
        ReferenceExportCommands.Flats,
        ReferenceExportCommands.People,
        ReferenceExportCommands.Terrain,
        ReferenceExportCommands.DirectionalSprites,
    }.ToDictionary(command => command.Name, StringComparer.Ordinal);

    internal static int Main(string[] args) => ToolCommand.Dispatch(Commands, args, Tool,
        $"usage: {Tool} <{string.Join('|', Commands.Keys)}> [options]; each command prints its own options when they are wrong.");
}
