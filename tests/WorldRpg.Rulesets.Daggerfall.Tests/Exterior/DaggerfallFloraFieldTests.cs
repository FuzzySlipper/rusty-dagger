using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using Rusty.Engine.Testing;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Nature a location places grows in the flora field where its record grows as a mesh (#10728): a copy of its record's own
/// layer, standing where the location placed it, in its season's record, counted in the layer's budget, while a record the
/// field draws as a sprite is left to the location. A tree's trunk and a rock's bulk stand as bodies near the player
/// (#10948), so walkers are stopped by them and the wild grid paths round them.
/// </summary>
public sealed class DaggerfallFloraFieldTests
{
    private const string HashHex = "0000000000000001000000000000000200000000000000030000000000000004";
    private const float Spacing = DaggerfallTerrainSurfaceBuilder.SampleSpacing;
    private const double Cell = DaggerfallExteriorCellResidency.CellSize;
    private static readonly (int MapX, int MapY) Frame = (2, 2);

    [Fact]
    public void Placed_nature_whose_record_grows_as_a_mesh_is_a_copy_of_its_season_s_layer_where_it_stands()
    {
        EngineContextFake engine = Engine();
        // Grown at no density, a layer's budget is what the locations place in it, so placed copies are drawn however thin the wild growth.
        using DaggerfallFloraField field = new(engine.Context, Ground(), DaggerfallFloraTuning.Default with { Density = 0 }, DaggerfallFlatsTuning.Default,
            new Dictionary<int, float>());
        (double x, double z) = ((Frame.MapX * Cell) + 120.2, (Frame.MapY * Cell) + 140.3);
        // One rock as the town placed it, and again as measured from another frame a hair away; and a bush.
        DaggerfallPlacedNature[] town = [new(x, 31.25F, z, (504, 3), (505, 3)), new(x + .001, 31.25F, z - .001, (504, 3), (505, 3)), new(x + 4, 31, z, (504, 7), (505, 7))];
        field.Place(town);

        Show(field, winter: false);
        Assert.True(field.GrowsPlaced((504, 3)));
        Assert.False(field.GrowsPlaced((504, 7)));
        Assert.False(field.GrowsPlaced((504, 9)));
        Assert.Equal(2U, Last(engine, 504, 3).MaximumInstances);
        Assert.Equal(0U, Last(engine, 504, 7).MaximumInstances);
        // The winter archive grows only once winter comes.
        Assert.DoesNotContain(engine.FloraLayers, layer => layer.Layer == DaggerfallFloraField.Layer(505, 3));

        VegetationTileRequest tile = Tile(x, z);
        VegetationRecord[] summer = Sample(engine, field, tile, winter: false);
        Assert.Equal(2, summer.Length);
        Assert.All(summer, copy => Assert.Equal(DaggerfallFloraField.Layer(504, 3), copy.Layer));
        Assert.Equal(new Vector3((float)(x - tile.OriginX), 31.25F, (float)(z - tile.OriginZ)), summer[0].Position);
        Assert.Equal(1F, summer[0].Scale);
        Assert.InRange(summer[0].Yaw, 0F, MathF.Tau);
        // The same rock stands the same way whatever frame measured it.
        Assert.Equal(summer[0].Yaw, summer[1].Yaw);

        // Winter grows the winter record's layer in its place.
        VegetationRecord[] winter = Sample(engine, field, tile, winter: true);
        Assert.All(winter, copy => Assert.Equal(DaggerfallFloraField.Layer(505, 3), copy.Layer));
        Assert.True(field.GrowsPlaced((505, 3)));

        // A town arriving beside it has its own ground sampled again, and no other.
        int invalidated = engine.FloraInvalidated.Count;
        (double farX, double farZ) = (x + 2_000, z + 1_500);
        field.Place([.. town, new(farX, 40, farZ, (504, 3), (505, 3))]);
        Show(field, winter: true);
        VegetationInvalidateRequest arrived = Assert.Single(engine.FloraInvalidated.Skip(invalidated));
        Assert.Equal((farX, farZ, farX, farZ), (arrived.MinX, arrived.MinZ, arrived.MaxX, arrived.MaxZ));

        // When both are gone, the ground under each rock is sampled again, and holds no copy of it.
        invalidated = engine.FloraInvalidated.Count;
        field.Place([]);
        Show(field, winter: true);
        Assert.Equal([(x, z), (farX, farZ)], engine.FloraInvalidated.Skip(invalidated).Select(moved => (moved.MinX, moved.MinZ)));
        Assert.Empty(Sample(engine, field, tile, winter: true));
    }

    [Fact]
    public void A_field_hidden_or_growing_sprites_leaves_placed_nature_to_its_location()
    {
        EngineContextFake engine = Engine();
        using DaggerfallFloraField field = new(engine.Context, Ground(), DaggerfallFloraTuning.Default, DaggerfallFlatsTuning.Default, new Dictionary<int, float>());
        field.Place([new((Frame.MapX * Cell) + 120, 31, (Frame.MapY * Cell) + 140, (504, 3), (505, 3))]);
        Assert.False(field.GrowsPlaced((504, 3)));

        Show(field, winter: false);
        Assert.True(field.GrowsPlaced((504, 3)));
        field.Hide();
        Assert.False(field.GrowsPlaced((504, 3)));
        Show(field, winter: false);
        Assert.True(field.GrowsPlaced((504, 3)));

        // `world.trees sprite`: the field grows the rock as its sprite, and the location draws its own.
        field.UseTreeMeshes(false);
        Show(field, winter: false);
        Assert.False(field.GrowsPlaced((504, 3)));
    }

    /// <summary>
    /// The desert's smallest nature meshes (#10871): 503-22 draws 0.15 m tall, so a card twelve of its heights out would
    /// start inside the band the Engine crossfades it over, and the Engine refused the whole layer's detail, faulting the
    /// update on arrival in the Alik'r Desert (The Nhurche Tunnel). Grown in the real Engine from the art pack as it ships,
    /// the desert's ground grows it.
    /// </summary>
    [Fact]
    public void The_desert_s_smallest_mesh_grows_its_card_in_the_real_Engine() =>
        GrowsInTheRealEngine((503, 22), climate: 224, winter: false, new Vector2(.15F, .148F));

    /// <summary>
    /// Charing's winter rock (#10871): 507-4's mesh shipped with 216 normals of no length, which the reader normalized to
    /// no number at all, and the Engine refused the mesh, faulting the update in winter in Charing's climate. Grown in the
    /// real Engine from the art pack as it ships, it stands with every normal of unit length.
    /// </summary>
    [Fact]
    public void Charing_s_winter_rock_grows_in_the_real_Engine_with_sound_normals()
    {
        DaggerfallTreeArt rock = GrowsInTheRealEngine((507, 4), climate: 230, winter: true, new Vector2(1.5F, 2.3F));
        using JsonDocument mesh = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(Content, rock.Mesh.Path)));
        float[] normals = [.. mesh.RootElement.GetProperty("parts")[0].GetProperty("normals").EnumerateArray().Select(value => value.GetSingle())];
        Assert.All(normals.Chunk(3), n => Assert.InRange(new Vector3(n[0], n[1], n[2]).Length(), .999F, 1.001F));
    }

    [Fact]
    public void A_mesh_record_s_layer_collides_as_its_body_near_the_player_and_a_sprite_s_does_not()
    {
        EngineContextFake engine = Engine();
        using DaggerfallFloraField field = new(engine.Context, Ground(), DaggerfallFloraTuning.Default, DaggerfallFlatsTuning.Default, new Dictionary<int, float>());
        field.Place([new((Frame.MapX * Cell) + 120, 31, (Frame.MapY * Cell) + 140, (504, 3), (505, 3))]);
        Show(field, winter: false);
        // The rock's one face rises 1 m from its foot; cut a third of the way up, its edges cross at 0 and 2/3 m from the axis.
        VegetationLayerRequest rock = Last(engine, 504, 3);
        Assert.Equal(2F / 3, rock.ColliderRadius, 1e-5F);
        Assert.Equal((1F, DaggerfallFloraTuning.Default.ColliderReach), (rock.ColliderHeight, rock.ColliderReach));
        VegetationLayerRequest bush = Last(engine, 504, 7);
        Assert.Equal((0F, 0F, 0F), (bush.ColliderRadius, bush.ColliderHeight, bush.ColliderReach));
        // `world.tree-bodies off` sets the rock's layer again with no body, and on gives it back.
        field.UseBodies(false);
        Assert.Equal(0F, Last(engine, 504, 3).ColliderRadius);
        field.UseBodies(true);
        Assert.Equal(2F / 3, Last(engine, 504, 3).ColliderRadius, 1e-5F);
    }

    /// <summary>
    /// Charing's winter tree (507-16, 14.5 m tall on a trunk about 0.48 m round) and rock (507-4, 2 m tall, about 0.43 m round its
    /// bulk), placed by a location and grown from the art pack as it ships in the real Engine: each stops a walker going
    /// straight at it, and the wild grid about the player, derived from the session's collision with no change of its own,
    /// takes a walker round the tree to the far side.
    /// </summary>
    [Fact]
    public void Charing_s_tree_and_rock_stop_a_walker_and_the_wild_grid_paths_round_them()
    {
        (int, int) tree = (507, 16), rock = (507, 4);
        // Everything stands on the flora's own ground, whose height its layers' reach is measured from.
        DaggerfallHorizonGround ground = Ground(230);
        float height = (float)ground.WalkedGrid(Frame.MapX * Cell, Frame.MapY * Cell, 1, 1)[4];
        Vector3 treeAt = new(.5F, height, .5F), rockAt = new(20.5F, height, .5F);
        DaggerfallArtReplacements art = DaggerfallArtReplacements.Read(
            File.ReadAllBytes(Path.Combine(Content, "worldrpg", "packs", "daggerfall.art-replacements", "payload.json")), "daggerfall.art-replacements");
        Dictionary<(int, int), DaggerfallFloraLook> looks = [];
        Dictionary<string, ReadOnlyMemory<byte>> served = [];
        foreach ((int, int) record in new[] { tree, rock })
        {
            DaggerfallTreeArt mesh = art.Trees[record];
            DaggerfallArtSprite flat = art.Scenery[record];
            foreach (DaggerfallArtFile file in new[] { mesh.Mesh, flat.File, mesh.Bark, mesh.Leaves, mesh.Stone, art.TreeSnow }.OfType<DaggerfallArtFile>())
                served[file.Path] = File.ReadAllBytes(Path.Combine(Content, file.Path));
            looks[record] = new(flat.File, Vector2.Zero, Vector2.One, new Vector2(1, mesh.Height), new Vector2(.5F, 0)) { Tree = mesh, TreeSnow = art.TreeSnow };
        }
        DaggerfallPlacedNature Placed(Vector3 at, (int Archive, int Record) record) =>
            new((Frame.MapX * Cell) + at.X, at.Y, (Frame.MapY * Cell) + at.Z, (record.Archive - 1, record.Record), record);

        using EngineTestHost host = EngineTestHost.Create(new EngineTestHostOptions { Content = served });
        SpatialMovementSystem? movement = null;
        DaggerfallFloraField? field = null;
        DaggerfallWildNavigation? wild = null;
        try
        {
            host.Call(engine =>
            {
                movement = new(engine.Spatial, engine.Content, null, DaggerfallTuning.Defaults.Spatial);
                Vector3[] floor = [new(-60, height, -60), new(-60, height, 60), new(60, height, 60), new(60, height, -60)];
                _ = engine.Spatial.ApplyCollisionResidency(new CollisionResidencyRequest(movement.Session,
                    new[] { new StaticMeshAsset(1, 0, 4, 0, 2) }, floor, new Triangle[] { new(0, 1, 2), new(0, 2, 3) },
                    new[] { new StaticMeshInstance(2, 1, new Transform(Vector3.Zero, Quaternion.Identity, Vector3.One)) },
                    ReadOnlyMemory<ulong>.Empty, ReadOnlyMemory<ulong>.Empty));
                // Only what the location places grows, so the two stand alone.
                field = new(engine, ground, DaggerfallFloraTuning.Default with { Density = 0 }, DaggerfallFlatsTuning.Default, new Dictionary<int, float>());
                field.Place([Placed(treeAt, tree), Placed(rockAt, rock)]);
            });
            // Calls go by while the field samples its tiles, places the copies and their bodies are admitted.
            bool Standing(Vector3 at, float above) =>
                movement!.CastRay(at + (Vector3.UnitY * 30), -Vector3.UnitY, 40) is { Present: true } hit && hit.Point.Y > at.Y + above;
            for (int call = 0; call < 2_000 && !host.Call(_ =>
            {
                field!.Show(movement!.Session, Frame, new Vector3(0, height, 0), winter: true, (archive, record) => looks.GetValueOrDefault((archive, record)));
                return Standing(treeAt, 10) && Standing(rockAt, 1.5F);
            }); call++)
                Thread.Sleep(5);
            host.Call(engine =>
            {
                Assert.True(Standing(treeAt, 10), "The tree never stood as a body.");
                Assert.True(Standing(rockAt, 1.5F), $"The rock never stood as a body: {field!.Readout()}, ray {movement!.CastRay(rockAt + (Vector3.UnitY * 30), -Vector3.UnitY, 40).Point}.");
                // Its trunk, not its crown: a ray down beside the trunk, under the branches, reaches the floor.
                Assert.False(Standing(treeAt + new Vector3(1.2F, 0, 0), .5F), "The tree stood as a body past its trunk.");
                // Off every grid a walker goes straight at each, and stops short of it.
                using ActorsState actors = new();
                actors.CreatePlayer(1, new EntityTypeId("player"), new StatsComponent(), "health");
                foreach ((Vector3 at, long id) in new[] { (treeAt, 42L), (rockAt, 43L) })
                {
                    ActorState walker = Walker(actors, id, at + new Vector3(0, 0, 6));
                    ActorNavigationCoordinator straight = new(engine.Spatial, movement!.Session, actors.Store, movement.CurrentController,
                        _ => CharacterStepEnvironment.Empty, () => [Composition]);
                    for (int step = 0; step < 200; step++)
                        straight.Evaluate(walker, new ActorNavigationRequest(WorldPoint.From(at - new Vector3(0, 0, 6)), .25F, 4096));
                    Assert.True(walker.Position.Z > at.Z + .4F, $"The walker passed through what stands at {at} to {walker.Position}.");
                }
                wild = new(engine.Spatial, movement!, WildTuning);
                wild.Update(WorldPoint.From(treeAt), outdoors: true, 1d / 60, Composition);
            });
            for (int call = 0; call < 400 && wild!.Pending; call++)
            {
                Thread.Sleep(5);
                host.Call(_ => wild.Update(WorldPoint.From(treeAt), outdoors: true, 1d / 60, Composition));
            }
            Assert.False(wild!.Pending, "The wild grid never installed.");
            host.Call(engine =>
            {
                // On the wild grid a walker goes round the tree to the far side.
                using ActorsState actors = new();
                actors.CreatePlayer(1, new EntityTypeId("player"), new StatsComponent(), "health");
                Vector3 goal = treeAt - new Vector3(0, 0, 6);
                ActorState walker = Walker(actors, 44, treeAt + new Vector3(0, 0, 6));
                ActorNavigationCoordinator navigation = new(engine.Spatial, movement!.Session, actors.Store, movement.CurrentController,
                    _ => CharacterStepEnvironment.Empty, () => wild.Grids(Composition));
                float widest = 0;
                for (int step = 0; step < 400 && Vector2.Distance(new(walker.Position.X, walker.Position.Z), new(goal.X, goal.Z)) > .6F; step++)
                {
                    navigation.Evaluate(walker, new ActorNavigationRequest(WorldPoint.From(goal), .25F, 4096));
                    if (MathF.Abs(walker.Position.Z - treeAt.Z) < 1) widest = MathF.Max(widest, MathF.Abs(walker.Position.X - treeAt.X));
                }
                Assert.True(Vector2.Distance(new(walker.Position.X, walker.Position.Z), new(goal.X, goal.Z)) <= .6F, $"The walker stopped at {walker.Position}.");
                Assert.True(widest > .48F, $"The walker passed the tree no wider than {widest} m from its axis, inside its trunk.");
                Assert.True(navigation.TryRoute(walker.Actor.Entity, out ulong? grid));
                Assert.Equal(DaggerfallWildNavigation.GridId, grid);
            });
        }
        finally
        {
            host.Call(_ =>
            {
                field?.Dispose();
                movement?.Dispose();
            });
        }
    }

    /// <summary>The location's own grid, which the test publishes none of.</summary>
    private const ulong Composition = 1;

    /// <summary>The wild grid 16 m about the player in 1 m cells.</summary>
    private static readonly DaggerfallWildNavigationTuning WildTuning = new(16F, 1F, 4F, 60d, 8F);

    private static ActorState Walker(ActorsState actors, long id, Vector3 at) =>
        actors.CreateActor(id, new EntityTypeId("test"), new StatsComponent(), new ActorPose(WorldPoint.From(at), 0), "health");

    private static readonly string Content = Path.Combine(TestData.RepositoryRoot, "content");

    /// <summary>Grows one nature record's mesh from the art pack as it ships on a climate's ground, in the real Engine.</summary>
    private static DaggerfallTreeArt GrowsInTheRealEngine((int Archive, int Record) record, int climate, bool winter, Vector2 size)
    {
        DaggerfallArtReplacements art = DaggerfallArtReplacements.Read(
            File.ReadAllBytes(Path.Combine(Content, "worldrpg", "packs", "daggerfall.art-replacements", "payload.json")), "daggerfall.art-replacements");
        DaggerfallTreeArt tree = art.Trees[record];
        DaggerfallArtSprite flat = art.Scenery[record];
        DaggerfallArtFile[] files = [tree.Mesh, flat.File, .. new[] { tree.Bark, tree.Leaves, tree.Stone, art.TreeSnow }.OfType<DaggerfallArtFile>()];
        Dictionary<string, ReadOnlyMemory<byte>> served = files.ToDictionary(file => file.Path, file => (ReadOnlyMemory<byte>)File.ReadAllBytes(Path.Combine(Content, file.Path)));
        DaggerfallFloraLook look = new(flat.File, Vector2.Zero, Vector2.One, size, new Vector2(.5F, 0)) { Tree = tree, TreeSnow = art.TreeSnow };

        using EngineTestHost host = EngineTestHost.Create(new EngineTestHostOptions { Content = served });
        host.Call(engine =>
        {
            using SpatialMovementSystem spatial = new(engine.Spatial, engine.Content, null, DaggerfallTuning.Defaults.Spatial);
            using DaggerfallFloraField field = new(engine, Ground(climate), DaggerfallFloraTuning.Default, DaggerfallFlatsTuning.Default, new Dictionary<int, float>());
            field.Show(spatial.Session, Frame, new Vector3(100, 0, 100), winter, (archive, number) => (archive, number) == record ? look : null);
            Assert.True(field.GrowsPlaced(record));
        });
        return tree;
    }

    private static void Show(DaggerfallFloraField field, bool winter) =>
        field.Show(new SpatialSession(new SpatialSessionHandle(1), static () => { }), Frame, new Vector3(100, 0, 100), winter, Looks);

    /// <summary>The records one tile of the field commits once its workers have sampled it.</summary>
    private static VegetationRecord[] Sample(EngineContextFake engine, DaggerfallFloraField field, VegetationTileRequest tile, bool winter)
    {
        int committed = engine.FloraCommits.Count;
        engine.FloraRequests.Enqueue(tile);
        for (long until = Environment.TickCount64 + 10_000; engine.FloraCommits.Count == committed && Environment.TickCount64 < until;)
        {
            Show(field, winter);
            Thread.Sleep(5);
        }
        return Assert.Single(engine.FloraCommits.Skip(committed)).Records.ToArray();
    }

    private static VegetationTileRequest Tile(double x, double z)
    {
        const uint cells = 32;
        double size = cells * Spacing;
        (long column, long row) = ((long)Math.Floor(x / size), (long)Math.Floor(z / size));
        return new VegetationTileRequest(cells, column, row, 1, column * size, row * size, Spacing);
    }

    private static VegetationLayerRequest Last(EngineContextFake engine, int archive, int record) =>
        engine.FloraLayers.Last(layer => layer.Layer == DaggerfallFloraField.Layer(archive, record));

    private static DaggerfallArtFile Art(string path) => new(path, DaggerfallContentHash.Parse(HashHex, path));

    private static readonly byte[] RockMesh = Encoding.UTF8.GetBytes("""
        {"height": 1, "parts": [{"role": "stone", "positions": [0, 0, 0, 1, 0, 0, 0, 1, 0], "normals": [0, 0, 1, 0, 0, 1, 0, 0, 1],
          "uvs": [0, 0, 1, 0, 0, 1], "colors": [1, 1, 1, 0, 1, 1, 1, 0, 1, 1, 1, 0], "indices": [0, 1, 2]}]}
        """);

    /// <summary>Record 3 is a rock with a mesh in either season; record 7 a bush, a sprite alone; no other record draws.</summary>
    private static DaggerfallFloraLook? Looks(int archive, int record) => record switch
    {
        3 => new(Art("flora/rock.png"), Vector2.Zero, Vector2.One, new Vector2(1, 1), new Vector2(.5F, 0))
        {
            Tree = new(new DaggerfallArtFile("trees/rock.prop-mesh.json", DaggerfallContentHash.Parse(Convert.ToHexStringLower(SHA256.HashData(RockMesh)), "rock")),
                1, 1, 0, null, null, Stone: Art("trees/rock-stone.png")),
        },
        7 => new(Art("flora/bush.png"), Vector2.Zero, Vector2.One, new Vector2(1, 1), new Vector2(.5F, 0)),
        _ => null,
    };

    private static EngineContextFake Engine()
    {
        List<string> releases = [];
        ContentFake content = MediaContent(releases);
        content.Add("trees/rock.prop-mesh.json", RockMesh);
        content.Add("trees/rock-stone.png", Hash);
        content.Add("flora/rock.png", Hash);
        content.Add("flora/bush.png", Hash);
        return EngineContextFake.Create(content, SpatialFake.Create(Hash, releases).Service, new AppearanceFake(releases));
    }

    private static DaggerfallHorizonGround Ground(int climate = 231)
    {
        const int side = 8;
        byte[] heights = new byte[side * side], samples = new byte[side * side * 25];
        Array.Fill(heights, (byte)90);
        Array.Fill(samples, (byte)40);
        return new(new DaggerfallTerrainSet(side, side, heights, () => samples), (_, _) => climate, DaggerfallHorizonTuning.Default, DaggerfallGroundTuning.Default,
            new Dictionary<DaggerfallExteriorCellId, DaggerfallSiteExterior>())
        {
            Materials = new(0, new Dictionary<int, DaggerfallHorizonGround.ArchiveMaterials>
            {
                [2] = new(1, 1, 2, 3, false, false), [102] = new(1, 1, 2, 3, false, false), [103] = new(4, 4, 5, 6, false, false),
                [302] = new(1, 1, 2, 3, true, false), [303] = new(4, 4, 5, 6, false, false), [402] = new(7, 8, 9, 10, false, true),
            }),
        };
    }
}
