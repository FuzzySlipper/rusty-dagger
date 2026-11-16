using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Rusty.Engine;
using Rusty.Engine.Testing;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Nature a location places grows in the flora field where its record grows as a mesh (#10728): a copy of its record's own
/// layer, standing where the location placed it, in its season's record, counted in the layer's budget, while a record the
/// field draws as a sprite is left to the location.
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
    public void The_desert_s_smallest_mesh_grows_its_card_in_the_real_Engine()
    {
        string content = Path.Combine(TestData.RepositoryRoot, "content");
        DaggerfallArtReplacements art = DaggerfallArtReplacements.Read(
            File.ReadAllBytes(Path.Combine(content, "worldrpg", "packs", "daggerfall.art-replacements", "payload.json")), "daggerfall.art-replacements");
        DaggerfallTreeArt plant = art.Trees[(503, 22)];
        DaggerfallArtFlat flat = art.Flats[(503, 22)];
        DaggerfallArtFile[] files = [plant.Mesh, flat.File, .. new[] { plant.Bark, plant.Leaves, plant.Stone }.OfType<DaggerfallArtFile>()];
        Dictionary<string, ReadOnlyMemory<byte>> served = files.ToDictionary(file => file.Path, file => (ReadOnlyMemory<byte>)File.ReadAllBytes(Path.Combine(content, file.Path)));
        DaggerfallFloraLook look = new(flat.File, Vector2.Zero, Vector2.One, new Vector2(.15F, .148F), new Vector2(.5F, 0)) { Tree = plant };

        using EngineTestHost host = EngineTestHost.Create(new EngineTestHostOptions { Content = served });
        host.Call(engine =>
        {
            using SpatialMovementSystem spatial = new(engine.Spatial, engine.Content, null, DaggerfallTuning.Defaults.Spatial);
            using DaggerfallFloraField field = new(engine, Ground(climate: 224), DaggerfallFloraTuning.Default, DaggerfallFlatsTuning.Default, new Dictionary<int, float>());
            field.Show(spatial.Session, Frame, new Vector3(100, 0, 100), false, (archive, record) => (archive, record) == (503, 22) ? look : null);
            Assert.True(field.GrowsPlaced((503, 22)));
        });
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
                [2] = new(1, 1, 2, 3, false, false), [302] = new(1, 1, 2, 3, true, false), [303] = new(4, 4, 5, 6, false, false), [402] = new(7, 8, 9, 10, false, true),
            }),
        };
    }
}
