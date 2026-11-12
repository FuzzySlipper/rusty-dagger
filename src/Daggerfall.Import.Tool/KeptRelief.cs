using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Daggerfall.Import.Relief;

namespace Daggerfall.Import.Tool;

/// <summary>
/// What a relief bake was baked from: the bake's own source (<see cref="ImporterBuild.ReliefBakeRevision"/>), the stages
/// and river threshold it was given, and digests of its input's heights, rain and places, with the SHA-256 of the two
/// files it published. Two bakes with the same inputs give the same bytes, on any machine and .NET runtime we use, so a
/// kept bake whose record names this bake's inputs is the bake this import would make. Nothing in it names a machine.
/// </summary>
internal sealed record ReliefRecord(string Code, string Settings, string Heights, string Rain, string Places, string Relief = "", string Rivers = "")
{
    private static readonly (string Field, string Name, Func<ReliefRecord, string> Value)[] Inputs =
    [
        ("code", "the bake's code (src/Daggerfall.Import/Relief)", record => record.Code),
        ("settings", "the bake's stages and river threshold", record => record.Settings),
        ("heights", "the heights from WOODS.WLD", record => record.Heights),
        ("rain", "the rain from the climate section", record => record.Rain),
        ("places", "the places from the locations section", record => record.Places),
    ];

    /// <summary>The record of a bake of <paramref name="input"/> through <paramref name="stages"/>, by this build of the bake.</summary>
    public static ReliefRecord Of(ReliefInput input, IReadOnlyList<ReliefStage> stages, double sourceSquareKilometres)
    {
        string places = string.Concat(input.Places.Select(place => $"{place.MapPixelX},{place.MapPixelY},{place.Blocks}\n"));
        return new(ImporterBuild.ReliefBakeRevision, SettingsOf(stages, sourceSquareKilometres),
            Digest(input.Width, input.Height, input.Classic), Digest(input.Width, input.Height, input.Rain), Digest(Encoding.UTF8.GetBytes(places)));
    }

    /// <summary>How the record names the stages and river threshold a bake was given.</summary>
    public static string SettingsOf(IReadOnlyList<ReliefStage> stages, double sourceSquareKilometres) =>
        $"stages {string.Join(' ', stages.Select(stage => $"{stage.NodesPerPixel}/{stage.Rounds}/{stage.Steps}"))}, rivers from {sourceSquareKilometres.ToString(CultureInfo.InvariantCulture)} km2";

    /// <summary>Each input whose value differs from <paramref name="kept"/>'s, named, with both values; none when the bake is the same.</summary>
    public IReadOnlyList<string> Changed(ReliefRecord kept) =>
        [.. Inputs.Where(input => input.Value(this) != input.Value(kept))
            .Select(input => $"{input.Name} changed (recorded {Short(input.Value(kept))}, now {Short(input.Value(this))})")];

    public JsonObject ToJson()
    {
        JsonObject json = [];
        foreach ((string field, _, Func<ReliefRecord, string> value) in Inputs) json[field] = value(this);
        json["relief"] = Relief;
        json["rivers"] = Rivers;
        return json;
    }

    /// <summary>A record read back, refusing one that lacks a field.</summary>
    public static ReliefRecord FromJson(JsonNode? json)
    {
        string Field(string name) => json?[name]?.GetValue<string>() ?? throw new InvalidDataException($"it has no '{name}'");
        return new(Field("code"), Field("settings"), Field("heights"), Field("rain"), Field("places"), Field("relief"), Field("rivers"));
    }

    public static string Digest(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static string Digest(int width, int height, double[] field)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes($"{width}x{height}\n"));
        hash.AppendData(MemoryMarshal.AsBytes(field.AsSpan()));
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static string Short(string value) => value.Length == 64 && value.All(char.IsAsciiHexDigitLower) ? value[..12] : $"'{value}'";
}

/// <summary>
/// The relief bake the repository tracks, in <see cref="ImportLayout.KeptRelief"/>: the two published files and the
/// <see cref="ReliefRecord"/> of what they were baked from. An import takes them only when the record names exactly the
/// bake it would make and the files are the ones recorded; anything else, a missing or unreadable record included, is
/// baked again, the line it prints says why, and the new bake replaces the tracked one, which is how it is refreshed.
/// </summary>
internal static class KeptRelief
{
    public const string RecordFile = "record.json";
    private const string ReliefFile = "relief.bin", RiversFile = "rivers.bin";

    /// <summary>The bake's relief and rivers when its record matches <paramref name="wanted"/>; otherwise null, and why not.</summary>
    public static (byte[] Relief, byte[] Rivers)? Take(string directory, ReliefRecord wanted, out string why)
    {
        string record = Path.Combine(directory, RecordFile);
        if (!File.Exists(record))
        {
            why = $"{directory} holds no bake (it has no {RecordFile})";
            return null;
        }

        ReliefRecord kept;
        try
        {
            kept = ReliefRecord.FromJson(JsonNode.Parse(File.ReadAllBytes(record)));
        }
        catch (Exception error) when (error is IOException or JsonException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException)
        {
            why = $"the bake's record {record} cannot be read: {error.Message}";
            return null;
        }

        IReadOnlyList<string> changed = wanted.Changed(kept);
        if (changed.Count != 0)
        {
            why = string.Join("; ", changed);
            return null;
        }

        byte[]? relief = Read(Path.Combine(directory, ReliefFile)), rivers = Read(Path.Combine(directory, RiversFile));
        if (relief is null || ReliefRecord.Digest(relief) != kept.Relief || rivers is null || ReliefRecord.Digest(rivers) != kept.Rivers)
        {
            why = $"the files in {directory} are not the ones its record names";
            return null;
        }

        why = "";
        return (relief, rivers);
    }

    /// <summary>Keeps a bake with its record; the record goes last, so a keep cut short leaves no record and is baked again.</summary>
    public static void Keep(string directory, ReliefRecord baked, byte[] relief, byte[] rivers)
    {
        Directory.CreateDirectory(directory);
        File.Delete(Path.Combine(directory, RecordFile));
        File.WriteAllBytes(Path.Combine(directory, ReliefFile), relief);
        File.WriteAllBytes(Path.Combine(directory, RiversFile), rivers);
        JsonObject record = (baked with { Relief = ReliefRecord.Digest(relief), Rivers = ReliefRecord.Digest(rivers) }).ToJson();
        File.WriteAllText(Path.Combine(directory, RecordFile), record.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
    }

    private static byte[]? Read(string path) => File.Exists(path) ? File.ReadAllBytes(path) : null;
}
