using System.Collections.ObjectModel;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Rusty.Engine;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>One product-wide mesh: its artifact and the material each of its slots draws.</summary>
internal sealed record DaggerfallWorldMesh(string Path, ContentSha256 Sha256, IReadOnlyDictionary<uint, string> MaterialsBySlot);

/// <summary>
/// Assembles a catalog location's exterior, building interior or dungeon from the per-block publication:
/// each block the location places, moved to its grid position, its identities given that position as the
/// site closures give them, its models drawn from the product-wide meshes with the location's textures,
/// and its collision and navigation placed beside its neighbours by whole navigation cells.
/// </summary>
/// <remarks>
/// Texture choice follows DEC-11: an exterior or interior draws each mesh texture through the published
/// climate swaps for the location's own-pixel climate, and a dungeon through its classic texture table.
/// The exterior ground is the terrain's, drawn with the archive of the climate one pixel east. A swap is
/// chosen for the summer variant: the season does not yet rebind an assembled location's materials.
/// </remarks>
internal sealed class DaggerfallLocationAssembly
{
    // Every block artifact is derived on the sites' 0.8-unit navigation grid: a map pixel (one terrain cell)
    // is eight RMB blocks or sixteen RDB blocks wide, so blocks sit whole cells apart.
    private const float RmbBlockSide = DaggerfallTerrainSurfaceBuilder.HorizontalSize / 8F;
    private const float RdbBlockSide = DaggerfallTerrainSurfaceBuilder.HorizontalSize / 16F;
    private const long RmbBlockCells = DaggerfallSiteLifecycle.NavigationCellsPerExteriorCell / 8;
    private const long RdbBlockCells = DaggerfallSiteLifecycle.NavigationCellsPerExteriorCell / 16;

    // The donor PopulationManager's bounded mobile pool, as the RMB exterior site normalizer sizes it.
    private const int PopulationBlocksPerBand = 16;
    private const int PopulationBandSize = 24;
    private const int PopulationMinimumBands = 1;
    private const int PopulationMaximumBands = 4;
    private const int PeopleFactionType = 15;
    private const int AutomapSide = 64;
    // One clear automap cell is 64 source units; its centre is 32 in.
    private const float AutomapCellMetres = RmbBlockSide / AutomapSide;

    /// <summary>Where a location receives the player through its start: the start marker, else the enter marker.</summary>
    internal const string StartAnchor = "start";

    /// <summary>A dungeon's enter marker, where the player wakes when brought inside rather than walking in.</summary>
    internal const string EnterAnchor = "enter";

    /// <summary>An exterior's landing in front of its lowest dungeon entrance, where leaving the dungeon arrives.</summary>
    internal const string DungeonEntranceAnchor = "dungeon-entrance";

    /// <summary>The prefix of an exterior's start markers, in block order: travel arrival chooses among them.</summary>
    internal const string StartMarkerAnchorPrefix = "start-marker/";

    internal static string StartMarkerAnchor(int index) => string.Create(CultureInfo.InvariantCulture, $"{StartMarkerAnchorPrefix}{index}");

    /// <summary>An exterior's landing in front of a building's door, where leaving that building arrives.</summary>
    internal static string BuildingAnchor(DaggerfallSiteBuildingId building) =>
        string.Create(CultureInfo.InvariantCulture, $"building/{building.BlockX}/{building.BlockY}/{building.Index}");

    private readonly DaggerfallDefinitions _definitions;
    private readonly Lazy<Dictionary<DaggerfallSiteId, DaggerfallSiteRecord>> _records;
    private readonly DaggerfallWorldBlocks _blocks;
    private readonly Func<DaggerfallProductMedia> _media;
    private readonly Func<IReadOnlyDictionary<string, DaggerfallWorldMesh>> _meshes;

    internal DaggerfallLocationAssembly(DaggerfallDefinitions definitions, DaggerfallWorldBlocks blocks,
        Func<DaggerfallProductMedia> media, Func<IReadOnlyDictionary<string, DaggerfallWorldMesh>> meshes)
    {
        _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
        // Streaming asks whether each location in the exterior window places an exterior on every window change.
        _records = new(() => _definitions.Locations.Records.ToDictionary(record => record.Id));
        _blocks = blocks ?? throw new ArgumentNullException(nameof(blocks));
        _media = media ?? throw new ArgumentNullException(nameof(media));
        _meshes = meshes ?? throw new ArgumentNullException(nameof(meshes));
    }

    /// <summary>
    /// Whether this profile exists: a location with exterior blocks has an exterior, a location with dungeon
    /// blocks has a dungeon, and each building its exterior places has an interior when its block declares
    /// one. Answered from the catalog and, for an interior, the publication's index; no block is read.
    /// </summary>
    internal bool Places(DaggerfallWorldProfileKey key)
    {
        if (!DaggerfallWorldProfileIds.TryParse(key.LogicalId, out DaggerfallWorldProfileKey parsed, out DaggerfallSiteBuildingId? building)
            || parsed != key || !TryRecord(key.Site, out DaggerfallSiteRecord? record)) return false;
        return key.Kind switch
        {
            DaggerfallWorldProfileKind.Exterior => record!.Exterior is { Blocks.Count: > 0 },
            DaggerfallWorldProfileKind.Dungeon => record!.DungeonBlocks.Count > 0,
            DaggerfallWorldProfileKind.Interior => record!.Exterior is { } exterior && building is { } placed
                && exterior.Buildings.ContainsKey(placed)
                && exterior.Blocks.SingleOrDefault(block => block.X == placed.BlockX && block.Y == placed.BlockY) is { } source
                && _blocks.Contains(new DaggerfallWorldBlockKey(DaggerfallWorldBlockKind.RmbInterior, source.SourceName, placed.Index)),
            _ => false,
        };
    }

    /// <summary>Assembles one profile, reading only the blocks it places. Anything the publication lacks is named.</summary>
    internal DaggerfallSiteProfile Assemble(DaggerfallWorldProfileKey key)
    {
        if (_definitions.AssembledSites is not { } presentation)
            throw new InvalidOperationException($"Profile '{key.LogicalId}' cannot be assembled: the base payload publishes no assembledSites presentation.");
        if (!DaggerfallWorldProfileIds.TryParse(key.LogicalId, out DaggerfallWorldProfileKey parsed, out DaggerfallSiteBuildingId? building) || parsed != key)
            throw new InvalidOperationException($"Profile '{key.LogicalId}' is not a location profile id.");
        if (!TryRecord(key.Site, out DaggerfallSiteRecord? record))
            throw new InvalidOperationException($"Profile '{key.LogicalId}' names location {key.Site}, which the catalog does not carry.");
        if (record!.Climate is not int climate)
            throw new InvalidOperationException($"Location {key.Site} ('{record.Name}') publishes no climate, so its textures cannot be chosen.");
        DaggerfallContentDiagnostics diagnostics = new();
        Builder builder = new(this, key, record, climate, presentation, diagnostics);
        switch (key.Kind)
        {
            case DaggerfallWorldProfileKind.Exterior: builder.Exterior(); break;
            case DaggerfallWorldProfileKind.Interior: builder.Interior(building!.Value); break;
            default: builder.Dungeon(); break;
        }

        diagnostics.ThrowIfAny();
        DaggerfallSiteProfile profile = builder.Build();
        diagnostics.ThrowIfAny();
        return profile;
    }

    private bool TryRecord(DaggerfallSiteId site, out DaggerfallSiteRecord? record) => _records.Value.TryGetValue(site, out record);

    /// <summary>
    /// Reads the world media publication's mesh index: each mesh artifact with its digest and the material
    /// its slots draw.
    /// </summary>
    internal static IReadOnlyDictionary<string, DaggerfallWorldMesh> ReadMeshIndex(ProductContent content, DaggerfallWorldMedia product)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(product);
        const string indexPath = DaggerfallWorldMedia.Root + "/geometry/index.json";
        DaggerfallContentDiagnostics diagnostics = new();
        if (!content.TryReadFile(indexPath, out ProductContentFile file))
        {
            diagnostics.Add($"The world media publication carries no mesh index '{indexPath}'.");
            throw diagnostics.Exception();
        }

        Dictionary<string, DaggerfallWorldMesh> meshes = new(StringComparer.Ordinal);
        try
        {
            using JsonDocument document = JsonDocument.Parse(file.Bytes);
            foreach (JsonElement mesh in DaggerfallBaseContent.Array(document.RootElement, "meshes", diagnostics))
            {
                string id = DaggerfallBaseContent.Text(mesh, "artifactId", diagnostics);
                string path = $"{DaggerfallWorldMedia.Root}/{DaggerfallBaseContent.Text(mesh, "relativePath", diagnostics)}";
                ContentSha256 hash = DaggerfallContentHash.Parse(DaggerfallBaseContent.Text(mesh, "contentDigest", diagnostics), $"World media mesh '{id}'");
                if (!product.Shared.TryGetValue(path, out ContentSha256 admitted) || admitted != hash)
                    diagnostics.Add($"World media mesh '{id}' does not agree with the world media manifest.");
                Dictionary<uint, string> slots = [];
                foreach (JsonElement slot in DaggerfallBaseContent.Array(mesh, "materialSlots", diagnostics))
                {
                    int number = DaggerfallBaseContent.Integer(slot, "slot", diagnostics);
                    if (number < 0 || !slots.TryAdd((uint)number, DaggerfallBaseContent.Text(slot, "material", diagnostics)))
                        diagnostics.Add($"World media mesh '{id}' repeats or misnumbers material slot {number}.");
                }

                if (!meshes.TryAdd(id, new(path, hash, slots))) diagnostics.Add($"World media mesh index repeats '{id}'.");
            }
        }
        catch (JsonException exception) { diagnostics.Add($"World media mesh index is not valid JSON: {exception.Message}"); }

        diagnostics.ThrowIfAny();
        return meshes;
    }

    /// <summary>One profile's assembly: its geometry, material table and normalized world sections.</summary>
    private sealed class Builder(DaggerfallLocationAssembly owner, DaggerfallWorldProfileKey key, DaggerfallSiteRecord record, int climate,
        DaggerfallAssembledSiteDefinition presentation, DaggerfallContentDiagnostics diagnostics)
    {
        private readonly DaggerfallProductMedia _media = owner._media();
        private readonly IReadOnlyDictionary<string, DaggerfallWorldMesh> _meshes = owner._meshes();
        private readonly List<DaggerfallSiteSpatialPart> _spatial = [];
        private readonly List<(string Path, ContentSha256 Sha256, Transform Pose, IReadOnlyDictionary<uint, string> Materials)> _placedMeshes = [];
        private readonly SortedSet<string> _materials = new(StringComparer.Ordinal);
        private readonly List<DaggerfallRdbDoorDefinition> _doors = [];
        private readonly List<DaggerfallDungeonActionModelDefinition> _actionModels = [];
        private readonly List<DaggerfallDungeonMapGeometry> _mapGeometry = [];
        private readonly JsonObject _world = new()
        {
            ["lights"] = new JsonArray(),
            ["population"] = new JsonArray(),
            ["questMarkers"] = new JsonArray(),
            ["staticNpcs"] = new JsonArray(),
            ["propertyContainers"] = new JsonArray(),
            ["actions"] = new JsonArray(),
            ["ambientZones"] = new JsonArray(),
        };
        private IReadOnlyDictionary<int, int>? _remaps;
        private Vector3? _start;
        private Vector3? _enter;
        private readonly List<DaggerfallSitePortal> _portals = [];
        private readonly Dictionary<string, DaggerfallSiteAnchor> _anchors = new(StringComparer.Ordinal);
        private readonly List<Vector3> _startMarkers = [];
        private readonly List<DaggerfallWorldBlockTransitionDoor> _dungeonExits = [];
        private DaggerfallWorldBlockTransitionDoor? _lowestEntrance;

        /// <summary>A location's exterior: every RMB block of its grid, in the site closure's block order.</summary>
        internal void Exterior()
        {
            DaggerfallSiteExterior exterior = record.Exterior!;
            DaggerfallSiteBlock[] placed = [.. exterior.Blocks.OrderBy(block => block.X).ThenBy(block => block.Y).ThenBy(block => block.SourceName, StringComparer.Ordinal)];
            IReadOnlyDictionary<DaggerfallWorldBlockKey, DaggerfallWorldBlockDocument> blocks = owner._blocks.Read(
                placed.Select(block => new DaggerfallWorldBlockKey(DaggerfallWorldBlockKind.RmbExterior, block.SourceName)));
            HashSet<(int X, int Z)> clear = [];
            foreach (DaggerfallSiteBlock block in placed)
            {
                DaggerfallWorldBlockDocument document = blocks[new(DaggerfallWorldBlockKind.RmbExterior, block.SourceName)];
                Vector3 origin = new(block.X * RmbBlockSide, 0F, -(block.Y * RmbBlockSide));
                Place(document, $"block/{block.X}/{block.Y}", block.X * RmbBlockCells, -(block.Y * RmbBlockCells));
                HashSet<string> gates = [.. document.Gates.Select(gate => gate.ModelId)];
                foreach (DaggerfallWorldBlockModel model in document.Models.Where(model => !gates.Contains(model.Id))) Draw(model, origin);
                foreach (DaggerfallWorldBlockDoor door in document.Doors) ExteriorDoor(door, block, origin);
                foreach (DaggerfallWorldBlockTransitionDoor door in document.TransitionDoors)
                    DungeonEntrance(door, origin, id => PlaceId(id, block.X, block.Y));
                if (document.StartMarker is { } start) _start ??= origin + start;
                if (document.EnterMarker is { } enter) _enter ??= origin + enter;
                _startMarkers.AddRange(document.StartMarkers.Select(marker => origin + marker));
                foreach (JsonNode? person in document.Section("population")) Append("population", Moved(person, origin));
                if (document.ClearGround is { } bits)
                    for (int index = 0; index < AutomapSide * AutomapSide; index++)
                        if ((bits[index / 8] & (1 << (index % 8))) != 0)
                            clear.Add(((block.X * AutomapSide) + (index % AutomapSide), (block.Y * AutomapSide) + (index / AutomapSide)));
            }

            DynamicPopulation(placed, clear);
        }

        /// <summary>One building's inside half, in the frame its block builds it in.</summary>
        internal void Interior(DaggerfallSiteBuildingId building)
        {
            DaggerfallSiteExterior exterior = record.Exterior!;
            DaggerfallSiteBlock placed = exterior.Blocks.SingleOrDefault(block => block.X == building.BlockX && block.Y == building.BlockY)
                ?? throw new InvalidOperationException($"Location {key.Site} places no block at ({building.BlockX}, {building.BlockY}).");
            DaggerfallWorldBlockKey blockKey = new(DaggerfallWorldBlockKind.RmbInterior, placed.SourceName, building.Index);
            DaggerfallWorldBlockDocument document = owner._blocks.Read([blockKey])[blockKey];
            Place(document, "block", 0, 0);
            foreach (DaggerfallWorldBlockModel model in document.Models) Draw(model, Vector3.Zero);
            _start = document.StartMarker;
            _enter = document.EnterMarker;
            // A building door seen from inside leads back out; without an entrance to return through, the
            // player comes out in front of this building's door (BuildingTransitionExteriorLogic).
            foreach (DaggerfallWorldBlockTransitionDoor door in document.TransitionDoors.Where(door => door.Kind == DaggerfallWorldBlockTransitionKind.BuildingExit))
                Portal(door.Id, door.Position, DaggerfallWorldProfileIds.Exterior(key.Site), BuildingAnchor(building));
            foreach (JsonNode? marker in document.Section("questMarkers"))
            {
                JsonObject value = Clone(marker);
                value["sourceKey"] = placed.SourceName;
                value["buildingIndex"] = building.Index;
                value["blockX"] = 0;
                value["blockZ"] = 0;
                Append("questMarkers", value);
            }

            // The classic name seed folds the building's grid key and the location index into the source offset.
            int buildingKey = (building.BlockX << 16) + (building.BlockY << 8) + building.Index;
            if (buildingKey == 0) buildingKey = 1 << 24;
            foreach (JsonNode? person in document.Section("staticNpcs"))
            {
                JsonObject value = Clone(person);
                int offset = value["sourceOffset"]?.GetValue<int>() ?? Missing("staticNpcs sourceOffset");
                value.Remove("sourceOffset");
                value["nameSeed"] = offset ^ (buildingKey + key.Site.Index);
                Append("staticNpcs", value);
            }

            foreach (JsonNode? container in document.Section("propertyContainers")) Append("propertyContainers", Clone(container));
            DaggerfallWorldBlockBuilding slot = document.InteriorBuilding!;
            _world["interiorBuilding"] = new JsonObject
            {
                ["blockX"] = building.BlockX,
                ["blockY"] = building.BlockY,
                ["sourceKey"] = placed.SourceName,
                ["buildingIndex"] = building.Index,
                ["buildingType"] = slot.BuildingType,
                ["factionId"] = slot.FactionId,
            };
        }

        /// <summary>A location's dungeon: every RDB block placement, with the dungeon's texture table and start block.</summary>
        internal void Dungeon()
        {
            if (record.DungeonTextureRemaps.Count == 0)
                diagnostics.Add($"Dungeon of location {key.Site} ('{record.Name}') publishes no texture table, so its textures cannot be chosen.");
            _remaps = record.DungeonTextureRemaps;
            DaggerfallSiteDungeonBlock[] placed = [.. record.DungeonBlocks.OrderBy(block => block.X).ThenBy(block => block.Z).ThenBy(block => block.SourceKey, StringComparer.Ordinal)];
            IReadOnlyDictionary<DaggerfallWorldBlockKey, DaggerfallWorldBlockDocument> blocks = owner._blocks.Read(
                placed.Select(block => new DaggerfallWorldBlockKey(DaggerfallWorldBlockKind.Rdb, block.SourceKey)));
            float minimumY = float.PositiveInfinity, maximumY = float.NegativeInfinity;
            List<(string Id, string Kind, int X, int Z)> zones = [];
            foreach (DaggerfallSiteDungeonBlock block in placed)
            {
                DaggerfallWorldBlockDocument document = blocks[new(DaggerfallWorldBlockKind.Rdb, block.SourceKey)];
                Vector3 origin = new(block.X * RdbBlockSide, 0F, -(block.Z * RdbBlockSide));
                string Placed(string id) => PlaceId(id, block.X, block.Z);
                Place(document, $"block/{block.X}/{block.Z}", block.X * RdbBlockCells, -(block.Z * RdbBlockCells));
                foreach (DaggerfallWorldBlockModel model in document.Models)
                {
                    if (model.Action is null && model.DoorId is null) Draw(model, origin, Placed);
                    if (model.Action is { } action) ActionModel(model, action, origin, Placed);
                }

                foreach (DaggerfallWorldBlockDoor door in document.Doors) DungeonDoor(door, document, origin, Placed);
                // A dungeon exit leads out to the location's exterior, in front of its dungeon entrance.
                foreach (DaggerfallWorldBlockTransitionDoor door in document.TransitionDoors.Where(door => door.Kind == DaggerfallWorldBlockTransitionKind.DungeonExit))
                {
                    DaggerfallWorldBlockTransitionDoor exit = Moved(door, origin, Placed);
                    _dungeonExits.Add(exit);
                    if (record.Exterior is { Blocks.Count: > 0 })
                        Portal(exit.Id, exit.Position, DaggerfallWorldProfileIds.Exterior(key.Site), DungeonEntranceAnchor);
                }
                foreach (JsonNode? light in document.Section("lights")) Append("lights", Moved(light, origin, Placed));
                foreach (JsonNode? action in document.Section("actions"))
                {
                    JsonObject value = Moved(action, origin, Placed);
                    foreach (string reference in new[] { "nextActionId", "doorId" })
                        if (value[reference] is JsonValue target && target.TryGetValue(out string? id)) value[reference] = Placed(id);
                    Append("actions", value);
                }

                foreach (JsonNode? marker in document.Section("questMarkers"))
                {
                    JsonObject value = Moved(marker, origin, Placed);
                    value["sourceKey"] = block.SourceKey;
                    value["buildingIndex"] = null;
                    value["blockX"] = block.X;
                    value["blockZ"] = block.Z;
                    Append("questMarkers", value);
                }

                if (block.Start)
                {
                    if (document.StartMarker is { } start) _start ??= origin + start;
                    if (document.EnterMarker is { } enter) _enter ??= origin + enter;
                }

                // An ambient area spans the dungeon's full height: every placed model, moving ones included.
                if (document.Bounds is { } extent)
                {
                    minimumY = MathF.Min(minimumY, extent.Minimum.Y);
                    maximumY = MathF.Max(maximumY, extent.Maximum.Y);
                }

                if (document.AmbientZone is { } zone)
                    zones.Add(($"ambient/{Scope(document)}/{block.X}/{block.Z}/{zone.ToLowerInvariant()}", zone, block.X, block.Z));
            }

            foreach ((string id, string kind, int x, int z) in zones)
                Append("ambientZones", new JsonObject
                {
                    ["id"] = id,
                    ["kind"] = kind,
                    ["bounds"] = new JsonObject
                    {
                        ["minimum"] = Vector(new Vector3(x * RdbBlockSide, minimumY, -((z + 1) * RdbBlockSide))),
                        ["maximum"] = Vector(new Vector3((x + 1) * RdbBlockSide, maximumY, -(z * RdbBlockSide))),
                    },
                });
        }

        internal DaggerfallSiteProfile Build()
        {
            // One profile slot per drawn texture, in a stable order.
            Dictionary<string, uint> slots = [];
            List<NormalizedMaterial> materials = [];
            foreach (string material in _materials)
            {
                if (!_media.Textures.TryGetValue(material, out ContentArtifact? texture))
                {
                    diagnostics.Add($"Profile '{key.LogicalId}' draws texture '{material}', which the world media publication does not carry.");
                    continue;
                }

                uint slot = checked((uint)materials.Count);
                slots.Add(material, slot);
                materials.Add(new NormalizedMaterial(slot, texture.Path, texture.Sha256, material));
            }

            DaggerfallMeshMaterialBinding[] Bindings(IReadOnlyDictionary<uint, string> mesh) =>
                [.. mesh.Where(pair => slots.ContainsKey(pair.Value)).OrderBy(pair => pair.Key)
                    .Select(pair => new DaggerfallMeshMaterialBinding(pair.Key, slots[pair.Value]))];
            List<DaggerfallSiteMesh> meshes = [.. _placedMeshes.Select(mesh => new DaggerfallSiteMesh(mesh.Path, mesh.Sha256, mesh.Pose, Bindings(mesh.Materials)))];
            DaggerfallRdbDoorDefinition[] doors = [.. _doors.Select(door => door.Visual is { } visual
                ? door with { Visual = visual with { Materials = Bindings(Visual(visual)) } }
                : door)];
            DaggerfallDungeonActionModelDefinition[] actionModels = [.. _actionModels.Select(model =>
                model with { Visual = model.Visual with { Materials = Bindings(Visual(model.Visual)) } })];

            // A site closure's people and furniture are canonically ordered by identity.
            foreach (string section in new[] { "staticNpcs", "propertyContainers" })
            {
                JsonNode?[] sorted = [.. ((JsonArray)_world[section]!).OrderBy(entry => entry?["id"]?.GetValue<string>(), StringComparer.Ordinal)
                    .Select(entry => entry?.DeepClone())];
                _world[section] = new JsonArray(sorted);
            }

            byte[] normalized = System.Text.Encoding.UTF8.GetBytes(new JsonObject { ["world"] = _world.DeepClone() }.ToJsonString());
            IReadOnlyList<DaggerfallDungeonActionDefinition> actions = DaggerfallSiteContent.ReadNormalizedActions(normalized, doors, diagnostics);
            HashSet<string> actionIds = [.. actions.Select(action => action.Id)];
            foreach (DaggerfallDungeonActionModelDefinition model in actionModels.Where(model => !actionIds.Contains(model.ActionId)))
                diagnostics.Add($"Assembled action model '{model.ActionId}' has no matching world action node.");
            Vector3? start = key.Kind == DaggerfallWorldProfileKind.Interior ? _enter ?? _start : _start ?? _enter;
            PlayerInitialLook look = new(0F, 0F);
            // Entering a dungeon faces the player away from the exit nearest its start (TransitionDungeonInterior).
            if (key.Kind == DaggerfallWorldProfileKind.Dungeon && start is { } landing
                && _dungeonExits.OrderBy(exit => Vector3.DistanceSquared(exit.Position, landing)).FirstOrDefault() is { } nearest)
                look = new(ActorHeading.Yaw(nearest.Normal), 0F);
            WorldPoint? position = start is { } at ? new WorldPoint(at.X, at.Y, at.Z) : null;
            if (position is { } startAnchor) Anchor(StartAnchor, startAnchor, look.YawRadians);
            if (key.Kind == DaggerfallWorldProfileKind.Dungeon && _enter is { } enterMarker)
                Anchor(EnterAnchor, new WorldPoint(enterMarker.X, enterMarker.Y, enterMarker.Z), look.YawRadians);
            for (int index = 0; index < _startMarkers.Count; index++)
                Anchor(StartMarkerAnchor(index), new WorldPoint(_startMarkers[index].X, _startMarkers[index].Y, _startMarkers[index].Z), 0F);
            // Leaving the dungeon lands outside its lowest entrance, facing away from it (PositionPlayerToDungeonExit).
            if (_lowestEntrance is { } entrance)
                Anchor(DungeonEntranceAnchor, Landing(entrance.Position with { Y = entrance.Bounds.Minimum.Y }, entrance.Normal, presentation.DungeonExitLanding),
                    ActorHeading.Yaw(entrance.Normal));
            DaggerfallDungeonMapContent? map = null;
            if (key.Kind == DaggerfallWorldProfileKind.Dungeon)
            {
                try
                {
                    List<DaggerfallSiteMarker> markers = [];
                    if (_enter is { } enter) markers.Add(new DaggerfallSiteMarker("marker/enter", DaggerfallSiteMarkerKind.Entrance, new WorldPoint(enter.X, enter.Y, enter.Z)));
                    map = new DaggerfallDungeonMapContent(_mapGeometry, doors.Select(door => door.Id), markers);
                }
                catch (ArgumentException exception) { diagnostics.Add($"Assembled dungeon map of '{key.LogicalId}' is invalid: {exception.Message}"); }
            }

            DaggerfallSiteGeometry geometry = new(presentation.NavigationGridId, _spatial, meshes, owner._blocks.Open);
            return new DaggerfallSiteProfile(
                new ProjectFacts(position, new Dictionary<long, AuthoredActor>()),
                geometry,
                presentation.Appearance,
                look,
                materials,
                new Dictionary<long, NormalizedActorSprite>(),
                _media.MobileSprites,
                _media.Audio,
                _media.ClassicPresentation,
                key.Site,
                doors,
                key.Kind,
                key.LogicalId,
                _portals,
                [.. _anchors.Values],
                DaggerfallSiteContent.ReadNormalizedLights(normalized, diagnostics),
                _media.GroundContainerSprite,
                map,
                actions,
                actionModels,
                ReadInterior(normalized),
                _media.Music,
                DaggerfallQuestMarkerContent.ReadWorld(normalized, diagnostics),
                _media.BillboardSprites,
                DaggerfallStaticNpcPlacement.Read(normalized, _media.BillboardSprites, owner._definitions, key.Site, diagnostics),
                terrainTextures: _media.TerrainTextures,
                population: DaggerfallSiteContent.ReadNormalizedPopulation(normalized, diagnostics))
            {
                AmbientZones = DaggerfallAmbientZones.Read(normalized, diagnostics),
                PropertyContainers = DaggerfallPropertyContainerPlacement.Read(normalized),
            };
        }

        private DaggerfallInteriorBuilding? ReadInterior(byte[] normalized)
        {
            try { return DaggerfallInteriorBuilding.Read(normalized, key.Kind); }
            catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException or KeyNotFoundException)
            {
                diagnostics.Add($"Assembled interior building of '{key.LogicalId}' is malformed: {exception.Message}");
                return null;
            }
        }

        // A door or action model visual carries its mesh's material table until the profile slots are known.
        private readonly Dictionary<string, IReadOnlyDictionary<uint, string>> _visualMaterials = new(StringComparer.Ordinal);

        private IReadOnlyDictionary<uint, string> Visual(DaggerfallDoorVisual visual) => _visualMaterials[visual.Path];

        private void Place(DaggerfallWorldBlockDocument document, string id, long column, long row)
        {
            if (document.Spatial is not { } spatial)
            {
                diagnostics.Add($"Block '{document.PublishedKey}' has no collision and navigation artifact to place.");
                return;
            }

            _spatial.Add(new DaggerfallSiteSpatialPart(id, spatial.ContentPath, spatial.Sha256, column, row));
        }

        /// <summary>Draws one static model: its mesh posed in the profile frame with the location's textures.</summary>
        private void Draw(DaggerfallWorldBlockModel model, Vector3 origin, Func<string, string>? place = null)
        {
            if (Mesh(model) is not { } mesh) return;
            Vector3 position = origin + model.Position;
            _placedMeshes.Add((mesh.Path, mesh.Sha256, DaggerfallSiteGeometry.Pose(position, model.RotationDegrees), Textures(mesh)));
            if (key.Kind != DaggerfallWorldProfileKind.Dungeon) return;
            if (model.Bounds is not { } bounds)
            {
                diagnostics.Add($"Static block model '{model.Id}' states no map bounds.");
                return;
            }

            try
            {
                _mapGeometry.Add(new DaggerfallDungeonMapGeometry(place!(model.Id), bounds.Minimum + origin, bounds.Maximum + origin,
                    [model.MeshArtifactId!], [.. model.SamplePoints.Select(point => point + origin)], null).Validate());
            }
            catch (ArgumentException exception) { diagnostics.Add($"Block model '{model.Id}' has invalid map facts: {exception.Message}"); }
        }

        /// <summary>
        /// The mesh a model places, or null when the archive cannot serve it: the publication states why, and
        /// no geometry stands in for it.
        /// </summary>
        private DaggerfallWorldMesh? Mesh(DaggerfallWorldBlockModel model)
        {
            if (model.MeshArtifactId is not { } artifact) return null;
            if (_meshes.TryGetValue(artifact, out DaggerfallWorldMesh? mesh)) return mesh;
            diagnostics.Add($"Block model '{model.Id}' places mesh '{artifact}', which the world media publication does not carry.");
            return null;
        }

        /// <summary>A mesh's slots with the textures this location draws them with, each recorded for the material table.</summary>
        private IReadOnlyDictionary<uint, string> Textures(DaggerfallWorldMesh mesh)
        {
            Dictionary<uint, string> textures = [];
            foreach ((uint slot, string material) in mesh.MaterialsBySlot)
            {
                string drawn = Texture(material);
                textures.Add(slot, drawn);
                _materials.Add(drawn);
            }

            return textures;
        }

        private string Texture(string material)
        {
            const string prefix = "material/texture-";
            string[] parts = material.StartsWith(prefix, StringComparison.Ordinal) ? material[prefix.Length..].Split('-') : [];
            if (parts is not [string archiveText, string recordText]
                || !int.TryParse(archiveText, NumberStyles.None, CultureInfo.InvariantCulture, out int archive)
                || !int.TryParse(recordText, NumberStyles.None, CultureInfo.InvariantCulture, out int recordIndex))
            {
                diagnostics.Add($"World media material '{material}' is not a 'material/texture-<archive>-<record>' identity.");
                return material;
            }

            int drawn = _remaps is { } table
                ? table.GetValueOrDefault(archive, archive)
                : owner._definitions.Grids.Climate.SwapArchive(archive, recordIndex, climate, DaggerfallClimateSeason.Summer);
            return string.Create(CultureInfo.InvariantCulture, $"{prefix}{drawn}-{recordIndex}");
        }

        private void ExteriorDoor(DaggerfallWorldBlockDoor door, DaggerfallSiteBlock block, Vector3 origin)
        {
            string id = PlaceId(door.Id, block.X, block.Y);
            if (!DaggerfallSiteContent.TryDoorIdentity(id, out DaggerfallRdbDoorId identity))
            {
                diagnostics.Add($"Block door '{door.Id}' does not place to a door identity.");
                return;
            }

            if (door.CollisionBounds is not { } bounds)
            {
                diagnostics.Add($"Exterior door '{id}' states no collision bounds.");
                return;
            }

            try
            {
                // The building's mesh draws the door plane; an exterior door is entered, not swung.
                _doors.Add(new DaggerfallRdbDoorDefinition(identity, origin + door.Position, door.RotationDegrees, bounds.Minimum, bounds.Maximum,
                    door.Kind, door.StartingLockValue, Action: door.Action)
                {
                    LockSurface = DaggerfallSiteContent.DoorSurface(DaggerfallWorldProfileKind.Exterior),
                    ExteriorBuilding = door.BuildingIndex is int building ? new DaggerfallSiteBuildingId(block.X, block.Y, building) : null,
                }.Validate());
            }
            catch (ArgumentException exception) { diagnostics.Add($"Exterior door '{id}' is invalid: {exception.Message}"); }

            // Coming out of the building without an entrance to return through lands in front of its first door,
            // on its threshold: the door's yaw is its plane normal's, which faces out.
            if (door.BuildingIndex is int index)
            {
                float yaw = door.RotationDegrees.Y * (MathF.PI / 180F);
                Vector3 normal = new(MathF.Sin(yaw), 0F, MathF.Cos(yaw));
                Vector3 threshold = origin + door.Position + (Vector3.UnitY * bounds.Minimum.Y);
                string anchor = BuildingAnchor(new DaggerfallSiteBuildingId(block.X, block.Y, index));
                if (!_anchors.ContainsKey(anchor))
                    Anchor(anchor, Landing(threshold, normal, presentation.BuildingExitLanding), ActorHeading.Yaw(normal));
            }
        }

        /// <summary>A dungeon entrance on the exterior leads into the location's dungeon, landing at its start.</summary>
        private void DungeonEntrance(DaggerfallWorldBlockTransitionDoor door, Vector3 origin, Func<string, string> place)
        {
            if (door.Kind != DaggerfallWorldBlockTransitionKind.DungeonEntrance) return;
            DaggerfallWorldBlockTransitionDoor entrance = Moved(door, origin, place);
            if (_lowestEntrance is null || entrance.Position.Y < _lowestEntrance.Position.Y) _lowestEntrance = entrance;
            // A location without dungeon blocks has nowhere for its entrance to lead.
            if (record.DungeonBlocks.Count != 0) Portal(entrance.Id, entrance.Position, DaggerfallWorldProfileIds.Dungeon(key.Site), StartAnchor);
        }

        private void Portal(string id, Vector3 position, DaggerfallWorldProfileKey destination, string arrival)
        {
            try { _portals.Add(new DaggerfallSitePortal(id, new WorldPoint(position.X, position.Y, position.Z), presentation.DoorReach, destination.LogicalId, arrival).Validate()); }
            catch (ArgumentException exception) { diagnostics.Add($"Transition door '{id}' is invalid: {exception.Message}"); }
        }

        private void Anchor(string id, WorldPoint position, float yaw)
        {
            try { _anchors[id] = new DaggerfallSiteAnchor(id, position, yaw, 0F).Validate(); }
            catch (ArgumentException exception) { diagnostics.Add($"Landing '{id}' of '{key.LogicalId}' is invalid: {exception.Message}"); }
        }

        private static WorldPoint Landing(Vector3 threshold, Vector3 normal, float distance)
        {
            Vector3 at = threshold + (Vector3.Normalize(normal with { Y = 0F }) * distance);
            return new WorldPoint(at.X, at.Y, at.Z);
        }

        private static DaggerfallWorldBlockTransitionDoor Moved(DaggerfallWorldBlockTransitionDoor door, Vector3 origin, Func<string, string> place) =>
            door with { Id = place(door.Id), Position = origin + door.Position, Bounds = (origin + door.Bounds.Minimum, origin + door.Bounds.Maximum) };

        private void DungeonDoor(DaggerfallWorldBlockDoor door, DaggerfallWorldBlockDocument document, Vector3 origin, Func<string, string> place)
        {
            string id = place(door.Id);
            if (!DaggerfallSiteContent.TryDoorIdentity(id, out DaggerfallRdbDoorId identity))
            {
                diagnostics.Add($"Block door '{door.Id}' does not place to a door identity.");
                return;
            }

            if (door.RotationDegrees.X != 0F || door.RotationDegrees.Z != 0F)
            {
                diagnostics.Add($"Dungeon door '{id}' must have a yaw-only rotation.");
                return;
            }

            DaggerfallWorldBlockModel? model = document.Models.SingleOrDefault(candidate => candidate.Id == door.ModelId);
            if (model is null || Mesh(model) is not { } mesh || model.LocalBounds is not { } local)
            {
                diagnostics.Add($"Dungeon door '{id}' has no placed mesh and model-local bounds to swing.");
                return;
            }

            DaggerfallDoorVisual visual = new(mesh.Path, mesh.Sha256, [new DaggerfallMeshMaterialBinding(0, 0)]);
            (Vector3 Minimum, Vector3 Maximum) bounds = local;
            if (model.Action is null)
            {
                // The door runtime swings a door about its closed pose, which is not the model's own turn: its
                // collider and visual live in the door frame, so the model frame is turned into it, as a site
                // closure writes its door visual.
                Quaternion toDoor = Quaternion.Normalize(Quaternion.Inverse(DaggerfallDoorPose.ClosedRotation(door.RotationDegrees))
                    * DaggerfallSiteGeometry.SourceRotation(model.RotationDegrees));
                bounds = Turned(local, toDoor);
                visual = visual with { LocalPose = new Transform(Vector3.Zero, toDoor, Vector3.One) };
            }

            _visualMaterials[mesh.Path] = Textures(mesh);
            try
            {
                _doors.Add(new DaggerfallRdbDoorDefinition(identity, origin + door.Position, door.RotationDegrees, bounds.Minimum, bounds.Maximum,
                    door.Kind, door.StartingLockValue, Visual: visual, Action: door.Action)
                {
                    LockSurface = DaggerfallSiteContent.DoorSurface(DaggerfallWorldProfileKind.Dungeon),
                }.Validate());
            }
            catch (ArgumentException exception) { diagnostics.Add($"Dungeon door '{id}' is invalid: {exception.Message}"); }
        }

        /// <summary>The bounds of a turned box: its eight corners turned, then bounded.</summary>
        private static (Vector3 Minimum, Vector3 Maximum) Turned((Vector3 Minimum, Vector3 Maximum) box, Quaternion turn)
        {
            Vector3 minimum = new(float.PositiveInfinity), maximum = new(float.NegativeInfinity);
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = Vector3.Transform(new Vector3(
                    (corner & 1) == 0 ? box.Minimum.X : box.Maximum.X,
                    (corner & 2) == 0 ? box.Minimum.Y : box.Maximum.Y,
                    (corner & 4) == 0 ? box.Minimum.Z : box.Maximum.Z), turn);
                minimum = Vector3.Min(minimum, point);
                maximum = Vector3.Max(maximum, point);
            }

            return (minimum, maximum);
        }

        private void ActionModel(DaggerfallWorldBlockModel model, DaggerfallWorldBlockModelAction action, Vector3 origin, Func<string, string> place)
        {
            if (Mesh(model) is not { } mesh) return;
            if (model.LocalBounds is not { } bounds)
            {
                diagnostics.Add($"Action model '{model.Id}' states no model-local bounds.");
                return;
            }

            string? doorId = model.DoorId is { } door ? place(door) : null;
            DaggerfallRdbDoorId? doorIdentity = null;
            if (doorId is not null)
            {
                if (DaggerfallSiteContent.TryDoorIdentity(doorId, out DaggerfallRdbDoorId parsed)) doorIdentity = parsed;
                else diagnostics.Add($"Action model '{model.Id}' names door '{doorId}', which is not a door identity.");
            }

            _visualMaterials[mesh.Path] = Textures(mesh);
            try
            {
                _actionModels.Add(new DaggerfallDungeonActionModelDefinition(place(action.ActionId), doorId, doorIdentity, action.Description,
                    action.ModelIndex, action.RawIndex, new DaggerfallDoorVisual(mesh.Path, mesh.Sha256, [new DaggerfallMeshMaterialBinding(0, 0)]),
                    DaggerfallDungeonMotionPolicy.InitialTransform(origin + model.Position, model.RotationDegrees), bounds.Minimum, bounds.Maximum,
                    model.CollisionVertices, model.CollisionTriangles).Validate());
            }
            catch (ArgumentException exception) { diagnostics.Add($"Action model '{model.Id}' is invalid: {exception.Message}"); }
        }

        /// <summary>
        /// The donor PopulationManager's bounded mobile pool over the location's clear outdoor cells, as the RMB
        /// exterior site normalizer places it: evenly sampled from the sorted pool, dressed from the region's
        /// People faction.
        /// </summary>
        private void DynamicPopulation(IReadOnlyList<DaggerfallSiteBlock> placed, IReadOnlySet<(int X, int Z)> clear)
        {
            IReadOnlyCollection<DaggerfallFactionDefinition> factions = owner._definitions.Factions.Factions.Values.ToArray();
            DaggerfallFactionDefinition? people = factions
                .Where(faction => faction.Type == PeopleFactionType && faction.Region == key.Site.Region && faction.Flats.Count != 0)
                .OrderBy(faction => faction.Id).FirstOrDefault()
                ?? factions.Where(faction => faction.Type == PeopleFactionType && faction.Flats.Count != 0).OrderBy(faction => faction.Id).FirstOrDefault();
            if (people is null) return;
            Dictionary<(int X, int Y), DaggerfallSiteBlock> blocks = placed.ToDictionary(block => (block.X, block.Y));
            int bands = Math.Clamp(placed.Count / PopulationBlocksPerBand, PopulationMinimumBands, PopulationMaximumBands);
            (int X, int Z)[] cells = [.. clear.Where(cell => blocks.ContainsKey((cell.X / AutomapSide, cell.Z / AutomapSide)))
                .OrderBy(cell => cell.X).ThenBy(cell => cell.Z)];
            int count = Math.Min(bands * PopulationBandSize, cells.Length);
            string location = $"{Slug(record.Name)}-{key.Site.Region}-{key.Site.Index}";
            for (int index = 0; index < count; index++)
            {
                (int X, int Z) cell = cells[(int)((long)index * cells.Length / count)];
                DaggerfallSiteBlock block = blocks[(cell.X / AutomapSide, cell.Z / AutomapSide)];
                int sourceX = cell.X - (block.X * AutomapSide);
                int sourceY = AutomapSide - 1 - (cell.Z - (block.Y * AutomapSide));
                // The block origin and the cell centre in source units, as the site places them, then flipped.
                float x = (block.X * RmbBlockSide) + (((sourceX * AutomapSide) + (AutomapSide / 2)) * (AutomapCellMetres / AutomapSide));
                float z = (block.Y * RmbBlockSide) + (((sourceY * AutomapSide) + (AutomapSide / 2)) * (AutomapCellMetres / AutomapSide));
                string id = string.Create(CultureInfo.InvariantCulture, $"population/{location}/dynamic/{index:D3}");
                int flat = people.Flats[index % people.Flats.Count];
                Append("population", new JsonObject
                {
                    ["id"] = id,
                    ["position"] = Vector(new Vector3(x, 0F, -z)),
                    ["billboardArchive"] = flat >> 7,
                    ["billboardRecord"] = flat & 0x7f,
                    ["factionId"] = people.Id,
                    ["flags"] = index % people.Flats.Count == 1 ? 0x20 : 0,
                    ["nameSeed"] = (int)StablePopulationSeed(id),
                });
            }
        }

        private void Append(string section, JsonNode node) => ((JsonArray)_world[section]!).Add(node);

        private int Missing(string field)
        {
            diagnostics.Add($"Profile '{key.LogicalId}' block section lacks {field}.");
            return 0;
        }

        private JsonObject Moved(JsonNode? node, Vector3 origin, Func<string, string>? place = null)
        {
            JsonObject value = Clone(node);
            if (place is not null && value["id"] is JsonValue id && id.TryGetValue(out string? text)) value["id"] = place(text);
            if (value["position"] is JsonObject position)
                value["position"] = Vector(new Vector3(position["x"]!.GetValue<float>(), position["y"]!.GetValue<float>(), position["z"]!.GetValue<float>()) + origin);
            return value;
        }

        private JsonObject Clone(JsonNode? node)
        {
            if (node is JsonObject value) return (JsonObject)value.DeepClone();
            diagnostics.Add($"Profile '{key.LogicalId}' block section carries an entry that is not an object.");
            return [];
        }
    }

    /// <summary>A block identity placed at a grid position: <c>kind/scope/rest</c> becomes <c>kind/scope/x/z/rest</c>.</summary>
    private static string PlaceId(string id, int x, int z)
    {
        string[] parts = id.Split('/', 3);
        if (parts.Length != 3) throw new InvalidOperationException($"Block identity '{id}' has no block scope to place.");
        return string.Create(CultureInfo.InvariantCulture, $"{parts[0]}/{parts[1]}/{x}/{z}/{parts[2]}");
    }

    /// <summary>The block's identity scope: its published key without its kind (<c>rdb/b0000009-rdb</c> → <c>b0000009-rdb</c>).</summary>
    private static string Scope(DaggerfallWorldBlockDocument document) => document.PublishedKey.Split('/')[1];

    private static JsonObject Vector(Vector3 value) => new() { ["x"] = value.X, ["y"] = value.Y, ["z"] = value.Z };

    /// <summary>A lowercase identity segment, as the published site and block identities name a source name.</summary>
    private static string Slug(string value)
    {
        System.Text.StringBuilder result = new(value.Length);
        foreach (char character in value) result.Append(char.IsAsciiLetterOrDigit(character) ? char.ToLowerInvariant(character) : '-');
        string slug = result.ToString().Trim('-');
        return slug.Length == 0 ? "source" : slug;
    }

    private static ushort StablePopulationSeed(string id)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (char value in id) hash = (hash ^ value) * 16777619;
            return (ushort)(hash & ushort.MaxValue);
        }
    }
}
