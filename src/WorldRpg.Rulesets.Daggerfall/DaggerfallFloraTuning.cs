using System.Text.Json;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// How refreshed nature grows over open ground. Each climate's nature records grow as layers of an Engine vegetation field,
/// each record as often as the others, wherever the ground's look is soil. Classic art keeps the donor's placement.
/// </summary>
/// <param name="Density">How many times the donor's count of nature grows: 1 is one record in a tile of soil as often as the donor's chance.</param>
/// <param name="GrassChance">The donor's chance a tile grows nature, on grass.</param>
/// <param name="DirtChance">The same on dirt.</param>
/// <param name="StoneChance">The same on stone.</param>
/// <param name="DesertShare">The share of that chance a desert keeps.</param>
/// <param name="ReachPerMetre">How far a record is drawn per metre of its height, so a flower is gone long before a tree.</param>
/// <param name="Reach">The farthest any record is drawn, inside near ground's own reach.</param>
/// <param name="Fade">How far before its reach a record shrinks away, so the edge never pops.</param>
/// <param name="SlopeLimitDegrees">The steepest ground anything grows on.</param>
internal sealed record DaggerfallFloraTuning(float Density, float GrassChance, float DirtChance, float StoneChance, float DesertShare,
    float ReachPerMetre, float Reach, float Fade, float SlopeLimitDegrees)
{
    internal static DaggerfallFloraTuning Default { get; } = new(1, .9f, .2f, .05f, .25f, 400, 3_200, 600, 50);

    internal DaggerfallFloraTuning Validate()
    {
        if (!float.IsFinite(Density) || Density < 0) throw new ArgumentException("Flora grows at a finite density, 0 or more.");
        if (GrassChance is < 0 or > 1 || DirtChance is < 0 or > 1 || StoneChance is < 0 or > 1 || DesertShare is < 0 or > 1)
            throw new ArgumentException("Flora's chances and desert share are each from 0 to 1.");
        if (!float.IsFinite(ReachPerMetre) || ReachPerMetre <= 0 || !float.IsFinite(Reach) || Reach <= 0 || !float.IsFinite(Fade) || Fade < 0 || Fade > Reach)
            throw new ArgumentException("Flora reaches a positive distance and fades within it.");
        if (SlopeLimitDegrees is <= 0 or > 90) throw new ArgumentException("Flora grows on slopes up to some angle of 90 degrees or less.");
        return this;
    }

    /// <summary>How far a record this tall is drawn.</summary>
    internal float ReachOf(float height) => Math.Clamp(height * ReachPerMetre, Fade, Reach);

    internal static DaggerfallFloraTuning Read(JsonElement e) => new(
        e.GetProperty("density").GetSingle(), e.GetProperty("grassChance").GetSingle(), e.GetProperty("dirtChance").GetSingle(),
        e.GetProperty("stoneChance").GetSingle(), e.GetProperty("desertShare").GetSingle(), e.GetProperty("reachPerMetre").GetSingle(),
        e.GetProperty("reach").GetSingle(), e.GetProperty("fade").GetSingle(), e.GetProperty("slopeLimitDegrees").GetSingle());
}
