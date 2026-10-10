using Rusty.Engine;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The real Engine graphics service as an Engine test host serves it, which has no Product.Update callback: every
/// call reaches the Engine, including each complete appearance snapshot and its validation, except a sprite playback
/// advance, which the Engine admits only inside Product.Update. That is answered with the playback's current readout,
/// not advanced. A test reads what the Engine accepted from the counts kept here.
/// </summary>
internal sealed class EngineGraphicsOutsideUpdate(IGraphicsService engine) : IGraphicsService
{
    private readonly IGraphicsService _engine = engine;

    /// <summary>How many complete snapshots the Engine accepted.</summary>
    internal int AcceptedSnapshots { get; private set; }

    /// <summary>The largest object identity of any accepted snapshot.</summary>
    internal ulong LargestObjectId { get; private set; }

    /// <summary>How many objects the last accepted snapshot held.</summary>
    internal int LastSnapshotObjects { get; private set; }

    /// <summary>The object identities of the last accepted snapshot.</summary>
    internal IReadOnlyList<ulong> LastSnapshotIds { get; private set; } = [];

    /// <summary>The facts of the last accepted snapshot.</summary>
    internal IReadOnlyList<AppearanceFact> LastSnapshot { get; private set; } = [];

    /// <summary>How many retained Engine appearances and mesh resources the product created through this service.</summary>
    internal int CreatedResources { get; private set; }

    public void PublishSnapshot(ReadOnlySpan<AppearanceFact> values)
    {
        _engine.PublishSnapshot(values);
        AcceptedSnapshots++;
        LastSnapshotObjects = values.Length;
        ulong[] ids = new ulong[values.Length];
        for (int index = 0; index < values.Length; index++)
        {
            ids[index] = values[index].ObjectId;
            LargestObjectId = Math.Max(LargestObjectId, ids[index]);
        }
        LastSnapshotIds = ids;
        LastSnapshot = values.ToArray();
    }

    public SpritePlaybackAdvanceResult AdvanceSpritePlayback(SpritePlaybackAdvanceRequest arg0) =>
        new(ReadOnlyMemory<SpritePlaybackMarkerCrossing>.Empty, _engine.ReadSpritePlayback(arg0.Playback), false);

    public RenderResourceInfo OpenResource(RenderResourceRequest arg0) { CreatedResources++; return _engine.OpenResource(arg0); }
    public TextureResourceInfo ReadTextureInfo(RenderResource arg0) => _engine.ReadTextureInfo(arg0);
    public RenderResourceInfo OpenResourceFromContent(RenderResourceContentRequest arg0) => _engine.OpenResourceFromContent(arg0);
    public Appearance CreateStaticMeshFromContentReference(StaticMeshContentReferenceRequest arg0) { CreatedResources++; return _engine.CreateStaticMeshFromContentReference(arg0); }
    public Material CreateMaterial(MaterialRequest arg0) { CreatedResources++; return _engine.CreateMaterial(arg0); }
    public void UpdateMaterial(MaterialUpdateRequest arg0) => _engine.UpdateMaterial(arg0);
    public Material ReplaceMaterial(MaterialUpdateRequest arg0) => _engine.ReplaceMaterial(arg0);
    public Appearance CreatePrimitive(PrimitiveAppearanceRequest arg0) { CreatedResources++; return _engine.CreatePrimitive(arg0); }
    public Appearance ReplacePrimitive(PrimitiveAppearanceReplaceRequest arg0) => _engine.ReplacePrimitive(arg0);
    public MeshResource CreateMeshResource(MeshResourceCreateRequest arg0) { CreatedResources++; return _engine.CreateMeshResource(arg0); }
    public Appearance CreateMeshAppearance(MeshResource arg0) { CreatedResources++; return _engine.CreateMeshAppearance(arg0); }
    public MeshPartition PartitionMesh(MeshPartitionRequest arg0) => _engine.PartitionMesh(arg0);
    public MeshPartitionReadout ReadMeshPartition(MeshPartition arg0) => _engine.ReadMeshPartition(arg0);
    public MeshResource TakeMeshPartitionPart(MeshPartitionPartRequest arg0) => _engine.TakeMeshPartitionPart(arg0);
    public Appearance CreateStaticMesh(StaticMeshAppearanceRequest arg0) { CreatedResources++; return _engine.CreateStaticMesh(arg0); }
    public Appearance CreateStaticMeshFromContent(StaticMeshContentAppearanceRequest arg0) { CreatedResources++; return _engine.CreateStaticMeshFromContent(arg0); }
    public Appearance ReplaceStaticMesh(Appearance arg0, StaticMeshAppearanceRequest arg1) => _engine.ReplaceStaticMesh(arg0, arg1);
    public Appearance ReplaceStaticMeshFromContent(Appearance arg0, StaticMeshContentAppearanceRequest arg1) => _engine.ReplaceStaticMeshFromContent(arg0, arg1);
    public void UpdateStaticMeshMaterials(StaticMeshMaterialUpdateRequest arg0) => _engine.UpdateStaticMeshMaterials(arg0);
    public void UpdateStaticMeshMaterialFactors(StaticMeshMaterialFactorsRequest arg0) => _engine.UpdateStaticMeshMaterialFactors(arg0);
    public Appearance CreateSprite(SpriteAppearanceRequest arg0) { CreatedResources++; return _engine.CreateSprite(arg0); }
    public Appearance CreateSpriteBatch(SpriteBatchRequest arg0) { CreatedResources++; return _engine.CreateSpriteBatch(arg0); }
    public Appearance ReplaceSprite(SpriteAppearanceReplaceRequest arg0) => _engine.ReplaceSprite(arg0);
    public SpriteAtlas CreateSpriteAtlas(SpriteAtlasCreateRequest arg0) { CreatedResources++; return _engine.CreateSpriteAtlas(arg0); }
    public Appearance CreateSpriteFromAtlas(SpriteFromAtlasRequest arg0) { CreatedResources++; return _engine.CreateSpriteFromAtlas(arg0); }
    public Appearance ReplaceSpriteFromAtlas(SpriteFromAtlasReplaceRequest arg0) => _engine.ReplaceSpriteFromAtlas(arg0);
    public void SetSpriteFrame(SpriteFrameUpdateRequest arg0) => _engine.SetSpriteFrame(arg0);
    public void SetSpriteViewport(SpriteViewportUpdateRequest arg0) => _engine.SetSpriteViewport(arg0);
    public SpriteReadout ReadSprite(Appearance arg0) => _engine.ReadSprite(arg0);
    public SpritePlayback CreateSpritePlayback(SpritePlaybackCreateRequest arg0) { CreatedResources++; return _engine.CreateSpritePlayback(arg0); }
    public SpritePlaybackReadout ControlSpritePlayback(SpritePlaybackControlRequest arg0) => _engine.ControlSpritePlayback(arg0);
    public SpritePlaybackReadout SelectSpritePlaybackFrame(SpritePlaybackFrameSelectionRequest arg0) => _engine.SelectSpritePlaybackFrame(arg0);
    public SpritePlaybackSample SampleSpritePlayback(SpritePlaybackSampleRequest arg0) => _engine.SampleSpritePlayback(arg0);
    public SpritePlaybackReadout ReadSpritePlayback(SpritePlayback arg0) => _engine.ReadSpritePlayback(arg0);
    public void PublishChanges(AppearanceChangesRequest arg0) => _engine.PublishChanges(arg0);
    public Light CreateLight(LightRequest arg0) { CreatedResources++; return _engine.CreateLight(arg0); }
    public void UpdateLight(LightUpdateRequest arg0) => _engine.UpdateLight(arg0);
    public Light ReplaceLight(LightUpdateRequest arg0) => _engine.ReplaceLight(arg0);
    public LightReadout ReadLight(Light arg0) => _engine.ReadLight(arg0);
    public PresentationReadout ReadPresentation() => _engine.ReadPresentation();
    public Material CreateAuthoredMaterial(AuthoredMaterialAppearanceRequest arg0) { CreatedResources++; return _engine.CreateAuthoredMaterial(arg0); }
    public Material CreateTerrainLayerMaterial(TerrainLayerMaterialRequest arg0) { CreatedResources++; return _engine.CreateTerrainLayerMaterial(arg0); }
}
