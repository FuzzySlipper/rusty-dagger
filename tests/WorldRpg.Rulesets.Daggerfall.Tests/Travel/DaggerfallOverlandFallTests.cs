using System.Numerics;
using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.Travel;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallOverlandFallTests
{
    private static readonly DaggerfallOverlandFallTuning Tuning = DaggerfallTuning.Defaults.Expedition.Map.Fall;
    private const double FieldOfView = 40;

    [Fact]
    public void Rain_falls_over_what_the_view_shows_at_a_share_of_its_distance_a_second_on_the_maps_own_seconds_and_dies_away_when_it_stops()
    {
        List<string> releases = [];
        AppearanceFake graphics = new(releases);
        using DaggerfallOverlandFall fall = Fall(graphics);
        (CameraDescriptor view, Vector3 eye) = View(4_000);
        fall.Update(view, 16d / 9, eye, FieldOfView, 4_000, 0, (_, _) => Weather(DaggerfallWeatherKind.Rain, 1), 1, 1d / 60);

        // The whole pool falls at once where it rains hardest, part-way through each drop's fall, in one batch.
        Assert.Equal((int)Tuning.Rain.Drops, fall.Falling);
        Assert.NotNull(fall.Rain);
        Assert.Null(fall.Snow);
        SpriteBatchRequest first = Assert.Single(graphics.BatchRequests);
        SpriteBatchInstance[] drops = first.Instances.ToArray();
        Assert.Equal(fall.Falling, drops.Length);
        Assert.All(drops, drop =>
        {
            // Between the eye and the land, sized to the tuned share of the view's height at its distance, in a fade frame.
            Assert.True(drop.Position.Y >= -1e-4 && drop.Position.Y <= eye.Y, $"A drop stands at {drop.Position.Y}.");
            float expected = Vector3.Distance(drop.Position, eye) * Tuning.Rain.Size * 2 * MathF.Tan(20 * MathF.PI / 180);
            Assert.Equal(expected, drop.Scale, expected * 1e-3f);
            Assert.InRange(drop.FrameId, 0u, (uint)DaggerfallOverlandFall.FadeFrames - 1);
        });
        Assert.Contains(drops, drop => drop.FrameId == 0);
        Assert.Equal(Tuning.Rain.Opacity, first.Tint.A);

        // A tenth of a second on, each drop has fallen about the tuned share of the view's distance. The batch drawn over is
        // still in the snapshot published last, so it is let go only an update later.
        fall.Update(view, 16d / 9, eye, FieldOfView, 4_000, 0, (_, _) => Weather(DaggerfallWeatherKind.Rain, 1), 1, .1);
        SpriteBatchInstance[] later = graphics.BatchRequests[^1].Instances.ToArray();
        float down = (float)(Tuning.Rain.Speed * 4_000 * DaggerfallOverlandTerrain.Scale * .1);
        int fell = drops.Zip(later).Count(pair => pair.First.Position.Y - pair.Second.Position.Y is var drop && drop >= down * .87f && drop <= down * 1.13f);
        Assert.True(fell > drops.Length * .8, $"{fell} of {drops.Length} drops fell their share.");
        Assert.DoesNotContain("appearance", releases);

        // When it stops, no new drop starts and the last ones fall out; then none is drawn.
        fall.Update(view, 16d / 9, eye, FieldOfView, 4_000, 0, (_, _) => Weather(DaggerfallWeatherKind.Sunny, 0), 1, Tuning.Rain.Seconds * 1.3);
        Assert.Equal(0, fall.Falling);
        Assert.Null(fall.Rain);
        Assert.Single(releases, release => release == "appearance");
        // Taken away as the map closes, the last batch goes once the map's next snapshot is out.
        fall.Clear();
        fall.Release();
        Assert.Equal(2, releases.Count(release => release == "appearance"));
    }

    [Fact]
    public void Snow_falls_only_where_the_weather_snows_slowly_and_drifting_downwind_and_dimmer_at_night_and_closing_takes_it_away()
    {
        AppearanceFake graphics = new([]);
        using DaggerfallOverlandFall fall = Fall(graphics);
        (CameraDescriptor view, Vector3 eye) = View(4_000);
        // Snow east of the map's middle and dry to the west, under a wind at 0 degrees, blowing north (-z).
        DaggerfallWeatherConditions WeatherAt(double x, double z) => x > 0 ? Weather(DaggerfallWeatherKind.Snow, 1) : Weather(DaggerfallWeatherKind.Sunny, 0);
        fall.Update(view, 16d / 9, eye, FieldOfView, 4_000, 0, WeatherAt, 0, 1d / 60);
        SpriteBatchInstance[] flakes = Assert.Single(graphics.BatchRequests).Instances.ToArray();
        Assert.Null(fall.Rain);
        Assert.NotEmpty(flakes);
        Assert.All(flakes, flake => Assert.True(flake.Position.X > 0, "Snow falls only where it snows."));
        // Dimmed to the tuned night share.
        Assert.Equal(Tuning.Night * Tuning.Snow.Color.X, graphics.BatchRequests[0].Tint.R, 4);

        // Over snow everywhere, so every flake that ends starts again in its place.
        fall.Clear();
        fall.Update(view, 16d / 9, eye, FieldOfView, 4_000, 0, (_, _) => Weather(DaggerfallWeatherKind.Snow, 1), 0, 1d / 60);
        flakes = graphics.BatchRequests[^1].Instances.ToArray();
        fall.Update(view, 16d / 9, eye, FieldOfView, 4_000, 0, (_, _) => Weather(DaggerfallWeatherKind.Snow, 1), 0, .5);
        SpriteBatchInstance[] later = graphics.BatchRequests[^1].Instances.ToArray();
        float drift = flakes.Zip(later).Select(pair => pair.Second.Position.Z - pair.First.Position.Z).Order().ElementAt(flakes.Length / 2);
        float sink = flakes.Zip(later).Select(pair => pair.First.Position.Y - pair.Second.Position.Y).Order().ElementAt(flakes.Length / 2);
        Assert.True(drift < 0, "Snow drifts downwind.");
        Assert.InRange(sink, 0, Tuning.Snow.Speed * 4 * .5 * 1.13);

        fall.Clear();
        Assert.Equal(0, fall.Falling);
        Assert.Null(fall.Snow);
    }

    [Fact]
    public void Its_tuning_refuses_drops_that_cannot_fall()
    {
        Assert.Throws<InvalidOperationException>(() => (Tuning with { Rain = Tuning.Rain with { Seconds = 0 } }).Validate());
        Assert.Throws<InvalidOperationException>(() => (Tuning with { Snow = Tuning.Snow with { Size = 2 } }).Validate());
        Assert.Throws<InvalidOperationException>(() => (Tuning with { Night = 2 }).Validate());
    }

    private static DaggerfallOverlandFall Fall(AppearanceFake graphics) =>
        new(graphics, Tuning, graphics.CreateSpriteAtlas(default), graphics.CreateSpriteAtlas(default));

    private static DaggerfallWeatherConditions Weather(DaggerfallWeatherKind kind, float falling) => new(kind, falling, 1, 0, 0, 0);

    /// <summary>The map's view of its middle from <paramref name="distance"/> metres at its opening tilt, in backdrop units.</summary>
    private static (CameraDescriptor View, Vector3 Eye) View(double distance)
    {
        float scale = DaggerfallOverlandTerrain.Scale, tilt = 55 * MathF.PI / 180;
        Vector3 eye = new Vector3(0, MathF.Sin(tilt), MathF.Cos(tilt)) * (float)distance * scale;
        Assert.True(CameraQueries.TryLookAtPose(eye, Vector3.Zero, 0, out CameraPose pose));
        return (new CameraDescriptor(pose, CameraBasisMode.Derived, default, new CameraProjection(CameraProjectionKind.Perspective, FieldOfView, 0, 1e-3, 1e4),
            new CameraViewport(0, 0, 1, 1)), eye);
    }
}
