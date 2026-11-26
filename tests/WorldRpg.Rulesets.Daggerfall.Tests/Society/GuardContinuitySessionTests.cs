using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Crime;
using WorldRpg.Rulesets.Daggerfall.Modules.Behavior;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Focused end-to-end tests for #11014 guard continuity:
/// 1. Seeded exterior guard you spoke to witnesses your crime and promotes in place on the same entity;
/// 2. Mid-pursuit save, reload, leave town, and return preserve sparse ActorLedger diff (zero untouched walkers)
///    and resolve the guard once as the watchman with witness memory and watch gear;
/// 3. Unwitnessed/out-of-sight crime spawns fresh Minted watch reinforcements without conscripting a met guard across town.
/// </summary>
public sealed class GuardContinuitySessionTests
{
    [Fact]
    public void Seeded_exterior_guard_spoke_witnesses_crime_and_promotes_in_place_on_same_entity()
    {
        using Fixture f = new();
        DaggerfallSession s = f.Session;
        long guardId = f.GateGuardId;
        long[] untouchedWalkers = f.UntouchedWalkerIds;
        Assert.NotEmpty(untouchedWalkers);
        Assert.True(DaggerfallPeopleSlots.IsDeterministicId(guardId));

        ActorState guardBefore = s.State.Actors.Get(guardId);
        EntityId originalEntity = guardBefore.Actor.Entity;
        string? originalName = s.State.Npcs.Require(guardId).DisplayName;
        Assert.True(guardBefore.Actor.Get<ActorHarmPolicy>().IsLawProtectedCivilian);
        Assert.Equal("city-watch", guardBefore.Actor.Get<FactionComponent>().CreatureFactionId);
        Assert.Equal(ActorOriginKind.SeededSlot, guardBefore.Actor.Get<ActorOriginComponent>().Origin.Kind);
        Assert.Equal(ActorVisualKind.SplatCharacter, guardBefore.Actor.Get<ActorRenderComponent>().Spec.Kind);

        // Speak to the gate guard first -> sets Met = true on live overlay.
        f.TalkTo(guardId);
        Assert.True(guardBefore.Actor.Get<ActorOverlayComponent>().Met);

        // Commit a crime in sight of ONLY this gate guard.
        f.CanSee = id => id == guardId;
        f.CommitCrime("crime:gate-theft");
        f.Step();

        // Same entity, same durable ID, same name, same active NPC — promoted in place.
        DaggerfallLegalResponseSave response = Assert.Single(s.State.Crime.LegalResponses);
        Assert.Equal([guardId], response.Guards);
        Assert.Equal("arrest", s.Law.View?.Phase);
        Assert.True(s.State.Actors.TryGet(guardId, out ActorState? guardAfter));
        Assert.Equal(originalEntity, guardAfter!.Actor.Entity);
        Assert.Equal(originalName, s.State.Npcs.Require(guardId).DisplayName);
        Assert.Equal(DaggerfallNpcPresence.Active, s.State.Npcs.Require(guardId).Presence);

        // Witness memory, Met flag, combat capabilities, mobile 146 render spec, and watch gear all sit on the same actor.
        ActorOverlayComponent overlay = guardAfter.Actor.Get<ActorOverlayComponent>();
        Assert.True(overlay.Met);
        Assert.Contains("crime:gate-theft", overlay.WitnessedCrimes);
        Assert.Equal(DaggerfallArchetypeIds.Guard, overlay.ArchetypeId);
        Assert.True(guardAfter.Actor.Get<ActorHarmPolicy>().IsCombatant);
        Assert.Equal(146, s.DefinitionsByActor[guardId].MobileId);
        ActorVisualSpec visual = guardAfter.Actor.Get<ActorRenderComponent>().Spec;
        Assert.Equal(ActorVisualKind.DirectionalMobile, visual.Kind);
        Assert.Equal(146, visual.MobileId);
        Assert.True(guardAfter.Actor.Get<DaggerfallEnemyPerceptionMemory>().ForcedHostile);
        Assert.NotEmpty(s.State.ActorInventories.EquipmentFor(guardId).Read().Assignments);

        // Untouched civilian walkers remain calm civilians.
        foreach (long walkerId in untouchedWalkers)
        {
            ActorState walker = s.State.Actors.Get(walkerId);
            Assert.True(walker.Actor.Get<ActorHarmPolicy>().IsLawProtectedCivilian);
            Assert.Null(s.DefinitionsByActor[walkerId].MobileId);
            Assert.DoesNotContain(walkerId, response.Guards);
        }
    }

    [Fact]
    public void Mid_pursuit_save_reload_leave_and_return_keeps_sparse_ledger_diff_and_single_watchman()
    {
        using Fixture f = new();
        DaggerfallSession s = f.Session;
        long guardId = f.GateGuardId;
        long[] untouchedWalkers = f.UntouchedWalkerIds;
        string? originalName = s.State.Npcs.Require(guardId).DisplayName;

        // R11014-1: Simulate pre-promotion street wandering for BOTH the guard and an untouched walker
        // (recording idle wander poses the same way DaggerfallSourcePopulation.Step does).
        // The untouched walker must still self-prune to zero bytes, while the promoted guard must
        // preserve their exact wandered position across reload and town return even if saved immediately
        // after promotion before any pursuit movement occurs.
        ActorState guardBeforeWander = s.State.Actors.Get(guardId);
        WorldPoint authoredGuardLocal = guardBeforeWander.Position;
        WorldPoint wanderedGuardLocal = new(authoredGuardLocal.X + 1.25f, authoredGuardLocal.Y, authoredGuardLocal.Z - 0.75f);
        float wanderedGuardHeading = 1.1f;
        guardBeforeWander.ApplyPose(new ActorPose(wanderedGuardLocal, wanderedGuardHeading));
        guardBeforeWander.Actor.Get<ActorOriginComponent>().RecordIdleWanderPose(
            WorldPoint.From(s.Exterior.LocalToProfile(wanderedGuardLocal.ToVector())), wanderedGuardHeading);

        ActorState untouchedWalker = s.State.Actors.Get(untouchedWalkers[0]);
        WorldPoint wanderedCivilianLocal = new(untouchedWalker.Position.X + 2.5f, untouchedWalker.Position.Y, untouchedWalker.Position.Z + 1.5f);
        untouchedWalker.ApplyPose(new ActorPose(wanderedCivilianLocal, 0.4f));
        untouchedWalker.Actor.Get<ActorOriginComponent>().RecordIdleWanderPose(
            WorldPoint.From(s.Exterior.LocalToProfile(wanderedCivilianLocal.ToVector())), 0.4f);

        // Speak to guard, commit crime in sight, promote, resist arrest to enter mid-pursuit, and wound them.
        f.TalkTo(guardId);
        f.CanSee = id => id == guardId;
        f.CommitCrime("crime:pursuit-theft");
        f.Step();
        Assert.Equal("arrest", s.Law.View?.Phase);
        s.ChooseLegal(s.Law.View!.Revision, "resist");
        Assert.Null(s.Law.View);

        ActorState pursuing = s.State.Actors.Get(guardId);
        Track health = pursuing.Stats.GetTrack(TrackId.Parse("health"));
        double woundedHealth = Math.Max(1, health.Maximum.Value - 5);
        health.SetCurrent(woundedHealth);
        int equippedBeforeSave = s.State.ActorInventories.EquipmentFor(guardId).Read().Assignments.Count;
        Assert.True(equippedBeforeSave > 0);
        WorldPoint expectedPursuitProfile = WorldPoint.From(s.Exterior.LocalToProfile(pursuing.Position.ToVector()));

        // Save mid-pursuit (before any post-promotion movement): ONLY the sparse diff for the promoted guard is saved — zero untouched town walkers!
        RulesetSavePayload midPursuitSaveBytes = s.CaptureSave();
        DaggerfallSavePayload midPursuitSave = DaggerfallSavePayload.Read(midPursuitSaveBytes);
        ActorLedgerEntry sparseGuard = Assert.Single(midPursuitSave.ActorLedger, entry => entry.Location?.Kind != ActorLocationKind.OnOverlandBand);
        Assert.Equal(guardId, sparseGuard.DurableId);
        Assert.Equal(ActorOriginKind.SeededSlot, sparseGuard.Origin.Kind);
        Assert.NotNull(sparseGuard.Pose);
        Assert.Equal(expectedPursuitProfile.X, sparseGuard.Pose!.X, 3);
        Assert.Equal(expectedPursuitProfile.Z, sparseGuard.Pose.Z, 3);
        Assert.Equal(DaggerfallArchetypeIds.Guard, sparseGuard.Combat?.PromotedArchetype);
        Assert.True(sparseGuard.Combat?.ForcedHostile);
        Assert.True(sparseGuard.Social?.Met);
        Assert.Contains("crime:pursuit-theft", sparseGuard.Social?.WitnessedCrimes ?? []);
        foreach (long walkerId in untouchedWalkers)
            Assert.DoesNotContain(midPursuitSave.ActorLedger, e => e.DurableId == walkerId);
        Assert.Single(midPursuitSave.ActorInventories, inv => inv.EntityId == guardId);
        foreach (long walkerId in untouchedWalkers)
            Assert.DoesNotContain(midPursuitSave.ActorInventories, inv => inv.EntityId == walkerId);

        // Reload mid-pursuit: guard resolves ONCE as the watchman at the exact wandered pursuit pose (never snapped back to authored slot).
        using DaggerfallSession reloaded = f.Restore(midPursuitSaveBytes);
        reloaded.Population.Reconcile();
        Assert.Single(reloaded.State.Actors.All, a => a.DurableId == guardId);
        Assert.Single(reloaded.State.Npcs.All, n => n.StableKey == Fixture.GateGuardSlotKey);
        ActorState reloadedGuard = reloaded.State.Actors.Get(guardId);
        WorldPoint expectedReloadedLocal = reloaded.Exterior.ProfileToLocal(expectedPursuitProfile);
        Assert.Equal(expectedReloadedLocal.X, reloadedGuard.Position.X, 3);
        Assert.Equal(expectedReloadedLocal.Z, reloadedGuard.Position.Z, 3);
        Assert.Equal(originalName, reloaded.State.Npcs.Require(guardId).DisplayName);
        Assert.Equal(146, reloaded.DefinitionsByActor[guardId].MobileId);
        Assert.True(reloadedGuard.Actor.Get<ActorHarmPolicy>().IsCombatant);
        Assert.Equal(ActorVisualKind.DirectionalMobile, reloadedGuard.Actor.Get<ActorRenderComponent>().Spec.Kind);
        Assert.True(reloadedGuard.Actor.Get<DaggerfallEnemyPerceptionMemory>().ForcedHostile);
        Assert.True(reloadedGuard.Actor.Get<ActorOverlayComponent>().Met);
        Assert.Contains("crime:pursuit-theft", reloadedGuard.Actor.Get<ActorOverlayComponent>().WitnessedCrimes);
        Assert.Equal(woundedHealth, reloadedGuard.Stats.GetTrack(TrackId.Parse("health")).Current, 3);
        Assert.Equal(equippedBeforeSave, reloaded.State.ActorInventories.EquipmentFor(guardId).Read().Assignments.Count);

        // Leave town, save while away, reload while away, then return to town.
        Assert.True(reloaded.TryTransitionTo(f.OtherSite.ProfileKey));
        Assert.False(reloaded.State.Actors.TryGet(guardId, out _));

        RulesetSavePayload awaySaveBytes = reloaded.CaptureSave();
        DaggerfallSavePayload awaySave = DaggerfallSavePayload.Read(awaySaveBytes);
        ActorLedgerEntry detachedGuard = Assert.Single(awaySave.ActorLedger, entry => entry.Location?.Kind != ActorLocationKind.OnOverlandBand);
        Assert.Equal(guardId, detachedGuard.DurableId);
        Assert.Equal(ActorLocationKind.AtSite, detachedGuard.Location?.Kind);
        Assert.True(detachedGuard.Location?.Detached);
        Assert.NotNull(detachedGuard.Pose);
        Assert.Equal(expectedPursuitProfile.X, detachedGuard.Pose!.X, 3);
        Assert.Equal(expectedPursuitProfile.Z, detachedGuard.Pose.Z, 3);
        Assert.Equal(DaggerfallArchetypeIds.Guard, detachedGuard.Combat?.PromotedArchetype);
        Assert.NotNull(detachedGuard.Combat?.DetachedInventory);
        Assert.True(detachedGuard.Social?.Met);
        Assert.Contains("crime:pursuit-theft", detachedGuard.Social?.WitnessedCrimes ?? []);

        using DaggerfallSession awayReloaded = f.Restore(awaySaveBytes);
        Assert.True(awayReloaded.TryTransitionTo(f.Town.ProfileKey));
        awayReloaded.Population.Reconcile();
        f.Step(awayReloaded);

        // Guard resolves ONCE on return — never both a civilian and a watchman — at their exact persisted pose with full continuity and rearrest.
        Assert.Single(awayReloaded.State.Actors.All, a => a.DurableId == guardId);
        Assert.Single(awayReloaded.State.Npcs.All, n => n.StableKey == Fixture.GateGuardSlotKey);
        Assert.Single(awayReloaded.State.Crime.LegalResponses.Single().Guards, id => id == guardId);
        Assert.Equal("arrest", awayReloaded.Law.View?.Phase);

        ActorState returnedGuard = awayReloaded.State.Actors.Get(guardId);
        WorldPoint expectedReturnedLocal = awayReloaded.Exterior.ProfileToLocal(expectedPursuitProfile);
        Assert.Equal(expectedReturnedLocal.X, returnedGuard.Position.X, 3);
        Assert.Equal(expectedReturnedLocal.Z, returnedGuard.Position.Z, 3);
        Assert.Equal(originalName, awayReloaded.State.Npcs.Require(guardId).DisplayName);
        Assert.Equal(146, awayReloaded.DefinitionsByActor[guardId].MobileId);
        Assert.True(returnedGuard.Actor.Get<ActorHarmPolicy>().IsCombatant);
        Assert.Equal(ActorVisualKind.DirectionalMobile, returnedGuard.Actor.Get<ActorRenderComponent>().Spec.Kind);
        Assert.Equal(146, returnedGuard.Actor.Get<ActorRenderComponent>().Spec.MobileId);
        Assert.True(returnedGuard.Actor.Get<ActorOverlayComponent>().Met);
        Assert.Contains("crime:pursuit-theft", returnedGuard.Actor.Get<ActorOverlayComponent>().WitnessedCrimes);
        Assert.Equal(woundedHealth, returnedGuard.Stats.GetTrack(TrackId.Parse("health")).Current, 3);
        Assert.Equal(equippedBeforeSave, awayReloaded.State.ActorInventories.EquipmentFor(guardId).Read().Assignments.Count);
    }

    [Fact]
    public void Unwitnessed_crime_out_of_gate_guard_sight_spawns_fresh_minted_reinforcements_without_conscripting_met_guard()
    {
        using Fixture f = new();
        DaggerfallSession s = f.Session;
        long gateGuardId = f.GateGuardId;
        long distantBystanderId = f.UntouchedWalkerIds[0];

        // Speak to the gate guard first so they are Met.
        f.TalkTo(gateGuardId);
        Assert.True(s.State.Actors.Get(gateGuardId).Actor.Get<ActorOverlayComponent>().Met);

        // Commit a reported crime witnessed by a civilian bystander across town — NOT by the gate guard!
        f.CanSee = id => id == distantBystanderId;
        f.CommitCrime("crime:across-town-theft");
        f.Step();

        DaggerfallLegalResponseSave response = Assert.Single(s.State.Crime.LegalResponses);
        Assert.NotEmpty(response.Guards);
        Assert.DoesNotContain(gateGuardId, response.Guards);

        // Reinforcements are newly Minted people with distinct identities.
        foreach (long reinforcementId in response.Guards)
        {
            Assert.NotEqual(gateGuardId, reinforcementId);
            Assert.Equal(146, s.DefinitionsByActor[reinforcementId].MobileId);
            ActorState reinforcement = s.State.Actors.Get(reinforcementId);
            Assert.Equal(ActorOriginKind.Minted, reinforcement.Actor.Get<ActorOriginComponent>().Origin.Kind);
            Assert.True(reinforcement.Actor.Get<ActorHarmPolicy>().IsCombatant);
        }

        // The gate guard you met across town remains calm and unconscripted.
        ActorState gateGuard = s.State.Actors.Get(gateGuardId);
        Assert.True(gateGuard.Actor.Get<ActorHarmPolicy>().IsLawProtectedCivilian);
        Assert.Null(s.DefinitionsByActor[gateGuardId].MobileId);
        Assert.Equal(ActorVisualKind.SplatCharacter, gateGuard.Actor.Get<ActorRenderComponent>().Spec.Kind);
        Assert.True(gateGuard.Actor.Get<ActorOverlayComponent>().Met);
        Assert.Empty(gateGuard.Actor.Get<ActorOverlayComponent>().WitnessedCrimes);
    }

    private sealed class Fixture : IDisposable
    {
        internal const string GateGuardSlotKey = "population/guard/gate-0";

        private readonly DaggerfallSessionComposition _composition;
        private readonly DaggerfallSiteProfiles _profiles;
        private DaggerfallSession? _activeSession;
        private ulong _frame = 1;

        internal DaggerfallSiteProfile Town { get; }
        internal DaggerfallSiteProfile OtherSite { get; }
        internal DaggerfallSession Session { get; }
        internal long GateGuardId { get; }
        internal long[] UntouchedWalkerIds { get; }
        internal Func<long, bool> CanSee { get; set; } = _ => true;

        internal Fixture()
        {
            DaggerfallDefinitions definitions = TestPayload.Definitions;
            DaggerfallSiteProfile baseTown = Assembled(DaggerfallWorldProfileIds.Exterior(Charing));
            DaggerfallPopulationPlacement seededBase = baseTown.Population.First(p => p.IsGuard);
            WorldPoint playerStart = baseTown.Project.PlayerPosition ?? new WorldPoint(0f, 0f, 0f);
            DaggerfallPopulationPlacement gateGuardPlacement = new(
                GateGuardSlotKey,
                new WorldPoint(playerStart.X, playerStart.Y, playerStart.Z - 2f),
                seededBase.BillboardArchive,
                seededBase.BillboardRecord,
                seededBase.FactionId,
                Flags: 0,
                NameSeed: 4242)
            {
                IsGuard = true,
            };
            // Keep 4 ordinary non-guard civilian walkers alongside our seeded gate guard.
            DaggerfallPopulationPlacement[] civilianWalkers = [.. baseTown.Population
                .Where(p => !p.IsGuard)
                .Take(4)];
            Town = new DaggerfallSiteProfile(
                new ProjectFacts(baseTown.Project.PlayerPosition, new Dictionary<long, AuthoredActor>()),
                baseTown.Geometry,
                baseTown.WorldAppearance,
                baseTown.InitialLook,
                baseTown.Materials,
                baseTown.ActorSprites,
                baseTown.MobileSprites,
                baseTown.Audio,
                baseTown.ClassicPresentation,
                baseTown.Site,
                baseTown.Doors,
                baseTown.ProfileKind,
                baseTown.ProfileKey.LogicalId,
                Array.Empty<DaggerfallSitePortal>(),
                baseTown.Anchors.Values.ToArray(),
                baseTown.Lights,
                baseTown.GroundContainerSprites,
                baseTown.DungeonMap,
                baseTown.DungeonActions,
                baseTown.DungeonActionModels,
                baseTown.InteriorBuilding,
                baseTown.QuestMarkers,
                billboardSprites: baseTown.BillboardSprites,
                terrainTextures: baseTown.TerrainTextures,
                population: [gateGuardPlacement, .. civilianWalkers]);
            DaggerfallSiteProfile emptyBase = ReadInputs(TestData.RepositoryRoot);
            OtherSite = SameContentAt(emptyBase, baseTown.Site!.Value, DaggerfallWorldProfileKind.Interior, "guard-continuity-other",
                new ProjectFacts(emptyBase.Project.PlayerPosition, new Dictionary<long, AuthoredActor>()));
            _profiles = new DaggerfallSiteProfiles([Town, OtherSite]);
            _composition = new DaggerfallSessionComposition(definitions, Town,
                DaggerfallTuning.Defaults with { Law = new(0, 0, 2, 2, 12.8f, 51.2f, 3.2) })
            {
                Profiles = _profiles,
            };

            Session = DaggerfallSession.StartNew(BuildEngine().Context, _composition);
            Session.ApplyProductMode(ProductMode.Playing);
            _activeSession = Session;
            Session.AdvanceElapsedTime(8 * 60 * 60); // Daytime so population is admitted.
            Session.Population.Reconcile();

            DaggerfallNpc gateNpc = Assert.Single(Session.State.Npcs.All, npc => npc.StableKey == GateGuardSlotKey);
            GateGuardId = gateNpc.DurableId;
            UntouchedWalkerIds = [.. Session.State.Npcs.All
                .Where(npc => DaggerfallCivilians.IsSourcePopulation(npc) && npc.DurableId != GateGuardId && Session.State.Actors.TryGet(npc.DurableId, out _))
                .Select(npc => npc.DurableId)];
        }

        internal void TalkTo(long npcId, DaggerfallSession? session = null)
        {
            session ??= Session;
            DaggerfallActivationTarget target = Assert.Single(session.Dialogue.NpcTargets(),
                value => value.Identity.Value == checked((ulong)npcId));
            Assert.True(session.Dialogue.ActivateNpc(new(DaggerfallActivationMode.Talk, target)).Applied);
            session.Dialogue.Close();
        }

        internal void CommitCrime(string operationId, DaggerfallSession? session = null)
        {
            session ??= Session;
            DaggerfallCrimeWitnessEvidence witnesses = session.Crimes.QueryWitnesses();
            session.State.Crime.RecordIncident(new DaggerfallCrimeIncidentSave(
                operationId,
                DaggerfallCrimeKind.Theft,
                DaggerfallCrimeStage.Completed,
                DaggerfallActorIdentity.PlayerEntityId,
                null,
                session.Site.Region!.Value,
                0,
                DaggerfallCrimeTargetKind.Other,
                witnesses,
                DaggerfallCrimeGuildCredit.None,
                Reported: witnesses.Query == DaggerfallCrimeWitnessQuery.CompletedWithWitnesses));
        }

        internal void Step(DaggerfallSession? session = null)
        {
            session ??= Session;
            session.Update(new ProductUpdate(OuterUpdate(_frame++), []));
        }

        internal DaggerfallSession Restore(RulesetSavePayload save)
        {
            DaggerfallSession restored = DaggerfallSession.Restore(BuildEngine().Context, _composition, save);
            restored.ApplyProductMode(ProductMode.Playing);
            _activeSession = restored;
            return restored;
        }

        private EngineContextFake BuildEngine()
        {
            List<string> releases = [];
            ContentFake content = new(releases);
            PopulateContent(content, Town);
            PopulateContent(content, OtherSite);
            SpatialFake spatial = SpatialFake.Create(Town.Geometry.Spatial[0].Sha256, releases);
            spatial.KeepPosition = true;
            spatial.FloorHit = request => request.Direction.Y < 0
                ? default(SpatialHit) with { Present = true, Point = new(request.Origin.X, 0, request.Origin.Z), Normal = Vector3.UnitY, Converged = true }
                : default;
            PerceptionFake perception = PerceptionFake.Create();
            perception.Responder = request =>
            {
                List<PerceptionPair> pairs = [];
                foreach (var observer in request.Observers.ToArray())
                {
                    long durableId = checked((long)observer.Entity);
                    if (durableId != DaggerfallActorIdentity.PlayerEntityId && !CanSee(durableId)) continue;
                    foreach (var target in request.Targets.ToArray())
                        pairs.Add(new PerceptionPair(observer.Entity, target.Entity, 2, 1, PerceptionPairKind.Visible, 1));
                }
                return Receipt([.. pairs]);
            };
            return EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), perception.Service);
        }

        private long ResolveDurableId(ulong entity)
        {
            DaggerfallSession? active = _activeSession ?? Session;
            if (active is not null)
            {
                foreach (ActorState actor in active.State.Actors.All)
                    if (actor.Actor.Entity.Value == entity) return actor.DurableId;
            }
            return -1;
        }

        public void Dispose() => Session.Dispose();
    }
}
