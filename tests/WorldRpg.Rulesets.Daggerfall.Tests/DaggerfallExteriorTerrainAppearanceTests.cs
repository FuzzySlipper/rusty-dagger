using System.Numerics;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallExteriorTerrainAppearanceTests
{
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
        appearance.Publish([new AppearanceFact(900, false, 0, default, baseAppearance, true, RenderLayer.Scene)]);

        AppearanceFact[] snapshot = Assert.Single(graphics.Snapshots);
        Assert.Equal(900UL, snapshot[0].ObjectId);
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

    private sealed class GraphicsDouble : IGraphicsService
    {
        private ulong _nextHandle = 1;

        internal List<MeshResourceCreateRequest> MeshRequests { get; } = [];
        internal List<AppearanceFact[]> Snapshots { get; } = [];
        internal List<string> Releases { get; } = [];
        internal int ReleasedMeshes { get; private set; }
        internal int ReleasedAppearances { get; private set; }

        public RenderResourceInfo OpenResource(RenderResourceRequest request) => throw new NotSupportedException();
        public RenderResourceInfo OpenResourceFromContent(RenderResourceContentRequest request) => throw new NotSupportedException();
        public Appearance CreateStaticMeshFromContentReference(StaticMeshContentReferenceRequest request) => throw new NotSupportedException();

        public Material CreateMaterial(MaterialRequest request) => new(
            new MaterialHandle(_nextHandle++),
            () => Releases.Add("material"));

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
        public Appearance CreateSprite(SpriteAppearanceRequest request) => throw new NotSupportedException();
        public Appearance ReplaceSprite(SpriteAppearanceReplaceRequest request) => throw new NotSupportedException();
        public SpriteAtlas CreateSpriteAtlas(SpriteAtlasCreateRequest request) => throw new NotSupportedException();
        public Appearance CreateSpriteFromAtlas(SpriteFromAtlasRequest request) => throw new NotSupportedException();
        public Appearance ReplaceSpriteFromAtlas(SpriteFromAtlasReplaceRequest request) => throw new NotSupportedException();
        public void SetSpriteFrame(SpriteFrameUpdateRequest request) => throw new NotSupportedException();
        public void SetSpriteViewport(SpriteViewportUpdateRequest request) => throw new NotSupportedException();
        public SpriteReadout ReadSprite(Appearance appearance) => throw new NotSupportedException();
        public SpritePlayback CreateSpritePlayback(SpritePlaybackCreateRequest request) => throw new NotSupportedException();
        public SpritePlaybackReadout ControlSpritePlayback(SpritePlaybackControlRequest request) => throw new NotSupportedException();
        public SpritePlaybackReadout SelectSpritePlaybackFrame(SpritePlaybackFrameSelectionRequest request) => throw new NotSupportedException();
        public SpritePlaybackAdvanceLeaseReceipt AdvanceSpritePlayback(SpritePlaybackAdvanceRequest request) => throw new NotSupportedException();
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
