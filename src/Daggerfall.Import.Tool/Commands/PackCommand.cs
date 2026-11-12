namespace Daggerfall.Import.Tool.Commands;

/// <summary>
/// Repacks the containers from what the import staged, after a step was rerun by hand: each container's
/// description is rewritten, then <c>local/data/daggerfall-data.rpak</c> and, when tracks are staged,
/// <c>local/data/music.rpak</c> are packed and checked against the staged files. The staged files may come from
/// several importer revisions, so the description records the repack as a departure and the container never reads
/// as current; only a full <c>import</c> produces one that does.
/// </summary>
internal static class PackCommand
{
    public static ToolCommand Command { get; } = new("pack",
        [CommandOption.Optional("--repository", "DIR"), CommandOption.Optional("--install", "DIR"), CommandOption.Switch("--compress")], Run);

    private static int Run(CommandArguments args)
    {
        string? install = args.Optional("--install") is { } named ? Path.GetFullPath(named) : null;
        Directory.SetCurrentDirectory(ImportLayout.RepositoryRoot(args));
        DataContainers.Pack(args.Switch("--compress"), install, "pack");
        return 0;
    }
}
