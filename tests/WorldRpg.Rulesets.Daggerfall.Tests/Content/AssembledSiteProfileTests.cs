using System.Numerics;
using System.Text.Json;
using Rusty.Engine;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Travel;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// A location assembled from the catalog and the per-block publication: its identities, doors, markers, people,
/// landings and spawn points, placed through the product-wide meshes and one collision/navigation artifact per block.
/// </summary>
public sealed class AssembledSiteProfileTests
{
    private static readonly Lazy<(ProductContent Content, DaggerfallLocationAssembly Assembly)> Fixture = new(() =>
    {
        ProductContent content = FullContent(TestData.RepositoryRoot);
        DaggerfallWorldMedia media = DaggerfallWorldMedia.Read(TestSessions.Data(content));
        Lazy<DaggerfallProductMedia> product = new(() => DaggerfallSiteContent.ReadProductMedia(TestSessions.Data(content), media, TestPayload.Definitions));
        Lazy<IReadOnlyDictionary<string, DaggerfallWorldMesh>> meshes = new(() => DaggerfallLocationAssembly.ReadMeshIndex(TestSessions.Data(content), media));
        return (content, new DaggerfallLocationAssembly(TestPayload.Definitions, new DaggerfallWorldBlocks(TestSessions.Data(content)), () => product.Value, () => meshes.Value));
    });

    /// <summary>
    /// Privateer's Hold's cave is its enter marker, where a teleport wakes the player facing north and a new game
    /// begins (the base start names the Hold and no pose of its own); walking in through its door lands at its start
    /// marker, the first room.
    /// </summary>
    [Fact]
    public void The_assembled_hold_keeps_the_cave_enter_marker_separate_from_door_arrival()
    {
        DaggerfallSiteProfile assembled = Fixture.Value.Assembly.Assemble(DaggerfallWorldProfileIds.Dungeon(PrivateersHold));
        DaggerfallSiteAnchor enter = assembled.RequireAnchor(DaggerfallLocationAssembly.EnterAnchor);
        DaggerfallSiteAnchor start = assembled.RequireAnchor(DaggerfallLocationAssembly.StartAnchor);
        Near(new Vector3(12.75F, .625F, -41.8F), enter.Position.ToVector());
        Assert.Equal(0F, enter.YawRadians);
        Near(new Vector3(28.375F, 38.975F, -12.4F), start.Position.ToVector());
        Assert.Null(TestPayload.Definitions.NewGame.Start.Pose);
        Assert.Same(enter, DaggerfallSiteCrossing.NewCharacterAnchor(assembled.ProfileKey.Kind, assembled.Anchors));
    }

    [Theory]
    [InlineData("S0000001.RDB")]
    [InlineData("S0000003.RDB")]
    public void Source_dungeons_with_malformed_spawn_markers_assemble_with_usable_ranges(string block)
    {
        var location = TestPayload.Definitions.Locations.Records.First(value => value.DungeonBlocks.Any(value => value.SourceKey == block));
        var profile = Fixture.Value.Assembly.Assemble(DaggerfallWorldProfileIds.Dungeon(location.Id));
        Assert.NotEmpty(profile.Project.Actors.Values);
        Assert.All(profile.Project.Actors.Values, actor =>
        {
            Assert.InRange(actor.SpawnDistance, 0, 6);
            var memory = new WorldRpg.Rulesets.Daggerfall.Modules.Behavior.DaggerfallEnemyPerceptionMemory { SpawnDistance = actor.SpawnDistance };
            Assert.True(WorldRpg.Rulesets.Daggerfall.Modules.Behavior.DaggerfallClassicSpawnRange.Update(memory, new(0, 0, 0), new(0, 0, 0), true));
            Assert.False(WorldRpg.Rulesets.Daggerfall.Modules.Behavior.DaggerfallClassicSpawnRange.Update(memory, new(0, 0, 0), new(100, 0, 0), true));
        });
    }

    [Fact]
    public void Assembled_town_materials_carry_real_winter_textures()
    {
        var seasonal = Fixture.Value.Assembly.Assemble(DaggerfallWorldProfileIds.Exterior(Charing)).Materials.Where(material => material.WinterTexture is not null).ToArray();
        Assert.NotEmpty(seasonal);
        Assert.All(seasonal, material => Assert.NotEqual(material.TexturePath, material.WinterTexture!.Path));
    }

    [Fact]
    public void Town_windows_carry_their_glass_and_a_dungeon_shows_none()
    {
        Assert.Contains(Fixture.Value.Assembly.Assemble(DaggerfallWorldProfileIds.Exterior(Charing)).Materials, material => material.SummerGlass is not null);

        var dungeon = TestPayload.Definitions.Locations.Records.First(site => site.DungeonBlocks.Any());
        Assert.All(Fixture.Value.Assembly.Assemble(DaggerfallWorldProfileIds.Dungeon(dungeon.Id)).Materials,
            material => Assert.Null(material.SummerGlass));
    }

    [Theory]
    [InlineData("SHRIAA00.RMB", true)]
    [InlineData("S0000070.RDB", false)]
    public void Real_source_static_npcs_are_admitted_by_location_assembly(string block, bool exterior)
    {
        var location = TestPayload.Definitions.Locations.Records.First(site => exterior
            ? site.Exterior?.Blocks.Any(value => value.SourceName == block) == true
            : site.DungeonBlocks.Any(value => value.SourceKey == block));
        var key = exterior ? DaggerfallWorldProfileIds.Exterior(location.Id) : DaggerfallWorldProfileIds.Dungeon(location.Id);
        var profile = Fixture.Value.Assembly.Assemble(key);
        Assert.Null(profile.InteriorBuilding);
        Assert.NotEmpty(profile.StaticNpcs);
        Assert.Equal(profile.StaticNpcs.Count, profile.StaticNpcs.Select(person => person.Id).Distinct().Count());
        Assert.All(profile.StaticNpcs, person =>
        {
            Assert.Contains("/", person.Id);
            Assert.Contains("talk", person.Services);
            Assert.NotEmpty(person.Sprite.Frames);
        });
        Assert.Equal(profile.StaticNpcs.Select(person => person.Appearance.NameSeed),
            Fixture.Value.Assembly.Assemble(key).StaticNpcs.Select(person => person.Appearance.NameSeed));
    }

    [Fact]
    public void The_assembled_castle_admits_its_flooded_block_surface()
    {
        var assembled = Fixture.Value.Assembly.Assemble(DaggerfallWorldProfileIds.Dungeon(new(17, 9)));
        var water = Assert.Single(assembled.WaterVolumes);
        Assert.Equal(10F, water.Maximum.Y);
        Assert.NotEqual(0UL, water.Trigger);
        Assert.Equal(water, Assert.Single(Fixture.Value.Assembly.Assemble(assembled.ProfileKey).WaterVolumes));
    }

    [Fact]
    public void An_assembled_charing_exterior_places_its_doors_people_and_blocks()
    {
        DaggerfallWorldProfileKey key = DaggerfallWorldProfileIds.Exterior(Charing);
        Assert.True(Fixture.Value.Assembly.Places(key));
        DaggerfallSiteProfile assembled = Fixture.Value.Assembly.Assemble(key);

        Assert.Equal("17/4/exterior", assembled.ProfileKey.LogicalId);
        Assert.NotEmpty(assembled.Doors);
        Assert.All(assembled.Doors, door => Assert.Equal(DaggerfallLockInteractionSurface.Exterior, door.LockSurface));
        Assert.NotEmpty(assembled.Population);
        // The arrival is the first source start marker of the exterior's blocks.
        Assert.Equal(assembled.RequireAnchor(DaggerfallLocationAssembly.StartMarkerAnchor(0)).Position, assembled.RequireAnchor("start").Position);
        Assert.Empty(assembled.DungeonActions);
        Assert.Null(assembled.DungeonMap);

        // One collision/navigation artifact per placed block, each at its whole-cell grid offset.
        DaggerfallSiteExterior exterior = TestPayload.Definitions.Locations.Records.Single(record => record.Id == Charing).Exterior!;
        Assert.Equal(exterior.Blocks.Count, assembled.Geometry.Spatial.Count);
        Assert.Contains(assembled.Geometry.Spatial, part => part.Id == "block/3/5" && (part.ColumnOffset, part.RowOffset) == (3 * 128, -5 * 128));
        Assert.All(assembled.Geometry.Spatial, part => Assert.StartsWith(DaggerfallWorldBlocks.Root + "/rmb/", part.Path, StringComparison.Ordinal));
        Assert.NotEmpty(assembled.Geometry.Meshes);
        // Each model draws its world media mesh, or the trimmed mesh of it when an earlier placement covers some of its faces.
        Assert.All(assembled.Geometry.Meshes, mesh => Assert.True(
            mesh.Path.StartsWith(DaggerfallWorldMedia.Root + "/geometry/mesh-", StringComparison.Ordinal)
                || mesh.Path.StartsWith(DaggerfallWorldMedia.Root + "/geometry/placement/", StringComparison.Ordinal), mesh.Path));
        Assert.Contains(assembled.Geometry.Meshes, mesh => mesh.Path.StartsWith(DaggerfallWorldMedia.Root + "/geometry/placement/", StringComparison.Ordinal));
        HashSet<uint> slots = [.. assembled.Materials.Select(material => material.Slot)];
        Assert.All(assembled.Geometry.Meshes, mesh => Assert.All(mesh.Materials!, binding => Assert.Contains(binding.WorldMaterialSlot, slots)));
    }

    /// <summary>
    /// Travel lands at a city's source start marker nearest the side it arrives from: each side of Charing at its own
    /// gate, on the street, never the town start's authored point beside a building.
    /// </summary>
    [Fact]
    public void Travel_lands_at_charing_s_gates_on_the_street()
    {
        DaggerfallSiteRecord site = TestPayload.Definitions.Locations.Records.Single(record => record.Id == Charing);
        DaggerfallSiteProfile assembled = Fixture.Value.Assembly.Assemble(DaggerfallWorldProfileIds.Exterior(Charing));
        WorldPoint[] gates = [.. assembled.Anchors.Values.Where(anchor => anchor.Id.StartsWith(DaggerfallLocationAssembly.StartMarkerAnchorPrefix, StringComparison.Ordinal))
            .Select(anchor => anchor.Position)];
        Assert.Equal(4, gates.Length);
        Assert.All(gates, gate => Assert.Equal(.1F, gate.Y, 3));

        DaggerfallSiteAnchor[] landings = [.. Enum.GetValues<DaggerfallArrivalSide>().Select(side => DaggerfallLocationArrival.Landing(assembled, site, side))];
        Assert.All(landings, landing => Assert.Contains(landing.Position, gates));
        Assert.Equal(4, landings.Select(landing => landing.Position).Distinct().Count());
    }

    [Fact]
    public void A_location_profile_the_catalog_does_not_state_is_not_placed_and_is_refused_by_name()
    {
        DaggerfallLocationAssembly assembly = Fixture.Value.Assembly;
        Assert.False(assembly.Places(DaggerfallWorldProfileIds.Dungeon(Charing)));
        Assert.False(assembly.Places(DaggerfallWorldProfileIds.Interior(Charing, new(0, 0, 99))));
        Assert.False(assembly.Places(new DaggerfallWorldProfileKey(Charing, DaggerfallWorldProfileKind.Exterior, "charing/exterior")));
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => assembly.Assemble(DaggerfallWorldProfileIds.Exterior(new(17, 99999))));
        Assert.Contains("17/99999", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every source door that changes worlds is a portal both ways: an assembled interior's building doors lead
    /// out to its location's exterior, landing in front of that building's door; an exterior's dungeon entrance
    /// leads in to the dungeon's start, which faces away from the exit nearest it; and the dungeon's exit leads
    /// out in front of the lowest entrance. Each landing exists in the profile it names.
    /// </summary>
    [Fact]
    public void Source_transition_doors_are_portals_both_ways_with_their_landings()
    {
        DaggerfallLocationAssembly assembly = Fixture.Value.Assembly;
        DaggerfallWorldProfileKey interiorKey = DaggerfallWorldProfileIds.Interior(Charing, new(3, 4, 0));
        DaggerfallSiteProfile interior = assembly.Assemble(interiorKey), outside = assembly.Assemble(DaggerfallWorldProfileIds.Exterior(Charing));
        Assert.Equal(3, interior.Portals.Count);
        Assert.All(interior.Portals, portal =>
        {
            Assert.Equal("17/4/exterior", portal.DestinationLogicalProfile);
            Assert.Equal("building/3/4/0", portal.ArrivalAnchor);
        });
        // The landing stands on the door's threshold, in front of it and facing out.
        DaggerfallRdbDoorDefinition door = outside.Doors.First(candidate => candidate.ExteriorBuilding == new DaggerfallSiteBuildingId(3, 4, 0));
        DaggerfallSiteAnchor landing = outside.RequireAnchor("building/3/4/0");
        Assert.Equal(door.Position.Y + door.BoundsMin.Y, landing.Position.Y, 3);
        Assert.Equal(.75F, Vector2.Distance(new(door.Position.X, door.Position.Z), new(landing.Position.X, landing.Position.Z)), 3);
        // No exterior door leads into a dungeon Charing does not have.
        Assert.Empty(outside.Portals);

        DaggerfallWorldProfileKey klerd = DaggerfallWorldProfileIds.Exterior(new(0, 11)), crypt = DaggerfallWorldProfileIds.Dungeon(new(0, 11));
        DaggerfallSiteProfile exterior = assembly.Assemble(klerd), dungeon = assembly.Assemble(crypt);
        DaggerfallSitePortal entrance = Assert.Single(exterior.Portals);
        Assert.Equal((crypt.LogicalId, "start"), (entrance.DestinationLogicalProfile, entrance.ArrivalAnchor));
        DaggerfallSitePortal exit = Assert.Single(dungeon.Portals);
        Assert.Equal((klerd.LogicalId, "dungeon-entrance"), (exit.DestinationLogicalProfile, exit.ArrivalAnchor));
        DaggerfallSiteAnchor outOfCrypt = exterior.RequireAnchor("dungeon-entrance");
        Assert.Equal(.35F, Vector2.Distance(new(entrance.Position.X, entrance.Position.Z), new(outOfCrypt.Position.X, outOfCrypt.Position.Z)), 3);
        Assert.True(outOfCrypt.Position.Y < entrance.Position.Y, "The landing stands on the entrance's threshold, below its centre.");
        DaggerfallSiteAnchor start = dungeon.RequireAnchor("start");
        Assert.Equal(dungeon.Project.PlayerPosition, start.Position);
        Assert.Equal(dungeon.InitialLook.YawRadians, start.YawRadians);
        // Facing away from the exit: the exit lies behind the start.
        Vector3 facing = WorldRpg.Kit.Actors.ActorHeading.Forward(start.YawRadians);
        Assert.True(Vector3.Dot(facing, exit.Position.ToVector() - start.Position.ToVector()) < 0F);

        // Each portal is on its door plane and names the side it is used from, so interaction sights it off the
        // wall it is set into: the entrance from where its landing stands, the exits from inside.
        Assert.All(interior.Portals.Append(entrance).Append(exit), portal => Assert.NotNull(portal.Normal));
        Assert.True(Vector3.Dot(outOfCrypt.Position.ToVector() - entrance.Position.ToVector(), entrance.Normal!.Value) > 0F);
        Assert.True(Vector3.Dot(start.Position.ToVector() - exit.Position.ToVector(), exit.Normal!.Value) > 0F);
    }

    private static void Near(Vector3 expected, Vector3 actual)
    {
        Assert.InRange(actual.X, expected.X - 1e-3F, expected.X + 1e-3F);
        Assert.InRange(actual.Y, expected.Y - 1e-3F, expected.Y + 1e-3F);
        Assert.InRange(actual.Z, expected.Z - 1e-3F, expected.Z + 1e-3F);
    }
}
