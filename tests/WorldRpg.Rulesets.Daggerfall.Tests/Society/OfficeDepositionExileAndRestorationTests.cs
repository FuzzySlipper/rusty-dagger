using System.Numerics;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Politics;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Crime;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Den #11132: office holders can be deposed, exiled, put under regency, or restored in play through real callers
/// (quest outcomes, crime/banishment &amp; legal denunciation, political board holder/court/conquest changes, and
/// refuge exile dialogue), all flowing through <see cref="DaggerfallOfficeLedger.OfficeChanged"/> and surviving save/load.
/// </summary>
public sealed class OfficeDepositionExileAndRestorationTests
{
    private const int DaggerfallRegion = 17;

    [Fact]
    public void Exiled_local_holder_is_placed_at_refuge_away_from_door_has_exile_dialogue_and_player_can_restore_through_OfficeChanged_and_save_load()
    {
        using var fixture = SourceBackedGuildBankSessionFixture.Create();
        using DaggerfallSession session = fixture.Start(fixture.ExteriorProfile);
        session.AdvanceElapsedTime(8 * 60 * 60);
        DaggerfallSiteRecord charing = session.Site.ActiveSite!;

        DaggerfallLocalOffice magesMaster = session.OfficeLedger.ResolveLocalOffices(charing)
            .Single(o => o.Kind == DaggerfallOfficeKind.GuildHallMaster && o.ProfileKey == fixture.MagesProfile.ProfileKey);
        long masterId = magesMaster.HolderActorId!.Value;

        // 1. Depose Mages Guild Hall Master -> exile record captured with deterministic refuge & origin anchor.
        _ = session.OfficeLedger.DeposeLocalOffice(
            magesMaster,
            DaggerfallOfficeChangeCause.Quest,
            checked((int)session.Calendar.DayNumber),
            causeDetail: "removed by guild inquiry",
            currentMinute: session.Calendar.MinuteNumber);

        DaggerfallOfficeExileSave? exile = session.OfficeLedger.TryGetExile(masterId);
        Assert.NotNull(exile);
        Assert.Equal(magesMaster.OfficeKey, exile!.OfficeKey);
        Assert.Equal(magesMaster.Title, exile.Title);
        DaggerfallWorldProfileKey refugeKey = exile.RefugeProfile.Require();
        Assert.NotEqual(fixture.MagesProfile.ProfileKey, refugeKey);

        // Save/load preserves sparse exile ledger entry before visiting refuge.
        using DaggerfallSession reloadedBeforeVisit = fixture.Restore(session.CaptureSave());
        DaggerfallOfficeExileSave? persistedExile = reloadedBeforeVisit.OfficeLedger.TryGetExile(masterId);
        Assert.NotNull(persistedExile);
        Assert.Equal(refugeKey, persistedExile!.RefugeProfile.Require());

        // 2. Travel to the exile's refuge interior -> physically materialized, NOT stacked on top of entrance door!
        _ = fixture.AdmitProfile(refugeKey);
        Assert.True(reloadedBeforeVisit.TryTransitionTo(refugeKey));
        ActorState liveExile = Assert.Single(reloadedBeforeVisit.State.Actors.All, a => a.DurableId == masterId);
        if (reloadedBeforeVisit.State.PlayerControl.Position is { } doorPos)
        {
            float doorDist = Vector3.Distance(liveExile.Position.ToVector(), doorPos.ToVector());
            Assert.True(doorDist > 0.25f, $"Exile should stand inside the refuge rather than on top of the entrance door (distance {doorDist:F2}m).");
        }

        // 3. Open dialogue with the exiled holder -> exile greeting + restore-exile topic!
        DaggerfallActivationOutcome opened = ActivateNpc(reloadedBeforeVisit, masterId);
        Assert.True(opened.Applied, opened.Message);
        DaggerfallDialogueView view = Assert.IsType<DaggerfallDialogueView>(reloadedBeforeVisit.ActivationPanel.View.Dialogue);
        Assert.Contains("in exile", view.Greeting, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(magesMaster.Title, view.Greeting, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(view.Topics, t => t.Id == "restore-exile");

        // 4. Attempting restoration with neither standing nor gold is refused cleanly.
        ulong currentGold = reloadedBeforeVisit.State.Currency.Read().Gold;
        if (currentGold > 0)
            Assert.True(reloadedBeforeVisit.State.Currency.TrySpendGold(currentGold, []));
        DaggerfallActivationOutcome refused = reloadedBeforeVisit.Dialogue.Ask(view.Revision, "restore-exile", null, null);
        Assert.False(refused.Applied);
        Assert.Contains("500 gold", refused.Message, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(reloadedBeforeVisit.OfficeLedger.TryGetExile(masterId));

        // 5. Grant standing backing (>= 10) and restore through dialogue -> writes back via OfficeChanged!
        List<DaggerfallOfficeChangedNotification> restoreEvents = [];
        reloadedBeforeVisit.OfficeLedger.OfficeChanged += restoreEvents.Add;
        reloadedBeforeVisit.State.Social.ChangeRegionalReputation(DaggerfallRegion, 15);
        DaggerfallDialogueView afterRefusal = Assert.IsType<DaggerfallDialogueView>(reloadedBeforeVisit.ActivationPanel.View.Dialogue);
        DaggerfallActivationOutcome accepted = reloadedBeforeVisit.Dialogue.Ask(afterRefusal.Revision, "restore-exile", null, null);
        Assert.True(accepted.Applied, accepted.Message);
        Assert.Contains("reclaim my seat", accepted.Message, StringComparison.OrdinalIgnoreCase);
        reloadedBeforeVisit.Dialogue.Close();

        DaggerfallOfficeChangedNotification restoreEvent = Assert.Single(restoreEvents, e => e.OfficeKey == magesMaster.OfficeKey);
        Assert.Equal(DaggerfallOfficeChangeKind.SuccessorSeated, restoreEvent.ChangeKind);
        Assert.Equal(masterId, restoreEvent.NewHolderActorId);
        Assert.Null(reloadedBeforeVisit.OfficeLedger.TryGetExile(masterId));
        Assert.False(reloadedBeforeVisit.State.Actors.TryGet(masterId, out _));

        // 6. Return to the Mages Guild and verify restored holder stands at their slot and survives save/load!
        DaggerfallLocalOffice restoredOffice = reloadedBeforeVisit.OfficeLedger.ResolveLocalOffices(charing)
            .Single(o => o.OfficeKey == magesMaster.OfficeKey);
        Assert.False(restoredOffice.IsVacant);
        Assert.Equal(masterId, restoredOffice.HolderActorId);
        Assert.True(reloadedBeforeVisit.TryTransitionTo(fixture.MagesProfile.ProfileKey));
        Assert.Single(reloadedBeforeVisit.State.Actors.All, a => a.DurableId == masterId);

        using DaggerfallSession afterRestoreSave = fixture.Restore(reloadedBeforeVisit.CaptureSave());
        Assert.Null(afterRestoreSave.OfficeLedger.TryGetExile(masterId));
        Assert.Single(afterRestoreSave.State.Actors.All, a => a.DurableId == masterId);
    }

    [Fact]
    public void Exiled_ruler_gold_backed_restoration_updates_PoliticalBoard_and_exiles_usurper()
    {
        using var fixture = SourceBackedGuildBankSessionFixture.Create();
        using DaggerfallSession session = fixture.Start(fixture.ExteriorProfile);
        session.AdvanceElapsedTime(8 * 60 * 60);
        Assert.NotNull(session.PoliticalBoard);

        long gothrydId = DaggerfallPeopleSlots.CastIndividualIdentity("king-gothryd");
        long aubkiId = DaggerfallPeopleSlots.CastIndividualIdentity("queen-aubk-i");

        // 1. Depose King Gothryd and let Queen Aubk-i take the throne on succession.
        DaggerfallOfficeOverrideSave deposed = session.OfficeLedger.DeposeBoardSeat(
            "daggerfall",
            DaggerfallOfficeChangeCause.Quest,
            checked((int)session.Calendar.DayNumber),
            causeDetail: "driven from Castle Daggerfall",
            currentMinute: session.Calendar.MinuteNumber);
        long minutesToExpiry = deposed.VacancyUntilMinute!.Value - session.Calendar.MinuteNumber + 1;
        session.AdvanceElapsedTime(checked(minutesToExpiry * 60L));

        DaggerfallSeatLeaderPersona usurper = session.OfficeLedger.ResolveBoardSeatPersona("daggerfall");
        Assert.Equal("queen-aubk-i", usurper.HolderCastId);
        DaggerfallOfficeExileSave? gothrydExile = session.OfficeLedger.TryGetExile(gothrydId);
        Assert.NotNull(gothrydExile);
        DaggerfallWorldProfileKey refugeKey = gothrydExile!.RefugeProfile.Require();

        // 2. Visit King Gothryd at his refuge, verify exile dialogue mentions Queen Aubk-i, and restore him with 2000g!
        _ = fixture.AdmitProfile(refugeKey);
        Assert.True(session.TryTransitionTo(refugeKey));
        Assert.Single(session.State.Actors.All, a => a.DurableId == gothrydId);

        DaggerfallActivationOutcome opened = ActivateNpc(session, gothrydId);
        Assert.True(opened.Applied, opened.Message);
        DaggerfallDialogueView view = Assert.IsType<DaggerfallDialogueView>(session.ActivationPanel.View.Dialogue);
        Assert.Contains("in exile", view.Greeting, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Queen Aubk-i", view.Greeting, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(view.Topics, t => t.Id == "restore-exile");

        Assert.True(session.State.Currency.ReceiveGold(2500UL));
        ulong goldBefore = session.State.Currency.Read().Gold;
        DaggerfallActivationOutcome restored = session.Dialogue.Ask(view.Revision, "restore-exile", null, null);
        Assert.True(restored.Applied, restored.Message);
        Assert.Equal(goldBefore - DaggerfallExileDialogue.MajorRestorationGoldCost, session.State.Currency.Read().Gold);
        session.Dialogue.Close();

        // King Gothryd is restored, Queen Aubk-i is now exiled to her refuge, and PoliticalBoard hears HolderChanged!
        DaggerfallSeatLeaderPersona restoredPersona = session.OfficeLedger.ResolveBoardSeatPersona("daggerfall");
        Assert.Equal("king-gothryd", restoredPersona.HolderCastId);
        Assert.Null(session.OfficeLedger.TryGetExile(gothrydId));
        Assert.NotNull(session.OfficeLedger.TryGetExile(aubkiId));

        session.AdvanceElapsedTime(7L * 24 * 60 * 60);
        Assert.Equal("king-gothryd", session.PoliticalBoard!.Board.Seat("daggerfall").Holder);
    }

    [Fact]
    public void Shipped_nobility_quests_depose_restore_and_declare_regency_through_normal_quest_progression_and_save_load()
    {
        using var fixture = SourceBackedGuildBankSessionFixture.Create();
        using DaggerfallSession session = fixture.Start(fixture.ExteriorProfile);
        Assert.NotNull(session.PoliticalBoard);

        // Charing's outdoors tavern/townsperson serves as questor; Nobility faction 201 (Daggerfall) has SocialGroupName = "Nobility".
        long questorId = session.State.Npcs.All.First(n => n.Services.Contains("talk", StringComparer.Ordinal)).DurableId;
        long gothrydId = DaggerfallPeopleSlots.CastIndividualIdentity("king-gothryd");

        // 1. While King Gothryd sits on Daggerfall's throne, R0C10Y29 ("The Court Writ") is offered in Nobility's
        //    ordinary work pool, while R0C10Y30 ("Charter of Restoration") is withheld until a ruler is in exile.
        DaggerfallQuestCatalogRow[] initialPool = session.State.Quests.OrdinaryWorkPool(
            factionId: 201, playerIsMember: false, playerLevel: 1, playerReputation: 0, playerRank: 0, DaggerfallCharacterGender.Male);
        Assert.Contains(initialPool, r => r.Name == "R0C10Y29");
        Assert.DoesNotContain(initialPool, r => r.Name == "R0C10Y30");

        List<DaggerfallOfficeChangedNotification> events = [];
        session.OfficeLedger.OfficeChanged += events.Add;

        // 2. Start shipped R0C10Y29, click questor -> prompt 1012 opens -> choose Yes (1 = _depose_).
        DaggerfallQuestInstanceSave writDepose = session.State.Quests.Start(new(
            "nobility:writ-depose", "R0C10Y29.txt", "R0C10Y29", DaggerfallQuestLifecycle.Active, null, [], [])
        { FactionId = 201, QuestorId = questorId });
        Assert.Equal([gothrydId], writDepose.Resources.Single(r => r.Symbol == "ruler").Binding.ActorIds);

        Assert.True(session.State.Quests.ActorClicked(questorId));
        session.AdvanceElapsedTime(1);
        DaggerfallQuestPromptSave deposePrompt = Assert.IsType<DaggerfallQuestPromptSave>(session.State.Quests.Messages.Pending);
        Assert.Equal("nobility:writ-depose", deposePrompt.InstanceId);
        Assert.Equal(1012, deposePrompt.MessageId);
        DaggerfallQuestPromptOption yesOption = Assert.Single(deposePrompt.Options, o => o.Target == "depose");
        Assert.True(session.State.Quests.ChoosePrompt(session.State.Variables, "nobility:writ-depose", deposePrompt.MessageId, deposePrompt.Id, yesOption.Id));
        session.AdvanceElapsedTime(1);

        DaggerfallOfficeChangedNotification deposeEvent = Assert.Single(events, e => e.OfficeFactionId == 201);
        Assert.Equal(DaggerfallOfficeChangeCause.Quest, deposeEvent.Cause);
        Assert.Equal(DaggerfallOfficeChangeKind.HolderDeposed, deposeEvent.ChangeKind);
        Assert.True(session.OfficeLedger.ResolveBoardSeatPersona("daggerfall").IsVacant);
        Assert.NotNull(session.OfficeLedger.TryGetExile(gothrydId));
        Assert.Contains(session.PoliticalBoard!.Board.Queued,
            n => n is HolderChanged hc && hc.Seat == "daggerfall" && hc.Holder == "vacant:daggerfall");

        // 3. Now that King Gothryd is in exile, R0C10Y30 ("Charter of Restoration") enters Nobility's ordinary work pool
        //    (while R0C10Y29 is withheld while the throne is vacant). Click questor -> restores King Gothryd with Cause.Quest!
        DaggerfallQuestCatalogRow[] exiledPool = session.State.Quests.OrdinaryWorkPool(
            factionId: 201, playerIsMember: false, playerLevel: 1, playerReputation: 0, playerRank: 0, DaggerfallCharacterGender.Male);
        Assert.DoesNotContain(exiledPool, r => r.Name == "R0C10Y29");
        Assert.Contains(exiledPool, r => r.Name == "R0C10Y30");

        events.Clear();
        DaggerfallQuestInstanceSave charter = session.State.Quests.Start(new(
            "nobility:charter-restore", "R0C10Y30.txt", "R0C10Y30", DaggerfallQuestLifecycle.Active, null, [], [])
        { FactionId = 201, QuestorId = questorId });
        Assert.Equal([gothrydId], charter.Resources.Single(r => r.Symbol == "exile").Binding.ActorIds);

        Assert.True(session.State.Quests.ActorClicked(questorId));
        session.AdvanceElapsedTime(1);
        DaggerfallOfficeChangedNotification restoreEvent = Assert.Single(events, e => e.OfficeFactionId == 201);
        Assert.Equal(DaggerfallOfficeChangeCause.Quest, restoreEvent.Cause);
        Assert.Equal(DaggerfallOfficeChangeKind.SuccessorSeated, restoreEvent.ChangeKind);
        Assert.Equal("king-gothryd", session.OfficeLedger.ResolveBoardSeatPersona("daggerfall").HolderCastId);
        Assert.Null(session.OfficeLedger.TryGetExile(gothrydId));

        // 4. Start shipped R0C10Y29 again, click questor -> prompt 1012 -> choose No (_regency_) -> seats regent & exiles Gothryd!
        events.Clear();
        _ = session.State.Quests.Start(new(
            "nobility:writ-regency", "R0C10Y29.txt", "R0C10Y29", DaggerfallQuestLifecycle.Active, null, [], [])
        { FactionId = 201, QuestorId = questorId });

        Assert.True(session.State.Quests.ActorClicked(questorId));
        session.AdvanceElapsedTime(1);
        DaggerfallQuestPromptSave regencyPrompt = Assert.IsType<DaggerfallQuestPromptSave>(session.State.Quests.Messages.Pending);
        Assert.Equal("nobility:writ-regency", regencyPrompt.InstanceId);
        DaggerfallQuestPromptOption noOption = Assert.Single(regencyPrompt.Options, o => o.Target == "regency");
        Assert.True(session.State.Quests.ChoosePrompt(session.State.Variables, "nobility:writ-regency", regencyPrompt.MessageId, regencyPrompt.Id, noOption.Id));
        session.AdvanceElapsedTime(1);

        DaggerfallOfficeChangedNotification regencyEvent = Assert.Single(events, e => e.OfficeFactionId == 201);
        Assert.Equal(DaggerfallOfficeChangeCause.Quest, regencyEvent.Cause);
        Assert.Equal(DaggerfallOfficeChangeKind.RegencyDeclared, regencyEvent.ChangeKind);
        DaggerfallSeatLeaderPersona regentPersona = session.OfficeLedger.ResolveBoardSeatPersona("daggerfall");
        Assert.True(regentPersona.IsRegency);
        Assert.False(string.IsNullOrWhiteSpace(regentPersona.DisplayName));
        Assert.NotEqual("king-gothryd", regentPersona.HolderCastId);
        Assert.NotNull(session.OfficeLedger.TryGetExile(gothrydId));

        // Save/load preserves regency and exile cleanly.
        using DaggerfallSession restored = fixture.Restore(session.CaptureSave());
        Assert.True(restored.OfficeLedger.ResolveBoardSeatPersona("daggerfall").IsRegency);
        Assert.Equal(regentPersona.HolderCastId, restored.OfficeLedger.ResolveBoardSeatPersona("daggerfall").HolderCastId);
        Assert.NotNull(restored.OfficeLedger.TryGetExile(gothrydId));
    }

    [Fact]
    public void Law_banishment_and_dialogue_denunciation_of_implicated_holder_depose_exile_and_notify_board()
    {
        using var fixture = SourceBackedGuildBankSessionFixture.Create();
        using DaggerfallSession session = fixture.Start(fixture.ExteriorProfile);
        session.AdvanceElapsedTime(8 * 60 * 60);
        DaggerfallSiteRecord charing = session.Site.ActiveSite!;
        Assert.NotNull(session.PoliticalBoard);

        DaggerfallLocalOffice watchCaptain = session.OfficeLedger.ResolveWatchCaptain(charing);
        DaggerfallLocalOffice magesMaster = session.OfficeLedger.ResolveLocalOffices(charing)
            .Single(o => o.Kind == DaggerfallOfficeKind.GuildHallMaster && o.ProfileKey == fixture.MagesProfile.ProfileKey);
        long captainId = watchCaptain.HolderActorId!.Value;
        long masterId = magesMaster.HolderActorId!.Value;

        // 1. Activate a regional CrimeCrackdown so Charing's local holders are legally implicated in dialogue.
        session.WorldConditions.Set(new DaggerfallWorldConditionToken(
            "test:charing:crackdown",
            DaggerfallWorldConditionKind.CrimeCrackdown,
            Region: DaggerfallRegion,
            Severity: 1,
            StartedDay: checked((int)session.Calendar.DayNumber),
            ExpiresDay: checked((int)session.Calendar.DayNumber) + 14));

        // Ensure Charing's Watch Captain is materialized outdoors and talkable.
        Assert.True(session.State.Actors.TryGet(captainId, out _));
        DaggerfallActivationOutcome opened = ActivateNpc(session, captainId);
        Assert.True(opened.Applied, opened.Message);
        DaggerfallDialogueView view = Assert.IsType<DaggerfallDialogueView>(session.ActivationPanel.View.Dialogue);
        string denounceTopic = $"denounce-holder:{masterId}";
        Assert.Contains(view.Topics, t => t.Id == denounceTopic);

        // Low standing (< 5) is refused cleanly.
        DaggerfallActivationOutcome refused = session.Dialogue.Ask(view.Revision, denounceTopic, null, null);
        Assert.False(refused.Applied);
        Assert.Null(session.OfficeLedger.TryGetExile(masterId));

        // Standing >= 5 -> Law.ConvictOrBanishOfficeHolder deposes and banishes the local holder with Cause.Law!
        List<DaggerfallOfficeChangedNotification> lawEvents = [];
        session.OfficeLedger.OfficeChanged += lawEvents.Add;
        session.State.Social.ChangeRegionalReputation(DaggerfallRegion, 10);
        DaggerfallDialogueView refreshed = Assert.IsType<DaggerfallDialogueView>(session.ActivationPanel.View.Dialogue);
        DaggerfallActivationOutcome banished = session.Dialogue.Ask(refreshed.Revision, denounceTopic, null, null);
        Assert.True(banished.Applied, banished.Message);
        Assert.Contains("banished", banished.Message, StringComparison.OrdinalIgnoreCase);
        session.Dialogue.Close();

        DaggerfallOfficeChangedNotification localLawEvent = Assert.Single(lawEvents, e => e.OfficeKey == magesMaster.OfficeKey);
        Assert.Equal(DaggerfallOfficeChangeCause.Law, localLawEvent.Cause);
        Assert.Equal(DaggerfallOfficeChangeKind.HolderDeposed, localLawEvent.ChangeKind);
        Assert.NotNull(session.OfficeLedger.TryGetExile(masterId));

        // 2. Direct Law.ConvictOrBanishOfficeHolder on King Gothryd -> deposes seat, exiles him, notifies PoliticalBoard!
        long gothrydId = DaggerfallPeopleSlots.CastIndividualIdentity("king-gothryd");
        int removed = session.Law.ConvictOrBanishOfficeHolder(gothrydId, DaggerfallRegion, DaggerfallCrimeKind.HighTreason, banished: true);
        Assert.Equal(1, removed);
        Assert.True(session.OfficeLedger.ResolveBoardSeatPersona("daggerfall").IsVacant);
        Assert.NotNull(session.OfficeLedger.TryGetExile(gothrydId));
        Assert.Contains(session.PoliticalBoard!.Board.Queued,
            n => n is HolderChanged hc && hc.Seat == "daggerfall" && hc.Holder == "vacant:daggerfall");

        using DaggerfallSession restored = fixture.Restore(session.CaptureSave());
        Assert.True(restored.OfficeLedger.ResolveBoardSeatPersona("daggerfall").IsVacant);
        Assert.NotNull(restored.OfficeLedger.TryGetExile(gothrydId));
        Assert.NotNull(restored.OfficeLedger.TryGetExile(masterId));
    }

    [Fact]
    public void Board_conquest_and_severe_court_crisis_drive_office_ledger_with_CardMove_and_zero_echo()
    {
        using var fixture = SourceBackedGuildBankSessionFixture.Create();
        using DaggerfallSession session = fixture.Start(fixture.ExteriorProfile);
        DaggerfallPoliticalBoard? politicalBoard = session.PoliticalBoard;
        Assert.NotNull(politicalBoard);
        Board board = politicalBoard!.Board;

        Card conquerCard = TestPayload.Definitions.PoliticalCanon!.Cards.Cards
            .First(c => c.Effects.Any(e => e.Kind == CardEffectKind.Conquer));
        Card annexCard = TestPayload.Definitions.PoliticalCanon!.Cards.Cards
            .First(c => c.Effects.Any(e => e.Kind == CardEffectKind.Annex));

        Seat wayrest = board.Seat("wayrest");
        Seat daggerfall = board.Seat("daggerfall");
        Region targetRegion = board.Regions.First(r =>
            !r.Capital
            && r.Allegiance is null
            && r.Borders.Any(b => board.Region(b).Allegiance == "wayrest"));

        // 1. Peaceful Annexation keeps the regional lord (suzerainty: NO deposition!).
        board.AdjustControl(targetRegion, -targetRegion.Control);
        board.AdjustInfluence(targetRegion, wayrest, 3);
        int annexRegionCatalog = targetRegion.CatalogRegion;
        List<DaggerfallOfficeChangedNotification> events = [];
        session.OfficeLedger.OfficeChanged += events.Add;

        board.Schedule(new PendingMove($"{board.Turn}-{wayrest.Id}", wayrest.Id, annexCard, [targetRegion.Id], wayrest.Home, board.Turn, board.Turn + 1));
        politicalBoard.AdvanceToDay(politicalBoard.NextTurnDay, () => []);
        Assert.Equal(wayrest.Id, targetRegion.Allegiance);
        Assert.DoesNotContain(events, e => e.Region == annexRegionCatalog && e.Cause == DaggerfallOfficeChangeCause.CardMove);

        // 2. Forceful Conquest (Conquer card during war) deposes the regional ruler with Cause.CardMove and ZERO echo!
        Region conquerTarget = board.Regions.First(r =>
            !r.Capital
            && r.Id != targetRegion.Id
            && r.Borders.Any(b => board.Region(b).Allegiance == "wayrest"));
        board.SetAllegiance(conquerTarget, daggerfall);
        if (!board.Wars.Any(w => (w.A == wayrest.Id && w.B == daggerfall.Id) || (w.A == daggerfall.Id && w.B == wayrest.Id)))
            board.OpenWar(wayrest, daggerfall, WarAim.Conquest);
        string borderOfConquer = conquerTarget.Borders.First(b => board.Region(b).Allegiance == "wayrest");
        board.AdjustInfluence(conquerTarget, wayrest, 2);
        board.AdjustControl(conquerTarget, -conquerTarget.Control);

        events.Clear();
        board.Schedule(new PendingMove($"{board.Turn}-{wayrest.Id}", wayrest.Id, conquerCard, [conquerTarget.Id], borderOfConquer, board.Turn, board.Turn + 1));
        politicalBoard.AdvanceToDay(politicalBoard.NextTurnDay, () => []);

        Assert.Equal(wayrest.Id, conquerTarget.Allegiance);
        // The lord is deposed, then a governor from Wayrest's court takes the seat (#11143), both by the board's own cause.
        DaggerfallOfficeChangedNotification conquestEvent = events.First(
            e => e.Region == conquerTarget.CatalogRegion && e.Cause == DaggerfallOfficeChangeCause.CardMove);
        Assert.True(conquestEvent.ChangeKind is DaggerfallOfficeChangeKind.HolderDeposed or DaggerfallOfficeChangeKind.VacancyOpened);
        Assert.All(events.Where(e => e.Region == conquerTarget.CatalogRegion), e => Assert.Equal(DaggerfallOfficeChangeCause.CardMove, e.Cause));
        Assert.DoesNotContain(board.Queued, n => n is HolderChanged);

        // 3. Severe court crisis (HostageTreaty via demand-hostages card on a crown seat) places that seat under
        //    interim regency via Cause.CardMove with ZERO echo back to the board!
        Card hostageCard = TestPayload.Definitions.PoliticalCanon!.Cards.Cards
            .First(c => c.Effects.Any(e => e.Kind == CardEffectKind.AddCourtCondition && e.Court == CourtConditionKind.HostageTreaty));
        Seat sentinel = board.Seat("sentinel");
        board.AdjustInfluence(board.Region(sentinel.Home!), wayrest, 1);

        events.Clear();
        board.Schedule(new PendingMove($"{board.Turn}-{wayrest.Id}", wayrest.Id, hostageCard, [sentinel.Id], sentinel.Home, board.Turn, board.Turn + 1));
        politicalBoard.AdvanceToDay(politicalBoard.NextTurnDay, () => []);

        DaggerfallSeatLeaderPersona sentinelPersona = session.OfficeLedger.ResolveBoardSeatPersona("sentinel");
        Assert.True(sentinelPersona.IsRegency, "HostageTreaty court crisis should place the crown seat under interim regency.");
        Assert.Contains(events, e => e.SeatId == "sentinel" && e.ChangeKind == DaggerfallOfficeChangeKind.RegencyDeclared && e.Cause == DaggerfallOfficeChangeCause.CardMove);
        Assert.DoesNotContain(board.Queued, n => n is HolderChanged hc && hc.Seat == "sentinel");

        // Board and world must immediately agree on Sentinel's regent holder without queuing any echo nudge!
        string expectedSentinelHolder = sentinelPersona.HolderCastId
            ?? (sentinelPersona.HolderActorId is long actor ? $"actor:{actor}" : "untitled:sentinel");
        Assert.NotEqual("queen-akorithi", expectedSentinelHolder);
        Assert.Equal(expectedSentinelHolder, board.Seat("sentinel").Holder);

        // Save/load preserves both world regency and synchronized board seat holder cleanly!
        using DaggerfallSession restored = fixture.Restore(session.CaptureSave());
        DaggerfallSeatLeaderPersona restoredSentinel = restored.OfficeLedger.ResolveBoardSeatPersona("sentinel");
        Assert.True(restoredSentinel.IsRegency);
        Assert.Equal(expectedSentinelHolder, restored.PoliticalBoard!.Board.Seat("sentinel").Holder);
        Assert.Equal(expectedSentinelHolder, restoredSentinel.HolderCastId
            ?? (restoredSentinel.HolderActorId is long restoredActor ? $"actor:{restoredActor}" : "untitled:sentinel"));
    }

    private static DaggerfallActivationOutcome ActivateNpc(DaggerfallSession session, long durableId)
    {
        DaggerfallActivationTarget target = Assert.Single(
            session.Dialogue.NpcTargets(),
            candidate => candidate.Identity.Value == checked((ulong)durableId));
        return session.Dialogue.ActivateNpc(new DaggerfallActivationSelection(DaggerfallActivationMode.Talk, target));
    }
}
