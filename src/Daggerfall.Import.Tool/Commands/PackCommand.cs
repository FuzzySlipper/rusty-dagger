namespace Daggerfall.Import.Tool.Commands;

/// <summary>
/// Repacks the containers from what the import staged, after a step was rerun by hand: each container's
/// description is rewritten, then <c>local/daggerfall-data.rpak</c> and, when tracks are staged,
/// <c>local/music.rpak</c> are packed and checked against the staged files.
/// </summary>
internal static class PackCommand
{
    public static ToolCommand Command { get; } = new("pack",
        [CommandOption.Optional("--repository", "DIR"), CommandOption.Switch("--compress")], Run);

    private static int Run(CommandArguments args)
    {
        Directory.SetCurrentDirectory(ImportLayout.RepositoryRoot(args));
        DataContainers.Pack(args.Switch("--compress"));
        return 0;
    }
}
