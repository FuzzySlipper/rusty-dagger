using Daggerfall.Source.Tool.Commands;

namespace Daggerfall.Source.Tool;

/// <summary>
/// The source tool: reference sets for image generation, the developer command listing, the art pack's ARCH3D remodel meshes, and the baked world variants and terrain relief a pack commits. Nothing here
/// feeds the data containers; the import and its steps are <c>Daggerfall.Import.Tool</c>'s.
/// </summary>
internal static class Program
{
    internal const string Tool = "daggerfall-source-tool";

    internal static readonly IReadOnlyDictionary<string, ToolCommand> Commands = new ToolCommand[]
    {
        // The art pack's ARCH3D remodels, written and held to the classic's volume at promotion.
        ModelMeshCommand.Command,
        // A content pack's world variants, baked as block documents with their collision and navigation for the pack.
        WorldVariantBakeCommand.Command,
        // The base pack's terrain relief and rivers, baked from WOODS.WLD for the pack to commit.
        ReliefBakeCommand.Command,
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
        // The developer commands and their descriptions, read from the Kit's compiled attributes.
        DebugCommandsCommand.Command,
    }.ToDictionary(command => command.Name, StringComparer.Ordinal);

    internal static int Main(string[] args) => ToolCommand.Dispatch(Commands, args, Tool,
        $"usage: {Tool} <{string.Join('|', Commands.Keys)}> [options]; each command prints its own options when they are wrong.");
}
