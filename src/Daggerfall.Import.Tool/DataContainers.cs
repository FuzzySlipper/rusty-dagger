using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Daggerfall.Import.Publication;
using Rusty.Engine;
using Rusty.Engine.Testing;

namespace Daggerfall.Import.Tool;

/// <summary>
/// Where the import writes, relative to the repository root: the staging tree the data container is packed
/// from, the import records, the music container's tracks, and the two containers.
/// </summary>
/// <remarks>
/// The commands record the paths they were given in what they publish, so these stay relative and use
/// <c>/</c>: the same import on another machine, or on Windows, records the same strings. Paths inside the
/// staging tree are the content-root-relative paths the product names (<c>worldrpg/imports/...</c>,
/// <c>worldrpg/payloads/...</c>), so a manifest reads the same from the container as from the staging tree.
/// </remarks>
internal static class ImportLayout
{
    /// <summary>Where the player's ARENA2 folder is supplied in a repository, with the other supplied inputs.</summary>
    public const string Arena2 = "local/inputs/arena2";

    public const string Staging = "local/build/import/staging";
    public const string Records = "local/build/import/records";

    /// <summary>The music container's files: one recording per track at the root, by the naming scheme.</summary>
    public const string MusicTracks = "local/build/import/music";

    /// <summary>The imported payload: every base section the commands derive.</summary>
    public const string ImportedPayload = $"{Staging}/worldrpg/payloads/daggerfall.imported.json";

    /// <summary>The directory the quest corpus payloads are written into.</summary>
    public const string Payloads = $"{Staging}/worldrpg/payloads";

    /// <summary>The block payload the runtime reads: building fields, map positions and automap footprints.</summary>
    public const string BuildingsPayload = $"{Staging}/worldrpg/payloads/daggerfall.blocks.json";

    /// <summary>The complete block document, an importer record whose use sites geometry reads.</summary>
    public const string BlockDocument = $"{Records}/daggerfall.blocks.json";

    /// <summary>The importer records nothing at runtime reads: the mesh inventory and the original quest-source selections.</summary>
    public const string ImportRecords = $"{Records}/daggerfall.import-records.json";

    /// <summary>The classic media group the classic, sky, character and paper-doll media are written into.</summary>
    public const string ClassicGroup = $"{Staging}/worldrpg";

    /// <summary>The product-wide world media publication every site closure references.</summary>
    public const string WorldMedia = $"{Staging}/worldrpg/imports/shared";

    /// <summary>The per-block world publication.</summary>
    public const string WorldBlocks = $"{Staging}/worldrpg/imports/world-blocks";

    public const string DataContainer = "local/data/daggerfall-data.rpak";
    public const string MusicContainer = "local/data/music.rpak";

    /// <summary>Each container's own description, at its container-relative path.</summary>
    public const string DescriptorPath = "worldrpg/data-container.json";

    /// <summary>
    /// Our own tables the steps join with the player's files, taken once from Daggerfall Unity; a release publishes
    /// them beside the tool at this same path. The import never reads a Daggerfall Unity file.
    /// </summary>
    public const string Inputs = "src/Daggerfall.Import.Tool/inputs";

    /// <summary>The tracked file that marks the repository root the import works in.</summary>
    public const string Inventory = $"{Inputs}/content-source-manifest.csv";

    /// <summary>
    /// The release the tool was installed into by <c>rusty build --release</c>, which puts it in
    /// <c>tools/&lt;project&gt;/</c> with the tracked inputs it reads beside it; null when it runs from a repository.
    /// </summary>
    public static string? ReleaseDirectory { get; } = FindRelease(AppContext.BaseDirectory);

    /// <summary>The nearest directory above this one that holds the repository's inventory, or null.</summary>
    internal static string? EnclosingRepository(string directory)
    {
        for (DirectoryInfo? current = new(directory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, Inventory))) return current.FullName;
        return null;
    }

    internal static string? FindRelease(string toolDirectory)
    {
        string release = Path.GetFullPath(Path.Combine(toolDirectory, "..", ".."));
        return File.Exists(Path.Combine(release, "product.rpak")) && File.Exists(Path.Combine(toolDirectory, Inventory)) ? release : null;
    }

    /// <summary>
    /// Where a container is installed: in the directory the caller names (the product passes the one the host
    /// opens containers from), else beside <c>product.rpak</c> in a release, else in <c>local/data/</c> in a repository.
    /// </summary>
    public static string Installed(string container, string? install = null) =>
        (install ?? ReleaseDirectory) is { } directory ? Path.Combine(directory, Path.GetFileName(container)) : container;

    /// <summary>
    /// Refuses to write the import's output where it is, or lies under, a link inside the repository. A worktree links
    /// <c>local/data</c> (and may link <c>local/build/import</c>) to the main checkout's to run the game and its tests;
    /// an import there would replace them for every checkout that shares them with this worktree's importer revision.
    /// </summary>
    public static void RefuseLinkedOutputs(string repository, string? install)
    {
        foreach (string output in new[] { Installed(DataContainer, install), Installed(MusicContainer, install), Staging, Records, MusicTracks })
        {
            for (FileSystemInfo? current = new FileInfo(Path.GetFullPath(output, repository));
                 current is not null;
                 current = current is FileInfo file ? file.Directory : ((DirectoryInfo)current).Parent)
            {
                string relative = Path.GetRelativePath(repository, current.FullName);
                if (relative == "." || relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative)) break;
                if (current.LinkTarget is not { } target) continue;
                string linked = relative.Replace('\\', '/');
                throw new InvalidOperationException(
                    $"'{linked}' is a link to '{target}', so an import here would replace what every checkout sharing it opens. "
                    + $"Give this checkout its own (rm {linked}), then import again.");
            }
        }
    }

    /// <summary>
    /// The repository root a command works in: <c>--repository</c>, or the current directory; in a release, the
    /// tool's own directory, which holds the tracked inputs; and when the product runs the tool from its build
    /// output in another directory, the repository that build output lies in. A directory that is not one is
    /// refused with the command's usage, since every path the import writes is below it.
    /// </summary>
    public static string RepositoryRoot(CommandArguments args)
    {
        string repository = Path.GetFullPath(args.Optional("--repository")
            ?? (ReleaseDirectory is not null ? AppContext.BaseDirectory
                : File.Exists(Path.Combine(Directory.GetCurrentDirectory(), Inventory)) ? Directory.GetCurrentDirectory()
                : EnclosingRepository(AppContext.BaseDirectory) ?? Directory.GetCurrentDirectory()));
        return File.Exists(Path.Combine(repository, Inventory))
            ? repository
            : throw args.Invalid($"'{repository}' is not the repository root: it has no '{Inventory}'. Run from the repository or pass --repository.");
    }
}

/// <summary>
/// Describes and packs the containers the import produces: <c>daggerfall-data.rpak</c> from the staging tree
/// and, when music was supplied, <c>music.rpak</c> from its tracks.
/// </summary>
internal static class DataContainers
{
    public const string DescriptorKind = "worldrpg.data-container";

    /// <summary>
    /// The content packs whose payloads the import derives. Their descriptors travel in the data container's
    /// description, beside the payloads they name, rather than as tracked files.
    /// </summary>
    internal static IReadOnlyList<JsonObject> DerivedPacks()
    {
        List<JsonObject> packs =
        [
            Pack("daggerfall.imported", "daggerfall.imported", dependsOnBase: false),
            Pack("daggerfall.blocks", "daggerfall.blocks"),
            Pack("daggerfall.quests.fighters", "daggerfall.fighters-guild-quest-corpus", "Retained Fighters Guild quest corpus receipt."),
            .. ClassicQuestCorpusPublication.Specifications.Select(specification => Pack($"daggerfall.quests.{specification.Id}", "daggerfall.quest-corpus")),
            .. NamedQuestCorpusPublication.Selections.Keys.Select(id => Pack($"daggerfall.quests.{id}", "daggerfall.named-quest-corpus")),
        ];
        return [.. packs.OrderBy(pack => (string)pack["id"]!, StringComparer.Ordinal)];

        static JsonObject Pack(string id, string role, string? description = null, bool dependsOnBase = true)
        {
            JsonObject pack = new()
            {
                ["kind"] = "worldrpg.content-pack",
                ["id"] = id,
                ["ruleset"] = "daggerfall",
                ["role"] = role,
                ["dependencies"] = dependsOnBase ? new JsonArray(new JsonObject { ["id"] = "daggerfall.base" }) : new JsonArray(),
            };
            if (description is not null) pack["description"] = description;
            pack["payload"] = $"worldrpg/payloads/{id}.json";
            return pack;
        }
    }

    /// <summary>
    /// Writes the data container's description into the staging tree: the importer revision the container was
    /// produced by, how its content departs from one whole import at that revision, and the descriptors of the
    /// derived packs. A pack whose payload the staging tree lacks is refused by name, since the container would
    /// otherwise describe content it does not carry.
    /// </summary>
    /// <param name="repack">The command that repacked files an earlier run staged, or null for a full import.</param>
    internal static void WriteDescriptor(string staging, string? repack = null)
    {
        IReadOnlyList<JsonObject> packs = DerivedPacks();
        string[] absent = [.. packs.Select(pack => (string)pack["payload"]!).Where(payload => !File.Exists(Path.Combine(staging, payload)))];
        if (absent.Length != 0)
            throw new InvalidOperationException($"The staging tree lacks the derived payloads [{string.Join(", ", absent)}]; run the import before packing.");
        string[] departures = [.. Repacked(repack)];
        Describe(staging, packs, departures);
        Console.WriteLine($"data container: importer revision {ImporterBuild.Revision}, {packs.Count} derived content packs described"
            + (departures.Length == 0 ? "" : $"; it departs from a whole import, so it reads as stale: {string.Join("; ", departures)}"));
    }

    /// <summary>
    /// Writes the music container's description beside its tracks: the importer revision that staged them, and
    /// no content packs, since the tracks are read by name rather than through a pack. A music container packed
    /// from recordings by other means carries only the tracks and is read the same way.
    /// </summary>
    internal static void WriteMusicDescriptor(string tracks, string? repack = null)
    {
        Describe(tracks, [], [.. Repacked(repack)]);
        Console.WriteLine($"music container: importer revision {ImporterBuild.Revision} described");
    }

    /// <summary>
    /// A repack over files an earlier run staged: those files may come from another importer revision, so the
    /// revision this run records no longer says what the container holds.
    /// </summary>
    private static IEnumerable<string> Repacked(string? repack) =>
        repack is null ? [] : [$"repacked by {repack} over files an earlier run staged; run the full import"];

    private static void Describe(string root, IReadOnlyList<JsonObject> packs, IReadOnlyList<string> departures)
    {
        JsonObject descriptor = new()
        {
            ["kind"] = DescriptorKind,
            ["importerRevision"] = ImporterBuild.Revision,
            ["departures"] = new JsonArray([.. departures.Select(departure => JsonValue.Create(departure))]),
            ["contentPacks"] = new JsonArray([.. packs]),
        };
        PayloadFiles.WriteFile(Path.Combine(root, ImportLayout.DescriptorPath),
            JsonSerializer.SerializeToUtf8Bytes(descriptor, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>
    /// Describes and packs the data container from the staging tree, and the music container from its tracks
    /// when they were staged, then opens each through the Engine and checks that it carries exactly the staged
    /// files.
    /// </summary>
    /// <param name="repack">The command that repacked files an earlier run staged, or null for a full import.</param>
    internal static void Pack(bool compress, string? install, string? repack)
    {
        ImportLayout.RefuseLinkedOutputs(Directory.GetCurrentDirectory(), install);
        WriteDescriptor(ImportLayout.Staging, repack);
        if (Directory.Exists(ImportLayout.MusicTracks)) WriteMusicDescriptor(ImportLayout.MusicTracks, repack);
        using EngineTestHost host = EngineTestHost.Create(new EngineTestHostOptions());
        PackOne(host, ImportLayout.Staging, ImportLayout.Installed(ImportLayout.DataContainer, install), compress);
        if (Directory.Exists(ImportLayout.MusicTracks))
            PackOne(host, ImportLayout.MusicTracks, ImportLayout.Installed(ImportLayout.MusicContainer, install), compress);
        else
            Console.WriteLine($"music container: no tracks were staged, so '{ImportLayout.Installed(ImportLayout.MusicContainer, install)}' is left as it is; the product plays no score without one");
    }

    /// <summary>Packs one directory into one container and checks the container against the directory.</summary>
    internal static void PackOne(EngineTestHost host, string directory, string output, bool compress)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException($"There is nothing to pack at '{directory}'; run the import first.");
        long started = Environment.TickCount64;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        host.Call(engine => ProductContentBundle.PackContainer(engine.Content, directory, output, compress));
        Dictionary<string, ContentReferenceInfo> entries = host.Call(engine =>
        {
            using ProductContentBundle container = ProductContentBundle.OpenContainer(engine.Content, output);
            return container.Entries.ToArray().ToDictionary(entry => entry.Path, StringComparer.Ordinal);
        });

        string[] files = [.. Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)];
        List<string> disagreements = [];
        foreach (string file in files)
        {
            string path = Path.GetRelativePath(directory, file).Replace('\\', '/');
            if (!entries.Remove(path, out ContentReferenceInfo entry))
            {
                disagreements.Add($"'{path}' is not in the container");
                continue;
            }

            byte[] bytes = File.ReadAllBytes(file);
            if (entry.ByteLength != (ulong)bytes.LongLength || entry.Sha256 != Sha256(bytes))
                disagreements.Add($"'{path}' differs from the staged file");
        }

        disagreements.AddRange(entries.Keys.Order(StringComparer.Ordinal).Select(path => $"'{path}' was not staged"));
        if (disagreements.Count != 0)
            throw new InvalidOperationException($"The container '{output}' does not carry the files staged at '{directory}': {string.Join("; ", disagreements.Take(20))}.");
        Console.WriteLine($"packed {output}: {files.Length} entries, {new FileInfo(output).Length} bytes{(compress ? " (compressed)" : "")}, in {(Environment.TickCount64 - started) / 1000.0:F1}s");
    }

    private static ContentSha256 Sha256(ReadOnlySpan<byte> bytes)
    {
        Span<byte> digest = stackalloc byte[32];
        SHA256.HashData(bytes, digest);
        return new ContentSha256(
            System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(digest[..8]),
            System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(digest[8..16]),
            System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(digest[16..24]),
            System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(digest[24..]));
    }
}
