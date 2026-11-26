using Rusty.Engine;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// NPC identity: stable keys return one person, civilians regenerate distinct, presence and
/// relocation preserve references, and saves round-trip.
/// </summary>
public sealed class DaggerfallNpcRegistryTests
{
    [Fact]
    public void Stable_keys_return_one_person_while_civilians_stay_distinct()
    {
        DaggerfallNpcRegistry npcs = Registry();
        DaggerfallNpcSite site = new(17, "Daggerfall", "Mages Guild");
        DaggerfallNpcAppearance look = new("Breton", "Female", 211, 12, 7, 0);

        long first = npcs.RegisterStable(DaggerfallNpcKind.Questor, "M0B00Y00", site, look, "questor", ["talk", "quest"]);
        long second = npcs.RegisterStable(DaggerfallNpcKind.Questor, "M0B00Y00", site, look, "questor", ["talk", "quest"]);
        Assert.Equal(first, second);

        // Identical visuals still mint distinct civilians.
        long civilianA = npcs.RegisterCivilian(site, look, "civilian", ["talk"]);
        long civilianB = npcs.RegisterCivilian(site, look, "civilian", ["talk"]);
        Assert.NotEqual(civilianA, civilianB);
        Assert.NotEqual(first, civilianA);

        // Civilians cannot hold a stable key.
        Assert.Throws<ArgumentOutOfRangeException>(() => npcs.RegisterStable(DaggerfallNpcKind.Civilian, "key", site, look, "civilian", ["talk"]));
        Assert.Throws<ArgumentOutOfRangeException>(() => npcs.RegisterCivilian(new DaggerfallNpcSite(62, "Nowhere", string.Empty), look, "civilian", ["talk"]));

        // Placing and hiding preserve the reference; the detached pose is held only until a live
        // actor owns it.
        DaggerfallWorldProfileKey profile = new DaggerfallWorldProfileKey(new DaggerfallSiteId(17, 0),
            DaggerfallWorldProfileKind.Exterior, "daggerfall-exterior").Validate();
        npcs.Place(first, profile, new(100, 200, 300));
        npcs.SetPresence(first, DaggerfallNpcPresence.Hidden);
        DaggerfallNpc questor = npcs.Require(first);
        Assert.Equal(DaggerfallNpcPresence.Hidden, questor.Presence);
        Assert.Equal((100, 200, 300), (questor.X, questor.Y, questor.Z));
        Assert.Equal(site, questor.Site);

        npcs.ReleasePose(first);
        DaggerfallNpc live = npcs.Require(first);
        Assert.Equal(profile, live.Profile);
        Assert.Null(live.X);
        npcs.Bind(civilianA, profile);
        Assert.Equal((profile, (float?)null, DaggerfallNpcPresence.Active),
            (npcs.Require(civilianA).Profile, npcs.Require(civilianA).X, npcs.Require(civilianA).Presence));
    }

    [Fact]
    public void Npcs_round_trip_through_save_records()
    {
        DaggerfallNpcRegistry npcs = Registry();
        DaggerfallNpcSite site = new(17, "Daggerfall", string.Empty);
        DaggerfallNpcAppearance look = new("Nord", "Male", 210, 4, 3, 5);
        long questor = npcs.RegisterStable(DaggerfallNpcKind.Questor, "Q", site, look, "questor", ["talk", "quest"]);
        npcs.SetPresence(questor, DaggerfallNpcPresence.Hidden);
        long civilian = npcs.RegisterCivilian(site, look, "civilian", ["talk"]);

        DaggerfallNpcSave saved = new([.. npcs.Capture().Select(npc => new DaggerfallNpcEntry(
            npc.DurableId, (int)npc.Kind, npc.StableKey, npc.Site.Region, npc.Site.Location, npc.Site.Building,
            npc.Appearance.Race, npc.Appearance.Gender, npc.Appearance.BillboardArchive, npc.Appearance.BillboardRecord,
            npc.Appearance.NameSeed, npc.Appearance.FactionId, npc.Role, [.. npc.Services],
            (int)npc.Presence, npc.X, npc.Y, npc.Z))]);
        saved.Validate();

        DaggerfallNpcRegistry restored = Registry();
        restored.Restore(saved.Entries.Select(entry => new DaggerfallNpc(
            entry.DurableId, (DaggerfallNpcKind)entry.Kind, entry.StableKey,
            new DaggerfallNpcSite(entry.Region, entry.Location, entry.Building),
            new DaggerfallNpcAppearance(entry.Race, entry.Gender, entry.BillboardArchive, entry.BillboardRecord, entry.NameSeed, entry.FactionId),
            entry.Role, entry.Services, (DaggerfallNpcPresence)entry.Presence, entry.X, entry.Y, entry.Z)));

        // The questor keeps its identity and presence; the civilian keeps its own identity too.
        Assert.Equal(questor, restored.RegisterStable(DaggerfallNpcKind.Questor, "Q", site, look, "questor", ["talk", "quest"]));
        Assert.Equal(DaggerfallNpcPresence.Hidden, restored.Require(questor).Presence);
        Assert.Equal(civilian, restored.Require(civilian).DurableId);
    }

    [Fact]
    public void Source_population_key_reuses_one_identity_across_reentry_and_restore()
    {
        DaggerfallNpcRegistry npcs = Registry();
        DaggerfallNpcSite site = new(17, "Daggerfall", string.Empty);
        DaggerfallNpcAppearance look = new("Breton", "Female", 211, 12, 7, 0);

        long first = npcs.RegisterPopulationCivilian(site, "population/17-2/0/4", look, "civilian", ["talk"]);
        long reentry = npcs.RegisterPopulationCivilian(site, "population/17-2/0/4", look, "civilian", ["talk"]);

        Assert.Equal(first, reentry);
        npcs.SetPresence(first, DaggerfallNpcPresence.Hidden);
        DaggerfallNpc saved = npcs.Require(first);

        DaggerfallNpcRegistry restored = Registry();
        restored.Restore([saved]);

        Assert.Equal(first, restored.RegisterPopulationCivilian(site, saved.StableKey, look, "civilian", ["talk"]));
        Assert.Equal(DaggerfallNpcPresence.Hidden, restored.Require(first).Presence);
    }

    [Fact]
    public void Source_population_facts_refresh_without_replacing_identity_or_pose()
    {
        DaggerfallNpcRegistry npcs = Registry();
        DaggerfallNpcSite site = new(17, "Daggerfall", string.Empty);
        DaggerfallNpcAppearance original = new("Breton", "Female", 211, 12, 7, 0);
        DaggerfallNpcAppearance source = new("Nord", "Male", 212, 13, 8, 41);

        long id = npcs.RegisterPopulationCivilian(site, "population/17-2/0/4", original, "civilian", ["talk"]);
        npcs.Place(id, new DaggerfallWorldProfileKey(new DaggerfallSiteId(17, 2), DaggerfallWorldProfileKind.Exterior, "population").Validate(), new(1, 2, 3));
        npcs.SetPresence(id, DaggerfallNpcPresence.Hidden);
        npcs.RefreshPopulationFacts(id, source, "guard", ["talk", "arrest"]);

        DaggerfallNpc refreshed = npcs.Require(id);
        Assert.Equal(id, refreshed.DurableId);
        Assert.Equal("population/17-2/0/4", refreshed.StableKey);
        Assert.Equal(source, refreshed.Appearance);
        Assert.Equal("guard", refreshed.Role);
        Assert.Equal(["talk", "arrest"], refreshed.Services);
        Assert.Equal(DaggerfallNpcPresence.Hidden, refreshed.Presence);
        Assert.Equal((1F, 2F, 3F), (refreshed.X, refreshed.Y, refreshed.Z));
    }

    [Fact]
    public void Hidden_population_is_not_a_live_gameplay_actor()
    {
        DaggerfallNpcRegistry npcs = Registry();
        DaggerfallNpcSite site = new(17, "Daggerfall", string.Empty);
        DaggerfallNpcAppearance look = new("Breton", "Male", 211, 12, 7, 0);
        long id = npcs.RegisterPopulationCivilian(site, "population/17-2/0/4", look, "civilian", ["talk"]);

        Assert.True(npcs.IsGameplayActive(id));
        npcs.SetPresence(id, DaggerfallNpcPresence.Hidden);
        Assert.False(npcs.IsGameplayActive(id));
        Assert.True(npcs.IsGameplayActive(DaggerfallActorIdentity.PlayerEntityId));
    }

    [Fact]
    public void Live_townspeople_project_from_EntityStore_ActorPersonComponent_with_zero_untouched_ActorLedger_entries()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile charing = Assembled(DaggerfallWorldProfileIds.Exterior(Charing));
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, charing);
        SpatialFake spatial = SpatialFake.Create(charing.Geometry.Spatial[0].Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        DaggerfallSessionComposition composition = new(definitions, charing, DaggerfallTuning.Defaults)
        {
            Profiles = new([charing]),
        };

        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, composition);
        session.Update(new ProductUpdate(OuterUpdate(1), []));
        session.AdvanceElapsedTime(8 * 60 * 60);

        DaggerfallNpc[] liveWalkers = [.. session.State.Npcs.All.Where(npc =>
            npc.Kind == DaggerfallNpcKind.Civilian
            && npc.Profile == charing.ProfileKey
            && DaggerfallPeopleSlots.IsDeterministicId(npc.DurableId))];
        Assert.NotEmpty(liveWalkers);

        // Every live townsperson carries ActorPersonComponent directly on EntityStore, and untouched baselines keep 0 ledger entries.
        Assert.All(liveWalkers, walker =>
        {
            Assert.True(session.State.Actors.TryGet(walker.DurableId, out ActorState? actor));
            Assert.True(actor!.Actor.TryGet<ActorPersonComponent>(out ActorPersonComponent? person));
            Assert.Equal(walker.DurableId, person!.Snapshot.DurableId);
            Assert.Equal(charing.ProfileKey, person.Snapshot.Profile);
        });
        Assert.DoesNotContain(session.State.ActorLedger.All, entry => entry.Location?.Kind != ActorLocationKind.OnOverlandBand);

        // Mutating display name on one live townsperson updates ActorPersonComponent immediately and saves ONLY that one actor.
        long namedId = liveWalkers[0].DurableId;
        session.State.Npcs.SetDisplayName(namedId, "Talia the Weaver");
        Assert.Equal("Talia the Weaver", session.State.Actors.Get(namedId).Actor.Get<ActorPersonComponent>().Snapshot.DisplayName);
        Assert.Equal("Talia the Weaver", session.State.Npcs.Require(namedId).DisplayName);

        DaggerfallSavePayload saved = DaggerfallSavePayload.Read(session.CaptureSave());
        ActorLedgerEntry onlySaved = Assert.Single(saved.ActorLedger, entry => entry.Location?.Kind != ActorLocationKind.OnOverlandBand);
        Assert.Equal(namedId, onlySaved.DurableId);
        Assert.Equal("Talia the Weaver", onlySaved.Social?.DisplayNameOverride);
    }

    [Fact]
    public void Minted_npc_live_overlay_flushes_to_ActorLedger_on_site_unload_and_survives_save_reload_and_return()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile hold = ReadInputs(TestData.RepositoryRoot);
        DaggerfallSiteProfile castle = Assembled(DaggerfallWorldProfileIds.Exterior(Charing));
        DaggerfallSiteProfiles profiles = new([hold, castle]);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, hold);
        PopulateContent(content, castle);
        SpatialFake spatial = SpatialFake.Create(hold.Geometry.Spatial[0].Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        DaggerfallSessionComposition composition = new(definitions, hold, DaggerfallTuning.Defaults) { Profiles = profiles };

        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, composition);
        DaggerfallSiteRecord site = definitions.Locations.Records.Single(record => record.Id == hold.Site!.Value);
        (int archive, int record) = hold.BillboardSprites.Keys.First();
        long mintedId = session.State.Npcs.RegisterCivilian(
            new DaggerfallNpcSite(site.Id.Region, site.Name, string.Empty),
            new DaggerfallNpcAppearance("Breton", "Female", archive, record, 17, 0), "civilian", ["talk"]);
        session.State.Npcs.SetDisplayName(mintedId, "Sera the Traveller");
        session.Civilians.Materialize(mintedId, new ActorPose(hold.Project.PlayerPosition ?? default, 0f));
        session.State.Npcs.Bind(mintedId, hold.ProfileKey);

        ActorState live = session.State.Actors.Get(mintedId);
        Assert.True(live.Actor.Has<ActorPersonComponent>());
        ActorOverlayComponent overlay = live.Actor.Get<ActorOverlayComponent>();
        overlay.Met = true;
        overlay.IsMuted = true;

        // Leaving site unloads EntityStore entity, flushes overlay metadata into ActorLedger, and keeps State.Npcs coherent.
        Assert.True(session.TryTransitionTo(castle.ProfileKey));
        Assert.False(session.State.Actors.TryGet(mintedId, out _));
        Assert.Equal("Sera the Traveller", session.State.Npcs.Require(mintedId).DisplayName);

        RulesetSavePayload savedAway = session.CaptureSave();
        ContentFake reloadContent = new(releases);
        PopulateContent(reloadContent, hold);
        PopulateContent(reloadContent, castle);
        SpatialFake reloadSpatial = SpatialFake.Create(hold.Geometry.Spatial[0].Sha256, releases);
        EngineContextFake reloadEngine = EngineContextFake.Create(reloadContent, reloadSpatial.Service, new AppearanceFake(releases));

        using DaggerfallSession restored = DaggerfallSession.Restore(reloadEngine.Context, composition, savedAway);
        Assert.Equal("Sera the Traveller", restored.State.Npcs.Require(mintedId).DisplayName);
        Assert.True(restored.TryTransitionTo(hold.ProfileKey));

        ActorState returned = Assert.Single(restored.State.Actors.All, a => a.DurableId == mintedId);
        Assert.Equal("Sera the Traveller", returned.Actor.Get<ActorPersonComponent>().Snapshot.DisplayName);
        ActorOverlayComponent restoredOverlay = returned.Actor.Get<ActorOverlayComponent>();
        Assert.True(restoredOverlay.Met);
        Assert.True(restoredOverlay.IsMuted);
    }

    private static DaggerfallNpcRegistry Registry()
    {
        DaggerfallNpcRegistry npcs = new()
        {
            Identities = DurableIdentityAllocator.Restore(new DurableIdentityState(
            [
                new KindAllocatorState(DurableIdentityKind.Actor, 1_000_000, [], []),
            ])),
        };
        return npcs;
    }
}
