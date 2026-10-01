using Rusty.Engine;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

internal sealed record DaggerfallMapArea(string Id, float MinX, float MinZ, float MaxX, float MaxZ, float MinY, float MaxY, int Kind);
internal sealed record DaggerfallMapLabel(string Id, string Name, float X, float Y, float Z, bool Selected = false);
internal sealed record DaggerfallMapPresentation(string Id, string Name, string Kind, WorldPoint Player, float Yaw,
    IReadOnlyList<DaggerfallMapArea> Areas, IReadOnlyList<DaggerfallMapLabel> Labels, int? Region = null, int? Location = null);

/// <summary>Read-only presentation of the discovery owner, with no access to undiscovered placement data.</summary>
internal static class DaggerfallMapProjection
{
    internal static DaggerfallMapPresentation Dungeon(DaggerfallSiteProfile profile, DaggerfallDungeonDiscovery discovery, WorldPoint player, float yaw, string? name = null)
    {
        DaggerfallDungeonMapContent content = profile.DungeonMap ?? throw new InvalidOperationException("Dungeon has no normalized map content.");
        DaggerfallDungeonDiscoverySnapshot known = discovery.Capture();
        List<DaggerfallMapArea> areas = [];
        foreach (string id in known.DiscoveredPlacementIds)
        {
            DaggerfallDungeonMapGeometry geometry = content.RequirePlacement(id);
            areas.Add(new(id, geometry.BoundsMin.X, geometry.BoundsMin.Z, geometry.BoundsMax.X, geometry.BoundsMax.Z,
                geometry.BoundsMin.Y, geometry.BoundsMax.Y, geometry.DoorId is null ? 1 : 2));
        }
        foreach (DaggerfallDungeonSurfaceCell cell in known.DiscoveredSurfaceCells)
        {
            float size = DaggerfallDungeonSurfaceCell.Size;
            areas.Add(new($"surface:{cell.X}:{cell.Y}:{cell.Z}", cell.X * size, cell.Z * size, (cell.X + 1) * size, (cell.Z + 1) * size,
                cell.Y * size, (cell.Y + 1) * size, 3));
        }
        List<DaggerfallMapLabel> labels = [];
        foreach (string id in known.DiscoveredMarkerIds)
        {
            DaggerfallDungeonMapMarker marker = content.RequireMarker(id);
            labels.Add(new(id, marker.Kind.ToString(), marker.Position.X, marker.Position.Y, marker.Position.Z));
        }
        labels.AddRange(known.NoteMarkers.Select(note => new DaggerfallMapLabel(note.Id, note.Text, note.Position.X, note.Position.Y, note.Position.Z)));
        return new(profile.ProfileKey.LogicalId, name ?? profile.ProfileKey.LogicalId, "dungeon", player, yaw, areas, labels);
    }

    internal static DaggerfallMapPresentation City(DaggerfallSiteContext site, WorldPoint player, float yaw)
    {
        DaggerfallSiteRecord current = site.ActiveSite ?? throw new InvalidOperationException("City map requires an active site.");
        DaggerfallSiteExterior exterior = current.Exterior ?? throw new InvalidOperationException("Site has no normalized exterior layout.");
        DaggerfallBlocksSnapshot blocks = site.Blocks ?? throw new InvalidOperationException("City map block content has not been admitted.");
        List<DaggerfallMapArea> areas = [];
        foreach (DaggerfallSiteBlock placement in exterior.Blocks)
        {
            DaggerfallCityBlockMap block = blocks.Maps[placement.SourceName];
            float x = placement.X * block.BlockSize, z = -placement.Y * block.BlockSize;
            int index = 0;
            foreach (DaggerfallCityFootprint rect in block.Footprints)
                areas.Add(new($"{placement.X}/{placement.Y}/{index++}", x + rect.MinX, z + rect.MinZ, x + rect.MaxX, z + rect.MaxZ, 0, 0, rect.Kind));
        }
        List<DaggerfallMapLabel> labels = [];
        foreach (DaggerfallSiteBuildingSource source in site.BuildingsAt(current.Id))
        {
            DaggerfallSiteBuildingRecord building = site.RequireBuilding(current.Id, source.Id);
            if (string.IsNullOrWhiteSpace(building.Name)) continue;
            WorldPoint point = BuildingPosition(site, current.Id, source.Id);
            labels.Add(new(source.Id.ToString(), building.Name, point.X, point.Y, point.Z, site.SelectedBuilding == (current.Id, source.Id)));
        }
        return new(current.Id.ToString(), current.Name, "city", player, yaw, areas, labels, current.Region, current.Index);
    }

    internal static WorldPoint BuildingPosition(DaggerfallSiteContext site, DaggerfallSiteId location, DaggerfallSiteBuildingId building)
    {
        DaggerfallSiteBuildingSource source = site.RequireBuildingSource(location, building);
        DaggerfallBlocksSnapshot blocks = site.Blocks ?? throw new InvalidOperationException("City map blocks have not been admitted.");
        WorldPoint point = blocks.RmbBuildings[source.Source.Id].MapPosition;
        float size = blocks.Maps[source.Source.Id.SourceKey].BlockSize;
        return new(point.X + building.BlockX * size, point.Y, point.Z - building.BlockY * size);
    }

    internal static uint Wire(UiValueBuilder builder, DaggerfallMapPresentation map) => builder.Object(
        ("id", builder.String(map.Id)), ("name", builder.String(map.Name)), ("kind", builder.String(map.Kind)),
        ("region", map.Region is int region ? builder.Number(region) : builder.Null()),
        ("location", map.Location is int location ? builder.Number(location) : builder.Null()),
        ("player", builder.Object(("x", builder.Number(map.Player.X)), ("y", builder.Number(map.Player.Y)), ("z", builder.Number(map.Player.Z)), ("yaw", builder.Number(map.Yaw)))),
        ("areas", builder.Array(map.Areas.Select(area => builder.Object(("id", builder.String(area.Id)),
            ("minX", builder.Number(area.MinX)), ("minZ", builder.Number(area.MinZ)), ("maxX", builder.Number(area.MaxX)), ("maxZ", builder.Number(area.MaxZ)),
            ("minY", builder.Number(area.MinY)), ("maxY", builder.Number(area.MaxY)), ("kind", builder.Number(area.Kind)))).ToArray())),
        ("labels", builder.Array(map.Labels.Select(label => builder.Object(("id", builder.String(label.Id)), ("name", builder.String(label.Name)),
            ("x", builder.Number(label.X)), ("y", builder.Number(label.Y)), ("z", builder.Number(label.Z)), ("selected", builder.Boolean(label.Selected)))).ToArray())));
}
