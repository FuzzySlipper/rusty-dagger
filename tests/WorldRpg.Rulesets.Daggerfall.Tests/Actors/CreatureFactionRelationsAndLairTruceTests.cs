using System.Numerics;
using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Debugging;
using WorldRpg.Host;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Ai;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Behavior;
using WorldRpg.Rulesets.Daggerfall.Modules.Encounters;
using WorldRpg.Rulesets.Daggerfall.Travel;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Political layer A8 (#11053): creature-faction relations under LairTruce, downstream board-game surges,
/// live overland/staged faction clashes, save/load continuity, and developer clash/inspection commands.
/// </summary>
public sealed class CreatureFactionRelationsAndLairTruceTests
{
    private const int DaggerfallRegion = 17;

    [Fact]
    public void Creature_faction_relations_are_symmetric_conflict_free_and_map_board_conditions_to_factions()
    {
        DaggerfallCreatureFactionSet loaded = TestPayload.Definitions.CreatureFactions;
        DaggerfallCreatureFactionSet defaults = DaggerfallCreatureFactionSet.ClassicDefault;

        Assert.Equal(22, loaded.Factions.Count);
        Assert.Equal(22, defaults.Factions.Count);

        foreach (DaggerfallCreatureFactionSet set in new[] { loaded, defaults })
        {
            foreach (DaggerfallCreatureFactionDefinition faction in set.Factions.Values)
            {
                Assert.DoesNotContain(faction.Id, faction.Allies, StringComparer.Ordinal);
                Assert.DoesNotContain(faction.Id, faction.Enemies, StringComparer.Ordinal);
                foreach (string ally in faction.Allies)
                {
                    Assert.True(set.TryGet(ally, out DaggerfallCreatureFactionDefinition? other));
                    Assert.DoesNotContain(ally, faction.Enemies, StringComparer.Ordinal);
                    Assert.Contains(faction.Id, other!.Allies, StringComparer.Ordinal);
                    Assert.Equal(DaggerfallCreatureFactionRelation.Ally, set.Relation(faction.Id, ally));
                    Assert.False(set.AreTeamsHostile(faction.Id, ally, observerPacified: false));
                }
                foreach (string enemy in faction.Enemies)
                {
                    Assert.True(set.TryGet(enemy, out DaggerfallCreatureFactionDefinition? other));
                    Assert.DoesNotContain(enemy, faction.Allies, StringComparer.Ordinal);
                    Assert.Contains(faction.Id, other!.Enemies, StringComparer.Ordinal);
                    Assert.Equal(DaggerfallCreatureFactionRelation.Enemy, set.Relation(faction.Id, enemy));
                    Assert.True(set.AreNaturalEnemies(faction.Id, enemy));
                    Assert.True(set.AreTeamsHostile(faction.Id, enemy, observerPacified: false));
                }
            }
        }

        // Authored lore relations: allies, natural enemies, and neutrals.
        Assert.Equal(DaggerfallCreatureFactionRelation.Ally, loaded.Relation("spriggans", "nymphs"));
        Assert.Equal(DaggerfallCreatureFactionRelation.Ally, loaded.Relation("city-watch", "knights-and-mages"));
        Assert.Equal(DaggerfallCreatureFactionRelation.Ally, loaded.Relation("knights-and-mages", "magic"));
        Assert.Equal(DaggerfallCreatureFactionRelation.Enemy, loaded.Relation("orcs", "undead"));
        Assert.Equal(DaggerfallCreatureFactionRelation.Enemy, loaded.Relation("undead", "daedra"));
        Assert.Equal(DaggerfallCreatureFactionRelation.Enemy, loaded.Relation("city-watch", "criminals"));
        Assert.Equal(DaggerfallCreatureFactionRelation.Enemy, loaded.Relation("bears", "tigers"));
        Assert.Equal(DaggerfallCreatureFactionRelation.Neutral, loaded.Relation("vermin", "undead"));
        Assert.Equal(DaggerfallCreatureFactionRelation.Neutral, loaded.Relation("dragonlings", "bears"));

        // Downstream board-game condition -> creature faction mapping.
        DaggerfallWorldConditionToken orcSurge = new("cond:monster-surge:r17:orcs", DaggerfallWorldConditionKind.MonsterSurge, DaggerfallRegion, SubjectTag: "orcs", Severity: 2);
        DaggerfallWorldConditionToken undeadSurge = new("cond:monster-surge:r17:undead", DaggerfallWorldConditionKind.MonsterSurge, DaggerfallRegion, SubjectTag: "undead", Severity: 2);
        DaggerfallWorldConditionToken beastSurge = new("cond:monster-surge:r17:beasts", DaggerfallWorldConditionKind.MonsterSurge, DaggerfallRegion, SubjectTag: "beasts", Severity: 1);
        DaggerfallWorldConditionToken borderTension = new("cond:border-tension:r17", DaggerfallWorldConditionKind.BorderTension, DaggerfallRegion, Severity: 2);

        Assert.Equal(["orcs"], DaggerfallCreatureFactionPolicy.FactionsForCondition(orcSurge, loaded));
        Assert.Equal(["undead"], DaggerfallCreatureFactionPolicy.FactionsForCondition(undeadSurge, loaded));
        Assert.Contains("bears", DaggerfallCreatureFactionPolicy.FactionsForCondition(beastSurge, loaded));
        Assert.Contains("tigers", DaggerfallCreatureFactionPolicy.FactionsForCondition(beastSurge, loaded));
        Assert.True(DaggerfallCreatureFactionPolicy.IsConditionDrivingClash(loaded, [orcSurge], "orcs", "undead"));
        Assert.True(DaggerfallCreatureFactionPolicy.IsConditionDrivingClash(loaded, [borderTension], "knights-and-mages", "criminals"));
        Assert.False(DaggerfallCreatureFactionPolicy.IsConditionDrivingClash(loaded, [orcSurge], "spriggans", "nymphs"));
        Assert.False(DaggerfallCreatureFactionPolicy.IsConditionDrivingClash(loaded, [orcSurge], "vermin", "undead"));
        Assert.Contains("undead", DaggerfallCreatureFactionPolicy.RivalFactionsForCondition(loaded, orcSurge));
    }

    [Fact]
    public void LairTruce_holds_unalerted_dungeon_placements_until_luring_or_player_proximity_activates_them_and_survives_save_load()
    {
        using DungeonClashFixture fixture = new();
        DaggerfallSession session = fixture.Session;

        // Board-game MonsterSurge (orcs) is active in region 17.
        session.WorldConditions.Set(new DaggerfallWorldConditionToken(
            DaggerfallWorldConditions.RegionTokenId(DaggerfallWorldConditionKind.MonsterSurge, DaggerfallRegion, "orcs"),
            DaggerfallWorldConditionKind.MonsterSurge,
            DaggerfallRegion,
            SubjectTag: "orcs",
            Severity: 2,
            StartedDay: 0));

        // Put the player far from the distant dungeon room (35m away, outside LairTruceWakeRadius = 20m).
        Vector3 origin = session.State.PlayerControl.Position!.Value.ToVector();
        Vector3 roomCenter = origin + new Vector3(35f, 0f, 0f);
        Vector3 orcPos = roomCenter + new Vector3(-2f, 0f, 0f);
        Vector3 skelPos = roomCenter + new Vector3(2f, 0f, 0f);

        // Move any pre-existing Privateer's Hold dungeon actors far away so only our test room is evaluated.
        foreach (ActorState existing in session.State.Actors.All.Where(a => a.DurableId != DaggerfallActorIdentity.PlayerEntityId))
            existing.ApplyPose(new(WorldPoint.From(origin + new Vector3(0f, 0f, 400f)), 0f));

        // Spawn static dungeon placements under LairTruce facing each other 4m apart.
        long orcId = session.SpawnActor("orc", new ActorPose(WorldPoint.From(orcPos), ActorHeading.Yaw(skelPos - orcPos)), lairTruce: true);
        long skelId = session.SpawnActor("skeletal-warrior", new ActorPose(WorldPoint.From(skelPos), ActorHeading.Yaw(orcPos - skelPos)), lairTruce: true);

        Assert.True(session.State.Actors.Get(orcId).Actor.Get<DaggerfallEnemyPerceptionMemory>().LairTruce);
        Assert.True(session.State.Actors.Get(skelId).Actor.Get<DaggerfallEnemyPerceptionMemory>().LairTruce);

        // Step simulation while player stays 35m away behind dungeon walls (monsters in Room 1 see each other 4m apart,
        // while the distant player > 20m away is occluded by dungeon walls): LairTruce holds -> zero self-clearing!
        fixture.SeeEveryoneWithin(60d, playerWallOcclusionDistance: 20d);
        session.ApplyProductMode(ProductMode.Playing);
        for (ulong step = 2; step <= 6; step++)
            session.Update(new ProductUpdate(OuterUpdate(step), []));

        Assert.True(session.State.Actors.Get(orcId).Actor.Get<DaggerfallEnemyPerceptionMemory>().LairTruce);
        Assert.True(session.State.Actors.Get(skelId).Actor.Get<DaggerfallEnemyPerceptionMemory>().LairTruce);
        Assert.Equal(DaggerfallActorIdentity.PlayerEntityId, session.EnemyBehavior.LastEvidence[orcId].Target?.DurableId);
        Assert.Equal(DaggerfallActorIdentity.PlayerEntityId, session.EnemyBehavior.LastEvidence[skelId].Target?.DurableId);

        // Save and reload BEFORE activation: LairTruce must persist so dungeons never self-clear after loading a save!
        RulesetSavePayload savedUnderTruce = session.CaptureSave();
        using DaggerfallSession reloaded = fixture.Restore(savedUnderTruce);
        Assert.True(reloaded.State.Actors.Get(orcId).Actor.Get<DaggerfallEnemyPerceptionMemory>().LairTruce);
        Assert.True(reloaded.State.Actors.Get(skelId).Actor.Get<DaggerfallEnemyPerceptionMemory>().LairTruce);

        // Luring activation: provoking/luring the orc while player remains 35m away lifts LairTruce on the orc
        // and breaks LairTruce on the skeleton standing 4m in sight -> both immediately turn on each other!
        session.EnemyBehavior.Provoke(orcId, ((IDaggerfallExpeditionSurroundings)session).Calendar.MinuteNumber);
        session.Update(new ProductUpdate(OuterUpdate(7), []));

        Assert.False(session.State.Actors.Get(orcId).Actor.Get<DaggerfallEnemyPerceptionMemory>().LairTruce);
        Assert.False(session.State.Actors.Get(skelId).Actor.Get<DaggerfallEnemyPerceptionMemory>().LairTruce);
        Assert.Equal(skelId, session.EnemyBehavior.LastEvidence[orcId].Target?.DurableId);
        Assert.Equal(orcId, session.EnemyBehavior.LastEvidence[skelId].Target?.DurableId);

        // Proximity activation on a second dormant room: player walks within LairTruceWakeRadius (<= 20m) -> both wake and clash!
        Vector3 room2Center = origin + new Vector3(0f, 0f, 45f);
        Vector3 orc2Pos = room2Center + new Vector3(-2f, 0f, 0f);
        Vector3 skel2Pos = room2Center + new Vector3(2f, 0f, 0f);
        long orc2Id = session.SpawnActor("orc", new ActorPose(WorldPoint.From(orc2Pos), ActorHeading.Yaw(skel2Pos - orc2Pos)), lairTruce: true);
        long skel2Id = session.SpawnActor("skeletal-warrior", new ActorPose(WorldPoint.From(skel2Pos), ActorHeading.Yaw(orc2Pos - skel2Pos)), lairTruce: true);

        session.Update(new ProductUpdate(OuterUpdate(8), []));
        Assert.True(session.State.Actors.Get(orc2Id).Actor.Get<DaggerfallEnemyPerceptionMemory>().LairTruce);
        Assert.True(session.State.Actors.Get(skel2Id).Actor.Get<DaggerfallEnemyPerceptionMemory>().LairTruce);

        // Move player to 12m away from room 2 (still farther than the 4m between orc2 and skel2, but inside wake radius 20m).
        session.State.PlayerControl.MoveTo(room2Center + new Vector3(0f, 0f, -12f));
        session.Update(new ProductUpdate(OuterUpdate(9), []));
        Assert.False(session.State.Actors.Get(orc2Id).Actor.Get<DaggerfallEnemyPerceptionMemory>().LairTruce);
        Assert.False(session.State.Actors.Get(skel2Id).Actor.Get<DaggerfallEnemyPerceptionMemory>().LairTruce);
        Assert.Equal(skel2Id, session.EnemyBehavior.LastEvidence[orc2Id].Target?.DurableId);
        Assert.Equal(orc2Id, session.EnemyBehavior.LastEvidence[skel2Id].Target?.DurableId);

        // Allies/neutrals never infight even when woken in the same room under MonsterSurge!
        Vector3 room3Center = origin + new Vector3(-45f, 0f, 0f);
        Vector3 allyAPos = room3Center + new Vector3(-1.5f, 0f, 0f);
        Vector3 allyBPos = room3Center + new Vector3(1.5f, 0f, 0f);
        long sprigganId = session.SpawnActor("spriggan", new ActorPose(WorldPoint.From(allyAPos), ActorHeading.Yaw(allyBPos - allyAPos)), lairTruce: false, factionInfighting: true);
        long nymphId = session.SpawnActor("nymph", new ActorPose(WorldPoint.From(allyBPos), ActorHeading.Yaw(allyAPos - allyBPos)), lairTruce: false, factionInfighting: true);
        session.State.PlayerControl.MoveTo(room3Center + new Vector3(0f, 0f, -14f));
        session.Update(new ProductUpdate(OuterUpdate(10), []));
        Assert.Equal(DaggerfallActorIdentity.PlayerEntityId, session.EnemyBehavior.LastEvidence[sprigganId].Target?.DurableId);
        Assert.Equal(DaggerfallActorIdentity.PlayerEntityId, session.EnemyBehavior.LastEvidence[nymphId].Target?.DurableId);
    }

    [Fact]
    public void Overland_bands_and_staged_faction_clashes_fight_freely_downstream_of_board_conditions_and_survive_save_load()
    {
        using DungeonClashFixture fixture = new();
        DaggerfallSession session = fixture.Session;

        // Part A: Expedition map stages two-sided faction clash bands when opposing board-game conditions collide
        // or on severe (severity 3) MonsterSurge, without disturbing single severity-2 MonsterSurge bands.
        DaggerfallDefinitions defs = TestPayload.Definitions;
        DaggerfallTravelPolicy policy = session.Expedition.Policy;
        DaggerfallSiteRecord charing = defs.Locations.Records.First(r => r.Id.Region == DaggerfallRegion && r.Exterior is not null);
        (double X, double Y) party = (charing.MapPixelX + 0.5, charing.MapPixelY + 0.5);

        double chosenDistance = 3.0;
        double chosenBearing = 0.0;
        bool foundLand = false;
        for (double b = 0; b < 360 && !foundLand; b += 15)
        {
            double rad = b * Math.PI / 180;
            DaggerfallTravelMapPixel px = new((int)Math.Floor(party.X + Math.Sin(rad) * chosenDistance),
                (int)Math.Floor(party.Y - Math.Cos(rad) * chosenDistance));
            if (px.IsInBounds && !policy.IsOcean(px) && !defs.Locations.Records.Any(r => r.MapPixelX == px.X && r.MapPixelY == px.Y))
            {
                chosenBearing = b;
                foundLand = true;
            }
        }
        Assert.True(foundLand);

        DaggerfallExpeditionBands bands = new(policy, defs.Encounters, defs.Mobiles, DaggerfallBandTuning.Default,
            px => defs.Locations.Records.Any(r => r.MapPixelX == px.X && r.MapPixelY == px.Y),
            defs.CreatureFactions);
        long now = DaggerfallCalendar.Start.ToAbsoluteSeconds() + 12 * 3600;
        Func<string, int, int, int> lowRoll = (_, min, _) => min;

        DaggerfallWorldConditionToken orcSurge = new("cond:monster-surge:r17:orcs", DaggerfallWorldConditionKind.MonsterSurge, DaggerfallRegion, SubjectTag: "orcs", Severity: 2, StartedDay: 0);
        DaggerfallWorldConditionToken undeadSurge = new("cond:monster-surge:r17:undead", DaggerfallWorldConditionKind.MonsterSurge, DaggerfallRegion, SubjectTag: "undead", Severity: 2, StartedDay: 0);
        var (clashBand, clashErr) = bands.StageAt(now, party, playerLevel: 5, lowRoll, chosenDistance, chosenBearing, camp: false, [orcSurge, undeadSurge]);
        Assert.Null(clashErr);
        Assert.NotNull(clashBand);
        Assert.True(clashBand!.IsFactionClash);
        Assert.NotNull(clashBand.RivalChoice?.MobileId);
        Assert.True(clashBand.RivalSize >= 1);
        Assert.Contains("vs", clashBand.Name, StringComparison.Ordinal);
        string primaryFaction = DaggerfallExpeditionBands.ResolveMobileCreatureFaction(defs.Mobiles.Mobiles[clashBand.Choice.MobileId!.Value]);
        string rivalFaction = DaggerfallExpeditionBands.ResolveMobileCreatureFaction(defs.Mobiles.Mobiles[clashBand.RivalChoice!.MobileId!.Value]);
        Assert.True(defs.CreatureFactions.AreNaturalEnemies(primaryFaction, rivalFaction));

        // Severe (severity 3) Daedra surge also stages an active faction clash against a natural enemy.
        DaggerfallWorldConditionToken daedraSurge = new("cond:monster-surge:r17:daedra", DaggerfallWorldConditionKind.MonsterSurge, DaggerfallRegion, SubjectTag: "daedra", Severity: 3, StartedDay: 0);
        var (daedraClash, daedraErr) = bands.StageAt(now + 100, party, playerLevel: 10, lowRoll, chosenDistance, chosenBearing, camp: false, [daedraSurge]);
        Assert.Null(daedraErr);
        Assert.NotNull(daedraClash);
        Assert.True(daedraClash!.IsFactionClash);
        Assert.Equal("daedra", DaggerfallExpeditionBands.ResolveMobileCreatureFaction(defs.Mobiles.Mobiles[daedraClash.Choice.MobileId!.Value]));

        // Part B: Staged faction clashes via CreatureClashes & world.clash fight freely right away,
        // even beyond player wake proximity, refuse allied pairs, report in actor.inspect, and survive save/load!
        Vector3 origin = session.State.PlayerControl.Position!.Value.ToVector();
        foreach (ActorState existing in session.State.Actors.All.Where(a => a.DurableId != DaggerfallActorIdentity.PlayerEntityId))
            existing.ApplyPose(new(WorldPoint.From(origin + new Vector3(0f, 0f, 400f)), 0f));

        // Allied clash is cleanly refused.
        DaggerfallCreatureClashResult alliedRefusal = session.CreatureClashes.StageClash(
            WorldPoint.From(origin), 0f, [], explicitFactionA: "spriggans", explicitFactionB: "nymphs", countPerSide: 1, requireClearFloor: false);
        Assert.False(alliedRefusal.Staged);
        Assert.Contains("allied", alliedRefusal.Message, StringComparison.OrdinalIgnoreCase);

        // Board-game MonsterSurge (orcs) auto-plans a natural-enemy clash (orcs vs undead/enemy) with zero LairTruce!
        session.WorldConditions.Set(orcSurge);
        DaggerfallCreatureClashResult staged = session.CreatureClashes.StageClash(
            WorldPoint.From(origin), 0f, session.WorldConditions.ActiveForRegion(DaggerfallRegion, 0),
            countPerSide: 1, distanceMetres: 26f, requireClearFloor: false);
        Assert.True(staged.Staged, staged.Message);
        Assert.Equal("orcs", staged.FactionA);
        Assert.Equal(DaggerfallCreatureFactionRelation.Enemy, staged.Relation);
        Assert.Equal(orcSurge.TokenId, staged.DrivingConditionTokenId);
        long sideA = Assert.Single(staged.SideAActorIds!);
        long sideB = Assert.Single(staged.SideBActorIds!);

        DaggerfallEnemyPerceptionMemory memA = session.State.Actors.Get(sideA).Actor.Get<DaggerfallEnemyPerceptionMemory>();
        DaggerfallEnemyPerceptionMemory memB = session.State.Actors.Get(sideB).Actor.Get<DaggerfallEnemyPerceptionMemory>();
        Assert.False(memA.LairTruce);
        Assert.False(memB.LairTruce);
        Assert.True(memA.FactionInfighting);
        Assert.True(memB.FactionInfighting);

        // Even though the clash is 26m away (outside LairTruceWakeRadius = 20m), both sides target each other immediately!
        fixture.SeeEveryoneWithin(60d);
        session.ApplyProductMode(ProductMode.Playing);
        session.Update(new ProductUpdate(OuterUpdate(2), []));
        Assert.Equal(sideB, session.EnemyBehavior.LastEvidence[sideA].Target?.DurableId);
        Assert.Equal(sideA, session.EnemyBehavior.LastEvidence[sideB].Target?.DurableId);

        // actor.inspect reports both lairTruce and factionInfighting in disposition JSON!
        DaggerfallActorInspection inspection = Assert.Single(session.DebugCommands.OfType<DaggerfallActorInspection>());
        DebugCommandResult inspectResult = inspection.Inspect(sideA.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.True(inspectResult.Succeeded);
        using (JsonDocument doc = JsonDocument.Parse(inspectResult.Message))
        {
            JsonElement disp = doc.RootElement.GetProperty("disposition");
            Assert.False(disp.GetProperty("lairTruce").GetBoolean());
            Assert.True(disp.GetProperty("factionInfighting").GetBoolean());
        }

        // Save and reload preserves FactionInfighting = true and LairTruce = false!
        RulesetSavePayload savedClash = session.CaptureSave();
        using DaggerfallSession restoredClash = fixture.Restore(savedClash);
        Assert.False(restoredClash.State.Actors.Get(sideA).Actor.Get<DaggerfallEnemyPerceptionMemory>().LairTruce);
        Assert.True(restoredClash.State.Actors.Get(sideA).Actor.Get<DaggerfallEnemyPerceptionMemory>().FactionInfighting);
        Assert.False(restoredClash.State.Actors.Get(sideB).Actor.Get<DaggerfallEnemyPerceptionMemory>().LairTruce);
        Assert.True(restoredClash.State.Actors.Get(sideB).Actor.Get<DaggerfallEnemyPerceptionMemory>().FactionInfighting);

        // Developer world.clash command stages an explicit clash cleanly on clear floor!
        fixture.Spatial.FloorHit = req => req.Direction.Y < -0.99f
            ? default(SpatialHit) with { Present = true, Converged = true, Point = req.Origin - Vector3.UnitY, Normal = Vector3.UnitY }
            : default;
        IDeveloperWorldConditionCommands devCmds = Assert.Single(session.DebugCommands.OfType<IDeveloperWorldConditionCommands>());
        DebugCommandResult clashCmdResult = devCmds.StageClash("orcs", "undead", 1);
        Assert.True(clashCmdResult.Succeeded, clashCmdResult.Message);
        using (JsonDocument doc = JsonDocument.Parse(clashCmdResult.Message))
        {
            Assert.True(doc.RootElement.GetProperty("staged").GetBoolean());
            Assert.Equal("orcs", doc.RootElement.GetProperty("factionA").GetString());
            Assert.Equal("undead", doc.RootElement.GetProperty("factionB").GetString());
            Assert.Equal(1, doc.RootElement.GetProperty("sideAActorIds").GetArrayLength());
            Assert.Equal(1, doc.RootElement.GetProperty("sideBActorIds").GetArrayLength());
        }
    }

    private sealed class DungeonClashFixture : IDisposable
    {
        private readonly DaggerfallDefinitions _definitions;
        private readonly DaggerfallSiteProfile _inputs;
        private readonly ResolvedCompositionIdentity _identity;
        private readonly List<string> _releases = [];

        internal DaggerfallSession Session { get; }
        internal SpatialFake Spatial { get; }
        internal PerceptionFake Perception { get; }

        internal DungeonClashFixture()
        {
            string root = TestData.RepositoryRoot;
            _definitions = TestPayload.Definitions;
            _inputs = ReadInputs(root);
            _identity = Resolve(FullContent(root), new GameBundleId("daggerfall.classic")).RequireComposition().Identity;
            ContentFake content = new(_releases);
            PopulateContent(content, _inputs);
            Spatial = SpatialFake.Create(_inputs.Geometry.Spatial[0].Sha256, _releases);
            Spatial.KeepPosition = true;
            Perception = PerceptionFake.Create();
            EngineContextFake engine = EngineContextFake.Create(content, Spatial.Service, new AppearanceFake(_releases), Perception.Service);
            Session = DaggerfallSession.StartNew(engine.Context, new(_definitions, _inputs, DaggerfallTuning.Defaults));
        }

        internal void SeeEveryoneWithin(double maxDistance, double? playerWallOcclusionDistance = null) => Perception.Responder = request => Receipt(
            [.. request.Observers.ToArray().SelectMany(observer => request.Targets.ToArray().Select(target =>
            {
                Vector3 toward = target.Center - observer.Origin;
                double dist = toward.Length();
                double facing = dist > 0 ? Vector3.Dot(Vector3.Normalize(observer.Forward), toward / (float)dist) : 1d;
                bool playerOccludedByWall = playerWallOcclusionDistance is double wallDist
                    && target.Entity == (ulong)DaggerfallActorIdentity.PlayerEntityId
                    && dist > wallDist;
                PerceptionPairKind kind = playerOccludedByWall || dist > maxDistance || dist > observer.MaximumDistance
                    ? PerceptionPairKind.Occluded
                    : facing < observer.MinimumFacingCosine ? PerceptionPairKind.FacingRejected : PerceptionPairKind.Visible;
                return new PerceptionPair(observer.Entity, target.Entity, dist, facing, kind, kind == PerceptionPairKind.Visible ? 1d : 0d);
            }))]);

        internal DaggerfallSession Restore(RulesetSavePayload saved)
        {
            ContentFake content = new(_releases);
            PopulateContent(content, _inputs);
            SpatialFake spatial = SpatialFake.Create(_inputs.Geometry.Spatial[0].Sha256, _releases);
            EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(_releases));
            return DaggerfallSession.Restore(engine.Context, new(_definitions, _inputs, DaggerfallTuning.Defaults, _identity), saved);
        }

        public void Dispose() => Session.Dispose();
    }
}
