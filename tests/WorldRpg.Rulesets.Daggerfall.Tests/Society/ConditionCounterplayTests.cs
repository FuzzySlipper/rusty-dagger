using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Encounters;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.Travel;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Political follow-up (#11081): dungeon/lair clearing, staged surge-band wipeouts, and wilderness clearing
/// weaken or strip active regional WorldConditions tokens, emit WordSpread news wavefronts reflected on
/// town notice boards, persist progress/events sparsely across save/load, and feed Track B (#11055).
/// </summary>
public sealed class ConditionCounterplayTests
{
    private const int DaggerfallRegion = 17;

    [Fact]
    public void Clearing_matching_surge_dungeon_decrements_severity_emits_word_spread_updates_notice_board_and_guards_against_double_clear()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteRecord dungeonSite = definitions.Locations.Records.Single(r => r.Id == CastleNecromoghan);
        DaggerfallSiteRecord localTown = definitions.Locations.Records.First(r =>
            r.Id.Region == dungeonSite.Id.Region && r.Exterior is not null && r.Kind == DaggerfallSiteKind.TownCity);
        DaggerfallSiteRecord distantTown = definitions.Locations.Records
            .Where(r => r.Exterior is not null && r.Kind == DaggerfallSiteKind.TownCity && r.Id != localTown.Id)
            .Where(r =>
            {
                double dx = r.MapPixelX - dungeonSite.MapPixelX;
                double dy = r.MapPixelY - dungeonSite.MapPixelY;
                double dist = Math.Sqrt(dx * dx + dy * dy);
                return dist >= 20d && dist <= 120d;
            })
            .OrderBy(r => r.Id.Region)
            .ThenBy(r => r.Id.Index)
            .First();

        DaggerfallSiteProfile dungeonProfile = Assembled(DaggerfallWorldProfileIds.Dungeon(dungeonSite.Id));
        DaggerfallSiteProfiles profiles = new([dungeonProfile]);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, dungeonProfile);
        SpatialFake spatial = SpatialFake.Create(dungeonProfile.Geometry.Spatial[0].Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        DaggerfallSessionComposition composition = new(definitions, dungeonProfile, DaggerfallTuning.Defaults) { Profiles = profiles };

        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, composition);
        session.ApplyProductMode(ProductMode.Playing);

        // 1. Untouched session persists empty ConditionCounterplay state.
        DaggerfallSavePayload untouched = DaggerfallSavePayload.Read(session.CaptureSave());
        Assert.Empty(untouched.ConditionCounterplay.ClearedSources);
        Assert.Empty(untouched.ConditionCounterplay.Progress);
        Assert.Empty(untouched.ConditionCounterplay.PendingBoardEvents);
        Assert.Null(untouched.WorldConditions.ExternalReductions);

        // Pick a subject tag and matching published mobile definition that matches this dungeon's classic DungeonType.
        string dungeonFaction = DaggerfallConditionCounterplay.DungeonTypeFactions(dungeonSite.DungeonType)[0];
        string subjectTag = dungeonFaction == "criminals" ? "bandits" : dungeonFaction;
        DaggerfallActorDefinition matchingDef = definitions.Actors.Values
            .Where(a => a.MobileId is int mobId
                && definitions.Mobiles.Mobiles.TryGetValue(mobId, out DaggerfallMobileDefinition? m)
                && m.IsPublished
                && string.Equals(DaggerfallExpeditionBands.ResolveMobileCreatureFaction(m), dungeonFaction, StringComparison.Ordinal))
            .OrderBy(a => a.Level ?? int.MaxValue)
            .ThenBy(a => a.Id.Value, StringComparer.Ordinal)
            .First();

        // Remove any pre-populated living foes of that faction in this dungeon so our 2 spawned foes define exact clear state.
        foreach (ActorState existing in session.State.Actors.All.ToArray())
        {
            if (existing.DurableId == DaggerfallActorIdentity.PlayerEntityId || existing.IsDefeated) continue;
            session.RetireActor(existing.DurableId);
        }

        string tokenId = $"cond:monster-surge:r{dungeonSite.Id.Region}:lair-test";
        session.WorldConditions.Set(new DaggerfallWorldConditionToken(
            tokenId, DaggerfallWorldConditionKind.MonsterSurge, dungeonSite.Id.Region,
            SubjectTag: subjectTag, Severity: 2, StartedDay: 0));

        List<DaggerfallConditionCounterplayRecord> callbacks = [];
        session.ConditionCounterplay.Countered += callbacks.Add;

        WorldPoint playerPos = session.State.PlayerControl.Position ?? default;
        long foe1 = session.SpawnActor(matchingDef.Id.Value, new ActorPose(new WorldPoint(playerPos.X + 1.2f, playerPos.Y, playerPos.Z), 0f));
        long foe2 = session.SpawnActor(matchingDef.Id.Value, new ActorPose(new WorldPoint(playerPos.X - 1.2f, playerPos.Y, playerPos.Z), 0f));
        ulong step = 1_000;

        // Kill foe 1: progress recorded, but foe 2 is still alive so the surge is NOT weakened yet.
        KillAsPlayer(session, foe1, ref step);
        Assert.Empty(callbacks);
        Assert.Equal(2, session.WorldConditions.MaxSeverity(dungeonSite.Id.Region, DaggerfallWorldConditionKind.MonsterSurge));

        // Save/restore mid-dungeon: partial dungeon kill progress survives cleanly.
        RulesetSavePayload midSave = session.CaptureSave();
        DaggerfallSavePayload decodedMid = DaggerfallSavePayload.Read(midSave);
        Assert.Single(decodedMid.ConditionCounterplay.Progress);

        // Kill foe 2 (the final living matching foe in the dungeon): triggers LairCleared!
        KillAsPlayer(session, foe2, ref step);

        DaggerfallConditionCounterplayRecord record = Assert.Single(callbacks);
        Assert.Equal(DaggerfallConditionCounterplayKind.LairCleared, record.Kind);
        Assert.Equal(tokenId, record.TokenId);
        Assert.Equal(2, record.PreviousSeverity);
        Assert.Equal(1, record.NewSeverity);
        Assert.False(record.Stripped);
        Assert.NotNull(record.NewsEventId);
        Assert.Single(session.ConditionCounterplay.PendingBoardEvents);

        // Condition severity decremented from 2 -> 1 in WorldConditions.
        Assert.Equal(1, session.WorldConditions.MaxSeverity(dungeonSite.Id.Region, DaggerfallWorldConditionKind.MonsterSurge));

        // WordSpread news wavefront emitted: immediate at origin, in transit to distant town, then arrives after travel time.
        Assert.True(session.WordSpread.TryGet(record.NewsEventId!, out DaggerfallWordWavefrontSave? wf));
        Assert.NotNull(wf);
        long nowMinute = ((IDaggerfallExpeditionSurroundings)session).Calendar.MinuteNumber;
        int nowDay = checked((int)((IDaggerfallExpeditionSurroundings)session).Calendar.DayNumber);
        Assert.True(session.WordSpread.EvaluateAt(wf!, dungeonSite, nowMinute, nowDay).HasArrived);
        Assert.False(session.WordSpread.EvaluateAt(wf!, distantTown, nowMinute, nowDay).HasArrived);

        long laterMinute = nowMinute + 30L * DaggerfallCalendar.HoursPerDay * DaggerfallCalendar.MinutesPerHour;
        int laterDay = checked((int)(laterMinute / (DaggerfallCalendar.HoursPerDay * DaggerfallCalendar.MinutesPerHour)));
        Assert.True(session.WordSpread.EvaluateAt(wf!, distantTown, laterMinute, laterDay).HasArrived);
        DaggerfallTownNoticeBoardView distantBoard = session.NoticeBoard.BuildForSite(distantTown, laterMinute);
        Assert.Contains(distantBoard.Entries, e => e.Id == $"notice:word:{record.NewsEventId}");

        // Guard against double-clearing the same dungeon for the same token.
        long foe3 = session.SpawnActor(matchingDef.Id.Value, new ActorPose(new WorldPoint(playerPos.X + 1.2f, playerPos.Y, playerPos.Z), 0f));
        KillAsPlayer(session, foe3, ref step);
        Assert.Single(callbacks);
        Assert.Equal(1, session.WorldConditions.MaxSeverity(dungeonSite.Id.Region, DaggerfallWorldConditionKind.MonsterSurge));

        // Save and reload preserves ClearedSources and PendingBoardEvents.
        RulesetSavePayload afterClear = session.CaptureSave();
        List<string> restoreReleases = [];
        ContentFake restoreContent = new(restoreReleases);
        PopulateContent(restoreContent, dungeonProfile);
        SpatialFake restoreSpatial = SpatialFake.Create(dungeonProfile.Geometry.Spatial[0].Sha256, restoreReleases);
        EngineContextFake restoreEngine = EngineContextFake.Create(restoreContent, restoreSpatial.Service, new AppearanceFake(restoreReleases));
        using DaggerfallSession restored = DaggerfallSession.Restore(restoreEngine.Context, composition, afterClear);
        Assert.Equal(1, restored.WorldConditions.MaxSeverity(dungeonSite.Id.Region, DaggerfallWorldConditionKind.MonsterSurge));
        Assert.Single(restored.ConditionCounterplay.PendingBoardEvents);
        Assert.True(restored.WordSpread.TryGet(record.NewsEventId!, out _));
    }

    [Fact]
    public void Defeating_staged_surge_band_across_save_reload_strips_condition_and_updates_notice_board()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteRecord origin = definitions.Locations.Records.First(r =>
            r.Id.Region == DaggerfallRegion && r.Exterior is not null && r.Kind == DaggerfallSiteKind.TownCity);
        DaggerfallSiteProfile originProfile = Assembled(DaggerfallWorldProfileIds.Exterior(origin.Id));
        DaggerfallSiteProfiles profiles = new([originProfile]);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, originProfile);
        SpatialFake spatial = SpatialFake.Create(originProfile.Geometry.Spatial[0].Sha256, releases);
        spatial.FloorHit = WalkableFlatFloor;
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        DaggerfallSessionComposition composition = new(definitions, originProfile, DaggerfallTuning.Defaults) { Profiles = profiles };

        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, composition);
        session.ApplyProductMode(ProductMode.Playing);

        string tokenId = $"cond:monster-surge:r{DaggerfallRegion}:band-test";
        session.WorldConditions.Set(new DaggerfallWorldConditionToken(
            tokenId, DaggerfallWorldConditionKind.MonsterSurge, DaggerfallRegion,
            SubjectTag: "orcs", Severity: 1, StartedDay: 0));

        // Verify the bounty posting is initially live on the town notice board.
        long startMinute = ((IDaggerfallExpeditionSurroundings)session).Calendar.MinuteNumber;
        DaggerfallTownNoticeBoardView beforeBoard = session.NoticeBoard.BuildForSite(origin, startMinute);
        Assert.Contains(beforeBoard.Entries, e => e.ConditionTokenId == tokenId);

        // Queue a 2-member world-condition orc surge band directly through WorldEncounters (as Expedition.MeetBand does) and place both.
        int orcMobileId = definitions.Mobiles.Mobiles
            .Where(pair => pair.Value.IsPublished && pair.Value.DonorName.Contains("Orc", StringComparison.OrdinalIgnoreCase))
            .OrderBy(pair => pair.Key)
            .First().Key;
        DaggerfallEncounterRequest request = new(DaggerfallEncounterContext.WildernessDay, PlayerLevel: 5, Climate: 224);
        DaggerfallEncounterChoice choice = new(request.Context, null, null, orcMobileId, null, null, null, $"world-condition:{tokenId}");
        session.WorldEncounters.QueueBand(request, choice, new(77, DaggerfallEncounterStaging.Travelling, 0f), size: 2, originProfile.ProfileKey.LogicalId);
        session.Update(new ProductUpdate(OuterUpdate(1), []));

        List<ActorState> bandMembers = [.. session.State.Actors.All
            .Where(a => !a.IsDefeated
                && a.Actor.TryGet<DaggerfallEncounterBandMember>(out DaggerfallEncounterBandMember? m)
                && m.BandId == 77
                && StringComparer.Ordinal.Equals(m.ConditionTokenId, tokenId))];
        Assert.Equal(2, bandMembers.Count);

        // Defeat the first member, then save & reload mid-fight!
        ulong step = 1_000;
        KillAsPlayer(session, bandMembers[0].DurableId, ref step);
        Assert.True(session.WorldConditions.HasActive(DaggerfallRegion, DaggerfallWorldConditionKind.MonsterSurge));

        RulesetSavePayload midFight = session.CaptureSave();
        List<string> restoreReleases = [];
        ContentFake restoreContent = new(restoreReleases);
        PopulateContent(restoreContent, originProfile);
        SpatialFake restoreSpatial = SpatialFake.Create(originProfile.Geometry.Spatial[0].Sha256, restoreReleases);
        restoreSpatial.FloorHit = WalkableFlatFloor;
        EngineContextFake restoreEngine = EngineContextFake.Create(restoreContent, restoreSpatial.Service, new AppearanceFake(restoreReleases));
        using DaggerfallSession restored = DaggerfallSession.Restore(restoreEngine.Context, composition, midFight);
        restored.ApplyProductMode(ProductMode.Playing);

        // Remaining band member retains its ConditionTokenId across save/load.
        Assert.True(restored.State.Actors.TryGet(bandMembers[1].DurableId, out ActorState? survivor));
        Assert.True(survivor!.Actor.TryGet<DaggerfallEncounterBandMember>(out DaggerfallEncounterBandMember? restoredTag));
        Assert.Equal(tokenId, restoredTag!.ConditionTokenId);

        // Defeat the second (final) member after reload: triggers SurgeBandDefeated and STRIPS the Severity 1 token!
        KillAsPlayer(restored, survivor.DurableId, ref step);
        Assert.False(restored.WorldConditions.HasActive(DaggerfallRegion, DaggerfallWorldConditionKind.MonsterSurge));

        DaggerfallConditionCounterplayRecord record = Assert.Single(restored.ConditionCounterplay.PendingBoardEvents);
        Assert.Equal(DaggerfallConditionCounterplayKind.SurgeBandDefeated, record.Kind);
        Assert.Equal(tokenId, record.TokenId);
        Assert.True(record.Stripped);
        Assert.Equal(0, record.NewSeverity);

        // Notice board drops the old bounty and posts the cleared-threat bulletin!
        long afterMinute = ((IDaggerfallExpeditionSurroundings)restored).Calendar.MinuteNumber;
        DaggerfallTownNoticeBoardView afterBoard = restored.NoticeBoard.BuildForSite(origin, afterMinute);
        Assert.DoesNotContain(afterBoard.Entries, e => e.ConditionTokenId == tokenId);
        Assert.Contains(afterBoard.Entries, e => e.Id == $"notice:word:{record.NewsEventId}" && e.Heading.Contains("Broken", StringComparison.Ordinal));
    }

    [Fact]
    public void Wilderness_raider_clearing_weakens_external_board_condition_persists_reduction_and_consumes_on_board_turn()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteRecord origin = definitions.Locations.Records.First(r =>
            r.Id.Region == DaggerfallRegion && r.Exterior is not null && r.Kind == DaggerfallSiteKind.TownCity);
        DaggerfallSiteProfile originProfile = Assembled(DaggerfallWorldProfileIds.Exterior(origin.Id));
        DaggerfallSiteProfiles profiles = new([originProfile]);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, originProfile);
        SpatialFake spatial = SpatialFake.Create(originProfile.Geometry.Spatial[0].Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        DaggerfallSessionComposition composition = new(definitions, originProfile, DaggerfallTuning.Defaults) { Profiles = profiles };

        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, composition);
        session.ApplyProductMode(ProductMode.Playing);

        // Simulate Track B (#11055) contributing a Severity 2 GrainShortage condition via BindExternalSource.
        string boardTokenId = $"board:grain-shortage:r{DaggerfallRegion}";
        DaggerfallWorldConditionToken boardToken = new(
            boardTokenId, DaggerfallWorldConditionKind.GrainShortage, DaggerfallRegion, Severity: 2, StartedDay: 0);
        Func<int, IReadOnlyList<DaggerfallWorldConditionToken>> boardProvider = r => r == DaggerfallRegion ? [boardToken] : [];
        session.WorldConditions.BindExternalSource(boardProvider);
        Assert.Equal(2, session.WorldConditions.MaxSeverity(DaggerfallRegion, DaggerfallWorldConditionKind.GrainShortage));

        DaggerfallActorDefinition raiderDef = definitions.Actors.Values
            .Where(a => a.MobileId is int mobId
                && definitions.Mobiles.Mobiles.TryGetValue(mobId, out DaggerfallMobileDefinition? m)
                && m.IsPublished && m.Team == "Criminals")
            .OrderBy(a => a.Id.Value, StringComparer.Ordinal)
            .First();

        WorldPoint playerPos = session.State.PlayerControl.Position ?? default;
        ulong step = 1_000;
        for (int i = 0; i < DaggerfallConditionCounterplay.WildernessClearKillThreshold; i++)
        {
            long raiderId = session.SpawnActor(raiderDef.Id.Value, new ActorPose(new WorldPoint(playerPos.X + 1.2f, playerPos.Y, playerPos.Z), 0f));
            KillAsPlayer(session, raiderId, ref step);
        }

        // External board condition immediately weakened from 2 -> 1 in the world before Sunday's board turn!
        Assert.Equal(1, session.WorldConditions.MaxSeverity(DaggerfallRegion, DaggerfallWorldConditionKind.GrainShortage));
        DaggerfallConditionCounterplayRecord record = Assert.Single(session.ConditionCounterplay.PendingBoardEvents);
        Assert.Equal(DaggerfallConditionCounterplayKind.WildernessCleared, record.Kind);
        Assert.Equal(boardTokenId, record.TokenId);
        Assert.Equal(2, record.PreviousSeverity);
        Assert.Equal(1, record.NewSeverity);

        // Save/restore preserves BOTH ExternalReductions and PendingBoardEvents until Track B consumes them.
        RulesetSavePayload saved = session.CaptureSave();
        DaggerfallSavePayload decoded = DaggerfallSavePayload.Read(saved);
        DaggerfallConditionReductionSave reduction = Assert.Single(decoded.WorldConditions.ExternalReductions!);
        Assert.Equal(boardTokenId, reduction.TokenId);
        Assert.Equal(1, reduction.SeverityReduction);

        List<string> restoreReleases = [];
        ContentFake restoreContent = new(restoreReleases);
        PopulateContent(restoreContent, originProfile);
        SpatialFake restoreSpatial = SpatialFake.Create(originProfile.Geometry.Spatial[0].Sha256, restoreReleases);
        EngineContextFake restoreEngine = EngineContextFake.Create(restoreContent, restoreSpatial.Service, new AppearanceFake(restoreReleases));
        using DaggerfallSession restored = DaggerfallSession.Restore(restoreEngine.Context, composition, saved);
        restored.WorldConditions.BindExternalSource(boardProvider);
        Assert.Equal(1, restored.WorldConditions.MaxSeverity(DaggerfallRegion, DaggerfallWorldConditionKind.GrainShortage));

        // Simulate Sunday board turn consuming pending counter-play events!
        IReadOnlyList<DaggerfallConditionCounterplayRecord> consumed = restored.ConditionCounterplay.ConsumeBoardEvents();
        Assert.Single(consumed);
        Assert.Empty(restored.ConditionCounterplay.PendingBoardEvents);
        // External reduction cleared once consumed by the board turn (which updates its own token state).
        Assert.Equal(2, restored.WorldConditions.MaxSeverity(DaggerfallRegion, DaggerfallWorldConditionKind.GrainShortage));
    }

    private static void KillAsPlayer(DaggerfallSession session, long targetId, ref ulong step)
    {
        ActorState target = session.State.Actors.Get(targetId);
        WorldPoint playerPos = session.State.PlayerControl.Position ?? default;
        target.ApplyPose(new ActorPose(new WorldPoint(playerPos.X + 1.2f, playerPos.Y, playerPos.Z), 0f));
        target.Stats.GetTrack(TrackId.Parse("health")).SetCurrent(1d);
        for (int attempt = 0; attempt < 8 && !target.IsDefeated; attempt++)
        {
            step += 120;
            session.Strike(new(DaggerfallActorIdentity.PlayerEntityId, targetId, step, step, .125));
        }
        Assert.True(target.IsDefeated, $"Expected actor {targetId} to be defeated by player strike.");
    }

    private static SpatialHit WalkableFlatFloor(SpatialRaycastRequest request) =>
        request.Direction.Y < -0.5f
            ? new SpatialHit { Present = true, Point = new Vector3(request.Origin.X, 0f, request.Origin.Z), Normal = Vector3.UnitY, Distance = MathF.Max(0f, request.Origin.Y) }
            : default;
}
