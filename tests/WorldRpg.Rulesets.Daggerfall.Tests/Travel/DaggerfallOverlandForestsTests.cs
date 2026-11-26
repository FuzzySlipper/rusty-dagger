using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Travel;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests.Travel;

public sealed class DaggerfallOverlandForestsTests
{
    [Fact]
    public void Selects_climate_appropriate_forest_clump_kinds()
    {
        Assert.Equal(DaggerfallTravelMapForestKind.Palm, DaggerfallOverlandForests.KindFor(224, 0));
        Assert.Equal(DaggerfallTravelMapForestKind.Palm, DaggerfallOverlandForests.KindFor(225, 1));
        Assert.Equal(DaggerfallTravelMapForestKind.Palm, DaggerfallOverlandForests.KindFor(229, 2));
        Assert.Equal(DaggerfallTravelMapForestKind.Pine, DaggerfallOverlandForests.KindFor(226, 1));
        Assert.Equal(DaggerfallTravelMapForestKind.Pine, DaggerfallOverlandForests.KindFor(230, 3));
        Assert.Equal(DaggerfallTravelMapForestKind.Deadwood, DaggerfallOverlandForests.KindFor(228, 0));
        Assert.Equal(DaggerfallTravelMapForestKind.Deadwood, DaggerfallOverlandForests.KindFor(232, 1));

        // Temperate woodlands mix broadleaf clumps with ~25% pine stands.
        Assert.Equal(DaggerfallTravelMapForestKind.Pine, DaggerfallOverlandForests.KindFor(231, 0));
        Assert.Equal(DaggerfallTravelMapForestKind.Temperate, DaggerfallOverlandForests.KindFor(231, 1));
        Assert.Equal(DaggerfallTravelMapForestKind.Temperate, DaggerfallOverlandForests.KindFor(231, 2));
    }

    [Fact]
    public void Winter_snows_pines_and_temperate_canopies_but_keeps_desert_palms_green()
    {
        Color pineSnow = Color.FromSrgbHex(0x335a36, 1f);
        Color canopy = Color.FromSrgbHex(0x396228, 1f);
        Color frond = Color.FromSrgbHex(0x567c32, 1f);

        MaterialRequest summerPine = DaggerfallOverlandForests.ResolveMaterial(
            DaggerfallTravelMapForestKind.Pine, "pine-snow", pineSnow, winter: false, night: 0f, default);
        MaterialRequest winterPine = DaggerfallOverlandForests.ResolveMaterial(
            DaggerfallTravelMapForestKind.Pine, "pine-snow", pineSnow, winter: true, night: 0f, default);
        MaterialRequest winterOak = DaggerfallOverlandForests.ResolveMaterial(
            DaggerfallTravelMapForestKind.Temperate, "canopy", canopy, winter: true, night: 0f, default);
        MaterialRequest summerPalm = DaggerfallOverlandForests.ResolveMaterial(
            DaggerfallTravelMapForestKind.Palm, "frond", frond, winter: false, night: 0f, default);
        MaterialRequest winterPalm = DaggerfallOverlandForests.ResolveMaterial(
            DaggerfallTravelMapForestKind.Palm, "frond", frond, winter: true, night: 0f, default);

        Assert.Equal(pineSnow, summerPine.Color);
        Assert.True(winterPine.Color.R > summerPine.Color.R + .5f && winterPine.Color.B > summerPine.Color.B + .5f,
            "Winter pine upper tier should frost bright snow-white.");
        Assert.True(winterOak.Color.B > canopy.B + .3f, "Winter temperate broadleaf canopy should frost.");
        Assert.Equal(summerPalm.Color, winterPalm.Color);
        Assert.False(DaggerfallOverlandForests.IsDynamic(DaggerfallTravelMapForestKind.Palm, "frond"));
        Assert.True(DaggerfallOverlandForests.IsDynamic(DaggerfallTravelMapForestKind.Pine, "pine-snow"));
    }

    [Fact]
    public void Reach_and_edge_fade_taper_smoothly_and_disable_at_strategic_altitude()
    {
        double closeReach = DaggerfallOverlandForests.ReachFor(3_200);
        double midReach = DaggerfallOverlandForests.ReachFor(12_000);
        double highReach = DaggerfallOverlandForests.ReachFor(42_000);

        Assert.InRange(closeReach, 6_000, DaggerfallOverlandForests.MaxReachMetres);
        Assert.Equal(DaggerfallOverlandForests.MaxReachMetres, midReach);
        Assert.Equal(0, highReach);

        Assert.Equal(1f, DaggerfallOverlandForests.EdgeScale(midReach * .5, midReach));
        Assert.InRange(DaggerfallOverlandForests.EdgeScale(midReach * .9, midReach), .25f, .99f);
        Assert.Equal(0f, DaggerfallOverlandForests.EdgeScale(midReach, midReach));
    }

    [Fact]
    public void Candidate_cells_are_deterministic_jittered_and_have_unique_part_ids()
    {
        var a1 = DaggerfallOverlandForests.Candidate(120, 88);
        var a2 = DaggerfallOverlandForests.Candidate(120, 88);
        var b = DaggerfallOverlandForests.Candidate(121, 88);

        Assert.Equal(a1, a2);
        Assert.NotEqual(a1.Hash, b.Hash);
        Assert.NotEqual(DaggerfallOverlandForests.PartId(120, 88), DaggerfallOverlandForests.PartId(121, 88));
        Assert.True(DaggerfallOverlandForests.PlaceClearanceMetres(DaggerfallSiteKind.TownCity)
            > DaggerfallOverlandForests.PlaceClearanceMetres(DaggerfallSiteKind.TownVillage));
    }

    [Fact]
    public void Art_pack_ships_all_four_forest_clump_models_under_25_KiB_total()
    {
        string contentRoot = Path.Combine(TestData.RepositoryRoot, "content");
        DaggerfallArtReplacements art = DaggerfallArtReplacements.Read(
            TestContentFiles.Read(Path.Combine(contentRoot, DaggerfallArtReplacements.Folder, "payload.json")),
            "daggerfall.art-replacements");
        DaggerfallTravelMapArt map = Assert.IsType<DaggerfallTravelMapArt>(art.TravelMap);
        Assert.NotNull(map.Forests);
        Assert.Equal(4, map.Forests!.Count);

        long totalBytes = 0;
        foreach ((DaggerfallTravelMapForestKind kind, DaggerfallTravelMapPlace clump) in map.Forests)
        {
            Assert.NotNull(clump.SlotNames);
            Assert.Equal(clump.Slots.Count, clump.SlotNames!.Count);
            Assert.Contains("shadow", clump.SlotNames);
            string fullPath = Path.Combine(contentRoot, clump.File.Path);
            Assert.True(File.Exists(fullPath), $"Missing forest mesh for {kind}: {fullPath}");
            totalBytes += new FileInfo(fullPath).Length;
        }
        Assert.True(totalBytes < 25_600, $"Expected all 4 forest clump meshes under 25 KiB total, got {totalBytes} bytes.");
    }
}
