using System.Numerics;
using System.Text.Json;
using Rusty.Engine;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// A location assembled from the catalog and the per-block publication is the profile its published site
/// closure is: the same identities, doors, markers, people and spawn points, only placed through the
/// product-wide meshes and one collision/navigation artifact per block.
/// </summary>
public sealed class AssembledSiteProfileTests
{
    private static readonly DaggerfallSiteId Charing = new(17, 4);
    private static readonly DaggerfallSiteId PrivateersHold = new(17, 179);

    private static readonly Lazy<(ProductContent Content, DaggerfallLocationAssembly Assembly)> Fixture = new(() =>
    {
        ProductContent content = FullContent(TestData.RepositoryRoot);
        DaggerfallWorldMedia media = DaggerfallWorldMedia.Read(content);
        Lazy<DaggerfallProductMedia> product = new(() => DaggerfallSiteContent.ReadProductMedia(content, media, TestPayload.Definitions));
        Lazy<IReadOnlyDictionary<string, DaggerfallWorldMesh>> meshes = new(() => DaggerfallLocationAssembly.ReadMeshIndex(content, media));
        return (content, new DaggerfallLocationAssembly(TestPayload.Definitions, new DaggerfallWorldBlocks(content), () => product.Value, () => meshes.Value));
    });

    private static DaggerfallSiteProfile Published(string payload) =>
        DaggerfallSiteContent.Read(Fixture.Value.Content, TestContentFiles.Read(Path.Combine(TestData.RepositoryRoot, "content/worldrpg/payloads", payload)), TestPayload.Definitions);

    private static JsonElement Normalized(string closure) =>
        JsonDocument.Parse(TestContentFiles.Read(Path.Combine(TestData.RepositoryRoot, "content/worldrpg/imports", closure, "normalized.json"))).RootElement.GetProperty("world");

    [Fact]
    public void An_assembled_charing_exterior_names_the_published_doors_markers_and_people()
    {
        DaggerfallWorldProfileKey key = DaggerfallWorldProfileIds.Exterior(Charing);
        Assert.True(Fixture.Value.Assembly.Places(key));
        DaggerfallSiteProfile assembled = Fixture.Value.Assembly.Assemble(key);
        DaggerfallSiteProfile published = Published("daggerfall.charing-exterior.json");

        Assert.Equal(published.ProfileKey, assembled.ProfileKey);
        Assert.Equal("17/4/exterior", assembled.ProfileKey.LogicalId);
        AssertSameDoors(published, assembled);
        Assert.All(assembled.Doors, door => Assert.Equal(DaggerfallLockInteractionSurface.Exterior, door.LockSurface));
        Assert.Equal(published.Population.Select(person => person.Id), assembled.Population.Select(person => person.Id));
        foreach ((DaggerfallPopulationPlacement expected, DaggerfallPopulationPlacement actual) in published.Population.Zip(assembled.Population))
        {
            Near(expected.Position.ToVector(), actual.Position.ToVector());
            Assert.Equal(expected with { Position = actual.Position }, actual);
        }

        // The start marker is the assembled arrival; the published closure states the same source marker.
        JsonElement world = Normalized("charing/exterior");
        Near(Vector(world.GetProperty("startMarker").GetProperty("position")), assembled.RequireAnchor("start").Position.ToVector());
        Assert.Empty(assembled.DungeonActions);
        Assert.Null(assembled.DungeonMap);

        // One collision/navigation artifact per placed block, each at its whole-cell grid offset.
        DaggerfallSiteExterior exterior = TestPayload.Definitions.Locations.Records.Single(record => record.Id == Charing).Exterior!;
        Assert.Equal(exterior.Blocks.Count, assembled.Geometry.Spatial.Count);
        Assert.Contains(assembled.Geometry.Spatial, part => part.Id == "block/3/5" && (part.ColumnOffset, part.RowOffset) == (3 * 128, -5 * 128));
        Assert.All(assembled.Geometry.Spatial, part => Assert.StartsWith(DaggerfallWorldBlocks.Root + "/rmb/", part.Path, StringComparison.Ordinal));
        Assert.NotEmpty(assembled.Geometry.Meshes);
        Assert.All(assembled.Geometry.Meshes, mesh => Assert.StartsWith(DaggerfallWorldMedia.Root + "/geometry/mesh-", mesh.Path, StringComparison.Ordinal));
        HashSet<uint> slots = [.. assembled.Materials.Select(material => material.Slot)];
        Assert.All(assembled.Geometry.Meshes, mesh => Assert.All(mesh.Materials!, binding => Assert.Contains(binding.WorldMaterialSlot, slots)));
    }

    /// <summary>The other published exteriors are the exteriors their locations assemble.</summary>
    [Theory]
    [InlineData("daggerfall.the-hawkston-cemetery-exterior.json", "the-hawkston-cemetery/exterior", 17, 0)]
    [InlineData("daggerfall.the-tombs-of-klerd-exterior.json", "the-tombs-of-klerd/exterior", 0, 11)]
    [InlineData("daggerfall.small-ship.json", "small-ship/exterior", 31, 1)]
    [InlineData("daggerfall.large-ship.json", "large-ship/exterior", 31, 2)]
    public void An_assembled_exterior_names_the_published_doors_and_people(string payload, string closure, int region, int index)
    {
        DaggerfallWorldProfileKey key = DaggerfallWorldProfileIds.Exterior(new DaggerfallSiteId(region, index));
        DaggerfallSiteProfile assembled = Fixture.Value.Assembly.Assemble(key);
        DaggerfallSiteProfile published = Published(payload);

        Assert.Equal(published.ProfileKey, assembled.ProfileKey);
        AssertSameDoors(published, assembled);
        Assert.Equal(published.Population.Select(person => person with { Position = default }), assembled.Population.Select(person => person with { Position = default }));
        foreach ((DaggerfallPopulationPlacement expected, DaggerfallPopulationPlacement actual) in published.Population.Zip(assembled.Population))
            Near(expected.Position.ToVector(), actual.Position.ToVector());
        JsonElement world = Normalized(closure);
        if (world.GetProperty("startMarker") is { ValueKind: JsonValueKind.Object } start)
            Near(Vector(start.GetProperty("position")), assembled.RequireAnchor("start").Position.ToVector());
    }

    /// <summary>Each published Charing interior is the building interior its location assembles.</summary>
    [Theory]
    [InlineData(1, 1, 0)]
    [InlineData(2, 1, 0)]
    [InlineData(3, 4, 0)]
    [InlineData(3, 1, 13)]
    [InlineData(1, 5, 17)]
    [InlineData(4, 2, 0)]
    [InlineData(3, 2, 14)]
    public void An_assembled_charing_interior_names_the_published_building_people_and_markers(int x, int y, int building)
    {
        DaggerfallWorldProfileKey key = DaggerfallWorldProfileIds.Interior(Charing, new(x, y, building));
        DaggerfallSiteProfile assembled = Fixture.Value.Assembly.Assemble(key);
        DaggerfallSiteProfile published = Published($"daggerfall.charing-interior-{x}-{y}-{building}.json");

        Assert.Equal(published.ProfileKey, assembled.ProfileKey);
        Assert.Equal(published.InteriorBuilding, assembled.InteriorBuilding);
        Assert.Equal(published.QuestMarkers, assembled.QuestMarkers);
        Assert.Equal(published.StaticNpcs.Select(npc => (npc.Id, npc.Position, npc.Appearance, npc.Role, string.Join(',', npc.Services))),
            assembled.StaticNpcs.Select(npc => (npc.Id, npc.Position, npc.Appearance, npc.Role, string.Join(',', npc.Services))));
        Assert.Equal(published.PropertyContainers.Select(Container), assembled.PropertyContainers.Select(Container));
        Assert.Empty(assembled.Doors);
        // A building interior's arrival is its source entrance, as the published payload states it.
        Near(published.RequireAnchor("start").Position.ToVector(), assembled.RequireAnchor("start").Position.ToVector());
        DaggerfallSiteSpatialPart part = Assert.Single(assembled.Geometry.Spatial);
        Assert.Equal((0L, 0L), (part.ColumnOffset, part.RowOffset));
    }

    /// <summary>Each published dungeon is the dungeon its location assembles, Privateer's Hold among them.</summary>
    [Theory]
    [InlineData("daggerfall.privateers-hold.json", 17, 179)]
    [InlineData("daggerfall.castle-necromoghan.json", 17, 9)]
    [InlineData("daggerfall.the-hawkston-cemetery.json", 17, 0)]
    [InlineData("daggerfall.the-tombs-of-klerd.json", 0, 11)]
    public void An_assembled_dungeon_names_the_published_doors_actions_markers_and_map(string payload, int region, int index)
    {
        DaggerfallSiteId site = new(region, index);
        DaggerfallWorldProfileKey key = DaggerfallWorldProfileIds.Dungeon(site);
        DaggerfallSiteProfile assembled = Fixture.Value.Assembly.Assemble(key);
        DaggerfallSiteProfile published = Published(payload);

        Assert.Equal(published.ProfileKey, assembled.ProfileKey);
        Assert.Equal($"{region}/{index}/dungeon", assembled.ProfileKey.LogicalId);
        AssertSameDoors(published, assembled);
        Assert.All(published.Doors.Zip(assembled.Doors), pair =>
        {
            Near(pair.First.BoundsMin, pair.Second.BoundsMin);
            Near(pair.First.BoundsMax, pair.Second.BoundsMax);
            Assert.Equal(pair.First.Action, pair.Second.Action);
            Assert.NotNull(pair.Second.Visual);
        });
        Assert.Equal(published.DungeonActions.Select(action => action with { SourcePosition = null }),
            assembled.DungeonActions.Select(action => action with { SourcePosition = null }));
        foreach ((DaggerfallDungeonActionDefinition expected, DaggerfallDungeonActionDefinition actual) in published.DungeonActions.Zip(assembled.DungeonActions))
            Assert.Equal(expected.SourcePosition is null, actual.SourcePosition is null);
        Assert.Equal(published.DungeonActionModels.Select(model => (model.ActionId, model.DoorId, model.Description, model.ModelIndex, model.RawIndex)),
            assembled.DungeonActionModels.Select(model => (model.ActionId, model.DoorId, model.Description, model.ModelIndex, model.RawIndex)));
        foreach ((DaggerfallDungeonActionModelDefinition expected, DaggerfallDungeonActionModelDefinition actual) in published.DungeonActionModels.Zip(assembled.DungeonActionModels))
        {
            Near(expected.InitialTransform.Translation, actual.InitialTransform.Translation);
            Near(expected.LocalBoundsMin, actual.LocalBoundsMin);
            Near(expected.LocalBoundsMax, actual.LocalBoundsMax);
            Assert.NotEmpty(actual.CollisionTriangles);
        }

        Assert.Equal(published.Lights.Select(light => light.Id), assembled.Lights.Select(light => light.Id));
        foreach ((DaggerfallSiteLight expected, DaggerfallSiteLight actual) in published.Lights.Zip(assembled.Lights))
        {
            Near(expected.Position.ToVector(), actual.Position.ToVector());
            Assert.Equal((expected.Range, expected.Intensity, expected.Color), (actual.Range, actual.Intensity, actual.Color));
        }

        // Quest spawn and item markers are the dungeon's spawn points.
        Assert.Equal(published.QuestMarkers.Select(marker => marker with { Position = default }), assembled.QuestMarkers.Select(marker => marker with { Position = default }));
        foreach ((DaggerfallSiteMarker expected, DaggerfallSiteMarker actual) in published.QuestMarkers.Zip(assembled.QuestMarkers))
            Near(expected.Position.ToVector(), actual.Position.ToVector());
        Assert.Equal(published.AmbientZones, assembled.AmbientZones);
        DaggerfallDungeonMapContent expectedMap = published.DungeonMap!, actualMap = assembled.DungeonMap!;
        Assert.Equal(expectedMap.DoorIds, actualMap.DoorIds);
        // A published dungeon's authored transitions add portal markers; the source entrance is the block's.
        Assert.Equal(expectedMap.Markers.Where(marker => marker.Kind != DaggerfallSiteMarkerKind.Portal).Select(marker => (marker.Id, marker.Kind, marker.Position)),
            actualMap.Markers.Select(marker => (marker.Id, marker.Kind, marker.Position)));
        Assert.Equal(expectedMap.GeometryPlacements.Select(geometry => geometry.PlacementId), actualMap.GeometryPlacements.Select(geometry => geometry.PlacementId));
        foreach ((DaggerfallDungeonMapGeometry expected, DaggerfallDungeonMapGeometry actual) in expectedMap.GeometryPlacements.Zip(actualMap.GeometryPlacements))
        {
            Near(expected.BoundsMin, actual.BoundsMin);
            Near(expected.BoundsMax, actual.BoundsMax);
        }

        // The arrival is the start block's start marker, which the published payload starts at.
        Near(published.RequireAnchor("start").Position.ToVector(), assembled.RequireAnchor("start").Position.ToVector());
        Assert.Equal(TestPayload.Definitions.Locations.Records.Single(record => record.Id == site).DungeonBlocks.Count, assembled.Geometry.Spatial.Count);
        // The dungeon's texture table redraws its wall archives: no mesh draws an unmapped classic wall archive.
        Assert.DoesNotContain(assembled.Materials, material => material.MaterialResourceId.StartsWith("material/texture-119-", StringComparison.Ordinal));
    }

    [Fact]
    public void A_location_profile_the_catalog_does_not_state_is_not_placed_and_is_refused_by_name()
    {
        DaggerfallLocationAssembly assembly = Fixture.Value.Assembly;
        Assert.False(assembly.Places(DaggerfallWorldProfileIds.Dungeon(Charing)));
        Assert.False(assembly.Places(DaggerfallWorldProfileIds.Interior(Charing, new(0, 0, 99))));
        Assert.False(assembly.Places(new DaggerfallWorldProfileKey(Charing, DaggerfallWorldProfileKind.Exterior, "worldrpg/imports/charing/exterior")));
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => assembly.Assemble(DaggerfallWorldProfileIds.Exterior(new(17, 99999))));
        Assert.Contains("17/99999", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// An authored site pack overrides a generated profile only through the explicit id it states, and that
    /// id must be its own location's profile of its kind; a pack stating none, or another's, is refused by name.
    /// </summary>
    [Fact]
    public void An_authored_site_pack_overrides_only_the_profile_id_it_states_for_its_own_location()
    {
        System.Text.Json.Nodes.JsonObject payload = System.Text.Json.Nodes.JsonNode.Parse(TestContentFiles.Read(
            Path.Combine(TestData.RepositoryRoot, "content/worldrpg/payloads/daggerfall.privateers-hold.json")))!.AsObject();
        DaggerfallSitePayloadHeader header = DaggerfallSiteContent.ReadHeader(System.Text.Encoding.UTF8.GetBytes(payload.ToJsonString()), "daggerfall.privateers-hold");
        Assert.Equal(DaggerfallWorldProfileIds.Dungeon(PrivateersHold), header.Key);

        payload["world"]!["profile"] = "17/4/dungeon";
        DaggerfallContentException other = Assert.Throws<DaggerfallContentException>(() =>
            DaggerfallSiteContent.ReadHeader(System.Text.Encoding.UTF8.GetBytes(payload.ToJsonString()), "daggerfall.privateers-hold"));
        Assert.Contains("'daggerfall.privateers-hold' publishes profile '17/4/dungeon'", other.Message, StringComparison.Ordinal);

        payload["world"]!.AsObject().Remove("profile");
        DaggerfallContentException missing = Assert.Throws<DaggerfallContentException>(() =>
            DaggerfallSiteContent.ReadHeader(System.Text.Encoding.UTF8.GetBytes(payload.ToJsonString()), "daggerfall.privateers-hold"));
        Assert.Contains("'profile'", missing.Message, StringComparison.Ordinal);
    }

    private static void AssertSameDoors(DaggerfallSiteProfile published, DaggerfallSiteProfile assembled)
    {
        Assert.Equal(published.Doors.Select(door => door.Id), assembled.Doors.Select(door => door.Id));
        foreach ((DaggerfallRdbDoorDefinition expected, DaggerfallRdbDoorDefinition actual) in published.Doors.Zip(assembled.Doors))
        {
            Near(expected.Position, actual.Position);
            float turn = MathF.Abs(expected.RotationDegrees.Y - actual.RotationDegrees.Y) % 360F;
            Assert.True(MathF.Min(turn, 360F - turn) < 1e-3F, $"Door '{actual.Id}' turns {actual.RotationDegrees.Y} where the site turns {expected.RotationDegrees.Y}.");
            Assert.Equal((expected.Kind, expected.StartingLockValue, expected.ExteriorBuilding, expected.LockSurface),
                (actual.Kind, actual.StartingLockValue, actual.ExteriorBuilding, actual.LockSurface));
        }
    }

    private static string Container(DaggerfallPropertyContainerPlacement container) =>
        $"{container.Id}@{container.Position}:{string.Join(',', container.ItemGroups)}:{string.Join(';', container.InteractionPoints)}";

    private static Vector3 Vector(JsonElement value) => new(value.GetProperty("x").GetSingle(), value.GetProperty("y").GetSingle(), value.GetProperty("z").GetSingle());

    private static void Near(Vector3 expected, Vector3 actual)
    {
        Assert.InRange(actual.X, expected.X - 1e-3F, expected.X + 1e-3F);
        Assert.InRange(actual.Y, expected.Y - 1e-3F, expected.Y + 1e-3F);
        Assert.InRange(actual.Z, expected.Z - 1e-3F, expected.Z + 1e-3F);
    }
}
