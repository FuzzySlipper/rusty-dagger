using System.Numerics;
using System.Text.Json;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The sea's waves (#10910): the wind they stand to builds and falls with the weather and holds its bearing while the sea
/// is in view; the sea's sheets are cut into tiles near the player, fine where the waves move them; and the water's
/// material hands the sea's shader the wind, the fetch and the reach its mesh moves to.
/// </summary>
public sealed class DaggerfallSeaTests
{
    private static readonly DaggerfallWaterTuning Water = DaggerfallWaterTuning.Default;
    private const double Pixel = DaggerfallExteriorCellResidency.CellSize;

    [Fact]
    public void Waves_stand_to_the_weathers_wind_at_once_then_build_and_fall_over_game_hours()
    {
        DaggerfallSeaWind wind = new(Water);
        wind.Follow(100, .1f, 90);
        Assert.Equal(Water.WindFor(.1f), wind.Now.Speed, 4);

        // A storm comes: after its build time the waves have gone most of the way, not all of it.
        wind.Follow(100 + Water.BuildHours, .9f, 90);
        float gale = Water.WindFor(.9f), calm = Water.WindFor(.1f);
        Assert.Equal(calm + ((gale - calm) * (1 - MathF.Exp(-1))), wind.Now.Speed, 3);

        // A night's rest passes the hours at once; the clock running back passes none.
        wind.Follow(120, .9f, 90);
        Assert.Equal(gale, wind.Now.Speed, 3);
        wind.Follow(110, .1f, 90);
        Assert.Equal(gale, wind.Now.Speed, 3);
    }

    [Fact]
    public void The_waves_bearing_holds_while_the_sea_is_in_view_and_takes_the_wind_up_when_it_is_next_shown()
    {
        DaggerfallSeaWind wind = new(Water);
        // Toward the compass's east is +x; toward its south, +z.
        wind.Follow(0, .5f, 90);
        Assert.Equal(0, wind.Now.Bearing, 4);

        wind.Follow(1, .5f, 180);
        Assert.Equal(0, wind.Now.Bearing, 4);

        wind.Release();
        wind.Follow(2, .5f, 180);
        Assert.Equal(MathF.PI / 2, wind.Now.Bearing, 4);
    }

    [Fact]
    public void A_far_sea_pixel_draws_one_sheet_and_a_near_one_its_tiles_fine_only_within_the_waves_reach()
    {
        DaggerfallHorizon.SeaPiece whole = Assert.Single(DaggerfallHorizon.SeaPieces(-Pixel, Pixel / 2));
        Assert.Null(whole.Tile);

        // The player stands in the pixel's middle: every tile draws, side by side, the ones about the player fine.
        DaggerfallHorizon.SeaPiece[] tiles = [.. DaggerfallHorizon.SeaPieces(Pixel / 2, Pixel / 2)];
        Assert.Equal(64, tiles.Length);
        Assert.Equal(64, tiles.Select(tile => tile.Tile).Distinct().Count());
        Assert.Equal(64, tiles.Select(tile => (tile.Offset.X, tile.Offset.Z)).Distinct().Count());
        Assert.All(tiles, tile => Assert.InRange(tile.Offset.X, 0, (float)(Pixel * 7 / 8) + .01f));
        DaggerfallHorizon.SeaPiece[] fine = [.. tiles.Where(tile => tile.Fine)];
        Assert.InRange(fine.Length, 4, 36);
        Assert.Contains(fine, tile => tile.Offset.X < Pixel / 2 && tile.Offset.X + (Pixel / 8) > Pixel / 2);
        Assert.DoesNotContain(fine, tile => tile.Offset == Vector3.Zero);

        // Just outside the pixel, the tiles toward the player are fine and the rest coarse.
        DaggerfallHorizon.SeaPiece[] beside = [.. DaggerfallHorizon.SeaPieces(-10, Pixel / 2)];
        Assert.Equal(64, beside.Length);
        Assert.Contains(beside, tile => tile.Fine);
        Assert.All(beside.Where(tile => tile.Fine), tile => Assert.True(tile.Offset.X < 225 - 10));
    }

    [Fact]
    public void Every_sea_tile_has_its_own_identity_apart_from_the_sheets_rivers_and_ground_cover()
    {
        HashSet<ulong> ids = [DaggerfallPresentationObjectIds.Sea(3, 4), DaggerfallPresentationObjectIds.River(3, 4), DaggerfallPresentationObjectIds.GroundCover];
        for (int tile = 0; tile < 64; tile++)
        {
            Assert.True(ids.Add(DaggerfallPresentationObjectIds.SeaTile(3, 4, tile, fine: false)));
            Assert.True(ids.Add(DaggerfallPresentationObjectIds.SeaTile(3, 4, tile, fine: true)));
            Assert.True(ids.Add(DaggerfallPresentationObjectIds.SeaTile(-1, 4, tile, fine: true)));
        }
        Assert.All(ids, id => Assert.InRange(id, 1UL, DaggerfallPresentationObjectIds.Maximum));
        Assert.Throws<ArgumentOutOfRangeException>(() => DaggerfallPresentationObjectIds.SeaTile(3, 4, 64, fine: false));
    }

    [Fact]
    public void The_seas_shader_takes_the_wind_and_fetch_and_lets_the_mesh_move_only_where_its_reach_is_given()
    {
        RenderResource shader = Resource(1);
        DaggerfallSeaMaps maps = new(Resource(2), Resource(3));
        DaggerfallSeaWind.Blowing gale = new(MathF.PI / 2, Water.WindFor(.9f));

        MaterialShader near = Water.SeaShader(shader, maps, gale, Water.Fetch.X, 200, new(1, .5f, 0, 0), Vector3.One);
        Assert.Equal(new Vector4(MathF.PI / 2, Water.HighestWaves, gale.Speed, Water.Fetch.X), near.Parameter0);
        Assert.Equal(200, near.Parameter1.W);
        Assert.Equal(new Vector2(1, .5f), new Vector2(near.Parameter2.X, near.Parameter2.Y));
        Assert.True(near.DisplacementBound >= Water.HighestWaves);

        Assert.Equal(new RenderResourceReference(maps.Ripples), near.TextureA);
        Assert.Equal(new RenderResourceReference(maps.Foam), near.TextureB);

        MaterialShader river = Water.SeaShader(shader, maps, gale, Water.Fetch.Y, 0, default, Vector3.One);
        Assert.Equal(0, river.DisplacementBound);
        Assert.Equal(Water.Fetch.Y, river.Parameter0.W);

        // The shader ripples the water in place of the Engine; without it the Engine's own ripples stay.
        Assert.Equal(0, Water.Material(default, Vector3.One, near).NormalScale);
        Assert.Equal(Water.Ripple, Water.Material(default, Vector3.One, null).NormalScale);
    }

    private static RenderResource Resource(ulong handle) => new(new RenderResourceHandle(handle), () => { });

    [Fact]
    public void The_shipped_tuning_reads_as_the_water_defaults_and_refuses_waves_that_never_build()
    {
        using JsonDocument payload = JsonDocument.Parse(TestContentFiles.ReadText(Path.Combine(TestData.RepositoryRoot, "content/worldrpg/tuning/daggerfall.defaults/payload.json")));
        Assert.Equal(Water, DaggerfallWaterTuning.Read(payload.RootElement.GetProperty("water")));
        Assert.Throws<ArgumentException>(() => (Water with { BuildHours = 0 }).Validate());
        Assert.Throws<ArgumentException>(() => (Water with { Fetch = new(-1, 300) }).Validate());
    }
}
