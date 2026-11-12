using System.Text.Json;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>What the wilderness ground is made of, and how its layers draw where refreshed art is drawn.</summary>
/// <param name="Contrast">How hard layers meet: 1 blends softly, and higher narrows each transition toward a pixel edge.</param>
/// <param name="RepeatMetres">How far one texture spans before it repeats; a whole number of repeats fills a map pixel, so textures run on from cell to cell.</param>
/// <param name="TriplanarSharpness">Above 0, textures project from three planes, so steep faces don't stretch.</param>
/// <param name="DirtToGrassFrom">The ground noise turns dirt to grass from here.</param>
/// <param name="DirtToGrassFull">The ground noise is all grass from here.</param>
/// <param name="StoneFrom">The ground noise bares stone from here.</param>
/// <param name="StoneFull">The ground noise is all stone from here.</param>
/// <param name="RockFromSlope">Ground turns to rock from this slope (rise over run).</param>
/// <param name="RockFullSlope">Ground is bare rock from this slope.</param>
/// <param name="SnowFromMetres">Snow starts on high ground here, on the walked and far ground alike.</param>
/// <param name="SnowFullMetres">High ground is white from here.</param>
/// <param name="RockHoldsSnow">The share of its snow a bare rock face still holds.</param>
internal sealed record DaggerfallGroundTuning(float Contrast, float RepeatMetres, float TriplanarSharpness,
    float DirtToGrassFrom, float DirtToGrassFull, float StoneFrom, float StoneFull,
    float RockFromSlope, float RockFullSlope, float SnowFromMetres, float SnowFullMetres, float RockHoldsSnow)
{
    internal static DaggerfallGroundTuning Default { get; } = new(16, 6.4f, 4, .42f, .58f, .88f, 1, .5f, .9f, 1_500, 2_200, .3f);

    /// <summary>The blend these values draw.</summary>
    internal DaggerfallGroundBlendSettings Blend => new(Contrast, RepeatMetres, TriplanarSharpness);

    internal DaggerfallGroundTuning Validate()
    {
        if (!float.IsFinite(Contrast) || Contrast < 1) throw new ArgumentException("Ground layers meet at a contrast of 1 or more.");
        if (!DaggerfallGroundBlend.FillsCell(RepeatMetres))
            throw new ArgumentException($"A ground texture repeating every {RepeatMetres} metres would break at every cell's edge.");
        if (!float.IsFinite(TriplanarSharpness) || TriplanarSharpness < 0) throw new ArgumentException("A ground's triplanar sharpness is 0 or more.");
        if (DirtToGrassFrom >= DirtToGrassFull || StoneFrom >= StoneFull || RockFromSlope < 0 || RockFromSlope >= RockFullSlope
            || SnowFromMetres < 0 || SnowFromMetres >= SnowFullMetres)
            throw new ArgumentException("A ground's fades must rise from a start to a finish.");
        if (RockHoldsSnow is < 0 or > 1) throw new ArgumentException("A rock face holds from none to all of its snow.");
        return this;
    }

    internal static DaggerfallGroundTuning Read(JsonElement e) => new(
        e.GetProperty("contrast").GetSingle(), e.GetProperty("repeatMetres").GetSingle(), e.GetProperty("triplanarSharpness").GetSingle(),
        e.GetProperty("dirtToGrassFrom").GetSingle(), e.GetProperty("dirtToGrassFull").GetSingle(),
        e.GetProperty("stoneFrom").GetSingle(), e.GetProperty("stoneFull").GetSingle(),
        e.GetProperty("rockFromSlope").GetSingle(), e.GetProperty("rockFullSlope").GetSingle(),
        e.GetProperty("snowFromMetres").GetSingle(), e.GetProperty("snowFullMetres").GetSingle(), e.GetProperty("rockHoldsSnow").GetSingle());
}
