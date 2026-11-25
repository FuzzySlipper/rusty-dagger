using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Debugging;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Crime;
using WorldRpg.Rulesets.Daggerfall.Guilds;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Political layer A7 (#11052): elapsed-time news &amp; standing spread (instant bubble vs. disrupted courier spread
/// affecting crimes and guild standing symmetrically) and board-game courier dispatches (deliver, buy, intercept, kill).
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
    public void Board_game_couriers_support_deliver_buy_intercept_slain_courier_commands_and_save_reload()
    {
        using var fixture = SourceBackedGuildBankSessionFixture.Create();
        using DaggerfallSession session = fixture.Start(fixture.ExteriorProfile);

        DaggerfallSiteRecord origin = session.Site.ActiveSite!;
        DaggerfallSiteRecord destination = FindDistantTownInRegion(origin, minDistancePixels: 18.0);
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
        Travel.DaggerfallPersistentOverlandBand stagedBand = session.Expedition.PersistentBands.Single(b => b.BandId == devBandId);
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

        // Helper to place a live talkable courier actor in the current scene for direct dialogue testing.
        long PlaceLiveCourier(string stableKey, string role = "courier")
        {
            DaggerfallNpcSite npcSite = new(origin.Id.Region, origin.Name, string.Empty, session.Sites.ActiveProfile.LogicalId);
            long deterministicId = DaggerfallPeopleSlots.SeededSlotIdentity(origin.Id, $"{session.Sites.ActiveProfile.LogicalId}/test/{stableKey}");
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

        // 2. DELIVER path through real dialogue topics (dispatch:deliver -> dispatch:turn-in)!
        long deliverCourierId = PlaceLiveCourier("a7-courier-deliver");
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

        // Turn in to an official/guard in the destination region via real dialogue topic dispatch:turn-in!
        long guardId = PlaceLiveCourier("a7-garrison-guard", role: "guard");

        Assert.True(ActivateNpc(session, guardId).Applied);
        DaggerfallDialogueView guardView = Assert.IsType<DaggerfallDialogueView>(session.ActivationPanel.View.Dialogue);
        Assert.Contains(guardView.Topics, t => t.Id == $"dispatch:turn-in:{deliverDispatch.DispatchId}");
        DaggerfallActivationOutcome turnedIn = session.Dialogue.Ask(guardView.Revision, $"dispatch:turn-in:{deliverDispatch.DispatchId}", null, null);
        Assert.True(turnedIn.Applied, turnedIn.Message);
        session.Dialogue.Close();

        Assert.Equal(goldBeforeDelivery + 120UL, session.State.Currency.Read().Gold);
        Assert.True(session.State.Social.FactionReputation(201) > repBeforeDelivery);
        Assert.Contains(boardMoves, m => m.DispatchId == deliverDispatch.DispatchId && m.MoveAccelerated && m.DeliveredByPlayer && !m.MoveCancelled);
        // Linked wavefront is now accelerated at destination immediately!
        Assert.Equal(DaggerfallWordArrivalStatus.AcceleratedOverride,
            session.WordSpread.EvaluateAt(session.WordSpread.TryGet(linkedStanding.EventId, out var accWf) ? accWf! : linkedStanding, destination, nowMinute, nowDay).Status);

        // 3. BUY path through real dialogue topic (dispatch:buy) charges real gold and leaks intelligence onto the notice board!
        long buyCourierId = PlaceLiveCourier("a7-courier-buy");
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
        long interceptCourierId = PlaceLiveCourier("a7-courier-intercept");
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
        // Even 10 days later, the intercepted crime wavefront stays Blocked at the distant town!
        long tenDaysLater = nowMinute + 10L * DaggerfallCalendar.HoursPerDay * DaggerfallCalendar.MinutesPerHour;
        Assert.True(session.WordSpread.TryGet(crimeWf.EventId, out DaggerfallWordWavefrontSave? frozenWf));
        Assert.Equal(DaggerfallWordArrivalStatus.Blocked,
            session.WordSpread.EvaluateAt(frozenWf!, destination, tenDaysLater, nowDay + 10).Status);

        // 5. SLAIN COURIER path: ReactToCourierDeath cancels the dispatch automatically!
        long slainCourierId = PlaceLiveCourier("a7-courier-slain");
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
        ((Travel.IDaggerfallExpeditionSurroundings)session).Calendar.MinuteNumber;

    private static DaggerfallSiteRecord FindDistantTownInRegion(DaggerfallSiteRecord origin, double minDistancePixels)
    {
        return TestPayload.Definitions.Locations.Records
            .Where(r => r.Id.Region == origin.Id.Region && r.Id != origin.Id && r.Exterior is not null)
            .Select(r => (Site: r, Dist: Math.Sqrt(Math.Pow(r.MapPixelX - origin.MapPixelX, 2) + Math.Pow(r.MapPixelY - origin.MapPixelY, 2))))
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
}
