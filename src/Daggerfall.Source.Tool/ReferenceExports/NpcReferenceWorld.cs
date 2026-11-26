using System.Text.Json;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalization;

namespace Daggerfall.Source.Tool.ReferenceExports;

internal readonly record struct NpcSprite(int Archive, int Record)
{
    // FactionFile and FlatsFile both address an archive/record pair with this packed number.
    public int Number => (Archive << 7) + Record;
    // The classic key, kept as the NPC chart's source id and alias that review data is filed under.
    public string Id => $"npc-flat-{Number}";
    public static NpcSprite Decode(int number) => new(number >> 7, number & 127);
}

internal enum NpcSetting { Exterior, Interior, Dungeon }

internal sealed record NpcPlacement(NpcSprite Sprite, string Block, NpcSetting Setting, int Faction, string? Pose = null);
internal sealed record NpcLocation(int Region, int Index, string Name);

/// <summary>Joins source block placements to the locations that use those blocks.</summary>
internal sealed class NpcReferenceWorld
{
    private readonly Dictionary<string, HashSet<NpcLocation>> locations = new(StringComparer.OrdinalIgnoreCase);
    private readonly string[] regionNames;

    public NpcReferenceWorld(ReferenceExport export, string basePayload, IEnumerable<string> blocks)
    {
        using JsonDocument buildingNames = BasePackFiles.ReadSectionDocument(basePayload, "buildingNames");
        regionNames = [.. buildingNames.RootElement.GetProperty("regionNames")
            .EnumerateArray().Select(name => name.GetString() ?? "")];

        foreach (string block in blocks.Distinct(StringComparer.OrdinalIgnoreCase))
            locations.Add(block, []);

        BsaArchive maps = BsaArchive.Parse(export.Read("MAPS.BSA"), "arena2/MAPS.BSA");
        foreach (MapsRegionGroup region in MapsDecoder.DecodeRegionGroups(maps))
        {
            // Empty region tables also occur for unpopulated regions. Decode the available exterior
            // and dungeon layouts independently so a missing dungeon table cannot hide a town.
            if (region.Tables[3].State != MapsTableState.Read || region.Tables[2].State != MapsTableState.Read) continue;
            if (region.Tables[0].State == MapsTableState.Read)
                foreach (MapsLocationRecord location in MapsDecoder.DecodeRegionLocations(maps, region.Region))
                {
                    try
                    {
                        MapsExteriorLayout layout = MapsDecoder.DecodeExteriorLayout(maps, location.Region, location.Index);
                        Add(layout.Blocks.Select(block => block.SourceName), new(location.Region, location.Index, location.Name));
                    }
                    catch (Arena2FormatException failure)
                    {
                        export.Warn($"{location.Name} (region {location.Region}, location {location.Index}): {failure.Message}");
                    }
                }

            if (region.Tables[0].State == MapsTableState.Read && region.Tables[1].State == MapsTableState.Read)
                foreach (MapsDungeonLocation location in MapsDecoder.DecodeRegionDungeons(maps, region.Region))
                {
                    if (location.State == MapsDungeonState.Read)
                        Add(location.Blocks.Select(block => block.SourceName), new(location.Region, location.Index, location.Name));
                    else if (location.State == MapsDungeonState.Malformed)
                        export.Warn($"{location.Name} (region {location.Region}, dungeon {location.Index}): {location.Reason}");
                }
        }
    }

    public string Region(int id) => id >= 0 && id < regionNames.Length && regionNames[id].Length > 0
        ? regionNames[id] : $"Region {id}";

    public IEnumerable<NpcLocation> Locations(IEnumerable<string> blocks) => blocks
        .SelectMany(block => locations[block]).Distinct().OrderBy(location => Region(location.Region), StringComparer.Ordinal)
        .ThenBy(location => location.Name, StringComparer.Ordinal).ThenBy(location => location.Index);

    private void Add(IEnumerable<string> blocks, NpcLocation location)
    {
        foreach (string block in blocks)
            if (locations.TryGetValue(block, out HashSet<NpcLocation>? uses))
                uses.Add(location);
    }

    public static IEnumerable<NpcPlacement> ReadPlacements(ReferenceExport export, IReadOnlySet<int> peopleArchives,
        IReadOnlySet<NpcSprite> listedSprites)
    {
        bool Listed(NpcSprite sprite) => peopleArchives.Contains(sprite.Archive) || listedSprites.Contains(sprite);

        foreach (BlockRecord block in BlockRecordInventoryReader.Read(export.Read("BLOCKS.BSA"), "arena2/BLOCKS.BSA").Records)
        {
            if (block.RmbPlacements is { } rmb)
            {
                foreach (RmbBuildingPlacements building in rmb.Buildings)
                {
                    foreach (NpcPlacement placement in ReadHalf(building.Exterior, NpcSetting.Exterior)) yield return placement;
                    foreach (NpcPlacement placement in ReadHalf(building.Interior, NpcSetting.Interior)) yield return placement;
                }
                foreach (RmbFlatPlacement flat in rmb.MiscFlats)
                {
                    NpcSprite source = new(flat.TextureArchive, flat.TextureRecord);
                    if (Listed(source)) yield return new(source, block.SourceKey, NpcSetting.Exterior, flat.FactionId);
                }
            }

            if (block.Kind == BlockRecordKind.Rdb && block.Objects is { } rdb)
                foreach (RdbFlatPlacement flat in rdb.FlatPlacements.Where(flat => StaticNpcNormalization.IsDungeonNpc(flat.TextureArchive)))
                    yield return new(new(flat.TextureArchive, flat.TextureRecord), block.SourceKey, NpcSetting.Dungeon, flat.FactionOrMobileId);

            IEnumerable<NpcPlacement> ReadHalf(RmbHalfPlacements half, NpcSetting setting)
            {
                foreach (RmbPeoplePlacement person in half.People)
                    yield return new(new(person.TextureArchive, person.TextureRecord), block.SourceKey, setting, person.FactionId);
                foreach (RmbFlatPlacement flat in half.Flats)
                {
                    NpcSprite source = new(flat.TextureArchive, flat.TextureRecord);
                    if (Listed(source)) yield return new(source, block.SourceKey, setting, flat.FactionId);
                }
            }
        }
    }
}
