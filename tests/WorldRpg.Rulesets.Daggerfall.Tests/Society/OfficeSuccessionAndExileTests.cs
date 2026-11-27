using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Politics;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Live office continuity slice 2 (#11106): deterministic/dynastic successor selection across all 3 tiers when
/// <c>VacancyUntil</c> expires, physical slot seating with updated role/title and zero duplicate bodies,
/// deterministic exile relocation for non-lethally deposed living holders, and B2 <see cref="HolderChanged"/> emission.
/// </summary>
public sealed class OfficeSuccessionAndExileTests
{
    private const int DaggerfallRegion = 17;

    [Fact]
    public void Dynastic_succession_prefers_cast_kin_matching_FamilyId_falls_back_to_deputy_and_emits_B2_HolderChanged()
    {
        using var fixture = SourceBackedGuildBankSessionFixture.Create();
        using DaggerfallSession session = fixture.Start(fixture.ExteriorProfile);
        DaggerfallSiteRecord charing = session.Site.ActiveSite!;
        Assert.NotNull(session.PoliticalBoard);

        // 1. Kill King Gothryd (fam:dynasty:daggerfall) -> opens 7-14d VacancyUntil window and notifies PoliticalBoard.
        long gothrydId = DaggerfallPeopleSlots.CastIndividualIdentity("king-gothryd");
        session.State.ActorLedger.Upsert(new ActorLedgerEntry(
            gothrydId,
            ActorOrigin.Cast("king-gothryd"),
            Location: ActorLocationOverride.Dead(fixture.ExteriorProfile.ProfileKey)));
        DaggerfallOfficeOverrideSave vacancy = Assert.Single(session.OfficeLedger.ReactToDeath(
            gothrydId,
            checked((int)session.Calendar.DayNumber),
            charing,
            session.Calendar.MinuteNumber));
        Assert.True(vacancy.Vacant);
        Assert.True(session.WorldConditions.HasActive(DaggerfallRegion, DaggerfallWorldConditionKind.SuccessionCrisis));
        Assert.Contains(session.PoliticalBoard!.Board.Queued,
            n => n is HolderChanged hc && hc.Seat == "daggerfall" && hc.Holder == "vacant:daggerfall");

        // 2. Advance elapsed calendar time past VacancyUntilMinute -> automatic dynastic succession seats Queen Aubk-i!
        long minutesToExpiry = vacancy.VacancyUntilMinute!.Value - session.Calendar.MinuteNumber + 1;
        session.AdvanceElapsedTime(checked(minutesToExpiry * 60L));

        DaggerfallSeatLeaderPersona seated = session.OfficeLedger.ResolveBoardSeatPersona("daggerfall");
        Assert.False(seated.IsVacant);
        Assert.Null(seated.VacancyUntilMinute);
        Assert.Equal("queen-aubk-i", seated.HolderCastId);
        Assert.Equal("Queen Aubk-i", seated.DisplayName);
        Assert.Equal("Queen", seated.Title);
        Assert.Equal("King Gothryd", seated.PreviousHolderName);
        // Our office vacancy SuccessionCrisis token is cleared on seating (while Track B's 13-week post-accession chronicle arc runs on the board).
        Assert.DoesNotContain(session.WorldConditions.AuthoredTokens,
            t => t.TokenId == DaggerfallWorldConditions.OfficeSuccessionCrisisTokenId(vacancy.OfficeKey));

        // Queen Aubk-i is physically seated at Castle Daggerfall's throne slot via ONE sparse ActorLedger entry!
        long aubkiId = DaggerfallPeopleSlots.CastIndividualIdentity("queen-aubk-i");
        Assert.True(session.State.ActorLedger.TryGet(aubkiId, out ActorLedgerEntry? aubkiEntry));
        Assert.Equal(ActorLocationKind.AtSite, aubkiEntry!.Location?.Kind);
        Assert.Equal("cast:king-gothryd", aubkiEntry.Location!.TargetSlotId);
        Assert.Equal("Queen", aubkiEntry.Social?.Role);

        // Track B PoliticalBoard receives HolderChanged("daggerfall", "queen-aubk-i") and applies it on the next Sunday!
        session.AdvanceElapsedTime(7L * 24 * 60 * 60);
        Assert.Equal("queen-aubk-i", session.PoliticalBoard.Board.Seat("daggerfall").Holder);

        // 3. Kill Queen Aubk-i too -> dynastic succession walks to the next living fam:dynasty:daggerfall kin (Mynisera)!
        session.State.ActorLedger.Upsert(aubkiEntry with { Location = ActorLocationOverride.Dead(fixture.ExteriorProfile.ProfileKey) });
        DaggerfallOfficeOverrideSave secondVacancy = Assert.Single(session.OfficeLedger.ReactToDeath(
            aubkiId,
            checked((int)session.Calendar.DayNumber),
            charing,
            session.Calendar.MinuteNumber));
        Assert.True(secondVacancy.Vacant);
        session.AdvanceElapsedTime(checked((secondVacancy.VacancyUntilMinute!.Value - session.Calendar.MinuteNumber + 1) * 60L));

        DaggerfallSeatLeaderPersona nextHeir = session.OfficeLedger.ResolveBoardSeatPersona("daggerfall");
        Assert.False(nextHeir.IsVacant);
        Assert.Equal("mynisera", nextHeir.HolderCastId);
        Assert.Equal("Queen", nextHeir.Title);
        session.AdvanceElapsedTime(7L * 24 * 60 * 60);
        Assert.Equal("mynisera", session.PoliticalBoard.Board.Seat("daggerfall").Holder);

        // 4. Non-dynastic MajorSeat (fighters-guild: Oswin Harrowgate) dies -> falls back to promoted town-* SeededSlot deputy!
        long oswinId = DaggerfallPeopleSlots.CastIndividualIdentity("oswin-harrowgate");
        session.State.ActorLedger.Upsert(new ActorLedgerEntry(
            oswinId,
            ActorOrigin.Cast("oswin-harrowgate"),
            Location: ActorLocationOverride.Dead(fixture.ExteriorProfile.ProfileKey)));
        DaggerfallOfficeOverrideSave guildVacancy = Assert.Single(session.OfficeLedger.ReactToDeath(
            oswinId,
            checked((int)session.Calendar.DayNumber),
            charing,
            session.Calendar.MinuteNumber));
        Assert.True(guildVacancy.Vacant);
        session.AdvanceElapsedTime(checked((guildVacancy.VacancyUntilMinute!.Value - session.Calendar.MinuteNumber + 1) * 60L));

        DaggerfallSeatLeaderPersona guildSuccessor = session.OfficeLedger.ResolveBoardSeatPersona("fighters-guild");
        Assert.False(guildSuccessor.IsVacant);
        Assert.Null(guildSuccessor.HolderCastId);
        Assert.NotNull(guildSuccessor.HolderActorId);
        Assert.NotEqual(oswinId, guildSuccessor.HolderActorId);
        Assert.True(session.State.ActorLedger.TryGet(guildSuccessor.HolderActorId!.Value, out ActorLedgerEntry? deputyEntry));
        Assert.Equal(ActorOriginKind.SeededSlot, deputyEntry!.Origin.Kind);
        Assert.Equal(ActorLocationKind.AtSite, deputyEntry.Location?.Kind);
        Assert.True(session.State.ActorLedger.IsHomeSlotSuppressed(deputyEntry.DurableId, deputyEntry.Origin.SiteProfile!.Require()));

        // B2 PoliticalBoard hears the promoted deputy via HolderChanged("fighters-guild", "actor:<id>")!
        // Depending on whether the vacancy expired on a Sunday, it is either queued for the next Sunday or already applied.
        string expectedBoardHolder = $"actor:{guildSuccessor.HolderActorId.Value}";
        bool queuedOrApplied = session.PoliticalBoard.Board.Seat("fighters-guild").Holder == expectedBoardHolder
            || session.PoliticalBoard.Board.Queued.Any(n => n is HolderChanged hc && hc.Seat == "fighters-guild" && hc.Holder == expectedBoardHolder);
        Assert.True(queuedOrApplied, $"Expected HolderChanged('fighters-guild', '{expectedBoardHolder}') to be queued or applied on PoliticalBoard.");
        session.AdvanceElapsedTime(7L * 24 * 60 * 60);
        Assert.Equal(expectedBoardHolder, session.PoliticalBoard.Board.Seat("fighters-guild").Holder);
    }

    [Fact]
    public void Physical_local_successor_seating_relocates_deputy_updates_role_and_guarantees_zero_duplicate_bodies_across_save_load()
    {
        using var fixture = SourceBackedGuildBankSessionFixture.Create();
        using DaggerfallSession session = fixture.Start(fixture.ExteriorProfile);
        session.AdvanceElapsedTime(8 * 60 * 60);
        DaggerfallSiteRecord charing = session.Site.ActiveSite!;

        DaggerfallLocalOffice magesMaster = session.OfficeLedger.ResolveLocalOffices(charing)
            .Single(o => o.Kind == DaggerfallOfficeKind.GuildHallMaster && o.ProfileKey == fixture.MagesProfile.ProfileKey);
        long originalMasterId = magesMaster.HolderActorId!.Value;
        DaggerfallStaticNpcPlacement masterSlotPlacement = fixture.MagesProfile.StaticNpcs
            .Single(s => StringComparer.Ordinal.Equals(s.Id, magesMaster.SlotId));

        // 1. Depose the Mages Guild Hall Master -> seat stands visibly empty during vacancy.
        DaggerfallOfficeOverrideSave vacancy = session.OfficeLedger.DeposeLocalOffice(
            magesMaster,
            DaggerfallOfficeChangeCause.Quest,
            checked((int)session.Calendar.DayNumber),
            causeDetail: "removed by guild inquiry",
            currentMinute: session.Calendar.MinuteNumber);
        Assert.True(session.TryTransitionTo(fixture.MagesProfile.ProfileKey));
        Assert.False(session.State.Actors.TryGet(originalMasterId, out _));

        // 2. Advance past the 24-48h local vacancy window while standing INSIDE the Mages Guild!
        long minutesToExpiry = vacancy.VacancyUntilMinute!.Value - session.Calendar.MinuteNumber + 1;
        session.AdvanceElapsedTime(checked(minutesToExpiry * 60L));

        DaggerfallLocalOffice seated = session.OfficeLedger.ResolveLocalOffices(charing)
            .Single(o => o.OfficeKey == magesMaster.OfficeKey);
        Assert.False(seated.IsVacant);
        Assert.NotNull(seated.HolderActorId);
        long deputyId = seated.HolderActorId!.Value;
        Assert.NotEqual(originalMasterId, deputyId);

        // Deputy is physically seated ONCE at the vacated Hall Master slot (zero duplicate bodies!) with updated Role!
        ActorState liveDeputy = Assert.Single(session.State.Actors.All, a => a.DurableId == deputyId);
        Assert.InRange(MathF.Abs(liveDeputy.Position.X - masterSlotPlacement.Position.X), 0f, 0.05f);
        Assert.InRange(MathF.Abs(liveDeputy.Position.Z - masterSlotPlacement.Position.Z), 0f, 0.05f);
        DaggerfallNpc deputyNpc = session.State.Npcs.Require(deputyId);
        Assert.Equal(magesMaster.Title, deputyNpc.Role);
        Assert.True(session.State.ActorLedger.IsHomeSlotSuppressed(deputyId, fixture.MagesProfile.ProfileKey));
        Assert.True(session.State.ActorLedger.TryGet(deputyId, out ActorLedgerEntry? deputyLedger));
        Assert.Equal(magesMaster.SlotId, deputyLedger!.Location?.TargetSlotId);

        // 3. Cross out to exterior and back into Mages Guild -> STILL zero duplicates and still seated at person/0!
        Assert.True(session.TryTransitionTo(fixture.ExteriorProfile.ProfileKey));
        Assert.False(session.State.Actors.TryGet(deputyId, out _));
        Assert.True(session.TryTransitionTo(fixture.MagesProfile.ProfileKey));
        ActorState reenteredDeputy = Assert.Single(session.State.Actors.All, a => a.DurableId == deputyId);
        Assert.InRange(MathF.Abs(reenteredDeputy.Position.X - masterSlotPlacement.Position.X), 0f, 0.05f);
        Assert.InRange(MathF.Abs(reenteredDeputy.Position.Z - masterSlotPlacement.Position.Z), 0f, 0.05f);
        Assert.Equal(magesMaster.Title, session.State.Npcs.Require(deputyId).Role);

        // 4. Save & reload preserves physical seating, role, home suppression, and zero duplicates!
        using DaggerfallSession restored = fixture.Restore(session.CaptureSave());
        ActorState restoredDeputy = Assert.Single(restored.State.Actors.All, a => a.DurableId == deputyId);
        Assert.InRange(MathF.Abs(restoredDeputy.Position.X - masterSlotPlacement.Position.X), 0f, 0.05f);
        Assert.InRange(MathF.Abs(restoredDeputy.Position.Z - masterSlotPlacement.Position.Z), 0f, 0.05f);
        Assert.Equal(magesMaster.Title, restored.State.Npcs.Require(deputyId).Role);
        Assert.False(restored.State.Actors.TryGet(originalMasterId, out _));
    }

    [Fact]
    public void Non_lethal_deposition_relocates_living_holder_to_deterministic_exile_destination_across_save_load()
    {
        using var fixture = SourceBackedGuildBankSessionFixture.Create();
        using DaggerfallSession session = fixture.Start(fixture.ExteriorProfile);
        session.AdvanceElapsedTime(8 * 60 * 60);
        DaggerfallSiteRecord charing = session.Site.ActiveSite!;

        // 1. Depose King Gothryd non-lethally -> deterministic (seed, office) exile destination in a nearby friendly court/tavern!
        DaggerfallOfficeChangedNotification? rulerEvent = null;
        session.OfficeLedger.OfficeChanged += n =>
        {
            if (n.SeatId == "daggerfall")
                rulerEvent = n;
        };
        DaggerfallOfficeOverrideSave deposedRuler = session.OfficeLedger.DeposeBoardSeat(
            seatId: "daggerfall",
            cause: DaggerfallOfficeChangeCause.CardMove,
            changedDay: checked((int)session.Calendar.DayNumber),
            causeDetail: "driven into exile",
            currentMinute: session.Calendar.MinuteNumber);

        Assert.NotNull(rulerEvent);
        Assert.NotNull(rulerEvent!.ExileProfileKey);
        Assert.NotEqual(rulerEvent.OfficeProfileKey, rulerEvent.ExileProfileKey);
        DaggerfallWorldProfileKey expectedExile = session.OfficeLedger.ResolveExileDestinationFor(
            deposedRuler.OfficeKey,
            deposedRuler.Kind,
            deposedRuler.OfficeRegion,
            deposedRuler.OfficeSiteId,
            rulerEvent.OfficeProfileKey)!.Value;
        Assert.Equal(expectedExile, rulerEvent.ExileProfileKey!.Value);

        // One sparse ActorLedger AtSite override places King Gothryd at his exile destination (NOT OffStage!).
        long gothrydId = DaggerfallPeopleSlots.CastIndividualIdentity("king-gothryd");
        Assert.True(session.State.ActorLedger.TryGet(gothrydId, out ActorLedgerEntry? gothrydEntry));
        Assert.Equal(ActorLocationKind.AtSite, gothrydEntry!.Location?.Kind);
        Assert.Equal(expectedExile, gothrydEntry.Location!.Profile!.Require());
        Assert.True(session.State.ActorLedger.IsHomeSlotSuppressed(gothrydId, rulerEvent.OfficeProfileKey!.Value));

        // 2. Depose Charing's local Mages Guild Hall Master non-lethally -> exiled to deterministic tavern, NOT OffStage!
        DaggerfallLocalOffice magesMaster = session.OfficeLedger.ResolveLocalOffices(charing)
            .Single(o => o.Kind == DaggerfallOfficeKind.GuildHallMaster && o.ProfileKey == fixture.MagesProfile.ProfileKey);
        long masterId = magesMaster.HolderActorId!.Value;
        DaggerfallOfficeOverrideSave deposedMaster = session.OfficeLedger.DeposeLocalOffice(
            magesMaster,
            DaggerfallOfficeChangeCause.CardMove,
            checked((int)session.Calendar.DayNumber),
            causeDetail: "banished from the guildhall",
            currentMinute: session.Calendar.MinuteNumber);

        DaggerfallWorldProfileKey expectedLocalExile = session.OfficeLedger.ResolveExileDestinationFor(
            deposedMaster.OfficeKey,
            deposedMaster.Kind,
            deposedMaster.OfficeRegion,
            deposedMaster.OfficeSiteId,
            fixture.MagesProfile.ProfileKey)!.Value;
        Assert.NotEqual(fixture.MagesProfile.ProfileKey, expectedLocalExile);
        Assert.True(session.State.ActorLedger.TryGet(masterId, out ActorLedgerEntry? masterExileEntry));
        Assert.Equal(ActorLocationKind.AtSite, masterExileEntry!.Location?.Kind);
        Assert.Equal(expectedLocalExile, masterExileEntry.Location!.Profile!.Require());
        Assert.NotNull(masterExileEntry.Social);
        Assert.True(session.State.ActorLedger.IsHomeSlotSuppressed(masterId, fixture.MagesProfile.ProfileKey));

        // 3. Exile overrides round-trip cleanly across save/load and keep vacated slots suppressed.
        using DaggerfallSession restored = fixture.Restore(session.CaptureSave());
        Assert.True(restored.State.ActorLedger.TryGet(gothrydId, out ActorLedgerEntry? restoredGothryd));
        Assert.Equal(ActorLocationKind.AtSite, restoredGothryd!.Location?.Kind);
        Assert.Equal(expectedExile, restoredGothryd.Location!.Profile!.Require());
        Assert.True(restored.State.ActorLedger.TryGet(masterId, out ActorLedgerEntry? restoredMaster));
        Assert.Equal(ActorLocationKind.AtSite, restoredMaster!.Location?.Kind);
        Assert.Equal(expectedLocalExile, restoredMaster.Location!.Profile!.Require());
    }

    [Fact]
    public void Great_house_elevation_and_watch_captain_promotion_seat_physically_without_duplicates()
    {
        using var fixture = SourceBackedGuildBankSessionFixture.Create();
        using DaggerfallSession session = fixture.Start(fixture.ExteriorProfile);
        session.AdvanceElapsedTime(8 * 60 * 60);
        DaggerfallSiteRecord charing = session.Site.ActiveSite!;

        // 1. Wipe out every cast candidate in Daggerfall's court (201) -> Ruler succession elevates a Tier 2 Great House CourtFactor
        //    and cleanly opens a non-exile vacancy on their former CourtFactor post!
        foreach (string castId in new[] { "queen-aubk-i", "mynisera", "mobar", "lord-bridwell", "lady-northbridge" })
        {
            long id = DaggerfallPeopleSlots.CastIndividualIdentity(castId);
            session.State.ActorLedger.Upsert(new ActorLedgerEntry(
                id,
                ActorOrigin.Cast(castId),
                Location: ActorLocationOverride.Dead(fixture.ExteriorProfile.ProfileKey)));
        }
        long gothrydId = DaggerfallPeopleSlots.CastIndividualIdentity("king-gothryd");
        session.State.ActorLedger.Upsert(new ActorLedgerEntry(
            gothrydId,
            ActorOrigin.Cast("king-gothryd"),
            Location: ActorLocationOverride.Dead(fixture.ExteriorProfile.ProfileKey)));
        DaggerfallOfficeOverrideSave throneVacancy = Assert.Single(session.OfficeLedger.ReactToDeath(
            gothrydId,
            checked((int)session.Calendar.DayNumber),
            charing,
            session.Calendar.MinuteNumber));
        session.AdvanceElapsedTime(checked((throneVacancy.VacancyUntilMinute!.Value - session.Calendar.MinuteNumber + 1) * 60L));

        DaggerfallSeatLeaderPersona elevatedRuler = session.OfficeLedger.ResolveBoardSeatPersona("daggerfall");
        Assert.False(elevatedRuler.IsVacant);
        Assert.Null(elevatedRuler.HolderCastId);
        Assert.NotNull(elevatedRuler.HolderActorId);
        Assert.Equal("elevated from the provincial Great Houses", elevatedRuler.CauseDetail);
        Assert.True(session.State.ActorLedger.TryGet(elevatedRuler.HolderActorId!.Value, out ActorLedgerEntry? elevatedEntry));
        Assert.Equal(ActorLocationKind.AtSite, elevatedEntry!.Location?.Kind);
        Assert.Equal("cast:king-gothryd", elevatedEntry.Location!.TargetSlotId);
        // The elevated Great House CourtFactor's prior local office is now vacant (awaiting its own successor), not duplicated.
        DaggerfallOfficeOverrideSave vacatedCourtFactor = Assert.Single(session.OfficeLedger.Overrides, o =>
            o.Kind == DaggerfallOfficeKind.CourtFactor && o.Vacant && o.PreviousHolderActorId == elevatedRuler.HolderActorId);
        DaggerfallWorldProfileKey throneProfile = elevatedEntry.Location!.Profile!.Require();
        long elevatedRulerId = elevatedRuler.HolderActorId!.Value;

        // Save/reload mid-vacancy and let the former CourtFactor vacancy expire -> filling the old CourtFactor post
        // must seat a new local successor WITHOUT exiling or unseating the newly elevated ruler!
        using DaggerfallSession restoredAfterElevation = fixture.Restore(session.CaptureSave());
        long factorExpiryMinutes = vacatedCourtFactor.VacancyUntilMinute!.Value - restoredAfterElevation.Calendar.MinuteNumber + 1;
        restoredAfterElevation.AdvanceElapsedTime(checked(factorExpiryMinutes * 60L));

        DaggerfallOfficeOverrideSave filledCourtFactor = Assert.Single(restoredAfterElevation.OfficeLedger.Overrides,
            o => o.OfficeKey == vacatedCourtFactor.OfficeKey);
        Assert.False(filledCourtFactor.Vacant);
        Assert.NotNull(filledCourtFactor.HolderActorId);
        Assert.NotEqual(elevatedRulerId, filledCourtFactor.HolderActorId);
        Assert.True(restoredAfterElevation.State.ActorLedger.TryGet(filledCourtFactor.HolderActorId!.Value, out ActorLedgerEntry? newFactorEntry));
        Assert.Equal(ActorLocationKind.AtSite, newFactorEntry!.Location?.Kind);
        Assert.Equal(vacatedCourtFactor.OfficeSlotId, newFactorEntry.Location!.TargetSlotId);

        Assert.True(restoredAfterElevation.State.ActorLedger.TryGet(elevatedRulerId, out ActorLedgerEntry? stillRulerEntry));
        Assert.Equal(ActorLocationKind.AtSite, stillRulerEntry!.Location?.Kind);
        Assert.Equal(throneProfile, stillRulerEntry.Location!.Profile!.Require());
        Assert.Equal("cast:king-gothryd", stillRulerEntry.Location.TargetSlotId);

        // 2. Depose Charing's exterior Watch Captain -> after 24-48h vacancy, promoted exterior deputy takes dynamic/000
        //    with updated Watch Captain role and zero duplicate bodies!
        DaggerfallLocalOffice watchCaptain = session.OfficeLedger.ResolveWatchCaptain(charing);
        long oldCaptainId = watchCaptain.HolderActorId!.Value;
        DaggerfallOfficeOverrideSave captainVacancy = session.OfficeLedger.DeposeLocalOffice(
            watchCaptain,
            DaggerfallOfficeChangeCause.CardMove,
            checked((int)session.Calendar.DayNumber),
            causeDetail: "reassigned away",
            currentMinute: session.Calendar.MinuteNumber);
        Assert.False(session.State.Actors.TryGet(oldCaptainId, out _));

        session.AdvanceElapsedTime(checked((captainVacancy.VacancyUntilMinute!.Value - session.Calendar.MinuteNumber + 1) * 60L));
        DaggerfallLocalOffice newCaptain = session.OfficeLedger.ResolveWatchCaptain(charing);
        Assert.False(newCaptain.IsVacant);
        Assert.NotNull(newCaptain.HolderActorId);
        Assert.NotEqual(oldCaptainId, newCaptain.HolderActorId);
        ActorState liveNewCaptain = Assert.Single(session.State.Actors.All, a => a.DurableId == newCaptain.HolderActorId!.Value);
        Assert.Equal(watchCaptain.Title, session.State.Npcs.Require(liveNewCaptain.DurableId).Role);
        Assert.True(session.State.ActorLedger.IsHomeSlotSuppressed(liveNewCaptain.DurableId, fixture.ExteriorProfile.ProfileKey));
    }
}
