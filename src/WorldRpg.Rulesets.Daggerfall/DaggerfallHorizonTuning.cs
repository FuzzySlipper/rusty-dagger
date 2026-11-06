using System.Numerics;
using System.Text.Json;
using Rusty.Engine;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// How the world past the walked ground is drawn: how far ground above the player is lifted toward the skyline,
/// the haze over it, and the colours the far ground wears, which are the walked ground's own as noon lights it.
/// </summary>
/// <param name="Lift">How many times ground above its valley floor stands taller once fully lifted; 1 draws true height.</param>
/// <param name="LiftFromMetres">Ground this far off is true height, so it meets the walked ground's edge.</param>
/// <param name="LiftFullMetres">Ground this far off is fully lifted.</param>
/// <param name="FogDensity">The clear-weather haze per metre, in place of the classic fog that ends with the walked ground.</param>
/// <param name="SinkMetres">How far far ground is sunk under the walked window, where it isn't wanted.</param>
/// <param name="SinkFromMetres">The sink starts to ease out here, past where the window always reaches.</param>
/// <param name="SinkGoneMetres">Far ground stands at its height from here, past the window however far the player has moved from the centre.</param>
/// <param name="NightTint">How bright far ground stands in the dark, as a share of its daylight colour.</param>
/// <param name="FollowMetres">The player moves this far before the lift's centre follows.</param>
/// <param name="RockFromSlope">Far ground turns to rock from this slope (rise over run); its coarse cells smooth a slope below the walked ground's.</param>
/// <param name="RockFullSlope">Far ground is bare rock from this slope.</param>
/// <param name="CloudCoverage">How much of a fair sky is cloud (0 to 1), now the painted low cloud is cleared with the horizon.</param>
/// <param name="StormCloudCoverage">How much of a foul sky is cloud.</param>
/// <param name="CloudAltitudeMetres">The cloud layer's height, above the map's highest ground.</param>
/// <param name="CloudScaleMetres">The size of one cloud.</param>
/// <param name="CloudTwilightDegrees">How far below the horizon the sun still lights the clouds.</param>
/// <param name="CloudDrift">The clouds' drift east and south, in metres a second.</param>
internal sealed record DaggerfallHorizonTuning(float Lift, float LiftFromMetres, float LiftFullMetres, float FogDensity, float SinkMetres, float SinkFromMetres, float SinkGoneMetres, float NightTint,
    float FollowMetres, float RockFromSlope, float RockFullSlope,
    float CloudCoverage, float StormCloudCoverage, float CloudAltitudeMetres, float CloudScaleMetres, float CloudTwilightDegrees, Vector2 CloudDrift,
    Color Sea, Color Rock, Color Snow, IReadOnlyDictionary<int, Color> Climates)
{
    /// <summary>The side of a cloud's cells in metres, so the sky sits with the pixel art under it.</summary>
    internal float CloudPixelMetres { get; init; } = 180;
    /// <summary>How many shades a cloud is drawn in.</summary>
    internal uint CloudShadeSteps { get; init; } = 5;

    internal static DaggerfallHorizonTuning Default { get; } = new(4.5f, 3_000, 24_000, .00003f, 150, 2_900, 3_700, .1f, 204.8f, .25f, .7f,
        .45f, .9f, 3_500, 1_800, 8, new(14, 5),
        Color.FromSrgb(.16f, .24f, .32f), Color.FromSrgb(.28f, .28f, .29f), Color.FromSrgb(.78f, .78f, .81f),
        new Dictionary<int, Color>
        {
            [223] = Color.FromSrgb(.27f, .24f, .18f), [224] = Color.FromSrgb(.54f, .41f, .29f), [225] = Color.FromSrgb(.54f, .41f, .29f),
            [226] = Color.FromSrgb(.25f, .26f, .2f), [227] = Color.FromSrgb(.27f, .24f, .18f), [228] = Color.FromSrgb(.27f, .24f, .18f),
            [229] = Color.FromSrgb(.54f, .41f, .29f), [230] = Color.FromSrgb(.25f, .26f, .2f), [231] = Color.FromSrgb(.24f, .3f, .2f),
            [232] = Color.FromSrgb(.24f, .3f, .2f),
        });

    internal DaggerfallHorizonTuning Validate()
    {
        foreach (float value in new[] {Lift, LiftFullMetres, FogDensity, FollowMetres, RockFullSlope})
            if (!float.IsFinite(value) || value <= 0) throw new ArgumentException("Horizon values must be finite and positive.");
        if (SinkMetres < 0 || SinkFromMetres < 0 || SinkFromMetres >= SinkGoneMetres)
            throw new ArgumentException("A horizon's sink is at least nothing, and eases out from a start to a finish.");
        if (NightTint is < 0 or > 1) throw new ArgumentException("A horizon's night tint is from 0 to 1.");
        if (Lift < 1 || LiftFromMetres < 0 || LiftFromMetres >= LiftFullMetres
            || RockFromSlope < 0 || RockFromSlope >= RockFullSlope)
            throw new ArgumentException("A horizon's lift cannot fall, and its fades must rise from a start to a finish.");
        foreach (float value in new[] {CloudAltitudeMetres, CloudScaleMetres, CloudPixelMetres})
            if (!float.IsFinite(value) || value <= 0) throw new ArgumentException("A horizon's clouds stand above the ground and have a size.");
        if (CloudCoverage is < 0 or > 1 || StormCloudCoverage is < 0 or > 1 || CloudTwilightDegrees < 0
            || !float.IsFinite(CloudDrift.X) || !float.IsFinite(CloudDrift.Y))
            throw new ArgumentException("A horizon's cloud cover is from 0 to 1, with a finite drift and twilight.");
        if (Climates.Count == 0) throw new ArgumentException("A horizon needs a colour for its climates.");
        return this;
    }

    internal static DaggerfallHorizonTuning Read(JsonElement e) => new(
        e.GetProperty("lift").GetSingle(), e.GetProperty("liftFromMetres").GetSingle(), e.GetProperty("liftFullMetres").GetSingle(),
        e.GetProperty("fogDensity").GetSingle(), e.GetProperty("sinkMetres").GetSingle(), e.GetProperty("sinkFromMetres").GetSingle(),
        e.GetProperty("sinkGoneMetres").GetSingle(), e.GetProperty("nightTint").GetSingle(), e.GetProperty("followMetres").GetSingle(),
        e.GetProperty("rockFromSlope").GetSingle(), e.GetProperty("rockFullSlope").GetSingle(),
        e.GetProperty("cloudCoverage").GetSingle(), e.GetProperty("stormCloudCoverage").GetSingle(),
        e.GetProperty("cloudAltitudeMetres").GetSingle(), e.GetProperty("cloudScaleMetres").GetSingle(),
        e.GetProperty("cloudTwilightDegrees").GetSingle(),
        new(e.GetProperty("cloudDrift")[0].GetSingle(), e.GetProperty("cloudDrift")[1].GetSingle()),
        ReadColor(e.GetProperty("sea")), ReadColor(e.GetProperty("rock")), ReadColor(e.GetProperty("snow")),
        e.GetProperty("climates").EnumerateObject().ToDictionary(climate => int.Parse(climate.Name), climate => ReadColor(climate.Value)))
    {
        CloudPixelMetres = e.GetProperty("cloudPixelMetres").GetSingle(),
        CloudShadeSteps = e.GetProperty("cloudShadeSteps").GetUInt32(),
    };

    /// <summary>A colour as content writes it, sRGB from 0 to 1; a heightfield takes linear.</summary>
    private static Color ReadColor(JsonElement e)
    {
        float[] rgb = [.. e.EnumerateArray().Select(channel => channel.GetSingle())];
        if (rgb.Length != 3 || rgb.Any(channel => !float.IsFinite(channel) || channel is < 0 or > 1))
            throw new ArgumentException("A horizon colour is three sRGB channels from 0 to 1.");
        return Color.FromSrgb(rgb[0], rgb[1], rgb[2]);
    }
}
