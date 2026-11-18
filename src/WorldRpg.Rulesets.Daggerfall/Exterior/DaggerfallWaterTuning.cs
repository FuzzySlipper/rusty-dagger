using System.Numerics;
using System.Text.Json;
using Rusty.Engine;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>How the sea draws over the walked ground.</summary>
/// <param name="Shallow">The water's colour over a bed close under it.</param>
/// <param name="Deep">The water's colour with depth.</param>
/// <param name="DepthMetres">The view turns from shallow to deep by e every so many metres of water along it.</param>
/// <param name="ShoreMetres">The surface foams where the bed lies this close under it.</param>
/// <param name="FoamThreshold">How little of the shore foams, from 0 to 1.</param>
/// <param name="WaveMetres">How far one ripple spans.</param>
/// <param name="Ripple">How much the ripples tilt the surface; 0 draws it flat.</param>
/// <param name="Roughness">How much a ripple scatters what the surface reflects.</param>
/// <param name="Opacity">The surface's alpha before depth and the view's angle raise it.</param>
/// <param name="Drift">The ripples' drift east and south, in repeats a second.</param>
/// <param name="SwellMetres">How far one swell spans, in the sea's shader: it runs at deep water's speed for its length.</param>
/// <param name="Swell">How much the swells tilt the surface; 0 draws none.</param>
/// <param name="Glint">How brightly the water reflects the moons (x) and a low sun (y), each a path toward it.</param>
/// <param name="GlintDegrees">How far a glint spreads from the mirror's direction, for the waves too small to draw.</param>
/// <param name="Shimmer">How far the sea's colour shimmers where its ripples are too far to draw, as a share of it.</param>
internal sealed record DaggerfallWaterTuning(Color Shallow, Color Deep, float DepthMetres, float ShoreMetres, float FoamThreshold, float WaveMetres, float Ripple, float Roughness, float Opacity, Vector2 Drift,
    float SwellMetres, float Swell, Vector2 Glint, float GlintDegrees, float Shimmer)
{
    internal static DaggerfallWaterTuning Default { get; } = new(Color.FromSrgb(.85f, .95f, .95f), Color.FromSrgb(.5f, .6f, .7f), 3, .3f, .55f, 7, .2f, .85f, .48f, new(.03f, .02f),
        60, .04f, new(1, .05f), 3, .25f);

    /// <summary>
    /// The Engine material for this water, over a climate's water art, at the day's light: water's colours are its own, so
    /// they don't fall with the sun. It draws with the sea's <paramref name="shader"/>, if the art set has one.
    /// </summary>
    internal MaterialRequest Material(RenderResourceReference art, Vector3 sky, MaterialShader shader) => new MaterialRequest(new Color(sky.X, sky.Y, sky.Z, Opacity), art, Roughness, new Color(1, 1, 1, 1), Vector3.Zero, 0, false, MaterialAlphaMode.Blend, 0) with
    {
        NormalScale = Ripple,
        Water = new MaterialWater(Shallow, Deep, DepthMetres, ShoreMetres, FoamThreshold, Drift, Drift, new(-Drift.Y, Drift.X), WaveMetres, default, default),
        Shader = shader,
    };

    /// <summary>
    /// The sea's shader (<c>sea.wgsl</c>) over the Engine's water, or over the far sea, which it ripples itself, with each
    /// moon slot's lit fraction (<paramref name="moons"/>) and the sea's colour under the sky's light.
    /// </summary>
    internal MaterialShader SeaShader(RenderResource shader, bool far, Vector4 moons, Vector3 sea) => new(new RenderResourceReference(shader),
        new Vector4(WaveMetres, Ripple, SwellMetres, Swell), new Vector4(Glint, GlintDegrees * MathF.PI / 180, far ? 1 : 0), moons, new Vector4(sea, Shimmer));

    internal DaggerfallWaterTuning Validate()
    {
        foreach (float value in new[] {DepthMetres, ShoreMetres, WaveMetres})
            if (!float.IsFinite(value) || value <= 0) throw new ArgumentException("Water's depth, shore and waves have a size.");
        foreach (float value in new[] {FoamThreshold, Roughness, Opacity})
            if (value is < 0 or > 1) throw new ArgumentException("Water's foam, roughness and opacity are from 0 to 1.");
        if (!float.IsFinite(Ripple) || Ripple < 0 || !float.IsFinite(Drift.X) || !float.IsFinite(Drift.Y))
            throw new ArgumentException("Water's ripple is at least nothing, with a finite drift.");
        if (!float.IsFinite(SwellMetres) || SwellMetres <= 0 || !float.IsFinite(Swell) || Swell < 0)
            throw new ArgumentException("A swell has a length, and tilts the water by nothing or more.");
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
        e.GetProperty("swellMetres").GetSingle(), e.GetProperty("swell").GetSingle(),
        new(e.GetProperty("glint")[0].GetSingle(), e.GetProperty("glint")[1].GetSingle()), e.GetProperty("glintDegrees").GetSingle(),
        e.GetProperty("shimmer").GetSingle()).Validate();

    private static Color ReadColor(JsonElement e) => Color.FromSrgb(e[0].GetSingle(), e[1].GetSingle(), e[2].GetSingle());
}
