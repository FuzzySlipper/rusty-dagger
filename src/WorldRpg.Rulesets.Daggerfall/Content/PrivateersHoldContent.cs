using System.Collections.ObjectModel;
using System.Buffers.Binary;
using System.Numerics;
using System.Text.Json;
using Rusty.Engine;
using WorldRpg.Kit;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>Reads authored Privateer's Hold scenario facts; no project entity names participate in runtime selection.</summary>
internal static class PrivateersHoldContent
{
    internal static PrivateersHoldInputs Read(ProductContent content, ReadOnlyMemory<byte> payload, DaggerfallDefinitions definitions)
    {
        DaggerfallContentDiagnostics diagnostics = new();
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            JsonElement root = DaggerfallBaseContent.Object(document.RootElement, "root", diagnostics);
            DaggerfallBaseContent.RejectDuplicateProperties(root, "root", diagnostics);
            if (DaggerfallBaseContent.Text(root, "ruleset", diagnostics) != DaggerfallRuleset.Identity.Value) diagnostics.Add("Privateer's Hold payload must identify ruleset 'daggerfall'.");
            AdmittedFiles files = AdmittedFiles.From(content);
            ScenarioStart start = ReadStart(DaggerfallBaseContent.Object(DaggerfallBaseContent.Property(root, "startingState", diagnostics), "startingState", diagnostics), diagnostics);
            // A starting site the published locations do not carry would leave the session standing at a
            // location nothing can name, so it is refused against the section that does carry them. The
            // identity set answers this question; `ResolveSite` asks the records instead, because there
            // the question is whether a site can resolve to a name and a kind. The two can only differ on
            // a pack carrying a location kind the ruleset cannot name, and loading such a pack already
            // fails, so no reader ever sees them disagree.
            if (start.Site is { } startSite && !definitions.Locations.Keys.Contains((startSite.Region, startSite.Index)))
            {
                diagnostics.Add($"Privateer's Hold startingState.site names location {startSite}, which the published locations do not carry.");
            }
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
        DaggerfallWorldProfileKind profileKind = DaggerfallBaseContent.Text(world, "profileKind", diagnostics) switch
        {
            "exterior" => DaggerfallWorldProfileKind.Exterior,
            "interior" => DaggerfallWorldProfileKind.Interior,
            "dungeon" => DaggerfallWorldProfileKind.Dungeon,
            _ => InvalidProfileKind(diagnostics),
        };
        if (!DaggerfallBaseContent.ValidId(publicationRoot.Replace('/', '-')) || publicationRoot.Contains("..", StringComparison.Ordinal))
        {
            diagnostics.Add("Privateer's Hold publicationRoot must be a stable relative logical path.");
        }

        string Prefix(string relativePath) => $"{publicationRoot.TrimEnd('/')}/{relativePath}";
        Dictionary<string, ContentSha256> artifacts = ReadImportArtifacts(files, Prefix("import-manifest.json"), publicationRoot, diagnostics);
        string spatialPath = Prefix(DaggerfallBaseContent.Text(world, "collisionNavigationPath", diagnostics));
        string meshPath = Prefix(DaggerfallBaseContent.Text(world, "staticMeshPath", diagnostics));
        string normalizedPath = Prefix("normalized.json");
        string mediaPath = Prefix("media/dungeon/manifest.json");
        string classicMediaPath = Prefix("media/classic/manifest.json");
        ContentSha256 spatialHash = RequireArtifact(artifacts, spatialPath, diagnostics);
        ContentSha256 meshHash = RequireArtifact(artifacts, meshPath, diagnostics);
        _ = RequireArtifact(artifacts, normalizedPath, diagnostics);
        ContentSha256 mediaHash = RequireArtifact(artifacts, mediaPath, diagnostics);
        ContentSha256 classicMediaHash = RequireArtifact(artifacts, classicMediaPath, diagnostics);
        ulong gridId = UnsignedInteger(world, "navigationGridId", diagnostics);
        AuthoredWorldAppearance worldAppearance = ReadWorldAppearance(DaggerfallBaseContent.Object(DaggerfallBaseContent.Property(world, "appearance", diagnostics), "world.appearance", diagnostics), diagnostics);
        IReadOnlyList<DaggerfallSitePortal> portals = ReadSitePortals(world, diagnostics);
        IReadOnlyList<DaggerfallSiteAnchor> anchors = ReadSiteAnchors(world, start, diagnostics);
        Dictionary<long, AuthoredActor> actors = ReadNormalizedPlacements(root, definitions, diagnostics);
        (IReadOnlyList<NormalizedMaterial> materials, IReadOnlyDictionary<int, NormalizedActorSprite> sprites, NormalizedGroundContainerSprite? groundContainerSprite) = ReadDungeonMedia(
            files.GetExactlyOne(mediaPath),
            publicationRoot,
            artifacts,
            definitions,
            diagnostics);
        // Every normal encounter table can select any entry at a valid player level. Refuse a
        // profile whose generated media closure cannot present one before gameplay rolls it.
        foreach (int mobileId in definitions.Encounters.Tables.SelectMany(table => table).Distinct().Order())
        {
            if (!sprites.ContainsKey(mobileId))
                diagnostics.Add($"World profile '{publicationRoot}' lacks generated media for encounter mobile '{mobileId}'.");
        }
        ReadOnlyMemory<byte>? normalizedWorld = files.GetExactlyOne(normalizedPath);
        IReadOnlyList<DaggerfallSiteLight> lights = ReadNormalizedLights(normalizedWorld, diagnostics);
        IReadOnlyList<DaggerfallDungeonActionModelDefinition> actionModels = ReadNormalizedActionModels(
            normalizedWorld, publicationRoot, artifacts, files, materials, diagnostics);
        IReadOnlyList<DaggerfallRdbDoorDefinition> doors = ReadNormalizedDoors(
            normalizedWorld, publicationRoot, meshPath, artifacts, materials, actionModels, diagnostics);
        IReadOnlyList<DaggerfallDungeonActionDefinition> dungeonActions = ReadNormalizedActions(normalizedWorld, doors, diagnostics);
        HashSet<string> actionIds = dungeonActions.Select(action => action.Id).ToHashSet(StringComparer.Ordinal);
        foreach (DaggerfallDungeonActionModelDefinition model in actionModels)
            if (!actionIds.Contains(model.ActionId))
                diagnostics.Add($"Normalized action model '{model.ActionId}' has no matching world action node.");
        DaggerfallDungeonMapContent? dungeonMap = profileKind == DaggerfallWorldProfileKind.Dungeon
            ? ReadNormalizedDungeonMap(normalizedWorld, doors, portals, diagnostics)
            : null;
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
            new ReadOnlyDictionary<int, NormalizedActorSprite>(sprites.ToDictionary()),
            audio,
            classicPresentation,
            start.Site,
            doors,
            profileKind,
            publicationRoot,
            portals,
            anchors,
            lights,
            groundContainerSprite,
            dungeonMap,
            dungeonActions,
            actionModels,
            ReadInteriorBuilding(normalizedWorld, profileKind, diagnostics));
    }

    private static DaggerfallInteriorBuilding? ReadInteriorBuilding(ReadOnlyMemory<byte>? bytes,
        DaggerfallWorldProfileKind kind, DaggerfallContentDiagnostics diagnostics)
    {
        if (bytes is null) return null;
        try { return DaggerfallInteriorBuilding.Read(bytes.Value, kind); }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
        {
            diagnostics.Add($"Normalized interior building metadata is malformed: {error.Message}");
            return null;
        }
    }

    /// <summary>
    /// Projects normalized RDB action nodes without interpreting source flags as a successful
    /// operation. The graph runtime owns trigger admission and action-family policy; content admission
    /// only validates stable identities, raw parameters, and the links/doors this closure carries.
    /// </summary>
    private static IReadOnlyList<DaggerfallDungeonActionDefinition> ReadNormalizedActions(
        ReadOnlyMemory<byte>? bytes,
        IReadOnlyList<DaggerfallRdbDoorDefinition> doors,
        DaggerfallContentDiagnostics diagnostics)
    {
        ArgumentNullException.ThrowIfNull(doors);
        if (bytes is null)
        {
            diagnostics.Add("Normalized world closure must contain normalized.json for its dungeon actions.");
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
                bool isFlat = OptionalBoolean(action, "isFlat", false, diagnostics);
                byte soundIndex = Byte(action, "soundIndex", diagnostics);
                byte rawIndex = action.TryGetProperty("rawIndex", out _)
                    ? Byte(action, "rawIndex", diagnostics)
                    : soundIndex;
                Vector3? sourcePosition = OptionalObjectVector3(action, "position", $"normalized dungeon action '{id}' position", diagnostics);

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
                    isFlat,
                    SoundIndex: soundIndex,
                    SourcePosition: sourcePosition,
                    RawIndex: rawIndex);
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
            diagnostics.Add($"Normalized world closure is not valid JSON: {exception.Message}");
            return [];
        }
    }

    /// <summary>
    /// Projects the importer-owned RDB light facts without reopening Arena2. The source record
    /// has position and radius; older normalized records omitted a colour and retain donor-neutral white.
    /// </summary>
    private static IReadOnlyList<DaggerfallSiteLight> ReadNormalizedLights(ReadOnlyMemory<byte>? bytes, DaggerfallContentDiagnostics diagnostics)
    {
        if (bytes is null)
        {
            diagnostics.Add("Normalized world closure must contain normalized.json for its source lights.");
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
                Vector3 color = !light.TryGetProperty("color", out JsonElement colorValue)
                    || colorValue.ValueKind == JsonValueKind.Null
                    ? Vector3.One
                    : ObjectVector3(colorValue, $"normalized light '{id}' color", diagnostics);
                try
                {
                    DaggerfallSiteLight projected = new DaggerfallSiteLight(id, new WorldPoint(position.X, position.Y, position.Z), range, intensity, color).Validate();
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
            diagnostics.Add($"Normalized world closure is not valid JSON: {exception.Message}");
            return [];
        }
    }

    private static IReadOnlyList<DaggerfallSitePortal> ReadSitePortals(JsonElement world, DaggerfallContentDiagnostics diagnostics)
    {
        if (!world.TryGetProperty("transitions", out JsonElement section)) return [];
        if (section.ValueKind != JsonValueKind.Array)
        {
            diagnostics.Add("world.transitions must be an array when supplied.");
            return [];
        }
        Dictionary<string, DaggerfallSitePortal> portals = new(StringComparer.Ordinal);
        foreach (JsonElement value in section.EnumerateArray())
        {
            JsonElement portal = DaggerfallBaseContent.Object(value, "world transition", diagnostics);
            DaggerfallBaseContent.RejectDuplicateProperties(portal, "world transition", diagnostics);
            try
            {
                DaggerfallSitePortal parsed = new DaggerfallSitePortal(
                    DaggerfallBaseContent.Text(portal, "id", diagnostics),
                    Point(DaggerfallBaseContent.Property(portal, "position", diagnostics), "world transition position", diagnostics),
                    DaggerfallBaseContent.Number(portal, "radius", diagnostics),
                    DaggerfallBaseContent.Text(portal, "destinationProfile", diagnostics)).Validate();
                if (!portals.TryAdd(parsed.Id, parsed)) diagnostics.Add($"World transitions repeat portal '{parsed.Id}'.");
            }
            catch (Exception exception) when (exception is ArgumentException or ArgumentOutOfRangeException)
            {
                diagnostics.Add($"World transition is malformed: {exception.Message}");
            }
        }
        return portals.Values.OrderBy(portal => portal.Id, StringComparer.Ordinal).ToArray();
    }

    private static IReadOnlyList<DaggerfallSiteAnchor> ReadSiteAnchors(JsonElement world, ScenarioStart start, DaggerfallContentDiagnostics diagnostics)
    {
        Dictionary<string, DaggerfallSiteAnchor> anchors = new(StringComparer.Ordinal)
        {
            ["start"] = new DaggerfallSiteAnchor("start", start.Position, start.Look.YawRadians, start.Look.PitchRadians).Validate(),
        };
        if (!world.TryGetProperty("anchors", out JsonElement section)) return anchors.Values.ToArray();
        if (section.ValueKind != JsonValueKind.Array)
        {
            diagnostics.Add("world.anchors must be an array when supplied.");
            return anchors.Values.ToArray();
        }
        foreach (JsonElement value in section.EnumerateArray())
        {
            JsonElement anchor = DaggerfallBaseContent.Object(value, "world anchor", diagnostics);
            DaggerfallBaseContent.RejectDuplicateProperties(anchor, "world anchor", diagnostics);
            try
            {
                DaggerfallSiteAnchor parsed = new DaggerfallSiteAnchor(
                    DaggerfallBaseContent.Text(anchor, "id", diagnostics),
                    Point(DaggerfallBaseContent.Property(anchor, "position", diagnostics), "world anchor position", diagnostics),
                    DaggerfallBaseContent.Number(anchor, "yawRadians", diagnostics),
                    DaggerfallBaseContent.Number(anchor, "pitchRadians", diagnostics)).Validate();
                if (!anchors.TryAdd(parsed.Id, parsed)) diagnostics.Add($"World anchors repeat anchor '{parsed.Id}'.");
            }
            catch (Exception exception) when (exception is ArgumentException or ArgumentOutOfRangeException)
            {
                diagnostics.Add($"World anchor is malformed: {exception.Message}");
            }
        }
        return anchors.Values.OrderBy(anchor => anchor.Id, StringComparer.Ordinal).ToArray();
    }

    private static DaggerfallWorldProfileKind InvalidProfileKind(DaggerfallContentDiagnostics diagnostics)
    {
        diagnostics.Add("Selected world profileKind must be exterior, interior, or dungeon.");
        return DaggerfallWorldProfileKind.Dungeon;
    }

    /// <summary>
    /// Reads action models from their normalized model-local meshes and separate source pose.
    /// Only material groups explicitly admitted for collision contribute to dynamic triangles.
    /// </summary>
    internal static IReadOnlyList<DaggerfallDungeonActionModelDefinition> ReadNormalizedActionModels(
        ReadOnlyMemory<byte>? bytes,
        string publicationRoot,
        IReadOnlyDictionary<string, ContentSha256> artifacts,
        AdmittedFiles files,
        IReadOnlyList<NormalizedMaterial> materials,
        DaggerfallContentDiagnostics diagnostics)
    {
        if (bytes is null)
        {
            diagnostics.Add("Normalized world closure must contain normalized.json for its action models.");
            return [];
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(bytes.Value);
            JsonElement root = DaggerfallBaseContent.Object(document.RootElement, "normalized action models", diagnostics);
            JsonElement world = DaggerfallBaseContent.Object(DaggerfallBaseContent.Property(root, "world", diagnostics), "normalized action models.world", diagnostics);
            if (!world.TryGetProperty("actionModels", out JsonElement modelSection)) return [];
            if (modelSection.ValueKind != JsonValueKind.Array)
            {
                diagnostics.Add("Normalized world.actionModels must be an array.");
                return [];
            }

            Dictionary<string, ContentArtifact> artifactById = new(StringComparer.Ordinal);
            foreach (JsonElement candidate in DaggerfallBaseContent.Array(root, "artifacts", diagnostics))
            {
                JsonElement artifact = DaggerfallBaseContent.Object(candidate, "normalized action-model artifact", diagnostics);
                string id = DaggerfallBaseContent.Text(artifact, "id", diagnostics);
                string relativePath = DaggerfallBaseContent.Text(artifact, "relativePath", diagnostics);
                string path = $"{publicationRoot.TrimEnd('/')}/{relativePath}";
                if (!artifacts.TryGetValue(path, out ContentSha256 hash))
                    diagnostics.Add($"Normalized action-model artifact '{id}' is absent from the admitted import manifest.");
                else if (!artifactById.TryAdd(id, new ContentArtifact(path, hash)))
                    diagnostics.Add($"Normalized world repeats artifact '{id}'.");
            }

            Dictionary<string, uint> worldMaterialSlots = materials
                .Where(material => !string.IsNullOrWhiteSpace(material.MaterialResourceId))
                .ToDictionary(material => material.MaterialResourceId, material => material.Slot, StringComparer.Ordinal);
            Dictionary<string, JsonElement> meshes = new(StringComparer.Ordinal);
            foreach (JsonElement candidate in DaggerfallBaseContent.Array(root, "meshes", diagnostics))
            {
                JsonElement mesh = DaggerfallBaseContent.Object(candidate, "normalized action-model mesh", diagnostics);
                string id = DaggerfallBaseContent.Text(mesh, "id", diagnostics);
                if (!meshes.TryAdd(id, mesh)) diagnostics.Add($"Normalized world repeats mesh '{id}'.");
            }

            List<DaggerfallDungeonActionModelDefinition> result = [];
            HashSet<string> actionIds = new(StringComparer.Ordinal);
            HashSet<string> doorIds = new(StringComparer.Ordinal);
            foreach (JsonElement value in modelSection.EnumerateArray())
            {
                JsonElement model = DaggerfallBaseContent.Object(value, "normalized action model", diagnostics);
                DaggerfallBaseContent.RejectDuplicateProperties(model, "normalized action model", diagnostics);
                string actionId = DaggerfallBaseContent.Text(model, "actionId", diagnostics);
                string description = DaggerfallBaseContent.Text(model, "description", diagnostics);
                ushort modelIndex = UShort(model, "modelIndex", diagnostics);
                byte rawIndex = model.TryGetProperty("rawIndex", out _) ? Byte(model, "rawIndex", diagnostics) : (byte)0;
                string? doorId = OptionalNullableText(model, "doorId", diagnostics);
                DaggerfallRdbDoorId? doorIdentity = null;
                if (doorId is not null)
                {
                    if (TryDoorIdentity(doorId, out DaggerfallRdbDoorId parsedDoor)) doorIdentity = parsedDoor;
                    else diagnostics.Add($"Normalized action model '{actionId}' has invalid door identity '{doorId}'.");
                }
                Vector3 position = ObjectVector3(DaggerfallBaseContent.Property(model, "position", diagnostics), $"normalized action model '{actionId}' position", diagnostics);
                Vector3 rotation = ObjectVector3(DaggerfallBaseContent.Property(model, "rotationDegrees", diagnostics), $"normalized action model '{actionId}' rotation", diagnostics);
                JsonElement bounds = DaggerfallBaseContent.Object(DaggerfallBaseContent.Property(model, "localBounds", diagnostics), $"normalized action model '{actionId}' localBounds", diagnostics);
                Vector3 boundsMin = ObjectVector3(DaggerfallBaseContent.Property(bounds, "minimum", diagnostics), $"normalized action model '{actionId}' minimum", diagnostics);
                Vector3 boundsMax = ObjectVector3(DaggerfallBaseContent.Property(bounds, "maximum", diagnostics), $"normalized action model '{actionId}' maximum", diagnostics);
                string visualArtifactId = DaggerfallBaseContent.Text(model, "visualArtifactId", diagnostics);
                if (!actionIds.Add(actionId)) diagnostics.Add($"Normalized world repeats action model '{actionId}'.");
                if (doorId is not null && !doorIds.Add(doorId)) diagnostics.Add($"Normalized world repeats action-model door visual '{doorId}'.");
                if (!artifactById.TryGetValue(visualArtifactId, out ContentArtifact? visualArtifact))
                {
                    diagnostics.Add($"Normalized action model '{actionId}' has no admitted visual artifact '{visualArtifactId}'.");
                    continue;
                }

                List<Vector3> collisionVertices = [];
                List<Triangle> collisionTriangles = [];
                HashSet<string> referencedMeshIds = new(StringComparer.Ordinal);
                foreach (JsonElement meshIdValue in DaggerfallBaseContent.Array(model, "meshIds", diagnostics))
                {
                    if (meshIdValue.ValueKind != JsonValueKind.String || meshIdValue.GetString() is not { Length: > 0 } meshId)
                    {
                        diagnostics.Add($"Normalized action model '{actionId}' meshIds must contain non-empty strings.");
                        continue;
                    }
                    if (!referencedMeshIds.Add(meshId))
                    {
                        diagnostics.Add($"Normalized action model '{actionId}' repeats mesh '{meshId}'.");
                        continue;
                    }
                    if (!meshes.TryGetValue(meshId, out JsonElement mesh))
                    {
                        diagnostics.Add($"Normalized action model '{actionId}' refers to missing mesh '{meshId}'.");
                        continue;
                    }
                    string meshArtifactId = DaggerfallBaseContent.Text(mesh, "artifactId", diagnostics);
                    if (!StringComparer.Ordinal.Equals(meshArtifactId, visualArtifactId))
                        diagnostics.Add($"Normalized action model '{actionId}' mesh '{meshId}' refers to artifact '{meshArtifactId}', not '{visualArtifactId}'.");
                    Vector3[] vertices = DaggerfallBaseContent.Array(mesh, "vertices", diagnostics)
                        .Select(vertex => ObjectVector3(DaggerfallBaseContent.Object(vertex, $"action model '{actionId}' mesh vertex", diagnostics), $"action model '{actionId}' mesh vertex", diagnostics))
                        .ToArray();
                    JsonElement[] triangles = DaggerfallBaseContent.Array(mesh, "triangles", diagnostics).ToArray();
                    bool[] collidable = new bool[triangles.Length];
                    foreach (JsonElement groupValue in DaggerfallBaseContent.Array(mesh, "materialGroups", diagnostics))
                    {
                        JsonElement group = DaggerfallBaseContent.Object(groupValue, $"action model '{actionId}' material group", diagnostics);
                        string materialId = DaggerfallBaseContent.Text(group, "materialResourceId", diagnostics);
                        int start = DaggerfallBaseContent.Integer(group, "startTriangle", diagnostics);
                        int count = DaggerfallBaseContent.Integer(group, "triangleCount", diagnostics);
                        if (!group.TryGetProperty("participatesInCollision", out _))
                            diagnostics.Add($"Action model '{actionId}' material group '{materialId}' has no collision admission flag.");
                        bool participates = OptionalBoolean(group, "participatesInCollision", false, diagnostics);
                        if (start < 0 || count < 0 || start > triangles.Length - count)
                        {
                            diagnostics.Add($"Action model '{actionId}' material group '{materialId}' has an invalid triangle range.");
                            continue;
                        }
                        if (participates)
                            Array.Fill(collidable, true, start, count);
                    }
                    if (!collidable.Any(value => value)) continue;
                    uint firstVertex = checked((uint)collisionVertices.Count);
                    collisionVertices.AddRange(vertices);
                    for (int triangleIndex = 0; triangleIndex < triangles.Length; triangleIndex++)
                    {
                        if (!collidable[triangleIndex]) continue;
                        JsonElement triangle = DaggerfallBaseContent.Object(triangles[triangleIndex], $"action model '{actionId}' triangle", diagnostics);
                        int a = DaggerfallBaseContent.Integer(triangle, "firstVertex", diagnostics);
                        int b = DaggerfallBaseContent.Integer(triangle, "secondVertex", diagnostics);
                        int c = DaggerfallBaseContent.Integer(triangle, "thirdVertex", diagnostics);
                        if ((uint)a >= (uint)vertices.Length || (uint)b >= (uint)vertices.Length || (uint)c >= (uint)vertices.Length)
                        {
                            diagnostics.Add($"Action model '{actionId}' collision triangle {triangleIndex} references a missing vertex.");
                            continue;
                        }
                        collisionTriangles.Add(new Triangle(firstVertex + checked((uint)a), firstVertex + checked((uint)b), firstVertex + checked((uint)c)));
                    }
                }

                try
                {
                    ReadOnlyMemory<byte> visualBytes = files.GetExactlyOne(visualArtifact.Path)
                        ?? throw new InvalidOperationException($"Normalized action-model visual '{visualArtifact.Path}' is not admitted.");
                    IReadOnlyList<DaggerfallDoorMaterialBinding> materialBindings = ReadActionModelMaterials(
                        visualBytes, actionId, worldMaterialSlots, diagnostics);
                    DaggerfallDoorVisual visual = new DaggerfallDoorVisual(visualArtifact.Path, visualArtifact.Sha256, materialBindings).Validate();
                    result.Add(new DaggerfallDungeonActionModelDefinition(
                        actionId,
                        doorId,
                        doorIdentity,
                        description,
                        modelIndex,
                        rawIndex,
                        visual,
                        DaggerfallDungeonMotionPolicy.InitialTransform(position, rotation),
                        boundsMin,
                        boundsMax,
                        [.. collisionVertices],
                        [.. collisionTriangles]).Validate());
                }
                catch (ArgumentException exception)
                {
                    diagnostics.Add($"Normalized action model '{actionId}' is invalid: {exception.Message}");
                }
            }
            return result.OrderBy(model => model.ActionId, StringComparer.Ordinal).ToArray();
        }
        catch (JsonException exception)
        {
            diagnostics.Add($"Normalized world closure is not valid JSON: {exception.Message}");
            return [];
        }
    }

    private static IReadOnlyList<DaggerfallDoorMaterialBinding> ReadActionModelMaterials(
        ReadOnlyMemory<byte> bytes,
        string actionId,
        IReadOnlyDictionary<string, uint> worldMaterialSlots,
        DaggerfallContentDiagnostics diagnostics)
    {
        using JsonDocument document = JsonDocument.Parse(bytes);
        JsonElement root = DaggerfallBaseContent.Object(document.RootElement, $"action model '{actionId}' visual artifact", diagnostics);
        List<DaggerfallDoorMaterialBinding> bindings = [];
        HashSet<uint> localSlots = [];
        foreach (JsonElement value in DaggerfallBaseContent.Array(root, "materialSlots", diagnostics))
        {
            JsonElement slot = DaggerfallBaseContent.Object(value, $"action model '{actionId}' material slot", diagnostics);
            int local = DaggerfallBaseContent.Integer(slot, "slot", diagnostics);
            string resourceId = DaggerfallBaseContent.Text(slot, "material", diagnostics);
            if (local < 0 || !localSlots.Add(checked((uint)Math.Max(local, 0))))
            {
                diagnostics.Add($"Action model '{actionId}' has a negative or repeated visual material slot.");
                continue;
            }
            if (!worldMaterialSlots.TryGetValue(resourceId, out uint worldSlot))
            {
                diagnostics.Add($"Action model '{actionId}' visual refers to missing material resource '{resourceId}'.");
                continue;
            }
            bindings.Add(new DaggerfallDoorMaterialBinding(checked((uint)local), worldSlot));
        }
        if (bindings.Count == 0) diagnostics.Add($"Action model '{actionId}' visual artifact has no admitted material slots.");
        return bindings.OrderBy(binding => binding.MeshSlot).ToArray();
    }

    /// <summary>
    /// Selects the actual RDB action doors published with this world closure.  The import has
    /// already converted source coordinates and model rotations; this reader only validates and
    /// projects those normalized facts.  Door bounds come from the action visual meshes, whose
    /// geometry is deliberately excluded from the world's static collision artifact.
    /// </summary>
    private static IReadOnlyList<DaggerfallRdbDoorDefinition> ReadNormalizedDoors(
        ReadOnlyMemory<byte>? bytes,
        string publicationRoot,
        string staticMeshPath,
        IReadOnlyDictionary<string, ContentSha256> artifacts,
        IReadOnlyList<NormalizedMaterial> materials,
        IReadOnlyList<DaggerfallDungeonActionModelDefinition> actionModels,
        DaggerfallContentDiagnostics diagnostics)
    {
        if (bytes is null)
        {
            diagnostics.Add("Normalized world closure must contain normalized.json for its selected RDB doors.");
            return [];
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(bytes.Value);
            JsonElement root = DaggerfallBaseContent.Object(document.RootElement, "normalized world", diagnostics);
            JsonElement world = DaggerfallBaseContent.Object(DaggerfallBaseContent.Property(root, "world", diagnostics), "normalized world.world", diagnostics);
            Dictionary<string, ContentArtifact> artifactById = [];
            foreach (JsonElement candidate in DaggerfallBaseContent.Array(root, "artifacts", diagnostics))
            {
                JsonElement artifact = DaggerfallBaseContent.Object(candidate, "normalized artifact", diagnostics);
                string id = DaggerfallBaseContent.Text(artifact, "id", diagnostics);
                string relativePath = DaggerfallBaseContent.Text(artifact, "relativePath", diagnostics);
                string path = $"{publicationRoot.TrimEnd('/')}/{relativePath}";
                if (!artifacts.TryGetValue(path, out ContentSha256 hash)) diagnostics.Add($"Normalized artifact '{id}' is absent from the admitted import manifest.");
                else if (!artifactById.TryAdd(id, new ContentArtifact(path, hash))) diagnostics.Add($"Normalized world repeats artifact '{id}'.");
            }
            string? staticMeshArtifactId = artifactById.SingleOrDefault(pair => StringComparer.Ordinal.Equals(pair.Value.Path, staticMeshPath)).Key;
            if (string.IsNullOrWhiteSpace(staticMeshArtifactId))
                diagnostics.Add("Normalized world has no admitted static-mesh artifact for its action-door visuals.");
            Dictionary<string, uint> worldMaterialSlots = materials.Where(material => !string.IsNullOrWhiteSpace(material.MaterialResourceId))
                .ToDictionary(material => material.MaterialResourceId, material => material.Slot, StringComparer.Ordinal);
            Dictionary<string, DaggerfallDungeonActionModelDefinition> actionModelsByDoor = [];
            foreach (DaggerfallDungeonActionModelDefinition model in actionModels.Where(model => model.DoorId is not null))
            {
                if (!actionModelsByDoor.TryAdd(model.DoorId!, model))
                    diagnostics.Add($"Normalized action models repeat door visual '{model.DoorId}'.");
            }
            Dictionary<string, JsonElement> meshes = [];
            foreach (JsonElement candidate in DaggerfallBaseContent.Array(root, "meshes", diagnostics))
            {
                JsonElement mesh = DaggerfallBaseContent.Object(candidate, "normalized world mesh", diagnostics);
                string id = DaggerfallBaseContent.Text(mesh, "id", diagnostics);
                if (!meshes.TryAdd(id, mesh)) diagnostics.Add($"Normalized world repeats mesh '{id}'.");
            }

            List<DaggerfallRdbDoorDefinition> result = [];
            HashSet<DaggerfallRdbDoorId> identities = [];
            foreach (JsonElement candidate in DaggerfallBaseContent.Array(world, "doors", diagnostics))
            {
                JsonElement door = DaggerfallBaseContent.Object(candidate, "normalized RDB door", diagnostics);
                string sourceId = DaggerfallBaseContent.Text(door, "id", diagnostics);
                if (!TryDoorIdentity(sourceId, out DaggerfallRdbDoorId identity))
                {
                    diagnostics.Add($"Normalized door '{sourceId}' must name one RDB model as door/<source>-rdb/<block-x>/<block-z>/<model-index>.");
                    continue;
                }
                if (!identities.Add(identity))
                {
                    diagnostics.Add($"Normalized world repeats RDB door '{identity}'.");
                    continue;
                }

                Vector3 position = ObjectVector3(DaggerfallBaseContent.Property(door, "position", diagnostics), $"normalized door '{sourceId}' position", diagnostics);
                Vector3 rotation = ObjectVector3(DaggerfallBaseContent.Property(door, "rotationDegrees", diagnostics), $"normalized door '{sourceId}' rotation", diagnostics);
                DaggerfallDoorKind kind = DaggerfallBaseContent.Text(door, "kind", diagnostics) switch
                {
                    "normal" => DaggerfallDoorKind.Normal,
                    "special" => DaggerfallDoorKind.Special,
                    _ => InvalidDoorKind(identity, diagnostics),
                };
                int startingLock = DaggerfallBaseContent.Integer(door, "startingLockValue", diagnostics);
                DaggerfallDoorActionSource? action = ReadDoorAction(DaggerfallBaseContent.Property(door, "action", diagnostics), identity, diagnostics);
                if (rotation.X != 0F || rotation.Z != 0F)
                {
                    diagnostics.Add($"Normalized RDB door '{identity}' must have a yaw-only rotation.");
                    continue;
                }

                if (actionModelsByDoor.TryGetValue(sourceId, out DaggerfallDungeonActionModelDefinition? actionModel))
                {
                    if (actionModel.Visual is not { } modelVisual)
                    {
                        diagnostics.Add($"Normalized RDB door '{identity}' action model has no visual artifact.");
                        continue;
                    }
                    try
                    {
                        result.Add(new DaggerfallRdbDoorDefinition(
                            identity,
                            actionModel.InitialTransform.Translation,
                            rotation,
                            actionModel.LocalBoundsMin,
                            actionModel.LocalBoundsMax,
                            kind,
                            startingLock,
                            Visual: modelVisual,
                            Action: action).Validate());
                    }
                    catch (ArgumentException exception)
                    {
                        diagnostics.Add($"Normalized RDB door '{identity}' has invalid action-model pose or bounds: {exception.Message}");
                    }
                    continue;
                }

                List<Vector3> vertices = [];
                HashSet<string> materialIds = [];
                foreach (JsonElement meshIdValue in DaggerfallBaseContent.Array(door, "visualMeshIds", diagnostics))
                {
                    if (meshIdValue.ValueKind != JsonValueKind.String)
                    {
                        diagnostics.Add($"Normalized RDB door '{identity}' visualMeshIds must contain strings.");
                        continue;
                    }
                    string meshId = meshIdValue.GetString() ?? string.Empty;
                    if (!meshes.TryGetValue(meshId, out JsonElement mesh))
                    {
                        diagnostics.Add($"Normalized RDB door '{identity}' refers to missing visual mesh '{meshId}'.");
                        continue;
                    }
                    foreach (JsonElement vertex in DaggerfallBaseContent.Array(mesh, "vertices", diagnostics))
                        vertices.Add(ObjectVector3(DaggerfallBaseContent.Object(vertex, $"normalized RDB door '{identity}' vertex", diagnostics), $"normalized RDB door '{identity}' vertex", diagnostics));
                    foreach (JsonElement groupValue in DaggerfallBaseContent.Array(mesh, "materialGroups", diagnostics))
                        materialIds.Add(DaggerfallBaseContent.Text(DaggerfallBaseContent.Object(groupValue, $"normalized RDB door '{identity}' material group", diagnostics), "materialResourceId", diagnostics));
                }
                if (vertices.Count == 0)
                {
                    diagnostics.Add($"Normalized RDB door '{identity}' must have action visual geometry.");
                    continue;
                }

                Quaternion closed = DaggerfallDoorPose.ClosedRotation(rotation);
                Quaternion inverse = Quaternion.Inverse(closed);
                Vector3 first = Vector3.Transform(vertices[0] - position, inverse);
                Vector3 minimum = first;
                Vector3 maximum = first;
                foreach (Vector3 worldVertex in vertices.Skip(1))
                {
                    Vector3 local = Vector3.Transform(worldVertex - position, inverse);
                    minimum = Vector3.Min(minimum, local);
                    maximum = Vector3.Max(maximum, local);
                }
                try
                {
                    string artifactId = $"{staticMeshArtifactId}/door/{sourceId["door/".Length..].Replace('/', '-')}";
                    if (!artifactById.TryGetValue(artifactId, out ContentArtifact? visualArtifact))
                    {
                        diagnostics.Add($"Normalized RDB door '{identity}' has no separately published visual artifact.");
                        continue;
                    }
                    DaggerfallDoorMaterialBinding[] bindings = materialIds.OrderBy(material => material, StringComparer.Ordinal)
                        .Select(material => worldMaterialSlots.TryGetValue(material, out uint worldSlot)
                            ? new DaggerfallDoorMaterialBinding(worldSlot, worldSlot)
                            : throw new InvalidOperationException($"Normalized RDB door '{identity}' refers to missing material '{material}'."))
                        .ToArray();
                    result.Add(new DaggerfallRdbDoorDefinition(identity, position, rotation, minimum, maximum, kind, startingLock,
                        Visual: new DaggerfallDoorVisual(visualArtifact.Path, visualArtifact.Sha256, bindings), Action: action).Validate());
                }
                catch (ArgumentException exception)
                {
                    diagnostics.Add($"Normalized RDB door '{identity}' has invalid pose or bounds: {exception.Message}");
                }
            }
            return result.OrderBy(value => value.Id.SourceKey, StringComparer.Ordinal)
                .ThenBy(value => value.Id.BlockX)
                .ThenBy(value => value.Id.BlockZ)
                .ThenBy(value => value.Id.ModelIndex)
                .ToArray();
        }
        catch (JsonException exception)
        {
            diagnostics.Add($"Normalized world closure is not valid JSON: {exception.Message}");
            return [];
        }
    }

    private static bool TryDoorIdentity(string sourceId, out DaggerfallRdbDoorId identity)
    {
        identity = default;
        string[] parts = sourceId.Split('/', StringSplitOptions.None);
        if (parts.Length != 5 || !StringComparer.Ordinal.Equals(parts[0], "door")
            || !parts[1].EndsWith("-rdb", StringComparison.Ordinal)
            || !int.TryParse(parts[2], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int blockX)
            || !int.TryParse(parts[3], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int blockZ)
            || !int.TryParse(parts[4], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int modelIndex))
            return false;
        string source = parts[1][..^"-rdb".Length];
        if (source.Length == 0) return false;
        identity = new DaggerfallRdbDoorId($"{source.ToUpperInvariant()}.RDB", blockX, blockZ, modelIndex);
        return true;
    }

    /// <summary>
    /// Projects stable dungeon discovery facts directly from normalized importer metadata. Geometry is
    /// placement-scoped even where its render mesh is a shared material-group aggregate. This deliberately
    /// reads neither static-mesh nor collision artifact bytes.
    /// </summary>
    private static DaggerfallDungeonMapContent? ReadNormalizedDungeonMap(
        ReadOnlyMemory<byte>? bytes,
        IReadOnlyList<DaggerfallRdbDoorDefinition> doors,
        IReadOnlyList<DaggerfallSitePortal> portals,
        DaggerfallContentDiagnostics diagnostics)
    {
        if (bytes is null)
        {
            diagnostics.Add("Normalized dungeon map content requires normalized.json.");
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(bytes.Value);
            JsonElement root = DaggerfallBaseContent.Object(document.RootElement, "normalized dungeon map", diagnostics);
            JsonElement world = DaggerfallBaseContent.Object(
                DaggerfallBaseContent.Property(root, "world", diagnostics),
                "normalized dungeon map.world",
                diagnostics);

            List<string> allWorldMeshIds = [];
            HashSet<string> allWorldMeshIdSet = new(StringComparer.Ordinal);
            foreach (JsonElement value in DaggerfallBaseContent.Array(world, "meshIds", diagnostics))
            {
                if (value.ValueKind != JsonValueKind.String || value.GetString() is not { Length: > 0 } meshId)
                {
                    diagnostics.Add("Normalized dungeon world.meshIds must contain non-empty strings.");
                    continue;
                }
                if (!DaggerfallBaseContent.ValidId(meshId))
                {
                    diagnostics.Add($"Normalized dungeon world.meshIds contains invalid mesh id '{meshId}'.");
                    continue;
                }
                if (!allWorldMeshIdSet.Add(meshId))
                {
                    diagnostics.Add($"Normalized dungeon world.meshIds repeats mesh '{meshId}'.");
                    continue;
                }
                allWorldMeshIds.Add(meshId);
            }

            HashSet<string> actionMeshIds = new(StringComparer.Ordinal);
            if (world.TryGetProperty("actionModels", out JsonElement actionModelsValue)
                && actionModelsValue.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement modelValue in actionModelsValue.EnumerateArray())
                {
                    JsonElement model = DaggerfallBaseContent.Object(modelValue, "normalized dungeon action model", diagnostics);
                    foreach (JsonElement meshValue in DaggerfallBaseContent.Array(model, "meshIds", diagnostics))
                    {
                        if (meshValue.ValueKind == JsonValueKind.String && meshValue.GetString() is { Length: > 0 } meshId)
                            actionMeshIds.Add(meshId);
                    }
                }
            }
            HashSet<string> actionDoorMeshIds = new(StringComparer.Ordinal);
            foreach (JsonElement doorValue in DaggerfallBaseContent.Array(world, "doors", diagnostics))
            {
                JsonElement door = DaggerfallBaseContent.Object(doorValue, "normalized dungeon door", diagnostics);
                foreach (JsonElement meshValue in DaggerfallBaseContent.Array(door, "visualMeshIds", diagnostics))
                    if (meshValue.ValueKind == JsonValueKind.String && meshValue.GetString() is { Length: > 0 } meshId)
                        actionDoorMeshIds.Add(meshId);
            }

            List<string> worldMeshIds = [];
            HashSet<string> worldMeshIdSet = new(StringComparer.Ordinal);
            if (world.TryGetProperty("staticMeshIds", out JsonElement staticMeshIdsValue))
            {
                if (staticMeshIdsValue.ValueKind != JsonValueKind.Array)
                    diagnostics.Add("Normalized dungeon world.staticMeshIds must be an array.");
                else
                {
                    foreach (JsonElement value in staticMeshIdsValue.EnumerateArray())
                    {
                        if (value.ValueKind != JsonValueKind.String || value.GetString() is not { Length: > 0 } meshId)
                        {
                            diagnostics.Add("Normalized dungeon world.staticMeshIds must contain non-empty strings.");
                            continue;
                        }
                        if (!DaggerfallBaseContent.ValidId(meshId))
                        {
                            diagnostics.Add($"Normalized dungeon world.staticMeshIds contains invalid mesh id '{meshId}'.");
                            continue;
                        }
                        if (!worldMeshIdSet.Add(meshId))
                        {
                            diagnostics.Add($"Normalized dungeon world.staticMeshIds repeats mesh '{meshId}'.");
                            continue;
                        }
                        worldMeshIds.Add(meshId);
                    }
                }
            }
            else
            {
                // Legacy normalized worlds have no explicit static mesh partition. Their
                // geometry placements still name door/action meshes because those placements
                // are also the source of dungeon discovery bounds. Keep the legacy map closure
                // whole here; rendering/collision admission is handled by the world/action
                // projection owners, not by this discovery metadata reader.
                foreach (string meshId in allWorldMeshIds)
                {
                    worldMeshIds.Add(meshId);
                    worldMeshIdSet.Add(meshId);
                }
            }
            foreach (string meshId in worldMeshIds)
            {
                if (!allWorldMeshIdSet.Contains(meshId))
                    diagnostics.Add($"Normalized dungeon world.staticMeshIds refers to mesh '{meshId}', which world.meshIds does not carry.");
                if (world.TryGetProperty("staticMeshIds", out _)
                    && (actionMeshIds.Contains(meshId) || actionDoorMeshIds.Contains(meshId)))
                    diagnostics.Add($"Normalized dungeon world.staticMeshIds includes dynamic action visual mesh '{meshId}'.");
            }

            Dictionary<string, JsonElement> meshes = new(StringComparer.Ordinal);
            foreach (JsonElement value in DaggerfallBaseContent.Array(root, "meshes", diagnostics))
            {
                JsonElement mesh = DaggerfallBaseContent.Object(value, "normalized dungeon mesh", diagnostics);
                string meshId = DaggerfallBaseContent.Text(mesh, "id", diagnostics);
                if (!DaggerfallBaseContent.ValidId(meshId))
                {
                    diagnostics.Add($"Normalized dungeon mesh has invalid id '{meshId}'.");
                    continue;
                }
                if (!meshes.TryAdd(meshId, mesh))
                    diagnostics.Add($"Normalized dungeon repeats mesh '{meshId}'.");
            }

            HashSet<string> doorVisualMeshIds = new(StringComparer.Ordinal);
            foreach (JsonElement value in DaggerfallBaseContent.Array(world, "doors", diagnostics))
            {
                JsonElement door = DaggerfallBaseContent.Object(value, "normalized dungeon door", diagnostics);
                string doorId = DaggerfallBaseContent.Text(door, "id", diagnostics);
                foreach (JsonElement visual in DaggerfallBaseContent.Array(door, "visualMeshIds", diagnostics))
                {
                    if (visual.ValueKind != JsonValueKind.String || visual.GetString() is not { Length: > 0 } meshId)
                    {
                        diagnostics.Add($"Normalized dungeon door '{doorId}' visualMeshIds must contain non-empty strings.");
                        continue;
                    }
                    if (!allWorldMeshIdSet.Contains(meshId))
                        diagnostics.Add($"Normalized dungeon door '{doorId}' refers to mesh '{meshId}', which world.meshIds does not carry.");
                    if (!doorVisualMeshIds.Add(meshId))
                        diagnostics.Add($"Normalized dungeon door visuals repeat mesh '{meshId}'.");
                }
            }

            Dictionary<string, DaggerfallRdbDoorId> doorIdsBySourceId = [];
            foreach (JsonElement value in DaggerfallBaseContent.Array(world, "doors", diagnostics))
            {
                JsonElement door = DaggerfallBaseContent.Object(value, "normalized dungeon door", diagnostics);
                string doorId = DaggerfallBaseContent.Text(door, "id", diagnostics);
                if (TryDoorIdentity(doorId, out DaggerfallRdbDoorId identity))
                {
                    if (!doorIdsBySourceId.TryAdd(doorId, identity))
                        diagnostics.Add($"Normalized dungeon doors repeat source id '{doorId}'.");
                }
            }

            List<DaggerfallDungeonMapGeometry> geometry = [];
            HashSet<string> placementMeshIds = new(StringComparer.Ordinal);
            foreach (JsonElement value in DaggerfallBaseContent.Array(world, "geometryPlacements", diagnostics))
            {
                JsonElement placement = DaggerfallBaseContent.Object(value, "normalized dungeon geometry placement", diagnostics);
                DaggerfallBaseContent.RejectDuplicateProperties(placement, "normalized dungeon geometry placement", diagnostics);
                string placementId = DaggerfallBaseContent.Text(placement, "id", diagnostics);
                JsonElement bounds = DaggerfallBaseContent.Object(
                    DaggerfallBaseContent.Property(placement, "bounds", diagnostics),
                    $"normalized dungeon geometry placement '{placementId}' bounds",
                    diagnostics);
                Vector3 boundsMin = ObjectVector3(
                    DaggerfallBaseContent.Property(bounds, "minimum", diagnostics),
                    $"normalized dungeon geometry placement '{placementId}' minimum",
                    diagnostics);
                Vector3 boundsMax = ObjectVector3(
                    DaggerfallBaseContent.Property(bounds, "maximum", diagnostics),
                    $"normalized dungeon geometry placement '{placementId}' maximum",
                    diagnostics);

                List<string> meshIds = [];
                HashSet<string> placementMeshIdSet = new(StringComparer.Ordinal);
                foreach (JsonElement meshValue in DaggerfallBaseContent.Array(placement, "meshIds", diagnostics))
                {
                    if (meshValue.ValueKind != JsonValueKind.String || meshValue.GetString() is not { Length: > 0 } meshId)
                    {
                        diagnostics.Add($"Normalized dungeon geometry placement '{placementId}' meshIds must contain non-empty strings.");
                        continue;
                    }
                    if (!worldMeshIdSet.Contains(meshId))
                    {
                        diagnostics.Add($"Normalized dungeon geometry placement '{placementId}' refers to mesh '{meshId}', which world.meshIds does not carry.");
                        continue;
                    }
                    if (!meshes.ContainsKey(meshId))
                        diagnostics.Add($"Normalized dungeon world references mesh '{meshId}', which normalized.json does not carry.");
                    if (!placementMeshIdSet.Add(meshId))
                    {
                        diagnostics.Add($"Normalized dungeon geometry placement '{placementId}' repeats mesh '{meshId}'.");
                        continue;
                    }
                    meshIds.Add(meshId);
                    placementMeshIds.Add(meshId);
                }

                List<Vector3> samplePoints = [];
                foreach (JsonElement sampleValue in DaggerfallBaseContent.Array(placement, "samplePoints", diagnostics))
                {
                    samplePoints.Add(ObjectVector3(
                        DaggerfallBaseContent.Object(sampleValue, $"normalized dungeon geometry placement '{placementId}' sample point", diagnostics),
                        $"normalized dungeon geometry placement '{placementId}' sample point",
                        diagnostics));
                }

                DaggerfallRdbDoorId? doorId = null;
                if (placement.TryGetProperty("doorId", out JsonElement doorValue) && doorValue.ValueKind != JsonValueKind.Null)
                {
                    if (doorValue.ValueKind != JsonValueKind.String || doorValue.GetString() is not { Length: > 0 } sourceDoorId)
                    {
                        diagnostics.Add($"Normalized dungeon geometry placement '{placementId}' doorId must be a source door id or null.");
                    }
                    else if (!doorIdsBySourceId.TryGetValue(sourceDoorId, out DaggerfallRdbDoorId identity))
                    {
                        diagnostics.Add($"Normalized dungeon geometry placement '{placementId}' refers to unknown source door '{sourceDoorId}'.");
                    }
                    else
                    {
                        doorId = identity;
                        if (!doors.Any(candidate => candidate.Id == identity))
                            diagnostics.Add($"Normalized dungeon geometry placement '{placementId}' refers to door '{sourceDoorId}', which was not admitted as a runtime door.");
                        if (meshIds.Any(meshId => !doorVisualMeshIds.Contains(meshId)))
                            diagnostics.Add($"Normalized dungeon geometry placement '{placementId}' names door '{sourceDoorId}' but includes a non-door visual mesh.");
                    }
                }
                else if (meshIds.Any(doorVisualMeshIds.Contains))
                {
                    diagnostics.Add($"Normalized static dungeon geometry placement '{placementId}' includes an action-door visual mesh.");
                }

                try
                {
                    geometry.Add(new DaggerfallDungeonMapGeometry(placementId, boundsMin, boundsMax, meshIds, samplePoints, doorId).Validate());
                }
                catch (ArgumentException exception)
                {
                    diagnostics.Add($"Normalized dungeon geometry placement '{placementId}' is invalid: {exception.Message}");
                }
            }

            foreach (string meshId in worldMeshIds)
            {
                if (!meshes.ContainsKey(meshId))
                    diagnostics.Add($"Normalized dungeon world references mesh '{meshId}', which normalized.json does not carry.");
                if (!placementMeshIds.Contains(meshId))
                    diagnostics.Add($"Normalized dungeon world mesh '{meshId}' has no source geometry placement.");
            }

            List<DaggerfallDungeonMapMarker> markers = [];
            if (world.TryGetProperty("enterMarker", out JsonElement entranceValue) && entranceValue.ValueKind != JsonValueKind.Null)
            {
                JsonElement entrance = DaggerfallBaseContent.Object(entranceValue, "normalized dungeon entrance marker", diagnostics);
                DaggerfallBaseContent.RejectDuplicateProperties(entrance, "normalized dungeon entrance marker", diagnostics);
                string id = DaggerfallBaseContent.Text(entrance, "id", diagnostics);
                Vector3 position = ObjectVector3(
                    DaggerfallBaseContent.Property(entrance, "position", diagnostics),
                    $"normalized dungeon entrance marker '{id}' position",
                    diagnostics);
                try
                {
                    markers.Add(new DaggerfallDungeonMapMarker(
                        id,
                        DaggerfallDungeonMapMarkerKind.Entrance,
                        new WorldPoint(position.X, position.Y, position.Z)).Validate());
                }
                catch (ArgumentException exception)
                {
                    diagnostics.Add($"Normalized dungeon entrance marker '{id}' is invalid: {exception.Message}");
                }
            }

            foreach (DaggerfallSitePortal portal in portals)
            {
                try
                {
                    markers.Add(new DaggerfallDungeonMapMarker(
                        portal.Id,
                        DaggerfallDungeonMapMarkerKind.Portal,
                        portal.Position,
                        portal.DestinationLogicalProfile).Validate());
                }
                catch (ArgumentException exception)
                {
                    diagnostics.Add($"Dungeon transition marker '{portal.Id}' is invalid: {exception.Message}");
                }
            }

            try
            {
                return new DaggerfallDungeonMapContent(geometry, doors.Select(door => door.Id), markers);
            }
            catch (ArgumentException exception)
            {
                diagnostics.Add($"Normalized dungeon map facts are invalid: {exception.Message}");
                return null;
            }
        }
        catch (JsonException exception)
        {
            diagnostics.Add($"Normalized dungeon map closure is not valid JSON: {exception.Message}");
            return null;
        }
    }

    private static DaggerfallDoorKind InvalidDoorKind(DaggerfallRdbDoorId identity, DaggerfallContentDiagnostics diagnostics)
    {
        diagnostics.Add($"Normalized RDB door '{identity}' has an unknown kind.");
        return DaggerfallDoorKind.Normal;
    }

    private static DaggerfallDoorActionSource? ReadDoorAction(JsonElement value, DaggerfallRdbDoorId identity, DaggerfallContentDiagnostics diagnostics)
    {
        if (value.ValueKind == JsonValueKind.Null) return null;
        JsonElement action = DaggerfallBaseContent.Object(value, $"normalized RDB door '{identity}' action", diagnostics);
        int axis = DaggerfallBaseContent.Integer(action, "axis", diagnostics);
        int duration = DaggerfallBaseContent.Integer(action, "duration", diagnostics);
        int magnitude = DaggerfallBaseContent.Integer(action, "magnitude", diagnostics);
        int next = DaggerfallBaseContent.Integer(action, "nextObjectOffset", diagnostics);
        int flags = DaggerfallBaseContent.Integer(action, "flags", diagnostics);
        if (axis is < byte.MinValue or > byte.MaxValue || duration is < ushort.MinValue or > ushort.MaxValue || magnitude is < ushort.MinValue or > ushort.MaxValue || flags is < byte.MinValue or > byte.MaxValue)
        {
            diagnostics.Add($"Normalized RDB door '{identity}' action values are out of source range.");
            return null;
        }
        return new((byte)axis, (ushort)duration, (ushort)magnitude, next, (byte)flags);
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
        ReadOnlyMemory<byte>? bytes = files.GetExactlyOne(manifestPath);
        if (bytes is null) { diagnostics.Add($"Generated import manifest '{manifestPath}' must occur exactly once in admitted content."); return []; }
        Dictionary<string, ContentSha256> artifacts = new(StringComparer.Ordinal);
        try
        {
            using JsonDocument document = JsonDocument.Parse(bytes.Value);
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

    private static (IReadOnlyList<NormalizedMaterial> Materials, IReadOnlyDictionary<int, NormalizedActorSprite> Sprites, NormalizedGroundContainerSprite? GroundContainerSprite) ReadDungeonMedia(
        ReadOnlyMemory<byte>? bytes,
        string publicationRoot,
        IReadOnlyDictionary<string, ContentSha256> artifacts,
        DaggerfallDefinitions definitions,
        DaggerfallContentDiagnostics diagnostics)
    {
        if (bytes is null) { diagnostics.Add("Generated dungeon media manifest is unavailable."); return ([], new Dictionary<int, NormalizedActorSprite>(), null); }
        try
        {
            using JsonDocument document = JsonDocument.Parse(bytes.Value);
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
                else materials.Add(new NormalizedMaterial(slot, texture.Path, texture.Hash, DaggerfallBaseContent.Text(material, "materialResourceId", diagnostics)));
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
                Vector2 sourceSize = GeneratedVector2(DaggerfallBaseContent.Property(actor, "sourceWorldSize", diagnostics), "actor.sourceWorldSize", diagnostics);
                if (!PositiveFinite(size) || !PositiveFinite(sourceSize)) diagnostics.Add($"Generated actor mobile '{mobileId}' has a non-positive world size.");
                (IReadOnlyDictionary<string, NormalizedSpriteState> states, IReadOnlyList<NormalizedAtlasFrame> frames) = ReadActorStates(actor, texture, size, sourceSize, mobileId, diagnostics);
                string? preferredRestState = DaggerfallBaseContent.OptionalText(actor, "preferredRestState", diagnostics);
                if (preferredRestState is not null && !states.ContainsKey(preferredRestState)) diagnostics.Add($"Generated actor mobile '{mobileId}' preferredRestState '{preferredRestState}' is not a published state.");
                IReadOnlyList<NormalizedAttackSequence> attacks = ReadAttackSequences(actor, states, mobileId, diagnostics);
                NormalizedAttackSequence? rangedAttack = ReadRangedAttackSequence(actor, states, mobileId, diagnostics);
                NormalizedActorSprite? corpse = ReadCorpse(actor, resources, publicationRoot, artifacts, mobileId, diagnostics);
                if (!sprites.TryAdd(mobileId, new NormalizedActorSprite(texture.Path, texture.Hash, texture.AtlasWidth, texture.AtlasHeight, frames, texture.Frames[0].Id, pivot, size)
                {
                    States = states,
                    PreferredRestState = preferredRestState,
                    AttackSequences = attacks,
                    RangedAttackSequence = rangedAttack,
                    Corpse = corpse,
                })) diagnostics.Add($"Generated actor media repeats mobile '{mobileId}'.");
            }
            NormalizedGroundContainerSprite? groundContainerSprite = ReadGroundContainerSprite(root, resources, diagnostics);
            return (Array.AsReadOnly(materials.OrderBy(material => material.Slot).ToArray()), new ReadOnlyDictionary<int, NormalizedActorSprite>(sprites.ToDictionary()), groundContainerSprite);
        }
        catch (JsonException exception)
        {
            diagnostics.Add($"Generated dungeon media manifest is not valid JSON: {exception.Message}");
            return ([], new Dictionary<int, NormalizedActorSprite>(), null);
        }
    }

    private static NormalizedGroundContainerSprite? ReadGroundContainerSprite(JsonElement root, IReadOnlyDictionary<string, MediaResource> resources, DaggerfallContentDiagnostics diagnostics)
    {
        const string resourceId = "sprite/texture-216-0";
        if (!resources.TryGetValue(resourceId, out MediaResource? resource) || resource.Frames.Count == 0)
        {
            diagnostics.Add($"Generated dungeon media has no ground-container billboard '{resourceId}'.");
            return null;
        }

        JsonElement billboard = DaggerfallBaseContent.Array(root, "billboards", diagnostics)
            .FirstOrDefault(value => value.ValueKind == JsonValueKind.Object
                && value.TryGetProperty("spriteResourceId", out JsonElement id)
                && id.ValueKind == JsonValueKind.String
                && id.GetString() == resourceId);
        if (billboard.ValueKind != JsonValueKind.Object)
        {
            diagnostics.Add($"Generated dungeon media has no billboard descriptor '{resourceId}'.");
            return null;
        }

        Vector2 pivot = GeneratedVector2(DaggerfallBaseContent.Property(billboard, "pivot", diagnostics), "ground-container.pivot", diagnostics);
        Vector2 size = GeneratedVector2(DaggerfallBaseContent.Property(billboard, "worldSize", diagnostics), "ground-container.worldSize", diagnostics);
        if (!PositiveFinite(size)) diagnostics.Add("Generated ground-container billboard has a non-positive world size.");
        return new NormalizedGroundContainerSprite(resource.Path, resource.Hash, resource.AtlasWidth, resource.AtlasHeight,
            resource.Frames, resource.Frames[0].Id, pivot, size);
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
                Vector2 displaySize = PositiveFinite(worldSize) && PositiveFinite(sourceWorldSize) && PositiveFinite(frameSourceWorldSize)
                    ? new Vector2(frameSourceWorldSize.X * worldSize.X / sourceWorldSize.X, frameSourceWorldSize.Y * worldSize.Y / sourceWorldSize.Y)
                    : default;
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

    private static (IReadOnlyList<NormalizedAudioClip> Audio, NormalizedClassicPresentation Presentation) ReadClassicPresentation(AdmittedFiles files, ReadOnlyMemory<byte>? bytes, string publicationRoot, IReadOnlyDictionary<string, ContentSha256> artifacts, DaggerfallContentDiagnostics diagnostics)
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
                string path = $"{publicationRoot.TrimEnd('/')}/{relativePath}";
                ContentSha256 hash = ContentHash(DaggerfallBaseContent.Text(resource, "contentDigest", diagnostics), diagnostics);
                if (!artifacts.TryGetValue(path, out ContentSha256 artifact) || artifact != hash) diagnostics.Add($"Generated classic audio '{id}' does not agree with the import manifest.");
                long byteLength = Long(resource, "byteLength", diagnostics);
                string mimeType = DaggerfallBaseContent.Text(resource, "mimeType", diagnostics);
                int sourceWidth = DaggerfallBaseContent.Integer(resource, "sourceWidth", diagnostics);
                int sourceHeight = DaggerfallBaseContent.Integer(resource, "sourceHeight", diagnostics);
                int atlasWidth = DaggerfallBaseContent.Integer(resource, "atlasWidth", diagnostics);
                int atlasHeight = DaggerfallBaseContent.Integer(resource, "atlasHeight", diagnostics);
                // WAV descriptors retain their import-manifest identity and digest in eager content,
                // but their bodies are opened from the declared Engine bundle on the first cue.
                // Everything else remains part of the eagerly admitted closure.
                bool hasExpectedBody = kind == "audio"
                    || files.GetExactlyOne(path) is ReadOnlyMemory<byte> artifactBytes && artifactBytes.Length == byteLength;
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
                else audio.Add(new NormalizedAudioClip(id, resource.Path, resource.Hash));
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
            return (Array.AsReadOnly(audio.ToArray()), new NormalizedClassicPresentation(weapons, effects)
            {
                InventoryIcons = new ReadOnlyDictionary<string, string>(icons),
                Resources = publishedResources,
            });
        }
        catch (JsonException exception)
        {
            diagnostics.Add($"Generated classic media manifest is not valid JSON: {exception.Message}");
            return ([], NormalizedClassicPresentation.Empty);
        }
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
        string[] requiredActions = ["idle", "strikeDown", "strikeDownLeft", "strikeLeft", "strikeRight", "strikeDownRight", "strikeUp"];
        if (actions.Count != requiredActions.Length || requiredActions.Any(name => !actions.ContainsKey(name))) diagnostics.Add("Classic weapons require the normalized ready and six directional attack actions.");
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
            if (!classic.Weapons.ContainsKey(resource) || !definitions.Items.TryGetValue(new DaggerfallItemId(itemId), out DaggerfallItemDefinition? item) || item.Weapon is null) diagnostics.Add($"Classic weapon visual '{itemId}' does not select the admitted weaponSprite resource.");
            if (!mappings.TryAdd(itemId, resource)) diagnostics.Add($"Classic weapon visuals repeat item '{itemId}'.");
        }
        foreach (DaggerfallItemDefinition item in definitions.Items.Values.Where(item => item.Weapon is not null))
            if (!mappings.ContainsKey(item.Id.Value)) diagnostics.Add($"Weapon '{item.Id.Value}' has no presentation mapping.");
        string unarmed = DaggerfallBaseContent.Text(presentation, "unarmedVisual", diagnostics);
        if (!classic.Weapons.ContainsKey(unarmed)) diagnostics.Add("Unarmed presentation must select admitted weaponSprite media.");
        ClassicViewmodelStyle? viewmodel = null;
        if (mappings.Count > 0)
        {
            JsonElement style = DaggerfallBaseContent.Object(DaggerfallBaseContent.Property(presentation, "viewmodel", diagnostics), "classic viewmodel", diagnostics);
            DaggerfallBaseContent.RejectDuplicateProperties(style, "classic viewmodel", diagnostics);
            int renderOrder = DaggerfallBaseContent.Integer(style, "renderOrder", diagnostics);
            viewmodel = new ClassicViewmodelStyle(renderOrder);
        }
        return classic with { CompatibleItemVisuals = new ReadOnlyDictionary<string, string>(mappings), UnarmedVisual = unarmed, Viewmodel = viewmodel };
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

    private static bool PositiveFinite(Vector2 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && value.X > 0F && value.Y > 0F;

    private static ScenarioStart ReadStart(JsonElement value, DaggerfallContentDiagnostics diagnostics)
    {
        WorldPoint position = Point(DaggerfallBaseContent.Property(value, "position", diagnostics), "startingState.position", diagnostics);
        JsonElement look = DaggerfallBaseContent.Object(DaggerfallBaseContent.Property(value, "look", diagnostics), "startingState.look", diagnostics);
        PlayerInitialLook initialLook = new(DaggerfallBaseContent.Number(look, "yawRadians", diagnostics), DaggerfallBaseContent.Number(look, "pitchRadians", diagnostics));
        return new(position, initialLook, ReadStartSite(value, diagnostics));
    }

    /// <summary>
    /// The site the scenario starts at, when it declares one.
    /// </summary>
    /// <remarks>
    /// A scenario that starts the player somewhere in the world names the location it starts them at by
    /// region and index, because the display name is not unique. The site is optional: a scenario whose
    /// start is not a location the pack publishes - a test fixture's, or a later scenario that begins in
    /// transit - simply has none, and the session then starts at no site rather than at an invented one.
    /// </remarks>
    private static DaggerfallSiteId? ReadStartSite(JsonElement value, DaggerfallContentDiagnostics diagnostics)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("site", out JsonElement site) || site.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (site.ValueKind != JsonValueKind.Object)
        {
            diagnostics.Add("startingState.site must be an object naming the location's region and index.");
            return null;
        }

        JsonElement region = DaggerfallBaseContent.Property(site, "region", diagnostics);
        JsonElement index = DaggerfallBaseContent.Property(site, "index", diagnostics);
        if (region.ValueKind != JsonValueKind.Number || !region.TryGetInt32(out int regionIndex) || regionIndex < 0
            || index.ValueKind != JsonValueKind.Number || !index.TryGetInt32(out int locationIndex) || locationIndex < 0)
        {
            diagnostics.Add("startingState.site must name a non-negative region and index.");
            return null;
        }

        return new DaggerfallSiteId(regionIndex, locationIndex);
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
    private static Vector3 ObjectVector3(JsonElement value, string name, DaggerfallContentDiagnostics diagnostics)
    {
        if (value.ValueKind != JsonValueKind.Object) { diagnostics.Add($"'{name}' must be an object with x, y and z."); return default; }
        return new(Number(DaggerfallBaseContent.Property(value, "x", diagnostics), name, diagnostics),
            Number(DaggerfallBaseContent.Property(value, "y", diagnostics), name, diagnostics),
            Number(DaggerfallBaseContent.Property(value, "z", diagnostics), name, diagnostics));
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

internal sealed class AdmittedFiles
{
    private readonly ProductContent _content;
    private AdmittedFiles(ProductContent content) => _content = content;
    internal static AdmittedFiles From(ProductContent content) => new(content ?? throw new ArgumentNullException(nameof(content)));
    internal bool ContainsExactlyOne(string path) => _content.TryReadFile(path, out _);
    internal ReadOnlyMemory<byte>? GetExactlyOne(string path) => _content.TryReadFile(path, out ProductContentFile file) ? file.Bytes : null;
}

internal sealed record ScenarioStart(WorldPoint Position, PlayerInitialLook Look, DaggerfallSiteId? Site);
internal sealed record AuthoredWorldAppearance(Color Tint, Transform Transform, bool Visible, RenderLayer Layer);
internal sealed record ContentArtifact(string Path, ContentSha256 Sha256);
internal sealed record NormalizedMaterial(uint Slot, string TexturePath, ContentSha256 TextureSha256, string MaterialResourceId = "");
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
internal sealed record NormalizedAudioClip(string Id, string Path, ContentSha256 Sha256);
internal sealed record NormalizedClassicWeaponAction(string Name, int SourceRecordOrdinal, int FrameStart, int FrameCount, string Alignment, float ScreenOffset, float FramesPerSecond, bool Loops, short SourceXOffset, short SourceYOffset)
{
    internal IReadOnlyList<int>? Sequence { get; init; }
}
internal sealed record NormalizedClassicWeapon(string ResourceId, string TexturePath, ContentSha256 TextureSha256, int AtlasWidth, int AtlasHeight, IReadOnlyList<NormalizedAtlasFrame> Frames, Vector2 Pivot, Vector2 DisplaySize, IReadOnlyList<int> Sequence, IReadOnlyDictionary<string, NormalizedClassicWeaponAction> Actions);
internal sealed record NormalizedClassicEffect(string Name, int SourceRecordOrdinal, string TexturePath, ContentSha256 TextureSha256, int AtlasWidth, int AtlasHeight, IReadOnlyList<NormalizedAtlasFrame> Frames, Vector2 Pivot, Vector2 DisplaySize, IReadOnlyList<int> Sequence, float FramesPerSecond, bool Loops);
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
    internal bool TryEffect(string name, out NormalizedClassicEffect? effect)
    {
        effect = Effects.FirstOrDefault(candidate => candidate.Name == name);
        return effect is not null;
    }
}
internal sealed record ClassicViewmodelStyle(int RenderOrder);
internal sealed record NormalizedClassicMediaResource(string Id, string Kind, string RelativePath, ContentSha256 Sha256, long ByteLength);
internal sealed record NormalizedGroundContainerSprite(string TexturePath, ContentSha256 TextureSha256, int AtlasWidth, int AtlasHeight, IReadOnlyList<NormalizedAtlasFrame> Frames, uint InitialFrameId, Vector2 Pivot, Vector2 Size);
internal sealed record NormalizedActorSprite(string TexturePath, ContentSha256 TextureSha256, int AtlasWidth, int AtlasHeight, IReadOnlyList<NormalizedAtlasFrame> Frames, uint InitialFrameId, Vector2 Pivot, Vector2 Size)
{
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
}
internal sealed class PrivateersHoldInputs(ProjectFacts project, SpatialContentArtifact spatialArtifact, ContentArtifact staticMesh, AuthoredWorldAppearance worldAppearance, PlayerInitialLook initialLook, IReadOnlyList<NormalizedMaterial> materials, IReadOnlyDictionary<long, NormalizedActorSprite> actorSprites, IReadOnlyDictionary<int, NormalizedActorSprite>? mobileSprites = null, IReadOnlyList<NormalizedAudioClip>? audio = null, NormalizedClassicPresentation? classicPresentation = null, DaggerfallSiteId? site = null, IReadOnlyList<DaggerfallRdbDoorDefinition>? doors = null, DaggerfallWorldProfileKind profileKind = DaggerfallWorldProfileKind.Dungeon, string? logicalProfileId = null, IReadOnlyList<DaggerfallSitePortal>? portals = null, IReadOnlyList<DaggerfallSiteAnchor>? anchors = null, IReadOnlyList<DaggerfallSiteLight>? lights = null, NormalizedGroundContainerSprite? groundContainerSprite = null, DaggerfallDungeonMapContent? dungeonMap = null, IReadOnlyList<DaggerfallDungeonActionDefinition>? dungeonActions = null, IReadOnlyList<DaggerfallDungeonActionModelDefinition>? dungeonActionModels = null, DaggerfallInteriorBuilding? interiorBuilding = null)
{
    internal ProjectFacts Project { get; } = project;
    internal SpatialContentArtifact SpatialArtifact { get; } = spatialArtifact;
    internal ContentArtifact StaticMesh { get; } = staticMesh;
    internal AuthoredWorldAppearance WorldAppearance { get; } = worldAppearance;
    internal PlayerInitialLook InitialLook { get; } = initialLook;
    internal IReadOnlyList<NormalizedMaterial> Materials { get; } = Array.AsReadOnly(materials.OrderBy(material => material.Slot).ToArray());
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
    /// <summary>Named landing poses for relocation. Every normalized closure exposes its declared start as <c>start</c>.</summary>
    internal IReadOnlyDictionary<string, DaggerfallSiteAnchor> Anchors { get; } = new ReadOnlyDictionary<string, DaggerfallSiteAnchor>((anchors ?? StartAnchor(project, initialLook))
        .Select(anchor => anchor.Validate())
        .ToDictionary(anchor => anchor.Id, StringComparer.Ordinal));
    /// <summary>Source-normalized dungeon lights, ordered by their stable RDB placement identity.</summary>
    internal IReadOnlyList<DaggerfallSiteLight> Lights { get; } = Array.AsReadOnly((lights ?? [])
        .Select(light => light.Validate())
        .OrderBy(light => light.Id, StringComparer.Ordinal)
        .ToArray());
    internal NormalizedGroundContainerSprite? GroundContainerSprite { get; } = groundContainerSprite;
    /// <summary>Normalized per-placement bounds, visibility samples, and source markers used by dungeon discovery; absent on non-dungeons.</summary>
    internal DaggerfallDungeonMapContent? DungeonMap { get; } = dungeonMap;
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
internal sealed record AuthoredActor(long EntityId, DaggerfallActorId ActorId, WorldPoint Position);
internal sealed record ClassicMediaResource(string Id, string Kind, string RelativePath, string Path, ContentSha256 Hash, long ByteLength, int AtlasWidth, int AtlasHeight, IReadOnlyList<NormalizedAtlasFrame> Frames, Vector2 Pivot, Vector2 DisplaySize, float? FramesPerSecond, bool? Loop, IReadOnlyList<int> Sequence);
