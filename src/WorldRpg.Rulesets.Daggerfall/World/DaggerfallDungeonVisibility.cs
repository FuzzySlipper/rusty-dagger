using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.World;

/// <summary>Admits exploration from the current Engine scene, never from the map's source bounds alone.</summary>
internal sealed class DaggerfallDungeonVisibility
{
    private const ulong ScanStepInterval = 12;
    private const float DownDistance = 3f;
    private const float ForwardDistance = 30f;
    private const float RaySeparation = .1f;
    private readonly SpatialMovementSystem _spatial;
    private readonly float _surfaceTolerance;

    internal DaggerfallDungeonVisibility(SpatialMovementSystem spatial, double collisionVoxelSize)
    {
        _spatial = spatial ?? throw new ArgumentNullException(nameof(spatial));
        if (!double.IsFinite(collisionVoxelSize) || collisionVoxelSize <= 0d || collisionVoxelSize > float.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(collisionVoxelSize));
        _surfaceTolerance = (float)collisionVoxelSize;
    }

    internal void Observe(DaggerfallDungeonDiscovery discovery, PlayerControlState player,
        DaggerfallDoorRuntime doors, CharacterStepEnvironment environment, ulong simulationStep, float eyeHeight)
    {
        ArgumentNullException.ThrowIfNull(discovery);
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(doors);
        if (simulationStep % ScanStepInterval != 0 || player.Position is not WorldPoint position) return;
        if (!float.IsFinite(eyeHeight)) throw new ArgumentOutOfRangeException(nameof(eyeHeight));

        Vector3 origin = position.ToVector() + Vector3.UnitY * eyeHeight;
        (float sinYaw, float cosYaw) = MathF.SinCos(player.YawRadians);
        (float sinPitch, float cosPitch) = MathF.SinCos(player.PitchRadians);
        Vector3 forward = new(sinYaw * cosPitch, sinPitch, -cosYaw * cosPitch);
        Vector3 right = new(cosYaw, 0f, sinYaw);
        Dictionary<ulong, DaggerfallRdbDoorId> doorsByEntity = doors.All
            .ToDictionary(view => view.Entity.Value, view => view.Id);

        ScanTriplet(discovery, origin, -Vector3.UnitY, DownDistance, right, environment, doorsByEntity);
        ScanTriplet(discovery, origin, forward, ForwardDistance, right, environment, doorsByEntity);
        SpatialHit forwardStop = _spatial.CastRay(origin, forward, ForwardDistance, environment);
        float clearDistance = forwardStop.Present ? MathF.Min((float)forwardStop.Distance, ForwardDistance) : ForwardDistance;
        for (float distance = 3f; distance < clearDistance; distance += 3f)
            ScanTriplet(discovery, origin + forward * distance, -Vector3.UnitY, DownDistance,
                right, environment, doorsByEntity);

        foreach (DaggerfallDungeonMapMarker marker in discovery.Content.Markers)
        {
            if (discovery.WasMarkerVisitedThisEntry(marker.Id)) continue;
            Vector3 target = marker.Position.ToVector();
            Vector3 delta = target - origin;
            float distance = delta.Length();
            if (distance > ForwardDistance || distance < .001f) continue;
            SpatialHit blocker = _spatial.CastRay(origin, delta / distance, distance, environment);
            if (!blocker.Present || blocker.Distance >= distance - _surfaceTolerance)
                discovery.RevealMarker(marker.Id);
        }
    }

    private void ScanTriplet(DaggerfallDungeonDiscovery discovery, Vector3 origin, Vector3 direction,
        float distance, Vector3 right, CharacterStepEnvironment environment,
        IReadOnlyDictionary<ulong, DaggerfallRdbDoorId> doorsByEntity)
    {
        string? placement = null;
        DaggerfallRdbDoorId? door = null;
        Vector3? surfacePoint = null;
        Vector3? surfaceNormal = null;
        bool sourceUnambiguous = true;
        Vector3 secondOffset = Vector3.Normalize(Vector3.Cross(direction, right));
        for (int index = 0; index < 3; index++)
        {
            Vector3 rayOrigin = origin + (index switch
            {
                1 => right * RaySeparation,
                2 => secondOffset * RaySeparation,
                _ => Vector3.Zero,
            });
            SpatialHit hit = _spatial.CastRay(rayOrigin, direction, distance, environment);
            if (!hit.Present) return;
            if (hit.Kind == SpatialHitKind.Entity && doorsByEntity.TryGetValue(hit.Entity, out DaggerfallRdbDoorId doorId))
            {
                if (surfacePoint is not null) return;
                if (index == 0) door = doorId;
                else if (door != doorId) return;
                continue;
            }
            if (door is not null || hit.Kind != SpatialHitKind.StaticMesh) return;
            if (surfacePoint is { } firstPoint)
            {
                if (Vector3.DistanceSquared(firstPoint, hit.Point) > .25f
                    || Vector3.Dot(surfaceNormal!.Value, hit.Normal) < .9f) return;
            }
            else
            {
                surfacePoint = hit.Point;
                surfaceNormal = hit.Normal;
            }
            DaggerfallDungeonMapGeometry? geometry = PlacementAt(discovery.Content, hit.Point);
            if (index == 0) placement = geometry?.PlacementId;
            else if (placement != geometry?.PlacementId) sourceUnambiguous = false;
        }
        if (door is { } seenDoor) discovery.ObserveDoor(seenDoor);
        else if (surfacePoint is { } seenSurface)
        {
            discovery.ObserveSurface(seenSurface);
            if (sourceUnambiguous && placement is not null) discovery.ObservePlacement(placement);
        }
    }

    private DaggerfallDungeonMapGeometry? PlacementAt(DaggerfallDungeonMapContent content, Vector3 point)
    {
        // The Engine ray reports a surface point, not the source placement ID. An
        // overlap is ambiguous even when one source sample happens to be nearer.
        DaggerfallDungeonMapGeometry? matched = null;
        foreach (DaggerfallDungeonMapGeometry geometry in content.GeometryPlacements.Where(geometry =>
                point.X >= geometry.BoundsMin.X - _surfaceTolerance
                && point.X <= geometry.BoundsMax.X + _surfaceTolerance
                && point.Y >= geometry.BoundsMin.Y - _surfaceTolerance
                && point.Y <= geometry.BoundsMax.Y + _surfaceTolerance
                && point.Z >= geometry.BoundsMin.Z - _surfaceTolerance
                && point.Z <= geometry.BoundsMax.Z + _surfaceTolerance))
        {
            if (matched is not null) return null;
            matched = geometry;
        }
        return matched;
    }
}
