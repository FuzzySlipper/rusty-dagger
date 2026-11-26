using System.Numerics;
using System.Text.Json;
using Rusty.Engine;

namespace WorldRpg.Rulesets.Daggerfall.World;

/// <summary>
/// The travel map's ground colours, painted per vertex from the same weights the walked ground's layers take: each
/// climate's open ground and bare earth, the rock of steep slopes and high ground, snow, a swamp's lush ground, the beach,
/// and water from the shallows to the deep and in rivers. Colours are read as sRGB and kept linear, as the Engine takes
/// them. A map seen from kilometres up reads its land by colour; the walked ground's textures, averaged that far, read
/// grey.
/// </summary>
/// <param name="Climates">Each climate value's open ground, bare earth and woods, and how much of its land the woods cover.</param>
/// <param name="Variation">How far broad patches lighten or darken the ground, as a share either way.</param>
/// <param name="WinterSnow">How much of open ground snow covers in winter, outside the deserts.</param>
/// <param name="ShallowReach">How far out from land in metres the shallows run before the water is all deep.</param>
/// <remarks>
/// Close in, the land takes the art set's detail over its colours where the set has it: a tiling mottle of swells, woods and
/// open ground, <see cref="DetailMetres"/> across, and its small relief, at <see cref="DetailRelief"/> of the normal map's
/// strength.
/// </remarks>
internal sealed record DaggerfallMapPalette(
    IReadOnlyDictionary<int, DaggerfallMapGround> Climates,
    Vector3 Rock, Vector3 Snow, Vector3 Lush, Vector3 Beach, Vector3 Shallow, Vector3 Deep, Vector3 River,
    float Variation, float WinterSnow, float ShallowReach)
{
    /// <summary>The climate values a palette names, by the names its tuning gives them.</summary>
    internal static IReadOnlyDictionary<string, int> ClimateNames { get; } = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["desert"] = 224, ["hotDesert"] = 225, ["mountain"] = 226, ["rainforest"] = 227, ["swamp"] = 228,
        ["subtropical"] = 229, ["mountainWoods"] = 230, ["woodlands"] = 231, ["hauntedWoodlands"] = 232,
    };

    internal static DaggerfallMapPalette Default { get; } = new(
        new Dictionary<int, DaggerfallMapGround>
        {
            [224] = new(Srgb("#c4a56a"), Srgb("#a8865a"), Srgb("#8f8a52"), 0), [225] = new(Srgb("#d2b17a"), Srgb("#b9935f"), Srgb("#9c9258"), 0),
            [226] = new(Srgb("#6a7650"), Srgb("#766a55"), Srgb("#3f5636"), .2f), [227] = new(Srgb("#3f7230"), Srgb("#665634"), Srgb("#244a22"), .7f),
            [228] = new(Srgb("#56663a"), Srgb("#4c472c"), Srgb("#33452a"), .35f), [229] = new(Srgb("#879450"), Srgb("#a8905e"), Srgb("#56683a"), .25f),
            [230] = new(Srgb("#4b7034"), Srgb("#695e44"), Srgb("#2c4a2a"), .6f), [231] = new(Srgb("#527a33"), Srgb("#6f5e3e"), Srgb("#2f5228"), .5f),
            [232] = new(Srgb("#45603a"), Srgb("#5a5042"), Srgb("#2c3f2c"), .6f),
        },
        Srgb("#7c7366"), Srgb("#eef2f5"), Srgb("#3e5a29"), Srgb("#d6c69a"), Srgb("#3f93a8"), Srgb("#1b4a6e"), Srgb("#3a86a0"),
        .14f, .75f, 2_500);

    /// <summary>How many map metres the land's detail spans before it repeats.</summary>
    internal float DetailMetres { get; init; } = 1_600;

    /// <summary>How strongly the detail's relief shades the land, as a share of its normal map's.</summary>
    internal float DetailRelief { get; init; } = .6f;

    private static readonly Vector3 TemperateRoad = Srgb("#9f885f");
    private static readonly Vector3 DesertRoad = Srgb("#917045");
    private static readonly Vector3 SubtropicalRoad = Srgb("#bea370");
    private static readonly Vector3 MountainRoad = Srgb("#9d9584");
    private static readonly Vector3 WinterRoadSnow = Srgb("#ced7e1");

    /// <summary>
    /// Open ground by its layers' weights (grass, earth, rock, and snow or a swamp's lush ground), in the climate's colours,
    /// lightened or darkened by <paramref name="patch"/> (0 to 1), blended with worn trade roads (<paramref name="road"/>),
    /// and, in winter, under snow.
    /// </summary>
    /// <param name="woods">Broad noise (0 to 1) whose high ground the climate's woods cover, by its share of forest.</param>
    /// <param name="road">Worn trade-road coverage at this vertex (0 to 1), softening under winter snow.</param>
    internal Vector3 Ground(int climate, Vector4 weights, bool lush, float patch, float woods, bool winter, float road = 0f)
    {
        DaggerfallMapGround ground = Climates.TryGetValue(climate, out DaggerfallMapGround found) ? found : Climates[231];
        float total = MathF.Max(weights.X + weights.Y + weights.Z + weights.W, 1e-4f);
        // Woods stand on open ground, thinning out up the rock and off the snow, as patches the noise draws.
        float forest = Math.Clamp((woods - (1 - ground.Forest)) * 6, 0, 1) * (weights.X + weights.Y) / total;
        Vector3 open = Vector3.Lerp(ground.Open, ground.Woods, forest);
        Vector3 colour = ((open * weights.X) + (Vector3.Lerp(ground.Earth, ground.Woods, forest) * weights.Y) + (Rock * weights.Z) + ((lush ? Lush : Snow) * weights.W)) / total;
        colour *= 1 + (Variation * ((patch * 2) - 1));
        if (winter && climate is not (224 or 225 or 229)) colour = Vector3.Lerp(colour, Snow, WinterSnow * (1 - (weights.Z / total)));
        if (road > .001f)
        {
            // Attenuate roads smoothly across steep rocky crags so tracks never smear up vertical cliffs.
            float rockShare = weights.Z / total;
            float cragFade = 1f - Math.Clamp((rockShare - .35f) / .35f, 0f, 1f);
            bool snowed = winter && climate is not (224 or 225 or 229);
            Vector3 track = climate switch
            {
                224 or 225 => DesertRoad,
                229 => SubtropicalRoad,
                226 => MountainRoad,
                _ => TemperateRoad,
            };
            if (snowed) track = Vector3.Lerp(track, WinterRoadSnow, .72f);
            float blend = Math.Clamp(road * cragFade * (snowed ? .46f : .80f) * (.86f + (.14f * patch)), 0f, .88f);
            colour = Vector3.Lerp(colour, track, blend);
        }
        return colour;
    }

    /// <summary>Water <paramref name="offshore"/> metres out from land: the shallows' colour by the coast, the deep's far out.</summary>
    internal Vector3 Water(double offshore) => Vector3.Lerp(Shallow, Deep, (float)Math.Clamp(offshore / ShallowReach, 0, 1));

    internal DaggerfallMapPalette Validate(string name)
    {
        foreach (int climate in ClimateNames.Values)
            if (!Climates.TryGetValue(climate, out DaggerfallMapGround ground)) throw new InvalidOperationException($"Tuning '{name}.climates' names no colours for climate {climate}.");
            else if (!float.IsFinite(ground.Forest) || ground.Forest is < 0 or > 1) throw new InvalidOperationException($"Tuning '{name}.climates' forest shares lie from 0 to 1.");
        if (!float.IsFinite(Variation) || Variation is < 0 or >= 1) throw new InvalidOperationException($"Tuning '{name}.variation' must be a share from 0 up to 1.");
        if (!float.IsFinite(WinterSnow) || WinterSnow is < 0 or > 1) throw new InvalidOperationException($"Tuning '{name}.winterSnow' must be a share from 0 to 1.");
        if (!float.IsFinite(ShallowReach) || ShallowReach <= 0) throw new InvalidOperationException($"Tuning '{name}.shallowReach' must be a positive distance.");
        if (!float.IsFinite(DetailMetres) || DetailMetres <= 0) throw new InvalidOperationException($"Tuning '{name}.detail.metres' must be a positive distance.");
        if (!float.IsFinite(DetailRelief) || DetailRelief is < 0 or > 2) throw new InvalidOperationException($"Tuning '{name}.detail.relief' must be from 0 to 2.");
        return this;
    }

    internal static DaggerfallMapPalette Read(JsonElement section, string name)
    {
        Vector3 Colour(string key) => Srgb(section.GetProperty(key).GetString() ?? throw new InvalidOperationException($"Tuning '{name}.{key}' is a colour."));
        JsonElement climates = section.GetProperty("climates");
        return new DaggerfallMapPalette(
            ClimateNames.ToDictionary(pair => pair.Value, pair =>
            {
                JsonElement ground = climates.GetProperty(pair.Key);
                return new DaggerfallMapGround(Srgb(ground[0].GetString()!), Srgb(ground[1].GetString()!), Srgb(ground[2].GetString()!), ground[3].GetSingle());
            }),
            Colour("rock"), Colour("snow"), Colour("lush"), Colour("beach"), Colour("shallow"), Colour("deep"), Colour("river"),
            section.GetProperty("variation").GetSingle(), section.GetProperty("winterSnow").GetSingle(), section.GetProperty("shallowReach").GetSingle())
        {
            DetailMetres = section.GetProperty("detail").GetProperty("metres").GetSingle(),
            DetailRelief = section.GetProperty("detail").GetProperty("relief").GetSingle(),
        }.Validate(name);
    }

    /// <summary>An sRGB hex colour, #rrggbb, as linear RGB.</summary>
    internal static Vector3 Srgb(string hex)
    {
        Color colour = Color.FromSrgbHex(int.Parse(hex.TrimStart('#'), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture), 1F);
        return new(colour.R, colour.G, colour.B);
    }
}

/// <summary>A climate's ground on the travel map: its open ground, its bare earth, its woods and how much of it they cover.</summary>
internal readonly record struct DaggerfallMapGround(Vector3 Open, Vector3 Earth, Vector3 Woods, float Forest);
