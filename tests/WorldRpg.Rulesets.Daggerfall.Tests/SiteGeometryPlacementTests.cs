using Rusty.Engine;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>A profile that places several collision/navigation parts admits each by its own whole-cell offset.</summary>
public sealed class SiteGeometryPlacementTests
{
    /// <summary>
    /// The site lifecycle places every spatial part a profile states, each under its own stable Engine
    /// identity and at its own whole-cell offset, in one admission that holds the parts' source open.
    /// </summary>
    [Fact]
    public void A_location_places_each_block_artifact_beside_the_others_while_their_source_is_open()
    {
        DaggerfallSiteProfile source = ReadInputs(TestData.RepositoryRoot);
        int opened = 0, closed = 0;
        DaggerfallSiteGeometry geometry = new(source.Geometry.NavigationGridId,
            [
                new DaggerfallSiteSpatialPart("block/0/0", source.SpatialArtifact.Path, source.SpatialArtifact.Sha256),
                new DaggerfallSiteSpatialPart("block/1/-1", source.SpatialArtifact.Path, source.SpatialArtifact.Sha256, 64, 64),
            ],
            source.Geometry.Meshes,
            () => { opened++; return new Release(() => closed++); });
        DaggerfallSiteProfile inputs = WithGeometry(source, geometry);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(source.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));

        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(TestPayload.Definitions, inputs, DaggerfallTuning.Defaults));

        SpatialContentArtifactResidencyRequest placed = Assert.Single(spatial.ContentResidencyRequests);
        SpatialContentArtifactInstance[] instances = placed.Admitted.ToArray();
        Assert.Equal(2, instances.Length);
        Assert.Equal(2, instances.Select(instance => instance.Id).Distinct().Count());
        Assert.Equal([(0L, 0L), (64L, 64L)], instances.Select(instance => (instance.ColumnOffset, instance.RowOffset)));
        Assert.Equal((1, 1), (opened, closed));
    }

    private sealed class Release(Action release) : IDisposable
    {
        public void Dispose() => release();
    }

    private static DaggerfallSiteProfile WithGeometry(DaggerfallSiteProfile source, DaggerfallSiteGeometry geometry) => new(
        source.Project, geometry, source.WorldAppearance, source.InitialLook, source.Materials, source.ActorSprites, source.MobileSprites,
        source.Audio, source.ClassicPresentation, source.Site, source.Doors, source.ProfileKind, source.ProfileKey.LogicalId, source.Portals,
        source.Anchors.Values.ToArray(), source.Lights, source.GroundContainerSprite, source.DungeonMap, source.DungeonActions,
        source.DungeonActionModels, source.InteriorBuilding, source.Music, source.QuestMarkers, source.BillboardSprites, source.StaticNpcs,
        source.WaterVolumes, source.TerrainTextures, source.Population)
    {
        AmbientZones = source.AmbientZones,
        PropertyContainers = source.PropertyContainers,
    };
}
