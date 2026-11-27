using System.Collections.ObjectModel;
using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Rusty.Engine;
using WorldRpg.Kit;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>
/// Reads what an assembled location draws on: the product-wide world media publication's media and classic presentation,
/// and the normalized world sections an assembly gathers from its blocks.
/// </summary>
internal static partial class DaggerfallSiteContent
{
    /// <summary>Ground tiles use the terrain climate's archive; models use the published climate swap table.</summary>
    internal static int ExteriorTextureArchive(DaggerfallClimateGridDefinition swaps, int archive, int record,
        int climate, int groundClimate, DaggerfallClimateSeason season) => archive < 500 && archive % 100 == 2
        ? DaggerfallClimateArchives.GroundTextureArchive(groundClimate, season switch
        {
            DaggerfallClimateSeason.Winter => DaggerfallExteriorSeason.Winter,
            DaggerfallClimateSeason.Rain => DaggerfallExteriorSeason.Rain,
            _ => DaggerfallExteriorSeason.Summer,
        })
        : swaps.SwapArchive(archive, record, climate, season);

    /// <summary>
    /// Reads the product-wide world media an assembled location draws from: every actor, billboard and
    /// terrain texture the world media publication carries, its material textures by material id, and its
    /// classic sidecar's audio and classic presentation, with the base payload's classic selection. Every location
    /// assembled from its blocks admits the whole publication; it is read once and shared.
    /// </summary>
    internal static DaggerfallProductMedia ReadProductMedia(DaggerfallData content, DaggerfallWorldMedia product, DaggerfallDefinitions definitions)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(product);
        ArgumentNullException.ThrowIfNull(definitions);
        DaggerfallContentDiagnostics diagnostics = new();
        if (!product.IsPublished)
        {
            diagnostics.Add($"Assembling a location needs the world media publication at '{DaggerfallWorldMedia.Root}', which admitted content does not carry.");
            throw diagnostics.Exception();
        }

        AdmittedFiles files = AdmittedFiles.From(content);
        (IReadOnlyDictionary<int, NormalizedActorSprite> sprites, IReadOnlyList<NormalizedBillboardSprite>? groundContainer,
            IReadOnlyDictionary<(int Archive, int Record), NormalizedBillboardSprite> billboards,
            IReadOnlyDictionary<(int Archive, int Record), NormalizedTerrainTexture> terrain) = ReadWorldMedia(product, diagnostics);
        foreach (int mobileId in definitions.Encounters.Tables.SelectMany(table => table).Distinct().Order())
            if (!sprites.ContainsKey(mobileId))
                diagnostics.Add($"The world media publication lacks media for encounter mobile '{mobileId}'.");
        (IReadOnlyList<NormalizedAudioClip> audio, NormalizedClassicPresentation classic) =
            ReadClassicPresentation(files, product.ClassicSidecar, product, diagnostics);
        if (definitions.ClassicSelection is { } held) classic = held.Select(classic, definitions, diagnostics);
        Dictionary<string, ContentArtifact> textures = new(StringComparer.Ordinal);
        foreach (string id in product.ResourceIds.Where(id => id.StartsWith("material/", StringComparison.Ordinal)))
        {
            if (!product.TryGetResource(id, out JsonElement resource)) continue;
            string path = product.Resolve(DaggerfallBaseContent.Text(resource, "relativePath", diagnostics));
            if (!product.TryGetArtifact(path, out ContentSha256 hash)) diagnostics.Add($"World media material '{id}' is not an admitted artifact.");
            else textures.Add(id, new ContentArtifact(path, hash));
        }

        // A material whose record has several frames names each frame's texture, its own first, and the rate it cycles at.
        Dictionary<string, DaggerfallMaterialFrames> animations = new(StringComparer.Ordinal);
        foreach (string id in textures.Keys)
        {
            if (!product.TryGetAnimation(id, out JsonElement animation)) continue;
            float rate = DaggerfallBaseContent.Number(animation, "framesPerSecond", diagnostics);
            List<ContentArtifact> frames = [];
            foreach (JsonElement frame in DaggerfallBaseContent.Array(animation, "frames", diagnostics))
            {
                string frameId = frame.GetString() ?? "";
                if (textures.TryGetValue(frameId, out ContentArtifact? texture)) frames.Add(texture);
                else diagnostics.Add($"World media material '{id}' cycles through '{frameId}', which the publication carries no texture for.");
            }
            if (frames.Count < 2 || frames[0] != textures[id] || !float.IsFinite(rate) || rate <= 0F)
                diagnostics.Add($"World media material '{id}' cycles {frames.Count} frames at {rate} a second, its own not first: a cycle is two frames or more at a rate above zero.");
            else animations.Add(id, new DaggerfallMaterialFrames(frames, rate));
        }

        diagnostics.ThrowIfAny();
        return new(sprites, groundContainer, billboards, terrain, audio, classic, textures) { Animations = animations };
    }

    /// <summary>
    /// Projects normalized RDB action nodes without interpreting source flags as a successful
    /// operation. The graph runtime owns trigger admission and action-family policy; content admission
    /// only validates stable identities, raw parameters, and the links/doors this document carries.
    /// </summary>
    internal static IReadOnlyList<DaggerfallDungeonActionDefinition> ReadNormalizedActions(
        ReadOnlyMemory<byte>? bytes,
        IReadOnlyList<DaggerfallRdbDoorDefinition> doors,
        DaggerfallContentDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(doors);
        if (bytes is null)
        {
            diagnostics.Add("Normalized world document must contain normalized.json for its dungeon actions.");
            return [];
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(bytes.Value);
            JsonElement root = DaggerfallBaseContent.Object(document.RootElement, "normalized world", diagnostics);
            JsonElement world = DaggerfallBaseContent.Object(DaggerfallBaseContent.Property(root, "world", diagnostics), "normalized world.world", diagnostics);
            if (!world.TryGetProperty("actions", out JsonElement section))
            {
                // Profiles without RDB actions predate this optional normalized section and remain
                // valid empty graphs. A present section still has to be an array.
                return [];
            }
            if (section.ValueKind != JsonValueKind.Array)
            {
                diagnostics.Add("Normalized world.actions must be an array.");
                return [];
            }

            List<DaggerfallDungeonActionDefinition> actions = [];
            HashSet<string> actionIds = new(StringComparer.Ordinal);
            foreach (JsonElement value in section.EnumerateArray())
            {
                JsonElement action = DaggerfallBaseContent.Object(value, "normalized dungeon action", diagnostics);
                DaggerfallBaseContent.RejectDuplicateProperties(action, "normalized dungeon action", diagnostics);
                string id = DaggerfallBaseContent.Text(action, "id", diagnostics);
                int sourceOffset = DaggerfallBaseContent.Integer(action, "sourceOffset", diagnostics);
                uint triggerFlag = Unsigned32(action, "triggerFlag", diagnostics);
                byte actionFlag = Byte(action, "actionFlag", diagnostics);
                byte axis = Byte(action, "axis", diagnostics);
                ushort duration = UShort(action, "duration", diagnostics);
                ushort magnitude = UShort(action, "magnitude", diagnostics);
                int nextObjectOffset = DaggerfallBaseContent.Integer(action, "nextObjectOffset", diagnostics);
                string? nextActionId = OptionalNullableText(action, "nextActionId", diagnostics);
                string? doorId = OptionalNullableText(action, "doorId", diagnostics);
                bool isSprite = OptionalBoolean(action, "isFlat", false, diagnostics); // the import names an action on a sprite record isFlat
                byte soundIndex = Byte(action, "soundIndex", diagnostics);
                byte rawIndex = action.TryGetProperty("rawIndex", out _)
                    ? Byte(action, "rawIndex", diagnostics)
                    : soundIndex;
                Vector3? sourcePosition = OptionalObjectVector3(action, "position", $"normalized dungeon action '{id}' position", diagnostics);
                DaggerfallDungeonPoisonAction? poison = null;
                if (action.TryGetProperty("poison", out JsonElement poisonValue) && poisonValue.ValueKind != JsonValueKind.Null)
                {
                    JsonElement source = DaggerfallBaseContent.Object(poisonValue, $"normalized dungeon action '{id}' poison", diagnostics);
                    DaggerfallBaseContent.RejectDuplicateProperties(source, $"normalized dungeon action '{id}' poison", diagnostics);
                    int? poisonId = null;
                    if (source.TryGetProperty("poisonId", out JsonElement declared) && declared.ValueKind != JsonValueKind.Null)
                    {
                        if (declared.ValueKind == JsonValueKind.Number && declared.TryGetInt32(out int variant)) poisonId = variant;
                        else diagnostics.Add($"Normalized dungeon Poison action '{id}' poisonId must be an integer or null.");
                    }
                    poison = new(DaggerfallBaseContent.Text(source, "sourceRecord", diagnostics),
                        DaggerfallBaseContent.Text(source, "disposition", diagnostics), poisonId);
                }

                DaggerfallDungeonActionDefinition parsed = new(
                    id,
                    sourceOffset,
                    triggerFlag,
                    actionFlag,
                    axis,
                    duration,
                    magnitude,
                    nextObjectOffset,
                    nextActionId,
                    doorId,
                    isSprite,
                    SoundIndex: soundIndex,
                    SourcePosition: sourcePosition,
                    RawIndex: rawIndex,
                    Poison: poison,
                    NextObjectPosition: OptionalObjectVector3(action, "nextObjectPosition", $"dungeon teleport {id} destination", diagnostics),
                    NextObjectRotation: action.TryGetProperty("nextObjectRotation", out JsonElement rotation) && rotation.ValueKind != JsonValueKind.Null
                        ? DaggerfallWorldBlocks.Rotation(rotation, $"dungeon teleport {id} rotation", diagnostics)
                        : null)
                {
                    Motion = action.TryGetProperty("motion", out JsonElement motion) && motion.ValueKind != JsonValueKind.Null
                        ? Motion(motion, $"normalized dungeon action '{id}' motion", diagnostics)
                        : null,
                };
                try
                {
                    if (actionFlag == (byte)DaggerfallDungeonActionFlag.Poison && poison is null)
                        diagnostics.Add($"Normalized dungeon Poison action '{id}' requires explicit source authority or exclusion.");
                    poison?.Validate(parsed);
                }
                catch (ArgumentException exception) { diagnostics.Add(exception.Message); }
                actions.Add(parsed);
                if (!actionIds.Add(id))
                    diagnostics.Add($"Normalized world repeats dungeon action '{id}'.");
            }

            HashSet<DaggerfallRdbDoorId> doorIds = doors.Select(door => door.Id).ToHashSet();
            foreach (DaggerfallDungeonActionDefinition action in actions)
            {
                if (action.SourceOffset <= 0)
                    diagnostics.Add($"Normalized dungeon action '{action.Id}' has a non-positive source offset.");
                if (action.NextActionId is not null)
                {
                    if (!actionIds.Contains(action.NextActionId))
                        diagnostics.Add($"Normalized dungeon action '{action.Id}' links to unknown action '{action.NextActionId}'.");
                    if (action.NextObjectOffset <= 0)
                        diagnostics.Add($"Normalized dungeon action '{action.Id}' resolves a target while preserving non-link source offset {action.NextObjectOffset}.");
                }
                if (action.DoorId is not null)
                {
                    if (!TryDoorIdentity(action.DoorId, out DaggerfallRdbDoorId doorId) || !doorIds.Contains(doorId))
                        diagnostics.Add($"Normalized dungeon action '{action.Id}' refers to unknown door '{action.DoorId}'.");
                }
            }

            return actions
                .OrderBy(action => action.Id, StringComparer.Ordinal)
                .ToArray();
        }
        catch (JsonException exception)
        {
            diagnostics.Add($"Normalized world document is not valid JSON: {exception.Message}");
            return [];
        }
    }

    /// <summary>
    /// Projects the importer-owned RDB light facts without reopening Arena2: position, range, intensity and
    /// the colour the importer states for every light.
    /// </summary>
    internal static IReadOnlyList<DaggerfallSiteLight> ReadNormalizedLights(ReadOnlyMemory<byte>? bytes, DaggerfallContentDiagnostics diagnostics)
    {
        if (bytes is null)
        {
            diagnostics.Add("Normalized world document must contain normalized.json for its source lights.");
            return [];
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(bytes.Value);
            JsonElement root = DaggerfallBaseContent.Object(document.RootElement, "normalized world", diagnostics);
            JsonElement world = DaggerfallBaseContent.Object(DaggerfallBaseContent.Property(root, "world", diagnostics), "normalized world.world", diagnostics);
            Dictionary<string, DaggerfallSiteLight> lights = new(StringComparer.Ordinal);
            foreach (JsonElement value in DaggerfallBaseContent.Array(world, "lights", diagnostics))
            {
                JsonElement light = DaggerfallBaseContent.Object(value, "normalized world light", diagnostics);
                string id = DaggerfallBaseContent.Text(light, "id", diagnostics);
                Vector3 position = ObjectVector3(DaggerfallBaseContent.Property(light, "position", diagnostics), $"normalized light '{id}' position", diagnostics);
                float range = DaggerfallBaseContent.Number(light, "range", diagnostics);
                float intensity = DaggerfallBaseContent.Number(light, "intensity", diagnostics);
                Vector3 color = ObjectVector3(DaggerfallBaseContent.Property(light, "color", diagnostics), $"normalized light '{id}' color", diagnostics);
                try
                {
                    DaggerfallSiteLight projected = new DaggerfallSiteLight(id, new WorldPoint(position.X, position.Y, position.Z), range, intensity, color)
                    { CityLight = OptionalBoolean(light, "cityLight", false, diagnostics) }.Validate();
                    if (!lights.TryAdd(projected.Id, projected)) diagnostics.Add($"Normalized world repeats light '{projected.Id}'.");
                }
                catch (ArgumentException exception)
                {
                    diagnostics.Add($"Normalized light '{id}' is invalid: {exception.Message}");
                }
            }
            return lights.Values.OrderBy(light => light.Id, StringComparer.Ordinal).ToArray();
        }
        catch (JsonException exception)
        {
            diagnostics.Add($"Normalized world document is not valid JSON: {exception.Message}");
            return [];
        }
    }

    internal static DaggerfallLockInteractionSurface DoorSurface(DaggerfallWorldProfileKind profileKind) =>
        profileKind == DaggerfallWorldProfileKind.Exterior
            ? DaggerfallLockInteractionSurface.Exterior
            : DaggerfallLockInteractionSurface.Interior;

    internal static bool TryDoorIdentity(string sourceId, out DaggerfallRdbDoorId identity)
    {
        identity = default;
        string[] parts = sourceId.Split('/', StringSplitOptions.None);
        if (parts.Length != 5 || !StringComparer.Ordinal.Equals(parts[0], "door")
            || (!parts[1].EndsWith("-rdb", StringComparison.Ordinal)
                && !parts[1].EndsWith("-rmb", StringComparison.Ordinal))
            || !int.TryParse(parts[2], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int blockX)
            || !int.TryParse(parts[3], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int blockZ)
            || !int.TryParse(parts[4], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int modelIndex))
            return false;
        bool rmb = parts[1].EndsWith("-rmb", StringComparison.Ordinal);
        string source = parts[1][..^(rmb ? "-rmb".Length : "-rdb".Length)];
        if (source.Length == 0) return false;
        identity = new DaggerfallRdbDoorId($"{source.ToUpperInvariant()}.{(rmb ? "RMB" : "RDB")}", blockX, blockZ, modelIndex);
        return true;
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

    /// <summary>The texture archive of the treasure piles a ground container stands as.</summary>
    private const int TreasureArchive = 216;

    /// <summary>
    /// The records of <see cref="TreasureArchive"/> the classic draws a pile's image from at random, in its order
    /// (DFU <c>DaggerfallLootDataTables.randomTreasureIconIndices</c>): the piles a ground container may stand as.
    /// </summary>
    private static readonly int[] TreasurePileRecords = [0, 1, 3, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 36, 37, 38, 39, 40, 43, 44, 45, 46, 47];

    /// <summary>
    /// The world media publication's actor, billboard and terrain media: every descriptor, billboard and actor entry of
    /// its dungeon sidecar, each checked against the publication's manifest.
    /// </summary>
    private static (IReadOnlyDictionary<int, NormalizedActorSprite> Sprites, IReadOnlyList<NormalizedBillboardSprite>? GroundContainerSprites, IReadOnlyDictionary<(int Archive, int Record), NormalizedBillboardSprite> BillboardSprites, IReadOnlyDictionary<(int Archive, int Record), NormalizedTerrainTexture> TerrainTextures) ReadWorldMedia(
        DaggerfallWorldMedia product,
        DaggerfallContentDiagnostics diagnostics)
    {
        try
        {
            Dictionary<string, MediaResource> resources = [];
            foreach (JsonElement value in product.Resources)
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
                // Engine's managed atlas API currently publishes no frame-limit constant.
                // This is Daggerfall import/publication admission policy matched to the
                // currently supported 4096-frame generated atlas shape, not copied Engine authority.
                if (frames.Count > 4096 || frames.Select(frame => frame.Id).Distinct().Count() != frames.Count)
                {
                    diagnostics.Add($"Generated atlas resource '{id}' exceeds Daggerfall's 4096-frame publication admission limit or repeats a frame id.");
                }
                string path = product.Resolve(relativePath);
                if (!product.TryGetArtifact(path, out ContentSha256 artifactHash) || artifactHash != hash)
                {
                    diagnostics.Add($"Generated media resource '{id}' does not agree with the import manifest.");
                }
                if (!resources.TryAdd(id, new MediaResource(path, hash, atlasWidth, atlasHeight, frames))) diagnostics.Add($"Generated dungeon media repeats resource '{id}'.");
            }

            Dictionary<int, NormalizedActorSprite> sprites = [];
            foreach (JsonElement value in product.Actors)
            {
                JsonElement actor = DaggerfallBaseContent.Object(value, "dungeon actor media", diagnostics);
                int mobileId = DaggerfallBaseContent.Integer(actor, "mobileId", diagnostics);
                if (ReadSprite(actor) is { } sprite && !sprites.TryAdd(mobileId, sprite))
                    diagnostics.Add($"Generated actor media repeats mobile '{mobileId}'.");
            }
            NormalizedActorSprite? ReadSprite(JsonElement actor)
            {
                int mobileId = DaggerfallBaseContent.Integer(actor, "mobileId", diagnostics);
                string spriteId = DaggerfallBaseContent.Text(actor, "spriteResourceId", diagnostics);
                if (!resources.TryGetValue(spriteId, out MediaResource? texture)) { diagnostics.Add($"Generated actor mobile '{mobileId}' refers to missing sprite media '{spriteId}'."); return null; }
                if (texture.Frames.Count == 0) { diagnostics.Add($"Generated actor mobile '{mobileId}' has no atlas frames."); return null; }
                Vector2 pivot = GeneratedVector2(DaggerfallBaseContent.Property(actor, "pivot", diagnostics), "actor.pivot", diagnostics);
                Vector2 size = GeneratedVector2(DaggerfallBaseContent.Property(actor, "worldSize", diagnostics), "actor.worldSize", diagnostics);
                Vector2 sourceSize = GeneratedVector2(DaggerfallBaseContent.Property(actor, "sourceWorldSize", diagnostics), "actor.sourceWorldSize", diagnostics);
                if (!PositiveFinite(size) || !PositiveFinite(sourceSize)) diagnostics.Add($"Generated actor mobile '{mobileId}' has a non-positive world size.");
                (IReadOnlyDictionary<string, NormalizedSpriteState> states, IReadOnlyList<NormalizedAtlasFrame> frames) = ReadActorStates(actor, texture, size, sourceSize, mobileId, diagnostics);
                string? preferredRestState = DaggerfallBaseContent.OptionalText(actor, "preferredRestState", diagnostics);
                if (preferredRestState is not null && !states.ContainsKey(preferredRestState)) diagnostics.Add($"Generated actor mobile '{mobileId}' preferredRestState '{preferredRestState}' is not a published state.");
                IReadOnlyList<NormalizedAttackSequence> attacks = ReadAttackSequences(actor, states, mobileId, diagnostics);
                NormalizedAttackSequence? rangedAttack = ReadRangedAttackSequence(actor, states, mobileId, diagnostics);
                NormalizedActorSprite? corpse = ReadCorpse(actor, resources, mobileId, diagnostics);
                return new NormalizedActorSprite(texture.Path, texture.Hash, texture.AtlasWidth, texture.AtlasHeight, frames, texture.Frames[0].Id, pivot, size)
                {
                    States = states,
                    PreferredRestState = preferredRestState,
                    AttackSequences = attacks,
                    RangedAttackSequence = rangedAttack,
                    Corpse = corpse,
                    Feedback = ReadActorFeedback(actor, mobileId, diagnostics),
                    Glow = resources.TryGetValue($"{spriteId}-glow", out MediaResource? glow) ? new(glow.Path, glow.Hash) : null,
                    Female = actor.TryGetProperty("female", out var female) && female.ValueKind == JsonValueKind.Object ? ReadSprite(female) : null,
                };
            }
            var billboards = ReadBillboardSprites([.. product.Billboards], resources, diagnostics);
            Dictionary<(int Archive, int Record), NormalizedTerrainTexture> terrainTextures = [];
            foreach ((string id, MediaResource resource) in resources)
            {
                if (!TryParseTerrainTextureId(id, out int archive, out int record)) continue;
                if (!terrainTextures.TryAdd((archive, record), new NormalizedTerrainTexture(resource.Path, resource.Hash)))
                    diagnostics.Add($"Generated terrain texture address {archive}/{record} repeats.");
            }
            List<NormalizedBillboardSprite> groundContainerSprites = [];
            foreach (int record in TreasurePileRecords)
            {
                if (billboards.TryGetValue((TreasureArchive, record), out NormalizedBillboardSprite? pile)) groundContainerSprites.Add(pile);
                else diagnostics.Add($"The world media publication has no treasure pile billboard 'sprite/texture-{TreasureArchive}-{record}'.");
            }
            return (new ReadOnlyDictionary<int, NormalizedActorSprite>(sprites.ToDictionary()), groundContainerSprites.AsReadOnly(), billboards, new ReadOnlyDictionary<(int, int), NormalizedTerrainTexture>(terrainTextures));
        }
        catch (Exception exception) when (exception is InvalidOperationException or KeyNotFoundException && exception is not DaggerfallContentException)
        {
            diagnostics.Add($"The world media dungeon sidecar is malformed: {exception.Message}");
            return (new Dictionary<int, NormalizedActorSprite>(), null, new Dictionary<(int, int), NormalizedBillboardSprite>(), new Dictionary<(int, int), NormalizedTerrainTexture>());
        }
    }

    private static IReadOnlyDictionary<(int Archive, int Record), NormalizedBillboardSprite> ReadBillboardSprites(
        IReadOnlyList<JsonElement> entries, IReadOnlyDictionary<string, MediaResource> resources, DaggerfallContentDiagnostics diagnostics)
    {
        Dictionary<(int, int), NormalizedBillboardSprite> result = [];
        foreach (var billboard in entries)
        {
            string id = DaggerfallBaseContent.Text(billboard, "spriteResourceId", diagnostics);
            if (!resources.TryGetValue(id, out var resource) || resource.Frames.Count == 0)
            { diagnostics.Add($"Generated billboard '{id}' has no published sprite atlas."); continue; }
            int archive = DaggerfallBaseContent.Integer(billboard, "textureArchive", diagnostics);
            int record = DaggerfallBaseContent.Integer(billboard, "textureRecord", diagnostics);
            Vector2 pivot = GeneratedVector2(DaggerfallBaseContent.Property(billboard, "pivot", diagnostics), id + ".pivot", diagnostics);
            Vector2 size = GeneratedVector2(DaggerfallBaseContent.Property(billboard, "worldSize", diagnostics), id + ".worldSize", diagnostics);
            if (archive < 0 || record is < 0 or > 127 || id != $"sprite/texture-{archive}-{record}" || !PositiveFinite(size))
            { diagnostics.Add($"Generated billboard '{id}' has an invalid address or world size."); continue; }
            if (!result.TryAdd((archive, record), new(resource.Path, resource.Hash, resource.AtlasWidth, resource.AtlasHeight,
                resource.Frames, resource.Frames[0].Id, pivot, size)
                { FramesPerSecond = billboard.TryGetProperty("playback", out var playback) && playback.ValueKind == JsonValueKind.Object
                    ? DaggerfallBaseContent.Number(playback, "framesPerSecond", diagnostics) : 0f, Source = (archive, record) }))
                diagnostics.Add($"Generated billboard address {archive}/{record} repeats.");
        }
        return new ReadOnlyDictionary<(int, int), NormalizedBillboardSprite>(result);
    }

    private static bool TryParseTerrainTextureId(string id, out int archive, out int record)
    {
        archive = 0;
        record = 0;
        const string prefix = "terrain/texture-";
        if (!id.StartsWith(prefix, StringComparison.Ordinal)) return false;
        string[] parts = id[prefix.Length..].Split('-');
        if (parts is not [string archiveText, string recordText]
            || !int.TryParse(archiveText, out archive)
            || !int.TryParse(recordText, out record)
            || archive is < 0 or > 999
            || record is < 0 or > 127)
        {
            throw new InvalidOperationException($"Generated terrain texture '{id}' does not use the expected 'terrain/texture-<archive>-<record>' source handle.");
        }
        return true;
    }

    private static (IReadOnlyDictionary<string, NormalizedSpriteState> States, IReadOnlyList<NormalizedAtlasFrame> Frames) ReadActorStates(JsonElement actor, MediaResource texture, Vector2 worldSize, Vector2 sourceWorldSize, int mobileId, DaggerfallContentDiagnostics diagnostics)
    {
        Dictionary<string, NormalizedSpriteState> result = new(StringComparer.Ordinal);
        Dictionary<uint, Vector2> displaySizes = [];
        foreach (JsonElement value in DaggerfallBaseContent.Array(actor, "states", diagnostics))
        {
            JsonElement state = DaggerfallBaseContent.Object(value, "actor state", diagnostics);
            string name = DaggerfallBaseContent.Text(state, "state", diagnostics);
            JsonElement playback = DaggerfallBaseContent.Object(DaggerfallBaseContent.Property(state, "playback", diagnostics), "actor state playback", diagnostics);
            float fps = DaggerfallBaseContent.Property(playback, "framesPerSecond", diagnostics).TryGetSingle(out float parsedFps) ? parsedFps : 0F;
            JsonElement loopsValue = DaggerfallBaseContent.Property(playback, "loops", diagnostics);
            bool loops = loopsValue.ValueKind == JsonValueKind.True;
            if (loopsValue.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) diagnostics.Add($"Generated actor mobile '{mobileId}' state '{name}' loops must be a JSON boolean.");
            Dictionary<int, List<uint>> sectors = [];
            foreach (JsonElement frameValue in DaggerfallBaseContent.Array(state, "frames", diagnostics))
            {
                JsonElement frame = DaggerfallBaseContent.Object(frameValue, "actor state frame", diagnostics);
                int orientation = DaggerfallBaseContent.Integer(frame, "orientation", diagnostics);
                if (orientation is < 0 or > 7) diagnostics.Add($"Generated actor mobile '{mobileId}' state '{name}' has orientation outside 0..7.");
                JsonElement atlas = DaggerfallBaseContent.Object(DaggerfallBaseContent.Property(frame, "atlasFrame", diagnostics), "actor state atlas frame", diagnostics);
                uint frameId = checked((uint)DaggerfallBaseContent.Integer(atlas, "frameIndex", diagnostics));
                Vector2 frameSourceWorldSize = GeneratedVector2(DaggerfallBaseContent.Property(frame, "sourceWorldSize", diagnostics), "actor state frame sourceWorldSize", diagnostics);
                if (!PositiveFinite(frameSourceWorldSize)) diagnostics.Add($"Generated actor mobile '{mobileId}' state '{name}' has a non-positive frame sourceWorldSize.");
                Vector2 displaySize = SpriteFrameDisplaySize.Scale(frameSourceWorldSize, worldSize, sourceWorldSize) ?? default;
                if (!PositiveFinite(displaySize)) diagnostics.Add($"Generated actor mobile '{mobileId}' state '{name}' has an invalid scaled frame display size.");
                if (!displaySizes.TryAdd(frameId, displaySize)) diagnostics.Add($"Generated actor mobile '{mobileId}' has an ambiguous sourceWorldSize mapping for atlas frame '{frameId}'.");
                (sectors.TryGetValue(orientation, out List<uint>? sector) ? sector : sectors[orientation] = []).Add(frameId);
            }
            IReadOnlyDictionary<int, IReadOnlyList<uint>> orientations = new ReadOnlyDictionary<int, IReadOnlyList<uint>>(sectors.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<uint>)Array.AsReadOnly(pair.Value.ToArray())));
            IReadOnlyList<uint> frames = orientations.TryGetValue(0, out IReadOnlyList<uint>? forward) ? forward : orientations.Values.FirstOrDefault() ?? [];
            bool completeSectors = orientations.Count == 8
                && Enumerable.Range(0, 8).All(orientations.ContainsKey);
            if (!completeSectors)
                diagnostics.Add($"Generated actor mobile '{mobileId}' state '{name}' must provide all eight directional sectors.");
            if (!float.IsFinite(fps) || fps <= 0F || frames.Count == 0 || orientations.Values.Any(sequence => sequence.Any(frame => !texture.Frames.Any(atlas => atlas.Id == frame))))
                diagnostics.Add($"Generated actor mobile '{mobileId}' state '{name}' has invalid playback frames.");
            if (!result.TryAdd(name, new NormalizedSpriteState(name, frames, fps, loops) { Orientations = orientations })) diagnostics.Add($"Generated actor mobile '{mobileId}' repeats state '{name}'.");
        }
        if (result.Count == 0) diagnostics.Add($"Generated actor mobile '{mobileId}' has no playable states.");
        if (displaySizes.Count != texture.Frames.Count || texture.Frames.Any(frame => !displaySizes.ContainsKey(frame.Id)))
            diagnostics.Add($"Generated actor mobile '{mobileId}' must map every atlas frame to one unambiguous sourceWorldSize.");
        IReadOnlyList<NormalizedAtlasFrame> normalizedFrames = Array.AsReadOnly(texture.Frames.Select(frame =>
            displaySizes.TryGetValue(frame.Id, out Vector2 displaySize)
                ? frame with { DisplaySize = displaySize }
                : frame).ToArray());
        return (new ReadOnlyDictionary<string, NormalizedSpriteState>(result), normalizedFrames);
    }

    private static IReadOnlyList<NormalizedAttackSequence> ReadAttackSequences(JsonElement actor, IReadOnlyDictionary<string, NormalizedSpriteState> states, int mobileId, DaggerfallContentDiagnostics diagnostics)
    {
        List<NormalizedAttackSequence> sequences = [];
        if (!actor.TryGetProperty("sourceAttackSequence", out JsonElement source) || source.ValueKind != JsonValueKind.Object) return sequences;
        if (!states.TryGetValue("primaryAttack", out NormalizedSpriteState? attack))
        {
            diagnostics.Add($"Generated actor mobile '{mobileId}' declares an attack sequence without a primaryAttack state.");
            return sequences;
        }
        List<int> primary = DaggerfallBaseContent.Array(source, "primaryFrames", diagnostics).Select(value => value.TryGetInt32(out int frame) ? frame : int.MinValue).ToList();
        AddAttack(primary, 100, attack, mobileId, diagnostics, sequences);
        foreach (JsonElement alternate in DaggerfallBaseContent.Array(source, "alternates", diagnostics))
        {
            JsonElement value = DaggerfallBaseContent.Object(alternate, "attack alternate", diagnostics);
            int chance = DaggerfallBaseContent.Integer(value, "chance", diagnostics);
            List<int> frames = DaggerfallBaseContent.Array(value, "frames", diagnostics).Select(frame => frame.TryGetInt32(out int parsed) ? parsed : int.MinValue).ToList();
            AddAttack(frames, chance, attack, mobileId, diagnostics, sequences);
        }
        return Array.AsReadOnly(sequences.ToArray());
    }

    private static NormalizedAttackSequence? ReadRangedAttackSequence(JsonElement actor, IReadOnlyDictionary<string, NormalizedSpriteState> states, int mobileId, DaggerfallContentDiagnostics diagnostics)
    {
        if (!actor.TryGetProperty("sourceAttackSequence", out JsonElement source) || source.ValueKind != JsonValueKind.Object) return null;
        // The manifest states the ranged frames as a nullable array: null names a mobile that
        // declares no ranged attack, and the value is published only beside a rangedAttack1 state.
        if (!source.TryGetProperty("rangedFrames", out JsonElement ranged) || ranged.ValueKind != JsonValueKind.Array) return null;
        if (!states.TryGetValue("rangedAttack1", out NormalizedSpriteState? rangedState))
        {
            diagnostics.Add($"Generated actor mobile '{mobileId}' declares a ranged attack sequence without a rangedAttack1 state.");
            return null;
        }
        List<int> frames = ranged.EnumerateArray().Select(value => value.TryGetInt32(out int frame) ? frame : int.MinValue).ToList();
        List<NormalizedAttackSequence> target = [];
        AddAttack(frames, 100, rangedState, mobileId, diagnostics, target, "rangedAttack1");
        return target.Count > 0 ? target[0] : null;
    }

    private static void AddAttack(IReadOnlyList<int> source, int chance, NormalizedSpriteState state, int mobileId, DaggerfallContentDiagnostics diagnostics, List<NormalizedAttackSequence> target, string stateName = "primaryAttack")
    {
        // EnemyBasics supplies one attack script for all eight records. Some genuine source
        // records are shorter than that script's longest direction (e.g. mobile 30 record 6);
        // DFU starts on the current record and abandons an out-of-range directional frame rather
        // than manufacturing one. Validate the canonical initial direction here; the presentation
        // direction switch makes the same per-record bound check before selecting a frame.
        IReadOnlyList<uint> canonical = state.SelectOrientation(0);
        if (chance is < 1 or > 100 || source.Count == 0 || source[^1] == -1 || source.Any(frame => frame < -1 || frame >= canonical.Count))
        {
            diagnostics.Add($"Generated actor mobile '{mobileId}' has an invalid attack sequence.");
            return;
        }
        target.Add(new NormalizedAttackSequence(chance, source, stateName));
    }

    private static DaggerfallActorFeedback? ReadActorFeedback(JsonElement actor, int mobileId, DaggerfallContentDiagnostics diagnostics)
    {
        JsonElement feedback = DaggerfallBaseContent.Property(actor, "feedback", diagnostics);
        if (feedback.ValueKind == JsonValueKind.Null) return null;
        int feedbackMobile = DaggerfallBaseContent.Integer(feedback, "mobileId", diagnostics);
        int blood = DaggerfallBaseContent.Integer(feedback, "bloodIndex", diagnostics);
        if (feedbackMobile != mobileId || blood is < 0 or > 2) diagnostics.Add($"Generated mobile '{mobileId}' feedback identity or blood index is invalid.");
        return new(feedbackMobile,
            DaggerfallBaseContent.Text(feedback, "moveCue", diagnostics), DaggerfallBaseContent.Text(feedback, "barkCue", diagnostics),
            DaggerfallBaseContent.Text(feedback, "attackCue", diagnostics), DaggerfallBaseContent.Boolean(feedback, "parrySounds", diagnostics),
            blood);
    }

    private static NormalizedActorSprite? ReadCorpse(JsonElement actor, IReadOnlyDictionary<string, MediaResource> resources, int mobileId, DaggerfallContentDiagnostics diagnostics)
    {
        if (!actor.TryGetProperty("corpse", out JsonElement corpse) || corpse.ValueKind == JsonValueKind.Null) return null;
        JsonElement value = DaggerfallBaseContent.Object(corpse, "actor corpse", diagnostics);
        string mediaId = DaggerfallBaseContent.Text(value, "mediaId", diagnostics);
        if (!resources.TryGetValue(mediaId, out MediaResource? resource) || resource.Frames.Count == 0)
        {
            diagnostics.Add($"Generated actor mobile '{mobileId}' refers to missing corpse media '{mediaId}'.");
            return null;
        }
        Vector2 pivot = GeneratedVector2(DaggerfallBaseContent.Property(value, "pivot", diagnostics), "actor.corpse.pivot", diagnostics);
        Vector2 size = GeneratedVector2(DaggerfallBaseContent.Property(value, "worldSize", diagnostics), "actor.corpse.worldSize", diagnostics);
        if (size.X <= 0 || size.Y <= 0) diagnostics.Add($"Generated actor mobile '{mobileId}' has a non-positive corpse world size.");
        // The texture record it was cut from names the corpse every human enemy class shares (TEXTURE.380 record 1).
        (int, int)? source = value.TryGetProperty("textureArchive", out JsonElement archive) && value.TryGetProperty("textureRecord", out JsonElement record)
            && archive.TryGetInt32(out int archiveValue) && record.TryGetInt32(out int recordValue) ? (archiveValue, recordValue) : null;
        return new NormalizedActorSprite(resource.Path, resource.Hash, resource.AtlasWidth, resource.AtlasHeight, resource.Frames, resource.Frames[0].Id, pivot, size)
            { SourceTexture = source };
    }

    private sealed record MediaResource(string Path, ContentSha256 Hash, int AtlasWidth, int AtlasHeight, IReadOnlyList<NormalizedAtlasFrame> Frames);

    private static (IReadOnlyList<NormalizedAudioClip> Audio, NormalizedClassicPresentation Presentation) ReadClassicPresentation(AdmittedFiles files, ReadOnlyMemory<byte>? bytes, DaggerfallWorldMedia artifacts, DaggerfallContentDiagnostics diagnostics)
    {
        if (bytes is null) { diagnostics.Add("Generated classic media manifest is unavailable."); return ([], NormalizedClassicPresentation.Empty); }
        try
        {
            using JsonDocument document = JsonDocument.Parse(bytes.Value);
            JsonElement root = DaggerfallBaseContent.Object(document.RootElement, "classic media manifest", diagnostics);
            DaggerfallBaseContent.RejectDuplicateProperties(root, "classic media manifest", diagnostics);
            Dictionary<string, ClassicMediaResource> resources = [];
            JsonElement media = DaggerfallBaseContent.Object(DaggerfallBaseContent.Property(root, "media", diagnostics), "classic media", diagnostics);
            DaggerfallBaseContent.RejectDuplicateProperties(media, "classic media", diagnostics);
            foreach (JsonElement value in DaggerfallBaseContent.Array(media, "resources", diagnostics))
            {
                JsonElement resource = DaggerfallBaseContent.Object(value, "classic media resource", diagnostics);
                DaggerfallBaseContent.RejectDuplicateProperties(resource, "classic media resource", diagnostics);
                string id = DaggerfallBaseContent.Text(resource, "id", diagnostics);
                string kind = DaggerfallBaseContent.Text(resource, "kind", diagnostics);
                string relativePath = DaggerfallBaseContent.Text(resource, "relativePath", diagnostics);
                // Audio resolves into the world media publication and the images into the classic media
                // group, by the publication-relative path the sidecar states.
                string path = artifacts.Resolve(relativePath);
                ContentSha256 hash = ContentHash(DaggerfallBaseContent.Text(resource, "contentDigest", diagnostics), diagnostics);
                if (!artifacts.TryGetArtifact(path, out ContentSha256 artifact) || artifact != hash) diagnostics.Add($"Generated classic audio '{id}' does not agree with the import manifest.");
                long byteLength = Long(resource, "byteLength", diagnostics);
                string mimeType = DaggerfallBaseContent.Text(resource, "mimeType", diagnostics);
                int sourceWidth = DaggerfallBaseContent.Integer(resource, "sourceWidth", diagnostics);
                int sourceHeight = DaggerfallBaseContent.Integer(resource, "sourceHeight", diagnostics);
                int atlasWidth = DaggerfallBaseContent.Integer(resource, "atlasWidth", diagnostics);
                int atlasHeight = DaggerfallBaseContent.Integer(resource, "atlasHeight", diagnostics);
                // WAV descriptors retain their published identity and digest in eager content, but
                // their bodies are opened from the world media audio bundle on the first cue. Classic media the art pack
                // draws anew is described here but left out of a player's data. Everything else is eagerly admitted content.
                bool hasExpectedBody = kind == "audio" || files.LeftOut(path)
                    || files.GetExactlyOne(path).Length == byteLength;
                if (!ValidLogicalId(id) || !ValidLogicalPath(relativePath) || !KnownClassicMediaKind(kind) || byteLength <= 0 || string.IsNullOrWhiteSpace(mimeType) || sourceWidth < 0 || sourceHeight < 0
                    || atlasWidth < 0 || atlasHeight < 0 || !hasExpectedBody)
                    diagnostics.Add($"Classic media descriptor '{id}' does not match the canonical importer contract.");
                List<NormalizedAtlasFrame> frames = [];
                foreach (JsonElement frameValue in DaggerfallBaseContent.Array(resource, "frames", diagnostics))
                {
                    JsonElement frame = DaggerfallBaseContent.Object(frameValue, "classic atlas frame", diagnostics);
                    DaggerfallBaseContent.RejectDuplicateProperties(frame, "classic atlas frame", diagnostics);
                    string frameId = DaggerfallBaseContent.Text(frame, "id", diagnostics);
                    int frameIndex = DaggerfallBaseContent.Integer(frame, "frameIndex", diagnostics);
                    int x = DaggerfallBaseContent.Integer(frame, "x", diagnostics);
                    int y = DaggerfallBaseContent.Integer(frame, "y", diagnostics);
                    int width = DaggerfallBaseContent.Integer(frame, "width", diagnostics);
                    int height = DaggerfallBaseContent.Integer(frame, "height", diagnostics);
                    int frameSourceWidth = DaggerfallBaseContent.Integer(frame, "sourceWidth", diagnostics);
                    int frameSourceHeight = DaggerfallBaseContent.Integer(frame, "sourceHeight", diagnostics);
                    if (!ValidLogicalId(frameId) || frameIndex < 0 || atlasWidth <= 0 || atlasHeight <= 0 || width <= 0 || height <= 0 || frameSourceWidth <= 0 || frameSourceHeight <= 0 || x < 0 || y < 0 || (long)x + width > atlasWidth || (long)y + height > atlasHeight)
                        diagnostics.Add($"Classic media resource '{id}' has an atlas frame outside its declared bounds.");
                    frames.Add(new NormalizedAtlasFrame(checked((uint)Math.Max(frameIndex, 0)), x, y, width, height));
                }
                if ((frames.Count == 0 && (atlasWidth != 0 || atlasHeight != 0)) || (frames.Count != 0 && (atlasWidth <= 0 || atlasHeight <= 0))
                    || frames.Count > 4096 || !frames.Select(frame => frame.Id).SequenceEqual(Enumerable.Range(0, frames.Count).Select(index => (uint)index)))
                    diagnostics.Add($"Classic media resource '{id}' must use canonical contiguous frame indexes and atlas dimensions.");
                Vector2 pivot = OptionalClassicVector2(resource, "pivot", new Vector2(.5F, .5F), $"Classic media resource '{id}' pivot", diagnostics);
                Vector2 displaySize = OptionalClassicVector2(resource, "displaySize", DerivedDisplaySize(frames), $"Classic media resource '{id}' displaySize", diagnostics, positive: true, normalized: false);
                float? framesPerSecond = OptionalSingle(resource, "framesPerSecond", diagnostics);
                bool? loop = OptionalBoolean(resource, "loop", diagnostics);
                IReadOnlyList<int> sequence = OptionalSequence(resource, frames, diagnostics);
                if (!resources.TryAdd(id, new ClassicMediaResource(id, kind, relativePath, path, hash, byteLength, atlasWidth, atlasHeight, Array.AsReadOnly(frames.ToArray()), pivot, displaySize, framesPerSecond, loop, sequence))) diagnostics.Add($"Generated classic media repeats resource '{id}'.");
            }
            List<NormalizedAudioClip> audio = [];
            foreach (JsonElement value in DaggerfallBaseContent.Array(root, "audio", diagnostics))
            {
                JsonElement clip = DaggerfallBaseContent.Object(value, "classic audio mapping", diagnostics);
                DaggerfallBaseContent.RejectDuplicateProperties(clip, "classic audio mapping", diagnostics);
                string id = DaggerfallBaseContent.Text(clip, "clip", diagnostics);
                string mediaId = DaggerfallBaseContent.Text(clip, "mediaId", diagnostics);
                if (!resources.TryGetValue(mediaId, out ClassicMediaResource? resource) || resource.Kind != "audio") diagnostics.Add($"Classic audio mapping '{id}' refers to missing audio media '{mediaId}'.");
                else if (audio.Any(item => item.Id == id)) diagnostics.Add($"Classic media repeats audio mapping '{id}'.");
                else audio.Add(new NormalizedAudioClip(id, resource.Path, resource.Hash, DaggerfallBaseContent.Integer(clip, "sourceNumericId", diagnostics),
                    DaggerfallBaseContent.Integer(clip, "sourceRecordOrdinal", diagnostics)));
            }
            try { OrderedHitCues(audio); }
            catch (InvalidOperationException exception) { diagnostics.Add(exception.Message); }
            IReadOnlyDictionary<string, NormalizedClassicWeapon> weapons = ReadClassicWeapons(root, resources, diagnostics);
            IReadOnlyList<NormalizedClassicEffect> effects = ReadClassicEffects(root, resources, diagnostics);
            Dictionary<string, string> icons = new(StringComparer.Ordinal);
            foreach (JsonElement icon in DaggerfallBaseContent.Array(root, "inventoryIcons", diagnostics))
            {
                string itemId = DaggerfallBaseContent.Text(icon, "itemId", diagnostics);
                string mediaId = DaggerfallBaseContent.Text(icon, "mediaId", diagnostics);
                if (resources.TryGetValue(mediaId, out ClassicMediaResource? resource) && resource.Kind == "userInterface")
                {
                    // The media identity travels to the DOM; the bytes arrive through the published UI
                    // art, so an item icon is a name the pack owns rather than a file bundled beside the UI.
                    icons[itemId] = mediaId;
                }
                else diagnostics.Add($"Inventory icon '{itemId}' refers to missing inventory media.");
            }
            IReadOnlyDictionary<string, NormalizedClassicMediaResource> publishedResources = new ReadOnlyDictionary<string, NormalizedClassicMediaResource>(
                resources.Values.ToDictionary(
                    resource => resource.Id,
                    resource => new NormalizedClassicMediaResource(resource.Id, resource.Kind, resource.RelativePath, resource.Hash, resource.ByteLength),
                    StringComparer.Ordinal));
            IReadOnlyList<DaggerfallMissileVisual> worldVisuals = ReadClassicWorldVisuals(
                root, artifacts, files, diagnostics);
            return (Array.AsReadOnly(audio.ToArray()), new NormalizedClassicPresentation(weapons, effects)
            {
                InventoryIcons = new ReadOnlyDictionary<string, string>(icons),
                Resources = publishedResources,
                WorldVisuals = worldVisuals,
            });
        }
        catch (JsonException exception)
        {
            diagnostics.Add($"Generated classic media manifest is not valid JSON: {exception.Message}");
            return ([], NormalizedClassicPresentation.Empty);
        }
    }

    /// <summary>
    /// Reads the world visuals a site's classic publication carries from classic mesh geometry.
    /// </summary>
    /// <remarks>
    /// A world visual is admitted from the artifacts it names, not from its own description of them: the
    /// import manifest has to carry the mesh and every texture path at the declared digest, and the bodies
    /// have to be present. A descriptor therefore cannot show a mesh or a texture the site did not publish.
    /// </remarks>
    private static IReadOnlyList<DaggerfallMissileVisual> ReadClassicWorldVisuals(
        JsonElement root,
        DaggerfallWorldMedia artifacts,
        AdmittedFiles files,
        DaggerfallContentDiagnostics diagnostics)
    {
        List<DaggerfallMissileVisual> visuals = [];
        if (!root.TryGetProperty("worldVisuals", out JsonElement section))
        {
            // The publication always states the section, so its absence means this sidecar was not
            // produced with the visual the site is supposed to carry rather than that it carries none.
            diagnostics.Add("Classic media manifest states no world visuals, which every published sidecar states.");
            return visuals;
        }

        if (section.ValueKind != JsonValueKind.Array)
        {
            diagnostics.Add("Classic world visuals must be an array.");
            return visuals;
        }

        HashSet<string> mediaIds = new(StringComparer.Ordinal);
        foreach (JsonElement value in section.EnumerateArray())
        {
            JsonElement visual = DaggerfallBaseContent.Object(value, "classic world visual", diagnostics);
            DaggerfallBaseContent.RejectDuplicateProperties(visual, "classic world visual", diagnostics);
            string mediaId = DaggerfallBaseContent.Text(visual, "mediaId", diagnostics);
            string use = DaggerfallBaseContent.Text(visual, "use", diagnostics);
            string meshId = DaggerfallBaseContent.Text(visual, "meshId", diagnostics);
            string relativePath = DaggerfallBaseContent.Text(visual, "relativePath", diagnostics);
            string path = artifacts.Resolve(relativePath);
            ContentSha256 digest = ContentHash(DaggerfallBaseContent.Text(visual, "contentDigest", diagnostics), diagnostics);
            string sourceArchive = DaggerfallBaseContent.Text(visual, "sourceArchive", diagnostics);
            long sourceRecordId = Long(visual, "sourceRecordId", diagnostics);
            int sourceRecordOrdinal = DaggerfallBaseContent.Integer(visual, "sourceRecordOrdinal", diagnostics);
            string sourceRecordDigest = DaggerfallBaseContent.Text(visual, "sourceRecordDigest", diagnostics);
            if (!ValidLogicalId(mediaId) || !mediaIds.Add(mediaId))
            {
                diagnostics.Add($"Classic world visual '{mediaId}' has an invalid or repeated media identity.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(use)
                || !uint.TryParse(meshId, NumberStyles.None, CultureInfo.InvariantCulture, out uint meshNumber)
                || !StringComparer.Ordinal.Equals(meshId, meshNumber.ToString(CultureInfo.InvariantCulture))
                || !ValidLogicalPath(relativePath)
                || !ValidLogicalPath(sourceArchive)
                || sourceRecordId != meshNumber || sourceRecordOrdinal < 0
                || !IsSha256Hex(sourceRecordDigest))
            {
                diagnostics.Add($"Classic world visual '{mediaId}' does not describe its mesh, artifact, or source record.");
                continue;
            }

            if (!artifacts.TryGetArtifact(path, out ContentSha256 artifact) || artifact != digest)
            {
                diagnostics.Add($"Classic world visual '{mediaId}' names '{relativePath}', which the import manifest does not admit at '{digest}'.");
                continue;
            }

            files.RequireExactlyOne(path);

            // The descriptor states the texture each of the mesh's own material slots draws with. The
            // importer publishes each slot from the assembly that wrote the mesh and checks that every
            // slot has its texture; the mesh bytes go to the Engine unread.
            List<DaggerfallMissileTextureBinding> textures = [];
            HashSet<string> materials = new(StringComparer.Ordinal);
            bool usable = true;
            foreach (JsonElement material in DaggerfallBaseContent.Array(visual, "materials", diagnostics))
            {
                JsonElement texture = DaggerfallBaseContent.Object(material, "classic world visual material", diagnostics);
                string materialResourceId = DaggerfallBaseContent.Text(texture, "materialResourceId", diagnostics);
                int slot = DaggerfallBaseContent.Integer(texture, "slot", diagnostics);
                string textureRelativePath = DaggerfallBaseContent.Text(texture, "relativePath", diagnostics);
                string texturePath = artifacts.Resolve(textureRelativePath);
                ContentSha256 textureDigest = ContentHash(DaggerfallBaseContent.Text(texture, "contentDigest", diagnostics), diagnostics);
                long byteLength = Long(texture, "byteLength", diagnostics);
                if (!ValidLogicalPath(textureRelativePath)
                    || !artifacts.TryGetArtifact(texturePath, out ContentSha256 admitted) || admitted != textureDigest
                    || files.GetExactlyOne(texturePath).Length != byteLength)
                {
                    diagnostics.Add($"Classic world visual '{mediaId}' names texture '{textureRelativePath}', which the import manifest does not admit at '{textureDigest}'.");
                    usable = false;
                    continue;
                }

                if (!materials.Add(materialResourceId) || slot < 0 || textures.Any(binding => binding.MeshSlot == (uint)slot))
                {
                    diagnostics.Add($"Classic world visual '{mediaId}' repeats material '{materialResourceId}' or states a negative or repeated slot {slot}.");
                    usable = false;
                    continue;
                }

                textures.Add(new DaggerfallMissileTextureBinding(checked((uint)slot), texturePath, textureDigest));
            }

            if (!usable)
            {
                continue;
            }

            if (textures.Count == 0)
            {
                diagnostics.Add($"Classic world visual '{mediaId}' states no textures.");
                continue;
            }

            try
            {
                visuals.Add(new DaggerfallMissileVisual(mediaId, use, path, artifact, [.. textures.OrderBy(texture => texture.MeshSlot)]).Validate());
            }
            catch (ArgumentException exception)
            {
                diagnostics.Add($"Classic world visual '{mediaId}' is invalid: {exception.Message}");
            }
        }

        return Array.AsReadOnly(visuals.OrderBy(visual => visual.MediaId, StringComparer.Ordinal).ToArray());
    }

    private static IReadOnlyDictionary<string, NormalizedClassicWeapon> ReadClassicWeapons(JsonElement root, IReadOnlyDictionary<string, ClassicMediaResource> resources, DaggerfallContentDiagnostics diagnostics)
    {
        Dictionary<string, NormalizedClassicWeapon> weapons = new(StringComparer.Ordinal);
        foreach (JsonElement entry in DaggerfallBaseContent.Array(root, "weaponMedia", diagnostics))
        {
            string id = DaggerfallBaseContent.Text(entry, "resourceId", diagnostics);
            if (!resources.TryGetValue(id, out ClassicMediaResource? resource) || resource.Kind != "weaponSprite")
            {
                diagnostics.Add($"Classic weapon '{id}' refers to missing weaponSprite media.");
                continue;
            }
            NormalizedClassicWeapon weapon = ReadClassicWeapon(entry, resource, diagnostics);
            if (!weapons.TryAdd(id, weapon)) diagnostics.Add($"Classic weapons repeat '{id}'.");
        }
        if (weapons.Count != resources.Values.Count(resource => resource.Kind == "weaponSprite")) diagnostics.Add("Every weaponSprite requires one action collection.");
        return new ReadOnlyDictionary<string, NormalizedClassicWeapon>(weapons);
    }

    private static NormalizedClassicWeapon ReadClassicWeapon(JsonElement root, ClassicMediaResource resource, DaggerfallContentDiagnostics diagnostics)
    {
        Dictionary<string, NormalizedClassicWeaponAction> actions = new(StringComparer.Ordinal);
        foreach (JsonElement value in DaggerfallBaseContent.Array(root, "actions", diagnostics))
        {
            JsonElement action = DaggerfallBaseContent.Object(value, "classic weapon action", diagnostics);
            DaggerfallBaseContent.RejectDuplicateProperties(action, "classic weapon action", diagnostics);
            string name = DaggerfallBaseContent.Text(action, "action", diagnostics);
            int sourceRecordOrdinal = DaggerfallBaseContent.Integer(action, "sourceRecordOrdinal", diagnostics);
            int start = DaggerfallBaseContent.Integer(action, "frameStart", diagnostics);
            int count = DaggerfallBaseContent.Integer(action, "frameCount", diagnostics);
            string alignment = DaggerfallBaseContent.Text(action, "alignment", diagnostics);
            float offset = DaggerfallBaseContent.Number(action, "screenOffset", diagnostics);
            JsonElement timing = DaggerfallBaseContent.Object(DaggerfallBaseContent.Property(action, "timing", diagnostics), "classic weapon action timing", diagnostics);
            DaggerfallBaseContent.RejectDuplicateProperties(timing, "classic weapon action timing", diagnostics);
            float fps = DaggerfallBaseContent.Property(timing, "framesPerSecond", diagnostics).TryGetSingle(out float parsedFps) ? parsedFps : 0F;
            JsonElement loop = DaggerfallBaseContent.Property(timing, "loop", diagnostics);
            bool loops = loop.ValueKind == JsonValueKind.True;
            if (loop.ValueKind is not (JsonValueKind.True or JsonValueKind.False) || !float.IsFinite(fps) || fps <= 0F || start < 0 || count <= 0 || !float.IsFinite(offset) || alignment is not ("left" or "right" or "center"))
                diagnostics.Add($"Classic weapon action '{name}' has invalid framing, timing, or alignment.");
            short sourceXOffset = checked((short)DaggerfallBaseContent.Integer(action, "sourceXOffset", diagnostics));
            short sourceYOffset = checked((short)DaggerfallBaseContent.Integer(action, "sourceYOffset", diagnostics));
            if (start < 0 || count <= 0 || (long)start + count > resource.Frames.Count) diagnostics.Add($"Classic weapon action '{name}' is outside weaponSprite frame bounds.");
            if (!actions.TryAdd(name, new NormalizedClassicWeaponAction(name, sourceRecordOrdinal, start, count, alignment, offset, fps, loops, sourceXOffset, sourceYOffset) { Sequence = action.TryGetProperty("sequence", out JsonElement sequence) && sequence.ValueKind != JsonValueKind.Null ? sequence.EnumerateArray().Select(frame => frame.GetInt32()).ToArray() : null })) diagnostics.Add($"Classic weapon actions repeat '{name}'.");
        }
        foreach (NormalizedClassicWeaponAction action in actions.Values)
            if (action.Sequence is { } sequence && (sequence.Count == 0 || sequence.Any(frame => frame < action.FrameStart || frame >= (long)action.FrameStart + action.FrameCount))) diagnostics.Add($"Classic weapon action '{action.Name}' sequence is outside its frame range.");
        string[] requiredActions = ["idle", .. NormalizedClassicWeapon.StrikeActions];
        if (actions.Count != requiredActions.Length || requiredActions.Any(name => !actions.ContainsKey(name))) diagnostics.Add("Classic weapons require the normalized ready and six directional attack actions.");
        // Every strike can play a melee swing, whose impact is delivered on the melee hit frame; a strike
        // that ends before it would spend the swing and never land it. The bow's own frame is checked
        // where the selection says which weapons are bows.
        foreach (string strike in NormalizedClassicWeapon.StrikeActions)
            if (actions.TryGetValue(strike, out NormalizedClassicWeaponAction? played) && played.PlayedFrameCount <= NormalizedClassicWeapon.MeleeWeaponHitFrame)
                diagnostics.Add($"Classic weapon '{resource.Id}' strike '{strike}' plays {played.PlayedFrameCount} frames, so it never reaches the melee hit frame {NormalizedClassicWeapon.MeleeWeaponHitFrame}.");
        {
            if (!resource.Frames.Select(frame => frame.Id).Order().SequenceEqual(Enumerable.Range(0, resource.Frames.Count).Select(index => (uint)index))) diagnostics.Add("Classic weaponSprite frames must use contiguous canonical frame indexes.");
            HashSet<int> covered = [];
            foreach (NormalizedClassicWeaponAction action in actions.Values)
            {
                long end = (long)action.FrameStart + action.FrameCount;
                if (action.FrameStart < 0 || action.FrameCount <= 0 || end > resource.Frames.Count) continue;
                for (int frame = action.FrameStart; frame < (int)end; frame++) covered.Add(frame);
            }
            if (!covered.SetEquals(Enumerable.Range(0, resource.Frames.Count))) diagnostics.Add("Classic weapon action ranges must cover every canonical weapon frame.");
        }
        return new NormalizedClassicWeapon(resource.Id, resource.Path, resource.Hash, resource.AtlasWidth, resource.AtlasHeight, resource.Frames, resource.Pivot, resource.DisplaySize, resource.Sequence, new ReadOnlyDictionary<string, NormalizedClassicWeaponAction>(actions));
    }

    private static IReadOnlyList<NormalizedClassicEffect> ReadClassicEffects(JsonElement root, IReadOnlyDictionary<string, ClassicMediaResource> resources, DaggerfallContentDiagnostics diagnostics)
    {
        Dictionary<string, NormalizedClassicEffect> effects = new(StringComparer.Ordinal);
        foreach (JsonElement value in DaggerfallBaseContent.Array(root, "effects", diagnostics))
        {
            JsonElement effect = DaggerfallBaseContent.Object(value, "classic effect", diagnostics);
            DaggerfallBaseContent.RejectDuplicateProperties(effect, "classic effect", diagnostics);
            string name = DaggerfallBaseContent.Text(effect, "effect", diagnostics);
            string mediaId = DaggerfallBaseContent.Text(effect, "mediaId", diagnostics);
            int sourceRecordOrdinal = DaggerfallBaseContent.Integer(effect, "sourceRecordOrdinal", diagnostics);
            JsonElement timing = DaggerfallBaseContent.Object(DaggerfallBaseContent.Property(effect, "timing", diagnostics), "classic effect timing", diagnostics);
            DaggerfallBaseContent.RejectDuplicateProperties(timing, "classic effect timing", diagnostics);
            float fps = DaggerfallBaseContent.Property(timing, "framesPerSecond", diagnostics).TryGetSingle(out float parsedFps) ? parsedFps : 0F;
            JsonElement loop = DaggerfallBaseContent.Property(timing, "loop", diagnostics);
            bool loops = loop.ValueKind == JsonValueKind.True;
            if (!resources.TryGetValue(mediaId, out ClassicMediaResource? resource) || resource.Kind != "effectSprite" || resource.Frames.Count == 0 || !float.IsFinite(fps) || fps <= 0F || loop.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
                || resource.FramesPerSecond != fps || resource.Loop != loops)
            {
                diagnostics.Add($"Classic effect '{name}' refers to invalid effectSprite media or timing.");
                continue;
            }
            if (!effects.TryAdd(name, new NormalizedClassicEffect(name, mediaId, sourceRecordOrdinal, resource.Path, resource.Hash, resource.AtlasWidth, resource.AtlasHeight, resource.Frames, resource.Pivot, resource.DisplaySize, resource.Sequence, fps, loops))) diagnostics.Add($"Classic effects repeat '{name}'.");
        }
        // Blood and the sparkle are TEXTURE.380 records 0-3; each element's missile and impact are records 0 and 1 of its own archive.
        (string Name, int Ordinal)[] expected = [("blood0", 0), ("blood1", 1), ("blood2", 2), ("magicSparkle", 3),
            .. DaggerfallSpellElements.Names.SelectMany(element => new[] { ($"{element}Missile", 0), ($"{element}Impact", 1) }),
            // The inventory window's glints, TEXTURE.434 records 5 and 6.
            ("magicGlint", 5), ("coinGlint", 6)];
        if (effects.Count != expected.Length || expected.Any(value => !effects.ContainsKey(value.Name))) diagnostics.Add("Classic effects must provide blood0..blood2, magicSparkle, each element's missile and impact, and the magic and coin glints exactly once.");
        foreach ((string expectedName, int ordinal) in expected)
            if (effects.TryGetValue(expectedName, out NormalizedClassicEffect? effect) && effect.SourceRecordOrdinal != ordinal) diagnostics.Add($"Classic effect '{expectedName}' must retain source record ordinal {ordinal}.");
        return Array.AsReadOnly(effects.Values.OrderBy(effect => effect.Name, StringComparer.Ordinal).ToArray());
    }

    private static Vector2 OptionalClassicVector2(JsonElement objectValue, string property, Vector2 fallback, string name, DaggerfallContentDiagnostics diagnostics, bool positive = false, bool normalized = true)
    {
        if (!objectValue.TryGetProperty(property, out JsonElement value) || value.ValueKind == JsonValueKind.Null) return fallback;
        Vector2 parsed = GeneratedVector2(value, name, diagnostics);
        if (normalized && (parsed.X is < 0F or > 1F || parsed.Y is < 0F or > 1F)) diagnostics.Add($"'{name}' must be normalized within [0,1].");
        if (positive && (parsed.X <= 0F || parsed.Y <= 0F)) diagnostics.Add($"'{name}' must be positive.");
        return parsed;
    }

    private static Vector2 DerivedDisplaySize(IReadOnlyList<NormalizedAtlasFrame> frames)
    {
        NormalizedAtlasFrame? canonicalFrame = frames.SingleOrDefault(frame => frame.Id == 0);
        if (canonicalFrame is null || canonicalFrame.Width <= 0 || canonicalFrame.Height <= 0) return Vector2.One;
        float largest = Math.Max(canonicalFrame.Width, canonicalFrame.Height);
        return new(canonicalFrame.Width / largest, canonicalFrame.Height / largest);
    }

    // Daggerfall.Import's NormalizedImportDocument admits descriptors by the same rules. The runtime ruleset
    // may not reference the offline importer, so NormalizedContractRoundTripTests holds the two together.
    private static bool ValidLogicalId(string value) => !string.IsNullOrWhiteSpace(value) && !value.Any(char.IsWhiteSpace);
    private static bool ValidLogicalPath(string value) => !string.IsNullOrWhiteSpace(value) && !value.StartsWith("/", StringComparison.Ordinal) && !value.StartsWith('\\') && !value.Contains('\\') && !value.Split('/').Any(segment => segment is "." or ".." or "");
    private static bool IsSha256Hex(string value) => value.Length == 64 && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
    /// <summary>The media kinds a published descriptor may state (the importer's <c>NormalizedMediaKind</c>, camelCased).</summary>
    internal static readonly IReadOnlySet<string> ClassicMediaKinds = new HashSet<string>(
        ["texture", "billboard", "enemySprite", "weaponSprite", "effectSprite", "audio", "userInterface"], StringComparer.Ordinal);

    private static bool KnownClassicMediaKind(string value) => ClassicMediaKinds.Contains(value);

    private static float? OptionalSingle(JsonElement objectValue, string property, DaggerfallContentDiagnostics diagnostics)
    {
        if (!objectValue.TryGetProperty(property, out JsonElement value) || value.ValueKind == JsonValueKind.Null) return null;
        if (!value.TryGetSingle(out float parsed) || !float.IsFinite(parsed) || parsed <= 0F) { diagnostics.Add($"'{property}' must be a positive finite number when present."); return null; }
        return parsed;
    }

    private static bool? OptionalBoolean(JsonElement objectValue, string property, DaggerfallContentDiagnostics diagnostics)
    {
        if (!objectValue.TryGetProperty(property, out JsonElement value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) { diagnostics.Add($"'{property}' must be a boolean when present."); return null; }
        return value.GetBoolean();
    }

    private static IReadOnlyList<int> OptionalSequence(JsonElement objectValue, IReadOnlyList<NormalizedAtlasFrame> frames, DaggerfallContentDiagnostics diagnostics)
    {
        if (!objectValue.TryGetProperty("sequence", out JsonElement value) || value.ValueKind == JsonValueKind.Null) return Array.AsReadOnly(frames.Select(frame => checked((int)frame.Id)).ToArray());
        List<int> result = [];
        foreach (JsonElement item in DaggerfallBaseContent.Array(objectValue, "sequence", diagnostics))
        {
            if (!item.TryGetInt32(out int index) || index < 0 || !frames.Any(frame => frame.Id == index)) diagnostics.Add("Classic media sequence must refer to regenerated frame indexes.");
            else result.Add(index);
        }
        if (result.Count == 0) diagnostics.Add("Classic media sequence cannot be empty when present.");
        return Array.AsReadOnly(result.ToArray());
    }

    /// <summary>Defines the Daggerfall hit-cue family carried by normalized classic audio mappings.</summary>
    internal static IReadOnlyList<string> OrderedHitCues(IReadOnlyList<NormalizedAudioClip> audio)
    {
        List<(int Ordinal, string Id)> parsed = [];
        foreach (NormalizedAudioClip clip in audio)
        {
            if (!clip.Id.StartsWith("hit", StringComparison.Ordinal)) continue;
            if (!int.TryParse(clip.Id.AsSpan(3), out int ordinal) || ordinal <= 0 || clip.Id != $"hit{ordinal}")
                throw new InvalidOperationException("Daggerfall hit cue IDs must be hit followed by a positive ordinal.");
            parsed.Add((ordinal, clip.Id));
        }
        parsed.Sort((left, right) => left.Ordinal.CompareTo(right.Ordinal));
        if (parsed.Count == 0
            || parsed.Select((item, index) => item.Ordinal == index + 1).Any(valid => !valid)
            || parsed.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != parsed.Count)
            throw new InvalidOperationException("Daggerfall normalized audio must provide contiguous hit1..hitN cues.");
        return Array.AsReadOnly(parsed.Select(item => item.Id).ToArray());
    }

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

    private static bool PositiveFinite(Vector2 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && value.X > 0F && value.Y > 0F;

    internal static AuthoredWorldAppearance ReadWorldAppearance(JsonElement value, DaggerfallContentDiagnostics diagnostics)
    {
        RenderLayer layer = DaggerfallBaseContent.Text(value, "layer", diagnostics) switch { "scene" => RenderLayer.Scene, _ => InvalidLayer(diagnostics) };
        return new(ColorValue(DaggerfallBaseContent.Property(value, "tint", diagnostics), "world.appearance.tint", diagnostics), new Transform(Vector3Value(DaggerfallBaseContent.Property(value, "position", diagnostics), "world.appearance.position", diagnostics), QuaternionValue(DaggerfallBaseContent.Property(value, "rotation", diagnostics), "world.appearance.rotation", diagnostics), Vector3Value(DaggerfallBaseContent.Property(value, "scale", diagnostics), "world.appearance.scale", diagnostics)), Boolean(value, "visible", diagnostics), layer);
    }

    private static Vector3 Vector3Value(JsonElement value, string name, DaggerfallContentDiagnostics diagnostics)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != 3) { diagnostics.Add($"'{name}' must be a three-number vector."); return default; }
        return new(NumberAt(value, 0, name, diagnostics), NumberAt(value, 1, name, diagnostics), NumberAt(value, 2, name, diagnostics));
    }
    private static Vector3 ObjectVector3(JsonElement value, string name, DaggerfallContentDiagnostics diagnostics)
    {
        if (value.ValueKind != JsonValueKind.Object) { diagnostics.Add($"'{name}' must be an object with x, y and z."); return default; }
        return new(Number(DaggerfallBaseContent.Property(value, "x", diagnostics), name, diagnostics),
            Number(DaggerfallBaseContent.Property(value, "y", diagnostics), name, diagnostics),
            Number(DaggerfallBaseContent.Property(value, "z", diagnostics), name, diagnostics));
    }

    /// <summary>An action's published right-handed motion.</summary>
    private static DaggerfallDungeonMotionSpecification Motion(JsonElement value, string name, DaggerfallContentDiagnostics diagnostics)
    {
        DaggerfallDungeonMotionKind? kind = DaggerfallBaseContent.Text(value, "kind", diagnostics) switch
        {
            "translation" => DaggerfallDungeonMotionKind.Translation,
            "rotation" => DaggerfallDungeonMotionKind.Rotation,
            _ => null,
        };
        if (kind is null) diagnostics.Add($"{name} has an unknown kind.");
        DaggerfallDungeonMotionSpecification motion = new(kind ?? DaggerfallDungeonMotionKind.Translation,
            DaggerfallWorldBlocks.Vector(DaggerfallBaseContent.Property(value, "translation", diagnostics), $"{name} translation", diagnostics),
            DaggerfallWorldBlocks.Vector(DaggerfallBaseContent.Property(value, "rotationAxis", diagnostics), $"{name} rotation axis", diagnostics),
            DaggerfallBaseContent.Number(value, "rotationDegrees", diagnostics) * (MathF.PI / 180F),
            DaggerfallBaseContent.Number(value, "durationSeconds", diagnostics));
        try { motion.Validate(); }
        catch (ArgumentException exception) { diagnostics.Add($"{name} is invalid: {exception.Message}"); }
        return motion;
    }

    private static Vector3? OptionalObjectVector3(JsonElement value, string property, string name, DaggerfallContentDiagnostics diagnostics)
    {
        if (!value.TryGetProperty(property, out JsonElement result) || result.ValueKind == JsonValueKind.Null) return null;
        return ObjectVector3(result, name, diagnostics);
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

    private static bool OptionalBoolean(JsonElement value, string property, bool fallback, DaggerfallContentDiagnostics diagnostics)
    {
        if (!value.TryGetProperty(property, out JsonElement result) || result.ValueKind == JsonValueKind.Null) return fallback;
        if (result.ValueKind is JsonValueKind.True or JsonValueKind.False) return result.GetBoolean();
        diagnostics.Add($"'{property}' must be a boolean when present.");
        return fallback;
    }

    private static string? OptionalNullableText(JsonElement value, string property, DaggerfallContentDiagnostics diagnostics)
    {
        if (!value.TryGetProperty(property, out JsonElement result) || result.ValueKind == JsonValueKind.Null) return null;
        if (result.ValueKind == JsonValueKind.String && result.GetString() is { Length: > 0 } text) return text;
        diagnostics.Add($"'{property}' must be a non-empty string or null when present.");
        return null;
    }

    private static uint Unsigned32(JsonElement value, string property, DaggerfallContentDiagnostics diagnostics)
    {
        JsonElement result = DaggerfallBaseContent.Property(value, property, diagnostics);
        if (result.ValueKind == JsonValueKind.Number && result.TryGetUInt32(out uint number)) return number;
        diagnostics.Add($"'{property}' must be an unsigned 32-bit integer.");
        return 0;
    }

    private static byte Byte(JsonElement value, string property, DaggerfallContentDiagnostics diagnostics)
    {
        int number = DaggerfallBaseContent.Integer(value, property, diagnostics);
        if (number is >= byte.MinValue and <= byte.MaxValue) return (byte)number;
        diagnostics.Add($"'{property}' must be an unsigned byte.");
        return 0;
    }

    private static ushort UShort(JsonElement value, string property, DaggerfallContentDiagnostics diagnostics)
    {
        int number = DaggerfallBaseContent.Integer(value, property, diagnostics);
        if (number is >= ushort.MinValue and <= ushort.MaxValue) return (ushort)number;
        diagnostics.Add($"'{property}' must be an unsigned 16-bit integer.");
        return 0;
    }

    private static float NumberAt(JsonElement value, int index, string name, DaggerfallContentDiagnostics diagnostics)
    {
        if (value[index].ValueKind == JsonValueKind.Number && value[index].TryGetSingle(out float number) && float.IsFinite(number)) return number;
        diagnostics.Add($"'{name}' values must be finite numbers.");
        return 0f;
    }
    private static float Number(JsonElement value, string name, DaggerfallContentDiagnostics diagnostics)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetSingle(out float number) && float.IsFinite(number)) return number;
        diagnostics.Add($"'{name}' values must be finite numbers.");
        return 0F;
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

/// <summary>Deterministic interactive in-world notice board placed at a town square or tavern entrance.</summary>
internal sealed record DaggerfallSiteNoticeBoard(
    string Id,
    WorldPoint Position,
    Vector3 SurfaceNormal,
    string Label,
    double ReachDistance = 3.0,
    double SightHeight = 1.15)
{
    internal DaggerfallSiteNoticeBoard Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(Label);
        if (!float.IsFinite(Position.X) || !float.IsFinite(Position.Y) || !float.IsFinite(Position.Z))
            throw new ArgumentOutOfRangeException(nameof(Position), $"Notice board '{Id}' position must be finite.");
        if (!float.IsFinite(SurfaceNormal.X) || !float.IsFinite(SurfaceNormal.Y) || !float.IsFinite(SurfaceNormal.Z)
            || SurfaceNormal.LengthSquared() < 1e-4f)
        {
            throw new ArgumentOutOfRangeException(nameof(SurfaceNormal), $"Notice board '{Id}' surface normal must be non-zero and finite.");
        }
        if (!double.IsFinite(ReachDistance) || ReachDistance <= 0d)
            throw new ArgumentOutOfRangeException(nameof(ReachDistance), $"Notice board '{Id}' reach distance must be positive.");
        if (!double.IsFinite(SightHeight) || SightHeight < 0d)
            throw new ArgumentOutOfRangeException(nameof(SightHeight), $"Notice board '{Id}' sight height must be non-negative.");
        return this;
    }
}

/// <summary>The product-wide world media every assembled location draws from.</summary>
internal sealed record DaggerfallProductMedia(
    IReadOnlyDictionary<int, NormalizedActorSprite> MobileSprites,
    IReadOnlyList<NormalizedBillboardSprite>? GroundContainerSprites,
    IReadOnlyDictionary<(int Archive, int Record), NormalizedBillboardSprite> BillboardSprites,
    IReadOnlyDictionary<(int Archive, int Record), NormalizedTerrainTexture> TerrainTextures,
    IReadOnlyList<NormalizedAudioClip> Audio,
    NormalizedClassicPresentation ClassicPresentation,
    IReadOnlyDictionary<string, ContentArtifact> Textures)
{
    /// <summary>The materials whose texture cycles through several frames, by material id.</summary>
    internal IReadOnlyDictionary<string, DaggerfallMaterialFrames> Animations { get; init; } = new Dictionary<string, DaggerfallMaterialFrames>();
}

internal sealed class AdmittedFiles
{
    private readonly DaggerfallData _content;
    private AdmittedFiles(DaggerfallData content) => _content = content;
    internal static AdmittedFiles From(DaggerfallData content) => new(content ?? throw new ArgumentNullException(nameof(content)));
    internal bool ContainsExactlyOne(string path) => _content.Contains(path);

    /// <summary>Whether a file is classic media a player's data leaves out because the art pack draws it anew.</summary>
    internal bool LeftOut(string path) => _content.LeftOut(path);

    /// <summary>Requires a file to be admitted without reading it.</summary>
    internal void RequireExactlyOne(string path)
    {
        if (!_content.Contains(path))
            throw new DaggerfallContentException([$"Required admitted file '{path}' is missing; the site cannot be loaded without it."]);
    }
    internal ReadOnlyMemory<byte> GetExactlyOne(string path) => _content.TryReadFile(path, out ReadOnlyMemory<byte> bytes)
        ? bytes
        : throw new DaggerfallContentException([$"Required admitted file '{path}' is missing; the site cannot be loaded without it."]);
}

internal sealed record AuthoredWorldAppearance(Color Tint, Transform Transform, bool Visible, RenderLayer Layer);
internal sealed record ContentArtifact(string Path, ContentSha256 Sha256);
internal sealed record NormalizedMaterial(uint Slot, string TexturePath, ContentSha256 TextureSha256, string MaterialResourceId = "")
{
    internal ContentArtifact? WinterTexture { get; init; }
    internal ContentArtifact? RainTexture { get; init; }
    /// <summary>The mask of the window glass each season's texture shows, on an exterior's window textures.</summary>
    internal ContentArtifact? SummerGlass { get; init; }
    internal ContentArtifact? WinterGlass { get; init; }
    internal ContentArtifact? RainGlass { get; init; }
    /// <summary>The frames the texture cycles through, where its record has several.</summary>
    internal DaggerfallMaterialFrames? Frames { get; init; }
}

/// <summary>
/// A classic texture record of several frames, a texture apiece, the record's first first, which a surface drawing it
/// cycles at <paramref name="FramesPerSecond"/>, as the donor's <c>AnimatedMaterial</c> does a hearth's fire.
/// </summary>
internal sealed record DaggerfallMaterialFrames(IReadOnlyList<ContentArtifact> Textures, float FramesPerSecond)
{
    /// <summary>The cycle as a flipbook, which steps on the same clock as a remodel's strip.</summary>
    internal DaggerfallArtFlipbook Flipbook => new(Textures.Count, FramesPerSecond);
}
internal sealed record NormalizedAtlasFrame(uint Id, int X, int Y, int Width, int Height, Vector2? DisplaySize = null);
internal sealed record NormalizedSpriteState(string Name, IReadOnlyList<uint> Frames, float FramesPerSecond, bool Loops)
{
    /// <summary>Ruleset-authored effective playback rate; defaults to the normalized import rate.</summary>
    internal float EffectiveFramesPerSecond { get; init; } = FramesPerSecond;
    /// <summary>All normalized directional sectors; callers must supply an explicit heading before selecting one.</summary>
    internal IReadOnlyDictionary<int, IReadOnlyList<uint>> Orientations { get; init; } = new ReadOnlyDictionary<int, IReadOnlyList<uint>>(new Dictionary<int, IReadOnlyList<uint>>());
    internal IReadOnlyList<uint> SelectOrientation(int sector)
    {
        if (Orientations.Count == 0) return Frames;
        return Orientations.TryGetValue(sector, out IReadOnlyList<uint>? exact)
            ? exact
            : throw new InvalidOperationException($"Normalized Daggerfall sprite state '{Name}' lacks directional sector '{sector}'.");
    }
}
internal sealed record NormalizedAttackSequence(int Chance, IReadOnlyList<int> SourceFrames, string State = "primaryAttack");
internal sealed record NormalizedAudioClip(string Id, string Path, ContentSha256 Sha256, int? SourceNumericId = null, int? SourceRecordOrdinal = null);

internal sealed record NormalizedClassicWeaponAction(string Name, int SourceRecordOrdinal, int FrameStart, int FrameCount, string Alignment, float ScreenOffset, float FramesPerSecond, bool Loops, short SourceXOffset, short SourceYOffset)
{
    internal IReadOnlyList<int>? Sequence { get; init; }
    /// <summary>How many frames the action plays: its authored sequence, or its frame range.</summary>
    internal int PlayedFrameCount => Sequence?.Count ?? FrameCount;
}
internal sealed record NormalizedClassicWeapon(string ResourceId, string TexturePath, ContentSha256 TextureSha256, int AtlasWidth, int AtlasHeight, IReadOnlyList<NormalizedAtlasFrame> Frames, Vector2 Pivot, Vector2 DisplaySize, IReadOnlyList<int> Sequence, IReadOnlyDictionary<string, NormalizedClassicWeaponAction> Actions)
{
    /// <summary>The classic art's six strike actions, each one a donor FPSWeapon strike state.</summary>
    internal static IReadOnlyList<string> StrikeActions { get; } = ["strikeDown", "strikeDownLeft", "strikeLeft", "strikeRight", "strikeDownRight", "strikeUp"];

    /// <summary>The strike action a swing or a bow's release plays.</summary>
    internal const string PlayedStrike = "strikeDown";

    /// <summary>The classic weapon animation frame a melee swing's damage lands on. The donor's FPSWeapon
    /// reports frame 2 for every melee weapon and frame 5 for a bow, and WeaponManager runs its melee
    /// damage when the playing swing's current frame reaches it, which is why one swing is not one
    /// instant.
    /// </summary>
    internal const int MeleeWeaponHitFrame = 2;

    /// <summary>
    /// The classic bow animation frame a loosed arrow leaves on: the donor's FPSWeapon reports frame 5
    /// for a bow where every melee weapon reports frame 2.
    /// </summary>
    internal const int BowWeaponHitFrame = 5;
}
internal sealed record NormalizedClassicEffect(string Name, string MediaId, int SourceRecordOrdinal, string TexturePath, ContentSha256 TextureSha256, int AtlasWidth, int AtlasHeight, IReadOnlyList<NormalizedAtlasFrame> Frames, Vector2 Pivot, Vector2 DisplaySize, IReadOnlyList<int> Sequence, float FramesPerSecond, bool Loops);
internal sealed record NormalizedClassicPresentation(IReadOnlyDictionary<string, NormalizedClassicWeapon> Weapons, IReadOnlyList<NormalizedClassicEffect> Effects)
{
    internal static NormalizedClassicPresentation Empty { get; } = new(new Dictionary<string, NormalizedClassicWeapon>(), Array.Empty<NormalizedClassicEffect>());
    internal IReadOnlyDictionary<string, string> InventoryIcons { get; init; } = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());
    /// <summary>
    /// Every normalized classic descriptor admitted by the selected site's generated sidecar. The
    /// ruleset joins this eager metadata to the public media inventory once during composition;
    /// callers can then request the declared bodies lazily by their public paths.
    /// </summary>
    internal IReadOnlyDictionary<string, NormalizedClassicMediaResource> Resources { get; init; } = new ReadOnlyDictionary<string, NormalizedClassicMediaResource>(new Dictionary<string, NormalizedClassicMediaResource>());
    internal IReadOnlyDictionary<string, string> CompatibleItemVisuals { get; init; } = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());
    internal string? UnarmedVisual { get; init; }
    internal ClassicViewmodelStyle? Viewmodel { get; init; }
    /// <summary>
    /// The world visuals the site published from classic mesh geometry, admitted with the artifact each
    /// one names. A missile the rules put in flight is shown through the visual it selects here.
    /// </summary>
    internal IReadOnlyList<DaggerfallMissileVisual> WorldVisuals { get; init; } = [];
    internal bool TryEffect(string name, out NormalizedClassicEffect? effect)
    {
        effect = Effects.FirstOrDefault(candidate => candidate.Name == name);
        return effect is not null;
    }
}
internal sealed record ClassicViewmodelStyle(int RenderOrder);
internal sealed record NormalizedClassicMediaResource(string Id, string Kind, string RelativePath, ContentSha256 Sha256, long ByteLength);
internal sealed record NormalizedBillboardSprite(string TexturePath, ContentSha256 TextureSha256, int AtlasWidth, int AtlasHeight, IReadOnlyList<NormalizedAtlasFrame> Frames, uint InitialFrameId, Vector2 Pivot, Vector2 Size)
{
    internal float FramesPerSecond { get; init; }

    /// <summary>The texture record the billboard is cut from, by which the refreshed set redraws it.</summary>
    internal (int Archive, int Record)? Source { get; init; }

    /// <summary>This billboard, cut from <paramref name="source"/>, as an actor's sprite. An actor's pose is where it stands, so the feet sit on the pose, not the billboard's centre.</summary>
    internal NormalizedActorSprite Standing((int Archive, int Record) source) =>
        new(TexturePath, TextureSha256, AtlasWidth, AtlasHeight, Frames, InitialFrameId, new Vector2(.5F, 0F), Size) { SourceTexture = source };
}
internal sealed record NormalizedTerrainTexture(string TexturePath, ContentSha256 TextureSha256);
/// <summary>Ruleset input projected from an importer-owned source population placement.</summary>
internal sealed record DaggerfallPopulationPlacement(string Id, WorldPoint Position, int BillboardArchive,
    int BillboardRecord, int FactionId, byte Flags, ushort NameSeed)
{
    internal int SourceBuildingType { get; init; }
    internal int SourceBuildingFactionId { get; init; }
    internal bool IsGuard { get; init; }
    internal DaggerfallGuardPostKind GuardPost { get; init; } = DaggerfallGuardPostKind.StreetWatch;
    /// <summary>For a <see cref="DaggerfallGuardPostKind.WalledCityGateOfficer"/>, the profile-frame gate anchor this officer guards.</summary>
    internal WorldPoint? GatePostAnchor { get; init; }
    internal string? FamilyId { get; init; }

    /// <summary>
    /// For a guard a town holds in reserve, how many extra guards a crime crackdown must call for before this one walks
    /// (1 first); 0 for every other walker.
    /// </summary>
    internal int CrackdownGuard { get; init; }
}
internal sealed record DaggerfallActorFeedback(int MobileId, string MoveCue, string BarkCue, string AttackCue, bool ParrySounds, int BloodIndex);
internal sealed record NormalizedActorSprite(string TexturePath, ContentSha256 TextureSha256, int AtlasWidth, int AtlasHeight, IReadOnlyList<NormalizedAtlasFrame> Frames, uint InitialFrameId, Vector2 Pivot, Vector2 Size)
{
    internal DaggerfallActorFeedback? Feedback { get; init; }
    internal IReadOnlyDictionary<string, NormalizedSpriteState> States { get; init; } = new ReadOnlyDictionary<string, NormalizedSpriteState>(new Dictionary<string, NormalizedSpriteState>());
    /// <summary>Resolved Daggerfall rest-state policy. Null preserves the generic idle-then-move fallback.</summary>
    internal string? PreferredRestState { get; init; }
    internal IReadOnlyList<NormalizedAttackSequence> AttackSequences { get; init; } = Array.Empty<NormalizedAttackSequence>();
    /// <summary>
    /// The published ranged attack declaration, kept out of the melee alternate pool. A mobile
    /// carrying it also publishes the rangedAttack1 state its frames play against; presence of
    /// that published state is the donor's HasRangedAttack1 fact.
    /// </summary>
    internal NormalizedAttackSequence? RangedAttackSequence { get; init; }
    internal NormalizedActorSprite? Corpse { get; init; }
    /// <summary>The classic texture archive and record this sprite was cut from, when the publication states it.</summary>
    internal (int Archive, int Record)? SourceTexture { get; init; }
    internal NormalizedActorSprite? Female { get; init; }
    /// <summary>
    /// What of the sprite shines and in what colour, in the atlas's own layout: a spectral actor's red eyes and pale bones,
    /// cut by the importer from shade levels the published atlas no longer carries.
    /// </summary>
    internal ContentArtifact? Glow { get; init; }

    internal NormalizedActorSprite ForGender(string gender) => gender == "female" && Female is { } female
        ? female with { Corpse = female.Corpse ?? Corpse } : this;
}
internal sealed class DaggerfallSiteProfile(ProjectFacts project, DaggerfallSiteGeometry geometry, AuthoredWorldAppearance worldAppearance, PlayerInitialLook initialLook, IReadOnlyList<NormalizedMaterial> materials, IReadOnlyDictionary<long, NormalizedActorSprite> actorSprites, IReadOnlyDictionary<int, NormalizedActorSprite>? mobileSprites = null, IReadOnlyList<NormalizedAudioClip>? audio = null, NormalizedClassicPresentation? classicPresentation = null, DaggerfallSiteId? site = null, IReadOnlyList<DaggerfallRdbDoorDefinition>? doors = null, DaggerfallWorldProfileKind profileKind = DaggerfallWorldProfileKind.Dungeon, string? logicalProfileId = null, IReadOnlyList<DaggerfallSitePortal>? portals = null, IReadOnlyList<DaggerfallSiteAnchor>? anchors = null, IReadOnlyList<DaggerfallSiteLight>? lights = null, IReadOnlyList<NormalizedBillboardSprite>? groundContainerSprites = null, DaggerfallDungeonMapContent? dungeonMap = null, IReadOnlyList<DaggerfallDungeonActionDefinition>? dungeonActions = null, IReadOnlyList<DaggerfallDungeonActionModelDefinition>? dungeonActionModels = null, DaggerfallInteriorBuilding? interiorBuilding = null, IReadOnlyList<DaggerfallSiteMarker>? questMarkers = null, IReadOnlyDictionary<(int Archive, int Record), NormalizedBillboardSprite>? billboardSprites = null, IReadOnlyList<DaggerfallStaticNpcPlacement>? staticNpcs = null, IReadOnlyList<CharacterWaterVolume>? waterVolumes = null, IReadOnlyDictionary<(int Archive, int Record), NormalizedTerrainTexture>? terrainTextures = null, IReadOnlyList<DaggerfallPopulationPlacement>? population = null)
{
    internal IReadOnlyList<DaggerfallPropertyContainerPlacement> PropertyContainers { get; init; } = [];
    internal IReadOnlyList<DaggerfallStaticNpcPlacement> StaticNpcs { get; } = staticNpcs ?? [];
    internal ProjectFacts Project { get; } = project;
    /// <summary>The collision/navigation artifacts the profile places and the static meshes it draws.</summary>
    internal DaggerfallSiteGeometry Geometry { get; } = geometry ?? throw new ArgumentNullException(nameof(geometry));
    internal AuthoredWorldAppearance WorldAppearance { get; } = worldAppearance;
    internal PlayerInitialLook InitialLook { get; } = initialLook;
    internal IReadOnlyList<NormalizedMaterial> Materials { get; } = Array.AsReadOnly(materials.OrderBy(material => material.Slot).ToArray());
    internal DaggerfallDungeonPopulationContent DungeonPopulation { get; init; } = DaggerfallDungeonPopulationContent.Empty;
    internal IReadOnlyDictionary<long, NormalizedActorSprite> ActorSprites { get; } = new ReadOnlyDictionary<long, NormalizedActorSprite>(actorSprites.ToDictionary());
    /// <summary>Published mobile media, resolved independently of the authored site placements for dynamic encounter actors.</summary>
    internal IReadOnlyDictionary<int, NormalizedActorSprite> MobileSprites { get; } = new ReadOnlyDictionary<int, NormalizedActorSprite>((mobileSprites ?? new Dictionary<int, NormalizedActorSprite>()).ToDictionary());
    internal IReadOnlyList<NormalizedAudioClip> Audio { get; } = Array.AsReadOnly((audio ?? []).ToArray());
    internal NormalizedClassicPresentation ClassicPresentation { get; } = classicPresentation ?? NormalizedClassicPresentation.Empty;
    internal DaggerfallWorldProfileKind ProfileKind { get; } = profileKind;
    internal DaggerfallInteriorBuilding? InteriorBuilding { get; } = interiorBuilding?.Validate();
    internal DaggerfallWorldProfileKey ProfileKey => Site is { } selected
        ? new DaggerfallWorldProfileKey(selected, ProfileKind, logicalProfileId ?? "unscoped-profile").Validate()
        : throw new InvalidOperationException("A selectable world profile must name its geographic site.");
    /// <summary>Normalized RDB action doors for this selected published world, in stable source identity order.</summary>
    /// <summary>Source-derived interaction portals for this profile; destinations resolve only through admitted logical profiles.</summary>
    internal IReadOnlyList<DaggerfallSitePortal> Portals { get; } = Array.AsReadOnly((portals ?? [])
        .Select(portal => portal.Validate())
        .OrderBy(portal => portal.Id, StringComparer.Ordinal)
        .ToArray());
    /// <summary>Named landing poses for relocation. A profile with a start exposes it as <c>start</c>.</summary>
    internal IReadOnlyDictionary<string, DaggerfallSiteAnchor> Anchors { get; } = new ReadOnlyDictionary<string, DaggerfallSiteAnchor>((anchors ?? StartAnchor(project, initialLook))
        .Select(anchor => anchor.Validate())
        .ToDictionary(anchor => anchor.Id, StringComparer.Ordinal));
    /// <summary>Source-normalized dungeon lights, ordered by their stable RDB placement identity.</summary>
    internal IReadOnlyList<DaggerfallAmbientZone> AmbientZones { get; init; } = [];
    /// <summary>The city gates an assembled exterior places, which open by day and close at night.</summary>
    internal IReadOnlyList<DaggerfallCityGateDefinition> CityGates { get; init; } = [];
    /// <summary>Deterministic interactive town/tavern notice boards placed during site assembly.</summary>
    internal IReadOnlyList<DaggerfallSiteNoticeBoard> NoticeBoards { get; init; } = [];
    internal IReadOnlyList<DaggerfallSiteDecoration> Decorations { get; init; } = [];
    internal IReadOnlyList<DaggerfallSiteLight> Lights { get; } = Array.AsReadOnly((lights ?? [])
        .Select(light => light.Validate())
        .OrderBy(light => light.Id, StringComparer.Ordinal)
        .ToArray());
    internal IReadOnlyList<NormalizedBillboardSprite>? GroundContainerSprites { get; } = groundContainerSprites;
    internal IReadOnlyDictionary<(int Archive, int Record), NormalizedBillboardSprite> BillboardSprites { get; } =
        new ReadOnlyDictionary<(int, int), NormalizedBillboardSprite>((billboardSprites ?? new Dictionary<(int, int), NormalizedBillboardSprite>()).ToDictionary());
    /// <summary>World-normalized water volumes supplied to the one admitted character step.</summary>
    internal IReadOnlyList<CharacterWaterVolume> WaterVolumes { get; } = ReadWaterVolumes(waterVolumes);
    /// <summary>Published climate ground textures available for exterior material remapping.</summary>
    internal IReadOnlyDictionary<(int Archive, int Record), NormalizedTerrainTexture> TerrainTextures { get; } =
        new ReadOnlyDictionary<(int, int), NormalizedTerrainTexture>((terrainTextures ?? new Dictionary<(int, int), NormalizedTerrainTexture>()).ToDictionary());
    /// <summary>Source-backed population records an exterior admits.</summary>
    internal IReadOnlyList<DaggerfallPopulationPlacement> Population { get; } = Array.AsReadOnly((population ?? [])
        .OrderBy(value => value.Id, StringComparer.Ordinal).ToArray());
    /// <summary>Normalized per-placement bounds, visibility samples, and source markers used by dungeon discovery; absent on non-dungeons.</summary>
    internal DaggerfallDungeonMapContent? DungeonMap { get; } = dungeonMap;
    internal IReadOnlyList<DaggerfallSiteMarker> QuestMarkers { get; } = Array.AsReadOnly((questMarkers ?? []).ToArray());
    /// <summary>Normalized RDB action nodes; runtime trigger and action-family policy remain in the graph owner.</summary>
    internal IReadOnlyList<DaggerfallDungeonActionDefinition> DungeonActions { get; } = Array.AsReadOnly((dungeonActions ?? [])
        .OrderBy(action => action.Id, StringComparer.Ordinal)
        .ToArray());
    /// <summary>Model-local render and collision data for action-bearing RDB models.</summary>
    internal IReadOnlyList<DaggerfallDungeonActionModelDefinition> DungeonActionModels { get; } = Array.AsReadOnly((dungeonActionModels ?? [])
        .Select(model => model.Validate())
        .OrderBy(model => model.ActionId, StringComparer.Ordinal)
        .ToArray());

    internal DaggerfallSiteAnchor RequireAnchor(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return Anchors.TryGetValue(id, out DaggerfallSiteAnchor? anchor)
            ? anchor
            : throw new InvalidOperationException($"World profile '{ProfileKey.LogicalId}' has no anchor '{id}'.");
    }

    private static IReadOnlyList<CharacterWaterVolume> ReadWaterVolumes(IReadOnlyList<CharacterWaterVolume>? volumes)
    {
        CharacterWaterVolume[] normalized = (volumes ?? [])
            .Select(volume => volume.Validate())
            .OrderBy(volume => volume.Trigger)
            .ToArray();
        if (normalized.Select(volume => volume.Trigger).Distinct().Count() != normalized.Length)
            throw new ArgumentException("A site cannot admit duplicate water trigger identities.", nameof(volumes));
        return Array.AsReadOnly(normalized);
    }

    internal IReadOnlyList<DaggerfallRdbDoorDefinition> Doors { get; } = Array.AsReadOnly((doors ?? [])
        .Select(door => door.Validate())
        .OrderBy(door => door.Id.SourceKey, StringComparer.Ordinal)
        .ThenBy(door => door.Id.BlockX)
        .ThenBy(door => door.Id.BlockZ)
        .ThenBy(door => door.Id.ModelIndex)
        .ToArray());

    /// <summary>
    /// The site the scenario starts the player at, when it declares one. It is the session's starting
    /// site and not an authority over a save: a save that records where the player is keeps them there.
    /// </summary>
    internal DaggerfallSiteId? Site { get; } = site;

    private static IReadOnlyList<DaggerfallSiteAnchor> StartAnchor(ProjectFacts project, PlayerInitialLook look) => project.PlayerPosition is { } position
        ? [new DaggerfallSiteAnchor("start", position, look.YawRadians, look.PitchRadians)]
        : [];
}

internal sealed class ProjectFacts(WorldPoint? playerPosition, IReadOnlyDictionary<long, AuthoredActor> actors)
{
    internal WorldPoint? PlayerPosition { get; } = playerPosition;
    internal IReadOnlyDictionary<long, AuthoredActor> Actors { get; } = new ReadOnlyDictionary<long, AuthoredActor>(actors.ToDictionary());
}
internal sealed record AuthoredActor(long EntityId, DaggerfallActorId ActorId, WorldPoint Position,
    int SpawnDistance = 0, bool Passive = false, string Gender = "unspecified");
internal sealed record ClassicMediaResource(string Id, string Kind, string RelativePath, string Path, ContentSha256 Hash, long ByteLength, int AtlasWidth, int AtlasHeight, IReadOnlyList<NormalizedAtlasFrame> Frames, Vector2 Pivot, Vector2 DisplaySize, float? FramesPerSecond, bool? Loop, IReadOnlyList<int> Sequence);
