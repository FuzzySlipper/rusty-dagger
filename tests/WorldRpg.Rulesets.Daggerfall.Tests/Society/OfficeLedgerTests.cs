using System.Text.Json.Nodes;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Political layer A5 (#11050): sparse office ledger connecting local seeded building/town slots and major
/// board-game seats (B1) to live actors, quest office resolution (A2), death reactions, and save/reload.
/// </summary>
public sealed class OfficeLedgerTests
{
    private const int DaggerfallRegion = 17;
    private const int DaggerfallProvinceFaction = 201;

    [Fact]
    public void Untouched_world_writes_zero_office_ledger_bytes_and_resolves_all_13_board_seats_and_local_offices()
    {
        using var fixture = SourceBackedGuildBankSessionFixture.Create();
        using DaggerfallSession session = fixture.Start(fixture.ExteriorProfile);

        // 1. Untouched world has zero overrides before and after save/reload.
        Assert.Equal(0, session.OfficeLedger.OverrideCount);
        RulesetSavePayload saved = session.CaptureSave();
        DaggerfallSavePayload decoded = DaggerfallSavePayload.Read(saved);
        Assert.Empty(decoded.OfficeLedger.Overrides);

        // 2. All 13 canonical B1 board-game seats resolve deterministic leader personas with zero overrides.
        IReadOnlyList<DaggerfallSeatLeaderPersona> seats = session.OfficeLedger.ResolveAllBoardSeatPersonas();
        Assert.Equal(13, seats.Count);
        Assert.All(seats, seat =>
        {
            Assert.False(seat.IsOverridden);
            Assert.False(seat.IsVacant);
            Assert.False(string.IsNullOrWhiteSpace(seat.DisplayName));
        });

        DaggerfallSeatLeaderPersona daggerfall = seats.Single(s => s.SeatId == "daggerfall");
        Assert.Equal("King Gothryd", daggerfall.DisplayName);
        Assert.Equal("king-gothryd", daggerfall.HolderCastId);
        Assert.Equal(DaggerfallPeopleSlots.CastIndividualIdentity("king-gothryd"), daggerfall.HolderActorId);

        DaggerfallSeatLeaderPersona wayrest = seats.Single(s => s.SeatId == "wayrest");
        Assert.Equal("King Eadwyre", wayrest.DisplayName);
        Assert.Equal("king-eadwyre", wayrest.HolderCastId);

        DaggerfallSeatLeaderPersona sentinel = seats.Single(s => s.SeatId == "sentinel");
        Assert.Equal("Queen Akorithi", sentinel.DisplayName);
        Assert.Equal("queen-akorithi", sentinel.HolderCastId);

        DaggerfallSeatLeaderPersona orsinium = seats.Single(s => s.SeatId == "orsinium");
        Assert.Equal("Gortwog", orsinium.DisplayName);
        Assert.Equal("gortwog", orsinium.HolderCastId);

        Assert.Equal("King of Worms", seats.Single(s => s.SeatId == "necromancers").DisplayName);
        Assert.Equal("The Night Mother", seats.Single(s => s.SeatId == "dark-brotherhood").DisplayName);
        Assert.Equal("Medora", seats.Single(s => s.SeatId == "balfiera").DisplayName);
        Assert.Equal("The Underking", seats.Single(s => s.SeatId == "underking").DisplayName);

        // 3. Find a real town with a Palace (BuildingType 16), a Temple (14), and a Guild Hall (11) -> all 4 local office kinds resolve deterministically with 0 save bytes!
        DaggerfallSiteRecord palaceTown = TestPayload.Definitions.Locations.Records.First(r =>
            r.Exterior is { Buildings.Count: > 0 } ext
            && ext.Buildings.Values.Any(b => b.Source.BuildingType == 16)
            && ext.Buildings.Values.Any(b => b.Source.BuildingType == 14)
            && ext.Buildings.Values.Any(b => b.Source.BuildingType == 11));

        IReadOnlyList<DaggerfallLocalOffice> localOffices = session.OfficeLedger.ResolveLocalOffices(palaceTown);
        Assert.Contains(localOffices, o => o.Kind == DaggerfallOfficeKind.WatchCaptain && o.HolderActorId is > 0 && !o.IsOverridden);
        Assert.Contains(localOffices, o => o.Kind == DaggerfallOfficeKind.GuildHallMaster && o.HolderActorId is > 0 && !o.IsOverridden);
        Assert.Contains(localOffices, o => o.Kind == DaggerfallOfficeKind.TempleHead && o.HolderActorId is > 0 && !o.IsOverridden);
        Assert.Contains(localOffices, o => o.Kind == DaggerfallOfficeKind.CourtFactor && o.HolderActorId is > 0 && !o.IsOverridden);
        Assert.Equal(0, session.OfficeLedger.OverrideCount);
    }

    [Fact]
    public void Local_offices_bind_deterministically_to_exact_live_seeded_slots_before_and_after_visiting()
    {
        using var fixture = SourceBackedGuildBankSessionFixture.Create();
        using DaggerfallSession session = fixture.Start(fixture.ExteriorProfile);

        // Advance to daytime so exterior population (including gate guard / watch captain) is materialized.
        session.AdvanceElapsedTime(8 * 60 * 60);
        DaggerfallSiteRecord charing = session.Site.ActiveSite!;

        // 1. Watch Captain resolves to the EXACT live gate guard slot (dynamic/000) in Charing exterior!
        DaggerfallLocalOffice watchCaptain = session.OfficeLedger.ResolveWatchCaptain(charing);
        Assert.Equal(DaggerfallOfficeKind.WatchCaptain, watchCaptain.Kind);
        Assert.NotNull(watchCaptain.HolderActorId);
        DaggerfallPopulationPlacement primaryGuard = fixture.ExteriorProfile.Population.First(p => p.IsGuard);
        Assert.Equal(DaggerfallExteriorPopulation.WatchCaptainSlotId(charing), primaryGuard.Id);
        long expectedGuardId = DaggerfallPeopleSlots.SeededSlotIdentity(charing.Id, $"{fixture.ExteriorProfile.ProfileKey.LogicalId}/{primaryGuard.Id}");
        Assert.Equal(expectedGuardId, watchCaptain.HolderActorId!.Value);
        Assert.True(session.State.Actors.TryGet(watchCaptain.HolderActorId.Value, out _));

        // 2. Resolve Charing's Mages Guild Hall Master BEFORE ever entering the Mages Guild interior!
        DaggerfallLocalOffice preVisitMaster = session.OfficeLedger.ResolveLocalOffices(charing)
            .Single(o => o.Kind == DaggerfallOfficeKind.GuildHallMaster && o.ProfileKey == fixture.MagesProfile.ProfileKey);
        Assert.NotNull(preVisitMaster.HolderActorId);
        Assert.False(preVisitMaster.IsOverridden);
        Assert.Equal(0, session.OfficeLedger.OverrideCount);

        // Enter the real assembled Mages Guild interior -> exact pre-visit durable actor ID is live in EntityStore!
        Assert.True(session.TryTransitionTo(fixture.MagesProfile.ProfileKey));
        Assert.True(session.State.Actors.TryGet(preVisitMaster.HolderActorId!.Value, out _),
            $"Expected pre-visit GuildHallMaster id {preVisitMaster.HolderActorId} (slot {preVisitMaster.SlotId}) to match a live materialized Mages Guild actor.");
        DaggerfallNpc liveMasterNpc = session.State.Npcs.Require(preVisitMaster.HolderActorId.Value);
        Assert.Equal(preVisitMaster.SlotId, liveMasterNpc.StableKey);
        Assert.Equal(0, session.OfficeLedger.OverrideCount);
    }

    [Fact]
    public void Board_game_card_move_override_updates_office_holders_quests_macros_and_seat_persona_across_save_reload()
    {
        using var fixture = SourceBackedGuildBankSessionFixture.Create();
        using DaggerfallSession session = fixture.Start(fixture.ExteriorProfile);

        // Board-game card deposes King Gothryd and installs Lord Bridwell on Daggerfall's throne.
        DaggerfallOfficeOverrideSave applied = session.OfficeLedger.SetBoardSeatHolder(
            seatId: "daggerfall",
            holderActorId: null,
            holderCastId: "lord-bridwell",
            holderFactionId: 367,
            holderDisplayName: null,
            cause: DaggerfallOfficeChangeCause.CardMove,
            changedDay: 14,
            causeDetail: "was deposed in a palace coup");

        Assert.Equal(1, session.OfficeLedger.OverrideCount);
        Assert.Equal("King Gothryd", applied.PreviousHolderName);
        Assert.Equal("Lord Bridwell", applied.HolderDisplayName);

        // 1. DaggerfallOfficeHolders immediately sees the new holder & predecessor fate.
        DaggerfallOfficeHolder? ruler = session.OfficeHolders.ResolveRegionalRuler(DaggerfallRegion);
        Assert.NotNull(ruler);
        Assert.True(ruler!.HasLivingHolder);
        Assert.Equal("Lord Bridwell", ruler.DisplayName);
        Assert.Equal("lord-bridwell", ruler.CastId);
        Assert.Equal("King Gothryd", ruler.PreviousHolderName);
        Assert.Equal("was deposed in a palace coup", ruler.PreviousHolderFate);

        // 2. Board-game seat leader persona reflects Lord Bridwell immediately.
        DaggerfallSeatLeaderPersona seatPersona = session.OfficeLedger.ResolveBoardSeatPersona("daggerfall");
        Assert.True(seatPersona.IsOverridden);
        Assert.False(seatPersona.IsVacant);
        Assert.Equal("Lord Bridwell", seatPersona.DisplayName);
        Assert.Equal("lord-bridwell", seatPersona.HolderCastId);
        Assert.Equal("King Gothryd", seatPersona.PreviousHolderName);
        Assert.Equal(DaggerfallOfficeChangeCause.CardMove, seatPersona.ChangeCause);

        // 3. Real A2 office-addressed quest binds directly to the overridden holder and renders predecessor macros!
        JsonArray questRecords = TestPayload.QuestRecords();
        questRecords.Add(JsonNode.Parse("""
            {
              "name": "office-ledger-petition",
              "displayName": "",
              "sourceFile": "office-ledger-petition.txt",
              "messages": [
                { "id": 1000, "lines": ["Regent: %rt %rn (leader: %nrn / %fl1)."] },
                { "id": 1001, "lines": ["New %lt1 %fl1 succeeds %ol1, who %olf."] }
              ],
              "blocks": [
                { "kind": "person", "firstLine": 1, "lines": ["Person _ruler_ office Province atHome"] }
              ]
            }
            """)!);
        DaggerfallDefinitions questDefs = TestPayload.WithQuests(questRecords);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, fixture.ExteriorProfile);
        SpatialFake spatial = SpatialFake.Create(fixture.ExteriorProfile.Geometry.Spatial[0].Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        DaggerfallSessionComposition questComp = new(questDefs, fixture.ExteriorProfile, DaggerfallTuning.Defaults)
        {
            Profiles = new DaggerfallSiteProfiles([fixture.ExteriorProfile]),
        };

        // 4. Sparse override survives CaptureSave / Restore cleanly AND drives A2 quest person binding + macros!
        RulesetSavePayload saved = session.CaptureSave();
        DaggerfallSavePayload decoded = DaggerfallSavePayload.Read(saved);
        Assert.Single(decoded.OfficeLedger.Overrides);

        using DaggerfallSession restored = DaggerfallSession.Restore(engine.Context, questComp, saved);
        Assert.Equal(1, restored.OfficeLedger.OverrideCount);
        DaggerfallOfficeHolder? restoredRuler = restored.OfficeHolders.ResolveRegionalRuler(DaggerfallRegion);
        Assert.Equal("Lord Bridwell", restoredRuler!.DisplayName);
        Assert.Equal("King Gothryd", restoredRuler.PreviousHolderName);
        Assert.Equal("was deposed in a palace coup", restoredRuler.PreviousHolderFate);
        Assert.Equal("Lord Bridwell", restored.OfficeLedger.ResolveBoardSeatPersona("daggerfall").DisplayName);

        DaggerfallQuestInstanceSave started = restored.State.Quests.Start(new(
            "office-ledger:1", "office-ledger-petition.txt", "office-ledger-petition",
            DaggerfallQuestLifecycle.Active, null, [], [])
        { FactionId = DaggerfallProvinceFaction });
        DaggerfallQuestResourceState boundRuler = started.Resources.Single();
        Assert.Equal(DaggerfallQuestResourceBindingKind.Actor, boundRuler.Binding.Kind);
        Assert.Equal([DaggerfallPeopleSlots.CastIndividualIdentity("lord-bridwell")], boundRuler.Binding.ActorIds);
        Assert.Equal("lord-bridwell", boundRuler.SelectedPerson!.CastId);
        Assert.Equal("Lord Bridwell", boundRuler.SelectedPerson.DisplayName);
        Assert.Contains("King Lord Bridwell (leader: Lord Bridwell / Lord Bridwell).",
            restored.State.Quests.RenderMessage(started, 1000, DaggerfallQuestMessageDelivery.Popup, 0).Text);
        Assert.Contains("New King Lord Bridwell succeeds King Gothryd, who was deposed in a palace coup.",
            restored.State.Quests.RenderMessage(started, 1001, DaggerfallQuestMessageDelivery.Popup, 0).Text);
    }

    [Fact]
    public void Local_office_quest_override_and_explicit_vacancy_persist_and_prune_cleanly()
    {
        using var fixture = SourceBackedGuildBankSessionFixture.Create();
        using DaggerfallSession session = fixture.Start(fixture.ExteriorProfile);
        DaggerfallSiteRecord charing = session.Site.ActiveSite!;

        DaggerfallLocalOffice magesMaster = session.OfficeLedger.ResolveLocalOffices(charing)
            .Single(o => o.Kind == DaggerfallOfficeKind.GuildHallMaster && o.ProfileKey == fixture.MagesProfile.ProfileKey);
        string originalName = magesMaster.DisplayName;

        // Quest installs a new Guild Hall Master -> writes 1 sparse override.
        long replacementId = session.State.Npcs.RegisterCivilian(
            new DaggerfallNpcSite(charing.Id.Region, charing.Name, string.Empty),
            new DaggerfallNpcAppearance("breton", "Female", 182, 0, 77, 40),
            "mage", ["talk"]);
        session.State.Npcs.SetDisplayName(replacementId, "Archmagister Vaelen");
        session.OfficeLedger.SetLocalOfficeHolder(
            magesMaster, replacementId, "Archmagister Vaelen",
            DaggerfallOfficeChangeCause.Quest, changedDay: 5, causeDetail: "installed by guild tribunal");

        // Board card vacates Daggerfall's throne -> explicit interregnum vacancy.
        session.OfficeLedger.VacateFactionOffice(
            DaggerfallProvinceFaction, DaggerfallOfficeChangeCause.CardMove, changedDay: 6, causeDetail: "fled into exile");

        RulesetSavePayload saved = session.CaptureSave();
        using DaggerfallSession restored = fixture.Restore(saved);
        Assert.Equal(2, restored.OfficeLedger.OverrideCount);

        DaggerfallLocalOffice restoredMaster = restored.OfficeLedger.ResolveLocalOffices(charing)
            .Single(o => o.OfficeKey == magesMaster.OfficeKey);
        Assert.True(restoredMaster.IsOverridden);
        Assert.Equal(replacementId, restoredMaster.HolderActorId);
        Assert.Equal("Archmagister Vaelen", restoredMaster.DisplayName);
        Assert.Equal(originalName, restoredMaster.PreviousHolderName);
        Assert.Equal(DaggerfallOfficeChangeCause.Quest, restoredMaster.ChangeCause);

        // Vacant throne reports HasLivingHolder == false and IsVacant == true (no dead or fabricated ruler!).
        DaggerfallOfficeHolder? vacantRuler = restored.OfficeHolders.ResolveRegionalRuler(DaggerfallRegion);
        Assert.NotNull(vacantRuler);
        Assert.False(vacantRuler!.HasLivingHolder);
        Assert.Equal("King Gothryd", vacantRuler.PreviousHolderName);
        Assert.Equal("fled into exile", vacantRuler.PreviousHolderFate);
        DaggerfallSeatLeaderPersona vacantSeat = restored.OfficeLedger.ResolveBoardSeatPersona("daggerfall");
        Assert.True(vacantSeat.IsVacant);
        Assert.Equal("King Gothryd", vacantSeat.PreviousHolderName);

        // Clearing overrides returns both offices to zero-byte baseline.
        Assert.True(restored.OfficeLedger.ClearOverride(magesMaster.OfficeKey));
        Assert.True(restored.OfficeLedger.ClearOverride(DaggerfallOfficeLedger.FactionOfficeKey(DaggerfallProvinceFaction)));
        Assert.Equal(0, restored.OfficeLedger.OverrideCount);
        Assert.Equal("King Gothryd", restored.OfficeHolders.ResolveRegionalRuler(DaggerfallRegion)!.DisplayName);
    }

    [Fact]
    public void Killing_watch_captain_or_ruler_writes_sparse_death_override_while_ordinary_walker_writes_zero_bytes()
    {
        using var fixture = SourceBackedGuildBankSessionFixture.Create();
        using DaggerfallSession session = fixture.Start(fixture.ExteriorProfile);
        session.AdvanceElapsedTime(8 * 60 * 60);
        DaggerfallSiteRecord charing = session.Site.ActiveSite!;

        DaggerfallLocalOffice watchCaptain = session.OfficeLedger.ResolveWatchCaptain(charing);
        long captainId = watchCaptain.HolderActorId!.Value;
        string captainName = watchCaptain.DisplayName;

        // 1. Killing an ordinary non-office walker writes ZERO office-ledger overrides.
        ActorState ordinaryWalker = session.State.Actors.All.First(a =>
            a.DurableId != DaggerfallActorIdentity.PlayerEntityId && a.DurableId != captainId);
        IReadOnlyList<DaggerfallOfficeOverrideSave> nonOfficeRecorded =
            session.OfficeLedger.ReactToDeath(ordinaryWalker.DurableId, currentDay: 10, charing);
        Assert.Empty(nonOfficeRecorded);
        Assert.Equal(0, session.OfficeLedger.OverrideCount);

        // 2. Killing the Watch Captain writes ONE sparse Death override recording the slain captain.
        session.State.ActorLedger.Upsert(new ActorLedgerEntry(
            captainId,
            ActorOrigin.Seeded(fixture.ExteriorProfile.ProfileKey, watchCaptain.SlotId),
            Location: ActorLocationOverride.Dead(fixture.ExteriorProfile.ProfileKey)));
        IReadOnlyList<DaggerfallOfficeOverrideSave> captainRecorded =
            session.OfficeLedger.ReactToDeath(captainId, currentDay: 10, charing);
        DaggerfallOfficeOverrideSave captainOverride = Assert.Single(captainRecorded);
        Assert.Equal(watchCaptain.OfficeKey, captainOverride.OfficeKey);
        Assert.Equal(DaggerfallOfficeChangeCause.Death, captainOverride.Cause);
        Assert.Equal(captainName, captainOverride.PreviousHolderName);
        Assert.True(captainOverride.Vacant);

        // 3. Marking King Gothryd dead + ReactToDeath writes a sparse succession override promoting Mobar!
        long gothrydId = DaggerfallPeopleSlots.CastIndividualIdentity("king-gothryd");
        session.State.ActorLedger.Upsert(new ActorLedgerEntry(
            gothrydId,
            ActorOrigin.Cast("king-gothryd"),
            Location: ActorLocationOverride.Dead(fixture.ExteriorProfile.ProfileKey)));
        IReadOnlyList<DaggerfallOfficeOverrideSave> rulerRecorded =
            session.OfficeLedger.ReactToDeath(gothrydId, currentDay: 11, charing);
        DaggerfallOfficeOverrideSave rulerOverride = Assert.Single(rulerRecorded);
        Assert.Equal(DaggerfallOfficeLedger.FactionOfficeKey(DaggerfallProvinceFaction), rulerOverride.OfficeKey);
        Assert.Equal(DaggerfallOfficeChangeCause.Death, rulerOverride.Cause);
        Assert.Equal("King Gothryd", rulerOverride.PreviousHolderName);
        Assert.Equal("Mobar", rulerOverride.HolderDisplayName);
        Assert.False(rulerOverride.Vacant);

        // Survives save & reload cleanly.
        using DaggerfallSession restored = fixture.Restore(session.CaptureSave());
        Assert.Equal(2, restored.OfficeLedger.OverrideCount);
        Assert.True(restored.OfficeLedger.ResolveWatchCaptain(charing).IsVacant);
        Assert.Equal(captainName, restored.OfficeLedger.ResolveWatchCaptain(charing).PreviousHolderName);
        Assert.Equal("Mobar", restored.OfficeLedger.ResolveBoardSeatPersona("daggerfall").DisplayName);
        Assert.Equal("King Gothryd", restored.OfficeLedger.ResolveBoardSeatPersona("daggerfall").PreviousHolderName);
    }
}
