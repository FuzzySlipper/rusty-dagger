using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>Which half of the world a published block is.</summary>
internal enum DaggerfallWorldBlockKind { RmbExterior, RmbInterior, Rdb }

/// <summary>A published block's identity: its kind, its source record and, for an RMB interior, its building.</summary>
internal readonly record struct DaggerfallWorldBlockKey(DaggerfallWorldBlockKind Kind, string SourceKey, int? BuildingIndex = null)
{
    public override string ToString() => BuildingIndex is int building ? $"{Kind} {SourceKey} building {building}" : $"{Kind} {SourceKey}";
}

/// <summary>A block's collision and navigation artifact: its content path in the bundle and its digest.</summary>
internal sealed record DaggerfallWorldBlockSpatial(string ContentPath, ContentSha256 Sha256, string NavigationId);

/// <summary>The source facts an RDB action model keeps besides its action node.</summary>
internal sealed record DaggerfallWorldBlockModelAction(string ActionId, string Description, ushort ModelIndex, byte RawIndex);

/// <summary>One placed model in its block's frame: a product-wide mesh, its pose and what moves it.</summary>
internal sealed record DaggerfallWorldBlockModel(string Id, string ModelId, string? MeshArtifactId, Vector3 Position, Vector3 RotationDegrees)
{
    internal int? BuildingIndex { get; init; }
    internal string? UnresolvedReason { get; init; }
    internal DaggerfallWorldBlockModelAction? Action { get; init; }
    internal string? DoorId { get; init; }
    /// <summary>A static RDB placement's bounds in the block's frame.</summary>
    internal (Vector3 Minimum, Vector3 Maximum)? Bounds { get; init; }
    /// <summary>A static RDB placement's map visibility samples in the block's frame.</summary>
    internal IReadOnlyList<Vector3> SamplePoints { get; init; } = [];
    /// <summary>A moving RDB placement's model-local bounds.</summary>
    internal (Vector3 Minimum, Vector3 Maximum)? LocalBounds { get; init; }
    internal Vector3[] CollisionVertices { get; init; } = [];
    internal Triangle[] CollisionTriangles { get; init; } = [];
}

/// <summary>One door in its block's frame.</summary>
internal sealed record DaggerfallWorldBlockDoor(string Id, string ModelId, Vector3 Position, Vector3 RotationDegrees, DaggerfallDoorKind Kind, int StartingLockValue)
{
    internal DaggerfallDoorActionSource? Action { get; init; }
    internal (Vector3 Minimum, Vector3 Maximum)? CollisionBounds { get; init; }
    internal int? BuildingIndex { get; init; }
}

/// <summary>One RMB building sub-record as its block states it.</summary>
internal sealed record DaggerfallWorldBlockBuilding(int Index, int BuildingType, int FactionId, int Quality, int NameSeed, bool HasInterior);

/// <summary>One RDB random-enemy marker in its block's frame.</summary>
internal sealed record DaggerfallWorldBlockRandomEnemy(string Id, Vector3 Position, int EncounterSlot, int SpawnDistance, bool Passive, string? ActionId);

/// <summary>
/// One published block document, read from the world block bundle: its typed placement facts, and the
/// sections a placing location only re-identifies and moves (people, furniture, lights, flats, actions)
/// kept as the normalized JSON the site closure readers interpret.
/// </summary>
internal sealed class DaggerfallWorldBlockDocument
{
    internal required DaggerfallWorldBlockKey Key { get; init; }
    internal required string PublishedKey { get; init; }
    internal DaggerfallWorldBlockSpatial? Spatial { get; init; }
    internal required IReadOnlyList<DaggerfallWorldBlockModel> Models { get; init; }
    internal required IReadOnlyList<DaggerfallWorldBlockDoor> Doors { get; init; }
    internal Vector3? StartMarker { get; init; }
    internal Vector3? EnterMarker { get; init; }
    internal required IReadOnlyList<DaggerfallWorldBlockBuilding> Buildings { get; init; }
    internal DaggerfallWorldBlockBuilding? InteriorBuilding { get; init; }
    /// <summary>An RMB exterior's clear automap cells: a 64-by-64 bit grid, cell (x, z) at bit <c>x + 64 z</c>.</summary>
    internal byte[]? ClearGround { get; init; }
    internal required IReadOnlyList<DaggerfallWorldBlockRandomEnemy> RandomEnemies { get; init; }
    internal float? WaterLevel { get; init; }
    internal string? AmbientZone { get; init; }

    /// <summary>The block's normalized sections by their published names; each is a JSON array.</summary>
    internal required IReadOnlyDictionary<string, JsonArray> Sections { get; init; }

    internal JsonArray Section(string name) => Sections.TryGetValue(name, out JsonArray? section) ? section : [];
}

/// <summary>
/// The per-block world publication, served lazily through the one content bundle the Host declares for
/// it. Nothing is read at composition: the index is read the first time a location needs a block, and
/// each block document only when a location that places it is assembled. The bundle is opened for each
/// read and closed again, so the Engine keeps no block bodies once the documents are interpreted.
/// </summary>
internal sealed class DaggerfallWorldBlocks
{
    /// <summary>The bundle the Host stages the per-block publication as.</summary>
    internal const string BundleId = "daggerfall.world-blocks";

    /// <summary>The bundle's root inside the content store.</summary>
    internal const string Root = "worldrpg/imports/world-blocks";

    /// <summary>The block index's bundle-relative path.</summary>
    internal const string IndexPath = "blocks.json";

    private readonly ProductContent _content;
    private readonly object _gate = new();
    private IReadOnlyDictionary<DaggerfallWorldBlockKey, IndexEntry>? _index;

    private sealed record IndexEntry(string Key, string Document, string? Spatial);

    internal DaggerfallWorldBlocks(ProductContent content) => _content = content ?? throw new ArgumentNullException(nameof(content));

    /// <summary>The content path a bundle-relative path names, as the Engine resolves it while the bundle is open.</summary>
    internal static string ContentPath(string relativePath) => $"{Root}/{relativePath}";

    /// <summary>Whether the index has been read yet; composition must not read it.</summary>
    internal bool IndexRead { get { lock (_gate) return _index is not null; } }

    /// <summary>
    /// Opens the bundle for one Engine admission that resolves block artifacts by their content path. The
    /// caller disposes it once the admission has read them.
    /// </summary>
    internal ProductContentBundle Open()
    {
        try { return _content.OpenBundle(BundleId); }
        catch (Exception exception) when (exception is FileNotFoundException or KeyNotFoundException or EngineCallException or InvalidOperationException)
        {
            throw new InvalidOperationException(
                $"The per-block world publication (bundle '{BundleId}' at '{Root}') is not staged: {exception.Message} Run scripts/regenerate-content.sh and restage.", exception);
        }
    }

    /// <summary>Whether the publication places a block for this key.</summary>
    internal bool Contains(DaggerfallWorldBlockKey key)
    {
        using ProductContentBundle bundle = Open();
        return RequireIndex(bundle).ContainsKey(key);
    }

    /// <summary>
    /// Reads the documents of the given blocks in one bundle opening. A block the publication does not
    /// carry, or a document that does not read, is reported by name; nothing is skipped.
    /// </summary>
    internal IReadOnlyDictionary<DaggerfallWorldBlockKey, DaggerfallWorldBlockDocument> Read(IEnumerable<DaggerfallWorldBlockKey> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        DaggerfallWorldBlockKey[] requested = [.. keys.Distinct()];
        using ProductContentBundle bundle = Open();
        IReadOnlyDictionary<DaggerfallWorldBlockKey, IndexEntry> index = RequireIndex(bundle);
        DaggerfallContentDiagnostics diagnostics = new();
        Dictionary<DaggerfallWorldBlockKey, DaggerfallWorldBlockDocument> documents = [];
        foreach (DaggerfallWorldBlockKey key in requested)
        {
            if (!index.TryGetValue(key, out IndexEntry? entry))
            {
                diagnostics.Add($"The per-block world publication carries no block for {key}.");
                continue;
            }

            ReadOnlyMemory<byte> bytes;
            try { bytes = bundle.ReadBytes(entry.Document); }
            catch (Exception exception) when (exception is FileNotFoundException or EngineCallException)
            {
                diagnostics.Add($"Block document '{entry.Document}' of {key} cannot be read from bundle '{BundleId}': {exception.Message}");
                continue;
            }

            if (ReadDocument(key, entry, bytes, diagnostics) is { } document) documents.Add(key, document);
        }

        diagnostics.ThrowIfAny();
        return documents;
    }

    private IReadOnlyDictionary<DaggerfallWorldBlockKey, IndexEntry> RequireIndex(ProductContentBundle bundle)
    {
        lock (_gate)
        {
            if (_index is not null) return _index;
            DaggerfallContentDiagnostics diagnostics = new();
            Dictionary<DaggerfallWorldBlockKey, IndexEntry> index = [];
            try
            {
                using JsonDocument document = JsonDocument.Parse(bundle.ReadBytes(IndexPath));
                JsonElement root = DaggerfallBaseContent.Object(document.RootElement, "world block index", diagnostics);
                foreach (JsonElement value in DaggerfallBaseContent.Array(root, "blocks", diagnostics))
                {
                    JsonElement block = DaggerfallBaseContent.Object(value, "world block index entry", diagnostics);
                    string key = DaggerfallBaseContent.Text(block, "key", diagnostics);
                    DaggerfallWorldBlockKind? kind = Kind(DaggerfallBaseContent.Text(block, "kind", diagnostics));
                    string sourceKey = DaggerfallBaseContent.Text(block, "sourceKey", diagnostics);
                    int? building = DaggerfallBaseContent.OptionalInteger(block, "buildingIndex", diagnostics);
                    string documentPath = DaggerfallBaseContent.Text(block, "document", diagnostics);
                    string? spatial = DaggerfallBaseContent.OptionalText(block, "spatial", diagnostics);
                    if (kind is null || (kind == DaggerfallWorldBlockKind.RmbInterior) != building.HasValue)
                    {
                        diagnostics.Add($"World block index entry '{key}' has an unknown kind or a building index its kind does not take.");
                        continue;
                    }

                    if (!index.TryAdd(new(kind.Value, sourceKey, building), new(key, documentPath, spatial)))
                        diagnostics.Add($"World block index repeats block '{key}'.");
                }
            }
            catch (Exception exception) when (exception is JsonException or FileNotFoundException or EngineCallException)
            {
                diagnostics.Add($"The world block index '{IndexPath}' in bundle '{BundleId}' cannot be read: {exception.Message}");
            }

            diagnostics.ThrowIfAny();
            return _index = index;
        }
    }

    private static DaggerfallWorldBlockKind? Kind(string value) => value switch
    {
        "rmbExterior" => DaggerfallWorldBlockKind.RmbExterior,
        "rmbInterior" => DaggerfallWorldBlockKind.RmbInterior,
        "rdb" => DaggerfallWorldBlockKind.Rdb,
        _ => null,
    };

    /// <summary>The sections a placing location re-identifies and moves without interpreting them here.</summary>
    private static readonly string[] SectionNames =
        ["questMarkers", "population", "staticNpcs", "propertyContainers", "lights", "billboards", "actors", "treasures", "actions"];

    private static DaggerfallWorldBlockDocument? ReadDocument(DaggerfallWorldBlockKey key, IndexEntry entry, ReadOnlyMemory<byte> bytes, DaggerfallContentDiagnostics diagnostics)
    {
        string owner = $"Block '{entry.Key}'";
        try
        {
            using JsonDocument document = JsonDocument.Parse(bytes);
            JsonElement root = DaggerfallBaseContent.Object(document.RootElement, owner, diagnostics);
            if (DaggerfallBaseContent.Text(root, "key", diagnostics) != entry.Key
                || Kind(DaggerfallBaseContent.Text(root, "kind", diagnostics)) != key.Kind
                || DaggerfallBaseContent.Text(root, "sourceKey", diagnostics) != key.SourceKey
                || DaggerfallBaseContent.OptionalInteger(root, "buildingIndex", diagnostics) != key.BuildingIndex)
            {
                diagnostics.Add($"{owner} does not state the identity its index entry gives it.");
                return null;
            }

            DaggerfallWorldBlockSpatial? spatial = null;
            if (root.TryGetProperty("spatial", out JsonElement spatialValue) && spatialValue.ValueKind != JsonValueKind.Null)
            {
                string relativePath = DaggerfallBaseContent.Text(spatialValue, "relativePath", diagnostics);
                if (!StringComparer.Ordinal.Equals(relativePath, entry.Spatial))
                    diagnostics.Add($"{owner} names spatial artifact '{relativePath}' where its index entry names '{entry.Spatial}'.");
                spatial = new(ContentPath(relativePath),
                    DaggerfallContentHash.Parse(DaggerfallBaseContent.Text(spatialValue, "contentDigest", diagnostics), $"{owner} spatial artifact"),
                    DaggerfallBaseContent.Text(spatialValue, "navigationId", diagnostics));
            }
            else if (entry.Spatial is not null) diagnostics.Add($"{owner} states no spatial artifact where its index entry names '{entry.Spatial}'.");

            List<DaggerfallWorldBlockModel> models = [];
            foreach (JsonElement value in DaggerfallBaseContent.Array(root, "models", diagnostics))
                models.Add(ReadModel(value, owner, diagnostics));
            List<DaggerfallWorldBlockDoor> doors = [];
            foreach (JsonElement value in DaggerfallBaseContent.Array(root, "doors", diagnostics))
                doors.Add(ReadDoor(value, owner, diagnostics));
            List<DaggerfallWorldBlockBuilding> buildings = [.. DaggerfallBaseContent.Array(root, "buildings", diagnostics).Select(value => ReadBuilding(value, diagnostics))];
            DaggerfallWorldBlockBuilding? interior = root.TryGetProperty("interiorBuilding", out JsonElement interiorValue) && interiorValue.ValueKind != JsonValueKind.Null
                ? ReadBuilding(interiorValue, diagnostics)
                : null;
            if ((key.Kind == DaggerfallWorldBlockKind.RmbInterior) != (interior is not null))
                diagnostics.Add($"{owner} must state its interior building exactly when it is an RMB interior.");
            byte[]? clear = null;
            if (DaggerfallBaseContent.OptionalText(root, "clearGround", diagnostics) is { } clearText)
            {
                try { clear = Convert.FromBase64String(clearText); }
                catch (FormatException) { diagnostics.Add($"{owner} clearGround is not base64."); }
                if (clear is not null && clear.Length != 64 * 64 / 8) diagnostics.Add($"{owner} clearGround is not a 64-by-64 bit grid.");
            }

            List<DaggerfallWorldBlockRandomEnemy> enemies = [];
            foreach (JsonElement value in DaggerfallBaseContent.Array(root, "randomEnemies", diagnostics))
            {
                enemies.Add(new(DaggerfallBaseContent.Text(value, "id", diagnostics), Vector(value, "position", owner, diagnostics),
                    DaggerfallBaseContent.Integer(value, "encounterSlot", diagnostics), DaggerfallBaseContent.Integer(value, "spawnDistance", diagnostics),
                    DaggerfallBaseContent.Boolean(value, "passive", diagnostics), DaggerfallBaseContent.OptionalText(value, "actionId", diagnostics)));
            }

            Dictionary<string, JsonArray> sections = new(StringComparer.Ordinal);
            foreach (string name in SectionNames)
            {
                if (!root.TryGetProperty(name, out JsonElement section)) continue;
                if (section.ValueKind != JsonValueKind.Array) { diagnostics.Add($"{owner} section '{name}' must be an array."); continue; }
                sections.Add(name, JsonNode.Parse(section.GetRawText())!.AsArray());
            }

            float? waterLevel = root.TryGetProperty("waterLevel", out JsonElement water) && water.ValueKind != JsonValueKind.Null
                ? DaggerfallBaseContent.Number(root, "waterLevel", diagnostics)
                : null;
            return new DaggerfallWorldBlockDocument
            {
                Key = key,
                PublishedKey = entry.Key,
                Spatial = spatial,
                Models = models,
                Doors = doors,
                StartMarker = Marker(root, "startMarker", owner, diagnostics),
                EnterMarker = Marker(root, "enterMarker", owner, diagnostics),
                Buildings = buildings,
                InteriorBuilding = interior,
                ClearGround = clear,
                RandomEnemies = enemies,
                WaterLevel = waterLevel,
                AmbientZone = DaggerfallBaseContent.OptionalText(root, "ambientZone", diagnostics),
                Sections = sections,
            };
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException && exception is not DaggerfallContentException)
        {
            diagnostics.Add($"{owner} is malformed: {exception.Message}");
            return null;
        }
    }

    private static DaggerfallWorldBlockModel ReadModel(JsonElement value, string owner, DaggerfallContentDiagnostics diagnostics)
    {
        string id = DaggerfallBaseContent.Text(value, "id", diagnostics);
        string name = $"{owner} model '{id}'";
        DaggerfallWorldBlockModelAction? action = null;
        if (value.TryGetProperty("action", out JsonElement actionValue) && actionValue.ValueKind != JsonValueKind.Null)
        {
            int modelIndex = DaggerfallBaseContent.Integer(actionValue, "modelIndex", diagnostics);
            int rawIndex = DaggerfallBaseContent.Integer(actionValue, "rawIndex", diagnostics);
            if (modelIndex is < 0 or > ushort.MaxValue || rawIndex is < 0 or > byte.MaxValue)
                diagnostics.Add($"{name} has an action model index outside its source range.");
            action = new(DaggerfallBaseContent.Text(actionValue, "actionId", diagnostics), DaggerfallBaseContent.Text(actionValue, "description", diagnostics),
                (ushort)Math.Clamp(modelIndex, 0, ushort.MaxValue), (byte)Math.Clamp(rawIndex, 0, byte.MaxValue));
        }

        Vector3[] vertices = [];
        Triangle[] triangles = [];
        if (value.TryGetProperty("collision", out JsonElement collision) && collision.ValueKind != JsonValueKind.Null)
        {
            vertices = [.. DaggerfallBaseContent.Array(collision, "vertices", diagnostics).Select(vertex => Vector(vertex, name, diagnostics))];
            triangles = [.. DaggerfallBaseContent.Array(collision, "triangles", diagnostics).Select(triangle =>
            {
                int a = DaggerfallBaseContent.Integer(triangle, "firstVertex", diagnostics);
                int b = DaggerfallBaseContent.Integer(triangle, "secondVertex", diagnostics);
                int c = DaggerfallBaseContent.Integer(triangle, "thirdVertex", diagnostics);
                if ((uint)a >= (uint)vertices.Length || (uint)b >= (uint)vertices.Length || (uint)c >= (uint)vertices.Length)
                    diagnostics.Add($"{name} has a collision triangle outside its vertices.");
                return new Triangle((uint)Math.Max(a, 0), (uint)Math.Max(b, 0), (uint)Math.Max(c, 0));
            })];
        }

        return new DaggerfallWorldBlockModel(id, DaggerfallBaseContent.Text(value, "modelId", diagnostics),
            DaggerfallBaseContent.OptionalText(value, "meshArtifactId", diagnostics), Vector(value, "position", name, diagnostics),
            Vector(value, "rotationDegrees", name, diagnostics))
        {
            BuildingIndex = DaggerfallBaseContent.OptionalInteger(value, "buildingIndex", diagnostics),
            UnresolvedReason = DaggerfallBaseContent.OptionalText(value, "unresolvedReason", diagnostics),
            Action = action,
            DoorId = DaggerfallBaseContent.OptionalText(value, "doorId", diagnostics),
            Bounds = OptionalBounds(value, "bounds", name, diagnostics),
            SamplePoints = value.TryGetProperty("samplePoints", out JsonElement samples) && samples.ValueKind != JsonValueKind.Null
                ? [.. DaggerfallBaseContent.Array(value, "samplePoints", diagnostics).Select(point => Vector(point, name, diagnostics))]
                : [],
            LocalBounds = OptionalBounds(value, "localBounds", name, diagnostics),
            CollisionVertices = vertices,
            CollisionTriangles = triangles,
        };
    }

    private static DaggerfallWorldBlockDoor ReadDoor(JsonElement value, string owner, DaggerfallContentDiagnostics diagnostics)
    {
        string id = DaggerfallBaseContent.Text(value, "id", diagnostics);
        string name = $"{owner} door '{id}'";
        DaggerfallDoorKind kind = DaggerfallBaseContent.Text(value, "kind", diagnostics) switch
        {
            "normal" => DaggerfallDoorKind.Normal,
            "special" => DaggerfallDoorKind.Special,
            string unknown => Invalid(unknown),
        };
        DaggerfallDoorActionSource? action = null;
        if (value.TryGetProperty("action", out JsonElement actionValue) && actionValue.ValueKind != JsonValueKind.Null)
        {
            int axis = DaggerfallBaseContent.Integer(actionValue, "axis", diagnostics);
            int duration = DaggerfallBaseContent.Integer(actionValue, "duration", diagnostics);
            int magnitude = DaggerfallBaseContent.Integer(actionValue, "magnitude", diagnostics);
            int next = DaggerfallBaseContent.Integer(actionValue, "nextObjectOffset", diagnostics);
            int flags = DaggerfallBaseContent.Integer(actionValue, "flags", diagnostics);
            if (axis is < 0 or > byte.MaxValue || duration is < 0 or > ushort.MaxValue || magnitude is < 0 or > ushort.MaxValue || flags is < 0 or > byte.MaxValue)
                diagnostics.Add($"{name} has an action outside its source ranges.");
            action = new((byte)Math.Clamp(axis, 0, byte.MaxValue), (ushort)Math.Clamp(duration, 0, ushort.MaxValue),
                (ushort)Math.Clamp(magnitude, 0, ushort.MaxValue), next, (byte)Math.Clamp(flags, 0, byte.MaxValue));
        }

        return new DaggerfallWorldBlockDoor(id, DaggerfallBaseContent.Text(value, "modelId", diagnostics), Vector(value, "position", name, diagnostics),
            Vector(value, "rotationDegrees", name, diagnostics), kind, DaggerfallBaseContent.Integer(value, "startingLockValue", diagnostics))
        {
            Action = action,
            CollisionBounds = OptionalBounds(value, "collisionBounds", name, diagnostics),
            BuildingIndex = DaggerfallBaseContent.OptionalInteger(value, "buildingIndex", diagnostics),
        };

        DaggerfallDoorKind Invalid(string unknown)
        {
            diagnostics.Add($"{name} has unknown kind '{unknown}'.");
            return DaggerfallDoorKind.Normal;
        }
    }

    private static DaggerfallWorldBlockBuilding ReadBuilding(JsonElement value, DaggerfallContentDiagnostics diagnostics) => new(
        DaggerfallBaseContent.Integer(value, "index", diagnostics),
        DaggerfallBaseContent.Integer(value, "buildingType", diagnostics),
        DaggerfallBaseContent.Integer(value, "factionId", diagnostics),
        DaggerfallBaseContent.Integer(value, "quality", diagnostics),
        DaggerfallBaseContent.Integer(value, "nameSeed", diagnostics),
        DaggerfallBaseContent.Boolean(value, "hasInterior", diagnostics));

    private static Vector3? Marker(JsonElement root, string property, string owner, DaggerfallContentDiagnostics diagnostics) =>
        root.TryGetProperty(property, out JsonElement marker) && marker.ValueKind != JsonValueKind.Null
            ? Vector(marker, "position", $"{owner} {property}", diagnostics)
            : null;

    private static (Vector3, Vector3)? OptionalBounds(JsonElement value, string property, string owner, DaggerfallContentDiagnostics diagnostics)
    {
        if (!value.TryGetProperty(property, out JsonElement bounds) || bounds.ValueKind == JsonValueKind.Null) return null;
        Vector3 minimum = Vector(bounds, "minimum", owner, diagnostics), maximum = Vector(bounds, "maximum", owner, diagnostics);
        if (minimum.X > maximum.X || minimum.Y > maximum.Y || minimum.Z > maximum.Z) diagnostics.Add($"{owner} {property} are not ordered.");
        return (minimum, maximum);
    }

    private static Vector3 Vector(JsonElement owner, string property, string name, DaggerfallContentDiagnostics diagnostics) =>
        Vector(DaggerfallBaseContent.Property(owner, property, diagnostics), $"{name} {property}", diagnostics);

    internal static Vector3 Vector(JsonElement value, string name, DaggerfallContentDiagnostics diagnostics)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            diagnostics.Add($"{name} must be an object with x, y and z.");
            return default;
        }

        Vector3 vector = new(DaggerfallBaseContent.Number(value, "x", diagnostics), DaggerfallBaseContent.Number(value, "y", diagnostics),
            DaggerfallBaseContent.Number(value, "z", diagnostics));
        if (!float.IsFinite(vector.X) || !float.IsFinite(vector.Y) || !float.IsFinite(vector.Z)) diagnostics.Add($"{name} must be finite.");
        return vector;
    }
}
