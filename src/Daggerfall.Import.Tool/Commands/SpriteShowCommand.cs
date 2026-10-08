using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Tool.Commands;

/// <summary>Prints one sprite inspection entry by its logical ID.</summary>
internal static class SpriteShowCommand
{
    public static ToolCommand Command { get; } = new("sprite-show", [.. SpriteOverlays.PublicationOptions, CommandOption.Required("--id", "ID")], Run);

    private static int Run(CommandArguments args)
    {
        string id = args["--id"];
        if (string.IsNullOrWhiteSpace(id) || id.Any(char.IsWhiteSpace)) throw new ArgumentException("--id must be a non-empty whitespace-free logical ID.");
        SpriteOverlays.PrintJson(SpriteOverlays.ReadPublication(args).Catalog.Require(id));
        return 0;
    }
}
