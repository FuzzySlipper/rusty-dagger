using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Effects;
using WorldRpg.Kit.Inventory;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Behavior;
using WorldRpg.Rulesets.Daggerfall.Travel;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Focused tests for #11018: persistent <see cref="ActorLocationKind.OnOverlandBand"/> travelling identities
/// alongside unsaved generated <see cref="DaggerfallExpeditionBand"/> chasers, home <c>SeededSlot</c> suppression,
/// intercept/arrival materialization with zero duplicate bodies, and cross-boundary <see cref="ActorLocationKind.WithParty"/>
/// staging for drivers/companions.
/// </summary>
public sealed class OverlandBandContinuityTests
{
    [Fact]
    public void Persistent_OnOverlandBand_survives_map_close_reopen_and_save_reload_alongside_unsaved_generated_chasers()
    {
        using TwoTownFixture fixture = TwoTownFixture.Create();
        DaggerfallSession session = fixture.Session;

        // Materialize Charing daytime population and pick a real deterministic SeededSlot townsperson.
        session.AdvanceElapsedTime(8 * 60 * 60);
        DaggerfallNpc traveller = session.State.Npcs.All.First(npc =>
            npc.Kind == DaggerfallNpcKind.Civilian
            && npc.Profile == fixture.Origin.ProfileKey
            && DaggerfallPeopleSlots.IsDeterministicId(npc.DurableId)
            && session.State.Actors.TryGet(npc.DurableId, out _));
        long travellerId = traveller.DurableId;

        DaggerfallTravelMapPixel originPixel = new(fixture.OriginRecord.MapPixelX, fixture.OriginRecord.MapPixelY);
        DaggerfallTravelMapPixel destPixel = new(fixture.DestinationRecord.MapPixelX, fixture.DestinationRecord.MapPixelY);

        // Dispatch onto persistent OnOverlandBand heading toward the destination town.
        DaggerfallPersistentOverlandBand persistent = session.OverlandParties.DispatchOverlandBand(
            bandId: "caravan-north",
            name: "Northern Merchant Caravan",
            kind: DaggerfallBandKind.Travellers,
            memberIds: [travellerId],
            startX: originPixel.X + 1.5,
            startY: originPixel.Y + 0.5,
            speedPixelsPerHour: 0.4,
            destination: new DaggerfallExpeditionMark(destPixel, fixture.DestinationRecord.Id),
            destinationProfile: fixture.Destination.ProfileKey);

        // Home SeededSlot is suppressed while travelling: absent from EntityStore and QueryNearby.
        Assert.True(session.State.ActorLedger.IsHomeSlotSuppressed(travellerId, fixture.Origin.ProfileKey));
        Assert.False(session.State.Actors.TryGet(travellerId, out _));
        Assert.DoesNotContain(session.QueryNearby(session.State.PlayerControl.Position!.Value, radius: 500d), hit => hit.Actor.DurableId == travellerId);

        // Reconciling population while in transit NEVER duplicates the home SeededSlot body.
        session.AdvanceElapsedTime(60);
        Assert.False(session.State.Actors.TryGet(travellerId, out _));

        // Open expedition map and stage an unsaved generated chaser alongside the persistent band.
        session.OpenExpedition();
        Assert.True(session.Expedition.Open);
        string? stageRefusal = null;
        foreach (double bearing in new[] { 0d, 45d, 90d, 135d, 180d, 225d, 270d, 315d })
        {
            stageRefusal = session.Expedition.StageBand(distance: 2.0, bearing: bearing, camp: false);
            if (stageRefusal is null) break;
        }
        Assert.Null(stageRefusal);

        // Both the persistent OnOverlandBand and the unsaved generated chaser coexist in AllBands.
        Assert.Contains(session.Expedition.AllBands, b => b.PersistentBandId == "caravan-north");
        Assert.Contains(session.Expedition.AllBands, b => b.PersistentBandId is null);

        // Advance one stretch on the map: persistent band moves toward destination.
        double beforeX = persistent.X;
        double beforeY = persistent.Y;
        session.Expedition.SetMode(DaggerfallExpeditionMode.Halt);
        session.Expedition.SetSpeed(1);
        _ = session.Expedition.Advance(0.25);
        Assert.True(Math.Abs(persistent.X - beforeX) > 1e-6 || Math.Abs(persistent.Y - beforeY) > 1e-6);
        double movedX = persistent.X;
        double movedY = persistent.Y;

        // Close and reopen the expedition map: transient chaser clears, persistent band survives intact!
        session.Expedition.Close();
        session.OpenExpedition();
        Assert.DoesNotContain(session.Expedition.AllBands, b => b.PersistentBandId is null);
        DaggerfallExpeditionBandView reopenedPersistent = Assert.Single(session.Expedition.AllBands, b => b.PersistentBandId == "caravan-north");
        Assert.Equal(movedX, reopenedPersistent.X, precision: 6);
        Assert.Equal(movedY, reopenedPersistent.Y, precision: 6);

        // Save and reload: persistent band + OnOverlandBand ledger entry survive, transient chaser is absent.
        RulesetSavePayload savedBytes = session.CaptureSave();
        DaggerfallSavePayload decoded = DaggerfallSavePayload.Read(savedBytes);
        DaggerfallPersistentBandSave savedBand = Assert.Single(decoded.Expedition.PersistentBands!);
        Assert.Equal("caravan-north", savedBand.BandId);
        Assert.Equal(movedX, savedBand.X, precision: 6);
        Assert.Equal(movedY, savedBand.Y, precision: 6);
        ActorLedgerEntry savedMember = Assert.Single(decoded.ActorLedger, e => e.DurableId == travellerId);
        Assert.Equal(ActorLocationKind.OnOverlandBand, savedMember.Location?.Kind);
        Assert.Equal("caravan-north", savedMember.Location?.BandId);

        using DaggerfallSession restored = DaggerfallSession.Restore(fixture.CreateEngine().Context, fixture.Composition, savedBytes);
        DaggerfallPersistentOverlandBand restoredBand = Assert.Single(restored.Expedition.PersistentBands);
        Assert.Equal("caravan-north", restoredBand.BandId);
        Assert.Equal(movedX, restoredBand.X, precision: 6);
        Assert.Equal(movedY, restoredBand.Y, precision: 6);
        Assert.True(restored.State.ActorLedger.IsHomeSlotSuppressed(travellerId, fixture.Origin.ProfileKey));
        Assert.False(restored.State.Actors.TryGet(travellerId, out _));
    }

    [Fact]
    public void Intercepting_persistent_OnOverlandBand_materializes_exact_ledger_actor_in_EntityStore_with_zero_duplicates()
    {
        using TwoTownFixture fixture = TwoTownFixture.Create();
        DaggerfallSession session = fixture.Session;

        session.AdvanceElapsedTime(8 * 60 * 60);
        DaggerfallNpc traveller = session.State.Npcs.All.First(npc =>
            npc.Kind == DaggerfallNpcKind.Civilian
            && npc.Profile == fixture.Origin.ProfileKey
            && DaggerfallPeopleSlots.IsDeterministicId(npc.DurableId)
            && session.State.Actors.TryGet(npc.DurableId, out _));
        long travellerId = traveller.DurableId;
        session.State.Npcs.SetDisplayName(travellerId, "Joryn the Courier");
        session.State.Actors.Get(travellerId).Actor.Get<ActorOverlayComponent>().Met = true;

        DaggerfallTravelMapPixel originPixel = new(fixture.OriginRecord.MapPixelX, fixture.OriginRecord.MapPixelY);
        session.OpenExpedition();
        (double partyX, double partyY) = session.Expedition.Position;

        // Place persistent band right within meet distance of the party (travellers camped beside the road).
        session.OverlandParties.DispatchOverlandBand(
            bandId: "courier-intercept",
            name: "Joryn's Escort",
            kind: DaggerfallBandKind.Travellers,
            memberIds: [travellerId],
            startX: partyX + 0.1,
            startY: partyY,
            speedPixelsPerHour: 0.2,
            camp: true);

        Assert.False(session.State.Actors.TryGet(travellerId, out _));

        // Advancing one stretch stops on CameUpon for the persistent band.
        session.Expedition.SetMode(DaggerfallExpeditionMode.Halt);
        session.Expedition.SetSpeed(1);
        _ = session.Expedition.Advance(0.25);
        Assert.NotNull(session.Expedition.BandAhead);
        Assert.Equal("courier-intercept", session.Expedition.BandAhead!.PersistentBandId);

        // Enter the 3D scene from the expedition map: exact ledger actor materializes beside the player with 0 duplicates.
        session.EnterExpedition();
        Assert.False(session.Expedition.Open);
        Assert.Empty(session.Expedition.PersistentBands);

        Assert.True(session.State.Actors.TryGet(travellerId, out ActorState? materialized));
        Assert.Single(session.State.Actors.All, a => a.DurableId == travellerId);
        Assert.Equal("Joryn the Courier", session.State.Npcs.Require(travellerId).DisplayName);
        Assert.True(materialized!.Actor.Has<StatsComponent>());
        Assert.True(materialized.Actor.Get<ActorOverlayComponent>().Met);
        Assert.Contains(session.QueryNearby(session.State.PlayerControl.Position!.Value, radius: 15d), hit => hit.Actor.DurableId == travellerId);
    }

    [Fact]
    public void Persistent_OnOverlandBand_reaching_destination_delivers_member_to_destination_site_without_duplicating_home_slot()
    {
        using TwoTownFixture fixture = TwoTownFixture.Create();
        DaggerfallSession session = fixture.Session;

        session.AdvanceElapsedTime(8 * 60 * 60);
        DaggerfallNpc traveller = session.State.Npcs.All.First(npc =>
            npc.Kind == DaggerfallNpcKind.Civilian
            && npc.Profile == fixture.Origin.ProfileKey
            && DaggerfallPeopleSlots.IsDeterministicId(npc.DurableId)
            && session.State.Actors.TryGet(npc.DurableId, out _));
        long travellerId = traveller.DurableId;
        session.State.Npcs.SetDisplayName(travellerId, "Haldor the Factor");

        DaggerfallTravelMapPixel destPixel = new(fixture.DestinationRecord.MapPixelX, fixture.DestinationRecord.MapPixelY);

        // Start persistent band 0.05 pixels from its destination so one short map stretch arrives cleanly.
        session.OverlandParties.DispatchOverlandBand(
            bandId: "factor-delivery",
            name: "Haldor's Cart",
            kind: DaggerfallBandKind.Travellers,
            memberIds: [travellerId],
            startX: destPixel.X + 0.55,
            startY: destPixel.Y + 0.5,
            speedPixelsPerHour: 1.0,
            destination: new DaggerfallExpeditionMark(destPixel, fixture.DestinationRecord.Id),
            destinationProfile: fixture.Destination.ProfileKey);

        session.OpenExpedition();
        session.Expedition.SetMode(DaggerfallExpeditionMode.Halt);
        session.Expedition.SetSpeed(1);
        _ = session.Expedition.Advance(0.25);

        // Band arrived: removed from map, ledger member relocated to Destination, home slot still suppressed.
        Assert.Empty(session.Expedition.PersistentBands);
        Assert.True(session.State.ActorLedger.TryGet(travellerId, out ActorLedgerEntry? arrivedEntry));
        Assert.Equal(ActorLocationKind.AtSite, arrivedEntry!.Location?.Kind);
        Assert.Equal(fixture.Destination.ProfileKey, arrivedEntry.Location?.Profile?.Require());
        Assert.True(session.State.ActorLedger.IsHomeSlotSuppressed(travellerId, fixture.Origin.ProfileKey));
        Assert.False(session.State.Actors.TryGet(travellerId, out _));
        session.Expedition.Close();

        // Travel to destination town: exact ledger actor materializes at destination with zero duplicate bodies.
        Assert.True(session.TryTransitionTo(fixture.Destination.ProfileKey));
        Assert.True(session.State.Actors.TryGet(travellerId, out _));
        Assert.Single(session.State.Actors.All, a => a.DurableId == travellerId);
        Assert.Equal("Haldor the Factor", session.State.Npcs.Require(travellerId).DisplayName);
    }

    [Fact]
    public void WithParty_driver_stays_out_of_EntityStore_on_map_drives_portraits_and_stages_as_same_durable_actor_on_entry()
    {
        using TwoTownFixture fixture = TwoTownFixture.Create();
        DaggerfallSession session = fixture.Session;

        session.AdvanceElapsedTime(8 * 60 * 60);
        DaggerfallNpc driver = session.State.Npcs.All.First(npc =>
            npc.Kind == DaggerfallNpcKind.Civilian
            && npc.Profile == fixture.Origin.ProfileKey
            && DaggerfallPeopleSlots.IsDeterministicId(npc.DurableId)
            && session.State.Actors.TryGet(npc.DurableId, out _));
        long driverId = driver.DurableId;
        session.State.Npcs.SetDisplayName(driverId, "Bram the Driver");

        // Enlist as WithParty Driver: leaves EntityStore/QueryNearby, drives ledger portrait strip, suppresses home slot.
        session.OverlandParties.JoinParty(driverId, ActorPartyRole.Driver, portraitMedia: "face-driver-01");
        Assert.False(session.State.Actors.TryGet(driverId, out _));
        Assert.DoesNotContain(session.QueryNearby(session.State.PlayerControl.Position!.Value, radius: 500d), hit => hit.Actor.DurableId == driverId);
        Assert.True(session.State.ActorLedger.IsHomeSlotSuppressed(driverId, fixture.Origin.ProfileKey));
        DaggerfallPartyPortrait portrait = Assert.Single(session.OverlandParties.PartyPortraits());
        Assert.Equal(driverId, portrait.DurableId);
        Assert.Equal(ActorPartyRole.Driver, portrait.Role);
        Assert.Equal("Bram the Driver", portrait.Name);
        Assert.Equal("face-driver-01", portrait.MediaId);

        // Save and reload while WithParty: portrait & WithParty ledger state persist, still absent from EntityStore.
        RulesetSavePayload savedWithParty = session.CaptureSave();
        using DaggerfallSession restored = DaggerfallSession.Restore(fixture.CreateEngine().Context, fixture.Composition, savedWithParty);
        Assert.False(restored.State.Actors.TryGet(driverId, out _));
        Assert.Single(restored.OverlandParties.PartyPortraits(), p => p.DurableId == driverId && p.MediaId == "face-driver-01");

        // Open expedition map and enter on foot (camp / exterior landing): exact same durable driver stages into EntityStore!
        restored.OpenExpedition();
        Assert.False(restored.State.Actors.TryGet(driverId, out _));
        restored.EnterExpedition();

        Assert.True(restored.State.Actors.TryGet(driverId, out ActorState? stagedDriver));
        Assert.Single(restored.State.Actors.All, a => a.DurableId == driverId);
        Assert.Equal("Bram the Driver", restored.State.Npcs.Require(driverId).DisplayName);
        Assert.True(stagedDriver!.Actor.Has<StatsComponent>());
        Assert.Contains(restored.QueryNearby(restored.State.PlayerControl.Position!.Value, radius: 15d), hit => hit.Actor.DurableId == driverId);

        // Re-opening expedition boards the staged driver back into WithParty (out of EntityStore, back on HUD strip).
        restored.OpenExpedition();
        Assert.False(restored.State.Actors.TryGet(driverId, out _));
        Assert.Single(restored.OverlandParties.PartyPortraits(), p => p.DurableId == driverId && p.MediaId == "face-driver-01");
        Assert.True(restored.State.ActorLedger.TryGet(driverId, out ActorLedgerEntry? boarded));
        Assert.Equal(ActorLocationKind.WithParty, boarded!.Location?.Kind);
        Assert.Equal(ActorPartyRole.Driver, boarded.Location?.PartyRole);
    }

    [Fact]
    public void Developer_dispatch_persistent_command_and_tampered_save_missing_band_validation_work_cleanly()
    {
        using TwoTownFixture fixture = TwoTownFixture.Create();
        DaggerfallSession session = fixture.Session;
        session.AdvanceElapsedTime(8 * 60 * 60);
        session.OpenExpedition();

        IDeveloperTravelMapCommands mapCommands = session.Commands<IDeveloperTravelMapCommands>();
        var dispatched = mapCommands.DispatchPersistentBand("dev-caravan", 1.5, 90);
        Assert.True(dispatched.Succeeded);
        Assert.Contains("\"persistent\":true", dispatched.Message, StringComparison.Ordinal);
        Assert.Contains("\"bandId\":\"dev-caravan\"", dispatched.Message, StringComparison.Ordinal);

        // Tampering a save so an OnOverlandBand actor references a missing PersistentBand is rejected at load.
        DaggerfallSavePayload saved = DaggerfallSavePayload.Read(session.CaptureSave());
        DaggerfallSavePayload tampered = saved with { Expedition = saved.Expedition with { PersistentBands = null } };
        Assert.Throws<ArgumentException>(() =>
            tampered.ResolveRestore(TestPayload.Definitions, fixture.Origin, fixture.Composition.Profiles));
    }

    [Fact]
    public void Caught_hostile_persistent_band_saves_cleanly_before_entry_and_materializes_after_reload()
    {
        using TwoTownFixture fixture = TwoTownFixture.Create();
        DaggerfallSession session = fixture.Session;

        session.AdvanceElapsedTime(8 * 60 * 60);
        DaggerfallNpc raider = session.State.Npcs.All.First(npc =>
            npc.Kind == DaggerfallNpcKind.Civilian
            && npc.Profile == fixture.Origin.ProfileKey
            && DaggerfallPeopleSlots.IsDeterministicId(npc.DurableId)
            && session.State.Actors.TryGet(npc.DurableId, out _));
        long raiderId = raider.DurableId;
        session.State.Npcs.SetDisplayName(raiderId, "Vorn the Highwayman");

        session.OpenExpedition();
        (double partyX, double partyY) = session.Expedition.Position;

        // Dispatch a hostile persistent band right beside the halted party so it catches the party on advance.
        session.OverlandParties.DispatchOverlandBand(
            bandId: "hostile-raiders",
            name: "Vorn's Marauders",
            kind: DaggerfallBandKind.Bandits,
            memberIds: [raiderId],
            startX: partyX + 0.08,
            startY: partyY,
            speedPixelsPerHour: 1.0,
            hostile: true);

        session.Expedition.SetMode(DaggerfallExpeditionMode.Halt);
        session.Expedition.SetSpeed(1);
        _ = session.Expedition.Advance(0.25);

        Assert.True(session.Expedition.EncounterWaiting);
        Assert.Equal("Vorn's Marauders", session.Expedition.EncounterFoe);
        // Band remains registered until Entry() so saving while EncounterWaiting keeps ActorLedger/PersistentBands consistent.
        Assert.Single(session.Expedition.PersistentBands, b => b.BandId == "hostile-raiders");

        RulesetSavePayload savedBeforeEntry = session.CaptureSave();
        DaggerfallSavePayload decoded = DaggerfallSavePayload.Read(savedBeforeEntry);
        Assert.Equal("hostile-raiders", decoded.Expedition.PendingPersistentBandId);
        Assert.Single(decoded.Expedition.PersistentBands!, b => b.BandId == "hostile-raiders");

        using DaggerfallSession restored = DaggerfallSession.Restore(fixture.CreateEngine().Context, fixture.Composition, savedBeforeEntry);
        Assert.True(restored.Expedition.Open);
        Assert.True(restored.Expedition.EncounterWaiting);
        Assert.Single(restored.Expedition.PersistentBands, b => b.BandId == "hostile-raiders");

        // Entering after reload resolves the pending caught band into EntityStore with zero duplicate bodies.
        restored.EnterExpedition();
        Assert.False(restored.Expedition.Open);
        Assert.Empty(restored.Expedition.PersistentBands);
        Assert.True(restored.State.Actors.TryGet(raiderId, out _));
        Assert.Single(restored.State.Actors.All, a => a.DurableId == raiderId);
        Assert.Equal("Vorn the Highwayman", restored.State.Npcs.Require(raiderId).DisplayName);
    }

    [Fact]
    public void Staged_driver_survives_save_reload_both_at_home_and_away_and_reboards_on_open_expedition()
    {
        using TwoTownFixture fixture = TwoTownFixture.Create();
        DaggerfallSession session = fixture.Session;

        session.AdvanceElapsedTime(8 * 60 * 60);
        DaggerfallNpc driver = session.State.Npcs.All.First(npc =>
            npc.Kind == DaggerfallNpcKind.Civilian
            && npc.Profile == fixture.Origin.ProfileKey
            && DaggerfallPeopleSlots.IsDeterministicId(npc.DurableId)
            && session.State.Actors.TryGet(npc.DurableId, out _));
        long driverId = driver.DurableId;
        session.State.Npcs.SetDisplayName(driverId, "Tarin the Coachman");

        session.OverlandParties.JoinParty(driverId, ActorPartyRole.Driver, portraitMedia: "face-driver-02");

        // Case A: Stage driver at its home site (Origin), save & reload while staged, then open expedition map.
        session.OpenExpedition();
        session.EnterExpedition();
        Assert.True(session.State.Actors.TryGet(driverId, out _));
        Assert.Contains(driverId, session.OverlandParties.StagedPartyMemberIds);

        RulesetSavePayload savedStagedAtHome = session.CaptureSave();
        using (DaggerfallSession restoredHome = DaggerfallSession.Restore(fixture.CreateEngine().Context, fixture.Composition, savedStagedAtHome))
        {
            Assert.True(restoredHome.State.Actors.TryGet(driverId, out ActorState? stagedHome));
            Assert.Equal("Driver", stagedHome!.Actor.Get<ActorOverlayComponent>().StagedPartyRole);
            Assert.Contains(driverId, restoredHome.OverlandParties.StagedPartyMemberIds);

            restoredHome.OpenExpedition();
            Assert.False(restoredHome.State.Actors.TryGet(driverId, out _));
            DaggerfallPartyPortrait portrait = Assert.Single(restoredHome.OverlandParties.PartyPortraits());
            Assert.Equal(driverId, portrait.DurableId);
            Assert.Equal(ActorPartyRole.Driver, portrait.Role);
            Assert.Equal("face-driver-02", portrait.MediaId);
        }

        // Case B: Board back into WithParty, travel to Destination (away from home), stage there, save & reload, then reboard.
        session.OpenExpedition();
        Assert.False(session.State.Actors.TryGet(driverId, out _));
        session.Expedition.Close();
        Assert.True(session.TryTransitionTo(fixture.Destination.ProfileKey));
        session.OpenExpedition();
        session.EnterExpedition();
        Assert.True(session.State.Actors.TryGet(driverId, out _));
        Assert.Contains(driverId, session.OverlandParties.StagedPartyMemberIds);

        RulesetSavePayload savedStagedAway = session.CaptureSave();
        using DaggerfallSession restoredAway = DaggerfallSession.Restore(fixture.CreateEngine().Context, fixture.Composition, savedStagedAway);
        Assert.True(restoredAway.State.Actors.TryGet(driverId, out ActorState? stagedAway));
        Assert.Equal("Driver", stagedAway!.Actor.Get<ActorOverlayComponent>().StagedPartyRole);
        Assert.Contains(driverId, restoredAway.OverlandParties.StagedPartyMemberIds);

        restoredAway.OpenExpedition();
        Assert.False(restoredAway.State.Actors.TryGet(driverId, out _));
        Assert.Single(restoredAway.OverlandParties.PartyPortraits(), p => p.DurableId == driverId && p.Role == ActorPartyRole.Driver);
    }

    [Fact]
    public void Overland_and_party_custody_preserve_vitals_inventory_combat_state_and_active_effects_across_save_reload()
    {
        using TwoTownFixture fixture = TwoTownFixture.Create();
        DaggerfallSession session = fixture.Session;

        session.AdvanceElapsedTime(8 * 60 * 60);
        DaggerfallNpc[] civilians = session.State.Npcs.All.Where(npc =>
            npc.Kind == DaggerfallNpcKind.Civilian
            && npc.Profile == fixture.Origin.ProfileKey
            && DaggerfallPeopleSlots.IsDeterministicId(npc.DurableId)
            && session.State.Actors.TryGet(npc.DurableId, out _)).Take(2).ToArray();
        Assert.Equal(2, civilians.Length);
        long travellerId = civilians[0].DurableId;
        long companionId = civilians[1].DurableId;

        // Mutate traveller before dispatching onto OnOverlandBand: vitals, inventory stack, combat state, and active Fortify Strength effect.
        ActorState liveTraveller = session.State.Actors.Get(travellerId);
        Track travellerHealth = liveTraveller.Stats.GetTrack(TrackId.Parse("health"));
        travellerHealth.Maximum.BaseValue = 40;
        travellerHealth.SetCurrent(17);
        double baseStrength = liveTraveller.Stats.GetStat(StatId.Parse("strength")).Value;

        InventoryStackId bandGoldStack = InventoryStackId.Parse("test.band.gold");
        DaggerfallItemDefinition goldDef = TestPayload.Definitions.RequireItem(new DaggerfallItemId("gold-piece"));
        session.State.ActorInventories.InventoryFor(travellerId)!
            .Grant(new InventoryGrant(new InventoryItemId(goldDef.Id.Value), bandGoldStack, 77));
        session.State.ItemInstances.RegisterDefaultStack(DaggerfallItemOwner.Actor(travellerId),
            new InventoryStack(bandGoldStack, ItemDefinitionId.Parse(goldDef.Id.Value), 77), goldDef);

        liveTraveller.Actor.Get<DaggerfallEnemyPerceptionMemory>().SetForcedHostile(true);
        liveTraveller.Actor.Get<FactionComponent>().SetTeamOverride("bandits");

        DaggerfallSpellEffectDefinition fortifySetting = new("fortify", 9, 0, 10, 0, 1, 0, 0, 1, 12, 12, 0, 0, 1);
        DaggerfallEffectAdmissionOutcome effectOutcome = session.State.Effects.Start(new(
            "band-fortify-str", DaggerfallFortifyEffects.Key(0), "fortify", DaggerfallActorIdentity.PlayerEntityId,
            travellerId, "fortify", "Magic", null, 1, 10,
            JsonSerializer.SerializeToElement(new DaggerfallCastEffectState(fortifySetting, 1, 12, 100), DaggerfallMagicJson.Default.DaggerfallCastEffectState)));
        Assert.Equal(DaggerfallEffectAdmissionOutcome.Started, effectOutcome);
        double fortifiedStrength = liveTraveller.Stats.GetStat(StatId.Parse("strength")).Value;
        Assert.Equal(baseStrength + 12, fortifiedStrength);

        // Mutate companion before boarding into WithParty: vitals + inventory stack.
        ActorState liveCompanion = session.State.Actors.Get(companionId);
        Track companionHealth = liveCompanion.Stats.GetTrack(TrackId.Parse("health"));
        companionHealth.Maximum.BaseValue = 35;
        companionHealth.SetCurrent(19);
        InventoryStackId companionGoldStack = InventoryStackId.Parse("test.party.gold");
        session.State.ActorInventories.InventoryFor(companionId)!
            .Grant(new InventoryGrant(new InventoryItemId(goldDef.Id.Value), companionGoldStack, 33));
        session.State.ItemInstances.RegisterDefaultStack(DaggerfallItemOwner.Actor(companionId),
            new InventoryStack(companionGoldStack, ItemDefinitionId.Parse(goldDef.Id.Value), 33), goldDef);

        session.OverlandParties.JoinParty(companionId, ActorPartyRole.Companion, portraitMedia: "face-companion-01");

        session.OpenExpedition();
        (double partyX, double partyY) = session.Expedition.Position;
        session.OverlandParties.DispatchOverlandBand(
            bandId: "buffed-escort",
            name: "Fortified Escort",
            kind: DaggerfallBandKind.Travellers,
            memberIds: [travellerId],
            startX: partyX + 0.1,
            startY: partyY,
            speedPixelsPerHour: 0.2,
            camp: true);

        // Save and reload while BOTH actors are in travel custody (OnOverlandBand + WithParty).
        RulesetSavePayload savedInCustody = session.CaptureSave();
        using DaggerfallSession restored = DaggerfallSession.Restore(fixture.CreateEngine().Context, fixture.Composition, savedInCustody);

        // Advance one stretch to meet the camped band, then enter the 3D scene to materialize both traveller and companion.
        restored.Expedition.SetMode(DaggerfallExpeditionMode.Halt);
        restored.Expedition.SetSpeed(1);
        _ = restored.Expedition.Advance(0.25);
        Assert.NotNull(restored.Expedition.BandAhead);
        restored.EnterExpedition();

        // Traveller preserves wounded vitals, non-default inventory, combat overrides, and active stat-modifying effect!
        ActorState restoredTraveller = restored.State.Actors.Get(travellerId);
        Track restoredTravellerHealth = restoredTraveller.Stats.GetTrack(TrackId.Parse("health"));
        Assert.Equal(40, restoredTravellerHealth.Maximum.BaseValue);
        Assert.Equal(17, restoredTravellerHealth.Current);
        InventoryStack restoredBandStack = Assert.Single(restored.State.ActorInventories.InventoryFor(travellerId)!.Read().Stacks);
        Assert.Equal(bandGoldStack, restoredBandStack.Id);
        Assert.Equal(77UL, restoredBandStack.Quantity);
        Assert.True(restoredTraveller.Actor.Get<DaggerfallEnemyPerceptionMemory>().ForcedHostile);
        Assert.Equal("bandits", restoredTraveller.Actor.Get<FactionComponent>().TeamOverride);
        Assert.Contains(restored.State.Effects.Active, e => e.Context.Instance.Value == "band-fortify-str" && e.Context.Target.Value == (ulong)travellerId);
        Assert.Equal(fortifiedStrength, restoredTraveller.Stats.GetStat(StatId.Parse("strength")).Value);
        Assert.True(restored.State.Effects.Cancel(EffectInstanceId.Parse("band-fortify-str")));
        Assert.Equal(baseStrength, restoredTraveller.Stats.GetStat(StatId.Parse("strength")).Value);

        // Companion preserves wounded vitals and non-default inventory across WithParty -> save -> reload -> stage -> wound -> board!
        ActorState restoredCompanion = restored.State.Actors.Get(companionId);
        Track restoredCompanionHealth = restoredCompanion.Stats.GetTrack(TrackId.Parse("health"));
        Assert.Equal(35, restoredCompanionHealth.Maximum.BaseValue);
        Assert.Equal(19, restoredCompanionHealth.Current);
        InventoryStack restoredPartyStack = Assert.Single(restored.State.ActorInventories.InventoryFor(companionId)!.Read().Stacks);
        Assert.Equal(companionGoldStack, restoredPartyStack.Id);
        Assert.Equal(33UL, restoredPartyStack.Quantity);

        // Wound staged companion further, board back via OpenExpedition, save/reload, stage again: new wound survives!
        restoredCompanionHealth.SetCurrent(9);
        restored.OpenExpedition();
        RulesetSavePayload savedAfterBoard = restored.CaptureSave();
        using DaggerfallSession reboarded = DaggerfallSession.Restore(fixture.CreateEngine().Context, fixture.Composition, savedAfterBoard);
        reboarded.EnterExpedition();
        Assert.Equal(9, reboarded.State.Actors.Get(companionId).Stats.GetTrack(TrackId.Parse("health")).Current);
    }

    private sealed class TwoTownFixture : IDisposable
    {
        private TwoTownFixture(
            DaggerfallSession session,
            DaggerfallSessionComposition composition,
            DaggerfallSiteProfile origin,
            DaggerfallSiteProfile destination,
            DaggerfallSiteRecord originRecord,
            DaggerfallSiteRecord destinationRecord)
        {
            Session = session;
            Composition = composition;
            Origin = origin;
            Destination = destination;
            OriginRecord = originRecord;
            DestinationRecord = destinationRecord;
        }

        internal DaggerfallSession Session { get; }
        internal DaggerfallSessionComposition Composition { get; }
        internal DaggerfallSiteProfile Origin { get; }
        internal DaggerfallSiteProfile Destination { get; }
        internal DaggerfallSiteRecord OriginRecord { get; }
        internal DaggerfallSiteRecord DestinationRecord { get; }

        internal static TwoTownFixture Create()
        {
            DaggerfallDefinitions definitions = TestPayload.Definitions;
            DaggerfallSiteRecord originRecord = definitions.Locations.Records.Single(r => r.Id == Charing);
            DaggerfallSiteRecord destinationRecord = definitions.Locations.Records.First(r =>
                r.Id != originRecord.Id
                && r.Exterior is not null
                && Math.Abs(r.MapPixelX - originRecord.MapPixelX) >= 3
                && Math.Abs(r.MapPixelX - originRecord.MapPixelX) <= 12
                && Math.Abs(r.MapPixelY - originRecord.MapPixelY) <= 12);

            DaggerfallSiteProfile origin = Assembled(DaggerfallWorldProfileIds.Exterior(originRecord.Id));
            DaggerfallSiteProfile destination = Assembled(DaggerfallWorldProfileIds.Exterior(destinationRecord.Id));
            DaggerfallSiteProfiles profiles = new([origin, destination]);
            DaggerfallSessionComposition composition = new(definitions, origin, DaggerfallTuning.Defaults)
            {
                Profiles = profiles,
            };

            TwoTownFixture holder = new(null!, composition, origin, destination, originRecord, destinationRecord);
            DaggerfallSession session = DaggerfallSession.StartNew(holder.CreateEngine().Context, composition);
            session.Update(new ProductUpdate(OuterUpdate(1), []));
            return new(session, composition, origin, destination, originRecord, destinationRecord);
        }

        internal EngineContextFake CreateEngine()
        {
            List<string> releases = [];
            ContentFake content = new(releases);
            PopulateContent(content, Origin);
            PopulateContent(content, Destination);
            SpatialFake spatial = SpatialFake.Create(Origin.Geometry.Spatial[0].Sha256, releases);
            AppearanceFake appearance = new(releases);
            return EngineContextFake.Create(content, spatial.Service, appearance);
        }

        public void Dispose() => Session?.Dispose();
    }
}
