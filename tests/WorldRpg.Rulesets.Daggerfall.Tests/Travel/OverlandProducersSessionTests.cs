using System.Numerics;
using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Behavior;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using WorldRpg.Rulesets.Daggerfall.Modules.Transport;
using WorldRpg.Rulesets.Daggerfall.Travel;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// End-to-end gameplay producer tests for #11031:
/// 1. Hiring and dismissing WithParty drivers and companions through real dialogue topics (gold payment,
///    published HUD portrait strip, threshold crossing, expedition boarding/staging, save/reload, dismissal).
/// 2. Autonomous regional OnOverlandBand caravans and couriers dispatched from real seeded settlement slots
///    as expedition time crosses daytime dispatch windows, surviving save/reload and arriving with real actors.
/// </summary>
public sealed class OverlandProducersSessionTests
{
    [Fact]
    public void Dialogue_party_hiring_and_dismissal_enlists_stages_renders_hud_portraits_and_survives_travel_and_save_reload()
    {
        using ProducerFixture fixture = ProducerFixture.Create();
        DaggerfallSession session = fixture.Session;
        session.ApplyProductMode(ProductMode.Playing);
        session.AdvanceElapsedTime(8 * 60 * 60);

        DaggerfallNpc[] townsfolk = [.. session.State.Npcs.All
            .Where(npc => npc.Kind == DaggerfallNpcKind.Civilian
                && npc.Profile == fixture.Origin.ProfileKey
                && !string.Equals(npc.Role, "guard", StringComparison.OrdinalIgnoreCase)
                && DaggerfallPeopleSlots.IsDeterministicId(npc.DurableId)
                && session.State.Actors.TryGet(npc.DurableId, out _))
            .OrderBy(npc => npc.DurableId)
            .Take(2)];
        Assert.Equal(2, townsfolk.Length);
        long firstId = townsfolk[0].DurableId;
        long secondId = townsfolk[1].DurableId;

        // Spend any starting kit gold so we verify broke hiring fails cleanly without mutating party or ledger.
        ulong startingGold = session.State.Currency.Read().Gold;
        if (startingGold > 0)
            Assert.True(session.State.Currency.TrySpendGold(startingGold, []));
        Assert.Equal(0UL, session.State.Currency.Read().Gold);

        DaggerfallDialogueView? openFirst = TalkTo(session, firstId);
        Assert.NotNull(openFirst);
        Assert.Contains(openFirst!.Topics, t => t.Id == "hire-companion");
        Assert.DoesNotContain(openFirst.Topics, t => t.Id == "hire-driver");

        DaggerfallActivationOutcome broke = session.Dialogue.Ask(openFirst.Revision, "hire-companion", null, null);
        Assert.False(broke.Applied);
        Assert.Contains("gold", broke.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(session.OverlandParties.PartyPortraits());

        // Grant a cart and gold: now BOTH "hire-companion" and "hire-driver" appear in dialogue.
        DurableIdentityReference cartIdentity = new(DurableIdentityKind.Item, 900_001);
        DaggerfallItemDefinition cartDef = TestPayload.Definitions.RequireItem(new DaggerfallItemId(DaggerfallTransportPolicy.CartItemId));
        session.State.Inventory.GrantAtomic([new InventoryAtomicGrant(new InventoryItemId(DaggerfallTransportPolicy.CartItemId), UniqueItem: cartIdentity)]);
        session.State.ItemInstances.RegisterUnique(cartIdentity.Value, DaggerfallItemInstanceMetadata.Default(cartDef, DaggerfallItemOwner.Player));
        session.State.Currency.ReceiveGold(100);
        openFirst = TalkTo(session, firstId);
        Assert.Contains(openFirst!.Topics, t => t.Id == "hire-companion");
        Assert.Contains(openFirst.Topics, t => t.Id == "hire-driver");

        // Hire the first townsperson as Driver via dialogue: 15 gold spent, staged in 3D, portrait immediately on HUD strip,
        // and dialogue topic switches to "dismiss-party".
        DaggerfallActivationOutcome hiredDriver = session.Dialogue.Ask(openFirst.Revision, "hire-driver", null, null);
        Assert.True(hiredDriver.Applied);
        Assert.Equal(85UL, session.State.Currency.Read().Gold);
        Assert.True(session.State.Actors.TryGet(firstId, out ActorState? stagedDriver));
        Assert.Equal("Driver", stagedDriver!.Actor.Get<ActorOverlayComponent>().StagedPartyRole);

        DaggerfallPartyPortrait driverPortrait = Assert.Single(session.OverlandParties.PartyPortraits());
        Assert.Equal(firstId, driverPortrait.DurableId);
        Assert.Equal(ActorPartyRole.Driver, driverPortrait.Role);
        Assert.False(string.IsNullOrWhiteSpace(driverPortrait.MediaId));

        // Verify the HUD snapshot publishes the driver's real portrait in escortFaces.
        session.PublishInitial();
        string hudJson = JsonSerializer.Serialize(fixture.Engine.Published());
        Assert.Contains(driverPortrait.MediaId, hudJson, StringComparison.Ordinal);

        // Re-talking to the hired driver offers "dismiss-party" and NOT hire topics.
        DaggerfallDialogueView? driverAgain = TalkTo(session, firstId);
        Assert.Contains(driverAgain!.Topics, t => t.Id == "dismiss-party");
        Assert.DoesNotContain(driverAgain.Topics, t => t.Id is "hire-driver" or "hire-companion");

        // Talking to the second townsperson offers "hire-companion" but NOT "hire-driver" (driver slot already filled).
        DaggerfallDialogueView? openSecond = TalkTo(session, secondId);
        Assert.Contains(openSecond!.Topics, t => t.Id == "hire-companion");
        Assert.DoesNotContain(openSecond.Topics, t => t.Id == "hire-driver");

        DaggerfallActivationOutcome hiredCompanion = session.Dialogue.Ask(openSecond.Revision, "hire-companion", null, null);
        Assert.True(hiredCompanion.Applied);
        Assert.Equal(60UL, session.State.Currency.Read().Gold);
        Assert.Equal(2, session.OverlandParties.PartyPortraits().Count);

        // Walking across a site transition brings both staged party members seamlessly into the destination scene.
        Assert.True(session.TryTransitionTo(fixture.Destination.ProfileKey));
        Assert.True(session.State.Actors.TryGet(firstId, out _));
        Assert.True(session.State.Actors.TryGet(secondId, out _));
        Assert.Equal(2, session.OverlandParties.StagedPartyMemberIds.Count);

        // Opening the expedition map boards both into WithParty (out of EntityStore, still on HUD portrait strip).
        session.OpenExpedition();
        Assert.False(session.State.Actors.TryGet(firstId, out _));
        Assert.False(session.State.Actors.TryGet(secondId, out _));
        Assert.Equal(2, session.OverlandParties.PartyPortraits().Count);

        // Save and reload while WithParty, then enter 3D from expedition: both stage back with exact durable IDs & portraits.
        RulesetSavePayload saved = session.CaptureSave();
        using DaggerfallSession restored = DaggerfallSession.Restore(fixture.CreateEngine().Context, fixture.Composition, saved);
        Assert.Equal(2, restored.OverlandParties.PartyPortraits().Count);
        restored.EnterExpedition();
        Assert.True(restored.State.Actors.TryGet(firstId, out _));
        Assert.True(restored.State.Actors.TryGet(secondId, out _));

        // Dismiss the companion through real dialogue ("dismiss-party"): portrait drops, companion stays at destination,
        // and reopening the expedition map boards ONLY the driver.
        DaggerfallDialogueView? dismissView = TalkTo(restored, secondId);
        Assert.Contains(dismissView!.Topics, t => t.Id == "dismiss-party");
        DaggerfallActivationOutcome dismissed = restored.Dialogue.Ask(dismissView.Revision, "dismiss-party", null, null);
        Assert.True(dismissed.Applied);

        DaggerfallPartyPortrait remainingPortrait = Assert.Single(restored.OverlandParties.PartyPortraits());
        Assert.Equal(firstId, remainingPortrait.DurableId);
        Assert.True(restored.State.Actors.TryGet(secondId, out ActorState? dismissedActor));
        Assert.Null(dismissedActor!.Actor.Get<ActorOverlayComponent>().StagedPartyRole);

        restored.OpenExpedition();
        Assert.False(restored.State.Actors.TryGet(firstId, out _));
        Assert.True(restored.State.Actors.TryGet(secondId, out _));
        Assert.Single(restored.OverlandParties.PartyPortraits(), p => p.DurableId == firstId);
    }

    [Fact]
    public void Autonomous_overland_traffic_dispatches_real_seeded_caravans_and_couriers_across_daytime_stretches()
    {
        using ProducerFixture fixture = ProducerFixture.Create();
        DaggerfallSession session = fixture.Session;
        session.ApplyProductMode(ProductMode.Playing);
        session.AdvanceElapsedTime(8 * 60 * 60);

        // Untouched town before expedition travel keeps zero ActorLedger traffic entries.
        Assert.Empty(session.Expedition.PersistentBands);
        Assert.Empty(DaggerfallSavePayload.Read(session.CaptureSave()).ActorLedger);

        // Open the expedition map and march across daytime 6-hour windows (crossing 12:00 and 18:00).
        session.OpenExpedition();
        Assert.True(session.Expedition.Open);
        session.Expedition.SetMode(DaggerfallExpeditionMode.Halt);
        session.Expedition.SetSpeed(1);

        // Advance game time via admitted session updates (each 4.0s at speed 1 advances expedition time; halt keeps party safe on land).
        for (ulong step = 2; step <= 40 && session.Expedition.PersistentBands.Count < 2; step++)
        {
            if (session.Expedition.BandAhead is not null)
                session.Expedition.SetMode(DaggerfallExpeditionMode.Halt);
            session.Update(new ProductUpdate(OuterUpdate(step) with { HostElapsedSeconds = 4.0 }, []));
        }

        Assert.True(session.Expedition.PersistentBands.Count >= 1,
            "Advancing expedition across daytime dispatch windows should autonomously dispatch regional traffic.");
        Assert.True(session.Expedition.PersistentBands.Count <= DaggerfallOverlandTraffic.MaxActiveTrafficBands);

        DaggerfallPersistentOverlandBand trafficBand = session.Expedition.PersistentBands
            .First(b => b.BandId.StartsWith(DaggerfallOverlandTraffic.BandIdPrefix, StringComparison.Ordinal));
        Assert.Equal(DaggerfallBandKind.Travellers, trafficBand.Kind);
        Assert.NotNull(trafficBand.Destination);
        Assert.NotNull(trafficBand.DestinationProfile);

        // Backing actor in ActorLedger is a real deterministic SeededSlot actor with OnOverlandBand(bandId),
        // valid spawner/origin, climate race, and generated regional name (never stolen from active scene).
        ActorLedgerEntry member = Assert.Single(session.State.ActorLedger.EntriesOnOverlandBand(trafficBand.BandId));
        Assert.Equal(ActorOriginKind.SeededSlot, member.Origin.Kind);
        Assert.True(DaggerfallPeopleSlots.IsDeterministicId(member.DurableId));
        Assert.False(string.IsNullOrWhiteSpace(member.Origin.SpawnerId));
        Assert.NotNull(member.Social);
        Assert.False(string.IsNullOrWhiteSpace(member.Social!.DisplayNameOverride));
        Assert.False(session.State.Actors.TryGet(member.DurableId, out _));

        // Persistent traffic survives full save and reload, and marches toward its destination.
        double startX = trafficBand.X;
        double startY = trafficBand.Y;
        RulesetSavePayload savedTraffic = session.CaptureSave();
        using DaggerfallSession restored = DaggerfallSession.Restore(fixture.CreateEngine().Context, fixture.Composition, savedTraffic);
        DaggerfallPersistentOverlandBand restoredBand = Assert.Single(restored.Expedition.PersistentBands,
            b => b.BandId == trafficBand.BandId);
        Assert.Equal(startX, restoredBand.X, precision: 6);
        Assert.Equal(startY, restoredBand.Y, precision: 6);
        Assert.Single(restored.State.ActorLedger.EntriesOnOverlandBand(trafficBand.BandId), e => e.DurableId == member.DurableId);

        // Advance one stretch: the traffic band moves along its route toward its destination settlement.
        restored.Expedition.SetMode(DaggerfallExpeditionMode.Halt);
        restored.Expedition.SetSpeed(1);
        _ = restored.Expedition.Advance(0.5);
        if (restored.Expedition.PersistentBands.Any(b => b.BandId == trafficBand.BandId))
        {
            Assert.True(Math.Abs(restoredBand.X - startX) > 1e-6 || Math.Abs(restoredBand.Y - startY) > 1e-6);
        }

        // Intercepting a dispatched traffic band materializes the exact durable seeded actor in 3D with real stats.
        restored.OverlandParties.OnMeetPersistentBand(restoredBand, restored.Expedition.Party,
            WorldRpg.Rulesets.Daggerfall.Modules.Encounters.DaggerfallEncounterStaging.Travelling);
        restored.EnterExpedition();
        Assert.True(restored.State.Actors.TryGet(member.DurableId, out ActorState? metTraveller));
        Assert.Equal(member.Social!.DisplayNameOverride, restored.State.Npcs.Require(member.DurableId).DisplayName);
        Assert.True(metTraveller!.Stats.GetTrack(TrackId.Parse("health")).Maximum.Value > 1);
    }

    [Fact]
    public void Overland_traffic_dispatch_preserves_existing_actor_ledger_deltas_across_save_and_reload()
    {
        const long windowIndex = 101L;
        ActorLedgerEntry baselineEntry;
        (double X, double Y) partyPosition;

        // Discover the deterministic home-slot candidate selected for this traffic window on a clean probe session.
        using (ProducerFixture probe = ProducerFixture.Create())
        {
            probe.Session.ApplyProductMode(ProductMode.Playing);
            partyPosition = probe.Session.Expedition.Position;
            DaggerfallPersistentOverlandBand? probeBand = probe.Session.OverlandTraffic.TryDispatchWindow(windowIndex, partyPosition);
            Assert.NotNull(probeBand);
            baselineEntry = Assert.Single(probe.Session.State.ActorLedger.EntriesOnOverlandBand(probeBand!.BandId));
        }

        // Case 1: existing inactive home-slot actor with prior Social, Vitals, Combat, and Referral deltas.
        using (ProducerFixture fixture = ProducerFixture.Create())
        {
            DaggerfallSession session = fixture.Session;
            session.ApplyProductMode(ProductMode.Playing);
            session.AdvanceElapsedTime(8 * 60 * 60);

            // Capture a real injured vitals snapshot (4 HP remaining) from an active civilian actor.
            ActorState sampleActor = session.State.Actors.All.First(a => a.DurableId != DaggerfallActorIdentity.PlayerEntityId);
            sampleActor.Stats.GetTrack(TrackId.Parse("health")).SetCurrent(4.0, clamp: true);
            DaggerfallStatsSave injuredStats = DaggerfallStatsSaveBoundary.Capture(sampleActor.Stats, sampleActor.Actor.Entity);

            ActorSocialDiff priorSocial = baselineEntry.Social! with
            {
                DisplayNameOverride = "Mara the Witness",
                Met = true,
                DispositionDelta = 17,
                WitnessedCrimes = ["crime:42"],
                ReferralTags = ["quest:caravan-clue"],
                Muted = true,
                Presence = (int)DaggerfallNpcPresence.Active,
            };
            ActorReferralDiff priorReferral = new(["quest:caravan-clue"], "Mara the Witness");
            ActorCombatStateDiff priorCombat = new(LanguagePacified: true);
            ActorVitalsDiff priorVitals = new(injuredStats);
            session.State.ActorLedger.Upsert(new ActorLedgerEntry(
                baselineEntry.DurableId,
                baselineEntry.Origin,
                Vitals: priorVitals,
                Combat: priorCombat,
                Social: priorSocial,
                Location: null,
                Referral: priorReferral));

            DaggerfallPersistentOverlandBand? dispatched = session.OverlandTraffic.TryDispatchWindow(windowIndex, partyPosition);
            Assert.NotNull(dispatched);

            ActorLedgerEntry onBand = Assert.Single(session.State.ActorLedger.EntriesOnOverlandBand(dispatched!.BandId));
            Assert.Equal(baselineEntry.DurableId, onBand.DurableId);
            Assert.Equal(baselineEntry.Origin, onBand.Origin);
            Assert.NotNull(onBand.Vitals);
            Assert.Equal(4.0, onBand.Vitals!.Stats.Snapshot.Tracks.Single(t => t.Id == DaggerfallMechanicsIds.Health.Value).Current);
            Assert.NotNull(onBand.Combat);
            Assert.True(onBand.Combat!.LanguagePacified);
            Assert.NotNull(onBand.Referral);
            Assert.Equal(["quest:caravan-clue"], onBand.Referral!.Tags);
            Assert.Equal("Mara the Witness", onBand.Referral.DisplayNameOverride);
            Assert.NotNull(onBand.Social);
            Assert.Equal("Mara the Witness", onBand.Social!.DisplayNameOverride);
            Assert.True(onBand.Social.Met);
            Assert.Equal(17, onBand.Social.DispositionDelta);
            Assert.Equal(["crime:42"], onBand.Social.WitnessedCrimes!);
            Assert.Equal(["quest:caravan-clue"], onBand.Social.ReferralTags!);
            Assert.True(onBand.Social.Muted);

            // Save, reload, and intercept: every delta survives persistence and materializes onto the live 3D actor.
            RulesetSavePayload saved = session.CaptureSave();
            using DaggerfallSession restored = DaggerfallSession.Restore(fixture.CreateEngine().Context, fixture.Composition, saved);
            ActorLedgerEntry restoredOnBand = Assert.Single(restored.State.ActorLedger.EntriesOnOverlandBand(dispatched.BandId));
            Assert.Equal(4.0, restoredOnBand.Vitals!.Stats.Snapshot.Tracks.Single(t => t.Id == DaggerfallMechanicsIds.Health.Value).Current);
            Assert.True(restoredOnBand.Combat!.LanguagePacified);
            Assert.Equal(["quest:caravan-clue"], restoredOnBand.Referral!.Tags);
            Assert.Equal("Mara the Witness", restoredOnBand.Social!.DisplayNameOverride);
            Assert.True(restoredOnBand.Social.Met);
            Assert.Equal(17, restoredOnBand.Social.DispositionDelta);
            Assert.Equal(["crime:42"], restoredOnBand.Social.WitnessedCrimes!);
            Assert.Equal(["quest:caravan-clue"], restoredOnBand.Social.ReferralTags!);
            Assert.True(restoredOnBand.Social.Muted);

            DaggerfallPersistentOverlandBand restoredBand = Assert.Single(restored.Expedition.PersistentBands, b => b.BandId == dispatched.BandId);
            restored.OpenExpedition();
            restored.OverlandParties.OnMeetPersistentBand(restoredBand, restored.Expedition.Party,
                WorldRpg.Rulesets.Daggerfall.Modules.Encounters.DaggerfallEncounterStaging.Travelling);
            restored.EnterExpedition();

            Assert.True(restored.State.Actors.TryGet(baselineEntry.DurableId, out ActorState? live));
            Assert.Equal(4.0, live!.Stats.GetTrack(TrackId.Parse("health")).Current);
            Assert.True(live.Actor.Get<DaggerfallEnemyPerceptionMemory>().LanguagePacified);
            ActorOverlayComponent overlay = live.Actor.Get<ActorOverlayComponent>();
            Assert.True(overlay.Met);
            Assert.True(overlay.IsMuted);
            Assert.Contains("crime:42", overlay.WitnessedCrimes);
            Assert.Contains("quest:caravan-clue", overlay.ReferralTags);
            Assert.Equal("Mara the Witness", restored.State.Npcs.Require(baselineEntry.DurableId).DisplayName);
        }

        // Case 2: pre-visit referral-only entry (Social == null, Origin missing SpawnerId) preserves Referral,
        // backfills spawner metadata, initializes Social with referral name/tags, and materializes cleanly.
        using (ProducerFixture fixture = ProducerFixture.Create())
        {
            DaggerfallSession session = fixture.Session;
            session.ApplyProductMode(ProductMode.Playing);

            long referredId = session.State.ActorLedger.RegisterSlotReferral(
                baselineEntry.Origin.SiteProfile!.Require(),
                baselineEntry.Origin.SlotKey!,
                "quest:pre-visit-referral",
                "Aldric the Factor");
            Assert.Equal(baselineEntry.DurableId, referredId);

            DaggerfallPersistentOverlandBand? dispatched = session.OverlandTraffic.TryDispatchWindow(windowIndex, partyPosition);
            Assert.NotNull(dispatched);

            ActorLedgerEntry onBand = Assert.Single(session.State.ActorLedger.EntriesOnOverlandBand(dispatched!.BandId));
            Assert.Equal(baselineEntry.DurableId, onBand.DurableId);
            Assert.Equal(baselineEntry.Origin.SpawnerId, onBand.Origin.SpawnerId);
            Assert.NotNull(onBand.Referral);
            Assert.Equal(["quest:pre-visit-referral"], onBand.Referral!.Tags);
            Assert.Equal("Aldric the Factor", onBand.Referral.DisplayNameOverride);
            Assert.NotNull(onBand.Social);
            Assert.Equal("Aldric the Factor", onBand.Social!.DisplayNameOverride);
            Assert.Equal(["quest:pre-visit-referral"], onBand.Social.ReferralTags!);
        }
    }

    [Fact]
    public void Overland_traffic_dispatch_excludes_unavailable_saved_actors()
    {
        const long windowIndex = 101L;
        ActorLedgerEntry baselineEntry;
        (double X, double Y) partyPosition;

        using (ProducerFixture probe = ProducerFixture.Create())
        {
            probe.Session.ApplyProductMode(ProductMode.Playing);
            partyPosition = probe.Session.Expedition.Position;
            DaggerfallPersistentOverlandBand? probeBand = probe.Session.OverlandTraffic.TryDispatchWindow(windowIndex, partyPosition);
            Assert.NotNull(probeBand);
            baselineEntry = Assert.Single(probe.Session.State.ActorLedger.EntriesOnOverlandBand(probeBand!.BandId));
        }

        // Verify each unavailable saved state excludes the actor from being drafted onto regional traffic:
        // Hidden presence, staged party member, forced hostile, defeated/zero health.
        Func<DaggerfallSession, ActorLedgerEntry>[] unavailableFactories =
        [
            _ => new ActorLedgerEntry(baselineEntry.DurableId, baselineEntry.Origin,
                Social: baselineEntry.Social! with { Presence = (int)DaggerfallNpcPresence.Hidden }),
            _ => new ActorLedgerEntry(baselineEntry.DurableId, baselineEntry.Origin,
                Social: baselineEntry.Social! with { StagedPartyRole = ActorPartyRole.Companion }),
            _ => new ActorLedgerEntry(baselineEntry.DurableId, baselineEntry.Origin,
                Combat: new ActorCombatStateDiff(ForcedHostile: true),
                Social: baselineEntry.Social),
            session =>
            {
                session.AdvanceElapsedTime(8 * 60 * 60);
                ActorState sample = session.State.Actors.All.First(a => a.DurableId != DaggerfallActorIdentity.PlayerEntityId);
                sample.Stats.GetTrack(TrackId.Parse("health")).SetCurrent(0.0, clamp: true);
                DaggerfallStatsSave zeroHp = DaggerfallStatsSaveBoundary.Capture(sample.Stats, sample.Actor.Entity);
                return new ActorLedgerEntry(baselineEntry.DurableId, baselineEntry.Origin,
                    Vitals: new ActorVitalsDiff(zeroHp),
                    Social: baselineEntry.Social);
            },
        ];

        foreach (Func<DaggerfallSession, ActorLedgerEntry> makeUnavailable in unavailableFactories)
        {
            using ProducerFixture fixture = ProducerFixture.Create();
            DaggerfallSession session = fixture.Session;
            session.ApplyProductMode(ProductMode.Playing);
            session.State.ActorLedger.Upsert(makeUnavailable(session));

            DaggerfallPersistentOverlandBand? dispatched = session.OverlandTraffic.TryDispatchWindow(windowIndex, partyPosition);
            if (dispatched is not null)
            {
                Assert.DoesNotContain(session.State.ActorLedger.EntriesOnOverlandBand(dispatched.BandId),
                    e => e.DurableId == baselineEntry.DurableId);
            }
            Assert.Null(session.State.ActorLedger.Require(baselineEntry.DurableId).Location);
        }
    }

    private static DaggerfallDialogueView? TalkTo(DaggerfallSession session, long npcId)
    {
        DaggerfallActivationTarget target = Assert.Single(session.Dialogue.NpcTargets(),
            t => t.Identity.Value == checked((ulong)npcId));
        Assert.True(session.Dialogue.ActivateNpc(new(DaggerfallActivationMode.Talk, target)).Applied);
        return session.ActivationPanel.View.Dialogue;
    }

    private sealed class ProducerFixture : IDisposable
    {
        private ProducerFixture(DaggerfallSession session, DaggerfallSessionComposition composition,
            DaggerfallSiteProfile origin, DaggerfallSiteProfile destination, EngineContextFake engine)
        {
            Session = session;
            Composition = composition;
            Origin = origin;
            Destination = destination;
            Engine = engine;
        }

        internal DaggerfallSession Session { get; }
        internal DaggerfallSessionComposition Composition { get; }
        internal DaggerfallSiteProfile Origin { get; }
        internal DaggerfallSiteProfile Destination { get; }
        internal EngineContextFake Engine { get; }

        internal static ProducerFixture Create()
        {
            DaggerfallDefinitions definitions = TestPayload.Definitions;
            DaggerfallSiteRecord originRecord = definitions.Locations.Records.Single(r => r.Id == Charing);
            DaggerfallSiteRecord destRecord = definitions.Locations.Records.First(r =>
                r.Exterior is not null
                && r.Id != originRecord.Id
                && r.Id.Region == originRecord.Id.Region
                && Math.Abs(r.MapPixelX - originRecord.MapPixelX) + Math.Abs(r.MapPixelY - originRecord.MapPixelY) is >= 3 and <= 15);

            DaggerfallSiteProfile template = Assembled(DaggerfallWorldProfileIds.Exterior(Charing));
            DaggerfallSiteProfile origin = WithPopulation(template, originRecord.Id, count: 3);
            DaggerfallSiteProfile destination = WithPopulation(template, destRecord.Id, count: 1);

            DaggerfallSessionComposition composition = new(definitions, origin, DaggerfallTuning.Defaults)
            {
                Profiles = new DaggerfallSiteProfiles([origin, destination]),
            };
            EngineContextFake engine = BuildEngine(origin, destination);
            DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, composition);
            session.Update(new ProductUpdate(OuterUpdate(1), []));
            return new(session, composition, origin, destination, engine);
        }

        internal EngineContextFake CreateEngine() => BuildEngine(Origin, Destination);

        private static EngineContextFake BuildEngine(DaggerfallSiteProfile origin, DaggerfallSiteProfile destination)
        {
            List<string> releases = [];
            ContentFake content = new(releases);
            PopulateContent(content, origin);
            PopulateContent(content, destination);
            SpatialFake spatial = SpatialFake.Create(origin.Geometry.Spatial[0].Sha256, releases);
            return EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        }

        private static DaggerfallSiteProfile WithPopulation(DaggerfallSiteProfile template, DaggerfallSiteId siteId, int count)
        {
            DaggerfallSiteProfile exterior = SameContentAt(template, siteId, DaggerfallWorldProfileKind.Exterior,
                DaggerfallWorldProfileIds.Exterior(siteId).LogicalId);
            NormalizedBillboardSprite billboard = new("sprite/population.png", Hash, 8, 8,
                [new NormalizedAtlasFrame(0, 0, 0, 8, 8)], 0, new Vector2(.5F, .5F), Vector2.One);
            Dictionary<(int Archive, int Record), NormalizedBillboardSprite> billboards = template.BillboardSprites.ToDictionary();
            billboards[(210, 4)] = billboard;
            int factionId = TestPayload.Definitions.Factions.Factions.Values.First(f => f.Id > 0 && f.Type != 10
                && !StringComparer.Ordinal.Equals(f.TypeName, "KnightlyGuard")
                && TestPayload.Definitions.Catalogs.Races.Any(r => r.DonorRaceId == f.Race)).Id;
            DaggerfallPopulationPlacement[] pop = [.. Enumerable.Range(0, count).Select(i =>
                new DaggerfallPopulationPlacement($"population/source/0/{i}", new WorldPoint(1.5F + i, 1F, 1.5F),
                    210, 4, factionId, (byte)(i % 2 == 1 ? 0x20 : 0), (ushort)(300 + i * 19)))];
            return new DaggerfallSiteProfile(
                new ProjectFacts(exterior.Project.PlayerPosition, new Dictionary<long, AuthoredActor>()),
                exterior.Geometry,
                exterior.WorldAppearance,
                exterior.InitialLook,
                exterior.Materials,
                exterior.ActorSprites,
                exterior.MobileSprites,
                exterior.Audio,
                exterior.ClassicPresentation,
                exterior.Site,
                exterior.Doors,
                exterior.ProfileKind,
                exterior.ProfileKey.LogicalId,
                Array.Empty<DaggerfallSitePortal>(),
                exterior.Anchors.Values.ToArray(),
                exterior.Lights,
                exterior.GroundContainerSprites,
                exterior.DungeonMap,
                exterior.DungeonActions,
                exterior.DungeonActionModels,
                exterior.InteriorBuilding,
                exterior.QuestMarkers,
                billboardSprites: billboards,
                terrainTextures: template.TerrainTextures,
                population: pop);
        }

        public void Dispose() => Session.Dispose();
    }
}
