using WorldRpg.Kit.Controls;
using System.Text.Json;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed record DaggerfallTuning(
    PlayerControlTuning PlayerControl,
    SpatialTuning Spatial,
    FirstPersonCameraTuning Camera,
    DaggerfallMeleeTargetingTuning MeleeTargeting,
    DaggerfallEnemyBehaviorTuning EnemyBehavior,
    DaggerfallLootInteractionTuning LootInteraction,
    DaggerfallPresentationAudioTuning PresentationAudio)
{
    internal static DaggerfallTuning Defaults { get; } = new(
        new PlayerControlTuning(.0035f, -1.5533f, 1.5533f, .35f, InvertHorizontal: false, InvertVertical: false, WrapYaw: true),
        new SpatialTuning(.5, 32, 32, 2, new CharacterControllerTuning(
            StandingHeight: 1.8f,
            Radius: .25f,
            ForwardSpeed: 3.5f,
            BackwardSpeed: 3.5f,
            StrafeSpeed: 3.5f,
            RecoveryMaximumDistance: 1f,
            MaximumStepHeight: .75f)),
        new FirstPersonCameraTuning(.75f, 65d, .1d, 100d),
        new DaggerfallMeleeTargetingTuning(2.25d, .5d),
        new DaggerfallEnemyBehaviorTuning(12d, .5d, 1.25d, 3f, 32),
        new DaggerfallLootInteractionTuning(2.25d, .5d),
        new DaggerfallPresentationAudioTuning(1F, 1F, 0F, 1F));

    internal DaggerfallTuning Validate() => this with
    {
        PlayerControl = PlayerControl.Validate(),
        Spatial = Spatial.Validate(),
        Camera = Camera.Validate(),
        MeleeTargeting = MeleeTargeting.Validate(),
        EnemyBehavior = EnemyBehavior.Validate(),
        LootInteraction = LootInteraction.Validate(),
        PresentationAudio = PresentationAudio.Validate(),
    };

    internal static DaggerfallTuning Read(ReadOnlySpan<byte> payload)
    {
        using JsonDocument document = JsonDocument.Parse(payload.ToArray());
        JsonElement root = document.RootElement;
        JsonElement controls = root.GetProperty("playerControl");
        JsonElement spatial = root.GetProperty("spatial");
        JsonElement camera = root.GetProperty("camera");
        JsonElement meleeTargeting = root.GetProperty("meleeTargeting");
        JsonElement enemyBehavior = root.GetProperty("enemyBehavior");
        JsonElement lootInteraction = root.GetProperty("lootInteraction");
        JsonElement presentationAudio = root.GetProperty("presentationAudio");
        return new DaggerfallTuning(
            new PlayerControlTuning(
                controls.GetProperty("lookSensitivity").GetSingle(),
                controls.GetProperty("pitchMinimumRadians").GetSingle(),
                controls.GetProperty("pitchMaximumRadians").GetSingle(),
                controls.GetProperty("maximumLookDeltaRadians").GetSingle(),
                controls.GetProperty("invertHorizontal").GetBoolean(),
                controls.GetProperty("invertVertical").GetBoolean(),
                controls.GetProperty("wrapYaw").GetBoolean()),
            new SpatialTuning(
                spatial.GetProperty("collisionVoxelSize").GetDouble(),
                checked((uint)spatial.GetProperty("collisionChunkSize").GetInt32()),
                checked((uint)spatial.GetProperty("navigationChunkSize").GetInt32()),
                checked((uint)spatial.GetProperty("navigationMaximumStepCells").GetInt32()),
                ReadCharacterController(spatial.GetProperty("characterController"))),
            new FirstPersonCameraTuning(
                camera.GetProperty("eyeHeight").GetSingle(),
                camera.GetProperty("fieldOfViewYDegrees").GetDouble(),
                camera.GetProperty("nearPlane").GetDouble(),
                camera.GetProperty("farPlane").GetDouble()),
            new DaggerfallMeleeTargetingTuning(
                meleeTargeting.GetProperty("maximumDistance").GetDouble(),
                meleeTargeting.GetProperty("minimumFacingCosine").GetDouble()),
            new DaggerfallEnemyBehaviorTuning(
                enemyBehavior.GetProperty("detectionDistance").GetDouble(),
                enemyBehavior.GetProperty("minimumFacingCosine").GetDouble(),
                enemyBehavior.GetProperty("attackReach").GetDouble(),
                enemyBehavior.GetProperty("chaseSpeedUnitsPerSecond").GetSingle(),
                checked((uint)enemyBehavior.GetProperty("navigationMaximumVisited").GetInt32())),
            new DaggerfallLootInteractionTuning(
                lootInteraction.GetProperty("maximumDistance").GetDouble(),
                lootInteraction.GetProperty("minimumFacingCosine").GetDouble()),
            new DaggerfallPresentationAudioTuning(
                presentationAudio.GetProperty("volume").GetSingle(),
                presentationAudio.GetProperty("pitch").GetSingle(),
                presentationAudio.GetProperty("spatialBlend").GetSingle(),
                presentationAudio.GetProperty("attenuation").GetSingle()))
            .Validate();
    }

    private static CharacterControllerTuning ReadCharacterController(JsonElement controller) => new(
        StandingHeight: controller.GetProperty("standingHeight").GetSingle(),
        Radius: controller.GetProperty("radius").GetSingle(),
        ForwardSpeed: controller.GetProperty("forwardSpeed").GetSingle(),
        BackwardSpeed: controller.GetProperty("backwardSpeed").GetSingle(),
        StrafeSpeed: controller.GetProperty("strafeSpeed").GetSingle(),
        RecoveryMaximumDistance: controller.GetProperty("recoveryMaximumDistance").GetSingle(),
        MaximumStepHeight: controller.GetProperty("maximumStepHeight").GetSingle());
}

/// <summary>Ruleset-tunable Engine visibility query bounds for explicit corpse looting.</summary>
internal sealed record DaggerfallLootInteractionTuning(double MaximumDistance, double MinimumFacingCosine)
{
    internal DaggerfallLootInteractionTuning Validate()
    {
        if (!double.IsFinite(MaximumDistance) || MaximumDistance <= 0d) throw new ArgumentOutOfRangeException(nameof(MaximumDistance));
        if (!double.IsFinite(MinimumFacingCosine) || MinimumFacingCosine is < -1d or > 1d) throw new ArgumentOutOfRangeException(nameof(MinimumFacingCosine));
        return this;
    }
}

/// <summary>Ruleset policy for visibility-led enemy chase and attack decisions.</summary>
internal sealed record DaggerfallEnemyBehaviorTuning(
    double DetectionDistance,
    double MinimumFacingCosine,
    double AttackReach,
    float ChaseSpeedUnitsPerSecond,
    uint NavigationMaximumVisited)
{
    internal DaggerfallEnemyBehaviorTuning Validate()
    {
        if (!double.IsFinite(DetectionDistance) || DetectionDistance <= 0d) throw new ArgumentOutOfRangeException(nameof(DetectionDistance));
        if (!double.IsFinite(MinimumFacingCosine) || MinimumFacingCosine is < -1d or > 1d) throw new ArgumentOutOfRangeException(nameof(MinimumFacingCosine));
        if (!double.IsFinite(AttackReach) || AttackReach <= 0d || AttackReach > DetectionDistance) throw new ArgumentOutOfRangeException(nameof(AttackReach));
        if (!float.IsFinite(ChaseSpeedUnitsPerSecond) || ChaseSpeedUnitsPerSecond <= 0f) throw new ArgumentOutOfRangeException(nameof(ChaseSpeedUnitsPerSecond));
        if (NavigationMaximumVisited == 0) throw new ArgumentOutOfRangeException(nameof(NavigationMaximumVisited));
        return this;
    }
}

/// <summary>Ruleset-tunable query bounds for ordinary Daggerfall player melee.</summary>
internal sealed record DaggerfallMeleeTargetingTuning(double MaximumDistance, double MinimumFacingCosine)
{
    internal DaggerfallMeleeTargetingTuning Validate()
    {
        if (!double.IsFinite(MaximumDistance) || MaximumDistance <= 0d) throw new ArgumentOutOfRangeException(nameof(MaximumDistance));
        if (!double.IsFinite(MinimumFacingCosine) || MinimumFacingCosine is < -1d or > 1d) throw new ArgumentOutOfRangeException(nameof(MinimumFacingCosine));
        return this;
    }
}

/// <summary>Ruleset-authored descriptor values for one-shot classic presentation audio.</summary>
internal sealed record DaggerfallPresentationAudioTuning(float Volume, float Pitch, float SpatialBlend, float Attenuation)
{
    internal DaggerfallPresentationAudioTuning Validate()
    {
        if (!float.IsFinite(Volume) || Volume < 0F) throw new ArgumentOutOfRangeException(nameof(Volume));
        if (!float.IsFinite(Pitch) || Pitch <= 0F) throw new ArgumentOutOfRangeException(nameof(Pitch));
        if (!float.IsFinite(SpatialBlend) || SpatialBlend is < 0F or > 1F) throw new ArgumentOutOfRangeException(nameof(SpatialBlend));
        if (!float.IsFinite(Attenuation) || Attenuation <= 0F) throw new ArgumentOutOfRangeException(nameof(Attenuation));
        return this;
    }
}

internal readonly record struct PlayerInitialLook(float YawRadians, float PitchRadians)
{
    internal PlayerInitialLook Validate()
    {
        if (!float.IsFinite(YawRadians)) throw new ArgumentOutOfRangeException(nameof(YawRadians));
        if (!float.IsFinite(PitchRadians)) throw new ArgumentOutOfRangeException(nameof(PitchRadians));
        return this;
    }
}
