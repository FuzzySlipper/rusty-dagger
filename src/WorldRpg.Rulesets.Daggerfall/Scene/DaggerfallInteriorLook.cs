using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>
/// Live interior/dungeon lighting overrides applied directly to <see cref="DaggerfallSiteLighting"/>:
/// chromatic <see cref="LightKind.Ambient"/> shadow floor (survives <see cref="IndirectAmbient.Floor"/> probes
/// and multiplies by SSAO for ink-black crevices inside colored shadows), optional <see cref="LightKind.Hemisphere"/>
/// fill when probes are off, linearized warm firelight &amp; arcane lamp colors, tunable attenuation decay,
/// soft 5×5 shadows, and flame emissive glow.
/// </summary>
internal sealed record DaggerfallInteriorLightOverride(
    Vector3 AmbientColor,
    float AmbientIntensity,
    Vector3 HemisphereSky,
    Vector3 HemisphereGround,
    float HemisphereIntensity,
    Vector3 LampWarmColor,
    Vector3 HearthColor,
    Vector3 ArcaneColor,
    float LampIntensityMultiplier,
    float LampDecay,
    bool LampShadowSoft,
    float LampGlowLevel);

/// <summary>
/// Complete Pass 1 visual look preset for a building interior or dungeon: lighting, raster indirect bounce,
/// SSAO, volumetric &amp; distance fog, tone mapping, auto exposure, bloom, color grading, full-screen pulp
/// image effect, and grounded viewmodel floor.
/// </summary>
internal sealed record DaggerfallInteriorLookProfile(
    string Label,
    DaggerfallInteriorLightOverride Lighting,
    bool UseIndirectProbes,
    float ProbeSpacing,
    uint ProbeBounces,
    float ProbeExtentXZ,
    float ProbeExtentY,
    bool UseSsao,
    float SsaoStrength,
    float SsaoRadius,
    bool UseVolumetricFog,
    float VolFogDensity,
    Vector3 VolFogAlbedo,
    float VolFogAnisotropy,
    float VolFogDistance,
    float VolFogAmbient,
    FogRequest DistanceFog,
    ToneMappingOperator ToneMapping,
    float Exposure,
    bool UseAutoExposure,
    float AutoExposureMin,
    float AutoExposureMax,
    float AutoExposureTarget,
    float BloomThreshold,
    float BloomIntensity,
    ColorGradingRequest Grading,
    bool UseImageEffect,
    Vector4 ShadowSplit,
    Vector4 HighlightSplit,
    Vector4 VignetteGrain,
    Vector4 CurveDepth,
    float ViewFloor);

/// <summary>
/// Session-owned coordinator for Pass 1 interior &amp; dungeon look experiments (<c>world.interior-look</c>,
/// Den #11037 / #11040 / #11041 / #11042). Keeps exteriors and the <c>baseline</c> preset 100% identical
/// to shipped behavior and applies clean per-site presets under roofs and in dungeons.
/// </summary>
internal sealed class DaggerfallInteriorLook : IDisposable
{
    private const string ShaderPath = "worldrpg/packs/daggerfall.base/shaders/interior-look.wgsl";
    private const float ProbeSnapMetres = 8f;
    // Engine requires volumetric fog Distance > 0 even when density is 0 (disabled).
    private static readonly VolumetricFogRequest VolumetricFogOff = new(0f, Vector3.One, 0f, 0f, 0f, 96f, 0f);

    private readonly IEngineContext _engine;
    private readonly DaggerfallData? _data;
    private readonly DaggerfallSiteLightingTuning _lightingTuning;
    private readonly RendererSettingsRequest _baselineRenderer;

    private string _preset = "baseline";
    private RenderResource? _shader;
    private bool _shaderAttempted;
    private bool _activeOnCamera;
    private bool _rendererModified;
    private DaggerfallInteriorLookProfile? _appliedProfile;
    private DaggerfallSiteLighting? _appliedLighting;
    private Vector3? _probeCenter;
    private bool _disposed;

    internal DaggerfallInteriorLook(IEngineContext engine, DaggerfallData? data, DaggerfallSiteLightingTuning lightingTuning)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _data = data;
        _lightingTuning = lightingTuning ?? throw new ArgumentNullException(nameof(lightingTuning));
        _baselineRenderer = engine.RendererSettings.Read().Requested;
    }

    internal string Preset => _preset;

    internal static IReadOnlyList<string> PresetNames { get; } =
    [
        "baseline",
        "step1-light",
        "step2-gi-ao",
        "step3-fog",
        "pulp-rolloff",
        "pulp-neutral",
        "pulp-aces",
        "pulp-aces-grain",
        "pulp-aces-grain-bold",
        "pulp-aces-autoexp",
        "dungeon-cobalt",
        "dungeon-violet",
        "dungeon-teal",
        "dungeon-emerald",
        "interior-cozy",
        "interior-dramatic",
    ];

    internal string? Select(string name)
    {
        string normalized = name.Trim().ToLowerInvariant();
        if (normalized == "reset") normalized = "baseline";
        if (!PresetNames.Contains(normalized))
            return $"Unknown interior look '{name}'. Valid presets: {string.Join(", ", PresetNames)}.";
        _preset = normalized;
        _appliedProfile = null;
        _probeCenter = null;
        return null;
    }

    internal string Describe() =>
        $"interior-look preset={_preset} active={_activeOnCamera} shaderLoaded={_shader is not null} available=[{string.Join(", ", PresetNames)}]";

    /// <summary>
    /// Reconciles the active site's lighting, fog, indirect bounce, SSAO, tone mapping, bloom,
    /// color grading, image effect and viewmodel floor with the selected preset.
    /// </summary>
    internal void Sync(
        DaggerfallSiteProfile site,
        DaggerfallSiteLighting lighting,
        DaggerfallSiteAppearance appearance,
        DaggerfallWeatherPresentation weather,
        DaggerfallViewLight viewLight,
        DaggerfallSkyTime sky,
        Vector3 playerPosition,
        bool overlandOpen)
    {
        if (_disposed) return;

        bool indoors = site.ProfileKind is DaggerfallWorldProfileKind.Interior or DaggerfallWorldProfileKind.Dungeon;
        if (!indoors || overlandOpen || _preset == "baseline")
        {
            RestoreBaseline(lighting, appearance, weather, viewLight, sky, overlandOpen);
            return;
        }

        bool isDungeon = site.ProfileKind == DaggerfallWorldProfileKind.Dungeon;
        bool isNight = sky.Hour is < 6 or >= 18;
        DaggerfallInteriorLookProfile profile = Resolve(_preset, isDungeon, isNight);

        if (_appliedLighting is not null && !ReferenceEquals(_appliedLighting, lighting))
            _appliedLighting = null;

        bool changed = !Equals(_appliedProfile, profile) || !ReferenceEquals(_appliedLighting, lighting) || !_activeOnCamera;
        if (changed)
        {
            lighting.ApplyLookOverride(profile.Lighting, sky);
            lighting.UpdateLampGlow(appearance, sky, profile.Lighting.LampGlowLevel);
            weather.InteriorFogOverride = profile.DistanceFog;
            viewLight.FloorOverride = profile.ViewFloor;

            _engine.CameraView.SetToneMapping(new(profile.ToneMapping, profile.Exposure));
            _engine.CameraView.SetBloom(new(profile.BloomThreshold, profile.BloomIntensity));
            _engine.CameraView.SetAutoExposure(profile.UseAutoExposure
                ? new(true, 1.5f, profile.AutoExposureMin, profile.AutoExposureMax, profile.AutoExposureTarget)
                : new(false, 1f, 1f, 1f, 0.18f));
            _engine.CameraView.SetColorGrading(profile.Grading);

            ApplyRendererSettings(profile);
            _engine.CameraView.SetVolumetricFog(profile.UseVolumetricFog
                ? new(profile.VolFogDensity, profile.VolFogAlbedo, profile.VolFogAnisotropy,
                    playerPosition.Y - 2f, 0f, profile.VolFogDistance, profile.VolFogAmbient)
                : VolumetricFogOff);

            ApplyImageEffect(profile);

            _appliedProfile = profile;
            _appliedLighting = lighting;
            _activeOnCamera = true;
            _probeCenter = null;
        }

        if (profile.UseIndirectProbes)
        {
            Vector3 snapped = Snap(playerPosition, ProbeSnapMetres);
            if (_probeCenter != snapped)
            {
                _engine.CameraView.SetIndirectLight(new(
                    snapped,
                    new(profile.ProbeExtentXZ, profile.ProbeExtentY, profile.ProbeExtentXZ),
                    profile.ProbeSpacing,
                    profile.ProbeBounces,
                    IndirectAmbient.Floor));
                _probeCenter = snapped;
            }
        }
        else if (_probeCenter is not null || changed)
        {
            _engine.CameraView.SetIndirectLight(default);
            _probeCenter = null;
        }
    }

    private void RestoreBaseline(
        DaggerfallSiteLighting lighting,
        DaggerfallSiteAppearance appearance,
        DaggerfallWeatherPresentation weather,
        DaggerfallViewLight viewLight,
        DaggerfallSkyTime sky,
        bool overlandOpen)
    {
        if (!_activeOnCamera && _appliedLighting is null && !_rendererModified)
            return;

        lighting.ApplyLookOverride(null, sky);
        lighting.UpdateLampGlow(appearance, sky, null);
        weather.InteriorFogOverride = null;
        viewLight.FloorOverride = null;

        _engine.CameraView.SetToneMapping(new(ToneMappingOperator.Rolloff, 1f));
        _engine.CameraView.SetBloom(new(1f, _lightingTuning.Bloom));
        _engine.CameraView.SetAutoExposure(new(false, 1f, 1f, 1f, 0.18f));
        if (!overlandOpen)
            _engine.CameraView.SetColorGrading(default);
        _engine.CameraView.SetVolumetricFog(VolumetricFogOff);
        _engine.CameraView.SetIndirectLight(default);
        _engine.CameraView.SetImageEffect(default);

        if (_rendererModified)
        {
            RendererSettingsRequest current = _engine.RendererSettings.Read().Requested;
            _engine.RendererSettings.Set(current with
            {
                AmbientOcclusion = _baselineRenderer.AmbientOcclusion,
                AmbientOcclusionStrength = _baselineRenderer.AmbientOcclusionStrength,
                AmbientOcclusionRadius = _baselineRenderer.AmbientOcclusionRadius,
                VolumetricFog = _baselineRenderer.VolumetricFog,
            });
            _rendererModified = false;
        }

        _appliedProfile = null;
        _appliedLighting = null;
        _probeCenter = null;
        _activeOnCamera = false;
    }

    private void ApplyRendererSettings(DaggerfallInteriorLookProfile profile)
    {
        RendererSettingsRequest current = _engine.RendererSettings.Read().Requested;
        AmbientOcclusionMode aoMode = profile.UseSsao ? AmbientOcclusionMode.ScreenSpace : _baselineRenderer.AmbientOcclusion;
        float aoStrength = profile.UseSsao ? profile.SsaoStrength : _baselineRenderer.AmbientOcclusionStrength;
        float aoRadius = profile.UseSsao ? profile.SsaoRadius : _baselineRenderer.AmbientOcclusionRadius;
        VolumetricFogQuality vfMode = profile.UseVolumetricFog ? VolumetricFogQuality.Low : _baselineRenderer.VolumetricFog;

        RendererSettingsRequest next = current with
        {
            AmbientOcclusion = aoMode,
            AmbientOcclusionStrength = aoStrength,
            AmbientOcclusionRadius = aoRadius,
            VolumetricFog = vfMode,
        };
        if (next != current)
        {
            _engine.RendererSettings.Set(next);
            _rendererModified = true;
        }
    }

    private void ApplyImageEffect(DaggerfallInteriorLookProfile profile)
    {
        if (!profile.UseImageEffect)
        {
            _engine.CameraView.SetImageEffect(default);
            return;
        }
        RenderResource? shader = EnsureShader();
        if (shader is null)
        {
            _engine.CameraView.SetImageEffect(default);
            return;
        }
        _engine.CameraView.SetImageEffect(new ImageEffectRequest(
            new RenderResourceReference(shader),
            profile.ShadowSplit,
            profile.HighlightSplit,
            profile.VignetteGrain,
            profile.CurveDepth,
            default,
            default));
    }

    private RenderResource? EnsureShader()
    {
        if (_shader is not null || _shaderAttempted || _data is null) return _shader;
        _shaderAttempted = true;
        if (!_data.TryReadFile(ShaderPath, out ReadOnlyMemory<byte> bytes)) return null;
        byte[] digest = SHA256.HashData(bytes.Span);
        ContentSha256 sha = new(
            BinaryPrimitives.ReadUInt64BigEndian(digest.AsSpan(0, 8)),
            BinaryPrimitives.ReadUInt64BigEndian(digest.AsSpan(8, 8)),
            BinaryPrimitives.ReadUInt64BigEndian(digest.AsSpan(16, 8)),
            BinaryPrimitives.ReadUInt64BigEndian(digest.AsSpan(24, 8)));
        _shader = _engine.Graphics.OpenShader(_engine.Content, ShaderPath, sha);
        return _shader;
    }

    private static Vector3 Snap(Vector3 p, float step) =>
        new(MathF.Round(p.X / step) * step, MathF.Round(p.Y / step) * step, MathF.Round(p.Z / step) * step);

    private static DaggerfallInteriorLookProfile Resolve(string preset, bool isDungeon, bool isNight)
    {
        return preset switch
        {
            // Explicit dungeon mood palettes (side-by-side comparison).
            "dungeon-cobalt" => BuildDungeonMood(
                label: "Dungeon: Cobalt Stronghold (Image 1)",
                ambientColor: new(0.22f, 0.40f, 0.92f),
                ambientIntensity: 0.28f,
                lampWarm: new(1.0f, 0.36f, 0.06f),
                hearth: new(1.0f, 0.26f, 0.04f),
                arcane: new(0.14f, 0.78f, 1.0f),
                volAlbedo: new(0.55f, 0.66f, 0.92f),
                volDensity: 0.014f,
                distFog: new(0.012f, 0.032f, 0.095f, 1f),
                distDensity: 0.028f,
                shadowSplit: new(0.22f, 0.44f, 0.95f, 0.26f),
                hiSplit: new(1.0f, 0.74f, 0.34f, 0.36f),
                grain: 0.030f,
                grainScale: 1.5f),

            "dungeon-violet" => BuildDungeonMood(
                label: "Dungeon: Indigo-Violet Crypt (Image 2)",
                ambientColor: new(0.38f, 0.26f, 0.88f),
                ambientIntensity: 0.28f,
                lampWarm: new(1.0f, 0.40f, 0.09f),
                hearth: new(1.0f, 0.30f, 0.05f),
                arcane: new(0.28f, 0.50f, 1.0f),
                volAlbedo: new(0.66f, 0.52f, 0.88f),
                volDensity: 0.015f,
                distFog: new(0.032f, 0.014f, 0.072f, 1f),
                distDensity: 0.030f,
                shadowSplit: new(0.44f, 0.26f, 0.88f, 0.30f),
                hiSplit: new(1.0f, 0.74f, 0.44f, 0.34f),
                grain: 0.034f,
                grainScale: 1.5f),

            "dungeon-teal" => BuildDungeonMood(
                label: "Dungeon: Subterranean Teal Cavern (Image 4)",
                ambientColor: new(0.16f, 0.56f, 0.68f),
                ambientIntensity: 0.28f,
                lampWarm: new(1.0f, 0.52f, 0.12f),
                hearth: new(1.0f, 0.40f, 0.08f),
                arcane: new(0.12f, 0.96f, 0.86f),
                volAlbedo: new(0.46f, 0.78f, 0.82f),
                volDensity: 0.015f,
                distFog: new(0.008f, 0.044f, 0.052f, 1f),
                distDensity: 0.028f,
                shadowSplit: new(0.16f, 0.68f, 0.80f, 0.28f),
                hiSplit: new(1.0f, 0.86f, 0.48f, 0.34f),
                grain: 0.034f,
                grainScale: 1.5f),

            "dungeon-emerald" => BuildDungeonMood(
                label: "Dungeon: Nocturnal Jade Gatehouse (Image 3)",
                ambientColor: new(0.18f, 0.50f, 0.34f),
                ambientIntensity: 0.27f,
                lampWarm: new(1.0f, 0.34f, 0.05f),
                hearth: new(1.0f, 0.24f, 0.03f),
                arcane: new(0.20f, 1.0f, 0.50f),
                volAlbedo: new(0.48f, 0.74f, 0.60f),
                volDensity: 0.015f,
                distFog: new(0.008f, 0.036f, 0.024f, 1f),
                distDensity: 0.028f,
                shadowSplit: new(0.20f, 0.60f, 0.44f, 0.28f),
                hiSplit: new(1.0f, 0.72f, 0.28f, 0.36f),
                grain: 0.034f,
                grainScale: 1.5f),

            "interior-cozy" => BuildInteriorMood(isNight, dramatic: false, grain: 0.024f),
            "interior-dramatic" => BuildInteriorMood(isNight, dramatic: true, grain: 0.030f),

            // Progressive Pass 1 stack presets (auto-adapt to Interior vs Dungeon).
            "step1-light" => BuildStage(isDungeon, isNight, giAo: false, fog: false, post: false,
                ToneMappingOperator.Rolloff, exposure: 1.05f, autoExp: false, grain: 0f, grainScale: 1.5f),

            "step2-gi-ao" => BuildStage(isDungeon, isNight, giAo: true, fog: false, post: false,
                ToneMappingOperator.Rolloff, exposure: 1.05f, autoExp: false, grain: 0f, grainScale: 1.5f),

            "step3-fog" => BuildStage(isDungeon, isNight, giAo: true, fog: true, post: false,
                ToneMappingOperator.Rolloff, exposure: 1.05f, autoExp: false, grain: 0f, grainScale: 1.5f),

            "pulp-rolloff" => BuildStage(isDungeon, isNight, giAo: true, fog: true, post: true,
                ToneMappingOperator.Rolloff, exposure: 1.15f, autoExp: false, grain: 0f, grainScale: 1.5f),

            "pulp-neutral" => BuildStage(isDungeon, isNight, giAo: true, fog: true, post: true,
                ToneMappingOperator.Neutral, exposure: 1.32f, autoExp: false, grain: 0f, grainScale: 1.5f),

            "pulp-aces" => BuildStage(isDungeon, isNight, giAo: true, fog: true, post: true,
                ToneMappingOperator.AcesFilmic, exposure: 1.38f, autoExp: false, grain: 0f, grainScale: 1.5f),

            "pulp-aces-grain" => BuildStage(isDungeon, isNight, giAo: true, fog: true, post: true,
                ToneMappingOperator.AcesFilmic, exposure: 1.38f, autoExp: false, grain: 0.030f, grainScale: 1.5f),

            "pulp-aces-grain-bold" => BuildStage(isDungeon, isNight, giAo: true, fog: true, post: true,
                ToneMappingOperator.AcesFilmic, exposure: 1.38f, autoExp: false, grain: 0.072f, grainScale: 2.0f),

            "pulp-aces-autoexp" => BuildStage(isDungeon, isNight, giAo: true, fog: true, post: true,
                ToneMappingOperator.AcesFilmic, exposure: 1.22f, autoExp: true, grain: 0.030f, grainScale: 1.5f),

            _ => BuildStage(isDungeon, isNight, giAo: true, fog: true, post: true,
                ToneMappingOperator.AcesFilmic, exposure: 1.38f, autoExp: false, grain: 0.030f, grainScale: 1.5f),
        };
    }

    private static DaggerfallInteriorLookProfile BuildStage(
        bool isDungeon, bool isNight, bool giAo, bool fog, bool post,
        ToneMappingOperator op, float exposure, bool autoExp, float grain, float grainScale)
    {
        if (isDungeon)
        {
            DaggerfallInteriorLightOverride light = new(
                AmbientColor: new(0.22f, 0.40f, 0.92f),
                AmbientIntensity: giAo ? 0.28f : 0.22f,
                HemisphereSky: new(0.18f, 0.36f, 0.84f),
                HemisphereGround: new(0.06f, 0.12f, 0.30f),
                HemisphereIntensity: giAo ? 0f : 0.12f,
                LampWarmColor: new(1.0f, 0.36f, 0.06f),
                HearthColor: new(1.0f, 0.26f, 0.04f),
                ArcaneColor: new(0.14f, 0.78f, 1.0f),
                LampIntensityMultiplier: 2.10f,
                LampDecay: 1.42f,
                LampShadowSoft: true,
                LampGlowLevel: post ? 2.3f : 1.5f);

            return new DaggerfallInteriorLookProfile(
                Label: $"Dungeon ({op})",
                Lighting: light,
                UseIndirectProbes: giAo,
                ProbeSpacing: 2.0f,
                ProbeBounces: 2,
                ProbeExtentXZ: 24f,
                ProbeExtentY: 12f,
                UseSsao: giAo,
                SsaoStrength: 1.30f,
                SsaoRadius: 1.05f,
                UseVolumetricFog: fog,
                VolFogDensity: 0.014f,
                VolFogAlbedo: new(0.55f, 0.66f, 0.92f),
                VolFogAnisotropy: 0.68f,
                VolFogDistance: 44f,
                VolFogAmbient: 0.16f,
                DistanceFog: fog
                    ? new(FogMode.Exponential, new Color(0.012f, 0.032f, 0.095f, 1f), 0f, 0f, 0.028f)
                    : new(FogMode.Exponential, new Color(0f, 0f, 0f, 1f), 0f, 0f, 0.005f),
                ToneMapping: op,
                Exposure: exposure,
                UseAutoExposure: autoExp,
                AutoExposureMin: 0.95f,
                AutoExposureMax: 1.65f,
                AutoExposureTarget: 0.16f,
                BloomThreshold: post ? 0.78f : 1f,
                BloomIntensity: post ? 0.48f : 0.25f,
                Grading: post ? new(-0.02f, 0f, 0.05f, 0.20f) : default,
                UseImageEffect: post,
                ShadowSplit: new(0.22f, 0.44f, 0.95f, 0.26f),
                HighlightSplit: new(1.0f, 0.74f, 0.34f, 0.36f),
                VignetteGrain: new(0.18f, 2.6f, grain, grainScale),
                CurveDepth: new(0.09f, 0.05f, 0.18f, 0f),
                ViewFloor: post ? 0.07f : 0.20f);
        }
        else
        {
            // Building interiors: cozy, sculpted hearth-lit rooms with clear Day vs. Night mood.
            DaggerfallInteriorLightOverride light = new(
                AmbientColor: isNight ? new(0.88f, 0.48f, 0.22f) : new(1.0f, 0.78f, 0.48f),
                AmbientIntensity: isNight ? (giAo ? 0.14f : 0.12f) : (giAo ? 0.24f : 0.20f),
                HemisphereSky: isNight ? new(0.55f, 0.30f, 0.15f) : new(1.0f, 0.78f, 0.48f),
                HemisphereGround: isNight ? new(0.18f, 0.09f, 0.05f) : new(0.38f, 0.22f, 0.10f),
                HemisphereIntensity: giAo ? 0f : (isNight ? 0.08f : 0.12f),
                LampWarmColor: new(1.0f, 0.50f, 0.14f),
                HearthColor: new(1.0f, 0.32f, 0.05f),
                ArcaneColor: new(0.25f, 0.78f, 1.0f),
                LampIntensityMultiplier: isNight ? 2.15f : 1.85f,
                LampDecay: 1.45f,
                LampShadowSoft: true,
                LampGlowLevel: post ? 2.1f : 1.4f);

            return new DaggerfallInteriorLookProfile(
                Label: $"Interior ({(isNight ? "Night" : "Day")}, {op})",
                Lighting: light,
                UseIndirectProbes: giAo,
                ProbeSpacing: 1.5f,
                ProbeBounces: 2,
                ProbeExtentXZ: 20f,
                ProbeExtentY: 10f,
                UseSsao: giAo,
                SsaoStrength: 1.15f,
                SsaoRadius: 0.90f,
                UseVolumetricFog: fog,
                VolFogDensity: isNight ? 0.011f : 0.008f,
                VolFogAlbedo: new(0.98f, 0.78f, 0.52f),
                VolFogAnisotropy: 0.58f,
                VolFogDistance: 36f,
                VolFogAmbient: 0.16f,
                DistanceFog: fog
                    ? new(FogMode.Exponential, new Color(0.055f, 0.032f, 0.016f, 1f), 0f, 0f, 0.012f)
                    : new(FogMode.Exponential, new Color(0f, 0f, 0f, 1f), 0f, 0f, 0.001f),
                ToneMapping: op,
                Exposure: exposure,
                UseAutoExposure: autoExp,
                AutoExposureMin: 0.95f,
                AutoExposureMax: 1.55f,
                AutoExposureTarget: 0.18f,
                BloomThreshold: post ? 0.80f : 1f,
                BloomIntensity: post ? 0.42f : 0.25f,
                Grading: post ? new(0.08f, 0f, 0.05f, 0.16f) : default,
                UseImageEffect: post,
                ShadowSplit: new(0.84f, 0.50f, 0.28f, 0.22f),
                HighlightSplit: new(1.0f, 0.82f, 0.46f, 0.30f),
                VignetteGrain: new(0.16f, 2.6f, grain, grainScale),
                CurveDepth: new(0.07f, 0.04f, 0.08f, 0f),
                ViewFloor: post ? 0.09f : 0.20f);
        }
    }

    private static DaggerfallInteriorLookProfile BuildDungeonMood(
        string label,
        Vector3 ambientColor,
        float ambientIntensity,
        Vector3 lampWarm,
        Vector3 hearth,
        Vector3 arcane,
        Vector3 volAlbedo,
        float volDensity,
        Color distFog,
        float distDensity,
        Vector4 shadowSplit,
        Vector4 hiSplit,
        float grain,
        float grainScale)
    {
        return new DaggerfallInteriorLookProfile(
            Label: label,
            Lighting: new DaggerfallInteriorLightOverride(
                AmbientColor: ambientColor,
                AmbientIntensity: ambientIntensity,
                HemisphereSky: ambientColor,
                HemisphereGround: ambientColor * 0.35f,
                HemisphereIntensity: 0f,
                LampWarmColor: lampWarm,
                HearthColor: hearth,
                ArcaneColor: arcane,
                LampIntensityMultiplier: 2.10f,
                LampDecay: 1.42f,
                LampShadowSoft: true,
                LampGlowLevel: 2.3f),
            UseIndirectProbes: true,
            ProbeSpacing: 2.0f,
            ProbeBounces: 2,
            ProbeExtentXZ: 24f,
            ProbeExtentY: 12f,
            UseSsao: true,
            SsaoStrength: 1.30f,
            SsaoRadius: 1.05f,
            UseVolumetricFog: true,
            VolFogDensity: volDensity,
            VolFogAlbedo: volAlbedo,
            VolFogAnisotropy: 0.68f,
            VolFogDistance: 44f,
            VolFogAmbient: 0.16f,
            DistanceFog: new(FogMode.Exponential, distFog, 0f, 0f, distDensity),
            ToneMapping: ToneMappingOperator.AcesFilmic,
            Exposure: 1.38f,
            UseAutoExposure: false,
            AutoExposureMin: 0.95f,
            AutoExposureMax: 1.65f,
            AutoExposureTarget: 0.16f,
            BloomThreshold: 0.78f,
            BloomIntensity: 0.48f,
            Grading: new(-0.02f, 0f, 0.05f, 0.20f),
            UseImageEffect: true,
            ShadowSplit: shadowSplit,
            HighlightSplit: hiSplit,
            VignetteGrain: new(0.18f, 2.6f, grain, grainScale),
            CurveDepth: new(0.09f, 0.05f, 0.18f, 0f),
            ViewFloor: 0.07f);
    }

    private static DaggerfallInteriorLookProfile BuildInteriorMood(bool isNight, bool dramatic, float grain)
    {
        DaggerfallInteriorLookProfile baseProfile = BuildStage(
            isDungeon: false, isNight: isNight, giAo: true, fog: true, post: true,
            ToneMappingOperator.AcesFilmic, exposure: dramatic ? 1.32f : 1.36f, autoExp: false, grain: grain, grainScale: 1.5f);
        if (!dramatic) return baseProfile with { Label = "Interior: Cozy Hearth" };

        return baseProfile with
        {
            Label = "Interior: Dramatic Tavern (Warm/Cool Split)",
            Lighting = baseProfile.Lighting with
            {
                AmbientColor = isNight ? new(0.34f, 0.42f, 0.78f) : new(0.72f, 0.68f, 0.80f),
                AmbientIntensity = isNight ? 0.16f : 0.22f,
                LampWarmColor = new(1.0f, 0.40f, 0.08f),
                LampIntensityMultiplier = isNight ? 2.30f : 2.00f,
                LampGlowLevel = 2.4f,
            },
            VolFogDensity = 0.012f,
            VolFogAlbedo = new(0.88f, 0.72f, 0.56f),
            VolFogAmbient = 0.16f,
            Grading = new(0.04f, 0f, 0.06f, 0.20f),
            ShadowSplit = new(0.32f, 0.40f, 0.78f, 0.26f),
            HighlightSplit = new(1.0f, 0.74f, 0.34f, 0.36f),
            VignetteGrain = new(0.20f, 2.5f, grain, 1.5f),
            CurveDepth = new(0.08f, 0.06f, 0.10f, 0f),
            ViewFloor = 0.08f,
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _engine.CameraView.SetImageEffect(default); } catch { }
        try { _engine.CameraView.SetVolumetricFog(VolumetricFogOff); } catch { }
        try { _engine.CameraView.SetIndirectLight(default); } catch { }
        try { _engine.CameraView.SetAutoExposure(new(false, 1f, 1f, 1f, 0.18f)); } catch { }
        try { _engine.CameraView.SetColorGrading(default); } catch { }
        _shader?.Dispose();
        _shader = null;
    }
}
