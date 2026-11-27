using System.Text.Json;
using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Live office continuity slice 1 (#11102): deterministic <c>VacancyUntil</c> windows across all 3 tiers,
/// typed <see cref="DaggerfallOfficeLedger.OfficeChanged"/> fan-out into <see cref="DaggerfallWordSpread"/>,
/// <see cref="DaggerfallNoticeBoard"/>, <see cref="DaggerfallWorldConditions"/> (<c>SuccessionCrisis</c>),
/// <see cref="DaggerfallScrutinyPolicy"/>, visible empty-seat slot suppression, and save/load continuity.
/// </summary>
public sealed class OfficeContinuityTests
{
    private const int DaggerfallRegion = 17;
    private const int DaggerfallProvinceFaction = 201;
    private const int WayrestProvinceFaction = 207;

    [Fact]
    public void Vacancy_duration_bounds_are_deterministic_across_all_three_office_tiers()
    {
        DaggerfallOfficeKind[] localTier3 =
        [
            DaggerfallOfficeKind.GuildHallMaster,
            DaggerfallOfficeKind.TempleHead,
            DaggerfallOfficeKind.WatchCaptain,
        ];
        DaggerfallOfficeKind[] regionalAndSeatTier12 =
        [
            DaggerfallOfficeKind.CourtFactor,
            DaggerfallOfficeKind.Ruler,
            DaggerfallOfficeKind.MajorSeat,
        ];

        foreach (ulong seed in new ulong[] { 0UL, 42UL, 999_999UL })
        {
            foreach (DaggerfallOfficeKind kind in localTier3)
            {
                string key = $"local:{kind}:17:1";
                long a = DaggerfallOfficeLedger.ComputeVacancyDurationMinutes(seed, key, kind, openedMinute: 500);
                long b = DaggerfallOfficeLedger.ComputeVacancyDurationMinutes(seed, key, kind, openedMinute: 500);
                Assert.Equal(a, b);
                Assert.InRange(a, DaggerfallOfficeLedger.LocalVacancyMinMinutes, DaggerfallOfficeLedger.LocalVacancyMaxMinutes);
            }

            foreach (DaggerfallOfficeKind kind in regionalAndSeatTier12)
            {
                string key = kind == DaggerfallOfficeKind.CourtFactor ? "local:court-factor:17:1:0:0:0" : $"faction:{(int)kind + 201}";
                long a = DaggerfallOfficeLedger.ComputeVacancyDurationMinutes(seed, key, kind, openedMinute: 500);
                long b = DaggerfallOfficeLedger.ComputeVacancyDurationMinutes(seed, key, kind, openedMinute: 500);
                Assert.Equal(a, b);
                Assert.InRange(a, DaggerfallOfficeLedger.RegionalVacancyMinMinutes, DaggerfallOfficeLedger.RegionalVacancyMaxMinutes);
            }
        }
    }

    [Fact]
    public void Ruler_death_regency_and_seating_drive_OfficeChanged_SuccessionCrisis_and_ScrutinyPolicy()
    {
        using var fixture = SourceBackedGuildBankSessionFixture.Create();
        using DaggerfallSession session = fixture.Start(fixture.ExteriorProfile);
        DaggerfallSiteRecord charing = session.Site.ActiveSite!;
        List<DaggerfallOfficeChangedNotification> events = [];
        session.OfficeLedger.OfficeChanged += events.Add;

        // Untouched baseline: 0 overrides, 0 conditions, Normal scrutiny for a rep -3 traveler.
        session.State.Social.ChangeRegionalReputation(DaggerfallRegion, -3);
        Assert.Equal(0, session.OfficeLedger.OverrideCount);
        Assert.Equal(0, session.WorldConditions.Count);
        Assert.Equal(0, session.WordSpread.Count);
        DaggerfallGateScrutinyAssessment baselineGate = session.Scrutiny.EvaluateGateScrutiny(DaggerfallRegion, charing);
        Assert.Equal(DaggerfallScrutinyLevel.Normal, baselineGate.Level);
        Assert.False(baselineGate.WatchChallengeActive);

        // 1. King Gothryd dies -> opens 7-14d VacancyUntil window, emits VacancyOpened, raises SuccessionCrisis!
        long gothrydId = DaggerfallPeopleSlots.CastIndividualIdentity("king-gothryd");
        session.State.ActorLedger.Upsert(new ActorLedgerEntry(
            gothrydId,
            ActorOrigin.Cast("king-gothryd"),
            Location: ActorLocationOverride.Dead(fixture.ExteriorProfile.ProfileKey)));
        IReadOnlyList<DaggerfallOfficeOverrideSave> deathOverrides = session.OfficeLedger.ReactToDeath(
            gothrydId,
            checked((int)session.Calendar.DayNumber),
            charing,
            session.Calendar.MinuteNumber);
        DaggerfallOfficeOverrideSave vacancy = Assert.Single(deathOverrides);
        Assert.True(vacancy.Vacant);
        Assert.NotNull(vacancy.VacancyOpenedMinute);
        Assert.NotNull(vacancy.VacancyUntilMinute);
        Assert.InRange(
            vacancy.VacancyUntilMinute!.Value - vacancy.VacancyOpenedMinute!.Value,
            DaggerfallOfficeLedger.RegionalVacancyMinMinutes,
            DaggerfallOfficeLedger.RegionalVacancyMaxMinutes);

        DaggerfallOfficeChangedNotification deathEvent = Assert.Single(events);
        Assert.Equal(DaggerfallOfficeChangeKind.VacancyOpened, deathEvent.ChangeKind);
        Assert.Equal(DaggerfallOfficeKind.Ruler, deathEvent.Kind);
        Assert.Equal(DaggerfallRegion, deathEvent.Region);
        Assert.Equal("daggerfall", deathEvent.SeatId);
        Assert.Equal("King Gothryd", deathEvent.PreviousHolderName);
        Assert.False(deathEvent.PreviousHolderIsAlive);
        Assert.Equal(vacancy.VacancyUntilMinute, deathEvent.VacancyUntilMinute);

        // Regional SuccessionCrisis is automatically active and elevates gate scrutiny to Strict!
        string crisisTokenId = DaggerfallWorldConditions.OfficeSuccessionCrisisTokenId(vacancy.OfficeKey);
        Assert.True(session.WorldConditions.HasActive(DaggerfallRegion, DaggerfallWorldConditionKind.SuccessionCrisis));
        DaggerfallGateScrutinyAssessment crisisGate = session.Scrutiny.EvaluateGateScrutiny(DaggerfallRegion, charing);
        Assert.Equal(DaggerfallScrutinyLevel.Strict, crisisGate.Level);
        Assert.True(crisisGate.WatchChallengeActive);
        Assert.Equal(0, crisisGate.EffectiveWantedThreshold);
        Assert.Equal(crisisTokenId, crisisGate.ConditionTokenId);

        // Local notice board shows SuccessionCrisis decree and ONE non-duplicated Throne Vacant OfficeNotice.
        DaggerfallTownNoticeBoardView localBoard = session.ReadTownNoticeBoard()!;
        Assert.Contains(localBoard.Entries, e => e.Kind == DaggerfallNoticeEntryKind.Decree && e.ConditionTokenId == crisisTokenId);
        int daggerfallOfficeNotices = localBoard.Entries.Count(e =>
            e.Kind == DaggerfallNoticeEntryKind.OfficeNotice
            && e.FactionId == DaggerfallProvinceFaction);
        Assert.Equal(1, daggerfallOfficeNotices);

        // 2. Interim regency keeps the vacancy deadline and SuccessionCrisis active until a lawful successor takes the throne.
        DaggerfallOfficeOverrideSave regency = session.OfficeLedger.DeclareFactionRegency(
            DaggerfallProvinceFaction,
            regentActorId: null,
            regentCastId: "lord-bridwell",
            regentFactionId: 367,
            regentDisplayName: "Lord Bridwell",
            cause: DaggerfallOfficeChangeCause.Succession,
            changedDay: checked((int)session.Calendar.DayNumber),
            currentMinute: session.Calendar.MinuteNumber);
        Assert.True(regency.IsRegency);
        Assert.Equal(vacancy.VacancyUntilMinute, regency.VacancyUntilMinute);
        Assert.Equal(DaggerfallOfficeChangeKind.RegencyDeclared, events[^1].ChangeKind);
        Assert.True(session.WorldConditions.HasActive(DaggerfallRegion, DaggerfallWorldConditionKind.SuccessionCrisis));

        // 3. Seating a lawful successor closes the vacancy window, clears SuccessionCrisis, and restores Normal scrutiny!
        DaggerfallOfficeOverrideSave seated = session.OfficeLedger.SetBoardSeatHolder(
            seatId: "daggerfall",
            holderActorId: null,
            holderCastId: "mobar",
            holderFactionId: null,
            holderDisplayName: "Mobar",
            cause: DaggerfallOfficeChangeCause.Succession,
            changedDay: checked((int)session.Calendar.DayNumber),
            causeDetail: "crowned by the council",
            currentMinute: session.Calendar.MinuteNumber);
        Assert.False(seated.Vacant);
        Assert.False(seated.IsRegency);
        Assert.Null(seated.VacancyUntilMinute);
        Assert.Equal(DaggerfallOfficeChangeKind.SuccessorSeated, events[^1].ChangeKind);
        Assert.False(session.WorldConditions.HasActive(DaggerfallRegion, DaggerfallWorldConditionKind.SuccessionCrisis));

        DaggerfallGateScrutinyAssessment settledGate = session.Scrutiny.EvaluateGateScrutiny(DaggerfallRegion, charing);
        Assert.Equal(DaggerfallScrutinyLevel.Normal, settledGate.Level);
        Assert.False(settledGate.WatchChallengeActive);

        // 4. Non-lethal deposition of another ruler raises that region's SuccessionCrisis and emits HolderDeposed.
        int wayrestRegion = TestPayload.Definitions.Factions.Factions[WayrestProvinceFaction].Region;
        Assert.True(wayrestRegion >= 0);
        DaggerfallOfficeOverrideSave wayrestDeposed = session.OfficeLedger.DeposeBoardSeat(
            seatId: "wayrest",
            cause: DaggerfallOfficeChangeCause.CardMove,
            changedDay: checked((int)session.Calendar.DayNumber),
            causeDetail: "forced into exile",
            currentMinute: session.Calendar.MinuteNumber);
        Assert.True(wayrestDeposed.Vacant);
        Assert.Equal(wayrestRegion, wayrestDeposed.OfficeRegion);
        Assert.Equal(DaggerfallOfficeChangeKind.HolderDeposed, events[^1].ChangeKind);
        Assert.True(events[^1].PreviousHolderIsAlive);
        Assert.True(session.WorldConditions.HasActive(wayrestRegion, DaggerfallWorldConditionKind.SuccessionCrisis));
        Assert.True(session.OfficeLedger.ClearOverride(wayrestDeposed.OfficeKey));
        Assert.False(session.WorldConditions.HasActive(wayrestRegion, DaggerfallWorldConditionKind.SuccessionCrisis));
    }

    [Fact]
    public void OfficeChanged_emits_WordSpread_proclamation_that_reaches_distant_town_notice_board_at_courier_speed()
    {
        using var fixture = SourceBackedGuildBankSessionFixture.Create();
        using DaggerfallSession session = fixture.Start(fixture.ExteriorProfile);

        // Find a distant town in Sentinel (Region 20) well outside Daggerfall's instant news bubble.
        DaggerfallSiteRecord distantTown = TestPayload.Definitions.Locations.Records
            .Where(r => r.Id.Region == 20 && r.Exterior is { Buildings.Count: > 0 })
            .OrderByDescending(r => r.Exterior!.Buildings.Count)
            .First();

        // Depose Daggerfall's king -> emits wf:office:faction:201|deposed|... wavefront from Daggerfall City!
        DaggerfallOfficeOverrideSave deposed = session.OfficeLedger.DeposeBoardSeat(
            seatId: "daggerfall",
            cause: DaggerfallOfficeChangeCause.CardMove,
            changedDay: checked((int)session.Calendar.DayNumber),
            causeDetail: "ousted by court cabal",
            currentMinute: session.Calendar.MinuteNumber);

        DaggerfallWordWavefrontSave vacancyWavefront = Assert.Single(session.WordSpread.Wavefronts,
            wf => DaggerfallWordSpread.TryParseOfficeProclamationKey(wf.EventId, out string key) && key == deposed.OfficeKey);
        Assert.Equal(DaggerfallRegion, vacancyWavefront.OriginRegion);
        Assert.Equal(DaggerfallWordEventKind.NewsRumor, vacancyWavefront.Kind);

        long t0 = session.Calendar.MinuteNumber;
        int day0 = checked((int)session.Calendar.DayNumber);
        DaggerfallWordArrivalEvaluation evalAtStart = session.WordSpread.EvaluateAt(vacancyWavefront, distantTown, t0, day0);
        Assert.Equal(DaggerfallWordArrivalStatus.InTransit, evalAtStart.Status);
        Assert.True(evalAtStart.RemainingMinutes > 0);

        // Before couriers arrive, distant Sentinel town does NOT post Daggerfall's vacancy proclamation.
        DaggerfallTownNoticeBoardView distantBefore = session.NoticeBoard.BuildForSite(distantTown, t0);
        Assert.DoesNotContain(distantBefore.Entries, e =>
            e.Kind == DaggerfallNoticeEntryKind.OfficeNotice && e.FactionId == DaggerfallProvinceFaction);

        // Advance elapsed calendar time past the courier arrival ETA -> proclamation appears on Sentinel's notice board!
        long travelSeconds = checked((evalAtStart.RemainingMinutes + 60L) * 60L);
        session.AdvanceElapsedTime(travelSeconds);
        long t1 = session.Calendar.MinuteNumber;
        int day1 = checked((int)session.Calendar.DayNumber);
        Assert.True(session.WordSpread.EvaluateAt(vacancyWavefront, distantTown, t1, day1).HasArrived);

        DaggerfallTownNoticeBoardView distantAfterVacancy = session.NoticeBoard.BuildForSite(distantTown, t1);
        DaggerfallNoticeEntry arrivedVacancy = Assert.Single(distantAfterVacancy.Entries, e =>
            e.Kind == DaggerfallNoticeEntryKind.OfficeNotice && e.FactionId == DaggerfallProvinceFaction);
        Assert.Contains("King Gothryd", arrivedVacancy.Body, StringComparison.Ordinal);
        Assert.Contains("vacant", arrivedVacancy.Body, StringComparison.OrdinalIgnoreCase);

        // Seat a successor -> once the newer proclamation reaches Sentinel, it supersedes the old vacancy entry without duplicating!
        session.OfficeLedger.SetBoardSeatHolder(
            seatId: "daggerfall",
            holderActorId: null,
            holderCastId: "lord-bridwell",
            holderFactionId: 367,
            holderDisplayName: "Lord Bridwell",
            cause: DaggerfallOfficeChangeCause.Succession,
            changedDay: day1,
            causeDetail: "acclaimed by the estates",
            currentMinute: t1);

        DaggerfallWordWavefrontSave seatedWavefront = Assert.Single(
            session.WordSpread.Wavefronts,
            wf => wf.EventId.StartsWith($"{DaggerfallWordSpread.OfficeProclamationPrefix}{deposed.OfficeKey}|seated|", StringComparison.Ordinal));
        DaggerfallWordArrivalEvaluation seatedEta = session.WordSpread.EvaluateAt(seatedWavefront, distantTown, t1, day1);
        session.AdvanceElapsedTime(checked((seatedEta.RemainingMinutes + 60L) * 60L));

        DaggerfallTownNoticeBoardView distantAfterSeating = session.NoticeBoard.BuildForSite(distantTown, session.Calendar.MinuteNumber);
        DaggerfallNoticeEntry arrivedSeating = Assert.Single(distantAfterSeating.Entries, e =>
            e.Kind == DaggerfallNoticeEntryKind.OfficeNotice && e.FactionId == DaggerfallProvinceFaction);
        Assert.Contains("Lord Bridwell", arrivedSeating.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Visible_empty_seat_suppresses_deposed_holders_slot_on_entry_and_survives_save_load()
    {
        using var fixture = SourceBackedGuildBankSessionFixture.Create();
        using DaggerfallSession session = fixture.Start(fixture.ExteriorProfile);
        session.AdvanceElapsedTime(8 * 60 * 60);
        DaggerfallSiteRecord charing = session.Site.ActiveSite!;

        // 1. Depose Charing's Mages Guild Hall Master BEFORE entering the Mages Guild -> slot is suppressed OffStage!
        DaggerfallLocalOffice magesMaster = session.OfficeLedger.ResolveLocalOffices(charing)
            .Single(o => o.Kind == DaggerfallOfficeKind.GuildHallMaster && o.ProfileKey == fixture.MagesProfile.ProfileKey);
        long masterId = magesMaster.HolderActorId!.Value;
        DaggerfallOfficeOverrideSave masterVacancy = session.OfficeLedger.DeposeLocalOffice(
            magesMaster,
            DaggerfallOfficeChangeCause.Quest,
            checked((int)session.Calendar.DayNumber),
            causeDetail: "suspended by the guild council",
            currentMinute: session.Calendar.MinuteNumber);

        Assert.True(masterVacancy.Vacant);
        Assert.InRange(
            masterVacancy.VacancyUntilMinute!.Value - masterVacancy.VacancyOpenedMinute!.Value,
            DaggerfallOfficeLedger.LocalVacancyMinMinutes,
            DaggerfallOfficeLedger.LocalVacancyMaxMinutes);
        Assert.True(session.State.ActorLedger.IsHomeSlotSuppressed(masterId, fixture.MagesProfile.ProfileKey));
        Assert.True(session.State.ActorLedger.TryGet(masterId, out ActorLedgerEntry? masterLedgerEntry));
        Assert.Equal(ActorLocationKind.OffStage, masterLedgerEntry!.Location?.Kind);

        // 2. Depose the currently live exterior Watch Captain -> immediately unloads from EntityStore & leaves post empty!
        DaggerfallLocalOffice watchCaptain = session.OfficeLedger.ResolveWatchCaptain(charing);
        long captainId = watchCaptain.HolderActorId!.Value;
        Assert.True(session.State.Actors.TryGet(captainId, out _));
        DaggerfallOfficeOverrideSave captainVacancy = session.OfficeLedger.DeposeLocalOffice(
            watchCaptain,
            DaggerfallOfficeChangeCause.CardMove,
            checked((int)session.Calendar.DayNumber),
            causeDetail: "recalled to the capital",
            currentMinute: session.Calendar.MinuteNumber);

        Assert.False(session.State.Actors.TryGet(captainId, out _));
        Assert.True(session.State.ActorLedger.IsHomeSlotSuppressed(captainId, fixture.ExteriorProfile.ProfileKey));
        Assert.True(session.OfficeLedger.ResolveWatchCaptain(charing).IsVacant);

        // Social diagnostic inspection exposes vacancyUntilMinute cleanly.
        using (JsonDocument siteDoc = JsonDocument.Parse(session.Commands<IDeveloperSocialCommands>().Site().Message))
        {
            JsonElement masterJson = siteDoc.RootElement.GetProperty("localOffices")
                .EnumerateArray()
                .Single(e => e.GetProperty("officeKey").GetString() == magesMaster.OfficeKey);
            Assert.True(masterJson.GetProperty("isVacant").GetBoolean());
            Assert.Equal(masterVacancy.VacancyUntilMinute!.Value, masterJson.GetProperty("vacancyUntilMinute").GetInt64());
        }

        // 3. Enter the Mages Guild during the vacancy window -> deposed Hall Master is absent (seat visibly empty),
        //    and no instant replacement has popped into their office!
        Assert.True(session.TryTransitionTo(fixture.MagesProfile.ProfileKey));
        Assert.False(session.State.Actors.TryGet(masterId, out _));
        DaggerfallLocalOffice insideMaster = session.OfficeLedger.ResolveLocalOffices(charing)
            .Single(o => o.OfficeKey == magesMaster.OfficeKey);
        Assert.True(insideMaster.IsVacant);
        Assert.Equal(masterVacancy.VacancyUntilMinute, insideMaster.VacancyUntilMinute);

        // 4. Save & reload preserves vacancy windows, OffStage suppression, WordSpread wavefronts, and empty seat!
        RulesetSavePayload saved = session.CaptureSave();
        using DaggerfallSession restored = fixture.Restore(saved);
        Assert.False(restored.State.Actors.TryGet(masterId, out _));
        Assert.True(restored.State.ActorLedger.IsHomeSlotSuppressed(masterId, fixture.MagesProfile.ProfileKey));
        Assert.True(restored.State.ActorLedger.IsHomeSlotSuppressed(captainId, fixture.ExteriorProfile.ProfileKey));
        Assert.Equal(2, restored.OfficeLedger.OverrideCount);
        Assert.Empty(restored.OfficeLedger.DueVacancies(restored.Calendar.MinuteNumber));

        // 5. Advance calendar past both 24–48h local vacancy windows -> DueVacancies reports both matured vacancies!
        long maxUntil = Math.Max(masterVacancy.VacancyUntilMinute!.Value, captainVacancy.VacancyUntilMinute!.Value);
        long minutesToAdvance = maxUntil - restored.Calendar.MinuteNumber + 1;
        restored.AdvanceElapsedTime(checked(minutesToAdvance * 60L));
        IReadOnlyList<DaggerfallOfficeOverrideSave> due = restored.OfficeLedger.DueVacancies(restored.Calendar.MinuteNumber);
        Assert.Equal(2, due.Count);
        Assert.Contains(due, o => o.OfficeKey == magesMaster.OfficeKey);
        Assert.Contains(due, o => o.OfficeKey == watchCaptain.OfficeKey);
    }
}
