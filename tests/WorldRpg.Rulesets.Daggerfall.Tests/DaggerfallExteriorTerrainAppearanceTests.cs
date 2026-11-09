using System.Numerics;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallExteriorTerrainAppearanceTests
{
    [Theory]
    [InlineData(100F, true)]
    [InlineData(2_400F, false)]
    public void Nature_gives_out_where_snow_takes_the_ground(float height, bool grows)
    {
        DaggerfallExteriorCellId cell = new(1, 0);
        DaggerfallExteriorWorldOrigin origin = DaggerfallExteriorWorldOrigin.At(cell);
        int dimension = DaggerfallTerrainSurfaceBuilder.SampleDimension;
        DaggerfallTerrainSurface surface = new(cell.X, cell.Y,
            Enumerable.Range(0, dimension * dimension).Select(index => new Vector3(index % dimension, height, index / dimension)).ToArray(),
            Enumerable.Repeat(height / DaggerfallTerrainSurfaceBuilder.TerrainVerticalSize, dimension * dimension).ToArray())
        {
            SourceWorldHeight = 128,
        };
        DaggerfallSiteExterior location = new(1, 0, 1, 1, 0, 0, false, 2, 0, 1, 0, 1)
        {
            GroundTiles = DaggerfallGroundTileGrid.FromBytes(Enumerable.Repeat((byte)2, 128 * 128).ToArray(), "test"),
        };
        DaggerfallExteriorEnvironment environment = new(ScopedStreamRandom.Wrap(RandomMinimum.Create()));

        environment.ReconcileSurfaces([cell], origin, _ => surface, new Dictionary<DaggerfallExteriorCellId, DaggerfallSiteExterior> { [cell] = location },
            new DaggerfallWorldGridsSet(new DaggerfallClimateGridDefinition(3, 1, [0, 0, 231], [new(231, "Woodlands", DaggerfallClimateDisposition.Named)]), new(3, 1, [64, 64, 64], [])));

        Assert.Equal(grows, environment.NatureFor(cell).Count > 0);
    }

    private static (DaggerfallExteriorTerrainAppearance, DaggerfallTerrainSurface, DaggerfallExteriorEnvironment, Action<bool>) OpenGround(
        GraphicsDouble graphics, DaggerfallExteriorCellId cell, DaggerfallExteriorWorldOrigin origin, (int Archive, int Record)[] refreshedLayers,
        int climate = 231, float height = 100F)
    {
        DaggerfallSiteProfile source = TestSessions.MediaInputs();
        Dictionary<(int Archive, int Record), NormalizedTerrainTexture> textures = [];
        foreach (int archive in new[] { 302, 402 })
            for (int record = 0; record < 64; record++) textures[(archive, record)] = new($"terrain/texture-{archive}-{record}.png", TestSessions.Hash);
        textures[(303, 2)] = new("terrain/texture-303-2.png", TestSessions.Hash);
        textures[(403, 2)] = new("terrain/texture-403-2.png", TestSessions.Hash);
        textures[(102, 3)] = new("terrain/texture-102-3.png", TestSessions.Hash);
        Dictionary<(int Archive, int Record), NormalizedBillboardSprite> billboards = [];
        foreach (int archive in new[] { 501, 502, 503, 504, 505, 506, 507, 508, 509, 510, 511 })
            for (int record = 1; record <= 31; record++)
                billboards[(archive, record)] = new($"sprite/texture-{archive}-{record}.png", TestSessions.Hash, 16, 32,
                    [new NormalizedAtlasFrame(7, 0, 0, 16, 32)], 7, new Vector2(.5F, .5F), new Vector2(3F, 6F));
        DaggerfallSiteProfile profile = new(source.Project, source.Geometry, source.WorldAppearance, source.InitialLook, [],
            new Dictionary<long, NormalizedActorSprite>(), billboardSprites: billboards, terrainTextures: textures);
        string surfaces = string.Join(",", refreshedLayers.Select(layer => $$$"""
            {"replaces": {"archive": {{{layer.Archive}}}, "record": {{{layer.Record}}}}, "path": "refreshed/surfaces/texture-{{{layer.Archive}}}-{{{layer.Record}}}.png",
             "byteLength": 1, "sha256": "0000000000000001000000000000000200000000000000030000000000000004", "width": 128, "height": 128}
            """));
        foreach ((int archive, int record) in refreshedLayers) graphics.Content.Add($"refreshed/surfaces/texture-{archive}-{record}.png", TestSessions.Hash);
        DaggerfallArtReplacements art = DaggerfallArtReplacements.Read(System.Text.Encoding.UTF8.GetBytes(
            $$$"""{"artReplacements": {"images": [], "surfaces": [{{{surfaces}}}]}}"""), "test.art");
        bool refreshed = true;
        DaggerfallExteriorTerrainAppearance appearance = new(graphics.Content, graphics, graphics.Admit(profile));
        appearance.UseArt(art, () => refreshed);
        appearance.UseBlend(ServiceProxy<IAuthoredContentService, AuthoredContentFake>.Create(), DaggerfallGroundTuning.Default, DaggerfallGroundTuning.Default.Blend);
        int dimension = DaggerfallTerrainSurfaceBuilder.SampleDimension;
        DaggerfallTerrainSurface surface = new(cell.X, cell.Y,
            Enumerable.Range(0, dimension * dimension).Select(index => new Vector3(index % dimension, height, index / dimension)).ToArray(),
            Enumerable.Repeat(height / DaggerfallTerrainSurfaceBuilder.TerrainVerticalSize, dimension * dimension).ToArray())
        {
            SourceWorldHeight = 128,
        };
        DaggerfallExteriorEnvironment environment = new(ScopedStreamRandom.Wrap(RandomMinimum.Create()));
        environment.ReconcileSurfaces([cell], origin, _ => surface, new Dictionary<DaggerfallExteriorCellId, DaggerfallSiteExterior>(),
            new DaggerfallWorldGridsSet(new DaggerfallClimateGridDefinition(3, 1, [0, 0, (byte)climate], [new(climate, "Ground", DaggerfallClimateDisposition.Named)]), new(3, 1, [64, 64, 64], [])));
        return (appearance, surface, environment, drawn => refreshed = drawn);
    }

    /// <summary>
    /// Classic art draws nature in the donor's places, one batch per cell and record. Refreshed art leaves it to the flora
    /// field, which draws each record's art at the classic size, and choosing classic art again redraws the batches.
    /// </summary>
    [Fact]
    public void Refreshed_nature_leaves_the_cells_for_the_flora_field_and_classic_art_draws_them_again()
    {
        DaggerfallExteriorCellId cell = new(1, 0);
        DaggerfallExteriorWorldOrigin origin = DaggerfallExteriorWorldOrigin.At(cell);
        GraphicsDouble graphics = new();
        DaggerfallSiteProfile source = TestSessions.MediaInputs();
        Dictionary<(int Archive, int Record), NormalizedTerrainTexture> textures = [];
        for (int record = 0; record < 64; record++) textures[(302, record)] = new($"texture/terrain-302-{record}.png", TestSessions.Hash);
        Dictionary<(int Archive, int Record), NormalizedBillboardSprite> billboards = [];
        for (int record = 1; record <= 31; record++)
            billboards[(504, record)] = new($"sprite/texture-504-{record}.png", TestSessions.Hash, 16, 32,
                [new NormalizedAtlasFrame(7, 0, 0, 16, 32)], 7, new Vector2(.5F, .5F), new Vector2(3F, 6F));
        DaggerfallSiteProfile profile = new(source.Project, source.Geometry, source.WorldAppearance, source.InitialLook, [],
            new Dictionary<long, NormalizedActorSprite>(), billboardSprites: billboards, terrainTextures: textures);
        string flats = string.Join(",", Enumerable.Range(1, 31).Select(record => $$$"""
            {"replaces": {"archive": 504, "record": {{{record}}}}, "path": "refreshed/flats/504-{{{record}}}.png", "byteLength": 1,
             "sha256": "0000000000000001000000000000000200000000000000030000000000000004", "width": 64, "height": 64,
             "frames": [{"x": 0, "y": 0, "width": 64, "height": 64}], "classic": {"x": 0.25, "y": 0.25, "width": 0.5, "height": 0.5}}
            """));
        for (int record = 1; record <= 31; record++) graphics.Content.Add($"refreshed/flats/504-{record}.png", TestSessions.Hash);
        DaggerfallArtReplacements art = DaggerfallArtReplacements.Read(System.Text.Encoding.UTF8.GetBytes(
            $$$"""{"artReplacements": {"images": [], "flats": [{{{flats}}}]}}"""), "test.art");
        bool refreshed = false;
        using DaggerfallExteriorTerrainAppearance appearance = new(graphics.Content, graphics, graphics.Admit(profile));
        appearance.UseArt(art, () => refreshed);
        int dimension = DaggerfallTerrainSurfaceBuilder.SampleDimension;
        DaggerfallTerrainSurface surface = new(cell.X, cell.Y,
            Enumerable.Range(0, dimension * dimension).Select(index => new Vector3(index % dimension, 100F, index / dimension)).ToArray(),
            Enumerable.Repeat(100F / DaggerfallTerrainSurfaceBuilder.TerrainVerticalSize, dimension * dimension).ToArray())
        {
            SourceWorldHeight = 128,
        };
        DaggerfallSiteExterior location = new(1, 0, 1, 1, 0, 0, false, 2, 0, 1, 0, 1)
        {
            GroundTiles = DaggerfallGroundTileGrid.FromBytes(Enumerable.Repeat((byte)2, 128 * 128).ToArray(), "test"),
        };
        DaggerfallExteriorEnvironment environment = new(ScopedStreamRandom.Wrap(RandomMinimum.Create()));
        environment.ReconcileSurfaces([cell], origin, _ => surface, new Dictionary<DaggerfallExteriorCellId, DaggerfallSiteExterior> { [cell] = location },
            new DaggerfallWorldGridsSet(new DaggerfallClimateGridDefinition(3, 1, [0, 0, 231], [new(231, "Woodlands", DaggerfallClimateDisposition.Named)]), new(3, 1, [64, 64, 64], [])));
        appearance.Reconcile([cell], origin, environment);
        int batches = appearance.ActiveNatureBatchCount;
        Assert.True(batches > 0);
        Assert.All(graphics.BatchRequests, request =>
        {
            Assert.Equal(new Vector2(3F, 6F), request.Size);
            Assert.Equal(SpriteLightingMode.Synthetic, request.Material.Lighting);
            // Nature takes the camera's heading and leans in the wind by whole texels, as the flats tuning says.
            Assert.Equal(DaggerfallFlatsTuning.Default.Nature, request.Billboard);
            Assert.Equal(DaggerfallFlatsTuning.Default.FloraWindBend, request.Material.WindBend);
            Assert.True(request.Material.WindWholeTexels);
        });

        // Refreshed nature grows in the flora field instead: the cells keep their placements and draw no batches, and the
        // field draws each record's refreshed art at the classic size, its pivot moved into the art's box.
        refreshed = true;
        appearance.AppendFacts([]);
        Assert.Equal(0, appearance.ActiveNatureBatchCount);
        Assert.Equal(batches, graphics.BatchRequests.Count);
        DaggerfallFloraLook look = appearance.FloraLook(504, 1)!;
        Assert.Equal("refreshed/flats/504-1.png", look.Image.Path);
        Assert.Equal(new Vector2(6F, 12F), look.Size);
        Assert.Equal(new Vector2(.5F, .25F), look.Pivot);
        Assert.Equal((Vector2.Zero, Vector2.One), (look.UvMin, look.UvMax));
        Assert.Null(appearance.FloraLook(504, 40));

        refreshed = false;
        appearance.AppendFacts([]);
        Assert.Equal(batches, appearance.ActiveNatureBatchCount);
        Assert.Equal(2 * batches, graphics.BatchRequests.Count);
        Assert.All(graphics.BatchRequests.Skip(batches), request => Assert.Equal(new Vector2(3F, 6F), request.Size));
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
                    [new NormalizedAtlasFrame(7, 0, 0, 16, 32)], 7, new Vector2(.5F, .5F), new Vector2(record, 2F * record));
        DaggerfallSiteProfile profile = new(source.Project, source.Geometry, source.WorldAppearance, source.InitialLook, [],
            new Dictionary<long, NormalizedActorSprite>(), billboardSprites: billboards, terrainTextures: textures);
        using DaggerfallExteriorTerrainAppearance appearance = new(graphics.Content, graphics, graphics.Admit(profile));
        int dimension = DaggerfallTerrainSurfaceBuilder.SampleDimension;
        DaggerfallTerrainSurface surface = new(cell.X, cell.Y,
            Enumerable.Range(0, dimension * dimension).Select(index => new Vector3(index % dimension, 100F, index / dimension)).ToArray(),
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
        void Reconcile() => environment.ReconcileSurfaces([cell], origin, _ => surface,
            new Dictionary<DaggerfallExteriorCellId, DaggerfallSiteExterior> { [cell] = location }, grids);
        Reconcile();
        appearance.Reconcile([cell], origin, environment);

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
            Assert.Equal(DaggerfallFlatsTuning.Default.Nature, request.Billboard);
            Assert.Equal(SpriteDepthPolicy.Default, request.Depth);
        }
        Assert.All(graphics.AtlasTextures, path => Assert.StartsWith("sprite/texture-504-", path));
        AppearanceFact[] nature = [.. appearance.BuildFacts()];
        Assert.Equal(records.Select(record => DaggerfallExteriorTerrainAppearance.NatureBatchObjectId(cell, record)), nature.Select(fact => fact.ObjectId));
        Assert.All(nature, fact => Assert.Equal(origin.LocalTranslation(cell), fact.Transform.Translation));

        // Unchanged facts keep every batch; an origin rebase only moves them.
        DaggerfallExteriorWorldOrigin rebased = new(1, 0, Vector3.Zero);
        appearance.Reconcile([cell], rebased, environment);
        Assert.Equal(records.Length, graphics.BatchRequests.Count);
        Assert.Equal(nature.Select(fact => fact.Appearance), appearance.BuildFacts().Select(fact => fact.Appearance));
        Assert.All(appearance.BuildFacts(), fact => Assert.Equal(rebased.LocalTranslation(cell), fact.Transform.Translation));

        // Winter selects the woodland archive's snow set; the summer batches retire with the next accepted snapshot.
        environment.SetSeason(DaggerfallExteriorSeason.Winter);
        Reconcile();
        int released = graphics.ReleasedAppearances;
        appearance.Reconcile([cell], rebased, environment);
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
        public void PublishOverlaySet(OverlaySetRequest request) => throw new NotSupportedException("This test double does not publish overlays.");
        public RenderResourceInfo OpenResourceFromContent(RenderResourceContentRequest request)
        {
            ulong handle = _nextHandle++;
            _resources[handle] = Content.ReadReferenceInfo(request.Content).Span[0].Path;
            return new(new RenderResource(new RenderResourceHandle(handle), () => Releases.Add("resource")), default, 0);
        }

        /// <summary>The content the appearance resolves its textures through, by the path and digest a profile records.</summary>
        internal ContentFake Content { get; } = new([]);

        /// <summary>Admits a profile's terrain textures and billboards to <see cref="Content"/>.</summary>
        internal DaggerfallSiteProfile Admit(DaggerfallSiteProfile profile)
        {
            foreach (NormalizedTerrainTexture texture in profile.TerrainTextures.Values) Content.Add(texture.TexturePath, texture.TextureSha256);
            foreach (NormalizedBillboardSprite sprite in profile.BillboardSprites.Values) Content.Add(sprite.TexturePath, sprite.TextureSha256);
            return profile;
        }

        public Appearance CreateStaticMeshFromContentReference(StaticMeshContentReferenceRequest request) => throw new NotSupportedException();

        public Material CreateMaterial(MaterialRequest request) => new(
            new MaterialHandle(_nextHandle++),
            () => Releases.Add("material"));

        public Material CreateTerrainLayerMaterial(TerrainLayerMaterialRequest request)
        {
            TerrainLayerMaterialRequests.Add(request);
            return new Material(new MaterialHandle(_nextHandle++), () => Releases.Add("material"));
        }

        internal List<MaterialUpdateRequest> MaterialUpdates { get; } = [];
        public void UpdateMaterial(MaterialUpdateRequest request) => MaterialUpdates.Add(request);
        /// <summary>The content path of every texture opened, in order.</summary>
        internal IEnumerable<string> OpenedPaths => _resources.OrderBy(entry => entry.Key).Select(entry => entry.Value);
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
        public LandmarkReadResult ReadLandmarks(LandmarkReadRequest request) => throw new NotSupportedException();
        public SpritePlayback CreateSpritePlayback(SpritePlaybackCreateRequest request) => throw new NotSupportedException();
        public SpritePlaybackReadout ControlSpritePlayback(SpritePlaybackControlRequest request) => throw new NotSupportedException();
        public SpritePlaybackReadout SelectSpritePlaybackFrame(SpritePlaybackFrameSelectionRequest request) => throw new NotSupportedException();
        public SpritePlaybackAdvanceResult AdvanceSpritePlayback(SpritePlaybackAdvanceRequest request) => throw new NotSupportedException();
        public SpritePlaybackSample SampleSpritePlayback(SpritePlaybackSampleRequest request) => throw new NotSupportedException();
        public SpritePlaybackReadout ReadSpritePlayback(SpritePlayback playback) => throw new NotSupportedException();

        public void PublishSnapshot(ReadOnlySpan<AppearanceFact> values) => Snapshots.Add(values.ToArray());
        public Material CreateAuthoredMaterial(AuthoredMaterialAppearanceRequest request) => CreateMaterial(default);
        public void SetSpriteEmission(SpriteEmissionUpdateRequest request) => throw new NotSupportedException();
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
