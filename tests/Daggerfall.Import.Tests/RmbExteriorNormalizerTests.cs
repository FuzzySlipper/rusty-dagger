using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalization;
using Daggerfall.Import.Normalized;
using Xunit;

namespace Daggerfall.Import.Tests;

public sealed class RmbExteriorNormalizerTests
{
    [CorpusFact]
    public void A_towns_ground_plane_collides_and_carries_navigation_but_never_draws_over_the_walked_terrain()
    {
        RmbExteriorNormalizationResult exterior = RmbExteriorNormalizer.Normalize(new(Sources(), 17, "Charing", RmbWorldProfileKind.Exterior)
        {
            Navigation = NavigationDerivationConfig.ClassicDefault with { CellSize = 10F },
        });
        NormalizedMesh[] ground = [.. exterior.Document.Meshes.Where(mesh => mesh.MaterialGroups.All(group => group.CollisionOnly))];
        Assert.NotEmpty(ground);
        Assert.All(ground, mesh => Assert.Contains("/ground-", mesh.Id, StringComparison.Ordinal));
        Assert.All(ground, mesh => Assert.All(mesh.MaterialGroups, group => Assert.True(group.ParticipatesInCollision)));
        int all = exterior.Document.Meshes.Sum(mesh => mesh.Triangles.Count), plane = ground.Sum(mesh => mesh.Triangles.Count);

        using EngineBinaryContent.StaticMesh drawn = EngineBinaryContent.ReadStaticMesh(exterior.SpatialPublication.StaticMesh.Bytes);
        Assert.True(plane > 0 && drawn.Indices.Length / 3 <= all - plane, $"{drawn.Indices.Length / 3} drawn of {all}, {plane} of them ground.");
        EngineBinaryContent.SpatialArtifact spatial = EngineBinaryContent.ReadSpatial(exterior.SpatialPublication.CollisionNavigation.Bytes.Span);
        Assert.True(spatial.Triangles.Length >= plane, $"{spatial.Triangles.Length} collision triangles for {plane} of ground.");
        Assert.NotEmpty(exterior.SpatialPublication.Navigation.Cells);
    }

    [CorpusFact]
    public void Charing_exterior_and_selected_real_building_interior_publish_nonempty_static_collision_navigation_closures()
    {
        DungeonLogicalSourceSet sources = Sources();
        RmbExteriorNormalizationResult exterior = RmbExteriorNormalizer.Normalize(new(sources, 17, "Charing", RmbWorldProfileKind.Exterior)
        {
            // This assertion is about source assembly, not a full-world navigation fidelity benchmark.
            Navigation = NavigationDerivationConfig.ClassicDefault with { CellSize = 10F },
        });
        RmbExteriorNormalizationResult exteriorAgain = RmbExteriorNormalizer.Normalize(new(sources, 17, "Charing", RmbWorldProfileKind.Exterior)
        {
            Navigation = NavigationDerivationConfig.ClassicDefault with { CellSize = 10F },
        });
        RmbExteriorNormalizationResult interior = RmbExteriorNormalizer.Normalize(new(sources, 17, "Charing", RmbWorldProfileKind.Interior)
        {
            Building = new RmbBuildingSelection(1, 1, 0),
            Navigation = NavigationDerivationConfig.ClassicDefault with { CellSize = 2F },
        });

        Assert.Equal((6, 7, 42), (exterior.Layout.Width, exterior.Layout.Height, exterior.Layout.Blocks.Count));
        Assert.True(exterior.Document.Meshes.Count > 100);
        Assert.Equal(0F, exterior.Document.Bounds.Minimum.X);
        Assert.Equal(0F, exterior.Document.Bounds.Maximum.Z);
        Assert.Equal(614.4F, exterior.Document.Bounds.Maximum.X);
        Assert.Contains(exterior.Document.Meshes.SelectMany(mesh => mesh.Vertices), point => point.X == 0F && point.Y == 0F && point.Z == 0F);
        Assert.Contains(exterior.Document.Meshes.SelectMany(mesh => mesh.Vertices), point => point.X == 614.4F && point.Y == 0F && point.Z == -614.4F);
        Assert.NotEmpty(exterior.SpatialPublication.Navigation.Cells);
        Assert.Contains(exterior.SpatialPublication.Navigation.Cells, cell => cell.SupportHeight == 0F);
        Assert.DoesNotContain(exterior.Document.World.Population, person => person.Id.Contains("/dynamic/", StringComparison.Ordinal));
        Assert.NotEmpty(exterior.Document.World.ClearOutdoorCells);
        Assert.NotEmpty(exterior.Document.World.Gates);
        Assert.Equal(exterior.SpatialPublication.StaticMesh.Bytes.ToArray(), exteriorAgain.SpatialPublication.StaticMesh.Bytes.ToArray());
        Assert.Equal(exterior.SpatialPublication.CollisionNavigation.Bytes.ToArray(), exteriorAgain.SpatialPublication.CollisionNavigation.Bytes.ToArray());
        Assert.Equal("RESIAL05.RMB", interior.Layout.Blocks.Single(block => block.X == 1 && block.Y == 1).SourceName);
        Assert.Equal(new RmbBuildingSelection(1, 1, 0), interior.Building);
        Assert.Null(exterior.Document.World.InteriorBuilding);
        Assert.NotEmpty(exterior.Document.World.Doors);
        foreach (NormalizedDoorPlacement door in exterior.Document.World.Doors)
        {
            NormalizedBounds collision = Assert.IsType<NormalizedBounds>(door.CollisionBounds);
            Assert.True(collision.Maximum.X > collision.Minimum.X);
            Assert.True(collision.Maximum.Y > collision.Minimum.Y);
            Assert.True(collision.Maximum.Z > collision.Minimum.Z);
            Assert.Equal(-collision.Minimum.X, collision.Maximum.X);
            Assert.Equal(-collision.Minimum.Y, collision.Maximum.Y);
            Assert.Equal(-collision.Minimum.Z, collision.Maximum.Z);
        }
        Assert.Contains(exterior.Document.World.Doors, door => door.Id.Contains("/1/1/", StringComparison.Ordinal)
            && door.ExteriorBuildingIndex == 0);
        NormalizedInteriorBuilding building = Assert.IsType<NormalizedInteriorBuilding>(interior.Document.World.InteriorBuilding);
        Assert.Equal((1, 1, "RESIAL05.RMB", 0), (building.BlockX, building.BlockY, building.SourceKey, building.BuildingIndex));
        BsaArchive blockArchive = BsaArchive.Parse(sources.Require("BLOCKS.BSA").Bytes.Span, sources.Require("BLOCKS.BSA").Label);
        // The source slot selected for geometry must also supply the published access facts.
        Assert.True(blockArchive.TryGetByName(building.SourceKey, out BsaRecord? sourceRecord));
        byte[] sourceBytes = blockArchive.GetPayload(sourceRecord!).ToArray();
        Assert.True(RmbBlockSummaryReader.TryRead(sourceBytes, blockArchive.Source, 0, sourceBytes.Length, out RmbBlockSummary? summary, out _));
        Assert.Equal((summary!.Buildings[0].BuildingType, summary.Buildings[0].FactionId), ((byte)building.BuildingType, (ushort)building.FactionId));
        RmbBlockPlacements rawPlacements = RmbPlacementReader.Read(sourceBytes, 0, summary!, blockArchive.Source);
        var expectedMarkers = rawPlacements.Buildings[0].Interior.Flats.Select((flat, ordinal) => (flat, ordinal))
            .Where(value => value.flat.TextureArchive == 199 && value.flat.TextureRecord is 11 or 18).ToArray();
        Assert.Equal(expectedMarkers.Select(value => value.ordinal), interior.Document.World.QuestMarkers.Select(value => value.SourceOrdinal));
        Assert.Empty(exterior.Document.World.QuestMarkers);
        Assert.Empty(exterior.Document.World.PropertyContainers);
        Assert.NotEmpty(interior.Document.World.PropertyContainers);
        foreach (var container in interior.Document.World.PropertyContainers)
        {
            int ordinal = int.Parse(container.Id.Split('/')[1]);
            var model = rawPlacements.Buildings[0].Interior.Models[ordinal];
            Assert.Equal(new NormalizedVector3(model.X * .025f, -model.Y * .025f, -model.Z * .025f), container.Position);
            Assert.NotEmpty(container.ItemGroups);
            Assert.NotEmpty(container.InteractionPoints);
            Assert.DoesNotContain(container.Position, container.InteractionPoints);
        }
        foreach (var (raw, published) in expectedMarkers.Zip(interior.Document.World.QuestMarkers))
        {
            Assert.Equal(new NormalizedVector3(raw.flat.X * .025f, -raw.flat.Y * .025f, -raw.flat.Z * .025f), published.Position);
            Assert.Equal(raw.flat.TextureRecord == 11 ? NormalizedQuestMarkerKind.Spawn : NormalizedQuestMarkerKind.Item, published.Kind);
        }
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(building), System.Text.Json.JsonSerializer.Serialize(
            NormalizedImportSerializer.Deserialize(NormalizedImportSerializer.Serialize(interior.Document)).World.InteriorBuilding));
        IReadOnlyList<RmbPeoplePlacement> people = RmbPlacementReader.Read(sourceBytes, 0, summary, blockArchive.Source).Buildings[0].Interior.People;
        Assert.Equal(people.Count, interior.Document.World.StaticNpcs.Count);
        foreach ((RmbPeoplePlacement person, int ordinal) in people.Select((person, ordinal) => (person, ordinal)))
        {
            NormalizedStaticNpcPlacement npc = Assert.Single(interior.Document.World.StaticNpcs, npc => npc.Id == $"person/{ordinal}");
            Assert.Equal((person.TextureArchive, person.TextureRecord, person.FactionId), (npc.BillboardArchive, npc.BillboardRecord, npc.FactionId));
            Assert.Equal((person.Flags & 32) != 0 ? "Female" : "Male", npc.Gender);
            Assert.Equal(person.SourceOffset ^ (((1 << 16) + (1 << 8)) + interior.Layout.LocationIndex), npc.NameSeed);
        }
        Assert.NotEmpty(interior.Document.Meshes);
        Assert.NotEmpty(interior.SpatialPublication.Navigation.Cells);
        Assert.Equal(new NormalizedMarker("marker/enter", new NormalizedVector3(8F, 0F, 4.8F)), interior.Document.World.EnterMarker);
        Assert.All([exterior, interior], profile =>
        {
            Assert.True(profile.SpatialPublication.StaticMesh.Bytes.Length > 0);
            Assert.True(profile.SpatialPublication.CollisionNavigation.Bytes.Length > 0);
            Assert.NotEmpty(profile.SpatialPublication.MaterialSlots);
            profile.Validate();
        });
    }

    [CorpusFact]
    public void Marker_bearing_city_interior_publishes_the_same_source_points_as_the_compact_catalog()
    {
        DungeonLogicalSourceSet sources = Sources();
        BsaArchive maps = BsaArchive.Parse(sources.Require("MAPS.BSA").Bytes.Span, "arena2/MAPS.BSA");
        BsaArchive blocks = BsaArchive.Parse(sources.Require("BLOCKS.BSA").Bytes.Span, "arena2/BLOCKS.BSA");
        DaggerfallBlocks catalog = DaggerfallBlocksBuilder.Build(sources.Require("BLOCKS.BSA").Bytes.ToArray(), "arena2/BLOCKS.BSA",
            Daggerfall.Import.Publication.SourceManifestBuilder.ReadInventory(File.ReadAllBytes(Path.Combine(TestData.RepositoryRoot, "data/content-source-manifest.csv"))));
        DaggerfallBlockBuildingSet compact = DaggerfallBlockBuildingSet.From(catalog);
        MapsExteriorLayout layout = MapsDecoder.DecodeExteriorLayout(maps, 17, "Charing");
        var selection = layout.Blocks.SelectMany(block => compact.QuestMarkers
            .Where(set => set.SourceKey == block.SourceName && set.BuildingIndex is not null && set.Markers.Count > 0)
            .Select(set => (Block: block, Set: set))).First();
        RmbExteriorNormalizationResult interior = RmbExteriorNormalizer.Normalize(new(sources, 17, "Charing", RmbWorldProfileKind.Interior)
        {
            Building = new(selection.Block.X, selection.Block.Y, selection.Set.BuildingIndex!.Value),
            Navigation = NavigationDerivationConfig.ClassicDefault with { CellSize = 2F },
        });
        Assert.Equal(selection.Set.Markers, interior.Document.World.QuestMarkers);
        Assert.Equal(interior.Document.World.QuestMarkers,
            NormalizedImportSerializer.Deserialize(NormalizedImportSerializer.Serialize(interior.Document)).World.QuestMarkers);
    }

    [CorpusFact]
    public void Provider_interiors_retain_their_real_building_and_source_people()
    {
        DungeonLogicalSourceSet sources = Sources();
        (int Region, string Location, int LocationIndex, RmbBuildingSelection Selection, string SourceKey, byte Type, ushort BuildingFaction, int[] ProviderFactions)[] cases =
        [
            (17, "Charing", 4, new(3, 4, 0), "MAGEAA14.RMB", 11, 40, [60, 64]),
            (17, "Charing", 4, new(3, 1, 13), "TEMPAAH0.RMB", 14, 35, [254, 496, 497, 498, 810, 813]),
            (17, "Charing", 4, new(1, 5, 17), "BANKAL01.RMB", 3, 0, [510]),
            (0, "Berbaaqnia", 15, new(5, 2, 17), "MAGEBA01.RMB", 11, 40, [60, 64]),
            (0, "Berbaaqnia", 15, new(4, 4, 12), "BANKBL00.RMB", 3, 510, [510]),
            (0, "Bubyrydata", 13, new(3, 1, 13), "TEMPAAH0.RMB", 14, 35, [254, 496, 497, 498, 810, 813]),
        ];

        foreach (var item in cases)
        {
            RmbExteriorNormalizationResult result = RmbExteriorNormalizer.Normalize(new(sources, item.Region, item.Location, RmbWorldProfileKind.Interior)
            {
                LocationIndex = item.LocationIndex,
                Building = item.Selection,
                Navigation = NavigationDerivationConfig.ClassicDefault with { CellSize = 2F },
            });

            NormalizedInteriorBuilding building = Assert.IsType<NormalizedInteriorBuilding>(result.Document.World.InteriorBuilding);
            Assert.Equal((item.Selection.BlockX, item.Selection.BlockY, item.SourceKey, item.Selection.BuildingIndex,
                item.Type, item.BuildingFaction),
                (building.BlockX, building.BlockY, building.SourceKey, building.BuildingIndex,
                    building.BuildingType, building.FactionId));
            Assert.NotEmpty(result.Document.World.StaticNpcs);
            Assert.All(item.ProviderFactions, faction =>
                Assert.Contains(result.Document.World.StaticNpcs, person => person.FactionId == faction));
        }
    }

    [CorpusFact]
    public void Duplicate_Your_Ship_names_publish_unique_source_worlds_and_real_start_markers()
    {
        DungeonLogicalSourceSet sources = Sources();
        var small = RmbExteriorNormalizer.Normalize(new(sources, 31, "Your Ship", RmbWorldProfileKind.Exterior)
        { LocationIndex = 1, Navigation = NavigationDerivationConfig.ClassicDefault with { CellSize = 1F } });
        var large = RmbExteriorNormalizer.Normalize(new(sources, 31, "Your Ship", RmbWorldProfileKind.Exterior)
        { LocationIndex = 2, Navigation = NavigationDerivationConfig.ClassicDefault with { CellSize = 1F } });
        Assert.Equal(1, small.Layout.LocationIndex);
        Assert.Equal(2, large.Layout.LocationIndex);
        Assert.NotEqual(small.Document.World.VisualMeshAssetId, large.Document.World.VisualMeshAssetId);
        Assert.NotEqual(small.SpatialPublication.CollisionNavigation.Bytes.ToArray(), large.SpatialPublication.CollisionNavigation.Bytes.ToArray());
        Assert.Equal(new NormalizedVector3(106.2F, 9.575F, 31.400005F), small.Document.World.StartMarker!.Position);
        Assert.Equal(new NormalizedVector3(37.775F, 11.3F, -31.675003F), large.Document.World.StartMarker!.Position);
    }

    [CorpusFact]
    public void The_cemeterys_overlapping_hedge_pieces_draw_each_patch_of_hedge_once()
    {
        // The cemetery's hedge runs are short and long pieces laid over each other along one line; merged as
        // placed, two faces of a hedge filled the same pixels with different texture offsets and shimmered.
        RmbExteriorNormalizationResult exterior = RmbExteriorNormalizer.Normalize(new(Sources(), 17, "The Hawkston Cemetery", RmbWorldProfileKind.Exterior));
        List<NormalizedVector3[]> drawn = [];
        foreach (NormalizedMesh mesh in exterior.Document.Meshes.Where(mesh => mesh.MaterialGroups.All(group => !group.CollisionOnly)))
            drawn.AddRange(mesh.Triangles.Select(triangle =>
                new[] { mesh.Vertices[triangle.FirstVertex], mesh.Vertices[triangle.SecondVertex], mesh.Vertices[triangle.ThirdVertex] }));
        Assert.Contains(exterior.Document.Meshes, mesh => mesh.Id.EndsWith("/texture-33-0", StringComparison.Ordinal));

        AssertNoOverlaps(drawn);
    }

    /// <summary>Refuses two drawn triangles sharing more than an edge in one plane and facing.</summary>
    internal static void AssertNoOverlaps(IEnumerable<NormalizedVector3[]> drawn)
    {
        List<string> overlaps = [];
        foreach (IGrouping<(int, int, int, int), NormalizedVector3[]> plane in drawn.Where(triangle => Normal(triangle) != default).GroupBy(PlaneKey))
        {
            NormalizedVector3[][] triangles = [.. plane];
            for (int first = 0; first < triangles.Length; first++)
                for (int second = first + 1; second < triangles.Length; second++)
                    if (Overlap(triangles[first], triangles[second], Normal(triangles[first])))
                        overlaps.Add(string.Join(" ", triangles[first]) + " | " + string.Join(" ", triangles[second]));
        }
        Assert.True(overlaps.Count == 0, $"{overlaps.Count} overlapping coplanar pairs, first: {overlaps.FirstOrDefault()}");
    }

    internal static (int, int, int, int) PlaneKey(NormalizedVector3[] triangle)
    {
        NormalizedVector3 normal = Normal(triangle);
        return ((int)MathF.Round(normal.X * 100F), (int)MathF.Round(normal.Y * 100F), (int)MathF.Round(normal.Z * 100F),
            (int)MathF.Round(((normal.X * triangle[0].X) + (normal.Y * triangle[0].Y) + (normal.Z * triangle[0].Z)) * 100F));
    }

    internal static NormalizedVector3 Normal(NormalizedVector3[] triangle)
    {
        NormalizedVector3 a = new(triangle[1].X - triangle[0].X, triangle[1].Y - triangle[0].Y, triangle[1].Z - triangle[0].Z);
        NormalizedVector3 b = new(triangle[2].X - triangle[0].X, triangle[2].Y - triangle[0].Y, triangle[2].Z - triangle[0].Z);
        NormalizedVector3 cross = new((a.Y * b.Z) - (a.Z * b.Y), (a.Z * b.X) - (a.X * b.Z), (a.X * b.Y) - (a.Y * b.X));
        float length = MathF.Sqrt((cross.X * cross.X) + (cross.Y * cross.Y) + (cross.Z * cross.Z));
        return length > 1E-6F ? new(cross.X / length, cross.Y / length, cross.Z / length) : default;
    }

    /// <summary>Whether two triangles in one plane share more than an edge, by the separating edges of either.</summary>
    internal static bool Overlap(NormalizedVector3[] first, NormalizedVector3[] second, NormalizedVector3 normal)
    {
        int drop = MathF.Abs(normal.X) > MathF.Abs(normal.Y) ? (MathF.Abs(normal.X) > MathF.Abs(normal.Z) ? 0 : 2) : (MathF.Abs(normal.Y) > MathF.Abs(normal.Z) ? 1 : 2);
        (float, float) Flat(NormalizedVector3 point) => drop switch { 0 => (point.Y, point.Z), 1 => (point.X, point.Z), _ => (point.X, point.Y) };
        (float X, float Y)[] a = [.. first.Select(Flat)], b = [.. second.Select(Flat)];
        foreach ((float X, float Y)[] triangle in new[] { a, b })
            for (int edge = 0; edge < 3; edge++)
            {
                (float x0, float y0) = triangle[edge];
                (float x1, float y1) = triangle[(edge + 1) % 3];
                (float nx, float ny) = (y0 - y1, x1 - x0);
                float length = MathF.Sqrt((nx * nx) + (ny * ny));
                if (length < 1E-6F) continue;
                float[] pa = [.. a.Select(p => ((p.X * nx) + (p.Y * ny)) / length)], pb = [.. b.Select(p => ((p.X * nx) + (p.Y * ny)) / length)];
                if (pa.Max() <= pb.Min() + 0.002F || pb.Max() <= pa.Min() + 0.002F) return false;
            }
        return true;
    }

    internal static DungeonLogicalSourceSet Sources()
    {
        string arena2 = TestData.CorpusRoot;
        return new DungeonLogicalSourceSet(Directory.EnumerateFiles(arena2)
            .Where(path => Path.GetFileName(path) is "MAPS.BSA" or "BLOCKS.BSA" or "ARCH3D.BSA" or "CLIMATE.PAK" or "FACTION.TXT"
                || Path.GetFileName(path).StartsWith("TEXTURE.", StringComparison.Ordinal))
            .Select(path => new DungeonLogicalSource($"arena2/{Path.GetFileName(path)}", File.ReadAllBytes(path))));
    }
}
