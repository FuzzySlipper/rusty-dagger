using System.Numerics;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Travel;

/// <summary>One deterministically placed miniature forest clump cached for a world-space grid cell (#11113).</summary>
internal readonly record struct DaggerfallOverlandForestClump(
    double X, double Z, double Height, DaggerfallTravelMapForestKind Kind, float Scale, float Yaw);

/// <summary>
/// Climate selection, seasonal material resolution, settlement clearance and deterministic grid placement for the
/// 3D travel map's lightweight instanced miniature forest clumps (#11113).
/// </summary>
internal static class DaggerfallOverlandForests
{
    /// <summary>World-space spacing of candidate forest clump cells, in map metres.</summary>
    internal const double CellMetres = 520;
    /// <summary>Maximum deterministic offset of a clump from its cell centre, in map metres.</summary>
    internal const double JitterMetres = 175;
    /// <summary>Maximum distance from the view target at which forest clumps are drawn, in map metres.</summary>
    internal const double MaxReachMetres = 19_000;
    /// <summary>Maximum forest clump instances emitted in one update, after places have taken their share.</summary>
    internal const int MostClumps = 600;
    /// <summary>Stable part-id band inside <see cref="Presentation.DaggerfallPresentationObjectIds.TravelMap"/> for forest cells.</summary>
    internal const ulong PartBase = 1UL << 32;

    private static readonly Color WinterPineSnow = Color.FromSrgbHex(0xe8eff7, 1f);
    private static readonly Color WinterBroadleafFrost = Color.FromSrgbHex(0xdbe4ee, 1f);
    private static readonly Color WinterMossFrost = Color.FromSrgbHex(0xd2dce6, 1f);

    /// <summary>Selects the miniature forest silhouette for a climate cell and cell hash.</summary>
    internal static DaggerfallTravelMapForestKind KindFor(int climate, uint hash) => climate switch
    {
        224 or 225 or 229 => DaggerfallTravelMapForestKind.Palm,
        226 or 230 => DaggerfallTravelMapForestKind.Pine,
        228 or 232 => DaggerfallTravelMapForestKind.Deadwood,
        // Temperate woodlands mix broadleaf oak clumps with ~25% conifer stands for organic canopy variety.
        _ => (hash & 3U) == 0U ? DaggerfallTravelMapForestKind.Pine : DaggerfallTravelMapForestKind.Temperate,
    };

    /// <summary>Minimum clearance around a drawn place model so forest clumps never clip walls, roofs or yards.</summary>
    internal static double PlaceClearanceMetres(DaggerfallSiteKind kind) => kind switch
    {
        DaggerfallSiteKind.TownCity => 620,
        DaggerfallSiteKind.TownHamlet or DaggerfallSiteKind.DungeonLabyrinth => 440,
        DaggerfallSiteKind.TownVillage or DaggerfallSiteKind.HomeFarms or DaggerfallSiteKind.ReligionTemple or DaggerfallSiteKind.Graveyard => 340,
        _ => 280,
    };

    /// <summary>Deterministic world-space candidate position, scale and yaw for grid cell (<paramref name="gx"/>, <paramref name="gz"/>).</summary>
    internal static (double X, double Z, uint Hash, float Scale, float Yaw) Candidate(int gx, int gz)
    {
        uint h = Mix(gx, gz);
        float jx = (((h & 0xFFFFU) / 65535f) * 2f) - 1f;
        float jz = ((((h >> 16) & 0xFFFFU) / 65535f) * 2f) - 1f;
        uint h2 = Mix(gx ^ 0x51f15e, gz ^ 0x2c1b3c);
        float scale = .78f + (((h2 & 0xFFFFU) / 65535f) * .44f);
        float yaw = (((h2 >> 16) & 0xFFFFU) / 65535f) * MathF.Tau;
        return (((gx + .5) * CellMetres) + (jx * JitterMetres), ((gz + .5) * CellMetres) + (jz * JitterMetres), h, scale, yaw);
    }

    /// <summary>Stable travel-map part identity for forest grid cell (<paramref name="gx"/>, <paramref name="gz"/>).</summary>
    internal static ulong PartId(int gx, int gz) => PartBase | ((ulong)(uint)(gx + 0x20000) << 18) | (uint)(gz + 0x20000);

    /// <summary>Active forest reach for the current camera distance, tapering off at strategic high-altitude views.</summary>
    internal static double ReachFor(double viewDistance)
    {
        if (viewDistance >= 40_000) return 0;
        double baseReach = Math.Min(MaxReachMetres, Math.Max(6_500, viewDistance * 1.9));
        if (viewDistance <= 26_000) return baseReach;
        double fade = 1 - Math.Clamp((viewDistance - 26_000) / 14_000, 0, 1);
        return baseReach * fade;
    }

    /// <summary>Smooth 0–1 scale taper across the outer 22% of <paramref name="reach"/> so distant clumps never pop in.</summary>
    internal static float EdgeScale(double distance, double reach)
    {
        if (reach <= 0 || distance >= reach) return 0f;
        double start = reach * .78;
        if (distance <= start) return 1f;
        float t = (float)Math.Clamp((reach - distance) / (reach - start), 0, 1);
        return .25f + (.75f * t * t * (3f - (2f * t)));
    }

    /// <summary>Whether a forest slot material changes with winter snow or night lighting.</summary>
    internal static bool IsDynamic(DaggerfallTravelMapForestKind kind, string slotName) => slotName switch
    {
        "shadow" => true,
        "pine-snow" or "pine" or "canopy" or "canopy-alt" or "moss-canopy" => kind != DaggerfallTravelMapForestKind.Palm,
        _ => false,
    };

    /// <summary>Builds the shared Engine material for one forest clump slot under the current season and night level.</summary>
    internal static MaterialRequest ResolveMaterial(DaggerfallTravelMapForestKind kind, string slotName, Color baseColor,
        bool winter, float night, RenderResourceReference shadowTexture)
    {
        if (slotName == "shadow")
        {
            float dark = Math.Clamp(night, 0f, 1f);
            bool snowed = winter && kind != DaggerfallTravelMapForestKind.Palm;
            float dayAlpha = snowed ? .36f : .48f;
            float alpha = dayAlpha + ((.26f - dayAlpha) * dark);
            Color tint = snowed ? new Color(.035f, .05f, .085f, alpha) : new Color(.012f, .016f, .018f, alpha);
            return new MaterialRequest(tint, shadowTexture, 1f, new Color(1, 1, 1, 1), Vector3.Zero, 0, false, MaterialAlphaMode.Blend, 0) with
            {
                Unlit = true,
            };
        }

        Color colour = baseColor;
        if (winter && kind != DaggerfallTravelMapForestKind.Palm)
        {
            colour = slotName switch
            {
                "pine-snow" => Lerp(colour, WinterPineSnow, .84f),
                "pine" => Lerp(colour, WinterPineSnow, .20f),
                "canopy" or "canopy-alt" => Lerp(colour, WinterBroadleafFrost, .68f),
                "moss-canopy" => Lerp(colour, WinterMossFrost, .52f),
                _ => colour,
            };
        }

        return new MaterialRequest(colour, default(RenderResourceReference), .92f, new Color(1, 1, 1, 1), Vector3.Zero, 0, false) with
        {
            FlatShading = true,
        };
    }

    private static uint Mix(int x, int z)
    {
        uint h = (uint)(x * 374761393) ^ (uint)(z * 668265263) ^ 0x9e3779b9U;
        h = (h ^ (h >> 13)) * 1274126177U;
        return h ^ (h >> 16);
    }

    private static Color Lerp(Color a, Color b, float t) => new(
        a.R + ((b.R - a.R) * t),
        a.G + ((b.G - a.G) * t),
        a.B + ((b.B - a.B) * t),
        a.A + ((b.A - a.A) * t));
}
