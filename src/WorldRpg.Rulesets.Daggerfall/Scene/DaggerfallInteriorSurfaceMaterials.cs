using System.Numerics;
using Rusty.Engine;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>
/// Semantic surface class parsed once from a Daggerfall Arena2 texture path (<c>texture-NNN-R.png</c>) for Pass 3
/// material-aware interior/dungeon PBR &amp; voxel relief and volumetric shell overlays (#11043).
/// </summary>
internal enum DaggerfallInteriorSurfaceClass
{
    DefaultStone,
    FlatSolid,
    PolishedMarble,
    WetSewerCave,
    MossyStone,
    IronAndDoors,
    BloodAndGore,
    PlushCarpet,
    WovenTapestry,
    PaintingOrScroll,
    PlasterAndTimber,
    WoodAndFurniture,
    ArcaneStone,
}

/// <summary>
/// Volumetric 3D pixel-shell overlay family eligible for an interior or dungeon surface (<c>interior-shells.wgsl</c>).
/// </summary>
internal enum DaggerfallInteriorShellKind
{
    None = 0,
    MossGreen = 1,
    MossOlive = 2,
    PlushCarpet = 3,
    WovenTapestry = 4,
}

/// <summary>
/// Live Video-menu slider parameters driving outward interior relief (<c>interior-extrude.wgsl</c>, #11043).
/// </summary>
internal readonly record struct DaggerfallInteriorExtrudeTuning(
    float DepthScale,
    float Cutoff,
    float DistanceScale)
{
    internal static DaggerfallInteriorExtrudeTuning Default { get; } = new(1.0f, 0.35f, 1.0f);
}

/// <summary>
/// Per-material Pass 3 classification and shader parameter shaping for building interiors and dungeons (#11043).
/// Handles all four Arena2 climate archive banks (<c>0xx</c>, <c>1xx</c>, <c>3xx</c>, <c>4xx</c>) via <c>archive % 100</c>
/// for climate-variant interior/dungeon sets, mapping each material family to distinct pulp-fantasy PBR &amp; relief
/// properties plus volumetric 3D pixel-shell eligibility.
/// </summary>
internal static class DaggerfallInteriorSurfaceMaterials
{
    /// <summary>
    /// Parses the classic Arena2 <c>(archive, record)</c> pair from a normalized texture path such as
    /// <c>.../texture-363-3.png</c> or <c>.../texture-049-6.png</c>. Returns null for non-Arena2 paths.
    /// </summary>
    internal static (int Archive, int Record)? TryParseTextureRecord(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        int idx = path.LastIndexOf("texture-", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        ReadOnlySpan<char> rest = path.AsSpan(idx + 8);
        int dash = rest.IndexOf('-');
        if (dash <= 0) return null;
        if (!int.TryParse(rest[..dash], out int archive)) return null;
        ReadOnlySpan<char> afterDash = rest[(dash + 1)..];
        int end = 0;
        while (end < afterDash.Length && char.IsAsciiDigit(afterDash[end])) end++;
        if (end == 0 || !int.TryParse(afterDash[..end], out int record)) return null;
        return (archive, record);
    }

    internal static DaggerfallInteriorSurfaceClass Classify(int archive, int record)
    {
        if (archive is 0 or 1)
            return DaggerfallInteriorSurfaceClass.FlatSolid;

        DaggerfallInteriorShellKind shell = ShellKind(archive, record);
        if (shell is DaggerfallInteriorShellKind.MossGreen or DaggerfallInteriorShellKind.MossOlive)
            return DaggerfallInteriorSurfaceClass.MossyStone;
        if (shell == DaggerfallInteriorShellKind.PlushCarpet)
            return DaggerfallInteriorSurfaceClass.PlushCarpet;
        if (shell == DaggerfallInteriorShellKind.WovenTapestry)
            return DaggerfallInteriorSurfaceClass.WovenTapestry;

        if (archive == 93)
            return DaggerfallInteriorSurfaceClass.BloodAndGore;

        int mod = archive % 100;

        if (mod == 48 || archive == 73 || (archive == 89 && record == 4))
            return DaggerfallInteriorSurfaceClass.PaintingOrScroll;

        if (mod is 41 or 63 or 11)
            return DaggerfallInteriorSurfaceClass.PolishedMarble;

        if (mod == 37)
            return DaggerfallInteriorSurfaceClass.ArcaneStone;

        if (mod is 45 or 47 or 68)
            return DaggerfallInteriorSurfaceClass.WetSewerCave;

        if (mod is 56 or 74 || archive == 46 || (archive == 94 && record is 1 or 3))
            return DaggerfallInteriorSurfaceClass.IronAndDoors;

        if (mod is 16 or 28 or 40 or 44 or 60 or 66 or 86)
            return DaggerfallInteriorSurfaceClass.PlasterAndTimber;

        if (archive is 50 or 67 or 72 or 81 or 87 or 90 or 92)
            return DaggerfallInteriorSurfaceClass.WoodAndFurniture;

        return DaggerfallInteriorSurfaceClass.DefaultStone;
    }

    /// <summary>
    /// Identifies textures that grow volumetric 3D pixel shells (<c>interior-shells.wgsl</c>) across all climate banks:
    /// moss/vine dungeon stone (emerald green or golden-olive lichen), plush palace/manor/temple carpets, and heavy tapestries.
    /// </summary>
    internal static DaggerfallInteriorShellKind ShellKind(int archive, int record)
    {
        int mod = archive % 100;

        // Golden-olive lichen & sewer moss over dungeon stone (Castle Necromoghan 023-0/1, 068-0/5, 368-0/5, 468-5/6).
        if ((archive == 23 && record is 0 or 1)
            || (archive is 68 or 368 && record is 0 or 5)
            || (archive == 468 && record is 5 or 6))
            return DaggerfallInteriorShellKind.MossOlive;

        // Vivid emerald dungeon moss & hanging vines across mountain/temperate/swamp dungeon sets + 089 vines.
        if ((archive == 89 && record is 0 or 1)
            || (archive is 122 or 422 && record is 0 or 1)
            || (archive == 322 && record == 0)
            || (archive == 123 && record is 0 or 1)
            || (archive == 323 && record == 4)
            || (archive == 124 && record is 0 or 1 or 4)
            || (archive == 324 && record == 0)
            || (archive == 424 && record is 0 or 1)
            || (archive == 168 && record == 0)
            || (archive == 320 && record == 1))
            return DaggerfallInteriorShellKind.MossGreen;

        // Plush royal damask carpets, woven runners, and thick palace/manor/temple rugs (all climates).
        if ((archive == 6 && record == 0)
            || (mod == 41 && record == 3)
            || (mod == 49 && record is 4 or 6 or 9 or 10))
            return DaggerfallInteriorShellKind.PlushCarpet;

        // Heavy heraldic wall tapestries, banners, and velvet drapery (all climates).
        if ((mod is 55 or 57 && record is >= 0 and <= 6)
            || (archive == 90 && record is 5 or 6 or 7)
            || (archive == 92 && record is 8 or 11))
            return DaggerfallInteriorShellKind.WovenTapestry;

        return DaggerfallInteriorShellKind.None;
    }

    /// <summary>
    /// Shapes base Pass 2 relief settings (<c>voxel-pulp</c> etc.) for a specific surface class, dungeon dampness,
    /// and optional dungeon waterline height, returning the shader-ready <see cref="MaterialShader"/>.
    /// </summary>
    internal static MaterialShader BuildReliefShader(
        DaggerfallInteriorReliefSettings baseSettings,
        RenderResource shader,
        RenderResource? authoredHeightMap,
        DaggerfallInteriorSurfaceClass surfaceClass,
        bool materialAware,
        float dungeonDampness,
        float? waterLevelY,
        float? authoredDepthMetres = null)
    {
        if (!materialAware)
        {
            DaggerfallInteriorReliefSettings effective = authoredDepthMetres is { } rawDepth && baseSettings.DepthMetres > 1e-4f
                ? baseSettings with { DepthMetres = Math.Clamp(rawDepth, 0f, 0.095f) }
                : baseSettings;
            return effective.Shader(shader, authoredHeightMap);
        }

        float damp = Math.Clamp(dungeonDampness, 0f, 1f);
        float dampRoughScale = 1f - 0.22f * damp;
        float depthScale = baseSettings.DepthMetres > 1e-4f ? 1f : 0f;

        // Special shader flags (parameters[3].y):
        // 0 = stone/default, 1 = gold/brass inlay, 2 = forged iron + brass, 3 = wet crimson blood,
        // 4 = velvet/woven fabric + gold embroidery, 5 = mossy stone (matte moss + damp stone).
        (DaggerfallInteriorReliefSettings tuned, int specialFlags, bool waterSensitive) = surfaceClass switch
        {
            DaggerfallInteriorSurfaceClass.FlatSolid => (
                baseSettings with
                {
                    DepthMetres = 0f, MaxSteps = 1, CavityDarkness = 0f, TopSlopeStrength = 0f,
                    BaseRoughness = 0.74f, RimRoughness = 0.62f, RimMetalness = 0f, SelfShadowStrength = 0f,
                }, 0, false),

            DaggerfallInteriorSurfaceClass.PolishedMarble => (
                baseSettings with
                {
                    DepthMetres = depthScale * MathF.Min(baseSettings.DepthMetres, 0.024f),
                    CavityDarkness = MathF.Min(baseSettings.CavityDarkness, 0.44f),
                    TopSlopeStrength = baseSettings.TopSlopeStrength * 0.55f,
                    BaseRoughness = 0.22f, RimRoughness = 0.14f, RimMetalness = 0.10f,
                    SelfShadowStrength = baseSettings.SelfShadowStrength * 0.35f,
                }, 1, true),

            DaggerfallInteriorSurfaceClass.ArcaneStone => (
                baseSettings with
                {
                    DepthMetres = depthScale * MathF.Min(baseSettings.DepthMetres, 0.036f),
                    CavityDarkness = MathF.Min(baseSettings.CavityDarkness, 0.66f),
                    BaseRoughness = 0.48f, RimRoughness = 0.28f, RimMetalness = 0.12f,
                }, 1, false),

            DaggerfallInteriorSurfaceClass.WetSewerCave => (
                baseSettings with
                {
                    DepthMetres = depthScale * MathF.Max(baseSettings.DepthMetres, 0.074f),
                    CavityDarkness = MathF.Max(baseSettings.CavityDarkness, 0.84f),
                    BaseRoughness = 0.34f, RimRoughness = 0.20f, RimMetalness = 0.15f,
                }, 0, true),

            DaggerfallInteriorSurfaceClass.MossyStone => (
                baseSettings with
                {
                    DepthMetres = depthScale * MathF.Max(baseSettings.DepthMetres, 0.072f),
                    CavityDarkness = MathF.Max(baseSettings.CavityDarkness, 0.80f),
                    BaseRoughness = MathF.Max(0.46f, baseSettings.BaseRoughness * 0.86f),
                    RimRoughness = MathF.Max(0.26f, baseSettings.RimRoughness * 0.90f),
                }, 5, true),

            DaggerfallInteriorSurfaceClass.IronAndDoors => (
                baseSettings with
                {
                    DepthMetres = depthScale * MathF.Min(baseSettings.DepthMetres, 0.048f),
                    BaseRoughness = 0.60f, RimRoughness = 0.24f, RimMetalness = 0.22f,
                }, 2, true),

            DaggerfallInteriorSurfaceClass.BloodAndGore => (
                baseSettings with
                {
                    BaseRoughness = baseSettings.BaseRoughness * dampRoughScale,
                }, 3, true),

            DaggerfallInteriorSurfaceClass.PlushCarpet => (
                baseSettings with
                {
                    DepthMetres = depthScale * MathF.Min(baseSettings.DepthMetres, 0.018f),
                    CavityDarkness = 0.30f, TopSlopeStrength = 0.35f,
                    BaseRoughness = 0.88f, RimRoughness = 0.74f, RimMetalness = 0.02f,
                    SelfShadowStrength = 0f,
                }, 4, false),

            DaggerfallInteriorSurfaceClass.WovenTapestry => (
                baseSettings with
                {
                    DepthMetres = depthScale * MathF.Min(baseSettings.DepthMetres, 0.022f),
                    CavityDarkness = 0.32f, TopSlopeStrength = 0.45f,
                    BaseRoughness = 0.84f, RimRoughness = 0.68f, RimMetalness = 0.04f,
                    SelfShadowStrength = 0.25f,
                }, 4, false),

            DaggerfallInteriorSurfaceClass.PaintingOrScroll => (
                baseSettings with
                {
                    DepthMetres = depthScale * MathF.Min(baseSettings.DepthMetres, 0.012f),
                    CavityDarkness = 0.20f, TopSlopeStrength = 0.25f,
                    BaseRoughness = 0.44f, RimRoughness = 0.32f, RimMetalness = 0.05f,
                    SelfShadowStrength = 0f,
                }, 1, false),

            DaggerfallInteriorSurfaceClass.PlasterAndTimber => (
                baseSettings with
                {
                    DepthMetres = depthScale * MathF.Min(baseSettings.DepthMetres, 0.050f),
                    CavityDarkness = MathF.Min(baseSettings.CavityDarkness, 0.62f),
                    BaseRoughness = MathF.Max(baseSettings.BaseRoughness, 0.64f),
                }, 1, false),

            DaggerfallInteriorSurfaceClass.WoodAndFurniture => (
                baseSettings with
                {
                    DepthMetres = depthScale * MathF.Min(baseSettings.DepthMetres, 0.040f),
                    CavityDarkness = MathF.Min(baseSettings.CavityDarkness, 0.58f),
                    BaseRoughness = 0.54f, RimRoughness = 0.26f, RimMetalness = 0.10f,
                }, 1, false),

            _ => (
                baseSettings with
                {
                    BaseRoughness = MathF.Max(0.36f, baseSettings.BaseRoughness * dampRoughScale),
                    RimRoughness = MathF.Max(0.20f, baseSettings.RimRoughness * (1f - 0.14f * damp)),
                }, 0, true),
        };

        float finalDepth = authoredDepthMetres is { } authoredDepth && baseSettings.DepthMetres > 1e-4f
            ? Math.Clamp(authoredDepth, 0f, 0.095f)
            : tuned.DepthMetres;
        float hasWater = waterSensitive && waterLevelY.HasValue ? 1f : 0f;
        float waterY = waterLevelY ?? 0f;
        return new MaterialShader(
            new RenderResourceReference(shader),
            new Vector4(finalDepth, tuned.MaxSteps, tuned.FadeStart, tuned.FadeEnd),
            new Vector4(tuned.CavityDarkness, tuned.TopSlopeStrength, tuned.BaseRoughness, tuned.RimRoughness),
            new Vector4(tuned.RimMetalness, tuned.SelfShadowStrength, tuned.HeightGamma, tuned.MortarThreshold),
            new Vector4(authoredHeightMap is not null ? 1f : 0f, specialFlags, waterY, hasWater),
            authoredHeightMap is null ? default : new RenderResourceReference(authoredHeightMap),
            default);
    }

    /// <summary>
    /// Builds the <c>interior-shells.wgsl</c> layer shader for one tightly-spaced volumetric shell layer
    /// (<c>3–4 mm</c> spacing so oblique corridors fuse seamlessly with zero visible card-stacking).
    /// </summary>
    internal static MaterialShader BuildShellShader(
        RenderResource shader,
        DaggerfallInteriorShellKind kind,
        int layer,
        int layers)
    {
        if (kind == DaggerfallInteriorShellKind.None || layer <= 0 || layers <= 0)
        {
            return new MaterialShader(
                new RenderResourceReference(shader),
                Vector4.Zero, Vector4.Zero, Vector4.Zero, Vector4.Zero, default, default);
        }

        float frac = (float)layer / layers;
        float cutoff = 0.10f + 0.78f * MathF.Pow(frac, 1.12f);

        return kind switch
        {
            DaggerfallInteriorShellKind.MossGreen or DaggerfallInteriorShellKind.MossOlive => BuildShell(
                shader,
                mode: 1f,
                maxHeight: 0.032f,   // 8 layers @ 4.0 mm spacing = cohesive lush 3D moss cushions
                frac: frac,
                cutoff: cutoff,
                droop: 0.014f,
                fadeStart: 8f,
                fadeEnd: 15f,
                strandScale: 2.0f,
                rootDarken: 0.42f,
                tipBoost: 1.24f,
                sheen: 0.14f,
                chromaBias: kind == DaggerfallInteriorShellKind.MossOlive ? 1f : 0f),

            DaggerfallInteriorShellKind.PlushCarpet => BuildShell(
                shader,
                mode: 2f,
                maxHeight: 0.018f,   // 6 layers @ 3.0 mm spacing = dense velvety carpet pile
                frac: frac,
                cutoff: 0.14f + 0.72f * frac,
                droop: 0f,
                fadeStart: 7f,
                fadeEnd: 13f,
                strandScale: 2.0f,
                rootDarken: 0.52f,
                tipBoost: 1.18f,
                sheen: 0.28f,
                chromaBias: 0f),

            DaggerfallInteriorShellKind.WovenTapestry => BuildShell(
                shader,
                mode: 3f,
                maxHeight: 0.014f,   // 5 layers @ 2.8 mm spacing = soft woven tapestry nap
                frac: frac,
                cutoff: 0.18f + 0.70f * frac,
                droop: 0.003f,
                fadeStart: 7f,
                fadeEnd: 13f,
                strandScale: 2.0f,
                rootDarken: 0.58f,
                tipBoost: 1.14f,
                sheen: 0.24f,
                chromaBias: 0f),

            _ => new MaterialShader(new RenderResourceReference(shader), Vector4.Zero, Vector4.Zero, Vector4.Zero, Vector4.Zero, default, default),
        };
    }

    internal static int ShellLayerCount(DaggerfallInteriorShellKind kind) => kind switch
    {
        DaggerfallInteriorShellKind.MossGreen or DaggerfallInteriorShellKind.MossOlive => 8,
        DaggerfallInteriorShellKind.PlushCarpet => 6,
        DaggerfallInteriorShellKind.WovenTapestry => 5,
        _ => 0,
    };

    private static MaterialShader BuildShell(
        RenderResource shader,
        float mode,
        float maxHeight,
        float frac,
        float cutoff,
        float droop,
        float fadeStart,
        float fadeEnd,
        float strandScale,
        float rootDarken,
        float tipBoost,
        float sheen,
        float chromaBias)
    {
        float lift = maxHeight * frac;
        float maxDisp = lift + droop * frac * frac + 0.004f;
        return new MaterialShader(
            new RenderResourceReference(shader),
            new Vector4(mode, lift, frac, cutoff),
            new Vector4(droop, fadeStart, fadeEnd, strandScale),
            new Vector4(rootDarken, tipBoost, sheen, chromaBias),
            Vector4.Zero,
            default,
            default,
            maxDisp);
    }

    /// <summary>
    /// Per-dungeon-type ambient surface dampness (<c>0 = dry stronghold</c>, <c>1 = wet cave/mine/crypt</c>)
    /// so damp subterranean archetypes gleam naturally under firelight while dry halls keep richer stone tooth.
    /// </summary>
    internal static float DungeonDampnessFor(int? dungeonType) => dungeonType switch
    {
        5 or 6 or 16 or 17 => 0.85f,          // Mine, NaturalCave, VolcanicCaves, ScorpionNest
        0 or 4 or 7 or 8 or 9 or 18 => 0.55f, // Crypt, DesecratedTemple, Coven, VampireHaunt, Lab, Cemetery
        1 or 12 => 0.65f,                     // OrcStronghold, SpiderNest
        11 => 0.45f,                          // RuinedCastle (Castle Necromoghan)
        _ => 0.20f,                           // HumanStronghold (Privateer's Hold), Prison, etc.
    };

    /// <summary>
    /// Outward extrusion height multiplier for each semantic surface class under experimental
    /// <c>world.interior-extrude</c> (<c>0</c> skips flat paintings, solids, carpets, and tapestries).
    /// </summary>
    internal static float ExtrudeScale(DaggerfallInteriorSurfaceClass surfaceClass) => surfaceClass switch
    {
        DaggerfallInteriorSurfaceClass.FlatSolid
            or DaggerfallInteriorSurfaceClass.PaintingOrScroll
            or DaggerfallInteriorSurfaceClass.PlushCarpet
            or DaggerfallInteriorSurfaceClass.WovenTapestry => 0f,
        DaggerfallInteriorSurfaceClass.ArcaneStone => 1.15f,
        DaggerfallInteriorSurfaceClass.DefaultStone
            or DaggerfallInteriorSurfaceClass.WetSewerCave
            or DaggerfallInteriorSurfaceClass.MossyStone
            or DaggerfallInteriorSurfaceClass.BloodAndGore => 1.0f,
        DaggerfallInteriorSurfaceClass.PlasterAndTimber => 0.85f,
        DaggerfallInteriorSurfaceClass.IronAndDoors
            or DaggerfallInteriorSurfaceClass.WoodAndFurniture => 0.65f,
        DaggerfallInteriorSurfaceClass.PolishedMarble => 0.45f,
        _ => 0.85f,
    };

    internal static MaterialShader BuildExtrudeDiscardShader(RenderResource shader) =>
        new(new RenderResourceReference(shader), Vector4.Zero, Vector4.Zero, Vector4.Zero, Vector4.Zero, default, default);

    internal static MaterialShader BuildExtrudeShader(
        RenderResource shader,
        RenderResource? authoredHeightMap,
        DaggerfallInteriorReliefSettings baseSettings,
        DaggerfallInteriorSurfaceClass surfaceClass,
        float dungeonDampness,
        float? waterLevelY,
        DaggerfallInteriorExtrudeTuning tuning,
        float? authoredExtrudeMetres = null)
    {
        float baseSilhouetteDepth = authoredExtrudeMetres ?? (0.090f * ExtrudeScale(surfaceClass));
        if (baseSilhouetteDepth <= 0f)
            return BuildExtrudeDiscardShader(shader);

        float depthScale = Math.Clamp(tuning.DepthScale, 0.1f, 3.0f);
        float cutoff = Math.Clamp(tuning.Cutoff, 0.05f, 0.85f);
        float distScale = Math.Clamp(tuning.DistanceScale, 0.25f, 4.0f);
        float fadeStart = 11f * distScale;
        float fadeEnd = 19f * distScale;
        float hasMap = authoredHeightMap is not null ? 1f : 0f;
        RenderResourceReference heightRef = authoredHeightMap is null ? default : new RenderResourceReference(authoredHeightMap);

        float damp = Math.Clamp(dungeonDampness, 0f, 1f);
        float dampRoughScale = 1f - 0.22f * damp;
        (float baseRough, float rimRough, float rimMetal, float cavity, int specialFlags, bool waterSensitive) = surfaceClass switch
        {
            DaggerfallInteriorSurfaceClass.PolishedMarble => (0.22f, 0.14f, 0.10f, 0.44f, 1, true),
            DaggerfallInteriorSurfaceClass.ArcaneStone => (0.48f, 0.28f, 0.12f, 0.66f, 1, false),
            DaggerfallInteriorSurfaceClass.WetSewerCave => (0.34f, 0.20f, 0.15f, 0.84f, 0, true),
            DaggerfallInteriorSurfaceClass.MossyStone => (MathF.Max(0.46f, baseSettings.BaseRoughness * 0.86f), MathF.Max(0.26f, baseSettings.RimRoughness * 0.90f), baseSettings.RimMetalness, 0.80f, 5, true),
            DaggerfallInteriorSurfaceClass.IronAndDoors => (0.60f, 0.24f, 0.22f, baseSettings.CavityDarkness, 2, true),
            DaggerfallInteriorSurfaceClass.BloodAndGore => (baseSettings.BaseRoughness * dampRoughScale, baseSettings.RimRoughness, baseSettings.RimMetalness, baseSettings.CavityDarkness, 3, true),
            DaggerfallInteriorSurfaceClass.PlasterAndTimber => (MathF.Max(baseSettings.BaseRoughness, 0.64f), baseSettings.RimRoughness, baseSettings.RimMetalness, 0.62f, 1, false),
            DaggerfallInteriorSurfaceClass.WoodAndFurniture => (0.54f, 0.26f, 0.10f, 0.58f, 1, false),
            DaggerfallInteriorSurfaceClass.PaintingOrScroll => (0.46f, 0.24f, 0.14f, 0.48f, 1, false),
            _ => (MathF.Max(0.36f, baseSettings.BaseRoughness * dampRoughScale), MathF.Max(0.20f, baseSettings.RimRoughness * (1f - 0.14f * damp)), baseSettings.RimMetalness, baseSettings.CavityDarkness, 0, true),
        };

        float hasWater = waterSensitive && waterLevelY.HasValue ? 1f : 0f;
        float waterY = waterLevelY ?? 0f;
        float depth = baseSilhouetteDepth * depthScale;
        return new MaterialShader(
            new RenderResourceReference(shader),
            new Vector4(1f, depth, 24f, cutoff),
            new Vector4(fadeStart, fadeEnd, baseRough, rimRough),
            new Vector4(rimMetal, cavity, baseSettings.HeightGamma, baseSettings.MortarThreshold),
            new Vector4(hasMap, specialFlags, waterY, hasWater),
            heightRef,
            default,
            depth + 0.01f);
    }
}
