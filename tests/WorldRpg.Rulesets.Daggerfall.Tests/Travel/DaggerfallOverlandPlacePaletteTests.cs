using System.Numerics;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Travel;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests.Travel;

public sealed class DaggerfallOverlandPlacePaletteTests
{
    [Fact]
    public void Classifies_Daggerfall_climates_into_architectural_families()
    {
        Assert.Equal(DaggerfallOverlandPlaceClimate.Desert, DaggerfallOverlandPlacePalette.Classify(224));
        Assert.Equal(DaggerfallOverlandPlaceClimate.Desert, DaggerfallOverlandPlacePalette.Classify(225));
        Assert.Equal(DaggerfallOverlandPlaceClimate.Desert, DaggerfallOverlandPlacePalette.Classify(229));
        Assert.Equal(DaggerfallOverlandPlaceClimate.Highland, DaggerfallOverlandPlacePalette.Classify(226));
        Assert.Equal(DaggerfallOverlandPlaceClimate.Highland, DaggerfallOverlandPlacePalette.Classify(230));
        Assert.Equal(DaggerfallOverlandPlaceClimate.Temperate, DaggerfallOverlandPlacePalette.Classify(231));
        Assert.Equal(DaggerfallOverlandPlaceClimate.Temperate, DaggerfallOverlandPlacePalette.Classify(232));
        Assert.Equal(DaggerfallOverlandPlaceClimate.Temperate, DaggerfallOverlandPlacePalette.Classify(228));
    }

    [Fact]
    public void Windows_stay_unlit_by_day_and_glow_warm_amber_at_night()
    {
        Color darkGlass = Color.FromSrgbHex(0x2c3340, 1f);
        MaterialRequest day = DaggerfallOverlandPlacePalette.Resolve(
            DaggerfallOverlandPlaceClimate.Temperate, "window", darkGlass, winter: false, night: .10f, default);
        MaterialRequest winterNoon = DaggerfallOverlandPlacePalette.Resolve(
            DaggerfallOverlandPlaceClimate.Temperate, "window", darkGlass, winter: true, night: .42f, default);
        MaterialRequest dusk = DaggerfallOverlandPlacePalette.Resolve(
            DaggerfallOverlandPlaceClimate.Temperate, "window", darkGlass, winter: false, night: .72f, default);
        MaterialRequest midnight = DaggerfallOverlandPlacePalette.Resolve(
            DaggerfallOverlandPlaceClimate.Temperate, "window", darkGlass, winter: false, night: 1f, default);

        Assert.Equal(0f, day.EmissionIntensity);
        Assert.Equal(Vector3.Zero, day.EmissionColor);
        Assert.Equal(darkGlass, day.Color);
        Assert.Equal(0f, winterNoon.EmissionIntensity);

        Assert.InRange(dusk.EmissionIntensity, .5f, DaggerfallOverlandPlacePalette.MaxWindowEmission - .5f);
        Assert.Equal(DaggerfallOverlandPlacePalette.WindowEmissionColor, dusk.EmissionColor);

        Assert.Equal(DaggerfallOverlandPlacePalette.MaxWindowEmission, midnight.EmissionIntensity);
        Assert.True(midnight.Color.R > midnight.Color.B, "Lit windows shift warm amber rather than cool blue.");
    }

    [Fact]
    public void Winter_frosts_temperate_and_highland_roofs_but_never_desert_or_walls()
    {
        Color tile = Color.FromSrgbHex(0xa4492f, 1f);
        Color stone = Color.FromSrgbHex(0x8e8a82, 1f);

        MaterialRequest summerRoof = DaggerfallOverlandPlacePalette.Resolve(
            DaggerfallOverlandPlaceClimate.Temperate, "tile", tile, winter: false, night: 0f, default);
        MaterialRequest winterRoof = DaggerfallOverlandPlacePalette.Resolve(
            DaggerfallOverlandPlaceClimate.Temperate, "tile", tile, winter: true, night: 0f, default);
        MaterialRequest winterWall = DaggerfallOverlandPlacePalette.Resolve(
            DaggerfallOverlandPlaceClimate.Temperate, "stone", stone, winter: true, night: 0f, default);
        MaterialRequest desertWinterRoof = DaggerfallOverlandPlacePalette.Resolve(
            DaggerfallOverlandPlaceClimate.Desert, "tile", tile, winter: true, night: 0f, default);
        MaterialRequest desertSummerRoof = DaggerfallOverlandPlacePalette.Resolve(
            DaggerfallOverlandPlaceClimate.Desert, "tile", tile, winter: false, night: 0f, default);

        Assert.Equal(tile, summerRoof.Color);
        Assert.True(winterRoof.Color.B > summerRoof.Color.B + .4f && winterRoof.Color.G > summerRoof.Color.G + .4f,
            "Winter roof should frost bright snow-white over terracotta.");
        Assert.Equal(stone, winterWall.Color);
        Assert.Equal(desertSummerRoof.Color, desertWinterRoof.Color);
        Assert.False(DaggerfallOverlandPlacePalette.IsDynamic(DaggerfallOverlandPlaceClimate.Desert, "tile"));
        Assert.True(DaggerfallOverlandPlacePalette.IsDynamic(DaggerfallOverlandPlaceClimate.Temperate, "tile"));
    }

    [Fact]
    public void Desert_palette_shifts_stone_to_sandstone_and_slate_to_glazed_teal()
    {
        Color stone = Color.FromSrgbHex(0x8e8a82, 1f);
        Color slate = Color.FromSrgbHex(0x4a5262, 1f);

        MaterialRequest temperateStone = DaggerfallOverlandPlacePalette.Resolve(
            DaggerfallOverlandPlaceClimate.Temperate, "stone", stone, winter: false, night: 0f, default);
        MaterialRequest desertStone = DaggerfallOverlandPlacePalette.Resolve(
            DaggerfallOverlandPlaceClimate.Desert, "stone", stone, winter: false, night: 0f, default);
        MaterialRequest desertSlate = DaggerfallOverlandPlacePalette.Resolve(
            DaggerfallOverlandPlaceClimate.Desert, "slate", slate, winter: false, night: 0f, default);

        Assert.True(desertStone.Color.R > temperateStone.Color.R + .2f, "Desert masonry should read warm golden sandstone.");
        Assert.True(desertSlate.Color.G > slate.G + .08f, "Desert domes/caps should shift toward glazed teal.");
    }

    [Fact]
    public void Contact_shadow_uses_alpha_blend_and_softens_at_night_and_on_snow()
    {
        Color dark = Color.FromSrgbHex(0x141820, 1f);
        MaterialRequest summerDay = DaggerfallOverlandPlacePalette.Resolve(
            DaggerfallOverlandPlaceClimate.Temperate, "shadow", dark, winter: false, night: 0f, default);
        MaterialRequest winterDay = DaggerfallOverlandPlacePalette.Resolve(
            DaggerfallOverlandPlaceClimate.Temperate, "shadow", dark, winter: true, night: 0f, default);
        MaterialRequest summerNight = DaggerfallOverlandPlacePalette.Resolve(
            DaggerfallOverlandPlaceClimate.Temperate, "shadow", dark, winter: false, night: 1f, default);

        Assert.Equal(MaterialAlphaMode.Blend, summerDay.AlphaMode);
        Assert.True(summerDay.Unlit);
        Assert.InRange(summerDay.Color.A, .55f, .75f);
        Assert.True(winterDay.Color.A < summerDay.Color.A, "Snow shadows soften so they do not read harsh black.");
        Assert.True(summerNight.Color.A < summerDay.Color.A, "Night shadows soften when sun contrast drops.");
    }

    [Fact]
    public void Art_pack_ships_contact_shadow_and_semantic_slot_names_including_windows_on_settlements()
    {
        DaggerfallArtReplacements art = DaggerfallArtReplacements.Read(
            TestContentFiles.Read(Path.Combine(TestData.RepositoryRoot, "content", DaggerfallArtReplacements.Folder, "payload.json")),
            "daggerfall.art-replacements");
        DaggerfallTravelMapArt map = Assert.IsType<DaggerfallTravelMapArt>(art.TravelMap);
        Assert.NotNull(map.Shadow);

        foreach ((DaggerfallSiteKind kind, DaggerfallTravelMapPlace place) in map.Places)
        {
            Assert.NotNull(place.SlotNames);
            Assert.Equal(place.Slots.Count, place.SlotNames!.Count);
            Assert.Contains("shadow", place.SlotNames);
            if (kind is DaggerfallSiteKind.TownCity or DaggerfallSiteKind.TownHamlet or DaggerfallSiteKind.TownVillage
                or DaggerfallSiteKind.Tavern or DaggerfallSiteKind.HomeFarms or DaggerfallSiteKind.HomeWealthy
                or DaggerfallSiteKind.HomePoor or DaggerfallSiteKind.ReligionTemple)
            {
                Assert.Contains("window", place.SlotNames);
            }
            else
            {
                Assert.DoesNotContain("window", place.SlotNames);
            }
        }
    }
}
