using System.Numerics;
using System.Text.Json;
using Rusty.Engine;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>
/// Where a replacement image is drawn in the paper doll, in doll units (the classic 110x184 grid). It is the
/// generated image's own rect, independent of its pixel size, and may extend past the doll's frame.
/// </summary>
internal sealed record DaggerfallDollRect(double X, double Y, double Width, double Height);

/// <summary>One refreshed image that stands in for a classic media identity.</summary>
/// <param name="Replaces">The classic media identity it stands in for.</param>
/// <param name="MediaId">Its own media identity.</param>
/// <param name="Path">Its content-root path.</param>
/// <param name="ByteLength">Its length, as promotion recorded it.</param>
/// <param name="Doll">Where the paper doll draws it, for a paper-doll layer.</param>
/// <param name="DyeMask">For clothing, the image of the neutral cloth a dye recolours, and for armour or a weapon, of the
/// bare steel a metal recolours; the same size as this one.</param>
/// <param name="World">For an item's image, the PNG a dropped item stands as on the ground.</param>
/// <param name="Icon">For a held weapon, whose doll layer leaves a gap where the hand holds it, the whole weapon: what the
/// inventory, a shop and the ground draw. It shares the layer's tint, and is drawn in place of it by its own identity.</param>
internal sealed record DaggerfallArtReplacement(string Replaces, string MediaId, string Path, long ByteLength, DaggerfallDollRect? Doll,
    DaggerfallArtImage? DyeMask = null, DaggerfallArtWorldImage? World = null, DaggerfallArtReplacement? Icon = null);

/// <summary>One published image of the refreshed art set beside its replacement: its identity, path and length.</summary>
/// <param name="World">For a worn item's dye mask, its PNG the same size as the item's <see cref="DaggerfallArtReplacement.World"/>.</param>
internal sealed record DaggerfallArtImage(string MediaId, string Path, long ByteLength, DaggerfallArtWorldImage? World = null);

/// <summary>
/// A PNG of an item's image for the world, which reads PNG: its file and pixel size, and for an item icon, the size
/// of its classic image in classic pixels (a worn item's is its doll rect).
/// </summary>
internal sealed record DaggerfallArtWorldImage(DaggerfallArtFile File, int Width, int Height, (double Width, double Height)? Classic);

/// <summary>A rect of an image in pixels, or, for <see cref="DaggerfallEffectArt.Classic"/>, in fractions of a frame.</summary>
internal sealed record DaggerfallArtRect(double X, double Y, double Width, double Height);

/// <summary>
/// One refreshed world effect: a PNG atlas of the classic effect's frames in classic order, each frame the same
/// box, and where the classic frame sits in that box (fractions of the frame, top-down), which sizes and anchors it
/// as the classic.
/// </summary>
internal sealed record DaggerfallEffectArt(string Replaces, string Path, ContentSha256 Sha256, long ByteLength, int Width, int Height,
    IReadOnlyList<DaggerfallArtRect> Frames, DaggerfallArtRect Classic);

/// <summary>A published file of the refreshed set: its path and digest.</summary>
internal sealed record DaggerfallArtFile(string Path, ContentSha256 Sha256);

/// <summary>
/// A refreshed mesh standing in for the classic sprite cut from one texture record: a GLB resting on the floor, and its
/// footprint (its longest side in metres), from which it is drawn at the width of the sprite it replaces.
/// </summary>
internal sealed record DaggerfallArtMesh(DaggerfallArtFile File, double Footprint);

/// <summary>
/// A refreshed surface texture standing in for one classic texture record that walls, floors and ceilings draw: an RGBA
/// PNG the renderer repeats over the same faces the classic record covers.
/// </summary>
internal sealed record DaggerfallArtSurface(DaggerfallArtFile File, int Width, int Height);

/// <summary>
/// The refreshed ground cover: a tileable shell texture whose alpha is each texel's blade height, this many texels square and
/// drawn this many to the metre, the shader that sways the shells, and a grass tuft, both images greyscale; and each ground archive's grass, the
/// linear tints that colour the shells and the tufts there. An archive the set names no grass for grows no grass. Each
/// open ground layer's relief, by the ground archive and record it lifts, is that layer's own texture with its heights.
/// </summary>
internal sealed record DaggerfallGroundCoverArt(DaggerfallArtFile Shell, int ShellTexels, float ShellTexelsPerMetre, DaggerfallArtFile Shader, DaggerfallArtFile Tuft,
    IReadOnlyDictionary<int, (Vector3 Shells, Vector3 Tufts)> Grass, IReadOnlyDictionary<(int Archive, int Record), DaggerfallGroundRelief> Relief);

/// <summary>
/// A ground layer's relief: its own texture, whose alpha is each texel's height as a share of the shell stack; the tallest
/// texel's share, so the stack draws only the layers it reaches; its texels a side; and whether it sways in the wind, as tall grass does.
/// </summary>
internal sealed record DaggerfallGroundRelief(DaggerfallArtFile File, float Height, int Texels, bool Sways);

/// <summary>
/// A refreshed world billboard standing in for one classic texture record: a PNG atlas of its frames in classic order, each
/// the same box, and where the classic frame sits in that box (fractions of the frame, top-down). A flat that gives light
/// names its emission map, an atlas of the same layout holding only what glows.
/// </summary>
internal sealed record DaggerfallArtFlat(DaggerfallArtFile File, int Width, int Height, IReadOnlyList<DaggerfallArtRect> Frames, DaggerfallArtRect Classic,
    DaggerfallArtFile? Emission = null)
{
    /// <summary>
    /// The world size and pivot that draw this art where the classic sprite stands: the box is larger than the classic frame
    /// it holds, so the classic size grows by the share that frame takes, and the classic pivot (from the frame's bottom) moves into the box.
    /// </summary>
    internal (Vector2 Size, Vector2 Pivot) Placed(Vector2 size, Vector2 pivot) =>
        (size / new Vector2((float)Classic.Width, (float)Classic.Height),
            new((float)(Classic.X + pivot.X * Classic.Width), (float)(1 - Classic.Y - Classic.Height + pivot.Y * Classic.Height)));
}

/// <summary>
/// How refreshed clothing, painted neutral, is drawn in one classic dye, or refreshed armour or a weapon, in bare
/// steel, in one classic metal: its dye mask multiplied by <paramref name="Multiply"/> (a CSS colour), the image first
/// brightened by <paramref name="Brightness"/> where the dye or metal is lighter than a multiply can reach.
/// </summary>
internal sealed record DaggerfallDyeTint(string Multiply, double Brightness);

/// <summary>
/// The refreshed art set: generated images promoted from <c>authoring/</c> (<c>promote.py</c>), each standing in for
/// one classic media identity. A presentation that draws a classic identity draws its replacement instead when the
/// player has not chosen classic art, so every category substitutes through this one map and classic art stays
/// the fallback.
/// </summary>
internal sealed class DaggerfallArtReplacements
{
    private readonly Dictionary<string, DaggerfallArtReplacement> _byClassic;
    private readonly Dictionary<string, DaggerfallArtReplacement> _byMediaId;

    private DaggerfallArtReplacements(Dictionary<string, DaggerfallArtReplacement> byClassic, IReadOnlyDictionary<string, DaggerfallDyeTint> tints,
        IReadOnlyDictionary<string, DaggerfallEffectArt> effects, DaggerfallArtFile? effectGlow, IReadOnlyDictionary<(int Archive, int Record), DaggerfallArtMesh> meshes,
        IReadOnlyDictionary<(int Archive, int Record), DaggerfallArtSurface> surfaces, IReadOnlyDictionary<(int Archive, int Record), DaggerfallArtFlat> flats,
        IReadOnlyDictionary<(int Archive, int Record), DaggerfallArtFlat> people)
    {
        Meshes = meshes;
        Surfaces = surfaces;
        Flats = flats;
        People = people;
        _byClassic = byClassic;
        _byMediaId = Every(byClassic.Values).ToDictionary(replacement => replacement.MediaId, StringComparer.Ordinal);
        Tints = tints;
        Effects = effects;
        EffectGlow = effectGlow;
    }

    /// <summary>The refreshed world effects, by the classic effect media identity each stands in for.</summary>
    internal IReadOnlyDictionary<string, DaggerfallEffectArt> Effects { get; }

    /// <summary>The soft white glow the refreshed spell trails and impact bursts tint, when the set has effects.</summary>
    internal DaggerfallArtFile? EffectGlow { get; }

    /// <summary>
    /// The meshes drawn in place of classic world sprites, by the texture archive and record each replaces: the bone pile
    /// for the corpse every human enemy class shares (so it is no one in particular) and for the bone piles placed in dungeons.
    /// </summary>
    internal IReadOnlyDictionary<(int Archive, int Record), DaggerfallArtMesh> Meshes { get; }

    /// <summary>The refreshed grass that covers open ground near the player, when the set has it.</summary>
    internal DaggerfallGroundCoverArt? GroundCover { get; private init; }

    /// <summary>The refreshed surface textures, by the classic texture archive and record each replaces.</summary>
    internal IReadOnlyDictionary<(int Archive, int Record), DaggerfallArtSurface> Surfaces { get; }

    /// <summary>The refreshed world billboards, by the classic texture archive and record each replaces.</summary>
    internal IReadOnlyDictionary<(int Archive, int Record), DaggerfallArtFlat> Flats { get; }

    /// <summary>The refreshed standing people, by the classic billboard record each stands in for.</summary>
    internal IReadOnlyDictionary<(int Archive, int Record), DaggerfallArtFlat> People { get; }

    /// <summary>
    /// The refreshed surface for a classic texture record the importer published at <paramref name="texturePath"/>
    /// (<c>…/texture-ARCHIVE-RECORD.png</c>), if the set has one.
    /// </summary>
    internal DaggerfallArtSurface? Surface(string texturePath) =>
        System.Text.RegularExpressions.Regex.Match(texturePath, @"/texture-(\d+)-(\d+)\.png$") is { Success: true } match
        && Surfaces.TryGetValue((int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value)), out DaggerfallArtSurface? surface)
            ? surface : null;

    /// <summary>Every replacement.</summary>
    internal IEnumerable<DaggerfallArtReplacement> All => _byClassic.Values;

    /// <summary>Every image the set publishes: each replacement, its icon, and their dye masks.</summary>
    internal IEnumerable<DaggerfallArtImage> Images => Every(_byClassic.Values).SelectMany(replacement =>
        replacement.DyeMask is { } mask ? [new(replacement.MediaId, replacement.Path, replacement.ByteLength), mask]
            : new[] { new DaggerfallArtImage(replacement.MediaId, replacement.Path, replacement.ByteLength) });

    /// <summary>Each classic clothing dye's and metal's tint, by dye or metal name.</summary>
    internal IReadOnlyDictionary<string, DaggerfallDyeTint> Tints { get; }

    private static IEnumerable<DaggerfallArtReplacement> Every(IEnumerable<DaggerfallArtReplacement> replacements) =>
        replacements.SelectMany(replacement => replacement.Icon is { } icon ? [replacement, icon] : new[] { replacement });

    /// <summary>The replacement for a classic media identity, if the set has one.</summary>
    internal DaggerfallArtReplacement? For(string classicMediaId) =>
        _byClassic.TryGetValue(classicMediaId, out DaggerfallArtReplacement? replacement) ? replacement : null;

    /// <summary>The replacement whose own media identity this is, if the set has one.</summary>
    internal DaggerfallArtReplacement? ByMediaId(string mediaId) =>
        _byMediaId.TryGetValue(mediaId, out DaggerfallArtReplacement? replacement) ? replacement : null;

    /// <summary>The media identity the page knows a refreshed effect atlas by, as the UI draws the inventory glints.</summary>
    internal static string EffectMediaId(string classicMediaId) => $"refreshed.{classicMediaId}";

    /// <summary>The media identity drawn in place of a classic one: its replacement's, or the classic one when the set has none.</summary>
    internal string Drawn(string classicMediaId) => For(classicMediaId)?.MediaId ?? classicMediaId;

    /// <summary>Reads the <c>artReplacements</c> section of the pack payload.</summary>
    /// <exception cref="InvalidOperationException">The section is unreadable, or names one identity twice.</exception>
    internal static DaggerfallArtReplacements Read(ReadOnlyMemory<byte> payload, string packId)
    {
        Dictionary<string, DaggerfallArtReplacement> byClassic = new(StringComparer.Ordinal);
        HashSet<string> ids = new(StringComparer.Ordinal);
        Dictionary<string, DaggerfallDyeTint> tints = new(StringComparer.Ordinal);
        Dictionary<string, DaggerfallEffectArt> effects = new(StringComparer.Ordinal);
        DaggerfallArtFile? glow = null;
        DaggerfallGroundCoverArt? groundCover = null;
        Dictionary<(int Archive, int Record), DaggerfallArtMesh> meshes = [];
        Dictionary<(int Archive, int Record), DaggerfallArtSurface> surfaces = [];
        Dictionary<(int Archive, int Record), DaggerfallArtFlat> flats = [], people = [];
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            DaggerfallArtReplacement Image(JsonElement image, string replaces)
            {
                string mediaId = image.GetProperty("mediaId").GetString()!;
                string path = image.GetProperty("path").GetString()!;
                long byteLength = image.GetProperty("byteLength").GetInt64();
                if (string.IsNullOrWhiteSpace(replaces) || string.IsNullOrWhiteSpace(mediaId) || string.IsNullOrWhiteSpace(path) || byteLength <= 0)
                    throw new InvalidOperationException($"Content pack '{packId}' states a replacement without a usable identity, path or length.");
                DaggerfallDollRect? doll = image.TryGetProperty("doll", out JsonElement rect) && rect.ValueKind == JsonValueKind.Object
                    ? new(rect.GetProperty("x").GetDouble(), rect.GetProperty("y").GetDouble(), rect.GetProperty("width").GetDouble(), rect.GetProperty("height").GetDouble())
                    : null;
                if (doll is { Width: <= 0 } or { Height: <= 0 })
                    throw new InvalidOperationException($"Content pack '{packId}' places replacement '{mediaId}' in an empty doll rect.");
                DaggerfallArtImage? dyeMask = image.TryGetProperty("dyeMask", out JsonElement mask)
                    ? new(mask.GetProperty("mediaId").GetString()!, mask.GetProperty("path").GetString()!, mask.GetProperty("byteLength").GetInt64(), World(mask, mediaId))
                    : null;
                DaggerfallArtWorldImage? world = World(image, mediaId);
                if (world is { Classic: null } && doll is null)
                    throw new InvalidOperationException($"Content pack '{packId}' states no classic size for the world image of '{mediaId}'.");
                if (dyeMask?.World is { } maskWorld && (world is null || maskWorld.Width != world.Width || maskWorld.Height != world.Height))
                    throw new InvalidOperationException($"Content pack '{packId}' states a world dye mask for '{mediaId}' that is not the size of its world image.");
                if (!ids.Add(mediaId) || dyeMask is not null && !ids.Add(dyeMask.MediaId))
                    throw new InvalidOperationException($"Content pack '{packId}' names replacement '{mediaId}' for '{replaces}' twice.");
                DaggerfallArtReplacement? icon = image.TryGetProperty("icon", out JsonElement whole) ? Image(whole, replaces) : null;
                // The icon is drawn in the layer's tint, so it needs a dye mask exactly when the layer has one.
                if (icon is not null && (icon.DyeMask is null) != (dyeMask is null))
                    throw new InvalidOperationException($"Content pack '{packId}' states an icon for '{mediaId}' that is not dyed as its layer is.");
                return new(replaces, mediaId, path, byteLength, doll, dyeMask, world, icon);
            }

            foreach (JsonElement image in document.RootElement.GetProperty("artReplacements").GetProperty("images").EnumerateArray())
            {
                string replaces = image.GetProperty("replaces").GetString()!;
                DaggerfallArtReplacement replacement = Image(image, replaces);
                if (!byClassic.TryAdd(replaces, replacement))
                    throw new InvalidOperationException($"Content pack '{packId}' names replacement '{replacement.MediaId}' for '{replaces}' twice.");
            }

            if (document.RootElement.GetProperty("artReplacements").TryGetProperty("dyeTints", out JsonElement dyes))
                foreach (JsonProperty dye in dyes.EnumerateObject())
                {
                    string multiply = dye.Value.GetProperty("multiply").GetString()!;
                    double brightness = dye.Value.GetProperty("brightness").GetDouble();
                    if (!System.Text.RegularExpressions.Regex.IsMatch(multiply, "^#[0-9a-f]{6}$") || !double.IsFinite(brightness) || brightness < 1)
                        throw new InvalidOperationException($"Content pack '{packId}' states an unusable tint for dye '{dye.Name}'.");
                    tints[dye.Name] = new(multiply, brightness);
                }

            JsonElement section = document.RootElement.GetProperty("artReplacements");
            if (section.TryGetProperty("effects", out JsonElement effectArt))
                foreach (JsonElement effect in effectArt.EnumerateArray())
                {
                    string replaces = effect.GetProperty("replaces").GetString()!;
                    int width = effect.GetProperty("width").GetInt32(), height = effect.GetProperty("height").GetInt32();
                    DaggerfallArtRect[] frames = [.. effect.GetProperty("frames").EnumerateArray().Select(Rect)];
                    DaggerfallArtRect classic = Rect(effect.GetProperty("classic"));
                    if (frames.Length == 0 || frames.Any(frame => frame.X < 0 || frame.Y < 0 || frame.Width <= 0 || frame.Height <= 0 || frame.X + frame.Width > width || frame.Y + frame.Height > height)
                        || classic.Width <= 0 || classic.Height <= 0)
                        throw new InvalidOperationException($"Content pack '{packId}' places refreshed effect '{replaces}' outside its atlas.");
                    if (!effects.TryAdd(replaces, new(replaces, effect.GetProperty("path").GetString()!,
                            DaggerfallContentHash.Parse(effect.GetProperty("sha256").GetString()!, $"Refreshed effect '{replaces}'"),
                            effect.GetProperty("byteLength").GetInt64(), width, height, frames, classic)))
                        throw new InvalidOperationException($"Content pack '{packId}' names refreshed effect '{replaces}' twice.");
                }
            if (section.TryGetProperty("effectGlow", out JsonElement glowValue))
                glow = new(glowValue.GetProperty("path").GetString()!, DaggerfallContentHash.Parse(glowValue.GetProperty("sha256").GetString()!, "The refreshed effect glow"));
            if (section.TryGetProperty("meshes", out JsonElement meshArt))
                foreach (JsonElement mesh in meshArt.EnumerateArray())
                {
                    (int, int) replaces = (mesh.GetProperty("replaces").GetProperty("archive").GetInt32(), mesh.GetProperty("replaces").GetProperty("record").GetInt32());
                    double footprint = mesh.GetProperty("footprint").GetDouble();
                    if (!double.IsFinite(footprint) || footprint <= 0)
                        throw new InvalidOperationException($"Content pack '{packId}' states no usable footprint for the mesh replacing {replaces.Item1}/{replaces.Item2}.");
                    if (!meshes.TryAdd(replaces, new(new(mesh.GetProperty("path").GetString()!,
                            DaggerfallContentHash.Parse(mesh.GetProperty("sha256").GetString()!, $"The refreshed mesh for {replaces.Item1}/{replaces.Item2}")), footprint)))
                        throw new InvalidOperationException($"Content pack '{packId}' names two meshes for {replaces.Item1}/{replaces.Item2}.");
                }
            if (section.TryGetProperty("surfaces", out JsonElement surfaceArt))
                foreach (JsonElement surface in surfaceArt.EnumerateArray())
                {
                    (int, int) replaces = (surface.GetProperty("replaces").GetProperty("archive").GetInt32(), surface.GetProperty("replaces").GetProperty("record").GetInt32());
                    int width = surface.GetProperty("width").GetInt32(), height = surface.GetProperty("height").GetInt32();
                    if (width <= 0 || height <= 0)
                        throw new InvalidOperationException($"Content pack '{packId}' states an empty surface for {replaces.Item1}/{replaces.Item2}.");
                    if (!surfaces.TryAdd(replaces, new(new(surface.GetProperty("path").GetString()!,
                            DaggerfallContentHash.Parse(surface.GetProperty("sha256").GetString()!, $"The refreshed surface for {replaces.Item1}/{replaces.Item2}")), width, height)))
                        throw new InvalidOperationException($"Content pack '{packId}' names two surfaces for {replaces.Item1}/{replaces.Item2}.");
                }
            foreach ((string name, Dictionary<(int Archive, int Record), DaggerfallArtFlat> sprites) in new[] { ("flats", flats), ("people", people) })
                if (section.TryGetProperty(name, out JsonElement spriteArt))
                foreach (JsonElement flat in spriteArt.EnumerateArray())
                {
                    (int, int) replaces = (flat.GetProperty("replaces").GetProperty("archive").GetInt32(), flat.GetProperty("replaces").GetProperty("record").GetInt32());
                    int width = flat.GetProperty("width").GetInt32(), height = flat.GetProperty("height").GetInt32();
                    DaggerfallArtRect[] frames = [.. flat.GetProperty("frames").EnumerateArray().Select(Rect)];
                    DaggerfallArtRect classic = Rect(flat.GetProperty("classic"));
                    if (frames.Length == 0 || frames.Any(frame => frame.X < 0 || frame.Y < 0 || frame.Width <= 0 || frame.Height <= 0 || frame.X + frame.Width > width || frame.Y + frame.Height > height)
                        || classic.Width <= 0 || classic.Height <= 0)
                        throw new InvalidOperationException($"Content pack '{packId}' places the refreshed flat for {replaces.Item1}/{replaces.Item2} outside its atlas.");
                    DaggerfallArtFile? emission = flat.TryGetProperty("emission", out JsonElement map)
                        ? new(map.GetProperty("path").GetString()!, DaggerfallContentHash.Parse(map.GetProperty("sha256").GetString()!, $"The emission map for {replaces.Item1}/{replaces.Item2}"))
                        : null;
                    if (!sprites.TryAdd(replaces, new(new(flat.GetProperty("path").GetString()!,
                            DaggerfallContentHash.Parse(flat.GetProperty("sha256").GetString()!, $"The refreshed flat for {replaces.Item1}/{replaces.Item2}")), width, height, frames, classic, emission)))
                        throw new InvalidOperationException($"Content pack '{packId}' names two flats for {replaces.Item1}/{replaces.Item2}.");
                }
            if (section.TryGetProperty("groundCover", out JsonElement cover))
            {
                JsonElement shell = cover.GetProperty("shell"), tuft = cover.GetProperty("tuft");
                float texels = shell.GetProperty("texelsPerMetre").GetSingle();
                int side = shell.GetProperty("width").GetInt32();
                if (side <= 0 || side != shell.GetProperty("height").GetInt32())
                    throw new InvalidOperationException($"Content pack '{packId}' states a ground cover shell texture that is not square.");
                if (!float.IsFinite(texels) || texels <= 0)
                    throw new InvalidOperationException($"Content pack '{packId}' draws its ground cover's shells at {texels} texels to the metre.");
                Dictionary<int, (Vector3, Vector3)> grass = [];
                foreach (JsonProperty archive in cover.GetProperty("grass").EnumerateObject())
                    grass.Add(int.Parse(archive.Name, System.Globalization.CultureInfo.InvariantCulture),
                        (Tint(archive.Value.GetProperty("shells")), Tint(archive.Value.GetProperty("tufts"))));
                Dictionary<(int, int), DaggerfallGroundRelief> relief = [];
                foreach (JsonProperty layer in cover.GetProperty("relief").EnumerateObject())
                {
                    string[] key = layer.Name.Split('-');
                    float height = layer.Value.GetProperty("height").GetSingle();
                    if (key.Length != 2 || !float.IsFinite(height) || height is <= 0 or > 1)
                        throw new InvalidOperationException($"Content pack '{packId}' states ground relief '{layer.Name}' without an archive, record and height.");
                    relief.Add((int.Parse(key[0], System.Globalization.CultureInfo.InvariantCulture), int.Parse(key[1], System.Globalization.CultureInfo.InvariantCulture)),
                        new(File(layer.Value, $"The ground relief {layer.Name}"), height, layer.Value.GetProperty("width").GetInt32(), layer.Value.GetProperty("sways").GetBoolean()));
                }
                groundCover = new(File(shell, "The ground cover's shell texture"), side, texels, File(cover.GetProperty("shader"), "The ground cover's shader"),
                    File(tuft, "The ground cover's tuft"), grass, relief);
            }
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or FormatException)
        {
            throw new InvalidOperationException($"Content pack '{packId}' carries an unreadable artReplacements section: {exception.Message}", exception);
        }

        return new DaggerfallArtReplacements(byClassic, tints, effects, glow, meshes, surfaces, flats, people) { GroundCover = groundCover };

        DaggerfallArtWorldImage? World(JsonElement owner, string mediaId)
        {
            if (!owner.TryGetProperty("world", out JsonElement world)) return null;
            int width = world.GetProperty("width").GetInt32(), height = world.GetProperty("height").GetInt32();
            (double, double)? classic = world.TryGetProperty("classic", out JsonElement size)
                ? (size.GetProperty("width").GetDouble(), size.GetProperty("height").GetDouble()) : null;
            if (width <= 0 || height <= 0 || classic is (var w, var h) && (w <= 0 || h <= 0))
                throw new InvalidOperationException($"Content pack '{packId}' states an empty world image for '{mediaId}'.");
            return new(new(world.GetProperty("path").GetString()!, DaggerfallContentHash.Parse(world.GetProperty("sha256").GetString()!, $"The world image of '{mediaId}'")),
                width, height, classic);
        }

        static DaggerfallArtFile File(JsonElement file, string publisher) =>
            new(file.GetProperty("path").GetString()!, DaggerfallContentHash.Parse(file.GetProperty("sha256").GetString()!, publisher));

        Vector3 Tint(JsonElement tint)
        {
            Vector3 value = new(tint[0].GetSingle(), tint[1].GetSingle(), tint[2].GetSingle());
            if (!float.IsFinite(value.X + value.Y + value.Z) || value.X < 0 || value.Y < 0 || value.Z < 0)
                throw new InvalidOperationException($"Content pack '{packId}' tints its ground cover by {value}.");
            return value;
        }

        static DaggerfallArtRect Rect(JsonElement rect) => new(rect.GetProperty("x").GetDouble(), rect.GetProperty("y").GetDouble(),
            rect.GetProperty("width").GetDouble(), rect.GetProperty("height").GetDouble());
    }
}
