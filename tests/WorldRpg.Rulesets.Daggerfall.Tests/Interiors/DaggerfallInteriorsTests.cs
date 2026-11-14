using Rusty.Engine;
using Rusty.Engine.Entities;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>The interiors owner on its own, without a session: what an arrival and a departure do to a dungeon.</summary>
public sealed class DaggerfallInteriorsTests
{
    [Fact]
    public void Entering_a_dungeon_admits_its_actions_once_begins_its_visit_and_leaving_by_its_portal_reveals_it()
    {
        DaggerfallDungeonActionTriggerRuntimeTests.TriggerSpatialDouble spatial = DaggerfallDungeonActionTriggerRuntimeTests.TriggerSpatialDouble.Create();
        ReferenceResolvingContentFake content = ReferenceResolvingContentFake.Create();
        using SpatialMovementSystem movement = new(spatial.Service, content.Service,
            new SpatialContentArtifact("spatial/action-triggers", new ContentSha256(1, 2, 3, 4), 1), new SpatialTuning(.5d, 8, 8, 1));
        using EntityDirectory entities = new();
        DaggerfallSiteProfile outside = Profile("profile-outside", 1, null);
        DaggerfallSiteMarker exit = new("exit", DaggerfallSiteMarkerKind.Portal, new WorldPoint(4f, 0f, 4f), outside.ProfileKey.LogicalId);
        DaggerfallSiteProfile dungeon = Profile("profile-dungeon", 2, new DaggerfallDungeonMapContent([], [], [exit]));
        using DaggerfallDungeonActionTriggerRuntime triggers = new(entities, spatial.Service, movement,
            [(outside.ProfileKey, outside)], outside.ProfileKey);
        Dictionary<DaggerfallWorldProfileKey, DaggerfallDungeonActionGraph> graphs = [];
        Dictionary<DaggerfallWorldProfileKey, DaggerfallDungeonDiscovery> discoveries = [];
        int graphsMade = 0;
        DaggerfallInteriors interiors = new(triggers, graphs, discoveries, (key, inputs) =>
        {
            graphsMade++;
            return new DaggerfallDungeonActionGraph(key.LogicalId, inputs.DungeonActions, new DaggerfallVariableStore(new Dictionary<string, int>(StringComparer.Ordinal)).Write);
        }, new DaggerfallSiteContext(TestPayload.Definitions.Locations));

        interiors.Enter(dungeon.ProfileKey, dungeon);
        Assert.True(graphs.ContainsKey(dungeon.ProfileKey));
        DaggerfallDungeonDiscovery discovery = Assert.Contains(dungeon.ProfileKey, (IDictionary<DaggerfallWorldProfileKey, DaggerfallDungeonDiscovery>)discoveries);

        // Back out far from the portal: it stays hidden. A failed change gives the source its triggers back.
        interiors.Left(dungeon.ProfileKey, dungeon, outside.ProfileKey, new WorldPoint(40f, 0f, 40f));
        Assert.False(discovery.IsMarkerDiscovered(exit.Id));
        interiors.Reactivate(dungeon.ProfileKey);

        // In again: the same graph and discovery; out by the portal, which is revealed.
        interiors.Enter(dungeon.ProfileKey, dungeon);
        Assert.Equal(1, graphsMade);
        Assert.Same(discovery, discoveries[dungeon.ProfileKey]);
        interiors.Left(dungeon.ProfileKey, dungeon, outside.ProfileKey, new WorldPoint(5f, 0f, 5f));
        Assert.True(discovery.IsMarkerDiscovered(exit.Id));
    }

    private static DaggerfallSiteProfile Profile(string logicalProfileId, int index, DaggerfallDungeonMapContent? map) => new(
        new ProjectFacts(null, new Dictionary<long, AuthoredActor>()),
        DaggerfallSiteGeometry.Closure(new SpatialContentArtifact("spatial/action-triggers", new ContentSha256(1, 2, 3, 4), 1),
            new ContentArtifact("mesh/action-triggers", new ContentSha256(1, 2, 3, 4))),
        new AuthoredWorldAppearance(default, default, true, RenderLayer.Scene),
        new PlayerInitialLook(0f, 0f),
        [],
        new Dictionary<long, NormalizedActorSprite>(),
        site: new DaggerfallSiteId(1, index),
        logicalProfileId: logicalProfileId,
        dungeonMap: map);
}
