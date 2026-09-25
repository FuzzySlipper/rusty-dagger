using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallDungeonDiscoveryTests
{
    [Fact]
    public void Dungeon_map_content_uses_per_placement_bounds_entrance_and_door_identities()
    {
        (string root, PrivateersHoldInputs inputs) = ReadPrivateersHold();
        DaggerfallDungeonMapContent map = Assert.IsType<DaggerfallDungeonMapContent>(inputs.DungeonMap);
        using JsonDocument normalized = JsonDocument.Parse(File.ReadAllBytes(
            Path.Combine(root, "content/worldrpg/imports/privateers-hold/normalized.json")));
        JsonElement world = normalized.RootElement.GetProperty("world");
        JsonElement sourcePlacements = world.GetProperty("geometryPlacements");
        HashSet<string> staticMeshIds = world.GetProperty("staticMeshIds").EnumerateArray()
            .Select(value => value.GetString()!)
            .ToHashSet(StringComparer.Ordinal);
        HashSet<string> doorVisualMeshIds = world.GetProperty("doors").EnumerateArray()
            .SelectMany(door => door.GetProperty("visualMeshIds").EnumerateArray())
            .Select(value => value.GetString()!)
            .ToHashSet(StringComparer.Ordinal);
        Dictionary<string, HashSet<Vector3>> sourceVerticesByMesh = normalized.RootElement.GetProperty("meshes").EnumerateArray()
            .ToDictionary(
                mesh => mesh.GetProperty("id").GetString()!,
                mesh => mesh.GetProperty("vertices").EnumerateArray().Select(ReadVector3).ToHashSet(),
                StringComparer.Ordinal);
        string[] expectedGeometryIds = sourcePlacements.EnumerateArray()
            .Select(value => value.GetProperty("id").GetString()!)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expectedGeometryIds, map.GeometryPlacements.Select(value => value.PlacementId));
        Assert.Equal(inputs.Doors.Select(door => door.Id).OrderBy(id => id.SourceKey, StringComparer.Ordinal)
            .ThenBy(id => id.BlockX).ThenBy(id => id.BlockZ).ThenBy(id => id.ModelIndex), map.DoorIds);

        foreach (DaggerfallDungeonMapGeometry placement in map.GeometryPlacements)
        {
            JsonElement sourcePlacement = sourcePlacements.EnumerateArray()
                .Single(value => value.GetProperty("id").GetString() == placement.PlacementId);
            JsonElement bounds = sourcePlacement.GetProperty("bounds");
            Assert.Equal(ReadVector3(bounds.GetProperty("minimum")), placement.BoundsMin);
            Assert.Equal(ReadVector3(bounds.GetProperty("maximum")), placement.BoundsMax);
            Assert.Equal(sourcePlacement.GetProperty("meshIds").EnumerateArray().Select(value => value.GetString()), placement.MeshIds);
            Vector3[] samples = sourcePlacement.GetProperty("samplePoints").EnumerateArray().Select(ReadVector3).ToArray();
            Assert.Equal(samples, placement.SamplePoints);
            Assert.InRange(placement.SamplePoints.Count, 2, 4);
            HashSet<Vector3> placementVertices = placement.MeshIds.SelectMany(meshId => sourceVerticesByMesh[meshId]).ToHashSet();
            Assert.All(placement.SamplePoints, point => Assert.Contains(point, placementVertices));
        }

        Assert.Contains(map.GeometryPlacements.SelectMany(placement => placement.MeshIds.Select(meshId => (meshId, placement)))
            .GroupBy(value => value.meshId, value => value.placement),
            group => group.Count() > 1 && group.Select(value => (value.BoundsMin, value.BoundsMax)).Distinct().Count() > 1);
        Assert.NotEmpty(doorVisualMeshIds);
        Assert.Empty(doorVisualMeshIds.Intersect(staticMeshIds));
        Assert.All(map.GeometryPlacements.SelectMany(placement => placement.MeshIds), meshId => Assert.Contains(meshId, staticMeshIds));

        DaggerfallDungeonMapMarker entrance = Assert.Single(map.Markers, value => value.Kind == DaggerfallDungeonMapMarkerKind.Entrance);
        JsonElement sourceEntrance = world.GetProperty("enterMarker");
        Assert.Equal(sourceEntrance.GetProperty("id").GetString(), entrance.Id);
        Assert.Equal(ReadVector3(sourceEntrance.GetProperty("position")), new Vector3(entrance.Position.X, entrance.Position.Y, entrance.Position.Z));
        Assert.Equal(new DaggerfallSiteId(17, 179), inputs.Site);
    }

    [Fact]
    public void Castle_dungeon_content_admits_each_source_placement_with_visibility_samples()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        PrivateersHoldInputs inputs = PrivateersHoldContent.Read(
            GeneratedContent(root),
            File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.castle-necromoghan.json")),
            definitions);
        DaggerfallDungeonMapContent map = Assert.IsType<DaggerfallDungeonMapContent>(inputs.DungeonMap);
        using JsonDocument normalized = JsonDocument.Parse(File.ReadAllBytes(
            Path.Combine(root, "content/worldrpg/imports/castle-necromoghan/normalized.json")));
        JsonElement sourcePlacements = normalized.RootElement.GetProperty("world").GetProperty("geometryPlacements");

        Assert.Equal(sourcePlacements.GetArrayLength(), map.GeometryPlacements.Count);
        Assert.Equal(new DaggerfallSiteId(17, 9), inputs.Site);
        Assert.All(map.GeometryPlacements, placement => Assert.InRange(placement.SamplePoints.Count, 2, 4));
        Assert.Equal(125, map.DoorIds.Count);
    }

    [Fact]
    public void Discovery_captures_partial_profile_scoped_progress_and_starts_each_entry_unvisited()
    {
        (_, PrivateersHoldInputs inputs) = ReadPrivateersHold();
        DaggerfallDungeonMapContent map = Assert.IsType<DaggerfallDungeonMapContent>(inputs.DungeonMap);
        DaggerfallWorldProfileKey profile = inputs.ProfileKey;
        DaggerfallDungeonDiscovery discovery = new(profile, map);
        string placementId = map.GeometryPlacements[0].PlacementId;
        DaggerfallRdbDoorId doorId = map.DoorIds[0];
        Vector3 seenSurface = map.GeometryPlacements[0].SamplePoints[0];
        DaggerfallDungeonSurfaceCell seenCell = DaggerfallDungeonSurfaceCell.At(seenSurface);
        DaggerfallDungeonMapMarker entrance = Assert.Single(map.Markers, marker => marker.Kind == DaggerfallDungeonMapMarkerKind.Entrance);

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
        (_, PrivateersHoldInputs inputs) = ReadPrivateersHold();
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
    public void Authored_transition_is_a_stable_portal_marker_that_remains_hidden_until_revealed()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        JsonObject payload = JsonNode.Parse(File.ReadAllBytes(
            Path.Combine(root, "content/worldrpg/payloads/daggerfall.privateers-hold.json")))!.AsObject();
        payload["world"]!["transitions"] = new JsonArray(new JsonObject
        {
            ["id"] = "portal/stone-door",
            ["position"] = new JsonArray(4, 5, 6),
            ["radius"] = 2.5F,
            ["destinationProfile"] = "worldrpg/imports/stone-chamber",
        });

        PrivateersHoldInputs inputs = PrivateersHoldContent.Read(
            GeneratedContent(root),
            Encoding.UTF8.GetBytes(payload.ToJsonString()),
            definitions);
        DaggerfallDungeonMapContent map = Assert.IsType<DaggerfallDungeonMapContent>(inputs.DungeonMap);
        DaggerfallDungeonMapMarker portal = Assert.Single(map.Markers, value => value.Id == "portal/stone-door");
        Assert.Equal(DaggerfallDungeonMapMarkerKind.Portal, portal.Kind);
        Assert.Equal(new WorldRpg.Kit.Controls.WorldPoint(4F, 5F, 6F), portal.Position);
        Assert.Equal("worldrpg/imports/stone-chamber", portal.DestinationLogicalProfile);

        DaggerfallDungeonDiscovery discovery = new(inputs.ProfileKey, map);
        Assert.False(discovery.IsMarkerDiscovered(portal.Id));
        Assert.True(discovery.RevealMarker(portal.Id));
        Assert.True(discovery.IsMarkerDiscovered(portal.Id));
    }

    private static (string Root, PrivateersHoldInputs Inputs) ReadPrivateersHold()
    {
        string root = RepositoryRoot();
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        ProductContent content = GeneratedContent(root);
        PrivateersHoldInputs inputs = PrivateersHoldContent.Read(content,
            File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.privateers-hold.json")), definitions);
        return (root, inputs);
    }

    private static ProductContent GeneratedContent(string root)
    {
        string contentRoot = Path.Combine(root, "content");
        ProductContentFile[] files = Directory.GetFiles(Path.Combine(contentRoot, "worldrpg/imports"), "*", SearchOption.AllDirectories)
            .Select(path => new ProductContentFile(
                Encoding.UTF8.GetBytes(Path.GetRelativePath(contentRoot, path).Replace(Path.DirectorySeparatorChar, '/')),
                File.ReadAllBytes(path)))
            .ToArray();
        return new ProductContent(files);
    }

    private static Vector3 ReadVector3(JsonElement value) => new(
        value.GetProperty("x").GetSingle(),
        value.GetProperty("y").GetSingle(),
        value.GetProperty("z").GetSingle());

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md"))) return current.FullName;
        throw new InvalidOperationException("Could not locate the Rusty Dagger repository root.");
    }
}
