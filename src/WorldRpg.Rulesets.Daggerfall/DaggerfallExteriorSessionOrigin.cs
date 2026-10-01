using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Pure product-side interpretation of the Engine WorldOrigin commit receipt.</summary>
internal static class DaggerfallExteriorSessionOrigin
{
    /// <summary>
    /// Engine retains global positions while changing local origin. Existing local transforms move by
    /// the previous cell minus the committed cell, in the same world-unit frame used by Spatial.
    /// </summary>
    internal static Vector3 LocalDelta(WorldOriginCommitReceipt receipt) => receipt.LocalDelta;

    internal static WorldPoint Shift(WorldPoint position, Vector3 localDelta)
    {
        Vector3 shifted = position.ToVector() + localDelta;
        if (!float.IsFinite(shifted.X) || !float.IsFinite(shifted.Y) || !float.IsFinite(shifted.Z))
            throw new InvalidOperationException("WorldOrigin rebase produced a non-finite product position.");
        return WorldPoint.From(shifted);
    }

    /// <summary>
    /// Converts a product local pose back to the durable map-pixel cell that contains it. Daggerfall
    /// terrain's positive local Z points toward decreasing map-pixel Y, matching the surface builder's
    /// local translation and keeping boundary traversal deterministic on either side of zero.
    /// </summary>
    internal static DaggerfallExteriorCellId CellForLocalPosition(
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

        double offsetX = Math.Floor(((double)position.X - origin.Compensation.X) / DaggerfallExteriorCellResidency.CellSize);
        double offsetY = Math.Floor(((double)position.Z - origin.Compensation.Z) / DaggerfallExteriorCellResidency.CellSize);
        double mapPixelX = origin.MapPixelX + offsetX;
        double mapPixelY = origin.MapPixelY - offsetY;
        if (!double.IsFinite(mapPixelX) || !double.IsFinite(mapPixelY)
            || mapPixelX < int.MinValue || mapPixelX > int.MaxValue
            || mapPixelY < int.MinValue || mapPixelY > int.MaxValue)
        {
            throw new InvalidOperationException("Exterior local position cannot be mapped to a durable map-pixel identity.");
        }

        DaggerfallExteriorCellId cell = new((int)mapPixelX, (int)mapPixelY);
        bounds.Require(cell, nameof(position));
        return cell;
    }

}
