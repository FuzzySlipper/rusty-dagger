using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.World;

/// <summary>PlayerEnterExit's closest-marker/closest-door arrival, in the interior's own frame.</summary>
internal static class DaggerfallBuildingArrival
{
    internal static DaggerfallSiteAnchor Landing(DaggerfallSiteProfile interior, Vector3 exteriorDoor,
        CharacterShapeConfig shape)
    {
        WorldPoint marker = interior.Anchors.TryGetValue(DaggerfallLocationAssembly.EnterAnchor, out var enter)
            ? enter.Position : interior.Project.PlayerPosition ?? default;
        float yaw = interior.InitialLook.YawRadians;
        if (interior.InteriorBuilding is { EntryFrame: { } frame } building)
        {
            const float blockSide = DaggerfallTerrainSurfaceBuilder.HorizontalSize / 8f;
            Vector3 origin = frame.ExteriorOrigin + new Vector3(building.BlockX * blockSide, 0f, -building.BlockY * blockSide);
            Vector3 check = Vector3.Transform(exteriorDoor - origin,
                Quaternion.CreateFromAxisAngle(Vector3.UnitY, -frame.ExteriorYawDegrees * MathF.PI / 180f));
            if (frame.Markers.Count > 0)
            {
                marker = frame.Markers.MinBy(candidate => Vector3.DistanceSquared(candidate.ToVector(), check));
                check = marker.ToVector();
            }
            DaggerfallBuildingEntryDoor? door = frame.Doors.MinBy(door => Vector3.DistanceSquared(door.Position.ToVector(), check));
            if (door is { Normal: var inward })
                return new("building-entry", WorldPoint.From(door.Position.ToVector() + inward * (shape.Radius + .4f)),
                    ActorHeading.Yaw(inward), 0f);
        }
        // The source fallback clears the marker by 0.6 controller heights, then stands the player.
        return new("building-entry", marker with { Y = marker.Y + shape.StandingHeight * .6f }, yaw, 0f);
    }
}
