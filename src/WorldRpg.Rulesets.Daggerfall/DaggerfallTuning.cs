using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Policies;
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
    DaggerfallPresentationAudioTuning PresentationAudio,
    DaggerfallProgressionTuning Progression,
    DaggerfallSiteLightingTuning SiteLighting,
    DaggerfallClimbingTuning Climbing,
    DaggerfallPropertyTuning Property,
    DaggerfallTransportTuning Transport)
{
    internal DaggerfallQuestOfferTuning QuestOffers { get; init; } = new(25, 25);
    internal DaggerfallQuestRewardTuning QuestRewards { get; init; } = new(7, 18, 4, 50);
    internal DaggerfallQuestSpawningTuning QuestSpawning { get; init; } = new(5f, 20f, 8f, 25f, 5, 77.5f, 95f, 105.469f, 25);
    internal DaggerfallLawTuning Law { get; init; } = new(5, 10, 2, 5, 12.8f, 51.2f, 3.2d);
    internal DaggerfallNormalLightTuning NormalLight { get; init; } = new(1.4f, .25f, 15f, 1f);
    internal DaggerfallVampirismTuning Vampirism { get; init; } = DaggerfallVampirismTuning.Classic;
    internal DaggerfallLycanthropyTuning Lycanthropy { get; init; } = DaggerfallLycanthropyTuning.Classic;
    internal DaggerfallStrikeEnchantmentTuning StrikeEnchantments { get; init; } = new(5, 2.25d);
    internal DaggerfallDetectionTuning Detection { get; init; } = new(14d);
    internal DaggerfallMusicTuning Music { get; init; } = new(AlternatePlaylists: false);
    internal DaggerfallWorldOriginTuning WorldOrigin { get; init; } = new(500f);
    internal DaggerfallCivilianWanderTuning CivilianWander { get; init; } = DaggerfallCivilianWanderTuning.Classic;
    internal DaggerfallSwimmingTuning Swimming { get; init; } = DaggerfallSwimmingTuning.Classic;
    internal DaggerfallWeatherTuning Weather { get; init; } = DaggerfallWeatherTuning.Classic;
    internal DaggerfallAmbientTuning Ambient { get; init; } = DaggerfallAmbientTuning.Classic;

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
        PresentationAudio = PresentationAudio.Validate(),
        Progression = Progression.Validate(),
        SiteLighting = SiteLighting.Validate(),
        Climbing = Climbing.Validate(),
        Property = Property.Validate(),
        Transport = Transport.Validate(),
        Detection = Detection.Validate(),
        StrikeEnchantments = StrikeEnchantments.Validate(),
        WorldOrigin = WorldOrigin.Validate(),
        CivilianWander = CivilianWander.Validate(),
        Swimming = Swimming.Validate(),
        Weather = Weather.Validate(),
        Ambient = Ambient.Validate(),
        Lycanthropy = Lycanthropy.Validate(),
        Vampirism = Vampirism.Validate(),
        NormalLight = NormalLight.Validate(),
        Law = Law.Validate(),
        QuestSpawning = QuestSpawning.Validate(),
        QuestOffers = QuestOffers.Validate(),
        QuestRewards = QuestRewards.Validate(),
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
                locomotion.TryGetProperty("swimmingFatiguePerGameMinute", out JsonElement swimmingFatigue)
                    ? swimmingFatigue.GetInt32() : 44,
                locomotion.GetProperty("levitationVerticalSpeed").GetSingle(),
                locomotion.GetProperty("athleticJumpBonus").GetSingle(),
                locomotion.GetProperty("improvedAthleticJumpBonus").GetSingle(),
                locomotion.GetProperty("enhancedJumpBonus").GetSingle(),
                locomotion.GetProperty("slowfallDescentSpeed").GetSingle()),
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
                enemyBehavior.GetProperty("spawnGroundProbeDistance").GetDouble())
            {
                Maneuvers = new(
                    enemyBehavior.GetProperty("maneuvers").GetProperty("retreatDistance").GetSingle(),
                    enemyBehavior.GetProperty("maneuvers").GetProperty("strafeDistance").GetSingle(),
                    enemyBehavior.GetProperty("maneuvers").GetProperty("strafeWindowMultiplier").GetSingle(),
                    enemyBehavior.GetProperty("maneuvers").GetProperty("strafePhaseSteps").GetUInt64(),
                    enemyBehavior.GetProperty("maneuvers").GetProperty("strafeChancePeriod").GetUInt64()),
            },
            new DaggerfallLootInteractionTuning(
                lootInteraction.GetProperty("maximumDistance").GetDouble(),
                lootInteraction.GetProperty("minimumFacingCosine").GetDouble()),
            new DaggerfallTimeTuning(time.GetProperty("gameSecondsPerRealSecond").GetDouble()),
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
                BeastMinimumDelaySeconds = presentationAudio.GetProperty("beastMinimumDelaySeconds").GetInt32(),
                BeastMaximumDelaySeconds = presentationAudio.GetProperty("beastMaximumDelaySeconds").GetInt32(),
                BeastAttackChancePercent = presentationAudio.GetProperty("beastAttackChancePercent").GetInt32(),
                VampireAttackChancePercent = presentationAudio.GetProperty("vampireAttackChancePercent").GetInt32(),
                VampireBarkChancePercent = presentationAudio.GetProperty("vampireBarkChancePercent").GetInt32(),
                BeastBarkChancePercent = presentationAudio.GetProperty("beastBarkChancePercent").GetInt32(),
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
            QuestOffers = new(root.GetProperty("questOffers").GetProperty("socialProviderChancePercent").GetInt32(), root.GetProperty("questOffers").GetProperty("castleProviderChancePercent").GetInt32()),
            QuestRewards = new(root.GetProperty("questRewards").GetProperty("minimumDeliveryHour").GetInt32(),
                root.GetProperty("questRewards").GetProperty("maximumDeliveryHour").GetInt32(),
                root.GetProperty("questRewards").GetProperty("minimumDelaySeconds").GetInt32(),
                root.GetProperty("questRewards").GetProperty("maximumDelaySeconds").GetInt32()),
            QuestSpawning = new(root.GetProperty("questSpawning").GetProperty("minimumFoeDistance").GetSingle(),
                root.GetProperty("questSpawning").GetProperty("maximumFoeDistance").GetSingle(),
                root.GetProperty("questSpawning").GetProperty("minimumWildernessDistance").GetSingle(),
                root.GetProperty("questSpawning").GetProperty("maximumWildernessDistance").GetSingle(),
                root.GetProperty("questSpawning").GetProperty("maximumActiveGuards").GetInt32(),
                root.GetProperty("questSpawning").GetProperty("guardWitnessDistance").GetSingle(),
                root.GetProperty("questSpawning").GetProperty("guardWitnessAngleDegrees").GetSingle(),
                root.GetProperty("questSpawning").GetProperty("guardConversionAngleDegrees").GetSingle(),
                root.GetProperty("questSpawning").GetProperty("guardConversionChance").GetInt32()),
            Law = new(root.GetProperty("law").GetProperty("minimumResponseSeconds").GetInt32(),
                root.GetProperty("law").GetProperty("maximumResponseSeconds").GetInt32(),
                root.GetProperty("law").GetProperty("minimumGuards").GetInt32(), root.GetProperty("law").GetProperty("maximumGuards").GetInt32(),
                root.GetProperty("law").GetProperty("minimumArrivalDistance").GetSingle(), root.GetProperty("law").GetProperty("maximumArrivalDistance").GetSingle(),
                root.GetProperty("law").GetProperty("arrestReach").GetDouble()),
            NormalLight = new(root.GetProperty("normalLight").GetProperty("distance").GetSingle(),
                root.GetProperty("normalLight").GetProperty("heightFraction").GetSingle(),
                root.GetProperty("normalLight").GetProperty("range").GetSingle(), root.GetProperty("normalLight").GetProperty("intensity").GetSingle()),
            Vampirism = new(root.GetProperty("vampirism").GetProperty("attributeBonus").GetInt32(),
                root.GetProperty("vampirism").GetProperty("skillBonus").GetInt32(),
                root.GetProperty("vampirism").GetProperty("satiationMinutes").GetInt32()),
            Lycanthropy = new(root.GetProperty("lycanthropy").GetProperty("attributeBonus").GetInt32(),
                root.GetProperty("lycanthropy").GetProperty("skillBonus").GetInt32(),
                root.GetProperty("lycanthropy").GetProperty("morphCooldownMinutes").GetInt32(),
                root.GetProperty("lycanthropy").GetProperty("hungerPeriodMinutes").GetInt32(),
                root.GetProperty("lycanthropy").GetProperty("healthLossPerMinute").GetDouble(),
                root.GetProperty("lycanthropy").GetProperty("minimumHealth").GetInt32()),
            Detection = new(root.GetProperty("detection").GetProperty("maximumDistance").GetDouble()),
            Music = new DaggerfallMusicTuning(root.GetProperty("music").GetProperty("alternatePlaylists").GetBoolean()),
            WorldOrigin = new(root.GetProperty("worldOrigin").GetProperty("verticalRebaseDistance").GetSingle()),
            CivilianWander = new(root.GetProperty("civilianWander").GetProperty("movementSpeedUnitsPerSecond").GetSingle(),
                root.GetProperty("civilianWander").GetProperty("waypointDistance").GetSingle(),
                root.GetProperty("civilianWander").GetProperty("idleDurationSeconds").GetSingle(),
                root.GetProperty("civilianWander").GetProperty("navigationMaximumVisited").GetUInt32(),
                root.GetProperty("civilianWander").GetProperty("recycleDistanceMeters").GetSingle()),
            Swimming = root.TryGetProperty("swimming", out JsonElement swimming)
                ? ReadSwimming(swimming)
                : DaggerfallSwimmingTuning.Classic,
            Weather = DaggerfallWeatherTuning.Read(root.GetProperty("weather")),
            Ambient = DaggerfallAmbientTuning.Read(root.GetProperty("ambient")),
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
    /// <summary>EnemyMotor's close-range retreat and strafe decisions, which the Kit pursuit policy applies.</summary>
    internal DaggerfallPursuitManeuverTuning Maneuvers { get; init; } = DaggerfallPursuitManeuverTuning.Classic;

    internal DaggerfallEnemyBehaviorTuning Validate()
    {
        Maneuvers.Validate();
        if (!double.IsFinite(DetectionDistance) || DetectionDistance <= 0d) throw new ArgumentOutOfRangeException(nameof(DetectionDistance));
        if (!float.IsFinite(ChaseSpeedUnitsPerSecond) || ChaseSpeedUnitsPerSecond <= 0f) throw new ArgumentOutOfRangeException(nameof(ChaseSpeedUnitsPerSecond));
        if (!float.IsFinite(SpawnGroundProbeLift) || SpawnGroundProbeLift < 0f) throw new ArgumentOutOfRangeException(nameof(SpawnGroundProbeLift));
        if (!double.IsFinite(SpawnGroundProbeDistance) || SpawnGroundProbeDistance <= SpawnGroundProbeLift) throw new ArgumentOutOfRangeException(nameof(SpawnGroundProbeDistance));
        if (NavigationMaximumVisited == 0) throw new ArgumentOutOfRangeException(nameof(NavigationMaximumVisited));
        return this;
    }
}

/// <summary>
/// The enemy's close-range maneuvers: retreat inside reach plus <paramref name="RetreatDistance"/>,
/// and a strafe offered within reach plus <paramref name="StrafeDistance"/> times
/// <paramref name="StrafeWindowMultiplier"/>, chosen on one phase in <paramref name="StrafeChancePeriod"/>
/// of <paramref name="StrafePhaseSteps"/> simulation steps (EnemyMotor's one-in-four strafe decision).
/// </summary>
internal sealed record DaggerfallPursuitManeuverTuning(float RetreatDistance, float StrafeDistance,
    float StrafeWindowMultiplier, ulong StrafePhaseSteps, ulong StrafeChancePeriod)
{
    internal static DaggerfallPursuitManeuverTuning Classic { get; } = new(2.5f, 1.5f, 4f, 15, 4);

    internal DaggerfallPursuitManeuverTuning Validate()
    {
        if (!float.IsFinite(RetreatDistance) || RetreatDistance <= 0f) throw new ArgumentOutOfRangeException(nameof(RetreatDistance));
        if (!float.IsFinite(StrafeDistance) || StrafeDistance <= 0f) throw new ArgumentOutOfRangeException(nameof(StrafeDistance));
        if (!float.IsFinite(StrafeWindowMultiplier) || StrafeWindowMultiplier <= 0f) throw new ArgumentOutOfRangeException(nameof(StrafeWindowMultiplier));
        if (StrafePhaseSteps == 0) throw new ArgumentOutOfRangeException(nameof(StrafePhaseSteps));
        if (StrafeChancePeriod == 0) throw new ArgumentOutOfRangeException(nameof(StrafeChancePeriod));
        return this;
    }
}

/// <summary>
/// Outdoor civilians' wander movement (MobilePersonMotor's walk speed and idle distance) and the
/// distance at which a wandering civilian is recycled away from the player.
/// </summary>
internal sealed record DaggerfallCivilianWanderTuning(float MovementSpeedUnitsPerSecond, float WaypointDistance,
    float IdleDurationSeconds, uint NavigationMaximumVisited, float RecycleDistanceMeters)
{
    internal static DaggerfallCivilianWanderTuning Classic { get; } = new(1.3f, 2.5f, 2.5f, 64, 150f);

    internal DaggerfallCivilianWanderTuning Validate()
    {
        if (!float.IsFinite(MovementSpeedUnitsPerSecond) || MovementSpeedUnitsPerSecond <= 0f) throw new ArgumentOutOfRangeException(nameof(MovementSpeedUnitsPerSecond));
        if (!float.IsFinite(WaypointDistance) || WaypointDistance <= 0f) throw new ArgumentOutOfRangeException(nameof(WaypointDistance));
        if (!float.IsFinite(IdleDurationSeconds) || IdleDurationSeconds < 0f) throw new ArgumentOutOfRangeException(nameof(IdleDurationSeconds));
        if (NavigationMaximumVisited == 0) throw new ArgumentOutOfRangeException(nameof(NavigationMaximumVisited));
        if (!float.IsFinite(RecycleDistanceMeters) || RecycleDistanceMeters <= 0f) throw new ArgumentOutOfRangeException(nameof(RecycleDistanceMeters));
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
    internal int BeastMinimumDelaySeconds { get; init; } = 4;
    internal int BeastMaximumDelaySeconds { get; init; } = 20;
    internal int BeastAttackChancePercent { get; init; } = 10;
    internal int VampireAttackChancePercent { get; init; } = 20;
    internal int VampireBarkChancePercent { get; init; } = 20;
    internal int BeastBarkChancePercent { get; init; } = 20;
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
        if (BeastMinimumDelaySeconds < 0 || BeastMaximumDelaySeconds < BeastMinimumDelaySeconds
            || VampireAttackChancePercent is < 0 or > 100 || VampireBarkChancePercent is < 0 or > 100 || BeastAttackChancePercent is < 0 or > 100 || BeastBarkChancePercent is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(BeastMaximumDelaySeconds));
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

internal sealed record DaggerfallNormalLightTuning(float Distance, float HeightFraction, float Range, float Intensity)
{
    internal DaggerfallNormalLightTuning Validate() => float.IsFinite(Distance) && Distance > 0
        && float.IsFinite(HeightFraction) && HeightFraction is >= 0 and <= 1 && float.IsFinite(Range) && Range > 0
        && float.IsFinite(Intensity) && Intensity > 0 ? this : throw new ArgumentException("Normal light tuning requires a finite pose, range and intensity.");
}

internal sealed record DaggerfallLawTuning(int MinimumResponseSeconds, int MaximumResponseSeconds,
    int MinimumGuards, int MaximumGuards, float MinimumArrivalDistance, float MaximumArrivalDistance, double ArrestReach)
{
    internal DaggerfallLawTuning Validate() => MinimumResponseSeconds >= 0 && MaximumResponseSeconds >= MinimumResponseSeconds
        && MinimumGuards > 0 && MaximumGuards >= MinimumGuards && MaximumGuards <= 64
        && float.IsFinite(MinimumArrivalDistance) && MinimumArrivalDistance > 0 && float.IsFinite(MaximumArrivalDistance)
        && MaximumArrivalDistance >= MinimumArrivalDistance && double.IsFinite(ArrestReach) && ArrestReach > 0
        ? this : throw new ArgumentException("Law tuning requires ordered finite response delays, guard counts and arrival distances.");
}

internal sealed record DaggerfallQuestSpawningTuning(float MinimumFoeDistance, float MaximumFoeDistance,
    float MinimumWildernessDistance, float MaximumWildernessDistance, int MaximumActiveGuards,
    float GuardWitnessDistance, float GuardWitnessAngleDegrees, float GuardConversionAngleDegrees, int GuardConversionChance)
{
    internal DaggerfallQuestSpawningTuning Validate() => float.IsFinite(MinimumFoeDistance) && MinimumFoeDistance > 0
        && float.IsFinite(MaximumFoeDistance) && MaximumFoeDistance >= MinimumFoeDistance
        && float.IsFinite(MinimumWildernessDistance) && MinimumWildernessDistance > 0
        && float.IsFinite(MaximumWildernessDistance) && MaximumWildernessDistance >= MinimumWildernessDistance
        && MaximumActiveGuards >= 0 && float.IsFinite(GuardWitnessDistance) && GuardWitnessDistance > 0
        && float.IsFinite(GuardWitnessAngleDegrees) && GuardWitnessAngleDegrees is >= 0 and <= 180
        && float.IsFinite(GuardConversionAngleDegrees) && GuardConversionAngleDegrees is >= 0 and <= 180
        && GuardConversionChance is >= 0 and <= 100 ? this : throw new ArgumentException("Quest spawn tuning has invalid ranges, angles, count, or chance.");
}

internal sealed record DaggerfallQuestRewardTuning(int MinimumDeliveryHour, int MaximumDeliveryHour, int MinimumDelaySeconds, int MaximumDelaySeconds)
{
    internal DaggerfallQuestRewardTuning Validate() => MinimumDeliveryHour is >= 0 and <= 23 && MaximumDeliveryHour >= MinimumDeliveryHour
        && MaximumDeliveryHour <= 23 && MinimumDelaySeconds >= 0 && MaximumDelaySeconds >= MinimumDelaySeconds && MaximumDelaySeconds <= int.MaxValue / 10
        ? this : throw new ArgumentException("Quest reward delivery tuning requires ordered hours and bounded nonnegative delays.");
}

internal sealed record DaggerfallQuestOfferTuning(int SocialProviderChancePercent, int CastleProviderChancePercent)
{
    internal DaggerfallQuestOfferTuning Validate() => SocialProviderChancePercent is >= 0 and <= 100 && CastleProviderChancePercent is >= 0 and <= 100
        ? this : throw new ArgumentException("Quest provider chances must be percentages.");
}
