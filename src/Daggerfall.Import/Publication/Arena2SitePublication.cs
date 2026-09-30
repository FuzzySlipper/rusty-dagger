using System.Text.Json;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Audio;
using Daggerfall.Import.Normalization;
using Daggerfall.Import.Normalized;

namespace Daggerfall.Import.Publication;

/// <summary>What a site closure publishes beyond its own spatial selection.</summary>
/// <param name="RuntimeActorResources">The actor media every site carries because the runtime may spawn any of them there.</param>
/// <param name="ClassicMedia">The classic media profile: authored UI art and any classic sprite overlays.</param>
/// <param name="Music">The published music cues the site names.</param>
/// <param name="DungeonOverlays">Authored overlays that apply to the site's dungeon sprites.</param>
public sealed record Arena2SiteMedia(
    IReadOnlyList<string> RuntimeActorResources,
    Arena2ClassicMediaProfile ClassicMedia,
    IReadOnlyList<ClassicMusicRecord> Music,
    IReadOnlyList<AuthoredMediaOverlay> DungeonOverlays);

/// <summary>
/// Builds one site closure: the normalized RDB dungeon or RMB profile, the geometry and world visuals it
/// references, its dungeon media and the classic media, composed into one publication plan.
/// </summary>
public static class Arena2SitePublication
{
    /// <summary>The ground-container billboard every site publishes for dropped loot.</summary>
    public const string GroundContainerBillboard = "sprite/texture-216-0";

    /// <summary>
    /// The actor media every site publishes: one sprite per mobile the imported mobile catalog says the
    /// runtime may materialize — its published actors, their numbered variants and the human mobiles it
    /// resolves through careers. A mobile the catalog leaves unpublished has no actor to draw.
    /// </summary>
    public static IReadOnlyList<string> RuntimeActorResources(string importedPayloadJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(importedPayloadJson);
        using JsonDocument document = JsonDocument.Parse(importedPayloadJson);
        if (!document.RootElement.TryGetProperty("mobiles", out JsonElement section)
            || !section.TryGetProperty("mobiles", out JsonElement mobiles))
        {
            throw new InvalidOperationException("The imported payload carries no mobile catalog, which is what names the actors a site publishes media for: run the mobile-catalog command first.");
        }

        return [.. mobiles.EnumerateArray()
            .Where(mobile => mobile.GetProperty("disposition").GetString() != "unpublished")
            .Select(mobile => mobile.GetProperty("donorId").GetInt32())
            .Order()
            .Select(id => $"actor/mobile-{id}")];
    }

    /// <summary>Publishes one RDB dungeon site, loading the texture leaves its closure names on demand.</summary>
    public static ImportPublicationPlan Dungeon(
        Arena2SiteSources sources,
        int region,
        string location,
        DungeonTextureTableMode textureTable,
        Arena2SiteMedia media)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(media);
        return sources.LoadingOnDemand(() =>
        {
            DungeonNormalizationResult result = DungeonNormalizer.Normalize(new DungeonNormalizationRequest(
                new DungeonLogicalSourceSet(sources.DungeonSources),
                region,
                location,
                textureTable,
                DungeonNormalizationQuotas.Default with { MaximumSourceBytes = Arena2SiteSources.MaximumTotalSourceBytes }));
            (GeometryPublication geometry, Arena2DungeonMediaPublication dungeonMedia, Arena2ClassicMediaPublication classicMedia) =
                PublishMedia(sources, result.Document, result.ReferencedMeshIds, media, $"selected dungeon media '{location}'");
            return Arena2MediaBundlePublication.Create(result, dungeonMedia, classicMedia, geometry).Plan;
        });
    }

    /// <summary>Publishes one RMB exterior, or one building interior when <paramref name="building"/> is given.</summary>
    public static (ImportPublicationPlan Plan, RmbExteriorNormalizationResult Result) Rmb(
        Arena2SiteSources sources,
        int region,
        string location,
        RmbBuildingSelection? building,
        Arena2SiteMedia media)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(media);
        return sources.LoadingOnDemand(() =>
        {
            RmbExteriorNormalizationResult result = RmbExteriorNormalizer.Normalize(new(
                new DungeonLogicalSourceSet(sources.DungeonSources), region, location,
                building is null ? RmbWorldProfileKind.Exterior : RmbWorldProfileKind.Interior) { Building = building });
            (GeometryPublication geometry, Arena2DungeonMediaPublication dungeonMedia, Arena2ClassicMediaPublication classicMedia) =
                PublishMedia(sources, result.Document, result.ReferencedMeshIds, media, $"selected RMB media '{result.Layout.LocationName}'");
            return (Arena2MediaBundlePublication.Create(result, dungeonMedia, classicMedia, geometry).Plan, result);
        });
    }

    private static (GeometryPublication, Arena2DungeonMediaPublication, Arena2ClassicMediaPublication) PublishMedia(
        Arena2SiteSources sources,
        NormalizedImportDocument document,
        IReadOnlyList<string> referencedMeshIds,
        Arena2SiteMedia media,
        string textureLeafConsumer)
    {
        TextureLeafInventory textureLeaves = sources.TextureLeaves();
        (GeometryPublication geometry, IReadOnlyList<ClassicWorldVisualRequest> worldVisuals) =
            PublishGeometryAndSiteVisuals(sources.MeshArchive, referencedMeshIds, textureLeaves);
        foreach (ushort archive in worldVisuals.SelectMany(visual => visual.Materials).Select(material => material.Archive).Distinct().Order())
        {
            sources.AdmitWorldVisualTextureLeaf(archive);
        }

        Arena2DungeonMediaPublication dungeonMedia = Arena2DungeonMediaPublication.Create(
            Arena2DungeonMediaRequest.Create(document, new Arena2DungeonMediaSourceSet(sources.DungeonMediaSources)) with
            {
                RuntimeActorResources = media.RuntimeActorResources,
                RuntimeBillboardResources = [GroundContainerBillboard],
                AuthoredOverlays = media.DungeonOverlays,
                TextureLeaves = textureLeaves,
                TextureLeafConsumer = textureLeafConsumer,
            });
        Arena2ClassicMediaPublication classicMedia = Arena2ClassicMediaPublication.Create(
            sources.ClassicMediaInputs with { Music = media.Music },
            media.ClassicMedia,
            new Arena2ClassicMediaPublicationOptions(MaximumSourceBytes: Arena2SiteSources.MaximumIndividualSourceBytes),
            worldVisuals);
        return (geometry, dungeonMedia, classicMedia);
    }

    /// <summary>
    /// Publishes a site's mesh geometry together with the classic world visuals that name it, in one pass.
    /// </summary>
    /// <remarks>
    /// The missile mesh of a flying arrow is not placed by any block, so nothing in a pack references it;
    /// the site's own world visuals do, which is why their mesh numbers join the referenced set rather
    /// than being published behind the publication's back. Reading each descriptor's facts out of the
    /// geometry publication it just produced is what keeps a visual from naming an artifact nobody wrote.
    /// </remarks>
    private static (GeometryPublication Geometry, IReadOnlyList<ClassicWorldVisualRequest> WorldVisuals) PublishGeometryAndSiteVisuals(
        DungeonLogicalSource archSource,
        IReadOnlyList<string> referencedMeshIds,
        TextureLeafInventory textures)
    {
        Arch3dMeshInventory inventory = Arch3dInventoryReader.Read(archSource.Bytes.ToArray(), archSource.Label);
        GeometryPublication geometry = GeometryPublicationBuilder.Create(new GeometryPublicationRequest(
            inventory,
            archSource.Bytes,
            [.. referencedMeshIds, .. ClassicMissileVisuals.MeshIds],
            textures));
        IReadOnlyList<ClassicWorldVisualRequest> worldVisuals =
            [.. ClassicMissileVisuals.Published.Select(visual => ClassicWorldVisualRequest.FromGeometry(visual, geometry, inventory, archSource.Bytes))];
        return (geometry, worldVisuals);
    }
}
