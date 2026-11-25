using System.Text.Json;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Normalization;
using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;
using Xunit;

namespace Daggerfall.Import.Tests;

/// <summary>
/// The per-block publication: every block a location places normalized in its own frame, with the doors, markers,
/// people and buildings a site assembles from it.
/// </summary>
public sealed class WorldBlockTests
{
    [CorpusFact("BLOCKS.BSA", "ARCH3D.BSA", "FACTION.TXT", "PAL.PAL")]
    public void Source_exterior_faction_flats_and_dungeon_people_become_static_placements()
    {
        var sources = Sources();
        var normalizer = new WorldBlockNormalizer(sources);
        var archive = BsaArchive.Parse(sources.Require("BLOCKS.BSA").Bytes.Span, "BLOCKS.BSA");
        int exteriorCount = 0, dungeonCount = 0;
        foreach (var record in archive.Records.Where(record => record.Name is not null))
        {
            if (record.Name!.EndsWith(".RMB", StringComparison.Ordinal))
            {
                byte[] bytes = archive.GetPayload(record).ToArray();
                Assert.True(RmbBlockSummaryReader.TryRead(bytes, archive.Source, 0, bytes.Length, out RmbBlockSummary? summary, out _));
                var source = RmbPlacementReader.Read(bytes, 0, summary!, archive.Source);
                var expected = source.Buildings.SelectMany(building => building.Exterior.Flats).Concat(source.MiscFlats)
                    .Where(flat => flat.FactionId != 0 && flat.TextureArchive != 199).ToArray();
                if (expected.Length == 0) continue;
                var people = normalizer.RmbExterior(record.Name).Block.StaticNpcs;
                Assert.Equal(expected.Length, people.Count);
                for (int i = 0; i < expected.Length; i++)
                    Assert.Equal(((int)expected[i].FactionId, expected[i].SourceOffset), (people[i].FactionId, people[i].SourceOffset));
                exteriorCount += people.Count;
            }
            else if (record.Name.EndsWith(".RDB", StringComparison.Ordinal))
            {
                var source = RdbDecoder.Decode(archive.GetPayload(record).Span, "BLOCKS.BSA");
                var expected = source.Flats.Where(flat => flat.TextureArchive is 334 or 346 or 357 or >= 175 and <= 184).ToArray();
                if (expected.Length == 0) continue;
                var block = normalizer.Rdb(record.Name).Block;
                Assert.Equal(expected.Length, block.StaticNpcs.Count);
                Assert.DoesNotContain(block.Billboards, billboard => block.StaticNpcs.Any(person => billboard.SpriteResourceId == $"sprite/texture-{person.BillboardArchive}-{person.BillboardRecord}"));
                dungeonCount += block.StaticNpcs.Count;
            }
        }
        Assert.True(exteriorCount > 0);
        Assert.True(dungeonCount > 0);
    }

    [CorpusFact("BLOCKS.BSA", "ARCH3D.BSA", "FACTION.TXT", "PAL.PAL")]
    public void Ground_scenery_grid_places_climate_nature_where_the_donor_stands_it()
    {
        var sources = Sources();
        var archive = BsaArchive.Parse(sources.Require("BLOCKS.BSA").Bytes.Span, "BLOCKS.BSA");
        Assert.True(archive.TryGetByName("TVRNAL01.RMB", out BsaRecord? record));
        byte[] bytes = archive.GetPayload(record!).ToArray();
        const int grid = 1995, tile = 256;
        const float scale = .025f;

        // RMBLayout.AddNatureFlats reads GroundScenery[x, 15 - y] and stands it at (x, y + 1) tiles, skipping records below 1.
        List<(string Sprite, NormalizedVector3 Position)> expected = [];
        for (int row = 0; row < 16; row++)
        for (int x = 0; x < 16; x++)
        {
            int cell = bytes[grid + (row * 16) + x];
            if (cell == 255 || (cell / 4) - 1 < 1) continue;
            expected.Add(($"sprite/texture-504-{(cell / 4) - 1}", new(x * tile * scale, -2 * scale, -(16 - row) * tile * scale)));
        }
        Assert.NotEmpty(expected);

        var plants = new WorldBlockNormalizer(sources).RmbExterior("TVRNAL01.RMB").Block.Billboards
            .Where(billboard => billboard.Id.Contains("/scenery-", StringComparison.Ordinal)).ToArray();
        Assert.Equal(expected, plants.Select(plant => (plant.SpriteResourceId, plant.Position)));
        Assert.All(plants, plant => Assert.True(plant.ClimateNature && plant.BaseAligned));
    }

    [CorpusFact("BLOCKS.BSA", "ARCH3D.BSA", "FACTION.TXT", "PAL.PAL")]
    public void Castle_necromoghans_flooded_block_publishes_the_source_water_surface()
    {
        var sources = Sources();
        var block = new WorldBlockNormalizer(sources).Rdb("W0000020.RDB").Block;
        var water = Assert.Single(block.WaterVolumes);
        Assert.Equal(10F, water.Bounds.Maximum.Y);
        Assert.Equal((0F, -51.2F, 51.2F, 0F),
            (water.Bounds.Minimum.X, water.Bounds.Minimum.Z, water.Bounds.Maximum.X, water.Bounds.Maximum.Z));
        Assert.True(water.Bounds.Minimum.Y < water.Bounds.Maximum.Y);
        Assert.Empty(new WorldBlockNormalizer(sources).Rdb("N0000065.RDB").Block.WaterVolumes);
    }

    [CorpusFact("BLOCKS.BSA", "ARCH3D.BSA", "FACTION.TXT", "PAL.PAL")]
    public void Invalid_spawn_distance_preserves_the_fixed_actor_and_reports_raw_source()
    {
        var block = new WorldBlockNormalizer(Sources()).Rdb("S0000003.RDB").Block;
        var actor = Assert.Single(block.Actors, actor => actor.Id == "actor/s0000003-rdb/0");
        Assert.Equal("actor/mobile-0", actor.ActorResourceId);
        Assert.Equal(0, actor.SpawnDistance);
        Assert.Contains(block.SourceWarnings, warning => warning.Contains("S0000003.RDB") && warning.Contains("spawnDistance 35") && warning.Contains("default 0"));
        Assert.All(block.Actors, value => Assert.InRange(value.SpawnDistance, 0, 6));
    }

    [CorpusFact("BLOCKS.BSA", "ARCH3D.BSA", "FACTION.TXT", "PAL.PAL")]
    public void Fixed_enemy_metadata_matches_every_source_marker()
    {
        var sources = Sources();
        var archive = BsaArchive.Parse(sources.Require("BLOCKS.BSA").Bytes.Span, sources.Require("BLOCKS.BSA").Label);
        var normalizer = new WorldBlockNormalizer(sources);
        int count = 0, passive = 0, genderFlags = 0, rawCount = 0, rawPassive = 0, rawFlags = 0;
        foreach (var record in archive.Records.Where(record => record.Name?.EndsWith(".RDB", StringComparison.Ordinal) == true))
        {
            var source = RdbDecoder.Decode(archive.GetPayload(record).Span, archive.Source);
            var markers = source.Flats.Where(flat => flat.TextureArchive == 199 && flat.TextureRecord == 16).ToArray();
            rawCount += markers.Length;
            rawPassive += markers.Count(flat => flat.Action == 99);
            rawFlags += markers.Count(flat => flat.Flags != 0);
            var expected = markers.Where(flat => (flat.FactionOrMobileId & 255) != 99).ToArray();
            var actors = normalizer.Rdb(record.Name!).Block.Actors;
            Assert.Equal(expected.Length, actors.Count);
            for (int i = 0; i < expected.Length; i++)
            {
                var flat = expected[i]; var actor = actors[i];
                string gender = (flat.FactionOrMobileId & 255) <= 43 ? "unspecified"
                    : (flat.Flags & 2) != 0 ? "male" : (flat.Flags & 1) != 0 ? "female" : "unspecified";
                Assert.Equal((flat.SoundIndex <= 6 ? (int)flat.SoundIndex : 0, flat.Action == 99, gender), (actor.SpawnDistance, actor.Passive, actor.Gender));
                count++; if (actor.Passive) passive++;
                if ((flat.FactionOrMobileId & 255) > 43 && flat.Flags != 0) genderFlags++;
            }
        }
        Assert.Equal((369, 70, 35), (rawCount, rawPassive, rawFlags));
        // The donor ignores mobile 99 and interprets gender flags only on class mobiles.
        Assert.Equal((368, 69, 26), (count, passive, genderFlags));
    }

    /// <summary>
    /// Travel lands a traveller on a city's start marker. The automap paints the marker's pixel as it paints every
    /// flat, and the outdoor navigation mask dropped the ground under it: arriving at Charing's east gate stood the
    /// player on no navigation. Each of Charing's gate markers now stands on a walkable ground cell of its block.
    /// </summary>
    [CorpusFact("MAPS.BSA", "BLOCKS.BSA", "ARCH3D.BSA", "FACTION.TXT")]
    public void Charing_s_navigation_holds_ground_under_every_gate_start_marker()
    {
        DungeonLogicalSourceSet sources = Sources();
        float cellSize = NavigationDerivationConfig.ClassicDefault.CellSize;
        WorldBlockNormalizer normalizer = new(sources);
        Dictionary<string, WorldBlockNormalization> blocks = new(StringComparer.Ordinal);
        int markers = 0;
        foreach (MapsExteriorBlock placed in Exterior(sources, 17, "Charing").Blocks)
        {
            if (!blocks.TryGetValue(placed.SourceName, out WorldBlockNormalization? block))
                blocks.Add(placed.SourceName, block = normalizer.RmbExterior(placed.SourceName));
            if (block.Block.StartMarkers.Count == 0) continue;
            HashSet<(int Column, int Row)> walkable = [.. EngineBinaryContent.ReadSpatial(block.Spatial!.Bytes.Span).Cells
                .Where(cell => cell.Walkable && cell.SupportHeight == 0).Select(cell => (cell.Column, cell.Row))];
            foreach (NormalizedMarker marker in block.Block.StartMarkers)
            {
                Assert.Contains(((int)MathF.Floor(marker.Position.X / cellSize), (int)MathF.Floor(marker.Position.Z / cellSize)), walkable);
                markers++;
            }
        }

        Assert.Equal(4, markers);
    }

    /// <summary>
    /// Each door into a building on Charing's 42 placed blocks names a building slot its block states, and each
    /// provider interior states the building its exterior slot describes, with its own collision and placements.
    /// </summary>
    [CorpusFact("MAPS.BSA", "BLOCKS.BSA", "ARCH3D.BSA", "FACTION.TXT")]
    public void Charings_blocks_state_every_building_a_door_enters()
    {
        DungeonLogicalSourceSet sources = Sources();
        MapsExteriorLayout charing = Exterior(sources, 17, "Charing");
        WorldBlockNormalizer normalizer = new(sources);
        Dictionary<string, DaggerfallWorldBlock> facts = charing.Blocks.Select(block => block.SourceName).Distinct(StringComparer.Ordinal)
            .ToDictionary(name => name, name => normalizer.RmbExterior(name).Block, StringComparer.Ordinal);
        Assert.Equal(42, charing.Blocks.Count);
        foreach ((string name, DaggerfallWorldBlock block) in facts)
        {
            Assert.All(block.Doors, door => Assert.StartsWith($"door/{PublishedIds.Slug(name)}-rmb/", door.Id, StringComparison.Ordinal));
            Assert.All(block.Doors.Where(door => door.BuildingIndex is not null), door => Assert.Contains(block.Buildings, building => building.Index == door.BuildingIndex));
        }

        foreach ((byte x, byte y, int building) in new (byte, byte, int)[] { (1, 1, 0), (2, 1, 0), (3, 4, 0), (3, 1, 13), (1, 5, 17), (4, 2, 0), (3, 2, 14) })
        {
            string name = Assert.Single(charing.Blocks, placed => placed.X == x && placed.Y == y).SourceName;
            DaggerfallWorldBlockBuilding slot = Assert.Single(facts[name].Buildings, value => value.Index == building);
            DaggerfallWorldBlock interior = normalizer.RmbInterior(name, building).Block;
            Assert.True(slot.HasInterior);
            Assert.Equal((slot.Index, slot.BuildingType, slot.FactionId), (interior.InteriorBuilding!.Index, interior.InteriorBuilding.BuildingType, interior.InteriorBuilding.FactionId));
            Assert.NotNull(interior.Spatial);
            Assert.Contains(interior.Models, model => model.Bounds is not null && model.SamplePoints is { Count: > 0 });
        }
    }

    /// <summary>
    /// DaggerfallInterior.AddModels stands an interior prop (object type 3) at +YPos, lifted so its lowest vertex
    /// rests there, where the room shells take -YPos as the exterior does. The tavern TVRNGL02 #9 has its ground
    /// floor at 0 and its upper floor at 3.225, and its props stand 5 cm above the floor their source names.
    /// </summary>
    [CorpusFact("BLOCKS.BSA", "ARCH3D.BSA", "FACTION.TXT", "PAL.PAL")]
    public void An_interior_prop_rests_its_lowest_vertex_at_its_source_height()
    {
        DungeonLogicalSourceSet sources = Sources();
        IReadOnlyList<RmbModelPlacement> source = SourcePlacements(sources, "TVRNGL02.RMB").Buildings[9].Interior.Models;
        DaggerfallWorldBlock tavern = new WorldBlockNormalizer(sources).RmbInterior("TVRNGL02.RMB", 9).Block;
        (DaggerfallWorldBlockModel Model, RmbModelPlacement Source)[] placed = [.. tavern.Models.Select(model => (model, source[Ordinal(model.Id)]))];
        Assert.All(placed.Where(value => value.Source.ObjectType != 3),
            value => Assert.Equal(-value.Source.Y * Arena2SourceTransform.SourceUnitMetres, value.Model.Position.Y, 3));
        Assert.All(placed.Where(value => value.Source.ObjectType == 3),
            value => Assert.Equal(value.Source.Y * Arena2SourceTransform.SourceUnitMetres, value.Model.Bounds!.Minimum.Y, 3));

        // The chest's lowest vertex lies 0.3 m below its origin, which the lift raises to 0.35.
        DaggerfallWorldBlockModel chest = Assert.Single(tavern.Models, model => model.ModelId == "41827");
        Assert.Equal(-0.3, LowestVertex(sources, "41827"), 3);
        Assert.Equal((0.35, 0.05), (Math.Round(chest.Position.Y, 3), Math.Round(chest.Bounds!.Minimum.Y, 3)));
        float[] Lowest(params string[] models) => [.. tavern.Models.Where(model => models.Contains(model.ModelId)).Select(model => model.Bounds!.Minimum.Y)];
        Assert.All(Lowest("41108", "41112", "41827", "41831", "41116", "41117"), lowest => Assert.Equal(0.05, lowest, 3));
        Assert.All(Lowest("41001", "41002", "41003", "41004"), lowest => Assert.Equal(3.275, lowest, 3));

        // A property container stands where its furniture is placed.
        Assert.NotEmpty(tavern.PropertyContainers);
        Assert.All(tavern.PropertyContainers, container => Assert.Equal(
            Assert.Single(tavern.Models, model => model.Id == $"model/{Ordinal(container.Id)}").Position, container.Position));
    }

    /// <summary>
    /// RMBLayout.AddProps lowers a block's own models by four raw units (its <c>propsOffsetY</c>), so the bank
    /// BANKAL01's fountain, signs and benches stand on the street rather than 0.1 m above it. Models a building
    /// places take no offset.
    /// </summary>
    [CorpusFact("BLOCKS.BSA", "ARCH3D.BSA", "FACTION.TXT", "PAL.PAL")]
    public void A_blocks_own_models_stand_a_tenth_of_a_metre_below_their_source_height()
    {
        DungeonLogicalSourceSet sources = Sources();
        RmbBlockPlacements source = SourcePlacements(sources, "BANKAL01.RMB");
        DaggerfallWorldBlock bank = new WorldBlockNormalizer(sources).RmbExterior("BANKAL01.RMB").Block;
        DaggerfallWorldBlockModel[] own = [.. bank.Models.Where(model => model.BuildingIndex is null)];
        Assert.Equal(source.MiscModels.Count, own.Length);
        Assert.All(own, model => Assert.Equal(
            (-source.MiscModels[Ordinal(model.Id)].Y * Arena2SourceTransform.SourceUnitMetres) - 0.1, model.Position.Y, 3));
        Assert.All(bank.Models.Where(model => model.BuildingIndex is int), model => Assert.Equal(
            -source.Buildings[model.BuildingIndex!.Value].Exterior.Models[Ordinal(model.Id)].Y * Arena2SourceTransform.SourceUnitMetres, model.Position.Y, 3));

        DaggerfallWorldBlockModel fountain = Assert.Single(own, model => model.ModelId == "41221");
        Assert.InRange(fountain.Position.Y + LowestVertex(sources, "41221"), -0.02F, 0.02F);
    }

    private static RmbBlockPlacements SourcePlacements(DungeonLogicalSourceSet sources, string block)
    {
        BsaArchive archive = BsaArchive.Parse(sources.Require("BLOCKS.BSA").Bytes.Span, "BLOCKS.BSA");
        Assert.True(archive.TryGetByName(block, out BsaRecord? record));
        byte[] bytes = archive.GetPayload(record!).ToArray();
        Assert.True(RmbBlockSummaryReader.TryRead(bytes, archive.Source, 0, bytes.Length, out RmbBlockSummary? summary, out _));
        return RmbPlacementReader.Read(bytes, 0, summary!, archive.Source);
    }

    /// <summary>A model's lowest vertex in its own Y-up frame.</summary>
    private static float LowestVertex(DungeonLogicalSourceSet sources, string modelId)
    {
        BsaArchive arch = BsaArchive.Parse(sources.Require("ARCH3D.BSA").Bytes.Span, "ARCH3D.BSA");
        uint id = uint.Parse(modelId, System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(arch.TryGetByNumericId(id, out BsaRecord? record));
        return Arch3dDecoder.Decode(arch.GetPayload(record!).Span, arch.Source, id).Planes
            .SelectMany(plane => plane.Points).Min(point => Arena2SourceTransform.ToImportPoint(point).YMetres);
    }

    /// <summary>The ordinal that ends a block model's or container's identity.</summary>
    private static int Ordinal(string id) => int.Parse(id[(id.LastIndexOf('/') + 1)..], System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Privateer's Hold's blocks, with the catalog's start block and texture table: each action model carries its
    /// own collision and each door its local extent, and the table states every archive it redraws.
    /// </summary>
    [CorpusFact("MAPS.BSA", "BLOCKS.BSA", "ARCH3D.BSA", "CLIMATE.PAK", "FACTION.TXT", "PAL.PAL")]
    public void Privateers_hold_blocks_carry_their_moving_models_and_the_catalogs_texture_table()
    {
        DungeonLogicalSourceSet sources = Sources();
        DaggerfallLocations catalog = Catalog(sources);
        DaggerfallLocationMap location = Assert.Single(catalog.Locations, value => value.Region == 17 && value.Name == "Privateer's Hold");
        DaggerfallDungeonRecord dungeon = Assert.Single(catalog.Dungeons, value => value.Region == 17 && value.Index == location.Index);
        DaggerfallDungeonTextureTable table = dungeon.TextureTable!;
        WorldBlockNormalizer normalizer = new(sources);
        DaggerfallWorldBlock[] facts = [.. dungeon.BlockPlacements.Select(block => block.SourceKey).Distinct(StringComparer.Ordinal)
            .Select(name => normalizer.Rdb(name).Block)];

        foreach (DaggerfallWorldBlock block in facts)
        {
            Assert.All(block.Models.Where(model => model.Action is not null && model.MeshArtifactId is not null), model =>
            {
                Assert.NotNull(model.Collision);
                Assert.NotEmpty(model.Collision!.Triangles);
                Assert.All(model.Collision.Triangles, triangle => Assert.True(
                    Math.Max(triangle.FirstVertex, Math.Max(triangle.SecondVertex, triangle.ThirdVertex)) < model.Collision.Vertices.Count));
            });
            Assert.All(block.Models.Where(model => model.DoorId is not null && model.MeshArtifactId is not null),
                model => Assert.NotNull(model.LocalBounds));
        }

        Assert.Contains(facts.SelectMany(block => block.Models), model => model.Action is not null && model.LocalBounds is not null);
        Assert.Contains(facts, block => block.WaterLevel is not null || block.AmbientZone is not null || block.Spatial is not null);
        // The catalog's texture table states every archive it redraws, the door archive included.
        Assert.Equal(DungeonTextureTableTransform.RemappedArchives.Select(archive => (int)archive), table.Remaps.Select(remap => remap.Archive));
        Assert.All(table.Remaps, remap => Assert.Equal(DungeonTextureTableTransform.RemapArchive((ushort)remap.Archive,
            [.. table.Archives.Select(value => (ushort)value)], (ushort)table.DoorArchiveOffset), remap.TargetArchive));
    }

    /// <summary>
    /// The doors that change worlds are published as the donor's MeshReader types them: a building door seen
    /// from an interior leads out, facing the interior's enter marker; a dungeon entrance plane on an RMB
    /// exterior leads in; a dungeon exit plane in an RDB block leads out beside the start block's start marker.
    /// A city gate's open and closed models each carry their own collision, and an exterior states every
    /// start marker it places.
    /// </summary>
    [CorpusFact("MAPS.BSA", "BLOCKS.BSA", "ARCH3D.BSA", "CLIMATE.PAK", "FACTION.TXT", "PAL.PAL")]
    public void Transition_doors_gates_and_start_markers_follow_the_donor_door_types()
    {
        DungeonLogicalSourceSet sources = Sources();
        WorldBlockNormalizer normalizer = new(sources);

        DaggerfallWorldBlock interior = normalizer.RmbInterior("RESIAL05.RMB", 0).Block;
        NormalizedVector3 enter = interior.EnterMarker!.Position;
        DaggerfallWorldBlockTransitionDoor exit = Assert.Single(interior.TransitionDoors);
        Assert.Equal((DaggerfallWorldBlockTransitionKind.BuildingExit, 74), (exit.Kind, exit.Texture.Archive % 100));
        Assert.True(((enter.X - exit.Position.X) * exit.Normal.X) + ((enter.Z - exit.Position.Z) * exit.Normal.Z) > 0F,
            "A building exit faces the interior it is used from.");
        Assert.Contains(interior.Models, model => model.Id == exit.ModelId);

        DaggerfallLocations catalog = Catalog(sources);
        DaggerfallDungeonRecord dungeon = Assert.Single(catalog.Dungeons, value => value.Region == 17
            && value.Index == Assert.Single(catalog.Locations, location => location.Region == 17 && location.Name == "Privateer's Hold").Index);
        DaggerfallWorldBlock startBlock = normalizer.Rdb(Assert.Single(dungeon.BlockPlacements, block => block.Start).SourceKey).Block;
        DaggerfallWorldBlockTransitionDoor dungeonExit = Assert.Single(startBlock.TransitionDoors);
        Assert.Equal((DaggerfallWorldBlockTransitionKind.DungeonExit, 95), (dungeonExit.Kind, dungeonExit.Texture.Archive));
        Assert.True(Distance(dungeonExit.Position, startBlock.StartMarker!.Position) < 2F, "The start marker stands at the dungeon's exit.");

        MapsExteriorLayout klerd = Exterior(sources, 0, "The Tombs of Klerd");
        DaggerfallWorldBlockTransitionDoor[] entrances = [.. klerd.Blocks.SelectMany(block => normalizer.RmbExterior(block.SourceName).Block.TransitionDoors)];
        Assert.NotEmpty(entrances);
        Assert.All(entrances, door => Assert.True(door.Kind == DaggerfallWorldBlockTransitionKind.DungeonEntrance
            && (door.Texture.Archive == 56 || door.Texture.Archive % 100 == 56 || door.Texture.Archive == 331 && door.Texture.Record > 0)));

        DaggerfallWorldBlock wall = normalizer.RmbExterior("WALLAA08.RMB").Block;
        DaggerfallWorldBlockGate gate = Assert.Single(wall.Gates);
        Assert.Equal(("446", "447"), (gate.Open.ModelId, gate.Closed.ModelId));
        Assert.Equal(("geometry/mesh-446", "geometry/mesh-447"), (gate.Open.MeshArtifactId, gate.Closed.MeshArtifactId));
        Assert.All(new[] { gate.Open, gate.Closed }, state => Assert.NotEmpty(state.Collision.Triangles));
        DaggerfallWorldBlockModel placed = Assert.Single(wall.Models, model => model.Id == gate.ModelId);
        Assert.Equal((placed.Position, placed.Rotation), (gate.Position, gate.Rotation));

        MapsExteriorLayout charing = Exterior(sources, 17, "Charing");
        Assert.All(charing.Blocks.Select(block => normalizer.RmbExterior(block.SourceName).Block), block =>
            Assert.Equal(block.StartMarker?.Position, block.StartMarkers.FirstOrDefault()?.Position));
        Assert.Contains(charing.Blocks, block => normalizer.RmbExterior(block.SourceName).Block.StartMarkers.Count != 0);
    }

    private static float Distance(NormalizedVector3 first, NormalizedVector3 second) =>
        MathF.Sqrt(((first.X - second.X) * (first.X - second.X)) + ((first.Y - second.Y) * (first.Y - second.Y)) + ((first.Z - second.Z) * (first.Z - second.Z)));

    /// <summary>
    /// Every RDB random-enemy editor marker (archive 199, record 15) is published on its block with its
    /// position, encounter slot, spawn distance and reaction, whether or not it is also an action node: 2,195
    /// markers in 171 blocks. N0000000.RDB's flat 14 carries no action record; B0000000.RDB's flat 0 has flags only
    /// and is not an action node.
    /// </summary>
    [CorpusFact("BLOCKS.BSA", "ARCH3D.BSA", "FACTION.TXT", "PAL.PAL")]
    public void Every_rdb_random_enemy_marker_is_published_on_its_block()
    {
        DungeonLogicalSourceSet sources = Sources();
        BsaArchive archive = BsaArchive.Parse(sources.Require("BLOCKS.BSA").Bytes.Span, sources.Require("BLOCKS.BSA").Label);
        WorldBlockNormalizer normalizer = new(sources);
        int markers = 0, markedBlocks = 0, examples = 0;
        foreach (string name in archive.Records.Select(record => record.Name).OfType<string>().Where(name => name.EndsWith(".RDB", StringComparison.Ordinal)))
        {
            Assert.True(archive.TryGetByName(name, out BsaRecord? record));
            RdbBlockSource source = RdbDecoder.Decode(archive.GetPayload(record!).Span, archive.Source);
            (RdbFlatSource Flat, int Ordinal)[] expected = [.. source.Flats.Select((flat, ordinal) => (flat, ordinal))
                .Where(value => value.flat.TextureArchive == 199 && value.flat.TextureRecord == 15)];
            DaggerfallWorldBlock block = normalizer.Rdb(name).Block;
            string scope = PublishedIds.Slug(name);
            Assert.Equal(expected.Select(value => (
                    $"random-enemy/{scope}/{value.Ordinal}",
                    MeshGeometry.ToRightHanded(Arena2SourceTransform.ToImportPoint(value.Flat.X, value.Flat.Y, value.Flat.Z)),
                    (int)value.Flat.Flags, (int)value.Flat.SoundIndex, value.Flat.Action == 99, value.Ordinal, value.Flat.ObjectOffset)),
                block.RandomEnemies.Select(enemy => (enemy.Id, enemy.Position, enemy.EncounterSlot, enemy.SpawnDistance, enemy.Passive, enemy.SourceOrdinal, enemy.ObjectOffset)));
            var start = source.Flats.FirstOrDefault(flat => flat.TextureArchive == 199 && flat.TextureRecord == 10);
            int? water = start is { SoundIndex: not 0 } ? -8 * start.SoundIndex : null;
            Assert.Equal(expected.Select(value => water is int level && level < value.Flat.Y), block.RandomEnemies.Select(enemy => enemy.Underwater));
            HashSet<string> actions = [.. block.Actions.Select(action => action.Id)];
            Assert.All(expected, value => Assert.Equal(value.Flat.Action > 0,
                actions.Contains($"action/{scope}/flat-{value.Ordinal}")));
            Assert.All(block.RandomEnemies, enemy => Assert.Equal(actions.Contains($"action/{scope}/flat-{enemy.SourceOrdinal}"), enemy.ActionId is not null));
            Assert.All(block.RandomEnemies.Where(enemy => enemy.ActionId is not null), enemy => Assert.Contains(enemy.ActionId!, actions));
            markers += block.RandomEnemies.Count;
            markedBlocks += block.RandomEnemies.Count == 0 ? 0 : 1;

            if (name == "N0000000.RDB")
            {
                DaggerfallWorldBlockRandomEnemy unlinked = Assert.Single(block.RandomEnemies, enemy => enemy.SourceOrdinal == 14);
                // Source (496, -1840, 752): Y down to Y up and Z forward to the right-handed frame's -Z.
                Near(new(12.4F, 46F, -18.8F), unlinked.Position);
                Assert.Equal(new DaggerfallWorldBlockRandomEnemy("random-enemy/n0000000-rdb/14", unlinked.Position, 0, 2, false, null, 14, 10410), unlinked);
                Assert.DoesNotContain(block.Actions, action => action.Id == "action/n0000000-rdb/flat-14");
                examples++;
            }
            else if (name == "B0000000.RDB")
            {
                DaggerfallWorldBlockRandomEnemy linked = Assert.Single(block.RandomEnemies, enemy => enemy.SourceOrdinal == 0);
                Assert.Equal(((string?)null, 1, 1, 12567), (linked.ActionId, linked.EncounterSlot, linked.SpawnDistance, linked.ObjectOffset));
                Assert.DoesNotContain(block.Actions, action => action.Id == "action/b0000000-rdb/flat-0");
                examples++;
            }
        }

        Assert.Equal((2195, 171, 2), (markers, markedBlocks, examples));
    }

    /// <summary>
    /// The catalog states each location's climate and each dungeon's start block and classic texture table: the
    /// inputs and table a site assembles the dungeon with.
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
        MapsLocationRecord source = Assert.Single(MapsDecoder.DecodeRegionLocations(maps, 17), value => value.Index == hold.Index);
        MapsDungeonLocation layout = Assert.Single(MapsDecoder.DecodeRegionDungeons(maps, 17), value => value.Index == hold.Index);
        (int x, int y) = MapsDecoder.ToMapPixel(source.Longitude, source.Latitude);
        Assert.True(climate.TryGetPixel(x, y, out byte value));
        Assert.Equal(value, hold.Climate);
        DaggerfallDungeonRecord dungeon = Assert.Single(catalog.Dungeons, record => record.Region == 17 && record.Index == hold.Index);
        Assert.Equal(DungeonTextureTableTransform.CreateClassic(layout.DungeonLocationId, value).Select(archive => (int)archive), dungeon.TextureTable!.Archives);
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
        const string root = "worldrpg/imports/world-blocks/";
        DaggerfallWorldBlockIndex index = JsonSerializer.Deserialize<DaggerfallWorldBlockIndex>(
            ImportedContent.Read(root + Arena2WorldBlocksPublication.IndexRelativePath), PublishedJson.SectionRead)!;
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
            DaggerfallWorldBlock block = JsonSerializer.Deserialize<DaggerfallWorldBlock>(ImportedContent.Read(root + entry.Document), PublishedJson.SectionRead)!;
            Assert.Equal((entry.Key, entry.Kind, entry.SourceKey, entry.BuildingIndex), (block.Key, block.Kind, block.SourceKey, block.BuildingIndex));
            Assert.Equal(entry.Spatial, block.Spatial?.RelativePath);
            if (block.Spatial is { } spatial)
                Assert.Equal(spatial.ContentDigest, ContentDigest.Compute(ImportedContent.Read(root + spatial.RelativePath)));
        }
    }

    private static void Near(NormalizedVector3 expected, NormalizedVector3 actual)
    {
        Assert.InRange(actual.X, expected.X - 1e-3F, expected.X + 1e-3F);
        Assert.InRange(actual.Y, expected.Y - 1e-3F, expected.Y + 1e-3F);
        Assert.InRange(actual.Z, expected.Z - 1e-3F, expected.Z + 1e-3F);
    }

    /// <summary>The RMB grid of the region's first location of that name, as a by-name lookup in MAPNAMES finds it.</summary>
    private static MapsExteriorLayout Exterior(DungeonLogicalSourceSet sources, int region, string name)
    {
        BsaArchive maps = BsaArchive.Parse(sources.Require("MAPS.BSA").Bytes.Span, sources.Require("MAPS.BSA").Label);
        return MapsDecoder.DecodeExteriorLayout(maps, region, MapsDecoder.DecodeLocationNames(maps, region).ToList().IndexOf(name));
    }

    private static DaggerfallLocations Catalog(DungeonLogicalSourceSet sources) => DaggerfallLocationBuilder.Build(
        BsaArchive.Parse(sources.Require("MAPS.BSA").Bytes.Span, sources.Require("MAPS.BSA").Label),
        BsaArchive.Parse(sources.Require("BLOCKS.BSA").Bytes.Span, sources.Require("BLOCKS.BSA").Label),
        BsaArchive.Parse(sources.Require("ARCH3D.BSA").Bytes.Span, sources.Require("ARCH3D.BSA").Label),
        PakDecoder.Decode(sources.Require("CLIMATE.PAK").Bytes.Span, sources.Require("CLIMATE.PAK").Label));

    [CorpusFact("BLOCKS.BSA", "ARCH3D.BSA", "FACTION.TXT")]
    public void A_placed_block_whose_building_slot_or_city_map_the_block_catalog_lacks_is_refused()
    {
        // Every building slot of a placed RMB block is a building of each location placing it, so the block building
        // catalog the runtime joins locations to must carry them all, and the block's city map, or the import stops.
        DaggerfallBlockBuildingSet catalog = DaggerfallBlockBuildingSet.From(DaggerfallBlocksBuilder.Build(
            File.ReadAllBytes(Path.Combine(TestData.CorpusRoot, "BLOCKS.BSA")), PublishedSourcePath.Arena2("BLOCKS.BSA")));
        HashSet<string> mapped = [.. catalog.Maps.Select(map => map.Block)];
        string block = catalog.Buildings.Select(building => building.Block).First(mapped.Contains);
        DaggerfallLocations locations = new([new DaggerfallLocationMap(0, 0, "Fixture", 1, 128 * 300, 128 * 200, 0, 8, true)
        {
            Exterior = new DaggerfallLocationExterior(1, 300, 299, 1, 1, 'A', 56, 56, false, 2, new DaggerfallLocationTerrainRect(54, 73, 54, 73),
                [new DaggerfallLocationExteriorBlock(block, 0, 0)]),
        }], [], [], [], [], []);
        DaggerfallBlockBuildingFields slot = catalog.Buildings.First(building => building.Block == block);

        DaggerfallBlockBuildingSet withoutSlot = catalog with { Buildings = [.. catalog.Buildings.Where(building => building != slot)] };
        Assert.Contains($"'{block}', whose building {slot.Index} the block catalog does not publish",
            Assert.Throws<InvalidOperationException>(() => Arena2WorldBlocksPublication.Create(Sources(), locations, "world-media", catalog: withoutSlot)).Message);
        DaggerfallBlockBuildingSet withoutMap = catalog with { Maps = [.. catalog.Maps.Where(map => map.Block != block)] };
        Assert.Contains($"'{block}', which the block catalog publishes no city map of",
            Assert.Throws<InvalidOperationException>(() => Arena2WorldBlocksPublication.Create(Sources(), locations, "world-media", catalog: withoutMap)).Message);
        Assert.NotEmpty(Arena2WorldBlocksPublication.Create(Sources(), locations, "world-media", catalog: catalog).Plan.Artifacts);
    }

    internal static DungeonLogicalSourceSet Sources(bool textures = true) => new(Directory.EnumerateFiles(TestData.CorpusRoot)
        .Where(path => Path.GetFileName(path) is "MAPS.BSA" or "BLOCKS.BSA" or "ARCH3D.BSA" or "CLIMATE.PAK" or "FACTION.TXT" or "FLATS.CFG" or "PAL.PAL"
            || textures && Path.GetFileName(path).StartsWith("TEXTURE.", StringComparison.Ordinal))
        .Select(path => new DungeonLogicalSource($"arena2/{Path.GetFileName(path)}", File.ReadAllBytes(path))));
}
