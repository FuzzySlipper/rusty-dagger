using WorldRpg.Politics;
using WorldRpg.Rulesets.Daggerfall.Content;
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
}
