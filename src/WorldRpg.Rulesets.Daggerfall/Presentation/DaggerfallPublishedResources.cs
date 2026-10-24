using Rusty.Engine;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>
/// Opens published artifacts for the renderer and the mixer by the path and digest a profile records for them.
/// </summary>
/// <remarks>
/// The Engine resolves a path and digest against every place it stages content, so a presentation owner
/// never names where an artifact is kept. The reference is released once the resource is open; the resource
/// keeps what it needs.
/// </remarks>
internal static class DaggerfallPublishedResources
{
    /// <summary>Opens a published texture with the given sampling, as sRGB colour.</summary>
    internal static RenderResourceInfo OpenTexture(this IGraphicsService graphics, IContentService content, string path, ContentSha256 sha256,
        TextureFilter filter = TextureFilter.Nearest, TextureWrap wrap = TextureWrap.Clamp)
    {
        using ContentReference reference = Resolve(content, path, sha256);
        return graphics.OpenResourceFromContent(new RenderResourceContentRequest(reference, filter, wrap));
    }

    /// <summary>Creates a static mesh appearance from a published <c>.rstatmsh</c>.</summary>
    internal static Appearance CreateStaticMesh(this IGraphicsService graphics, IContentService content, string path, ContentSha256 sha256, Color tint)
    {
        using ContentReference reference = Resolve(content, path, sha256);
        return graphics.CreateStaticMeshFromContentReference(new StaticMeshContentReferenceRequest(reference, tint));
    }

    /// <summary>Opens a published audio clip.</summary>
    internal static AudioClip OpenClip(this IAudioService audio, IContentService content, string path, ContentSha256 sha256)
    {
        using ContentReference reference = Resolve(content, path, sha256);
        return audio.OpenClipFromContent(new AudioClipFromContentRequest(reference));
    }

    private static ContentReference Resolve(IContentService content, string path, ContentSha256 sha256)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return content.ResolveReference(new ContentResolveRequest(path, sha256));
    }
}
