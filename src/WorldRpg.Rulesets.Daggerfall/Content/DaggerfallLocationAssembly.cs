using System.Collections.ObjectModel;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Rusty.Engine;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>One product-wide mesh: its artifact and the material each of its slots draws.</summary>
internal sealed record DaggerfallWorldMesh(string Path, ContentSha256 Sha256, IReadOnlyDictionary<uint, string> MaterialsBySlot);

/// <summary>
/// Assembles a catalog location's exterior, building interior or dungeon from the per-block publication:
/// each block the location places, moved to its grid position, its identities given that position, its models drawn from the product-wide meshes with the location's textures,
/// and its collision and navigation placed beside its neighbours by whole navigation cells.
/// </summary>
/// <remarks>
/// Texture choice follows DEC-11: an exterior or interior draws each mesh texture through the published
/// climate swaps for the location's own-pixel climate, and a dungeon through its classic texture table.
/// The exterior ground is the terrain's, drawn with the archive of the climate one pixel east. A swap is
/// published for both summer and winter, so the calendar can rebind the retained materials.
/// </remarks>
internal sealed class DaggerfallLocationAssembly
{
    // Every block artifact is derived on the sites' 0.8-unit navigation grid: a map pixel (one terrain cell)
    // is eight RMB blocks or sixteen RDB blocks wide, so blocks sit whole cells apart.
    private const float RmbBlockSide = DaggerfallWorldGrid.HorizontalSize / 8F;
    private const float RdbBlockSide = DaggerfallWorldGrid.HorizontalSize / 16F;
    private const long RmbBlockCells = DaggerfallWorldGrid.NavigationCellsPerExteriorCell / 8;
    private const long RdbBlockCells = DaggerfallWorldGrid.NavigationCellsPerExteriorCell / 16;

    private const int AutomapSide = 64;

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
    private readonly IReadOnlyDictionary<string, DaggerfallArtModel> _models;

    /// <param name="models">The art pack's remodels by ARCH3D record id (<see cref="DaggerfallArtReplacements.Models"/>), none without one.</param>
    internal DaggerfallLocationAssembly(DaggerfallDefinitions definitions, DaggerfallWorldBlocks blocks,
        Func<DaggerfallProductMedia> media, Func<IReadOnlyDictionary<string, DaggerfallWorldMesh>> meshes,
        IReadOnlyDictionary<string, DaggerfallArtModel>? models = null)
    {
        _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
        // Streaming asks whether each location in the exterior window places an exterior on every window change.
        _records = new(() => _definitions.Locations.Records.ToDictionary(record => record.Id));
        _blocks = blocks ?? throw new ArgumentNullException(nameof(blocks));
        _media = media ?? throw new ArgumentNullException(nameof(media));
        _meshes = meshes ?? throw new ArgumentNullException(nameof(meshes));
        _models = models ?? new Dictionary<string, DaggerfallArtModel>();
    }

    /// <summary>
    /// The models a block draws as static meshes or moving models in a profile of this kind. An exterior's city gates and a
    /// dungeon's still doors are drawn by their own parts, which keep the classic mesh.
    /// </summary>
    private static IEnumerable<DaggerfallWorldBlockModel> DrawnModels(DaggerfallWorldProfileKind kind, DaggerfallWorldBlockDocument document)
    {
        HashSet<string> gates = kind == DaggerfallWorldProfileKind.Exterior ? [.. document.Gates.Select(gate => gate.ModelId)] : [];
        return document.Models.Where(model => !gates.Contains(model.Id)
            && (kind != DaggerfallWorldProfileKind.Dungeon || model.Action is not null || model.DoorId is null));
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
                && BuildingBlock(exterior, placed) is { } source && _blocks.Contains(InteriorBlockKey(source, placed)),
            _ => false,
        };
    }

    /// <summary>
    /// Whether a location variant changes how this profile assembles: a selected pack carries the variant for the
    /// profile's location, and the profile is its exterior, or the interior of a building in a cell the variant places
    /// another block in. A block variant redraws a block's outside only.
    /// </summary>
    internal bool Varies(DaggerfallWorldProfileKey key, string variant)
    {
        if (_blocks.Variants.Location(key.Site, variant) is not { } cells) return false;
        return key.Kind switch
        {
            DaggerfallWorldProfileKind.Exterior => true,
            DaggerfallWorldProfileKind.Interior => DaggerfallWorldProfileIds.TryParse(key.LogicalId, out _, out DaggerfallSiteBuildingId? building)
                && building is { } placed && cells.GetValueOrDefault((placed.BlockX, placed.BlockY))?.Block is not null,
            _ => false,
        };
    }

    /// <summary>
    /// Assembles one profile, reading only the blocks it places; with a <paramref name="variant"/> a selected pack carries
    /// for the location (<see cref="Varies"/>), each cell it names places its block, or draws its block variant, as
    /// Daggerfall Unity swaps a location's record or a block's data while a quest holds the variant. Anything the
    /// publication or the packs lack is named.
    /// </summary>
    internal DaggerfallSiteProfile Assemble(DaggerfallWorldProfileKey key, string? variant = null)
    {
        if (_definitions.AssembledSites is not { } presentation)
            throw new InvalidOperationException($"Profile '{key.LogicalId}' cannot be assembled: the base payload publishes no assembledSites presentation.");
        if (!DaggerfallWorldProfileIds.TryParse(key.LogicalId, out DaggerfallWorldProfileKey parsed, out DaggerfallSiteBuildingId? building) || parsed != key)
            throw new InvalidOperationException($"Profile '{key.LogicalId}' is not a location profile id.");
        if (!TryRecord(key.Site, out DaggerfallSiteRecord? record))
            throw new InvalidOperationException($"Profile '{key.LogicalId}' names location {key.Site}, which the catalog does not carry.");
        if (record!.Climate is not int climate)
            throw new InvalidOperationException($"Location {key.Site} ('{record.Name}') publishes no climate, so its textures cannot be chosen.");
        Dictionary<(int X, int Y), string> drawn = [];
        if (variant is not null)
        {
            if (!Varies(key, variant)) throw new InvalidOperationException($"Profile '{key.LogicalId}' has no variant '{variant}' in the selected packs.");
            IReadOnlyDictionary<(int X, int Y), DaggerfallWorldVariantCell> cells = _blocks.Variants.Location(key.Site, variant)!;
            record = Swapped(record, variant, cells);
            foreach (((int X, int Y) cell, DaggerfallWorldVariantCell value) in cells)
                if (value.Variant is { } blockVariant) drawn.Add(cell, blockVariant);
        }

        DaggerfallContentDiagnostics diagnostics = new();
        Builder builder = new(this, key, record, climate, presentation, diagnostics, drawn);
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
    /// The location's record with the blocks a location variant places in its cells. A block placed in another's cell
    /// must state the same building slots, each of the same kind and with an interior exactly where the block it replaces
    /// has one: the location's buildings (their names, factions and quests) are the catalog's by slot, so a slot that
    /// changed kind or lost its interior would name a building that is not there.
    /// </summary>
    private DaggerfallSiteRecord Swapped(DaggerfallSiteRecord record, string variant, IReadOnlyDictionary<(int X, int Y), DaggerfallWorldVariantCell> cells)
    {
        DaggerfallSiteExterior exterior = record.Exterior!;
        (DaggerfallSiteBlock Placed, string Block)[] swaps = [.. exterior.Blocks
            .Select(block => (Placed: block, Block: cells.GetValueOrDefault((block.X, block.Y))?.Block))
            .Where(swap => swap.Block is not null && swap.Block != swap.Placed.SourceName)
            .Select(swap => (swap.Placed, swap.Block!))];
        if (swaps.Length == 0) return record;
        IReadOnlyDictionary<DaggerfallWorldBlockKey, DaggerfallWorldBlockDocument> documents = _blocks.Read(swaps
            .SelectMany(swap => new[] { swap.Placed.SourceName, swap.Block })
            .Select(block => new DaggerfallWorldBlockKey(DaggerfallWorldBlockKind.RmbExterior, block)));
        DaggerfallContentDiagnostics diagnostics = new();
        foreach ((DaggerfallSiteBlock placed, string block) in swaps)
        {
            static (int, int, bool)[] Slots(DaggerfallWorldBlockDocument document) =>
                [.. document.Buildings.OrderBy(building => building.Index).Select(building => (building.Index, building.BuildingType, building.HasInterior))];
            if (!Slots(documents[new(DaggerfallWorldBlockKind.RmbExterior, block)]).SequenceEqual(Slots(documents[new(DaggerfallWorldBlockKind.RmbExterior, placed.SourceName)])))
                diagnostics.Add($"Location variant '{variant}' of {record.Id} places '{block}' in cell ({placed.X}, {placed.Y}), whose building slots are not those of the '{placed.SourceName}' it replaces.");
        }

        diagnostics.ThrowIfAny();
        Dictionary<DaggerfallSiteBlock, string> replaced = swaps.ToDictionary(swap => swap.Placed, swap => swap.Block);
        return record with { Exterior = exterior with { Blocks = [.. exterior.Blocks.Select(block => replaced.TryGetValue(block, out string? swapped) ? block with { SourceName = swapped } : block)] } };
    }

    /// <summary>An exterior's blocks in grid order.</summary>
    private static DaggerfallSiteBlock[] PlacedBlocks(DaggerfallSiteExterior exterior) =>
        [.. exterior.Blocks.OrderBy(block => block.X).ThenBy(block => block.Y).ThenBy(block => block.SourceName, StringComparer.Ordinal)];

    private static DaggerfallWorldBlockKey ExteriorBlockKey(DaggerfallSiteBlock block, string? variant = null) =>
        new(DaggerfallWorldBlockKind.RmbExterior, block.SourceName, Variant: variant);

    /// <summary>The exterior block a building stands in, or null where its location places none there.</summary>
    private static DaggerfallSiteBlock? BuildingBlock(DaggerfallSiteExterior exterior, DaggerfallSiteBuildingId building) =>
        exterior.Blocks.SingleOrDefault(block => block.X == building.BlockX && block.Y == building.BlockY);

    /// <summary>The interior block of a building standing in <paramref name="block"/>.</summary>
    private static DaggerfallWorldBlockKey InteriorBlockKey(DaggerfallSiteBlock block, DaggerfallSiteBuildingId building) =>
        new(DaggerfallWorldBlockKind.RmbInterior, block.SourceName, building.Index);

    private static DaggerfallWorldBlockKey DungeonBlockKey(DaggerfallSiteDungeonBlock block) => new(DaggerfallWorldBlockKind.Rdb, block.SourceKey);

    /// <summary>Where an RMB block's own frame sits in its location's exterior: blocks run east along +X and north along -Z.</summary>
    private static Vector3 RmbBlockOrigin(DaggerfallSiteBlock block) => new(block.X * RmbBlockSide, 0F, -(block.Y * RmbBlockSide));

    private static IEnumerable<WorldPoint> StartMarkers(DaggerfallSiteBlock block, DaggerfallWorldBlockDocument document) =>
        document.StartMarkers.Select(marker => WorldPoint.From(RmbBlockOrigin(block) + marker));

    /// <summary>
    /// Reads the world media publication's mesh index: each mesh artifact with its digest and the material
    /// its slots draw.
    /// </summary>
    internal static IReadOnlyDictionary<string, DaggerfallWorldMesh> ReadMeshIndex(DaggerfallData content, DaggerfallWorldMedia product)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(product);
        const string indexPath = DaggerfallWorldMedia.Root + "/geometry/index.json";
        DaggerfallContentDiagnostics diagnostics = new();
        if (!content.TryReadFile(indexPath, out ReadOnlyMemory<byte> index))
        {
            diagnostics.Add($"The world media publication carries no mesh index '{indexPath}'.");
            throw diagnostics.Exception();
        }

        Dictionary<string, DaggerfallWorldMesh> meshes = new(StringComparer.Ordinal);
        try
        {
            using JsonDocument document = JsonDocument.Parse(index);
            // A block placement an earlier one partly covers draws its own trimmed mesh, indexed beside the models'.
            foreach (JsonElement mesh in DaggerfallBaseContent.Array(document.RootElement, "meshes", diagnostics)
                .Concat(DaggerfallBaseContent.Array(document.RootElement, "placementMeshes", diagnostics)))
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
    /// <param name="variantCells">The exterior cells whose block draws a block variant, each with its name; empty for the base.</param>
    private sealed class Builder(DaggerfallLocationAssembly owner, DaggerfallWorldProfileKey key, DaggerfallSiteRecord record, int climate,
        DaggerfallAssembledSiteDefinition presentation, DaggerfallContentDiagnostics diagnostics,
        IReadOnlyDictionary<(int X, int Y), string> variantCells)
    {
        private readonly DaggerfallProductMedia _media = owner._media();
        private readonly int _groundClimate = owner._definitions.Grids.ClimateAtWorldPixel(record.MapPixelX, record.MapPixelY).Value;
        private readonly IReadOnlyDictionary<string, DaggerfallWorldMesh> _meshes = owner._meshes();
        private readonly List<DaggerfallSiteSpatialPart> _spatial = [];
        private readonly List<(string Path, ContentSha256 Sha256, Transform Pose, IReadOnlyDictionary<uint, string> Materials, string ModelId)> _placedMeshes = [];
        // Each remodel the profile places, by ARCH3D id, with the textures its classic slots draw here.
        private readonly Dictionary<string, (DaggerfallArtModel Art, IReadOnlyDictionary<uint, string> Textures, IReadOnlyList<DaggerfallRemodelTexture> Own)> _remodels = new(StringComparer.Ordinal);
        private readonly SortedSet<string> _materials = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _winterTextures = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _rainTextures = new(StringComparer.Ordinal);
        private readonly List<DaggerfallRdbDoorDefinition> _doors = [];
        private readonly List<DaggerfallDungeonActionModelDefinition> _actionModels = [];
        private readonly List<DaggerfallDungeonMapGeometry> _mapGeometry = [];
        private readonly JsonObject _world = new()
        {
            ["lights"] = new JsonArray(),
            ["billboards"] = new JsonArray(),
            ["actors"] = new JsonArray(),
            ["randomEnemies"] = new JsonArray(),
            ["treasures"] = new JsonArray(),
            ["population"] = new JsonArray(),
            ["questMarkers"] = new JsonArray(),
            ["staticNpcs"] = new JsonArray(),
            ["propertyContainers"] = new JsonArray(),
            ["actions"] = new JsonArray(),
            ["ambientZones"] = new JsonArray(),
            ["waterVolumes"] = new JsonArray(),
        };
        private IReadOnlyDictionary<int, int>? _remaps;
        private Vector3? _start;
        private Vector3? _enter;
        private readonly List<DaggerfallSitePortal> _portals = [];
        private readonly Dictionary<string, DaggerfallSiteAnchor> _anchors = new(StringComparer.Ordinal);
        private readonly List<WorldPoint> _startMarkers = [];
        private readonly List<DaggerfallWorldBlockTransitionDoor> _dungeonExits = [];
        private DaggerfallWorldBlockTransitionDoor? _lowestEntrance;
        private readonly List<DaggerfallCityGateDefinition> _gates = [];
        private IReadOnlyList<DaggerfallPopulationPlacement> _dynamicPopulation = [];

        /// <summary>A location's exterior: every RMB block of its grid, in grid order.</summary>
        internal void Exterior()
        {
            DaggerfallSiteExterior exterior = record.Exterior!;
            DaggerfallSiteBlock[] placed = PlacedBlocks(exterior);
            DaggerfallWorldBlockKey Key(DaggerfallSiteBlock block) => ExteriorBlockKey(block, variantCells.GetValueOrDefault((block.X, block.Y)));
            IReadOnlyDictionary<DaggerfallWorldBlockKey, DaggerfallWorldBlockDocument> blocks = owner._blocks.Read(placed.Select(Key));
            HashSet<(int X, int Z)> clear = [];
            foreach (DaggerfallSiteBlock block in placed)
            {
                DaggerfallWorldBlockDocument document = blocks[Key(block)];
                Vector3 origin = RmbBlockOrigin(block);
                Place(document, $"block/{block.X}/{block.Y}", block.X * RmbBlockCells, -(block.Y * RmbBlockCells));
                foreach (DaggerfallWorldBlockModel model in DrawnModels(key.Kind, document)) Draw(model, origin);
                foreach (DaggerfallWorldBlockDoor door in document.Doors) ExteriorDoor(door, block, origin);
                foreach (DaggerfallWorldBlockTransitionDoor door in document.TransitionDoors)
                    DungeonEntrance(door, origin, id => PlaceId(id, block.X, block.Y));
                foreach (DaggerfallWorldBlockGate gate in document.Gates) CityGate(gate, origin, id => PlaceId(id, block.X, block.Y));
                if (document.StartMarker is { } start) _start ??= origin + start;
                if (document.EnterMarker is { } enter) _enter ??= origin + enter;
                _startMarkers.AddRange(StartMarkers(block, document));
                PlaceStaticNpcs(
                    document,
                    origin,
                    id => PlaceId(id, block.X, block.Y),
                    owner._definitions.People.GetRmbExteriorSlots(block.SourceName),
                    slotId => DaggerfallPeopleSlots.ExteriorBlockSlotKey(block.X, block.Y, slotId));
                bool hasPackClutter = PlacePeopleClutterSlots(
                    document,
                    owner._definitions.People.GetRmbExteriorSlots(block.SourceName),
                    origin,
                    slotId => DaggerfallPeopleSlots.PlacedExteriorBlockPersonId(block.SourceName, block.X, block.Y, slotId),
                    (block.X << 8) ^ block.Y);
                foreach (JsonNode? light in document.Section("lights")) Append("lights", Moved(light, origin, id => PlaceId(id, block.X, block.Y)));
                int fallbackClutter = 0;
                foreach (JsonNode? billboard in document.Section("billboards"))
                    AppendBillboardOrPromoteClutter(billboard, document, origin, id => PlaceId(id, block.X, block.Y), hasPackClutter, (block.X << 8) ^ block.Y, ref fallbackClutter);
                if (document.ClearGround is { } bits)
                    for (int index = 0; index < AutomapSide * AutomapSide; index++)
                        if ((bits[index / 8] & (1 << (index % 8))) != 0)
                            clear.Add(((block.X * AutomapSide) + (index % AutomapSide), (block.Y * AutomapSide) + (index / AutomapSide)));
            }

            Vector3? gateAnchor = _gates.Count > 0 ? _gates[0].Pose.Translation : _start;
            _dynamicPopulation = DaggerfallExteriorPopulation.Create(owner._definitions, record, clear,
                gateAnchor is { } anchor ? new WorldPoint(anchor.X, anchor.Y, anchor.Z) : null);
        }

        private void PlaceStaticNpcs(
            DaggerfallWorldBlockDocument document,
            Vector3 origin,
            Func<string, string> placed,
            IReadOnlyList<DaggerfallPeopleSlotDefinition>? authoredSlots = null,
            Func<string, string>? placementSlotKey = null)
        {
            IReadOnlyList<DaggerfallPeopleSlotDefinition> talkableSlots = FilterTalkableSlots(authoredSlots);
            bool[] matched = talkableSlots.Count > 0 ? new bool[talkableSlots.Count] : [];
            string regionName = owner._definitions.BuildingNames.RegionName(key.Site.Region);
            string climateName = owner._definitions.Grids.Climate.GetCell(record.MapPixelX + 1, record.MapPixelY).Name;
            foreach (JsonNode? person in document.Section("staticNpcs"))
            {
                Vector3? rawPos = TryReadPosition(person);
                JsonObject value = Moved(person, origin, id => placed($"person/{Scope(document)}/{id["person/".Length..]}"));
                int offset = value["sourceOffset"]?.GetValue<int>() ?? Missing("staticNpcs sourceOffset");
                value.Remove("sourceOffset");
                value["nameSeed"] = offset ^ key.Site.Index;
                if (talkableSlots.Count > 0 && placementSlotKey is not null)
                    ApplyAuthoredSpawnerSlot(value, rawPos, talkableSlots, matched, placementSlotKey, regionName, climateName);
                Append("staticNpcs", value);
            }
        }

        private static IReadOnlyList<DaggerfallPeopleSlotDefinition> FilterTalkableSlots(
            IReadOnlyList<DaggerfallPeopleSlotDefinition>? slots)
        {
            if (slots is null || slots.Count == 0) return [];
            List<DaggerfallPeopleSlotDefinition> talkable = [];
            foreach (DaggerfallPeopleSlotDefinition slot in slots)
            {
                if (string.Equals(slot.Interaction, "full-dialogue", StringComparison.Ordinal)
                    && !string.Equals(slot.SpawnerId, "clutter-person", StringComparison.Ordinal))
                    talkable.Add(slot);
            }
            return talkable;
        }

        private static Vector3? TryReadPosition(JsonNode? node)
        {
            if (node is not JsonObject obj || obj["position"] is not JsonObject pos) return null;
            if (pos["x"] is null || pos["y"] is null || pos["z"] is null) return null;
            return new Vector3(pos["x"]!.GetValue<float>(), pos["y"]!.GetValue<float>(), pos["z"]!.GetValue<float>());
        }

        private void ApplyAuthoredSpawnerSlot(
            JsonObject value,
            Vector3? rawPosition,
            IReadOnlyList<DaggerfallPeopleSlotDefinition> talkableSlots,
            bool[] matched,
            Func<string, string> placementSlotKey,
            string regionName,
            string climateName)
        {
            int bestIndex = -1;
            if (rawPosition is { } pos)
            {
                float bestDistSq = 0.25f;
                for (int i = 0; i < talkableSlots.Count; i++)
                {
                    if (matched[i]) continue;
                    float distSq = Vector3.DistanceSquared(pos, talkableSlots[i].Position.ToVector());
                    if (distSq < bestDistSq)
                    {
                        bestDistSq = distSq;
                        bestIndex = i;
                    }
                }
            }
            if (bestIndex < 0)
            {
                for (int i = 0; i < talkableSlots.Count; i++)
                {
                    if (!matched[i]) { bestIndex = i; break; }
                }
            }
            if (bestIndex < 0) return;

            matched[bestIndex] = true;
            DaggerfallPeopleSlotDefinition slot = talkableSlots[bestIndex];
            // Cast individuals keep their unique cast sprite, faction, and identity unchanged.
            if (slot.CastId is not null || slot.SpawnerId is null) return;
            if (!owner._definitions.People.Spawners.ContainsKey(slot.SpawnerId)) return;

            DaggerfallResolvedPeopleSlot resolved = DaggerfallPeopleSlots.ResolveSlot(
                owner._definitions.People, owner._definitions.Factions, 0UL, key.Site, placementSlotKey(slot.SlotId), slot,
                regionKey: regionName, climateKey: climateName);
            if (_media.BillboardSprites.ContainsKey((resolved.Visual.Archive, resolved.Visual.Record)))
            {
                value["billboardArchive"] = resolved.Visual.Archive;
                value["billboardRecord"] = resolved.Visual.Record;
                value["gender"] = resolved.Visual.Gender;
            }
            value["spawnerId"] = resolved.SpawnerId;
            value["interaction"] = resolved.Interaction;
            value["harm"] = resolved.Harm;
            value["deriveRegionalFaction"] = slot.DeriveRegionalFaction;
        }

        private bool PlacePeopleClutterSlots(
            DaggerfallWorldBlockDocument document,
            IReadOnlyList<DaggerfallPeopleSlotDefinition> slots,
            Vector3 origin,
            Func<string, string>? placeSlotId,
            int nameSeedSalt)
        {
            bool emittedAny = false;
            string regionName = owner._definitions.BuildingNames.RegionName(key.Site.Region);
            string climateName = owner._definitions.Grids.Climate.GetCell(record.MapPixelX + 1, record.MapPixelY).Name;
            foreach (DaggerfallPeopleSlotDefinition slot in slots)
            {
                if (string.Equals(slot.Interaction, "full-dialogue", StringComparison.Ordinal)
                    && !string.Equals(slot.SpawnerId, "clutter-person", StringComparison.Ordinal))
                    continue;
                emittedAny = true;
                string id = placeSlotId is not null
                    ? placeSlotId(slot.SlotId)
                    : $"person/{slot.SlotId.Replace(':', '-')}";
                DaggerfallPeopleVisual visual = slot.ClassicVisual;
                string harm = "law-protected-civilian";
                if (slot.SpawnerId is not null
                    && owner._definitions.People.Spawners.ContainsKey(slot.SpawnerId))
                {
                    DaggerfallResolvedPeopleSlot resolved = DaggerfallPeopleSlots.ResolveSlot(
                        owner._definitions.People, owner._definitions.Factions, 0UL, key.Site, id, slot,
                        regionKey: regionName, climateKey: climateName);
                    harm = resolved.Harm;
                    if (_media.BillboardSprites.ContainsKey((resolved.Visual.Archive, resolved.Visual.Record)))
                        visual = resolved.Visual;
                }
                int nameSeed = unchecked((int)StableHash.NonZeroFnv1a64(
                    $"clutter-person:{key.Site.Region}:{key.Site.Index}:{id}:{nameSeedSalt}"));
                JsonObject person = new()
                {
                    ["id"] = id,
                    ["position"] = Vector(slot.Position.ToVector() + origin),
                    ["billboardArchive"] = visual.Archive,
                    ["billboardRecord"] = visual.Record,
                    ["factionId"] = slot.FactionId ?? 0,
                    ["deriveRegionalFaction"] = slot.DeriveRegionalFaction,
                    ["race"] = null,
                    ["gender"] = visual.Gender,
                    ["nameSeed"] = nameSeed,
                    ["interaction"] = slot.Interaction,
                    ["harm"] = harm,
                    ["spawnerId"] = slot.SpawnerId,
                };
                Append("staticNpcs", person);
            }
            return emittedAny;
        }

        private void AppendBillboardOrPromoteClutter(
            JsonNode? billboard,
            DaggerfallWorldBlockDocument document,
            Vector3 origin,
            Func<string, string>? placeId,
            bool hasPackClutterSlots,
            int nameSeedSalt,
            ref int fallbackClutterOrdinal)
        {
            if (billboard is JsonObject obj
                && obj["spriteResourceId"] is JsonValue spriteVal
                && spriteVal.TryGetValue(out string? spriteId)
                && TryParseBillboardSprite(spriteId, out int archive, out int record)
                && DaggerfallPeopleSlots.IsPersonClutterBillboard(archive, record))
            {
                // Person clutter flats never enter decorative scenery billboards.
                if (!hasPackClutterSlots && obj["position"] is JsonObject pos)
                {
                    int ordinal = fallbackClutterOrdinal++;
                    string id = placeId is not null
                        ? placeId($"person/{Scope(document)}/clutter-{ordinal}")
                        : $"person/clutter-{ordinal}";
                    Vector3 position = new Vector3(
                        pos["x"]!.GetValue<float>(),
                        pos["y"]!.GetValue<float>(),
                        pos["z"]!.GetValue<float>()) + origin;
                    int nameSeed = unchecked((int)StableHash.NonZeroFnv1a64(
                        $"clutter-billboard:{key.Site.Region}:{key.Site.Index}:{id}:{nameSeedSalt}"));
                    Append("staticNpcs", new JsonObject
                    {
                        ["id"] = id,
                        ["position"] = Vector(position),
                        ["billboardArchive"] = archive,
                        ["billboardRecord"] = record,
                        ["factionId"] = 0,
                        ["deriveRegionalFaction"] = true,
                        ["race"] = null,
                        ["gender"] = "Male",
                        ["nameSeed"] = nameSeed,
                        ["interaction"] = "none",
                        ["harm"] = "law-protected-civilian",
                        ["spawnerId"] = "clutter-person",
                    });
                }
                return;
            }
            Append("billboards", placeId is not null ? Billboard(billboard, origin, placeId) : Clone(billboard));
        }

        private static bool TryParseBillboardSprite(string spriteId, out int archive, out int record)
        {
            archive = 0;
            record = 0;
            string[] parts = spriteId.Split('-');
            return parts is ["sprite/texture", var archiveText, var recordText]
                && int.TryParse(archiveText, NumberStyles.None, CultureInfo.InvariantCulture, out archive)
                && int.TryParse(recordText, NumberStyles.None, CultureInfo.InvariantCulture, out record);
        }

        /// <summary>One building's inside half, in the frame its block builds it in.</summary>
        internal void Interior(DaggerfallSiteBuildingId building)
        {
            DaggerfallSiteExterior exterior = record.Exterior!;
            DaggerfallSiteBlock placed = BuildingBlock(exterior, building)
                ?? throw new InvalidOperationException($"Location {key.Site} places no block at ({building.BlockX}, {building.BlockY}).");
            DaggerfallWorldBlockKey blockKey = InteriorBlockKey(placed, building);
            DaggerfallWorldBlockDocument document = owner._blocks.Read([blockKey])[blockKey];
            Place(document, "block", 0, 0);
            foreach (DaggerfallWorldBlockModel model in DrawnModels(key.Kind, document)) Draw(model, Vector3.Zero);
            _start = document.StartMarker;
            _enter = document.EnterMarker;
            // A building door seen from inside leads back out; without an entrance to return through, the
            // player comes out in front of this building's door (BuildingTransitionExteriorLogic).
            foreach (DaggerfallWorldBlockTransitionDoor door in document.TransitionDoors.Where(door => door.Kind == DaggerfallWorldBlockTransitionKind.BuildingExit))
                Portal(door.Id, door.Position, door.Normal, DaggerfallWorldProfileIds.Exterior(key.Site), BuildingAnchor(building));
            foreach (JsonNode? marker in document.Section("questMarkers"))
            {
                JsonObject value = Clone(marker);
                value["sourceKey"] = placed.SourceName;
                value["buildingIndex"] = building.Index;
                value["blockX"] = 0;
                value["blockZ"] = 0;
                Append("questMarkers", value);
            }

            bool hasInteriorPeople = owner._definitions.People.TryGetRmbInteriorSlots(placed.SourceName, building.Index, out DaggerfallPeopleBuildingSlots? interiorPeople)
                && interiorPeople is not null;
            IReadOnlyList<DaggerfallPeopleSlotDefinition> interiorTalkable = hasInteriorPeople
                ? FilterTalkableSlots(interiorPeople!.Slots)
                : [];
            bool[] interiorMatched = interiorTalkable.Count > 0 ? new bool[interiorTalkable.Count] : [];
            string interiorRegionName = owner._definitions.BuildingNames.RegionName(key.Site.Region);
            string interiorClimateName = owner._definitions.Grids.Climate.GetCell(record.MapPixelX + 1, record.MapPixelY).Name;

            // The classic name seed folds the building's grid key and the location index into the source offset.
            int buildingKey = (building.BlockX << 16) + (building.BlockY << 8) + building.Index;
            if (buildingKey == 0) buildingKey = 1 << 24;
            foreach (JsonNode? person in document.Section("staticNpcs"))
            {
                Vector3? rawPos = TryReadPosition(person);
                JsonObject value = Clone(person);
                int offset = value["sourceOffset"]?.GetValue<int>() ?? Missing("staticNpcs sourceOffset");
                value.Remove("sourceOffset");
                value["nameSeed"] = offset ^ (buildingKey + key.Site.Index);
                if (interiorTalkable.Count > 0)
                    ApplyAuthoredSpawnerSlot(value, rawPos, interiorTalkable, interiorMatched,
                        slotId => DaggerfallPeopleSlots.InteriorBuildingSlotKey(building.BlockX, building.BlockY, slotId),
                        interiorRegionName, interiorClimateName);
                Append("staticNpcs", value);
            }

            bool hasPackClutter = hasInteriorPeople
                && PlacePeopleClutterSlots(document, interiorPeople!.Slots, Vector3.Zero, placeSlotId: null, buildingKey);

            foreach (JsonNode? light in document.Section("lights")) Append("lights", Clone(light));
            int fallbackClutter = 0;
            foreach (JsonNode? billboard in document.Section("billboards"))
                AppendBillboardOrPromoteClutter(billboard, document, Vector3.Zero, placeId: null, hasPackClutter, buildingKey, ref fallbackClutter);
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
                ["entryFrame"] = slot.EntryFrame is { } frame ? new JsonObject
                {
                    ["exteriorOrigin"] = Vector(frame.ExteriorOrigin),
                    ["exteriorYawDegrees"] = frame.ExteriorYawDegrees,
                    ["markers"] = new JsonArray(frame.Markers.Select(marker => (JsonNode)Vector(marker.ToVector())).ToArray()),
                    ["doors"] = new JsonArray(frame.Doors.Select(door => (JsonNode)new JsonObject
                    { ["position"] = Vector(door.Position.ToVector()), ["normal"] = Vector(door.Normal) }).ToArray()),
                } : null,
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
                placed.Select(DungeonBlockKey));
            float minimumY = float.PositiveInfinity, maximumY = float.NegativeInfinity;
            List<(string Id, string Kind, int X, int Z)> zones = [];
            foreach (DaggerfallSiteDungeonBlock block in placed)
            {
                DaggerfallWorldBlockDocument document = blocks[DungeonBlockKey(block)];
                Vector3 origin = new(block.X * RdbBlockSide, 0F, -(block.Z * RdbBlockSide));
                string Placed(string id) => PlaceId(id, block.X, block.Z);
                // Named by its source block as well as its grid position: the catalog places two blocks on one cell of
                // Orsinium's dungeon, and the donor lays out both.
                Place(document, $"block/{Scope(document)}/{block.X}/{block.Z}", block.X * RdbBlockCells, -(block.Z * RdbBlockCells));
                foreach (DaggerfallWorldBlockModel model in DrawnModels(key.Kind, document))
                {
                    if (model.Action is { } action) ActionModel(model, action, origin, Placed);
                    else Draw(model, origin, Placed);
                }

                foreach (DaggerfallWorldBlockDoor door in document.Doors) DungeonDoor(door, document, origin, Placed);
                // A dungeon exit leads out to the location's exterior, in front of its dungeon entrance.
                foreach (DaggerfallWorldBlockTransitionDoor door in document.TransitionDoors.Where(door => door.Kind == DaggerfallWorldBlockTransitionKind.DungeonExit))
                {
                    DaggerfallWorldBlockTransitionDoor exit = Moved(door, origin, Placed);
                    _dungeonExits.Add(exit);
                    if (record.Exterior is { Blocks.Count: > 0 })
                        Portal(exit.Id, exit.Position, exit.Normal, DaggerfallWorldProfileIds.Exterior(key.Site), DungeonEntranceAnchor);
                }
                foreach (string section in new[] { "actors", "randomEnemies", "treasures" })
                    foreach (JsonNode? marker in document.Section(section)) Append(section, Moved(marker, origin, Placed));
                foreach (JsonNode? water in document.Section("waterVolumes"))
                {
                    JsonObject volume = Moved(water, origin, Placed);
                    JsonObject bounds = (JsonObject)volume["bounds"]!;
                    foreach (string edge in new[] { "minimum", "maximum" })
                    {
                        JsonObject point = (JsonObject)bounds[edge]!;
                        bounds[edge] = Vector(new Vector3(point["x"]!.GetValue<float>(), point["y"]!.GetValue<float>(), point["z"]!.GetValue<float>()) + origin);
                    }
                    Append("waterVolumes", volume);
                }
                bool hasPackClutter = PlacePeopleClutterSlots(
                    document,
                    owner._definitions.People.GetRdbDungeonSlots(block.SourceKey),
                    origin,
                    slotId => Placed($"person/{Scope(document)}/{slotId.Replace(':', '-')}"),
                    (block.X << 8) ^ block.Z);
                foreach (JsonNode? light in document.Section("lights")) Append("lights", Moved(light, origin, Placed));
                int fallbackClutter = 0;
                foreach (JsonNode? billboard in document.Section("billboards"))
                    AppendBillboardOrPromoteClutter(billboard, document, origin, Placed, hasPackClutter, (block.X << 8) ^ block.Z, ref fallbackClutter);
                foreach (JsonNode? action in document.Section("actions"))
                {
                    JsonObject value = Moved(action, origin, Placed);
                    if (value["nextObjectPosition"] is JsonObject landing)
                        value["nextObjectPosition"] = Vector(new Vector3(landing["x"]!.GetValue<float>(), landing["y"]!.GetValue<float>(), landing["z"]!.GetValue<float>()) + origin);
                    foreach (string reference in new[] { "nextActionId", "doorId" })
                        if (value[reference] is JsonValue target && target.TryGetValue(out string? id)) value[reference] = Placed(id);
                    Append("actions", value);
                }

                PlaceStaticNpcs(
                    document,
                    origin,
                    Placed,
                    owner._definitions.People.GetRdbDungeonSlots(block.SourceKey),
                    slotId => DaggerfallPeopleSlots.DungeonBlockSlotKey(block.X, block.Z, slotId));
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
                ContentArtifact? winter = null;
                if (_winterTextures.TryGetValue(material, out var winterId) && winterId != material)
                {
                    if (!_media.Textures.TryGetValue(winterId, out winter))
                        diagnostics.Add($"Profile '{key.LogicalId}' winter texture '{winterId}' is not published.");
                }
                ContentArtifact? rain = null;
                if (_rainTextures.TryGetValue(material, out var rainId) && rainId != material)
                {
                    if (!_media.Textures.TryGetValue(rainId, out rain))
                        diagnostics.Add($"Profile '{key.LogicalId}' rain texture '{rainId}' is not published.");
                }
                ContentArtifact? Glass(string? id) =>
                    key.Kind == DaggerfallWorldProfileKind.Exterior && _media.Textures.TryGetValue($"{id ?? material}-glass", out ContentArtifact? glass) ? glass : null;
                DaggerfallMaterialFrames? frames = _media.Animations.GetValueOrDefault(material);
                // A cycling surface draws its own frames in every season, so a season's variant would never show.
                if (frames is not null && (winter is not null || rain is not null))
                    diagnostics.Add($"Profile '{key.LogicalId}' draws texture '{material}', which cycles frames, with a season's variant.");
                materials.Add(new NormalizedMaterial(slot, texture.Path, texture.Sha256, material)
                {
                    WinterTexture = winter, RainTexture = rain,
                    SummerGlass = Glass(material), WinterGlass = Glass(winterId), RainGlass = Glass(rainId),
                    Frames = frames,
                });
            }

            DaggerfallMeshMaterialBinding[] Bindings(IReadOnlyDictionary<uint, string> mesh) =>
                [.. mesh.Where(pair => slots.ContainsKey(pair.Value)).OrderBy(pair => pair.Key)
                    .Select(pair => new DaggerfallMeshMaterialBinding(pair.Key, slots[pair.Value]))];
            List<DaggerfallSiteMesh> meshes = [.. _placedMeshes.Select(mesh => new DaggerfallSiteMesh(mesh.Path, mesh.Sha256, mesh.Pose, Bindings(mesh.Materials)) { ModelId = mesh.ModelId })];
            Dictionary<string, DaggerfallSiteRemodel> remodels = _remodels.ToDictionary(pair => pair.Key, pair => new DaggerfallSiteRemodel(
                pair.Value.Art.File.Path, pair.Value.Art.File.Sha256, Bindings(pair.Value.Textures), pair.Value.Own)
            {
                Shells = pair.Value.Art.Slots.Where(slot => slot.Shell is not null).ToDictionary(slot => slot.Slot, slot => slot.Shell!),
            },
                StringComparer.Ordinal);
            DaggerfallRdbDoorDefinition[] doors = [.. _doors.Select(door => door.Visual is { } visual
                ? door with { Visual = visual with { Materials = Bindings(Visual(visual)) } }
                : door)];
            DaggerfallDungeonActionModelDefinition[] actionModels = [.. _actionModels.Select(model =>
                model with { Visual = model.Visual with { Materials = Bindings(Visual(model.Visual)) } })];
            DaggerfallCityGateDefinition[] gates = [.. _gates.Select(gate => gate with
            {
                Open = gate.Open with { Visual = gate.Open.Visual with { Materials = Bindings(Visual(gate.Open.Visual)) } },
                Closed = gate.Closed with { Visual = gate.Closed.Visual with { Materials = Bindings(Visual(gate.Closed.Visual)) } },
            })];

            // People and furniture are canonically ordered by identity.
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
            // A new game or a teleport starts on the Enter marker facing north (StartDungeonInterior).
            if (key.Kind == DaggerfallWorldProfileKind.Dungeon && _enter is { } enterMarker)
                Anchor(EnterAnchor, new WorldPoint(enterMarker.X, enterMarker.Y, enterMarker.Z), 0F);
            for (int index = 0; index < _startMarkers.Count; index++)
                Anchor(StartMarkerAnchor(index), _startMarkers[index], 0F);
            // Leaving the dungeon lands outside its lowest entrance, facing away from it (PositionPlayerToDungeonExit).
            if (_lowestEntrance is { } entrance)
                Anchor(DungeonEntranceAnchor, Landing(entrance.Position with { Y = entrance.Bounds.Minimum.Y }, entrance.Normal, presentation.DungeonExitLanding),
                    ActorHeading.Yaw(entrance.Normal));
            DaggerfallDungeonMapContent? map = null;
            if (key.Kind is DaggerfallWorldProfileKind.Dungeon or DaggerfallWorldProfileKind.Interior)
            {
                try
                {
                    List<DaggerfallSiteMarker> markers = [];
                    if (_enter is { } enter) markers.Add(new DaggerfallSiteMarker("marker/enter", DaggerfallSiteMarkerKind.Entrance, new WorldPoint(enter.X, enter.Y, enter.Z)));
                    // Each source exit is on the map too, revealed once the player has left through it.
                    markers.AddRange(_portals.Select(portal =>
                        new DaggerfallSiteMarker(portal.Id, DaggerfallSiteMarkerKind.Portal, portal.Position, portal.DestinationLogicalProfile)));
                    map = new DaggerfallDungeonMapContent(_mapGeometry, doors.Select(door => door.Id), markers);
                }
                catch (ArgumentException exception) { diagnostics.Add($"Assembled dungeon map of '{key.LogicalId}' is invalid: {exception.Message}"); }
            }

            DaggerfallSiteGeometry geometry = new(presentation.NavigationGridId, _spatial, meshes, owner._blocks.Open) { Remodels = remodels };
            var actors = DaggerfallSiteContent.ReadFixedActors(normalized, key.LogicalId, owner._definitions, diagnostics);
            var actorSprites = actors.Values.ToDictionary(actor => actor.EntityId, actor =>
            {
                DaggerfallActorDefinition definition = owner._definitions.RequireActor(actor.ActorId);
                return DaggerfallSiteContent.ActorPresentation(actor, definition, _media.MobileSprites[definition.MobileId!.Value], diagnostics);
            });
            return new DaggerfallSiteProfile(
                new ProjectFacts(position, actors),
                geometry,
                presentation.Appearance,
                look,
                materials,
                actorSprites,
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
                _media.GroundContainerSprites,
                map,
                actions,
                actionModels,
                ReadInterior(normalized),
                DaggerfallQuestMarkerContent.ReadWorld(normalized, diagnostics),
                _media.BillboardSprites,
                DaggerfallStaticNpcPlacement.Read(normalized, _media.BillboardSprites, owner._definitions, key.Site, diagnostics),
                waterVolumes: DaggerfallDungeonWater.Read(normalized, key.LogicalId, diagnostics),
                terrainTextures: _media.TerrainTextures,
                population: _dynamicPopulation)
            {
                AmbientZones = DaggerfallAmbientZones.Read(normalized, diagnostics),
                DungeonPopulation = DaggerfallDungeonPopulationContent.Read(normalized, diagnostics),
                CityGates = gates,
                Decorations = DaggerfallSiteContent.ReadDecorations(normalized, _media.BillboardSprites, key.Kind, climate, diagnostics),
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

        /// <summary>
        /// Draws one static model: its mesh posed in the profile frame with the location's textures, tagged with its ARCH3D
        /// id, which draws its remodel there while refreshed art is drawn. The map keeps the classic mesh.
        /// </summary>
        private void Draw(DaggerfallWorldBlockModel model, Vector3 origin, Func<string, string>? place = null)
        {
            if (Mesh(model) is not { } mesh) return;
            Vector3 position = origin + model.Position;
            _placedMeshes.Add((mesh.Path, mesh.Sha256, DaggerfallSiteGeometry.Pose(position, model.Rotation), Textures(mesh), model.ModelId));
            Remodel(model.ModelId);
            if (key.Kind == DaggerfallWorldProfileKind.Exterior) return;
            if (model.Bounds is not { } bounds)
            {
                diagnostics.Add($"Static block model '{model.Id}' states no map bounds.");
                return;
            }

            try
            {
                _mapGeometry.Add(new DaggerfallDungeonMapGeometry(place?.Invoke(model.Id) ?? model.Id, bounds.Minimum + origin, bounds.Maximum + origin,
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
        private IReadOnlyDictionary<uint, string> Textures(DaggerfallWorldMesh mesh) => Textures(mesh.MaterialsBySlot);

        private IReadOnlyDictionary<uint, string> Textures(IEnumerable<KeyValuePair<uint, string>> materialsBySlot)
        {
            Dictionary<uint, string> textures = [];
            foreach ((uint slot, string material) in materialsBySlot)
            {
                string drawn = Texture(material);
                textures.Add(slot, drawn);
                _materials.Add(drawn);
            }

            return textures;
        }

        /// <summary>
        /// Records the art pack's remodel of a placed model, once per profile: its slots that name classic texture identities
        /// take the textures this location draws them with, as the classic's own slots do, and a slot painted for an identity
        /// takes the texture painted for the identity this location draws it as.
        /// </summary>
        private void Remodel(string modelId)
        {
            if (_remodels.ContainsKey(modelId) || !owner._models.TryGetValue(modelId, out DaggerfallArtModel? art)) return;
            _remodels.Add(modelId, (art, Textures(art.Slots.Where(slot => slot.Material is not null && slot.Texture is null)
                .Select(slot => KeyValuePair.Create(slot.Slot, slot.Material!))),
                [.. art.Slots.Where(slot => slot.Texture is not null).Select(slot =>
                {
                    DaggerfallArtFile file = slot.Material is null ? slot.Texture! : slot.Painted(Texture(slot.Material));
                    return new DaggerfallRemodelTexture(slot.Slot, file.Path, file.Sha256, slot.Mask, slot.Flipbook, slot.Unlit);
                })]));
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
                : key.Kind == DaggerfallWorldProfileKind.Exterior
                    ? DaggerfallSiteContent.ExteriorTextureArchive(owner._definitions.Grids.Climate, archive, recordIndex, climate, _groundClimate, DaggerfallClimateSeason.Summer)
                    : owner._definitions.Grids.Climate.SwapArchive(archive, recordIndex, climate, DaggerfallClimateSeason.Summer);
            string summer = string.Create(CultureInfo.InvariantCulture, $"{prefix}{drawn}-{recordIndex}");
            if (key.Kind == DaggerfallWorldProfileKind.Exterior)
            {
                int winter = DaggerfallSiteContent.ExteriorTextureArchive(owner._definitions.Grids.Climate, archive, recordIndex, climate, _groundClimate, DaggerfallClimateSeason.Winter);
                string winterId = string.Create(CultureInfo.InvariantCulture, $"{prefix}{winter}-{recordIndex}");
                if (_winterTextures.TryGetValue(summer, out var existing) && existing != winterId)
                    diagnostics.Add($"Summer texture '{summer}' collapses distinct winter textures '{existing}' and '{winterId}'.");
                else _winterTextures[summer] = winterId;
                int rain = DaggerfallSiteContent.ExteriorTextureArchive(owner._definitions.Grids.Climate, archive, recordIndex, climate, _groundClimate, DaggerfallClimateSeason.Rain);
                string rainId = string.Create(CultureInfo.InvariantCulture, $"{prefix}{rain}-{recordIndex}");
                if (_rainTextures.TryGetValue(summer, out var existingRain) && existingRain != rainId)
                    diagnostics.Add($"Summer texture '{summer}' collapses distinct rain textures '{existingRain}' and '{rainId}'.");
                else _rainTextures[summer] = rainId;
            }
            return summer;
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
                _doors.Add(new DaggerfallRdbDoorDefinition(identity, origin + door.Position, door.Rotation, bounds.Minimum, bounds.Maximum,
                    door.Kind, door.StartingLockValue, Action: door.Action)
                {
                    LockSurface = DaggerfallSiteContent.DoorSurface(DaggerfallWorldProfileKind.Exterior),
                    ExteriorBuilding = door.BuildingIndex is int building ? new DaggerfallSiteBuildingId(block.X, block.Y, building) : null,
                }.Validate());
            }
            catch (ArgumentException exception) { diagnostics.Add($"Exterior door '{id}' is invalid: {exception.Message}"); }

            // Coming out of the building without an entrance to return through lands in front of its first door,
            // on its threshold: the door's local +Z is its plane normal, which faces out.
            if (door.BuildingIndex is int index)
            {
                Vector3 normal = Vector3.Transform(Vector3.UnitZ, door.Rotation);
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
            if (record.DungeonBlocks.Count != 0) Portal(entrance.Id, entrance.Position, entrance.Normal, DaggerfallWorldProfileIds.Dungeon(key.Site), StartAnchor);
        }

        /// <summary>
        /// A city gate: its placement's pose, as its model is drawn, with its open and closed models, each textured
        /// through the location's climate, tagged with its ARCH3D id for its remodel, and carrying its own collision. Its
        /// model is not drawn as a static mesh.
        /// </summary>
        private void CityGate(DaggerfallWorldBlockGate gate, Vector3 origin, Func<string, string> place)
        {
            DaggerfallCityGateState? State(DaggerfallWorldBlockGateState state)
            {
                if (!_meshes.TryGetValue(state.MeshArtifactId, out DaggerfallWorldMesh? mesh))
                {
                    diagnostics.Add($"City gate '{gate.Id}' draws mesh '{state.MeshArtifactId}', which the world media publication does not carry.");
                    return null;
                }

                _visualMaterials[mesh.Path] = Textures(mesh);
                Remodel(state.ModelId);
                return new DaggerfallCityGateState(new DaggerfallDoorVisual(mesh.Path, mesh.Sha256, [new DaggerfallMeshMaterialBinding(0, 0)]),
                    state.LocalBounds.Minimum, state.LocalBounds.Maximum, state.CollisionVertices, state.CollisionTriangles) { ModelId = state.ModelId };
            }

            if (State(gate.Open) is not { } open || State(gate.Closed) is not { } closed) return;
            try { _gates.Add(new DaggerfallCityGateDefinition(place(gate.Id), DaggerfallSiteGeometry.Pose(origin + gate.Position, gate.Rotation), open, closed).Validate()); }
            catch (ArgumentException exception) { diagnostics.Add($"City gate '{gate.Id}' is invalid: {exception.Message}"); }
        }

        /// <summary>A portal on a source transition door: the plane's centre, used from the side its normal faces.</summary>
        private void Portal(string id, Vector3 position, Vector3 normal, DaggerfallWorldProfileKey destination, string arrival)
        {
            try { _portals.Add(new DaggerfallSitePortal(id, new WorldPoint(position.X, position.Y, position.Z), presentation.DoorReach, destination.LogicalId, arrival,
                Vector3.Normalize(normal)).Validate()); }
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

            // A door keeps its model's whole turn: several main-quest blocks (Daggerfall, Wayrest and Woodborne Hall
            // castles, the Mantellan Crux) place doors turned over, and the runtime swings a door about its own
            // up axis from that closed turn, as the donor's action door rotates in its own frame. Its mesh, bounds
            // and collider are therefore all in the model's frame.
            DaggerfallWorldBlockModel? model = document.Models.SingleOrDefault(candidate => candidate.Id == door.ModelId);
            if (model is null || Mesh(model) is not { } mesh || model.LocalBounds is not { } local)
            {
                diagnostics.Add($"Dungeon door '{id}' has no placed mesh and model-local bounds to swing.");
                return;
            }

            DaggerfallDoorVisual visual = new(mesh.Path, mesh.Sha256, [new DaggerfallMeshMaterialBinding(0, 0)]);
            _visualMaterials[mesh.Path] = Textures(mesh);
            try
            {
                _doors.Add(new DaggerfallRdbDoorDefinition(identity, origin + door.Position, door.Rotation, local.Minimum, local.Maximum,
                    door.Kind, door.StartingLockValue, Visual: visual, Action: door.Action)
                {
                    LockSurface = DaggerfallSiteContent.DoorSurface(DaggerfallWorldProfileKind.Dungeon),
                }.Validate());
            }
            catch (ArgumentException exception) { diagnostics.Add($"Dungeon door '{id}' is invalid: {exception.Message}"); }
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
            Remodel(model.ModelId);
            try
            {
                _actionModels.Add(new DaggerfallDungeonActionModelDefinition(place(action.ActionId), doorId, doorIdentity, action.Description,
                    action.ModelIndex, action.RawIndex, new DaggerfallDoorVisual(mesh.Path, mesh.Sha256, [new DaggerfallMeshMaterialBinding(0, 0)]),
                    DaggerfallSiteGeometry.Pose(origin + model.Position, model.Rotation), bounds.Minimum, bounds.Maximum,
                    model.CollisionVertices, model.CollisionTriangles) { ModelId = model.ModelId }.Validate());
            }
            catch (ArgumentException exception) { diagnostics.Add($"Action model '{model.Id}' is invalid: {exception.Message}"); }
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

        /// <summary>A block's billboard, moved as <see cref="Moved"/> moves it, naming the light it sheds as that light is placed.</summary>
        private JsonObject Billboard(JsonNode? billboard, Vector3 origin, Func<string, string> place)
        {
            JsonObject value = Moved(billboard, origin, place);
            if (value["light"] is JsonValue light && light.TryGetValue(out string? id)) value["light"] = place(id);
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
}
