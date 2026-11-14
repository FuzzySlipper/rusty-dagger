using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// Native origin changed but the product could not finish the matching pose update. Ordinary
/// teleport, travel and entrance refusal handlers must let this terminate the inconsistent session.
/// </summary>
internal sealed class DaggerfallOriginCommitException(Exception innerException)
    : Exception("The Engine origin was committed but product rebasing failed; the session cannot continue with inconsistent world coordinates.", innerException);

internal sealed record DaggerfallWorldOriginTuning(float VerticalRebaseDistance)
{
    internal DaggerfallWorldOriginTuning Validate()
    {
        if (!float.IsFinite(VerticalRebaseDistance) || VerticalRebaseDistance <= 0f)
            throw new ArgumentOutOfRangeException(nameof(VerticalRebaseDistance));
        return this;
    }
}

/// <summary>Pure product-side interpretation of the Engine WorldOrigin commit receipt.</summary>
internal static class DaggerfallExteriorSessionOrigin
{
    // The donor's PlayerGPS stops the player this far inside each map edge, in metres, so the end of the
    // world is not so visible: 10,000, 14,000, 18,000 and 10,000 of its 40-to-the-metre world units.
    private const double WestMargin = 250;
    private const double NorthMargin = 350;
    private const double EastMargin = 450;
    private const double SouthMargin = 250;

    /// <summary>
    /// Engine retains global positions while changing local origin. Existing local transforms move by
    /// the previous cell minus the committed cell, in the same world-unit frame used by Spatial.
    /// </summary>
    internal static Vector3 LocalDelta(WorldOriginCommitReceipt receipt) => receipt.LocalDelta;

    /// <summary>
    /// Converts a product local pose back to the durable map-pixel cell that contains it. Daggerfall
    /// terrain's negative local Z points toward decreasing map-pixel Y, matching the surface builder's
    /// local translation and keeping boundary traversal deterministic on either side of zero. A pose
    /// past the map's edge belongs to the nearest edge pixel: only the player is held inside the world
    /// edge, so a corpse or pile can still lie beyond it.
    /// </summary>
    internal static DaggerfallExteriorCellId CellForLocalPosition(
        WorldPoint position,
        DaggerfallExteriorWorldOrigin origin,
        DaggerfallExteriorWorldBounds bounds)
    {
        RequireFrame(position, origin, bounds);
        double offsetX = Math.Floor(((double)position.X - origin.Compensation.X) / DaggerfallExteriorCellResidency.CellSize);
        double offsetY = Math.Floor((origin.Compensation.Z - (double)position.Z) / DaggerfallExteriorCellResidency.CellSize);
        return new(
            (int)Math.Clamp(origin.MapPixelX + offsetX, 0, bounds.Width - 1),
            (int)Math.Clamp(origin.MapPixelY - offsetY, 0, bounds.Height - 1));
    }

    /// <summary>
    /// The nearest local pose inside the world edge. Each axis stops at its own edge, so a player pressing
    /// along the boundary slides rather than sticks.
    /// </summary>
    internal static WorldPoint InsideWorldEdge(
        WorldPoint position,
        DaggerfallExteriorWorldOrigin origin,
        DaggerfallExteriorWorldBounds bounds)
    {
        RequireFrame(position, origin, bounds);
        float cell = DaggerfallExteriorCellResidency.CellSize;
        // Local X grows east from the origin pixel's west side, and local Z grows south from its north side.
        double west = origin.Compensation.X - ((double)origin.MapPixelX * cell);
        double north = origin.Compensation.Z - (((double)origin.MapPixelY + 1) * cell);
        return position with
        {
            X = (float)Math.Clamp(position.X, west + WestMargin, west + ((double)bounds.Width * cell) - EastMargin),
            Z = (float)Math.Clamp(position.Z, north + NorthMargin, north + ((double)bounds.Height * cell) - SouthMargin),
        };
    }

    private static void RequireFrame(
        WorldPoint position,
        DaggerfallExteriorWorldOrigin origin,
        DaggerfallExteriorWorldBounds bounds)
    {
        bounds.Validate();
        bounds.Require(new DaggerfallExteriorCellId(origin.MapPixelX, origin.MapPixelY), nameof(origin));
        if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(position.Z))
            throw new ArgumentOutOfRangeException(nameof(position), "Exterior local position must be finite.");
        if (!float.IsFinite(origin.Compensation.X)
            || !float.IsFinite(origin.Compensation.Y)
            || !float.IsFinite(origin.Compensation.Z))
        {
            throw new ArgumentOutOfRangeException(nameof(origin), "Exterior origin compensation must be finite.");
        }
    }

}
