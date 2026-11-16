using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallDungeonDiscoveryTests
{
    /// <summary>
    /// The map places every static model of the dungeon's blocks once, by its own bounds and visibility samples, beside the
    /// dungeon's doors and its entrance: Privateer's Hold places one cave mesh at more than one pose.
    /// </summary>
    [Fact]
    public void Dungeon_map_content_uses_per_placement_bounds_entrance_and_door_identities()
    {
        DaggerfallSiteProfile inputs = ReadInputs(TestData.RepositoryRoot);
        DaggerfallDungeonMapContent map = Assert.IsType<DaggerfallDungeonMapContent>(inputs.DungeonMap);

        Assert.Equal(StaticModels(PrivateersHold), map.GeometryPlacements.Count);
        Assert.Equal(map.GeometryPlacements.Count, map.GeometryPlacements.Select(value => value.PlacementId).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(inputs.Doors.Select(door => door.Id).OrderBy(id => id.SourceKey, StringComparer.Ordinal)
            .ThenBy(id => id.BlockX).ThenBy(id => id.BlockZ).ThenBy(id => id.ModelIndex), map.DoorIds);
        foreach (DaggerfallDungeonMapGeometry placement in map.GeometryPlacements)
        {
            Assert.InRange(placement.SamplePoints.Count, 2, 4);
            Assert.All(placement.SamplePoints, point => Assert.True(Vector3.Clamp(point, placement.BoundsMin, placement.BoundsMax) == point,
                $"Sample {point} of '{placement.PlacementId}' lies outside its bounds."));
            Assert.Single(placement.MeshIds);
        }

        Assert.Contains(map.GeometryPlacements.GroupBy(placement => placement.MeshIds[0]),
            group => group.Count() > 1 && group.Select(value => (value.BoundsMin, value.BoundsMax)).Distinct().Count() > 1);

        DaggerfallSiteMarker entrance = Assert.Single(map.Markers, value => value.Kind == DaggerfallSiteMarkerKind.Entrance);
        Assert.Equal(inputs.RequireAnchor(DaggerfallLocationAssembly.EnterAnchor).Position, entrance.Position);
        Assert.Equal(new DaggerfallSiteId(17, 179), inputs.Site);
    }

    [Fact]
    public void Castle_dungeon_content_admits_each_source_placement_with_visibility_samples()
    {
        DaggerfallSiteProfile inputs = Assembled(DaggerfallWorldProfileIds.Dungeon(CastleNecromoghan));
        DaggerfallDungeonMapContent map = Assert.IsType<DaggerfallDungeonMapContent>(inputs.DungeonMap);

        Assert.Equal(StaticModels(CastleNecromoghan), map.GeometryPlacements.Count);
        Assert.Equal(new DaggerfallSiteId(17, 9), inputs.Site);
        Assert.All(map.GeometryPlacements, placement => Assert.InRange(placement.SamplePoints.Count, 2, 4));
        Assert.Equal(125, map.DoorIds.Count);
    }

    /// <summary>How many static models (neither moving nor a door) a location's dungeon blocks place, as the block publication states them.</summary>
    private static int StaticModels(DaggerfallSiteId site)
    {
        DaggerfallWorldBlocks blocks = new(TestSessions.Data(FullContent(TestData.RepositoryRoot)));
        DaggerfallSiteDungeonBlock[] placed = [.. TestPayload.Definitions.Locations.Records.Single(record => record.Id == site).DungeonBlocks];
        IReadOnlyDictionary<DaggerfallWorldBlockKey, DaggerfallWorldBlockDocument> documents =
            blocks.Read(placed.Select(block => new DaggerfallWorldBlockKey(DaggerfallWorldBlockKind.Rdb, block.SourceKey)));
        return placed.Sum(block => documents[new(DaggerfallWorldBlockKind.Rdb, block.SourceKey)].Models
            .Count(model => model.Action is null && model.DoorId is null && model.MeshArtifactId is not null));
    }

    [Fact]
    public void Discovery_captures_partial_profile_scoped_progress_and_starts_each_entry_unvisited()
    {
        DaggerfallSiteProfile inputs = ReadInputs(TestData.RepositoryRoot);
        DaggerfallDungeonMapContent map = Assert.IsType<DaggerfallDungeonMapContent>(inputs.DungeonMap);
        DaggerfallWorldProfileKey profile = inputs.ProfileKey;
        DaggerfallDungeonDiscovery discovery = new(profile, map);
        string placementId = map.GeometryPlacements[0].PlacementId;
        DaggerfallRdbDoorId doorId = map.DoorIds[0];
        Vector3 seenSurface = map.GeometryPlacements[0].SamplePoints[0];
        DaggerfallDungeonSurfaceCell seenCell = DaggerfallDungeonSurfaceCell.At(seenSurface);
        DaggerfallSiteMarker entrance = Assert.Single(map.Markers, marker => marker.Kind == DaggerfallSiteMarkerKind.Entrance);

        Assert.True(discovery.ObservePlacement(placementId));
        Assert.True(discovery.ObserveDoor(doorId));
        Assert.True(discovery.RevealMarker(entrance.Id));
        Assert.True(discovery.ObserveSurface(seenSurface));
        Assert.True(discovery.IsPlacementDiscovered(placementId));
        Assert.True(discovery.IsDoorDiscovered(doorId));
        Assert.True(discovery.IsMarkerDiscovered(entrance.Id));
        Assert.True(discovery.WasPlacementVisitedThisEntry(placementId));
        Assert.True(discovery.IsSurfaceCellDiscovered(seenCell));

        discovery.AddNoteMarker("note-1", new WorldRpg.Kit.Controls.WorldPoint(5F, 2F, -3F), "stairs");
        discovery.EditNoteMarker("note-1", "stairs down");
        DaggerfallDungeonDiscoverySnapshot snapshot = discovery.Capture();

        Assert.Equal(profile, snapshot.Profile);
        Assert.Equal([placementId], snapshot.DiscoveredPlacementIds);
        Assert.Equal([doorId], snapshot.DiscoveredDoors);
        Assert.Equal([entrance.Id], snapshot.DiscoveredMarkerIds);
        Assert.Equal([seenCell], snapshot.DiscoveredSurfaceCells);
        Assert.Equal("stairs down", Assert.Single(snapshot.NoteMarkers).Text);

        DaggerfallDungeonDiscovery restored = new(profile, map, snapshot);
        Assert.True(restored.IsPlacementDiscovered(placementId));
        Assert.True(restored.IsDoorDiscovered(doorId));
        Assert.True(restored.IsMarkerDiscovered(entrance.Id));
        Assert.True(restored.IsSurfaceCellDiscovered(seenCell));
        Assert.False(restored.WasPlacementVisitedThisEntry(placementId));
        Assert.False(restored.WasDoorVisitedThisEntry(doorId));
        Assert.False(restored.WasMarkerVisitedThisEntry(entrance.Id));
        Assert.Equal("stairs down", Assert.Single(restored.NoteMarkers).Text);

        Assert.False(restored.ObservePlacement(placementId));
        Assert.True(restored.WasPlacementVisitedThisEntry(placementId));
        restored.BeginVisit();
        Assert.True(restored.IsPlacementDiscovered(placementId));
        Assert.False(restored.WasPlacementVisitedThisEntry(placementId));
        Assert.False(restored.RevealMarker(entrance.Id));
        Assert.True(restored.WasMarkerVisitedThisEntry(entrance.Id));
        Assert.True(restored.RemoveNoteMarker("note-1"));
        Assert.Empty(restored.NoteMarkers);
    }

    [Fact]
    public void Discovery_rejects_cross_profile_and_unknown_source_restore_ids()
    {
        DaggerfallSiteProfile inputs = ReadInputs(TestData.RepositoryRoot);
        DaggerfallDungeonMapContent map = Assert.IsType<DaggerfallDungeonMapContent>(inputs.DungeonMap);
        DaggerfallWorldProfileKey profile = inputs.ProfileKey;
        DaggerfallDungeonDiscovery discovery = new(profile, map);
        discovery.ObservePlacement(map.GeometryPlacements[0].PlacementId);
        DaggerfallDungeonDiscoverySnapshot snapshot = discovery.Capture();

        DaggerfallWorldProfileKey otherProfile = new DaggerfallWorldProfileKey(profile.Site, profile.Kind, $"{profile.LogicalId}/alternate").Validate();
        Assert.Throws<InvalidOperationException>(() => new DaggerfallDungeonDiscovery(otherProfile, map, snapshot));

        DaggerfallDungeonDiscoverySnapshot unknownPlacement = snapshot with { DiscoveredPlacementIds = ["model/unadmitted"] };
        Assert.Throws<InvalidOperationException>(() => new DaggerfallDungeonDiscovery(profile, map, unknownPlacement));
        DaggerfallDungeonDiscoverySnapshot outsideSurface = snapshot with { DiscoveredSurfaceCells = [new(999999, 999999, 999999)] };
        Assert.Throws<InvalidOperationException>(() => new DaggerfallDungeonDiscovery(profile, map, outsideSurface));
    }

    [Fact]
    public void A_source_exit_is_a_stable_portal_marker_that_remains_hidden_until_revealed()
    {
        DaggerfallSiteProfile inputs = ReadInputs(TestData.RepositoryRoot);
        DaggerfallDungeonMapContent map = Assert.IsType<DaggerfallDungeonMapContent>(inputs.DungeonMap);
        DaggerfallSitePortal exit = Assert.Single(inputs.Portals);
        DaggerfallSiteMarker portal = Assert.Single(map.Markers, value => value.Kind == DaggerfallSiteMarkerKind.Portal);
        Assert.Equal((exit.Id, exit.Position, exit.DestinationLogicalProfile), (portal.Id, portal.Position, portal.DestinationLogicalProfile));

        DaggerfallDungeonDiscovery discovery = new(inputs.ProfileKey, map);
        Assert.False(discovery.IsMarkerDiscovered(portal.Id));
        Assert.True(discovery.RevealMarker(portal.Id));
        Assert.True(discovery.IsMarkerDiscovered(portal.Id));
    }
}
