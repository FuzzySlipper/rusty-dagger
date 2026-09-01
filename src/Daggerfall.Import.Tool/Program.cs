using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Daggerfall.Import.Normalization;
using Daggerfall.Import.Publication;
using Daggerfall.Import.Normalized;

namespace Daggerfall.Import.Tool;

internal static class Program
{
    private const long MaximumIndividualSourceBytes = 128L * 1024L * 1024L;
    private const long MaximumTotalSourceBytes = 512L * 1024L * 1024L;

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length != 0 && args[0].StartsWith("sprite-", StringComparison.Ordinal))
            {
                return RunSpriteCommand(args);
            }

            ToolOptions options = ToolOptions.Parse(args);
            ImportPublicationPlan plan = BuildPlan(options);
            switch (options.Command)
            {
                case ToolCommand.Plan:
                    PrintPlan(plan.Compare(options.OutputDirectory));
                    return 0;
                case ToolCommand.Write:
                    PrintPlan(ImportPublicationWriter.Write(plan, options.OutputDirectory));
                    return 0;
                case ToolCommand.VerifyRealData:
                    VerifyDeterminism(plan, options);
                    return 0;
                default:
                    throw new InvalidOperationException("The import command is not known.");
            }
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException or FormatException)
        {
            Console.Error.WriteLine($"daggerfall-import-tool: {exception.Message}");
            return 1;
        }
    }

    private static int RunSpriteCommand(IReadOnlyList<string> args)
    {
        SpriteToolOptions options = SpriteToolOptions.Parse(args);
        if (options.Command == SpriteToolCommand.OverlayDiscard)
        {
            Console.WriteLine(SpriteAuthoredOverlayStore.Discard(options.PublicationDirectory, options.AuthoringDirectory!, options.OverlayPath!)
                ? "sprite overlay moved to its .discarded recovery path"
                : "sprite overlay was not present");
            return 0;
        }

        SpritePublicationSnapshot publication = SpritePublicationReader.Read(options.PublicationDirectory);
        switch (options.Command)
        {
            case SpriteToolCommand.List:
            {
                IEnumerable<SpriteInspectionEntry> entries = publication.Catalog.Entries;
                if (options.Kind is not null)
                {
                    entries = entries.Where(entry => entry.Kind == options.Kind.Value);
                }

                PrintJson(entries.OrderBy(entry => entry.Id, StringComparer.Ordinal).ToArray());
                return 0;
            }
            case SpriteToolCommand.Show:
                PrintJson(publication.Catalog.Require(options.Id!));
                return 0;
            case SpriteToolCommand.OverlayValidate:
            {
                SpriteAuthoredOverlayStore.ValidateRootSeparation(options.PublicationDirectory, options.AuthoringDirectory!);
                SpriteAuthoredOverlayDocument overlay = ReadOverlay(options.AuthoringDirectory!, options.OverlayPath!);
                SpriteAuthoredOverlayStore.Validate(overlay, publication.Catalog, publication.AuthoringBasisDigest);
                Console.WriteLine("sprite overlay is valid");
                return 0;
            }
            case SpriteToolCommand.OverlayWrite:
            {
                SpriteAuthoredOverlayDocument overlay = ReadExternalOverlay(options.InputPath!);
                SpriteAuthoredOverlayStore.Write(options.PublicationDirectory, options.AuthoringDirectory!, options.OverlayPath!, overlay, publication.Catalog, publication.AuthoringBasisDigest);
                Console.WriteLine("sprite overlay written; later regeneration may pass this typed document through SpriteAuthoredOverlayStore.ToMediaOverlays.");
                return 0;
            }
            default:
                throw new InvalidOperationException("The sprite command is not known.");
        }
    }

    private static SpriteAuthoredOverlayDocument ReadOverlay(string authoringDirectory, string relativePath)
    {
        string path = SpriteAuthoredOverlayStore.ResolveRelativePath(authoringDirectory, relativePath);
        return ReadExternalOverlay(path);
    }

    private static SpriteAuthoredOverlayDocument ReadExternalOverlay(string path)
    {
        FileInfo file = new(Path.GetFullPath(path));
        if (!file.Exists || file.Length is <= 0 or > 1024 * 1024)
        {
            throw new FormatException("The sprite overlay input is missing or outside its byte quota.");
        }

        byte[] bytes = File.ReadAllBytes(file.FullName);
        if (bytes.LongLength != file.Length)
        {
            throw new IOException("The sprite overlay input changed while it was being read.");
        }

        return SpriteAuthoredOverlayStore.Read(bytes);
    }

    private static readonly JsonSerializerOptions SpriteJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private static void PrintJson<T>(T value) => Console.WriteLine(JsonSerializer.Serialize(value, SpriteJsonOptions));

    private static ImportPublicationPlan BuildPlan(ToolOptions options)
    {
        if (options.SpriteAuthoringDirectory is null)
        {
            return BuildPlanCore(options, [], []);
        }

        SpriteAuthoredOverlayStore.ValidateRootSeparation(options.OutputDirectory, options.SpriteAuthoringDirectory);
        ImportPublicationPlan currentInputs = BuildPlanCore(options, [], []);
        SpritePublicationSnapshot current = SpritePublicationReader.FromPlan(currentInputs);
        SpriteAuthoredOverlayDocument overlay = ReadOverlay(options.SpriteAuthoringDirectory, options.SpriteOverlayPath!);
        IReadOnlyList<AuthoredMediaOverlay> values = SpriteAuthoredOverlayStore.ToMediaOverlays(overlay, current.Catalog, current.AuthoringBasisDigest);

        HashSet<string> dungeonIds = current.Catalog.Entries
            .Where(entry => entry.Kind is SpriteInspectionKind.DungeonBillboard or SpriteInspectionKind.DungeonActor or SpriteInspectionKind.DungeonCorpse)
            .Select(entry => entry.Id)
            .ToHashSet(StringComparer.Ordinal);
        return BuildPlanCore(options, values.Where(value => dungeonIds.Contains(value.Id)).ToArray(), values.Where(value => !dungeonIds.Contains(value.Id)).ToArray());
    }

    private static ImportPublicationPlan BuildPlanCore(ToolOptions options, IReadOnlyList<AuthoredMediaOverlay> dungeonOverlays, IReadOnlyList<AuthoredMediaOverlay> classicOverlays)
    {
        AdmittedArena2Sources sources = new(options.Arena2Directory);
        LoadRequiredDungeonSources(sources);
        LoadClassicMediaSources(sources);
        while (true)
        {
            try
            {
                DungeonNormalizationRequest request = new(
                    new DungeonLogicalSourceSet(sources.DungeonSources),
                    options.Region,
                    options.Location,
                    options.TextureTableMode,
                    DungeonNormalizationQuotas.Default with { MaximumSourceBytes = MaximumTotalSourceBytes });
                DungeonNormalizationResult result = DungeonNormalizer.Normalize(request);

                Arena2DungeonMediaPublication dungeonMedia = Arena2DungeonMediaPublication.Create(
                    Arena2DungeonMediaRequest.Create(result.Document, new Arena2DungeonMediaSourceSet(sources.DungeonMediaSources)) with { AuthoredOverlays = dungeonOverlays });
                Arena2ClassicMediaPublication classicMedia = Arena2ClassicMediaPublication.Create(
                    sources.ClassicMediaInputs,
                    options.ClassicMediaProfile with { AuthoredOverlays = classicOverlays },
                    new Arena2ClassicMediaPublicationOptions(MaximumSourceBytes: MaximumIndividualSourceBytes));
                return Arena2MediaBundlePublication.Create(result, dungeonMedia, classicMedia).Plan;
            }
            catch (InvalidOperationException exception) when (TryRequiredTexture(exception.Message, out string? textureName))
            {
                sources.LoadDungeon(textureName!);
            }
            catch (InvalidOperationException exception) when (TryRequiredDungeonMediaTexture(exception.Message, out string? textureName))
            {
                sources.LoadDungeon(textureName!);
            }
            catch (InvalidOperationException exception) when (TryMissingDungeonMediaTextures(exception.Message, out IReadOnlyList<string>? textureNames))
            {
                foreach (string textureName in textureNames!)
                {
                    sources.LoadDungeon(textureName);
                }
            }
        }
    }

    private static void LoadRequiredDungeonSources(AdmittedArena2Sources sources)
    {
        foreach (string name in DungeonSourceNames)
        {
            sources.LoadDungeon(name);
        }
    }

    private static void LoadClassicMediaSources(AdmittedArena2Sources sources)
    {
        foreach (string name in ClassicMediaSourceNames)
        {
            sources.LoadClassic(name);
        }
    }

    private static DungeonLogicalSource ReadSource(string arena2Directory, string fileName)
    {
        if (!IsAdmittedSourceName(fileName))
        {
            throw new InvalidOperationException($"'{fileName}' is not an admitted Arena2 source name.");
        }

        string sourcePath = Path.Combine(arena2Directory, fileName);
        FileInfo info = new(sourcePath);
        if (!info.Exists)
        {
            throw new FileNotFoundException($"Required Arena2 source '{fileName}' was not found.", sourcePath);
        }

        if (info.Length is <= 0 or > MaximumIndividualSourceBytes)
        {
            throw new InvalidOperationException($"Arena2 source '{fileName}' is outside the permitted byte range.");
        }

        byte[] bytes = File.ReadAllBytes(sourcePath);
        if (bytes.LongLength != info.Length)
        {
            throw new IOException($"Arena2 source '{fileName}' changed while it was being read.");
        }

        return new DungeonLogicalSource($"arena2/{fileName}", bytes);
    }

    private static bool IsAdmittedSourceName(string value) => DungeonSourceNames.Contains(value, StringComparer.Ordinal)
        || ClassicMediaSourceNames.Contains(value, StringComparer.Ordinal)
        || IsTextureLeaf(value);

    private static bool IsTextureLeaf(string value) => value.Length == "TEXTURE.000".Length
        && value.StartsWith("TEXTURE.", StringComparison.Ordinal)
        && value[8..].All(char.IsAsciiDigit);

    private static bool TryRequiredTexture(string message, out string? textureName)
    {
        const string prefix = "Dungeon normalization requires logical source '";
        textureName = null;
        if (!message.StartsWith(prefix, StringComparison.Ordinal) || !message.EndsWith("'.", StringComparison.Ordinal))
        {
            return false;
        }

        string candidate = message[prefix.Length..^2];
        if (!IsTextureLeaf(candidate))
        {
            return false;
        }

        textureName = candidate;
        return true;
    }

    private static bool TryRequiredDungeonMediaTexture(string message, out string? textureName)
    {
        const string prefix = "Arena2 dungeon media requires ";
        textureName = null;
        if (!message.StartsWith(prefix, StringComparison.Ordinal) || !message.EndsWith(".", StringComparison.Ordinal))
        {
            return false;
        }

        string candidate = message[prefix.Length..^1];
        if (!IsTextureLeaf(candidate))
        {
            return false;
        }

        textureName = candidate;
        return true;
    }

    private static bool TryMissingDungeonMediaTextures(string message, out IReadOnlyList<string>? textureNames)
    {
        const string prefix = "Arena2 dungeon media texture closure does not match normalized references. Missing: [";
        const string separator = "]. Unneeded: [";
        textureNames = null;
        if (!message.StartsWith(prefix, StringComparison.Ordinal) || !message.EndsWith("].", StringComparison.Ordinal))
        {
            return false;
        }

        int separatorIndex = message.IndexOf(separator, prefix.Length, StringComparison.Ordinal);
        if (separatorIndex < 0)
        {
            return false;
        }

        string unneeded = message[(separatorIndex + separator.Length)..^2];
        if (unneeded.Length != 0)
        {
            return false;
        }

        string missing = message[prefix.Length..separatorIndex];
        string[] names = missing.Length == 0 ? [] : missing.Split(", ", StringSplitOptions.None);
        if (names.Length == 0 || names.Any(name => !IsTextureLeaf(name)))
        {
            return false;
        }

        textureNames = names.OrderBy(name => name, StringComparer.Ordinal).ToArray();
        return true;
    }

    /// <summary>
    /// Reads the tracked authored UI input through two explicit caller paths.
    /// The importer itself receives only portable labels and copied bytes, so
    /// it never gains filesystem authority or discovers a directory.
    /// </summary>
    private static Arena2ClassicMediaProfile LoadClassicMediaProfile(string authoredManifestPath, string originalDirectory)
    {
        byte[] manifestBytes = ReadBoundedExternalFile(authoredManifestPath, "authored UI manifest");
        AuthoredUiAssetFileSet manifest = JsonSerializer.Deserialize<AuthoredUiAssetFileSet>(manifestBytes, AuthoredUiJsonOptions)
            ?? throw new FormatException("The authored UI manifest is empty.");
        if (manifest.SchemaVersion != AuthoredUiAssetFileSet.CurrentSchemaVersion || manifest.Assets is null || manifest.Assets.Count == 0)
        {
            throw new FormatException("The authored UI manifest schema or asset list is not supported.");
        }

        string originalsRoot = Path.GetFullPath(originalDirectory);
        if (!Directory.Exists(originalsRoot))
        {
            throw new DirectoryNotFoundException($"The authored UI original directory '{originalsRoot}' was not found.");
        }

        HashSet<string> ids = new(StringComparer.Ordinal);
        HashSet<string> outputFiles = new(StringComparer.Ordinal);
        HashSet<string> sourceFiles = new(StringComparer.Ordinal);
        long totalBytes = manifestBytes.LongLength;
        List<ClassicAuthoredUiAsset> assets = new(manifest.Assets.Count);
        foreach (AuthoredUiAssetFile asset in manifest.Assets.OrderBy(asset => asset.Id, StringComparer.Ordinal))
        {
            ArgumentNullException.ThrowIfNull(asset);
            RequirePortableLeaf(asset.File, nameof(asset.File));
            RequirePortableLeaf(asset.SourceFile, nameof(asset.SourceFile));
            if (string.IsNullOrWhiteSpace(asset.Id) || asset.Id.Any(char.IsControl)
                || string.IsNullOrWhiteSpace(asset.Generator) || string.IsNullOrWhiteSpace(asset.Prompt)
                || asset.Generator.Any(char.IsControl) || asset.Prompt.Any(char.IsControl)
                || !ids.Add(asset.Id) || !outputFiles.Add(asset.File) || !sourceFiles.Add(asset.SourceFile))
            {
                throw new FormatException("Authored UI asset IDs, output files, and source files must be unique plain values.");
            }

            string sourcePath = Path.Combine(originalsRoot, asset.SourceFile);
            if (!StringComparer.Ordinal.Equals(Path.GetFullPath(sourcePath), Path.Combine(originalsRoot, asset.SourceFile)))
            {
                throw new FormatException("An authored UI source file escaped its explicit original directory.");
            }

            byte[] bytes = ReadBoundedExternalFile(sourcePath, $"authored UI source '{asset.SourceFile}'");
            totalBytes = checked(totalBytes + bytes.LongLength);
            if (totalBytes > MaximumTotalSourceBytes)
            {
                throw new InvalidOperationException("The authored UI input exceeds the total source byte quota.");
            }

            assets.Add(new ClassicAuthoredUiAsset(
                asset.Id,
                $"media/ui/authored/{asset.File}",
                $"ui-original/{asset.SourceFile}",
                bytes,
                asset.Generator,
                asset.Prompt));
        }

        return new Arena2ClassicMediaProfile(
            AuthoredUiManifest: new ClassicAuthoredUiManifestInput("ui-authored-assets.json", manifestBytes),
            AuthoredUiAssets: assets);
    }

    private static byte[] ReadBoundedExternalFile(string path, string subject)
    {
        string fullPath = Path.GetFullPath(path);
        FileInfo info = new(fullPath);
        if (!info.Exists)
        {
            throw new FileNotFoundException($"Required {subject} '{fullPath}' was not found.", fullPath);
        }

        if (info.Length is <= 0 or > MaximumIndividualSourceBytes)
        {
            throw new InvalidOperationException($"{subject} is outside the permitted byte range.");
        }

        byte[] bytes = File.ReadAllBytes(fullPath);
        if (bytes.LongLength != info.Length)
        {
            throw new IOException($"{subject} changed while it was being read.");
        }

        return bytes;
    }

    private static void RequirePortableLeaf(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !StringComparer.Ordinal.Equals(value, Path.GetFileName(value))
            || value is "." or ".."
            || value.Contains('/') || value.Contains('\\')
            || value.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0)
        {
            throw new FormatException($"{name} must be one portable file leaf.");
        }
    }

    private static readonly JsonSerializerOptions AuthoredUiJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private sealed record AuthoredUiAssetFileSet(int SchemaVersion, IReadOnlyList<AuthoredUiAssetFile> Assets)
    {
        public const int CurrentSchemaVersion = 1;
    }

    private sealed record AuthoredUiAssetFile(string Id, string File, string SourceFile, string Generator, string Prompt);

    private static void VerifyDeterminism(ImportPublicationPlan plan, ToolOptions options)
    {
        string parent = Path.GetTempPath();
        string root = Path.Combine(parent, $"daggerfall-import-verify-{Guid.NewGuid():N}");
        string first = Path.Combine(root, "first");
        string second = Path.Combine(root, "second");
        try
        {
            ImportPublicationWriter.Write(plan, first);
            ImportPublicationWriter.Write(BuildPlan(options), second);
            IReadOnlyDictionary<string, ContentDigest> firstHashes = HashClosure(first);
            IReadOnlyDictionary<string, ContentDigest> secondHashes = HashClosure(second);
            if (firstHashes.Count != secondHashes.Count || firstHashes.Any(entry => !secondHashes.TryGetValue(entry.Key, out ContentDigest hash) || hash != entry.Value))
            {
                throw new InvalidOperationException("Repeated real-data import did not produce the same publication closure.");
            }

            Console.WriteLine($"verified deterministic publication ({firstHashes.Count} artifacts)");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static IReadOnlyDictionary<string, ContentDigest> HashClosure(string directory) => Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
        .OrderBy(path => path, StringComparer.Ordinal)
        .ToDictionary(
            path => Path.GetRelativePath(directory, path).Replace(Path.DirectorySeparatorChar, '/'),
            path => ContentDigest.Compute(File.ReadAllBytes(path)),
            StringComparer.Ordinal);

    private static void PrintPlan(ImportPublicationComparison comparison)
    {
        if (comparison.IsNoOp)
        {
            Console.WriteLine("publication is current");
            return;
        }

        Console.WriteLine($"publication changes: missing={comparison.Missing.Count}, changed={comparison.Changed.Count}, unexpected={comparison.Unexpected.Count}");
    }

    private enum ToolCommand
    {
        Plan,
        Write,
        VerifyRealData,
    }

    private enum SpriteToolCommand
    {
        List,
        Show,
        OverlayValidate,
        OverlayWrite,
        OverlayDiscard,
    }

    private sealed record SpriteToolOptions(
        SpriteToolCommand Command,
        string PublicationDirectory,
        string? AuthoringDirectory,
        string? Id,
        string? OverlayPath,
        string? InputPath,
        SpriteInspectionKind? Kind)
    {
        public static SpriteToolOptions Parse(IReadOnlyList<string> args)
        {
            if (args.Count == 0)
            {
                throw new ArgumentException(Usage());
            }

            SpriteToolCommand command = args[0] switch
            {
                "sprite-list" => SpriteToolCommand.List,
                "sprite-show" => SpriteToolCommand.Show,
                "sprite-overlay-validate" => SpriteToolCommand.OverlayValidate,
                "sprite-overlay-write" => SpriteToolCommand.OverlayWrite,
                "sprite-overlay-discard" => SpriteToolCommand.OverlayDiscard,
                _ => throw new ArgumentException(Usage()),
            };
            Dictionary<string, string> values = [];
            for (int index = 1; index < args.Count; index += 2)
            {
                if (index + 1 >= args.Count || !args[index].StartsWith("--", StringComparison.Ordinal) || !values.TryAdd(args[index], args[index + 1]))
                {
                    throw new ArgumentException(Usage());
                }
            }

            values.TryGetValue("--publication", out string? publication);
            if (string.IsNullOrWhiteSpace(publication))
            {
                throw new ArgumentException(Usage());
            }

            string[] required = command switch
            {
                SpriteToolCommand.List => ["--publication"],
                SpriteToolCommand.Show => ["--publication", "--id"],
                SpriteToolCommand.OverlayValidate => ["--publication", "--authoring", "--overlay"],
                SpriteToolCommand.OverlayWrite => ["--publication", "--authoring", "--overlay", "--input"],
                SpriteToolCommand.OverlayDiscard => ["--publication", "--authoring", "--overlay"],
                _ => throw new InvalidOperationException(),
            };
            bool listWithKind = command == SpriteToolCommand.List && values.ContainsKey("--kind");
            if (values.Count != required.Length + (listWithKind ? 1 : 0) || required.Any(key => !values.ContainsKey(key)))
            {
                throw new ArgumentException(Usage());
            }

            SpriteInspectionKind? kind = null;
            if (listWithKind && (!Enum.TryParse(values["--kind"], ignoreCase: true, out SpriteInspectionKind parsedKind) || !Enum.IsDefined(parsedKind)))
            {
                throw new ArgumentException("--kind must name a known sprite inspection kind.");
            }
            else if (listWithKind)
            {
                kind = Enum.Parse<SpriteInspectionKind>(values["--kind"], ignoreCase: true);
            }

            if (values.TryGetValue("--overlay", out string? overlay))
            {
                SpriteAuthoredOverlayStore.ValidateOverlayRelativePath(overlay);
            }

            if (values.TryGetValue("--id", out string? id) && (string.IsNullOrWhiteSpace(id) || id.Any(char.IsWhiteSpace)))
            {
                throw new ArgumentException("--id must be a non-empty whitespace-free logical ID.");
            }

            if (values.TryGetValue("--authoring", out string? authoring) && string.IsNullOrWhiteSpace(authoring))
            {
                throw new ArgumentException("--authoring must name a non-empty source directory.");
            }

            return new(command, Path.GetFullPath(publication), authoring is null ? null : Path.GetFullPath(authoring), values.GetValueOrDefault("--id"), values.GetValueOrDefault("--overlay"), values.GetValueOrDefault("--input"), kind);
        }

        private static string Usage() => "usage: daggerfall-import-tool sprite-list --publication GENERATED_DIR [--kind KIND] | sprite-show --publication GENERATED_DIR --id ID | sprite-overlay-validate --publication GENERATED_DIR --authoring SOURCE_DIR --overlay sprites/RELATIVE.json | sprite-overlay-write --publication GENERATED_DIR --authoring SOURCE_DIR --overlay sprites/RELATIVE.json --input FILE | sprite-overlay-discard --publication GENERATED_DIR --authoring SOURCE_DIR --overlay sprites/RELATIVE.json";
    }

    private sealed class AdmittedArena2Sources
    {
        private readonly string arena2Directory;
        private readonly Dictionary<string, DungeonLogicalSource> loaded = new(StringComparer.Ordinal);
        private readonly HashSet<string> dungeonSourceNames = new(StringComparer.Ordinal);
        private long totalBytes;

        public AdmittedArena2Sources(string arena2Directory)
        {
            this.arena2Directory = arena2Directory;
        }

        public IReadOnlyList<DungeonLogicalSource> DungeonSources => dungeonSourceNames
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name => loaded[name])
            .ToArray();

        /// <summary>
        /// Only PAL.PAL and the exact dynamically selected dungeon texture
        /// closure enter this source set. Classic-only TEXTURE archives never
        /// reach the dungeon media exact-closure validator.
        /// </summary>
        public IReadOnlyList<Arena2DungeonMediaSource> DungeonMediaSources => dungeonSourceNames
            .Where(name => name is "PAL.PAL" || IsTextureLeaf(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name => new Arena2DungeonMediaSource(loaded[name].Label, loaded[name].Bytes.Span))
            .ToArray();

        public Arena2ClassicMediaInputs ClassicMediaInputs => new(
            Require("WEAPON02.CIF").Bytes.ToArray(),
            Require("ART_PAL.COL").Bytes.ToArray(),
            Require("TEXTURE.380").Bytes.ToArray(),
            Require("PAL.PAL").Bytes.ToArray(),
            Require("DAGGER.SND").Bytes.ToArray(),
            Require("MAIN00I0.IMG").Bytes.ToArray(),
            Require("MAIN03I0.IMG").Bytes.ToArray(),
            Require("MAIN04I0.IMG").Bytes.ToArray(),
            Require("MAIN05I0.IMG").Bytes.ToArray(),
            Require("INVE00I0.IMG").Bytes.ToArray(),
            Require("INFO00I0.IMG").Bytes.ToArray(),
            Require("TEXTURE.207").Bytes.ToArray(),
            Require("TEXTURE.216").Bytes.ToArray(),
            Require("TEXTURE.234").Bytes.ToArray(),
            Require("TEXTURE.245").Bytes.ToArray(),
            Require("FONT0003.FNT").Bytes.ToArray());

        public void LoadDungeon(string fileName)
        {
            if (!DungeonSourceNames.Contains(fileName, StringComparer.Ordinal) && !IsTextureLeaf(fileName))
            {
                throw new InvalidOperationException($"'{fileName}' is not an admitted Arena2 dungeon source name.");
            }

            Load(fileName);
            dungeonSourceNames.Add(fileName);
        }

        public void LoadClassic(string fileName)
        {
            if (!ClassicMediaSourceNames.Contains(fileName, StringComparer.Ordinal))
            {
                throw new InvalidOperationException($"'{fileName}' is not in the fixed classic media source set.");
            }

            Load(fileName);
        }

        private DungeonLogicalSource Require(string fileName) => loaded.TryGetValue(fileName, out DungeonLogicalSource? source)
            ? source
            : throw new InvalidOperationException($"The admitted Arena2 source '{fileName}' was not loaded.");

        private void Load(string fileName)
        {
            if (loaded.ContainsKey(fileName))
            {
                return;
            }

            DungeonLogicalSource source = ReadSource(arena2Directory, fileName);
            long nextTotal = checked(totalBytes + source.Bytes.Length);
            if (nextTotal > MaximumTotalSourceBytes)
            {
                throw new InvalidOperationException("The admitted Arena2 source closure exceeds the total byte quota.");
            }

            loaded.Add(fileName, source);
            totalBytes = nextTotal;
        }
    }

    private static readonly string[] DungeonSourceNames =
    [
        "MAPS.BSA",
        "BLOCKS.BSA",
        "ARCH3D.BSA",
        "CLIMATE.PAK",
        "PAL.PAL",
    ];

    private static readonly string[] ClassicMediaSourceNames =
    [
        "ART_PAL.COL",
        "WEAPON02.CIF",
        "DAGGER.SND",
        "MAIN00I0.IMG",
        "MAIN03I0.IMG",
        "MAIN04I0.IMG",
        "MAIN05I0.IMG",
        "INVE00I0.IMG",
        "INFO00I0.IMG",
        "FONT0003.FNT",
        "TEXTURE.380",
        "TEXTURE.207",
        "TEXTURE.216",
        "TEXTURE.234",
        "TEXTURE.245",
        "PAL.PAL",
    ];

    private sealed record ToolOptions(
        ToolCommand Command,
        string Arena2Directory,
        string OutputDirectory,
        int Region,
        string Location,
        DungeonTextureTableMode TextureTableMode,
        Arena2ClassicMediaProfile ClassicMediaProfile,
        string? SpriteAuthoringDirectory,
        string? SpriteOverlayPath)
    {
        public static ToolOptions Parse(IReadOnlyList<string> args)
        {
            if (args.Count == 0 || args[0] is "--help" or "-h")
            {
                throw new ArgumentException(Usage());
            }

            ToolCommand command = args[0] switch
            {
                "plan" => ToolCommand.Plan,
                "write" => ToolCommand.Write,
                "verify-real-data" => ToolCommand.VerifyRealData,
                _ => throw new ArgumentException(Usage()),
            };
            Dictionary<string, string> values = new(StringComparer.Ordinal);
            for (int index = 1; index < args.Count; index += 2)
            {
                if (index + 1 >= args.Count || !args[index].StartsWith("--", StringComparison.Ordinal) || !values.TryAdd(args[index], args[index + 1]))
                {
                    throw new ArgumentException(Usage());
                }
            }

            string[] required = ["--arena2", "--output", "--region", "--location", "--texture-table", "--ui-authored-assets", "--ui-original"];
            bool hasSpriteAuthoring = values.ContainsKey("--sprite-authoring") || values.ContainsKey("--sprite-overlay");
            if (hasSpriteAuthoring && (!values.ContainsKey("--sprite-authoring") || !values.ContainsKey("--sprite-overlay")))
            {
                throw new ArgumentException("--sprite-authoring and --sprite-overlay must be supplied together.");
            }

            EnsureExactKeys(values, hasSpriteAuthoring ? [.. required, "--sprite-authoring", "--sprite-overlay"] : required);
            if (!int.TryParse(values["--region"], NumberStyles.None, CultureInfo.InvariantCulture, out int region) || region is < 0 or > 999)
            {
                throw new ArgumentException("--region must be an integer in 0..999.");
            }

            if (string.IsNullOrWhiteSpace(values["--location"]))
            {
                throw new ArgumentException("--location must be non-empty.");
            }

            DungeonTextureTableMode table = values["--texture-table"] switch
            {
                "classic" => DungeonTextureTableMode.Classic,
                "default" => DungeonTextureTableMode.Default,
                _ => throw new ArgumentException("--texture-table must be classic or default."),
            };
            string? spriteAuthoring = hasSpriteAuthoring ? Path.GetFullPath(values["--sprite-authoring"]) : null;
            string? spriteOverlay = hasSpriteAuthoring ? values["--sprite-overlay"] : null;
            if (spriteAuthoring is not null)
            {
                SpriteAuthoredOverlayStore.ValidateOverlayRelativePath(spriteOverlay!);
            }

            return new(
                command,
                Path.GetFullPath(values["--arena2"]),
                Path.GetFullPath(values["--output"]),
                region,
                values["--location"],
                table,
                LoadClassicMediaProfile(values["--ui-authored-assets"], values["--ui-original"]),
                spriteAuthoring,
                spriteOverlay);
        }

        private static void EnsureExactKeys(IReadOnlyDictionary<string, string> values, IReadOnlyList<string> keys)
        {
            if (values.Count != keys.Count || keys.Any(key => !values.ContainsKey(key)))
            {
                throw new ArgumentException(Usage());
            }
        }

        private static string Usage() => "usage: daggerfall-import-tool <plan|write|verify-real-data> --arena2 DIR --output DIR --region 0..999 --location NAME --texture-table classic|default --ui-authored-assets FILE --ui-original DIR [--sprite-authoring SOURCE_DIR --sprite-overlay sprites/RELATIVE.json]";
    }
}
