using System.Numerics;
using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Debugging;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Crime;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Focused tests for Den #11161 (household mourning, temporary shop bereavement closure,
/// kin inheritance / town replacement, and non-fatal arrest counter cover + release return).
/// </summary>
public sealed class HouseholdMourningAndShopContinuityTests
{
    [Fact]
    public void Murdering_household_member_triggers_mourning_grieving_dialogue_barks_and_refuses_non_essential_favours_until_window_expires()
    {
        using var fixture = new MourningSessionFixture();
        DaggerfallSession session = fixture.Session;
        DaggerfallSiteRecord site = session.Site.ActiveSite!;
        DaggerfallNpcSite npcSite = new(site.Id.Region, site.Name, "1/4/0", session.Sites.ActiveProfile.LogicalId);
        string familyId = DaggerfallHouseholds.BuildingHouseholdId(site.Id, 1, 4, 0);

        long victimId = AddNpcWithFamily(session, site.Id, npcSite, "t11161/victim", "Hector Vale", familyId);
        long grievingKinId = AddNpcWithFamily(session, site.Id, npcSite, "t11161/kin-talk", "Miriam Vale", familyId);
        long barkingKinId = AddNpcWithFamily(session, site.Id, npcSite, "t11161/kin-bark", "Tobias Vale", familyId,
            interaction: ActorInteractionPolicyKind.AmbientBark);
        long introTargetId = AddNpcWithFamily(session, site.Id, npcSite, "t11161/neighbor", "Alden Moss",
            DaggerfallHouseholds.BuildingHouseholdId(site.Id, 1, 4, 1));

        // Before the killing, surviving kin is NOT mourning and accepts non-essential favours.
        DaggerfallNpc kinNpc = session.State.Npcs.Require(grievingKinId);
        Assert.False(session.SocialConsequences.IsActorMourning(grievingKinId, kinNpc));
        Assert.True(session.Introductions.CanCurryFavourToday(kinNpc));

        // Unwitnessed killing of victim: zero kin grudge against the player, yet surviving kin STILL enters mourning!
        fixture.Perception.Responder = _ => new PerceptionReadoutResult(
            ReadOnlyMemory<PerceptionPair>.Empty, ReadOnlyMemory<PerceptionAggregate>.Empty,
            0, false, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        ActorState victimActor = session.State.Actors.Get(victimId);
        victimActor.Stats.GetTrack(TrackId.Parse("health")).SetCurrent(0, clamp: true);
        session.SocialConsequences.ReactToDeath(
            new ActorDiedFact(victimId, DaggerfallActorIdentity.PlayerEntityId, DaggerfallDamageCause.PhysicalAttack, 50, 50.0, 0, 0),
            victimActor,
            minute: session.Calendar.MinuteNumber);

        Assert.Equal(0, session.SocialConsequences.HouseholdStandingDelta(familyId, site));
        Assert.True(session.SocialConsequences.IsHouseholdMourning(familyId));
        Assert.True(session.SocialConsequences.IsActorMourning(grievingKinId, kinNpc));

        // 1. Mourning kin refuses non-essential favours and non-essential introductions.
        Assert.False(session.Introductions.CanCurryFavourToday(kinNpc));
        DaggerfallFavourResult favourRefused = session.Introductions.TryCurryFavour(kinNpc, politeTone: true, bluntTone: false);
        Assert.False(favourRefused.Applied);
        Assert.Contains("mourning", favourRefused.Message, StringComparison.OrdinalIgnoreCase);

        Assert.Empty(session.Introductions.ListIntroductionTargets(kinNpc, site));
        DaggerfallIntroductionResult introRefused = session.Introductions.TryIssueIntroduction(kinNpc, introTargetId, site);
        Assert.False(introRefused.Accepted);
        Assert.Contains("mourning", introRefused.Message, StringComparison.OrdinalIgnoreCase);

        // 2. Full-dialogue kin speaks a grieving greeting; ambient-bark kin speaks a grieving bark.
        DaggerfallActivationTarget talkTarget = Assert.Single(
            session.Dialogue.NpcTargets(),
            candidate => checked((long)candidate.Identity.Value) == grievingKinId);
        DaggerfallActivationOutcome opened = session.Dialogue.ActivateNpc(new(DaggerfallActivationMode.Talk, talkTarget));
        Assert.True(opened.Applied, opened.Message);
        DaggerfallDialogueView view = Assert.IsType<DaggerfallDialogueView>(session.ActivationPanel.View.Dialogue);
        Assert.Contains("mourning", view.Greeting, StringComparison.OrdinalIgnoreCase);
        session.Dialogue.Close();

        DaggerfallActivationTarget barkTarget = Assert.Single(
            session.Dialogue.NpcTargets(),
            candidate => checked((long)candidate.Identity.Value) == barkingKinId);
        DaggerfallActivationOutcome barked = session.Dialogue.ActivateNpc(new(DaggerfallActivationMode.Talk, barkTarget));
        Assert.True(barked.Applied, barked.Message);
        Assert.Contains("mourning", barked.Message, StringComparison.OrdinalIgnoreCase);

        // 3. Mourning persists across save/load.
        RulesetSavePayload midMourningSave = session.CaptureSave();
        using (DaggerfallSession restored = fixture.Restore(midMourningSave))
        {
            Assert.True(restored.SocialConsequences.IsHouseholdMourning(familyId));
            DaggerfallNpc restoredKin = restored.State.Npcs.Require(grievingKinId);
            Assert.False(restored.Introductions.CanCurryFavourToday(restoredKin));

            // 4. Advancing past the 7-day mourning window clears mourning, restores favours, and prunes the empty standing!
            restored.AdvanceElapsedTime(8L * 24L * 60L * 60L, idleFatigue: false);
            Assert.False(restored.SocialConsequences.IsHouseholdMourning(familyId));
            Assert.True(restored.Introductions.CanCurryFavourToday(restoredKin));

            DaggerfallSavePayload afterExpiry = DaggerfallSavePayload.Read(restored.CaptureSave());
            Assert.Empty(afterExpiry.LocalHouseholds.Standings);
        }
    }

    [Fact]
    public void Murdering_shop_proprietor_closes_door_for_bereavement_then_surviving_adult_kin_inherits_counter_with_grieving_dialogue_and_services()
    {
        using var fixture = SourceBackedServiceSessionFixture.Create();
        var tavern = fixture.TavernProvider();
        DaggerfallSiteBuildingSource building = fixture.SourceBuilding(tavern.Profile);
        using DaggerfallSession session = fixture.Start(tavern.Profile);

        DaggerfallNpc proprietor = SourceNpc(session, tavern.Placement.Id);
        Assert.Contains(DaggerfallNpcServiceFacts.TavernService, proprietor.Services);

        // Ensure the proprietor has a household and a surviving adult kin member in the same settlement.
        string familyId = !string.IsNullOrWhiteSpace(proprietor.FamilyId)
            ? proprietor.FamilyId!
            : DaggerfallHouseholds.BuildingHouseholdId(tavern.Profile.Site!.Value, building.Id.BlockX, building.Id.BlockY, building.Id.Index);
        if (string.IsNullOrWhiteSpace(proprietor.FamilyId))
        {
            DaggerfallNpc updatedProp = proprietor with { FamilyId = familyId };
            session.State.Actors.Get(proprietor.DurableId).Actor.Get<ActorPersonComponent>().Snapshot = updatedProp;
            proprietor = updatedProp;
        }
        DaggerfallNpcSite localSite = new(tavern.Profile.Site!.Value.Region, proprietor.Site.Location,
            $"{building.Id.BlockX}/{building.Id.BlockY}/{building.Id.Index}", tavern.Profile.ProfileKey.LogicalId);
        long survivingKinId = AddNpcWithFamily(session, tavern.Profile.Site!.Value, localSite, "t11161/tavern-kin", "Elenor Greaves", familyId);

        int murderDay = checked((int)session.Calendar.DayNumber);

        // Kill the proprietor via normal death consequences (unwitnessed or witnessed -> mourning + shop bereavement).
        ActorState propActor = session.State.Actors.Get(proprietor.DurableId);
        propActor.Stats.GetTrack(TrackId.Parse("health")).SetCurrent(0, clamp: true);
        session.State.ActorLedger.Upsert(new ActorLedgerEntry(
            proprietor.DurableId,
            ActorOrigin.Seeded(tavern.Profile.ProfileKey, proprietor.StableKey),
            Location: ActorLocationOverride.Dead(
                tavern.Profile.ProfileKey,
                new ActorPoseOverride(propActor.Position.X, propActor.Position.Y, propActor.Position.Z, 0f))));
        session.SocialConsequences.ReactToDeath(
            new ActorDiedFact(proprietor.DurableId, DaggerfallActorIdentity.PlayerEntityId, DaggerfallDamageCause.PhysicalAttack, 50, 50.0, 0, 0),
            propActor,
            session.Calendar.MinuteNumber);
        DaggerfallShopPremisesSnapshot? afterDeath = session.ShopContinuity.ReactToProprietorDeath(
            proprietor.DurableId, proprietor, tavern.Profile.ProfileKey, tavern.Profile, murderDay);

        Assert.NotNull(afterDeath);
        Assert.Equal(DaggerfallShopContinuityState.BereavementClosed, afterDeath!.Value.State);
        Assert.Equal(murderDay + 2, afterDeath.Value.ClosedUntilDay);
        Assert.Equal(survivingKinId, afterDeath.Value.ReplacementActorId);

        // Exterior door entry policy sees the bereavement closure notice and holds the door locked during bereavement.
        string? closedNotice = session.ShopContinuity.GetClosedNotice(tavern.Profile.Site!.Value, building.Id, murderDay);
        Assert.NotNull(closedNotice);
        Assert.Contains("mourning", closedNotice!, StringComparison.OrdinalIgnoreCase);
        DaggerfallBuildingEntry lockedEntry = DaggerfallBuildingEntryPolicy.Evaluate(new(
            building.Source.BuildingType, building.Source.FactionId, building.Quality,
            Hour: 12, HolidayId: 0, ClosedOverrideNotice: closedNotice));
        Assert.False(lockedEntry.Unlocked);

        // Save/load mid-bereavement preserves closure and pending kin inheritance.
        RulesetSavePayload bereavementSave = session.CaptureSave();
        DaggerfallSavePayload rawSave = DaggerfallSavePayload.Read(bereavementSave);
        Assert.Single(rawSave.ShopContinuity.Premises);

        using DaggerfallSession restored = fixture.Restore(bereavementSave);
        Assert.NotNull(restored.ShopContinuity.GetClosedNotice(tavern.Profile.Site!.Value, building.Id, murderDay));

        // Still closed on day + 1.
        restored.AdvanceElapsedTime(1L * 24L * 60L * 60L, idleFatigue: false);
        Assert.NotNull(restored.ShopContinuity.GetClosedNotice(tavern.Profile.Site!.Value, building.Id, checked((int)restored.Calendar.DayNumber)));

        // Day + 2: bereavement ends -> door reopens, surviving kin inherits the counter with grieving dialogue & tavern service!
        restored.AdvanceElapsedTime(1L * 24L * 60L * 60L, idleFatigue: false);
        int reopenDay = checked((int)restored.Calendar.DayNumber);
        Assert.Null(restored.ShopContinuity.GetClosedNotice(tavern.Profile.Site!.Value, building.Id, reopenDay));
        Assert.True(restored.ShopContinuity.TryGetPremises(tavern.Profile.ProfileKey.LogicalId, out DaggerfallShopPremisesSnapshot inherited));
        Assert.Equal(DaggerfallShopContinuityState.KinInherited, inherited.State);
        Assert.Equal(survivingKinId, inherited.ReplacementActorId);
        Assert.Equal("kin", inherited.ReplacementOriginKind);

        // Surviving kin is materialized behind the counter with tavern services and zero duplicate bodies!
        DaggerfallNpc heirNpc = restored.State.Npcs.Require(survivingKinId);
        Assert.Contains(DaggerfallNpcServiceFacts.TavernService, heirNpc.Services);
        Assert.Single(restored.State.Actors.All, a => a.DurableId == survivingKinId);

        DaggerfallActivationTarget heirTarget = Assert.Single(
            restored.Dialogue.NpcTargets(),
            candidate => checked((long)candidate.Identity.Value) == survivingKinId);
        DaggerfallActivationOutcome opened = restored.Dialogue.ActivateNpc(new(DaggerfallActivationMode.Talk, heirTarget));
        Assert.True(opened.Applied, opened.Message);
        DaggerfallDialogueView heirView = Assert.IsType<DaggerfallDialogueView>(restored.ActivationPanel.View.Dialogue);
        Assert.Contains("taken over the counter", heirView.Greeting, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("grieves", heirView.Greeting, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(heirView.Tavern);
        restored.Dialogue.Close();

        // Kin inheritance survives another save/load cleanly.
        using DaggerfallSession restoredAfterInherit = fixture.Restore(restored.CaptureSave());
        Assert.True(restoredAfterInherit.ShopContinuity.TryGetPremises(tavern.Profile.ProfileKey.LogicalId, out DaggerfallShopPremisesSnapshot savedInherit));
        Assert.Equal(DaggerfallShopContinuityState.KinInherited, savedInherit.State);
        Assert.Single(restoredAfterInherit.State.Actors.All, a => a.DurableId == survivingKinId);
    }

    [Fact]
    public void Murdering_shop_proprietor_with_no_living_kin_holds_vacancy_then_appoints_non_kin_town_replacement_keeping_commerce_functional()
    {
        using var fixture = SourceBackedServiceSessionFixture.Create();
        var tavern = fixture.TavernProvider();
        DaggerfallSiteBuildingSource building = fixture.SourceBuilding(tavern.Profile);
        using DaggerfallSession session = fixture.Start(tavern.Profile);

        DaggerfallNpc proprietor = SourceNpc(session, tavern.Placement.Id);
        // Isolate proprietor into a household with zero surviving kin.
        const string lonelyFamily = "fam:local:r17:s4:x99y99b9";
        DaggerfallNpc lonelyProp = proprietor with { FamilyId = lonelyFamily };
        session.State.Actors.Get(proprietor.DurableId).Actor.Get<ActorPersonComponent>().Snapshot = lonelyProp;

        int murderDay = checked((int)session.Calendar.DayNumber);
        ActorState propActor = session.State.Actors.Get(proprietor.DurableId);
        propActor.Stats.GetTrack(TrackId.Parse("health")).SetCurrent(0, clamp: true);
        session.State.ActorLedger.Upsert(new ActorLedgerEntry(
            proprietor.DurableId,
            ActorOrigin.Seeded(tavern.Profile.ProfileKey, proprietor.StableKey),
            Location: ActorLocationOverride.Dead(
                tavern.Profile.ProfileKey,
                new ActorPoseOverride(propActor.Position.X, propActor.Position.Y, propActor.Position.Z, 0f))));

        DaggerfallShopPremisesSnapshot? afterDeath = session.ShopContinuity.ReactToProprietorDeath(
            proprietor.DurableId, lonelyProp, tavern.Profile.ProfileKey, tavern.Profile, murderDay);

        Assert.NotNull(afterDeath);
        Assert.Equal(DaggerfallShopContinuityState.TownReplacementPending, afterDeath!.Value.State);
        Assert.Equal(murderDay + 5, afterDeath.Value.ClosedUntilDay);

        // Still closed on day + 2 and day + 4 (waiting out the 5-day town vacancy).
        session.AdvanceElapsedTime(4L * 24L * 60L * 60L, idleFatigue: false);
        string? vacancyNotice = session.ShopContinuity.GetClosedNotice(tavern.Profile.Site!.Value, building.Id, checked((int)session.Calendar.DayNumber));
        Assert.NotNull(vacancyNotice);
        Assert.Contains("new proprietor", vacancyNotice!, StringComparison.OrdinalIgnoreCase);

        // Day + 5: newly appointed non-kin town replacement takes over and reopens commerce!
        session.AdvanceElapsedTime(1L * 24L * 60L * 60L, idleFatigue: false);
        int reopenDay = checked((int)session.Calendar.DayNumber);
        Assert.Null(session.ShopContinuity.GetClosedNotice(tavern.Profile.Site!.Value, building.Id, reopenDay));
        Assert.True(session.ShopContinuity.TryGetPremises(tavern.Profile.ProfileKey.LogicalId, out DaggerfallShopPremisesSnapshot replaced));
        Assert.Equal(DaggerfallShopContinuityState.TownReplaced, replaced.State);
        Assert.Equal("town", replaced.ReplacementOriginKind);
        Assert.NotNull(replaced.ReplacementActorId);
        Assert.NotEqual(proprietor.DurableId, replaced.ReplacementActorId!.Value);

        long replacementId = replaced.ReplacementActorId.Value;
        DaggerfallNpc repNpc = session.State.Npcs.Require(replacementId);
        Assert.Contains(DaggerfallNpcServiceFacts.TavernService, repNpc.Services);
        Assert.NotEqual(lonelyFamily, repNpc.FamilyId);
        Assert.False(session.SocialConsequences.IsActorMourning(replacementId, repNpc));

        DaggerfallActivationTarget repTarget = Assert.Single(
            session.Dialogue.NpcTargets(),
            candidate => checked((long)candidate.Identity.Value) == replacementId);
        DaggerfallActivationOutcome opened = session.Dialogue.ActivateNpc(new(DaggerfallActivationMode.Talk, repTarget));
        Assert.True(opened.Applied, opened.Message);
        DaggerfallDialogueView repView = Assert.IsType<DaggerfallDialogueView>(session.ActivationPanel.View.Dialogue);
        Assert.Contains("town appointed me", repView.Greeting, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(repView.Tavern);
        session.Dialogue.Close();

        // Persists across save/load cleanly with replacement live behind the counter.
        using DaggerfallSession restored = fixture.Restore(session.CaptureSave());
        Assert.True(restored.State.Actors.TryGet(replacementId, out ActorState? restoredRep));
        Assert.False(restoredRep.IsDefeated);
    }

    [Fact]
    public void Arresting_shop_proprietor_provides_temporary_cover_while_jailed_and_returns_original_on_release_unless_exiled()
    {
        using var fixture = SourceBackedServiceSessionFixture.Create();
        var tavern = fixture.TavernProvider();
        DaggerfallSiteBuildingSource building = fixture.SourceBuilding(tavern.Profile);
        using DaggerfallSession session = fixture.Start(tavern.Profile);

        DaggerfallNpc proprietor = SourceNpc(session, tavern.Placement.Id);
        int arrestDay = checked((int)session.Calendar.DayNumber);

        // Non-fatal arrest of proprietor: removed into OffStage custody, premises stays OPEN under temporary cover!
        DaggerfallNpcArrestOutcome arrest = session.Law.ArrestOrImprisonNpc(
            proprietor.DurableId, tavern.Profile.Site!.Value.Region, DaggerfallCrimeKind.Theft, banished: false, detail: "tax warrant");
        Assert.True(arrest.Imprisoned);
        Assert.False(session.State.Actors.TryGet(proprietor.DurableId, out _));
        Assert.Null(session.ShopContinuity.GetClosedNotice(tavern.Profile.Site!.Value, building.Id, arrestDay));

        Assert.True(session.ShopContinuity.TryGetPremises(tavern.Profile.ProfileKey.LogicalId, out DaggerfallShopPremisesSnapshot covered));
        Assert.Equal(DaggerfallShopContinuityState.ArrestCovered, covered.State);
        Assert.Equal(arrestDay + 5, covered.JailedUntilDay);
        Assert.NotNull(covered.ReplacementActorId);

        long coverId = covered.ReplacementActorId!.Value;
        DaggerfallNpc coverNpc = session.State.Npcs.Require(coverId);
        Assert.Contains(DaggerfallNpcServiceFacts.TavernService, coverNpc.Services);

        // Cover greets as temporary minder and serves customers while proprietor is jailed.
        DaggerfallActivationTarget coverTarget = Assert.Single(
            session.Dialogue.NpcTargets(),
            candidate => checked((long)candidate.Identity.Value) == coverId);
        DaggerfallActivationOutcome opened = session.Dialogue.ActivateNpc(new(DaggerfallActivationMode.Talk, coverTarget));
        Assert.True(opened.Applied, opened.Message);
        DaggerfallDialogueView coverView = Assert.IsType<DaggerfallDialogueView>(session.ActivationPanel.View.Dialogue);
        Assert.Contains("minding the counter", coverView.Greeting, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(coverView.Tavern);
        session.Dialogue.Close();

        // Persists across save/load mid-sentence.
        using DaggerfallSession restored = fixture.Restore(session.CaptureSave());
        Assert.True(restored.State.Actors.TryGet(coverId, out _));
        Assert.False(restored.State.Actors.TryGet(proprietor.DurableId, out _));

        // After the 5-day jail sentence expires, original proprietor returns behind the counter, cover leaves,
        // and ShopContinuity prunes back to zero save bytes!
        restored.AdvanceElapsedTime(5L * 24L * 60L * 60L, idleFatigue: false);
        Assert.True(restored.State.Actors.TryGet(proprietor.DurableId, out ActorState? returnedProp));
        Assert.False(returnedProp.IsDefeated);
        Assert.False(restored.State.Actors.TryGet(coverId, out _));
        Assert.False(restored.ShopContinuity.TryGetPremises(tavern.Profile.ProfileKey.LogicalId, out _));
        Assert.Empty(DaggerfallSavePayload.Read(restored.CaptureSave()).ShopContinuity.Premises);

        // Banished arrest does NOT return the original; permanently transitions to replacement instead.
        DaggerfallNpcArrestOutcome banishedArrest = restored.Law.ArrestOrImprisonNpc(
            proprietor.DurableId, tavern.Profile.Site!.Value.Region, DaggerfallCrimeKind.HighTreason, banished: true, detail: "banished");
        Assert.True(banishedArrest.Imprisoned);
        Assert.True(restored.ShopContinuity.TryGetPremises(tavern.Profile.ProfileKey.LogicalId, out DaggerfallShopPremisesSnapshot afterBanish));
        Assert.True(afterBanish.State is DaggerfallShopContinuityState.KinInherited or DaggerfallShopContinuityState.TownReplaced);
        restored.AdvanceElapsedTime(10L * 24L * 60L * 60L, idleFatigue: false);
        Assert.False(restored.State.Actors.TryGet(proprietor.DurableId, out _));
    }

    [Fact]
    public void Arresting_town_or_kin_successor_proprietor_returns_them_to_counter_with_permanent_succession_preserved_across_save_load()
    {
        // 1. Town replacement successor arrested -> covered -> released back to counter with TownReplaced preserved across save/load!
        using (var fixture = SourceBackedServiceSessionFixture.Create())
        {
            var tavern = fixture.TavernProvider();
            using DaggerfallSession session = fixture.Start(tavern.Profile);
            DaggerfallNpc proprietor = SourceNpc(session, tavern.Placement.Id);

            const string lonelyFamily = "fam:local:r17:s4:x98y98b8";
            DaggerfallNpc lonelyProp = proprietor with { FamilyId = lonelyFamily };
            session.State.Actors.Get(proprietor.DurableId).Actor.Get<ActorPersonComponent>().Snapshot = lonelyProp;

            ActorState propActor = session.State.Actors.Get(proprietor.DurableId);
            propActor.Stats.GetTrack(TrackId.Parse("health")).SetCurrent(0, clamp: true);
            session.State.ActorLedger.Upsert(new ActorLedgerEntry(
                proprietor.DurableId,
                ActorOrigin.Seeded(tavern.Profile.ProfileKey, proprietor.StableKey),
                Location: ActorLocationOverride.Dead(
                    tavern.Profile.ProfileKey,
                    new ActorPoseOverride(propActor.Position.X, propActor.Position.Y, propActor.Position.Z, 0f))));
            _ = session.ShopContinuity.ReactToProprietorDeath(
                proprietor.DurableId, lonelyProp, tavern.Profile.ProfileKey, tavern.Profile, checked((int)session.Calendar.DayNumber));

            // Advance 5 days so town replacement takes over.
            session.AdvanceElapsedTime(5L * 24L * 60L * 60L, idleFatigue: false);
            Assert.True(session.ShopContinuity.TryGetPremises(tavern.Profile.ProfileKey.LogicalId, out DaggerfallShopPremisesSnapshot townSnap));
            Assert.Equal(DaggerfallShopContinuityState.TownReplaced, townSnap.State);
            long townSuccessorId = townSnap.ReplacementActorId!.Value;
            Assert.True(session.State.Actors.TryGet(townSuccessorId, out _));

            // Arrest the town successor (non-fatal) -> temporary cover takes the counter.
            DaggerfallNpcArrestOutcome successorArrest = session.Law.ArrestOrImprisonNpc(
                townSuccessorId, tavern.Profile.Site!.Value.Region, DaggerfallCrimeKind.Theft, banished: false, detail: "successor warrant");
            Assert.True(successorArrest.Imprisoned);
            Assert.False(session.State.Actors.TryGet(townSuccessorId, out _));
            Assert.True(session.ShopContinuity.TryGetPremises(tavern.Profile.ProfileKey.LogicalId, out DaggerfallShopPremisesSnapshot duringJail));
            Assert.Equal(DaggerfallShopContinuityState.ArrestCovered, duringJail.State);
            long tempCoverId = duringJail.ReplacementActorId!.Value;
            Assert.NotEqual(townSuccessorId, tempCoverId);
            Assert.True(session.State.Actors.TryGet(tempCoverId, out _));

            // Save/load mid-sentence, then advance 5 days -> town successor returns behind the counter with TownReplaced preserved!
            using DaggerfallSession restored = fixture.Restore(session.CaptureSave());
            restored.AdvanceElapsedTime(5L * 24L * 60L * 60L, idleFatigue: false);
            Assert.True(restored.State.Actors.TryGet(townSuccessorId, out ActorState? returnedSuccessor));
            Assert.False(returnedSuccessor.IsDefeated);
            Assert.False(restored.State.Actors.TryGet(tempCoverId, out _));
            Assert.True(restored.ShopContinuity.TryGetPremises(tavern.Profile.ProfileKey.LogicalId, out DaggerfallShopPremisesSnapshot afterRelease));
            Assert.Equal(DaggerfallShopContinuityState.TownReplaced, afterRelease.State);
            Assert.Equal(townSuccessorId, afterRelease.ReplacementActorId);
            Assert.Contains(DaggerfallNpcServiceFacts.TavernService, restored.State.Npcs.Require(townSuccessorId).Services);

            // Survives another save/load cleanly with town successor still behind the counter.
            using DaggerfallSession reloaded = fixture.Restore(restored.CaptureSave());
            Assert.True(reloaded.State.Actors.TryGet(townSuccessorId, out _));
            Assert.True(reloaded.ShopContinuity.TryGetPremises(tavern.Profile.ProfileKey.LogicalId, out DaggerfallShopPremisesSnapshot reloadedSnap));
            Assert.Equal(DaggerfallShopContinuityState.TownReplaced, reloadedSnap.State);
        }

        // 2. Kin successor (from another slot) arrested -> covered -> released back to the INHERITED counter (not old home slot)!
        using (var fixture = SourceBackedServiceSessionFixture.Create())
        {
            var tavern = fixture.TavernProvider();
            DaggerfallSiteBuildingSource building = fixture.SourceBuilding(tavern.Profile);
            using DaggerfallSession session = fixture.Start(tavern.Profile);
            DaggerfallNpc proprietor = SourceNpc(session, tavern.Placement.Id);

            string familyId = DaggerfallHouseholds.BuildingHouseholdId(tavern.Profile.Site!.Value, building.Id.BlockX, building.Id.BlockY, building.Id.Index);
            DaggerfallNpc updatedProp = proprietor with { FamilyId = familyId };
            session.State.Actors.Get(proprietor.DurableId).Actor.Get<ActorPersonComponent>().Snapshot = updatedProp;

            DaggerfallNpcSite localSite = new(tavern.Profile.Site!.Value.Region, proprietor.Site.Location,
                $"{building.Id.BlockX}/{building.Id.BlockY}/{building.Id.Index}", tavern.Profile.ProfileKey.LogicalId);
            long kinId = AddNpcWithFamily(session, tavern.Profile.Site!.Value, localSite, "t11161/kin-heir-r2", "Maren Greaves", familyId);

            ActorState propActor = session.State.Actors.Get(proprietor.DurableId);
            propActor.Stats.GetTrack(TrackId.Parse("health")).SetCurrent(0, clamp: true);
            session.State.ActorLedger.Upsert(new ActorLedgerEntry(
                proprietor.DurableId,
                ActorOrigin.Seeded(tavern.Profile.ProfileKey, proprietor.StableKey),
                Location: ActorLocationOverride.Dead(
                    tavern.Profile.ProfileKey,
                    new ActorPoseOverride(propActor.Position.X, propActor.Position.Y, propActor.Position.Z, 0f))));
            _ = session.ShopContinuity.ReactToProprietorDeath(
                proprietor.DurableId, updatedProp, tavern.Profile.ProfileKey, tavern.Profile, checked((int)session.Calendar.DayNumber));

            session.AdvanceElapsedTime(2L * 24L * 60L * 60L, idleFatigue: false);
            Assert.True(session.ShopContinuity.TryGetPremises(tavern.Profile.ProfileKey.LogicalId, out DaggerfallShopPremisesSnapshot kinSnap));
            Assert.Equal(DaggerfallShopContinuityState.KinInherited, kinSnap.State);
            Assert.Equal(kinId, kinSnap.ReplacementActorId);

            // Arrest the kin successor -> temporary cover -> save/load -> release returns kin to the inherited counter slot!
            _ = session.Law.ArrestOrImprisonNpc(kinId, tavern.Profile.Site!.Value.Region, DaggerfallCrimeKind.Theft, banished: false, detail: "kin warrant");
            Assert.False(session.State.Actors.TryGet(kinId, out _));
            using DaggerfallSession restored = fixture.Restore(session.CaptureSave());
            restored.AdvanceElapsedTime(5L * 24L * 60L * 60L, idleFatigue: false);
            Assert.True(restored.State.Actors.TryGet(kinId, out _));
            Assert.True(restored.ShopContinuity.TryGetPremises(tavern.Profile.ProfileKey.LogicalId, out DaggerfallShopPremisesSnapshot kinRestored));
            Assert.Equal(DaggerfallShopContinuityState.KinInherited, kinRestored.State);
            Assert.Equal(kinId, kinRestored.ReplacementActorId);
            Assert.True(restored.State.ActorLedger.TryGet(kinId, out ActorLedgerEntry? kinLedger));
            Assert.Equal(ActorLocationKind.AtSite, kinLedger!.Location?.Kind);
            Assert.Equal(proprietor.StableKey, kinLedger.Location?.TargetSlotId);
        }
    }

    [Fact]
    public void Arresting_temporary_counter_cover_preserves_original_proprietor_release_schedule_and_releases_both_across_save_load()
    {
        using var fixture = SourceBackedServiceSessionFixture.Create();
        var tavern = fixture.TavernProvider();
        DaggerfallSiteBuildingSource building = fixture.SourceBuilding(tavern.Profile);
        using DaggerfallSession session = fixture.Start(tavern.Profile);
        DaggerfallNpc proprietor = SourceNpc(session, tavern.Placement.Id);

        // Give proprietor a living adult kin so the first arrest cover is a real kin member.
        string familyId = DaggerfallHouseholds.BuildingHouseholdId(tavern.Profile.Site!.Value, building.Id.BlockX, building.Id.BlockY, building.Id.Index);
        DaggerfallNpc updatedProp = proprietor with { FamilyId = familyId };
        session.State.Actors.Get(proprietor.DurableId).Actor.Get<ActorPersonComponent>().Snapshot = updatedProp;
        DaggerfallNpcSite localSite = new(tavern.Profile.Site!.Value.Region, proprietor.Site.Location,
            $"{building.Id.BlockX}/{building.Id.BlockY}/{building.Id.Index}", tavern.Profile.ProfileKey.LogicalId);
        long firstKinCoverId = AddNpcWithFamily(session, tavern.Profile.Site!.Value, localSite, "t11161/seq-kin-cover", "Bram Greaves", familyId);

        int day0 = checked((int)session.Calendar.DayNumber);

        // Day 0: Arrest the original proprietor (release due on day0 + 5); kin steps in as temporary cover.
        _ = session.Law.ArrestOrImprisonNpc(proprietor.DurableId, tavern.Profile.Site!.Value.Region, DaggerfallCrimeKind.Theft, banished: false, detail: "first warrant");
        Assert.True(session.ShopContinuity.TryGetPremises(tavern.Profile.ProfileKey.LogicalId, out DaggerfallShopPremisesSnapshot afterFirst));
        Assert.Equal(proprietor.DurableId, afterFirst.OriginalProprietorId);
        Assert.Equal(day0 + 5, afterFirst.JailedUntilDay);
        Assert.Equal(firstKinCoverId, afterFirst.ReplacementActorId);

        // Day +2: Arrest the temporary kin cover BEFORE the original's sentence expires!
        session.AdvanceElapsedTime(2L * 24L * 60L * 60L, idleFatigue: false);
        _ = session.Law.ArrestOrImprisonNpc(firstKinCoverId, tavern.Profile.Site!.Value.Region, DaggerfallCrimeKind.Assault, banished: false, detail: "cover warrant");

        // Original proprietor's identity and day0 + 5 release schedule MUST be preserved; a fresh cover takes the counter!
        Assert.True(session.ShopContinuity.TryGetPremises(tavern.Profile.ProfileKey.LogicalId, out DaggerfallShopPremisesSnapshot afterSecond));
        Assert.Equal(DaggerfallShopContinuityState.ArrestCovered, afterSecond.State);
        Assert.Equal(proprietor.DurableId, afterSecond.OriginalProprietorId);
        Assert.Equal(day0 + 5, afterSecond.JailedUntilDay);
        Assert.NotNull(afterSecond.ReplacementActorId);
        long secondCoverId = afterSecond.ReplacementActorId!.Value;
        Assert.NotEqual(firstKinCoverId, secondCoverId);
        Assert.NotEqual(proprietor.DurableId, secondCoverId);
        Assert.True(session.State.Actors.TryGet(secondCoverId, out _));
        Assert.False(session.State.Actors.TryGet(firstKinCoverId, out _));

        // Save/load preserves BOTH release timers!
        using DaggerfallSession restored = fixture.Restore(session.CaptureSave());

        // Day +5 (3 more days): Original proprietor returns behind the counter and second cover leaves;
        // firstKinCoverId is STILL serving until day0 + 7!
        restored.AdvanceElapsedTime(3L * 24L * 60L * 60L, idleFatigue: false);
        Assert.True(restored.State.Actors.TryGet(proprietor.DurableId, out _));
        Assert.False(restored.State.Actors.TryGet(secondCoverId, out _));
        Assert.False(restored.ShopContinuity.TryGetPremises(tavern.Profile.ProfileKey.LogicalId, out _));
        Assert.True(restored.State.ActorLedger.TryGet(firstKinCoverId, out ActorLedgerEntry? kinStillJailed));
        Assert.Equal(ActorLocationKind.OffStage, kinStillJailed!.Location?.Kind);

        // Day +7 (2 more days): Arrested kin cover is released from OffStage custody with original role restored,
        // and ShopContinuity prunes completely back to zero save bytes!
        restored.AdvanceElapsedTime(2L * 24L * 60L * 60L, idleFatigue: false);
        Assert.True(restored.State.ActorLedger.TryGet(firstKinCoverId, out ActorLedgerEntry? kinReleased));
        Assert.Null(kinReleased!.Location);
        Assert.Equal("civilian", kinReleased.Social?.Role);
        DaggerfallSavePayload finalSave = DaggerfallSavePayload.Read(restored.CaptureSave());
        Assert.Empty(finalSave.ShopContinuity.Premises);
        Assert.True(finalSave.ShopContinuity.JailedCovers is null or { Length: 0 });
    }

    [Fact]
    public void Untouched_shops_write_zero_ShopContinuity_save_bytes_and_social_site_reports_active_continuity_state()
    {
        using var fixture = SourceBackedServiceSessionFixture.Create();
        var tavern = fixture.TavernProvider();
        using DaggerfallSession session = fixture.Start(tavern.Profile);

        // Untouched session writes zero ShopContinuity records.
        DaggerfallSavePayload untouched = DaggerfallSavePayload.Read(session.CaptureSave());
        Assert.Empty(untouched.ShopContinuity.Premises);

        // Arresting proprietor populates social.site shopContinuity readout.
        DaggerfallNpc proprietor = SourceNpc(session, tavern.Placement.Id);
        session.Law.ArrestOrImprisonNpc(
            proprietor.DurableId, tavern.Profile.Site!.Value.Region, DaggerfallCrimeKind.Theft, banished: false, detail: "inspection check");

        DebugCommandResult siteReply = session.Commands<IDeveloperSocialCommands>().Site();
        Assert.Equal(DebugCommandStatus.Success, siteReply.Status);
        using JsonDocument doc = JsonDocument.Parse(siteReply.Message);
        JsonElement continuity = doc.RootElement.GetProperty("shopContinuity");
        Assert.Equal(1, continuity.GetArrayLength());
        Assert.Equal("ArrestCovered", continuity[0].GetProperty("state").GetString());
        Assert.Equal(proprietor.DurableId, continuity[0].GetProperty("originalProprietorId").GetInt64());
    }

    private static DaggerfallNpc SourceNpc(DaggerfallSession session, string slotId) =>
        session.State.Npcs.All.Single(n => n.StableKey == slotId);

    private static long AddNpcWithFamily(
        DaggerfallSession session,
        DaggerfallSiteId siteId,
        DaggerfallNpcSite npcSite,
        string slotId,
        string displayName,
        string? familyId,
        string role = "civilian",
        ActorInteractionPolicyKind interaction = ActorInteractionPolicyKind.FullDialogue)
    {
        DaggerfallWorldProfileKey profile = session.Sites.ActiveProfile;
        long id = DaggerfallPeopleSlots.SeededSlotIdentity(siteId, $"{profile.LogicalId}/{slotId}");
        DaggerfallNpcAppearance look = new("breton", "Male", 182, 0, Math.Abs(slotId.GetHashCode(StringComparison.Ordinal)), 518);
        session.State.Npcs.RegisterDeterministic(
            id, DaggerfallNpcKind.Static, slotId, npcSite, look, role, ["talk"],
            displayName: displayName, homeProfile: profile, familyId: familyId);
        DaggerfallNpc npc = session.State.Npcs.Require(id);
        WorldPoint pose = new(2, 0, -2);
        session.Roster.MaterializeDeterministicCivilian(
            npc,
            ActorOrigin.Seeded(profile, slotId, interactionPolicy: interaction),
            pose,
            new ActorPose(pose, 0f),
            Vector3.Zero,
            null);
        if (!string.IsNullOrWhiteSpace(familyId)
            && session.State.Actors.TryGet(id, out ActorState? matActor)
            && matActor.Actor.TryGet<ActorOverlayComponent>(out ActorOverlayComponent? matOverlay))
        {
            matOverlay.FamilyId = familyId;
        }
        session.State.Npcs.Bind(id, profile);
        return id;
    }

    private sealed class MourningSessionFixture : IDisposable
    {
        private readonly DaggerfallSiteProfile _profile;
        private readonly List<string> _releases = [];
        private readonly ContentFake _content;
        private readonly SpatialFake _spatial;

        internal PerceptionFake Perception { get; } = PerceptionFake.Create();
        internal DaggerfallSession Session { get; }

        internal MourningSessionFixture()
        {
            _profile = ReadInputs(TestData.RepositoryRoot);
            _content = new ContentFake(_releases);
            PopulateContent(_content, _profile);
            _spatial = SpatialFake.Create(_profile.Geometry.Spatial[0].Sha256, _releases);
            EngineContextFake engine = EngineContextFake.Create(_content, _spatial.Service, new AppearanceFake(_releases), Perception.Service);
            Session = DaggerfallSession.StartNew(engine.Context, new(TestPayload.Definitions, _profile, DaggerfallTuning.Defaults));
        }

        internal DaggerfallSession Restore(RulesetSavePayload save)
        {
            EngineContextFake engine = EngineContextFake.Create(_content, _spatial.Service, new AppearanceFake(_releases), Perception.Service);
            return DaggerfallSession.Restore(engine.Context, new(TestPayload.Definitions, _profile, DaggerfallTuning.Defaults), save);
        }

        public void Dispose() => Session.Dispose();
    }
}
