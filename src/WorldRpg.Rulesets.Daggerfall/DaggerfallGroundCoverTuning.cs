using System.Text.Json;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// How refreshed grass covers the ground near the player: shells, a stack of layers over near ground cut into pixel
/// blades, and tufts, crossed quads of grass among them.
/// </summary>
/// <param name="ShellLayers">How many layers the stack has.</param>
/// <param name="ShellGap">How far apart the layers stand, in metres, which with their count is the grass's height.</param>
/// <param name="ShellLift">How far the lowest layer stands above the ground, so it never fights it.</param>
/// <param name="ShellReach">How far the shells reach about the player, in metres.</param>
/// <param name="ShellFade">How far before their reach the blades shorten away.</param>
/// <param name="ShellSway">How far the top layer slides at full wind, in metres; the layers below slide less, each by whole texels.</param>
/// <param name="TuftDensity">How many tufts grow in a square metre where the flora's cover is full.</param>
/// <param name="TuftReach">How far tufts are drawn.</param>
/// <param name="TuftFade">How far before their reach they shrink away.</param>
/// <param name="TuftWidth">A tuft's width, in metres.</param>
/// <param name="TuftHeight">A tuft's height, in metres.</param>
/// <param name="TuftScaleMin">The smallest a tuft grows, as a share of its size.</param>
/// <param name="TuftScaleMax">The largest.</param>
internal sealed record DaggerfallGroundCoverTuning(int ShellLayers, float ShellGap, float ShellLift, float ShellReach, float ShellFade, float ShellSway,
    float TuftDensity, float TuftReach, float TuftFade, float TuftWidth, float TuftHeight, float TuftScaleMin, float TuftScaleMax)
{
    internal static DaggerfallGroundCoverTuning Default { get; } = new(8, .04f, .02f, 44, 20, .12f, 2, 70, 25, .5f, .45f, .7f, 1.4f);

    internal DaggerfallGroundCoverTuning Validate()
    {
        if (ShellLayers is < 2 or > 16) throw new ArgumentException("Ground cover's shells stand in 2 to 16 layers.");
        float[] distances = [ShellGap, ShellLift, ShellReach, ShellFade, ShellSway, TuftDensity, TuftReach, TuftFade, TuftWidth, TuftHeight, TuftScaleMin, TuftScaleMax];
        if (distances.Any(value => !float.IsFinite(value) || value < 0) || ShellGap <= 0 || ShellReach <= 0 || ShellFade > ShellReach
            || TuftReach <= 0 || TuftFade >= TuftReach || TuftWidth <= 0 || TuftHeight <= 0 || TuftScaleMin <= 0 || TuftScaleMin > TuftScaleMax)
            throw new ArgumentException("Ground cover's sizes and distances are finite, positive where they must be, and fade within their reach.");
        return this;
    }

    internal static DaggerfallGroundCoverTuning Read(JsonElement e) => new(
        e.GetProperty("shellLayers").GetInt32(), e.GetProperty("shellGap").GetSingle(), e.GetProperty("shellLift").GetSingle(),
        e.GetProperty("shellReach").GetSingle(), e.GetProperty("shellFade").GetSingle(), e.GetProperty("shellSway").GetSingle(),
        e.GetProperty("tuftDensity").GetSingle(), e.GetProperty("tuftReach").GetSingle(), e.GetProperty("tuftFade").GetSingle(),
        e.GetProperty("tuftWidth").GetSingle(), e.GetProperty("tuftHeight").GetSingle(), e.GetProperty("tuftScaleMin").GetSingle(),
        e.GetProperty("tuftScaleMax").GetSingle());
}
