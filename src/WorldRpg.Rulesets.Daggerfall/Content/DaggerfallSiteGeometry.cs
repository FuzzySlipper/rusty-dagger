using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>
/// One collision and navigation artifact a profile places beside its others, by whole navigation cells
/// from the profile's frame.
/// </summary>
/// <param name="Id">The part's identity within its profile, which its Engine placement identity derives from.</param>
/// <param name="Path">The artifact's content path.</param>
/// <param name="Sha256">The artifact's admitted digest.</param>
/// <param name="ColumnOffset">Whole navigation cells along +X.</param>
/// <param name="RowOffset">Whole navigation cells along +Z.</param>
internal sealed record DaggerfallSiteSpatialPart(string Id, string Path, ContentSha256 Sha256, long ColumnOffset = 0, long RowOffset = 0)
{
    internal DaggerfallSiteSpatialPart Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(Path);
        return this;
    }
}

/// <summary>
/// One static mesh a profile draws: a content artifact posed in the profile's frame, its mesh material
/// slots bound to the profile's material slots.
/// </summary>
internal sealed record DaggerfallSiteMesh(string Path, ContentSha256 Sha256, Transform Pose, IReadOnlyList<DaggerfallMeshMaterialBinding> Materials)
{
    /// <summary>The ARCH3D record id of the model a block places here.</summary>
    internal string? ModelId { get; init; }

    internal DaggerfallSiteMesh Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Path);
        if (!float.IsFinite(Pose.Translation.X) || !float.IsFinite(Pose.Translation.Y) || !float.IsFinite(Pose.Translation.Z)
            || !float.IsFinite(Pose.Rotation.X) || !float.IsFinite(Pose.Rotation.Y) || !float.IsFinite(Pose.Rotation.Z) || !float.IsFinite(Pose.Rotation.W))
            throw new ArgumentOutOfRangeException(nameof(Pose), "A site mesh pose must be finite.");
        if (Materials.Select(binding => binding.MeshSlot).Distinct().Count() != Materials.Count)
            throw new ArgumentException("A site mesh binds each of its material slots once.", nameof(Materials));
        return this;
    }
}

/// <summary>
/// The remodel a profile draws in place of one classic ARCH3D model while refreshed art is drawn: the art pack's static
/// mesh, its slots that name classic texture identities bound to the profile's material slots for them (so they take the
/// location's climate, season and dungeon remaps), the rest bound to the textures the pack ships for them, and which of
/// its classic slots draw their texture as a layer of a shell stack.
/// </summary>
internal sealed record DaggerfallSiteRemodel(string Path, ContentSha256 Sha256, IReadOnlyList<DaggerfallMeshMaterialBinding> Materials,
    IReadOnlyList<DaggerfallRemodelTexture> Textures)
{
    /// <summary>The shell layer each stacked slot draws, by its mesh slot.</summary>
    internal IReadOnlyDictionary<uint, DaggerfallArtShell> Shells { get; init; } = new Dictionary<uint, DaggerfallArtShell>();
}

/// <summary>A remodel's material slot that draws a texture the art pack ships, opaque or cut out by its alpha (<paramref name="Mask"/>).</summary>
internal sealed record DaggerfallRemodelTexture(uint MeshSlot, string Path, ContentSha256 Sha256, bool Mask);

/// <summary>
/// What a profile places in the world: one collision and navigation artifact per block, each beside its
/// neighbours, and each block's models drawn from the product-wide meshes, with the remodel of each model the
/// art pack has one for.
/// </summary>
internal sealed class DaggerfallSiteGeometry
{
    internal DaggerfallSiteGeometry(ulong navigationGridId, IEnumerable<DaggerfallSiteSpatialPart> spatial, IEnumerable<DaggerfallSiteMesh> meshes,
        Func<IDisposable>? spatialSource = null)
    {
        ArgumentNullException.ThrowIfNull(spatial);
        ArgumentNullException.ThrowIfNull(meshes);
        NavigationGridId = navigationGridId;
        Spatial = Array.AsReadOnly(spatial.Select(part => part.Validate()).ToArray());
        if (Spatial.Count == 0) throw new ArgumentException("A site places at least one collision and navigation artifact.", nameof(spatial));
        if (Spatial.Select(part => part.Id).Distinct(StringComparer.Ordinal).Count() != Spatial.Count)
            throw new ArgumentException("A site's spatial parts must have distinct identities.", nameof(spatial));
        Meshes = Array.AsReadOnly(meshes.Select(mesh => mesh.Validate()).ToArray());
        SpatialSource = spatialSource;
    }

    /// <summary>The navigation grid every part shares.</summary>
    internal ulong NavigationGridId { get; }

    /// <summary>
    /// The remodels this profile draws, by the ARCH3D record id of the model each replaces: every remodelled model its
    /// meshes and action models place.
    /// </summary>
    internal IReadOnlyDictionary<string, DaggerfallSiteRemodel> Remodels { get; init; } = new Dictionary<string, DaggerfallSiteRemodel>();

    internal IReadOnlyList<DaggerfallSiteSpatialPart> Spatial { get; }

    internal IReadOnlyList<DaggerfallSiteMesh> Meshes { get; }

    /// <summary>
    /// Opens what the Engine resolves the spatial parts from for one admission, when they are not eager
    /// content: the per-block publication's bundle stays open while the Engine reads the block artifacts.
    /// </summary>
    internal Func<IDisposable>? SpatialSource { get; }

    /// <summary>
    /// A model mesh's pose from its placement's position and source Euler degrees. The donor turns
    /// importer-space points by <c>Rz * Rx * Ry</c> before the right-handed flip of Z, so in the profile's
    /// right-handed frame the same turn is <c>Rz(z) * Rx(-x) * Ry(-y)</c>.
    /// </summary>
    internal static Transform Pose(Vector3 position, Vector3 rotationDegrees) =>
        new(position, SourceRotation(rotationDegrees), Vector3.One);

    /// <summary>An action model's initial Engine pose from its normalized source Euler tuple, refused unless finite.</summary>
    internal static Transform InitialTransform(Vector3 position, Vector3 rotationDegrees)
    {
        if (!IsFinite(position) || !IsFinite(rotationDegrees))
            throw new ArgumentOutOfRangeException(nameof(position), "Action model pose must be finite.");
        return Pose(position, rotationDegrees);
    }

    /// <summary>The right-handed rotation the donor's source Euler degrees turn a model by.</summary>
    internal static Quaternion SourceRotation(Vector3 rotationDegrees)
    {
        Vector3 radians = rotationDegrees * (MathF.PI / 180F);
        return Quaternion.Normalize(Quaternion.CreateFromAxisAngle(Vector3.UnitZ, radians.Z)
            * Quaternion.CreateFromAxisAngle(Vector3.UnitX, -radians.X)
            * Quaternion.CreateFromAxisAngle(Vector3.UnitY, -radians.Y));
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
