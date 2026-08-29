using System.Collections.ObjectModel;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Rusty.Engine;
using WorldRpg.Kit.Controls;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>Reads authored Privateer's Hold scenario facts; no project entity names participate in runtime selection.</summary>
internal static class PrivateersHoldContent
{
    private const int SchemaVersion = 1;

    internal static PrivateersHoldInputs Read(ProductContent content, ReadOnlyMemory<byte> payload, DaggerfallDefinitions definitions)
    {
        DaggerfallContentDiagnostics diagnostics = new();
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            JsonElement root = DaggerfallBaseContent.Object(document.RootElement, "root", diagnostics);
            if (DaggerfallBaseContent.Text(root, "ruleset", diagnostics) != DaggerfallRuleset.Identity.Value) diagnostics.Add("Privateer's Hold payload must identify ruleset 'daggerfall'.");
            if (DaggerfallBaseContent.Integer(root, "schemaVersion", diagnostics) != SchemaVersion) diagnostics.Add($"Privateer's Hold payload schemaVersion must be {SchemaVersion}.");
            AdmittedFiles files = AdmittedFiles.Copy(content, diagnostics);
            ScenarioStart start = ReadStart(DaggerfallBaseContent.Object(DaggerfallBaseContent.Property(root, "startingState", diagnostics), "startingState", diagnostics), definitions, diagnostics);
            Dictionary<DaggerfallAppearanceId, AuthoredSprite> appearances = ReadAppearances(root, diagnostics);
            List<AuthoredActor> actors = ReadPlacements(root, definitions, appearances, diagnostics);
            List<ScenarioEncounter> encounters = ReadEncounters(root, actors, diagnostics);
            WorldArtifactReferences world = ReadWorld(DaggerfallBaseContent.Object(DaggerfallBaseContent.Property(root, "world", diagnostics), "world", diagnostics), diagnostics);
            ValidateAssetReferences(files, world, appearances.Values, diagnostics);
            (PlanarNavCell[] navigation, CollisionMesh collision) = ReadSpatialArtifacts(files, world, diagnostics);
            diagnostics.ThrowIfAny();
            return new PrivateersHoldInputs(new ProjectFacts(start.Position, new ReadOnlyDictionary<long, AuthoredActor>(actors.ToDictionary(actor => actor.EntityId))), navigation, collision, world.StaticMesh, world.Appearance, start.Look, Array.AsReadOnly(start.Loadout.ToArray()), Array.AsReadOnly(encounters.ToArray()));
        }
        catch (JsonException exception)
        {
            diagnostics.Add($"Privateer's Hold payload is not valid JSON: {exception.Message}");
            throw diagnostics.Exception();
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException or OverflowException && exception is not DaggerfallContentException)
        {
            diagnostics.Add($"Privateer's Hold payload is malformed: {exception.Message}");
            throw diagnostics.Exception();
        }
    }

    private static ScenarioStart ReadStart(JsonElement value, DaggerfallDefinitions definitions, DaggerfallContentDiagnostics diagnostics)
    {
        WorldPoint position = Point(DaggerfallBaseContent.Property(value, "position", diagnostics), "startingState.position", diagnostics);
        JsonElement look = DaggerfallBaseContent.Object(DaggerfallBaseContent.Property(value, "look", diagnostics), "startingState.look", diagnostics);
        PlayerInitialLook initialLook = new(DaggerfallBaseContent.Number(look, "yawRadians", diagnostics), DaggerfallBaseContent.Number(look, "pitchRadians", diagnostics));
        List<ScenarioInventoryStack> loadout = [];
        foreach (JsonElement stack in DaggerfallBaseContent.Array(value, "loadout", diagnostics))
        {
            JsonElement item = DaggerfallBaseContent.Object(stack, "loadout item", diagnostics);
            DaggerfallItemId itemId = new(DaggerfallBaseContent.Text(item, "item", diagnostics));
            int quantity = DaggerfallBaseContent.Integer(item, "quantity", diagnostics);
            if (!definitions.Items.TryGetValue(itemId, out DaggerfallItemDefinition? definition)) diagnostics.Add($"Starting loadout refers to missing item '{itemId.Value}'.");
            else if (quantity < 1 || (ulong)quantity > definition.MaximumQuantity) diagnostics.Add($"Starting loadout quantity for '{itemId.Value}' is outside its item limit.");
            loadout.Add(new(itemId, quantity > 0 ? checked((ulong)quantity) : 1));
        }
        if (loadout.Count == 0) diagnostics.Add("Privateer's Hold must define a starting loadout.");
        return new(position, initialLook, loadout);
    }

    private static Dictionary<DaggerfallAppearanceId, AuthoredSprite> ReadAppearances(JsonElement root, DaggerfallContentDiagnostics diagnostics)
    {
        Dictionary<DaggerfallAppearanceId, AuthoredSprite> result = [];
        foreach (JsonElement value in DaggerfallBaseContent.Array(root, "appearances", diagnostics))
        {
            JsonElement appearance = DaggerfallBaseContent.Object(value, "appearance", diagnostics);
            DaggerfallAppearanceId id = new(DaggerfallBaseContent.Text(appearance, "id", diagnostics));
            string texture = DaggerfallBaseContent.Text(appearance, "texture", diagnostics);
            if (!DaggerfallBaseContent.ValidId(id.Value) || string.IsNullOrWhiteSpace(texture)) diagnostics.Add("Appearance id and texture must be stable non-empty references.");
            uint billboard = DaggerfallBaseContent.Text(appearance, "billboard", diagnostics) switch { "spherical" => 1u, "cylindrical" => 2u, "none" => 0u, _ => InvalidBillboard(diagnostics) };
            Vector2 size = Vector2Value(DaggerfallBaseContent.Property(appearance, "size", diagnostics), "appearance.size", diagnostics);
            if (size.X <= 0f || size.Y <= 0f) diagnostics.Add("Appearance sprite size must be positive.");
            SpriteSizeMode sizeMode = DaggerfallBaseContent.Text(appearance, "sizeMode", diagnostics) switch { "world" => SpriteSizeMode.World, _ => InvalidSizeMode(diagnostics) };
            SpriteDepthPolicy depth = DaggerfallBaseContent.Text(appearance, "depth", diagnostics) switch { "default" => SpriteDepthPolicy.Default, _ => InvalidDepthPolicy(diagnostics) };
            RenderLayer layer = DaggerfallBaseContent.Text(appearance, "layer", diagnostics) switch { "scene" => RenderLayer.Scene, _ => InvalidLayer(diagnostics) };
            int renderOrder = DaggerfallBaseContent.Integer(appearance, "renderOrder", diagnostics);
            if (renderOrder is < -10_000 or > 10_000) diagnostics.Add("Appearance renderOrder is outside the supported range.");
            AuthoredSprite sprite = new(texture, Vector2Value(DaggerfallBaseContent.Property(appearance, "uvMin", diagnostics), "appearance.uvMin", diagnostics), Vector2Value(DaggerfallBaseContent.Property(appearance, "uvMax", diagnostics), "appearance.uvMax", diagnostics), Vector2Value(DaggerfallBaseContent.Property(appearance, "pivot", diagnostics), "appearance.pivot", diagnostics), size, billboard, ColorValue(DaggerfallBaseContent.Property(appearance, "tint", diagnostics), "appearance.tint", diagnostics), sizeMode, renderOrder, depth, Boolean(appearance, "visible", diagnostics), layer);
            if (!result.TryAdd(id, sprite)) diagnostics.Add($"Duplicate appearance '{id.Value}'.");
        }
        return result;
    }

    private static List<AuthoredActor> ReadPlacements(JsonElement root, DaggerfallDefinitions definitions, IReadOnlyDictionary<DaggerfallAppearanceId, AuthoredSprite> appearances, DaggerfallContentDiagnostics diagnostics)
    {
        List<AuthoredActor> actors = [];
        HashSet<long> entityIds = [];
        foreach (JsonElement value in DaggerfallBaseContent.Array(root, "placements", diagnostics))
        {
            JsonElement placement = DaggerfallBaseContent.Object(value, "placement", diagnostics);
            long entityId = Long(placement, "entityId", diagnostics);
            DaggerfallActorId actorId = new(DaggerfallBaseContent.Text(placement, "actor", diagnostics));
            DaggerfallAppearanceId? appearanceId = placement.TryGetProperty("appearance", out JsonElement appearance) && appearance.ValueKind != JsonValueKind.Null ? new(DaggerfallBaseContent.Text(placement, "appearance", diagnostics)) : null;
            if (entityId < 1 || !entityIds.Add(entityId)) diagnostics.Add($"Placement entity id '{entityId}' is invalid or duplicated.");
            if (!definitions.Actors.ContainsKey(actorId)) diagnostics.Add($"Placement '{entityId}' refers to missing actor '{actorId.Value}'.");
            AuthoredSprite? sprite = null;
            if (appearanceId is { } id && !appearances.TryGetValue(id, out sprite)) diagnostics.Add($"Placement '{entityId}' refers to missing appearance '{id.Value}'.");
            actors.Add(new(entityId, actorId, Point(DaggerfallBaseContent.Property(placement, "position", diagnostics), "placement.position", diagnostics), sprite));
        }
        return actors;
    }

    private static List<ScenarioEncounter> ReadEncounters(JsonElement root, IReadOnlyList<AuthoredActor> actors, DaggerfallContentDiagnostics diagnostics)
    {
        HashSet<long> placements = actors.Select(actor => actor.EntityId).ToHashSet();
        HashSet<string> ids = new(StringComparer.Ordinal);
        List<ScenarioEncounter> result = [];
        foreach (JsonElement value in DaggerfallBaseContent.Array(root, "encounters", diagnostics))
        {
            JsonElement encounter = DaggerfallBaseContent.Object(value, "encounter", diagnostics);
            string id = DaggerfallBaseContent.Text(encounter, "id", diagnostics);
            if (!ids.Add(id)) diagnostics.Add($"Duplicate encounter '{id}'.");
            long[] members = DaggerfallBaseContent.Array(encounter, "members", diagnostics).Select(member => member.TryGetInt64(out long entityId) ? entityId : InvalidMember(diagnostics)).ToArray();
            foreach (long member in members)
                if (!placements.Contains(member)) diagnostics.Add($"Encounter '{id}' refers to missing placement '{member}'.");
            result.Add(new(id, DaggerfallBaseContent.Text(encounter, "name", diagnostics), DaggerfallBaseContent.Text(encounter, "objective", diagnostics), Array.AsReadOnly(members)));
        }
        return result;
    }

    private static WorldArtifactReferences ReadWorld(JsonElement value, DaggerfallContentDiagnostics diagnostics) => new(
        DaggerfallBaseContent.Text(value, "staticMesh", diagnostics),
        DaggerfallBaseContent.Text(value, "navigation", diagnostics),
        DaggerfallBaseContent.Text(value, "collision", diagnostics),
        ReadWorldAppearance(DaggerfallBaseContent.Object(DaggerfallBaseContent.Property(value, "appearance", diagnostics), "world.appearance", diagnostics), diagnostics));

    private static AuthoredWorldAppearance ReadWorldAppearance(JsonElement value, DaggerfallContentDiagnostics diagnostics)
    {
        RenderLayer layer = DaggerfallBaseContent.Text(value, "layer", diagnostics) switch { "scene" => RenderLayer.Scene, _ => InvalidLayer(diagnostics) };
        return new(ColorValue(DaggerfallBaseContent.Property(value, "tint", diagnostics), "world.appearance.tint", diagnostics), new Transform(Vector3Value(DaggerfallBaseContent.Property(value, "position", diagnostics), "world.appearance.position", diagnostics), QuaternionValue(DaggerfallBaseContent.Property(value, "rotation", diagnostics), "world.appearance.rotation", diagnostics), Vector3Value(DaggerfallBaseContent.Property(value, "scale", diagnostics), "world.appearance.scale", diagnostics)), Boolean(value, "visible", diagnostics), layer);
    }

    private static void ValidateAssetReferences(AdmittedFiles files, WorldArtifactReferences world, IEnumerable<AuthoredSprite> sprites, DaggerfallContentDiagnostics diagnostics)
    {
        foreach (string path in new[] { world.StaticMesh, world.Navigation, world.Collision }.Concat(sprites.Select(sprite => sprite.TexturePath)))
            if (!files.ContainsExactlyOne(path)) diagnostics.Add($"Privateer's Hold asset '{path}' must occur exactly once in admitted content.");
    }

    private static (PlanarNavCell[] Navigation, CollisionMesh Collision) ReadSpatialArtifacts(AdmittedFiles files, WorldArtifactReferences references, DaggerfallContentDiagnostics diagnostics)
    {
        byte[]? navigation = files.GetExactlyOne(references.Navigation);
        byte[]? collision = files.GetExactlyOne(references.Collision);
        if (navigation is null || collision is null) return ([], new CollisionMesh([], []));
        try { return (ReadNavigation(navigation), ReadCollision(collision)); }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException or OverflowException or IndexOutOfRangeException or KeyNotFoundException or InvalidDataException) { diagnostics.Add($"Privateer's Hold spatial artifact is malformed: {exception.Message}"); return ([], new CollisionMesh([], [])); }
    }

    // These two readers isolate the current admitted artifact shapes. Their format/provenance conversion remains a Daggerfall.Import task; session construction consumes only the typed result above.
    private static PlanarNavCell[] ReadNavigation(byte[] bytes)
    {
        using JsonDocument document = JsonDocument.Parse(bytes);
        if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("cells", out JsonElement cells) || cells.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Navigation artifact requires a cells array.");
        if (cells.GetArrayLength() > 1_000_000) throw new InvalidDataException("Navigation cells exceed the supported limit.");
        PlanarNavCell[] result = new PlanarNavCell[cells.GetArrayLength()];
        int index = 0;
        foreach (JsonElement cell in cells.EnumerateArray())
        {
            if (cell.ValueKind != JsonValueKind.Array || cell.GetArrayLength() != 4 || !cell[0].TryGetInt64(out long x) || !cell[1].TryGetInt64(out long z) || !cell[2].TryGetInt64(out long level) || !cell[3].TryGetSingle(out float support) || !float.IsFinite(support) || Math.Abs(x) > 10_000_000 || Math.Abs(z) > 10_000_000 || Math.Abs(level) > 10_000_000) throw new InvalidDataException("Navigation cell must be [x, z, level, finiteSupport] within supported bounds.");
            result[index++] = new(x, level, z);
        }
        return result;
    }

    private static CollisionMesh ReadCollision(byte[] bytes)
    {
        using JsonDocument document = JsonDocument.Parse(bytes);
        if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("payload", out JsonElement payload) || payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty("source", out JsonElement source) || source.ValueKind != JsonValueKind.Object || !source.TryGetProperty("positions", out JsonElement positions) || positions.ValueKind != JsonValueKind.Array || !source.TryGetProperty("indices", out JsonElement indices) || indices.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Collision artifact requires payload.source positions and indices arrays.");
        if (positions.GetArrayLength() % 3 != 0 || indices.GetArrayLength() % 3 != 0 || positions.GetArrayLength() > 3_000_000 || indices.GetArrayLength() > 3_000_000) throw new InvalidDataException("Collision positions/indices must be bounded multiples of three.");
        Vector3[] vertices = new Vector3[positions.GetArrayLength() / 3];
        for (int index = 0; index < vertices.Length; index++)
        {
            if (!positions[index * 3].TryGetSingle(out float x) || !positions[index * 3 + 1].TryGetSingle(out float y) || !positions[index * 3 + 2].TryGetSingle(out float z) || !float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z)) throw new InvalidDataException("Collision positions must be finite numbers.");
            vertices[index] = new(x, y, z);
        }
        Triangle[] triangles = new Triangle[indices.GetArrayLength() / 3];
        for (int index = 0; index < triangles.Length; index++)
        {
            if (!indices[index * 3].TryGetUInt32(out uint a) || !indices[index * 3 + 1].TryGetUInt32(out uint b) || !indices[index * 3 + 2].TryGetUInt32(out uint c) || a >= vertices.Length || b >= vertices.Length || c >= vertices.Length) throw new InvalidDataException("Collision triangle indices must address an existing vertex.");
            triangles[index] = new(a, b, c);
        }
        return new(vertices, triangles);
    }

    private static WorldPoint Point(JsonElement value, string name, DaggerfallContentDiagnostics diagnostics)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != 3) { diagnostics.Add($"'{name}' must be a three-number position."); return default; }
        float x = NumberAt(value, 0, name, diagnostics), y = NumberAt(value, 1, name, diagnostics), z = NumberAt(value, 2, name, diagnostics);
        return new(x, y, z);
    }
    private static Vector2 Vector2Value(JsonElement value, string name, DaggerfallContentDiagnostics diagnostics)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != 2) { diagnostics.Add($"'{name}' must be a two-number vector."); return default; }
        return new(NumberAt(value, 0, name, diagnostics), NumberAt(value, 1, name, diagnostics));
    }
    private static Vector3 Vector3Value(JsonElement value, string name, DaggerfallContentDiagnostics diagnostics)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != 3) { diagnostics.Add($"'{name}' must be a three-number vector."); return default; }
        return new(NumberAt(value, 0, name, diagnostics), NumberAt(value, 1, name, diagnostics), NumberAt(value, 2, name, diagnostics));
    }
    private static Quaternion QuaternionValue(JsonElement value, string name, DaggerfallContentDiagnostics diagnostics)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != 4) { diagnostics.Add($"'{name}' must be a four-number rotation."); return Quaternion.Identity; }
        Quaternion result = new(NumberAt(value, 0, name, diagnostics), NumberAt(value, 1, name, diagnostics), NumberAt(value, 2, name, diagnostics), NumberAt(value, 3, name, diagnostics));
        if (result.LengthSquared() is < .99f or > 1.01f) diagnostics.Add($"'{name}' must be normalized.");
        return result;
    }
    private static Color ColorValue(JsonElement value, string name, DaggerfallContentDiagnostics diagnostics)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != 4) { diagnostics.Add($"'{name}' must be an RGBA color."); return default; }
        float red = NumberAt(value, 0, name, diagnostics), green = NumberAt(value, 1, name, diagnostics), blue = NumberAt(value, 2, name, diagnostics), alpha = NumberAt(value, 3, name, diagnostics);
        if (red is < 0f or > 1f || green is < 0f or > 1f || blue is < 0f or > 1f || alpha is < 0f or > 1f) diagnostics.Add($"'{name}' channels must be between zero and one.");
        return new(red, green, blue, alpha);
    }
    private static bool Boolean(JsonElement value, string property, DaggerfallContentDiagnostics diagnostics)
    {
        JsonElement result = DaggerfallBaseContent.Property(value, property, diagnostics);
        if (result.ValueKind is JsonValueKind.True or JsonValueKind.False) return result.GetBoolean();
        diagnostics.Add($"'{property}' must be a boolean.");
        return false;
    }
    private static float NumberAt(JsonElement value, int index, string name, DaggerfallContentDiagnostics diagnostics)
    {
        if (value[index].ValueKind == JsonValueKind.Number && value[index].TryGetSingle(out float number) && float.IsFinite(number)) return number;
        diagnostics.Add($"'{name}' values must be finite numbers.");
        return 0f;
    }
    private static long Long(JsonElement value, string property, DaggerfallContentDiagnostics diagnostics)
    {
        JsonElement result = DaggerfallBaseContent.Property(value, property, diagnostics);
        if (result.ValueKind == JsonValueKind.Number && result.TryGetInt64(out long number)) return number;
        diagnostics.Add($"'{property}' must be an integer.");
        return 0;
    }
    private static uint InvalidBillboard(DaggerfallContentDiagnostics diagnostics) { diagnostics.Add("Appearance billboard must be spherical, cylindrical, or none."); return 0; }
    private static SpriteSizeMode InvalidSizeMode(DaggerfallContentDiagnostics diagnostics) { diagnostics.Add("Appearance sizeMode must be world."); return SpriteSizeMode.World; }
    private static SpriteDepthPolicy InvalidDepthPolicy(DaggerfallContentDiagnostics diagnostics) { diagnostics.Add("Appearance depth must be default."); return SpriteDepthPolicy.Default; }
    private static RenderLayer InvalidLayer(DaggerfallContentDiagnostics diagnostics) { diagnostics.Add("Appearance layer must be scene."); return RenderLayer.Scene; }
    private static long InvalidMember(DaggerfallContentDiagnostics diagnostics) { diagnostics.Add("Encounter members must be integer placement ids."); return 0; }
}

internal sealed class AdmittedFiles
{
    private readonly IReadOnlyDictionary<string, IReadOnlyList<byte[]>> _files;
    private AdmittedFiles(IReadOnlyDictionary<string, IReadOnlyList<byte[]>> files) => _files = files;
    internal static AdmittedFiles Copy(ProductContent content, DaggerfallContentDiagnostics diagnostics)
    {
        Dictionary<string, List<byte[]>> copied = new(StringComparer.Ordinal);
        foreach (ProductContentFile file in content.Files.Span)
        {
            if (file.Path.IsEmpty) continue;
            string path;
            try { path = new UTF8Encoding(false, true).GetString(file.Path.Span); }
            catch (DecoderFallbackException) { diagnostics.Add("Admitted content contains a non-UTF8 path."); continue; }
            if (!copied.TryGetValue(path, out List<byte[]>? values)) copied[path] = values = [];
            values.Add(file.Bytes.ToArray());
        }
        return new(new ReadOnlyDictionary<string, IReadOnlyList<byte[]>>(copied.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<byte[]>)Array.AsReadOnly(pair.Value.ToArray()), StringComparer.Ordinal)));
    }
    internal bool ContainsExactlyOne(string path) => _files.TryGetValue(path, out IReadOnlyList<byte[]>? values) && values.Count == 1;
    internal byte[]? GetExactlyOne(string path) => ContainsExactlyOne(path) ? _files[path][0].ToArray() : null;
}

internal sealed record ScenarioStart(WorldPoint Position, PlayerInitialLook Look, IReadOnlyList<ScenarioInventoryStack> Loadout);
internal sealed record ScenarioInventoryStack(DaggerfallItemId ItemId, ulong Quantity);
internal sealed record ScenarioEncounter(string Id, string Name, string Objective, IReadOnlyList<long> Members);
internal sealed record WorldArtifactReferences(string StaticMesh, string Navigation, string Collision, AuthoredWorldAppearance Appearance);
internal sealed record AuthoredWorldAppearance(Color Tint, Transform Transform, bool Visible, RenderLayer Layer);
internal sealed class PrivateersHoldInputs(ProjectFacts project, IEnumerable<PlanarNavCell> navigation, CollisionMesh collision, string? staticMeshContentPath, AuthoredWorldAppearance worldAppearance, PlayerInitialLook initialLook, IReadOnlyList<ScenarioInventoryStack> loadout, IReadOnlyList<ScenarioEncounter> encounters)
{
    internal PrivateersHoldInputs(ProjectFacts project, IEnumerable<PlanarNavCell> navigation, CollisionMesh collision, string? staticMeshContentPath, PlayerInitialLook initialLook, IReadOnlyList<ScenarioInventoryStack> loadout, IReadOnlyList<ScenarioEncounter> encounters)
        : this(project, navigation, collision, staticMeshContentPath, new AuthoredWorldAppearance(new Color(.72f, .7f, .65f, 1f), new Transform(Vector3.Zero, Quaternion.Identity, Vector3.One), true, RenderLayer.Scene), initialLook, loadout, encounters) { }
    internal PrivateersHoldInputs(ProjectFacts project, PlanarNavCell[] navigation, CollisionMesh collision, string? staticMeshContentPath)
        : this(project, navigation, collision, staticMeshContentPath, new AuthoredWorldAppearance(new Color(.72f, .7f, .65f, 1f), new Transform(Vector3.Zero, Quaternion.Identity, Vector3.One), true, RenderLayer.Scene), new PlayerInitialLook(MathF.PI, 0f), Array.Empty<ScenarioInventoryStack>(), Array.Empty<ScenarioEncounter>()) { }
    internal ProjectFacts Project { get; } = project;
    private readonly PlanarNavCell[] _navigation = navigation.ToArray();
    internal ReadOnlyMemory<PlanarNavCell> Navigation => _navigation;
    internal CollisionMesh Collision { get; } = collision;
    internal string? StaticMeshContentPath { get; } = staticMeshContentPath;
    internal AuthoredWorldAppearance WorldAppearance { get; } = worldAppearance;
    internal PlayerInitialLook InitialLook { get; } = initialLook;
    internal IReadOnlyList<ScenarioInventoryStack> Loadout { get; } = Array.AsReadOnly(loadout.ToArray());
    internal IReadOnlyList<ScenarioEncounter> Encounters { get; } = Array.AsReadOnly(encounters.Select(encounter => new ScenarioEncounter(encounter.Id, encounter.Name, encounter.Objective, Array.AsReadOnly(encounter.Members.ToArray()))).ToArray());
    internal SpatialSceneInputs ToSpatialScene() => new(Collision.Vertices, Collision.Triangles, Navigation);
}

internal sealed class ProjectFacts(WorldPoint? playerPosition, IReadOnlyDictionary<long, AuthoredActor> actors)
{
    internal WorldPoint? PlayerPosition { get; } = playerPosition;
    internal IReadOnlyDictionary<long, AuthoredActor> Actors { get; } = new ReadOnlyDictionary<long, AuthoredActor>(actors.ToDictionary());
}
internal sealed record AuthoredActor(long EntityId, DaggerfallActorId ActorId, WorldPoint Position, AuthoredSprite? Sprite);
internal sealed record AuthoredSprite(string TexturePath, Vector2 UvMin, Vector2 UvMax, Vector2 Pivot, Vector2 Size, uint BillboardMode, Color Tint, SpriteSizeMode SizeMode, int RenderOrder, SpriteDepthPolicy DepthPolicy, bool Visible, RenderLayer Layer)
{
    internal AuthoredSprite(string texturePath, Vector2 uvMin, Vector2 uvMax, Vector2 pivot, Vector2 size, uint billboardMode)
        : this(texturePath, uvMin, uvMax, pivot, size, billboardMode, new Color(1, 1, 1, 1), SpriteSizeMode.World, 0, SpriteDepthPolicy.Default, true, RenderLayer.Scene) { }
}
internal sealed class CollisionMesh(IEnumerable<Vector3> vertices, IEnumerable<Triangle> triangles)
{
    private readonly Vector3[] _vertices = vertices.ToArray();
    private readonly Triangle[] _triangles = triangles.ToArray();
    internal ReadOnlyMemory<Vector3> Vertices => _vertices;
    internal ReadOnlyMemory<Triangle> Triangles => _triangles;
}
