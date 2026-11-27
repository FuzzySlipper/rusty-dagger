using System.Numerics;
using Rusty.Engine;
using WorldRpg.Politics;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Heraldry (#11144): every crown and region bears arms from the one heraldry file, a region shows its liege's arms or
/// its own, and the guards in livery and the map's Realms view follow a change of liege.
/// </summary>
public sealed class HeraldryTests
{
    [Fact]
    public void Every_board_region_bears_arms_and_the_crowns_bear_their_castles_tapestries()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallHeraldry heraldry = definitions.Heraldry;
        Assert.Equal(definitions.PoliticalCanon!.Setup.Regions.Select(region => region.Id).Order(StringComparer.Ordinal),
            heraldry.Regions.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("Vert, a dragon rampant gules", heraldry.Seats["daggerfall"].Blazon);
        Assert.Same(heraldry.Seats["wayrest"], heraldry.Regions["wayrest"]);
        // A metal field gives its livery the charge's colour.
        Assert.Equal(DaggerfallTincture.Gules, heraldry.Seats["empire"].Livery);
        Assert.Equal(DaggerfallTincture.Vert, heraldry.Seats["daggerfall"].Livery);
        Assert.Equal("Per pale gules and or, a bull passant or", heraldry.Regions["isle-of-balfiera"].Blazon);
        // The page draws the arms' colours as the heraldry states them: linear Engine colours go back to sRGB.
        Assert.Equal("#11381D", DaggerfallHeraldry.Hex(heraldry.Colour(DaggerfallTincture.Vert)));
    }

    [Fact]
    public void A_region_shows_its_lieges_arms_or_its_own_and_its_liveried_guards_and_the_realms_view_follow_an_annexation()
    {
        using DaggerfallSession session = FreshSession();
        Board board = session.PoliticalBoard!.Board;
        DaggerfallRealmArms arms = session.RealmArms;
        Region glenpoint = board.Region("glenpoint"), betony = board.Region("betony"), daggerfall = board.Region("daggerfall");
        DaggerfallHeraldry heraldry = TestPayload.Definitions.Heraldry;

        Assert.Same(heraldry.Seats["daggerfall"], arms.Of(betony.CatalogRegion));
        Assert.Same(heraldry.Seats["daggerfall"], arms.Of(daggerfall.CatalogRegion));
        Assert.Null(glenpoint.Allegiance);
        Assert.Same(heraldry.Regions["glenpoint"], arms.Of(glenpoint.CatalogRegion));

        DaggerfallGuardLiveryResolution Palace() => session.GuardPosts.ResolveLivery(null, glenpoint.CatalogRegion, DaggerfallGuardPostKind.PalaceGuard);
        DaggerfallGuardLiveryResolution watch = session.GuardPosts.ResolveLivery(null, glenpoint.CatalogRegion, DaggerfallGuardPostKind.StreetWatch);
        Assert.Equal(($"region:{glenpoint.CatalogRegion}", $"watch:region:{glenpoint.CatalogRegion}"), (Palace().LiveryKey, watch.LiveryKey));
        Assert.Null(watch.LiveryTint);
        DaggerfallRealmsView before = new DaggerfallRealmsMap(TestPayload.Definitions, arms).Read()!;
        DaggerfallRealmShade Shade(DaggerfallRealmsView view) => view.Regions.Single(shade => shade.Region == glenpoint.CatalogRegion);
        Assert.False(Shade(before).Liege);

        // Wayrest annexes Glenpoint: its palace guards take Wayrest's livery, its street watch keeps the region's, and
        // the Realms view shades it Wayrest's colour with Wayrest in the key.
        board.SetAllegiance(glenpoint, board.Seat("wayrest"));
        Assert.Same(heraldry.Seats["wayrest"], arms.Of(glenpoint.CatalogRegion));
        Assert.Equal("crown:wayrest", Palace().LiveryKey);
        Assert.NotEqual(before.Regions.Single(shade => shade.Region == glenpoint.CatalogRegion).Colour, Shade(new DaggerfallRealmsMap(TestPayload.Definitions, arms).Read()!).Colour);
        DaggerfallRealmsView after = new DaggerfallRealmsMap(TestPayload.Definitions, arms).Read()!;
        Assert.Equal(("seat:wayrest", true), (Shade(after).Arms, Shade(after).Liege));
        Assert.Contains(after.Key, line => line.Arms == "seat:wayrest" && line.Name == board.Seat("wayrest").Name);
        Assert.Equal(session.GuardPosts.ResolveLivery(null, glenpoint.CatalogRegion, DaggerfallGuardPostKind.StreetWatch).LiveryKey, watch.LiveryKey);
    }

    [Fact]
    public void The_realms_view_holds_every_board_regions_shape_once_and_small()
    {
        using DaggerfallSession session = FreshSession();
        DaggerfallRealmsView realms = new DaggerfallRealmsMap(TestPayload.Definitions, session.RealmArms).Read()!;
        HashSet<int> boardRegions = [.. TestPayload.Definitions.PoliticalCanon!.Setup.Regions.Select(region => region.CatalogRegion)];
        Assert.Equal(boardRegions.Order(), realms.Outlines.Select(outline => outline.Region).Order());
        Assert.Equal(boardRegions.Order(), realms.Regions.Select(shade => shade.Region).Order());
        Assert.All(realms.Outlines, outline => Assert.Matches(@"^(M\d+ \d+(L\d+ \d+)+Z)+$", outline.Path));
        Assert.True(realms.Outlines.Sum(outline => outline.Path.Length) < 200_000,
            $"The regions' shapes run to {realms.Outlines.Sum(outline => outline.Path.Length)} characters.");
    }

    [Fact]
    public void Every_coat_has_a_banner_and_a_shield_as_8_bit_rgba_art()
    {
        DaggerfallArtReplacements art = Art();
        DaggerfallHeraldry heraldry = TestPayload.Definitions.Heraldry;
        Assert.Equal(heraldry.Seats.Values.Concat(heraldry.Regions.Values).Select(arms => arms.Id).Distinct().Order(StringComparer.Ordinal),
            art.Heraldry.Keys.Order(StringComparer.Ordinal));
        Assert.All(art.Heraldry.Values.SelectMany(coat => new[] { coat.Banner, coat.Shield }), file =>
        {
            byte[] png = TestContentFiles.ReadCopy(Path.Combine(TestData.RepositoryRoot, "content", file.Path));
            // The IHDR chunk: bit depth 8, colour type 6 (RGBA), which the Engine admits for a separate PNG.
            Assert.Equal((8, 6), (png[24], png[25]));
        });
    }

    [Fact]
    public void A_towns_gates_palace_and_offices_hang_banners_in_the_arms_its_region_shows_and_redraw_them_as_it_changes()
    {
        using var fixture = SourceBackedGuildBankSessionFixture.Create(configure: composition => composition with { ArtReplacements = Art() });
        using DaggerfallSession session = fixture.Start(fixture.ExteriorProfile);
        DaggerfallSiteProfile profile = fixture.ExteriorProfile;
        DaggerfallSiteRecord site = session.Site.ActiveSite!;

        // A pair either side of each palace, guild hall and temple door, and of each gate on both its faces.
        int offices = site.Exterior!.Buildings.Values.Count(building => building.Source.BuildingType is 11 or 14 or 16
            && profile.Doors.Any(door => door.ExteriorBuilding == building.Id));
        Assert.Equal(offices * 2 + profile.CityGates.Count * 4, profile.Banners.Count);
        Assert.Contains(profile.Banners, banner => banner.Post == DaggerfallSiteBannerPost.Office);
        Assert.All(profile.Banners, banner => Assert.InRange(banner.SurfaceNormal.Y, -1e-4f, 1e-4f));

        // The banners show the arms the region shows, and follow its liege.
        Assert.Equal(session.RealmArms.Of(site.Id.Region)!.Id, session.ActiveAppearance.BannerArmsShown);
        List<AppearanceFact> facts = [];
        session.ActiveAppearance.AppendLocationFacts(facts, DaggerfallPresentationObjectIds.ActiveLocationSlot);
        DaggerfallSiteBanner first = profile.Banners[0];
        // Each banner is drawn scaled to its cloth, deeper than the wall it hangs on is thick.
        AppearanceFact[] drawn = [.. facts.Where(fact => MathF.Abs(fact.Transform.Scale.Y - fact.Transform.Scale.Z) > 1e-3f)];
        Assert.Equal(profile.Banners.Count, drawn.Length);
        Assert.Contains(drawn, fact => MathF.Abs(fact.Transform.Scale.Y / fact.Transform.Scale.X - first.Drop / first.Width) < 1e-3f);
        // The arms are read every draw, so whatever the region shows next (the realms test follows a liege's change into
        // RealmArms) hangs at once, and arms without art hang nothing.
        session.ActiveAppearance.UseBanners(Art().Heraldry, _ => "region:glenpoint");
        Assert.Equal("region:glenpoint", session.ActiveAppearance.BannerArmsShown);
        session.ActiveAppearance.UseBanners(Art().Heraldry, _ => "region:nowhere");
        Assert.Null(session.ActiveAppearance.BannerArmsShown);
        facts.Clear();
        session.ActiveAppearance.AppendLocationFacts(facts, DaggerfallPresentationObjectIds.ActiveLocationSlot);
        Assert.DoesNotContain(facts, fact => MathF.Abs(fact.Transform.Scale.Y - fact.Transform.Scale.Z) > 1e-3f);

        // The unit banner hangs a unit deep from its top and faces +Z, its cloth and rod in their own groups.
        DaggerfallBannerMeshData mesh = DaggerfallSiteAppearance.BuildBannerMeshData();
        Assert.Equal(2, mesh.Groups.Length);
        Assert.Equal(-1f, mesh.Positions.Min(position => position.Y));
        Assert.Contains(mesh.Normals, normal => normal == Vector3.UnitZ);
    }

    private static DaggerfallArtReplacements Art() => DaggerfallArtReplacements.Read(TestContentFiles.Read(Path.Combine(TestData.RepositoryRoot, "content",
        "worldrpg/packs/daggerfall.art-replacements/payload.json")), "daggerfall.art-replacements");
}
