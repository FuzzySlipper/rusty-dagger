using System.Numerics;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DungeonTeleportSessionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Teleport_dispatch_relocates_and_saves_the_same_player_and_graph_state(bool linkedAction)
    {
        var source = ReadInputs(TestData.RepositoryRoot);
        var destination = new Vector3(14, 8, -19);
        var teleport = new DaggerfallDungeonActionDefinition("test/teleport", 700001,
            (uint)DaggerfallDungeonTriggerFlag.Direct, (byte)DaggerfallDungeonActionFlag.Teleport,
            0, 0, 0, 700002, linkedAction ? "test/destination" : null, NextObjectPosition: destination,
            NextObjectRotationDegrees: new(0, 90, 0));
        var target = new DaggerfallDungeonActionDefinition("test/destination", 700002, 0, 0, 0, 0, 0, 0, null,
            SourcePosition: destination);
        var profile = new DaggerfallSiteProfile(source.Project, source.Geometry, source.WorldAppearance,
            source.InitialLook, source.Materials, source.ActorSprites, source.MobileSprites, source.Audio, source.ClassicPresentation,
            source.Site, source.Doors, source.ProfileKind, source.ProfileKey.LogicalId, source.Portals, source.Anchors.Values.ToArray(), source.Lights,
            source.GroundContainerSprite, source.DungeonMap, [.. source.DungeonActions, teleport, target], source.DungeonActionModels,
            source.InteriorBuilding, source.Music, source.QuestMarkers, source.BillboardSprites, source.StaticNpcs,
            source.WaterVolumes, source.TerrainTextures, source.Population);
        List<string> releases = [];
        var content = new ContentFake(releases); PopulateContent(content, profile);
        var spatial = SpatialFake.Create(profile.SpatialArtifact.Sha256, releases); spatial.KeepPosition = true;
        var engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        var composition = new DaggerfallSessionComposition(TestPayload.Definitions, profile, DaggerfallTuning.Defaults);
        using var session = DaggerfallSession.StartNew(engine.Context, composition);
        var player = session.State.Actors.Player.Actor.Entity;
        var result = session.State.DungeonActions[profile.ProfileKey].Trigger(teleport.Id, DaggerfallDungeonActionEvent.Direct);
        Assert.Contains(result.Executions, value => value.ActionId == teleport.Id && value.Outcome == DaggerfallDungeonActionOutcome.Applied);
        Assert.Equal(destination, session.State.PlayerControl.Position!.Value.ToVector());
        // The player takes the destination's source pose like any posed model (#9793): a +90-degree source
        // yaw faces the player along +X.
        Assert.Equal(MathF.PI / 2, session.State.PlayerControl.YawRadians, 5);
        Assert.Equal(player, session.State.Actors.Player.Actor.Entity);
        var saved = session.CaptureSave();
        session.Dispose();
        using var restored = DaggerfallSession.Restore(engine.Context, composition, saved);
        Assert.Equal(destination, restored.State.PlayerControl.Position!.Value.ToVector());
        Assert.Equal(MathF.PI / 2, restored.State.PlayerControl.YawRadians, 5);
        Assert.Equal(1UL, restored.State.DungeonActions[profile.ProfileKey].State[teleport.Id].ActivationCount);
    }
}
