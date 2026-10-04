using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalization;
using Daggerfall.Import.Normalized;
using Xunit;

namespace Daggerfall.Import.Tests;

public sealed class RmbExteriorNormalizerTests
{
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
        Assert.Equal(exterior.SpatialPublication.StaticMesh.Bytes.ToArray(), exteriorAgain.SpatialPublication.StaticMesh.Bytes.ToArray());
        Assert.Equal(exterior.SpatialPublication.CollisionNavigation.Bytes.ToArray(), exteriorAgain.SpatialPublication.CollisionNavigation.Bytes.ToArray());
        Assert.Equal("RESIAL05.RMB", interior.Layout.Blocks.Single(block => block.X == 1 && block.Y == 1).SourceName);
        Assert.Equal(new RmbBuildingSelection(1, 1, 0), interior.Building);
        Assert.Null(exterior.Document.World.InteriorBuilding);
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
        foreach (var (raw, published) in expectedMarkers.Zip(interior.Document.World.QuestMarkers))
        {
            Assert.Equal(new NormalizedVector3(raw.flat.X * .025f, -raw.flat.Y * .025f, -raw.flat.Z * .025f), published.Position);
            Assert.Equal(raw.flat.TextureRecord == 11 ? NormalizedQuestMarkerKind.Spawn : NormalizedQuestMarkerKind.Item, published.Kind);
        }
        Assert.Equal(building, NormalizedImportSerializer.Deserialize(NormalizedImportSerializer.Serialize(interior.Document)).World.InteriorBuilding);
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

    private static DungeonLogicalSourceSet Sources()
    {
        string arena2 = TestData.CorpusRoot;
        return new DungeonLogicalSourceSet(Directory.EnumerateFiles(arena2)
            .Where(path => Path.GetFileName(path) is "MAPS.BSA" or "BLOCKS.BSA" or "ARCH3D.BSA" or "CLIMATE.PAK" or "FACTION.TXT"
                || Path.GetFileName(path).StartsWith("TEXTURE.", StringComparison.Ordinal))
            .Select(path => new DungeonLogicalSource($"arena2/{Path.GetFileName(path)}", File.ReadAllBytes(path))));
    }
}
