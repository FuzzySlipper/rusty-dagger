using System.Numerics;
using System.Text;
using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Inventory;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>Dungeon interaction: doors, action links, text modals, lockpicking, corpse activation, books and discovery.</summary>
public sealed class DungeonInteractionSessionTests
{
    [Fact]
    public void Dungeon_visibility_records_only_engine_seen_placement_and_restores_it()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        DaggerfallDungeonMapGeometry target = inputs.DungeonMap!.GeometryPlacements.Single(geometry => geometry.PlacementId == "model/b0000003-rdb/1/0/11");
        Vector3 point = target.SamplePoints[2]; // Unique within the admitted 0.5 m collision tolerance.
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        spatial.KeepPosition = true;
        spatial.FloorHit = _ => default(SpatialHit) with
        {
            Present = true,
            Kind = SpatialHitKind.StaticMesh,
            Point = point,
            Normal = Vector3.UnitY,
            Distance = 1d,
        };
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults));
        session.State.PlayerControl.Restore(new WorldPoint(point.X, point.Y + 1f, point.Z), default);

        session.Update(new ProductUpdate(OuterUpdate(12), []));

        DaggerfallDungeonDiscoverySnapshot discovered = Assert.Single(DaggerfallSavePayload.Read(session.CaptureSave()).DungeonDiscovery);
        Assert.Contains(target.PlacementId, discovered.DiscoveredPlacementIds);
        Assert.True(discovered.DiscoveredPlacementIds.Length < inputs.DungeonMap.GeometryPlacements.Count);

        DaggerfallDungeonMapGeometry overlapA = inputs.DungeonMap.GeometryPlacements.Single(geometry => geometry.PlacementId == "model/s0000999-rdb/0/0/108");
        DaggerfallDungeonMapGeometry overlapB = inputs.DungeonMap.GeometryPlacements.Single(geometry => geometry.PlacementId == "model/s0000999-rdb/0/0/115");
        point = (Vector3.Max(overlapA.BoundsMin, overlapB.BoundsMin)
            + Vector3.Min(overlapA.BoundsMax, overlapB.BoundsMax)) * .5f;
        session.Update(new ProductUpdate(OuterUpdate(24), []));
        DaggerfallDungeonDiscoverySnapshot afterAmbiguousHit = Assert.Single(DaggerfallSavePayload.Read(session.CaptureSave()).DungeonDiscovery);
        Assert.DoesNotContain(overlapA.PlacementId, afterAmbiguousHit.DiscoveredPlacementIds);
        Assert.DoesNotContain(overlapB.PlacementId, afterAmbiguousHit.DiscoveredPlacementIds);
        DaggerfallDungeonSurfaceCell ambiguousCell = DaggerfallDungeonSurfaceCell.At(point);
        Assert.Contains(ambiguousCell, afterAmbiguousHit.DiscoveredSurfaceCells);

        ContentFake resumedContent = new(releases);
        PopulateContent(resumedContent, inputs);
        SpatialFake resumedSpatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake resumedEngine = EngineContextFake.Create(resumedContent, resumedSpatial.Service, new AppearanceFake(releases));
        ResolvedCompositionIdentity identity = GameCompositionResolver.Resolve(FullContent(root), new GameBundleId("daggerfall.classic")).RequireComposition().Identity;
        using DaggerfallSession restored = DaggerfallSession.Restore(resumedEngine.Context, new(definitions, inputs, DaggerfallTuning.Defaults, identity), session.CaptureSave());
        DaggerfallDungeonDiscoverySnapshot restoredMap = Assert.Single(DaggerfallSavePayload.Read(restored.CaptureSave()).DungeonDiscovery);
        Assert.Equal(discovered.DiscoveredPlacementIds, restoredMap.DiscoveredPlacementIds);
        Assert.Contains(ambiguousCell, restoredMap.DiscoveredSurfaceCells);
    }

    [Theory]
    [InlineData("interact", "attack")]
    [InlineData("attack", "interact")]
    [InlineData("inventory", "menu")]
    [InlineData("interact", "inventory")]
    public void Two_intents_in_one_delivery_survive_the_door_contract(string first, string second)
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile source = ReadInputs(root);
        DaggerfallSiteProfile castle = DaggerfallSiteContent.Read(FullContent(root),
            File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.castle-necromoghan.json")), definitions);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, source);
        PopulateContent(content, castle);
        SpatialFake spatial = SpatialFake.Create(source.SpatialArtifact.Sha256, releases);
        spatial.KeepPosition = true;
        PerceptionFake perception = PerceptionFake.Create();
        perception.Responder = request => Receipt([.. request.Targets.Span.ToArray().Select(target =>
            new PerceptionPair(1, target.Entity, 1d, 1d, PerceptionPairKind.Visible, 1d))]);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), perception.Service);
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, source, DaggerfallTuning.Defaults));
        session.AdmitSiteProfiles(new DaggerfallSiteProfiles([source, castle]));
        Assert.True(session.TryTransitionTo(castle.ProfileKey));
        // The ray finds something that is not a loaded door, which is what an ordinary click in the open
        // dungeon does, and the delivery carries two intents the way one service batch can.
        ulong notADoor = session.Doors.All.Select(value => value.Entity.Value).DefaultIfEmpty(1UL).Max() + 1UL;
        spatial.FloorHit = request => request.Direction.Y < -.5f ? default : new SpatialHit
        {
            Present = true,
            Kind = SpatialHitKind.Entity,
            Entity = notADoor,
            Point = Vector3.Zero,
            Distance = 1f,
        };

        session.Update(new ProductUpdate(OuterUpdate(1),
        [
            Input(InputEventKind.DirectDigital, x: 1f, phase: InputPhase.DirectUi, intent: first),
            Input(InputEventKind.DirectDigital, x: 1f, phase: InputPhase.DirectUi, intent: second),
        ]));
    }

    [Theory]
    [InlineData("inventory")]
    [InlineData("character")]
    [InlineData("attack")]
    [InlineData("loot")]
    [InlineData("activation-mode")]
    [InlineData("save-slots")]
    [InlineData("save-game")]
    [InlineData("load-game")]
    public void Dom_action_in_ordinary_play_survives_the_door_contract(string action)
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile source = ReadInputs(root);
        DaggerfallSiteProfile castle = DaggerfallSiteContent.Read(FullContent(root),
            File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.castle-necromoghan.json")), definitions);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, source);
        PopulateContent(content, castle);
        SpatialFake spatial = SpatialFake.Create(source.SpatialArtifact.Sha256, releases);
        spatial.KeepPosition = true;
        PerceptionFake perception = PerceptionFake.Create();
        perception.Responder = request => Receipt([.. request.Targets.Span.ToArray().Select(target =>
            new PerceptionPair(1, target.Entity, 1d, 1d, PerceptionPairKind.Visible, 1d))]);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), perception.Service);
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, source, DaggerfallTuning.Defaults));
        session.AdmitSiteProfiles(new DaggerfallSiteProfiles([source, castle]));
        Assert.True(session.TryTransitionTo(castle.ProfileKey));

        // The UI's own controls arrive as typed actions rather than as named input intents, so they are
        // a separate route into the same session state and must survive the same door contract.
        session.Update(new ProductUpdate(OuterUpdate(1), [Input(InputEventKind.DirectDigital) with
        {
            ValueKind = InputValueKind.ProductPayload,
            PayloadContract = "dagger.ui.action.v1"u8.ToArray(),
            PayloadData = Encoding.UTF8.GetBytes($"{{\"action\":\"{action}\"}}"),
        }]));
    }

    [Theory]
    [InlineData("inventory")]
    [InlineData("character")]
    [InlineData("menu")]
    public void Panel_input_in_ordinary_play_survives_the_door_contract(string intent)
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile source = ReadInputs(root);
        DaggerfallSiteProfile castle = DaggerfallSiteContent.Read(FullContent(root),
            File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.castle-necromoghan.json")), definitions);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, source);
        PopulateContent(content, castle);
        SpatialFake spatial = SpatialFake.Create(source.SpatialArtifact.Sha256, releases);
        spatial.KeepPosition = true;
        PerceptionFake perception = PerceptionFake.Create();
        perception.Responder = request => Receipt([.. request.Targets.Span.ToArray().Select(target =>
            new PerceptionPair(1, target.Entity, 1d, 1d, PerceptionPairKind.Visible, 1d))]);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), perception.Service);
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, source, DaggerfallTuning.Defaults));
        session.AdmitSiteProfiles(new DaggerfallSiteProfiles([source, castle]));
        Assert.True(session.TryTransitionTo(castle.ProfileKey));

        // A panel request is ordinary play input: nothing in it may reach a door validator with an
        // identity the caller never resolved.
        session.Update(new ProductUpdate(OuterUpdate(1), [Input(InputEventKind.DirectDigital,
            x: 1f, phase: InputPhase.DirectUi, intent: intent)]));

        Assert.Equal(intent, session.LatestPanelRequest?.Panel);
    }

    [Fact]
    public void An_activation_ray_that_hits_no_door_leaves_the_update_alive_and_dispatches_no_door_action()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile source = ReadInputs(root);
        DaggerfallSiteProfile castle = DaggerfallSiteContent.Read(FullContent(root),
            File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.castle-necromoghan.json")), definitions);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, source);
        PopulateContent(content, castle);
        SpatialFake spatial = SpatialFake.Create(source.SpatialArtifact.Sha256, releases);
        spatial.KeepPosition = true;
        PerceptionFake perception = PerceptionFake.Create();
        perception.Responder = request => Receipt([.. request.Targets.Span.ToArray().Select(target =>
            new PerceptionPair(1, target.Entity, 1d, 1d, PerceptionPairKind.Visible, 1d))]);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), perception.Service);
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, source, DaggerfallTuning.Defaults));
        session.AdmitSiteProfiles(new DaggerfallSiteProfiles([source, castle]));
        Assert.True(session.TryTransitionTo(castle.ProfileKey));
        // The ray finds an entity that is none of the loaded doors. Asking the door graph about it must
        // not hand the graph a door that was never matched: a door view is a value type, so a miss is a
        // default view whose identity carries no RDB source key, and that identity ends the update.
        ulong notADoor = session.Doors.All.Select(value => value.Entity.Value).DefaultIfEmpty(1UL).Max() + 1UL;
        spatial.FloorHit = request => request.Direction.Y < -.5f ? default : new SpatialHit
        {
            Present = true,
            Kind = SpatialHitKind.Entity,
            Entity = notADoor,
            Point = Vector3.Zero,
            Distance = 1f,
        };

        session.Update(new ProductUpdate(OuterUpdate(1), [Input(InputEventKind.DirectDigital,
            x: 1f, phase: InputPhase.DirectUi, intent: "interact")]));

        Assert.False(session.ActivationView.Applied);
        Assert.All(session.State.DungeonActions[castle.ProfileKey].State.Values,
            state => Assert.Equal(0UL, state.ActivationCount));
    }

    [Fact]
    public void Contextual_action_door_opens_before_its_direct_link_without_reversing_in_the_same_input()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile source = ReadInputs(root);
        DaggerfallSiteProfile castle = DaggerfallSiteContent.Read(FullContent(root),
            File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.castle-necromoghan.json")), definitions);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, source);
        PopulateContent(content, castle);
        SpatialFake spatial = SpatialFake.Create(source.SpatialArtifact.Sha256, releases);
        spatial.KeepPosition = true;
        PerceptionFake perception = PerceptionFake.Create();
        perception.Responder = request => Receipt([.. request.Targets.Span.ToArray().Select(target =>
            new PerceptionPair(1, target.Entity, 1d, 1d, PerceptionPairKind.Visible, 1d))]);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), perception.Service);
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, source, DaggerfallTuning.Defaults));
        session.AdmitSiteProfiles(new DaggerfallSiteProfiles([source, castle]));
        Assert.True(session.TryTransitionTo(castle.ProfileKey));
        // Castle supplies a real action door and OpenDoor parameters; the direct-trigger variant
        // exercises the donor ordering path that its packed lock selector does not request.
        DaggerfallDungeonActionDefinition openAction = castle.DungeonActions
            .First(action => action.ActionFlag == (byte)DaggerfallDungeonActionFlag.OpenDoor)
            with { TriggerFlag = (uint)DaggerfallDungeonTriggerFlag.Direct };
        session.State.DungeonActions[castle.ProfileKey] = new DaggerfallDungeonActionGraph(
            castle.ProfileKey.LogicalId,
            castle.DungeonActions.Select(action => action.Id == openAction.Id ? openAction : action),
            session.State.Variables,
            executeFamilyAction: action => DaggerfallDungeonDoorActions.Execute(action, session.Doors));
        DaggerfallDoorView door = session.Doors.All.Single(value =>
            DaggerfallDungeonActionGraph.DoorSourceId(value.Id) == openAction.DoorId);
        Assert.Equal(DaggerfallDoorOperationResult.Started,
            session.Doors.Unlock(door.Id, DaggerfallDoorOperationSource.DungeonAction));
        session.State.PlayerControl.MoveTo(door.Pose.Translation + Vector3.UnitZ);
        session.State.PlayerControl.YawRadians = 0f;
        session.State.PlayerControl.PitchRadians = 0f;
        spatial.FloorHit = request => request.Direction.Y < -.5f ? default : new SpatialHit
        {
            Present = true,
            Kind = SpatialHitKind.Entity,
            Entity = door.Entity.Value,
            Point = door.Pose.Translation,
            Distance = 1f,
        };

        session.Update(new ProductUpdate(OuterUpdate(1), [Input(InputEventKind.DirectDigital,
            x: 1f, phase: InputPhase.DirectUi, intent: "interact")]));

        Assert.True(session.ActivationView.Applied, session.ActivationView.Message);
        Assert.Equal(DaggerfallDoorMotion.Opening, session.Doors.Read(door.Id).Motion);
        Assert.Equal(1UL, session.State.DungeonActions[castle.ProfileKey].State[openAction.Id].ActivationCount);
    }

    [Theory]
    [InlineData((int)DaggerfallDungeonTriggerFlag.Direct)]
    [InlineData((int)DaggerfallDungeonTriggerFlag.Door)]
    public void Door_text_first_click_blocks_the_real_door_and_second_click_opens_it(int trigger)
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile source = ReadInputs(root);
        DaggerfallSiteProfile castle = DaggerfallSiteContent.Read(FullContent(root),
            File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.castle-necromoghan.json")), definitions);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, source);
        PopulateContent(content, castle);
        SpatialFake spatial = SpatialFake.Create(source.SpatialArtifact.Sha256, releases);
        spatial.KeepPosition = true;
        PerceptionFake perception = PerceptionFake.Create();
        perception.Responder = request => Receipt([.. request.Targets.Span.ToArray().Select(target =>
            new PerceptionPair(1, target.Entity, 1d, 1d, PerceptionPairKind.Visible, 1d))]);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), perception.Service);
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, source, DaggerfallTuning.Defaults));
        session.AdmitSiteProfiles(new DaggerfallSiteProfiles([source, castle]));
        Assert.True(session.TryTransitionTo(castle.ProfileKey));

        DaggerfallDoorView door = session.Doors.All.First(value =>
            value.Kind == DaggerfallDoorKind.Normal && !value.IsLocked && value.Motion == DaggerfallDoorMotion.Closed);
        string doorId = DaggerfallDungeonActionGraph.DoorSourceId(door.Id);
        DaggerfallDungeonActionDefinition warning = new(
            "test/door-warning",
            SourceOffset: 100,
            TriggerFlag: (uint)trigger,
            ActionFlag: (byte)DaggerfallDungeonActionFlag.DoorText,
            Axis: 0,
            Duration: 0,
            Magnitude: 0,
            NextObjectOffset: 0,
            NextActionId: null,
            DoorId: doorId,
            SoundIndex: 1);
        session.State.DungeonActions[castle.ProfileKey] = new DaggerfallDungeonActionGraph(
            castle.ProfileKey.LogicalId,
            [warning],
            session.State.Variables,
            executeFamilyAction: session.ExecuteDungeonTextAction);

        session.State.PlayerControl.MoveTo(door.Pose.Translation + Vector3.UnitZ);
        session.State.PlayerControl.YawRadians = 0f;
        session.State.PlayerControl.PitchRadians = 0f;
        spatial.FloorHit = request => request.Direction.Y < -.5f ? default : new SpatialHit
        {
            Present = true,
            Kind = SpatialHitKind.Entity,
            Entity = door.Entity.Value,
            Point = door.Pose.Translation,
            Distance = 1f,
        };

        session.Update(new ProductUpdate(OuterUpdate(1), [Input(InputEventKind.DirectDigital,
            x: 1f, phase: InputPhase.DirectUi, intent: "interact")]));

        Assert.Equal(DaggerfallDungeonTextActionKind.DoorText, session.DungeonTextProjection?.Kind);
        Assert.Equal(DaggerfallDoorMotion.Closed, session.Doors.Read(door.Id).Motion);
        DaggerfallDungeonTextProjection first = Assert.IsType<DaggerfallDungeonTextProjection>(session.DungeonTextProjection);
        ProductInputEvent close = Input(InputEventKind.DirectDigital) with
        {
            ValueKind = InputValueKind.ProductPayload,
            PayloadContract = "dagger.ui.action.v1"u8.ToArray(),
            PayloadData = Encoding.UTF8.GetBytes($"{{\"action\":\"dungeon-text-close\",\"item\":\"{first.ActionId}\",\"revision\":\"{first.Revision}\"}}"),
        };
        session.Update(new ProductUpdate(OuterUpdate(2), [close]));
        Assert.Null(session.DungeonTextProjection);

        session.Update(new ProductUpdate(OuterUpdate(3), [Input(InputEventKind.DirectDigital,
            x: 1f, phase: InputPhase.DirectUi, intent: "interact")]));
        Assert.Equal(DaggerfallDoorMotion.Opening, session.Doors.Read(door.Id).Motion);
        Assert.Equal(2UL, session.State.DungeonActions[castle.ProfileKey].State[warning.Id].ActivationCount);
    }

    [Fact]
    public void Lockpick_activation_records_failed_skill_once_and_restores_door_attribution()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        spatial.KeepPosition = true;
        PerceptionFake perception = PerceptionFake.Create();
        perception.Responder = request => Receipt([.. request.Targets.Span.ToArray().Select(target =>
            new PerceptionPair(1, target.Entity, 1d, 1d, PerceptionPairKind.Visible, 1d))]);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases),
            perception.Service, random: RandomMaximum.Create());
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults));

        DaggerfallDoorView door = session.Doors.All.First(value =>
            value.Motion == DaggerfallDoorMotion.Closed && !value.IsLocked && value.Kind == DaggerfallDoorKind.Normal);
        Assert.Equal(DaggerfallDoorOperationResult.Started,
            session.Doors.Lock(door.Id, DaggerfallDoorOperationSource.DungeonAction));
        int skill = session.State.SkillUses.PermanentSkillValue(DaggerfallSkills.Lockpicking);
        session.State.PlayerControl.MoveTo(door.Pose.Translation + Vector3.UnitZ);
        session.State.PlayerControl.YawRadians = 0f;
        session.State.PlayerControl.PitchRadians = 0f;
        spatial.FloorHit = request => request.Direction.Y < -.5f ? default : new SpatialHit
        {
            Present = true,
            Kind = SpatialHitKind.Entity,
            Entity = door.Entity.Value,
            Point = door.Pose.Translation,
            Distance = 1f,
        };

        static ProductInputEvent Ui(string json) => Input(InputEventKind.DirectDigital) with
        {
            ValueKind = InputValueKind.ProductPayload,
            PayloadContract = "dagger.ui.action.v1"u8.ToArray(),
            PayloadData = Encoding.UTF8.GetBytes(json),
        };

        session.Update(new ProductUpdate(OuterUpdate(1), [Ui("{\"action\":\"activation-mode\",\"mode\":\"lockpick\"}")]));
        session.Update(new ProductUpdate(OuterUpdate(2), [Input(InputEventKind.DirectDigital,
            x: 1f, phase: InputPhase.DirectUi, intent: "interact")]));

        Assert.Equal(DaggerfallDoorMotion.Closed, session.Doors.Read(door.Id).Motion);
        Assert.Equal(skill, session.Doors.FailedLockpickingSkill(door.Id));
        Assert.Equal(1, session.State.Progression.SkillUses[DaggerfallSkills.Lockpicking]);

        // The source rejects a repeat at the same skill before drawing or recording another use.
        session.Update(new ProductUpdate(OuterUpdate(3), [Input(InputEventKind.DirectDigital,
            x: 1f, phase: InputPhase.DirectUi, intent: "interact")]));
        Assert.Equal(1, session.State.Progression.SkillUses[DaggerfallSkills.Lockpicking]);

        DaggerfallSavePayload saved = DaggerfallSavePayload.Read(session.CaptureSave());
        Assert.Equal(skill, Assert.Single(saved.Doors, value => value.Id == door.Id).FailedLockpickingSkill);
        Assert.Equal(1, saved.SkillUses.Counters.Single(value => value.Skill == DaggerfallSkills.Lockpicking).Uses);

        // A temporary skill modifier changes both the live chance and the source retry gate.
        _ = session.State.Actors.Player.Stats.GetStat(StatId.Parse(DaggerfallSkills.Lockpicking)).AddModifier(5);
        session.Update(new ProductUpdate(OuterUpdate(4), [Input(InputEventKind.DirectDigital,
            x: 1f, phase: InputPhase.DirectUi, intent: "interact")]));
        Assert.Equal(skill + 5, session.Doors.FailedLockpickingSkill(door.Id));
        Assert.Equal(2, session.State.Progression.SkillUses[DaggerfallSkills.Lockpicking]);

        ContentFake restoredContent = new(releases);
        PopulateContent(restoredContent, inputs);
        EngineContextFake restoredEngine = EngineContextFake.Create(restoredContent,
            SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases).Service, new AppearanceFake(releases),
            random: RandomMaximum.Create());
        ResolvedCompositionIdentity identity = GameCompositionResolver.Resolve(FullContent(root), new GameBundleId("daggerfall.classic")).RequireComposition().Identity;
        using DaggerfallSession restored = DaggerfallSession.Restore(restoredEngine.Context, new(definitions, inputs, DaggerfallTuning.Defaults, identity), DaggerfallSavePayload.Encode(saved));
        Assert.Equal(skill, restored.Doors.FailedLockpickingSkill(door.Id));
        Assert.Equal(1, restored.State.Progression.SkillUses[DaggerfallSkills.Lockpicking]);
    }

    [Fact]
    public void Exterior_lockpick_activation_uses_the_admitted_surface_for_retry_skill_use_and_legal_noise()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile source = ReadInputs(root);
        DaggerfallSiteProfile exterior = new(source.Project, source.SpatialArtifact, source.StaticMesh, source.WorldAppearance,
            source.InitialLook, source.Materials, source.ActorSprites, source.MobileSprites, source.Audio, source.ClassicPresentation,
            source.Site, source.Doors.Select(door => door with { LockSurface = DaggerfallLockInteractionSurface.Exterior }).ToArray(),
            DaggerfallWorldProfileKind.Exterior, "test/exterior", source.Portals, source.Anchors.Values.ToArray(), source.Lights,
            source.GroundContainerSprite, dungeonMap: null, dungeonActions: [], dungeonActionModels: [], source.InteriorBuilding,
            source.Music, source.AudioBundle, source.QuestMarkers, source.BillboardSprites, source.StaticNpcs,
            source.WaterVolumes, source.TerrainTextures, source.Population);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, exterior);
        SpatialFake spatial = SpatialFake.Create(exterior.SpatialArtifact.Sha256, releases);
        spatial.KeepPosition = true;
        PerceptionFake perception = PerceptionFake.Create();
        perception.Responder = request => Receipt([.. request.Targets.Span.ToArray().Select(target =>
            new PerceptionPair(1, target.Entity, 1d, 1d, PerceptionPairKind.Visible, 1d))]);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases),
            perception.Service, random: RandomMaximum.Create());
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, exterior, DaggerfallTuning.Defaults));

        DaggerfallDoorView door = session.Doors.All.First(value =>
            value.Motion == DaggerfallDoorMotion.Closed && value.IsLocked && value.Kind == DaggerfallDoorKind.Normal);
        Assert.Equal(DaggerfallLockInteractionSurface.Exterior, session.Doors.InteractionSurface(door.Id));
        int skill = session.State.SkillUses.PermanentSkillValue(DaggerfallSkills.Lockpicking);
        DaggerfallLockIncident? incident = null;
        session.LockIncident += value => incident = value;
        session.State.PlayerControl.MoveTo(door.Pose.Translation + Vector3.UnitZ);
        session.State.PlayerControl.YawRadians = 0f;
        session.State.PlayerControl.PitchRadians = 0f;
        spatial.FloorHit = request => request.Direction.Y < -.5f ? default : new SpatialHit
        {
            Present = true,
            Kind = SpatialHitKind.Entity,
            Entity = door.Entity.Value,
            Point = door.Pose.Translation,
            Distance = 1f,
        };

        session.Update(new ProductUpdate(OuterUpdate(1), [Ui("{\"action\":\"activation-mode\",\"mode\":\"lockpick\"}")]));
        session.Update(new ProductUpdate(OuterUpdate(2), [Input(InputEventKind.DirectDigital,
            x: 1f, phase: InputPhase.DirectUi, intent: "interact")]));

        Assert.Equal(DaggerfallDoorMotion.Closed, session.Doors.Read(door.Id).Motion);
        Assert.Equal(skill, session.Doors.FailedLockpickingSkill(door.Id));
        Assert.Equal(1, session.State.Progression.SkillUses[DaggerfallSkills.Lockpicking]);
        Assert.Equal(DaggerfallLockInteractionSurface.Exterior, incident?.Surface);
        Assert.Equal(DaggerfallLockInteractionStatus.Failed, incident?.Status);
        Assert.True(incident?.EmitsNoise);
        Assert.True(incident?.ReportsBreakingAndEntering);

        session.Update(new ProductUpdate(OuterUpdate(3), [Input(InputEventKind.DirectDigital,
            x: 1f, phase: InputPhase.DirectUi, intent: "interact")]));
        Assert.Equal(1, session.State.Progression.SkillUses[DaggerfallSkills.Lockpicking]);
        Assert.Equal(DaggerfallLockInteractionStatus.DuplicateAttempt, incident?.Status);
        Assert.False(incident?.EmitsNoise);
    }

    [Fact]
    public void Dungeon_text_modal_preserves_answer_continuation_and_linked_displays()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), PerceptionFake.Create().Service);
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults));
        DaggerfallDungeonActionDefinition prompt = new("test/prompt", 100, (uint)DaggerfallDungeonTriggerFlag.Direct,
            (byte)DaggerfallDungeonActionFlag.ShowTextWithInput, 0, 0, 0, 200, "test/link", SoundIndex: 4);
        DaggerfallDungeonActionDefinition link = new("test/link", 200, 0,
            (byte)DaggerfallDungeonActionFlag.SetGlobalVar, 7, 0, 0, 0, null);
        DaggerfallDungeonActionGraph graph = new(inputs.ProfileKey.LogicalId, [prompt, link], session.State.Variables,
            executeFamilyAction: session.ExecuteDungeonTextAction);
        session.State.DungeonActions[inputs.ProfileKey] = graph;

        DaggerfallDungeonActionDispatch initial = graph.Trigger(prompt.Id, DaggerfallDungeonActionEvent.Direct);
        Assert.Contains(initial.Executions, entry => entry.Outcome == DaggerfallDungeonActionOutcome.AwaitingAnswer);
        Assert.False(session.State.Variables.Read(new(DaggerfallVariableScope.Global, 0, 7)));
        DaggerfallDungeonTextProjection projection = Assert.IsType<DaggerfallDungeonTextProjection>(session.DungeonTextProjection);
        Assert.Equal(5404, projection.TextId);
        Assert.Equal(prompt.Id, DaggerfallSavePayload.Read(session.CaptureSave()).DungeonText.Pending?.ActionId);

        ProductInputEvent answer = Input(InputEventKind.DirectDigital) with
        {
            ValueKind = InputValueKind.ProductPayload,
            PayloadContract = "dagger.ui.action.v1"u8.ToArray(),
            PayloadData = Encoding.UTF8.GetBytes($"{{\"action\":\"dungeon-text-answer\",\"item\":\"{prompt.Id}\",\"revision\":\"{projection.Revision}\",\"text\":\"bow\"}}"),
        };
        session.Update(new ProductUpdate(OuterUpdate(1), [answer]));
        Assert.True(session.State.Variables.Read(new(DaggerfallVariableScope.Global, 0, 7)));
        Assert.Null(session.DungeonTextProjection);
        Assert.Null(DaggerfallSavePayload.Read(session.CaptureSave()).DungeonText.Pending);
        Assert.Equal(1UL, graph.State[link.Id].ActivationCount);

        session.Update(new ProductUpdate(OuterUpdate(2), [answer]));
        Assert.Equal(1UL, graph.State[link.Id].ActivationCount);

        DaggerfallDungeonActionDefinition first = new("test/first-text", 300, (uint)DaggerfallDungeonTriggerFlag.Direct,
            (byte)DaggerfallDungeonActionFlag.ShowText, 0, 0, 0, 400, "test/linked-text", SoundIndex: 4);
        DaggerfallDungeonActionDefinition linked = new("test/linked-text", 400, 0,
            (byte)DaggerfallDungeonActionFlag.ShowText, 0, 0, 0, 0, null, SoundIndex: 0);
        session.State.DungeonActions[inputs.ProfileKey] = new DaggerfallDungeonActionGraph(inputs.ProfileKey.LogicalId,
            [first, linked], session.State.Variables, executeFamilyAction: session.ExecuteDungeonTextAction);
        _ = session.State.DungeonActions[inputs.ProfileKey].Trigger(first.Id, DaggerfallDungeonActionEvent.Direct);
        DaggerfallDungeonTextProjection top = Assert.IsType<DaggerfallDungeonTextProjection>(session.DungeonTextProjection);
        Assert.Equal(8604, top.TextId);

        ProductInputEvent close = Input(InputEventKind.DirectDigital) with
        {
            ValueKind = InputValueKind.ProductPayload,
            PayloadContract = "dagger.ui.action.v1"u8.ToArray(),
            PayloadData = Encoding.UTF8.GetBytes($"{{\"action\":\"dungeon-text-close\",\"item\":\"{top.ActionId}\",\"revision\":\"{top.Revision}\"}}"),
        };
        session.Update(new ProductUpdate(OuterUpdate(3), [close]));
        DaggerfallDungeonTextProjection beneath = Assert.IsType<DaggerfallDungeonTextProjection>(session.DungeonTextProjection);
        Assert.Equal(8600, beneath.TextId);
        close = close with { PayloadData = Encoding.UTF8.GetBytes($"{{\"action\":\"dungeon-text-close\",\"item\":\"{beneath.ActionId}\",\"revision\":\"{beneath.Revision}\"}}") };
        session.Update(new ProductUpdate(OuterUpdate(4), [close]));
        Assert.Null(session.DungeonTextProjection);
    }

    [Fact]
    public void Contextual_activation_opens_the_current_corpse_with_one_visibility_query()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        spatial.KeepPosition = true;
        PerceptionFake perception = PerceptionFake.Create();
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), perception.Service);
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults));

        WorldPoint playerPosition = session.State.PlayerControl.Position ?? throw new InvalidOperationException("The test session has no player position.");
        long spawned = session.SpawnActor("rat", new ActorPose(playerPosition with { Z = playerPosition.Z - 1f }, 0f));
        session.State.Actors.Get(spawned).Stats.GetTrack(TrackId.Parse("health")).SetCurrent(1, clamp: true);
        session.State.Actors.Player.Stats.GetStat(StatId.Parse("strength")).BaseValue = 60; // classic damage floors at zero; stage a swing this fixture can rely on
        session.ResolveExplicitMelee(new ExplicitMeleeRequest(1, spawned, 1, 1, .125));
        Assert.True(session.Corpses.ContainsKey(spawned));
        AimActivationAt(session, spawned);
        perception.Receipt = Receipt(new PerceptionPair(1, checked((ulong)spawned), 1d, 1d, PerceptionPairKind.Visible, 1d));
        perception.Requests.Clear();

        session.Update(new ProductUpdate(OuterUpdate(1), [
            Input(InputEventKind.DirectDigital, x: 1f, phase: InputPhase.DirectUi, intent: "interact"),
        ]));

        Assert.Single(perception.Requests, request => request.Targets.Span.ToArray()
            .Any(target => target.Entity == checked((ulong)spawned)));
        Assert.IsType<LootPresentation>(session.OpenLoot);
        Assert.True(session.ActivationView.Applied);
        Assert.Equal("grab", session.ActivationView.Mode);
    }

    [Fact]
    public void Session_book_use_opens_a_retained_reader_and_persists_its_notes_through_restore()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        DaggerfallSavePayload saved;
        DaggerfallItemDefinition definition = definitions.RequireItem(new DaggerfallItemId("template-277"));
        InventoryStackId stack = InventoryStackId.Parse("session.retained-book");
        using (DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults)))
        {
    session.State.Inventory.Grant(new(new InventoryItemId(definition.Id.Value), stack, 1));
    session.State.ItemInstances.RegisterStack(DaggerfallItemOwner.Player,
        stack,
        DaggerfallItemInstanceMetadata.Default(definition, DaggerfallItemOwner.Player) with { BookId = 59 });
            session.Update(new ProductUpdate(OuterUpdate(1), []));

            void Submit(object action, ulong step) => session.Update(new ProductUpdate(OuterUpdate(step), [Ui(JsonSerializer.Serialize(action))]));

            Submit(new { action = "inventory-use", revision = engine.PublishedNested("inventory", "revision"), item = $"stack:{stack.Value}" }, 2);
            Assert.Equal("journal", engine.PublishedNested("panelRequest", "panel"));
            Assert.Equal(1UL, session.State.Inventory.Read().Stacks.Single(item => item.Id == stack).Quantity);
            Submit(new { action = "notebook-add", revision = engine.PublishedNested("notebook", "revision"), text = "Remember this passage." }, 3);
            saved = DaggerfallSavePayload.Read(session.CaptureSave());
        }

        Assert.Single(saved.Notebook.Books, book => book.BookId == 59);
        Assert.Equal(["Remember this passage."], saved.Notebook.Notes.Select(note => note.Text));
        ContentFake resumedContent = new(releases);
        PopulateContent(resumedContent, inputs);
        EngineContextFake resumedEngine = EngineContextFake.Create(resumedContent, spatial.Service, new AppearanceFake(releases));
        ResolvedCompositionIdentity identity = GameCompositionResolver.Resolve(FullContent(root), new GameBundleId("daggerfall.classic")).RequireComposition().Identity;
        using DaggerfallSession restored = DaggerfallSession.Restore(resumedEngine.Context, new(definitions, inputs, DaggerfallTuning.Defaults, identity), DaggerfallSavePayload.Encode(saved));
        restored.PublishInitial();

        Dictionary<string, object?> projection = Assert.IsType<Dictionary<string, object?>>(resumedEngine.Published());
        Dictionary<string, object?> notebook = Assert.IsType<Dictionary<string, object?>>(projection["notebook"]);
        Dictionary<string, object?> book = Assert.IsType<Dictionary<string, object?>>(notebook["book"]);
        Assert.Equal(59d, Assert.IsType<double>(book["id"]));
        Assert.Equal(saved.Notebook.Books.Single(value => value.BookId == 59).Pages[0], Assert.IsType<string>(book["text"]));
        object?[] notes = Assert.IsType<object?[]>(notebook["notes"]);
        Assert.Equal("Remember this passage.", Assert.IsType<Dictionary<string, object?>>(Assert.Single(notes))["text"]);
    }
}
