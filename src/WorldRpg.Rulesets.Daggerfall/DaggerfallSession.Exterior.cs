using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Session ownership of the active Daggerfall exterior residency.</summary>
internal sealed partial class DaggerfallSession
{
    private DaggerfallExteriorCellResidency? _exteriorResidency;
    private DaggerfallExteriorTerrainAppearance? _exteriorTerrainAppearance;
    private Func<DaggerfallExteriorCellId, DaggerfallTerrainSurface>? _exteriorSurfaceFactory;

    /// <summary>Whether the current spatial session has an admitted exterior window.</summary>
    internal bool ExteriorResidencyInitialized => _exteriorResidency?.IsInitialized == true;

    /// <summary>
    /// Resolves the active site's normalized map-pixel identity. An interior or dungeon profile does
    /// not own a wilderness cell window, even though it retains the same geographic site context.
    /// </summary>
    internal DaggerfallExteriorCellId ActiveExteriorCell()
    {
        RequireExteriorProfile();
        DaggerfallSiteRecord site = _site.ActiveSite
            ?? throw new InvalidOperationException("An exterior profile requires an active Daggerfall site.");
        return new(site.MapPixelX, site.MapPixelY);
    }

    /// <summary>
    /// Resolves the current streaming center from the authoritative player local pose. Before the
    /// first admission the active site's map pixel supplies the local origin, so an authored landing
    /// pose at the site still starts in the site's cell; after admission, crossing a terrain tile
    /// boundary advances the center without creating a second world-position authority.
    /// </summary>
    internal DaggerfallExteriorCellId CurrentExteriorCell()
    {
        DaggerfallExteriorCellId site = ActiveExteriorCell();
        DaggerfallExteriorWorldOrigin origin = _exteriorResidency is { IsInitialized: true } residency
            ? residency.Origin
            : DaggerfallExteriorWorldOrigin.At(site);
        return State.PlayerControl.Position is WorldPoint position
            ? DaggerfallExteriorSessionOrigin.CellForLocalPosition(
                position,
                origin,
                new DaggerfallExteriorWorldBounds(_definitions.Terrain.Width, _definitions.Terrain.Height))
            : site;
    }

    /// <summary>
    /// Admits or advances the seven-by-seven window at the active site's map pixel, retaining the
    /// coordinator's current local-origin compensation after ordinary movement.
    /// </summary>
    internal DaggerfallExteriorCellResidencyUpdate UpdateExteriorResidency()
    {
        DaggerfallExteriorCellId center = CurrentExteriorCell();
        DaggerfallExteriorCellResidency residency = EnsureExteriorResidency();
        DaggerfallExteriorWorldOrigin origin = residency.IsInitialized
            ? residency.Origin
            : DaggerfallExteriorWorldOrigin.At(center);
        DaggerfallExteriorCellResidencyUpdate update = residency.Update(center, origin);
        ReconcileExteriorTerrainAppearance(residency);
        return update;
    }

    /// <summary>Admits the active exterior window with an explicit saved or rebased local origin.</summary>
    internal DaggerfallExteriorCellResidencyUpdate UpdateExteriorResidency(
        DaggerfallExteriorWorldOrigin origin)
    {
        RequireExteriorProfile();
        DaggerfallExteriorCellId center = State.PlayerControl.Position is WorldPoint position
            ? DaggerfallExteriorSessionOrigin.CellForLocalPosition(
                position,
                origin,
                new DaggerfallExteriorWorldBounds(_definitions.Terrain.Width, _definitions.Terrain.Height))
            : ActiveExteriorCell();
        DaggerfallExteriorCellResidency residency = EnsureExteriorResidency();
        DaggerfallExteriorCellResidencyUpdate update = residency.Update(center, origin);
        ReconcileExteriorTerrainAppearance(residency);
        return update;
    }

    /// <summary>Captures only durable exterior identity/origin facts; Engine handles stay native.</summary>
    internal DaggerfallExteriorCellResidencySave? CaptureExteriorResidency() =>
        _exteriorResidency is { IsInitialized: true }
            ? _exteriorResidency.Capture()
            : null;

    /// <summary>Restores the saved exterior window after the owning SpatialSession is admitted.</summary>
    internal DaggerfallExteriorCellResidencyUpdate RestoreExteriorResidency(
        DaggerfallExteriorCellResidencySave save)
    {
        RequireExteriorProfile();
        DaggerfallExteriorCellResidency residency = EnsureExteriorResidency();
        DaggerfallExteriorCellResidencyUpdate update = residency.Restore(save);
        ReconcileExteriorTerrainAppearance(residency);
        return update;
    }

    /// <summary>
    /// Removes product-owned exterior colliders before Spatial ReplaceContentArtifact replaces the
    /// complete static collision set. A null result means this session had no exterior window.
    /// </summary>
    internal DaggerfallExteriorCellResidencyUpdate? ClearExteriorResidency()
    {
        DaggerfallExteriorCellResidencyUpdate? update = _exteriorResidency is { IsInitialized: true } residency
            ? residency.Clear()
            : null;
        _exteriorTerrainAppearance?.Clear();
        return update;
    }

    /// <summary>
    /// Applies a committed Engine WorldOrigin rebase to C# authoritative player/actor poses and
    /// reprojects the exterior window in the new local frame. The Engine receipt is expected to have
    /// already shifted its retained static colliders; Update then supplies the same absolute local
    /// translations for resident terrain and admits/removes cells around the active site.
    /// </summary>
    internal DaggerfallExteriorCellResidencyUpdate ApplyExteriorOriginCommit(
        WorldOriginCommitReceipt receipt)
    {
        RequireExteriorProfile();
        DaggerfallExteriorCellResidency residency = EnsureExteriorResidency();
        if (!residency.IsInitialized)
            throw new InvalidOperationException("An exterior WorldOrigin commit requires admitted exterior residency.");

        Vector3 localDelta = DaggerfallExteriorSessionOrigin.LocalDelta(receipt);
        if (localDelta != Vector3.Zero)
        {
            ShiftPlayer(localDelta);
            foreach (ActorState actor in State.Actors.All)
            {
                ActorPose pose = actor.Pose;
                actor.ApplyPose(new ActorPose(
                    DaggerfallExteriorSessionOrigin.Shift(pose.Position, localDelta),
                    pose.HeadingYawRadians));
            }

            DaggerfallExteriorWorldOrigin prior = residency.Origin;
            residency.AdoptRebasedOrigin(prior with { Compensation = prior.Compensation + localDelta });
        }

        // The camera is Engine-owned but its descriptor is derived from the product player pose.
        // Refresh it after shifting that pose, before the caller publishes the next presentation
        // snapshot. This is safe only as part of a product-wide rebase contract; doors, actions,
        // and portals still need corresponding authoritative rebase ownership before this method
        // can be used as a live WorldOrigin commit caller.
        _camera.Update(State.PlayerControl);
        DaggerfallExteriorCellResidencyUpdate update = residency.Update(CurrentExteriorCell(), residency.Origin);
        ReconcileExteriorTerrainAppearance(residency);
        return update;
    }

    /// <summary>Releases retained terrain visuals after the complete appearance snapshot is empty.</summary>
    internal void DisposeExteriorAppearance()
    {
        if (_siteProjection is not null)
            _siteProjection.Appearance.SetSnapshotSupplement(null, null);
        _exteriorTerrainAppearance?.Dispose();
        _exteriorTerrainAppearance = null;
    }

    private DaggerfallExteriorCellResidency EnsureExteriorResidency()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_exteriorResidency is not null) return _exteriorResidency;
        Dictionary<DaggerfallExteriorCellId, DaggerfallSiteExterior> exteriors = _definitions.Locations.Records
            .Where(site => site.Exterior is not null)
            .ToDictionary(site => new DaggerfallExteriorCellId(site.Exterior!.MapPixelX, site.Exterior.MapPixelY),
                site => site.Exterior!);
        Func<DaggerfallExteriorCellId, DaggerfallTerrainSurface> surfaceFactory = cell =>
        {
            DaggerfallTerrainSurface surface = DaggerfallTerrainSurfaceBuilder.Build(_definitions.Terrain, cell.X, cell.Y);
            if (!exteriors.TryGetValue(cell, out DaggerfallSiteExterior? exterior)) return surface;
            return DaggerfallTerrainSurfaceBuilder.ApplyLocationFlattening(surface,
                new DaggerfallTerrainLocationFlattening(exterior.MinX, exterior.MaxX, exterior.MinY, exterior.MaxY));
        };
        _exteriorSurfaceFactory = surfaceFactory;
        return _exteriorResidency = new DaggerfallExteriorCellResidency(
            _spatialService,
            _spatial.Session,
            new DaggerfallExteriorWorldBounds(_definitions.Terrain.Width, _definitions.Terrain.Height),
            surfaceFactory);
    }

    private void ReconcileExteriorTerrainAppearance(DaggerfallExteriorCellResidency residency)
    {
        if (!residency.IsInitialized) return;
        Func<DaggerfallExteriorCellId, DaggerfallTerrainSurface> surfaceFactory = _exteriorSurfaceFactory
            ?? throw new InvalidOperationException("Exterior terrain surface factory was not initialized.");
        DaggerfallExteriorTerrainAppearance appearance =
            _exteriorTerrainAppearance ??= new DaggerfallExteriorTerrainAppearance(_engine.Graphics);
        _appearance.SetSnapshotSupplement(appearance.AppendFacts, appearance.CompleteAcceptedSnapshot);
        appearance.Reconcile(residency.ResidentCells, residency.Origin, surfaceFactory);
    }

    private void RequireExteriorProfile()
    {
        if (_activeProfileKey.Kind != DaggerfallWorldProfileKind.Exterior)
        {
            throw new InvalidOperationException(
                $"Exterior residency belongs only to an exterior profile; active profile is '{_activeProfileKey.LogicalId}'.");
        }
    }

    private void ShiftPlayer(Vector3 localDelta)
    {
        if (State.PlayerControl.Position is WorldPoint position)
        {
            State.PlayerControl.MoveTo(
                DaggerfallExteriorSessionOrigin.Shift(position, localDelta).ToVector());
        }
    }
}

/// <summary>Pure product-side interpretation of the Engine WorldOrigin commit receipt.</summary>
internal static class DaggerfallExteriorSessionOrigin
{
    /// <summary>
    /// Engine retains global positions while changing local origin. Existing local transforms move by
    /// the previous cell minus the committed cell, in the same world-unit frame used by Spatial.
    /// </summary>
    internal static Vector3 LocalDelta(WorldOriginCommitReceipt receipt) => new(
        CellDelta(receipt.OriginBeforeCellX, receipt.OriginAfterCellX),
        CellDelta(receipt.OriginBeforeCellY, receipt.OriginAfterCellY),
        CellDelta(receipt.OriginBeforeCellZ, receipt.OriginAfterCellZ));

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

    private static float CellDelta(long before, long after)
    {
        double delta = (double)before - after;
        if (!double.IsFinite(delta) || delta < float.MinValue || delta > float.MaxValue)
            throw new InvalidOperationException("WorldOrigin cell delta cannot be represented by product local coordinates.");
        return (float)delta;
    }
}
