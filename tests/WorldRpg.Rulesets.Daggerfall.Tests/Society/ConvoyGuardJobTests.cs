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
/// #11182: a giver's factor hires the player to guard its ration convoy while it musters; the party rides with it along
/// its route, an ambush on the road drops them into the fight beside its guards, and the job pays on arrival with a bonus
/// for an ambush driven off. Leaving it at a stop or losing it pays nothing; raiding it is desertion, which bars the
/// giver's court's work for a while.
/// </summary>
public sealed class ConvoyGuardJobTests
{
    private static readonly DaggerfallPoliticsTuning Politics = DaggerfallTuning.Defaults.Politics;

    [Fact]
    public void A_factor_hires_the_player_while_the_convoy_musters_and_the_party_rides_with_it_kept_through_a_save()
    {
        DaggerfallTuning tuning = QuietRoad();
        using DaggerfallSession session = FreshSession(tuning: tuning);
        (DaggerfallRationConvoySave convoy, DaggerfallSiteRecord origin, DaggerfallNpc factor) = MusteringConvoy(session);

        // Only the giver's court offers the job, and only while the convoy musters there.
        DaggerfallConvoyGuardOffer offer = Assert.Single(session.ConvoyGuardOffers(factor, origin));
        Assert.Equal(convoy.BandId, offer.BandId);
        Assert.Empty(session.ConvoyGuardOffers(factor with { Appearance = factor.Appearance with { FactionId = 0 } }, origin));
        // A crown's court keeps its castle apart from its town: anywhere in the convoy's region it offers the job, beyond it not.
        DaggerfallSiteRecord castle = TestPayload.Definitions.Locations.Records.First(site => site.Id.Region == origin.Id.Region && site.Id != origin.Id);
        DaggerfallSiteRecord abroad = TestPayload.Definitions.Locations.Records.First(site => site.Id.Region != origin.Id.Region);
        Assert.Single(session.ConvoyGuardOffers(factor, castle));
        Assert.Empty(session.ConvoyGuardOffers(factor, abroad));
        Assert.True(session.TakeConvoyGuard(factor, convoy.BandId, origin).Accepted);
        Assert.Equal(convoy.BandId, session.ConvoyGuard.Hire!.BandId);
        Assert.Empty(session.ConvoyGuardOffers(factor, origin));

        // Opened in its town, the map halts once at the muster, then carries the party along with the convoy.
        session.Expedition.Begin(new(origin.MapPixelX, origin.MapPixelY), aboardOwnShip: false);
        session.Expedition.SetMode(DaggerfallExpeditionMode.Halt);
        DaggerfallPersistentOverlandBand band = Ride(session, convoy.BandId, until: band => band.RouteStep >= 3);
        Assert.Equal(convoy.BandId, session.Expedition.RidingWith);
        Assert.Equal(new DaggerfallTravelMapPixel((int)Math.Floor(band.X), (int)Math.Floor(band.Y)), session.Expedition.Party);
        Assert.False(session.Expedition.RidingAtStop);
        // Riding, the party keeps the convoy's way and no course of its own.
        session.Expedition.SetDestination(new(origin.MapPixelX, origin.MapPixelY), origin.Id);
        Assert.Contains("Leave it at a stop", session.Expedition.Message);

        using DaggerfallSession restored = Restore(session.CaptureSave(), tuning);
        Assert.Equal(session.ConvoyGuard.Hire, restored.ConvoyGuard.Hire);
        Assert.Single(restored.Expedition.PersistentBands, kept => kept.BandId == convoy.BandId);
    }

    [Fact]
    public void The_job_pays_with_the_escorts_standing_only_when_the_party_rides_in_with_the_convoy()
    {
        using DaggerfallSession session = FreshSession(tuning: QuietRoad());
        (DaggerfallRationConvoySave convoy, DaggerfallSiteRecord origin, DaggerfallNpc factor) = MusteringConvoy(session);
        Assert.True(session.TakeConvoyGuard(factor, convoy.BandId, origin).Accepted);
        session.Expedition.Begin(new(origin.MapPixelX, origin.MapPixelY), aboardOwnShip: false);
        _ = Ride(session, convoy.BandId, until: band => band.RouteStep >= 1);
        ulong gold = session.State.Currency.Read().Gold;
        DaggerfallBoardSeatBinding giver = Seat(convoy.Giver), receiver = Seat(convoy.Receiver);
        int giverBefore = session.State.Social.FactionReputation(giver.OfficeFactionId);
        int receiverBefore = session.State.Social.FactionReputation(receiver.OfficeFactionId);

        Assert.True(session.RationConvoys.Arrived(convoy.BandId));

        Assert.Equal(gold + (ulong)Politics.ConvoyGuardPay, session.State.Currency.Read().Gold);
        Assert.Null(session.ConvoyGuard.Hire);
        Assert.True(session.State.Social.FactionReputation(giver.OfficeFactionId) >= giverBefore + Politics.ConvoyDefenceStanding - 1);
        Assert.True(session.State.Social.FactionReputation(receiver.OfficeFactionId) >= receiverBefore + Politics.ConvoyDefenceStanding - 1);
    }

    [Fact]
    public void Ridden_to_its_destination_the_party_stands_there_the_map_halts_and_the_job_pays_once()
    {
        using DaggerfallSession session = FreshSession(tuning: QuietRoad());
        (DaggerfallRationConvoySave convoy, DaggerfallSiteRecord origin, DaggerfallNpc factor) = MusteringConvoy(session);
        Assert.True(session.TakeConvoyGuard(factor, convoy.BandId, origin).Accepted);
        DaggerfallSiteRecord destination = TestPayload.Definitions.Locations.Records.Single(site => site.Id == convoy.Destination.Require());
        ulong gold = session.State.Currency.Read().Gold;
        session.Expedition.Begin(new(origin.MapPixelX, origin.MapPixelY), aboardOwnShip: false);
        session.Expedition.SetMode(DaggerfallExpeditionMode.Halt);

        // Going on after each halt (the muster, each stop), the party rides the whole road with the convoy.
        for (int step = 0; step < 4000 && session.ConvoyGuard.Hire is not null; step++)
        {
            if (!session.Expedition.Running) session.Expedition.SetSpeed(session.Expedition.Tuning.Speeds);
            _ = session.Expedition.Advance(0.25);
        }

        Assert.Null(session.ConvoyGuard.Hire);
        Assert.Equal(gold + (ulong)Politics.ConvoyGuardPay, session.State.Currency.Read().Gold);
        Assert.Equal(new DaggerfallTravelMapPixel(destination.MapPixelX, destination.MapPixelY), session.Expedition.Party);
        Assert.False(session.Expedition.Running);
        Assert.Null(session.Expedition.RidingWith);
        Assert.Contains("reaches", session.Expedition.Message);
        // Taken up again, the map pays nothing more.
        session.Expedition.SetSpeed(session.Expedition.Tuning.Speeds);
        _ = session.Expedition.Advance(0.25);
        Assert.Equal(gold + (ulong)Politics.ConvoyGuardPay, session.State.Currency.Read().Gold);
    }

    [Fact]
    public void Riding_with_the_convoy_the_party_meets_no_band_of_the_map_however_near()
    {
        using DaggerfallSession session = FreshSession(tuning: QuietRoad());
        (DaggerfallRationConvoySave convoy, DaggerfallSiteRecord origin, DaggerfallNpc factor) = MusteringConvoy(session);
        Assert.True(session.TakeConvoyGuard(factor, convoy.BandId, origin).Accepted);
        session.Expedition.Begin(new(origin.MapPixelX, origin.MapPixelY), aboardOwnShip: false);
        DaggerfallPersistentOverlandBand ridden = Ride(session, convoy.BandId, until: band => band.RouteStep >= 2);

        // A hostile band on the party's pixel, which would catch a party travelling alone.
        (double x, double y) = session.Expedition.Position;
        session.Expedition.RegisterPersistentBand(new DaggerfallPersistentBandSave("test:raiders", "Raiders", DaggerfallBandKind.Bandits,
            x, y, 0, 0, Hostile: true, Spotted: true));
        int before = ridden.RouteStep;
        _ = Ride(session, convoy.BandId, until: band => band.RouteStep >= before + 2);

        Assert.False(session.Expedition.EncounterWaiting);
        Assert.Equal(convoy.BandId, session.Expedition.RidingWith);
        Assert.Contains(session.Expedition.PersistentBands, band => band.BandId == "test:raiders");
    }

    [Fact]
    public void Leaving_at_a_stop_letting_it_go_on_alone_or_losing_it_pays_nothing()
    {
        // Left at a stop: the muster in its town is its first.
        using (DaggerfallSession session = FreshSession(tuning: QuietRoad()))
        {
            (DaggerfallRationConvoySave convoy, DaggerfallSiteRecord origin, DaggerfallNpc factor) = MusteringConvoy(session);
            Assert.True(session.TakeConvoyGuard(factor, convoy.BandId, origin).Accepted);
            session.Expedition.Begin(new(origin.MapPixelX, origin.MapPixelY), aboardOwnShip: false);
            _ = Ride(session, convoy.BandId, until: _ => session.Expedition.RidingWith is not null);
            Assert.True(session.Expedition.RidingAtStop);
            Assert.True(session.ConvoyGuard.Quit());
            session.Expedition.StopRiding();
            ulong gold = session.State.Currency.Read().Gold;
            Assert.True(session.RationConvoys.Arrived(convoy.BandId));
            Assert.Equal(gold, session.State.Currency.Read().Gold);
        }

        // Gone on without the party: it arrives, and its guard is paid nothing.
        using (DaggerfallSession session = FreshSession(tuning: QuietRoad()))
        {
            (DaggerfallRationConvoySave convoy, DaggerfallSiteRecord origin, DaggerfallNpc factor) = MusteringConvoy(session);
            Assert.True(session.TakeConvoyGuard(factor, convoy.BandId, origin).Accepted);
            ulong gold = session.State.Currency.Read().Gold;
            Assert.True(session.RationConvoys.Arrived(convoy.BandId));
            Assert.Equal(gold, session.State.Currency.Read().Gold);
            Assert.Null(session.ConvoyGuard.Hire);
        }

        // Lost: no pay, and the job ends.
        using (DaggerfallSession session = FreshSession(tuning: QuietRoad()))
        {
            (DaggerfallRationConvoySave convoy, DaggerfallSiteRecord origin, DaggerfallNpc factor) = MusteringConvoy(session);
            Assert.True(session.TakeConvoyGuard(factor, convoy.BandId, origin).Accepted);
            ulong gold = session.State.Currency.Read().Gold;
            Assert.True(session.RationConvoys.OnBandLost(convoy.BandId, raidedUnder: null, second: 0));
            Assert.Equal(gold, session.State.Currency.Read().Gold);
            Assert.Null(session.ConvoyGuard.Hire);
        }
    }

    [Fact]
    public void Raiding_the_convoy_you_guard_is_desertion_that_costs_its_giver_more_and_bars_its_courts_work_through_a_save()
    {
        DaggerfallTuning tuning = QuietRoad();
        using DaggerfallSession session = FreshSession(tuning: tuning);
        (DaggerfallRationConvoySave convoy, DaggerfallSiteRecord origin, DaggerfallNpc factor) = MusteringConvoy(session);
        Assert.True(session.TakeConvoyGuard(factor, convoy.BandId, origin).Accepted);
        DaggerfallBoardSeatBinding giver = Seat(convoy.Giver), receiver = Seat(convoy.Receiver);
        int giverBefore = session.State.Social.FactionReputation(giver.OfficeFactionId);
        int receiverBefore = session.State.Social.FactionReputation(receiver.OfficeFactionId);

        Assert.True(session.RationConvoys.Choose(convoy.BandId, DaggerfallConvoySide.Raid));

        // The raid costs both seats; desertion costs the giver more.
        int giverLoss = giverBefore - session.State.Social.FactionReputation(giver.OfficeFactionId);
        int receiverLoss = receiverBefore - session.State.Social.FactionReputation(receiver.OfficeFactionId);
        Assert.True(giverLoss >= Politics.ConvoyRaidStanding + Politics.ConvoyDesertionStanding - 2, $"giver lost {giverLoss}");
        Assert.True(receiverLoss < Politics.ConvoyRaidStanding + Politics.ConvoyDesertionStanding - 2, $"receiver lost {receiverLoss}");
        Assert.Null(session.ConvoyGuard.Hire);
        Assert.True(session.ConvoyGuard.Refuses(convoy.Giver));
        Assert.Single(session.State.Crime.Incidents, incident => incident.OperationId == $"convoy-raid:{convoy.BandId}");

        // The court still lists the work, and refuses it; the bar is kept through a save and lifts after its days.
        using DaggerfallSession restored = Restore(session.CaptureSave(), tuning);
        Assert.True(restored.ConvoyGuard.Refuses(convoy.Giver));
        DaggerfallConvoyGuardReply refused = restored.TakeConvoyGuard(factor, convoy.BandId, origin);
        Assert.False(refused.Accepted);
        Assert.Contains("no work here for you", refused.Message);
        restored.AdvanceElapsedTime((Politics.ConvoyDesertionBarDays + 1) * 86400L);
        Assert.False(restored.ConvoyGuard.Refuses(convoy.Giver));
    }

    [Fact]
    public void An_ambush_on_the_road_halts_the_map_at_a_fight_beside_the_party_and_the_convoy_rides_on_after_it_is_driven_off()
    {
        // The test Engine's keyed draws come up 1, so the wilds raid the first stretch of road the convoy sets out on.
        using var fixture = SourceBackedGuildBankSessionFixture.Create(Road(new(1, 0)));
        using DaggerfallSession session = fixture.Start(fixture.ExteriorProfile);
        (DaggerfallRationConvoySave convoy, DaggerfallSiteRecord origin, DaggerfallNpc factor) = MusteringConvoy(session);
        Assert.True(session.TakeConvoyGuard(factor, convoy.BandId, origin).Accepted);
        session.Expedition.Begin(new(origin.MapPixelX, origin.MapPixelY), aboardOwnShip: false);
        DaggerfallPersistentOverlandBand band = Ride(session, convoy.BandId, until: band => band.ActiveClash is not null);

        // Not lost: the raid falls on it beside the party, who comes upon the fight and may defend it or join the raiders.
        Assert.False(band.OnRoad);
        Assert.Equal(convoy.BandId, session.Expedition.BandAhead!.PersistentBandId);
        Assert.True(session.Expedition.BandAhead.IsClash);
        Assert.Contains("fall upon", session.Expedition.Message);
        Assert.Equal([DaggerfallConvoySide.Defend, DaggerfallConvoySide.JoinRaiders], session.RationConvoys.Sides(convoy.BandId, underAttack: true));
        // The fight stands at the party, whose ride is over: entering meets it.
        (double x, double y) = session.Expedition.Position;
        Assert.Equal(0d, band.Distance(x, y), 6);
        Assert.Null(session.Expedition.RidingWith);
        Assert.NotNull(session.Expedition.Entry());
        Assert.DoesNotContain(session.Expedition.PersistentBands, kept => kept.BandId == convoy.BandId);

        // Met and fought: the guard, entering without a word, stands with the convoy, and strikes the raiders.
        const long Raider = 9_000_001;
        session.RationConvoys.Staged(convoy.BandId, convoy.Riders, [Raider]);
        Assert.Equal(DaggerfallConvoySide.Defend, Assert.Single(session.RationConvoys.All).Side);
        session.RationConvoys.Struck(Raider);

        // Taking the map again, the convoy forms up beside the party and rides on, the ambush driven off.
        session.OpenExpedition();
        DaggerfallPersistentOverlandBand resumed = Assert.Single(session.Expedition.PersistentBands, kept => kept.BandId == convoy.BandId);
        Assert.Equal(session.Expedition.Party, new DaggerfallTravelMapPixel((int)Math.Floor(resumed.X), (int)Math.Floor(resumed.Y)));
        Assert.True(session.ConvoyGuard.Hire!.DroveOff);
        Assert.Equal(Politics.ConvoyGuardPay + Politics.ConvoyGuardBonus, session.ConvoyGuard.Arrived(convoy.BandId, rodeIn: true));
    }

    [Fact]
    public void A_guarded_convoy_that_lost_a_rider_in_its_ambush_is_lost_to_its_guard()
    {
        using var fixture = SourceBackedGuildBankSessionFixture.Create(QuietRoad());
        using DaggerfallSession session = fixture.Start(fixture.ExteriorProfile);
        (DaggerfallRationConvoySave convoy, DaggerfallSiteRecord origin, DaggerfallNpc factor) = MusteringConvoy(session);
        Assert.True(session.TakeConvoyGuard(factor, convoy.BandId, origin).Accepted);
        Assert.True(session.Expedition.RemovePersistentBand(convoy.BandId));
        long guard = convoy.Riders[1];
        Assert.True(session.State.ActorLedger.TryGet(guard, out ActorLedgerEntry? entry));
        session.State.ActorLedger.Upsert(entry! with { Location = ActorLocationOverride.Dead(DaggerfallWorldProfileIds.Exterior(convoy.Origin.Require())) });

        session.OpenExpedition();

        Assert.True(session.Expedition.Held is null, session.Expedition.Held);
        Assert.Null(session.ConvoyGuard.Hire);
        Assert.DoesNotContain(session.Expedition.PersistentBands, kept => kept.BandId == convoy.BandId);
    }

    /// <summary>Strikes a grain deal, plays the Sunday that sends its convoy, and finds the court factor of the town it musters in.</summary>
    private static (DaggerfallRationConvoySave Convoy, DaggerfallSiteRecord Origin, DaggerfallNpc Factor) MusteringConvoy(DaggerfallSession session)
    {
        Board board = session.PoliticalBoard!.Board;
        bool struck = false;
        foreach (Seat giver in board.Seats)
        {
            foreach (Seat receiver in board.Seats.Where(seat => seat != giver))
                if (Trade.Resolve(board, TradeTerm.Parse("grain")!, giver, receiver) is { } grain
                    && Trade.Fits(board, receiver, giver, TradeTerm.Parse("coin:1/4")!, grain))
                {
                    board.AddAgreement(Trade.Offer(board, receiver, giver, TradeTerm.Parse("coin:1/4")!, grain));
                    struck = true;
                    break;
                }
            if (struck) break;
        }
        Assert.True(struck, "No two seats on the canon board can trade grain.");
        session.AdvanceElapsedTime((session.PoliticalBoard.NextTurnDay * 86400) + 3600 - session.Calendar.ToAbsoluteSeconds());
        DaggerfallRationConvoySave convoy = Assert.Single(session.RationConvoys.All);
        DaggerfallPersistentOverlandBand band = Assert.Single(session.Expedition.PersistentBands, band => band.BandId == convoy.BandId);
        Assert.Equal((0, 0), (band.RouteStep, band.RoadSegment));
        DaggerfallSiteRecord origin = TestPayload.Definitions.Locations.Records.Single(site => site.Id == convoy.Origin.Require());
        // The giver's court: someone of court standing of its seat (a capital's court keeps no factor slot in its town).
        int court = TestPayload.Definitions.BoardSeats.TryGetBySeatId(convoy.Giver, out DaggerfallBoardSeatBinding? seat) ? seat!.OfficeFactionId : 0;
        DaggerfallNpc factor = new(9_100_001, DaggerfallNpcKind.Static, "test/court/9100001",
            new DaggerfallNpcSite(origin.Id.Region, origin.Name, "0/0/0", DaggerfallWorldProfileIds.Exterior(origin.Id).LogicalId),
            new DaggerfallNpcAppearance("breton", "Male", 182, 0, 1, court), "Chamberlain", ["talk"], DaggerfallNpcPresence.Active, null, null, null);
        return (convoy, origin, factor);
    }

    /// <summary>Runs the travel map at its fastest, going on after each halt, until <paramref name="until"/> holds for the band; returns it.</summary>
    private static DaggerfallPersistentOverlandBand Ride(DaggerfallSession session, string bandId, Func<DaggerfallPersistentOverlandBand, bool> until)
    {
        for (int step = 0; step < 400; step++)
        {
            DaggerfallPersistentOverlandBand band = Assert.Single(session.Expedition.PersistentBands, band => band.BandId == bandId);
            if (until(band)) return band;
            Assert.False(session.Expedition.EncounterWaiting, session.Expedition.Message);
            if (!session.Expedition.Running) session.Expedition.SetSpeed(session.Expedition.Tuning.Speeds);
            _ = session.Expedition.Advance(0.25);
        }
        DaggerfallPersistentOverlandBand last = Assert.Single(session.Expedition.PersistentBands, band => band.BandId == bandId);
        throw new InvalidOperationException($"Band '{bandId}' never came to the state the test waited for: running {session.Expedition.Running}, "
            + $"speed {session.Expedition.Speed}, held {session.Expedition.Held}, message {session.Expedition.Message}, riding {session.Expedition.RidingWith}, "
            + $"band step {last.RouteStep} segment {last.RoadSegment} onRoad {last.OnRoad} heldUntil {last.HeldUntilSecond} at ({last.X},{last.Y}), "
            + $"party {session.Expedition.Party}, now {session.Calendar.ToAbsoluteSeconds()}");
    }

    private static DaggerfallBoardSeatBinding Seat(string seat) =>
        TestPayload.Definitions.BoardSeats.TryGetBySeatId(seat, out DaggerfallBoardSeatBinding? binding) ? binding! : throw new InvalidOperationException(seat);

    private static DaggerfallTuning QuietRoad() => Road(new(0, 0));

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
