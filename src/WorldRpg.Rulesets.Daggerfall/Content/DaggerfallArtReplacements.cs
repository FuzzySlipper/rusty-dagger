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
internal sealed record DaggerfallArtReplacement(string Replaces, string MediaId, string Path, long ByteLength, DaggerfallDollRect? Doll,
    DaggerfallArtImage? DyeMask = null);

/// <summary>One published image of the refreshed art set beside its replacement: its identity, path and length.</summary>
internal sealed record DaggerfallArtImage(string MediaId, string Path, long ByteLength);

/// <summary>A rect of an image in pixels, or, for <see cref="DaggerfallEffectArt.Classic"/>, in fractions of a frame.</summary>
internal sealed record DaggerfallArtRect(double X, double Y, double Width, double Height);

/// <summary>
/// One refreshed world effect: a PNG atlas of the classic effect's frames in classic order, each frame the same
/// box, and where the classic frame sits in that box (fractions of the frame, top-down), which sizes and anchors it
/// as the classic.
/// </summary>
internal sealed record DaggerfallEffectArt(string Replaces, string Path, ContentSha256 Sha256, int Width, int Height,
    IReadOnlyList<DaggerfallArtRect> Frames, DaggerfallArtRect Classic);

/// <summary>A published file of the refreshed set: its path and digest.</summary>
internal sealed record DaggerfallArtFile(string Path, ContentSha256 Sha256);

/// <summary>A refreshed mesh standing in for a classic sprite cut from one texture record: the GLB, scaled to the sprite's world size and resting on the floor.</summary>
internal sealed record DaggerfallArtMesh(int Archive, int Record, DaggerfallArtFile File);

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

    private DaggerfallArtReplacements(Dictionary<string, DaggerfallArtReplacement> byClassic, IReadOnlyDictionary<string, DaggerfallDyeTint> tints,
        IReadOnlyDictionary<string, DaggerfallEffectArt> effects, DaggerfallArtFile? effectGlow, DaggerfallArtMesh? corpseMesh)
    {
        CorpseMesh = corpseMesh;
        _byClassic = byClassic;
        Tints = tints;
        Effects = effects;
        EffectGlow = effectGlow;
    }

    /// <summary>The refreshed world effects, by the classic effect media identity each stands in for.</summary>
    internal IReadOnlyDictionary<string, DaggerfallEffectArt> Effects { get; }

    /// <summary>The soft white glow the refreshed spell trails and impact bursts tint, when the set has effects.</summary>
    internal DaggerfallArtFile? EffectGlow { get; }

    /// <summary>The mesh drawn in place of the corpse every human enemy class shares, so it is no one in particular.</summary>
    internal DaggerfallArtMesh? CorpseMesh { get; }

    /// <summary>Every replacement.</summary>
    internal IEnumerable<DaggerfallArtReplacement> All => _byClassic.Values;

    /// <summary>Every image the set publishes: each replacement and its dye mask.</summary>
    internal IEnumerable<DaggerfallArtImage> Images => _byClassic.Values.SelectMany(replacement =>
        replacement.DyeMask is { } mask ? [new(replacement.MediaId, replacement.Path, replacement.ByteLength), mask]
            : new[] { new DaggerfallArtImage(replacement.MediaId, replacement.Path, replacement.ByteLength) });

    /// <summary>Each classic clothing dye's and metal's tint, by dye or metal name.</summary>
    internal IReadOnlyDictionary<string, DaggerfallDyeTint> Tints { get; }

    /// <summary>The replacement for a classic media identity, if the set has one.</summary>
    internal DaggerfallArtReplacement? For(string classicMediaId) =>
        _byClassic.TryGetValue(classicMediaId, out DaggerfallArtReplacement? replacement) ? replacement : null;

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
        DaggerfallArtMesh? corpseMesh = null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            foreach (JsonElement image in document.RootElement.GetProperty("artReplacements").GetProperty("images").EnumerateArray())
            {
                string replaces = image.GetProperty("replaces").GetString()!;
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
                    ? new(mask.GetProperty("mediaId").GetString()!, mask.GetProperty("path").GetString()!, mask.GetProperty("byteLength").GetInt64())
                    : null;
                if (!ids.Add(mediaId) || dyeMask is not null && !ids.Add(dyeMask.MediaId) || !byClassic.TryAdd(replaces, new(replaces, mediaId, path, byteLength, doll, dyeMask)))
                    throw new InvalidOperationException($"Content pack '{packId}' names replacement '{mediaId}' for '{replaces}' twice.");
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
                            DaggerfallContentHash.Parse(effect.GetProperty("sha256").GetString()!, $"Refreshed effect '{replaces}'"), width, height, frames, classic)))
                        throw new InvalidOperationException($"Content pack '{packId}' names refreshed effect '{replaces}' twice.");
                }
            if (section.TryGetProperty("effectGlow", out JsonElement glowValue))
                glow = new(glowValue.GetProperty("path").GetString()!, DaggerfallContentHash.Parse(glowValue.GetProperty("sha256").GetString()!, "The refreshed effect glow"));
            if (section.TryGetProperty("corpseMesh", out JsonElement mesh))
                corpseMesh = new(mesh.GetProperty("replaces").GetProperty("archive").GetInt32(), mesh.GetProperty("replaces").GetProperty("record").GetInt32(),
                    new(mesh.GetProperty("path").GetString()!, DaggerfallContentHash.Parse(mesh.GetProperty("sha256").GetString()!, "The refreshed corpse mesh")));
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or FormatException)
        {
            throw new InvalidOperationException($"Content pack '{packId}' carries an unreadable artReplacements section: {exception.Message}", exception);
        }

        return new DaggerfallArtReplacements(byClassic, tints, effects, glow, corpseMesh);

        static DaggerfallArtRect Rect(JsonElement rect) => new(rect.GetProperty("x").GetDouble(), rect.GetProperty("y").GetDouble(),
            rect.GetProperty("width").GetDouble(), rect.GetProperty("height").GetDouble());
    }
}
