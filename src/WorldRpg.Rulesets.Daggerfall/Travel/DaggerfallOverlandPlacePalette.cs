using System.Numerics;
using Rusty.Engine;

namespace WorldRpg.Rulesets.Daggerfall.Travel;

/// <summary>The architectural colour family a travel-map place wears for its climate.</summary>
internal enum DaggerfallOverlandPlaceClimate
{
    /// <summary>High Rock woodlands, swamps and rainforests: cream plaster, oak timber, grey stone and red/slate roofs.</summary>
    Temperate,
    /// <summary>Alik'r Desert and Sentinel coast: warm golden sandstone, ochre mudbrick, burnt terracotta and glazed teal domes.</summary>
    Desert,
    /// <summary>Wrothgarian and Dragontail mountains: cool alpine granite, dark weathered pine and deep slate.</summary>
    Highland,
}

/// <summary>
/// Resolves shared travel-map place materials for climate palettes, winter frosted roofs, warm lantern-lit night windows
/// and soft radial ground contact shadows without duplicating place geometry (#11112).
/// </summary>
internal static class DaggerfallOverlandPlacePalette
{
    /// <summary>Warm candle/lantern linear amber emitted by lit settlement windows at night.</summary>
    internal static readonly Vector3 WindowEmissionColor = new(1f, .58f, .14f);
    /// <summary>Peak linear emission intensity for lit windows at full night (catches bloom at 3–12 km).</summary>
    internal const float MaxWindowEmission = 6.5f;

    private static readonly Color WindowLitBase = Color.FromSrgbHex(0xffd27a, 1f);
    private static readonly Color WinterRoofSnow = Color.FromSrgbHex(0xe8eef6, 1f);
    private static readonly Color WinterGroundSnow = Color.FromSrgbHex(0xe2eaf2, 1f);

    private static readonly IReadOnlyDictionary<string, Color> DesertOverrides = new Dictionary<string, Color>(StringComparer.Ordinal)
    {
        ["plaster"] = Color.FromSrgbHex(0xe6cfa0, 1f),
        ["timber"] = Color.FromSrgbHex(0xb88a56, 1f),
        ["stone"] = Color.FromSrgbHex(0xc9a876, 1f),
        ["yard"] = Color.FromSrgbHex(0xc4a46e, 1f),
        ["darkstone"] = Color.FromSrgbHex(0x7a624a, 1f),
        ["tile"] = Color.FromSrgbHex(0xc46234, 1f),
        ["thatch"] = Color.FromSrgbHex(0xc8a564, 1f),
        ["slate"] = Color.FromSrgbHex(0x3a7a84, 1f),
        ["wood"] = Color.FromSrgbHex(0x8a613a, 1f),
        ["field"] = Color.FromSrgbHex(0xd4b872, 1f),
        ["crop"] = Color.FromSrgbHex(0x8ca04e, 1f),
        ["hedge"] = Color.FromSrgbHex(0x5b7338, 1f),
        ["moss"] = Color.FromSrgbHex(0x7a7d52, 1f),
        ["banner"] = Color.FromSrgbHex(0xb83b28, 1f),
    };

    private static readonly IReadOnlyDictionary<string, Color> HighlandOverrides = new Dictionary<string, Color>(StringComparer.Ordinal)
    {
        ["plaster"] = Color.FromSrgbHex(0xc4beb2, 1f),
        ["timber"] = Color.FromSrgbHex(0x5c4634, 1f),
        ["stone"] = Color.FromSrgbHex(0x767a80, 1f),
        ["yard"] = Color.FromSrgbHex(0x686c72, 1f),
        ["tile"] = Color.FromSrgbHex(0x7c4638, 1f),
        ["thatch"] = Color.FromSrgbHex(0x7a684e, 1f),
        ["slate"] = Color.FromSrgbHex(0x384252, 1f),
        ["wood"] = Color.FromSrgbHex(0x503c2a, 1f),
        ["crop"] = Color.FromSrgbHex(0x657e42, 1f),
        ["hedge"] = Color.FromSrgbHex(0x2e4a2b, 1f),
    };

    /// <summary>Maps a Daggerfall climate cell value (223–232) to its architectural palette family.</summary>
    internal static DaggerfallOverlandPlaceClimate Classify(int climate) => climate switch
    {
        224 or 225 or 229 => DaggerfallOverlandPlaceClimate.Desert,
        226 or 230 => DaggerfallOverlandPlaceClimate.Highland,
        _ => DaggerfallOverlandPlaceClimate.Temperate,
    };

    /// <summary>Smooth 0–1 warm window glow ramp from dusk (0.55) to full night (0.92), keeping afternoon and winter noon dark.</summary>
    internal static float WindowGlow(float night)
    {
        float t = Math.Clamp((night - .55f) / .37f, 0f, 1f);
        return t * t * (3f - (2f * t));
    }

    /// <summary>Whether a material slot changes with winter snow or night lighting and needs live material updates.</summary>
    internal static bool IsDynamic(DaggerfallOverlandPlaceClimate climate, string slotName) => slotName switch
    {
        "window" or "shadow" => true,
        "tile" or "thatch" or "slate" or "yard" or "field" or "crop" or "hedge" or "moss" => climate != DaggerfallOverlandPlaceClimate.Desert,
        _ => false,
    };

    /// <summary>Builds the Engine material for one travel-map place slot under the given climate, season and night level.</summary>
    internal static MaterialRequest Resolve(DaggerfallOverlandPlaceClimate climate, string slotName, Color baseColor,
        bool winter, float night, RenderResourceReference shadowTexture)
    {
        if (slotName == "shadow")
        {
            float dark = Math.Clamp(night, 0f, 1f);
            bool snowed = winter && climate != DaggerfallOverlandPlaceClimate.Desert;
            float dayAlpha = snowed ? .52f : .68f;
            float alpha = dayAlpha + ((.38f - dayAlpha) * dark);
            // Slightly cooler slate-blue tint on snow so shadows sit naturally against white terrain.
            Color tint = snowed ? new Color(.035f, .05f, .085f, alpha) : new Color(.012f, .015f, .020f, alpha);
            return new MaterialRequest(tint, shadowTexture, 1f, new Color(1, 1, 1, 1), Vector3.Zero, 0, false, MaterialAlphaMode.Blend, 0) with
            {
                Unlit = true,
            };
        }

        if (slotName == "window")
        {
            float glow = WindowGlow(night);
            Color glass = Lerp(baseColor, WindowLitBase, glow);
            return new MaterialRequest(glass, default(RenderResourceReference), glow > 0f ? .4f : .25f,
                new Color(1, 1, 1, 1), Vector3.Zero, 0, false) with
            {
                FlatShading = true,
                EmissionColor = glow > 0f ? WindowEmissionColor : Vector3.Zero,
                EmissionIntensity = glow * MaxWindowEmission,
            };
        }

        Color colour = climate switch
        {
            DaggerfallOverlandPlaceClimate.Desert when DesertOverrides.TryGetValue(slotName, out Color desert) => desert,
            DaggerfallOverlandPlaceClimate.Highland when HighlandOverrides.TryGetValue(slotName, out Color highland) => highland,
            _ => baseColor,
        };

        if (winter && climate != DaggerfallOverlandPlaceClimate.Desert)
        {
            colour = slotName switch
            {
                "tile" or "thatch" or "slate" => Lerp(colour, WinterRoofSnow, .78f),
                "yard" or "field" or "crop" or "hedge" or "moss" => Lerp(colour, WinterGroundSnow, .65f),
                _ => colour,
            };
        }

        return new MaterialRequest(colour, default(RenderResourceReference), .9f, new Color(1, 1, 1, 1), Vector3.Zero, 0, false) with
        {
            FlatShading = true,
        };
    }

    private static Color Lerp(Color a, Color b, float t) => new(
        a.R + ((b.R - a.R) * t),
        a.G + ((b.G - a.G) * t),
        a.B + ((b.B - a.B) * t),
        a.A + ((b.A - a.A) * t));
}
