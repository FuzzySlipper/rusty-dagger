using Daggerfall.Import.Tool.ReferenceExports;

namespace Daggerfall.Import.Tool.Commands;

/// <summary>Offline, original-resolution experiment references, separate from product publication.</summary>
internal static class ReferenceExportCommands
{
    private static readonly CommandOption Arena2 = CommandOption.Optional("--arena2", "SOURCE_DIR");
    private static readonly CommandOption Output = CommandOption.Optional("--output", "NEW_EXPORT_DIR");
    private static readonly CommandOption[] ItemOptions =
    [
        Arena2,
        CommandOption.Optional("--donor", "DONOR_DIR"),
        Output,
    ];

    public static ToolCommand Items { get; } = new("export-items", ItemOptions, args =>
    {
        ItemReferenceExport.Run(args.Optional("--arena2") ?? "local/arena2",
            args.Optional("--donor") ?? "data/daggerfall-unity",
            args.Optional("--output") ?? "authoring/df-raw/items");
        return 0;
    });

    public static ToolCommand Spells { get; } = new("export-spells", [Arena2, Output], args =>
    {
        SpellReferenceExport.Run(args.Optional("--arena2") ?? "local/arena2",
            args.Optional("--output") ?? "authoring/df-raw/spells");
        return 0;
    });
}
