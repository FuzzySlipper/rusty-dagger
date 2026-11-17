using System.Text.Json;
using System.Text.Json.Nodes;

namespace Daggerfall.Import.Tool;

/// <summary>
/// The classic media the product's art pack draws anew, which a player's data container leaves out: refreshed art never
/// opens it, and only the developer's classic-art comparison draws it. The import moves those staged files into a tree of
/// their own before the data container is packed, so <c>daggerfall-data.rpak</c> is the same bytes whether or not the
/// developer's <c>classic-art.rpak</c> is packed from that tree beside it.
/// </summary>
/// <remarks>
/// Which entries are drawn anew is the art pack's to say, per entry: <c>authoring/tools/shared/promote.py</c> writes their
/// classic identities to <see cref="ListFile"/>, an importer input. The sky's original panoramas are left out by this
/// importer's own rule, as the refreshed sky draws only the cleared ones. The manifests and inventories stay whole, naming
/// every file, so a left-out file is found by the same path in the classic container.
/// </remarks>
internal static class ClassicArt
{
    /// <summary>The classic identities the art pack draws anew, by kind, as promotion writes them.</summary>
    public const string ListFile = $"{ImportLayout.Inputs}/replaced-classic.json";

    /// <summary>The staged files the data container leaves out, at their staging-relative paths.</summary>
    public const string Tree = "local/build/import/classic-art";

    public const string Container = "local/data/classic-art.rpak";

    /// <summary>The kinds of texture record the list names, each published as one or more files of the record.</summary>
    private static readonly string[] RecordKinds = ["surfaces", "flats", "people", "meshes"];

    /// <summary>The list, as the data container's description records what it left out.</summary>
    internal static JsonObject LeftOut(string repository)
    {
        JsonObject list = JsonNode.Parse(File.ReadAllBytes(Path.Combine(repository, ListFile)))!.AsObject();
        JsonObject leftOut = [];
        foreach (string kind in (string[])["media", .. RecordKinds, "models"])
            leftOut[kind] = list[kind]?.DeepClone() ?? throw new InvalidDataException($"'{ListFile}' has no '{kind}' list.");
        leftOut["sky"] = "the original panoramas, day and night; the cleared ones, moons and stars stay";
        return leftOut;
    }

    /// <summary>
    /// Moves every staged file of an entry the art pack draws anew from the repository's staging tree into its
    /// <see cref="Tree"/>, replacing what an earlier run left there, and returns how many files and bytes it moved.
    /// </summary>
    internal static (int Files, long Bytes) Split(string repository)
    {
        string staging = Path.Combine(repository, ImportLayout.Staging);
        JsonObject list = JsonNode.Parse(File.ReadAllBytes(Path.Combine(repository, ListFile)))!.AsObject();
        HashSet<string> Names(string kind) => [.. (list[kind]?.AsArray() ?? throw new InvalidDataException($"'{ListFile}' has no '{kind}' list."))
            .Select(value => value!.GetValue<string>())];
        HashSet<string> media = Names("media");
        Dictionary<string, HashSet<string>> records = RecordKinds.ToDictionary(kind => kind, Names);
        HashSet<string> models = Names("models");

        int files = 0;
        long bytes = 0;
        foreach (string path in Files(staging, media, records, models).Distinct(StringComparer.Ordinal))
        {
            string from = Path.Combine(staging, path), to = Path.Combine(repository, Tree, path);
            // A step rerun with --only stages a file again; one it did not rerun was moved by an earlier run.
            if (!File.Exists(from)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            bytes += new FileInfo(from).Length;
            File.Move(from, to, overwrite: true);
            files++;
        }
        return (files, bytes);
    }

    /// <summary>The staging-relative paths of the files the list's entries and the sky's originals are published as.</summary>
    private static IEnumerable<string> Files(string staging, HashSet<string> media, Dictionary<string, HashSet<string>> records, HashSet<string> models)
    {
        // Published UI and character art, by the media identity its inventory states. A worn record's images are its dyes and
        // metals and its mask, which the art pack draws for itself. A spell atlas is drawn only for a cell without its own image.
        bool Drawn(string id) => media.Contains(id)
            || id.StartsWith("paper-doll.", StringComparison.Ordinal)
                && id.Split('.') is { Length: 4 } parts && media.Contains(string.Join('.', parts[..3]))
            || id == "spell.icons" && Enumerable.Range(0, 69).All(cell => media.Contains($"spell.icon.{cell}"))
            || id == "spell.selectors" && Enumerable.Range(0, 5).All(cell => media.Contains($"spell.target.{cell}") && media.Contains($"spell.element.{cell}"));
        foreach (string inventory in new[] { "worldrpg/media/classic-media-inventory.json", "worldrpg/media/character/character-media-inventory.json",
                     "worldrpg/media/character/paper-doll/paper-doll-inventory.json" })
            foreach (JsonElement artifact in Read(staging, inventory).GetProperty("artifacts").EnumerateArray())
                if (artifact.TryGetProperty("mediaId", out JsonElement id) && id.GetString() is { } mediaId && Drawn(mediaId))
                    yield return artifact.GetProperty("path").GetString()!;

        // World media by texture record: a surface is drawn anew as a material (with its frames and its window glass, which a
        // refreshed window carries for itself) and a ground tile; a flat, a person or a sprite mesh as a billboard; a corpse a
        // mesh replaces as each creature's corpse atlas cut from it.
        const string shared = "worldrpg/imports/shared";
        JsonElement dungeon = Read(staging, $"{shared}/media/dungeon/manifest.json");
        foreach (JsonElement resource in dungeon.GetProperty("media").GetProperty("resources").EnumerateArray())
        {
            string id = resource.GetProperty("id").GetString()!;
            string? kind = id.StartsWith("material/", StringComparison.Ordinal) ? "surfaces"
                : id.StartsWith("terrain/", StringComparison.Ordinal) ? "surfaces"
                : id.StartsWith("sprite/texture-", StringComparison.Ordinal) ? "billboard"
                : null;
            if (kind is null || Record(id) is not { } record) continue;
            if (kind == "surfaces" ? records["surfaces"].Contains(record)
                : records["flats"].Contains(record) || records["people"].Contains(record) || records["meshes"].Contains(record))
                yield return $"{shared}/{resource.GetProperty("relativePath").GetString()}";
        }
        foreach (JsonElement actor in dungeon.GetProperty("actors").EnumerateArray())
            if (actor.TryGetProperty("corpse", out JsonElement corpse) && corpse.ValueKind == JsonValueKind.Object
                && records["meshes"].Contains($"{corpse.GetProperty("textureArchive").GetInt32()}-{corpse.GetProperty("textureRecord").GetInt32()}"))
                yield return $"{shared}/media/dungeon/actors/mobile-{actor.GetProperty("mobileId").GetInt32()}-corpse.png";

        // A model a remodel stands in for, by its ARCH3D record.
        foreach (JsonElement mesh in Read(staging, $"{shared}/geometry/index.json").GetProperty("meshes").EnumerateArray())
            if (models.Contains(mesh.GetProperty("meshId").GetString()!))
                yield return $"{shared}/{mesh.GetProperty("relativePath").GetString()}";

        // The sky's painted panoramas, which the refreshed sky draws cleared over the real horizon.
        JsonElement sky = Read(staging, "worldrpg/media/sky/manifest.json");
        foreach (string section in new[] { "resources", "nightResources" })
            foreach (JsonElement resource in sky.GetProperty(section).EnumerateArray())
                yield return $"worldrpg/{resource.GetProperty("relativePath").GetString()}";
    }

    /// <summary>The texture record a published resource id names (<c>material/texture-A-R-frame-N</c> is record A-R).</summary>
    private static string? Record(string id)
    {
        int at = id.IndexOf("texture-", StringComparison.Ordinal);
        if (at < 0) return null;
        string[] parts = id[(at + "texture-".Length)..].Split('-');
        return parts.Length >= 2 && int.TryParse(parts[0], out _) && int.TryParse(parts[1], out _) ? $"{parts[0]}-{parts[1]}" : null;
    }

    private static JsonElement Read(string staging, string path) => JsonDocument.Parse(File.ReadAllBytes(Path.Combine(staging, path))).RootElement;
}
