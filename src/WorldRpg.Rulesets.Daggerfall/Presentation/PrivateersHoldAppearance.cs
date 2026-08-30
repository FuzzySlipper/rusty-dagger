using System.Numerics;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Kit.Actors;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>Publishes the normalized Privateer's Hold visual closure through Engine-owned resources and sprite atlases.</summary>
internal sealed class PrivateersHoldAppearance : IDisposable
{
    private readonly IAppearanceService appearance;
    private readonly Dictionary<long, Appearance> sprites = [];
    private readonly List<SpriteAtlas> atlases = [];
    private readonly List<Material> materials = [];
    private Appearance? world;
    private readonly AuthoredWorldAppearance worldAppearance;
    private bool disposed;

    internal PrivateersHoldAppearance(IContentService content, IAppearanceService appearance, PrivateersHoldInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(appearance);
        ArgumentNullException.ThrowIfNull(inputs);
        this.appearance = appearance;
        worldAppearance = inputs.WorldAppearance;
        try
        {
            VerifyContent(content, inputs.StaticMesh);
            world = appearance.CreateStaticMeshFromContent(new StaticMeshContentAppearanceRequest(inputs.StaticMesh.Path, worldAppearance.Tint));
            foreach (NormalizedMaterial material in inputs.Materials)
            {
                VerifyContent(content, new ContentArtifact(material.TexturePath, material.TextureSha256));
                RenderResourceInfo texture = appearance.OpenResource(new RenderResourceRequest(material.TexturePath));
                materials.Add(appearance.CreateMaterial(new MaterialRequest(new Color(1F, 1F, 1F, 1F), texture.Handle, 1F, new Color(1F, 1F, 1F, 1F), Vector3.Zero, 0F, false)));
            }
            appearance.UpdateStaticMeshMaterials(new StaticMeshMaterialUpdateRequest(world, inputs.Materials.Select((material, index) => new MeshMaterialBinding(material.Slot, materials[index])).ToArray()));
            foreach ((long entityId, NormalizedActorSprite sprite) in inputs.ActorSprites.OrderBy(pair => pair.Key))
            {
                VerifyContent(content, new ContentArtifact(sprite.TexturePath, sprite.TextureSha256));
                RenderResourceInfo texture = appearance.OpenResource(new RenderResourceRequest(sprite.TexturePath));
                SpriteAtlasFrame[] frames = sprite.Frames.Select(frame => new SpriteAtlasFrame(frame.Id, new Vector2((float)frame.X / sprite.AtlasWidth, (float)frame.Y / sprite.AtlasHeight), new Vector2((float)(frame.X + frame.Width) / sprite.AtlasWidth, (float)(frame.Y + frame.Height) / sprite.AtlasHeight), true, new Vector2(frame.Width, frame.Height))).ToArray();
                SpriteAtlas atlas = appearance.CreateSpriteAtlas(new SpriteAtlasCreateRequest(texture.Handle, frames));
                atlases.Add(atlas);
                sprites.Add(entityId, appearance.CreateSpriteFromAtlas(new SpriteFromAtlasRequest(atlas, sprite.InitialFrameId, sprite.Pivot, sprite.Size, BillboardMode.Cylindrical, SpriteSizeMode.World, 0, SpriteDepthPolicy.Default, new Color(1F, 1F, 1F, 1F))));
            }
        }
        catch { Dispose(); throw; }
    }

    internal void Publish(ActorsState actors)
    {
        if (disposed) return;
        List<AppearanceFact> facts = [];
        if (world is { } staticWorld) facts.Add(new AppearanceFact(1, worldAppearance.Transform, staticWorld, worldAppearance.Visible, worldAppearance.Layer));
        facts.AddRange(actors.All.Values.Where(actor => !actor.IsDefeated && sprites.ContainsKey(actor.EntityId)).Select(actor => new AppearanceFact(checked((ulong)actor.EntityId), new Transform(actor.Position.ToVector(), Quaternion.Identity, Vector3.One), sprites[actor.EntityId], true, RenderLayer.Scene)));
        appearance.PublishSnapshot([.. facts]);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        List<Exception>? failures = null;
        try { appearance.PublishSnapshot(ReadOnlySpan<AppearanceFact>.Empty); }
        catch (Exception exception) { failures = [exception]; }
        foreach (Appearance sprite in sprites.Values.Reverse()) Dispose(sprite, ref failures);
        sprites.Clear();
        if (world is { } staticWorld) { world = null; Dispose(staticWorld, ref failures); }
        foreach (SpriteAtlas atlas in atlases.AsEnumerable().Reverse()) Dispose(atlas, ref failures);
        atlases.Clear();
        foreach (Material material in materials.AsEnumerable().Reverse()) Dispose(material, ref failures);
        materials.Clear();
        if (failures is { Count: > 0 }) throw new AggregateException(failures);
    }

    private static void VerifyContent(IContentService content, ContentArtifact artifact)
    {
        using ContentReference reference = content.ResolveReference(new ContentResolveRequest(artifact.Path, artifact.Sha256));
        ReadOnlyMemory<ContentReferenceInfo> info = content.ReadReferenceInfo(reference);
        if (info.Length != 1 || info.Span[0].Path != artifact.Path || info.Span[0].Sha256 != artifact.Sha256) throw new InvalidOperationException($"Engine content did not preserve identity for '{artifact.Path}'.");
    }

    private static void Dispose(IDisposable value, ref List<Exception>? failures)
    {
        try { value.Dispose(); }
        catch (Exception exception) { (failures ??= []).Add(exception); }
    }
}
