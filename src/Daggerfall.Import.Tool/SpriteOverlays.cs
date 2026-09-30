using System.Text.Json;
using System.Text.Json.Serialization;
using WorldRpg.SpriteAuthoring;

namespace Daggerfall.Import.Tool;

/// <summary>The options and file reads the sprite commands share.</summary>
internal static class SpriteOverlays
{
    public static readonly CommandOption Publication = CommandOption.Required("--publication", "GENERATED_DIR");
    public static readonly CommandOption Authoring = CommandOption.Required("--authoring", "SOURCE_DIR");
    public static readonly CommandOption Overlay = CommandOption.Required("--overlay", "sprites/RELATIVE.json");

    private const int MaximumOverlayBytes = 1024 * 1024;

    private static readonly JsonSerializerOptions PrintOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string PublicationDirectory(CommandArguments args) => Path.GetFullPath(args[Publication.Name]);

    public static string AuthoringDirectory(CommandArguments args)
    {
        string authoring = args[Authoring.Name];
        if (string.IsNullOrWhiteSpace(authoring)) throw new ArgumentException("--authoring must name a non-empty source directory.");
        return Path.GetFullPath(authoring);
    }

    public static string OverlayPath(CommandArguments args)
    {
        string overlay = args[Overlay.Name];
        SpriteAuthoredOverlayStore.ValidateOverlayRelativePath(overlay);
        return overlay;
    }

    /// <summary>An overlay under an authoring root, read under its byte quota.</summary>
    public static byte[] ReadBytes(string authoringDirectory, string relativePath) =>
        ReadBytes(SpriteAuthoredOverlayStore.ResolveRelativePath(authoringDirectory, relativePath));

    /// <summary>An overlay file, read under its byte quota and refused if it changed while it was read.</summary>
    public static byte[] ReadBytes(string path)
    {
        FileInfo file = new(Path.GetFullPath(path));
        if (!file.Exists || file.Length is <= 0 or > MaximumOverlayBytes)
        {
            throw new FormatException("The sprite overlay input is missing or outside its byte quota.");
        }

        byte[] bytes = File.ReadAllBytes(file.FullName);
        if (bytes.LongLength != file.Length) throw new IOException("The sprite overlay input changed while it was being read.");
        return bytes;
    }

    public static void PrintJson<T>(T value) => Console.WriteLine(JsonSerializer.Serialize(value, PrintOptions));
}
