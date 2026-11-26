using System.Numerics;
using System.Text.Json;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Presentation;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>How the sea draws over the walked ground.</summary>
/// <param name="Shallow">The water's colour over a bed close under it.</param>
/// <param name="Deep">The water's colour with depth.</param>
/// <param name="DepthMetres">The view turns from shallow to deep by e every so many metres of water along it.</param>
/// <param name="ShoreMetres">The surface foams where the bed lies this close under it.</param>
/// <param name="FoamThreshold">How little of the shore foams, from 0 to 1.</param>
/// <param name="WaveMetres">How far one repeat of the ripples spans: the sea's ripple map, or the Engine's ripples without the sea's shader.</param>
/// <param name="Ripple">How much the ripples tilt the surface; 0 draws it flat.</param>
/// <param name="Roughness">How much a ripple scatters what the surface reflects.</param>
/// <param name="Opacity">The surface's alpha before depth and the view's angle raise it.</param>
/// <param name="Drift">The ripples' drift east and south, in repeats a second.</param>
/// <param name="CalmWind">The wind the sea's waves stand to in still weather, metres a second.</param>
/// <param name="GaleWind">What the weather's full wind adds to it: a gale.</param>
/// <param name="BuildHours">Waves rise toward a stronger wind, and fall after it, over about this many game hours.</param>
/// <param name="HighestWaves">The highest significant wave height drawn, metres, whatever the wind.</param>
/// <param name="Fetch">How far the wind blows over the water to raise the sea's waves (x) and a river's (y), metres.</param>
/// <param name="Glint">How brightly the water reflects the moons (x) and a low sun (y), each a path toward it.</param>
/// <param name="GlintDegrees">How far a glint spreads from the mirror's direction, for the waves too small to draw.</param>
/// <param name="Shimmer">How far the sea's colour shimmers where its ripples are too far to draw, as a share of it.</param>
internal sealed record DaggerfallWaterTuning(Color Shallow, Color Deep, float DepthMetres, float ShoreMetres, float FoamThreshold, float WaveMetres, float Ripple, float Roughness, float Opacity, Vector2 Drift,
    float CalmWind, float GaleWind, float BuildHours, float HighestWaves, Vector2 Fetch, Vector2 Glint, float GlintDegrees, float Shimmer)
{
    internal static DaggerfallWaterTuning Default { get; } = new(Color.FromSrgb(.85f, .95f, .95f), Color.FromSrgb(.5f, .6f, .7f), 3, .3f, .55f, 7, .45f, .35f, .48f, new(.03f, .02f),
        2, 18, 1, 2, new(100_000, 300), new(6, .05f), 3, .25f);

    /// <summary>
    /// The Engine material for this water, over a climate's water art, at the day's light: water's colours are its own, so
    /// they don't fall with the sun. It draws with the sea's <paramref name="shader"/>, if the art set has one, which ripples
    /// it in place of the Engine. Rivers flow downstream along their UVs' +v axis even without the custom shader.
    /// </summary>
    internal MaterialRequest Material(RenderResourceReference art, Vector3 sky, MaterialShader? shader, bool river = false)
    {
        Vector2 primary = river ? new(0, -MathF.Max(Drift.Length(), .12f)) : Drift;
        Vector2 secondary = river ? new(.015f, primary.Y * 1.4f) : new(-Drift.Y, Drift.X);
        return new MaterialRequest(new Color(sky.X, sky.Y, sky.Z, Opacity), art, Roughness, new Color(1, 1, 1, 1), Vector3.Zero, 0, false, MaterialAlphaMode.Blend, 0) with
        {
            NormalScale = shader is null ? Ripple : 0,
            Water = new MaterialWater(Shallow, Deep, DepthMetres, ShoreMetres, FoamThreshold, primary, primary, secondary, WaveMetres, default, default),
            Shader = shader ?? default,
        };
    }

    /// <summary>
    /// The sea's shader (<c>sea.wgsl</c>, a variant of it) over its <paramref name="art"/> maps, with the wind its waves
    /// stand to, how far that wind has blown over the water (<paramref name="fetch"/>), how far from the eye the water's
    /// mesh moves (<paramref name="reach"/>, 0 where it never does), each moon slot's lit fraction and the sea's colour
    /// under the sky's light.
    /// </summary>
    internal MaterialShader SeaShader(RenderResource shader, DaggerfallSeaMaps art, DaggerfallSeaWind.Blowing wind, float fetch, float reach,
        Vector4 moons, Vector3 sea) => new(new RenderResourceReference(shader),
        new Vector4(wind.Bearing, HighestWaves, wind.Speed, fetch), new Vector4(Glint, GlintDegrees * MathF.PI / 180, reach),
        new Vector4(moons.X, moons.Y, WaveMetres, Ripple), new Vector4(sea, Shimmer),
        new RenderResourceReference(art.Ripples), new RenderResourceReference(art.Foam), reach > 0 ? WavesReach : 0);

    /// <summary>
    /// The travel map's water shader (<c>sea.wgsl</c>, keyword <c>OVERLAND</c>) over the sea's ripple and foam maps:
    /// gentle map-scale animated ripples, shoreline surf along shallow coasts, and sun/moon glints over the map's palette.
    /// </summary>
    internal MaterialShader OverlandShader(RenderResource shader, DaggerfallSeaMaps art, DaggerfallSeaWind.Blowing wind, Vector4 moons,
        Vector3 shallow, Vector3 deep, float oceanBackdropY) => new(new RenderResourceReference(shader),
        new Vector4(wind.Bearing, oceanBackdropY, wind.Speed, GlintDegrees * MathF.PI / 180),
        new Vector4(shallow, moons.X),
        new Vector4(deep, moons.Y),
        new Vector4(Glint.X, Glint.Y, Ripple, Shimmer),
        new RenderResourceReference(art.Ripples), new RenderResourceReference(art.Foam), 0);

    /// <summary>
    /// How far the waves can carry the water from its mesh, metres, up and sideways, so the Engine culls the water's parts by
    /// bounds that hold them: the highest waves' crests with the swell's, and room to spare.
    /// </summary>
    private float WavesReach => (2 * HighestWaves) + 1;

    /// <summary>The wind's speed for the weather's 0 to 1 wind.</summary>
    internal float WindFor(float wind) => CalmWind + (GaleWind * Math.Clamp(wind, 0, 1));

    internal DaggerfallWaterTuning Validate()
    {
        foreach (float value in new[] {DepthMetres, ShoreMetres, WaveMetres})
            if (!float.IsFinite(value) || value <= 0) throw new ArgumentException("Water's depth, shore and waves have a size.");
        foreach (float value in new[] {FoamThreshold, Roughness, Opacity})
            if (value is < 0 or > 1) throw new ArgumentException("Water's foam, roughness and opacity are from 0 to 1.");
        if (!float.IsFinite(Ripple) || Ripple < 0 || !float.IsFinite(Drift.X) || !float.IsFinite(Drift.Y))
            throw new ArgumentException("Water's ripple is at least nothing, with a finite drift.");
        foreach (float value in new[] {CalmWind, GaleWind, Fetch.X, Fetch.Y})
            if (!float.IsFinite(value) || value < 0) throw new ArgumentException("The sea's wind and fetch are nothing or more.");
        if (!float.IsFinite(BuildHours) || BuildHours <= 0 || !float.IsFinite(HighestWaves) || HighestWaves < 0)
            throw new ArgumentException("Waves build over some time, to a height of nothing or more.");
        if (!float.IsFinite(Glint.X) || Glint.X < 0 || !float.IsFinite(Glint.Y) || Glint.Y < 0 || !float.IsFinite(GlintDegrees) || GlintDegrees <= 0
            || !float.IsFinite(Shimmer) || Shimmer is < 0 or > 1)
            throw new ArgumentException("Water glints by nothing or more over a spread above 0 degrees, and shimmers by a share from 0 to 1.");
        return this;
    }

    internal static DaggerfallWaterTuning Read(JsonElement e) => new DaggerfallWaterTuning(
        ReadColor(e.GetProperty("shallow")), ReadColor(e.GetProperty("deep")),
        e.GetProperty("depthMetres").GetSingle(), e.GetProperty("shoreMetres").GetSingle(), e.GetProperty("foamThreshold").GetSingle(),
        e.GetProperty("waveMetres").GetSingle(), e.GetProperty("ripple").GetSingle(), e.GetProperty("roughness").GetSingle(), e.GetProperty("opacity").GetSingle(),
        new(e.GetProperty("drift")[0].GetSingle(), e.GetProperty("drift")[1].GetSingle()),
        e.GetProperty("calmWind").GetSingle(), e.GetProperty("galeWind").GetSingle(), e.GetProperty("buildHours").GetSingle(),
        e.GetProperty("highestWaves").GetSingle(), new(e.GetProperty("fetch")[0].GetSingle(), e.GetProperty("fetch")[1].GetSingle()),
        new(e.GetProperty("glint")[0].GetSingle(), e.GetProperty("glint")[1].GetSingle()), e.GetProperty("glintDegrees").GetSingle(),
        e.GetProperty("shimmer").GetSingle()).Validate();

    private static Color ReadColor(JsonElement e) => Color.FromSrgb(e[0].GetSingle(), e[1].GetSingle(), e[2].GetSingle());
}
