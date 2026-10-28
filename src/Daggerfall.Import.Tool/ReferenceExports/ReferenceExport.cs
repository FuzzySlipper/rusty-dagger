using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalization;
using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Tool.ReferenceExports;

/// <summary>One native, unmirrored source frame. Paths are relative to its subject directory.</summary>
internal sealed record ReferenceImage(string File, string Role, string Source, int Record, int Frame,
    int Width, int Height, int XOffset, int YOffset, IReadOnlyList<string> Colors);

/// <summary>Source decoding and simple files for an offline reference set; no product state or rendering.</summary>
internal sealed class ReferenceExport
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly Dictionary<string, string> files;
    private readonly string arena2Root;
    private readonly Dictionary<int, TextureArchive> textures = [];
    private readonly List<object> entries = [];
    private readonly List<string> warnings = [];
    private readonly List<(string Directory, string Name, string? Preview)> links = [];
    private int imageCount;

    public ReferenceExport(string arena2, string output)
    {
        Root = Path.GetFullPath(output);
        // A rerun must not overwrite the user's edited experiment images or its existing manifests.
        if (Directory.Exists(Root) && Directory.EnumerateFileSystemEntries(Root).Any())
            throw new InvalidOperationException($"Export directory '{Root}' already contains files. Choose a new --output directory to preserve existing references and experiments.");
        arena2Root = Path.GetFullPath(arena2);
        files = Directory.EnumerateFiles(arena2Root).ToDictionary(path => Path.GetFileName(path), path => path, StringComparer.OrdinalIgnoreCase);
        Palette = PaletteDecoder.Decode(Read("ART_PAL.COL"), "arena2/ART_PAL.COL");
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }
    public Arena2Palette Palette { get; }
    public byte[] Read(string file) => File.ReadAllBytes(files.GetValueOrDefault(file, Path.Combine(arena2Root, file)));
    public IEnumerable<string> FileNames => files.Keys.Order(StringComparer.Ordinal);

    public static string Slug(string name) => Regex.Replace(
        Regex.Replace(name, "([a-z0-9])([A-Z])", "$1-$2").ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');

    public void Warn(string message)
    {
        warnings.Add(message);
        Console.Error.WriteLine($"reference export: {message}");
    }

    public TextureArchive Texture(int number)
    {
        if (!textures.TryGetValue(number, out TextureArchive? archive))
            textures[number] = archive = TextureArchive.Parse(Read($"TEXTURE.{number:000}"), $"arena2/TEXTURE.{number:000}");
        return archive;
    }

    public List<ReferenceImage> TextureImages(string directory, string role, int archive, int record,
        bool playerMask = false, bool? clothingDyes = null)
    {
        List<ReferenceImage> images = [];
        if (archive == 0 && record == 0) return images; // The template's absent-art sentinel, not TEXTURE.000.
        TextureArchive texture = Texture(archive);
        if (record < 0 || record >= texture.RecordCount)
        {
            Warn($"{directory}: {texture.Source} record {record} is outside its {texture.RecordCount} records; no image exported for {role}.");
            return images;
        }
        TextureRecordInfo info = texture.GetRecordInfo(record);
        if (info.FrameCount == 0)
            Warn($"{directory}: {texture.Source} record {record} declares no frames for {role}.");
        for (int frame = 0; frame < info.FrameCount; frame++)
        {
            IndexedTextureFrame decoded = texture.DecodeFrame(record, frame);
            byte[] pixels = decoded.Pixels.ToArray();
            string stem = $"{role}/texture-{archive:000}-record-{record:000}-frame-{frame:000}";
            if (playerMask && pixels.Contains((byte)255))
            {
                byte[] mask = new byte[pixels.Length * 4];
                for (int i = 0; i < pixels.Length; i++)
                    if (pixels[i] == 255) { mask.AsSpan(i * 4, 4).Fill(255); pixels[i] = 0; }
                images.Add(Rgba(directory, stem + "-mask.png", role + "/mask", texture.Source, record, frame,
                    decoded.Width, decoded.Height, info.XOffset, info.YOffset, mask, []));
            }
            images.AddRange(Indexed(directory, stem, role, texture.Source, record, frame, decoded.Width, decoded.Height,
                info.XOffset, info.YOffset, pixels, clothingDyes));
        }
        return images;
    }

    public List<ReferenceImage> Indexed(string directory, string stem, string role, string source, int record, int frame,
        int width, int height, int x, int y, byte[] pixels, bool? clothingDyes = null)
    {
        Dictionary<byte[], List<string>> variants = new(ReferenceEqualityComparer.Instance) { [pixels] = ["source"] };
        if (clothingDyes is bool clothing)
            foreach ((string dye, byte[] dyed) in PaperDollItemPublication.DyeVariants(pixels, clothing))
            {
                if (!variants.TryGetValue(dyed, out List<string>? aliases)) variants[dyed] = aliases = [];
                aliases.Add(dye);
            }
        List<ReferenceImage> images = [];
        foreach ((byte[] dyed, List<string> colors) in variants)
        {
            string file = stem + "-" + colors[0] + ".png";
            images.Add(Rgba(directory, file, role, source, record, frame, width, height, x, y,
                Palette.ToRgbaBytes(dyed, PaletteAlphaMode.IndexZeroTransparent), colors));
        }
        return images;
    }

    public ReferenceImage Rgba(string directory, string file, string role, string source, int record, int frame,
        int width, int height, int x, int y, byte[] rgba, IReadOnlyList<string> colors)
    {
        string path = Path.Combine(Root, directory, file);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, DeterministicPngEncoder.EncodeRgba8(width, height, rgba));
        imageCount++;
        return new(file, role, source, record, frame, width, height, x, y, colors);
    }

    public void Subject(string directory, string name, object data, IReadOnlyList<ReferenceImage> images, string note = "")
    {
        WriteJson(Path.Combine(directory, "manifest.json"), data);
        entries.Add(data);
        string? preview = images.FirstOrDefault(image => !image.Role.EndsWith("/mask", StringComparison.Ordinal))?.File;
        links.Add((directory, name, preview));
        StringBuilder page = Page(name);
        page.Append($"<p>{H(note)}</p><p><a href=\"manifest.json\">Source manifest</a></p><main>");
        foreach (ReferenceImage image in images)
            page.Append($"<figure><a href=\"{H(image.File)}\"><img loading=\"lazy\" src=\"{H(image.File)}\"></a><figcaption>{H(image.Role)}<br>{H(image.Source)} r{image.Record} f{image.Frame}<br>{image.Width}×{image.Height} · {H(string.Join(", ", image.Colors))}</figcaption></figure>");
        page.Append("</main>");
        File.WriteAllText(Path.Combine(Root, directory, "index.html"), page.ToString());
    }

    public void WriteJson(string relative, object data)
    {
        string path = Path.Combine(Root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(data, Json) + "\n");
    }

    public void Finish(string name, object summary, object relationships, string readme, IEnumerable<string> sources)
    {
        WriteJson("manifest.json", new
        {
            purpose = "Original Daggerfall reference images for future image-generation experiments.",
            processing = "Native-resolution palette decoding; no resizing, mirroring, inferred views, or image generation. Player masks are separate. Color aliases share unchanged images.",
            palette = "arena2/ART_PAL.COL", sources, summary, imageCount, warnings, relationships, entries,
        });
        File.WriteAllText(Path.Combine(Root, "README.txt"), readme + "\n");
        StringBuilder page = Page(name);
        page.Append("<p>Native-resolution PNGs. Open a subject for its source records, colors and frames. See <a href=\"README.txt\">README</a> and <a href=\"manifest.json\">manifest</a>.</p><main>");
        StringBuilder csv = new("name,directory,images\n");
        foreach ((string directory, string title, string? preview) in links)
        {
            page.Append($"<figure><a href=\"{H(directory)}/index.html\">");
            if (preview is not null) page.Append($"<img loading=\"lazy\" src=\"{H(directory)}/{H(preview)}\">");
            page.Append($"<figcaption>{H(title)}<br>{H(directory)}</figcaption></a></figure>");
            int count = Directory.EnumerateFiles(Path.Combine(Root, directory), "*.png", SearchOption.AllDirectories).Count();
            csv.Append($"\"{title.Replace("\"", "\"\"")}\",{directory},{count}\n");
        }
        page.Append("</main>");
        File.WriteAllText(Path.Combine(Root, "index.html"), page.ToString());
        File.WriteAllText(Path.Combine(Root, "index.csv"), csv.ToString());
        Console.WriteLine($"{name}: {entries.Count} subjects, {imageCount} PNGs, {warnings.Count} warnings → {Root}");
    }

    private static string H(string text) => WebUtility.HtmlEncode(text);
    private static StringBuilder Page(string title) => new StringBuilder("<!doctype html><meta charset=\"utf-8\">")
        .Append($"<title>{H(title)}</title><style>body{{background:#252830;color:#eee;font:16px system-ui;margin:24px}}a{{color:#a9d6ff}}main{{display:flex;flex-wrap:wrap;gap:16px}}figure{{margin:0;padding:16px;background:#343943;width:260px;overflow-wrap:anywhere}}img{{image-rendering:pixelated;object-fit:contain;width:256px;height:192px;background:#444a54}}figcaption{{margin-top:8px}}</style><h1>{H(title)}</h1>");
}
