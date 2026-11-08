using System.Text.Json;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Source fog/ambient variants, and how falling weather and what it leaves on the ground draw through the Engine.</summary>
internal sealed record DaggerfallAmbientTuning(float ClearFogEnd, float RainFogDensity, float SnowFogDensity,
    float HeavyFogDensity, float InteriorFogDensity, float DungeonFogDensity, float ZoneAmbient,
    int MinimumWaitSeconds, int MaximumWaitSeconds, float FlashSeconds, float FlashSkipChance,
    float FlashIntensity, float RollingThunderDelay, float ShelterProbeHeight,
    float PrecipitationRadius, float PrecipitationHeight, uint RainDrops, uint SnowDrops, float RainSpeed, float SnowSpeed,
    float RainSize, float SnowSize, float RainStreakSeconds, float PrecipitationPixels, float FallEaseSeconds, float NightFall, float WindDrift, float SnowTexelsPerMetre, float PuddleShare, float WetLook, float SnowLook)
{
    // WeatherManager fog; PlayerAdvanced castle/special light; AmbientEffectsPlayer cadence and flash
    // probability/intensity. How rain and snow draw is explicit product presentation tuning.
    internal static DaggerfallAmbientTuning Classic { get; } = new(2400, .003f, .005f, .05f, .001f,
        .005f, 165f/255f, 4, 35, 1f/30f, .6f, 2, 1.7f, 12, 18, 10,
        16_000, 9_000, 14, 1.6f, .025f, .06f, .05f, 3, 2, .2f, 3, 20, .6f, .6f, .65f);

    internal DaggerfallAmbientTuning Validate()
    {
        foreach (float value in new[] {ClearFogEnd, RainFogDensity, SnowFogDensity, HeavyFogDensity,
            InteriorFogDensity, DungeonFogDensity, FlashSeconds, FlashIntensity, ShelterProbeHeight,
            PrecipitationRadius, PrecipitationHeight, RainSpeed, SnowSpeed, RainSize, SnowSize, RainStreakSeconds,
            PrecipitationPixels, FallEaseSeconds, NightFall, WindDrift, SnowTexelsPerMetre, PuddleShare, WetLook, SnowLook})
            if (!float.IsFinite(value) || value <= 0) throw new ArgumentException("Ambient presentation values must be finite and positive.");
        if (!float.IsFinite(ZoneAmbient) || ZoneAmbient is < 0 or > 1
            || !float.IsFinite(FlashSkipChance) || FlashSkipChance is < 0 or > 1
            || !float.IsFinite(RollingThunderDelay) || RollingThunderDelay < 0
            || MinimumWaitSeconds < 1 || MaximumWaitSeconds <= MinimumWaitSeconds
            || RainDrops is 0 or > 200_000 || SnowDrops is 0 or > 200_000)
            throw new ArgumentException("Ambient timing, light and drop counts are outside their admitted ranges.");
        return this;
    }

    internal static DaggerfallAmbientTuning Read(JsonElement e) => new(
        e.GetProperty("clearFogEnd").GetSingle(), e.GetProperty("rainFogDensity").GetSingle(),
        e.GetProperty("snowFogDensity").GetSingle(), e.GetProperty("heavyFogDensity").GetSingle(),
        e.GetProperty("interiorFogDensity").GetSingle(), e.GetProperty("dungeonFogDensity").GetSingle(),
        e.GetProperty("zoneAmbient").GetSingle(), e.GetProperty("minimumWaitSeconds").GetInt32(),
        e.GetProperty("maximumWaitSeconds").GetInt32(), e.GetProperty("flashSeconds").GetSingle(),
        e.GetProperty("flashSkipChance").GetSingle(), e.GetProperty("flashIntensity").GetSingle(),
        e.GetProperty("rollingThunderDelay").GetSingle(), e.GetProperty("shelterProbeHeight").GetSingle(),
        e.GetProperty("precipitationRadius").GetSingle(), e.GetProperty("precipitationHeight").GetSingle(),
        e.GetProperty("rainDrops").GetUInt32(), e.GetProperty("snowDrops").GetUInt32(),
        e.GetProperty("rainSpeed").GetSingle(), e.GetProperty("snowSpeed").GetSingle(),
        e.GetProperty("rainSize").GetSingle(), e.GetProperty("snowSize").GetSingle(),
        e.GetProperty("rainStreakSeconds").GetSingle(), e.GetProperty("precipitationPixels").GetSingle(),
        e.GetProperty("fallEaseSeconds").GetSingle(),
        e.GetProperty("nightFall").GetSingle(), e.GetProperty("windDrift").GetSingle(),
        e.GetProperty("snowTexelsPerMetre").GetSingle(), e.GetProperty("puddleShare").GetSingle(),
        e.GetProperty("wetLook").GetSingle(), e.GetProperty("snowLook").GetSingle());
}
