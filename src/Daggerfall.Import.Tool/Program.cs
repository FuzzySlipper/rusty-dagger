using System.Globalization;
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

    private static ImportPublicationPlan BuildPlan(ToolOptions options)
    {
        List<DungeonLogicalSource> sources = LoadRequiredSources(options);
        while (true)
        {
            try
            {
                DungeonNormalizationRequest request = new(
                    new DungeonLogicalSourceSet(sources),
                    options.Region,
                    options.Location,
                    options.TextureTableMode,
                    DungeonNormalizationQuotas.Default with { MaximumSourceBytes = MaximumTotalSourceBytes });
                DungeonNormalizationResult result = DungeonNormalizer.Normalize(request);
                return ImportPublicationPlan.Create(
                    result.Document.Provenance,
                    [new ImportPublicationArtifact("normalized.json", NormalizedImportSerializer.Serialize(result.Document))]);
            }
            catch (InvalidOperationException exception) when (TryRequiredTexture(exception.Message, out string? textureName))
            {
                sources.Add(LoadSource(options.Arena2Directory, textureName!));
            }
        }
    }

    private static List<DungeonLogicalSource> LoadRequiredSources(ToolOptions options) =>
    [
        LoadSource(options.Arena2Directory, "MAPS.BSA"),
        LoadSource(options.Arena2Directory, "BLOCKS.BSA"),
        LoadSource(options.Arena2Directory, "ARCH3D.BSA"),
        LoadSource(options.Arena2Directory, "CLIMATE.PAK"),
        LoadSource(options.Arena2Directory, "PAL.PAL"),
    ];

    private static DungeonLogicalSource LoadSource(string arena2Directory, string fileName)
    {
        if (!IsKnownSourceName(fileName))
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

    private static bool IsKnownSourceName(string value) => value is "MAPS.BSA" or "BLOCKS.BSA" or "ARCH3D.BSA" or "CLIMATE.PAK" or "PAL.PAL"
        || value.Length == "TEXTURE.000".Length
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
        if (!IsKnownSourceName(candidate))
        {
            return false;
        }

        textureName = candidate;
        return true;
    }

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

    private sealed record ToolOptions(ToolCommand Command, string Arena2Directory, string OutputDirectory, int Region, string Location, DungeonTextureTableMode TextureTableMode)
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

            EnsureExactKeys(values, ["--arena2", "--output", "--region", "--location", "--texture-table"]);
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
            return new(command, Path.GetFullPath(values["--arena2"]), Path.GetFullPath(values["--output"]), region, values["--location"], table);
        }

        private static void EnsureExactKeys(IReadOnlyDictionary<string, string> values, IReadOnlyList<string> keys)
        {
            if (values.Count != keys.Count || keys.Any(key => !values.ContainsKey(key)))
            {
                throw new ArgumentException(Usage());
            }
        }

        private static string Usage() => "usage: daggerfall-import-tool <plan|write|verify-real-data> --arena2 DIR --output DIR --region 0..999 --location NAME --texture-table classic|default";
    }
}
