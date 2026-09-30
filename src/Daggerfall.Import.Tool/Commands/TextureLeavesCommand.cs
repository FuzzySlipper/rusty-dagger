using Daggerfall.Import.Arena2;
using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Tool.Commands;

/// <summary>
/// Enumerates the supplied texture leaves, reports what every documented id holds, and — when the
/// documented inventory is supplied — checks that the two agree.
/// </summary>
internal static class TextureLeavesCommand
{
    public static ToolCommand Command { get; } = new("texture-leaves",
        [Options.Arena2, CommandOption.Optional("--inventory", "CSV")], Run);

    private static int Run(CommandArguments args)
    {
        TextureLeafInventory inventory = Arena2SiteSources.ReadTextureLeaves(args["--arena2"]);
        Console.WriteLine($"texture leaves: {inventory.Decoded.Count()} decoded, {inventory.Malformed.Count()} supplied and unreadable, {inventory.NotSupplied.Count()} not supplied, {inventory.Records} records, {inventory.Frames} frames");
        foreach (TextureLeafRecord leaf in inventory.Malformed)
        {
            Console.WriteLine($"  unreadable {leaf.Path}: {leaf.Note}");
        }

        if (!args.Has("--inventory")) return 0;
        return Options.CheckDocumentedFamily(
            Options.DocumentedFiles(Options.ReadInventory(args), row => row.FamilyId == "CNT-018"),
            [.. inventory.Leaves.Where(leaf => leaf.Path.Length != 0).Select(leaf => leaf.Path)],
            $"texture leaves ({inventory.NotSupplied.Count()} not supplied)");
    }
}
