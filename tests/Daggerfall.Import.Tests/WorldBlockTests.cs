using System.Text.Json;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalization;
using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;
using Xunit;

namespace Daggerfall.Import.Tests;

/// <summary>
/// The per-block publication: every block a location places normalized in its own frame from the same
/// block-level content the site normalizers place, so a site assembled from its blocks' facts carries the
/// site closure's doors, markers, people and buildings.
/// </summary>
public sealed class WorldBlockTests
{
    private const float RmbBlockSide = 4096F * Arena2SourceTransform.SourceUnitMetres;
    private const float RdbBlockSide = 2048F * Arena2SourceTransform.SourceUnitMetres;

    /// <summary>
    /// Charing's exterior, assembled from its 42 placed blocks' facts (block-local identities given their
    /// grid position, block-local positions moved by the block origin), names the site closure's doors with
    /// their buildings and locks, its fixed people and its start marker; each provider interior's facts are
    /// the interior closure's quest markers, people, furniture and building.
    /// </summary>
    [CorpusFact("MAPS.BSA", "BLOCKS.BSA", "ARCH3D.BSA", "CLIMATE.PAK", "FACTION.TXT")]
    public void Charing_assembled_from_its_block_facts_carries_the_site_closures_doors_markers_and_buildings()
    {
        DungeonLogicalSourceSet sources = Sources();
        RmbExteriorNormalizationResult site = RmbExteriorNormalizer.Normalize(new(sources, 17, "Charing", RmbWorldProfileKind.Exterior)
        {
            LocationIndex = 4,
            // Doors, markers and people do not depend on the navigation grid; a coarse one keeps this quick.
            Navigation = NavigationDerivationConfig.ClassicDefault with { CellSize = 10F },
        });
        WorldBlockNormalizer normalizer = new(sources);
        Dictionary<string, DaggerfallWorldBlock> facts = site.Layout.Blocks.Select(block => block.SourceName).Distinct(StringComparer.Ordinal)
            .ToDictionary(name => name, name => normalizer.RmbExterior(name).Block, StringComparer.Ordinal);
        Assert.Equal(42, site.Layout.Blocks.Count);

        List<(string Id, NormalizedVector3 Position, NormalizedVector3 Rotation, int Lock, int? Building, NormalizedBounds? Bounds)> doors = [];
        List<NormalizedPopulationPlacement> people = [];
        NormalizedMarker? start = null;
        foreach (MapsExteriorBlock placed in site.Layout.Blocks.OrderBy(block => block.X).ThenBy(block => block.Y).ThenBy(block => block.SourceName, StringComparer.Ordinal))
        {
            DaggerfallWorldBlock block = facts[placed.SourceName];
            NormalizedVector3 origin = new(placed.X * RmbBlockSide, 0F, -(placed.Y * RmbBlockSide));
            string scope = $"door/{PublishedIds.Slug(placed.SourceName)}-rmb/";
            foreach (DaggerfallWorldBlockDoor door in block.Doors)
            {
                Assert.StartsWith(scope, door.Id, StringComparison.Ordinal);
                doors.Add(($"{scope}{placed.X}/{placed.Y}/{door.Id[scope.Length..]}", Add(door.Position, origin), door.RotationDegrees,
                    door.StartingLockValue, door.BuildingIndex, door.CollisionBounds));
            }

            people.AddRange(block.Population.Select(person => person with { Position = Add(person.Position, origin) }));
            if (block.StartMarker is { } marker) start ??= marker with { Position = Add(marker.Position, origin) };
        }

        Assert.Equal(site.Document.World.Doors.Select(door => door.Id).Order(StringComparer.Ordinal), doors.Select(door => door.Id).Order(StringComparer.Ordinal));
        Dictionary<string, NormalizedDoorPlacement> siteDoors = site.Document.World.Doors.ToDictionary(door => door.Id, StringComparer.Ordinal);
        foreach (var door in doors)
        {
            NormalizedDoorPlacement expected = siteDoors[door.Id];
            // A site places a door from its world-space plane; the block from its local plane, so only the
            // float rounding of the translation differs.
            Near(expected.Position, door.Position);
            Assert.Equal((0F, 0F), (door.Rotation.X, door.Rotation.Z));
            // A door facing exactly -Z states 180 or -180 as the sign of a zero normal component falls.
            float turn = MathF.Abs(expected.RotationDegrees.Y - door.Rotation.Y) % 360F;
            Assert.True(MathF.Min(turn, 360F - turn) < 1e-3F, $"Door '{door.Id}' turns {door.Rotation.Y} where the site turns {expected.RotationDegrees.Y}.");
            Assert.Equal((expected.StartingLockValue, expected.ExteriorBuildingIndex), (door.Lock, door.Building));
            Near(expected.CollisionBounds!.Minimum, door.Bounds!.Minimum);
            Near(expected.CollisionBounds.Maximum, door.Bounds.Maximum);
        }

        NormalizedPopulationPlacement[] sitePeople = [.. site.Document.World.Population.Where(person => !person.Id.Contains("/dynamic/", StringComparison.Ordinal))];
        Assert.Equal(sitePeople.OrderBy(person => person.Id, StringComparer.Ordinal), people.OrderBy(person => person.Id, StringComparer.Ordinal));
        Assert.Equal(site.Document.World.StartMarker, start);

        // Each building a door enters is a sub-record the block states, with the catalog's interior facts.
        foreach (MapsExteriorBlock placed in site.Layout.Blocks)
        {
            DaggerfallWorldBlock block = facts[placed.SourceName];
            Assert.All(block.Doors.Where(door => door.BuildingIndex is not null), door => Assert.Contains(block.Buildings, building => building.Index == door.BuildingIndex));
        }

        foreach ((byte x, byte y, int building) in new (byte, byte, int)[] { (1, 1, 0), (2, 1, 0), (3, 4, 0), (3, 1, 13), (1, 5, 17), (4, 2, 0), (3, 2, 14) })
        {
            RmbExteriorNormalizationResult interior = RmbExteriorNormalizer.Normalize(new(sources, 17, "Charing", RmbWorldProfileKind.Interior)
            {
                LocationIndex = 4,
                Building = new(x, y, building),
                Navigation = NavigationDerivationConfig.ClassicDefault with { CellSize = 2F },
            });
            NormalizedWorld world = interior.Document.World;
            DaggerfallWorldBlock block = normalizer.RmbInterior(world.InteriorBuilding!.SourceKey, building).Block;
            Assert.Equal((world.InteriorBuilding.BuildingIndex, world.InteriorBuilding.BuildingType, world.InteriorBuilding.FactionId),
                (block.InteriorBuilding!.Index, block.InteriorBuilding.BuildingType, block.InteriorBuilding.FactionId));
            Assert.Equal(world.QuestMarkers.Select(marker => (marker.Id, marker.Kind, marker.Position, marker.SourceOrdinal)),
                block.QuestMarkers.Select(marker => (marker.Id, marker.Kind, marker.Position, marker.SourceOrdinal)));
            Assert.Equal(world.StartMarker, block.StartMarker);
            Assert.Equal(world.EnterMarker, block.EnterMarker);
            int buildingKey = (x << 16) + (y << 8) + building;
            Assert.Equal(world.StaticNpcs, block.StaticNpcs.Select(npc => new NormalizedStaticNpcPlacement(npc.Id, npc.Position,
                npc.BillboardArchive, npc.BillboardRecord, npc.FactionId, npc.Race, npc.Gender, npc.SourceOffset ^ (buildingKey + 4)))
                .OrderBy(npc => npc.Id, StringComparer.Ordinal));
            Assert.Equal(world.PropertyContainers.Select(Container), block.PropertyContainers.OrderBy(container => container.Id, StringComparer.Ordinal).Select(Container));
            Assert.NotNull(block.Spatial);
        }
    }

    /// <summary>
    /// Privateer's Hold assembled from its RDB blocks' facts, with the catalog's start block, dungeon type and
    /// texture table, carries the dungeon closure's lights, billboards, actors, treasure, quest markers, doors,
    /// actions and start marker exactly.
    /// </summary>
    [CorpusFact("MAPS.BSA", "BLOCKS.BSA", "ARCH3D.BSA", "CLIMATE.PAK", "FACTION.TXT", "PAL.PAL")]
    public void Privateers_hold_assembled_from_its_block_facts_carries_the_dungeon_closures_placements()
    {
        DungeonLogicalSourceSet sources = Sources();
        DungeonNormalizationResult site = DungeonNormalizer.Normalize(DungeonNormalizationRequest.Create(sources, 17, "Privateer's Hold") with
        {
            Quotas = DungeonNormalizationQuotas.Default with { MaximumSourceBytes = 1024L * 1024L * 1024L },
        });
        DaggerfallLocations catalog = Catalog(sources);
        DaggerfallLocationMap location = Assert.Single(catalog.Locations, value => value.Region == 17 && value.Name == "Privateer's Hold");
        DaggerfallDungeonRecord dungeon = Assert.Single(catalog.Dungeons, value => value.Region == 17 && value.Index == location.Index);
        DaggerfallDungeonTextureTable table = dungeon.TextureTable!;
        WorldBlockNormalizer normalizer = new(sources);
        Dictionary<string, DaggerfallWorldBlock> facts = dungeon.BlockPlacements.Select(block => block.SourceKey).Distinct(StringComparer.Ordinal)
            .ToDictionary(name => name, name => normalizer.Rdb(name).Block, StringComparer.Ordinal);

        List<string> lights = [], billboards = [], actors = [], treasures = [], quests = [], doors = [], actions = [];
        NormalizedMarker? start = null, enter = null;
        foreach (DaggerfallDungeonBlockPlacement placed in dungeon.BlockPlacements.OrderBy(block => block.X).ThenBy(block => block.Z).ThenBy(block => block.SourceKey, StringComparer.Ordinal))
        {
            DaggerfallWorldBlock block = facts[placed.SourceKey];
            string slug = PublishedIds.Slug(placed.SourceKey);
            NormalizedVector3 origin = new(placed.X * RdbBlockSide, 0F, -(placed.Z * RdbBlockSide));
            string Place(string id)
            {
                int scope = id.IndexOf($"/{slug}/", StringComparison.Ordinal) + slug.Length + 2;
                return $"{id[..scope]}{placed.X}/{placed.Z}/{id[scope..]}";
            }

            string Key(string id, NormalizedVector3 position) => $"{Place(id)}@{Add(position, origin)}";
            lights.AddRange(block.Lights.Select(light => Key(light.Id, light.Position)));
            billboards.AddRange(block.Billboards.Select(billboard =>
            {
                // The dungeon's texture table remaps the classic wall archives; a billboard's record stays.
                string[] parts = billboard.SpriteResourceId["sprite/texture-".Length..].Split('-');
                ushort archive = DungeonTextureTableTransform.RemapArchive(ushort.Parse(parts[0]), [.. table.Archives.Select(value => (ushort)value)], (ushort)table.DoorArchiveOffset);
                return $"{Key(billboard.Id, billboard.Position)}={archive}-{parts[1]}";
            }));
            actors.AddRange(block.Actors.Select(actor => $"{Key(actor.Id, actor.Position)}={actor.ActorResourceId}"));
            treasures.AddRange(block.Treasures.Select(treasure => Key(treasure.Id, treasure.Position)));
            quests.AddRange(block.QuestMarkers.Select(marker => $"{Key(marker.Id, marker.Position)}={marker.Kind}/{marker.SourceOrdinal}"));
            doors.AddRange(block.Doors.Select(door => $"{Key(door.Id, door.Position)}={door.Kind}/{door.StartingLockValue}/{door.RotationDegrees}"));
            actions.AddRange(block.Actions.Select(action => $"{Place(action.Id)}->{(action.NextActionId is null ? "" : Place(action.NextActionId))}"
                + $"@{(action.Position is { } position ? Add(position, origin) : null)}"));
            if (placed.Start)
            {
                start ??= block.StartMarker is { } marker ? marker with { Position = Add(marker.Position, origin) } : null;
                enter ??= block.EnterMarker is { } marker2 ? marker2 with { Position = Add(marker2.Position, origin) } : null;
            }
        }

        NormalizedWorld world = site.Document.World;
        Assert.Equal(world.Lights.Select(light => $"{light.Id}@{light.Position}").Order(StringComparer.Ordinal), lights.Order(StringComparer.Ordinal));
        Assert.Equal(world.Billboards.Select(billboard => $"{billboard.Id}@{billboard.Position}={billboard.SpriteResourceId["sprite/texture-".Length..]}").Order(StringComparer.Ordinal),
            billboards.Order(StringComparer.Ordinal));
        Assert.Equal(world.Actors.Select(actor => $"{actor.Id}@{actor.Position}={actor.ActorResourceId}").Order(StringComparer.Ordinal), actors.Order(StringComparer.Ordinal));
        Assert.Equal(world.Treasures.Select(treasure => $"{treasure.Id}@{treasure.Position}").Order(StringComparer.Ordinal), treasures.Order(StringComparer.Ordinal));
        Assert.All(world.Treasures, treasure => Assert.Equal($"treasure/dungeon-type-{location.DungeonType}", treasure.TreasureResourceId));
        Assert.Equal(world.QuestMarkers.Select(marker => $"{marker.Id}@{marker.Position}={marker.Kind}/{marker.SourceOrdinal}").Order(StringComparer.Ordinal), quests.Order(StringComparer.Ordinal));
        Assert.Equal(world.Doors.Select(door => $"{door.Id}@{door.Position}={door.Kind}/{door.StartingLockValue}/{door.RotationDegrees}").Order(StringComparer.Ordinal), doors.Order(StringComparer.Ordinal));
        Assert.Equal(world.Actions.Select(action => $"{action.Id}->{action.NextActionId ?? ""}@{action.Position}").Order(StringComparer.Ordinal), actions.Order(StringComparer.Ordinal));
        Assert.Equal(world.StartMarker, start);
        Assert.Equal(world.EnterMarker, enter);
        Assert.Contains(facts.Values, block => block.WaterLevel is not null || block.AmbientZone is not null || block.Spatial is not null);
    }

    /// <summary>
    /// The catalog states each location's climate and each dungeon's start block and classic texture table: the
    /// inputs and table the dungeon normalizer builds the dungeon with.
    /// </summary>
    [CorpusFact("MAPS.BSA", "BLOCKS.BSA", "ARCH3D.BSA", "CLIMATE.PAK")]
    public void The_catalog_states_each_dungeons_start_block_and_classic_texture_table()
    {
        DaggerfallLocations catalog = Catalog(Sources(textures: false));
        BsaArchive maps = BsaArchive.Parse(File.ReadAllBytes(TestData.Corpus("MAPS.BSA")), "arena2/MAPS.BSA");
        PakMap climate = PakDecoder.Decode(File.ReadAllBytes(TestData.Corpus("CLIMATE.PAK")), "arena2/CLIMATE.PAK");
        Assert.All(catalog.Locations, location => Assert.InRange(location.Climate!.Value, 223, 232));
        Assert.All(catalog.Dungeons, dungeon => Assert.Single(dungeon.BlockPlacements, block => block.Start));
        DaggerfallLocationMap hold = Assert.Single(catalog.Locations, value => value.Region == 17 && value.Name == "Privateer's Hold");
        MapsDungeonLayout layout = MapsDecoder.DecodeDungeonLayout(maps, 17, "Privateer's Hold");
        (int x, int y) = MapsDecoder.ToMapPixel(layout.Longitude, layout.Latitude);
        Assert.True(climate.TryGetPixel(x, y, out byte value));
        Assert.Equal(value, hold.Climate);
        DaggerfallDungeonRecord dungeon = Assert.Single(catalog.Dungeons, record => record.Region == 17 && record.Index == hold.Index);
        Assert.Equal(DungeonTextureTableTransform.CreateClassic(layout.LocationId, value).Select(archive => (int)archive), dungeon.TextureTable!.Archives);
        Assert.Equal(layout.Blocks.Select(block => (block.SourceName, (int)block.X, (int)block.Z, block.IsStart)),
            dungeon.BlockPlacements.Select(block => (block.SourceKey, block.X, block.Z, block.Start)));
    }

    /// <summary>The climate swaps keep classic's exceptions and publish every record range they apply to.</summary>
    [Fact]
    public void Climate_swaps_follow_the_classic_climate_and_season_rules()
    {
        // Door 3 keeps its archive; other doors take the climate's door set, and doors have no winter.
        Assert.Equal(74, ClassicClimateSwaps.Apply(74, 3, ClassicClimateBase.Mountain, ClassicClimateSeason.Summer));
        Assert.Equal(174, ClassicClimateSwaps.Apply(74, 0, ClassicClimateBase.Mountain, ClassicClimateSeason.Winter));
        // Terrain swaps by climate, season and rain; desert climates never take a winter variant.
        Assert.Equal(303, ClassicClimateSwaps.Apply(2, 5, ClassicClimateBase.Temperate, ClassicClimateSeason.Winter));
        Assert.Equal(404, ClassicClimateSwaps.Apply(2, 5, ClassicClimateBase.Swamp, ClassicClimateSeason.Rain));
        Assert.Equal(12, ClassicClimateSwaps.Apply(12, 1, ClassicClimateBase.Desert, ClassicClimateSeason.Winter));
        // The castle set's later records have no winter variant.
        Assert.Equal(310, ClassicClimateSwaps.Apply(9, 3, ClassicClimateBase.Temperate, ClassicClimateSeason.Winter));
        Assert.Equal(309, ClassicClimateSwaps.Apply(9, 4, ClassicClimateBase.Temperate, ClassicClimateSeason.Winter));
        // Archives outside a climate set, and climate-specific gables, keep or take only their own variant.
        Assert.Equal(210, ClassicClimateSwaps.Apply(210, 0, ClassicClimateBase.Swamp, ClassicClimateSeason.Winter));
        Assert.Equal(75, ClassicClimateSwaps.Apply(75, 0, ClassicClimateBase.Mountain, ClassicClimateSeason.Winter));
        Assert.Equal(80, ClassicClimateSwaps.Apply(79, 1, ClassicClimateBase.Mountain, ClassicClimateSeason.Winter));
        Assert.Equal(82, ClassicClimateSwaps.Apply(82, 2, ClassicClimateBase.Mountain, ClassicClimateSeason.Winter));
        Assert.Equal(505, ClassicClimateSwaps.Apply(504, 0, ClassicClimateBase.Temperate, ClassicClimateSeason.Winter));

        IReadOnlyList<DaggerfallClimateSwap> swaps = DaggerfallWorldGridsBuilder.ClimateSwaps();
        DaggerfallClimateSwap[] doors = [.. swaps.Where(swap => swap.Archive == 74 && swap.Climate == ClassicClimateBase.Mountain && swap.Season == ClassicClimateSeason.Summer)];
        Assert.Equal([(0, (int?)2, 174), (4, null, 174)], doors.Select(swap => (swap.FirstRecord, swap.LastRecord, swap.TargetArchive)));
        Assert.All(swaps, swap => Assert.NotEqual(swap.Archive, swap.TargetArchive));
        foreach (DaggerfallClimateSwap swap in swaps)
        for (int record = swap.FirstRecord; record <= (swap.LastRecord ?? swap.FirstRecord + 8); record++)
            Assert.Equal(swap.TargetArchive, ClassicClimateSwaps.Apply(swap.Archive, record, swap.Climate, swap.Season));
    }

    /// <summary>
    /// The generated world block publication covers every block BLOCKS.BSA carries that a location places:
    /// every RDB record, every placed RMB record's exterior and each interior it declares, with the unplaced
    /// RMB records named. Each entry's document and collision/navigation artifact are the ones it lists.
    /// </summary>
    [CorpusAndGeneratedContentFact("BLOCKS.BSA")]
    public void The_world_block_publication_covers_the_block_archive()
    {
        string root = Path.Combine(TestData.RepositoryRoot, "import-records", "world-blocks");
        DaggerfallWorldBlockIndex index = JsonSerializer.Deserialize<DaggerfallWorldBlockIndex>(
            File.ReadAllBytes(Path.Combine(root, Arena2WorldBlocksPublication.IndexRelativePath)), PublishedJson.SectionRead)!;
        BsaArchive blocks = BsaArchive.Parse(File.ReadAllBytes(TestData.Corpus("BLOCKS.BSA")), "arena2/BLOCKS.BSA");
        string[] rmb = [.. blocks.Records.Select(record => record.Name).OfType<string>().Where(name => name.EndsWith(".RMB", StringComparison.Ordinal))];
        string[] rdb = [.. blocks.Records.Select(record => record.Name).OfType<string>().Where(name => name.EndsWith(".RDB", StringComparison.Ordinal))];
        Assert.Equal((920, 187), (rmb.Length, rdb.Length));
        DaggerfallWorldBlockEntry[] exteriors = [.. index.Blocks.Where(block => block.Kind == DaggerfallWorldBlockKind.RmbExterior)];
        Assert.Equal(rdb.Order(StringComparer.Ordinal), index.Blocks.Where(block => block.Kind == DaggerfallWorldBlockKind.Rdb).Select(block => block.SourceKey).Order(StringComparer.Ordinal));
        Assert.Equal(rmb.Order(StringComparer.Ordinal), exteriors.Select(block => block.SourceKey).Concat(index.UnplacedRmbBlocks).Order(StringComparer.Ordinal));
        Assert.Equal((658, 187), (index.Counts.RmbExterior, index.Counts.Rdb));
        int interiors = 0;
        foreach (DaggerfallWorldBlockEntry exterior in exteriors)
        {
            Assert.True(blocks.TryGetByName(exterior.SourceKey, out BsaRecord? record));
            byte[] bytes = blocks.GetPayload(record!).ToArray();
            Assert.True(RmbBlockSummaryReader.TryRead(bytes, blocks.Source, 0, bytes.Length, out RmbBlockSummary? summary, out _));
            RmbBlockPlacements placements = RmbPlacementReader.Read(bytes, 0, summary!, blocks.Source);
            int[] declared = [.. Enumerable.Range(0, summary!.Buildings.Count).Where(building => placements.Buildings[building].Interior.Models.Count != 0)];
            Assert.Equal(declared, index.Blocks.Where(block => block.Kind == DaggerfallWorldBlockKind.RmbInterior && block.SourceKey == exterior.SourceKey)
                .Select(block => block.BuildingIndex!.Value).Order());
            interiors += declared.Length;
        }

        Assert.Equal(interiors, index.Counts.RmbInterior);
        Assert.Equal(index.Counts.RmbExterior + index.Counts.RmbInterior + index.Counts.Rdb, index.Blocks.Count);
        foreach (DaggerfallWorldBlockEntry entry in index.Blocks.Where((_, ordinal) => ordinal % 97 == 0))
        {
            DaggerfallWorldBlock block = JsonSerializer.Deserialize<DaggerfallWorldBlock>(File.ReadAllBytes(Path.Combine(root, entry.Document)), PublishedJson.SectionRead)!;
            Assert.Equal((entry.Key, entry.Kind, entry.SourceKey, entry.BuildingIndex), (block.Key, block.Kind, block.SourceKey, block.BuildingIndex));
            Assert.Equal(entry.Spatial, block.Spatial?.RelativePath);
            if (block.Spatial is { } spatial)
                Assert.Equal(spatial.ContentDigest, ContentDigest.Compute(File.ReadAllBytes(Path.Combine(root, spatial.RelativePath))));
        }
    }

    /// <summary>
    /// The published Charing exterior closure's doors and start marker are the ones its published blocks
    /// state, placed at their grid positions: the generated per-block facts assemble to today's site.
    /// </summary>
    [CorpusAndGeneratedContentFact("MAPS.BSA")]
    public void The_published_charing_closure_is_assembled_from_the_published_blocks()
    {
        string blocksRoot = Path.Combine(TestData.RepositoryRoot, "import-records", "world-blocks");
        using JsonDocument closure = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(TestData.RepositoryRoot, "content", "worldrpg", "imports", "charing", "exterior", "normalized.json")));
        MapsExteriorLayout charing = MapsDecoder.DecodeExteriorLayout(BsaArchive.Parse(File.ReadAllBytes(TestData.Corpus("MAPS.BSA")), "arena2/MAPS.BSA"), 17, "Charing");
        Dictionary<string, DaggerfallWorldBlock> blocks = new(StringComparer.Ordinal);
        List<(string Id, NormalizedVector3 Position, int? Building, int Lock)> doors = [];
        NormalizedMarker? start = null;
        foreach ((string name, int x, int y) in charing.Blocks.Select(block => (block.SourceName, (int)block.X, (int)block.Y))
            .OrderBy(block => block.Item2).ThenBy(block => block.Item3).ThenBy(block => block.Item1, StringComparer.Ordinal))
        {
            if (!blocks.TryGetValue(name, out DaggerfallWorldBlock? block))
                blocks.Add(name, block = JsonSerializer.Deserialize<DaggerfallWorldBlock>(
                    File.ReadAllBytes(Path.Combine(blocksRoot, $"{WorldBlockNormalizer.RmbExteriorKey(name)}.json")), PublishedJson.SectionRead)!);
            NormalizedVector3 origin = new(x * RmbBlockSide, 0F, -(y * RmbBlockSide));
            string scope = $"door/{PublishedIds.Slug(name)}-rmb/";
            doors.AddRange(block.Doors.Select(door => ($"{scope}{x}/{y}/{door.Id[scope.Length..]}", Add(door.Position, origin), door.BuildingIndex, door.StartingLockValue)));
            if (block.StartMarker is { } marker) start ??= marker with { Position = Add(marker.Position, origin) };
        }

        JsonElement world = closure.RootElement.GetProperty("world");
        Dictionary<string, JsonElement> published = world.GetProperty("doors").EnumerateArray().ToDictionary(door => door.GetProperty("id").GetString()!, StringComparer.Ordinal);
        Assert.Equal(published.Keys.Order(StringComparer.Ordinal), doors.Select(door => door.Id).Order(StringComparer.Ordinal));
        foreach (var door in doors)
        {
            JsonElement expected = published[door.Id];
            Near(Vector(expected.GetProperty("position")), door.Position);
            Assert.Equal(expected.GetProperty("exteriorBuildingIndex").ValueKind == JsonValueKind.Null ? null : expected.GetProperty("exteriorBuildingIndex").GetInt32(), door.Building);
            Assert.Equal(expected.GetProperty("startingLockValue").GetInt32(), door.Lock);
        }

        Assert.Equal(Vector(world.GetProperty("startMarker").GetProperty("position")), start!.Position);
    }

    private static NormalizedVector3 Vector(JsonElement value) => new(value.GetProperty("x").GetSingle(), value.GetProperty("y").GetSingle(), value.GetProperty("z").GetSingle());

    private static string Container(NormalizedPropertyContainer container) =>
        $"{container.Id}@{container.Position}:{string.Join(',', container.ItemGroups)}:{string.Join(';', container.InteractionPoints)}";

    private static NormalizedVector3 Add(NormalizedVector3 left, NormalizedVector3 right) => new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);

    private static void Near(NormalizedVector3 expected, NormalizedVector3 actual)
    {
        Assert.InRange(actual.X, expected.X - 1e-3F, expected.X + 1e-3F);
        Assert.InRange(actual.Y, expected.Y - 1e-3F, expected.Y + 1e-3F);
        Assert.InRange(actual.Z, expected.Z - 1e-3F, expected.Z + 1e-3F);
    }

    private static DaggerfallLocations Catalog(DungeonLogicalSourceSet sources) => DaggerfallLocationBuilder.Build(
        BsaArchive.Parse(sources.Require("MAPS.BSA").Bytes.Span, sources.Require("MAPS.BSA").Label),
        BsaArchive.Parse(sources.Require("BLOCKS.BSA").Bytes.Span, sources.Require("BLOCKS.BSA").Label),
        BsaArchive.Parse(sources.Require("ARCH3D.BSA").Bytes.Span, sources.Require("ARCH3D.BSA").Label),
        PakDecoder.Decode(sources.Require("CLIMATE.PAK").Bytes.Span, sources.Require("CLIMATE.PAK").Label));

    private static DungeonLogicalSourceSet Sources(bool textures = true) => new(Directory.EnumerateFiles(TestData.CorpusRoot)
        .Where(path => Path.GetFileName(path) is "MAPS.BSA" or "BLOCKS.BSA" or "ARCH3D.BSA" or "CLIMATE.PAK" or "FACTION.TXT" or "PAL.PAL"
            || textures && Path.GetFileName(path).StartsWith("TEXTURE.", StringComparison.Ordinal))
        .Select(path => new DungeonLogicalSource($"arena2/{Path.GetFileName(path)}", File.ReadAllBytes(path))));
}
