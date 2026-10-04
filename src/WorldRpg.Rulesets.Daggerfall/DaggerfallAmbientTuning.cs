using System.Text.Json;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Source fog/ambient variants and the authored Engine precipitation adaptation.</summary>
internal sealed record DaggerfallAmbientTuning(float ClearFogEnd, float RainFogDensity, float SnowFogDensity,
    float HeavyFogDensity, float InteriorFogDensity, float DungeonFogDensity, float ZoneAmbient,
    int MinimumWaitSeconds, int MaximumWaitSeconds, float FlashSeconds, float FlashSkipChance,
    float FlashIntensity, float RollingThunderDelay, float ShelterProbeHeight,
    float PrecipitationHeight, float RainRate, float SnowRate, float RainLifetime, float SnowLifetime,
    float RainSpeed, float SnowSpeed, float RainSize, float SnowSize, float RainSpread, float SnowSpread, uint ParticleBudget)
{
    // WeatherManager fog; PlayerAdvanced castle/special light; AmbientEffectsPlayer cadence and flash
    // probability/intensity. Particle sizes/speeds adapt the donor's built-in Unity weather material
    // to Engine world-size billboards; these are explicit product presentation tuning.
    internal static DaggerfallAmbientTuning Classic { get; } = new(2400, .003f, .005f, .05f, .001f,
        .005f, 165f/255f, 4, 35, 1f/30f, .6f, 2, 1.7f, 12, 6,
        800, 400, .6f, 4, 14, 1.5f, .16f, .08f, 8, 2, 2048);

    internal DaggerfallAmbientTuning Validate()
    {
        foreach (float value in new[] {ClearFogEnd, RainFogDensity, SnowFogDensity, HeavyFogDensity,
            InteriorFogDensity, DungeonFogDensity, FlashSeconds, FlashIntensity, ShelterProbeHeight,
            PrecipitationHeight, RainRate, SnowRate, RainLifetime, SnowLifetime, RainSpeed, SnowSpeed,
            RainSize, SnowSize, RainSpread, SnowSpread})
            if (!float.IsFinite(value) || value <= 0) throw new ArgumentException("Ambient presentation values must be finite and positive.");
        if (!float.IsFinite(ZoneAmbient) || ZoneAmbient is < 0 or > 1
            || !float.IsFinite(FlashSkipChance) || FlashSkipChance is < 0 or > 1
            || !float.IsFinite(RollingThunderDelay) || RollingThunderDelay < 0
            || MinimumWaitSeconds < 1 || MaximumWaitSeconds <= MinimumWaitSeconds
            || ParticleBudget is < 1 or > 4096)
            throw new ArgumentException("Ambient timing, light and particle budget are outside their admitted ranges.");
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
        e.GetProperty("precipitationHeight").GetSingle(), e.GetProperty("rainRate").GetSingle(),
        e.GetProperty("snowRate").GetSingle(), e.GetProperty("rainLifetime").GetSingle(),
        e.GetProperty("snowLifetime").GetSingle(), e.GetProperty("rainSpeed").GetSingle(),
        e.GetProperty("snowSpeed").GetSingle(), e.GetProperty("rainSize").GetSingle(),
        e.GetProperty("snowSize").GetSingle(), e.GetProperty("rainSpread").GetSingle(), e.GetProperty("snowSpread").GetSingle(), e.GetProperty("particleBudget").GetUInt32());
}
