using System.Diagnostics;

namespace Daggerfall.Import.Tool.Commands;

/// <summary>
/// Turns the player's Daggerfall files into the data containers in one run: every import step in the
/// importer's dependency order, in process, into the staging tree, then <c>local/data/daggerfall-data.rpak</c>,
/// when a music folder is supplied <c>local/data/music.rpak</c>, and with <c>--classic-art</c> the developer's
/// <c>local/data/classic-art.rpak</c> of the classic media the art pack draws anew, which the data container leaves out.
/// </summary>
/// <remarks>
/// <para>
/// Every input is checked before anything is written, and every missing one is named, so an absent input
/// stops the run instead of leaving a half-written staging tree. A full run clears the staging tree and the
/// import records first, so a file no step writes any more disappears. Each step is the
/// tool's own command, run with the arguments it would be given on the command line, so a step can be rerun alone while the
/// importer is developed: <c>--only</c> reruns the named steps over the existing staging tree and repacks,
/// and <c>pack</c> repacks after a command was run by hand. Only a full run describes a container as its
/// revision's whole import; a repack records that it departs from one, so it never reads as current.
/// </para>
/// <para>
/// The run works in the repository root, and the steps are given repository-relative paths: the commands
/// record their arguments in what they publish, so the same inputs give the same bytes on any machine.
/// </para>
/// </remarks>
internal static class ImportCommand
{
    private const string UiAssets = AuthoredUi.ManifestFile;
    private const string UiOriginal = AuthoredUi.ProductDirectory;

    private static readonly string[] Arena2Files =
        ["MAPS.BSA", "BLOCKS.BSA", "ARCH3D.BSA", "DAGGER.SND", "CLIMATE.PAK", "POLITIC.PAK",
         "WOODS.WLD", "FACTION.TXT", "FLATS.CFG", "ANIM0000.VID", "AZURA.FLC"];

    public static ToolCommand Command { get; } = new("import",
    [
        CommandOption.Optional("--arena2", "DIR"), CommandOption.Optional("--music", "DIR"),
        CommandOption.Optional("--repository", "DIR"), CommandOption.Optional("--install", "DIR"), CommandOption.Optional("--only", "STEP[,STEP...]"),
        CommandOption.Switch("--compress"), CommandOption.Switch("--cinematics"), CommandOption.Switch("--classic-art"),
    ], Run);

    /// <summary>One import step: the label <c>--only</c> names it by, and the tool command it runs.</summary>
    internal sealed record Step(string Label, IReadOnlyList<string> Arguments);

    /// <summary>
    /// The inputs a run reads, as repository-relative paths where they lie inside the repository. The cinematics
    /// are converted only when asked (<c>--cinematics</c>, which needs FFmpeg); without them the game plays with none.
    /// They are off by default because their bytes depend on the machine that encodes them (another CPU encodes the
    /// same Opus audio differently), and a container must be the same wherever it is imported.
    /// </summary>
    internal sealed record Inputs(string Arena2, string? Music, bool Cinematics = false);

    private static int Run(CommandArguments args)
    {
        string invoked = Directory.GetCurrentDirectory();
        string repository = ImportLayout.RepositoryRoot(args);
        // The product names the directory the host opens containers from; by hand they go to the release or local/data/.
        string? install = args.Optional("--install") is { } named ? Path.GetFullPath(named, invoked) : null;
        // A release has no local/inputs/arena2: the player names the folder.
        string arena2 = args.Optional("--arena2")
            ?? (ImportLayout.ReleaseDirectory is null ? Environment.GetEnvironmentVariable("DAGGER_ARENA2") ?? Path.Combine(repository, ImportLayout.Arena2) : null)
            ?? throw args.Invalid("name the folder Daggerfall is installed in, or its ARENA2 folder.");
        bool cinematics = args.Switch("--cinematics");
        Inputs inputs = new(
            Portable(Arena2Folder(Path.GetFullPath(arena2, invoked)) ?? arena2, invoked, repository),
            args.Optional("--music") is { } music ? Portable(music, invoked, repository) : null,
            cinematics);
        IReadOnlyList<Step> steps = Steps(inputs);
        HashSet<string>? only = args.Optional("--only") is { } names
            ? [.. names.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)]
            : null;
        if (only is not null)
        {
            string[] unknown = [.. only.Where(label => !steps.Any(step => step.Label == label)).Order(StringComparer.Ordinal)];
            if (unknown.Length != 0)
                throw args.Invalid($"--only names no step [{string.Join(", ", unknown)}]; the steps are [{string.Join(", ", steps.Select(step => step.Label))}].");
        }

        string[] missing = [.. MissingInputs(inputs, repository)];
        if (missing.Length != 0)
        {
            string next = missing.Any(entry => entry.StartsWith("Arena2 ", StringComparison.Ordinal))
                ? $"{Environment.NewLine}Name a complete copy of the game's ARENA2 folder, or the folder Daggerfall is installed in."
                : "";
            throw new InvalidOperationException($"missing input{(missing.Length == 1 ? "" : "s")}:{Environment.NewLine}  {string.Join(Environment.NewLine + "  ", missing)}{next}");
        }
        Directory.SetCurrentDirectory(repository);
        ImportLayout.RefuseLinkedOutputs(repository, install);

        // The cinematics' bytes depend on the FFmpeg build (each artifact records it), so name it up front.
        Console.WriteLine(cinematics
            ? $"import: converting the cinematics with {FirstLine("ffmpeg", "-version")}"
            : "import: the cinematics are not converted, and the game plays without them; --cinematics adds them (it needs ffmpeg and ffprobe on PATH).");
        long started = Environment.TickCount64;
        if (only is null)
        {
            foreach (string directory in new[] { ImportLayout.Staging, ImportLayout.Records, ImportLayout.MusicTracks, ClassicArt.Tree })
                if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }

        foreach (Step step in steps.Where(step => only is null || only.Contains(step.Label)))
        {
            Console.WriteLine($"+ [{step.Label}] daggerfall-import-tool {string.Join(' ', step.Arguments.Select(Quote))}");
            int status = Program.Commands[step.Arguments[0]].Invoke(step.Arguments);
            if (status != 0) throw new InvalidOperationException($"step '{step.Label}' failed with exit status {status}; nothing was packed.");
        }

        DataContainers.Pack(args.Switch("--compress"), install,
            only is null ? null : $"import --only {string.Join(',', steps.Where(step => only.Contains(step.Label)).Select(step => step.Label))}",
            args.Switch("--classic-art"));
        // A player keeps only the containers; the staging tree and the records are the developer's.
        if (ImportLayout.ReleaseDirectory is not null) Directory.Delete("local", recursive: true);
        Console.WriteLine($"import: finished in {(Environment.TickCount64 - started) / 1000}s");
        return 0;
    }

    /// <summary>
    /// Every step, in the importer's dependency order; several read what an earlier one wrote.
    /// </summary>
    internal static IReadOnlyList<Step> Steps(Inputs inputs)
    {
        string arena2 = inputs.Arena2;
        const string staging = ImportLayout.Staging;
        const string imported = ImportLayout.ImportedPayload;
        const string blocks = ImportLayout.BlockDocument;
        const string buildings = ImportLayout.BuildingsPayload;
        const string worldMedia = ImportLayout.WorldMedia;
        const string classicGroup = ImportLayout.ClassicGroup;
        List<Step> steps = [];
        void Add(string label, params string[] arguments) => steps.Add(new(label, arguments));

        // 1. Product-wide media. The music container's tracks are staged by the naming scheme; nothing in the
        //    data container names them, so it is the same whichever recordings were supplied. The classic media
        //    group's inventory lists the authored UI art, whose bytes are the product's own tracked content.
        if (inputs.Music is { } music) Add("music-tracks", "music-tracks", "--music", music, "--out", ImportLayout.MusicTracks);

        Add("classic-media", "classic-media", "--arena2", arena2, "--out", staging, "--group", "worldrpg",
            "--ui-authored-assets", UiAssets, "--ui-original", UiOriginal, "--update");
        Add("sky-media", "sky-media", "--arena2", arena2, "--out", staging, "--group", "worldrpg", "--update");

        // 2. The imported payload's sections, in dependency order, with the block document and the import
        //    records. None reads the authored payload: the ruleset joins it to these sections when it loads, and the
        //    classic tables and the game's text are authored data there (DEC-13).
        Add("character-presentation", "character-presentation", "--arena2", arena2, "--pack", imported,
            "--out", staging, "--group", "worldrpg", "--update");
        Add("locations", "locations", "--arena2", arena2, "--pack", imported, "--out", staging, "--update");
        Add("blocks", "blocks", "--arena2", arena2, "--document", blocks, "--buildings", buildings, "--update");
        Add("climate", "climate", "--arena2", arena2, "--pack", imported, "--update");
        Add("terrain", "terrain", "--arena2", arena2, "--pack", imported, "--out", staging, "--update");
        Add("atlas", "atlas", "--pack", imported, "--terrain", ImportLayout.TerrainBake, "--out", staging, "--update");
        Add("paper-doll-media", "paper-doll-media", "--arena2", arena2, "--pack", imported, "--out", staging, "--group", "worldrpg", "--update");
        Add("videos", "videos", "--arena2", arena2, "--pack", imported, "--update");
        if (inputs.Cinematics)
        {
            Add("cinematic-media-vid", "cinematic-media", "--arena2", arena2, "--pack", imported, "--out", staging, "--kind", "vid", "--update");
            Add("cinematic-media-flc", "cinematic-media", "--arena2", arena2, "--pack", imported, "--out", staging, "--kind", "flc", "--update");
        }

        // 3. The product-wide world media every location draws from: every mesh, material texture, billboard
        //    and actor atlas, terrain texture, the classic sidecar and its audio clips, published once.
        Add("world-media", "world-media", "--arena2", arena2, "--ui-authored-assets", UiAssets, "--ui-original", UiOriginal,
            "--pack", imported, "--classic-group", classicGroup, "--out", worldMedia);

        // 4. The per-block world publication: every RMB exterior, building interior and RDB block a location
        //    places, normalized in its own frame, its static meshes placing the world media publication's meshes.
        Add("world-blocks", "world-blocks", "--arena2", arena2, "--pack", imported, "--buildings", buildings, "--shared", worldMedia,
            "--out", ImportLayout.WorldBlocks);
        return steps;
    }

    /// <summary>
    /// Names every input the run needs and lacks, in the order the steps read them; a relative path is read
    /// from the repository root, where the steps run.
    /// </summary>
    internal static IEnumerable<string> MissingInputs(Inputs inputs, string repository)
    {
        bool IsFile(string path) => File.Exists(Path.Combine(repository, path));
        bool IsDirectory(string path) => Directory.Exists(Path.Combine(repository, path));

        if (!IsDirectory(inputs.Arena2))
        {
            yield return $"Arena2 directory '{inputs.Arena2}' (link local/inputs/arena2 to the game's ARENA2 folder or pass --arena2)";
        }
        else if (!IsFile(Path.Combine(inputs.Arena2, "MAPS.BSA")) && !IsFile(Path.Combine(inputs.Arena2, "BLOCKS.BSA")))
        {
            yield return $"Arena2 folder: '{inputs.Arena2}' is not Daggerfall's ARENA2 folder and none was found inside it";
        }
        else
        {
            foreach (string file in Arena2Files.Where(file => !IsFile(Path.Combine(inputs.Arena2, file))))
                yield return $"Arena2 file '{inputs.Arena2}/{file}'";
            string[] sky = ["PAL.PAL", "NIGHTSKY.COL", "ART_PAL.COL", "MOON00I0.CFA", "MOON01I0.CFA", .. Enumerable.Range(0, 32).Select(index => $"SKY{index:D2}.DAT"), .. Enumerable.Range(0, 4).Select(index => $"NITE{index:D2}I0.IMG")];
            foreach (string file in sky.Where(file => !IsFile(Path.Combine(inputs.Arena2, file))))
                yield return $"Arena2 sky file '{inputs.Arena2}/{file}'";
        }

        if (inputs.Cinematics)
            foreach (string tool in new[] { "ffmpeg", "ffprobe" }.Where(tool => FindOnPath(tool) is null))
                yield return $"{tool} on PATH (--cinematics converts the cinematics with it)";
        if (inputs.Music is { } music && !IsDirectory(music)) yield return $"music folder '{music}' (a folder of <track>.<ext> recordings, or leave out --music)";
        foreach (string file in new[] { ClassicArt.ListFile, UiAssets }.Where(file => !IsFile(file)))
            yield return $"tracked file '{file}'";
        if (!IsDirectory(UiOriginal)) yield return $"tracked directory '{UiOriginal}'";
    }

    /// <summary>
    /// The ARENA2 folder a caller pointed at: the folder itself, or the one an installation holds a few levels
    /// down (GOG and Steam keep it at <c>DF/DAGGER/ARENA2</c>). Null when the path is no folder or holds none,
    /// which the input check then names.
    /// </summary>
    internal static string? Arena2Folder(string path)
    {
        static bool IsArena2(string directory) => File.Exists(Path.Combine(directory, "MAPS.BSA")) || File.Exists(Path.Combine(directory, "BLOCKS.BSA"));
        if (!Directory.Exists(path) || IsArena2(path)) return null;
        EnumerationOptions search = new() { RecurseSubdirectories = true, MaxRecursionDepth = 3, MatchCasing = MatchCasing.CaseInsensitive, IgnoreInaccessible = true };
        return Directory.EnumerateDirectories(path, "ARENA2", search).Where(IsArena2).Order(StringComparer.Ordinal).FirstOrDefault();
    }

    /// <summary>
    /// A caller's path as the steps are given it: relative to the repository with <c>/</c> when it lies inside
    /// the repository, so the commands record the same string on every machine, and absolute otherwise.
    /// </summary>
    internal static string Portable(string path, string invokedFrom, string repository)
    {
        string full = Path.GetFullPath(path, invokedFrom);
        string relative = Path.GetRelativePath(repository, full);
        return relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(relative)
            ? full
            : relative.Replace('\\', '/');
    }

    /// <summary>An executable on PATH, as the shell would find it, or null.</summary>
    private static string? FindOnPath(string name)
    {
        string[] names = OperatingSystem.IsWindows() ? [name + ".exe", name] : [name];
        return (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(directory => names.Select(file => Path.Combine(directory, file)))
            .FirstOrDefault(File.Exists);
    }

    private static string FirstLine(string executable, string argument)
    {
        using Process process = Process.Start(new ProcessStartInfo(executable, argument) { RedirectStandardOutput = true, UseShellExecute = false })
            ?? throw new InvalidOperationException($"'{executable}' could not be started.");
        string line = process.StandardOutput.ReadLine() ?? "";
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return line;
    }

    private static string Quote(string argument) => argument.Contains(' ', StringComparison.Ordinal) || argument.Contains('\'', StringComparison.Ordinal)
        ? $"\"{argument}\""
        : argument;
}
