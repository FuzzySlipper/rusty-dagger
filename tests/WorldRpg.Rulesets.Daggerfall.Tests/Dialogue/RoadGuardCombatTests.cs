using System.Numerics;
using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Host;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Modules.Behavior;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.Travel;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Den #11105 (Retainers 2): Road Guard player-ally combatant materialization &amp; targeting through ordinary
/// <see cref="DaggerfallEnemyBehaviorModule"/> + <see cref="DaggerfallActorPlacement.SelectAllyTarget"/>,
/// LairTruce &amp; crime-law respect, exterior follow-player locomotion &amp; dialogue stances, camp/inn rest healing,
/// HUD health/stance portraits, permanent death corpse/portrait cleanup, and live Caravan Raid (#11080) continuity.
/// </summary>
public sealed class RoadGuardCombatTests
{
    private const int DaggerfallRegion = 17;

    private static PerceptionFake CreateAllVisiblePerception(double maxDistance = 60d, double? playerWallOcclusionDistance = null)
    {
        PerceptionFake perception = PerceptionFake.Create();
        perception.Responder = request => Receipt(
            [.. request.Observers.ToArray().SelectMany(observer => request.Targets.ToArray().Select(target =>
            {
                Vector3 toward = target.Center - observer.Origin;
                double dist = toward.Length();
                double facing = dist > 0 ? Vector3.Dot(Vector3.Normalize(observer.Forward), toward / (float)dist) : 1d;
                bool playerOccluded = playerWallOcclusionDistance is double wallDist
                    && target.Entity == (ulong)DaggerfallActorIdentity.PlayerEntityId
                    && dist > wallDist;
                PerceptionPairKind kind = playerOccluded || dist > maxDistance || dist > observer.MaximumDistance
                    ? PerceptionPairKind.Occluded
                    : facing < observer.MinimumFacingCosine ? PerceptionPairKind.FacingRejected : PerceptionPairKind.Visible;
                return new PerceptionPair(observer.Entity, target.Entity, dist, facing, kind, kind == PerceptionPairKind.Visible ? 1d : 0d);
            }))]);
        return perception;
    }

    private sealed class ExteriorCombatFixture : IDisposable
    {
        private readonly List<string> _releases = [];
        internal DaggerfallDefinitions Definitions { get; }
        internal DaggerfallSiteProfile ExteriorProfile { get; }
        internal DaggerfallSessionComposition Composition { get; }
        internal SpatialFake Spatial { get; }
        internal PerceptionFake Perception { get; }
        internal AppearanceFake Appearance { get; }
        internal EngineContextFake Engine { get; }

        private static void WireStraightLineNavigation(SpatialFake spatial) => spatial.NavigationResult = request =>
        {
            Vector3 delta = request.Target - request.From;
            float dist = delta.Length();
            float step = MathF.Min(0.5f, dist);
            Vector3 next = dist <= 1e-5f
                ? request.Target
                : request.From + Vector3.Normalize(delta) * step;
            return new NavigationStepResult(
                ReadOnlyMemory<PlanarNavCell>.Empty,
                ReadOnlyMemory<NavigationPathEdge>.Empty,
                NavigationPathOutcome.Reached,
                next,
                default,
                default,
                dist,
                1, 1, 1, 1, 0, false, default, default);
        };

        internal ExteriorCombatFixture()
        {
            string root = TestData.RepositoryRoot;
            Definitions = TestPayload.Definitions;
            DaggerfallSiteRecord site = Definitions.Locations.Records.First(r =>
                r.Id.Region == DaggerfallRegion && r.Exterior is not null && r.Kind == DaggerfallSiteKind.TownCity);
            ExteriorProfile = Assembled(DaggerfallWorldProfileIds.Exterior(site.Id));
            DaggerfallSiteProfiles profiles = new([ExteriorProfile]);
            ContentFake content = new(_releases);
            PopulateContent(content, ExteriorProfile);
            Spatial = SpatialFake.Create(ExteriorProfile.Geometry.Spatial[0].Sha256, _releases);
            Spatial.KeepPosition = true;
            WireStraightLineNavigation(Spatial);
            Perception = CreateAllVisiblePerception(playerWallOcclusionDistance: 20d);
            Appearance = new AppearanceFake(_releases);
            Engine = EngineContextFake.Create(content, Spatial.Service, Appearance, Perception.Service);
            ResolvedGameComposition resolved = Resolve(FullContent(root), new GameBundleId("daggerfall.classic")).RequireComposition();
            Composition = new(Definitions, ExteriorProfile, DaggerfallTuning.Defaults, resolved.Identity) { Profiles = profiles };
        }

        internal DaggerfallSession Start() => DaggerfallSession.StartNew(Engine.Context, Composition);

        internal DaggerfallSession Restore(RulesetSavePayload saved, out SpatialFake restoredSpatial, out AppearanceFake restoredAppearance)
        {
            ContentFake content = new(_releases);
            PopulateContent(content, ExteriorProfile);
            restoredSpatial = SpatialFake.Create(ExteriorProfile.Geometry.Spatial[0].Sha256, _releases);
            restoredSpatial.KeepPosition = true;
            WireStraightLineNavigation(restoredSpatial);
            PerceptionFake perception = CreateAllVisiblePerception();
            restoredAppearance = new AppearanceFake(_releases);
            EngineContextFake engine = EngineContextFake.Create(content, restoredSpatial.Service, restoredAppearance, perception.Service);
            return DaggerfallSession.Restore(engine.Context, Composition, saved);
        }

        public void Dispose() { }
    }

    private static DaggerfallNpc HireRoadGuardInTown(DaggerfallSession session)
    {
        session.AdvanceElapsedTime(10 * 3600);
        session.State.Currency.ReceiveGold(300UL);
        DaggerfallNpc candidate = session.State.Npcs.All.First(npc =>
            npc.Presence == DaggerfallNpcPresence.Active
            && session.PartyHire.IsHireable(npc)
            && session.State.Actors.TryGet(npc.DurableId, out ActorState? live)
            && !live.IsDefeated);
        DaggerfallPartyHireResult hired = session.PartyHire.Resolve(candidate, DaggerfallDialogueTopic.HireCompanion);
        Assert.True(hired.Accepted, hired.Message);
        return candidate;
    }

    [Fact]
    public void Road_guard_fights_hostile_enemies_as_player_ally_while_respecting_lair_truce_and_peaceful_guards()
    {
        using var fixture = new ExteriorCombatFixture();
        using DaggerfallSession session = fixture.Start();
        session.ApplyProductMode(ProductMode.Playing);
        DaggerfallNpc guardNpc = HireRoadGuardInTown(session);
        ActorState guard = session.State.Actors.Get(guardNpc.DurableId);

        Assert.True(guard.Actor.Get<ActorHarmPolicy>().IsCombatant);
        Assert.Equal("player-ally", guard.Actor.Get<FactionComponent>().EffectiveTeam);
        Assert.Equal("warrior", guard.Actor.Get<ActorOverlayComponent>().ArchetypeId);
        Assert.NotEmpty(session.State.ActorInventories.EquipmentFor(guard.DurableId).Read().Assignments);

        // Warm one tick so exterior floating-origin recentering settles before placing test actors.
        session.Update(new ProductUpdate(OuterUpdate(2), []));
        Vector3 playerPos = session.State.PlayerControl.Position!.Value.ToVector();

        // 1. Place Road Guard, an unprovoked city-watch guard, and a dormant LairTruce orc beyond LairTruceWakeRadius (28m from player):
        //    Road Guard MUST respect both crime law (unprovoked city watch) AND LairTruce (dormant enemy)!
        guard.ApplyPose(new ActorPose(WorldPoint.From(playerPos + new Vector3(28.0f, 0f, 0f)), 0f));
        long watchId = session.SpawnActor("encounter-city-watch-the-haltmeister", new ActorPose(WorldPoint.From(playerPos + new Vector3(29.5f, 0f, 0f)), 0f));
        long dormantOrcId = session.SpawnActor("orc", new ActorPose(WorldPoint.From(playerPos + new Vector3(30.5f, 0f, 0f)), 0f), lairTruce: true);
        // Set Wait stance briefly so idle follow doesn't step back toward the distant player before the target check.
        Assert.True(session.PartyHire.Resolve(guardNpc, DaggerfallDialogueTopic.PartyWait).Accepted);
        session.Update(new ProductUpdate(OuterUpdate(3), []));
        Assert.Null(session.EnemyBehavior.LastEvidence[guard.DurableId].Target);
        Assert.Equal(EnemyBehaviorState.Idle, session.EnemyBehavior.LastEvidence[guard.DurableId].State);

        // 2. Active hostile enemy (no LairTruce) enters sight right beside Road Guard -> engages & deals real melee damage through ordinary EnemyBehaviorModule!
        Vector3 guardFightPos = playerPos + new Vector3(1.5f, 0f, 0f);
        Vector3 raiderFightPos = playerPos + new Vector3(2.3f, 0f, 0f);
        guard.ApplyPose(new ActorPose(WorldPoint.From(guardFightPos), ActorHeading.Yaw(raiderFightPos - guardFightPos)));
        guard.Stats.GetStat(StatId.Parse("agility")).BaseValue = 100;
        guard.Stats.GetStat(StatId.Parse("strength")).BaseValue = 100;
        long activeRaiderId = session.SpawnActor("orc", new ActorPose(WorldPoint.From(raiderFightPos), ActorHeading.Yaw(guardFightPos - raiderFightPos)), lairTruce: false);
        ActorState activeRaider = session.State.Actors.Get(activeRaiderId);
        session.Touches(activeRaiderId);
        Track raiderHealth = activeRaider.Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Health.Value));
        double raiderHpBefore = raiderHealth.Current;

        bool guardTargetedRaider = false;
        for (ulong step = 4; step <= 180; step++)
        {
            session.Update(new ProductUpdate(OuterUpdate(step), []));
            if (session.EnemyBehavior.LastEvidence.TryGetValue(guard.DurableId, out var ev)
                && ev.Target?.DurableId == activeRaiderId)
            {
                guardTargetedRaider = true;
            }
            if (raiderHealth.Current < raiderHpBefore)
                break;
        }
        Assert.True(guardTargetedRaider, "Road Guard must acquire active hostile enemy via ordinary SelectAllyTarget / EnemyBehaviorModule.");
        Assert.NotEqual(watchId, session.EnemyBehavior.LastEvidence[guard.DurableId].Target?.DurableId);
        Assert.NotEqual(dormantOrcId, session.EnemyBehavior.LastEvidence[guard.DurableId].Target?.DurableId);
        Assert.True(raiderHealth.Current < raiderHpBefore,
            $"Road Guard melee strikes must deal real combat damage to the active hostile enemy (guardState={session.EnemyBehavior.LastEvidence[guard.DurableId].State}, raiderHp={raiderHealth.Current}/{raiderHpBefore}).");
    }

    [Fact]
    public void Road_guard_follows_player_in_follow_stance_holds_in_wait_stance_and_exposes_dialogue_commands()
    {
        using var fixture = new ExteriorCombatFixture();
        using DaggerfallSession session = fixture.Start();
        session.ApplyProductMode(ProductMode.Playing);
        DaggerfallNpc guardNpc = HireRoadGuardInTown(session);
        ActorState guard = session.State.Actors.Get(guardNpc.DurableId);

        // Verify dialogue commands: Wait here, Check terms / Pay wages, Give healing draught, Pay hazard bonus, Part ways here.
        IReadOnlyList<DaggerfallDialogueTopicOption> initialTopics = session.PartyHire.TopicsFor(guardNpc);
        Assert.Contains(initialTopics, t => t.Id == "party-wait" && t.Label == "Wait here");
        Assert.Contains(initialTopics, t => t.Id == "party-terms");
        Assert.Contains(initialTopics, t => t.Id == "dismiss-party" && t.Label == "Part ways here");

        // Place Road Guard 7m away from settled player position in Follow stance -> steps closer via Engine/Kit navigation!
        session.Update(new ProductUpdate(OuterUpdate(2), []));
        Vector3 playerPos = session.State.PlayerControl.Position!.Value.ToVector();
        guard.ApplyPose(new ActorPose(WorldPoint.From(playerPos + new Vector3(7.0f, 0f, 0f)), 0f));
        float distBeforeFollow = Vector3.Distance(guard.Position.ToVector(), session.State.PlayerControl.Position!.Value.ToVector());
        for (ulong step = 3; step <= 11; step++)
            session.Update(new ProductUpdate(OuterUpdate(step), []));
        float distAfterFollow = Vector3.Distance(guard.Position.ToVector(), session.State.PlayerControl.Position!.Value.ToVector());
        Assert.True(distAfterFollow < distBeforeFollow - 0.25f,
            $"Expected Road Guard in Follow stance to navigate toward player (before={distBeforeFollow:F2}m, after={distAfterFollow:F2}m).");

        // Switch to Wait stance via dialogue -> topics flip to "Follow me", HUD stance becomes "wait", guard holds position!
        DaggerfallPartyHireResult waitResult = session.PartyHire.Resolve(guardNpc, DaggerfallDialogueTopic.PartyWait);
        Assert.True(waitResult.Accepted, waitResult.Message);
        Assert.Contains(session.PartyHire.TopicsFor(guardNpc), t => t.Id == "party-follow" && t.Label == "Follow me");
        Assert.Equal("wait", Assert.Single(session.OverlandParties.PartyPortraits()).Stance);

        Vector3 livePlayerPos = session.State.PlayerControl.Position!.Value.ToVector();
        guard.ApplyPose(new ActorPose(WorldPoint.From(livePlayerPos + new Vector3(8.0f, 0f, 0f)), 0f));
        WorldPoint heldSpot = guard.Position;
        for (ulong step = 12; step <= 17; step++)
            session.Update(new ProductUpdate(OuterUpdate(step), []));
        Assert.Equal(heldSpot, guard.Position);

        // Accrue 1 unpaid day, then settle via Check terms / Pay wages!
        ulong gold = session.State.Currency.Read().Gold;
        if (gold > 0) Assert.True(session.State.Currency.TrySpendGold(gold, []));
        session.AdvanceElapsedTime(24 * 3600);
        Assert.True(session.PartyHire.TryGetContract(guardNpc.DurableId, out ActorPartyContractDiff unpaidContract));
        Assert.Equal(1, unpaidContract.UnpaidDays);

        session.State.Currency.ReceiveGold(100UL);
        DaggerfallPartyHireResult settled = session.PartyHire.Resolve(guardNpc, DaggerfallDialogueTopic.CheckPartyTerms);
        Assert.True(settled.Accepted, settled.Message);
        Assert.True(session.PartyHire.TryGetContract(guardNpc.DurableId, out ActorPartyContractDiff settledContract));
        Assert.Equal(0, settledContract.UnpaidDays);

        // Flip back to Follow stance.
        DaggerfallPartyHireResult followResult = session.PartyHire.Resolve(guardNpc, DaggerfallDialogueTopic.PartyFollow);
        Assert.True(followResult.Accepted, followResult.Message);
        Assert.Equal("follow", Assert.Single(session.OverlandParties.PartyPortraits()).Stance);
    }

    [Fact]
    public void Road_guard_heals_during_rest_and_camp_projects_hud_portraits_and_cleans_up_on_permanent_death()
    {
        using var fixture = new ExteriorCombatFixture();
        using DaggerfallSession session = fixture.Start();
        session.ApplyProductMode(ProductMode.Playing);
        DaggerfallNpc guardNpc = HireRoadGuardInTown(session);
        ActorState guard = session.State.Actors.Get(guardNpc.DurableId);

        // Wound the Road Guard to ~40% HP and verify HUD portrait projects HealthShare & Stance.
        Track health = guard.Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Health.Value));
        double maxHp = health.Maximum.Value;
        health.SetCurrent(maxHp * 0.4d, clamp: true);
        DaggerfallPartyPortrait woundedPortrait = Assert.Single(session.OverlandParties.PartyPortraits());
        Assert.NotNull(woundedPortrait.HealthShare);
        Assert.InRange(woundedPortrait.HealthShare!.Value, 0.35d, 0.45d);
        Assert.Equal("follow", woundedPortrait.Stance);

        session.PublishInitial();
        string hudJson = JsonSerializer.Serialize(fixture.Engine.Published());
        Assert.Contains("\"healthShare\"", hudJson, StringComparison.Ordinal);
        Assert.Contains("\"stance\":\"follow\"", hudJson, StringComparison.Ordinal);

        // 1. Timed scene/inn rest heals the staged companion naturally!
        DaggerfallRestResult restResult = session.ApplyRest(
            new(DaggerfallRestMode.Timed, 2), new(true), seconds => new(seconds, seconds));
        Assert.True(restResult.Accepted);
        Assert.True(health.Current > maxHp * 0.5d, $"Expected rest to heal staged Road Guard above 50% (got {health.Current}/{maxHp}).");

        // 2. Wound again, open expedition map (boards Road Guard WithParty), camp for 2 hours -> heals boarded ledger vitals!
        health.SetCurrent(maxHp * 0.3d, clamp: true);
        session.OpenExpedition();
        Assert.False(session.State.Actors.TryGet(guardNpc.DurableId, out _));
        double boardedShareBefore = Assert.Single(session.OverlandParties.PartyPortraits()).HealthShare!.Value;
        Assert.InRange(boardedShareBefore, 0.25d, 0.35d);

        ((IDaggerfallExpeditionSurroundings)session).RecoverCampHour(sheltered: true);
        ((IDaggerfallExpeditionSurroundings)session).RecoverCampHour(sheltered: true);
        double boardedShareAfter = Assert.Single(session.OverlandParties.PartyPortraits()).HealthShare!.Value;
        Assert.True(boardedShareAfter > boardedShareBefore + 0.2d,
            $"Expected expedition camp hours to heal boarded Road Guard (before={boardedShareBefore:F2}, after={boardedShareAfter:F2}).");

        // 3. Save while WithParty, reload, and enter 3D scene -> Road Guard stages with healed HP, combat template, and weapon!
        RulesetSavePayload savedExpedition = session.CaptureSave();
        using DaggerfallSession reloaded = fixture.Restore(savedExpedition, out _, out AppearanceFake reloadedAppearance);
        reloaded.ApplyProductMode(ProductMode.Playing);
        reloaded.EnterExpedition();
        Assert.True(reloaded.State.Actors.TryGet(guardNpc.DurableId, out ActorState? restoredGuard));
        Assert.True(restoredGuard!.Actor.Get<ActorHarmPolicy>().IsCombatant);
        Assert.Equal("player-ally", restoredGuard.Actor.Get<FactionComponent>().EffectiveTeam);
        Assert.NotEmpty(reloaded.State.ActorInventories.EquipmentFor(guardNpc.DurableId).Read().Assignments);
        Track restoredHealth = restoredGuard.Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Health.Value));
        Assert.True(restoredHealth.Current / restoredHealth.Maximum.Value > boardedShareBefore + 0.2d);

        // 4. Permanent death by hostile enemy in real combat: corpse/loot created, ActorLocationKind.Dead recorded, HUD portrait vanishes immediately & across reload!
        reloaded.Update(new ProductUpdate(OuterUpdate(2), []));
        Vector3 guardSpot = restoredGuard.Position.ToVector();
        Vector3 orcSpot = guardSpot + new Vector3(0.6f, 0f, 0f);
        long killerId = reloaded.SpawnActor("orc", new ActorPose(WorldPoint.From(orcSpot), ActorHeading.Yaw(guardSpot - orcSpot)), lairTruce: false);
        ActorState killer = reloaded.State.Actors.Get(killerId);
        Track killerHealth = killer.Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Health.Value));
        killerHealth.Maximum.BaseValue = 1000d;
        killerHealth.SetCurrent(1000d, clamp: true);
        killer.Stats.GetStat(StatId.Parse("agility")).BaseValue = 100;
        killer.Stats.GetStat(StatId.Parse("strength")).BaseValue = 100;
        reloaded.Touches(guardNpc.DurableId);
        int orcMobile = fixture.Definitions.RequireActor(new("orc")).MobileId!.Value;
        NormalizedActorSprite orcSprite = fixture.ExteriorProfile.MobileSprites[orcMobile];
        ulong[] orcMarkers = [.. orcSprite.AttackSequences
            .Select(seq => seq.SourceFrames.ToList().IndexOf(-1))
            .Where(idx => idx >= 0)
            .Select(idx => checked((ulong)idx + 1))
            .Distinct()];
        reloaded.EnemyBehavior.Overrides.SetTarget(killerId, guardNpc.DurableId);
        restoredHealth.SetCurrent(1d, clamp: true);
        for (ulong step = 3; step <= 180 && !restoredGuard.IsDefeated; step++)
        {
            if (orcMarkers.Length > 0)
                reloadedAppearance.AdvanceReceiptForAll = CrossedMarker(1, markerId: orcMarkers[(int)(step % (ulong)orcMarkers.Length)]);
            reloaded.Update(new ProductUpdate(OuterUpdate(step), []));
        }
        Assert.True(restoredGuard.IsDefeated,
            $"Hostile orc should slay the 1-HP Road Guard in live combat (orcState={reloaded.EnemyBehavior.LastEvidence[killerId].State}, guardHp={restoredHealth.Current}).");

        Assert.Empty(reloaded.OverlandParties.PartyPortraits());
        Assert.Empty(reloaded.OverlandParties.StagedPartyMemberIds);
        Assert.Equal(ActorLocationKind.Dead, reloaded.State.ActorLedger.Require(guardNpc.DurableId).Location?.Kind);
        Assert.False(reloaded.PartyHire.TryGetContract(guardNpc.DurableId, out _));
        Assert.True(reloaded.Corpses.ContainsKey(guardNpc.DurableId));

        RulesetSavePayload savedDead = reloaded.CaptureSave();
        using DaggerfallSession afterDeathReload = fixture.Restore(savedDead, out _, out _);
        Assert.Empty(afterDeathReload.OverlandParties.PartyPortraits());
        Assert.Equal(ActorLocationKind.Dead, afterDeathReload.State.ActorLedger.Require(guardNpc.DurableId).Location?.Kind);
    }

    [Fact]
    public void Road_guard_stages_into_live_caravan_raid_and_fights_raiders_alongside_player()
    {
        using var fixture = new ExteriorCombatFixture();
        using DaggerfallSession session = fixture.Start();
        session.ApplyProductMode(ProductMode.Playing);
        DaggerfallNpc guardNpc = HireRoadGuardInTown(session);

        // Open expedition map -> Road Guard boards WithParty cleanly.
        session.OpenExpedition();
        Assert.False(session.State.Actors.TryGet(guardNpc.DurableId, out _));
        (double px, double py) = session.Expedition.Position;

        // Clear any autonomous off-map bands dispatched during initial setup so our Caravan + Raider collision is isolated.
        foreach (DaggerfallPersistentOverlandBand existing in session.Expedition.PersistentBands.ToArray())
        {
            session.OverlandParties.RetireBandOffScreen(existing.BandId);
            _ = session.Expedition.RemovePersistentBand(existing.BandId);
        }

        // Dispatch a real Caravan + hostile Raider band colliding into a live Caravan Raid (#11080) beside the player!
        DaggerfallPersistentOverlandBand? caravan = null;
        for (long window = 701L; window < 720L; window++)
        {
            DaggerfallPersistentOverlandBand? candidateBand = session.OverlandTraffic.TryDispatchWindow(window, (px, py));
            if (candidateBand is null) continue;
            if (candidateBand.Kind == DaggerfallBandKind.Caravan)
            {
                caravan = candidateBand;
                break;
            }
            session.OverlandParties.RetireBandOffScreen(candidateBand.BandId);
            _ = session.Expedition.RemovePersistentBand(candidateBand.BandId);
        }
        Assert.NotNull(caravan);
        long caravanMemberId = Assert.Single(session.State.ActorLedger.EntriesOnOverlandBand(caravan!.BandId)).DurableId;

        session.State.Social.ChangeRegionalReputation(DaggerfallRegion, -25);
        DaggerfallPersistentOverlandBand? raiders = session.HostileOverlandBands.TryDispatchWindow(702L, (px, py));
        Assert.NotNull(raiders);
        HashSet<long> raiderIds = [.. session.State.ActorLedger.EntriesOnOverlandBand(raiders!.BandId).Select(e => e.DurableId)];
        Assert.NotEmpty(raiderIds);

        caravan.X = px + 0.10;
        caravan.Y = py;
        raiders.X = px + 0.14;
        raiders.Y = py;

        // Refresh player fatigue after 10h town setup so expedition Halt/Advance is never interrupted by collapse.
        Track playerFatigue = session.State.Actors.Player.Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Fatigue.Value));
        playerFatigue.SetCurrent(playerFatigue.Maximum.Value, clamp: true);

        session.Expedition.SetMode(DaggerfallExpeditionMode.Halt);
        session.Expedition.SetSpeed(1);
        _ = session.Expedition.Advance(0.25);
        Assert.True(session.Expedition.BandAhead is not null,
            $"Expected live Caravan Raid BandAhead (msg={session.Expedition.Message}, bands={session.Expedition.PersistentBands.Count}, clash={caravan.ActiveClash?.RivalBandId}).");
        Assert.True(session.Expedition.BandAhead!.IsClash);

        // Intervene in the Caravan Raid -> Road Guard stages alongside the player, targets a Raider, and never targets the merchant!
        session.EnterExpedition();
        Assert.True(session.State.Actors.TryGet(guardNpc.DurableId, out ActorState? stagedGuard));
        Assert.True(session.State.Actors.TryGet(caravanMemberId, out _));
        Assert.True(raiderIds.All(id => session.State.Actors.TryGet(id, out _)));

        session.Update(new ProductUpdate(OuterUpdate(2), []));
        Assert.True(session.EnemyBehavior.LastEvidence.TryGetValue(stagedGuard!.DurableId, out var guardEvidence));
        Assert.NotNull(guardEvidence.Target);
        Assert.Contains(guardEvidence.Target!.Value.DurableId, raiderIds);
        Assert.NotEqual(caravanMemberId, guardEvidence.Target.Value.DurableId);
    }

    [Fact]
    public void Unequipped_road_guard_does_not_regenerate_starting_gear_across_boarding_save_load_and_restaging()
    {
        using var fixture = new ExteriorCombatFixture();
        using DaggerfallSession session = fixture.Start();
        session.ApplyProductMode(ProductMode.Playing);
        DaggerfallNpc guardNpc = HireRoadGuardInTown(session);

        // Initial enlistment grants starting combat gear once and persists StartingEquipmentGranted.
        Assert.True(session.PartyHire.TryGetContract(guardNpc.DurableId, out ActorPartyContractDiff initialContract));
        Assert.True(initialContract.StartingEquipmentGranted);
        Assert.NotEmpty(session.State.ActorInventories.EquipmentFor(guardNpc.DurableId).Read().Assignments);

        // Open dialogue with the Road Guard and unequip every equipped item through the retainer equipment UI owner.
        DaggerfallActivationTarget guardTarget = session.Dialogue.NpcTargets()
            .First(t => checked((long)t.Identity.Value) == guardNpc.DurableId);
        Assert.True(session.Dialogue.ActivateNpc(new(DaggerfallActivationMode.Talk, guardTarget)).Applied);
        while (session.RetainerExchange.Read() is { } retainerView)
        {
            DaggerfallRetainerItemView? equipped = retainerView.RetainerItems.FirstOrDefault(i => i.CanUnequip);
            if (equipped is null)
                break;
            session.RetainerExchange.Unequip(retainerView.Revision, equipped.Key);
        }

        Assert.Empty(session.State.ActorInventories.EquipmentFor(guardNpc.DurableId).Read().Assignments);
        static ulong[] ReadUniqueDurableIds(DaggerfallSession s, long actorId)
        {
            WorldRpg.Kit.Inventory.MechanicsInventoryCoordinator inv = s.State.ActorInventories.InventoryFor(actorId);
            return [.. inv.Read().UniqueItems.Select(item => inv.Entities.IdentityOf(item.Entity).Value).Order()];
        }
        ulong[] expectedItemIds = ReadUniqueDurableIds(session, guardNpc.DurableId);
        Assert.NotEmpty(expectedItemIds);

        // 1. Board into expedition, re-enter scene -> intentionally empty equipment stays empty and no duplicate items are generated.
        session.OpenExpedition();
        session.EnterExpedition();
        Assert.Empty(session.State.ActorInventories.EquipmentFor(guardNpc.DurableId).Read().Assignments);
        Assert.Equal(expectedItemIds, ReadUniqueDurableIds(session, guardNpc.DurableId));

        // 2. Board again, save while WithParty, reload, and re-enter scene -> still empty assignments and exact same item identities/count.
        session.OpenExpedition();
        RulesetSavePayload savedBoarded = session.CaptureSave();
        using DaggerfallSession reloadedFromBoarded = fixture.Restore(savedBoarded, out _, out _);
        reloadedFromBoarded.ApplyProductMode(ProductMode.Playing);
        reloadedFromBoarded.EnterExpedition();
        Assert.True(reloadedFromBoarded.PartyHire.TryGetContract(guardNpc.DurableId, out ActorPartyContractDiff reloadedContract));
        Assert.True(reloadedContract.StartingEquipmentGranted);
        Assert.Empty(reloadedFromBoarded.State.ActorInventories.EquipmentFor(guardNpc.DurableId).Read().Assignments);
        Assert.Equal(expectedItemIds, ReadUniqueDurableIds(reloadedFromBoarded, guardNpc.DurableId));

        // 3. Save while staged, reload, board, and restage again -> still exact same inventory and empty equipment.
        RulesetSavePayload savedStaged = reloadedFromBoarded.CaptureSave();
        using DaggerfallSession reloadedFromStaged = fixture.Restore(savedStaged, out _, out _);
        reloadedFromStaged.ApplyProductMode(ProductMode.Playing);
        reloadedFromStaged.OpenExpedition();
        reloadedFromStaged.EnterExpedition();
        Assert.Empty(reloadedFromStaged.State.ActorInventories.EquipmentFor(guardNpc.DurableId).Read().Assignments);
        Assert.Equal(expectedItemIds, ReadUniqueDurableIds(reloadedFromStaged, guardNpc.DurableId));
    }
}
