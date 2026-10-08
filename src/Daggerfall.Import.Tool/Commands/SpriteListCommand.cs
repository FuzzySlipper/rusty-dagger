using Daggerfall.Import.Publication;
using WorldRpg.SpriteAuthoring;

namespace Daggerfall.Import.Tool.Commands;

/// <summary>Lists a site closure's sprite inspection entries, optionally of one kind.</summary>
internal static class SpriteListCommand
{
    public static ToolCommand Command { get; } = new("sprite-list", [.. SpriteOverlays.PublicationOptions, CommandOption.Optional("--kind", "KIND")], Run);

    private static int Run(CommandArguments args)
    {
        IEnumerable<SpriteInspectionEntry> entries = SpriteOverlays.ReadPublication(args).Catalog.Entries;
        if (args.Optional("--kind") is { } kindName)
        {
            if (!Enum.TryParse(kindName, ignoreCase: true, out SpriteInspectionKind kind) || !Enum.IsDefined(kind))
            {
                throw new ArgumentException("--kind must name a known sprite inspection kind.");
            }

            entries = entries.Where(entry => entry.Kind == kind);
        }

        SpriteOverlays.PrintJson(entries.OrderBy(entry => entry.Id, StringComparer.Ordinal).ToArray());
        return 0;
    }
}
