using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Debugging;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Crime;
using WorldRpg.Rulesets.Daggerfall.Guilds;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using WorldRpg.Rulesets.Daggerfall.Travel;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Political layer A7 (#11052): elapsed-time overland route-distance news &amp; standing spread (instant bubble vs.
/// monotonic piecewise-integrated courier spread affecting crimes, small/propagated standing, and guild ranks
/// symmetrically) and board-game courier dispatches (deliver at destination, buy, intercept, kill).
/// </summary>
public sealed class WordSpreadAndCouriersTests
{
    private const int DaggerfallRegion = 17;

    [Fact]
    public void Untouched_world_writes_zero_spread_or_dispatch_bytes_and_bubble_is_instant_while_distant_town_waits_for_couriers()
    {
        using var fixture = SourceBackedGuildBankSessionFixture.Create();
        using DaggerfallSession session = fixture.Start(fixture.ExteriorProfile);

        // 1. Untouched session writes empty WordSpread and CourierDispatches save collections.
        Assert.Equal(0, session.WordSpread.Count);
        Assert.Equal(0, session.CourierDispatches.Count);
        DaggerfallSavePayload emptyPayload = DaggerfallSavePayload.Read(session.CaptureSave());
        Assert.Empty(emptyPayload.WordSpread.Wavefronts);
        Assert.Empty(emptyPayload.CourierDispatches.Dispatches);
        Assert.Empty(emptyPayload.CourierDispatches.Resolutions);

        DaggerfallSiteRecord origin = session.Site.ActiveSite!;
        DaggerfallSiteRecord distant = FindDistantTownInRegion(origin, minDistancePixels: 18.0);

        // 2. Commit a reported regional crime at origin through the real Crime -> Law.Admit -> WordSpread pipeline.
        DaggerfallCrimeIncidentSave incident = new(
            OperationId: "a7-theft-1",
            Crime: DaggerfallCrimeKind.Murder,
            Stage: DaggerfallCrimeStage.Completed,
            PerpetratorActorId: DaggerfallActorIdentity.PlayerEntityId,
            AffectedActorOrOwnerId: 9001,
            Region: DaggerfallRegion,
            GameMinute: CurrentMinute(session),
            TargetKind: DaggerfallCrimeTargetKind.Civilian,
            Witnesses: new DaggerfallCrimeWitnessEvidence(DaggerfallCrimeWitnessQuery.CompletedWithWitnesses, [9002]),
            GuildCredit: DaggerfallCrimeGuildCredit.CivilianMurder,
            Reported: true);
        session.State.Crime.RecordIncident(incident);

        Assert.True(session.WordSpread.TryGet("wf:crime:a7-theft-1", out DaggerfallWordWavefrontSave? crimeWf));
        Assert.NotNull(crimeWf);
        long nowMinute = CurrentMinute(session);
        int nowDay = checked((int)(nowMinute / (DaggerfallCalendar.HoursPerDay * DaggerfallCalendar.MinutesPerHour)));

        // Within the instant bubble (origin site): arrival is instant; notice board & gate scrutiny see the wanted status immediately.
        DaggerfallWordArrivalEvaluation atOrigin = session.WordSpread.EvaluateAt(crimeWf!, origin, nowMinute, nowDay);
        Assert.Equal(DaggerfallWordArrivalStatus.InstantBubble, atOrigin.Status);
        Assert.True(atOrigin.HasArrived);
        DaggerfallTownNoticeBoardView originBoard = session.NoticeBoard.BuildForSite(origin, nowMinute);
        Assert.Contains(originBoard.Entries, e => e.Kind == DaggerfallNoticeEntryKind.Bounty);
        Assert.True(session.Scrutiny.EvaluateGateScrutiny(DaggerfallRegion, origin).WatchChallengeActive);

        // Outside the instant bubble (distant town in same region): still in transit; distant notice board & scrutiny do NOT know yet.
        DaggerfallWordArrivalEvaluation atDistantBefore = session.WordSpread.EvaluateAt(crimeWf!, distant, nowMinute, nowDay);
        Assert.Equal(DaggerfallWordArrivalStatus.InTransit, atDistantBefore.Status);
        Assert.False(atDistantBefore.HasArrived);
        Assert.True(atDistantBefore.RemainingMinutes > 0);
        DaggerfallTownNoticeBoardView distantBoardBefore = session.NoticeBoard.BuildForSite(distant, nowMinute);
        Assert.DoesNotContain(distantBoardBefore.Entries, e => e.Kind == DaggerfallNoticeEntryKind.Bounty);
        Assert.False(session.Scrutiny.EvaluateGateScrutiny(DaggerfallRegion, distant).WatchChallengeActive);

        // 3. Spending elapsed time (as if in a dungeon, zero off-screen stepping) advances courier arrival naturally!
        long neededSeconds = checked((atDistantBefore.RemainingMinutes + 5) * DaggerfallCalendar.SecondsPerMinute);
        session.AdvanceElapsedTime(neededSeconds);
        long afterMinute = CurrentMinute(session);
        int afterDay = checked((int)(afterMinute / (DaggerfallCalendar.HoursPerDay * DaggerfallCalendar.MinutesPerHour)));

        DaggerfallWordArrivalEvaluation atDistantAfter = session.WordSpread.EvaluateAt(crimeWf!, distant, afterMinute, afterDay);
        Assert.Equal(DaggerfallWordArrivalStatus.ArrivedByCourier, atDistantAfter.Status);
        Assert.True(atDistantAfter.HasArrived);
        DaggerfallTownNoticeBoardView distantBoardAfter = session.NoticeBoard.BuildForSite(distant, afterMinute);
        Assert.Contains(distantBoardAfter.Entries, e => e.Kind == DaggerfallNoticeEntryKind.Bounty);
        Assert.True(session.Scrutiny.EvaluateGateScrutiny(DaggerfallRegion, distant).WatchChallengeActive);
    }

    [Fact]
    public void Route_distance_weights_terrain_and_water_crossings_beyond_straight_line_pixels_for_word_spread_and_dispatches()
    {
        // Use a faster courier speed so a 25+ pixel cross-bay/mountain route completes within daytime (06:00..18:00)
        // on the live expedition map without random nighttime town encounters interrupting the test.
        DaggerfallTuning fastCourierTuning = DaggerfallTuning.Defaults with
        {
            Social = DaggerfallTuning.Defaults.Social with { CourierSpeedPixelsPerHour = 6.0 },
        };
        using TwoTownFixture fixture = TwoTownFixture.Create(fastCourierTuning);
        DaggerfallSession session = fixture.Session;

        // Start at 6:00 AM so daytime town expedition advancement runs cleanly without nighttime random town encounters.
        session.AdvanceElapsedTime(5L * DaggerfallCalendar.MinutesPerHour * DaggerfallCalendar.SecondsPerMinute);

        DaggerfallSiteRecord origin = fixture.OriginSite;
        // Find the shortest rough/sea route whose overland route distance strictly exceeds straight-line distance by >= 5 pixels.
        DaggerfallSiteRecord seaOrMountainTarget = TestPayload.Definitions.Locations.Records
            .Where(r => r.Exterior is not null && r.Id != origin.Id)
            .Select(r => (
                Site: r,
                Direct: DirectDistance(origin, r),
                Route: session.WordSpread.RouteDistancePixels(origin.MapPixelX, origin.MapPixelY, r.MapPixelX, r.MapPixelY)))
            .Where(x => x.Direct >= 20.0 && x.Route >= x.Direct + 5.0)
            .OrderBy(x => x.Route)
            .First()
            .Site;

        double direct = DirectDistance(origin, seaOrMountainTarget);
        double route = session.WordSpread.RouteDistancePixels(
            origin.MapPixelX, origin.MapPixelY, seaOrMountainTarget.MapPixelX, seaOrMountainTarget.MapPixelY);
        Assert.True(route >= direct + 5.0,
            $"Expected overland route distance ({route:F2}) to exceed straight-line distance ({direct:F2}) by at least 5 pixels.");

        long nowMinute = CurrentMinute(session);
        int nowDay = checked((int)(nowMinute / (DaggerfallCalendar.HoursPerDay * DaggerfallCalendar.MinutesPerHour)));
        DaggerfallWordWavefrontSave wf = session.WordSpread.RecordNews(
            "wf:route-check", "Bay Route Bulletin", "Overland courier route verification.", origin, nowMinute);

        DaggerfallWordArrivalEvaluation eval = session.WordSpread.EvaluateAt(wf, seaOrMountainTarget, nowMinute, nowDay);
        Assert.Equal(route, eval.DistancePixels, precision: 4);
        long straightLineMinutes = checked((long)Math.Ceiling(((direct - session.WordSpread.Tuning.InstantNewsBubblePixels) / session.WordSpread.Tuning.CourierSpeedPixelsPerHour) * 60d));
        Assert.True(eval.RemainingMinutes >= straightLineMinutes + 45,
            $"Expected route arrival ({eval.RemainingMinutes}m) to strictly exceed straight-line arrival ({straightLineMinutes}m).");

        // StageDispatch with live overland band enabled: both ExpectedArrivalMinute and the physical overland band's
        // speed reflect route distance, so the courier stays en route past straight-line arrival and settles cleanly on arrival!
        List<DaggerfallDispatchResolution> routeResolutions = [];
        session.CourierDispatches.BindBoardMoveCallback(routeResolutions.Add);
        DaggerfallCourierDispatchSave dispatch = session.CourierDispatches.StageDispatch(
            dispatchId: "disp:route-check",
            seatId: "crown:daggerfall",
            actionKind: "bay-envoy",
            summary: "Cross-bay sealed envoy",
            origin: origin,
            destination: seaOrMountainTarget,
            currentMinute: nowMinute,
            currentDay: nowDay,
            stageOverlandBand: true);
        long directDispatchMinutes = checked((long)Math.Ceiling((direct / session.WordSpread.Tuning.CourierSpeedPixelsPerHour) * 60d));
        long actualDispatchMinutes = dispatch.ExpectedArrivalMinute - dispatch.DispatchedAtMinute;
        Assert.True(actualDispatchMinutes >= directDispatchMinutes + 45,
            $"Expected dispatch route duration ({actualDispatchMinutes}m) to strictly exceed straight-line duration ({directDispatchMinutes}m).");
        Assert.NotNull(dispatch.OverlandBandId);
        DaggerfallPersistentOverlandBand liveBand = Assert.Single(
            session.Expedition.PersistentBands, b => b.BandId == dispatch.OverlandBandId);
        Assert.Equal(new DaggerfallTravelMapPixel(origin.MapPixelX, origin.MapPixelY), liveBand.RouteOrigin);
        DaggerfallOverlandRoute? bandRoute = liveBand.ResolveRoute(session.Expedition.Policy);
        Assert.NotNull(bandRoute);
        Assert.Equal(route, bandRoute!.TotalEquivalentPixels, precision: 4);

        // Advance actual expedition travel past straight-line arrival time: courier MUST still be en route & interceptable!
        session.ApplyProductMode(ProductMode.Playing);
        session.OpenExpedition();
        Assert.Null(session.Expedition.Message);
        session.Expedition.SetMode(DaggerfallExpeditionMode.Halt);
        session.Expedition.SetSpeed(1);
        long targetMidMinute = dispatch.DispatchedAtMinute + directDispatchMinutes + 30;
        Assert.True(targetMidMinute < dispatch.ExpectedArrivalMinute);
        for (int guard = 0; guard < 2000 && CurrentMinute(session) < targetMidMinute; guard++)
        {
            if (!session.Expedition.Running)
                session.Expedition.SetSpeed(1);
            if (session.Expedition.Advance(0.1) <= 0)
                break;
        }
        Assert.True(CurrentMinute(session) >= targetMidMinute && CurrentMinute(session) < dispatch.ExpectedArrivalMinute,
            $"Expected expedition to sit strictly between straight-line arrival ({targetMidMinute}) and route arrival ({dispatch.ExpectedArrivalMinute}), reached {CurrentMinute(session)} (running={session.Expedition.Running}, msg='{session.Expedition.Message}').");
        Assert.Contains(session.Expedition.PersistentBands, b => b.BandId == dispatch.OverlandBandId);
        Assert.True(session.CourierDispatches.TryGet(dispatch.DispatchId, out DaggerfallCourierDispatchSave? midDispatch));
        Assert.Equal(DaggerfallDispatchStatus.InTransit, midDispatch!.Status);
        Assert.Empty(routeResolutions);

        // Continue actual expedition travel through route-based arrival: band arrives/retires and dispatch resolves cleanly!
        long targetEndMinute = dispatch.ExpectedArrivalMinute + 15;
        for (int guard = 0; guard < 2000 && CurrentMinute(session) < targetEndMinute; guard++)
        {
            if (!session.Expedition.Running)
                session.Expedition.SetSpeed(1);
            if (session.Expedition.Advance(0.1) <= 0)
                break;
        }
        Assert.True(CurrentMinute(session) >= dispatch.ExpectedArrivalMinute);
        Assert.DoesNotContain(session.Expedition.PersistentBands, b => b.BandId == dispatch.OverlandBandId);
        Assert.True(session.CourierDispatches.TryGet(dispatch.DispatchId, out DaggerfallCourierDispatchSave? arrivedDispatch));
        Assert.Equal(DaggerfallDispatchStatus.Delivered, arrivedDispatch!.Status);
        DaggerfallDispatchResolution arrivedResolution = Assert.Single(routeResolutions);
        Assert.Equal(dispatch.DispatchId, arrivedResolution.DispatchId);
        Assert.Equal(DaggerfallDispatchStatus.Delivered, arrivedResolution.Outcome);

        // Save and reload after band arrival succeeds cleanly with zero orphaned OnOverlandBand references!
        session.Expedition.Close();
        RulesetSavePayload savedAfterArrival = session.CaptureSave();
        using DaggerfallSession restoredAfterArrival = fixture.Restore(savedAfterArrival);
        Assert.True(restoredAfterArrival.CourierDispatches.TryGet(dispatch.DispatchId, out DaggerfallCourierDispatchSave? reloadedDispatch));
        Assert.Equal(DaggerfallDispatchStatus.Delivered, reloadedDispatch!.Status);
    }

    [Fact]
    public void Condition_changes_never_retract_arrived_news_and_preserve_mid_journey_disruption_across_save_load()
    {
        using var fixture = SourceBackedGuildBankSessionFixture.Create();
        using DaggerfallSession session = fixture.Start(fixture.ExteriorProfile);

        DaggerfallSiteRecord origin = session.Site.ActiveSite!;
        DaggerfallSiteRecord distant = FindDistantTownInRegion(origin, minDistancePixels: 18.0);
        long startMinute = CurrentMinute(session);
        int startDay = checked((int)(startMinute / (DaggerfallCalendar.HoursPerDay * DaggerfallCalendar.MinutesPerHour)));

        // Part A: News/crime arrives in peacetime -> adding BorderTension afterward MUST NOT retract arrival!
        session.State.Crime.RecordIncident(new DaggerfallCrimeIncidentSave(
            OperationId: "a7-monotonic-crime",
            Crime: DaggerfallCrimeKind.Murder,
            Stage: DaggerfallCrimeStage.Completed,
            PerpetratorActorId: DaggerfallActorIdentity.PlayerEntityId,
            AffectedActorOrOwnerId: 9021,
            Region: DaggerfallRegion,
            GameMinute: startMinute,
            TargetKind: DaggerfallCrimeTargetKind.Civilian,
            Witnesses: new DaggerfallCrimeWitnessEvidence(DaggerfallCrimeWitnessQuery.CompletedWithWitnesses, [9022]),
            GuildCredit: DaggerfallCrimeGuildCredit.CivilianMurder,
            Reported: true));
        Assert.True(session.WordSpread.TryGet("wf:crime:a7-monotonic-crime", out DaggerfallWordWavefrontSave? crimeWf));
        DaggerfallWordArrivalEvaluation initialEval = session.WordSpread.EvaluateAt(crimeWf!, distant, startMinute, startDay);
        Assert.False(initialEval.HasArrived);
        long peacefulMinutes = initialEval.RemainingMinutes;

        session.AdvanceElapsedTime(checked((peacefulMinutes + 5) * DaggerfallCalendar.SecondsPerMinute));
        long arrivedMinute = CurrentMinute(session);
        int arrivedDay = checked((int)(arrivedMinute / (DaggerfallCalendar.HoursPerDay * DaggerfallCalendar.MinutesPerHour)));
        Assert.True(session.WordSpread.TryGet("wf:crime:a7-monotonic-crime", out crimeWf));
        Assert.True(session.WordSpread.EvaluateAt(crimeWf!, distant, arrivedMinute, arrivedDay).HasArrived);
        Assert.Contains(session.NoticeBoard.BuildForSite(distant, arrivedMinute).Entries, e => e.Kind == DaggerfallNoticeEntryKind.Bounty);

        // Now start a severe BorderTension war zone: already-arrived bounty MUST remain arrived!
        string warTokenId = DaggerfallWorldConditions.RegionTokenId(DaggerfallWorldConditionKind.BorderTension, DaggerfallRegion);
        session.WorldConditions.Set(new DaggerfallWorldConditionToken(
            warTokenId, DaggerfallWorldConditionKind.BorderTension, DaggerfallRegion, Severity: 2, StartedDay: arrivedDay));
        Assert.True(session.WordSpread.TryGet("wf:crime:a7-monotonic-crime", out crimeWf));
        Assert.Equal(DaggerfallWordArrivalStatus.ArrivedByCourier,
            session.WordSpread.EvaluateAt(crimeWf!, distant, arrivedMinute, arrivedDay).Status);
        Assert.Contains(session.NoticeBoard.BuildForSite(distant, arrivedMinute).Entries, e => e.Kind == DaggerfallNoticeEntryKind.Bounty);
        Assert.True(session.Scrutiny.EvaluateGateScrutiny(DaggerfallRegion, distant).WatchChallengeActive);

        // Part B: Mid-journey disruption -> clear condition -> save/load -> past disrupted hours MUST NOT be rewritten as peaceful!
        long disruptedStart = CurrentMinute(session);
        int disruptedDay = checked((int)(disruptedStart / (DaggerfallCalendar.HoursPerDay * DaggerfallCalendar.MinutesPerHour)));
        DaggerfallWordWavefrontSave midWf = session.WordSpread.RecordNews(
            "wf:mid-journey", "Mid-Journey Dispatch", "Courier left during border war.", origin, disruptedStart);
        Assert.False(session.WordSpread.EvaluateAt(midWf, distant, disruptedStart, disruptedDay).HasArrived);

        // Advance half the peaceful duration while BorderTension (0.35x speed) is active, then lift the condition.
        long halfPeaceful = peacefulMinutes / 2;
        session.AdvanceElapsedTime(checked(halfPeaceful * DaggerfallCalendar.SecondsPerMinute));
        Assert.True(session.WorldConditions.Remove(warTokenId));

        // Save and reload right after lifting the condition!
        RulesetSavePayload saved = session.CaptureSave();
        using DaggerfallSession restored = fixture.Restore(saved);
        Assert.True(restored.WordSpread.TryGet("wf:mid-journey", out DaggerfallWordWavefrontSave? restoredMidWf));
        Assert.NotNull(restoredMidWf!.LastCheckpointMinute);

        // Advance the OTHER half of peaceful duration (+10m): if the first half had been rewritten as peaceful, it would have arrived!
        // Because the first half ran at 0.35x speed, it MUST still be InTransit!
        restored.AdvanceElapsedTime(checked((peacefulMinutes - halfPeaceful + 10) * DaggerfallCalendar.SecondsPerMinute));
        long checkMinute = CurrentMinute(restored);
        int checkDay = checked((int)(checkMinute / (DaggerfallCalendar.HoursPerDay * DaggerfallCalendar.MinutesPerHour)));
        DaggerfallWordArrivalEvaluation midCheck = restored.WordSpread.EvaluateAt(restoredMidWf, distant, checkMinute, checkDay);
        Assert.Equal(DaggerfallWordArrivalStatus.InTransit, midCheck.Status);
        Assert.True(midCheck.RemainingMinutes > 30,
            $"Expected mid-journey courier slowed by earlier BorderTension to still need >30m, got {midCheck.RemainingMinutes}m.");

        // Advancing the remaining peaceful minutes finishes delivery cleanly.
        restored.AdvanceElapsedTime(checked((midCheck.RemainingMinutes + 5) * DaggerfallCalendar.SecondsPerMinute));
        long doneMinute = CurrentMinute(restored);
        int doneDay = checked((int)(doneMinute / (DaggerfallCalendar.HoursPerDay * DaggerfallCalendar.MinutesPerHour)));
        Assert.Equal(DaggerfallWordArrivalStatus.ArrivedByCourier,
            restored.WordSpread.EvaluateAt(restoredMidWf, distant, doneMinute, doneDay).Status);

        // Part C: Timed expiry mid-journey (spending days in a dungeon across ExpiresDay) integrates both segments!
        long timedStart = CurrentMinute(restored);
        int timedDay = checked((int)(timedStart / (DaggerfallCalendar.HoursPerDay * DaggerfallCalendar.MinutesPerHour)));
        restored.WorldConditions.Set(new DaggerfallWorldConditionToken(
            "cond:timed-war", DaggerfallWorldConditionKind.BorderTension, DaggerfallRegion,
            Severity: 2, StartedDay: timedDay, ExpiresDay: timedDay + 1));
        DaggerfallWordWavefrontSave timedWf = restored.WordSpread.RecordNews(
            "wf:timed-expiry", "Timed War Bulletin", "News emitted before truce.", origin, timedStart);
        // Jump across ExpiresDay (1 war day + enough peaceful time to finish the remaining distance):
        long timedJumpSeconds = checked((peacefulMinutes + DaggerfallCalendar.HoursPerDay * DaggerfallCalendar.MinutesPerHour + 60) * DaggerfallCalendar.SecondsPerMinute);
        restored.AdvanceElapsedTime(timedJumpSeconds);
        int timedEndDay = checked((int)(CurrentMinute(restored) / (DaggerfallCalendar.HoursPerDay * DaggerfallCalendar.MinutesPerHour)));
        Assert.False(restored.WorldConditions.HasActive(DaggerfallRegion, DaggerfallWorldConditionKind.BorderTension, timedEndDay));
        Assert.True(restored.WordSpread.TryGet("wf:timed-expiry", out DaggerfallWordWavefrontSave? afterTimed));
        Assert.True(restored.WordSpread.EvaluateAt(afterTimed!, distant, CurrentMinute(restored), timedEndDay).HasArrived);
    }

    [Fact]
    public void Ordinary_quest_standing_and_propagated_reputation_including_clamped_deltas_wait_for_couriers_outside_bubble()
    {
        using var fixture = SourceBackedGuildBankSessionFixture.Create();
        using DaggerfallSession session = fixture.Start(fixture.ExteriorProfile);

        DaggerfallSiteRecord origin = session.Site.ActiveSite!;
        DaggerfallSiteRecord distant = FindDistantTownInRegion(origin, minDistancePixels: 18.0);
        long nowMinute = CurrentMinute(session);
        int nowDay = checked((int)(nowMinute / (DaggerfallCalendar.HoursPerDay * DaggerfallCalendar.MinutesPerHour)));

        // Pick a non-regional faction with both authored allies and enemies, plus a Knightly Order (GuildGroup == 9).
        var allFactions = TestPayload.Definitions.Factions.Factions;
        DaggerfallFactionDefinition primary = allFactions.Values
            .OrderBy(f => f.Id)
            .First(f => f.Type != 15 && f.GuildGroup != 9
                && f.Allies.Any(id => id != f.Id && allFactions.ContainsKey(id))
                && f.Enemies.Any(id => id != f.Id && allFactions.ContainsKey(id)));
        int allyId = primary.Allies.First(id => id != primary.Id && allFactions.ContainsKey(id));
        int enemyId = primary.Enemies.First(id => id != primary.Id && allFactions.ContainsKey(id));
        int knightlyId = allFactions.Values.OrderBy(f => f.Id).First(f => f.GuildGroup == 9 && f.Id != 844).Id;

        int primaryBefore = session.State.Social.FactionReputation(primary.Id);
        int allyBefore = session.State.Social.FactionReputation(allyId);
        int enemyBefore = session.State.Social.FactionReputation(enemyId);

        // 1. Ordinary quest-sized propagated reward (+5) & penalty (-2), plus Knightly Order (+5)!
        session.State.Social.ChangeFactionReputation(primary.Id, +5, DaggerfallFactionReputationChange.Propagate);
        session.State.Social.ChangeFactionReputation(primary.Id, +5, DaggerfallFactionReputationChange.Propagate);
        session.State.Social.ChangeFactionReputation(primary.Id, -2, DaggerfallFactionReputationChange.Direct);
        int knightBefore = session.State.Social.FactionReputation(knightlyId);
        int genericKnightBefore = session.State.Social.FactionReputation(844);
        int knightKnownBefore = knightBefore - session.WordSpread.UnarrivedFactionStandingDelta(knightlyId, distant, nowMinute, nowDay);
        int genericKnightKnownBefore = genericKnightBefore - session.WordSpread.UnarrivedFactionStandingDelta(844, distant, nowMinute, nowDay);
        session.State.Social.ChangeFactionReputation(knightlyId, +5, DaggerfallFactionReputationChange.Propagate);

        // Raw standing changed locally (and at origin inside bubble, UnarrivedFactionStandingDelta == 0).
        Assert.NotEqual(primaryBefore, session.State.Social.FactionReputation(primary.Id));
        Assert.NotEqual(allyBefore, session.State.Social.FactionReputation(allyId));
        Assert.NotEqual(enemyBefore, session.State.Social.FactionReputation(enemyId));
        Assert.Equal(knightBefore + 5, session.State.Social.FactionReputation(knightlyId));
        Assert.Equal(genericKnightBefore + 5, session.State.Social.FactionReputation(844));
        Assert.Equal(0, session.WordSpread.UnarrivedFactionStandingDelta(primary.Id, origin, nowMinute, nowDay));

        // At distant town outside bubble: subtracting UnarrivedFactionStandingDelta reconstructs the EXACT prior standing
        // for the initiator, propagated ally, propagated enemy, knightly order, AND generic knightly order!
        Assert.Equal(primaryBefore,
            session.State.Social.FactionReputation(primary.Id) - session.WordSpread.UnarrivedFactionStandingDelta(primary.Id, distant, nowMinute, nowDay));
        Assert.Equal(allyBefore,
            session.State.Social.FactionReputation(allyId) - session.WordSpread.UnarrivedFactionStandingDelta(allyId, distant, nowMinute, nowDay));
        Assert.Equal(enemyBefore,
            session.State.Social.FactionReputation(enemyId) - session.WordSpread.UnarrivedFactionStandingDelta(enemyId, distant, nowMinute, nowDay));
        Assert.Equal(knightKnownBefore,
            session.State.Social.FactionReputation(knightlyId) - session.WordSpread.UnarrivedFactionStandingDelta(knightlyId, distant, nowMinute, nowDay));
        Assert.Equal(genericKnightKnownBefore,
            session.State.Social.FactionReputation(844) - session.WordSpread.UnarrivedFactionStandingDelta(844, distant, nowMinute, nowDay));

        // 2. Clamped delta near +100: requesting +200 only moves standing to +100; subtracting unarrived delta still reconstructs exact prior standing!
        int preClamp = session.State.Social.FactionReputation(primary.Id)
            - session.WordSpread.UnarrivedFactionStandingDelta(primary.Id, distant, nowMinute, nowDay);
        session.State.Social.ChangeFactionReputation(primary.Id, +200, DaggerfallFactionReputationChange.Direct);
        Assert.Equal(100, session.State.Social.FactionReputation(primary.Id));
        Assert.Equal(preClamp,
            session.State.Social.FactionReputation(primary.Id) - session.WordSpread.UnarrivedFactionStandingDelta(primary.Id, distant, nowMinute, nowDay));

        // 3. After enough elapsed time for couriers to arrive, distant town knows the new standing!
        session.AdvanceElapsedTime(checked(5L * DaggerfallCalendar.SecondsPerDay));
        long laterMinute = CurrentMinute(session);
        int laterDay = checked((int)(laterMinute / (DaggerfallCalendar.HoursPerDay * DaggerfallCalendar.MinutesPerHour)));
        Assert.Equal(0, session.WordSpread.UnarrivedFactionStandingDelta(primary.Id, distant, laterMinute, laterDay));
        Assert.Equal(0, session.WordSpread.UnarrivedFactionStandingDelta(allyId, distant, laterMinute, laterDay));
        Assert.Equal(0, session.WordSpread.UnarrivedFactionStandingDelta(enemyId, distant, laterMinute, laterDay));
        Assert.Equal(0, session.WordSpread.UnarrivedFactionStandingDelta(knightlyId, distant, laterMinute, laterDay));
    }

    [Fact]
    public void Courier_disruption_slows_both_crimes_and_mages_guild_rank_gossip_at_distant_front_line()
    {
        using var fixture = SourceBackedGuildBankSessionFixture.Create();
        using DaggerfallSession session = fixture.Start(fixture.MagesProfile);
        DaggerfallSiteRecord origin = session.Site.ActiveSite!;
        DaggerfallSiteRecord frontLine = FindDistantTownInRegion(origin, minDistancePixels: 18.0);
        long baseMinute = CurrentMinute(session);
        int baseDay = checked((int)(baseMinute / (DaggerfallCalendar.HoursPerDay * DaggerfallCalendar.MinutesPerHour)));

        // Measure peaceful baseline travel time to the distant town.
        DaggerfallWordWavefrontSave probeWf = session.WordSpread.RecordNews(
            "wf:probe:baseline", "Baseline Probe", "Baseline courier probe.", origin, baseMinute);
        DaggerfallWordArrivalEvaluation peacefulEval = session.WordSpread.EvaluateAt(probeWf, frontLine, baseMinute, baseDay);
        Assert.Equal(1.0, peacefulEval.CourierDisruptionMultiplier, precision: 3);
        long peacefulMinutes = peacefulEval.RemainingMinutes;
        Assert.True(peacefulMinutes > 60);

        // Put the region into a war zone (BorderTension) + MonsterSurge!
        session.WorldConditions.Set(new DaggerfallWorldConditionToken(
            DaggerfallWorldConditions.RegionTokenId(DaggerfallWorldConditionKind.BorderTension, DaggerfallRegion),
            DaggerfallWorldConditionKind.BorderTension,
            DaggerfallRegion,
            Severity: 2,
            StartedDay: 0));
        session.WorldConditions.Set(new DaggerfallWorldConditionToken(
            DaggerfallWorldConditions.RegionTokenId(DaggerfallWorldConditionKind.MonsterSurge, DaggerfallRegion, "orcs"),
            DaggerfallWorldConditionKind.MonsterSurge,
            DaggerfallRegion,
            SubjectTag: "orcs",
            Severity: 1,
            StartedDay: 0));

        // 1. BAD standing (crime): delayed far beyond peaceful travel time inside the war zone.
        session.State.Crime.RecordIncident(new DaggerfallCrimeIncidentSave(
            OperationId: "a7-warzone-crime",
            Crime: DaggerfallCrimeKind.Murder,
            Stage: DaggerfallCrimeStage.Completed,
            PerpetratorActorId: DaggerfallActorIdentity.PlayerEntityId,
            AffectedActorOrOwnerId: 9011,
            Region: DaggerfallRegion,
            GameMinute: baseMinute,
            TargetKind: DaggerfallCrimeTargetKind.Civilian,
            Witnesses: new DaggerfallCrimeWitnessEvidence(DaggerfallCrimeWitnessQuery.CompletedWithWitnesses, [9012]),
            GuildCredit: DaggerfallCrimeGuildCredit.CivilianMurder,
            Reported: true));
        Assert.True(session.WordSpread.TryGet("wf:crime:a7-warzone-crime", out DaggerfallWordWavefrontSave? crimeWf));
        DaggerfallWordArrivalEvaluation warCrimeEval = session.WordSpread.EvaluateAt(crimeWf!, frontLine, baseMinute, baseDay);
        Assert.True(warCrimeEval.CourierDisruptionMultiplier < 0.30,
            $"Expected war-zone + monster-surge courier disruption < 0.30, got {warCrimeEval.CourierDisruptionMultiplier}.");
        Assert.True(warCrimeEval.RemainingMinutes > peacefulMinutes * 3,
            $"Expected war-zone crime delay ({warCrimeEval.RemainingMinutes}m) to be >3x peaceful ({peacefulMinutes}m).");

        // 2. GOOD standing (Mages Guild promotion): ALSO delayed to the distant front line!
        session.State.Social.JoinGuild(DaggerfallConcreteGuildCatalog.MagesFactionId, baseDay);
        session.State.Social.PromoteGuild(DaggerfallConcreteGuildCatalog.MagesFactionId, baseDay);

        // At origin (within instant bubble): guild rank IS known immediately, and Mages Guild dialogue greets by rank title!
        Assert.True(session.WordSpread.IsGuildRankKnownAt(DaggerfallConcreteGuildCatalog.MagesFactionId, 1, origin, baseMinute, baseDay));
        string expectedRankTitle = DaggerfallGuildRankTitles.Title(
            DaggerfallConcreteGuildCatalog.MagesFactionId, 1, session.State.Character.Identity.Gender == DaggerfallCharacterGender.Female)!;
        DaggerfallNpc magesNpc = session.State.Npcs.All.First(npc =>
            npc.Profile == fixture.MagesProfile.ProfileKey
            && npc.Services.Contains("talk", StringComparer.Ordinal));
        DaggerfallActivationOutcome opened = ActivateNpc(session, magesNpc.DurableId);
        Assert.True(opened.Applied, opened.Message);
        DaggerfallDialogueView magesView = Assert.IsType<DaggerfallDialogueView>(session.ActivationPanel.View.Dialogue);
        Assert.Contains(expectedRankTitle, magesView.Greeting, StringComparison.Ordinal);
        session.Dialogue.Close();

        // Advance past peaceful travel time, but STILL short of war-zone disrupted arrival time:
        // Neither the crime NOR the Mages Guild promotion has reached the front-line town!
        session.AdvanceElapsedTime(checked((peacefulMinutes + 30) * DaggerfallCalendar.SecondsPerMinute));
        long midMinute = CurrentMinute(session);
        int midDay = checked((int)(midMinute / (DaggerfallCalendar.HoursPerDay * DaggerfallCalendar.MinutesPerHour)));
        Assert.False(session.WordSpread.EvaluateAt(crimeWf!, frontLine, midMinute, midDay).HasArrived);
        Assert.False(session.WordSpread.IsGuildRankKnownAt(DaggerfallConcreteGuildCatalog.MagesFactionId, 1, frontLine, midMinute, midDay));
        Assert.DoesNotContain(session.WordSpread.ArrivedNewsAt(frontLine, midMinute, midDay),
            wf => wf.GuildRank == 1 && wf.FactionId == DaggerfallConcreteGuildCatalog.MagesFactionId);

        // Advance past the full disrupted war-zone travel time: NOW both the crime AND the Mages Guild rank gossip arrive!
        long remainingSeconds = checked((warCrimeEval.RemainingMinutes + 30) * DaggerfallCalendar.SecondsPerMinute);
        session.AdvanceElapsedTime(remainingSeconds);
        long lateMinute = CurrentMinute(session);
        int lateDay = checked((int)(lateMinute / (DaggerfallCalendar.HoursPerDay * DaggerfallCalendar.MinutesPerHour)));
        Assert.True(session.WordSpread.EvaluateAt(crimeWf!, frontLine, lateMinute, lateDay).HasArrived);
        Assert.True(session.WordSpread.IsGuildRankKnownAt(DaggerfallConcreteGuildCatalog.MagesFactionId, 1, frontLine, lateMinute, lateDay));
        Assert.Contains(session.WordSpread.ArrivedNewsAt(frontLine, lateMinute, lateDay),
            wf => wf.GuildRank == 1 && wf.FactionId == DaggerfallConcreteGuildCatalog.MagesFactionId);
    }

    [Fact]
    public void Board_game_couriers_require_destination_delivery_and_support_buy_intercept_slain_courier_commands_and_save_reload()
    {
        using TwoTownFixture fixture = TwoTownFixture.Create();
        DaggerfallSession session = fixture.Session;

        DaggerfallSiteRecord origin = fixture.OriginSite;
        DaggerfallSiteRecord destination = fixture.DestinationSite;
        List<DaggerfallDispatchResolution> boardMoves = [];
        session.CourierDispatches.BindBoardMoveCallback(boardMoves.Add);

        // Give the player carried gold for buying a dispatch.
        Assert.True(session.State.Currency.ReceiveGold(500));

        // 1. Playable developer command seam: world.dispatch-send stages a live courier with persistent overland band!
        IDeveloperWorldConditionCommands devCommands = session.DebugCommands
            .OfType<IDeveloperWorldConditionCommands>()
            .Single();
        DebugCommandResult sendResult = devCommands.SendDispatch("crown:daggerfall", "border-posture", DaggerfallRegion);
        Assert.True(sendResult.Succeeded, sendResult.Message);
        string devBandId;
        string devDispatchId;
        long devCourierId;
        using (JsonDocument doc = JsonDocument.Parse(sendResult.Message))
        {
            Assert.Equal(1, doc.RootElement.GetProperty("dispatches").GetArrayLength());
            JsonElement first = doc.RootElement.GetProperty("dispatches")[0];
            devDispatchId = first.GetProperty("dispatchId").GetString()!;
            devBandId = first.GetProperty("overlandBandId").GetString()!;
            devCourierId = first.GetProperty("courierActorId").GetInt64();
            Assert.Contains(session.Expedition.PersistentBands, b => b.BandId == devBandId);
        }

        // Meeting that persistent overland courier band and entering the scene materializes the courier as a live talkable NPC!
        DaggerfallPersistentOverlandBand stagedBand = session.Expedition.PersistentBands.Single(b => b.BandId == devBandId);
        session.OverlandParties.OnMeetPersistentBand(stagedBand, new(origin.MapPixelX, origin.MapPixelY), default);
        session.OverlandParties.OnEnterSceneFromExpedition(
            session.Sites.Projection.Inputs,
            session.State.PlayerControl.Position!.Value,
            session.State.PlayerControl.YawRadians);
        Assert.True(ActivateNpc(session, devCourierId).Applied);
        DaggerfallDialogueView metBandView = Assert.IsType<DaggerfallDialogueView>(session.ActivationPanel.View.Dialogue);
        Assert.Contains(metBandView.Topics, t => t.Id == $"dispatch:deliver:{devDispatchId}");
        Assert.Contains(metBandView.Topics, t => t.Id == $"dispatch:buy:{devDispatchId}");
        Assert.Contains(metBandView.Topics, t => t.Id == $"dispatch:intercept:{devDispatchId}");
        session.Dialogue.Close();

        // Helper to place a live talkable civilian actor in the currently active site for dialogue testing.
        long PlaceLiveCivilianAtActiveSite(string stableKey, string role = "courier")
        {
            DaggerfallSiteRecord activeSite = session.Site.ActiveSite!;
            DaggerfallNpcSite npcSite = new(activeSite.Id.Region, activeSite.Name, string.Empty, session.Sites.ActiveProfile.LogicalId);
            long deterministicId = DaggerfallPeopleSlots.SeededSlotIdentity(activeSite.Id, $"{session.Sites.ActiveProfile.LogicalId}/test/{stableKey}");
            long id = session.State.Npcs.RegisterDeterministic(
                deterministicId,
                DaggerfallNpcKind.Civilian,
                stableKey,
                npcSite,
                new("breton", "Male", 182, 0, 11, 201),
                role,
                ["talk"],
                homeProfile: session.Sites.ActiveProfile);
            session.State.Npcs.Bind(id, session.Sites.ActiveProfile);
            var pos = session.State.PlayerControl.Position!.Value;
            var offset = pos with { X = pos.X + 1.5f };
            ActorOrigin actorOrigin = ActorOrigin.Seeded(
                session.Sites.ActiveProfile,
                stableKey,
                "courtier",
                ActorHarmPolicyKind.LawProtectedCivilian,
                ActorInteractionPolicyKind.FullDialogue);
            session.Civilians.MaterializeDeterministic(id, actorOrigin, offset, new ActorPose(offset, 0f), System.Numerics.Vector3.Zero);
            return id;
        }

        // 2. DELIVER path: accept at origin -> REJECTED at origin guard -> travel to addressed destination -> ACCEPTED at destination guard!
        long deliverCourierId = PlaceLiveCivilianAtActiveSite("a7-courier-deliver");
        long nowMinute = CurrentMinute(session);
        int nowDay = checked((int)(nowMinute / (DaggerfallCalendar.HoursPerDay * DaggerfallCalendar.MinutesPerHour)));
        DaggerfallWordWavefrontSave linkedStanding = session.WordSpread.RecordNews(
            "wf:move:deliver-1", "Daggerfall Envoy Orders", "Royal orders from Castle Daggerfall.", origin, nowMinute, factionId: 201);
        DaggerfallCourierDispatchSave deliverDispatch = session.CourierDispatches.StageDispatch(
            dispatchId: "disp:deliver-1",
            seatId: "crown:daggerfall",
            actionKind: "mobilize-patrol",
            summary: "Royal patrol orders to distant garrison",
            origin: origin,
            destination: destination,
            currentMinute: nowMinute,
            currentDay: nowDay,
            sourceFactionId: 201,
            deliveryRewardGold: 120,
            linkedWavefrontId: linkedStanding.EventId,
            courierActorId: deliverCourierId,
            stageOverlandBand: false);

        ulong goldBeforeDelivery = session.State.Currency.Read().Gold;
        int repBeforeDelivery = session.State.Social.FactionReputation(201);
        Assert.True(ActivateNpc(session, deliverCourierId).Applied);
        DaggerfallDialogueView courierView = Assert.IsType<DaggerfallDialogueView>(session.ActivationPanel.View.Dialogue);
        Assert.Contains(courierView.Topics, t => t.Id == $"dispatch:deliver:{deliverDispatch.DispatchId}");
        Assert.Contains(courierView.Topics, t => t.Id == $"dispatch:buy:{deliverDispatch.DispatchId}");
        Assert.Contains(courierView.Topics, t => t.Id == $"dispatch:intercept:{deliverDispatch.DispatchId}");

        DaggerfallActivationOutcome accepted = session.Dialogue.Ask(courierView.Revision, $"dispatch:deliver:{deliverDispatch.DispatchId}", null, null);
        Assert.True(accepted.Applied, accepted.Message);
        Assert.Equal(DaggerfallDispatchStatus.CarriedByPlayer, session.CourierDispatches.TryGet(deliverDispatch.DispatchId, out var carried) ? carried!.Status : default);
        session.Dialogue.Close();

        // Talking to a guard at ORIGIN (same region, wrong town!) MUST NOT offer turn-in, and direct TurnInDelivery at origin MUST fail!
        long originGuardId = PlaceLiveCivilianAtActiveSite("a7-origin-guard", role: "guard");
        Assert.True(ActivateNpc(session, originGuardId).Applied);
        DaggerfallDialogueView originGuardView = Assert.IsType<DaggerfallDialogueView>(session.ActivationPanel.View.Dialogue);
        Assert.DoesNotContain(originGuardView.Topics, t => t.Id.StartsWith("dispatch:turn-in:", StringComparison.Ordinal));
        DaggerfallActivationOutcome refusedAtOrigin = session.Dialogue.Ask(originGuardView.Revision, $"dispatch:turn-in:{deliverDispatch.DispatchId}", null, null);
        Assert.False(refusedAtOrigin.Applied);
        Assert.Contains(destination.Name, refusedAtOrigin.Message, StringComparison.Ordinal);
        session.Dialogue.Close();
        Assert.Equal(DaggerfallDispatchStatus.CarriedByPlayer, session.CourierDispatches.TryGet(deliverDispatch.DispatchId, out var stillCarried) ? stillCarried!.Status : default);
        Assert.Equal(goldBeforeDelivery, session.State.Currency.Read().Gold);
        Assert.Empty(boardMoves);

        // Travel to the addressed destination town -> talk to a guard there -> turn-in succeeds!
        Assert.True(session.TryTransitionTo(fixture.DestinationProfile.ProfileKey));
        long destGuardId = PlaceLiveCivilianAtActiveSite("a7-dest-guard", role: "guard");
        Assert.True(ActivateNpc(session, destGuardId).Applied);
        DaggerfallDialogueView destGuardView = Assert.IsType<DaggerfallDialogueView>(session.ActivationPanel.View.Dialogue);
        Assert.Contains(destGuardView.Topics, t => t.Id == $"dispatch:turn-in:{deliverDispatch.DispatchId}");
        DaggerfallActivationOutcome turnedIn = session.Dialogue.Ask(destGuardView.Revision, $"dispatch:turn-in:{deliverDispatch.DispatchId}", null, null);
        Assert.True(turnedIn.Applied, turnedIn.Message);
        session.Dialogue.Close();

        Assert.Equal(goldBeforeDelivery + 120UL, session.State.Currency.Read().Gold);
        Assert.True(session.State.Social.FactionReputation(201) > repBeforeDelivery);
        Assert.Contains(boardMoves, m => m.DispatchId == deliverDispatch.DispatchId && m.MoveAccelerated && m.DeliveredByPlayer && !m.MoveCancelled);
        Assert.Equal(DaggerfallWordArrivalStatus.AcceleratedOverride,
            session.WordSpread.EvaluateAt(session.WordSpread.TryGet(linkedStanding.EventId, out var accWf) ? accWf! : linkedStanding, destination, nowMinute, nowDay).Status);

        // Return to origin for remaining courier tests.
        Assert.True(session.TryTransitionTo(fixture.OriginProfile.ProfileKey));

        // 3. BUY path through real dialogue topic (dispatch:buy) charges real gold and leaks intelligence onto the notice board!
        long buyCourierId = PlaceLiveCivilianAtActiveSite("a7-courier-buy");
        DaggerfallCourierDispatchSave buyDispatch = session.CourierDispatches.StageDispatch(
            dispatchId: "disp:buy-1",
            seatId: "crown:wayrest",
            actionKind: "trade-embargo",
            summary: "Wayrest plans a harbor tariff against Daggerfall",
            origin: origin,
            destination: destination,
            currentMinute: nowMinute,
            currentDay: nowDay,
            bribePriceGold: 150,
            courierActorId: buyCourierId,
            stageOverlandBand: false);

        ulong goldBeforeBuy = session.State.Currency.Read().Gold;
        Assert.True(ActivateNpc(session, buyCourierId).Applied);
        DaggerfallDialogueView buyView = Assert.IsType<DaggerfallDialogueView>(session.ActivationPanel.View.Dialogue);
        DaggerfallActivationOutcome bought = session.Dialogue.Ask(buyView.Revision, $"dispatch:buy:{buyDispatch.DispatchId}", null, null);
        Assert.True(bought.Applied, bought.Message);
        session.Dialogue.Close();

        Assert.Equal(goldBeforeBuy - 150UL, session.State.Currency.Read().Gold);
        Assert.Contains(boardMoves, m => m.DispatchId == buyDispatch.DispatchId && m.MoveRevealed && !m.MoveCancelled);
        DaggerfallTownNoticeBoardView leakedBoard = session.NoticeBoard.BuildForSite(destination, nowMinute);
        Assert.Contains(leakedBoard.Entries, e => e.Body.Contains("harbor tariff", StringComparison.OrdinalIgnoreCase));

        // 4. INTERCEPT path through real dialogue topic (dispatch:intercept) cancels move, strips condition, and freezes linked crime wavefront!
        long interceptCourierId = PlaceLiveCivilianAtActiveSite("a7-courier-intercept");
        string condTokenId = DaggerfallWorldConditions.RegionTokenId(DaggerfallWorldConditionKind.CrimeCrackdown, DaggerfallRegion, "dispatch");
        session.WorldConditions.Set(new DaggerfallWorldConditionToken(
            condTokenId, DaggerfallWorldConditionKind.CrimeCrackdown, DaggerfallRegion, Severity: 1, StartedDay: 0));
        DaggerfallWordWavefrontSave crimeWf = session.WordSpread.RecordCrime("a7-intercepted-crime", DaggerfallRegion, 15, origin, nowMinute);
        DaggerfallCourierDispatchSave interceptDispatch = session.CourierDispatches.StageDispatch(
            dispatchId: "disp:intercept-1",
            seatId: "crown:daggerfall",
            actionKind: "warrant-dispatch",
            summary: "Sealed warrant naming the player",
            origin: origin,
            destination: destination,
            currentMinute: nowMinute,
            currentDay: nowDay,
            linkedWavefrontId: crimeWf.EventId,
            linkedConditionTokenId: condTokenId,
            courierActorId: interceptCourierId,
            stageOverlandBand: false);

        Assert.True(ActivateNpc(session, interceptCourierId).Applied);
        DaggerfallDialogueView interceptView = Assert.IsType<DaggerfallDialogueView>(session.ActivationPanel.View.Dialogue);
        DaggerfallActivationOutcome seized = session.Dialogue.Ask(interceptView.Revision, $"dispatch:intercept:{interceptDispatch.DispatchId}", null, null);
        Assert.True(seized.Applied, seized.Message);
        session.Dialogue.Close();

        Assert.Contains(boardMoves, m => m.DispatchId == interceptDispatch.DispatchId && m.MoveCancelled);
        Assert.False(session.WorldConditions.HasActive(DaggerfallRegion, DaggerfallWorldConditionKind.CrimeCrackdown, nowDay));
        long tenDaysLater = nowMinute + 10L * DaggerfallCalendar.HoursPerDay * DaggerfallCalendar.MinutesPerHour;
        Assert.True(session.WordSpread.TryGet(crimeWf.EventId, out DaggerfallWordWavefrontSave? frozenWf));
        Assert.Equal(DaggerfallWordArrivalStatus.Blocked,
            session.WordSpread.EvaluateAt(frozenWf!, destination, tenDaysLater, nowDay + 10).Status);

        // 5. SLAIN COURIER path: ReactToCourierDeath cancels the dispatch automatically!
        long slainCourierId = PlaceLiveCivilianAtActiveSite("a7-courier-slain");
        DaggerfallCourierDispatchSave slainDispatch = session.CourierDispatches.StageDispatch(
            dispatchId: "disp:slain-1",
            seatId: "guild:mages",
            actionKind: "arcane-summons",
            summary: "Urgent Mages Guild convocation writ",
            origin: origin,
            destination: destination,
            currentMinute: nowMinute,
            currentDay: nowDay,
            courierActorId: slainCourierId,
            stageOverlandBand: false);
        IReadOnlyList<DaggerfallDispatchResolution> slainResolutions =
            session.CourierDispatches.ReactToCourierDeath(slainCourierId, nowMinute + 10, nowDay);
        Assert.Single(slainResolutions);
        Assert.True(slainResolutions[0].MoveCancelled);
        Assert.Equal(DaggerfallDispatchStatus.Intercepted,
            session.CourierDispatches.TryGet(slainDispatch.DispatchId, out var deadDisp) ? deadDisp!.Status : default);

        // 6. Save / reload round-trip preserves wavefronts, pending/resolved dispatches, AND elapsed time settles pending courier automatically!
        DaggerfallCourierDispatchSave pendingDispatch = session.CourierDispatches.StageDispatch(
            dispatchId: "disp:auto-arrive",
            seatId: "crown:sentinel",
            actionKind: "border-warning",
            summary: "Sentinel envoy carrying border terms",
            origin: origin,
            destination: destination,
            currentMinute: nowMinute,
            currentDay: nowDay,
            stageOverlandBand: false);

        RulesetSavePayload saved = session.CaptureSave();
        using DaggerfallSession restored = fixture.Restore(saved);
        Assert.Equal(session.WordSpread.Count, restored.WordSpread.Count);
        Assert.Equal(session.CourierDispatches.Count, restored.CourierDispatches.Count);
        Assert.True(restored.CourierDispatches.TryGet(pendingDispatch.DispatchId, out DaggerfallCourierDispatchSave? restoredPending));
        Assert.Equal(DaggerfallDispatchStatus.InTransit, restoredPending!.Status);

        // Inspect via world.word-spread and world.dispatches developer commands after reload.
        IDeveloperWorldConditionCommands restoredDev = restored.DebugCommands.OfType<IDeveloperWorldConditionCommands>().Single();
        Assert.True(restoredDev.ListWordSpread().Succeeded);
        Assert.True(restoredDev.ListDispatches().Succeeded);

        // Advance elapsed calendar time past ExpectedArrivalMinute -> AdvanceCalendar settles the dispatch automatically!
        List<DaggerfallDispatchResolution> restoredBoardMoves = [];
        restored.CourierDispatches.BindBoardMoveCallback(restoredBoardMoves.Add);
        long waitSeconds = checked((restoredPending.ExpectedArrivalMinute - CurrentMinute(restored) + 15) * DaggerfallCalendar.SecondsPerMinute);
        restored.AdvanceElapsedTime(waitSeconds);

        Assert.True(restored.CourierDispatches.TryGet(pendingDispatch.DispatchId, out DaggerfallCourierDispatchSave? settled));
        Assert.Equal(DaggerfallDispatchStatus.Delivered, settled!.Status);
        Assert.False(settled.DeliveredByPlayer);
        Assert.Contains(restoredBoardMoves, m => m.DispatchId == pendingDispatch.DispatchId && m.Outcome == DaggerfallDispatchStatus.Delivered && !m.MoveAccelerated);
    }

    private static long CurrentMinute(DaggerfallSession session) =>
        ((IDaggerfallExpeditionSurroundings)session).Calendar.MinuteNumber;

    private static double DirectDistance(DaggerfallSiteRecord a, DaggerfallSiteRecord b) =>
        Math.Sqrt(Math.Pow(b.MapPixelX - a.MapPixelX, 2) + Math.Pow(b.MapPixelY - a.MapPixelY, 2));

    private static DaggerfallSiteRecord FindDistantTownInRegion(DaggerfallSiteRecord origin, double minDistancePixels)
    {
        return TestPayload.Definitions.Locations.Records
            .Where(r => r.Id.Region == origin.Id.Region && r.Id != origin.Id && r.Exterior is not null)
            .Select(r => (Site: r, Dist: DirectDistance(origin, r)))
            .Where(x => x.Dist >= minDistancePixels)
            .OrderByDescending(x => x.Dist)
            .First()
            .Site;
    }

    private static DaggerfallActivationOutcome ActivateNpc(DaggerfallSession session, long durableId)
    {
        DaggerfallActivationTarget target = Assert.Single(
            session.Dialogue.NpcTargets(),
            t => checked((long)t.Identity.Value) == durableId);
        return session.Dialogue.ActivateNpc(new DaggerfallActivationSelection(DaggerfallActivationMode.Talk, target));
    }

    private sealed class TwoTownFixture : IDisposable
    {
        private readonly DaggerfallSessionComposition _composition;
        internal DaggerfallSiteRecord OriginSite { get; }
        internal DaggerfallSiteRecord DestinationSite { get; }
        internal DaggerfallSiteProfile OriginProfile { get; }
        internal DaggerfallSiteProfile DestinationProfile { get; }
        internal DaggerfallSession Session { get; }

        private TwoTownFixture(
            DaggerfallSiteRecord originSite,
            DaggerfallSiteRecord destinationSite,
            DaggerfallSiteProfile originProfile,
            DaggerfallSiteProfile destinationProfile,
            DaggerfallSessionComposition composition,
            DaggerfallSession session)
        {
            OriginSite = originSite;
            DestinationSite = destinationSite;
            OriginProfile = originProfile;
            DestinationProfile = destinationProfile;
            _composition = composition;
            Session = session;
        }

        internal static TwoTownFixture Create(DaggerfallTuning? tuning = null)
        {
            using var templateFixture = SourceBackedGuildBankSessionFixture.Create();
            DaggerfallDefinitions defs = TestPayload.Definitions;
            DaggerfallSiteProfile originProfile = templateFixture.ExteriorProfile;
            DaggerfallSiteRecord originSite = defs.Locations.Records.Single(r => r.Id == originProfile.Site);
            int originClimate = defs.Grids.ClimateAtWorldPixel(originSite.MapPixelX, originSite.MapPixelY).Value;
            bool SameNeighborhoodClimate(DaggerfallSiteRecord site)
            {
                for (int dy = -2; dy <= 2; dy++)
                    for (int dx = -2; dx <= 2; dx++)
                        if (defs.Grids.ClimateAtWorldPixel(site.MapPixelX + dx, site.MapPixelY + dy).Value != originClimate)
                            return false;
                return true;
            }
            DaggerfallSiteRecord destinationSite = defs.Locations.Records
                .Where(r => r.Id.Region == originSite.Id.Region && r.Id != originSite.Id && r.Exterior is not null
                    && SameNeighborhoodClimate(r))
                .Select(r => (Site: r, Dist: DirectDistance(originSite, r)))
                .Where(x => x.Dist >= 10.0)
                .OrderBy(x => x.Dist)
                .First()
                .Site;

            DaggerfallSiteProfile destinationProfile = Assembled(DaggerfallWorldProfileIds.Exterior(destinationSite.Id));

            DaggerfallSessionComposition composition = new(defs, originProfile, tuning ?? DaggerfallTuning.Defaults)
            {
                Profiles = new DaggerfallSiteProfiles([originProfile, destinationProfile]),
            };
            EngineContextFake engine = BuildEngine(originProfile, destinationProfile);
            DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, composition);
            session.Update(new ProductUpdate(OuterUpdate(1), []));
            return new(originSite, destinationSite, originProfile, destinationProfile, composition, session);
        }

        internal DaggerfallSession Restore(RulesetSavePayload saved)
        {
            EngineContextFake engine = BuildEngine(OriginProfile, DestinationProfile);
            return DaggerfallSession.Restore(engine.Context, _composition, saved);
        }

        private static EngineContextFake BuildEngine(params DaggerfallSiteProfile[] profiles)
        {
            List<string> releases = [];
            ContentFake content = new(releases);
            foreach (DaggerfallSiteProfile profile in profiles)
                PopulateContent(content, profile);
            SpatialFake spatial = SpatialFake.Create(profiles[0].Geometry.Spatial[0].Sha256, releases);
            return EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        }

        public void Dispose() => Session.Dispose();
    }
}
