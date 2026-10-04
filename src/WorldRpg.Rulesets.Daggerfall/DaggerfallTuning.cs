using WorldRpg.Kit.Controls;
using Rusty.Engine;
using System.Text.Json;
using WorldRpg.Rulesets.Daggerfall.Modules.Transport;
using WorldRpg.Rulesets.Daggerfall.Property;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed record DaggerfallTuning(
    PlayerControlTuning PlayerControl,
    ControllerInputTuning ControllerInput,
    SpatialTuning Spatial,
    DaggerfallLocomotionTuning Locomotion,
    FirstPersonCameraTuning Camera,
    DaggerfallMeleeTargetingTuning MeleeTargeting,
    DaggerfallEnemyBehaviorTuning EnemyBehavior,
    DaggerfallLootInteractionTuning LootInteraction,
    DaggerfallTimeTuning Time,
    DaggerfallStaminaRecoveryTuning StaminaRecovery,
    DaggerfallPresentationAudioTuning PresentationAudio,
    DaggerfallProgressionTuning Progression,
    DaggerfallSiteLightingTuning SiteLighting,
    DaggerfallClimbingTuning Climbing,
    DaggerfallPropertyTuning Property,
    DaggerfallTransportTuning Transport)
{
    internal DaggerfallStrikeEnchantmentTuning StrikeEnchantments { get; init; } = new(5, 2.25d);
    internal DaggerfallDetectionTuning Detection { get; init; } = new(14d);
    internal DaggerfallMusicTuning Music { get; init; } = new(AlternatePlaylists: false);
    internal DaggerfallWorldOriginTuning WorldOrigin { get; init; } = new(500f);
    internal DaggerfallSwimmingTuning Swimming { get; init; } = DaggerfallSwimmingTuning.Classic;

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
            CrouchedHeight: 1.1f,
            JumpSpeed: 7f,
            JumpBufferSeconds: .12f,
            JumpCoyoteSeconds: .1f,
            JumpLandingLockoutSeconds: .1f,
            JumpHeldInputRetriggers: false,
            RecoveryMaximumDistance: 1f,
            MaximumStepHeight: .75f)),
        DaggerfallLocomotionTuning.Classic,
        new FirstPersonCameraTuning(.75f, 65d, .1d, 100d),
        new DaggerfallMeleeTargetingTuning(2.25d, .5d, .35d),
        new DaggerfallEnemyBehaviorTuning(12d, 3f, 1024),
        new DaggerfallLootInteractionTuning(2.25d, .5d),
        new DaggerfallTimeTuning(12d),
        new DaggerfallStaminaRecoveryTuning(5d, 2d),
        new DaggerfallPresentationAudioTuning(1F, 1F, 0F, 16F),
        new DaggerfallProgressionTuning(EnableExperimentalKillExperience: false, ExperiencePerLevel: 500),
        DaggerfallSiteLightingTuning.Classic,
        DaggerfallClimbingTuning.Classic,
        // DaggerfallBankManager's house and ship prices, sale percentage and ship scene anchors.
        new DaggerfallPropertyTuning(
            HousePricePerModelRadius: 1280,
            SmallShipPrice: 100_000,
            LargeShipPrice: 200_000,
            SalePercent: 85,
            SmallShipArrival: new(2, 2),
            LargeShipArrival: new(5, 5)),
        // TransportManager and travel-time donor values, in their original integer units.
        new DaggerfallTransportTuning(
            FootTravelModifier: 256,
            HorseTravelModifier: 128,
            CartTravelModifier: 192,
            FootOceanMinutes: 255,
            ShipOceanMinutes: 51,
            WalkBaseClassicUnits: 150,
            HorseBaseClassicUnits: 375,
            CartBaseClassicUnits: 250,
            WagonCapacityClassicUnits: 300_000,
            WagonAccessRange: 5f));

    internal DaggerfallTuning Validate() => this with
    {
        PlayerControl = PlayerControl.Validate(),
        ControllerInput = ControllerInput.Validate(),
        Spatial = Spatial.Validate(),
        Locomotion = Locomotion.Validate(),
        Camera = Camera.Validate(),
        MeleeTargeting = MeleeTargeting.Validate(),
        EnemyBehavior = EnemyBehavior.Validate(),
        LootInteraction = LootInteraction.Validate(),
        Time = Time.Validate(),
        StaminaRecovery = StaminaRecovery.Validate(),
        PresentationAudio = PresentationAudio.Validate(),
        Progression = Progression.Validate(),
        SiteLighting = SiteLighting.Validate(),
        Climbing = Climbing.Validate(),
        Property = Property.Validate(),
        Transport = Transport.Validate(),
        Detection = Detection.Validate(),
        StrikeEnchantments = StrikeEnchantments.Validate(),
        WorldOrigin = WorldOrigin.Validate(),
        Swimming = Swimming.Validate(),
    };

    internal static DaggerfallTuning Read(ReadOnlySpan<byte> payload)
    {
        using JsonDocument document = JsonDocument.Parse(payload.ToArray());
        JsonElement root = document.RootElement;
        // This profile used to tune one reach for every enemy in the world. Reach is authored on the
        // attack that carries it now, so a profile still naming it is refused rather than loaded with its
        // intent quietly dropped: an operator who had tuned melee reach would otherwise get the authored
        // value back with no way to tell why their tuning stopped applying.
        if (root.TryGetProperty("enemyBehavior", out JsonElement obsoleteBehavior) && obsoleteBehavior.TryGetProperty("attackReach", out _))
            throw new InvalidOperationException("Tuning profile enemyBehavior.attackReach is obsolete: how far an attack carries is authored on the action, not on the enemy behaviour tuning. Remove the key and set reach on the actions that need it.");
        JsonElement controls = root.GetProperty("playerControl");
        JsonElement spatial = root.GetProperty("spatial");
        JsonElement locomotion = root.GetProperty("locomotion");
        JsonElement camera = root.GetProperty("camera");
        JsonElement meleeTargeting = root.GetProperty("meleeTargeting");
        JsonElement enemyBehavior = root.GetProperty("enemyBehavior");
        JsonElement lootInteraction = root.GetProperty("lootInteraction");
        JsonElement staminaRecovery = root.GetProperty("staminaRecovery");
        JsonElement time = root.GetProperty("time");
        JsonElement presentationAudio = root.GetProperty("presentationAudio");
        JsonElement progression = root.GetProperty("progression");
        JsonElement siteLighting = root.GetProperty("siteLighting");
        JsonElement climbing = root.GetProperty("climbing");
        JsonElement property = root.GetProperty("property");
        JsonElement transport = root.GetProperty("transport");
        return new DaggerfallTuning(
            new PlayerControlTuning(
                controls.GetProperty("lookSensitivity").GetSingle(),
                controls.GetProperty("pitchMinimumRadians").GetSingle(),
                controls.GetProperty("pitchMaximumRadians").GetSingle(),
                controls.GetProperty("maximumLookDeltaRadians").GetSingle(),
                controls.GetProperty("invertHorizontal").GetBoolean(),
                controls.GetProperty("invertVertical").GetBoolean(),
                controls.GetProperty("wrapYaw").GetBoolean()),
            ReadControllerInput(root.GetProperty("controllerInput")),
            new SpatialTuning(
                spatial.GetProperty("collisionVoxelSize").GetDouble(),
                checked((uint)spatial.GetProperty("collisionChunkSize").GetInt32()),
                checked((uint)spatial.GetProperty("navigationChunkSize").GetInt32()),
                checked((uint)spatial.GetProperty("navigationMaximumStepCells").GetInt32()),
                ReadCharacterController(spatial.GetProperty("characterController"))),
            new DaggerfallLocomotionTuning(
                locomotion.GetProperty("classicToEngineSpeedRatio").GetSingle(),
                locomotion.GetProperty("walkBase").GetSingle(),
                locomotion.GetProperty("crouchBase").GetSingle(),
                locomotion.GetProperty("runBaseMultiplier").GetSingle(),
                locomotion.GetProperty("runningSkillDivisor").GetSingle(),
                locomotion.GetProperty("minimumWalkSpeedAttribute").GetInt32(),
                locomotion.GetProperty("idleFatiguePerGameMinute").GetInt32(),
                locomotion.GetProperty("runningFatiguePerGameMinute").GetInt32(),
                locomotion.GetProperty("jumpFatigueCost").GetInt32(),
                locomotion.GetProperty("jumpBaseSpeed").GetSingle(),
                locomotion.GetProperty("jumpSkillMultiplier").GetSingle(),
                locomotion.GetProperty("crouchedJumpMultiplier").GetSingle(),
                locomotion.GetProperty("climbingFatiguePerGameMinute").GetInt32(),
                locomotion.GetProperty("levitationVerticalSpeed").GetSingle()),
            new FirstPersonCameraTuning(
                camera.GetProperty("eyeHeight").GetSingle(),
                camera.GetProperty("fieldOfViewYDegrees").GetDouble(),
                camera.GetProperty("nearPlane").GetDouble(),
                camera.GetProperty("farPlane").GetDouble()),
            new DaggerfallMeleeTargetingTuning(
                meleeTargeting.GetProperty("maximumDistance").GetDouble(),
                meleeTargeting.GetProperty("minimumFacingCosine").GetDouble(),
                meleeTargeting.GetProperty("minimumSwingGestureRadians").GetDouble()),
            new DaggerfallEnemyBehaviorTuning(
                enemyBehavior.GetProperty("detectionDistance").GetDouble(),
                enemyBehavior.GetProperty("chaseSpeedUnitsPerSecond").GetSingle(),
                checked((uint)enemyBehavior.GetProperty("navigationMaximumVisited").GetInt32()),
                enemyBehavior.GetProperty("spawnGroundProbeLift").GetSingle(),
                enemyBehavior.GetProperty("spawnGroundProbeDistance").GetDouble()),
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
                presentationAudio.GetProperty("maxDistance").GetSingle())
            {
                AttractRadius = presentationAudio.GetProperty("attractRadius").GetSingle(),
                AttractMinimumDelaySeconds = presentationAudio.GetProperty("attractMinimumDelaySeconds").GetInt32(),
                AttractMaximumDelaySeconds = presentationAudio.GetProperty("attractMaximumDelaySeconds").GetInt32(),
                AttractMoveChancePercent = presentationAudio.GetProperty("attractMoveChancePercent").GetInt32(),
                AttackCueChancePercent = presentationAudio.GetProperty("attackCueChancePercent").GetInt32(),
                OccludedVolumeScale = presentationAudio.GetProperty("occludedVolumeScale").GetSingle(),
                MuteHumanSounds = presentationAudio.GetProperty("muteHumanSounds").GetBoolean(),
                ContactPitch = presentationAudio.GetProperty("contactPitch").GetSingle(),
            },
            new DaggerfallProgressionTuning(
                progression.GetProperty("enableExperimentalKillExperience").GetBoolean(),
                progression.GetProperty("experiencePerLevel").GetInt32()),
            new DaggerfallSiteLightingTuning(
                siteLighting.GetProperty("interiorDay").GetSingle(),
                siteLighting.GetProperty("interiorNight").GetSingle(),
                siteLighting.GetProperty("dungeon").GetSingle(),
                siteLighting.GetProperty("exteriorNoon").GetSingle(),
                siteLighting.GetProperty("exteriorNight").GetSingle()),
            new DaggerfallClimbingTuning(
                climbing.GetProperty("startCheckSeconds").GetSingle(),
                climbing.GetProperty("continueCheckSeconds").GetSingle(),
                climbing.GetProperty("regainCheckSeconds").GetSingle(),
                climbing.GetProperty("startBaseChance").GetInt32(),
                climbing.GetProperty("graspBaseChance").GetInt32(),
                climbing.GetProperty("continueBaseChance").GetInt32(),
                climbing.GetProperty("regainBaseChance").GetInt32(),
                climbing.GetProperty("speedDivisor").GetSingle(),
                climbing.GetProperty("enhancedSpeedMultiplier").GetSingle()),
            new DaggerfallPropertyTuning(
                property.GetProperty("housePricePerModelRadius").GetInt32(),
                property.GetProperty("smallShipPrice").GetUInt64(),
                property.GetProperty("largeShipPrice").GetUInt64(),
                property.GetProperty("salePercent").GetInt32(),
                ReadShipArrival(property.GetProperty("smallShipArrival")),
                ReadShipArrival(property.GetProperty("largeShipArrival"))),
            new DaggerfallTransportTuning(
                transport.GetProperty("footTravelModifier").GetInt32(),
                transport.GetProperty("horseTravelModifier").GetInt32(),
                transport.GetProperty("cartTravelModifier").GetInt32(),
                transport.GetProperty("footOceanMinutes").GetInt32(),
                transport.GetProperty("shipOceanMinutes").GetInt32(),
                transport.GetProperty("walkBaseClassicUnits").GetInt32(),
                transport.GetProperty("horseBaseClassicUnits").GetInt32(),
                transport.GetProperty("cartBaseClassicUnits").GetInt32(),
                transport.GetProperty("wagonCapacityClassicUnits").GetInt32(),
                transport.GetProperty("wagonAccessRange").GetSingle()))
        {
            StrikeEnchantments = new(root.GetProperty("strikeEnchantments").GetProperty("damageAdjustment").GetInt32(),
                root.GetProperty("strikeEnchantments").GetProperty("vampiricRange").GetDouble()),
            Detection = new(root.GetProperty("detection").GetProperty("maximumDistance").GetDouble()),
            Music = new DaggerfallMusicTuning(root.GetProperty("music").GetProperty("alternatePlaylists").GetBoolean()),
            WorldOrigin = new(root.GetProperty("worldOrigin").GetProperty("verticalRebaseDistance").GetSingle()),
            Swimming = root.TryGetProperty("swimming", out JsonElement swimming)
                ? ReadSwimming(swimming)
                : DaggerfallSwimmingTuning.Classic,
        }.Validate();
    }

    private static DaggerfallShipArrivalAnchor ReadShipArrival(JsonElement anchor) => new(
        anchor.GetProperty("mapPixelX").GetInt32(),
        anchor.GetProperty("mapPixelY").GetInt32());

    private static CharacterControllerTuning ReadCharacterController(JsonElement controller) => new(
        StandingHeight: controller.GetProperty("standingHeight").GetSingle(),
        CrouchedHeight: controller.GetProperty("crouchedHeight").GetSingle(),
        Radius: controller.GetProperty("radius").GetSingle(),
        ForwardSpeed: controller.GetProperty("forwardSpeed").GetSingle(),
        BackwardSpeed: controller.GetProperty("backwardSpeed").GetSingle(),
        StrafeSpeed: controller.GetProperty("strafeSpeed").GetSingle(),
        JumpSpeed: controller.GetProperty("jumpSpeed").GetSingle(),
        JumpBufferSeconds: controller.GetProperty("jumpBufferSeconds").GetSingle(),
        JumpCoyoteSeconds: controller.GetProperty("jumpCoyoteSeconds").GetSingle(),
        JumpLandingLockoutSeconds: controller.GetProperty("jumpLandingLockoutSeconds").GetSingle(),
        JumpHeldInputRetriggers: controller.GetProperty("jumpHeldInputRetriggers").GetBoolean(),
        RecoveryMaximumDistance: controller.GetProperty("recoveryMaximumDistance").GetSingle(),
        MaximumStepHeight: controller.GetProperty("maximumStepHeight").GetSingle());

    private static DaggerfallSwimmingTuning ReadSwimming(JsonElement swimming) => new(
        swimming.GetProperty("speed").GetSingle(),
        swimming.GetProperty("acceleration").GetSingle(),
        swimming.GetProperty("drag").GetSingle(),
        swimming.GetProperty("gravityScale").GetSingle(),
        swimming.GetProperty("buoyancy").GetSingle(),
        swimming.GetProperty("breathSecondsPerPoint").GetSingle());

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
/// Explicit opt-in for the retained non-classic kill-XP experiment, and the experience each of its
/// levels costs. Classic progression is the donor's skill-sum formula and reads neither value.
/// </summary>
internal sealed record DaggerfallProgressionTuning(bool EnableExperimentalKillExperience, int ExperiencePerLevel)
{
    internal DaggerfallProgressionTuning Validate()
    {
        if (ExperiencePerLevel <= 0) throw new ArgumentOutOfRangeException(nameof(ExperiencePerLevel));
        return this;
    }
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

/// <summary>
/// Ruleset policy for visibility-led enemy chase and attack decisions.
/// </summary>
/// <remarks>
/// There is no reach here: how far an attack carries is a property of the attack, so it is authored on the
/// action the actor swings with and read from there. A single tuned reach would make every enemy in the
/// world reach the same distance, which is wrong the moment one of them carries a bow.
/// </remarks>
internal sealed record DaggerfallEnemyBehaviorTuning(
    double DetectionDistance,
    float ChaseSpeedUnitsPerSecond,
    uint NavigationMaximumVisited,
    float SpawnGroundProbeLift = .2f,
    double SpawnGroundProbeDistance = 3d)
{
    internal DaggerfallEnemyBehaviorTuning Validate()
    {
        if (!double.IsFinite(DetectionDistance) || DetectionDistance <= 0d) throw new ArgumentOutOfRangeException(nameof(DetectionDistance));
        if (!float.IsFinite(ChaseSpeedUnitsPerSecond) || ChaseSpeedUnitsPerSecond <= 0f) throw new ArgumentOutOfRangeException(nameof(ChaseSpeedUnitsPerSecond));
        if (!float.IsFinite(SpawnGroundProbeLift) || SpawnGroundProbeLift < 0f) throw new ArgumentOutOfRangeException(nameof(SpawnGroundProbeLift));
        if (!double.IsFinite(SpawnGroundProbeDistance) || SpawnGroundProbeDistance <= SpawnGroundProbeLift) throw new ArgumentOutOfRangeException(nameof(SpawnGroundProbeDistance));
        if (NavigationMaximumVisited == 0) throw new ArgumentOutOfRangeException(nameof(NavigationMaximumVisited));
        return this;
    }
}

/// <summary>Ruleset-tunable query bounds for ordinary Daggerfall player melee.</summary>
internal sealed record DaggerfallMeleeTargetingTuning(double MaximumDistance, double MinimumFacingCosine, double MinimumSwingGestureRadians)
{
    internal DaggerfallMeleeTargetingTuning Validate()
    {
        if (!double.IsFinite(MaximumDistance) || MaximumDistance <= 0d) throw new ArgumentOutOfRangeException(nameof(MaximumDistance));
        if (!double.IsFinite(MinimumFacingCosine) || MinimumFacingCosine is < -1d or > 1d) throw new ArgumentOutOfRangeException(nameof(MinimumFacingCosine));
        if (!double.IsFinite(MinimumSwingGestureRadians) || MinimumSwingGestureRadians <= 0d) throw new ArgumentOutOfRangeException(nameof(MinimumSwingGestureRadians));
        return this;
    }
}

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
    internal float ContactPitch { get; init; } = 1.1F;
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
        if (!float.IsFinite(ContactPitch) || ContactPitch <= 0F) throw new ArgumentOutOfRangeException(nameof(ContactPitch));
        return this;
    }
}

/// <summary>Neutral site fill from the donor's PlayerAdvanced prefab, independent of weather cues.</summary>
internal sealed record DaggerfallSiteLightingTuning(
    float InteriorDay,
    float InteriorNight,
    float Dungeon,
    float ExteriorNoon,
    float ExteriorNight)
{
    internal static DaggerfallSiteLightingTuning Classic { get; } = new(
        160F / 255F, 100F / 255F, 75F / 255F, 150F / 255F, 50F / 255F);

    internal DaggerfallSiteLightingTuning Validate()
    {
        foreach (float value in new[] { InteriorDay, InteriorNight, Dungeon, ExteriorNoon, ExteriorNight })
            if (!float.IsFinite(value) || value is < 0F or > 1F)
                throw new ArgumentOutOfRangeException(nameof(DaggerfallSiteLightingTuning), "Site ambient values must be finite normalized RGB levels.");
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

/// <summary>Chooses the donor's standard or alternate playlists; JSON admission requires a Boolean.</summary>
internal sealed record DaggerfallMusicTuning(bool AlternatePlaylists);

internal sealed record DaggerfallDetectionTuning(double MaximumDistance)
{
    internal DaggerfallDetectionTuning Validate()
    {
        if (!double.IsFinite(MaximumDistance) || MaximumDistance <= 0d)
            throw new ArgumentOutOfRangeException(nameof(MaximumDistance));
        return this;
    }
}
