using System.Diagnostics;

namespace Daggerfall.Import.Tool.Commands;

/// <summary>
/// Turns the player's Daggerfall files into the data containers in one run: every import step in the
/// importer's dependency order, in process, into the staging tree, then <c>local/daggerfall-data.rpak</c>
/// and, when a music folder is supplied, <c>local/music.rpak</c>.
/// </summary>
/// <remarks>
/// <para>
/// Every input is checked before anything is written, and every missing one is named, so an absent input
/// stops the run instead of leaving a half-written staging tree. A full run clears the staging tree and the
/// import records first, so a file no step writes any more disappears. Each step is the tool's own command,
/// run with the arguments it would be given on the command line, so a step can be rerun alone while the
/// importer is developed: <c>--only</c> reruns the named steps over the existing staging tree and repacks,
/// and <c>pack</c> repacks after a command was run by hand.
/// </para>
/// <para>
/// The run works in the repository root, and the steps are given repository-relative paths: the commands
/// record their arguments in what they publish, so the same inputs give the same bytes on any machine.
/// </para>
/// </remarks>
internal static class ImportCommand
{
    private const string DefaultArena2 = "local/arena2";
    /// <summary>The Daggerfall Unity values the import reads, tracked in the repository under DFU's own paths.</summary>
    internal const string Donor = "data/daggerfall-unity";
    private const string Inventory = ImportLayout.Inventory;
    private const string UiAssets = "data/ui-authored-assets.json";
    private const string UiOriginal = AuthoredUi.ProductDirectory;
    private const string Authored = "content/worldrpg/payloads/daggerfall.base.json";

    private static readonly string[] Arena2Files =
        ["MAPS.BSA", "BLOCKS.BSA", "ARCH3D.BSA", "MONSTER.BSA", "DAGGER.SND", "TEXT.RSC", "CLASSES.DAT", "CLIMATE.PAK", "POLITIC.PAK",
         "WOODS.WLD", "FACTION.TXT", "SPELLS.STD", "MAGIC.DEF", "ANIM0000.VID", "AZURA.FLC"];

    private static readonly string[] DonorFiles =
    [
        "Assets/Scripts/Utility/EnemyBasics.cs",
        "Assets/Scripts/Game/Items/ItemEnums.cs",
        "Assets/Scripts/Game/Items/ItemHelper.cs",
        "Assets/Scripts/API/ItemsFile.cs",
        "Assets/Scripts/API/MapsFile.cs",
        "Assets/Scripts/Game/Formulas/FormulaHelper.cs",
        "Assets/Scripts/Game/Entities/RaceTemplate.cs",
        "Assets/Resources/ItemTemplates.txt",
        "Assets/Resources/MagicItemTemplates.txt",
        "Assets/StreamingAssets/Text/Master Localization CSV Files/Internal_Strings.csv",
        "Assets/StreamingAssets/Text/Master Localization CSV Files/Internal_Flats.csv",
        "Assets/StreamingAssets/Tables/QuestList-Classic.txt",
        .. new[] { "GlobalVars", "StaticMessages", "Places", "Sounds", "Diseases", "Spells", "Items", "Factions", "Foes" }
            .Select(table => $"Assets/StreamingAssets/Tables/Quests-{table}.txt"),
    ];

    public static ToolCommand Command { get; } = new("import",
    [
        CommandOption.Optional("--arena2", "DIR"), CommandOption.Optional("--music", "DIR"),
        CommandOption.Optional("--sprite-authoring", "DIR"), CommandOption.Optional("--block-variants", "FILE"),
        CommandOption.Optional("--repository", "DIR"), CommandOption.Optional("--only", "STEP[,STEP...]"),
        CommandOption.Switch("--compress"),
    ], Run);

    /// <summary>One import step: the label <c>--only</c> names it by, and the tool command it runs.</summary>
    internal sealed record Step(string Label, IReadOnlyList<string> Arguments);

    /// <summary>
    /// The inputs a run reads, as repository-relative paths where they lie inside the repository. The cinematics
    /// are converted only when FFmpeg is on PATH; without it the game plays with none.
    /// </summary>
    internal sealed record Inputs(string Arena2, string? Music, string? SpriteAuthoring, string? BlockVariants, bool Cinematics = true);

    private static int Run(CommandArguments args)
    {
        string invoked = Directory.GetCurrentDirectory();
        string repository = ImportLayout.RepositoryRoot(args);
        string arena2 = args.Optional("--arena2") ?? Environment.GetEnvironmentVariable("DAGGER_ARENA2") ?? Path.Combine(repository, DefaultArena2);
        bool cinematics = FindOnPath("ffmpeg") is not null && FindOnPath("ffprobe") is not null;
        Inputs inputs = new(
            Portable(Arena2Folder(Path.GetFullPath(arena2, invoked)) ?? arena2, invoked, repository),
            args.Optional("--music") is { } music ? Portable(music, invoked, repository) : null,
            args.Optional("--sprite-authoring") is { } sprites ? Portable(sprites, invoked, repository) : null,
            args.Optional("--block-variants") is { } variants ? Portable(variants, invoked, repository) : null,
            cinematics);
        IReadOnlyList<Step> steps = Steps(inputs, repository);
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
                ? $"{Environment.NewLine}Point --arena2 at a complete copy of the game's ARENA2 folder, or at the folder Daggerfall is installed in."
                : "";
            throw new InvalidOperationException($"missing input{(missing.Length == 1 ? "" : "s")}:{Environment.NewLine}  {string.Join(Environment.NewLine + "  ", missing)}{next}");
        }
        Directory.SetCurrentDirectory(repository);

        // The cinematics' bytes depend on the FFmpeg build (each artifact records it), so name it up front.
        Console.WriteLine(cinematics
            ? $"import: {FirstLine("ffmpeg", "-version")}"
            : "import: warning: ffmpeg and ffprobe are not both on PATH, so the cinematics are not converted and the game plays without them; install FFmpeg and import again to add them.");
        long started = Environment.TickCount64;
        if (only is null)
        {
            foreach (string directory in new[] { ImportLayout.Staging, ImportLayout.Records, ImportLayout.MusicTracks })
                if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }

        foreach (Step step in steps.Where(step => only is null || only.Contains(step.Label)))
        {
            Console.WriteLine($"+ [{step.Label}] daggerfall-import-tool {string.Join(' ', step.Arguments.Select(Quote))}");
            int status = Program.Commands[step.Arguments[0]].Invoke(step.Arguments);
            if (status != 0) throw new InvalidOperationException($"step '{step.Label}' failed with exit status {status}; nothing was packed.");
        }

        DataContainers.Pack(args.Switch("--compress"));
        Console.WriteLine($"import: finished in {(Environment.TickCount64 - started) / 1000}s");
        return 0;
    }

    /// <summary>
    /// Every step, in the importer's dependency order; several read what an earlier one wrote. A site's sprite
    /// overlay is passed when the authoring root, read from the repository root, holds one for it.
    /// </summary>
    internal static IReadOnlyList<Step> Steps(Inputs inputs, string repository)
    {
        string arena2 = inputs.Arena2;
        static string Donor(string path) => $"{ImportCommand.Donor}/{path}";
        const string staging = ImportLayout.Staging;
        const string records = ImportLayout.Records;
        const string imported = ImportLayout.ImportedPayload;
        const string blocks = ImportLayout.BlockDocument;
        const string buildings = ImportLayout.BuildingsPayload;
        const string importRecords = ImportLayout.ImportRecords;
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
        //    records. Commands that join authored sections (vocabulary, actors, items) read the authored payload.
        Add("catalogs", "catalogs", "--arena2", arena2, "--inventory", Inventory, "--authored", Authored, "--pack", imported,
            "--donor-races", Donor("Assets/Scripts/Game/Entities/RaceTemplate.cs"), "--update");
        Add("item-template-ledger", "item-template-ledger", "--donor", Donor("Assets/Scripts/Game/Items"), "--inventory", Inventory,
            "--authored", Authored, "--pack", imported, "--update");
        Add("character-presentation", "character-presentation", "--arena2", arena2, "--inventory", Inventory, "--pack", imported,
            "--out", staging, "--group", "worldrpg", "--update");
        Add("locations", "locations", "--arena2", arena2, "--pack", imported, "--out", staging, "--update");
        Add("magic-catalog", "magic-catalog", "--arena2", arena2, "--pack", imported,
            "--donor-formulas", Donor("Assets/Scripts/Game/Formulas/FormulaHelper.cs"), "--update");
        Add("mobile-catalog", "mobile-catalog", "--donor", Donor("Assets/Scripts/Utility/EnemyBasics.cs"), "--authored", Authored,
            "--pack", imported, "--archive", $"{arena2}/MONSTER.BSA", "--update");
        Add("text", "text", "--arena2", arena2, "--pack", imported, "--inventory", Inventory, "--language", "en", "--update");
        Add("internal-strings", "internal-strings",
            "--source", Donor("Assets/StreamingAssets/Text/Master Localization CSV Files/Internal_Strings.csv"),
            "--label", "daggerfall-unity/Assets/StreamingAssets/Text/Master Localization CSV Files/Internal_Strings.csv",
            "--pack", imported, "--language", "en", "--update");
        Add("blocks", "blocks", "--arena2", arena2, "--document", blocks, "--buildings", buildings, "--inventory", Inventory, "--update");
        Add("geometry", "geometry", "--arena2", arena2, "--blocks", blocks, "--records", importRecords, "--inventory", Inventory, "--update");
        Add("climate", "climate", "--arena2", arena2, "--pack", imported, "--inventory", Inventory, "--update");
        Add("factions", "factions", "--arena2", arena2, "--pack", imported, "--inventory", Inventory,
            "--flat-captions", Donor("Assets/StreamingAssets/Text/Master Localization CSV Files/Internal_Flats.csv"), "--update");
        Add("terrain", "terrain", "--arena2", arena2, "--pack", imported, "--out", staging, "--inventory", Inventory, "--update");
        Add("items", "items", "--arena2", arena2, "--pack", imported, "--inventory", Inventory,
            "--item-templates", Donor("Assets/Resources/ItemTemplates.txt"),
            "--item-enums", Donor("Assets/Scripts/Game/Items/ItemEnums.cs"),
            "--item-helper", Donor("Assets/Scripts/Game/Items/ItemHelper.cs"),
            "--magic-templates", Donor("Assets/Resources/MagicItemTemplates.txt"), "--update");
        Add("paper-doll-media", "paper-doll-media", "--arena2", arena2, "--pack", imported, "--out", staging, "--group", "worldrpg", "--update");
        Add("quests", "quests", "--arena2", arena2, "--quest-text", Donor("Assets/StreamingAssets/Quests"),
            "--tables", Donor("Assets/StreamingAssets/Tables"), "--pack", imported, "--records", importRecords, "--inventory", Inventory, "--update");
        Add("videos", "videos", "--arena2", arena2, "--pack", imported, "--inventory", Inventory, "--update");
        if (inputs.Cinematics)
        {
            Add("cinematic-media-vid", "cinematic-media", "--arena2", arena2, "--pack", imported, "--out", staging, "--kind", "vid", "--update");
            Add("cinematic-media-flc", "cinematic-media", "--arena2", arena2, "--pack", imported, "--out", staging, "--kind", "flc", "--update");
        }
        Add("building-name-inputs", "building-name-inputs", "--maps-file", Donor("Assets/Scripts/API/MapsFile.cs"),
            "--label", "daggerfall-unity/Assets/Scripts/API/MapsFile.cs", "--pack", imported, "--update");

        // 3. Quest corpus payloads, read from the quest sections and the original-source selections above.
        Add("fighters-quest-corpus", "fighters-quest-corpus", "--pack", imported, "--records", importRecords,
            "--out", $"{ImportLayout.Payloads}/daggerfall.quests.fighters.json");
        Add("classic-quest-corpora", "classic-quest-corpora", "--pack", imported, "--records", importRecords, "--out", ImportLayout.Payloads);

        // 4. The product-wide world media every site closure references: every mesh, material texture, billboard
        //    and actor atlas, terrain texture, the classic sidecar and its audio clips, published once.
        Add("world-media", "world-media", "--arena2", arena2, "--ui-authored-assets", UiAssets, "--ui-original", UiOriginal, "--inventory", Inventory,
            "--pack", imported, "--classic-group", classicGroup, "--out", worldMedia,
            "--source-manifest", $"{records}/world-media.sources.json");

        // 5. The per-block world publication: every RMB exterior, building interior and RDB block a location
        //    places, normalized in its own frame, its static meshes placing the world media publication's meshes.
        string[] variants = inputs.BlockVariants is null ? [] : ["--variants", inputs.BlockVariants];
        Add("world-blocks", ["world-blocks", "--arena2", arena2, "--pack", imported, "--buildings", buildings, "--shared", worldMedia, "--inventory", Inventory,
            "--out", ImportLayout.WorldBlocks, "--source-manifest", $"{records}/world-blocks.sources.json", .. variants]);

        // 6. Site closures. Each writes only what is its own: every body and shared sidecar entry is referenced
        //    from the world media publication above. Its source manifest goes to the import records.
        string[] common = ["--arena2", arena2, "--ui-authored-assets", UiAssets, "--ui-original", UiOriginal, "--inventory", Inventory,
            "--pack", imported, "--shared", worldMedia, "--classic-group", classicGroup];
        string[] Overlay(string site) => inputs.SpriteAuthoring is { } root && File.Exists(Path.Combine(repository, root, "sprites", site + ".json"))
            ? ["--sprite-authoring", root, "--sprite-overlay", $"sprites/{site}.json"]
            : [];
        void Site(string verb, string label, string output, string region, string location, params string[] more) =>
            Add(label, [verb, .. common, "--out", $"{staging}/worldrpg/imports/{output}",
                "--source-manifest", $"{records}/sites/{label}.sources.json", "--region", region, "--location", location, .. more]);
        Site("write", "privateers-hold", "privateers-hold", "17", "Privateer's Hold", Overlay("privateers-hold"));
        Site("write", "castle-necromoghan", "castle-necromoghan", "17", "Castle Necromoghan", Overlay("castle-necromoghan"));
        Site("write", "the-hawkston-cemetery", "the-hawkston-cemetery", "17", "The Hawkston Cemetery");
        Site("write", "the-tombs-of-klerd", "the-tombs-of-klerd", "0", "The Tombs of Klerd");
        Site("rmb-spatial", "the-hawkston-cemetery-exterior", "the-hawkston-cemetery/exterior", "17", "The Hawkston Cemetery", "--profile", "exterior");
        Site("rmb-spatial", "the-tombs-of-klerd-exterior", "the-tombs-of-klerd/exterior", "0", "The Tombs of Klerd", "--profile", "exterior");
        Site("rmb-spatial", "charing-exterior", "charing/exterior", "17", "Charing", "--profile", "exterior");
        Site("rmb-spatial", "charing-interior-1-1-0", "charing/interior-1-1-0", "17", "Charing",
            "--profile", "interior", "--block-x", "1", "--block-y", "1", "--building", "0");
        // Source-backed ordinary providers are selected from the published RMB building records. These closures
        // keep their source block/building identity in the normalized document, so the ruleset admits the people
        // and their catalog services at the real interior site.
        foreach ((string label, string x, string y, string building) in new[]
        {
            ("charing-general-store-interior", "2", "1", "0"),
            ("charing-mages-interior", "3", "4", "0"),
            ("charing-kynareth-interior", "3", "1", "13"),
            ("charing-bank-interior", "1", "5", "17"),
            ("charing-shop-interior", "4", "2", "0"),
            ("charing-knights-interior", "3", "2", "14"),
        })
        {
            Site("rmb-spatial", label, $"charing/interior-{x}-{y}-{building}", "17", "Charing", "--location-index", "4",
                "--profile", "interior", "--block-x", x, "--block-y", y, "--building", building);
        }

        // The two ships share their display name; their MAPS indices select them.
        Site("rmb-spatial", "small-ship", "small-ship/exterior", "31", "Your Ship", "--location-index", "1", "--profile", "exterior");
        Site("rmb-spatial", "large-ship", "large-ship/exterior", "31", "Your Ship", "--location-index", "2", "--profile", "exterior");

        // 7. Reconcile every producer citation and raw-record ledger against the documented inventory, over the
        //    content the product will see. This is import coverage, not runtime parity certification.
        Add("source-coverage", "source-coverage", "--arena2", arena2, "--inventory", Inventory, "--repository", ".",
            "--content", staging, "--import-records", records, "--output", $"{records}/source-coverage.json");
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

        if (inputs.BlockVariants is { } variants && !IsFile(variants)) yield return $"block variant descriptor '{variants}'";
        if (!IsDirectory(inputs.Arena2))
        {
            yield return $"Arena2 directory '{inputs.Arena2}' (link local/arena2 to the game's ARENA2 folder or pass --arena2)";
        }
        else if (!IsFile(Path.Combine(inputs.Arena2, "MAPS.BSA")) && !IsFile(Path.Combine(inputs.Arena2, "BLOCKS.BSA")))
        {
            yield return $"Arena2 folder: '{inputs.Arena2}' is not Daggerfall's ARENA2 folder and none was found inside it";
        }
        else
        {
            foreach (string file in Arena2Files.Where(file => !IsFile(Path.Combine(inputs.Arena2, file))))
                yield return $"Arena2 file '{inputs.Arena2}/{file}'";
            string[] sky = ["PAL.PAL", "NIGHTSKY.COL", .. Enumerable.Range(0, 32).Select(index => $"SKY{index:D2}.DAT"), .. Enumerable.Range(0, 4).Select(index => $"NITE{index:D2}I0.IMG")];
            foreach (string file in sky.Where(file => !IsFile(Path.Combine(inputs.Arena2, file))))
                yield return $"Arena2 sky file '{inputs.Arena2}/{file}'";
            if (!IsDirectory(Path.Combine(inputs.Arena2, "books"))) yield return $"Arena2 books directory '{inputs.Arena2}/books'";
        }

        foreach (string file in DonorFiles.Where(file => !IsFile(Path.Combine(Donor, file))))
            yield return $"tracked file '{Donor}/{file}'";
        if (!IsDirectory(Path.Combine(Donor, "Assets/StreamingAssets/Quests")))
            yield return $"tracked directory '{Donor}/Assets/StreamingAssets/Quests'";

        if (inputs.Music is { } music && !IsDirectory(music)) yield return $"music folder '{music}' (a folder of <track>.<ext> recordings, or leave out --music)";
        if (inputs.SpriteAuthoring is { } sprites && !IsDirectory(sprites)) yield return $"sprite authoring root '{sprites}'";
        foreach (string file in new[] { Inventory, UiAssets, Authored }.Where(file => !IsFile(file)))
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
