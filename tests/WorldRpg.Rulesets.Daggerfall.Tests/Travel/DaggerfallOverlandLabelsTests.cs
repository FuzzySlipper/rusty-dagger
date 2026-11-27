using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Travel;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests.Travel;

public sealed class DaggerfallOverlandLabelsTests
{
    [Fact]
    public void Priority_culling_keeps_higher_tier_labels_and_rejects_overlapping_lower_tier_pills()
    {
        List<DaggerfallOverlandLabel> candidates =
        [
            new("Old Farmhouse", "Farmhouse", .50, .50),
            new("Dark Crypt", "Dungeon", .505, .50),
            new("Aldingwall", "Town", .502, .50),
            new("Daggerfall", "City", .50, .50),
            new("3 Orcs", "chase", .50, .50),
            new("Your party", "party", .50, .50),
            // Well-separated nearby town should survive cleanly.
            new("Gothway Garden", "Town", .75, .50),
        ];

        List<DaggerfallOverlandLabel> placed = DaggerfallOverlandLabels.Layout(candidates, aspect: 16.0 / 9.0);
        string[] names = [.. placed.Select(label => label.Name)];

        // Party sits below feet (+40%), chasing band sits above 64px figure (-66px), City sits at anchor (-100%): all three coexist without overlapping!
        Assert.Contains("Your party", names);
        Assert.Contains("3 Orcs", names);
        Assert.Contains("Daggerfall", names);
        Assert.Contains("Gothway Garden", names);

        // Overlapping Town, Dungeon, and Farmhouse right on top of Daggerfall City are culled.
        Assert.DoesNotContain("Aldingwall", names);
        Assert.DoesNotContain("Dark Crypt", names);
        Assert.DoesNotContain("Old Farmhouse", names);
    }

    [Fact]
    public void Geography_opacity_stays_hidden_near_and_fades_in_smoothly_at_wide_strategic_zoom()
    {
        Assert.Equal(0, DaggerfallOverlandLabels.GeographyOpacity(3_000));
        Assert.Equal(0, DaggerfallOverlandLabels.GeographyOpacity(12_000));
        Assert.Equal(0, DaggerfallOverlandLabels.GeographyOpacity(22_000));

        double midWide = DaggerfallOverlandLabels.GeographyOpacity(28_000);
        double strategic = DaggerfallOverlandLabels.GeographyOpacity(42_000);
        Assert.InRange(midWide, .2, .65);
        Assert.Equal(DaggerfallOverlandLabels.GeographyMaxOpacity, strategic, 4);
    }

    [Fact]
    public void Geography_labels_cull_overlapping_regions_without_hiding_foreground_cities()
    {
        List<DaggerfallOverlandLabel> candidates =
        [
            new("Iliac Bay", "bay", .50, .50, Opacity: .9),
            new("Daggerfall", "region", .51, .50, Opacity: .9),
            new("Sentinel", "region", .72, .72, Opacity: .9),
            new("Daggerfall", "City", .50, .50),
        ];

        List<DaggerfallOverlandLabel> placed = DaggerfallOverlandLabels.Layout(candidates, aspect: 16.0 / 9.0);
        Assert.Contains(placed, l => l.Name == "Iliac Bay" && l.Kind == "bay");
        Assert.Contains(placed, l => l.Name == "Sentinel" && l.Kind == "region");
        Assert.Contains(placed, l => l.Name == "Daggerfall" && l.Kind == "City");
        Assert.DoesNotContain(placed, l => l.Name == "Daggerfall" && l.Kind == "region");
    }

    [Fact]
    public void Place_visibility_tiers_thin_out_minor_places_and_dungeons_at_wider_zoom()
    {
        const double opening = 12_000;
        Assert.True(DaggerfallOverlandLabels.PlaceVisibleAtDistance(DaggerfallSiteKind.HomeFarms, 3_500, opening));
        Assert.False(DaggerfallOverlandLabels.PlaceVisibleAtDistance(DaggerfallSiteKind.HomeFarms, 12_000, opening));
        Assert.True(DaggerfallOverlandLabels.PlaceVisibleAtDistance(DaggerfallSiteKind.DungeonRuin, 12_000, opening));
        Assert.False(DaggerfallOverlandLabels.PlaceVisibleAtDistance(DaggerfallSiteKind.DungeonRuin, 42_000, opening));
        Assert.True(DaggerfallOverlandLabels.PlaceVisibleAtDistance(DaggerfallSiteKind.TownCity, 42_000, opening));
        Assert.True(DaggerfallOverlandLabels.PlaceVisibleAtDistance(DaggerfallSiteKind.TownHamlet, 42_000, opening));
    }

    [Fact]
    public void Nearest_water_name_selects_Iliac_Bay_inland_and_Eltheric_Ocean_west_of_the_peninsula()
    {
        DaggerfallWorldNames names = new(
            [new("bay", DaggerfallWorldNameZoom.World), new("sea", DaggerfallWorldNameZoom.World), new("realm", DaggerfallWorldNameZoom.World)],
            [
                new("high-rock", "High Rock", "realm", 300, 62),
                new("iliac-bay", "Iliac Bay", "bay", 480, 300),
                new("eltheric-ocean", "Eltheric Ocean", "sea", 105, 360),
            ]);

        Assert.Equal("Iliac Bay", DaggerfallOverlandLabels.NearestWaterName(names, 360, 290)?.Name);
        Assert.Equal("Eltheric Ocean", DaggerfallOverlandLabels.NearestWaterName(names, 120, 260)?.Name);
    }
}
