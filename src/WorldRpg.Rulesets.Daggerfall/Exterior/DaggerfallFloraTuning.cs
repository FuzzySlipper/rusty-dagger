using System.Text.Json;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// How refreshed nature grows over open ground. Each climate's nature records grow as layers of an Engine vegetation field,
/// each record as often as the others, wherever the ground's look is soil. Classic art keeps the donor's placement.
/// </summary>
/// <param name="Density">How many times the donor's count of nature grows: 1 is one record in a tile of soil as often as the donor's chance.</param>
/// <param name="ClimateDensity">How many times more again a climate grows, by its name; a climate not named grows at 1.</param>
/// <param name="GrassChance">The donor's chance a tile grows nature, on grass.</param>
/// <param name="DirtChance">The same on dirt.</param>
/// <param name="StoneChance">The same on stone.</param>
/// <param name="DesertShare">The share of that chance a desert keeps.</param>
/// <param name="ReachPerMetre">How far a record is drawn per metre of its height, so a flower is gone long before a tree.</param>
/// <param name="Reach">The farthest any record is drawn, inside near ground's own reach.</param>
/// <param name="Fade">How far before its reach a record shrinks away, so the edge never pops.</param>
/// <param name="SlopeLimitDegrees">The steepest ground anything grows on.</param>
/// <param name="FarReach">How far the far forest grows over far ground, in metres.</param>
/// <param name="FarShare">The far forest's density as a share of near flora's.</param>
/// <param name="FarScale">How much larger a far forest's copy is drawn than its sprite, so a thinner forest still reads as one.</param>
/// <param name="FarTreeMetres">The least height of a record the far forest grows: only trees show that far.</param>
/// <param name="TreeMeshes">Whether a record with a refreshed tree mesh grows as the mesh rather than its sprite.</param>
/// <param name="TreeImpostorHeights">How far from the eye a tree mesh gives way to a card the renderer bakes of it, in the
/// tree's own heights: the card holds 64 texels a view, so it should take over only once the tree stands about that tall on screen.</param>
internal sealed record DaggerfallFloraTuning(float Density, IReadOnlyDictionary<string, float> ClimateDensity, float GrassChance, float DirtChance, float StoneChance, float DesertShare,
    float ReachPerMetre, float Reach, float Fade, float SlopeLimitDegrees, float FarReach, float FarShare, float FarScale, float FarTreeMetres, bool TreeMeshes, float TreeImpostorHeights)
{
    internal static DaggerfallFloraTuning Default { get; } = new(3, new Dictionary<string, float>(), .9f, .2f, .05f, .25f, 400, 3_200, 600, 50, 8_192, .2f, 1.6f, 6, true, 12);

    internal DaggerfallFloraTuning Validate()
    {
        if (!float.IsFinite(Density) || Density < 0 || ClimateDensity.Values.Any(value => !float.IsFinite(value) || value < 0))
            throw new ArgumentException("Flora grows at a finite density, 0 or more, in every climate.");
        if (GrassChance is < 0 or > 1 || DirtChance is < 0 or > 1 || StoneChance is < 0 or > 1 || DesertShare is < 0 or > 1)
            throw new ArgumentException("Flora's chances and desert share are each from 0 to 1.");
        if (!float.IsFinite(ReachPerMetre) || ReachPerMetre <= 0 || !float.IsFinite(Reach) || Reach <= 0 || !float.IsFinite(Fade) || Fade < 0 || Fade > Reach)
            throw new ArgumentException("Flora reaches a positive distance and fades within it.");
        if (SlopeLimitDegrees is <= 0 or > 90) throw new ArgumentException("Flora grows on slopes up to some angle of 90 degrees or less.");
        if (!float.IsFinite(FarReach) || FarReach <= Reach || !float.IsFinite(FarShare) || FarShare is < 0 or > 1
            || !float.IsFinite(FarScale) || FarScale <= 0 || !float.IsFinite(FarTreeMetres) || FarTreeMetres < 0)
            throw new ArgumentException("The far forest reaches past near flora, at a share of its density from 0 to 1, a positive scale and a height of 0 or more.");
        if (!float.IsFinite(TreeImpostorHeights) || TreeImpostorHeights <= 0) throw new ArgumentException("Tree meshes give way to their cards some distance out.");
        return this;
    }

    /// <summary>
    /// Each nature archive's share of the whole's density, summer and winter set alike, from the climates the grid names. A climate the tuning
    /// names but the grid doesn't would never grow as tuned, so it is refused.
    /// </summary>
    internal IReadOnlyDictionary<int, float> ArchiveDensity(IEnumerable<DaggerfallClimateValueDefinition> climates)
    {
        DaggerfallClimateValueDefinition[] named = [.. climates.Where(climate => ClimateDensity.ContainsKey(climate.Name))];
        if (ClimateDensity.Keys.Except(named.Select(climate => climate.Name)).ToArray() is { Length: > 0 } unknown)
            throw new InvalidOperationException($"Flora's climate density names climates the world has none of: {string.Join(", ", unknown)}.");
        Dictionary<int, float> density = [];
        foreach (DaggerfallClimateValueDefinition climate in named)
        foreach (DaggerfallExteriorSeason season in new[] { DaggerfallExteriorSeason.Summer, DaggerfallExteriorSeason.Winter })
            density[DaggerfallExteriorEnvironment.NatureTextureArchive(climate.Value, season)] = ClimateDensity[climate.Name];
        return density;
    }

    /// <summary>How far a record this tall is drawn.</summary>
    internal float ReachOf(float height) => Math.Clamp(height * ReachPerMetre, Fade, Reach);

    internal static DaggerfallFloraTuning Read(JsonElement e) => new(
        e.GetProperty("density").GetSingle(),
        e.GetProperty("climateDensity").EnumerateObject().ToDictionary(climate => climate.Name, climate => climate.Value.GetSingle(), StringComparer.Ordinal),
        e.GetProperty("grassChance").GetSingle(), e.GetProperty("dirtChance").GetSingle(),
        e.GetProperty("stoneChance").GetSingle(), e.GetProperty("desertShare").GetSingle(), e.GetProperty("reachPerMetre").GetSingle(),
        e.GetProperty("reach").GetSingle(), e.GetProperty("fade").GetSingle(), e.GetProperty("slopeLimitDegrees").GetSingle(),
        e.GetProperty("farReach").GetSingle(), e.GetProperty("farShare").GetSingle(), e.GetProperty("farScale").GetSingle(), e.GetProperty("farTreeMetres").GetSingle(),
        e.GetProperty("treeMeshes").GetBoolean(), e.GetProperty("treeImpostorHeights").GetSingle());
}
