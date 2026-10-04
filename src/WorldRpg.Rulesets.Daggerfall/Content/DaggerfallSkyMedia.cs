using System.Text.Json;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Content;

internal sealed record DaggerfallSkyResource(string Id, string RelativePath, ContentSha256 Hash, ulong ByteLength, Color ClearColor);
internal readonly record struct DaggerfallSkySelection(string First, string Second, float Amount);

/// <summary>The normalized product-wide sky and precipitation resource catalog.</summary>
internal sealed class DaggerfallSkyMedia
{
    internal const string BundleId = "daggerfall.sky";
    internal const string LogicalRoot = "worldrpg/media/sky/resources";
    internal const string ManifestPath = "worldrpg/media/sky/manifest.json";
    private readonly ProductContent _content;
    private readonly IReadOnlyDictionary<string, DaggerfallSkyResource> _resources;
    private readonly IReadOnlyDictionary<(int Sky, int Frame), string> _frames;
    private readonly IReadOnlyDictionary<int, string> _nights;
    private readonly IReadOnlyDictionary<bool, string> _particles;
    private readonly float[][] _curve;

    private DaggerfallSkyMedia(ProductContent content, Dictionary<string, DaggerfallSkyResource> resources,
        Dictionary<(int, int), string> frames, Dictionary<int, string> nights, Dictionary<bool, string> particles, float[][] curve)
    { _content = content; _resources = resources; _frames = frames; _nights = nights; _particles = particles; _curve = curve; }

    internal static DaggerfallSkyMedia Read(ProductContent content)
    {
        using var document = JsonDocument.Parse(content.ReadBytes(ManifestPath));
        JsonElement root = document.RootElement;
        Dictionary<string, DaggerfallSkyResource> resources = new(StringComparer.Ordinal);
        foreach (string section in new[] {"resources", "nightResources", "weatherParticles"})
        foreach (JsonElement resource in root.GetProperty(section).EnumerateArray())
        {
            string id = resource.GetProperty("id").GetString()!;
            string path = resource.GetProperty("relativePath").GetString()!;
            if (string.IsNullOrWhiteSpace(id) || !path.StartsWith("media/sky/resources/", StringComparison.Ordinal)
                || path.Contains("..", StringComparison.Ordinal) || path.Contains('\\')
                || resource.GetProperty("byteLength").GetInt64() <= 0)
                throw new InvalidOperationException($"Sky resource '{id}' has invalid publication identity or artifact facts.");
            ContentSha256 hash = DaggerfallContentHash.Parse(resource.GetProperty("contentHash").GetProperty("value").GetString()!, $"Sky resource '{id}'");
            Color color = new(1,1,1,1);
            if (section != "weatherParticles")
            {
                float[] rgb = resource.GetProperty("clearColor").EnumerateArray().Select(value => value.GetSingle()).ToArray();
                if (rgb.Length != 3 || rgb.Any(value => !float.IsFinite(value) || value is < 0 or > 1))
                    throw new InvalidOperationException($"Sky resource '{id}' has an invalid normalized clear color.");
                color = new(rgb[0], rgb[1], rgb[2], 1);
            }
            int width = resource.GetProperty("width").GetInt32(), height = resource.GetProperty("height").GetInt32();
            if (width <= 0 || height <= 0 || section != "weatherParticles" && width != height * 2)
                throw new InvalidOperationException($"Sky resource '{id}' must be a normalized 2:1 panorama.");
            if (!resources.TryAdd(id, new(id, path["media/sky/resources/".Length..], hash, (ulong)resource.GetProperty("byteLength").GetInt64(), color)))
                throw new InvalidOperationException($"Sky publication repeats resource '{id}'.");
        }
        Dictionary<(int, int), string> frames = [];
        foreach (JsonElement selection in root.GetProperty("selections").EnumerateArray())
        {
            int sky = selection.GetProperty("skyIndex").GetInt32(), frame = selection.GetProperty("timeFrame").GetInt32();
            string id = selection.GetProperty("resourceId").GetString()!;
            if (sky is < 0 or > 31 || frame is < 0 or > 63 || !resources.ContainsKey(id) || !frames.TryAdd((sky, frame), id))
                throw new InvalidOperationException("Sky publication contains an invalid or repeated frame selection.");
        }
        Dictionary<int, string> nights = [];
        foreach (JsonElement night in root.GetProperty("nightResources").EnumerateArray())
            nights.Add(night.GetProperty("nightIndex").GetInt32(), night.GetProperty("id").GetString()!);
        Dictionary<bool, string> particles = [];
        foreach (JsonElement particle in root.GetProperty("weatherParticles").EnumerateArray())
        {
            bool snow = particle.GetProperty("kind").GetString() switch
            { "rain" => false, "snow" => true, var kind => throw new InvalidOperationException($"Unknown precipitation kind '{kind}'.") };
            particles.Add(snow, particle.GetProperty("id").GetString()!);
        }
        float[][] curve = root.GetProperty("daylightFrameCurve").EnumerateArray().Select(knot => new[]
            {knot.GetProperty("time").GetSingle(), knot.GetProperty("value").GetSingle(), knot.GetProperty("tangent").GetSingle()}).ToArray();
        if (frames.Count != 32 * 64 || nights.Count != 4 || Enumerable.Range(0,4).Any(index => !nights.ContainsKey(index))
            || particles.Count != 2 || curve.Length < 2 || curve[0][0] != 0 || curve[^1][0] != 1
            || curve.Any(knot => knot.Any(value => !float.IsFinite(value)) || knot[0] is < 0 or > 1 || knot[1] is < 0 or > 1)
            || curve.Zip(curve.Skip(1)).Any(pair => pair.First[0] >= pair.Second[0]))
            throw new InvalidOperationException("Sky publication does not close over all climate frames, night backgrounds, precipitation sprites and frame curve.");
        using var bundle = content.OpenBundle(BundleId);
        Dictionary<string, ContentReferenceInfo> entries = bundle.Entries.ToArray().ToDictionary(entry => entry.Path, StringComparer.Ordinal);
        foreach (var resource in resources.Values)
            if (!entries.TryGetValue(resource.RelativePath, out var entry) || entry.Sha256 != resource.Hash || entry.ByteLength != resource.ByteLength)
                throw new InvalidOperationException($"Sky artifact '{resource.Id}' is absent or disagrees with its published digest/length.");
        return new(content, resources, frames, nights, particles, curve);
    }

    internal DaggerfallSkySelection Select(int climate, DaggerfallCalendar calendar, DaggerfallWeatherKind weather, int variant)
    {
        int skyBase = climate switch {226 => 0, 224 or 225 => 8, 227 or 231 or 232 => 16,
            223 or 228 or 229 or 230 => 24, _ => throw new ArgumentOutOfRangeException(nameof(climate))};
        bool badWeather = weather is DaggerfallWeatherKind.Rain or DaggerfallWeatherKind.Thunder or DaggerfallWeatherKind.Fog or DaggerfallWeatherKind.Snow;
        if (!calendar.IsDay && !badWeather)
        {
            string night = _nights[skyBase switch {0 => 3, 8 => 1, 16 => 2, _ => 0}];
            return new(night, night, 0);
        }
        int offset = weather switch
        {
            DaggerfallWeatherKind.Rain or DaggerfallWeatherKind.Thunder or DaggerfallWeatherKind.Fog => 4 + variant,
            DaggerfallWeatherKind.Snow => 6 + variant, _ => (int)calendar.Season,
        };
        float normalized = (calendar.SecondOfDay / 60f - DaggerfallCalendar.DawnHour * 60f)
            / ((DaggerfallCalendar.DuskHour - DaggerfallCalendar.DawnHour) * 60f);
        float frame = calendar.IsDay ? Math.Clamp(Evaluate(Math.Clamp(normalized,0,1)) * 64, 0, 63) : 0;
        int first = (int)frame, second = Math.Min(first + 1, 63);
        return new(_frames[(skyBase + offset, first)], _frames[(skyBase + offset, second)], frame - first);
    }

    private float Evaluate(float time)
    {
        for (int index = 1; index < _curve.Length; index++)
        {
            float[] end = _curve[index]; if (time > end[0]) continue;
            float[] start = _curve[index - 1]; float span = end[0] - start[0], t = (time - start[0]) / span;
            float t2 = t*t, t3 = t2*t;
            return Math.Clamp((2*t3-3*t2+1)*start[1] + (t3-2*t2+t)*span*start[2]
                + (-2*t3+3*t2)*end[1] + (t3-t2)*span*end[2],0,1);
        }
        return 1;
    }

    internal Color ClearColor(DaggerfallSkySelection selection)
    {
        Color a = _resources[selection.First].ClearColor, b = _resources[selection.Second].ClearColor;
        float t = selection.Amount;
        return new(a.R + (b.R-a.R)*t, a.G + (b.G-a.G)*t, a.B + (b.B-a.B)*t, 1);
    }

    internal string Particle(bool snow) => _particles[snow];
    internal RenderResource Open(IGraphicsService graphics, string id)
    {
        DaggerfallSkyResource resource = _resources[id];
        using var bundle = _content.OpenBundle(BundleId);
        using var reference = bundle.OpenReference(resource.RelativePath);
        return graphics.OpenResourceFromContent(new(reference, TextureFilter.Linear, TextureWrap.Clamp, TextureColorSpace.Srgb)).Handle;
    }
}
