using Rusty.Engine;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// A town's street dressing for its region's conditions (#11169): which conditions put which dressing out, where the
/// pieces stand, the art they are drawn in, and a live town dressing up and clearing with its conditions.
/// </summary>
public sealed class TownDressingTests
{
    private const int Region = 17;

    private static DaggerfallArtReplacements Art() => DaggerfallArtReplacements.Read(TestContentFiles.Read(Path.Combine(TestData.RepositoryRoot, "content",
        "worldrpg/packs/daggerfall.art-replacements/payload.json")), "daggerfall.art-replacements");

    private static DaggerfallWorldConditionToken Token(string id, DaggerfallWorldConditionKind kind, string? tag, int severity, int started = 10, int? expires = 40) =>
        new(TokenId: id, Kind: kind, Region: Region, SubjectTag: tag, Severity: severity, StartedDay: started, ExpiresDay: expires);

    [Fact]
    public void Festival_prosperity_unrest_and_witch_hunt_conditions_put_out_their_dressing_at_their_strongest()
    {
        DaggerfallWorldConditions conditions = new();
        int day = 12;
        DaggerfallTownDressing dressing = new(conditions, () => day);
        conditions.Set(Token("festival", DaggerfallWorldConditionKind.Decree, DaggerfallConditionPrices.FestivalTag, 1));
        conditions.Set(Token("festival-big", DaggerfallWorldConditionKind.Decree, DaggerfallConditionPrices.FestivalTag, 3));
        conditions.Set(Token("prosperity", DaggerfallWorldConditionKind.Decree, DaggerfallConditionPrices.ProsperityTag, 2));
        conditions.Set(Token("unrest", DaggerfallWorldConditionKind.BorderTension, DaggerfallTownDressing.UnrestTag, 1));
        conditions.Set(Token("witch-hunt", DaggerfallWorldConditionKind.CrimeCrackdown, DaggerfallConditionPrices.WitchHuntTag, 2, expires: 20));
        // Neither a plain crackdown, a war's tension nor a recruitment decree dresses the streets.
        conditions.Set(Token("crackdown", DaggerfallWorldConditionKind.CrimeCrackdown, null, 3));
        conditions.Set(Token("war", DaggerfallWorldConditionKind.BorderTension, null, 3));
        conditions.Set(Token("recruitment", DaggerfallWorldConditionKind.Decree, DaggerfallConditionPrices.RecruitmentTag, 3));

        Assert.Equal([(DaggerfallTownDressingKind.Festival, 3), (DaggerfallTownDressingKind.Prosperity, 2), (DaggerfallTownDressingKind.Unrest, 1),
            (DaggerfallTownDressingKind.WitchHunt, 2)], dressing.For(Region));
        Assert.Empty(dressing.For(Region + 1));

        // The witch hunt's dressing comes down when its token expires.
        day = 25;
        Assert.DoesNotContain(dressing.For(Region), entry => entry.Kind == DaggerfallTownDressingKind.WitchHunt);
    }

    [Fact]
    public void Each_kind_stands_its_pieces_on_their_own_spots_and_no_spot_holds_two()
    {
        IReadOnlyList<DaggerfallTownDressingPiece> pieces = Art().TownDressing;
        DaggerfallSiteDressingSpot Spot(string id, DaggerfallDressingSpotKind kind) => new(id, WorldPoint.From(new(0f, 0f, 0f)), kind);
        DaggerfallSiteDressingSpot[] spots = [Spot("sq1", DaggerfallDressingSpotKind.Square), Spot("sq2", DaggerfallDressingSpotKind.Square),
            Spot("sq3", DaggerfallDressingSpotKind.Square), Spot("shop1", DaggerfallDressingSpotKind.Shop), Spot("shop2", DaggerfallDressingSpotKind.Shop),
            Spot("temple1", DaggerfallDressingSpotKind.Temple), Spot("temple2", DaggerfallDressingSpotKind.Temple),
            Spot("seat1", DaggerfallDressingSpotKind.Seat), Spot("seat2", DaggerfallDressingSpotKind.Seat)];

        // A small festival puts out two pieces about the square: the maypole first, then the garland arch.
        IReadOnlyList<(int Spot, DaggerfallTownDressingPiece Piece)> festival = DaggerfallTownDressingLayout.Place(spots, pieces,
            [(DaggerfallTownDressingKind.Festival, 1)]);
        Assert.Equal([("sq1", "festival-maypole"), ("sq2", "festival-garland-arch")], festival.Select(entry => (spots[entry.Spot].Id, entry.Piece.Id)));

        // A witch hunt beside it takes the temple's spots first, and what is left of the square.
        IReadOnlyList<(int Spot, DaggerfallTownDressingPiece Piece)> both = DaggerfallTownDressingLayout.Place(spots, pieces,
            [(DaggerfallTownDressingKind.WitchHunt, 2), (DaggerfallTownDressingKind.Festival, 1)]);
        Assert.Equal(6, both.Count);
        Assert.Equal(both.Count, both.Select(entry => entry.Spot).Distinct().Count());
        Assert.All(both, entry => Assert.Contains(spots[entry.Spot].Kind, entry.Piece.Spots));
        Assert.Contains(both, entry => entry.Piece.Id == "witch-hunt-pyre" && spots[entry.Spot].Kind == DaggerfallDressingSpotKind.Temple);

        // However strong, a kind puts out no more than the town has room for.
        IReadOnlyList<(int Spot, DaggerfallTownDressingPiece Piece)> crowded = DaggerfallTownDressingLayout.Place(spots, pieces,
            [(DaggerfallTownDressingKind.Prosperity, 5)]);
        Assert.Equal(spots.Count(spot => spot.Kind is DaggerfallDressingSpotKind.Shop or DaggerfallDressingSpotKind.Square), crowded.Count);
        Assert.Empty(DaggerfallTownDressingLayout.Place(spots, pieces, []));
    }

    [Fact]
    public void Every_kind_has_flats_in_eight_bit_rgba()
    {
        IReadOnlyList<DaggerfallTownDressingPiece> pieces = Art().TownDressing;
        Assert.All(Enum.GetValues<DaggerfallTownDressingKind>(), kind => Assert.Contains(pieces, piece => piece.Kind == kind));
        Assert.All(pieces, piece =>
        {
            Assert.NotEmpty(piece.Spots);
            byte[] png = TestContentFiles.ReadCopy(Path.Combine(TestData.RepositoryRoot, "content", piece.Sprite.File.Path));
            // The IHDR chunk: bit depth 8, colour type 6 (RGBA), which the Engine admits for a separate PNG.
            Assert.Equal((8, 6), (png[24], png[25]));
        });
    }

    [Fact]
    public void A_town_dresses_for_its_regions_festival_and_clears_when_it_ends()
    {
        using var fixture = SourceBackedGuildBankSessionFixture.Create(configure: composition => composition with { ArtReplacements = Art() });
        using DaggerfallSession session = fixture.Start(fixture.ExteriorProfile);
        DaggerfallSiteRecord charing = session.Site.ActiveSite!;
        IReadOnlyList<DaggerfallSiteDressingSpot> spots = fixture.ExteriorProfile.DressingSpots;
        Assert.All(Enum.GetValues<DaggerfallDressingSpotKind>(), kind => Assert.Contains(spots, spot => spot.Kind == kind));
        Assert.Equal(spots.Count, spots.Select(spot => spot.Id).Distinct().Count());
        Assert.Empty(session.ActiveAppearance.TownDressingShown);

        int today = checked((int)session.Calendar.DayNumber);
        session.WorldConditions.Set(new DaggerfallWorldConditionToken(TokenId: "test:festival", Kind: DaggerfallWorldConditionKind.Decree,
            Region: charing.Id.Region, SubjectTag: DaggerfallConditionPrices.FestivalTag, Severity: 2, StartedDay: today, ExpiresDay: today + 7));
        IReadOnlyList<(string Spot, string Piece)> shown = session.ActiveAppearance.TownDressingShown;
        Assert.Equal(DaggerfallTownDressingLayout.PiecesPerSeverity * 2, shown.Count);
        Assert.All(shown, entry => Assert.StartsWith("festival-", entry.Piece));
        List<AppearanceFact> facts = [];
        session.ActiveAppearance.AppendLocationFacts(facts, DaggerfallPresentationObjectIds.ActiveLocationSlot);

        Assert.True(session.WorldConditions.Remove("test:festival"));
        Assert.Empty(session.ActiveAppearance.TownDressingShown);
    }
}
