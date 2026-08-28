using System.Numerics;
using System.Text;
using System.Text.Json;
using Rusty.Engine;

using RustyDagger.Game.Modules.PlayerControl;

namespace RustyDagger.Game.Daggerfall.Content;

/// <summary>Owns the product interpretation of the admitted Privateer's Hold inputs.</summary>
internal static class PrivateersHoldContent
{
    private const string ProjectPath = "projects/privateers-hold.project.json";
    private const string NavgridPath = "projects/privateers-hold.navgrid.json";
    private const string StaticMeshPath = "imported/privateers-hold.static-mesh.json";

    public static PrivateersHoldInputs Read(ProductContent content)
    {
        var files = CopyKnownFiles(content);
        return files.TryGetValue(ProjectPath, out var project)
            && files.TryGetValue(NavgridPath, out var navgrid)
            && files.TryGetValue(StaticMeshPath, out var mesh)
            ? new PrivateersHoldInputs(ReadProject(project), ReadNavigation(navgrid), ReadCollision(mesh), StaticMeshPath)
            : PrivateersHoldInputs.Unavailable;
    }

    private static Dictionary<string, byte[]> CopyKnownFiles(ProductContent content)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var file in content.Files.Span)
        {
            if (file.Path.IsEmpty) continue;
            var path = Encoding.UTF8.GetString(file.Path.Span);
            if (path is not (ProjectPath or NavgridPath or StaticMeshPath)) continue;
            files[path] = file.Bytes.ToArray();
        }
        return files;
    }

    private static ProjectFacts ReadProject(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes);
        var sprites = ReadSpriteAssets(document.RootElement);
        WorldPoint? player = null;
        var actors = new Dictionary<long, AuthoredActor>();
        foreach (var scene in document.RootElement.GetProperty("scenes").EnumerateArray())
        foreach (var entity in scene.GetProperty("entities").EnumerateArray())
        {
            if (!TryPosition(entity, out var position)) continue;
            if (entity.TryGetProperty("name", out var name) && name.GetString() == "player") player = position;
            if (entity.TryGetProperty("id", out var id)
                && id.TryGetInt64(out var entityId)
                && entity.TryGetProperty("name", out name)
                && name.GetString() is { } actorName
                && actorName.StartsWith("enemy-", StringComparison.Ordinal))
                actors[entityId] = new AuthoredActor(entityId, actorName, position, ReadSprite(entity, sprites));
        }
        return new ProjectFacts(player, actors);
    }

    private static Dictionary<string, SpriteAsset> ReadSpriteAssets(JsonElement project)
    {
        var result = new Dictionary<string, SpriteAsset>(StringComparer.Ordinal);
        foreach (var asset in project.GetProperty("assets").EnumerateArray())
        {
            if (!asset.TryGetProperty("id", out var id) || !asset.TryGetProperty("texture", out var texture)
                || !texture.TryGetProperty("spriteAtlas", out var atlas) || !atlas.TryGetProperty("frames", out var frames)) continue;
            var frame = frames.EnumerateArray().FirstOrDefault(value => value.GetProperty("frame").GetInt32() == 0);
            if (frame.ValueKind == JsonValueKind.Undefined || !asset.TryGetProperty("catalog", out var catalog)
                || !catalog.TryGetProperty("sourcePath", out var sourcePath) || sourcePath.GetString() is not { } path) continue;
            result[id.GetString()!] = new SpriteAsset(path.StartsWith("content/", StringComparison.Ordinal) ? path[8..] : path, ReadVec2(frame.GetProperty("uvMin")), ReadVec2(frame.GetProperty("uvMax")));
        }
        return result;
    }

    private static AuthoredSprite? ReadSprite(JsonElement entity, IReadOnlyDictionary<string, SpriteAsset> assets)
    {
        if (!entity.TryGetProperty("sprite", out var sprite) || !sprite.TryGetProperty("asset", out var asset)
            || asset.GetString() is not { } assetId || !assets.TryGetValue(assetId, out var spriteAsset)) return null;
        var billboardMode = sprite.TryGetProperty("billboard", out var billboard) ? billboard.GetString() switch
        {
            "spherical" => 1u,
            "cylindrical" => 2u,
            _ => 0u,
        } : 0u;
        return new AuthoredSprite(spriteAsset.Path, spriteAsset.UvMin, spriteAsset.UvMax, ReadVec2(sprite.GetProperty("pivot")), ReadVec2(sprite.GetProperty("size")), billboardMode);
    }

    private static Vector2 ReadVec2(JsonElement value) => new(value[0].GetSingle(), value[1].GetSingle());

    private static PlanarNavCell[] ReadNavigation(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes);
        var cells = document.RootElement.GetProperty("cells");
        var result = new PlanarNavCell[cells.GetArrayLength()];
        var index = 0;
        foreach (var cell in cells.EnumerateArray())
        {
            // Authored order is [x, z, level, supportY]; Engine planar navigation uses x, level, z.
            result[index++] = new PlanarNavCell(cell[0].GetInt64(), cell[2].GetInt64(), cell[1].GetInt64());
        }
        return result;
    }

    private static CollisionMesh ReadCollision(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes);
        var source = document.RootElement.GetProperty("payload").GetProperty("source");
        var positions = source.GetProperty("positions");
        var vertices = new Vector3[positions.GetArrayLength() / 3];
        for (var index = 0; index < vertices.Length; index++)
        {
            var offset = index * 3;
            vertices[index] = new Vector3(positions[offset].GetSingle(), positions[offset + 1].GetSingle(), positions[offset + 2].GetSingle());
        }

        var indices = source.GetProperty("indices");
        var triangles = new Triangle[indices.GetArrayLength() / 3];
        for (var index = 0; index < triangles.Length; index++)
        {
            var offset = index * 3;
            triangles[index] = new Triangle(indices[offset].GetUInt32(), indices[offset + 1].GetUInt32(), indices[offset + 2].GetUInt32());
        }
        return new CollisionMesh(vertices, triangles);
    }

    private static bool TryPosition(JsonElement entity, out WorldPoint position)
    {
        position = default;
        if (!entity.TryGetProperty("translation", out var translation) || translation.GetArrayLength() != 3) return false;
        position = new WorldPoint(translation[0].GetSingle(), translation[1].GetSingle(), translation[2].GetSingle());
        return true;
    }
}

internal sealed class PrivateersHoldInputs(ProjectFacts project, PlanarNavCell[] navigation, CollisionMesh collision, string? staticMeshContentPath)
{
    internal static readonly PrivateersHoldInputs Unavailable = new(new ProjectFacts(null, new Dictionary<long, AuthoredActor>()), [], new CollisionMesh([], []), null);
    internal ProjectFacts Project { get; } = project;
    internal PlanarNavCell[] Navigation { get; } = navigation;
    internal CollisionMesh Collision { get; } = collision;
    internal string? StaticMeshContentPath { get; } = staticMeshContentPath;
}

internal sealed record ProjectFacts(WorldPoint? PlayerPosition, IReadOnlyDictionary<long, AuthoredActor> Actors);
internal sealed record AuthoredActor(long EntityId, string Name, WorldPoint Position, AuthoredSprite? Sprite);
internal sealed record SpriteAsset(string Path, Vector2 UvMin, Vector2 UvMax);
internal sealed record AuthoredSprite(string TexturePath, Vector2 UvMin, Vector2 UvMax, Vector2 Pivot, Vector2 Size, uint BillboardMode);
internal sealed record CollisionMesh(Vector3[] Vertices, Triangle[] Triangles);
