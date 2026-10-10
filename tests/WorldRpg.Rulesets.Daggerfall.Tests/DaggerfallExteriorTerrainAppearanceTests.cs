using System.Numerics;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallExteriorTerrainAppearanceTests
{
    [Fact]
    public void Donor_tile_uv_flags_flip_the_vertical_axis_after_rotation()
    {
        AssertUv(new(0.2F, 0.3F), false, false, new(0.2F, 0.3F));
        AssertUv(new(0.2F, 0.3F), false, true, new(0.2F, 0.7F));
        AssertUv(new(0.2F, 0.3F), true, false, new(0.3F, 0.8F));
        AssertUv(new(0.2F, 0.3F), true, true, new(0.3F, 0.2F));
    }

    [Fact]
    public void Reconcile_builds_normals_mesh_groups_and_stable_cell_facts()
    {
        GraphicsDouble graphics = new();
        using DaggerfallExteriorTerrainAppearance appearance = new(graphics);
        DaggerfallExteriorWorldOrigin origin = new(10, 20, new Vector3(2F, 3F, 4F));

        appearance.Reconcile(
            [new DaggerfallExteriorCellId(12, 20), new DaggerfallExteriorCellId(11, 20)],
            origin,
            Surface);

        Assert.Equal(2, appearance.ActiveCellCount);
        Assert.Equal(2, graphics.MeshRequests.Count);
        MeshResourceCreateRequest request = graphics.MeshRequests[0];
        Assert.Equal(3, request.Positions.Length);
        Assert.Equal(3, request.Normals.Length);
        Assert.Equal(3, request.Uvs.Length);
        Assert.Equal(3, request.Indices.Length);
        Assert.Equal(3U, request.Groups.Span[0].Count);
        Assert.Equal(new Vector3(0F, 1F, 0F), request.Normals.Span[0]);

        AppearanceFact[] facts = appearance.BuildFacts();
        Assert.Equal(2, facts.Length);
        Assert.Equal(DaggerfallExteriorTerrainAppearance.ObjectId(new(11, 20)), facts[0].ObjectId);
        Assert.Equal(DaggerfallExteriorTerrainAppearance.ObjectId(new(12, 20)), facts[1].ObjectId);
        Assert.Equal(new Vector3(2F + DaggerfallExteriorCellResidency.CellSize, 3F, 4F), facts[0].Transform.Translation);
        Assert.Equal(new Vector3(2F + (2F * DaggerfallExteriorCellResidency.CellSize), 3F, 4F), facts[1].Transform.Translation);
    }

    [Fact]
    public void Origin_reprojection_reuses_retained_visual_resources()
    {
        GraphicsDouble graphics = new();
        using DaggerfallExteriorTerrainAppearance appearance = new(graphics);
        DaggerfallExteriorCellId cell = new(10, 20);

        appearance.Reconcile([cell], DaggerfallExteriorWorldOrigin.At(cell), Surface);
        AppearanceFact first = Assert.Single(appearance.BuildFacts());

        appearance.Reconcile(
            [cell],
            new DaggerfallExteriorWorldOrigin(11, 20, new Vector3(2F, 3F, 4F)),
            Surface);
        AppearanceFact second = Assert.Single(appearance.BuildFacts());

        Assert.Same(first.Appearance, second.Appearance);
        Assert.Single(graphics.MeshRequests);
        Assert.Equal(
            new Vector3(2F - DaggerfallExteriorCellResidency.CellSize, 3F, 4F),
            second.Transform.Translation);
    }

    [Fact]
    public void Retired_resources_are_released_only_after_the_replacement_snapshot_is_accepted()
    {
        GraphicsDouble graphics = new();
        using DaggerfallExteriorTerrainAppearance appearance = new(graphics);
        DaggerfallExteriorCellId cell = new(10, 20);
        appearance.Reconcile([cell], DaggerfallExteriorWorldOrigin.At(cell), Surface);
        AppearanceFact terrain = Assert.Single(appearance.BuildFacts());
        Appearance baseAppearance = graphics.CreatePrimitive(default);

        appearance.Clear();
        Assert.Equal(0, appearance.ActiveCellCount);
        Assert.Equal(1, appearance.RetiredCellCount);
        List<AppearanceFact> snapshot =
        [new AppearanceFact(900, false, 0, default, baseAppearance, true, RenderLayer.Scene)];
        appearance.AppendFacts(snapshot);
        graphics.PublishSnapshot(snapshot.ToArray());
        appearance.CompleteAcceptedSnapshot();

        AppearanceFact[] published = Assert.Single(graphics.Snapshots);
        Assert.Equal(900UL, published[0].ObjectId);
        Assert.Equal(0, appearance.RetiredCellCount);
        Assert.Contains("appearance", graphics.Releases);
        Assert.Contains("mesh", graphics.Releases);
        Assert.DoesNotContain("material", graphics.Releases);
        Assert.DoesNotContain(terrain.Appearance, snapshot.Select(fact => fact.Appearance));
    }

    [Fact]
    public void Failed_addition_keeps_the_previous_visual_set_and_releases_staged_resources()
    {
        GraphicsDouble graphics = new();
        using DaggerfallExteriorTerrainAppearance appearance = new(graphics);
        DaggerfallExteriorCellId retainedCell = new(10, 20);
        appearance.Reconcile([retainedCell], DaggerfallExteriorWorldOrigin.At(retainedCell), Surface);
        AppearanceFact retained = Assert.Single(appearance.BuildFacts());

        Assert.Throws<InvalidOperationException>(() => appearance.Reconcile(
            [retainedCell, new(11, 20), new(12, 20)],
            DaggerfallExteriorWorldOrigin.At(retainedCell),
            cell => cell == new DaggerfallExteriorCellId(12, 20)
                ? InvalidSurface(cell)
                : Surface(cell)));

        AppearanceFact afterFailure = Assert.Single(appearance.BuildFacts());
        Assert.Equal(retained.ObjectId, afterFailure.ObjectId);
        Assert.Same(retained.Appearance, afterFailure.Appearance);
        Assert.Equal(2, graphics.MeshRequests.Count);
        Assert.Equal(1, appearance.ActiveCellCount);
        Assert.Equal(0, appearance.RetiredCellCount);
        Assert.Equal(1, graphics.ReleasedMeshes);
        Assert.Equal(1, graphics.ReleasedAppearances);
    }

    [Fact]
    public void Changed_tile_flags_replace_the_mesh_and_unchanged_flags_retain_it()
    {
        DaggerfallExteriorCellId cell = new(1, 0);
        DaggerfallExteriorWorldOrigin origin = DaggerfallExteriorWorldOrigin.At(cell);
        GraphicsDouble graphics = new();
        DaggerfallSiteProfile source = TestSessions.MediaInputs();
        DaggerfallSiteProfile profile = new(source.Project, source.Geometry,
            source.WorldAppearance, source.InitialLook, [], new Dictionary<long, NormalizedActorSprite>(),
            terrainTextures: new Dictionary<(int Archive, int Record), NormalizedTerrainTexture>
            {
                [(302, 0)] = new("texture/terrain.png", TestSessions.Hash),
            });
        using DaggerfallExteriorTerrainAppearance appearance = new(graphics, profile);
        DaggerfallTerrainSurface environmentSurface = new(cell.X, cell.Y,
            Enumerable.Range(0, 129 * 129).Select(index => new Vector3(index % 129, 0F, index / 129)).ToArray(),
            [new Triangle(0, 130, 1)], new float[129 * 129]);
        DaggerfallTerrainSurface surface = environmentSurface;
        DaggerfallClimateGridDefinition climate = new(3, 1, [0, 0, 231],
            [new(231, "Woodlands", DaggerfallClimateDisposition.Named)]);
        DaggerfallWorldGridsSet grids = new(climate, new(3, 1, [64, 64, 64], []));
        DaggerfallExteriorEnvironment Environment(byte tile)
        {
            DaggerfallSiteExterior location = new(1, 0, 1, 1, 0, 0, false, 2, 0, 1, 0, 1)
            {
                GroundTiles = DaggerfallGroundTileGrid.FromBytes(Enumerable.Repeat(tile, 128 * 128).ToArray(), "test"),
            };
            DaggerfallExteriorEnvironment environment = new(ScopedStreamRandom.Wrap(RandomMinimum.Create()));
            environment.Reconcile([cell], origin, _ => environmentSurface,
                new Dictionary<DaggerfallExteriorCellId, DaggerfallSiteExterior> { [cell] = location }, grids);
            return environment;
        }
        DaggerfallExteriorEnvironment original = Environment(0);
        DaggerfallExteriorEnvironment rotated = Environment(0x40);
        appearance.Reconcile([cell], origin, _ => surface, original);
        Appearance first = Assert.Single(appearance.BuildFacts()).Appearance;
        appearance.Reconcile([cell], origin, _ => surface, rotated);
        Appearance second = Assert.Single(appearance.BuildFacts()).Appearance;
        Assert.NotSame(first, second);
        Assert.NotEqual(graphics.MeshRequests[0].Uvs.Span[0], graphics.MeshRequests[1].Uvs.Span[0]);
        Assert.Equal(2, graphics.MeshRequests.Count);
        appearance.Reconcile([cell], origin, _ => surface, rotated);
        Assert.Same(second, Assert.Single(appearance.BuildFacts()).Appearance);
        Assert.Equal(2, graphics.MeshRequests.Count);
    }

    /// <summary>
    /// A cell's nature is drawn as one sprite batch per sprite record, each holding every placement of that record at
    /// its cell-local position, placed by the cell's translation. Reconciling unchanged facts keeps the batches; a
    /// season's archive replaces them, and the replaced batches are released once a snapshot without them is accepted.
    /// </summary>
    [Fact]
    public void Nature_is_drawn_in_one_batch_per_cell_and_sprite_record_and_replaced_by_season()
    {
        DaggerfallExteriorCellId cell = new(1, 0);
        DaggerfallExteriorWorldOrigin origin = new(0, 0, new Vector3(2F, 3F, 4F));
        GraphicsDouble graphics = new();
        DaggerfallSiteProfile source = TestSessions.MediaInputs();
        Dictionary<(int Archive, int Record), NormalizedTerrainTexture> textures = [];
        foreach (int archive in new[] { 302, 303 })
            for (int record = 0; record < 64; record++)
                textures[(archive, record)] = new($"texture/terrain-{archive}-{record}.png", TestSessions.Hash);
        Dictionary<(int Archive, int Record), NormalizedBillboardSprite> billboards = [];
        foreach (int archive in new[] { 504, 505 })
            for (int record = 1; record <= 31; record++)
                billboards[(archive, record)] = new($"sprite/texture-{archive}-{record}.png", TestSessions.Hash, 16, 32,
                    [new NormalizedAtlasFrame(7, 0, 0, 16, 32)], 7, new Vector2(.5F, 0F), new Vector2(record, 2F * record));
        DaggerfallSiteProfile profile = new(source.Project, source.Geometry, source.WorldAppearance, source.InitialLook, [],
            new Dictionary<long, NormalizedActorSprite>(), billboardSprites: billboards, terrainTextures: textures);
        using DaggerfallExteriorTerrainAppearance appearance = new(graphics, profile);
        int dimension = DaggerfallTerrainSurfaceBuilder.SampleDimension;
        DaggerfallTerrainSurface surface = new(cell.X, cell.Y,
            Enumerable.Range(0, dimension * dimension).Select(index => new Vector3(index % dimension, 100F, index / dimension)).ToArray(),
            [new Triangle(0, (uint)dimension + 1, 1)],
            Enumerable.Repeat(100F / DaggerfallTerrainSurfaceBuilder.TerrainVerticalSize, dimension * dimension).ToArray())
        {
            SourceWorldHeight = 128,
        };
        DaggerfallWorldGridsSet grids = new(new DaggerfallClimateGridDefinition(3, 1, [0, 0, 231],
            [new(231, "Woodlands", DaggerfallClimateDisposition.Named)]), new(3, 1, [64, 64, 64], []));
        DaggerfallSiteExterior location = new(1, 0, 1, 1, 0, 0, false, 2, 0, 1, 0, 1)
        {
            GroundTiles = DaggerfallGroundTileGrid.FromBytes(Enumerable.Repeat((byte)2, 128 * 128).ToArray(), "test"),
        };
        DaggerfallExteriorEnvironment environment = new(ScopedStreamRandom.Wrap(RandomMinimum.Create()));
        void Reconcile() => environment.Reconcile([cell], origin, _ => surface,
            new Dictionary<DaggerfallExteriorCellId, DaggerfallSiteExterior> { [cell] = location }, grids);
        Reconcile();
        appearance.Reconcile([cell], origin, _ => surface, environment);

        IReadOnlyList<DaggerfallExteriorNaturePlacement> placements = environment.NaturePlacements;
        Assert.True(placements.Count > 1000);
        int[] records = [.. placements.Select(placement => placement.SpriteRecord).Distinct().Order()];
        Assert.Equal(records.Length, graphics.BatchRequests.Count);
        Assert.Equal(records.Length, appearance.ActiveNatureBatchCount);
        Assert.Equal(placements.Count, appearance.ActiveNatureCount);
        for (int index = 0; index < records.Length; index++)
        {
            SpriteBatchRequest request = graphics.BatchRequests[index];
            DaggerfallExteriorNaturePlacement[] drawn = [.. placements.Where(placement => placement.SpriteRecord == records[index])];
            Assert.Equal(drawn.Select(placement => placement.LocalPosition), request.Instances.ToArray().Select(instance => instance.Position));
            Assert.All(request.Instances.ToArray(), instance => Assert.Equal((1F, 7U), (instance.Scale, instance.FrameId)));
            Assert.Equal(new Vector2(records[index], 2F * records[index]), request.Size);
            Assert.Equal(new Vector2(.5F, 0F), request.Pivot);
            Assert.Equal(BillboardMode.Cylindrical, request.Billboard);
            Assert.Equal(SpriteDepthPolicy.Default, request.Depth);
        }
        Assert.All(graphics.AtlasTextures, path => Assert.StartsWith("sprite/texture-504-", path));
        AppearanceFact[] nature = [.. appearance.BuildFacts().Where(fact => fact.ObjectId != DaggerfallExteriorTerrainAppearance.ObjectId(cell))];
        Assert.Equal(records.Select(record => DaggerfallExteriorTerrainAppearance.NatureBatchObjectId(cell, record)), nature.Select(fact => fact.ObjectId));
        Assert.All(nature, fact => Assert.Equal(origin.LocalTranslation(cell), fact.Transform.Translation));

        // Unchanged facts keep every batch; an origin rebase only moves them.
        DaggerfallExteriorWorldOrigin rebased = new(1, 0, Vector3.Zero);
        appearance.Reconcile([cell], rebased, _ => surface, environment);
        Assert.Equal(records.Length, graphics.BatchRequests.Count);
        Assert.Equal(nature.Select(fact => fact.Appearance), appearance.BuildFacts().Skip(1).Select(fact => fact.Appearance));
        Assert.All(appearance.BuildFacts().Skip(1), fact => Assert.Equal(rebased.LocalTranslation(cell), fact.Transform.Translation));

        // Winter selects the woodland archive's snow set; the summer batches retire with the next accepted snapshot.
        environment.SetSeason(DaggerfallExteriorSeason.Winter);
        Reconcile();
        int released = graphics.ReleasedAppearances;
        appearance.Reconcile([cell], rebased, _ => surface, environment);
        Assert.Equal(2 * records.Length, graphics.BatchRequests.Count);
        Assert.Equal(records.Length, appearance.RetiredNatureBatchCount);
        Assert.Contains(graphics.AtlasTextures, path => path.StartsWith("sprite/texture-505-", StringComparison.Ordinal));
        List<AppearanceFact> snapshot = [];
        appearance.AppendFacts(snapshot);
        Assert.DoesNotContain(snapshot, fact => nature.Any(summer => ReferenceEquals(summer.Appearance, fact.Appearance)));
        graphics.PublishSnapshot(snapshot.ToArray());
        appearance.CompleteAcceptedSnapshot();
        Assert.Equal(0, appearance.RetiredNatureBatchCount);
        Assert.True(graphics.ReleasedAppearances - released >= records.Length);
    }

    private static DaggerfallTerrainSurface Surface(DaggerfallExteriorCellId cell) => new(
        cell.X,
        cell.Y,
        [new Vector3(0F, 0F, 0F), new Vector3(1F, 0F, 0F), new Vector3(0F, 0F, 1F)],
        [new Triangle(0, 2, 1)],
        [0F, 0F, 0F]);

    private static DaggerfallTerrainSurface InvalidSurface(DaggerfallExteriorCellId cell) => new(
        cell.X,
        cell.Y,
        [new Vector3(0F, 0F, 0F), new Vector3(1F, 0F, 0F), new Vector3(0F, 0F, 1F)],
        [new Triangle(0, 2, 4)],
        [0F, 0F, 0F]);

    private static void AssertUv(Vector2 input, bool rotated, bool flipped, Vector2 expected)
    {
        Vector2 actual = DaggerfallExteriorTerrainAppearance.TransformTileUv(input, rotated, flipped);
        Assert.Equal(expected.X, actual.X, 5);
        Assert.Equal(expected.Y, actual.Y, 5);
    }

    private sealed class GraphicsDouble : IGraphicsService
    {
        private ulong _nextHandle = 1;

        internal List<MeshResourceCreateRequest> MeshRequests { get; } = [];
        internal List<TerrainLayerMaterialRequest> TerrainLayerMaterialRequests { get; } = [];
        internal List<AppearanceFact[]> Snapshots { get; } = [];
        internal List<string> Releases { get; } = [];
        internal int ReleasedMeshes { get; private set; }
        internal int ReleasedAppearances { get; private set; }

        internal List<SpriteBatchRequest> BatchRequests { get; } = [];
        internal List<string> AtlasTextures { get; } = [];
        private readonly Dictionary<ulong, string> _resources = [];

        public RenderResourceInfo OpenResource(RenderResourceRequest request)
        {
            ulong handle = _nextHandle++;
            _resources[handle] = request.Path;
            return new(new RenderResource(new RenderResourceHandle(handle), () => Releases.Add("resource")), default, 0);
        }
        public TextureResourceInfo ReadTextureInfo(RenderResource resource) => throw new NotSupportedException();
        public void PublishChanges(AppearanceChangesRequest request) => throw new NotSupportedException();
        public RenderResourceInfo OpenResourceFromContent(RenderResourceContentRequest request) => throw new NotSupportedException();
        public Appearance CreateStaticMeshFromContentReference(StaticMeshContentReferenceRequest request) => throw new NotSupportedException();

        public Material CreateMaterial(MaterialRequest request) => new(
            new MaterialHandle(_nextHandle++),
            () => Releases.Add("material"));

        public Material CreateTerrainLayerMaterial(TerrainLayerMaterialRequest request)
        {
            TerrainLayerMaterialRequests.Add(request);
            return new Material(new MaterialHandle(_nextHandle++), () => Releases.Add("material"));
        }

        public void UpdateMaterial(MaterialUpdateRequest request) => throw new NotSupportedException();
        public Material ReplaceMaterial(MaterialUpdateRequest request) => throw new NotSupportedException();
        public Appearance CreatePrimitive(PrimitiveAppearanceRequest request) => CreateAppearance();
        public Appearance ReplacePrimitive(PrimitiveAppearanceReplaceRequest request) => throw new NotSupportedException();

        public MeshResource CreateMeshResource(MeshResourceCreateRequest request)
        {
            MeshRequests.Add(request);
            return new MeshResource(new MeshResourceHandle(_nextHandle++), () =>
            {
                ReleasedMeshes++;
                Releases.Add("mesh");
            });
        }

        public Appearance CreateMeshAppearance(MeshResource resource) => CreateAppearance();
        public MeshPartition PartitionMesh(MeshPartitionRequest request) => throw new NotSupportedException();
        public MeshPartitionReadout ReadMeshPartition(MeshPartition partition) => throw new NotSupportedException();
        public MeshResource TakeMeshPartitionPart(MeshPartitionPartRequest request) => throw new NotSupportedException();
        public Appearance CreateStaticMesh(StaticMeshAppearanceRequest request) => CreateAppearance();
        public Appearance CreateStaticMeshFromContent(StaticMeshContentAppearanceRequest request) => CreateAppearance();
        public Appearance ReplaceStaticMesh(Appearance appearance, StaticMeshAppearanceRequest request) => throw new NotSupportedException();
        public Appearance ReplaceStaticMeshFromContent(Appearance appearance, StaticMeshContentAppearanceRequest request) => throw new NotSupportedException();
        public void UpdateStaticMeshMaterials(StaticMeshMaterialUpdateRequest request) => throw new NotSupportedException();
        public void UpdateStaticMeshMaterialFactors(StaticMeshMaterialFactorsRequest request) => throw new NotSupportedException();
        public Appearance CreateSprite(SpriteAppearanceRequest request) => throw new NotSupportedException();
        public Appearance CreateSpriteBatch(SpriteBatchRequest request)
        {
            BatchRequests.Add(request);
            return CreateAppearance();
        }
        public Appearance ReplaceSprite(SpriteAppearanceReplaceRequest request) => throw new NotSupportedException();
        public SpriteAtlas CreateSpriteAtlas(SpriteAtlasCreateRequest request)
        {
            AtlasTextures.Add(_resources[request.Texture.Handle.Value]);
            return new SpriteAtlas(new SpriteAtlasHandle(_nextHandle++), () => Releases.Add("atlas"));
        }
        public Appearance CreateSpriteFromAtlas(SpriteFromAtlasRequest request) => throw new NotSupportedException();
        public Appearance ReplaceSpriteFromAtlas(SpriteFromAtlasReplaceRequest request) => throw new NotSupportedException();
        public void SetSpriteFrame(SpriteFrameUpdateRequest request) => throw new NotSupportedException();
        public void SetSpriteViewport(SpriteViewportUpdateRequest request) => throw new NotSupportedException();
        public SpriteReadout ReadSprite(Appearance appearance) => throw new NotSupportedException();
        public SpritePlayback CreateSpritePlayback(SpritePlaybackCreateRequest request) => throw new NotSupportedException();
        public SpritePlaybackReadout ControlSpritePlayback(SpritePlaybackControlRequest request) => throw new NotSupportedException();
        public SpritePlaybackReadout SelectSpritePlaybackFrame(SpritePlaybackFrameSelectionRequest request) => throw new NotSupportedException();
        public SpritePlaybackAdvanceResult AdvanceSpritePlayback(SpritePlaybackAdvanceRequest request) => throw new NotSupportedException();
        public SpritePlaybackSample SampleSpritePlayback(SpritePlaybackSampleRequest request) => throw new NotSupportedException();
        public SpritePlaybackReadout ReadSpritePlayback(SpritePlayback playback) => throw new NotSupportedException();

        public void PublishSnapshot(ReadOnlySpan<AppearanceFact> values) => Snapshots.Add(values.ToArray());
        public Material CreateAuthoredMaterial(AuthoredMaterialAppearanceRequest request) => throw new NotSupportedException();
        public Light CreateLight(LightRequest request) => throw new NotSupportedException();
        public void UpdateLight(LightUpdateRequest request) => throw new NotSupportedException();
        public Light ReplaceLight(LightUpdateRequest request) => throw new NotSupportedException();
        public LightReadout ReadLight(Light light) => throw new NotSupportedException();
        public PresentationReadout ReadPresentation() => throw new NotSupportedException();

        private Appearance CreateAppearance()
        {
            return new Appearance(new AppearanceHandle(_nextHandle++), () =>
            {
                ReleasedAppearances++;
                Releases.Add("appearance");
            });
        }
    }
}
