using Rusty.Engine;

namespace WorldRpg.Rulesets.Daggerfall.World;

/// <summary>One texture a missile visual's mesh draws with, bound to the mesh's own material slot.</summary>
internal readonly record struct DaggerfallMissileTextureBinding(uint MeshSlot, string TexturePath, ContentSha256 TextureSha256);

/// <summary>
/// A content-backed world visual for a missile the rules put in flight: the mesh artifact a site's
/// classic publication admitted, with the textures that mesh's own material slots draw with.
/// </summary>
/// <remarks>
/// The textures travel with the visual rather than through the site's static-mesh material table,
/// because a missile's mesh is not part of that table and its slots do not address it. The visual names
/// content and nothing else: where the missile is, how it is oriented, and when it is retired belong to
/// the flight that spawned it, so this record carries no pose and no lifetime.
/// </remarks>
internal sealed record DaggerfallMissileVisual(
    string MediaId,
    string Use,
    string Path,
    ContentSha256 Sha256,
    IReadOnlyList<DaggerfallMissileTextureBinding> Textures)
{
    internal DaggerfallMissileVisual Validate()
    {
        if (string.IsNullOrWhiteSpace(MediaId)) throw new ArgumentException("A missile visual needs a media identity.", nameof(MediaId));
        if (string.IsNullOrWhiteSpace(Use)) throw new ArgumentException("A missile visual needs a stated consumer.", nameof(Use));
        if (string.IsNullOrWhiteSpace(Path)) throw new ArgumentException("A missile visual needs admitted content.", nameof(Path));
        ArgumentNullException.ThrowIfNull(Textures);
        if (Textures.Count == 0 || Textures.Select(texture => texture.MeshSlot).Distinct().Count() != Textures.Count)
            throw new ArgumentException("A missile visual needs distinct mesh material slots.", nameof(Textures));
        if (Textures.Any(texture => string.IsNullOrWhiteSpace(texture.TexturePath)))
            throw new ArgumentException("A missile visual needs admitted textures.", nameof(Textures));
        return this;
    }
}
