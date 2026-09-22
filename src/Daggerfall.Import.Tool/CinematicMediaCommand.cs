using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Tool;

internal static partial class Program
{
    private static int RunCinematicMediaCommand(IReadOnlyList<string> args)
    {
        const string usage = "usage: daggerfall-import-tool cinematic-media --arena2 SOURCE_DIR --pack PACK.json --out CONTENT_ROOT [--kind vid|flc] [--update]";
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        bool update = args.Contains("--update", StringComparer.Ordinal);
        for (int index = 1; index < args.Count; index++)
        {
            if (args[index] == "--update") continue;
            if (!args[index].StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Count || !values.TryAdd(args[index], args[++index]))
                throw new ArgumentException(usage);
        }
        if (new[] { "--arena2", "--pack", "--out" }.Any(key => !values.ContainsKey(key)) ||
            values.Keys.Any(key => key is not ("--arena2" or "--pack" or "--out" or "--kind"))) throw new ArgumentException(usage);
        string kind = values.GetValueOrDefault("--kind", "vid");
        if (kind is not ("vid" or "flc")) throw new ArgumentException(usage);
        JsonObject root = JsonNode.Parse(File.ReadAllText(values["--pack"]))!.AsObject();
        JsonObject section = root["cinematics"]!.AsObject();
        JsonSerializerOptions options = new() { PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } };
        DaggerfallCinematicPack pack = section.Deserialize<DaggerfallCinematicPack>(options) ?? throw new InvalidDataException("The pack has no cinematic provenance.");
        pack.Validate();
        DaggerfallCinematicKind selection = kind == "vid" ? DaggerfallCinematicKind.Vid : DaggerfallCinematicKind.Flc;
        DaggerfallCinematicRecord[] records = pack.Cinematics.ToArray();
        string logicalRoot = "worldrpg/media/cinematics";
        for (int index = 0; index < records.Length; index++)
        {
            DaggerfallCinematicRecord record = records[index];
            if (record.Kind != selection) continue;
            Console.WriteLine($"cinematic: {record.FileName} -> {CinematicMediaPublisher.ArtifactName(record.FileName)}{(update ? "" : " (plan only)")}");
            if (!update) continue;
            CinematicMediaArtifact artifact = CinematicMediaPublisher.Publish(Path.Combine(values["--arena2"], record.FileName), record,
                Path.Combine(values["--out"], logicalRoot), logicalRoot);
            records[index] = record with { Artifact = artifact };
            Console.WriteLine($"  {artifact.Width}x{artifact.Height}, {artifact.FrameCount} frames, {artifact.DurationSeconds:F3}s, {artifact.ByteLength} bytes, audio={artifact.HasAudio}");
        }
        if (update)
        {
            // Replace this section only after every selected source was converted successfully.
            root["cinematics"] = JsonNode.Parse(JsonSerializer.Serialize(pack with { Cinematics = records }, PublishedJson.Section));
            File.WriteAllText(values["--pack"], root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
        }
        return 0;
    }
}
