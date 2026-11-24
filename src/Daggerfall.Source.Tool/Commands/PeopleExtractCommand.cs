using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalization;
using Daggerfall.Import.Normalized;

namespace Daggerfall.Source.Tool.Commands;

/// <summary>
/// One-time extractor that turns Arena2's RMB/RDB people placements and person clutter flats into the
/// git-tracked <c>content/worldrpg/packs/daggerfall.people/</c> pack: genuine unique individuals in
/// <c>cast/individuals.json</c>, central regional typed spawner pools in <c>spawners.json</c>, and
/// revision-proof block slot templates in <c>blocks/</c>. Lives in <c>Daggerfall.Source.Tool</c> so
/// editing or running it never moves the importer revision.
/// </summary>
internal static class PeopleExtractCommand
{
    private static readonly HashSet<int> StaticPeopleArchives =
    [
        175, 176, 177, 178, 179, 180, 181, 182, 183, 184, 186, 197, 334, 346, 357,
    ];

    // Cloned generic-block Type-4 (Individual) factions audited in #11012 (347 placements across generic town blocks).
    private static readonly HashSet<int> DemotedClonedIndividuals = [302, 360, 373, 388, 404, 406, 407];

    private const int IndividualFactionType = 4;
    private const int WitchesFactionType = 8;
    private const int KnightlyGuardFactionType = 10;
    private const int MerchantSocialGroup = 1;
    private const int NobilitySocialGroup = 4;
    private const int MolagBalFactionId = 3;
    private const int ChildrenFactionId = 514;
    private const int DaedraArchive = 175;

    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };

    public static ToolCommand Command { get; } = new("people-extract",
    [
        Options.Arena2,
        CommandOption.Staged("--pack", "PACK_DIR", "content/worldrpg/packs/daggerfall.people"),
        Options.Update,
    ], Run, Program.Tool);

    private static int Run(CommandArguments args)
    {
        string arena2 = Path.GetFullPath(args[Options.Arena2.Name]);
        string packDir = Path.GetFullPath(args["--pack"]);

        byte[] blocksBytes = File.ReadAllBytes(Path.Combine(arena2, "BLOCKS.BSA"));
        BsaArchive bsa = BsaArchive.Parse(blocksBytes, Options.Arena2Label("BLOCKS.BSA"));
        IReadOnlyList<BlockRecord> blocks = BlockRecordInventoryReader.Read(blocksBytes, Options.Arena2Label("BLOCKS.BSA")).Records;
        Dictionary<int, ClassicFaction> factions = FactionReader
            .Read(File.ReadAllText(Path.Combine(arena2, "FACTION.TXT")), Options.Arena2Label("FACTION.TXT"))
            .ToDictionary(faction => faction.Id);
        Dictionary<(int Archive, int Record), string> flatGenders = ReadFlatsCfgGenders(Path.Combine(arena2, "FLATS.CFG"));

        Dictionary<int, CastEntry> castByFaction = [];
        Dictionary<string, SpawnerAccumulator> spawners = new(StringComparer.Ordinal);
        List<BlockSlotsDocument> rmbInteriors = [];
        List<BlockSlotsDocument> rmbExteriors = [];
        List<BlockSlotsDocument> rdbDungeons = [];

        int exteriorPeopleCount = 0;
        int demotedCount = 0;
        int molagTypoCount = 0;
        int unmappedFactionCount = 0;
        int nonPeopleFactionFlatCount = 0;
        int interiorTalkableCount = 0;
        int interiorClutterCount = 0;
        int exteriorTalkableCount = 0;
        int exteriorClutterCount = 0;
        int rdbTalkableCount = 0;
        int rdbClutterCount = 0;
        int castPlacements = 0;
        Dictionary<int, int> demotedByFaction = [];

        foreach (BlockRecord block in blocks.OrderBy(record => record.SourceKey, StringComparer.Ordinal))
        {
            if (block.RmbPlacements is { } rmb && block.RmbHeader is { } summary)
            {
                RmbBlockContent rmbContent = RmbBlockContent.Read(bsa, block.SourceKey);

                // Verify dead building.Exterior.People remains 0 across Arena2.
                exteriorPeopleCount += rmb.Buildings.Sum(building => building.Exterior.People.Count);

                // 1. RMB Interiors (per building)
                List<BuildingSlots> buildingsOut = [];
                for (int buildingIndex = 0; buildingIndex < summary.Buildings.Count; buildingIndex++)
                {
                    RmbBuildingSlot buildingSlot = summary.Buildings[buildingIndex];
                    RmbHalfPlacements interior = rmb.Buildings[buildingIndex].Interior;
                    List<SlotEntry> buildingSlots = [];

                    for (int ordinal = 0; ordinal < interior.People.Count; ordinal++)
                    {
                        RmbPeoplePlacement person = interior.People[ordinal];
                        NormalizedVector3 position = MeshGeometry.ToRightHanded(
                            Arena2SourceTransform.ToRmbImportPoint(person.X, person.Y, person.Z));
                        string gender = (person.Flags & 32) != 0 ? "Female" : "Male";
                        string slotId = string.Create(CultureInfo.InvariantCulture, $"b:{buildingIndex}:p{ordinal:D2}");

                        SlotEntry slot = BuildSlot(
                            block.SourceKey, slotId, position,
                            person.TextureArchive, person.TextureRecord, gender,
                            person.FactionId, isInterior: true, isClutter: false, isDungeon: false,
                            buildingSlot.BuildingType, buildingSlot.FactionId,
                            factions, castByFaction, spawners,
                            ref demotedCount, ref molagTypoCount, ref unmappedFactionCount, ref castPlacements, demotedByFaction);
                        buildingSlots.Add(slot);
                        interiorTalkableCount++;
                    }

                    IReadOnlyList<RmbBlockFlat> interiorFlats = rmbContent.InteriorFlats(buildingIndex);
                    int clutterOrdinal = 0;
                    foreach (RmbBlockFlat flat in interiorFlats)
                    {
                        if (!StaticPeopleArchives.Contains(flat.TextureArchive)) continue;
                        NormalizedVector3 position = MeshGeometry.ToRightHanded(flat.Point);
                        string gender = flatGenders.GetValueOrDefault((flat.TextureArchive, flat.TextureRecord))
                            ?? ((flat.Source.Flags & 32) != 0 ? "Female" : "Male");
                        string slotId = string.Create(CultureInfo.InvariantCulture, $"b:{buildingIndex}:c{clutterOrdinal:D2}");
                        clutterOrdinal++;

                        SlotEntry slot = BuildSlot(
                            block.SourceKey, slotId, position,
                            flat.TextureArchive, flat.TextureRecord, gender,
                            flat.Source.FactionId, isInterior: true, isClutter: true, isDungeon: false,
                            buildingSlot.BuildingType, buildingSlot.FactionId,
                            factions, castByFaction, spawners,
                            ref demotedCount, ref molagTypoCount, ref unmappedFactionCount, ref castPlacements, demotedByFaction);
                        buildingSlots.Add(slot);
                        interiorClutterCount++;
                    }

                    if (buildingSlots.Count > 0)
                    {
                        buildingsOut.Add(new BuildingSlots(
                            buildingIndex, buildingSlot.BuildingType, buildingSlot.FactionId, buildingSlots));
                    }
                }

                if (buildingsOut.Count > 0)
                    rmbInteriors.Add(new BlockSlotsDocument(block.SourceKey, buildingsOut, null));

                // 2. RMB Exteriors (building.Exterior.Flats + MiscFlats)
                List<SlotEntry> exteriorSlots = [];
                int extTalkableOrdinal = 0;
                int extClutterOrdinal = 0;
                foreach (RmbBlockFlat flat in rmbContent.ExteriorFlats)
                {
                    if (flat.TextureArchive == RdbSourceClassification.EditorFlatArchive) continue;
                    bool isPersonArchive = StaticPeopleArchives.Contains(flat.TextureArchive);
                    if (!isPersonArchive)
                    {
                        if (flat.Source.FactionId != 0) nonPeopleFactionFlatCount++;
                        continue;
                    }

                    bool isClutter = flat.Source.FactionId == 0;
                    NormalizedVector3 position = MeshGeometry.ToRightHanded(flat.Point);
                    string gender = flat.TextureArchive == 179
                        ? "Female"
                        : flatGenders.GetValueOrDefault((flat.TextureArchive, flat.TextureRecord))
                            ?? ((flat.Source.Flags & 32) != 0 ? "Female" : "Male");
                    string slotId = isClutter
                        ? string.Create(CultureInfo.InvariantCulture, $"e:c{extClutterOrdinal++:D2}")
                        : string.Create(CultureInfo.InvariantCulture, $"e:p{extTalkableOrdinal++:D2}");
                    RmbBuildingSlot? hostBuilding = flat.BuildingIndex is int bi ? summary.Buildings[bi] : null;

                    SlotEntry slot = BuildSlot(
                        block.SourceKey, slotId, position,
                        flat.TextureArchive, flat.TextureRecord, gender,
                        flat.Source.FactionId, isInterior: false, isClutter: isClutter, isDungeon: false,
                        hostBuilding?.BuildingType ?? -1, hostBuilding?.FactionId ?? 0,
                        factions, castByFaction, spawners,
                        ref demotedCount, ref molagTypoCount, ref unmappedFactionCount, ref castPlacements, demotedByFaction);
                    exteriorSlots.Add(slot);
                    if (isClutter) exteriorClutterCount++;
                    else exteriorTalkableCount++;
                }

                if (exteriorSlots.Count > 0)
                    rmbExteriors.Add(new BlockSlotsDocument(block.SourceKey, null, exteriorSlots));
            }
            else if (block.Kind == BlockRecordKind.Rdb && block.Objects is { } rdb)
            {
                List<SlotEntry> rdbSlots = [];
                int rdbTalkableOrdinal = 0;
                int rdbClutterOrdinal = 0;
                foreach (RdbFlatPlacement flat in rdb.FlatPlacements)
                {
                    bool isDungeonNpc = StaticNpcNormalization.IsDungeonNpc(flat.TextureArchive);
                    bool isClutterArchive = !isDungeonNpc && flat.TextureArchive is 186 or 197;
                    if (!isDungeonNpc && !isClutterArchive) continue;

                    NormalizedVector3 position = MeshGeometry.ToRightHanded(
                        Arena2SourceTransform.ToImportPoint(flat.X, flat.Y, flat.Z));
                    string gender = isClutterArchive
                        ? flatGenders.GetValueOrDefault((flat.TextureArchive, flat.TextureRecord))
                            ?? ((flat.Flags & 32) != 0 ? "Female" : "Male")
                        : (flat.Flags & 32) != 0 ? "Female" : "Male";
                    string slotId = isClutterArchive
                        ? string.Create(CultureInfo.InvariantCulture, $"d:c{rdbClutterOrdinal++:D2}")
                        : string.Create(CultureInfo.InvariantCulture, $"d:p{rdbTalkableOrdinal++:D2}");

                    SlotEntry slot = BuildSlot(
                        block.SourceKey, slotId, position,
                        flat.TextureArchive, flat.TextureRecord, gender,
                        flat.FactionOrMobileId, isInterior: true, isClutter: isClutterArchive, isDungeon: true,
                        buildingType: -1, buildingFactionId: 0,
                        factions, castByFaction, spawners,
                        ref demotedCount, ref molagTypoCount, ref unmappedFactionCount, ref castPlacements, demotedByFaction);
                    rdbSlots.Add(slot);
                    if (isClutterArchive) rdbClutterCount++;
                    else rdbTalkableCount++;
                }

                if (rdbSlots.Count > 0)
                    rdbDungeons.Add(new BlockSlotsDocument(block.SourceKey, null, rdbSlots));
            }
        }

        Console.WriteLine($"people-extract: scanned {blocks.Count} blocks (building.Exterior.People={exteriorPeopleCount})");
        Console.WriteLine($"  cast individuals: {castByFaction.Count} unique ({castPlacements} placements)");
        Console.WriteLine($"  spawners: {spawners.Count} typed roles");
        Console.WriteLine($"  RMB interiors: {rmbInteriors.Count} blocks, {interiorTalkableCount} talkable + {interiorClutterCount} clutter");
        Console.WriteLine($"  RMB exteriors: {rmbExteriors.Count} blocks, {exteriorTalkableCount} talkable + {exteriorClutterCount} clutter (skipped {nonPeopleFactionFlatCount} non-people faction flats)");
        Console.WriteLine($"  RDB dungeons:  {rdbDungeons.Count} blocks, {rdbTalkableCount} talkable + {rdbClutterCount} clutter");
        Console.WriteLine($"  bug fixes: demoted {demotedCount} cloned Type-4 placements, cleared {molagTypoCount} Molag Bal typos, cleared {unmappedFactionCount} unmapped faction IDs");
        foreach ((int fid, int count) in demotedByFaction.OrderByDescending(pair => pair.Value))
        {
            ClassicFaction? f = factions.GetValueOrDefault(fid);
            Console.WriteLine($"    demoted faction {fid} ({f?.Name ?? "?"}): {count}x (parent={f?.Parent}, group={f?.GuildGroup})");
        }
        foreach (CastEntry cast in castByFaction.Values.OrderBy(entry => entry.Id, StringComparer.Ordinal))
        {
            Console.WriteLine($"    cast '{cast.Id}': faction={cast.FactionId} ({cast.Name}), home={cast.HomeBlock}/{cast.HomeSlotId}, placements={cast.PlacementCount}");
        }

        if (exteriorPeopleCount != 0)
            throw new InvalidOperationException($"Expected 0 building.Exterior.People records in Arena2, found {exteriorPeopleCount}.");

        if (!args.Switch("--update"))
            return Options.ReportOnly("daggerfall.people");

        WritePack(packDir, castByFaction.Values, spawners.Values, rmbInteriors, rmbExteriors, rdbDungeons);
        Console.WriteLine($"people-extract: wrote {packDir}");
        return 0;
    }

    private static SlotEntry BuildSlot(
        string blockSourceKey,
        string slotId,
        NormalizedVector3 position,
        int archive,
        int record,
        string gender,
        int rawFactionId,
        bool isInterior,
        bool isClutter,
        bool isDungeon,
        int buildingType,
        int buildingFactionId,
        IReadOnlyDictionary<int, ClassicFaction> factions,
        Dictionary<int, CastEntry> castByFaction,
        Dictionary<string, SpawnerAccumulator> spawners,
        ref int demotedCount,
        ref int molagTypoCount,
        ref int unmappedFactionCount,
        ref int castPlacements,
        Dictionary<int, int> demotedByFaction)
    {
        int effectiveFactionId = rawFactionId;
        bool deriveRegional = isInterior || isClutter || effectiveFactionId == 0;

        // Fix 1: Clear the 9 Molag Bal (faction 3) source typos on non-Daedra commoner placements.
        if (effectiveFactionId == MolagBalFactionId && archive != DaedraArchive)
        {
            effectiveFactionId = 0;
            deriveRegional = true;
            molagTypoCount++;
        }

        ClassicFaction? faction = effectiveFactionId != 0 ? factions.GetValueOrDefault(effectiveFactionId) : null;

        // Fix 1b: Clear corrupt/unmapped faction IDs (e.g. the 5 invalid RDB faction IDs).
        if (effectiveFactionId != 0 && faction is null)
        {
            effectiveFactionId = 0;
            deriveRegional = true;
            unmappedFactionCount++;
        }

        // Fix 2: Demote cloned generic-block Type-4 placements into typed spawner slots.
        if (!isClutter && faction is { Type: IndividualFactionType } && DemotedClonedIndividuals.Contains(faction.Id))
        {
            demotedCount++;
            demotedByFaction[faction.Id] = demotedByFaction.GetValueOrDefault(faction.Id) + 1;
            int? replacement = faction.Parent != 0 && factions.TryGetValue(faction.Parent, out ClassicFaction? parent)
                && parent.Type != IndividualFactionType
                ? parent.Id
                : null;
            effectiveFactionId = replacement ?? 0;
            faction = effectiveFactionId != 0 ? factions.GetValueOrDefault(effectiveFactionId) : null;
            deriveRegional = true;
        }

        // Genuine unique Type-4 individuals go to cast/individuals.json.
        if (!isClutter && faction is { Type: IndividualFactionType })
        {
            castPlacements++;
            if (!castByFaction.TryGetValue(faction.Id, out CastEntry? cast))
            {
                string baseId = Slug(faction.Name);
                string castId = castByFaction.Values.Any(existing => existing.Id == baseId)
                    ? string.Create(CultureInfo.InvariantCulture, $"{baseId}-{faction.Id}")
                    : baseId;
                cast = new CastEntry(
                    castId,
                    faction.Id,
                    faction.Name,
                    RmbBlockContent.StaticNpcRace(faction.Id, factions),
                    gender,
                    archive,
                    record,
                    blockSourceKey,
                    slotId,
                    "full-dialogue",
                    "protected");
                castByFaction.Add(faction.Id, cast);
            }
            cast.PlacementCount++;

            return new SlotEntry(
                slotId,
                CastId: cast.Id,
                Spawner: null,
                position,
                Interaction: "full-dialogue",
                FactionId: faction.Id,
                DeriveRegionalFaction: false,
                Archive: archive,
                Record: record,
                Gender: gender,
                SourceBuildingType: buildingType >= 0 ? buildingType : null,
                SourceBuildingFactionId: buildingFactionId != 0 ? buildingFactionId : null);
        }

        string interaction = isClutter ? "none" : "full-dialogue";
        string spawnerId = ClassifySpawner(
            isClutter, isDungeon, archive, record, buildingType, buildingFactionId, faction, factions);
        string harm = isClutter ? "law-protected-civilian" : spawnerId is "royal-guard" or "daedra-summon" or "necromancer" ? "combatant" : "law-protected-civilian";

        if (!spawners.TryGetValue(spawnerId, out SpawnerAccumulator? accumulator))
        {
            accumulator = new SpawnerAccumulator(spawnerId, DefaultRoleForSpawner(spawnerId), interaction, harm, deriveRegional);
            spawners.Add(spawnerId, accumulator);
        }
        accumulator.AddVisual(archive, record, gender);

        return new SlotEntry(
            slotId,
            CastId: null,
            Spawner: spawnerId,
            position,
            Interaction: interaction,
            FactionId: effectiveFactionId != 0 ? effectiveFactionId : null,
            DeriveRegionalFaction: deriveRegional,
            Archive: archive,
            Record: record,
            Gender: gender,
            SourceBuildingType: buildingType >= 0 ? buildingType : null,
            SourceBuildingFactionId: buildingFactionId != 0 ? buildingFactionId : null);
    }

    private static string ClassifySpawner(
        bool isClutter,
        bool isDungeon,
        int archive,
        int record,
        int buildingType,
        int buildingFactionId,
        ClassicFaction? faction,
        IReadOnlyDictionary<int, ClassicFaction> factions)
    {
        if (isClutter) return "clutter-person";
        if (IsChild(archive, record, faction?.Id ?? 0)) return "child";
        if (faction?.Type == KnightlyGuardFactionType) return "royal-guard";
        if (faction?.Type == WitchesFactionType) return "witch";

        if (faction?.SocialGroup == MerchantSocialGroup)
        {
            string? merchantSpawner = buildingType switch
            {
                0 => "alchemist",
                2 => "armorer",
                3 => "banker",
                5 => "bookseller",
                6 => "clothier",
                7 => "furniture-merchant",
                8 => "jeweler",
                9 => "general-merchant",
                12 => "pawnbroker",
                13 => "weaponsmith",
                15 => "tavern-keeper",
                _ => null,
            };
            if (merchantSpawner is not null) return merchantSpawner;
        }

        if (buildingType == 11) // GuildHall
        {
            int guildKey = ResolveGuildRoot(buildingFactionId, faction, factions);
            return guildKey switch
            {
                40 => "mage-guild-member",
                41 => "fighters-guild-member",
                42 => "thieves-guild-member",
                108 => "dark-brotherhood-member",
                368 => "knightly-order-member",
                _ when faction?.Type == KnightlyGuardFactionType => "knightly-order-member",
                _ when archive == 177 => "mage-guild-member",
                _ when archive == 176 => "dark-brotherhood-member",
                _ => "guild-member",
            };
        }

        if (buildingType == 14) return "temple-priest";
        if (buildingType == 16) return archive is 180 or 183 or 334 or 346 or 357 || faction?.SocialGroup == NobilitySocialGroup ? "courtier" : "palace-attendant";
        if (buildingType == 15) return "tavern-patron";
        if (buildingType == 3) return "bank-patron";
        if (buildingType is 0 or 2 or 5 or 6 or 7 or 8 or 9 or 12 or 13) return "shop-patron";
        if (buildingType == 10) return "scholar";
        if (buildingType is 1 or 4 or >= 17 and <= 23) return "house-resident";

        return archive switch
        {
            175 => "daedra-summon",
            176 => "dark-brotherhood-member",
            177 => "mage-guild-member",
            178 => "necromancer",
            179 => "witch",
            180 => "courtier",
            181 => "temple-priest",
            183 => "noble",
            _ when isDungeon => "dungeon-inhabitant",
            _ => "town-resident",
        };
    }

    private static int ResolveGuildRoot(int buildingFactionId, ClassicFaction? personFaction, IReadOnlyDictionary<int, ClassicFaction> factions)
    {
        foreach (int start in new[] { buildingFactionId, personFaction?.Id ?? 0 })
        {
            int current = start;
            int guard = 0;
            while (current != 0 && guard++ < 8)
            {
                if (current is 40 or 41 or 42 or 108 or 368) return current;
                if (!factions.TryGetValue(current, out ClassicFaction? node)) break;
                if (node.Type == KnightlyGuardFactionType) return 368;
                current = node.Parent;
            }
        }
        return 0;
    }

    private static bool IsChild(int archive, int record, int factionId) => factionId == ChildrenFactionId || (archive switch
    {
        181 or 197 => record == 3,
        182 => record is 4 or 5 or 6 or 18 or 36 or 37 or 38 or 42 or 43 or 52 or 53,
        184 => record == 15,
        186 => record is 4 or 5 or 6 or 7 or 19 or 37 or 38 or 39 or 43 or 44 or 53 or 54,
        334 => record is 2 or 3 or 6 or 9 or 12,
        346 => record is 2 or 3 or 12 or 15 or 16 or 18,
        357 => record is 5 or 6 or 7 or 8,
        _ => false,
    });

    private static string DefaultRoleForSpawner(string spawnerId) => spawnerId switch
    {
        "banker" => "bank teller",
        "tavern-keeper" => "tavern keeper",
        "alchemist" or "armorer" or "bookseller" or "clothier" or "furniture-merchant"
            or "jeweler" or "general-merchant" or "pawnbroker" or "weaponsmith" => "merchant",
        "royal-guard" => "guard",
        "witch" => "witch",
        "temple-priest" => "priest",
        "clutter-person" => "bystander",
        _ => "person",
    };

    private static Dictionary<(int Archive, int Record), string> ReadFlatsCfgGenders(string path)
    {
        Dictionary<(int, int), string> result = [];
        if (!File.Exists(path)) return result;
        string text = File.ReadAllText(path).Replace("\r", "", StringComparison.Ordinal);
        foreach (string block in text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] lines = block.Trim().Split('\n');
            string[] pair = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length < 3 || pair.Length != 2 || !int.TryParse(pair[0], out int archive) || !int.TryParse(pair[1], out int record))
                continue;
            result.TryAdd((archive, record), lines[2].Contains('2') ? "Female" : "Male");
        }
        return result;
    }

    private static string Slug(string name)
    {
        StringBuilder sb = new(name.Length);
        bool dash = false;
        foreach (char c in name.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                sb.Append(c);
                dash = false;
            }
            else if (!dash && sb.Length > 0)
            {
                sb.Append('-');
                dash = true;
            }
        }
        return sb.ToString().TrimEnd('-');
    }

    private static void WritePack(
        string packDir,
        IEnumerable<CastEntry> cast,
        IEnumerable<SpawnerAccumulator> spawners,
        IReadOnlyList<BlockSlotsDocument> rmbInteriors,
        IReadOnlyList<BlockSlotsDocument> rmbExteriors,
        IReadOnlyList<BlockSlotsDocument> rdbDungeons)
    {
        Directory.CreateDirectory(Path.Combine(packDir, "cast"));
        Directory.CreateDirectory(Path.Combine(packDir, "blocks"));

        JsonObject packManifest = new()
        {
            ["kind"] = "worldrpg.content-pack",
            ["id"] = "daggerfall.people",
            ["ruleset"] = "daggerfall",
            ["role"] = "daggerfall.people",
            ["dependencies"] = new JsonArray { new JsonObject { ["id"] = "daggerfall.base" } },
            ["description"] = "Owned people placements, genuine unique cast individuals (cast/individuals.json), central regional typed spawner pools (spawners.json), and block slot templates (blocks/). Extracted once from Arena2 BLOCKS.BSA, FACTION.TXT, and FLATS.CFG via daggerfall-source-tool people-extract with known Arena2 people data bugs fixed; edited here in place.",
            ["payload"] = "worldrpg/packs/daggerfall.people/payload.json",
        };
        WriteUtf8(Path.Combine(packDir, "pack.json"), packManifest.ToJsonString(IndentedJson) + "\n");

        JsonObject payload = new()
        {
            ["castFile"] = "cast/individuals.json",
            ["spawnersFile"] = "spawners.json",
            ["blocksFolder"] = "blocks",
        };
        WriteUtf8(Path.Combine(packDir, "payload.json"), payload.ToJsonString(IndentedJson) + "\n");

        JsonArray castArray = [];
        foreach (CastEntry entry in cast.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            JsonObject obj = new()
            {
                ["id"] = entry.Id,
                ["factionId"] = entry.FactionId,
                ["name"] = entry.Name,
                ["race"] = entry.Race,
                ["gender"] = entry.Gender,
                ["visual"] = new JsonObject { ["archive"] = entry.Archive, ["record"] = entry.Record },
                ["homeBlock"] = entry.HomeBlock,
                ["homeSlotId"] = entry.HomeSlotId,
                ["interaction"] = entry.Interaction,
                ["harm"] = entry.Harm,
            };
            castArray.Add(obj);
        }
        JsonObject castDoc = new() { ["individuals"] = castArray };
        WriteUtf8(Path.Combine(packDir, "cast", "individuals.json"), castDoc.ToJsonString(IndentedJson) + "\n");

        JsonArray spawnersArray = [];
        foreach (SpawnerAccumulator spawner in spawners.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            JsonArray pool = [];
            foreach (((int archive, int record, string gender), int count) in spawner.Visuals
                .OrderByDescending(pair => pair.Value)
                .ThenBy(pair => pair.Key.Archive)
                .ThenBy(pair => pair.Key.Record)
                .ThenBy(pair => pair.Key.Gender, StringComparer.Ordinal))
            {
                pool.Add(new JsonObject
                {
                    ["archive"] = archive,
                    ["record"] = record,
                    ["gender"] = gender,
                    ["weight"] = count,
                });
            }
            spawnersArray.Add(new JsonObject
            {
                ["id"] = spawner.Id,
                ["role"] = spawner.Role,
                ["interaction"] = spawner.Interaction,
                ["harm"] = spawner.Harm,
                ["deriveRegionalFaction"] = spawner.DeriveRegionalFaction,
                ["visualMode"] = "classic-slot",
                ["defaultPool"] = pool,
                ["regionalPools"] = new JsonObject(),
            });
        }
        JsonObject spawnersDoc = new() { ["spawners"] = spawnersArray };
        WriteUtf8(Path.Combine(packDir, "spawners.json"), spawnersDoc.ToJsonString(IndentedJson) + "\n");

        WriteBlockFile(Path.Combine(packDir, "blocks", "rmb-interiors.json"), "rmb-interior", rmbInteriors);
        WriteBlockFile(Path.Combine(packDir, "blocks", "rmb-exteriors.json"), "rmb-exterior", rmbExteriors);
        WriteBlockFile(Path.Combine(packDir, "blocks", "rdb-dungeons.json"), "rdb-dungeon", rdbDungeons);
    }

    private static void WriteBlockFile(string path, string kind, IReadOnlyList<BlockSlotsDocument> blocks)
    {
        StringBuilder sb = new(256 * 1024);
        sb.Append("{\n  \"kind\": \"").Append(kind).Append("\",\n  \"blocks\": {\n");
        for (int b = 0; b < blocks.Count; b++)
        {
            BlockSlotsDocument block = blocks[b];
            sb.Append("    ").Append(JsonSerializer.Serialize(block.BlockSourceKey)).Append(": ");
            if (block.Buildings is { } buildings)
            {
                sb.Append("{\n");
                for (int bi = 0; bi < buildings.Count; bi++)
                {
                    BuildingSlots building = buildings[bi];
                    sb.Append("      \"").Append(building.BuildingIndex.ToString(CultureInfo.InvariantCulture)).Append("\": {\n");
                    sb.Append("        \"buildingType\": ").Append(building.BuildingType.ToString(CultureInfo.InvariantCulture))
                      .Append(", \"buildingFactionId\": ").Append(building.BuildingFactionId.ToString(CultureInfo.InvariantCulture))
                      .Append(", \"slots\": [\n");
                    AppendSlots(sb, building.Slots, "          ");
                    sb.Append("        ]\n      }").Append(bi + 1 < buildings.Count ? ",\n" : "\n");
                }
                sb.Append("    }");
            }
            else
            {
                sb.Append("[\n");
                AppendSlots(sb, block.Slots!, "      ");
                sb.Append("    ]");
            }
            sb.Append(b + 1 < blocks.Count ? ",\n" : "\n");
        }
        sb.Append("  }\n}\n");
        WriteUtf8(path, sb.ToString());
    }

    private static void AppendSlots(StringBuilder sb, IReadOnlyList<SlotEntry> slots, string indent)
    {
        for (int i = 0; i < slots.Count; i++)
        {
            SlotEntry s = slots[i];
            sb.Append(indent).Append("{\"slotId\":").Append(JsonSerializer.Serialize(s.SlotId));
            if (s.CastId is not null)
                sb.Append(",\"castId\":").Append(JsonSerializer.Serialize(s.CastId));
            else
                sb.Append(",\"spawner\":").Append(JsonSerializer.Serialize(s.Spawner));
            sb.Append(",\"position\":[")
              .Append(FormatFloat(s.Position.X)).Append(',')
              .Append(FormatFloat(s.Position.Y)).Append(',')
              .Append(FormatFloat(s.Position.Z)).Append(']');
            sb.Append(",\"interaction\":").Append(JsonSerializer.Serialize(s.Interaction));
            if (s.FactionId is int fid)
                sb.Append(",\"factionId\":").Append(fid.ToString(CultureInfo.InvariantCulture));
            if (s.DeriveRegionalFaction)
                sb.Append(",\"deriveRegionalFaction\":true");
            sb.Append(",\"visual\":{\"archive\":").Append(s.Archive.ToString(CultureInfo.InvariantCulture))
              .Append(",\"record\":").Append(s.Record.ToString(CultureInfo.InvariantCulture))
              .Append(",\"gender\":").Append(JsonSerializer.Serialize(s.Gender)).Append('}');
            if (s.SourceBuildingType is int bt)
                sb.Append(",\"buildingType\":").Append(bt.ToString(CultureInfo.InvariantCulture));
            if (s.SourceBuildingFactionId is int bf)
                sb.Append(",\"buildingFactionId\":").Append(bf.ToString(CultureInfo.InvariantCulture));
            sb.Append('}').Append(i + 1 < slots.Count ? ",\n" : "\n");
        }
    }

    private static string FormatFloat(float value) =>
        MathF.Round(value, 4).ToString("0.####", CultureInfo.InvariantCulture);

    private static void WriteUtf8(string path, string content) =>
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

    private sealed class CastEntry(
        string id, int factionId, string name, string? race, string gender, int archive, int record,
        string homeBlock, string homeSlotId, string interaction, string harm)
    {
        public string Id { get; } = id;
        public int FactionId { get; } = factionId;
        public string Name { get; } = name;
        public string? Race { get; } = race;
        public string Gender { get; } = gender;
        public int Archive { get; } = archive;
        public int Record { get; } = record;
        public string HomeBlock { get; } = homeBlock;
        public string HomeSlotId { get; } = homeSlotId;
        public string Interaction { get; } = interaction;
        public string Harm { get; } = harm;
        public int PlacementCount { get; set; }
    }

    private sealed class SpawnerAccumulator(string id, string role, string interaction, string harm, bool deriveRegionalFaction)
    {
        public string Id { get; } = id;
        public string Role { get; } = role;
        public string Interaction { get; } = interaction;
        public string Harm { get; } = harm;
        public bool DeriveRegionalFaction { get; } = deriveRegionalFaction;
        public Dictionary<(int Archive, int Record, string Gender), int> Visuals { get; } = [];
        public void AddVisual(int archive, int record, string gender) =>
            Visuals[(archive, record, gender)] = Visuals.GetValueOrDefault((archive, record, gender)) + 1;
    }

    private sealed record BlockSlotsDocument(
        string BlockSourceKey,
        IReadOnlyList<BuildingSlots>? Buildings,
        IReadOnlyList<SlotEntry>? Slots);

    private sealed record BuildingSlots(
        int BuildingIndex,
        int BuildingType,
        int BuildingFactionId,
        IReadOnlyList<SlotEntry> Slots);

    private sealed record SlotEntry(
        string SlotId,
        string? CastId,
        string? Spawner,
        NormalizedVector3 Position,
        string Interaction,
        int? FactionId,
        bool DeriveRegionalFaction,
        int Archive,
        int Record,
        string Gender,
        int? SourceBuildingType,
        int? SourceBuildingFactionId);
}
