using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using WorldRpg.Kit;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.World;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>
/// Central switch for deterministic seeded people generation, separate from <see cref="DaggerfallGameRandom.Seed"/>.
/// Defaults to zero so seeded townfolk and spawner slots stay stable across games unless a world social seed is chosen.
/// </summary>
internal sealed record DaggerfallSocialTuning(ulong WorldSocialSeed)
{
    internal static DaggerfallSocialTuning Default { get; } = new(0UL);

    internal static DaggerfallSocialTuning Read(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Tuning section 'social' must be a JSON object.");
        ulong seed = element.TryGetProperty("worldSocialSeed", out JsonElement value)
            ? value.GetUInt64()
            : 0UL;
        return new(seed);
    }
}

/// <summary>One selected <c>daggerfall.people</c> pack as the reader takes it.</summary>
internal sealed record DaggerfallPeoplePackFiles(
    string PackId,
    ReadOnlyMemory<byte> Payload,
    ReadOnlyMemory<byte> CastBytes,
    ReadOnlyMemory<byte> SpawnersBytes,
    IReadOnlyList<DaggerfallPeopleBlockFile> BlockFiles);

/// <summary>One block-slot file under a people pack's <c>blocks/</c> folder.</summary>
internal sealed record DaggerfallPeopleBlockFile(string Path, ReadOnlyMemory<byte> Bytes);

/// <summary>Classic sprite visual fallback attached to a people slot or cast individual.</summary>
internal sealed record DaggerfallPeopleVisual(int Archive, int Record, string Gender);

/// <summary>Weighted visual candidate inside a typed spawner pool.</summary>
internal sealed record DaggerfallPeoplePoolEntry(int Archive, int Record, string Gender, int Weight);

/// <summary>Central typed spawner pool resolved by region and <see cref="DaggerfallSocialTuning.WorldSocialSeed"/>.</summary>
internal sealed record DaggerfallPeopleSpawnerDefinition(
    string Id,
    string Role,
    string Interaction,
    string Harm,
    bool DeriveRegionalFaction,
    string VisualMode,
    IReadOnlyList<DaggerfallPeoplePoolEntry> DefaultPool,
    IReadOnlyDictionary<string, IReadOnlyList<DaggerfallPeoplePoolEntry>> RegionalPools);

/// <summary>Genuine unique named individual authored in <c>cast/individuals.json</c>.</summary>
internal sealed record DaggerfallCastIndividualDefinition(
    string Id,
    int FactionId,
    string Name,
    string? Race,
    string Gender,
    int Archive,
    int Record,
    string HomeBlock,
    string HomeSlotId,
    string Interaction,
    string Harm);

/// <summary>One authored people slot in an RMB interior, RMB exterior, or RDB dungeon block.</summary>
internal sealed record DaggerfallPeopleSlotDefinition(
    string SlotId,
    string? CastId,
    string? SpawnerId,
    WorldPoint Position,
    string Interaction,
    int? FactionId,
    bool DeriveRegionalFaction,
    DaggerfallPeopleVisual ClassicVisual,
    int? BuildingType,
    int? BuildingFactionId);

/// <summary>One RMB building's authored interior people slots.</summary>
internal sealed record DaggerfallPeopleBuildingSlots(
    int BuildingIndex,
    int BuildingType,
    int BuildingFactionId,
    IReadOnlyList<DaggerfallPeopleSlotDefinition> Slots);

/// <summary>The admitted <c>daggerfall.people</c> pack: unique cast, central spawners, and block slot templates.</summary>
internal sealed record DaggerfallPeopleSet(
    IReadOnlyDictionary<string, DaggerfallCastIndividualDefinition> Individuals,
    IReadOnlyDictionary<string, DaggerfallPeopleSpawnerDefinition> Spawners,
    IReadOnlyDictionary<string, IReadOnlyDictionary<int, DaggerfallPeopleBuildingSlots>> RmbInteriors,
    IReadOnlyDictionary<string, IReadOnlyList<DaggerfallPeopleSlotDefinition>> RmbExteriors,
    IReadOnlyDictionary<string, IReadOnlyList<DaggerfallPeopleSlotDefinition>> RdbDungeons)
{
    internal static DaggerfallPeopleSet Empty { get; } = new(
        new ReadOnlyDictionary<string, DaggerfallCastIndividualDefinition>(new Dictionary<string, DaggerfallCastIndividualDefinition>(StringComparer.Ordinal)),
        new ReadOnlyDictionary<string, DaggerfallPeopleSpawnerDefinition>(new Dictionary<string, DaggerfallPeopleSpawnerDefinition>(StringComparer.Ordinal)),
        new ReadOnlyDictionary<string, IReadOnlyDictionary<int, DaggerfallPeopleBuildingSlots>>(new Dictionary<string, IReadOnlyDictionary<int, DaggerfallPeopleBuildingSlots>>(StringComparer.Ordinal)),
        new ReadOnlyDictionary<string, IReadOnlyList<DaggerfallPeopleSlotDefinition>>(new Dictionary<string, IReadOnlyList<DaggerfallPeopleSlotDefinition>>(StringComparer.Ordinal)),
        new ReadOnlyDictionary<string, IReadOnlyList<DaggerfallPeopleSlotDefinition>>(new Dictionary<string, IReadOnlyList<DaggerfallPeopleSlotDefinition>>(StringComparer.Ordinal)));

    internal bool TryGetRmbInteriorSlots(string blockSourceKey, int buildingIndex, out DaggerfallPeopleBuildingSlots? building)
    {
        building = null;
        return RmbInteriors.TryGetValue(blockSourceKey, out IReadOnlyDictionary<int, DaggerfallPeopleBuildingSlots>? buildings)
            && buildings.TryGetValue(buildingIndex, out building);
    }

    internal IReadOnlyList<DaggerfallPeopleSlotDefinition> GetRmbExteriorSlots(string blockSourceKey) =>
        RmbExteriors.TryGetValue(blockSourceKey, out IReadOnlyList<DaggerfallPeopleSlotDefinition>? slots) ? slots : [];

    internal IReadOnlyList<DaggerfallPeopleSlotDefinition> GetRdbDungeonSlots(string blockSourceKey) =>
        RdbDungeons.TryGetValue(blockSourceKey, out IReadOnlyList<DaggerfallPeopleSlotDefinition>? slots) ? slots : [];

    internal bool TryGetIndividualByFaction(int factionId, out DaggerfallCastIndividualDefinition? individual)
    {
        if (factionId > 0)
        {
            foreach (DaggerfallCastIndividualDefinition candidate in Individuals.Values)
            {
                if (candidate.FactionId == factionId)
                {
                    individual = candidate;
                    return true;
                }
            }
        }
        individual = null;
        return false;
    }
}

/// <summary>Concrete resolved result for a single <see cref="DaggerfallPeopleSlotDefinition"/> at a site.</summary>
internal sealed record DaggerfallResolvedPeopleSlot(
    long DurableId,
    string SlotKey,
    ulong SlotSeed,
    string? CastId,
    string? SpawnerId,
    string? CastName,
    string Role,
    string Interaction,
    string Harm,
    DaggerfallPeopleVisual Visual,
    int? PrimaryFactionId,
    int? RegionalPeopleFactionId,
    WorldPoint Position);

/// <summary>Reads and validates the owned <c>daggerfall.people</c> content pack.</summary>
internal static class DaggerfallPeopleContent
{
    internal const string CastFile = "cast/individuals.json";
    internal const string SpawnersFile = "spawners.json";
    internal const string BlocksFolder = "blocks";
    private const int IndividualFactionType = 4;

    internal static DaggerfallPeopleSet Read(DaggerfallPeoplePackFiles pack, DaggerfallFactionsSet? factions = null)
    {
        ArgumentNullException.ThrowIfNull(pack);
        DaggerfallContentDiagnostics diagnostics = new();

        // Validate payload manifest pointers.
        try
        {
            using JsonDocument payloadDoc = JsonDocument.Parse(pack.Payload);
            JsonElement root = DaggerfallBaseContent.Object(payloadDoc.RootElement, $"People pack '{pack.PackId}' payload", diagnostics);
            _ = DaggerfallBaseContent.Text(root, "castFile", diagnostics);
            _ = DaggerfallBaseContent.Text(root, "spawnersFile", diagnostics);
            _ = DaggerfallBaseContent.Text(root, "blocksFolder", diagnostics);
        }
        catch (JsonException exception)
        {
            diagnostics.Add($"People pack '{pack.PackId}' payload is malformed: {exception.Message}");
        }

        Dictionary<string, DaggerfallCastIndividualDefinition> individuals = ReadIndividuals(pack.PackId, pack.CastBytes, factions, diagnostics);
        Dictionary<string, DaggerfallPeopleSpawnerDefinition> spawners = ReadSpawners(pack.PackId, pack.SpawnersBytes, diagnostics);

        Dictionary<string, IReadOnlyDictionary<int, DaggerfallPeopleBuildingSlots>> rmbInteriors = new(StringComparer.Ordinal);
        Dictionary<string, IReadOnlyList<DaggerfallPeopleSlotDefinition>> rmbExteriors = new(StringComparer.Ordinal);
        Dictionary<string, IReadOnlyList<DaggerfallPeopleSlotDefinition>> rdbDungeons = new(StringComparer.Ordinal);

        foreach (DaggerfallPeopleBlockFile file in pack.BlockFiles)
            ReadBlockFile(pack.PackId, file, individuals, spawners, factions, rmbInteriors, rmbExteriors, rdbDungeons, diagnostics);

        diagnostics.ThrowIfAny();
        return new(
            new ReadOnlyDictionary<string, DaggerfallCastIndividualDefinition>(individuals),
            new ReadOnlyDictionary<string, DaggerfallPeopleSpawnerDefinition>(spawners),
            new ReadOnlyDictionary<string, IReadOnlyDictionary<int, DaggerfallPeopleBuildingSlots>>(rmbInteriors),
            new ReadOnlyDictionary<string, IReadOnlyList<DaggerfallPeopleSlotDefinition>>(rmbExteriors),
            new ReadOnlyDictionary<string, IReadOnlyList<DaggerfallPeopleSlotDefinition>>(rdbDungeons));
    }

    private static Dictionary<string, DaggerfallCastIndividualDefinition> ReadIndividuals(
        string packId, ReadOnlyMemory<byte> bytes, DaggerfallFactionsSet? factions, DaggerfallContentDiagnostics diagnostics)
    {
        Dictionary<string, DaggerfallCastIndividualDefinition> result = new(StringComparer.Ordinal);
        try
        {
            using JsonDocument doc = JsonDocument.Parse(bytes);
            JsonElement root = DaggerfallBaseContent.Object(doc.RootElement, $"People pack '{packId}' cast", diagnostics);
            foreach (JsonElement item in DaggerfallBaseContent.Array(root, "individuals", diagnostics))
            {
                JsonElement obj = DaggerfallBaseContent.Object(item, "cast individual", diagnostics);
                string id = DaggerfallBaseContent.Text(obj, "id", diagnostics);
                int factionId = DaggerfallBaseContent.Integer(obj, "factionId", diagnostics);
                string name = DaggerfallBaseContent.Text(obj, "name", diagnostics);
                string? race = obj.TryGetProperty("race", out JsonElement raceEl) && raceEl.ValueKind != JsonValueKind.Null
                    ? raceEl.GetString()
                    : null;
                string gender = DaggerfallBaseContent.Text(obj, "gender", diagnostics);
                JsonElement visual = DaggerfallBaseContent.Object(DaggerfallBaseContent.Property(obj, "visual", diagnostics), $"cast '{id}' visual", diagnostics);
                int archive = DaggerfallBaseContent.Integer(visual, "archive", diagnostics);
                int record = DaggerfallBaseContent.Integer(visual, "record", diagnostics);
                string homeBlock = DaggerfallBaseContent.Text(obj, "homeBlock", diagnostics);
                string homeSlotId = DaggerfallBaseContent.Text(obj, "homeSlotId", diagnostics);
                string interaction = DaggerfallBaseContent.Text(obj, "interaction", diagnostics);
                string harm = DaggerfallBaseContent.Text(obj, "harm", diagnostics);

                if (!DaggerfallBaseContent.ValidId(id)) diagnostics.Add($"Cast individual '{id}' has an invalid id.");
                if (gender is not ("Male" or "Female")) diagnostics.Add($"Cast individual '{id}' has invalid gender '{gender}'.");
                if (!ValidInteraction(interaction)) diagnostics.Add($"Cast individual '{id}' has invalid interaction '{interaction}'.");
                if (!ValidHarm(harm)) diagnostics.Add($"Cast individual '{id}' has invalid harm '{harm}'.");
                if (archive < 0 || record < 0 || factionId <= 0) diagnostics.Add($"Cast individual '{id}' has invalid visual or factionId.");
                if (factions is not null)
                {
                    if (!factions.Factions.TryGetValue(factionId, out DaggerfallFactionDefinition? faction))
                        diagnostics.Add($"Cast individual '{id}' references missing faction {factionId}.");
                    else if (faction.Type != IndividualFactionType)
                        diagnostics.Add($"Cast individual '{id}' references faction {factionId} ('{faction.Name}') of type {faction.Type}, expected Individual (4).");
                }
                if (!result.TryAdd(id, new(id, factionId, name, race, gender, archive, record, homeBlock, homeSlotId, interaction, harm)))
                    diagnostics.Add($"People pack '{packId}' repeats cast individual '{id}'.");
            }
        }
        catch (JsonException exception)
        {
            diagnostics.Add($"People pack '{packId}' cast file is malformed: {exception.Message}");
        }
        return result;
    }

    private static Dictionary<string, DaggerfallPeopleSpawnerDefinition> ReadSpawners(
        string packId, ReadOnlyMemory<byte> bytes, DaggerfallContentDiagnostics diagnostics)
    {
        Dictionary<string, DaggerfallPeopleSpawnerDefinition> result = new(StringComparer.Ordinal);
        try
        {
            using JsonDocument doc = JsonDocument.Parse(bytes);
            JsonElement root = DaggerfallBaseContent.Object(doc.RootElement, $"People pack '{packId}' spawners", diagnostics);
            foreach (JsonElement item in DaggerfallBaseContent.Array(root, "spawners", diagnostics))
            {
                JsonElement obj = DaggerfallBaseContent.Object(item, "people spawner", diagnostics);
                string id = DaggerfallBaseContent.Text(obj, "id", diagnostics);
                string role = DaggerfallBaseContent.Text(obj, "role", diagnostics);
                string interaction = DaggerfallBaseContent.Text(obj, "interaction", diagnostics);
                string harm = DaggerfallBaseContent.Text(obj, "harm", diagnostics);
                bool deriveRegional = obj.TryGetProperty("deriveRegionalFaction", out JsonElement drv) && drv.ValueKind == JsonValueKind.True;
                string visualMode = DaggerfallBaseContent.Text(obj, "visualMode", diagnostics);

                if (!DaggerfallBaseContent.ValidId(id)) diagnostics.Add($"People spawner '{id}' has an invalid id.");
                if (!ValidInteraction(interaction)) diagnostics.Add($"People spawner '{id}' has invalid interaction '{interaction}'.");
                if (!ValidHarm(harm)) diagnostics.Add($"People spawner '{id}' has invalid harm '{harm}'.");
                if (visualMode is not ("classic-slot" or "pool")) diagnostics.Add($"People spawner '{id}' has invalid visualMode '{visualMode}'.");

                IReadOnlyList<DaggerfallPeoplePoolEntry> defaultPool = ReadPool(
                    DaggerfallBaseContent.Array(obj, "defaultPool", diagnostics), $"spawner '{id}' defaultPool", diagnostics);

                Dictionary<string, IReadOnlyList<DaggerfallPeoplePoolEntry>> regionalPools = new(StringComparer.OrdinalIgnoreCase);
                if (obj.TryGetProperty("regionalPools", out JsonElement regionalObj) && regionalObj.ValueKind == JsonValueKind.Object)
                {
                    foreach (JsonProperty regionProp in regionalObj.EnumerateObject())
                    {
                        IReadOnlyList<DaggerfallPeoplePoolEntry> pool = ReadPool(
                            regionProp.Value.ValueKind == JsonValueKind.Array ? regionProp.Value.EnumerateArray() : [],
                            $"spawner '{id}' regionalPools.{regionProp.Name}", diagnostics);
                        if (regionProp.Value.ValueKind != JsonValueKind.Array)
                            diagnostics.Add($"Spawner '{id}' regionalPools.'{regionProp.Name}' must be an array.");
                        else
                            regionalPools[regionProp.Name] = pool;
                    }
                }

                if (!result.TryAdd(id, new(id, role, interaction, harm, deriveRegional, visualMode, defaultPool,
                        new ReadOnlyDictionary<string, IReadOnlyList<DaggerfallPeoplePoolEntry>>(regionalPools))))
                    diagnostics.Add($"People pack '{packId}' repeats spawner '{id}'.");
            }
        }
        catch (JsonException exception)
        {
            diagnostics.Add($"People pack '{packId}' spawners file is malformed: {exception.Message}");
        }
        return result;
    }

    private static IReadOnlyList<DaggerfallPeoplePoolEntry> ReadPool(
        IEnumerable<JsonElement> array, string context, DaggerfallContentDiagnostics diagnostics)
    {
        List<DaggerfallPeoplePoolEntry> entries = [];
        foreach (JsonElement item in array)
        {
            JsonElement obj = DaggerfallBaseContent.Object(item, context, diagnostics);
            int archive = DaggerfallBaseContent.Integer(obj, "archive", diagnostics);
            int record = DaggerfallBaseContent.Integer(obj, "record", diagnostics);
            string gender = DaggerfallBaseContent.Text(obj, "gender", diagnostics);
            int weight = DaggerfallBaseContent.Integer(obj, "weight", diagnostics);
            if (archive < 0 || record < 0 || weight <= 0 || gender is not ("Male" or "Female"))
                diagnostics.Add($"{context} has an invalid pool entry ({archive}.{record}, gender='{gender}', weight={weight}).");
            else
                entries.Add(new(archive, record, gender, weight));
        }
        return entries.AsReadOnly();
    }

    private static void ReadBlockFile(
        string packId,
        DaggerfallPeopleBlockFile file,
        IReadOnlyDictionary<string, DaggerfallCastIndividualDefinition> individuals,
        IReadOnlyDictionary<string, DaggerfallPeopleSpawnerDefinition> spawners,
        DaggerfallFactionsSet? factions,
        Dictionary<string, IReadOnlyDictionary<int, DaggerfallPeopleBuildingSlots>> rmbInteriors,
        Dictionary<string, IReadOnlyList<DaggerfallPeopleSlotDefinition>> rmbExteriors,
        Dictionary<string, IReadOnlyList<DaggerfallPeopleSlotDefinition>> rdbDungeons,
        DaggerfallContentDiagnostics diagnostics)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(file.Bytes);
            JsonElement root = DaggerfallBaseContent.Object(doc.RootElement, $"People block file '{file.Path}'", diagnostics);
            string kind = DaggerfallBaseContent.Text(root, "kind", diagnostics);
            JsonElement blocksObj = DaggerfallBaseContent.Object(DaggerfallBaseContent.Property(root, "blocks", diagnostics), $"People block file '{file.Path}' blocks", diagnostics);

            foreach (JsonProperty blockProp in blocksObj.EnumerateObject())
            {
                string blockKey = blockProp.Name;
                if (kind == "rmb-interior")
                {
                    if (blockProp.Value.ValueKind != JsonValueKind.Object)
                    {
                        diagnostics.Add($"People block '{blockKey}' in '{file.Path}' must map building indices to objects.");
                        continue;
                    }
                    Dictionary<int, DaggerfallPeopleBuildingSlots> buildings = [];
                    foreach (JsonProperty buildingProp in blockProp.Value.EnumerateObject())
                    {
                        if (!int.TryParse(buildingProp.Name, NumberStyles.None, CultureInfo.InvariantCulture, out int buildingIndex))
                        {
                            diagnostics.Add($"People block '{blockKey}' has non-integer building key '{buildingProp.Name}'.");
                            continue;
                        }
                        JsonElement buildingObj = DaggerfallBaseContent.Object(buildingProp.Value, $"block '{blockKey}' building {buildingIndex}", diagnostics);
                        int buildingType = DaggerfallBaseContent.Integer(buildingObj, "buildingType", diagnostics);
                        int buildingFactionId = DaggerfallBaseContent.Integer(buildingObj, "buildingFactionId", diagnostics);
                        IReadOnlyList<DaggerfallPeopleSlotDefinition> slots = ReadSlots(
                            DaggerfallBaseContent.Array(buildingObj, "slots", diagnostics),
                            $"{blockKey}[{buildingIndex}]", individuals, spawners, factions, diagnostics);
                        buildings[buildingIndex] = new(buildingIndex, buildingType, buildingFactionId, slots);
                    }
                    if (!rmbInteriors.TryAdd(blockKey, new ReadOnlyDictionary<int, DaggerfallPeopleBuildingSlots>(buildings)))
                        diagnostics.Add($"People pack '{packId}' repeats rmb-interior block '{blockKey}'.");
                }
                else if (kind is "rmb-exterior" or "rdb-dungeon")
                {
                    if (blockProp.Value.ValueKind != JsonValueKind.Array)
                    {
                        diagnostics.Add($"People block '{blockKey}' in '{file.Path}' must map to a slot array.");
                        continue;
                    }
                    IReadOnlyList<DaggerfallPeopleSlotDefinition> slots = ReadSlots(
                        blockProp.Value.EnumerateArray(), blockKey, individuals, spawners, factions, diagnostics);
                    Dictionary<string, IReadOnlyList<DaggerfallPeopleSlotDefinition>> target =
                        kind == "rmb-exterior" ? rmbExteriors : rdbDungeons;
                    if (!target.TryAdd(blockKey, slots))
                        diagnostics.Add($"People pack '{packId}' repeats {kind} block '{blockKey}'.");
                }
                else
                {
                    diagnostics.Add($"People block file '{file.Path}' has unsupported kind '{kind}'.");
                    return;
                }
            }
        }
        catch (JsonException exception)
        {
            diagnostics.Add($"People block file '{file.Path}' is malformed: {exception.Message}");
        }
    }

    private static IReadOnlyList<DaggerfallPeopleSlotDefinition> ReadSlots(
        IEnumerable<JsonElement> array,
        string scope,
        IReadOnlyDictionary<string, DaggerfallCastIndividualDefinition> individuals,
        IReadOnlyDictionary<string, DaggerfallPeopleSpawnerDefinition> spawners,
        DaggerfallFactionsSet? factions,
        DaggerfallContentDiagnostics diagnostics)
    {
        List<DaggerfallPeopleSlotDefinition> result = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (JsonElement item in array)
        {
            JsonElement obj = DaggerfallBaseContent.Object(item, $"{scope} slot", diagnostics);
            string slotId = DaggerfallBaseContent.Text(obj, "slotId", diagnostics);
            if (string.IsNullOrWhiteSpace(slotId) || !seen.Add(slotId))
                diagnostics.Add($"Block '{scope}' has missing or duplicate slotId '{slotId}'.");

            string? castId = obj.TryGetProperty("castId", out JsonElement castEl) && castEl.ValueKind == JsonValueKind.String ? castEl.GetString() : null;
            string? spawnerId = obj.TryGetProperty("spawner", out JsonElement spEl) && spEl.ValueKind == JsonValueKind.String ? spEl.GetString() : null;
            if ((castId is null) == (spawnerId is null))
                diagnostics.Add($"Slot '{scope}/{slotId}' must specify exactly one of castId or spawner.");
            else if (castId is not null && !individuals.ContainsKey(castId))
                diagnostics.Add($"Slot '{scope}/{slotId}' references unknown castId '{castId}'.");
            else if (spawnerId is not null && !spawners.ContainsKey(spawnerId))
                diagnostics.Add($"Slot '{scope}/{slotId}' references unknown spawner '{spawnerId}'.");

            WorldPoint position = ReadPosition(DaggerfallBaseContent.Property(obj, "position", diagnostics), $"{scope}/{slotId}", diagnostics);
            string interaction = DaggerfallBaseContent.Text(obj, "interaction", diagnostics);
            if (!ValidInteraction(interaction))
                diagnostics.Add($"Slot '{scope}/{slotId}' has invalid interaction '{interaction}'.");

            int? factionId = obj.TryGetProperty("factionId", out JsonElement fEl) && fEl.ValueKind == JsonValueKind.Number ? fEl.GetInt32() : null;
            bool deriveRegional = obj.TryGetProperty("deriveRegionalFaction", out JsonElement drvEl) && drvEl.ValueKind == JsonValueKind.True;

            if (factionId is int fid && factions is not null)
            {
                if (!factions.Factions.TryGetValue(fid, out DaggerfallFactionDefinition? faction))
                    diagnostics.Add($"Slot '{scope}/{slotId}' references missing faction {fid}.");
                else if (castId is null && faction.Type == IndividualFactionType)
                    diagnostics.Add($"Non-cast spawner slot '{scope}/{slotId}' illegally carries Type-4 Individual faction {fid} ('{faction.Name}').");
            }

            JsonElement vis = DaggerfallBaseContent.Object(DaggerfallBaseContent.Property(obj, "visual", diagnostics), $"{scope}/{slotId} visual", diagnostics);
            int archive = DaggerfallBaseContent.Integer(vis, "archive", diagnostics);
            int record = DaggerfallBaseContent.Integer(vis, "record", diagnostics);
            string gender = DaggerfallBaseContent.Text(vis, "gender", diagnostics);
            if (archive < 0 || record < 0 || gender is not ("Male" or "Female"))
                diagnostics.Add($"Slot '{scope}/{slotId}' has invalid visual ({archive}.{record}, '{gender}').");

            int? buildingType = obj.TryGetProperty("buildingType", out JsonElement btEl) && btEl.ValueKind == JsonValueKind.Number ? btEl.GetInt32() : null;
            int? buildingFactionId = obj.TryGetProperty("buildingFactionId", out JsonElement bfEl) && bfEl.ValueKind == JsonValueKind.Number ? bfEl.GetInt32() : null;

            result.Add(new(slotId, castId, spawnerId, position, interaction, factionId, deriveRegional,
                new DaggerfallPeopleVisual(archive, record, gender), buildingType, buildingFactionId));
        }
        return result.AsReadOnly();
    }

    private static WorldPoint ReadPosition(JsonElement element, string context, DaggerfallContentDiagnostics diagnostics)
    {
        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() != 3)
        {
            diagnostics.Add($"Slot '{context}' position must be a 3-element array.");
            return default;
        }
        float x = element[0].GetSingle();
        float y = element[1].GetSingle();
        float z = element[2].GetSingle();
        if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z))
        {
            diagnostics.Add($"Slot '{context}' position has a non-finite coordinate.");
            return default;
        }
        return new(x, y, z);
    }

    private static bool ValidInteraction(string value) => value is "none" or "ambient-bark" or "full-dialogue";
    private static bool ValidHarm(string value) => value is "protected" or "law-protected-civilian" or "combatant";
}

/// <summary>
/// Deterministic slot-derived durable IDs, independent per-slot hashing (<c>Hash64(WorldSocialSeed, SiteId, SlotKey)</c>),
/// and regional typed spawner resolution shared by block spawner slots and exterior walkers.
/// </summary>
internal static class DaggerfallPeopleSlots
{
    private const ulong HighBandMask = 0x3fff_ffff_ffffUL;
    private const long HighBandBit = 0x4000_0000_0000L;
    private const int RegionalPeopleFactionType = 15;

    /// <summary>
    /// Whether billboard texture (<paramref name="archive"/>, <paramref name="record"/>) is a person flat
    /// that must materialize as a real actor rather than a decorative scenery billboard.
    /// Excludes non-person prop <c>(197, 6)</c>.
    /// </summary>
    internal static bool IsPersonClutterBillboard(int archive, int record) =>
        archive is (>= 175 and <= 184) or 186 or 197 or 334 or 346 or 357
        && !(archive == 197 && record == 6);

    internal static ActorInteractionPolicyKind ParseInteractionPolicy(string? value) => value switch
    {
        "none" => ActorInteractionPolicyKind.None,
        "ambient-bark" => ActorInteractionPolicyKind.AmbientBark,
        _ => ActorInteractionPolicyKind.FullDialogue,
    };

    internal static ActorHarmPolicyKind ParseHarmPolicy(string? value, ActorHarmPolicyKind fallback = ActorHarmPolicyKind.Protected) => value switch
    {
        "protected" => ActorHarmPolicyKind.Protected,
        "law-protected-civilian" => ActorHarmPolicyKind.LawProtectedCivilian,
        "combatant" => ActorHarmPolicyKind.Combatant,
        _ => fallback,
    };

    /// <summary>Canonical site-qualified slot key for an exterior RMB block person slot at grid cell (<paramref name="blockX"/>, <paramref name="blockY"/>).</summary>
    internal static string ExteriorBlockSlotKey(int blockX, int blockY, string templateSlotId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateSlotId);
        return string.Create(CultureInfo.InvariantCulture, $"ext:{blockX},{blockY}:{templateSlotId}");
    }

    /// <summary>Canonical site-qualified slot key for an interior RMB building slot placed on grid cell (<paramref name="blockX"/>, <paramref name="blockY"/>).</summary>
    internal static string InteriorBuildingSlotKey(int blockX, int blockY, string templateSlotId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateSlotId);
        return string.Create(CultureInfo.InvariantCulture, $"int:{blockX},{blockY}:{templateSlotId}");
    }

    /// <summary>Canonical site-qualified slot key for an RDB dungeon block slot placed at grid cell (<paramref name="blockX"/>, <paramref name="blockZ"/>).</summary>
    internal static string DungeonBlockSlotKey(int blockX, int blockZ, string templateSlotId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateSlotId);
        return string.Create(CultureInfo.InvariantCulture, $"dng:{blockX},{blockZ}:{templateSlotId}");
    }

    /// <summary>Canonical slot key for a procedural exterior walker at <paramref name="index"/>.</summary>
    internal static string WalkerSlotKey(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        return string.Create(CultureInfo.InvariantCulture, $"w:{index:D3}");
    }

    /// <summary>
    /// Deterministic durable actor ID for any <c>SeededSlot(SiteId, SlotKey)</c>, in the same <c>0x4000_0000_0000L</c>
    /// high-bit band as <see cref="DaggerfallSiteContent.FixedActorIdentity"/> and disjoint from monotonic allocator IDs.
    /// </summary>
    internal static long SeededSlotIdentity(DaggerfallSiteId site, string slotKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slotKey);
        return (long)(StableHash.NonZeroFnv1a64($"daggerfall.seeded-slot:{site.Region}:{site.Index}:{slotKey}") & HighBandMask) | HighBandBit;
    }

    /// <summary>Deterministic durable actor ID for a unique <c>cast/individuals.json</c> individual.</summary>
    internal static long CastIndividualIdentity(string castId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(castId);
        return (long)(StableHash.NonZeroFnv1a64($"daggerfall.cast:{castId}") & HighBandMask) | HighBandBit;
    }

    /// <summary>Whether <paramref name="durableId"/> sits in the deterministic high-bit band (seeded slot, cast individual, or fixed placement).</summary>
    internal static bool IsDeterministicId(long durableId) => durableId > 0 && (durableId & HighBandBit) != 0;

    /// <summary>
    /// Independent per-slot 64-bit seed derived strictly from <paramref name="worldSocialSeed"/>, <paramref name="site"/>,
    /// and <paramref name="slotKey"/> — completely separate from <see cref="DaggerfallGameRandom.Seed"/>.
    /// </summary>
    internal static ulong SlotSeed(ulong worldSocialSeed, DaggerfallSiteId site, string slotKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slotKey);
        ulong slotHash = StableHash.NonZeroFnv1a64($"daggerfall.slot-seed:{site.Region}:{site.Index}:{slotKey}");
        ulong z = unchecked(slotHash ^ DaggerfallGameRandom.Mix(worldSocialSeed, 0x9E3779B97F4A7C15UL));
        z = unchecked((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL);
        z = unchecked((z ^ (z >> 27)) * 0x94D049BB133111EBUL);
        return z ^ (z >> 31);
    }

    /// <summary>The region's people faction (<c>Type == 15</c>), used for both exterior walkers and interior regional people.</summary>
    internal static DaggerfallFactionDefinition? ResolveRegionalPeopleFaction(DaggerfallFactionsSet factions, int region)
    {
        ArgumentNullException.ThrowIfNull(factions);
        return factions.Factions.Values
            .Where(faction => faction.Type == RegionalPeopleFactionType && faction.Region == region)
            .OrderBy(faction => faction.Id)
            .FirstOrDefault();
    }

    /// <summary>
    /// Resolves a block slot against <paramref name="people"/>, <paramref name="factions"/>, <paramref name="worldSocialSeed"/>,
    /// <paramref name="site"/>, and its canonical site-qualified <paramref name="placementSlotKey"/> (which must qualify
    /// the placed block/building so repeated or distinct block templates at one site never collide).
    /// Regional spawner pools override <c>defaultPool</c>; otherwise <c>visualMode == "pool"</c> draws from <c>defaultPool</c>
    /// and <c>visualMode == "classic-slot"</c> (or an empty pool) falls back to the slot's classic visual.
    /// </summary>
    internal static DaggerfallResolvedPeopleSlot ResolveSlot(
        DaggerfallPeopleSet people,
        DaggerfallFactionsSet factions,
        ulong worldSocialSeed,
        DaggerfallSiteId site,
        string placementSlotKey,
        DaggerfallPeopleSlotDefinition slot,
        string? regionKey = null)
    {
        ArgumentNullException.ThrowIfNull(people);
        ArgumentNullException.ThrowIfNull(factions);
        ArgumentException.ThrowIfNullOrWhiteSpace(placementSlotKey);
        ArgumentNullException.ThrowIfNull(slot);

        ulong seed = SlotSeed(worldSocialSeed, site, placementSlotKey);

        if (slot.CastId is { } castId)
        {
            DaggerfallCastIndividualDefinition cast = people.Individuals[castId];
            return new(
                CastIndividualIdentity(castId),
                placementSlotKey,
                seed,
                castId,
                SpawnerId: null,
                CastName: cast.Name,
                Role: "person",
                Interaction: cast.Interaction,
                Harm: cast.Harm,
                Visual: new DaggerfallPeopleVisual(cast.Archive, cast.Record, cast.Gender),
                PrimaryFactionId: cast.FactionId,
                RegionalPeopleFactionId: null,
                Position: slot.Position);
        }

        DaggerfallPeopleSpawnerDefinition spawner = people.Spawners[slot.SpawnerId!];
        DaggerfallPeopleVisual visual = PickVisual(spawner, slot.ClassicVisual, seed, site.Region, regionKey);
        int? regionalFactionId = slot.DeriveRegionalFaction || spawner.DeriveRegionalFaction
            ? ResolveRegionalPeopleFaction(factions, site.Region)?.Id
            : null;
        int? primaryFactionId = slot.FactionId ?? regionalFactionId;

        return new(
            SeededSlotIdentity(site, placementSlotKey),
            placementSlotKey,
            seed,
            CastId: null,
            SpawnerId: spawner.Id,
            CastName: null,
            Role: spawner.Role,
            Interaction: slot.Interaction,
            Harm: spawner.Harm,
            Visual: visual,
            PrimaryFactionId: primaryFactionId,
            RegionalPeopleFactionId: regionalFactionId,
            Position: slot.Position);
    }

    private static DaggerfallPeopleVisual PickVisual(
        DaggerfallPeopleSpawnerDefinition spawner,
        DaggerfallPeopleVisual fallback,
        ulong seed,
        int regionIndex,
        string? regionKey)
    {
        IReadOnlyList<DaggerfallPeoplePoolEntry>? pool = null;
        if (!string.IsNullOrWhiteSpace(regionKey) && spawner.RegionalPools.TryGetValue(regionKey, out IReadOnlyList<DaggerfallPeoplePoolEntry>? byName) && byName.Count > 0)
            pool = byName;
        else if (spawner.RegionalPools.TryGetValue(regionIndex.ToString(CultureInfo.InvariantCulture), out IReadOnlyList<DaggerfallPeoplePoolEntry>? byIndex) && byIndex.Count > 0)
            pool = byIndex;
        else if (spawner.VisualMode == "pool" && spawner.DefaultPool.Count > 0)
            pool = spawner.DefaultPool;

        if (pool is null || pool.Count == 0)
            return fallback;

        long totalWeight = 0;
        for (int i = 0; i < pool.Count; i++) totalWeight += pool[i].Weight;
        long pick = (long)(seed % (ulong)totalWeight);
        long cumulative = 0;
        for (int i = 0; i < pool.Count; i++)
        {
            cumulative += pool[i].Weight;
            if (pick < cumulative)
                return new DaggerfallPeopleVisual(pool[i].Archive, pool[i].Record, pool[i].Gender);
        }
        return new DaggerfallPeopleVisual(pool[^1].Archive, pool[^1].Record, pool[^1].Gender);
    }
}
