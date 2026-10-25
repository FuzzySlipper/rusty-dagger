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
    public const string Staging = "local/import/staging";
    public const string Records = "local/import/records";

    /// <summary>The music container's files: one recording per track at the root, by the naming scheme.</summary>
    public const string MusicTracks = "local/import/music";

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

    public const string DataContainer = "local/daggerfall-data.rpak";
    public const string MusicContainer = "local/music.rpak";

    /// <summary>Each container's own description, at its container-relative path.</summary>
    public const string DescriptorPath = "worldrpg/data-container.json";

    /// <summary>The tracked file that marks the repository root the import works in.</summary>
    public const string Inventory = "data/content-source-manifest.csv";

    /// <summary>
    /// The repository root a command works in: <c>--repository</c>, or the current directory. A directory that
    /// is not one is refused with the command's usage, since every path the import writes is below it.
    /// </summary>
    public static string RepositoryRoot(CommandArguments args)
    {
        string repository = Path.GetFullPath(args.Optional("--repository") ?? Directory.GetCurrentDirectory());
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
    /// produced by and the descriptors of the derived packs. A pack whose payload the staging tree lacks is
    /// refused by name, since the container would otherwise describe content it does not carry.
    /// </summary>
    internal static void WriteDescriptor(string staging)
    {
        IReadOnlyList<JsonObject> packs = DerivedPacks();
        string[] absent = [.. packs.Select(pack => (string)pack["payload"]!).Where(payload => !File.Exists(Path.Combine(staging, payload)))];
        if (absent.Length != 0)
            throw new InvalidOperationException($"The staging tree lacks the derived payloads [{string.Join(", ", absent)}]; run the import before packing.");
        Describe(staging, packs);
        Console.WriteLine($"data container: importer revision {ImporterBuild.Revision}, {packs.Count} derived content packs described");
    }

    /// <summary>
    /// Writes the music container's description beside its tracks: the importer revision that staged them, and
    /// no content packs, since the tracks are read by name rather than through a pack. A music container packed
    /// from recordings by other means carries only the tracks and is read the same way.
    /// </summary>
    internal static void WriteMusicDescriptor(string tracks)
    {
        Describe(tracks, []);
        Console.WriteLine($"music container: importer revision {ImporterBuild.Revision} described");
    }

    private static void Describe(string root, IReadOnlyList<JsonObject> packs)
    {
        JsonObject descriptor = new()
        {
            ["kind"] = DescriptorKind,
            ["importerRevision"] = ImporterBuild.Revision,
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
    internal static void Pack(bool compress)
    {
        WriteDescriptor(ImportLayout.Staging);
        if (Directory.Exists(ImportLayout.MusicTracks)) WriteMusicDescriptor(ImportLayout.MusicTracks);
        using EngineTestHost host = EngineTestHost.Create(new EngineTestHostOptions());
        PackOne(host, ImportLayout.Staging, ImportLayout.DataContainer, compress);
        if (Directory.Exists(ImportLayout.MusicTracks))
            PackOne(host, ImportLayout.MusicTracks, ImportLayout.MusicContainer, compress);
        else
            Console.WriteLine($"music container: no tracks were staged, so '{ImportLayout.MusicContainer}' is left as it is; the product plays no score without one");
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
