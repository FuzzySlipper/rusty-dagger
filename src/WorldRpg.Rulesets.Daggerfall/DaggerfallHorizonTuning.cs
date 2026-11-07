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
/// <param name="FollowMetres">The player moves this far before the lift's centre follows.</param>
/// <param name="RockFromSlope">Far ground turns to rock from this slope (rise over run); its coarse cells smooth a slope below the walked ground's.</param>
/// <param name="RockFullSlope">Far ground is bare rock from this slope.</param>
/// <param name="CloudCoverage">How much of a fair sky is cloud (0 to 1), now the painted low cloud is cleared with the horizon.</param>
/// <param name="StormCloudCoverage">How much of a foul sky is cloud.</param>
/// <param name="CloudAltitudeMetres">The cloud layer's height, above the map's highest ground.</param>
/// <param name="CloudScaleMetres">The size of one cloud.</param>
/// <param name="CloudTwilightDegrees">How far below the horizon the sun still lights the clouds.</param>
/// <param name="CloudDrift">The clouds' drift east and south, in metres a second.</param>
internal sealed record DaggerfallHorizonTuning(float Lift, float LiftFromMetres, float LiftFullMetres, float FogDensity, float SinkMetres, float SinkFromMetres, float SinkGoneMetres,
    float FollowMetres, float RockFromSlope, float RockFullSlope,
    float CloudCoverage, float StormCloudCoverage, float CloudAltitudeMetres, float CloudScaleMetres, float CloudTwilightDegrees, Vector2 CloudDrift,
    Color Sea)
{
    /// <summary>The share of a moonlit night's ambient left on a night with no moon.</summary>
    internal float MoonlessNight { get; init; } = .45f;
    /// <summary>Masser's share of the moonlight; Secunda gives the rest.</summary>
    internal float MasserLight { get; init; } = .7f;
    /// <summary>How brightly a full moon lights the clouds.</summary>
    internal float MoonCloudLight { get; init; } = .1f;
    /// <summary>How high the star field's pole stands over the north, in degrees.</summary>
    internal float StarPoleDegrees { get; init; } = 50;
    /// <summary>The faintest magnitude that shows on a moonless night.</summary>
    internal float StarLimit { get; init; } = 6;
    /// <summary>How many magnitudes a full moon washes out.</summary>
    internal float StarMoonWash { get; init; } = 1.5f;
    /// <summary>How bright the stars stand, by the Engine's scale.</summary>
    internal float StarBrightness { get; init; } = 3;
    /// <summary>How much each star flickers, from 0 to 1.</summary>
    internal float StarTwinkle { get; init; } = .2f;
    /// <summary>The stars show once the daylight falls below this.</summary>
    internal float StarsFromDaylight { get; init; } = .35f;
    /// <summary>How far the world draws outdoors: past near ground's corners, for the far plane clips everything.</summary>
    internal double DrawnMetres { get; init; } = 6_000;
    /// <summary>The side of a cloud's cells in metres, so the sky sits with the pixel art under it.</summary>
    internal float CloudPixelMetres { get; init; } = 180;
    /// <summary>How many shades a cloud is drawn in.</summary>
    internal uint CloudShadeSteps { get; init; } = 5;
    /// <summary>The colour the clouds take the light in, by <see cref="World.DaggerfallWeatherKind"/>, so foul skies close over dark.</summary>
    internal Vector3[] CloudColors { get; init; } = [Vector3.One, Vector3.One, new(.6f, .6f, .63f), Vector3.One, new(.28f, .31f, .37f), new(.17f, .19f, .24f), new(.52f, .54f, .58f)];

    /// <summary>
    /// The colour thick weather closes the sky in by full day, by <see cref="World.DaggerfallWeatherKind"/>: the air the clouds
    /// hang in and the world fades into, so no panorama shows under them. Black keeps the panorama.
    /// </summary>
    internal Vector3[] AirColors { get; init; } = [Vector3.Zero, Vector3.Zero, Vector3.Zero, new(.6f, .6f, .62f), new(.19f, .23f, .31f), new(.13f, .15f, .22f), new(.66f, .69f, .78f)];
    /// <summary>The least of the day's light the air keeps by night.</summary>
    internal float NightAir { get; init; } = .06f;
    /// <summary>How much a lightning flash lights the air, for each of its intensity.</summary>
    internal float FlashAir { get; init; } = .12f;

    /// <summary>How large each moon stands, in degrees of sky from its centre to its edge.</summary>
    internal float MasserRadiusDegrees { get; init; } = 4;
    internal float SecundaRadiusDegrees { get; init; } = 2.4f;
    /// <summary>How far each moon's path leans south of the sun's, so the two don't run along one line.</summary>
    internal float MasserTiltDegrees { get; init; } = 20;
    internal float SecundaTiltDegrees { get; init; } = 32;
    /// <summary>How bright a moon stands by full day, as a share of its brightness at night.</summary>
    internal float MoonDayBrightness { get; init; } = .45f;

    internal static DaggerfallHorizonTuning Default { get; } = new(4.5f, 3_000, 24_000, .00003f, 150, 2_900, 3_700, 204.8f, .25f, .7f,
        .45f, .9f, 3_500, 1_800, 8, new(14, 5),
        Color.FromSrgb(.31f, .35f, .44f));

    internal DaggerfallHorizonTuning Validate()
    {
        if (!double.IsFinite(DrawnMetres) || DrawnMetres <= 0) throw new ArgumentException("The world draws out to a finite distance.");
        foreach (float value in new[] {Lift, LiftFullMetres, FogDensity, FollowMetres, RockFullSlope})
            if (!float.IsFinite(value) || value <= 0) throw new ArgumentException("Horizon values must be finite and positive.");
        if (SinkMetres < 0 || SinkFromMetres < 0 || SinkFromMetres >= SinkGoneMetres)
            throw new ArgumentException("A horizon's sink is at least nothing, and eases out from a start to a finish.");
        if (Lift < 1 || LiftFromMetres < 0 || LiftFromMetres >= LiftFullMetres
            || RockFromSlope < 0 || RockFromSlope >= RockFullSlope)
            throw new ArgumentException("A horizon's lift cannot fall, and its fades must rise from a start to a finish.");
        foreach (float value in new[] {CloudAltitudeMetres, CloudScaleMetres, CloudPixelMetres})
            if (!float.IsFinite(value) || value <= 0) throw new ArgumentException("A horizon's clouds stand above the ground and have a size.");
        if (CloudCoverage is < 0 or > 1 || StormCloudCoverage is < 0 or > 1 || CloudTwilightDegrees < 0
            || !float.IsFinite(CloudDrift.X) || !float.IsFinite(CloudDrift.Y))
            throw new ArgumentException("A horizon's cloud cover is from 0 to 1, with a finite drift and twilight.");
        if (CloudColors.Length != Enum.GetValues<World.DaggerfallWeatherKind>().Length
            || CloudColors.Any(color => !float.IsFinite(color.X + color.Y + color.Z) || color.X < 0 || color.Y < 0 || color.Z < 0))
            throw new ArgumentException("Every weather colours its clouds.");
        if (AirColors.Length != CloudColors.Length || AirColors.Any(color => !float.IsFinite(color.X + color.Y + color.Z) || color.X < 0 || color.Y < 0 || color.Z < 0)
            || NightAir is < 0 or > 1 || !float.IsFinite(FlashAir) || FlashAir < 0)
            throw new ArgumentException("Every weather states its air, with a night floor and a flash.");
        if (MoonlessNight is < 0 or > 1 || MasserLight is < 0 or > 1 || MoonCloudLight < 0 || StarMoonWash < 0 || StarBrightness < 0
            || StarTwinkle is < 0 or > 1 || StarsFromDaylight is <= 0 or > 1 || StarPoleDegrees is < 0 or > 90 || !float.IsFinite(StarLimit))
            throw new ArgumentException("Moonlight's shares are from 0 to 1, and the stars have a limit, a pole above the horizon and a daylight to come out by.");
        if (MasserRadiusDegrees <= 0 || SecundaRadiusDegrees <= 0 || MoonDayBrightness is < 0 or > 1)
            throw new ArgumentException("A moon has a size, and a day brightness from 0 to 1.");
        return this;
    }

    internal static DaggerfallHorizonTuning Read(JsonElement e) => new(
        e.GetProperty("lift").GetSingle(), e.GetProperty("liftFromMetres").GetSingle(), e.GetProperty("liftFullMetres").GetSingle(),
        e.GetProperty("fogDensity").GetSingle(), e.GetProperty("sinkMetres").GetSingle(), e.GetProperty("sinkFromMetres").GetSingle(),
        e.GetProperty("sinkGoneMetres").GetSingle(), e.GetProperty("followMetres").GetSingle(),
        e.GetProperty("rockFromSlope").GetSingle(), e.GetProperty("rockFullSlope").GetSingle(),
        e.GetProperty("cloudCoverage").GetSingle(), e.GetProperty("stormCloudCoverage").GetSingle(),
        e.GetProperty("cloudAltitudeMetres").GetSingle(), e.GetProperty("cloudScaleMetres").GetSingle(),
        e.GetProperty("cloudTwilightDegrees").GetSingle(),
        new(e.GetProperty("cloudDrift")[0].GetSingle(), e.GetProperty("cloudDrift")[1].GetSingle()),
        ReadColor(e.GetProperty("sea")))
    {
        DrawnMetres = e.GetProperty("drawnMetres").GetDouble(),
        MoonlessNight = e.GetProperty("moonlessNight").GetSingle(),
        MasserLight = e.GetProperty("masserLight").GetSingle(),
        MoonCloudLight = e.GetProperty("moonCloudLight").GetSingle(),
        StarPoleDegrees = e.GetProperty("starPoleDegrees").GetSingle(),
        StarLimit = e.GetProperty("starLimit").GetSingle(),
        StarMoonWash = e.GetProperty("starMoonWash").GetSingle(),
        StarBrightness = e.GetProperty("starBrightness").GetSingle(),
        StarTwinkle = e.GetProperty("starTwinkle").GetSingle(),
        StarsFromDaylight = e.GetProperty("starsFromDaylight").GetSingle(),
        CloudPixelMetres = e.GetProperty("cloudPixelMetres").GetSingle(),
        CloudShadeSteps = e.GetProperty("cloudShadeSteps").GetUInt32(),
        CloudColors = [.. e.GetProperty("cloudColors").EnumerateArray().Select(color => new Vector3(color[0].GetSingle(), color[1].GetSingle(), color[2].GetSingle()))],
        AirColors = [.. e.GetProperty("airColors").EnumerateArray().Select(color => new Vector3(color[0].GetSingle(), color[1].GetSingle(), color[2].GetSingle()))],
        NightAir = e.GetProperty("nightAir").GetSingle(),
        FlashAir = e.GetProperty("flashAir").GetSingle(),
        MasserRadiusDegrees = e.GetProperty("masserRadiusDegrees").GetSingle(),
        SecundaRadiusDegrees = e.GetProperty("secundaRadiusDegrees").GetSingle(),
        MasserTiltDegrees = e.GetProperty("masserTiltDegrees").GetSingle(),
        SecundaTiltDegrees = e.GetProperty("secundaTiltDegrees").GetSingle(),
        MoonDayBrightness = e.GetProperty("moonDayBrightness").GetSingle(),
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
