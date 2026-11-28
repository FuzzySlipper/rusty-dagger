using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Politics;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Travel;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// #11180: each grain agreement's weekly ration travels the overland map as a convoy flying its giver's arms; its arrival
/// clears the term's losses, a loss is a missed ration, and two in a row end the deal as "route raided".
/// </summary>
public sealed class RationConvoyTests
{
    [Fact]
    public void A_grain_deal_sends_its_ration_as_a_convoy_flying_the_givers_arms_that_keeps_itself_through_a_save()
    {
        DaggerfallTuning tuning = Road(new(0, 0));
        using DaggerfallSession session = FreshSession(tuning: tuning);
        (Agreement deal, string giver) = StrikeGrainDeal(session);
        DaggerfallPoliticalBoard board = session.PoliticalBoard!;

        NextSunday(session);
        DaggerfallRationConvoySave convoy = Assert.Single(session.RationConvoys.All);
        Assert.Equal((deal.Id, giver, deal.Other(giver)), (convoy.Agreement, convoy.Giver, convoy.Receiver));
        Region bound = Trade.RationDestination(board.Board, board.Board.Agreements.Single(held => held.Id == deal.Id), giver)!;
        Assert.Equal(board.Town(bound.Id)!.Id, convoy.Destination.Require());
        Assert.Equal(board.Town(deal.GivenBy(giver).Source!)!.Id, convoy.Origin.Require());
        // It goes by the towns of the regions the board lets the grain pass through, one leg at a time.
        DaggerfallSiteId[] route = [.. Trade.RationRoute(board.Board, board.Board.Agreements.Single(held => held.Id == deal.Id), giver)
            .Select(region => board.Town(region.Id)!.Id).Distinct()];
        Assert.Equal(route.Skip(1), convoy.Stops.Select(stop => stop.Require()));
        Assert.Equal(0, convoy.Leg);

        DaggerfallPersistentOverlandBand band = Assert.Single(session.Expedition.PersistentBands, band => band.BandId == convoy.BandId);
        Assert.Equal(DaggerfallBandKind.Convoy, band.Kind);
        DaggerfallArms arms = session.RealmArms.OfSeat(giver)!;
        Assert.Equal(DaggerfallHeraldry.Hex(TestPayload.Definitions.Heraldry.Colour(arms.Livery)), band.Banner);
        Assert.Equal(convoy.Riders.Order(), session.State.ActorLedger.EntriesOnOverlandBand(band.BandId).Select(rider => rider.DurableId).Order());

        // A save keeps the convoy, its band and its people aboard it.
        using DaggerfallSession restored = Restore(session.CaptureSave(), tuning);
        Assert.Equal(convoy.BandId, Assert.Single(restored.RationConvoys.All).BandId);
        Assert.Equal(band.X, Assert.Single(restored.Expedition.PersistentBands, kept => kept.BandId == convoy.BandId).X, 6);
        Assert.Equal(convoy.Riders.Length, restored.State.ActorLedger.EntriesOnOverlandBand(convoy.BandId).Count);
    }

    [Fact]
    public void A_convoy_that_arrives_clears_its_terms_losses_and_its_people_go_home()
    {
        using DaggerfallSession session = FreshSession(tuning: Road(new(0, 0)));
        (Agreement deal, string giver) = StrikeGrainDeal(session);
        DaggerfallPoliticalBoard board = session.PoliticalBoard!;
        board.Board.SetRationLosses(board.Board.Agreements.Single(held => held.Id == deal.Id), giver, 1);

        NextSunday(session);
        DaggerfallRationConvoySave convoy = Assert.Single(session.RationConvoys.All);
        // A ration's road runs region town to region town, a month or more at the wagons' pace across the Bay.
        for (int day = 0; day < 80 && session.RationConvoys.TryGet(convoy.BandId, out _); day++) session.AdvanceElapsedTime(86400);
        Assert.False(session.RationConvoys.TryGet(convoy.BandId, out _));
        Assert.Empty(session.State.ActorLedger.EntriesOnOverlandBand(convoy.BandId));

        NextSunday(session);
        Assert.Equal(0, board.Board.Agreements.Single(held => held.Id == deal.Id).LostBy(giver));
        Assert.Contains(board.LatestTrace!.Nudges, nudge => nudge.Nudge is RationArrived arrived && arrived.Agreement == deal.Id);
    }

    [Fact]
    public void A_ration_relieves_nothing_while_its_convoy_is_on_the_road_and_relieves_the_sunday_after_it_arrives()
    {
        using DaggerfallSession session = FreshSession(tuning: Road(new(0, 0)));
        (Agreement deal, string giver) = StrikeGrainDeal(session);
        DaggerfallPoliticalBoard board = session.PoliticalBoard!;
        NextSunday(session);
        DaggerfallRationConvoySave convoy = Assert.Single(session.RationConvoys.All);

        // Held on the road past the next Sunday, it has not arrived: the deal holds no ration to relieve with.
        DaggerfallPersistentOverlandBand band = Assert.Single(session.Expedition.PersistentBands, band => band.BandId == convoy.BandId);
        band.HeldUntilSecond = (board.NextTurnDay + 1) * 86400L;
        NextSunday(session);
        Assert.True(session.RationConvoys.TryGet(convoy.BandId, out _));
        Assert.DoesNotContain(board.LatestTrace!.Nudges, nudge => nudge.Nudge is RationArrived);
        Assert.DoesNotContain(board.LatestTrace.Upkeep, change => change.Field == $"ration:{deal.Id}");

        // Let go, it arrives; the Sunday after, its ration is spent relieving the region it was bound for.
        // A ration's road runs region town to region town, a month or more at the wagons' pace across the Bay.
        for (int day = 0; day < 80 && session.RationConvoys.TryGet(convoy.BandId, out _); day++) session.AdvanceElapsedTime(86400);
        Assert.False(session.RationConvoys.TryGet(convoy.BandId, out _));
        NextSunday(session);
        Assert.Contains(board.LatestTrace!.Nudges, nudge => nudge.Nudge is RationArrived arrived && arrived.Agreement == deal.Id);
        Assert.Contains(board.LatestTrace.Upkeep, change => change.Field == $"ration:{deal.Id}" && change.From == "arrived" && change.To == "awaited");
    }

    [Fact]
    public void A_convoy_met_and_left_with_a_rider_dead_is_lost_and_the_towns_hear_of_it()
    {
        using DaggerfallSession session = FreshSession(tuning: Road(new(0, 0)));
        (Agreement deal, string giver) = StrikeGrainDeal(session);
        DaggerfallPoliticalBoard board = session.PoliticalBoard!;
        NextSunday(session);
        DaggerfallRationConvoySave convoy = Assert.Single(session.RationConvoys.All);

        // Met on the map, its band left it and one of its guards fell.
        Assert.True(session.Expedition.RemovePersistentBand(convoy.BandId));
        long guard = convoy.Riders[1];
        Assert.True(session.State.ActorLedger.TryGet(guard, out ActorLedgerEntry? entry));
        session.State.ActorLedger.Upsert(entry! with { Location = ActorLocationOverride.Dead(DaggerfallWorldProfileIds.Exterior(convoy.Origin.Require())) });

        NextSunday(session);
        Assert.False(session.RationConvoys.TryGet(convoy.BandId, out _));
        Assert.Contains(board.LatestTrace!.Nudges, nudge => nudge.Nudge is RationLost lost && lost.Agreement == deal.Id && lost.Giver == giver);
        Assert.Contains(session.WordSpread.Wavefronts, news => news.EventId == $"convoy:{convoy.BandId}:lost");
    }

    [Fact]
    public void Convoys_lost_unseen_twice_running_end_the_deal_as_route_raided_and_the_towns_hear_of_it()
    {
        // The road takes every band at the first segment it sets out along, wherever the convoys go.
        using DaggerfallSession session = FreshSession(tuning: Road(new(100, 0)));
        (Agreement deal, string giver) = StrikeGrainDeal(session);
        DaggerfallPoliticalBoard board = session.PoliticalBoard!;

        // Sent on the Sunday, the convoy is taken within the hour after.
        NextSunday(session);
        string first = $"{DaggerfallRationConvoys.BandPrefix}{deal.Id}:{giver}:{board.Board.Turn}";
        Assert.False(session.RationConvoys.TryGet(first, out _));
        Assert.DoesNotContain(session.Expedition.PersistentBands, band => band.BandId == first);
        Assert.Empty(session.State.ActorLedger.EntriesOnOverlandBand(first));
        Assert.Contains(session.WordSpread.Wavefronts, news => news.EventId == $"convoy:{first}:lost");

        // The first loss is a missed ration; the second convoy is lost too, and the deal ends.
        NextSunday(session);
        Assert.Equal(1, board.Board.Agreements.Single(held => held.Id == deal.Id).LostBy(giver));
        NextSunday(session);
        Assert.DoesNotContain(board.Board.Agreements, held => held.Id == deal.Id);
        Assert.Contains(session.WordSpread.Wavefronts, news => news.EventId == $"politics:{board.Board.Turn}:route-raided:{deal.Id}");
        _ = DaggerfallSavePayload.Read(session.CaptureSave());
    }

    [Fact]
    public void Raiders_of_a_seat_and_a_war_against_the_giver_name_the_seat_whose_soldiers_took_the_convoy()
    {
        using DaggerfallSession session = FreshSession();
        DaggerfallBoardSeatBinding[] seats = [.. TestPayload.Definitions.BoardSeats.All.Take(3)];
        (string giver, string enemy, string other) = (seats[0].SeatId, seats[1].SeatId, seats[2].SeatId);
        DaggerfallWorldConditionToken Token(DaggerfallWorldConditionKind kind, string? tag, int? source, int? target) =>
            new("test", kind, Region: 17, SourceFactionId: source, TargetFactionId: target, SubjectTag: tag);

        DaggerfallRationConvoys convoys = session.RationConvoys;
        Assert.Equal(enemy, convoys.RaiderOf(Token(DaggerfallWorldConditionKind.GrainShortage, DaggerfallRoadTuning.RaidersTag, seats[1].OfficeFactionId, null), giver));
        Assert.Equal(enemy, convoys.RaiderOf(Token(DaggerfallWorldConditionKind.BorderTension, null, seats[0].OfficeFactionId, seats[1].OfficeFactionId), giver));
        Assert.Equal(enemy, convoys.RaiderOf(Token(DaggerfallWorldConditionKind.BorderTension, null, seats[1].OfficeFactionId, seats[0].OfficeFactionId), giver));
        // A war between others, the giver's own raiders, monsters and the road name no one.
        Assert.Null(convoys.RaiderOf(Token(DaggerfallWorldConditionKind.BorderTension, null, seats[1].OfficeFactionId, seats[2].OfficeFactionId), giver));
        Assert.Null(convoys.RaiderOf(Token(DaggerfallWorldConditionKind.GrainShortage, DaggerfallRoadTuning.RaidersTag, seats[0].OfficeFactionId, null), giver));
        Assert.Null(convoys.RaiderOf(Token(DaggerfallWorldConditionKind.MonsterSurge, null, seats[1].OfficeFactionId, null), giver));
        Assert.Null(convoys.RaiderOf(null, other));
    }

    /// <summary>
    /// Puts a coin-for-grain deal in force between the first two seats where one has grain to spare that reaches the
    /// other, as if it had been agreed: the receiver gives coin, the giver grain.
    /// </summary>
    private static (Agreement Deal, string Giver) StrikeGrainDeal(DaggerfallSession session)
    {
        Board board = session.PoliticalBoard!.Board;
        foreach (Seat giver in board.Seats)
            foreach (Seat receiver in board.Seats.Where(seat => seat != giver))
                if (Trade.Resolve(board, TradeTerm.Parse("grain")!, giver, receiver) is { } grain
                    && Trade.Fits(board, receiver, giver, TradeTerm.Parse("coin:1/4")!, grain))
                {
                    board.AddAgreement(Trade.Offer(board, receiver, giver, TradeTerm.Parse("coin:1/4")!, grain));
                    return (board.Agreements[^1], giver.Id);
                }
        throw new InvalidOperationException("No two seats on the canon board can trade grain.");
    }

    private static void NextSunday(DaggerfallSession session) =>
        session.AdvanceElapsedTime((session.PoliticalBoard!.NextTurnDay * 86400) + 3600 - session.Calendar.ToAbsoluteSeconds());

    private static DaggerfallTuning Road(DaggerfallRoadChances wilds)
    {
        DaggerfallRoadChances none = new(0, 0);
        return DaggerfallTuning.Defaults with
        {
            Expedition = DaggerfallTuning.Defaults.Expedition with
            {
                Road = DaggerfallTuning.Defaults.Expedition.Road with
                {
                    Wilds = wilds, HostileBand = none,
                    Hazards = Enum.GetValues<DaggerfallRoadHazard>().ToDictionary(hazard => hazard, _ => none),
                },
            },
        };
    }

    private static DaggerfallSession Restore(RulesetSavePayload saved, DaggerfallTuning tuning)
    {
        DaggerfallSiteProfile inputs = ReadInputs(TestData.RepositoryRoot);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.Geometry.Spatial[0].Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), PerceptionFake.Create().Service);
        return DaggerfallSession.Restore(engine.Context, new(TestPayload.Definitions, inputs, tuning), saved);
    }
}
