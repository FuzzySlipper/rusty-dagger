using System.Text;
using System.Text.Json;
using Rusty.Engine.Native;

namespace RustyDagger.Product;

/// <summary>Copies the one admitted project input while creation borrows it, retaining positions only.</summary>
public static unsafe class PrivateersHoldContent
{
    private const string ProjectPath = "projects/privateers-hold.project.json";

    public static PrivateersHoldPositions Read(NativeProductCreateArgs* args)
    {
        if (args is null || args->content is null) return PrivateersHoldPositions.Unavailable;
        for (nuint index = 0; index < args->content_len; index++)
        {
            var file = args->content[index];
            if (file.path_len == 0 || file.path is null || file.bytes_len == 0 || file.bytes is null) continue;
            var path = Encoding.UTF8.GetString(new ReadOnlySpan<byte>(file.path, checked((int)file.path_len)));
            if (!StringComparer.Ordinal.Equals(path, ProjectPath)) continue;
            var bytes = new ReadOnlySpan<byte>(file.bytes, checked((int)file.bytes_len)).ToArray();
            return ReadProject(bytes);
        }
        return PrivateersHoldPositions.Unavailable;
    }

    private static PrivateersHoldPositions ReadProject(byte[] bytes)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes);
            WorldPoint? player = null;
            var actors = new Dictionary<long, WorldPoint>();
            ReadNode(document.RootElement, ref player, actors);
            return new PrivateersHoldPositions(player, actors);
        }
        catch (JsonException)
        {
            return PrivateersHoldPositions.Unavailable;
        }
    }

    private static void ReadNode(JsonElement node, ref WorldPoint? player, IDictionary<long, WorldPoint> actors)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            var position = ReadTranslation(node);
            if (position is WorldPoint authoredPosition)
            {
                if (node.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String && name.GetString() == "player")
                    player = authoredPosition;
                if (node.TryGetProperty("id", out var id) && id.TryGetInt64(out var entityId) && (entityId == 2007 || entityId == 2000))
                    actors[entityId] = authoredPosition;
            }
            foreach (var property in node.EnumerateObject()) ReadNode(property.Value, ref player, actors);
        }
        else if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in node.EnumerateArray()) ReadNode(item, ref player, actors);
        }
    }

    private static WorldPoint? ReadTranslation(JsonElement node)
    {
        if (!node.TryGetProperty("translation", out var translation) || translation.ValueKind != JsonValueKind.Array || translation.GetArrayLength() != 3)
            return null;
        return translation[0].TryGetSingle(out var x) && translation[1].TryGetSingle(out var y) && translation[2].TryGetSingle(out var z)
            ? new WorldPoint(x, y, z)
            : null;
    }
}

public sealed class PrivateersHoldPositions(WorldPoint? player, IReadOnlyDictionary<long, WorldPoint> actors)
{
    public static readonly PrivateersHoldPositions Unavailable = new(null, new Dictionary<long, WorldPoint>());
    public WorldPoint? Player { get; } = player;
    public WorldPoint? ForEntity(long entityId) => actors.TryGetValue(entityId, out var position) ? position : null;
}
