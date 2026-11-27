using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Host;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Behavior;
using WorldRpg.Rulesets.Daggerfall.Modules.Transport;
using WorldRpg.Rulesets.Daggerfall.Travel;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Den #11109 (Retainers 3): Option C dungeon-delving split (<c>canDelveDungeons</c> from <c>party-hire.json</c>),
/// Road Guard &amp; Wagon Driver outdoor perimeter hold with automatic re-staging on dungeon exit, rank/vouch-gated
/// Knightly Retainer &amp; Guild Battlemage dungeon delving (capped at 2), Engine-sanctioned dungeon follow, door
/// opening, 1-tile corridor yield, brick teleporter escort, elevator/blocked catch-up, dungeon combat with
/// <c>LairTruce</c> respect, permanent death &amp; dismissal underground, and full save/load round-trips.
/// </summary>
public sealed class DungeonRetainersTests
{
    private sealed class DungeonRetainersFixture : IDisposable
    {
        private readonly List<string> _releases = [];
        internal DaggerfallDefinitions Definitions { get; }
        internal DaggerfallSiteProfile ExteriorProfile { get; }
        internal DaggerfallSiteProfile KnightlyProfile { get; }
        internal DaggerfallSiteProfile MagesProfile { get; }
        internal DaggerfallSiteProfile FightersProfile { get; }
        internal DaggerfallSiteProfile DungeonProfile { get; }
        internal DaggerfallSiteProfiles Profiles { get; }
        internal ResolvedCompositionIdentity Identity { get; }

        internal SpatialFake Spatial { get; private set; } = null!;
        internal PerceptionFake Perception { get; private set; } = null!;
        internal AppearanceFake Appearance { get; private set; } = null!;

        private static PerceptionFake CreateAllVisiblePerception(double maxDistance = 60d, double? playerWallOcclusionDistance = 20d)
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

        internal static void WireStraightLineNavigation(SpatialFake spatial) => spatial.NavigationResult = request =>
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

        internal DungeonRetainersFixture()
        {
            string root = TestData.RepositoryRoot;
            Definitions = TestPayload.Definitions;
            using var guildFixture = SourceBackedGuildBankSessionFixture.Create();
            ExteriorProfile = guildFixture.ExteriorProfile;
            KnightlyProfile = guildFixture.KnightlyProfile;
            MagesProfile = guildFixture.MagesProfile;
            (DaggerfallSiteId fightersSiteId, DaggerfallSiteBuildingId fightersBuildingId) = Definitions.Locations.Records
                .Where(r => r.Exterior is not null)
                .SelectMany(r => r.Exterior!.Buildings.Select(b => (r.Id, BuildingId: b.Key, b.Value.Source)))
                .Where(x => x.Source.BuildingType == 11 && x.Source.FactionId == 41)
                .Select(x => (x.Id, x.BuildingId))
                .First();
            FightersProfile = Assembled(DaggerfallWorldProfileIds.Interior(fightersSiteId, fightersBuildingId));
            DungeonProfile = ReadInputs(root); // Privateer's Hold RDB dungeon
            Profiles = new([ExteriorProfile, KnightlyProfile, MagesProfile, FightersProfile, DungeonProfile]);
            Identity = Resolve(FullContent(root), new GameBundleId("daggerfall.classic")).RequireComposition().Identity;
        }

        private EngineContextFake CreateEngine(DaggerfallSiteProfile startProfile, out SpatialFake spatial, out PerceptionFake perception, out AppearanceFake appearance)
        {
            ContentFake content = new(_releases);
            PopulateContent(content, ExteriorProfile);
            PopulateContent(content, KnightlyProfile);
            PopulateContent(content, MagesProfile);
            PopulateContent(content, FightersProfile);
            PopulateContent(content, DungeonProfile);
            spatial = SpatialFake.Create(startProfile.Geometry.Spatial[0].Sha256, _releases);
            spatial.KeepPosition = true;
            WireStraightLineNavigation(spatial);
            perception = CreateAllVisiblePerception();
            appearance = new AppearanceFake(_releases);
            return EngineContextFake.Create(content, spatial.Service, appearance, perception.Service);
        }

        internal DaggerfallSession Start(DaggerfallSiteProfile startProfile)
        {
            EngineContextFake engine = CreateEngine(startProfile, out SpatialFake spatial, out PerceptionFake perception, out AppearanceFake appearance);
            Spatial = spatial;
            Perception = perception;
            Appearance = appearance;
            DaggerfallSessionComposition composition = new(Definitions, startProfile, DaggerfallTuning.Defaults, Identity)
            {
                Profiles = Profiles,
            };
            DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, composition);
            session.ApplyProductMode(ProductMode.Playing);
            return session;
        }

        internal DaggerfallSession Restore(RulesetSavePayload saved, DaggerfallSiteProfile activeProfileHint,
            out SpatialFake restoredSpatial, out AppearanceFake restoredAppearance)
        {
            EngineContextFake engine = CreateEngine(activeProfileHint, out restoredSpatial, out _, out restoredAppearance);
            DaggerfallSessionComposition composition = new(Definitions, ExteriorProfile, DaggerfallTuning.Defaults, Identity)
            {
                Profiles = Profiles,
            };
            DaggerfallSession restored = DaggerfallSession.Restore(engine.Context, composition, saved);
            restored.ApplyProductMode(ProductMode.Playing);
            return restored;
        }

        public void Dispose() { }
    }

    private static void GrantCart(DaggerfallSession session)
    {
        DurableIdentityReference cartIdentity = new(DurableIdentityKind.Item, 900_001);
        DaggerfallItemDefinition cartDef = TestPayload.Definitions.RequireItem(new DaggerfallItemId(DaggerfallTransportPolicy.CartItemId));
        session.State.Inventory.GrantAtomic([new InventoryAtomicGrant(new InventoryItemId(DaggerfallTransportPolicy.CartItemId), UniqueItem: cartIdentity)]);
        session.State.ItemInstances.RegisterUnique(cartIdentity.Value, DaggerfallItemInstanceMetadata.Default(cartDef, DaggerfallItemOwner.Player));
    }

    private static DaggerfallNpc EnlistKnightRetainer(DaggerfallSession session, DaggerfallSiteProfile knightlyProfile)
    {
        if (session.Sites.ActiveProfile != knightlyProfile.ProfileKey)
            Assert.True(session.TryTransitionTo(knightlyProfile.ProfileKey));
        session.State.Currency.ReceiveGold(500UL);
        if (session.State.Social.ReadAffiliations().All(a => a.FactionId != 368))
        {
            session.State.Social.JoinGuild(368, 0);
            session.State.Social.PromoteGuild(368, 0);
        }
        DaggerfallNpc recruit = Assert.Single(session.State.Npcs.All, npc =>
            npc.Presence == DaggerfallNpcPresence.Active
            && string.Equals(npc.Role, "knight-retainer", StringComparison.Ordinal));
        long referrerId = session.State.Npcs.All.First(n => n.DurableId != recruit.DurableId).DurableId;
        session.Introductions.GrantVouchDirect(referrerId, recruit.DurableId, ActorVouchKind.Trade);
        DaggerfallPartyHireResult hired = session.PartyHire.Resolve(recruit, DaggerfallDialogueTopic.HireCompanion);
        Assert.True(hired.Accepted, hired.Message);
        Assert.Contains("delve dungeons", hired.Message, StringComparison.OrdinalIgnoreCase);
        return recruit;
    }

    private static DaggerfallNpc EnlistGuildBattlemage(DaggerfallSession session, DaggerfallSiteProfile magesProfile)
    {
        if (session.Sites.ActiveProfile != magesProfile.ProfileKey)
            Assert.True(session.TryTransitionTo(magesProfile.ProfileKey));
        session.State.Currency.ReceiveGold(500UL);
        if (session.State.Social.ReadAffiliations().All(a => a.FactionId != 40))
        {
            session.State.Social.JoinGuild(40, 0);
            session.State.Social.PromoteGuild(40, 0);
        }
        DaggerfallNpc recruit = Assert.Single(session.State.Npcs.All, npc =>
            npc.Presence == DaggerfallNpcPresence.Active
            && string.Equals(npc.Role, "battle-mage", StringComparison.Ordinal));
        long referrerId = session.State.Npcs.All.First(n => n.DurableId != recruit.DurableId).DurableId;
        session.Introductions.GrantVouchDirect(referrerId, recruit.DurableId, ActorVouchKind.Trade);
        DaggerfallPartyHireResult hired = session.PartyHire.Resolve(recruit, DaggerfallDialogueTopic.HireCompanion);
        Assert.True(hired.Accepted, hired.Message);
        return recruit;
    }

    [Fact]
    public void Dungeon_entry_holds_road_guards_and_driver_outside_stages_delving_retainers_and_restages_all_on_exit()
    {
        using var fixture = new DungeonRetainersFixture();
        using DaggerfallSession session = fixture.Start(fixture.ExteriorProfile);
        session.AdvanceElapsedTime(10 * 3600);
        session.State.Currency.ReceiveGold(800UL);
        GrantCart(session);

        // 1. Hire a Road Guard (CanDelveDungeons == false) and a Wagon Driver in town exterior.
        DaggerfallNpc[] candidates =
        [
            .. session.State.Npcs.All.Where(npc =>
                npc.Presence == DaggerfallNpcPresence.Active
                && session.PartyHire.IsHireable(npc)
                && session.State.Actors.TryGet(npc.DurableId, out ActorState? live)
                && !live.IsDefeated),
        ];
        Assert.True(candidates.Length >= 2, "Town exterior should provide at least two hireable civilians.");
        DaggerfallNpc roadGuard = candidates[0];
        DaggerfallNpc driver = candidates[1];

        DaggerfallPartyHireResult hiredGuard = session.PartyHire.Resolve(roadGuard, DaggerfallDialogueTopic.HireCompanion);
        Assert.True(hiredGuard.Accepted, hiredGuard.Message);
        Assert.Contains("outside dungeon", hiredGuard.Message, StringComparison.OrdinalIgnoreCase);
        DaggerfallPartyHireResult guardTerms = session.PartyHire.Resolve(roadGuard, DaggerfallDialogueTopic.CheckPartyTerms);
        Assert.Contains("holds watch outside dungeons", guardTerms.Message, StringComparison.OrdinalIgnoreCase);

        DaggerfallPartyHireResult hiredDriver = session.PartyHire.Resolve(driver, DaggerfallDialogueTopic.HireDriver);
        Assert.True(hiredDriver.Accepted, hiredDriver.Message);

        // 2. Visit Knightly Order hall to enlist a rank+vouch-gated Knightly Retainer (CanDelveDungeons == true), then return outside.
        DaggerfallNpc knight = EnlistKnightRetainer(session, fixture.KnightlyProfile);
        DaggerfallPartyHireResult knightTerms = session.PartyHire.Resolve(knight, DaggerfallDialogueTopic.CheckPartyTerms);
        Assert.Contains("delves dungeons", knightTerms.Message, StringComparison.OrdinalIgnoreCase);

        Assert.True(session.TryTransitionTo(fixture.ExteriorProfile.ProfileKey));
        Assert.True(session.State.Actors.TryGet(roadGuard.DurableId, out _));
        Assert.True(session.State.Actors.TryGet(driver.DurableId, out _));
        Assert.True(session.State.Actors.TryGet(knight.DurableId, out _));

        // 3. Enter real RDB Dungeon -> ONLY Knightly Retainer stages underground; Road Guard & Wagon Driver hold watch outside!
        Assert.True(session.TryTransitionTo(fixture.DungeonProfile.ProfileKey));
        Assert.True(session.State.Actors.TryGet(knight.DurableId, out ActorState? delvingKnight));
        Assert.True(delvingKnight!.Actor.Get<ActorHarmPolicy>().IsCombatant);
        Assert.Equal("player-ally", delvingKnight.Actor.Get<FactionComponent>().EffectiveTeam);
        Assert.NotEmpty(session.State.ActorInventories.EquipmentFor(knight.DurableId).Read().Assignments);

        Assert.False(session.State.Actors.TryGet(roadGuard.DurableId, out _));
        Assert.False(session.State.Actors.TryGet(driver.DurableId, out _));
        Assert.Equal(ActorLocationKind.WithParty, session.State.ActorLedger.Require(roadGuard.DurableId).Location?.Kind);
        Assert.Equal(ActorLocationKind.WithParty, session.State.ActorLedger.Require(driver.DurableId).Location?.Kind);
        Assert.Contains("hold watch outside the dungeon entrance", session.Presentation.LastOutcome, StringComparison.OrdinalIgnoreCase);

        // 4. Save & reload while delving underground -> exact split persists (Knight underground, Road Guard & Driver still held outside)!
        RulesetSavePayload savedInDungeon = session.CaptureSave();
        using DaggerfallSession reloadedInDungeon = fixture.Restore(savedInDungeon, fixture.DungeonProfile, out _, out _);
        Assert.Equal(fixture.DungeonProfile.ProfileKey, reloadedInDungeon.Sites.ActiveProfile);
        Assert.True(reloadedInDungeon.State.Actors.TryGet(knight.DurableId, out ActorState? restoredKnight));
        Assert.True(restoredKnight!.Actor.Get<ActorHarmPolicy>().IsCombatant);
        Assert.Equal("player-ally", restoredKnight.Actor.Get<FactionComponent>().EffectiveTeam);
        Assert.False(reloadedInDungeon.State.Actors.TryGet(roadGuard.DurableId, out _));
        Assert.False(reloadedInDungeon.State.Actors.TryGet(driver.DurableId, out _));
        Assert.Equal(ActorLocationKind.WithParty, reloadedInDungeon.State.ActorLedger.Require(roadGuard.DurableId).Location?.Kind);
        Assert.Equal(ActorLocationKind.WithParty, reloadedInDungeon.State.ActorLedger.Require(driver.DurableId).Location?.Kind);

        // 5. Exit Dungeon back to Exterior -> ALL THREE automatically re-stage alongside the player!
        Assert.True(reloadedInDungeon.TryTransitionTo(fixture.ExteriorProfile.ProfileKey));
        Assert.True(reloadedInDungeon.State.Actors.TryGet(knight.DurableId, out _));
        Assert.True(reloadedInDungeon.State.Actors.TryGet(roadGuard.DurableId, out _));
        Assert.True(reloadedInDungeon.State.Actors.TryGet(driver.DurableId, out _));
        Assert.Contains("rejoin you from perimeter watch", reloadedInDungeon.Presentation.LastOutcome, StringComparison.OrdinalIgnoreCase);

        // 6. Solo-delve regression check: dismiss the Knight, enter dungeon with ONLY Road Guard + Driver (0 staged underground),
        //    then exit back to Exterior -> Road Guard & Driver STILL automatically re-stage!
        DaggerfallNpc knightBackOutside = reloadedInDungeon.State.Npcs.Require(knight.DurableId);
        Assert.True(reloadedInDungeon.PartyHire.Resolve(knightBackOutside, DaggerfallDialogueTopic.DismissParty).Accepted);
        Assert.True(reloadedInDungeon.TryTransitionTo(fixture.DungeonProfile.ProfileKey));
        Assert.Empty(reloadedInDungeon.OverlandParties.StagedPartyMemberIds);
        Assert.False(reloadedInDungeon.State.Actors.TryGet(roadGuard.DurableId, out _));
        Assert.False(reloadedInDungeon.State.Actors.TryGet(driver.DurableId, out _));

        Assert.True(reloadedInDungeon.TryTransitionTo(fixture.ExteriorProfile.ProfileKey));
        Assert.True(reloadedInDungeon.State.Actors.TryGet(roadGuard.DurableId, out _));
        Assert.True(reloadedInDungeon.State.Actors.TryGet(driver.DurableId, out _));
    }

    [Fact]
    public void Delving_retainer_fights_in_dungeon_opens_doors_yields_in_corridors_and_catches_up_across_teleporters_and_elevators()
    {
        using var fixture = new DungeonRetainersFixture();
        using DaggerfallSession session = fixture.Start(fixture.KnightlyProfile);
        DaggerfallNpc knightNpc = EnlistKnightRetainer(session, fixture.KnightlyProfile);

        // Enter Privateer's Hold RDB dungeon with the Knightly Retainer.
        Assert.True(session.TryTransitionTo(fixture.DungeonProfile.ProfileKey));
        ActorState knight = session.State.Actors.Get(knightNpc.DurableId);
        // Pacify pre-existing authored Privateer's Hold monsters so our X-ray perception fake isolates our test actors.
        foreach (ActorState existing in session.State.Actors.All.Where(a => a.DurableId != knight.DurableId))
            session.EnemyBehavior.Pacify(existing.DurableId);
        Vector3 playerPos = session.State.PlayerControl.Position!.Value.ToVector();

        // 1. Dungeon combat & LairTruce: ignores dormant LairTruce enemy beyond sight/hearing wake radius; engages active dungeon foe and deals real melee damage!
        knight.ApplyPose(new ActorPose(WorldPoint.From(playerPos + new Vector3(28.5f, 0f, 0f)), 0f));
        long dormantSkeletonId = session.SpawnActor("orc", new ActorPose(WorldPoint.From(playerPos + new Vector3(30.5f, 0f, 0f)), 0f), lairTruce: true);
        Assert.True(session.PartyHire.Resolve(session.State.Npcs.Require(knightNpc.DurableId), DaggerfallDialogueTopic.PartyWait).Accepted);
        session.Update(new ProductUpdate(OuterUpdate(2), []));
        Assert.Null(session.EnemyBehavior.LastEvidence[knight.DurableId].Target);

        Vector3 fightKnightPos = playerPos + new Vector3(1.5f, 0f, 0f);
        Vector3 fightOrcPos = playerPos + new Vector3(2.3f, 0f, 0f);
        knight.ApplyPose(new ActorPose(WorldPoint.From(fightKnightPos), ActorHeading.Yaw(fightOrcPos - fightKnightPos)));
        knight.Stats.GetStat(StatId.Parse("agility")).BaseValue = 100;
        knight.Stats.GetStat(StatId.Parse("strength")).BaseValue = 100;
        long activeFoeId = session.SpawnActor("orc", new ActorPose(WorldPoint.From(fightOrcPos), ActorHeading.Yaw(fightKnightPos - fightOrcPos)), lairTruce: false);
        ActorState activeFoe = session.State.Actors.Get(activeFoeId);
        session.Touches(activeFoeId);
        Track foeHealth = activeFoe.Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Health.Value));
        double foeHpBefore = foeHealth.Current;

        ulong step = 3;
        bool targetedFoe = false;
        for (; step <= 180; step++)
        {
            session.Update(new ProductUpdate(OuterUpdate(step), []));
            if (session.EnemyBehavior.LastEvidence.TryGetValue(knight.DurableId, out var ev) && ev.Target?.DurableId == activeFoeId)
                targetedFoe = true;
            if (foeHealth.Current < foeHpBefore)
                break;
        }
        Assert.True(targetedFoe, "Delving Knightly Retainer must target active dungeon enemies.");
        Assert.NotEqual(dormantSkeletonId, session.EnemyBehavior.LastEvidence[knight.DurableId].Target?.DurableId);
        Assert.True(foeHealth.Current < foeHpBefore, "Delving Knightly Retainer must deal real melee damage inside a dungeon.");

        // Pacify combat actors before testing idle follow/door/corridor/teleporter/elevator behaviors.
        session.EnemyBehavior.Pacify(activeFoeId);
        session.EnemyBehavior.Pacify(dormantSkeletonId);
        Assert.True(session.PartyHire.Resolve(session.State.Npcs.Require(knightNpc.DurableId), DaggerfallDialogueTopic.PartyFollow).Accepted);

        // 2. Closed RDB door opening during Follow when Engine navigation reports NoPath!
        DaggerfallDoorView closedDoor = session.Sites.Projection.Doors.All.First(d =>
            d.Kind == DaggerfallDoorKind.Normal && d.Motion == DaggerfallDoorMotion.Closed && !d.IsLocked);
        knight.ApplyPose(new ActorPose(WorldPoint.From(closedDoor.Pose.Translation + new Vector3(0.8f, 0f, 0f)), 0f));
        // Move player far enough (> 3m) from the door so Follow triggers navigation against the closed door.
        Vector3 beyondDoor = closedDoor.Pose.Translation + new Vector3(-5.0f, 0f, 0f);
        session.State.PlayerControl.Restore(WorldPoint.From(beyondDoor), default);
        fixture.Spatial.NavigationResult = request => new NavigationStepResult(
            ReadOnlyMemory<PlanarNavCell>.Empty,
            ReadOnlyMemory<NavigationPathEdge>.Empty,
            NavigationPathOutcome.NoPath,
            request.From,
            default, default, 0f, 1, 1, 1, 1, 0, false, default, default);
        session.Update(new ProductUpdate(OuterUpdate(++step), []));
        DaggerfallDoorView doorAfter = session.Sites.Projection.Doors.All.First(d => d.Id == closedDoor.Id);
        Assert.True(doorAfter.Motion is DaggerfallDoorMotion.Opening or DaggerfallDoorMotion.Open,
            $"Delving retainer blocked behind a closed RDB door must open it through DaggerfallDoors (got {doorAfter.Motion}).");

        // 3. 1-tile corridor anti-jamming: player stepping directly into retainer causes retainer to yield away via Engine nav!
        DungeonRetainersFixture.WireStraightLineNavigation(fixture.Spatial);
        Vector3 corridorPlayer = beyondDoor;
        knight.ApplyPose(new ActorPose(WorldPoint.From(corridorPlayer + new Vector3(0.4f, 0f, 0f)), 0f));
        float distBeforeYield = Vector3.Distance(knight.Position.ToVector(), corridorPlayer);
        for (int i = 0; i < 4; i++)
            session.Update(new ProductUpdate(OuterUpdate(++step), []));
        float distAfterYield = Vector3.Distance(knight.Position.ToVector(), corridorPlayer);
        Assert.True(distAfterYield > distBeforeYield + 0.15f,
            $"Expected retainer in narrow corridor to yield away from overlapping player (before={distBeforeYield:F2}m, after={distAfterYield:F2}m).");

        // 4. Dungeon brick teleporter (same-profile relocation): Follow retainer teleports beside player, Wait retainer stays put!
        Vector3 teleportDest = corridorPlayer + new Vector3(35.0f, 0f, 20.0f);
        DaggerfallSiteAnchor brickAnchor = new("teleport:test-brick", WorldPoint.From(teleportDest), 0f, 0f);
        Assert.True(((IDaggerfallTeleportSites)session).TryRelocatePlayer(fixture.DungeonProfile.ProfileKey, brickAnchor));
        float distAfterTeleport = Vector3.Distance(knight.Position.ToVector(), session.State.PlayerControl.Position!.Value.ToVector());
        Assert.True(distAfterTeleport <= 5.0f,
            $"Follow retainer must follow through same-dungeon brick teleporters (got {distAfterTeleport:F2}m away).");

        // Wait stance must NOT follow through brick teleporters.
        Assert.True(session.PartyHire.Resolve(session.State.Npcs.Require(knightNpc.DurableId), DaggerfallDialogueTopic.PartyWait).Accepted);
        WorldPoint waitPos = knight.Position;
        DaggerfallSiteAnchor secondAnchor = new("teleport:test-brick-2", WorldPoint.From(teleportDest + new Vector3(30.0f, 0f, 0f)), 0f, 0f);
        Assert.True(((IDaggerfallTeleportSites)session).TryRelocatePlayer(fixture.DungeonProfile.ProfileKey, secondAnchor));
        Assert.Equal(waitPos, knight.Position);

        // 5. Elevator / vertical shaft separation catch-up via Engine spatial ClearFloor (no downstream pathfinder)!
        Assert.True(session.PartyHire.Resolve(session.State.Npcs.Require(knightNpc.DurableId), DaggerfallDialogueTopic.PartyFollow).Accepted);
        Vector3 upperFloorPlayer = session.State.PlayerControl.Position!.Value.ToVector();
        knight.ApplyPose(new ActorPose(WorldPoint.From(upperFloorPlayer + new Vector3(0f, -10.0f, 0f)), 0f));
        fixture.Spatial.NavigationResult = request => new NavigationStepResult(
            ReadOnlyMemory<PlanarNavCell>.Empty,
            ReadOnlyMemory<NavigationPathEdge>.Empty,
            NavigationPathOutcome.NoPath,
            request.From,
            default, default, 0f, 1, 1, 1, 1, 0, false, default, default);
        fixture.Spatial.FloorHit = request => request.Direction.Y < -0.5f
            ? new SpatialHit
            {
                Present = true,
                Kind = SpatialHitKind.StaticMesh,
                Point = new Vector3(request.Origin.X, upperFloorPlayer.Y, request.Origin.Z),
                Normal = Vector3.UnitY,
                Distance = MathF.Max(0.1f, request.Origin.Y - upperFloorPlayer.Y),
            }
            : default;

        for (int i = 0; i < 12; i++)
            session.Update(new ProductUpdate(OuterUpdate(++step), []));
        float distAfterElevatorCatchUp = Vector3.Distance(knight.Position.ToVector(), upperFloorPlayer);
        Assert.True(distAfterElevatorCatchUp <= 5.0f,
            $"Retainer stranded across an elevator/vertical shaft must catch up onto an Engine-verified clear floor pose (got {distAfterElevatorCatchUp:F2}m).");
    }

    [Fact]
    public void Delving_retainer_cap_dismissal_and_permanent_death_underground_persist_across_save_and_load()
    {
        using var fixture = new DungeonRetainersFixture();
        using DaggerfallSession session = fixture.Start(fixture.KnightlyProfile);

        // Enlist 2 delving retainers: Knightly Retainer + Guild Battlemage (Option C max 2 delving retainers).
        DaggerfallNpc knight = EnlistKnightRetainer(session, fixture.KnightlyProfile);
        DaggerfallNpc battlemage = EnlistGuildBattlemage(session, fixture.MagesProfile);
        Assert.Equal(2, session.PartyHire.ActiveDungeonRetainerCount());

        // Attempting to enlist a 3rd delving retainer in Fighters Guild hall must be rejected by MaximumDungeonRetainers = 2!
        Assert.True(session.TryTransitionTo(fixture.FightersProfile.ProfileKey));
        session.State.Social.JoinGuild(41, 0);
        session.State.Social.PromoteGuild(41, 0);
        DaggerfallNpc thirdDelver = Assert.Single(session.State.Npcs.All, npc =>
            npc.Presence == DaggerfallNpcPresence.Active
            && session.PartyHire.IsHireable(npc)
            && string.Equals(npc.Role, "knight-retainer", StringComparison.Ordinal));
        long referrerId = session.State.Npcs.All.First(n => n.DurableId != thirdDelver.DurableId).DurableId;
        session.Introductions.GrantVouchDirect(referrerId, thirdDelver.DurableId, ActorVouchKind.Trade);
        DaggerfallPartyHireResult refusedThird = session.PartyHire.Resolve(thirdDelver, DaggerfallDialogueTopic.HireCompanion);
        Assert.False(refusedThird.Accepted);
        Assert.Contains("at most 2 dungeon-delving retainers", refusedThird.Message, StringComparison.OrdinalIgnoreCase);

        // Enter RDB dungeon -> BOTH delving retainers stage underground!
        Assert.True(session.TryTransitionTo(fixture.DungeonProfile.ProfileKey));
        Assert.True(session.State.Actors.TryGet(knight.DurableId, out ActorState? liveKnight));
        Assert.True(session.State.Actors.TryGet(battlemage.DurableId, out _));
        Assert.Equal(2, session.OverlandParties.PartyPortraits().Count);

        // 1. Dismiss Guild Battlemage underground -> stays cleanly at the dungeon site, contract cleared, portrait count drops to 1.
        DaggerfallNpc stagedMage = session.State.Npcs.Require(battlemage.DurableId);
        DaggerfallPartyHireResult dismissed = session.PartyHire.Resolve(stagedMage, DaggerfallDialogueTopic.DismissParty);
        Assert.True(dismissed.Accepted, dismissed.Message);
        Assert.False(session.PartyHire.TryGetContract(battlemage.DurableId, out _));
        Assert.Single(session.OverlandParties.PartyPortraits());
        Assert.Equal(ActorLocationKind.AtSite, session.State.ActorLedger.Require(battlemage.DurableId).Location?.Kind);

        // 2. Permanent death of Knightly Retainer underground in live combat -> corpse created, ActorLocationKind.Dead recorded, portraits empty!
        foreach (ActorState existing in session.State.Actors.All.Where(a => a.DurableId != knight.DurableId && a.DurableId != battlemage.DurableId))
            session.EnemyBehavior.Pacify(existing.DurableId);
        Assert.True(session.PartyHire.Resolve(session.State.Npcs.Require(knight.DurableId), DaggerfallDialogueTopic.PartyWait).Accepted);
        Vector3 knightPos = liveKnight!.Position.ToVector();
        Vector3 killerPos = knightPos + new Vector3(0.6f, 0f, 0f);
        long killerId = session.SpawnActor("orc", new ActorPose(WorldPoint.From(killerPos), ActorHeading.Yaw(knightPos - killerPos)), level: 12, lairTruce: false);
        ActorState killer = session.State.Actors.Get(killerId);
        Track killerHp = killer.Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Health.Value));
        killerHp.Maximum.BaseValue = 1000d;
        killerHp.SetCurrent(1000d, clamp: true);
        killer.Stats.GetStat(StatId.Parse("agility")).BaseValue = 100;
        killer.Stats.GetStat(StatId.Parse("strength")).BaseValue = 100;
        // Strip armor so full knight plate + shield absorption does not reduce a dungeon orc's blow to zero.
        MechanicsEquipmentCoordinator knightEquip = session.State.ActorInventories.EquipmentFor(knight.DurableId);
        foreach (WorldRpg.Kit.Inventory.EquipmentAssignment worn in knightEquip.Read().Assignments.ToArray())
            knightEquip.Unequip(worn.Item);
        liveKnight.Stats.GetStat(StatId.Parse("agility")).BaseValue = 1;
        liveKnight.Stats.GetStat(StatId.Parse("strength")).BaseValue = 1;
        session.Touches(knight.DurableId);
        // Hold actors in place in melee so the orc does not retreat out of its own melee reach before the swing marker lands,
        // and clear the synthetic 0.5m horizontal ray from Touches so ActorContact.Occluded does not block the swing.
        fixture.Spatial.FloorHit = _ => default;
        fixture.Spatial.NavigationResult = request => new NavigationStepResult(
            ReadOnlyMemory<PlanarNavCell>.Empty,
            ReadOnlyMemory<NavigationPathEdge>.Empty,
            NavigationPathOutcome.Reached,
            request.From,
            default, default, 0f, 1, 1, 1, 1, 0, false, default, default);
        int orcMobile = fixture.Definitions.RequireActor(new("orc")).MobileId!.Value;
        ulong[] killerMarkers = fixture.DungeonProfile.MobileSprites.TryGetValue(orcMobile, out NormalizedActorSprite? killerSprite)
            ? [.. killerSprite.AttackSequences.Select(seq => seq.SourceFrames.ToList().IndexOf(-1)).Where(idx => idx >= 0).Select(idx => checked((ulong)idx + 1)).Distinct()]
            : [];
        session.EnemyBehavior.Overrides.SetTarget(killerId, knight.DurableId);
        Track knightHp = liveKnight.Stats.GetTrack(TrackId.Parse(DaggerfallMechanicsIds.Health.Value));
        knightHp.SetCurrent(1d, clamp: true);
        for (ulong step = 2; step <= 180 && !liveKnight.IsDefeated; step++)
        {
            if (killerMarkers.Length > 0)
                fixture.Appearance.AdvanceReceiptForAll = CrossedMarker(1, markerId: killerMarkers[(int)((step / 5) % (ulong)killerMarkers.Length)], crossing: step);
            session.Update(new ProductUpdate(OuterUpdate(step), []));
        }
        Assert.True(liveKnight.IsDefeated,
            $"Dungeon enemy should slay the 1-HP Knightly Retainer in live combat (killerState={session.EnemyBehavior.LastEvidence[killerId].State}, knightHp={knightHp.Current}).");
        Assert.Empty(session.OverlandParties.PartyPortraits());
        Assert.Empty(session.OverlandParties.StagedPartyMemberIds);
        Assert.Equal(ActorLocationKind.Dead, session.State.ActorLedger.Require(knight.DurableId).Location?.Kind);
        Assert.False(session.PartyHire.TryGetContract(knight.DurableId, out _));
        Assert.True(session.Corpses.ContainsKey(knight.DurableId));

        // 3. Full save/load round-trip underground preserves both the dead Knightly Retainer and the dismissed Battlemage!
        RulesetSavePayload saved = session.CaptureSave();
        using DaggerfallSession reloaded = fixture.Restore(saved, fixture.DungeonProfile, out _, out _);
        Assert.Empty(reloaded.OverlandParties.PartyPortraits());
        Assert.Equal(ActorLocationKind.Dead, reloaded.State.ActorLedger.Require(knight.DurableId).Location?.Kind);
        Assert.False(reloaded.PartyHire.TryGetContract(battlemage.DurableId, out _));
        Assert.True(reloaded.State.Actors.TryGet(battlemage.DurableId, out _));
        Assert.Empty(reloaded.OverlandParties.StagedPartyMemberIds);
    }
}
