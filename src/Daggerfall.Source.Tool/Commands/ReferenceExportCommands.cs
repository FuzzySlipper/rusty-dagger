using Daggerfall.Source.Tool.ReferenceExports;

namespace Daggerfall.Source.Tool.Commands;

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
        ItemReferenceExport.Run(args.Optional("--arena2") ?? ImportLayout.Arena2,
            args.Optional("--donor") ?? "data/daggerfall-unity",
            args.Optional("--output") ?? "local/art/reference/items");
        return 0;
    }, Program.Tool);

    public static ToolCommand Spells { get; } = new("export-spells", [Arena2, Output], args =>
    {
        SpellReferenceExport.Run(args.Optional("--arena2") ?? ImportLayout.Arena2,
            args.Optional("--output") ?? "local/art/reference/spells");
        return 0;
    }, Program.Tool);

    public static ToolCommand PaperDoll { get; } = new("export-paper-doll", [Arena2, Output], args =>
    {
        MediaReferenceExport.RunPaperDoll(args.Optional("--arena2") ?? ImportLayout.Arena2,
            args.Optional("--output") ?? "local/art/reference/paper-doll");
        return 0;
    }, Program.Tool);

    public static ToolCommand Surfaces { get; } = new("export-surfaces", [Arena2, Output], args =>
    {
        SurfaceReferenceExport.Run(args.Optional("--arena2") ?? ImportLayout.Arena2,
            args.Optional("--output") ?? "local/art/reference/surfaces");
        return 0;
    }, Program.Tool);

    public static ToolCommand Flats { get; } = new("export-flats", [Arena2, Output], args =>
    {
        FlatReferenceExport.Run(args.Optional("--arena2") ?? ImportLayout.Arena2,
            args.Optional("--output") ?? "local/art/reference/flats");
        return 0;
    }, Program.Tool);

    public static ToolCommand People { get; } = new("export-people", [Arena2, Output], args =>
    {
        PeopleReferenceExport.Run(args.Optional("--arena2") ?? ImportLayout.Arena2,
            args.Optional("--output") ?? "local/art/reference/people");
        return 0;
    }, Program.Tool);

    public static ToolCommand Terrain { get; } = new("export-terrain", [Arena2, Output], args =>
    {
        TerrainReferenceExport.Run(args.Optional("--arena2") ?? ImportLayout.Arena2,
            args.Optional("--output") ?? "local/art/reference/terrain");
        return 0;
    }, Program.Tool);

    public static ToolCommand Effects { get; } = new("export-effects", [Arena2, Output], args =>
    {
        MediaReferenceExport.RunEffects(args.Optional("--arena2") ?? ImportLayout.Arena2,
            args.Optional("--output") ?? "local/art/reference/effects");
        return 0;
    }, Program.Tool);

    public static ToolCommand DirectionalSprites { get; } = new("export-directional-sprites",
        [Arena2, Output, CommandOption.Optional("--baseline", "MOBILE_EXPORT_DIR")], args =>
    {
        DirectionalSpriteReferenceExport.Run(args.Optional("--arena2") ?? ImportLayout.Arena2,
            args.Optional("--baseline") ?? "local/art/reference/daggerfall-mobile-idle-unmirrored-r01",
            args.Optional("--output") ?? "local/art/reference/daggerfall-remaining-directional-unmirrored-r01");
        return 0;
    }, Program.Tool);
}
