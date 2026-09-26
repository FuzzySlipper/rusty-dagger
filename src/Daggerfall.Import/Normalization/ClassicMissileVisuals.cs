using System.Globalization;

namespace Daggerfall.Import.Normalization;

/// <summary>One classic mesh a published site shows for a missile while it is in flight.</summary>
/// <param name="MediaId">The logical identity the site composition names this visual by.</param>
/// <param name="MeshNumber">The mesh number, as the numeric mesh archive indexes it.</param>
/// <param name="Use">What shows it, so the visual is published with a consumer rather than on its own.</param>
public sealed record ClassicMissileVisual(string MediaId, uint MeshNumber, string Use);

/// <summary>
/// The missile art the classic corpus carries and the donor behaviour that selects it.
/// </summary>
/// <remarks>
/// <para>
/// The donor draws a flying arrow from mesh 99800: <c>Game/DaggerfallMissile.cs</c> creates that model
/// for an arrow projectile. The corpus states the same number for the arrow prop some blocks place —
/// the block object's own description is "ARW" — so the mesh is the arrow both in flight and at rest,
/// and it is reachable in the mesh archive's own inventory.
/// </para>
/// <para>
/// <c>ARROW.RAW</c> is supplied in Arena2 but no donor code reads it, and the mesh's planes do not
/// select it, so it is not this projectile's art. Publishing it here would put a record the game never
/// drew in place of the one it did.
/// </para>
/// </remarks>
public static class ClassicMissileVisuals
{
    /// <summary>The mesh number the donor creates for a flying arrow.</summary>
    public const uint ArrowMeshNumber = 99800;

    /// <summary>The missile visuals every published site carries.</summary>
    public static IReadOnlyList<ClassicMissileVisual> Published { get; } =
        [new("visual.missile.arrow", ArrowMeshNumber, "the in-flight arrow Game/DaggerfallMissile.cs creates")];

    /// <summary>
    /// The mesh numbers these visuals draw from, so a site publication can publish the geometry they
    /// name in the same pass rather than pointing a descriptor at an artifact nobody wrote.
    /// </summary>
    public static IReadOnlyList<string> MeshIds { get; } =
        [.. Published.Select(visual => visual.MeshNumber.ToString(CultureInfo.InvariantCulture))];
}
