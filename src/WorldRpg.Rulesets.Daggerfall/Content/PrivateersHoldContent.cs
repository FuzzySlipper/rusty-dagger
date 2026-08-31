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
    private const float EngineViewmodelLocalCoordinateLimit = 16F;

    internal static PrivateersHoldInputs Read(ProductContent content, ReadOnlyMemory<byte> payload, DaggerfallDefinitions definitions)
    {
        DaggerfallContentDiagnostics diagnostics = new();
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            JsonElement root = DaggerfallBaseContent.Object(document.RootElement, "root", diagnostics);
            DaggerfallBaseContent.RejectDuplicateProperties(root, "root", diagnostics);
            if (DaggerfallBaseContent.Text(root, "ruleset", diagnostics) != DaggerfallRuleset.Identity.Value) diagnostics.Add("Privateer's Hold payload must identify ruleset 'daggerfall'.");
            if (DaggerfallBaseContent.Integer(root, "schemaVersion", diagnostics) != SchemaVersion) diagnostics.Add($"Privateer's Hold payload schemaVersion must be {SchemaVersion}.");
            AdmittedFiles files = AdmittedFiles.Copy(content, diagnostics);
            ScenarioStart start = ReadStart(DaggerfallBaseContent.Object(DaggerfallBaseContent.Property(root, "startingState", diagnostics), "startingState", diagnostics), diagnostics);
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
        string classicMediaPath = Prefix("media/classic/manifest.json");
        ContentSha256 spatialHash = RequireArtifact(artifacts, spatialPath, diagnostics);
        ContentSha256 meshHash = RequireArtifact(artifacts, meshPath, diagnostics);
        ContentSha256 mediaHash = RequireArtifact(artifacts, mediaPath, diagnostics);
        ContentSha256 classicMediaHash = RequireArtifact(artifacts, classicMediaPath, diagnostics);
        VerifyAdmittedArtifact(files, spatialPath, spatialHash, diagnostics);
        VerifyAdmittedArtifact(files, meshPath, meshHash, diagnostics);
        VerifyAdmittedArtifact(files, mediaPath, mediaHash, diagnostics);
        VerifyAdmittedArtifact(files, classicMediaPath, classicMediaHash, diagnostics);
        ulong gridId = UnsignedInteger(world, "navigationGridId", diagnostics);
        AuthoredWorldAppearance worldAppearance = ReadWorldAppearance(DaggerfallBaseContent.Object(DaggerfallBaseContent.Property(world, "appearance", diagnostics), "world.appearance", diagnostics), diagnostics);
        Dictionary<long, AuthoredActor> actors = ReadNormalizedPlacements(root, definitions, diagnostics);
        (IReadOnlyList<NormalizedMaterial> materials, IReadOnlyDictionary<int, NormalizedActorSprite> sprites) = ReadDungeonMedia(
            files.GetExactlyOne(mediaPath),
            publicationRoot,
            artifacts,
            definitions,
            diagnostics);
        (IReadOnlyList<NormalizedAudioClip> audio, NormalizedClassicPresentation classicPresentation) = ReadClassicPresentation(
            files, files.GetExactlyOne(classicMediaPath), publicationRoot, artifacts, diagnostics);
        classicPresentation = ReadClassicSelection(root, classicPresentation, definitions, diagnostics);
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
            actorSprites.Add(actor.EntityId, ResolveActorPresentation(actor, definition, sprite, diagnostics));
        }

        return new PrivateersHoldInputs(
            new ProjectFacts(start.Position, new ReadOnlyDictionary<long, AuthoredActor>(actors)),
            new SpatialContentArtifact(spatialPath, spatialHash, gridId),
            new ContentArtifact(meshPath, meshHash),
            worldAppearance,
            start.Look,
            materials,
            new ReadOnlyDictionary<long, NormalizedActorSprite>(actorSprites),
            audio,
            classicPresentation);
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
                // Engine's managed atlas API currently publishes no frame-limit constant.
                // This is Daggerfall import/publication admission policy matched to the
                // currently supported 4096-frame generated atlas shape, not copied Engine authority.
                if (frames.Count > 4096 || frames.Select(frame => frame.Id).Distinct().Count() != frames.Count)
                {
                    diagnostics.Add($"Generated atlas resource '{id}' exceeds Daggerfall's 4096-frame publication admission limit or repeats a frame id.");
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
                IReadOnlyDictionary<string, NormalizedSpriteState> states = ReadActorStates(actor, texture, mobileId, diagnostics);
                string? preferredRestState = DaggerfallBaseContent.OptionalText(actor, "preferredRestState", diagnostics);
                if (preferredRestState is not null && !states.ContainsKey(preferredRestState)) diagnostics.Add($"Generated actor mobile '{mobileId}' preferredRestState '{preferredRestState}' is not a published state.");
                IReadOnlyList<NormalizedAttackSequence> attacks = ReadAttackSequences(actor, states, mobileId, diagnostics);
                NormalizedActorSprite? corpse = ReadCorpse(actor, resources, publicationRoot, artifacts, mobileId, diagnostics);
                if (!sprites.TryAdd(mobileId, new NormalizedActorSprite(texture.Path, texture.Hash, texture.AtlasWidth, texture.AtlasHeight, texture.Frames, texture.Frames[0].Id, pivot, size)
                {
                    States = states,
                    PreferredRestState = preferredRestState,
                    AttackSequences = attacks,
                    Corpse = corpse,
                })) diagnostics.Add($"Generated actor media repeats mobile '{mobileId}'.");
            }
            return (Array.AsReadOnly(materials.OrderBy(material => material.Slot).ToArray()), new ReadOnlyDictionary<int, NormalizedActorSprite>(sprites));
        }
        catch (JsonException exception)
        {
            diagnostics.Add($"Generated dungeon media manifest is not valid JSON: {exception.Message}");
            return ([], new Dictionary<int, NormalizedActorSprite>());
        }
    }

    private static NormalizedActorSprite ResolveActorPresentation(AuthoredActor actor, DaggerfallActorDefinition definition, NormalizedActorSprite sprite, DaggerfallContentDiagnostics diagnostics)
    {
        DaggerfallActorPresentationDefinition presentation = definition.Presentation;
        if (presentation.PreferredRestState is not null && !sprite.States.ContainsKey(presentation.PreferredRestState))
        {
            diagnostics.Add($"Actor '{actor.ActorId.Value}' preferredRestState '{presentation.PreferredRestState}' is not published by its normalized media.");
        }
        foreach (string state in presentation.EffectiveFramesPerSecond.Keys)
        {
            if (!sprite.States.ContainsKey(state)) diagnostics.Add($"Actor '{actor.ActorId.Value}' effective playback override '{state}' is not published by its normalized media.");
        }

        IReadOnlyDictionary<string, NormalizedSpriteState> states = new ReadOnlyDictionary<string, NormalizedSpriteState>(sprite.States.ToDictionary(
            pair => pair.Key,
            pair => presentation.EffectiveFramesPerSecond.TryGetValue(pair.Key, out float overrideFramesPerSecond)
                ? pair.Value with { EffectiveFramesPerSecond = overrideFramesPerSecond }
                : pair.Value,
            StringComparer.Ordinal));
        return sprite with
        {
            PreferredRestState = presentation.PreferredRestState ?? sprite.PreferredRestState,
            States = states,
        };
    }

    private static IReadOnlyDictionary<string, NormalizedSpriteState> ReadActorStates(JsonElement actor, MediaResource texture, int mobileId, DaggerfallContentDiagnostics diagnostics)
    {
        Dictionary<string, NormalizedSpriteState> result = new(StringComparer.Ordinal);
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
                (sectors.TryGetValue(orientation, out List<uint>? sector) ? sector : sectors[orientation] = []).Add(frameId);
            }
            IReadOnlyDictionary<int, IReadOnlyList<uint>> orientations = new ReadOnlyDictionary<int, IReadOnlyList<uint>>(sectors.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<uint>)Array.AsReadOnly(pair.Value.ToArray())));
            IReadOnlyList<uint> frames = orientations.TryGetValue(0, out IReadOnlyList<uint>? forward) ? forward : orientations.Values.FirstOrDefault() ?? [];
            bool completeSectors = orientations.Count == 8
                && Enumerable.Range(0, 8).All(orientations.ContainsKey);
            bool equalSectorFrames = completeSectors
                && orientations.Values.Select(sequence => sequence.Count).Distinct().Count() == 1;
            if (!completeSectors || !equalSectorFrames)
                diagnostics.Add($"Generated actor mobile '{mobileId}' state '{name}' must provide eight equally-sized directional sectors.");
            if (!float.IsFinite(fps) || fps <= 0F || frames.Count == 0 || orientations.Values.Any(sequence => sequence.Any(frame => !texture.Frames.Any(atlas => atlas.Id == frame))))
                diagnostics.Add($"Generated actor mobile '{mobileId}' state '{name}' has invalid playback frames.");
            if (!result.TryAdd(name, new NormalizedSpriteState(name, frames, fps, loops) { Orientations = orientations })) diagnostics.Add($"Generated actor mobile '{mobileId}' repeats state '{name}'.");
        }
        if (result.Count == 0) diagnostics.Add($"Generated actor mobile '{mobileId}' has no playable states.");
        return new ReadOnlyDictionary<string, NormalizedSpriteState>(result);
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

    private static void AddAttack(IReadOnlyList<int> source, int chance, NormalizedSpriteState state, int mobileId, DaggerfallContentDiagnostics diagnostics, List<NormalizedAttackSequence> target)
    {
        if (chance is < 1 or > 100 || source.Count == 0 || source[^1] == -1 || state.Orientations.Values.Any(orientation => source.Any(frame => frame < -1 || frame >= orientation.Count)))
        {
            diagnostics.Add($"Generated actor mobile '{mobileId}' has an invalid attack sequence.");
            return;
        }
        target.Add(new NormalizedAttackSequence(chance, source));
    }

    private static NormalizedActorSprite? ReadCorpse(JsonElement actor, IReadOnlyDictionary<string, MediaResource> resources, string publicationRoot, IReadOnlyDictionary<string, ContentSha256> artifacts, int mobileId, DaggerfallContentDiagnostics diagnostics)
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
        return new NormalizedActorSprite(resource.Path, resource.Hash, resource.AtlasWidth, resource.AtlasHeight, resource.Frames, resource.Frames[0].Id, pivot, size);
    }

    private sealed record MediaResource(string Path, ContentSha256 Hash, int AtlasWidth, int AtlasHeight, IReadOnlyList<NormalizedAtlasFrame> Frames);

    private static (IReadOnlyList<NormalizedAudioClip> Audio, NormalizedClassicPresentation Presentation) ReadClassicPresentation(AdmittedFiles files, byte[]? bytes, string publicationRoot, IReadOnlyDictionary<string, ContentSha256> artifacts, DaggerfallContentDiagnostics diagnostics)
    {
        if (bytes is null) { diagnostics.Add("Generated classic media manifest is unavailable."); return ([], NormalizedClassicPresentation.Empty); }
        try
        {
            using JsonDocument document = JsonDocument.Parse(bytes);
            JsonElement root = DaggerfallBaseContent.Object(document.RootElement, "classic media manifest", diagnostics);
            DaggerfallBaseContent.RejectDuplicateProperties(root, "classic media manifest", diagnostics);
            if (DaggerfallBaseContent.Integer(root, "schemaVersion", diagnostics) != 1) diagnostics.Add("Classic media manifest schemaVersion must be 1.");
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
                string path = $"{publicationRoot.TrimEnd('/')}/{relativePath}";
                ContentSha256 hash = ContentHash(DaggerfallBaseContent.Text(resource, "contentDigest", diagnostics), diagnostics);
                if (!artifacts.TryGetValue(path, out ContentSha256 artifact) || artifact != hash) diagnostics.Add($"Generated classic audio '{id}' does not agree with the import manifest.");
                long byteLength = Long(resource, "byteLength", diagnostics);
                string mimeType = DaggerfallBaseContent.Text(resource, "mimeType", diagnostics);
                int sourceWidth = DaggerfallBaseContent.Integer(resource, "sourceWidth", diagnostics);
                int sourceHeight = DaggerfallBaseContent.Integer(resource, "sourceHeight", diagnostics);
                int atlasWidth = DaggerfallBaseContent.Integer(resource, "atlasWidth", diagnostics);
                int atlasHeight = DaggerfallBaseContent.Integer(resource, "atlasHeight", diagnostics);
                if (!ValidLogicalId(id) || !ValidLogicalPath(relativePath) || !KnownClassicMediaKind(kind) || byteLength <= 0 || string.IsNullOrWhiteSpace(mimeType) || sourceWidth < 0 || sourceHeight < 0
                    || atlasWidth < 0 || atlasHeight < 0 || files.GetExactlyOne(path) is not byte[] artifactBytes || artifactBytes.LongLength != byteLength)
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
                if (!resources.TryAdd(id, new ClassicMediaResource(id, kind, path, hash, atlasWidth, atlasHeight, Array.AsReadOnly(frames.ToArray()), pivot, displaySize, framesPerSecond, loop, sequence))) diagnostics.Add($"Generated classic media repeats resource '{id}'.");
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
                else audio.Add(new NormalizedAudioClip(id, resource.Path, resource.Hash));
            }
            try { OrderedHitCues(audio); }
            catch (InvalidOperationException exception) { diagnostics.Add(exception.Message); }
            NormalizedClassicWeapon? weapon = ReadClassicWeapon(root, resources, diagnostics);
            IReadOnlyList<NormalizedClassicEffect> effects = ReadClassicEffects(root, resources, diagnostics);
            return (Array.AsReadOnly(audio.ToArray()), new NormalizedClassicPresentation(weapon, effects));
        }
        catch (JsonException exception)
        {
            diagnostics.Add($"Generated classic media manifest is not valid JSON: {exception.Message}");
            return ([], NormalizedClassicPresentation.Empty);
        }
    }

    private static NormalizedClassicWeapon? ReadClassicWeapon(JsonElement root, IReadOnlyDictionary<string, ClassicMediaResource> resources, DaggerfallContentDiagnostics diagnostics)
    {
        ClassicMediaResource[] weaponResources = resources.Values.Where(candidate => candidate.Kind == "weaponSprite").ToArray();
        if (weaponResources.Length != 1) diagnostics.Add("Classic media must publish exactly one weaponSprite resource.");
        ClassicMediaResource? resource = weaponResources.Length == 1 ? weaponResources[0] : null;
        Dictionary<string, NormalizedClassicWeaponAction> actions = new(StringComparer.Ordinal);
        foreach (JsonElement value in DaggerfallBaseContent.Array(root, "weaponActions", diagnostics))
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
            if (loop.ValueKind is not (JsonValueKind.True or JsonValueKind.False) || !float.IsFinite(fps) || fps <= 0F || start < 0 || count <= 0 || !float.IsFinite(offset) || alignment is not ("left" or "right"))
                diagnostics.Add($"Classic weapon action '{name}' has invalid framing, timing, or alignment.");
            short sourceXOffset = checked((short)DaggerfallBaseContent.Integer(action, "sourceXOffset", diagnostics));
            short sourceYOffset = checked((short)DaggerfallBaseContent.Integer(action, "sourceYOffset", diagnostics));
            if (resource is not null && (start < 0 || count <= 0 || (long)start + count > resource.Frames.Count)) diagnostics.Add($"Classic weapon action '{name}' is outside weaponSprite frame bounds.");
            if (!actions.TryAdd(name, new NormalizedClassicWeaponAction(name, sourceRecordOrdinal, start, count, alignment, offset, fps, loops, sourceXOffset, sourceYOffset))) diagnostics.Add($"Classic weapon actions repeat '{name}'.");
        }
        string[] expected = ["idle", "strikeDown", "strikeDownLeft", "strikeLeft", "strikeRight", "strikeDownRight", "strikeUp"];
        if (actions.Count != expected.Length || expected.Any(name => !actions.ContainsKey(name))) diagnostics.Add("Classic weapon actions must provide the complete authored dagger action family.");
        foreach ((int ordinal, string expectedName) in expected.Select((value, index) => (index, value)))
            if (actions.TryGetValue(expectedName, out NormalizedClassicWeaponAction? action) && action.SourceRecordOrdinal != ordinal) diagnostics.Add($"Classic weapon action '{expectedName}' must retain source record ordinal {ordinal}.");
        if (resource is not null)
        {
            if (!resource.Frames.Select(frame => frame.Id).Order().SequenceEqual(Enumerable.Range(0, resource.Frames.Count).Select(index => (uint)index))) diagnostics.Add("Classic weaponSprite frames must use contiguous canonical frame indexes.");
            HashSet<int> covered = [];
            foreach (NormalizedClassicWeaponAction action in actions.Values)
            {
                long end = (long)action.FrameStart + action.FrameCount;
                if (action.FrameStart < 0 || action.FrameCount <= 0 || end > resource.Frames.Count) continue;
                for (int frame = action.FrameStart; frame < (int)end; frame++) if (!covered.Add(frame)) diagnostics.Add("Classic weapon action ranges must not overlap.");
            }
            if (!covered.SetEquals(Enumerable.Range(0, resource.Frames.Count))) diagnostics.Add("Classic weapon action ranges must cover every canonical weapon frame.");
        }
        return resource is null ? null : new NormalizedClassicWeapon(resource.Id, resource.Path, resource.Hash, resource.AtlasWidth, resource.AtlasHeight, resource.Frames, resource.Pivot, resource.DisplaySize, resource.Sequence, new ReadOnlyDictionary<string, NormalizedClassicWeaponAction>(actions));
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
            if (!effects.TryAdd(name, new NormalizedClassicEffect(name, sourceRecordOrdinal, resource.Path, resource.Hash, resource.AtlasWidth, resource.AtlasHeight, resource.Frames, resource.Pivot, resource.DisplaySize, resource.Sequence, fps, loops))) diagnostics.Add($"Classic effects repeat '{name}'.");
        }
        string[] expected = ["blood0", "blood1", "blood2", "magicSparkle"];
        if (effects.Count != expected.Length || expected.Any(name => !effects.ContainsKey(name))) diagnostics.Add("Classic effects must provide blood0..blood2 and magicSparkle exactly once.");
        foreach ((int ordinal, string expectedName) in expected.Select((value, index) => (index, value)))
            if (effects.TryGetValue(expectedName, out NormalizedClassicEffect? effect) && effect.SourceRecordOrdinal != ordinal) diagnostics.Add($"Classic effect '{expectedName}' must retain source record ordinal {ordinal}.");
        return Array.AsReadOnly(effects.Values.OrderBy(effect => effect.Name, StringComparer.Ordinal).ToArray());
    }

    private static NormalizedClassicPresentation ReadClassicSelection(JsonElement root, NormalizedClassicPresentation classic, DaggerfallDefinitions definitions, DaggerfallContentDiagnostics diagnostics)
    {
        if (!root.TryGetProperty("classicPresentation", out JsonElement value) || value.ValueKind == JsonValueKind.Null) return classic;
        JsonElement presentation = DaggerfallBaseContent.Object(value, "classicPresentation", diagnostics);
        DaggerfallBaseContent.RejectDuplicateProperties(presentation, "classicPresentation", diagnostics);
        Dictionary<string, string> mappings = new(StringComparer.Ordinal);
        foreach (JsonElement entry in DaggerfallBaseContent.Array(presentation, "weaponVisuals", diagnostics))
        {
            JsonElement mapping = DaggerfallBaseContent.Object(entry, "classic weapon visual", diagnostics);
            DaggerfallBaseContent.RejectDuplicateProperties(mapping, "classic weapon visual", diagnostics);
            string itemId = DaggerfallBaseContent.Text(mapping, "itemId", diagnostics);
            string resource = DaggerfallBaseContent.Text(mapping, "resource", diagnostics);
            if (classic.Weapon is null || resource != classic.Weapon.ResourceId) diagnostics.Add($"Classic weapon visual '{itemId}' does not select the admitted weaponSprite resource.");
            if (!mappings.TryAdd(itemId, resource)) diagnostics.Add($"Classic weapon visuals repeat item '{itemId}'.");
        }
        if (mappings.Count != 1 || !mappings.TryGetValue("iron-dagger", out string? mappedResource) || mappedResource != "weapon.dagger.steel"
            || !definitions.Items.TryGetValue(new DaggerfallItemId("iron-dagger"), out DaggerfallItemDefinition? dagger)
            || dagger.Weapon is not { Material: "iron", Skill: "short-blade" })
            diagnostics.Add("Classic dagger viewmodel compatibility is restricted to the admitted iron-dagger bridge.");
        ClassicViewmodelStyle? viewmodel = null;
        if (mappings.Count > 0)
        {
            JsonElement style = DaggerfallBaseContent.Object(DaggerfallBaseContent.Property(presentation, "viewmodel", diagnostics), "classic viewmodel", diagnostics);
            DaggerfallBaseContent.RejectDuplicateProperties(style, "classic viewmodel", diagnostics);
            WorldPoint position = Point(DaggerfallBaseContent.Property(style, "position", diagnostics), "classic viewmodel.position", diagnostics);
            Vector2 pivot = GeneratedVector2(DaggerfallBaseContent.Property(style, "pivot", diagnostics), "classic viewmodel.pivot", diagnostics);
            Vector2 size = GeneratedVector2(DaggerfallBaseContent.Property(style, "worldSize", diagnostics), "classic viewmodel.worldSize", diagnostics);
            int renderOrder = DaggerfallBaseContent.Integer(style, "renderOrder", diagnostics);
            if (MathF.Abs(position.X) > EngineViewmodelLocalCoordinateLimit || MathF.Abs(position.Y) > EngineViewmodelLocalCoordinateLimit || MathF.Abs(position.Z) > EngineViewmodelLocalCoordinateLimit)
                diagnostics.Add($"Classic viewmodel position must remain within Engine local +/-{EngineViewmodelLocalCoordinateLimit} bounds.");
            if (pivot.X is < 0F or > 1F || pivot.Y is < 0F or > 1F) diagnostics.Add("Classic viewmodel pivot must be normalized within [0,1].");
            if (size.X <= 0F || size.Y <= 0F) diagnostics.Add("Classic viewmodel worldSize must be positive.");
            viewmodel = new ClassicViewmodelStyle(position, pivot, size, renderOrder);
        }
        return classic with { CompatibleItemVisuals = new ReadOnlyDictionary<string, string>(mappings), Viewmodel = viewmodel };
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

    // Mirrors Daggerfall.Import NormalizedImportDocument's descriptor admission
    // without making the runtime ruleset depend on the offline importer assembly.
    private static bool ValidLogicalId(string value) => !string.IsNullOrWhiteSpace(value) && !value.Any(char.IsWhiteSpace);
    private static bool ValidLogicalPath(string value) => !string.IsNullOrWhiteSpace(value) && !value.StartsWith("/", StringComparison.Ordinal) && !value.StartsWith('\\') && !value.Contains('\\') && !value.Split('/').Any(segment => segment is "." or ".." or "");
    private static bool KnownClassicMediaKind(string value) => value is "texture" or "billboard" or "enemySprite" or "weaponSprite" or "effectSprite" or "audio" or "userInterface" or "font";

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

    private static ScenarioStart ReadStart(JsonElement value, DaggerfallContentDiagnostics diagnostics)
    {
        WorldPoint position = Point(DaggerfallBaseContent.Property(value, "position", diagnostics), "startingState.position", diagnostics);
        JsonElement look = DaggerfallBaseContent.Object(DaggerfallBaseContent.Property(value, "look", diagnostics), "startingState.look", diagnostics);
        PlayerInitialLook initialLook = new(DaggerfallBaseContent.Number(look, "yawRadians", diagnostics), DaggerfallBaseContent.Number(look, "pitchRadians", diagnostics));
        return new(position, initialLook);
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

internal sealed record ScenarioStart(WorldPoint Position, PlayerInitialLook Look);
internal sealed record AuthoredWorldAppearance(Color Tint, Transform Transform, bool Visible, RenderLayer Layer);
internal sealed record ContentArtifact(string Path, ContentSha256 Sha256);
internal sealed record NormalizedMaterial(uint Slot, string TexturePath, ContentSha256 TextureSha256);
internal sealed record NormalizedAtlasFrame(uint Id, int X, int Y, int Width, int Height);
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
internal sealed record NormalizedAttackSequence(int Chance, IReadOnlyList<int> SourceFrames);
internal sealed record NormalizedAudioClip(string Id, string Path, ContentSha256 Sha256);
internal sealed record NormalizedClassicWeaponAction(string Name, int SourceRecordOrdinal, int FrameStart, int FrameCount, string Alignment, float ScreenOffset, float FramesPerSecond, bool Loops, short SourceXOffset, short SourceYOffset);
internal sealed record NormalizedClassicWeapon(string ResourceId, string TexturePath, ContentSha256 TextureSha256, int AtlasWidth, int AtlasHeight, IReadOnlyList<NormalizedAtlasFrame> Frames, Vector2 Pivot, Vector2 DisplaySize, IReadOnlyList<int> Sequence, IReadOnlyDictionary<string, NormalizedClassicWeaponAction> Actions);
internal sealed record NormalizedClassicEffect(string Name, int SourceRecordOrdinal, string TexturePath, ContentSha256 TextureSha256, int AtlasWidth, int AtlasHeight, IReadOnlyList<NormalizedAtlasFrame> Frames, Vector2 Pivot, Vector2 DisplaySize, IReadOnlyList<int> Sequence, float FramesPerSecond, bool Loops);
internal sealed record NormalizedClassicPresentation(NormalizedClassicWeapon? Weapon, IReadOnlyList<NormalizedClassicEffect> Effects)
{
    internal static NormalizedClassicPresentation Empty { get; } = new(null, Array.Empty<NormalizedClassicEffect>());
    internal IReadOnlyDictionary<string, string> CompatibleItemVisuals { get; init; } = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());
    internal ClassicViewmodelStyle? Viewmodel { get; init; }
    internal bool TryEffect(string name, out NormalizedClassicEffect? effect)
    {
        effect = Effects.FirstOrDefault(candidate => candidate.Name == name);
        return effect is not null;
    }
}
internal sealed record ClassicViewmodelStyle(WorldPoint Position, Vector2 Pivot, Vector2 Size, int RenderOrder);
internal sealed record NormalizedActorSprite(string TexturePath, ContentSha256 TextureSha256, int AtlasWidth, int AtlasHeight, IReadOnlyList<NormalizedAtlasFrame> Frames, uint InitialFrameId, Vector2 Pivot, Vector2 Size)
{
    internal IReadOnlyDictionary<string, NormalizedSpriteState> States { get; init; } = new ReadOnlyDictionary<string, NormalizedSpriteState>(new Dictionary<string, NormalizedSpriteState>());
    /// <summary>Resolved Daggerfall rest-state policy. Null preserves the generic idle-then-move fallback.</summary>
    internal string? PreferredRestState { get; init; }
    internal IReadOnlyList<NormalizedAttackSequence> AttackSequences { get; init; } = Array.Empty<NormalizedAttackSequence>();
    internal NormalizedActorSprite? Corpse { get; init; }
}
internal sealed class PrivateersHoldInputs(ProjectFacts project, SpatialContentArtifact spatialArtifact, ContentArtifact staticMesh, AuthoredWorldAppearance worldAppearance, PlayerInitialLook initialLook, IReadOnlyList<NormalizedMaterial> materials, IReadOnlyDictionary<long, NormalizedActorSprite> actorSprites, IReadOnlyList<NormalizedAudioClip>? audio = null, NormalizedClassicPresentation? classicPresentation = null)
{
    internal ProjectFacts Project { get; } = project;
    internal SpatialContentArtifact SpatialArtifact { get; } = spatialArtifact;
    internal ContentArtifact StaticMesh { get; } = staticMesh;
    internal AuthoredWorldAppearance WorldAppearance { get; } = worldAppearance;
    internal PlayerInitialLook InitialLook { get; } = initialLook;
    internal IReadOnlyList<NormalizedMaterial> Materials { get; } = Array.AsReadOnly(materials.OrderBy(material => material.Slot).ToArray());
    internal IReadOnlyDictionary<long, NormalizedActorSprite> ActorSprites { get; } = new ReadOnlyDictionary<long, NormalizedActorSprite>(actorSprites.ToDictionary());
    internal IReadOnlyList<NormalizedAudioClip> Audio { get; } = Array.AsReadOnly((audio ?? []).ToArray());
    internal NormalizedClassicPresentation ClassicPresentation { get; } = classicPresentation ?? NormalizedClassicPresentation.Empty;
}

internal sealed class ProjectFacts(WorldPoint? playerPosition, IReadOnlyDictionary<long, AuthoredActor> actors)
{
    internal WorldPoint? PlayerPosition { get; } = playerPosition;
    internal IReadOnlyDictionary<long, AuthoredActor> Actors { get; } = new ReadOnlyDictionary<long, AuthoredActor>(actors.ToDictionary());
}
internal sealed record AuthoredActor(long EntityId, DaggerfallActorId ActorId, WorldPoint Position);
internal sealed record ClassicMediaResource(string Id, string Kind, string Path, ContentSha256 Hash, int AtlasWidth, int AtlasHeight, IReadOnlyList<NormalizedAtlasFrame> Frames, Vector2 Pivot, Vector2 DisplaySize, float? FramesPerSecond, bool? Loop, IReadOnlyList<int> Sequence);
