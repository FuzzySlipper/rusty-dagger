using System.Collections.ObjectModel;
using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
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
            PrivateersHoldInputs inputs = ReadNormalizedClosure(
                files,
                root,
                start,
                definitions,
                diagnostics);
            diagnostics.ThrowIfAny();
            return inputs;
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

    private static PrivateersHoldInputs ReadNormalizedClosure(AdmittedFiles files, JsonElement root, ScenarioStart start, DaggerfallDefinitions definitions, DaggerfallContentDiagnostics diagnostics)
    {
        JsonElement world = DaggerfallBaseContent.Object(DaggerfallBaseContent.Property(root, "world", diagnostics), "world", diagnostics);
        string publicationRoot = DaggerfallBaseContent.Text(world, "publicationRoot", diagnostics);
        if (!DaggerfallBaseContent.ValidId(publicationRoot.Replace('/', '-')) || publicationRoot.Contains("..", StringComparison.Ordinal))
        {
            diagnostics.Add("Privateer's Hold publicationRoot must be a stable relative logical path.");
        }

        string Prefix(string relativePath) => $"{publicationRoot.TrimEnd('/')}/{relativePath}";
        Dictionary<string, ContentSha256> artifacts = ReadImportArtifacts(files, Prefix("import-manifest.json"), publicationRoot, diagnostics);
        string spatialPath = Prefix("spatial/privateer-s-hold/collision-navigation.json");
        string meshPath = Prefix("spatial/privateer-s-hold/static-mesh.json");
        string mediaPath = Prefix("media/dungeon/manifest.json");
        ContentSha256 spatialHash = RequireArtifact(artifacts, spatialPath, diagnostics);
        ContentSha256 meshHash = RequireArtifact(artifacts, meshPath, diagnostics);
        ContentSha256 mediaHash = RequireArtifact(artifacts, mediaPath, diagnostics);
        VerifyAdmittedArtifact(files, spatialPath, spatialHash, diagnostics);
        VerifyAdmittedArtifact(files, meshPath, meshHash, diagnostics);
        VerifyAdmittedArtifact(files, mediaPath, mediaHash, diagnostics);
        ulong gridId = UnsignedInteger(world, "navigationGridId", diagnostics);
        AuthoredWorldAppearance worldAppearance = ReadWorldAppearance(DaggerfallBaseContent.Object(DaggerfallBaseContent.Property(world, "appearance", diagnostics), "world.appearance", diagnostics), diagnostics);
        Dictionary<long, AuthoredActor> actors = ReadNormalizedPlacements(root, definitions, diagnostics);
        (IReadOnlyList<NormalizedMaterial> materials, IReadOnlyDictionary<int, NormalizedActorSprite> sprites) = ReadDungeonMedia(
            files.GetExactlyOne(mediaPath),
            publicationRoot,
            artifacts,
            definitions,
            diagnostics);
        Dictionary<long, NormalizedActorSprite> actorSprites = [];
        foreach (AuthoredActor actor in actors.Values)
        {
            if (!definitions.Actors.TryGetValue(actor.ActorId, out DaggerfallActorDefinition? definition))
            {
                // Placement validation already records the product-facing
                // diagnostic. Avoid indexing an untrusted authored ID while
                // gathering generated presentation facts.
                continue;
            }
            if (definition.MobileId is not int mobileId || !sprites.TryGetValue(mobileId, out NormalizedActorSprite? sprite))
            {
                diagnostics.Add($"Placement '{actor.EntityId}' has no generated actor media for Daggerfall mobile '{definition.MobileId}'.");
                continue;
            }
            actorSprites.Add(actor.EntityId, sprite);
        }

        return new PrivateersHoldInputs(
            new ProjectFacts(start.Position, new ReadOnlyDictionary<long, AuthoredActor>(actors)),
            new SpatialContentArtifact(spatialPath, spatialHash, gridId),
            new ContentArtifact(meshPath, meshHash),
            worldAppearance,
            start.Look,
            Array.AsReadOnly(start.Loadout.ToArray()),
            materials,
            new ReadOnlyDictionary<long, NormalizedActorSprite>(actorSprites));
    }

    private static Dictionary<long, AuthoredActor> ReadNormalizedPlacements(JsonElement root, DaggerfallDefinitions definitions, DaggerfallContentDiagnostics diagnostics)
    {
        Dictionary<long, AuthoredActor> actors = [];
        foreach (JsonElement value in DaggerfallBaseContent.Array(root, "placements", diagnostics))
        {
            JsonElement placement = DaggerfallBaseContent.Object(value, "placement", diagnostics);
            long entityId = Long(placement, "entityId", diagnostics);
            DaggerfallActorId actorId = new(DaggerfallBaseContent.Text(placement, "actor", diagnostics));
            if (entityId < 1 || !actors.TryAdd(entityId, new AuthoredActor(entityId, actorId, Point(DaggerfallBaseContent.Property(placement, "position", diagnostics), "placement.position", diagnostics))))
            {
                diagnostics.Add($"Placement entity id '{entityId}' is invalid or duplicated.");
            }
            if (!definitions.Actors.ContainsKey(actorId)) diagnostics.Add($"Placement '{entityId}' refers to missing actor '{actorId.Value}'.");
        }
        return actors;
    }

    private static Dictionary<string, ContentSha256> ReadImportArtifacts(AdmittedFiles files, string manifestPath, string publicationRoot, DaggerfallContentDiagnostics diagnostics)
    {
        byte[]? bytes = files.GetExactlyOne(manifestPath);
        if (bytes is null) { diagnostics.Add($"Generated import manifest '{manifestPath}' must occur exactly once in admitted content."); return []; }
        Dictionary<string, ContentSha256> artifacts = new(StringComparer.Ordinal);
        try
        {
            using JsonDocument document = JsonDocument.Parse(bytes);
            foreach (JsonElement artifact in DaggerfallBaseContent.Array(DaggerfallBaseContent.Object(document.RootElement, "import manifest", diagnostics), "artifacts", diagnostics))
            {
                JsonElement value = DaggerfallBaseContent.Object(artifact, "import artifact", diagnostics);
                string relativePath = DaggerfallBaseContent.Text(value, "relativePath", diagnostics);
                ContentSha256 hash = ContentHash(DaggerfallBaseContent.Text(value, "contentHash", diagnostics), diagnostics);
                string path = $"{publicationRoot.TrimEnd('/')}/{relativePath}";
                if (!artifacts.TryAdd(path, hash)) diagnostics.Add($"Generated import manifest repeats artifact '{relativePath}'.");
            }
        }
        catch (JsonException exception) { diagnostics.Add($"Generated import manifest is not valid JSON: {exception.Message}"); }
        return artifacts;
    }

    private static ContentSha256 RequireArtifact(IReadOnlyDictionary<string, ContentSha256> artifacts, string path, DaggerfallContentDiagnostics diagnostics)
    {
        if (artifacts.TryGetValue(path, out ContentSha256 hash)) return hash;
        diagnostics.Add($"Generated import manifest does not describe required artifact '{path}'.");
        return default;
    }

    private static void VerifyAdmittedArtifact(AdmittedFiles files, string path, ContentSha256 expected, DaggerfallContentDiagnostics diagnostics)
    {
        byte[]? bytes = files.GetExactlyOne(path);
        if (bytes is null) { diagnostics.Add($"Generated artifact '{path}' must occur exactly once in admitted content."); return; }
        if (ContentHash(Convert.ToHexString(SHA256.HashData(bytes)), diagnostics) != expected)
        {
            diagnostics.Add($"Generated artifact '{path}' does not match its manifest content digest.");
        }
    }

    private static ContentSha256 ContentHash(string hex, DaggerfallContentDiagnostics diagnostics)
    {
        try
        {
            byte[] bytes = Convert.FromHexString(hex);
            if (bytes.Length != 32) throw new FormatException("SHA-256 needs 32 bytes.");
            return new ContentSha256(
                BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(0, 8)),
                BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(8, 8)),
                BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(16, 8)),
                BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(24, 8)));
        }
        catch (FormatException) { diagnostics.Add("Generated content digest must be a 64-character hexadecimal SHA-256."); return default; }
    }

    private static (IReadOnlyList<NormalizedMaterial> Materials, IReadOnlyDictionary<int, NormalizedActorSprite> Sprites) ReadDungeonMedia(
        byte[]? bytes,
        string publicationRoot,
        IReadOnlyDictionary<string, ContentSha256> artifacts,
        DaggerfallDefinitions definitions,
        DaggerfallContentDiagnostics diagnostics)
    {
        if (bytes is null) { diagnostics.Add("Generated dungeon media manifest is unavailable."); return ([], new Dictionary<int, NormalizedActorSprite>()); }
        try
        {
            using JsonDocument document = JsonDocument.Parse(bytes);
            JsonElement root = DaggerfallBaseContent.Object(document.RootElement, "dungeon media manifest", diagnostics);
            Dictionary<string, MediaResource> resources = [];
            JsonElement media = DaggerfallBaseContent.Object(DaggerfallBaseContent.Property(root, "media", diagnostics), "dungeon media", diagnostics);
            foreach (JsonElement value in DaggerfallBaseContent.Array(media, "resources", diagnostics))
            {
                JsonElement resource = DaggerfallBaseContent.Object(value, "dungeon media resource", diagnostics);
                string id = DaggerfallBaseContent.Text(resource, "id", diagnostics);
                string relativePath = DaggerfallBaseContent.Text(resource, "relativePath", diagnostics);
                int atlasWidth = DaggerfallBaseContent.Integer(resource, "atlasWidth", diagnostics);
                int atlasHeight = DaggerfallBaseContent.Integer(resource, "atlasHeight", diagnostics);
                ContentSha256 hash = ContentHash(DaggerfallBaseContent.Text(resource, "contentDigest", diagnostics), diagnostics);
                List<NormalizedAtlasFrame> frames = [];
                foreach (JsonElement frameValue in DaggerfallBaseContent.Array(resource, "frames", diagnostics))
                {
                    JsonElement frame = DaggerfallBaseContent.Object(frameValue, "atlas frame", diagnostics);
                    uint frameId = checked((uint)DaggerfallBaseContent.Integer(frame, "frameIndex", diagnostics));
                    int x = DaggerfallBaseContent.Integer(frame, "x", diagnostics), y = DaggerfallBaseContent.Integer(frame, "y", diagnostics);
                    int width = DaggerfallBaseContent.Integer(frame, "width", diagnostics), height = DaggerfallBaseContent.Integer(frame, "height", diagnostics);
                    if (atlasWidth <= 0 || atlasHeight <= 0 || width <= 0 || height <= 0 || x < 0 || y < 0 || x + width > atlasWidth || y + height > atlasHeight)
                    {
                        diagnostics.Add($"Generated atlas resource '{id}' has a frame outside its atlas bounds.");
                    }
                    frames.Add(new NormalizedAtlasFrame(frameId, x, y, width, height));
                }
                if (frames.Count > 4096 || frames.Select(frame => frame.Id).Distinct().Count() != frames.Count)
                {
                    diagnostics.Add($"Generated atlas resource '{id}' exceeds Engine's 4096-frame limit or repeats a frame id.");
                }
                string path = $"{publicationRoot.TrimEnd('/')}/{relativePath}";
                if (!artifacts.TryGetValue(path, out ContentSha256 artifactHash) || artifactHash != hash)
                {
                    diagnostics.Add($"Generated media resource '{id}' does not agree with the import manifest.");
                }
                if (!resources.TryAdd(id, new MediaResource(path, hash, atlasWidth, atlasHeight, frames))) diagnostics.Add($"Generated dungeon media repeats resource '{id}'.");
            }

            List<NormalizedMaterial> materials = [];
            foreach (JsonElement value in DaggerfallBaseContent.Array(root, "materials", diagnostics))
            {
                JsonElement material = DaggerfallBaseContent.Object(value, "dungeon material", diagnostics);
                uint slot = checked((uint)DaggerfallBaseContent.Integer(material, "materialSlot", diagnostics));
                string textureId = DaggerfallBaseContent.Text(material, "mediaId", diagnostics);
                if (!resources.TryGetValue(textureId, out MediaResource? texture)) diagnostics.Add($"Generated material slot '{slot}' refers to missing media '{textureId}'.");
                else materials.Add(new NormalizedMaterial(slot, texture.Path, texture.Hash));
            }
            if (materials.Select(material => material.Slot).Distinct().Count() != materials.Count) diagnostics.Add("Generated dungeon materials repeat a static-mesh material slot.");

            Dictionary<int, NormalizedActorSprite> sprites = [];
            foreach (JsonElement value in DaggerfallBaseContent.Array(root, "actors", diagnostics))
            {
                JsonElement actor = DaggerfallBaseContent.Object(value, "dungeon actor media", diagnostics);
                int mobileId = DaggerfallBaseContent.Integer(actor, "mobileId", diagnostics);
                string spriteId = DaggerfallBaseContent.Text(actor, "spriteResourceId", diagnostics);
                if (!resources.TryGetValue(spriteId, out MediaResource? texture)) { diagnostics.Add($"Generated actor mobile '{mobileId}' refers to missing sprite media '{spriteId}'."); continue; }
                if (texture.Frames.Count == 0) { diagnostics.Add($"Generated actor mobile '{mobileId}' has no atlas frames."); continue; }
                Vector2 pivot = GeneratedVector2(DaggerfallBaseContent.Property(actor, "pivot", diagnostics), "actor.pivot", diagnostics);
                Vector2 size = GeneratedVector2(DaggerfallBaseContent.Property(actor, "worldSize", diagnostics), "actor.worldSize", diagnostics);
                if (size.X <= 0 || size.Y <= 0) diagnostics.Add($"Generated actor mobile '{mobileId}' has a non-positive world size.");
                if (!sprites.TryAdd(mobileId, new NormalizedActorSprite(texture.Path, texture.Hash, texture.AtlasWidth, texture.AtlasHeight, texture.Frames, texture.Frames[0].Id, pivot, size))) diagnostics.Add($"Generated actor media repeats mobile '{mobileId}'.");
            }
            return (Array.AsReadOnly(materials.OrderBy(material => material.Slot).ToArray()), new ReadOnlyDictionary<int, NormalizedActorSprite>(sprites));
        }
        catch (JsonException exception)
        {
            diagnostics.Add($"Generated dungeon media manifest is not valid JSON: {exception.Message}");
            return ([], new Dictionary<int, NormalizedActorSprite>());
        }
    }

    private sealed record MediaResource(string Path, ContentSha256 Hash, int AtlasWidth, int AtlasHeight, IReadOnlyList<NormalizedAtlasFrame> Frames);

    private static Vector2 GeneratedVector2(JsonElement value, string name, DaggerfallContentDiagnostics diagnostics)
    {
        if (value.ValueKind != JsonValueKind.Object
            || !value.TryGetProperty("x", out JsonElement x)
            || !value.TryGetProperty("y", out JsonElement y)
            || !x.TryGetSingle(out float horizontal)
            || !y.TryGetSingle(out float vertical)
            || !float.IsFinite(horizontal)
            || !float.IsFinite(vertical))
        {
            diagnostics.Add($"'{name}' must be a finite generated vector object.");
            return default;
        }
        return new(horizontal, vertical);
    }

    private static ScenarioStart ReadStart(JsonElement value, DaggerfallDefinitions definitions, DaggerfallContentDiagnostics diagnostics)
    {
        WorldPoint position = Point(DaggerfallBaseContent.Property(value, "position", diagnostics), "startingState.position", diagnostics);
        JsonElement look = DaggerfallBaseContent.Object(DaggerfallBaseContent.Property(value, "look", diagnostics), "startingState.look", diagnostics);
        PlayerInitialLook initialLook = new(DaggerfallBaseContent.Number(look, "yawRadians", diagnostics), DaggerfallBaseContent.Number(look, "pitchRadians", diagnostics));
        List<ScenarioLoadoutEntry> loadout = [];
        HashSet<ulong> uniqueEntityIds = [];
        HashSet<DaggerfallItemId> fungibleItemIds = [];
        HashSet<DaggerfallEquipmentSlotId> equippedSlots = [];
        foreach (JsonElement stack in DaggerfallBaseContent.Array(value, "loadout", diagnostics))
        {
            JsonElement item = DaggerfallBaseContent.Object(stack, "loadout item", diagnostics);
            DaggerfallItemId itemId = new(DaggerfallBaseContent.Text(item, "item", diagnostics));
            if (!definitions.Items.TryGetValue(itemId, out DaggerfallItemDefinition? definition)) diagnostics.Add($"Starting loadout refers to missing item '{itemId.Value}'.");
            else if (definition.IsFungible)
            {
                int quantity = DaggerfallBaseContent.Integer(item, "quantity", diagnostics);
                if (quantity < 1 || (ulong)quantity > definition.MaximumQuantity) diagnostics.Add($"Starting loadout quantity for '{itemId.Value}' is outside its item limit.");
                if (item.TryGetProperty("entityId", out JsonElement entityId) && entityId.ValueKind != JsonValueKind.Null) diagnostics.Add($"Fungible item '{itemId.Value}' must not have an entityId.");
                if (item.TryGetProperty("equipSlot", out JsonElement slot) && slot.ValueKind != JsonValueKind.Null) diagnostics.Add($"Fungible item '{itemId.Value}' cannot be equipped.");
                if (!fungibleItemIds.Add(itemId)) diagnostics.Add($"Starting loadout repeats fungible item '{itemId.Value}'.");
                loadout.Add(new(itemId, quantity > 0 ? checked((ulong)quantity) : 1, null, null));
            }
            else
            {
                ulong entityId = UnsignedInteger(item, "entityId", diagnostics);
                DaggerfallEquipmentSlotId? slot = item.TryGetProperty("equipSlot", out JsonElement slotValue) && slotValue.ValueKind != JsonValueKind.Null
                    ? new DaggerfallEquipmentSlotId(DaggerfallBaseContent.Text(item, "equipSlot", diagnostics))
                    : null;
                if (item.TryGetProperty("quantity", out JsonElement quantity) && quantity.ValueKind != JsonValueKind.Null) diagnostics.Add($"Unique item '{itemId.Value}' must use entityId rather than quantity.");
                if (!uniqueEntityIds.Add(entityId)) diagnostics.Add($"Starting loadout repeats unique entityId '{entityId}'.");
                if (slot is not null)
                {
                    if (definition.Equipment is null) diagnostics.Add($"Unique item '{itemId.Value}' has an equipSlot but is not equipable.");
                    else if (definition.Equipment.RequiredSlots != 1) diagnostics.Add($"Initial equipment assignment for '{itemId.Value}' requires {definition.Equipment.RequiredSlots} slots, but this scenario supports exactly one equipSlot.");
                    else if (!definitions.EquipmentSlots.TryGetValue(slot.Value, out DaggerfallEquipmentSlotDefinition? slotDefinition)) diagnostics.Add($"Starting loadout refers to missing equipment slot '{slot.Value.Value}'.");
                    else if (!definition.Equipment.Classifications.Any(slotDefinition.AllowedClassifications.Contains)) diagnostics.Add($"Item '{itemId.Value}' is incompatible with equipment slot '{slot.Value.Value}'.");
                    if (!equippedSlots.Add(slot.Value)) diagnostics.Add($"Starting loadout repeats equipment slot '{slot.Value.Value}'.");
                }
                loadout.Add(new(itemId, 1, entityId, slot));
            }
        }
        if (loadout.Count == 0) diagnostics.Add("Privateer's Hold must define a starting loadout.");
        return new(position, initialLook, loadout);
    }

    private static ulong UnsignedInteger(JsonElement value, string property, DaggerfallContentDiagnostics diagnostics)
    {
        JsonElement result = DaggerfallBaseContent.Property(value, property, diagnostics);
        if (result.ValueKind == JsonValueKind.Number && result.TryGetUInt64(out ulong integer) && integer > 0) return integer;
        diagnostics.Add($"'{property}' must be a non-zero unsigned integer.");
        return 1;
    }

    private static AuthoredWorldAppearance ReadWorldAppearance(JsonElement value, DaggerfallContentDiagnostics diagnostics)
    {
        RenderLayer layer = DaggerfallBaseContent.Text(value, "layer", diagnostics) switch { "scene" => RenderLayer.Scene, _ => InvalidLayer(diagnostics) };
        return new(ColorValue(DaggerfallBaseContent.Property(value, "tint", diagnostics), "world.appearance.tint", diagnostics), new Transform(Vector3Value(DaggerfallBaseContent.Property(value, "position", diagnostics), "world.appearance.position", diagnostics), QuaternionValue(DaggerfallBaseContent.Property(value, "rotation", diagnostics), "world.appearance.rotation", diagnostics), Vector3Value(DaggerfallBaseContent.Property(value, "scale", diagnostics), "world.appearance.scale", diagnostics)), Boolean(value, "visible", diagnostics), layer);
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
    private static RenderLayer InvalidLayer(DaggerfallContentDiagnostics diagnostics) { diagnostics.Add("Appearance layer must be scene."); return RenderLayer.Scene; }
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

internal sealed record ScenarioStart(WorldPoint Position, PlayerInitialLook Look, IReadOnlyList<ScenarioLoadoutEntry> Loadout);
internal sealed record ScenarioLoadoutEntry(DaggerfallItemId ItemId, ulong Quantity, ulong? UniqueEntityId, DaggerfallEquipmentSlotId? EquipSlot);
internal sealed record AuthoredWorldAppearance(Color Tint, Transform Transform, bool Visible, RenderLayer Layer);
internal sealed record ContentArtifact(string Path, ContentSha256 Sha256);
internal sealed record NormalizedMaterial(uint Slot, string TexturePath, ContentSha256 TextureSha256);
internal sealed record NormalizedAtlasFrame(uint Id, int X, int Y, int Width, int Height);
internal sealed record NormalizedActorSprite(string TexturePath, ContentSha256 TextureSha256, int AtlasWidth, int AtlasHeight, IReadOnlyList<NormalizedAtlasFrame> Frames, uint InitialFrameId, Vector2 Pivot, Vector2 Size);
internal sealed class PrivateersHoldInputs(ProjectFacts project, SpatialContentArtifact spatialArtifact, ContentArtifact staticMesh, AuthoredWorldAppearance worldAppearance, PlayerInitialLook initialLook, IReadOnlyList<ScenarioLoadoutEntry> loadout, IReadOnlyList<NormalizedMaterial> materials, IReadOnlyDictionary<long, NormalizedActorSprite> actorSprites)
{
    internal ProjectFacts Project { get; } = project;
    internal SpatialContentArtifact SpatialArtifact { get; } = spatialArtifact;
    internal ContentArtifact StaticMesh { get; } = staticMesh;
    internal AuthoredWorldAppearance WorldAppearance { get; } = worldAppearance;
    internal PlayerInitialLook InitialLook { get; } = initialLook;
    internal IReadOnlyList<ScenarioLoadoutEntry> Loadout { get; } = Array.AsReadOnly(loadout.ToArray());
    internal IReadOnlyList<NormalizedMaterial> Materials { get; } = Array.AsReadOnly(materials.OrderBy(material => material.Slot).ToArray());
    internal IReadOnlyDictionary<long, NormalizedActorSprite> ActorSprites { get; } = new ReadOnlyDictionary<long, NormalizedActorSprite>(actorSprites.ToDictionary());
}

internal sealed class ProjectFacts(WorldPoint? playerPosition, IReadOnlyDictionary<long, AuthoredActor> actors)
{
    internal WorldPoint? PlayerPosition { get; } = playerPosition;
    internal IReadOnlyDictionary<long, AuthoredActor> Actors { get; } = new ReadOnlyDictionary<long, AuthoredActor>(actors.ToDictionary());
}
internal sealed record AuthoredActor(long EntityId, DaggerfallActorId ActorId, WorldPoint Position);
