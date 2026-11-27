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

    [Fact]
    public void A_province_ceded_by_a_sued_peace_is_taken_by_force_with_exile_governor_and_occupation()
    {
        using var fixture = SourceBackedGuildBankSessionFixture.Create();
        using DaggerfallSession session = fixture.Start(fixture.ExteriorProfile);
        DaggerfallPoliticalBoard political = session.PoliticalBoard!;
        Board board = political.Board;
        Seat wayrest = board.Seat("wayrest"), daggerfall = board.Seat("daggerfall");
        Card peace = TestPayload.Definitions.PoliticalCanon!.Cards.Cards.First(card => card.Effects.Any(effect => effect.Kind == CardEffectKind.Peace));
        // Daggerfall holds a province on Wayrest's border and is losing the war; Wayrest is not weary, so the peace cedes.
        Region front = board.Regions.First(region => !region.Capital && region.Borders.Any(border => board.Region(border).Allegiance == "wayrest"));
        board.SetAllegiance(front, daggerfall);
        if (!board.Wars.Any(war => war.Side("wayrest") is { } side && war.Side("daggerfall") is { } other && side != other))
            board.OpenWar(wayrest, daggerfall, WarAim.Conquest);
        War war = board.Wars.First(held => held.Principal("daggerfall") && held.Principal("wayrest"));
        board.AddWeariness(war, "daggerfall", Wars.PeaceWeariness);
        war = board.War(war.Id);
        Assert.True(Wars.MaySue(war, daggerfall));
        Dictionary<string, string?> before = board.Regions.ToDictionary(region => region.Id, region => region.Allegiance);

        board.Schedule(new PendingMove($"{board.Turn}-daggerfall", "daggerfall", peace, [war.Id], daggerfall.Home, board.Turn, board.Turn + 1));
        political.AdvanceToDay(political.NextTurnDay, () => []);

        Region ceded = Assert.Single(board.Regions, region => before[region.Id] == "daggerfall" && region.Allegiance == "wayrest");
        DaggerfallOfficeHolder governor = session.OfficeHolders.ResolveRegionalRuler(ceded.CatalogRegion)!;
        Assert.Equal("Governor", governor.Title);
        Assert.DoesNotContain(board.Queued, nudge => nudge is HolderChanged);
        int turnDay = checked((int)(political.NextTurnDay - DaggerfallPoliticalBoard.DaysPerWeek));
        Assert.Contains(session.WorldConditions.ActiveForRegion(ceded.CatalogRegion, turnDay), token => token.SubjectTag == DaggerfallPoliticalBoard.OccupationTag);

        using DaggerfallSession restored = fixture.Restore(session.CaptureSave());
        Assert.Contains(restored.WorldConditions.ActiveForRegion(ceded.CatalogRegion, turnDay), token => token.SubjectTag == DaggerfallPoliticalBoard.OccupationTag);
        Assert.Equal(governor.DisplayName, restored.OfficeHolders.ResolveRegionalRuler(ceded.CatalogRegion)!.DisplayName);
    }
}
