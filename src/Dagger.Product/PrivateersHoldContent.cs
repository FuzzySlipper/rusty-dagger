using System.Text;
using System.Text.Json;
using Rusty.Engine.Native;

namespace RustyDagger.Product;

/// <summary>Owns the product interpretation of the admitted Privateer's Hold inputs.</summary>
public static unsafe class PrivateersHoldContent
{
    private const string ProjectPath = "projects/privateers-hold.project.json";
    private const string NavgridPath = "projects/privateers-hold.navgrid.json";
    private const string StaticMeshPath = "imported/privateers-hold.static-mesh.json";

    public static PrivateersHoldInputs Read(NativeProductCreateArgs* args)
    {
        if (args is null) return PrivateersHoldInputs.Unavailable;

        var files = CopyKnownFiles(args);
        return files.TryGetValue(ProjectPath, out var project)
            && files.TryGetValue(NavgridPath, out var navgrid)
            && files.TryGetValue(StaticMeshPath, out var mesh)
            ? new PrivateersHoldInputs(ReadProject(project), ReadNavigation(navgrid), ReadCollision(mesh), StaticMeshPath)
            : PrivateersHoldInputs.Unavailable;
    }

    private static Dictionary<string, byte[]> CopyKnownFiles(NativeProductCreateArgs* args)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        if (args->content is null) return files;

        for (nuint index = 0; index < args->content_len; index++)
        {
            var file = args->content[index];
            if (file.path is null || file.bytes is null || file.path_len == 0) continue;
            var path = Encoding.UTF8.GetString(new ReadOnlySpan<byte>(file.path, checked((int)file.path_len)));
            if (path is not (ProjectPath or NavgridPath or StaticMeshPath)) continue;
            files[path] = new ReadOnlySpan<byte>(file.bytes, checked((int)file.bytes_len)).ToArray();
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

    private static NativeVec2 ReadVec2(JsonElement value) => new() { x = value[0].GetSingle(), y = value[1].GetSingle() };

    private static NativePlanarNavCell[] ReadNavigation(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes);
        var cells = document.RootElement.GetProperty("cells");
        var result = new NativePlanarNavCell[cells.GetArrayLength()];
        var index = 0;
        foreach (var cell in cells.EnumerateArray())
        {
            // Authored order is [x, z, level, supportY]; Engine planar navigation uses x, level, z.
            result[index++] = new NativePlanarNavCell { x = cell[0].GetInt64(), y = cell[2].GetInt64(), z = cell[1].GetInt64() };
        }
        return result;
    }

    private static CollisionMesh ReadCollision(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes);
        var source = document.RootElement.GetProperty("payload").GetProperty("source");
        var positions = source.GetProperty("positions");
        var vertices = new NativeVec3[positions.GetArrayLength() / 3];
        for (var index = 0; index < vertices.Length; index++)
        {
            var offset = index * 3;
            vertices[index] = new NativeVec3 { x = positions[offset].GetSingle(), y = positions[offset + 1].GetSingle(), z = positions[offset + 2].GetSingle() };
        }

        var indices = source.GetProperty("indices");
        var triangles = new NativeTriangle[indices.GetArrayLength() / 3];
        for (var index = 0; index < triangles.Length; index++)
        {
            var offset = index * 3;
            triangles[index] = new NativeTriangle { a = indices[offset].GetUInt32(), b = indices[offset + 1].GetUInt32(), c = indices[offset + 2].GetUInt32() };
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

public sealed class PrivateersHoldInputs(ProjectFacts project, NativePlanarNavCell[] navigation, CollisionMesh collision, string? staticMeshContentPath)
{
    public static readonly PrivateersHoldInputs Unavailable = new(new ProjectFacts(null, new Dictionary<long, AuthoredActor>()), [], new CollisionMesh([], []), null);
    public ProjectFacts Project { get; } = project;
    public NativePlanarNavCell[] Navigation { get; } = navigation;
    public CollisionMesh Collision { get; } = collision;
    public string? StaticMeshContentPath { get; } = staticMeshContentPath;
}

public sealed record ProjectFacts(WorldPoint? PlayerPosition, IReadOnlyDictionary<long, AuthoredActor> Actors);
public sealed record AuthoredActor(long EntityId, string Name, WorldPoint Position, AuthoredSprite? Sprite);
public sealed record SpriteAsset(string Path, NativeVec2 UvMin, NativeVec2 UvMax);
public sealed record AuthoredSprite(string TexturePath, NativeVec2 UvMin, NativeVec2 UvMax, NativeVec2 Pivot, NativeVec2 Size, uint BillboardMode);
public sealed record CollisionMesh(NativeVec3[] Vertices, NativeTriangle[] Triangles);
