using WorldRpg.Kit.Controls;
using Rusty.Engine;
using System.Text.Json;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed record DaggerfallTuning(
    PlayerControlTuning PlayerControl,
    ControllerInputTuning ControllerInput,
    SpatialTuning Spatial,
    FirstPersonCameraTuning Camera,
    DaggerfallMeleeTargetingTuning MeleeTargeting,
    DaggerfallEnemyBehaviorTuning EnemyBehavior,
    DaggerfallLootInteractionTuning LootInteraction,
    DaggerfallTimeTuning Time,
    DaggerfallStaminaRecoveryTuning StaminaRecovery,
    DaggerfallPresentationAudioTuning PresentationAudio)
{
    internal static DaggerfallTuning Defaults { get; } = new(
        // Screen-space mouse Y increases downward; Engine camera pitch increases upward.
        new PlayerControlTuning(.0035f, -1.5533f, 1.5533f, .35f, InvertHorizontal: false, InvertVertical: true, WrapYaw: true),
        ControllerInputTuning.Standard with { Actions = DaggerfallInput.PadActions },
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
        new DaggerfallTimeTuning(12d),
        new DaggerfallStaminaRecoveryTuning(5d, 2d),
        new DaggerfallPresentationAudioTuning(1F, 1F, 0F, 1F));

    internal DaggerfallTuning Validate() => this with
    {
        PlayerControl = PlayerControl.Validate(),
        ControllerInput = ControllerInput.Validate(),
        Spatial = Spatial.Validate(),
        Camera = Camera.Validate(),
        MeleeTargeting = MeleeTargeting.Validate(),
        EnemyBehavior = EnemyBehavior.Validate(),
        LootInteraction = LootInteraction.Validate(),
        Time = Time.Validate(),
        StaminaRecovery = StaminaRecovery.Validate(),
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
        JsonElement staminaRecovery = root.GetProperty("staminaRecovery");
        JsonElement time = root.GetProperty("time");
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
            // A payload that predates the controller block keeps working: the layout the shell
            // delivers is a default rather than something every payload has to restate, and only a
            // payload that actually rebinds the pad has to carry the block.
            root.TryGetProperty("controllerInput", out JsonElement controllerInput) ? ReadControllerInput(controllerInput) : Defaults.ControllerInput,
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
                checked((uint)enemyBehavior.GetProperty("navigationMaximumVisited").GetInt32()),
                enemyBehavior.TryGetProperty("spawnGroundProbeLift", out JsonElement lift) ? lift.GetSingle() : Defaults.EnemyBehavior.SpawnGroundProbeLift,
                enemyBehavior.TryGetProperty("spawnGroundProbeDistance", out JsonElement distance) ? distance.GetDouble() : Defaults.EnemyBehavior.SpawnGroundProbeDistance),
            new DaggerfallLootInteractionTuning(
                lootInteraction.GetProperty("maximumDistance").GetDouble(),
                lootInteraction.GetProperty("minimumFacingCosine").GetDouble()),
            new DaggerfallTimeTuning(time.GetProperty("gameSecondsPerRealSecond").GetDouble()),
            new DaggerfallStaminaRecoveryTuning(
                staminaRecovery.GetProperty("pointsPerSecond").GetDouble(),
                staminaRecovery.GetProperty("delayAfterAttackSeconds").GetDouble()),
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

    /// <summary>
    /// Reads the pad's positional mapping. The Engine numbers controller axes and buttons rather than
    /// naming them, so the payload numbers them too and the ruleset refuses an index the Engine does
    /// not publish instead of silently binding the nearest one.
    /// </summary>
    private static ControllerInputTuning ReadControllerInput(JsonElement controller)
    {
        List<ControllerActionBinding> actions = [];
        foreach (JsonElement binding in controller.GetProperty("actions").EnumerateArray())
            actions.Add(new ControllerActionBinding(
                ReadControllerButton(binding.GetProperty("button").GetInt32()),
                // A named-but-empty action is the same dead button as a missing one, so it is refused
                // here rather than loaded as a binding that presses nothing.
                new InputActionId(binding.GetProperty("action").GetString() is { Length: > 0 } action && !string.IsNullOrWhiteSpace(action)
                    ? action
                    : throw new JsonException("A controller action binding must name an action."))));
        return new ControllerInputTuning(
            ReadControllerAxis(controller.GetProperty("movementXAxis").GetInt32()),
            ReadControllerAxis(controller.GetProperty("movementYAxis").GetInt32()),
            ReadControllerAxis(controller.GetProperty("lookXAxis").GetInt32()),
            ReadControllerAxis(controller.GetProperty("lookYAxis").GetInt32()),
            controller.GetProperty("movementDeadzone").GetSingle(),
            controller.GetProperty("lookDeadzone").GetSingle(),
            controller.GetProperty("movementStrafeSensitivity").GetSingle(),
            controller.GetProperty("movementForwardSensitivity").GetSingle(),
            controller.GetProperty("lookYawRadiansPerSecond").GetSingle(),
            controller.GetProperty("lookPitchRadiansPerSecond").GetSingle(),
            controller.GetProperty("invertMovementX").GetBoolean(),
            controller.GetProperty("invertMovementY").GetBoolean(),
            controller.GetProperty("invertLookX").GetBoolean(),
            controller.GetProperty("invertLookY").GetBoolean(),
            actions).Validate();
    }

    private static ControllerAxis ReadControllerAxis(int index) => index switch
    {
        0 => ControllerAxis.Axis0,
        1 => ControllerAxis.Axis1,
        2 => ControllerAxis.Axis2,
        3 => ControllerAxis.Axis3,
        _ => throw new JsonException($"Controller axis {index} is not one the Engine publishes; axes are numbered 0 through 3."),
    };

    private static ControllerButton ReadControllerButton(int index) => index switch
    {
        0 => ControllerButton.Button0,
        1 => ControllerButton.Button1,
        2 => ControllerButton.Button2,
        3 => ControllerButton.Button3,
        4 => ControllerButton.Button4,
        5 => ControllerButton.Button5,
        6 => ControllerButton.Button6,
        7 => ControllerButton.Button7,
        8 => ControllerButton.Button8,
        9 => ControllerButton.Button9,
        10 => ControllerButton.Button10,
        11 => ControllerButton.Button11,
        12 => ControllerButton.Button12,
        13 => ControllerButton.Button13,
        14 => ControllerButton.Button14,
        15 => ControllerButton.Button15,
        _ => throw new JsonException($"Controller button {index} is not one the Engine publishes; buttons are numbered 0 through 15."),
    };
}

/// <summary>
/// How fast the world's clock runs against admitted real time.
/// </summary>
/// <remarks>
/// The donor's own scale: <c>Assets/Scripts/Internal/WorldTime.cs</c> applies
/// <c>DaggerfallDateTime.RaiseTime(Time.deltaTime * TimeScale)</c> with a default of twelve, so one
/// admitted real second is twelve game seconds. It is tuning rather than a constant because it is
/// adjustable, and the calendar is the thing it is applied to.
/// </remarks>
internal sealed record DaggerfallTimeTuning(double GameSecondsPerRealSecond)
{
    internal DaggerfallTimeTuning Validate()
    {
        if (!double.IsFinite(GameSecondsPerRealSecond) || GameSecondsPerRealSecond <= 0d) throw new ArgumentOutOfRangeException(nameof(GameSecondsPerRealSecond));
        return this;
    }
}

/// <summary>Product-selected real-time stamina recovery; this is not the donor's per-rest-hour fatigue formula.</summary>
internal sealed record DaggerfallStaminaRecoveryTuning(double PointsPerSecond, double DelayAfterAttackSeconds)
{
    internal DaggerfallStaminaRecoveryTuning Validate()
    {
        if (!double.IsFinite(PointsPerSecond) || PointsPerSecond <= 0d) throw new ArgumentOutOfRangeException(nameof(PointsPerSecond));
        if (!double.IsFinite(DelayAfterAttackSeconds) || DelayAfterAttackSeconds < 0d) throw new ArgumentOutOfRangeException(nameof(DelayAfterAttackSeconds));
        return this;
    }
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
    uint NavigationMaximumVisited,
    float SpawnGroundProbeLift = .2f,
    double SpawnGroundProbeDistance = 3d)
{
    internal DaggerfallEnemyBehaviorTuning Validate()
    {
        if (!double.IsFinite(DetectionDistance) || DetectionDistance <= 0d) throw new ArgumentOutOfRangeException(nameof(DetectionDistance));
        if (!double.IsFinite(MinimumFacingCosine) || MinimumFacingCosine is < -1d or > 1d) throw new ArgumentOutOfRangeException(nameof(MinimumFacingCosine));
        if (!double.IsFinite(AttackReach) || AttackReach <= 0d || AttackReach > DetectionDistance) throw new ArgumentOutOfRangeException(nameof(AttackReach));
        if (!float.IsFinite(ChaseSpeedUnitsPerSecond) || ChaseSpeedUnitsPerSecond <= 0f) throw new ArgumentOutOfRangeException(nameof(ChaseSpeedUnitsPerSecond));
        if (!float.IsFinite(SpawnGroundProbeLift) || SpawnGroundProbeLift < 0f) throw new ArgumentOutOfRangeException(nameof(SpawnGroundProbeLift));
        if (!double.IsFinite(SpawnGroundProbeDistance) || SpawnGroundProbeDistance <= SpawnGroundProbeLift) throw new ArgumentOutOfRangeException(nameof(SpawnGroundProbeDistance));
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
