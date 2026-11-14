using System.Numerics;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Ruleset-authored descriptor values for one-shot classic presentation audio.</summary>
internal sealed record DaggerfallPresentationAudioTuning(float Volume, float Pitch, float SpatialBlend, float MaxDistance)
{
    internal float AttractRadius { get; init; } = 16F;
    internal int AttractMinimumDelaySeconds { get; init; } = 3;
    internal int AttractMaximumDelaySeconds { get; init; } = 9;
    internal int AttractMoveChancePercent { get; init; } = 20;
    internal int AttackCueChancePercent { get; init; } = 50;
    internal float OccludedVolumeScale { get; init; } = .25F;
    internal bool MuteHumanSounds { get; init; } = true;
    internal int BeastMinimumDelaySeconds { get; init; } = 4;
    internal int BeastMaximumDelaySeconds { get; init; } = 20;
    internal int BeastAttackChancePercent { get; init; } = 10;
    internal int VampireAttackChancePercent { get; init; } = 20;
    internal int VampireBarkChancePercent { get; init; } = 20;
    internal int BeastBarkChancePercent { get; init; } = 20;
    internal float ContactPitch { get; init; } = 1.1F;
    internal float FootstepDistance { get; init; } = 2.5F;
    internal float HoofDistance { get; init; } = 1.8F;
    internal float CartDistance { get; init; } = 6F;
    internal float FootstepVolumeScale { get; init; } = .7F;
    internal int PlayerPainChancePercent { get; init; } = 40;
    internal DaggerfallPresentationAudioTuning Validate()
    {
        if (!float.IsFinite(Volume) || Volume < 0F) throw new ArgumentOutOfRangeException(nameof(Volume));
        if (!float.IsFinite(Pitch) || Pitch <= 0F) throw new ArgumentOutOfRangeException(nameof(Pitch));
        if (!float.IsFinite(SpatialBlend) || SpatialBlend is < 0F or > 1F) throw new ArgumentOutOfRangeException(nameof(SpatialBlend));
        if (!float.IsFinite(MaxDistance) || MaxDistance <= 0F) throw new ArgumentOutOfRangeException(nameof(MaxDistance));
        if (!float.IsFinite(AttractRadius) || AttractRadius <= 0F) throw new ArgumentOutOfRangeException(nameof(AttractRadius));
        if (AttractMinimumDelaySeconds < 0 || AttractMaximumDelaySeconds < AttractMinimumDelaySeconds) throw new ArgumentOutOfRangeException(nameof(AttractMaximumDelaySeconds));
        if (AttractMoveChancePercent is < 0 or > 100 || AttackCueChancePercent is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(AttackCueChancePercent));
        if (!float.IsFinite(OccludedVolumeScale) || OccludedVolumeScale is < 0F or > 1F) throw new ArgumentOutOfRangeException(nameof(OccludedVolumeScale));
        if (BeastMinimumDelaySeconds < 0 || BeastMaximumDelaySeconds < BeastMinimumDelaySeconds
            || VampireAttackChancePercent is < 0 or > 100 || VampireBarkChancePercent is < 0 or > 100 || BeastAttackChancePercent is < 0 or > 100 || BeastBarkChancePercent is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(BeastMaximumDelaySeconds));
        if (!float.IsFinite(HoofDistance) || HoofDistance <= 0 || !float.IsFinite(CartDistance) || CartDistance <= 0) throw new ArgumentOutOfRangeException(nameof(HoofDistance));
        if (!float.IsFinite(FootstepDistance) || FootstepDistance <= 0 || !float.IsFinite(FootstepVolumeScale)
            || FootstepVolumeScale < 0 || PlayerPainChancePercent is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(FootstepDistance));
        if (!float.IsFinite(ContactPitch) || ContactPitch <= 0F) throw new ArgumentOutOfRangeException(nameof(ContactPitch));
        return this;
    }
}

/// <summary>
/// Neutral site fill from the donor's PlayerAdvanced prefab, independent of weather cues. Those levels count 1 as full
/// light; the Engine's lights divide by pi, so each kind of place scales its lights to stand where the art wants them.
/// Outdoors by day the sun carries most of the light, so the day's ambient stands well below the donor's.
/// </summary>
internal sealed record DaggerfallSiteLightingTuning(
    float InteriorDay,
    float InteriorNight,
    float Dungeon,
    float ExteriorNoon,
    float ExteriorNight)
{
    internal float MobileGlowRange { get; init; } = 4f;
    internal float MobileGlowIntensity { get; init; } = .1f;
    /// <summary>The light a flying spell carries in its element's colour (the donor's missile light).</summary>
    internal float SpellLightRange { get; init; } = 6f;
    internal float SpellLightIntensity { get; init; } = 1f;
    /// <summary>What the ambient and lamps of each kind of place are multiplied by.</summary>
    internal float ExteriorScale { get; init; } = 3f;
    internal float InteriorScale { get; init; } = 1.3f;
    internal float DungeonScale { get; init; } = 1f;
    /// <summary>
    /// The sun at full daylight. The day's ambient and the sun together light level ground under a high sun near its
    /// texture's own value; the sun carries most of it, as a clear day's direct light does, so what its shadow covers
    /// falls to about a third of the light at noon, and less deep as the sun lowers.
    /// </summary>
    internal float ExteriorSun { get; init; } = 2.3f;
    /// <summary>
    /// How far the sun's path leans south of overhead, in degrees, as at the Iliac Bay's latitude: at noon it stands that
    /// far from the zenith, so walls facing it take the sun and what stands in its way casts a shadow beside it.
    /// </summary>
    internal float SunTiltDegrees { get; init; } = 30f;
    /// <summary>
    /// How far from the camera the sun's shadow reaches, in metres, and the texels on a side of each of its cascades.
    /// The sun always asks for its shadow; the product draws scene shadows by default, and the player can turn off the
    /// Engine's Shadows video option.
    /// </summary>
    internal float SunShadowRange { get; init; } = 150f;
    internal uint SunShadowResolution { get; init; } = 2048;
    /// <summary>The sun's colour on the horizon, easing to white as it climbs, so dawn and dusk warm what they light.</summary>
    internal Vector3 LowSunColor { get; init; } = new(1f, .68f, .42f);
    /// <summary>The colour of the sky's ambient at night, easing to white by noon. A lamp's warmth stands out against it.</summary>
    internal Vector3 NightColor { get; init; } = new(.7f, .8f, 1f);
    /// <summary>The light, before the Engine divides by pi, at which the first-person view draws at its art's own value: what the clear noon sky gives.</summary>
    internal float ViewFull { get; init; } = 2.875f;
    /// <summary>The dimmest tint of the first-person view, so the hands stay readable in the dark.</summary>
    internal float ViewFloor { get; init; } = .2f;
    /// <summary>The linear colour and level of lit window glass, whose emission adds to the light the wall takes.</summary>
    internal Vector3 WindowColor { get; init; } = new(1f, .47f, .04f);
    internal float WindowLevel { get; init; } = .8f;
    /// <summary>How brightly a refreshed lamp's flame or glass shines over its painted colour, added to the light the lamp takes.</summary>
    internal float LampLevel { get; init; } = 1f;
    /// <summary>How brightly a glowing creature (a fire or frost daedra, a fire atronach) shines over its painted colour, added to the light it takes.</summary>
    internal float CreatureGlow { get; init; } = .6f;
    /// <summary>How brightly a fire, shock or magic effect shines over its painted colour; above 1 it blooms.</summary>
    internal float SpellGlow { get; init; } = 1.5f;
    /// <summary>How much of the light above 1 spreads as a glow; 0 for none.</summary>
    internal float Bloom { get; init; } = .25f;
    internal static DaggerfallSiteLightingTuning Classic { get; } = new(
        160F / 255F, 100F / 255F, 75F / 255F, .3333F, .1333F);

    internal DaggerfallSiteLightingTuning Validate()
    {
        if (!float.IsFinite(ViewFull) || ViewFull <= 0 || !float.IsFinite(ViewFloor) || ViewFloor is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(ViewFull), "The view's full light is finite and positive, and its floor a normalized tint.");
        foreach (float scale in new[] { ExteriorScale, ExteriorSun, InteriorScale, DungeonScale })
            if (!float.IsFinite(scale) || scale <= 0) throw new ArgumentOutOfRangeException(nameof(ExteriorScale), "A site's light scale is finite and positive.");
        if (!float.IsFinite(LowSunColor.X + LowSunColor.Y + LowSunColor.Z) || LowSunColor.X < 0 || LowSunColor.Y < 0 || LowSunColor.Z < 0)
            throw new ArgumentOutOfRangeException(nameof(LowSunColor), "The low sun's colour is finite and not negative.");
        if (!float.IsFinite(NightColor.X + NightColor.Y + NightColor.Z) || NightColor.X < 0 || NightColor.Y < 0 || NightColor.Z < 0)
            throw new ArgumentOutOfRangeException(nameof(NightColor), "The night's colour is finite and not negative.");
        if (!float.IsFinite(SunTiltDegrees) || SunTiltDegrees is < 0 or >= 90)
            throw new ArgumentOutOfRangeException(nameof(SunTiltDegrees), "The sun's path leans from 0 up to but not including 90 degrees.");
        if (!float.IsFinite(SunShadowRange) || SunShadowRange <= 0 || SunShadowResolution is not (256 or 512 or 1024 or 2048))
            throw new ArgumentOutOfRangeException(nameof(SunShadowRange), "The sun's shadow reaches a finite positive range, at 256, 512, 1024 or 2048 texels.");
        if (!float.IsFinite(Bloom) || Bloom < 0) throw new ArgumentOutOfRangeException(nameof(Bloom), "The bloom is finite and not negative.");
        if (!float.IsFinite(WindowColor.X + WindowColor.Y + WindowColor.Z + WindowLevel)
            || WindowColor.X < 0 || WindowColor.Y < 0 || WindowColor.Z < 0 || WindowLevel < 0)
            throw new ArgumentOutOfRangeException(nameof(WindowColor), "Window glass's colour and level are finite and not negative.");
        foreach (float level in new[] { LampLevel, CreatureGlow, SpellGlow })
            if (!float.IsFinite(level) || level < 0)
                throw new ArgumentOutOfRangeException(nameof(LampLevel), "A lamp's, creature's or spell's glow is finite and not negative.");
        if (!float.IsFinite(MobileGlowRange) || MobileGlowRange <= 0 || !float.IsFinite(MobileGlowIntensity) || MobileGlowIntensity < 0)
            throw new ArgumentOutOfRangeException(nameof(DaggerfallSiteLightingTuning), "Mobile glow requires a positive finite range and nonnegative finite intensity.");
        if (!float.IsFinite(SpellLightRange) || SpellLightRange <= 0 || !float.IsFinite(SpellLightIntensity) || SpellLightIntensity < 0)
            throw new ArgumentOutOfRangeException(nameof(DaggerfallSiteLightingTuning), "Spell light requires a positive finite range and nonnegative finite intensity.");
        foreach (float value in new[] { InteriorDay, InteriorNight, Dungeon, ExteriorNoon, ExteriorNight })
            if (!float.IsFinite(value) || value is < 0F or > 1F)
                throw new ArgumentOutOfRangeException(nameof(DaggerfallSiteLightingTuning), "Site ambient values must be finite normalized RGB levels.");
        return this;
    }
}
