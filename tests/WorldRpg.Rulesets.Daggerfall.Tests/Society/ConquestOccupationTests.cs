using WorldRpg.Politics;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Conquest (#11143): a region taken by force loses its lord to exile, takes a governor from the conqueror's court and
/// is occupied for 13 weeks, which a save keeps, and the board never hears its own writes back.
/// </summary>
public sealed class ConquestOccupationTests
{
    [Fact]
    public void A_conquest_exiles_the_lord_seats_a_governor_and_occupies_the_region_through_a_save_for_thirteen_weeks()
    {
        using var fixture = SourceBackedGuildBankSessionFixture.Create();
        using DaggerfallSession session = fixture.Start(fixture.ExteriorProfile);
        DaggerfallPoliticalBoard political = session.PoliticalBoard!;
        Board board = political.Board;
        Seat wayrest = board.Seat("wayrest"), daggerfall = board.Seat("daggerfall");
        Card conquer = TestPayload.Definitions.PoliticalCanon!.Cards.Cards.First(card => card.Effects.Any(effect => effect.Kind == CardEffectKind.Conquer));
        Region target = board.Regions.First(region => !region.Capital && region.Borders.Any(border => board.Region(border).Allegiance == "wayrest"));
        board.SetAllegiance(target, daggerfall);
        if (!board.Wars.Any(war => war.Side("wayrest") is { } side && war.Side("daggerfall") is { } other && side != other))
            board.OpenWar(wayrest, daggerfall, WarAim.Conquest);
        board.AdjustInfluence(target, wayrest, 2);
        board.AdjustControl(target, -target.Control);
        string lord = session.OfficeHolders.ResolveRegionalRuler(target.CatalogRegion)!.DisplayName;
        int exilesBefore = session.OfficeLedger.ExileCount;

        board.Schedule(new PendingMove($"{board.Turn}-wayrest", "wayrest", conquer, [target.Id], target.Borders.First(border => board.Region(border).Allegiance == "wayrest"),
            board.Turn, board.Turn + 1));
        IReadOnlyList<DaggerfallPoliticalNewsItem> news = political.AdvanceToDay(political.NextTurnDay, () => []);
        Assert.Equal("wayrest", target.Allegiance);

        // The lord is in exile and a noble of Wayrest's court governs, as the ledger holds it.
        Assert.True(session.OfficeLedger.ExileCount > exilesBefore);
        DaggerfallOfficeHolder governor = session.OfficeHolders.ResolveRegionalRuler(target.CatalogRegion)!;
        Assert.True(governor.HasLivingHolder);
        Assert.NotEqual(lord, governor.DisplayName);
        Assert.Equal("Governor", governor.Title);
        Assert.DoesNotContain(board.Queued, nudge => nudge is HolderChanged);

        // The region is occupied: the conqueror's crackdown, and the notice boards and the news name the governor.
        // The board was advanced ahead of the calendar, so the tokens are read on the day of its last turn.
        static int LastTurnDay(DaggerfallSession of) => checked((int)(of.PoliticalBoard!.NextTurnDay - DaggerfallPoliticalBoard.DaysPerWeek));
        DaggerfallWorldConditionToken Occupation(DaggerfallSession of) => Assert.Single(of.WorldConditions.ActiveForRegion(target.CatalogRegion, LastTurnDay(of)),
            token => token.SubjectTag == DaggerfallPoliticalBoard.OccupationTag);
        DaggerfallWorldConditionToken token = Occupation(session);
        Assert.Equal(DaggerfallWorldConditionKind.CrimeCrackdown, token.Kind);
        Assert.Contains(governor.DisplayName, token.Headline);
        Assert.Contains(news, item => item.Headline.Contains(governor.DisplayName, StringComparison.Ordinal));

        // A save mid-occupation keeps the occupation and the governor.
        using DaggerfallSession restored = fixture.Restore(session.CaptureSave());
        Assert.Equal(token.Headline, Occupation(restored).Headline);
        Assert.Equal(governor.DisplayName, restored.OfficeHolders.ResolveRegionalRuler(target.CatalogRegion)!.DisplayName);

        // Thirteen weeks on, the occupation lifts and the governor's court is the region's court.
        DaggerfallPoliticalBoard later = restored.PoliticalBoard!;
        for (int week = 0; week < DaggerfallPoliticalBoard.OccupationWeeks; week++)
            later.AdvanceToDay(later.NextTurnDay, () => []);
        Assert.DoesNotContain(restored.WorldConditions.ActiveForRegion(target.CatalogRegion, LastTurnDay(restored)),
            held => held.SubjectTag == DaggerfallPoliticalBoard.OccupationTag);
        Assert.Equal(governor.DisplayName, restored.OfficeHolders.ResolveRegionalRuler(target.CatalogRegion)!.DisplayName);
    }
}
