using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Crime;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Live office continuity slice 3 (#11110):
/// - <see cref="DaggerfallIntroductions"/> subscription to <see cref="DaggerfallOfficeLedger.OfficeChanged"/>:
///   shared <c>#11084</c> <c>FamilyId</c> dynastic heirs honor predecessor vouches unless the player has an
///   attributed crime/kill against the dynasty; unrelated outsider/rival turnovers invalidate prior court favours;
///   active vacancies / <see cref="DaggerfallWorldConditionKind.SuccessionCrisis"/> tighten court access and pause
///   routine patron introductions until a lawful successor is seated.
/// - Court &amp; guildhall dialogue continuity: live holder name/title, vacancy, and interim regency reflected in
///   greetings, rumours, and ambient barks with zero hardcoded cast names.
/// - Full succession round-trip across death -&gt; vacancy/crisis/proclamation -&gt; physical seating -&gt;
///   <see cref="DaggerfallOfficeHolders"/> quest rebinding -&gt; dynastic vs outsider vouching -&gt; save/load.
/// </summary>
public sealed class OfficeDialogueAndVouchingTests
{
    private const int DaggerfallRegion = 17;

    [Fact]
    public void Full_succession_round_trip_covers_death_vacancy_crisis_tightened_access_paused_intros_seating_quest_rebinding_dynastic_vouching_and_save_load()
    {
        using var fixture = SourceBackedGuildBankSessionFixture.Create();
        fixture.Random = RandomMinimum.Create();
        using DaggerfallSession session = fixture.Start(fixture.ExteriorProfile);
        session.AdvanceElapsedTime(8 * 60 * 60);
        DaggerfallSiteRecord charing = session.Site.ActiveSite!;
        DaggerfallBoardSeatBinding daggerfallSeat = TestPayload.Definitions.BoardSeats.All.Single(s => s.SeatId == "daggerfall");

        // Smooth enough for skillSkip = 1 (allows MinorCourt in normal times, blocked when SuccessionCrisis tightens reach +1!).
        SetSocialStats(session, etiquette: 55, streetwise: 30, personality: 50);

        // Materialize two real Region 17 court figures (CourtFavourite + MinorCourt) plus an ordinary local shopkeeper.
        DaggerfallCastIndividualDefinition bridwellDef = TestPayload.Definitions.People.Individuals["lord-bridwell"];
        DaggerfallCastIndividualDefinition mobarDef = TestPayload.Definitions.People.Individuals["mobar"];
        int daggerfallCourtFactionId = TestPayload.Definitions.Factions.Factions.Values
            .Where(f => f.Type == 14 && f.Region == DaggerfallRegion)
            .OrderBy(f => f.Id)
            .First().Id;
        long bridwellId = MaterializeCastCourtActor(session, charing, bridwellDef);
        long mobarId = MaterializeCastCourtActor(session, charing, mobarDef);
        long minorAttendantId = AddCourtNpc(session, charing, "court/attendant", "Gareth Vance", factionId: daggerfallCourtFactionId, ["talk"]);
        long merchantId = AddCourtNpc(session, charing, "market/tanner", "Bram Tanner", factionId: 0, ["talk", "shop"]);
        long neighborId = AddCourtNpc(session, charing, "market/neighbor", "Tessa Baker", factionId: 0, ["talk"]);

        DaggerfallNpc bridwell = session.State.Npcs.Require(bridwellId);
        DaggerfallNpc mobar = session.State.Npcs.Require(mobarId);
        DaggerfallNpc attendant = session.State.Npcs.Require(minorAttendantId);
        DaggerfallNpc merchant = session.State.Npcs.Require(merchantId);
        DaggerfallNpc neighbor = session.State.Npcs.Require(neighborId);

        // Baseline: King Gothryd is live regional ruler; MinorCourt attendant is accessible at requiredReach = 1;
        // courtier greeting reflects live ruler name/title dynamically.
        DaggerfallOfficeHolder baselineRuler = session.OfficeHolders.ResolveRegionalRuler(DaggerfallRegion)!;
        Assert.True(baselineRuler.HasLivingHolder);
        Assert.Equal("king-gothryd", baselineRuler.CastId);
        long gothrydId = baselineRuler.ActorId!.Value;

        DaggerfallIntroductionAccess baselineAttendantAccess = session.Introductions.AssessAccess(attendant);
        Assert.True(baselineAttendantAccess.Allowed);
        Assert.Equal(DaggerfallSocialProminence.MinorCourt, baselineAttendantAccess.Prominence);
        Assert.Equal(1, baselineAttendantAccess.RequiredReach);

        string baselineCourtGreeting = OpenGreetingAndClose(session, bridwellId);
        Assert.Contains("King Gothryd", baselineCourtGreeting, StringComparison.Ordinal);

        // Earn Bridwell's strong personal trust (Prominent direct standing under crisis requires disposition >= 40 + 15 = 55)
        // and get a Court vouch TO King Gothryd, plus a Court vouch FROM King Gothryd ON Mobar!
        session.Introductions.AdjustActorDisposition(bridwellId, 55);
        DaggerfallIntroductionResult vouchToKing = session.Introductions.TryIssueIntroduction(bridwell, gothrydId, charing);
        Assert.True(vouchToKing.Accepted, vouchToKing.Message);
        Assert.Equal(ActorVouchKind.Court, vouchToKing.Vouch!.Kind);

        ActorVouchRecord kingVouchToMobar = new(
            gothrydId,
            baselineRuler.HolderFactionId ?? daggerfallSeat.OfficeFactionId,
            ActorVouchKind.Court,
            checked((int)session.Calendar.DayNumber),
            baselineRuler.DisplayName,
            charing.Name,
            baselineRuler.FamilyId);
        session.State.ActorLedger.RecordVouch(mobarId, kingVouchToMobar, ActorOrigin.Cast(mobarDef.Id, fixture.ExteriorProfile.ProfileKey));
        session.State.Actors.Get(mobarId).Actor.Get<ActorOverlayComponent>().RecordVouch(kingVouchToMobar);

        // 1. Kill King Gothryd -> opens 7-14d VacancyUntil window, raises SuccessionCrisis, emits proclamation!
        ActorLedgerEntry gothrydDead = session.State.ActorLedger.TryGet(gothrydId, out ActorLedgerEntry? priorGothryd) && priorGothryd is not null
            ? priorGothryd with { Location = ActorLocationOverride.Dead(fixture.ExteriorProfile.ProfileKey) }
            : new ActorLedgerEntry(gothrydId, ActorOrigin.Cast("king-gothryd"), Location: ActorLocationOverride.Dead(fixture.ExteriorProfile.ProfileKey));
        session.State.ActorLedger.Upsert(gothrydDead);
        DaggerfallOfficeOverrideSave vacancy = Assert.Single(session.OfficeLedger.ReactToDeath(
            gothrydId,
            checked((int)session.Calendar.DayNumber),
            charing,
            session.Calendar.MinuteNumber));
        Assert.True(vacancy.Vacant);
        Assert.True(session.WorldConditions.HasActive(DaggerfallRegion, DaggerfallWorldConditionKind.SuccessionCrisis));
        Assert.True(session.Introductions.IsCourtInVacancyOrCrisis(DaggerfallRegion, daggerfallSeat.OfficeFactionId));

        // Court access tightens (+1 requiredReach) so the same unintroduced player is now refused by MinorCourt attendant!
        DaggerfallIntroductionAccess crisisAttendantAccess = session.Introductions.AssessAccess(attendant);
        Assert.False(crisisAttendantAccess.Allowed);
        Assert.Equal(2, crisisAttendantAccess.RequiredReach);
        Assert.Contains("succession", crisisAttendantAccess.DenialReason!, StringComparison.OrdinalIgnoreCase);

        // Routine patron court introductions pause during active vacancy/crisis, while ordinary local Trade intros still work!
        Assert.DoesNotContain(session.Introductions.ListIntroductionTargets(bridwell, charing), t => t.VouchKind == ActorVouchKind.Court);
        DaggerfallIntroductionResult pausedIntro = session.Introductions.TryIssueIntroduction(bridwell, mobarId, charing);
        Assert.False(pausedIntro.Accepted);
        Assert.Contains("paused", pausedIntro.Message, StringComparison.OrdinalIgnoreCase);

        session.Introductions.AdjustActorDisposition(neighborId, 25);
        DaggerfallIntroductionResult tradeIntro = session.Introductions.TryIssueIntroduction(neighbor, merchantId, charing);
        Assert.True(tradeIntro.Accepted, tradeIntro.Message);

        // Court greeting, "Any news?" rumour, and ambient bark all acknowledge the vacant throne!
        string vacantGreeting = OpenGreetingAndClose(session, bridwellId);
        Assert.Contains("vacant", vacantGreeting, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("in the court of King Gothryd", vacantGreeting, StringComparison.Ordinal);

        string vacantNews = AskNewsAndClose(session, bridwellId);
        Assert.Contains("vacant", vacantNews, StringComparison.OrdinalIgnoreCase);

        string vacantBark = TriggerAmbientBark(session, minorAttendantId);
        Assert.Contains("vacant", vacantBark, StringComparison.OrdinalIgnoreCase);

        // 2. Declare interim regency -> greeting, news, and ambient bark acknowledge interim regent, while court intros stay paused!
        session.OfficeLedger.DeclareFactionRegency(
            daggerfallSeat.OfficeFactionId,
            bridwellId,
            bridwellDef.Id,
            bridwellDef.FactionId,
            bridwellDef.Name,
            DaggerfallOfficeChangeCause.Succession,
            checked((int)session.Calendar.DayNumber),
            currentMinute: session.Calendar.MinuteNumber);

        Assert.True(session.Introductions.IsCourtInVacancyOrCrisis(DaggerfallRegion, daggerfallSeat.OfficeFactionId));
        Assert.False(session.Introductions.TryIssueIntroduction(bridwell, mobarId, charing).Accepted);

        string regencyGreeting = OpenGreetingAndClose(session, minorAttendantId);
        Assert.Contains("Regent", regencyGreeting, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(bridwellDef.Name, regencyGreeting, StringComparison.Ordinal);

        string regencyBark = TriggerAmbientBark(session, minorAttendantId);
        Assert.Contains("regency", regencyBark, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(bridwellDef.Name, regencyBark, StringComparison.Ordinal);

        // 3. Advance calendar past VacancyUntilMinute -> automatic dynastic succession seats Queen Aubk-i!
        long minutesToExpiry = vacancy.VacancyUntilMinute!.Value - session.Calendar.MinuteNumber + 1;
        session.AdvanceElapsedTime(checked(minutesToExpiry * 60L));

        // OfficeHolders (quest binding / text macros) rebinds automatically to Queen Aubk-i!
        DaggerfallOfficeHolder newRuler = session.OfficeHolders.ResolveRegionalRuler(DaggerfallRegion)!;
        Assert.True(newRuler.HasLivingHolder);
        Assert.Equal("queen-aubk-i", newRuler.CastId);
        Assert.Equal("Queen Aubk-i", newRuler.DisplayName);
        Assert.Equal("Queen", newRuler.Title);
        long aubkiId = newRuler.ActorId!.Value;

        // Physical seating at Castle Daggerfall throne slot with zero duplicates!
        Assert.True(session.State.ActorLedger.TryGet(aubkiId, out ActorLedgerEntry? aubkiLedger));
        Assert.Equal(ActorLocationKind.AtSite, aubkiLedger!.Location?.Kind);
        Assert.Equal("cast:king-gothryd", aubkiLedger.Location!.TargetSlotId);

        // Dynastic continuity (shared fam:dynasty:daggerfall): Queen Aubk-i honors Bridwell's vouch to King Gothryd,
        // and King Gothryd's vouch on Mobar stays intact!
        IReadOnlyList<ActorVouchRecord> aubkiVouches = session.Introductions.ReadVouches(aubkiId);
        Assert.Contains(aubkiVouches, v => v.ReferrerDurableId == bridwellId && v.Kind == ActorVouchKind.Court);
        Assert.Contains(session.Introductions.ReadVouches(mobarId), v => v.ReferrerDurableId == gothrydId && v.Kind == ActorVouchKind.Court);

        // Court access and routine patron court introductions resume immediately after seating (even while Track B's
        // post-accession chronicle arc runs on the board!).
        Assert.False(session.Introductions.IsCourtInVacancyOrCrisis(DaggerfallRegion, daggerfallSeat.OfficeFactionId));
        DaggerfallIntroductionAccess resumedAttendantAccess = session.Introductions.AssessAccess(attendant);
        Assert.True(resumedAttendantAccess.Allowed);
        Assert.Equal(1, resumedAttendantAccess.RequiredReach);
        DaggerfallIntroductionResult resumedIntro = session.Introductions.TryIssueIntroduction(bridwell, mobarId, charing);
        Assert.True(resumedIntro.Accepted, resumedIntro.Message);

        // Court greeting, news, and ambient bark now reflect Queen Aubk-i dynamically!
        string successorGreeting = OpenGreetingAndClose(session, bridwellId);
        Assert.Contains("Queen Aubk-i", successorGreeting, StringComparison.Ordinal);

        string successorNews = AskNewsAndClose(session, bridwellId);
        Assert.Contains("Queen Aubk-i", successorNews, StringComparison.Ordinal);

        string successorBark = TriggerAmbientBark(session, minorAttendantId);
        Assert.Contains("Queen Aubk-i", successorBark, StringComparison.Ordinal);

        // 4. Save & reload preserves seated successor, quest rebinding, dynastic vouches, and live court dialogue!
        using DaggerfallSession restored = fixture.Restore(session.CaptureSave());
        DaggerfallOfficeHolder restoredRuler = restored.OfficeHolders.ResolveRegionalRuler(DaggerfallRegion)!;
        Assert.True(restoredRuler.HasLivingHolder);
        Assert.Equal("queen-aubk-i", restoredRuler.CastId);
        Assert.Contains(restored.Introductions.ReadVouches(aubkiId), v => v.ReferrerDurableId == bridwellId && v.Kind == ActorVouchKind.Court);
        Assert.Contains(restored.Introductions.ReadVouches(mobarId), v => v.ReferrerDurableId == gothrydId && v.Kind == ActorVouchKind.Court);
        Assert.False(restored.Introductions.IsCourtInVacancyOrCrisis(DaggerfallRegion, daggerfallSeat.OfficeFactionId));
        Assert.True(restored.State.ActorLedger.TryGet(aubkiId, out ActorLedgerEntry? restoredAubki));
        Assert.Equal("cast:king-gothryd", restoredAubki!.Location?.TargetSlotId);
    }

    [Fact]
    public void Dynastic_heir_refuses_vouch_carryover_after_attributed_dynasty_crime_and_outsider_turnover_invalidates_prior_court_favours()
    {
        using var fixture = SourceBackedGuildBankSessionFixture.Create();
        fixture.Random = RandomMinimum.Create();

        // Part A: Attributed crime/murder against fam:dynasty:daggerfall blocks dynastic vouch carryover to Queen Aubk-i.
        using (DaggerfallSession crimeSession = fixture.Start(fixture.ExteriorProfile))
        {
            crimeSession.AdvanceElapsedTime(8 * 60 * 60);
            DaggerfallSiteRecord charing = crimeSession.Site.ActiveSite!;
            DaggerfallBoardSeatBinding daggerfallSeat = TestPayload.Definitions.BoardSeats.All.Single(s => s.SeatId == "daggerfall");
            DaggerfallCastIndividualDefinition bridwellDef = TestPayload.Definitions.People.Individuals["lord-bridwell"];
            DaggerfallCastIndividualDefinition mobarDef = TestPayload.Definitions.People.Individuals["mobar"];
            DaggerfallCastIndividualDefinition aubkiDef = TestPayload.Definitions.People.Individuals["queen-aubk-i"];

            long bridwellId = MaterializeCastCourtActor(crimeSession, charing, bridwellDef);
            long mobarId = MaterializeCastCourtActor(crimeSession, charing, mobarDef);
            long aubkiId = MaterializeCastCourtActor(crimeSession, charing, aubkiDef);
            long gothrydId = DaggerfallPeopleSlots.CastIndividualIdentity("king-gothryd");

            // Prominent direct-standing threshold is disposition >= 40 (without double-counting household goodwill).
            crimeSession.Introductions.AdjustActorDisposition(bridwellId, 40);
            Assert.True(crimeSession.Introductions.TryIssueIntroduction(
                crimeSession.State.Npcs.Require(bridwellId), gothrydId, charing).Accepted);

            ActorVouchRecord gothrydVouch = new(gothrydId, daggerfallSeat.OfficeFactionId, ActorVouchKind.Court, 1, "King Gothryd", charing.Name, "fam:dynasty:daggerfall");
            crimeSession.State.ActorLedger.RecordVouch(mobarId, gothrydVouch, ActorOrigin.Cast(mobarDef.Id, fixture.ExteriorProfile.ProfileKey));
            crimeSession.State.Actors.Get(mobarId).Actor.Get<ActorOverlayComponent>().RecordVouch(gothrydVouch);

            // Attributed murder against fam:dynasty:daggerfall!
            DaggerfallCrimeIncidentSave incident = new(
                OperationId: "op-dynasty-kill",
                Crime: DaggerfallCrimeKind.Murder,
                Stage: DaggerfallCrimeStage.Completed,
                PerpetratorActorId: DaggerfallActorIdentity.PlayerEntityId,
                AffectedActorOrOwnerId: gothrydId,
                Region: DaggerfallRegion,
                GameMinute: crimeSession.Calendar.MinuteNumber,
                TargetKind: DaggerfallCrimeTargetKind.Civilian,
                Witnesses: new DaggerfallCrimeWitnessEvidence(DaggerfallCrimeWitnessQuery.CompletedWithWitnesses, [bridwellId]),
                GuildCredit: DaggerfallCrimeGuildCredit.CivilianMurder,
                Reported: true);
            crimeSession.SocialConsequences.ReactToAttributedCrime(incident, charing, crimeSession.Calendar.MinuteNumber, "fam:dynasty:daggerfall");
            // Advance a few days so province-wide household standing wavefront reaches every site.
            crimeSession.AdvanceElapsedTime(72 * 60 * 60);

            // Now seat dynastic heir Queen Aubk-i -> must NOT inherit King Gothryd's vouch, and King Gothryd's court vouch is invalidated!
            crimeSession.OfficeLedger.SetBoardSeatHolder(
                "daggerfall", aubkiId, "queen-aubk-i", aubkiDef.FactionId, aubkiDef.Name,
                DaggerfallOfficeChangeCause.Succession, checked((int)crimeSession.Calendar.DayNumber),
                currentMinute: crimeSession.Calendar.MinuteNumber);

            Assert.DoesNotContain(crimeSession.Introductions.ReadVouches(aubkiId), v => v.ReferrerDurableId == bridwellId);
            Assert.DoesNotContain(crimeSession.Introductions.ReadVouches(mobarId), v => v.ReferrerDurableId == gothrydId);
            DaggerfallIntroductionAccess access = crimeSession.Introductions.AssessAccess(crimeSession.State.Npcs.Require(aubkiId));
            Assert.False(access.Allowed);
        }

        // Part B: Unrelated outsider / rival turnover invalidates prior court favours and requires fresh vouching,
        // while preserving unrelated ordinary Trade vouches across save/load!
        using (DaggerfallSession rivalSession = fixture.Start(fixture.ExteriorProfile))
        {
            rivalSession.AdvanceElapsedTime(8 * 60 * 60);
            DaggerfallSiteRecord charing = rivalSession.Site.ActiveSite!;
            DaggerfallBoardSeatBinding daggerfallSeat = TestPayload.Definitions.BoardSeats.All.Single(s => s.SeatId == "daggerfall");
            DaggerfallCastIndividualDefinition bridwellDef = TestPayload.Definitions.People.Individuals["lord-bridwell"];
            DaggerfallCastIndividualDefinition mobarDef = TestPayload.Definitions.People.Individuals["mobar"];
            DaggerfallCastIndividualDefinition aubkiDef = TestPayload.Definitions.People.Individuals["queen-aubk-i"];

            long bridwellId = MaterializeCastCourtActor(rivalSession, charing, bridwellDef);
            long mobarId = MaterializeCastCourtActor(rivalSession, charing, mobarDef);
            long gothrydId = DaggerfallPeopleSlots.CastIndividualIdentity("king-gothryd");
            long aubkiId = DaggerfallPeopleSlots.CastIndividualIdentity("queen-aubk-i");
            long outsiderId = AddCourtNpc(rivalSession, charing, "rival/usurper", "Lord Corin Blackthorne", factionId: daggerfallSeat.OfficeFactionId, ["talk"], familyId: "fam:house:17:99");
            long merchantId = AddCourtNpc(rivalSession, charing, "market/smith", "Haldor Smith", factionId: 0, ["talk", "shop"]);
            long neighborId = AddCourtNpc(rivalSession, charing, "market/friend", "Sera Miller", factionId: 0, ["talk"]);

            SetSocialStats(rivalSession, etiquette: 20, streetwise: 20, personality: 30);

            // Grant Court vouch to King Gothryd (Prominent direct-standing threshold is disposition >= 40),
            // Court vouches FROM King Gothryd and Queen Aubk-i on Mobar,
            // AND an unrelated ordinary Trade vouch on merchantId.
            rivalSession.Introductions.AdjustActorDisposition(bridwellId, 40);
            Assert.True(rivalSession.Introductions.TryIssueIntroduction(
                rivalSession.State.Npcs.Require(bridwellId), gothrydId, charing).Accepted);

            ActorVouchRecord gothrydVouch = new(gothrydId, daggerfallSeat.OfficeFactionId, ActorVouchKind.Court, 1, "King Gothryd", charing.Name, "fam:dynasty:daggerfall");
            ActorVouchRecord aubkiVouch = new(aubkiId, aubkiDef.FactionId, ActorVouchKind.Court, 1, "Queen Aubk-i", charing.Name, "fam:dynasty:daggerfall");
            rivalSession.State.ActorLedger.RecordVouch(mobarId, gothrydVouch, ActorOrigin.Cast(mobarDef.Id, fixture.ExteriorProfile.ProfileKey));
            rivalSession.State.ActorLedger.RecordVouch(mobarId, aubkiVouch, ActorOrigin.Cast(mobarDef.Id, fixture.ExteriorProfile.ProfileKey));
            ActorOverlayComponent mobarOverlay = rivalSession.State.Actors.Get(mobarId).Actor.Get<ActorOverlayComponent>();
            mobarOverlay.RecordVouch(gothrydVouch);
            mobarOverlay.RecordVouch(aubkiVouch);

            rivalSession.Introductions.AdjustActorDisposition(neighborId, 25);
            Assert.True(rivalSession.Introductions.TryIssueIntroduction(
                rivalSession.State.Npcs.Require(neighborId), merchantId, charing).Accepted);

            // Seat unrelated outsider/rival onto Daggerfall's throne!
            rivalSession.OfficeLedger.SetFactionOfficeHolder(
                daggerfallSeat.OfficeFactionId,
                DaggerfallOfficeKind.Ruler,
                outsiderId,
                holderCastId: null,
                holderFactionId: daggerfallSeat.OfficeFactionId,
                holderDisplayName: "Lord Corin Blackthorne",
                cause: DaggerfallOfficeChangeCause.CardMove,
                changedDay: checked((int)rivalSession.Calendar.DayNumber),
                causeDetail: "seized the throne",
                title: "King",
                currentMinute: rivalSession.Calendar.MinuteNumber);

            // Outsider ruler does NOT inherit King Gothryd's vouch and refuses unintroduced access!
            Assert.Empty(rivalSession.Introductions.ReadVouches(outsiderId));
            Assert.False(rivalSession.Introductions.AssessAccess(rivalSession.State.Npcs.Require(outsiderId)).Allowed);

            // Prior court favours from King Gothryd AND the ousted dynasty (Queen Aubk-i) are invalidated in ledger & live overlay!
            Assert.Empty(rivalSession.Introductions.ReadVouches(mobarId));
            Assert.Empty(mobarOverlay.Vouches);

            // Unrelated ordinary Trade vouch survives intact!
            Assert.Contains(rivalSession.Introductions.ReadVouches(merchantId), v => v.ReferrerDurableId == neighborId && v.Kind == ActorVouchKind.Trade);

            // Invalidated court favours stay gone across save/load!
            using DaggerfallSession restoredRival = fixture.Restore(rivalSession.CaptureSave());
            Assert.Empty(restoredRival.Introductions.ReadVouches(outsiderId));
            Assert.Empty(restoredRival.Introductions.ReadVouches(mobarId));
            Assert.Contains(restoredRival.Introductions.ReadVouches(merchantId), v => v.ReferrerDurableId == neighborId && v.Kind == ActorVouchKind.Trade);
        }
    }

    [Fact]
    public void Guildhall_greetings_rumours_and_ambient_barks_reflect_local_hall_master_and_major_seat_vacancy_regency_and_seating()
    {
        using var fixture = SourceBackedGuildBankSessionFixture.Create();
        fixture.Random = RandomMinimum.Create();
        using DaggerfallSession session = fixture.Start(fixture.MagesProfile);
        session.AdvanceElapsedTime(8 * 60 * 60);
        DaggerfallSiteRecord charing = session.Site.ActiveSite!;

        DaggerfallLocalOffice magesMaster = session.OfficeLedger.ResolveLocalOffices(charing)
            .Single(o => o.Kind == DaggerfallOfficeKind.GuildHallMaster && o.ProfileKey == fixture.MagesProfile.ProfileKey);
        long originalMasterId = magesMaster.HolderActorId!.Value;
        string originalMasterName = magesMaster.DisplayName;

        // Pick a non-master talkable member inside Charing's Mages Guild hall.
        DaggerfallNpc guildClerk = session.State.Npcs.All.First(npc =>
            npc.DurableId != originalMasterId
            && npc.Profile == fixture.MagesProfile.ProfileKey
            && npc.Services.Contains("talk", StringComparer.Ordinal));

        // 1. Baseline guildhall greeting reflects the live seeded local Guild Hall Master name/title!
        string baselineGreeting = OpenGreetingAndClose(session, guildClerk.DurableId);
        Assert.Contains(originalMasterName, baselineGreeting, StringComparison.Ordinal);

        // 2. Depose the local Guild Hall Master -> greeting, "Any news?" rumour, and ambient bark acknowledge vacancy!
        DaggerfallOfficeOverrideSave vacancy = session.OfficeLedger.DeposeLocalOffice(
            magesMaster,
            DaggerfallOfficeChangeCause.Quest,
            checked((int)session.Calendar.DayNumber),
            causeDetail: "removed by guild inquiry",
            currentMinute: session.Calendar.MinuteNumber);

        string vacantGreeting = OpenGreetingAndClose(session, guildClerk.DurableId);
        Assert.Contains("vacant", vacantGreeting, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(magesMaster.Title, vacantGreeting, StringComparison.Ordinal);

        string vacantNews = AskNewsAndClose(session, guildClerk.DurableId);
        Assert.Contains("vacant", vacantNews, StringComparison.OrdinalIgnoreCase);

        string vacantBark = TriggerAmbientBark(session, guildClerk.DurableId);
        Assert.Contains(magesMaster.Title, vacantBark, StringComparison.Ordinal);
        Assert.Contains("successor", vacantBark, StringComparison.OrdinalIgnoreCase);

        // 3. Advance past the 24-48h local vacancy window -> new Hall Master is physically seated and named in dialogue!
        long minutesToExpiry = vacancy.VacancyUntilMinute!.Value - session.Calendar.MinuteNumber + 1;
        session.AdvanceElapsedTime(checked(minutesToExpiry * 60L));

        DaggerfallLocalOffice seatedLocal = session.OfficeLedger.ResolveLocalOffices(charing)
            .Single(o => o.OfficeKey == magesMaster.OfficeKey);
        Assert.False(seatedLocal.IsVacant);
        Assert.NotEqual(originalMasterId, seatedLocal.HolderActorId);

        // Pick a non-master speaker (in case guildClerk itself was promoted to Hall Master!).
        DaggerfallNpc speakerAfterLocal = session.State.Npcs.All.First(npc =>
            npc.DurableId != seatedLocal.HolderActorId!.Value
            && npc.Profile == fixture.MagesProfile.ProfileKey
            && npc.Services.Contains("talk", StringComparer.Ordinal));
        string seatedGreeting = OpenGreetingAndClose(session, speakerAfterLocal.DurableId);
        Assert.Contains(seatedLocal.DisplayName, seatedGreeting, StringComparison.Ordinal);

        string seatedBark = TriggerAmbientBark(session, speakerAfterLocal.DurableId);
        Assert.Contains(seatedLocal.DisplayName, seatedBark, StringComparison.Ordinal);

        // 4. Overarching Mages Guild MajorSeat vacancy -> interim regency -> newly seated leader reflected in hall dialogue!
        DaggerfallBoardSeatBinding magesSeat = TestPayload.Definitions.BoardSeats.All.Single(s => s.SeatId == "mages-guild");
        session.OfficeLedger.DeposeBoardSeat(
            "mages-guild",
            DaggerfallOfficeChangeCause.CardMove,
            checked((int)session.Calendar.DayNumber),
            causeDetail: "stepped down in conclave",
            currentMinute: session.Calendar.MinuteNumber);

        string seatVacantGreeting = OpenGreetingAndClose(session, speakerAfterLocal.DurableId);
        Assert.Contains("Mages Guild", seatVacantGreeting, StringComparison.Ordinal);
        Assert.Contains("vacant", seatVacantGreeting, StringComparison.OrdinalIgnoreCase);

        session.OfficeLedger.DeclareFactionRegency(
            magesSeat.OfficeFactionId,
            regentActorId: null,
            regentCastId: null,
            regentFactionId: magesSeat.OfficeFactionId,
            regentDisplayName: "Arch-Steward Orin",
            cause: DaggerfallOfficeChangeCause.Succession,
            changedDay: checked((int)session.Calendar.DayNumber),
            currentMinute: session.Calendar.MinuteNumber);

        string seatRegencyGreeting = OpenGreetingAndClose(session, speakerAfterLocal.DurableId);
        Assert.Contains("Arch-Steward Orin", seatRegencyGreeting, StringComparison.Ordinal);
        Assert.Contains("regent", seatRegencyGreeting, StringComparison.OrdinalIgnoreCase);

        session.OfficeLedger.SetBoardSeatHolder(
            "mages-guild",
            holderActorId: null,
            holderCastId: null,
            holderFactionId: magesSeat.OfficeFactionId,
            holderDisplayName: "Archmage Selene",
            cause: DaggerfallOfficeChangeCause.Succession,
            changedDay: checked((int)session.Calendar.DayNumber),
            currentMinute: session.Calendar.MinuteNumber);

        string seatSeatedGreeting = OpenGreetingAndClose(session, speakerAfterLocal.DurableId);
        Assert.Contains("Archmage Selene", seatSeatedGreeting, StringComparison.Ordinal);
    }

    private static long MaterializeCastCourtActor(
        DaggerfallSession session,
        DaggerfallSiteRecord site,
        DaggerfallCastIndividualDefinition cast)
    {
        DaggerfallWorldProfileKey profile = session.Sites.ActiveProfile;
        long id = DaggerfallPeopleSlots.CastIndividualIdentity(cast.Id);
        DaggerfallNpcSite npcSite = new(site.Id.Region, site.Name, string.Empty, profile.LogicalId);
        DaggerfallNpcAppearance look = new(cast.Race ?? "breton", cast.Gender ?? "Male", cast.Archive, cast.Record, Math.Abs(cast.Id.GetHashCode()), cast.FactionId);
        session.State.Npcs.RegisterDeterministic(
            id, DaggerfallNpcKind.Static, cast.Id, npcSite, look, "noble", ["talk", "quests"],
            displayName: cast.Name, homeProfile: profile, familyId: cast.FamilyId);
        DaggerfallNpc npc = session.State.Npcs.Require(id);
        WorldPoint pose = new(2, 0, -2);
        session.Roster.MaterializeDeterministicCivilian(
            npc,
            ActorOrigin.Cast(cast.Id, profile),
            pose,
            new ActorPose(pose, 0f),
            Vector3.Zero,
            cast.FamilyId);
        session.State.Npcs.Bind(id, profile);
        return id;
    }

    private static long AddCourtNpc(
        DaggerfallSession session,
        DaggerfallSiteRecord site,
        string slotId,
        string displayName,
        int factionId,
        string[] services,
        string? familyId = null)
    {
        DaggerfallWorldProfileKey profile = session.Sites.ActiveProfile;
        string slotKey = $"{profile.LogicalId}/{slotId}";
        long id = DaggerfallPeopleSlots.SeededSlotIdentity(site.Id, slotKey);
        DaggerfallNpcSite npcSite = new(site.Id.Region, site.Name, string.Empty, profile.LogicalId);
        DaggerfallNpcAppearance look = new("breton", "Male", 182, 0, Math.Abs(slotId.GetHashCode()), factionId);
        session.State.Npcs.RegisterDeterministic(
            id, DaggerfallNpcKind.Static, slotId, npcSite, look, "person", services,
            displayName: displayName, homeProfile: profile, familyId: familyId);
        DaggerfallNpc npc = session.State.Npcs.Require(id);
        WorldPoint pose = new(3, 0, -2);
        session.Roster.MaterializeDeterministicCivilian(
            npc,
            ActorOrigin.Seeded(profile, slotId),
            pose,
            new ActorPose(pose, 0f),
            Vector3.Zero,
            familyId);
        session.State.Npcs.Bind(id, profile);
        return id;
    }

    private static void SetSocialStats(DaggerfallSession session, int etiquette, int streetwise, int personality)
    {
        StatsComponent stats = session.State.Actors.Player.Stats;
        stats.GetStat(StatId.Parse("etiquette")).BaseValue = etiquette;
        stats.GetStat(StatId.Parse("streetwise")).BaseValue = streetwise;
        stats.GetStat(StatId.Parse(DaggerfallMechanicsIds.Personality.Value)).BaseValue = personality;
    }

    private static string OpenGreetingAndClose(DaggerfallSession session, long npcId)
    {
        ActorInteractionPolicy policy = session.State.Actors.Get(npcId).Actor.Get<ActorInteractionPolicy>();
        ActorInteractionPolicyKind prior = policy.Policy;
        policy.Set(ActorInteractionPolicyKind.FullDialogue);
        try
        {
            DaggerfallActivationOutcome opened = ActivateNpc(session, npcId);
            Assert.True(opened.Applied, opened.Message);
            DaggerfallDialogueView view = Assert.IsType<DaggerfallDialogueView>(session.ActivationPanel.View.Dialogue);
            string greeting = view.Greeting;
            session.Dialogue.Close();
            return greeting;
        }
        finally
        {
            policy.Set(prior);
        }
    }

    private static string AskNewsAndClose(DaggerfallSession session, long npcId)
    {
        ActorInteractionPolicy policy = session.State.Actors.Get(npcId).Actor.Get<ActorInteractionPolicy>();
        ActorInteractionPolicyKind prior = policy.Policy;
        policy.Set(ActorInteractionPolicyKind.FullDialogue);
        try
        {
            DaggerfallActivationOutcome opened = ActivateNpc(session, npcId);
            Assert.True(opened.Applied, opened.Message);
            DaggerfallDialogueView view = Assert.IsType<DaggerfallDialogueView>(session.ActivationPanel.View.Dialogue);
            DaggerfallActivationOutcome reply = session.Dialogue.Ask(view.Revision, "news", null, null);
            Assert.True(reply.Applied, reply.Message);
            session.Dialogue.Close();
            return reply.Message;
        }
        finally
        {
            policy.Set(prior);
        }
    }

    private static string TriggerAmbientBark(DaggerfallSession session, long npcId)
    {
        ActorInteractionPolicy policy = session.State.Actors.Get(npcId).Actor.Get<ActorInteractionPolicy>();
        ActorInteractionPolicyKind prior = policy.Policy;
        policy.Set(ActorInteractionPolicyKind.AmbientBark);
        try
        {
            DaggerfallActivationOutcome bark = ActivateNpc(session, npcId);
            Assert.True(bark.Applied, bark.Message);
            return bark.Message;
        }
        finally
        {
            policy.Set(prior);
        }
    }

    private static DaggerfallActivationOutcome ActivateNpc(DaggerfallSession session, long npcId)
    {
        DaggerfallActivationTarget target = Assert.Single(
            session.Dialogue.NpcTargets(),
            t => checked((long)t.Identity.Value) == npcId);
        return session.Dialogue.ActivateNpc(new DaggerfallActivationSelection(DaggerfallActivationMode.Talk, target));
    }
}
