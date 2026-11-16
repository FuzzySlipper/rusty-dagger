using System.Numerics;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
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
    internal DaggerfallViewmodelTuning Viewmodel { get; init; } = DaggerfallViewmodelTuning.Classic;
    internal DaggerfallWorldOriginTuning WorldOrigin { get; init; } = new(500f);
    internal DaggerfallCivilianWanderTuning CivilianWander { get; init; } = DaggerfallCivilianWanderTuning.Classic;
    internal DaggerfallSwimmingTuning Swimming { get; init; } = DaggerfallSwimmingTuning.Classic;
    internal DaggerfallWeatherTuning Weather { get; init; } = DaggerfallWeatherTuning.Classic;
    internal DaggerfallAmbientTuning Ambient { get; init; } = DaggerfallAmbientTuning.Classic;
    internal DaggerfallHorizonTuning Horizon { get; init; } = DaggerfallHorizonTuning.Default;
    internal DaggerfallGroundTuning Ground { get; init; } = DaggerfallGroundTuning.Default;
    internal DaggerfallFlatsTuning Flats { get; init; } = DaggerfallFlatsTuning.Default;
    internal DaggerfallHangingTuning Hanging { get; init; } = DaggerfallHangingTuning.Default;
    /// <summary>What a site's projection and the exterior world read of these.</summary>
    internal DaggerfallSiteTuning Site => new(PresentationAudio, Flats, SiteLighting, WorldOrigin, Ground);
    internal DaggerfallFloraTuning Flora { get; init; } = DaggerfallFloraTuning.Default;
    internal DaggerfallGroundCoverTuning GroundCover { get; init; } = DaggerfallGroundCoverTuning.Default;
    internal DaggerfallSurfaceReliefTuning SurfaceRelief { get; init; } = DaggerfallSurfaceReliefTuning.Default;
    internal DaggerfallWaterTuning Water { get; init; } = DaggerfallWaterTuning.Default;
    internal DaggerfallContactTuning Contact { get; init; } = DaggerfallContactTuning.Default;
    internal DaggerfallAttackTuning Attacks { get; init; } = DaggerfallAttackTuning.Default;
    internal DaggerfallHazardTuning Hazards { get; init; } = DaggerfallHazardTuning.Default;
    internal DaggerfallRestTuning Rest { get; init; } = DaggerfallRestTuning.Default;

    internal static DaggerfallTuning Defaults { get; } = new(
        DaggerfallInput.Look,
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
        new DaggerfallMeleeTargetingTuning(2.25d, .5d),
        new DaggerfallEnemyBehaviorTuning(12d, 3f, 1024),
        new DaggerfallLootInteractionTuning(2.25d, .5d),
        new DaggerfallTimeTuning(12d),
        DaggerfallPresentationAudioTuning.Classic,
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
            WagonAccessRange: 5f,
            HorseRide: new(WalkShare: .2f, TrotShare: .6f, Acceleration: 3f, Coasting: 2.5f, Braking: 9f, ReverseSpeed: 1.2f,
                StandingTurnDegrees: 150f, FullTurnDegrees: 65f, LookRangeDegrees: 40f, UphillDrag: .6f, WaterShare: .5f),
            CartRide: new(WalkShare: .2f, TrotShare: .6f, Acceleration: 2f, Coasting: 1.8f, Braking: 6f, ReverseSpeed: .8f,
                StandingTurnDegrees: 60f, FullTurnDegrees: 40f, LookRangeDegrees: 50f, UphillDrag: .8f, WaterShare: .4f),
            GallopFatigueReserve: .1d));

    /// <summary>
    /// Reads a tuning profile. Each section's reader returns it validated, so reading a section is checking it. A new
    /// section is its record, with a Read that ends in its Validate, and its line here; no second list of sections to
    /// validate is kept beside this one, where it could leave a section out.
    /// </summary>
    internal static DaggerfallTuning Read(ReadOnlySpan<byte> payload)
    {
        using JsonDocument document = JsonDocument.Parse(payload.ToArray());
        JsonElement root = document.RootElement;
        return new DaggerfallTuning(
            ReadPlayerControl(root.GetProperty("playerControl")),
            ReadControllerInput(root.GetProperty("controllerInput")),
            ReadSpatial(root.GetProperty("spatial")),
            DaggerfallLocomotionTuning.Read(root.GetProperty("locomotion")),
            ReadCamera(root.GetProperty("camera")),
            DaggerfallMeleeTargetingTuning.Read(root.GetProperty("meleeTargeting")),
            DaggerfallEnemyBehaviorTuning.Read(root.GetProperty("enemyBehavior")),
            DaggerfallLootInteractionTuning.Read(root.GetProperty("lootInteraction")),
            DaggerfallTimeTuning.Read(root.GetProperty("time")),
            DaggerfallPresentationAudioTuning.Read(root.GetProperty("presentationAudio")),
            DaggerfallProgressionTuning.Read(root.GetProperty("progression")),
            DaggerfallSiteLightingTuning.Read(root.GetProperty("siteLighting")),
            DaggerfallClimbingTuning.Read(root.GetProperty("climbing")),
            DaggerfallPropertyTuning.Read(root.GetProperty("property")),
            DaggerfallTransportTuning.Read(root.GetProperty("transport")))
        {
            StrikeEnchantments = DaggerfallStrikeEnchantmentTuning.Read(root.GetProperty("strikeEnchantments")),
            QuestOffers = DaggerfallQuestOfferTuning.Read(root.GetProperty("questOffers")),
            QuestRewards = DaggerfallQuestRewardTuning.Read(root.GetProperty("questRewards")),
            QuestSpawning = DaggerfallQuestSpawningTuning.Read(root.GetProperty("questSpawning")),
            Law = DaggerfallLawTuning.Read(root.GetProperty("law")),
            NormalLight = DaggerfallNormalLightTuning.Read(root.GetProperty("normalLight")),
            Vampirism = DaggerfallVampirismTuning.Read(root.GetProperty("vampirism")),
            Lycanthropy = DaggerfallLycanthropyTuning.Read(root.GetProperty("lycanthropy")),
            Detection = DaggerfallDetectionTuning.Read(root.GetProperty("detection")),
            Music = DaggerfallMusicTuning.Read(root.GetProperty("music")),
            Viewmodel = DaggerfallViewmodelTuning.Read(root.GetProperty("viewmodel")),
            WorldOrigin = DaggerfallWorldOriginTuning.Read(root.GetProperty("worldOrigin")),
            CivilianWander = DaggerfallCivilianWanderTuning.Read(root.GetProperty("civilianWander")),
            Swimming = root.TryGetProperty("swimming", out JsonElement swimming)
                ? DaggerfallSwimmingTuning.Read(swimming)
                : DaggerfallSwimmingTuning.Classic,
            Weather = DaggerfallWeatherTuning.Read(root.GetProperty("weather")),
            Ambient = DaggerfallAmbientTuning.Read(root.GetProperty("ambient")),
            Horizon = DaggerfallHorizonTuning.Read(root.GetProperty("horizon")),
            Ground = DaggerfallGroundTuning.Read(root.GetProperty("ground")),
            Flats = DaggerfallFlatsTuning.Read(root.GetProperty("flats")),
            Hanging = DaggerfallHangingTuning.Read(root.GetProperty("hanging")),
            Flora = DaggerfallFloraTuning.Read(root.GetProperty("flora")),
            GroundCover = DaggerfallGroundCoverTuning.Read(root.GetProperty("groundCover")),
            SurfaceRelief = DaggerfallSurfaceReliefTuning.Read(root.GetProperty("surfaceRelief")),
            Water = DaggerfallWaterTuning.Read(root.GetProperty("water")),
            Contact = DaggerfallContactTuning.Read(root.GetProperty("contact")),
            Attacks = DaggerfallAttackTuning.Read(root.GetProperty("attacks")),
            Hazards = DaggerfallHazardTuning.Read(root.GetProperty("hazards")),
            Rest = DaggerfallRestTuning.Read(root.GetProperty("rest")),
        };
    }

    // The Kit's records don't know this payload, so their readers sit here.
    private static PlayerControlTuning ReadPlayerControl(JsonElement controls) => new PlayerControlTuning(
        controls.GetProperty("lookSensitivity").GetSingle(),
        controls.GetProperty("pitchMinimumRadians").GetSingle(),
        controls.GetProperty("pitchMaximumRadians").GetSingle(),
        controls.GetProperty("maximumLookDeltaRadians").GetSingle(),
        controls.GetProperty("invertHorizontal").GetBoolean(),
        controls.GetProperty("invertVertical").GetBoolean(),
        controls.GetProperty("wrapYaw").GetBoolean()).Validate();

    private static SpatialTuning ReadSpatial(JsonElement spatial) => new SpatialTuning(
        spatial.GetProperty("collisionVoxelSize").GetDouble(),
        checked((uint)spatial.GetProperty("collisionChunkSize").GetInt32()),
        checked((uint)spatial.GetProperty("navigationChunkSize").GetInt32()),
        checked((uint)spatial.GetProperty("navigationMaximumStepCells").GetInt32()),
        ReadCharacterController(spatial.GetProperty("characterController"))).Validate();

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

    private static FirstPersonCameraTuning ReadCamera(JsonElement camera) => new FirstPersonCameraTuning(
        camera.GetProperty("eyeHeight").GetSingle(),
        camera.GetProperty("fieldOfViewYDegrees").GetDouble(),
        camera.GetProperty("nearPlane").GetDouble(),
        camera.GetProperty("farPlane").GetDouble()).Validate();

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

    internal static DaggerfallTimeTuning Read(JsonElement section) => new DaggerfallTimeTuning(section.GetProperty("gameSecondsPerRealSecond").GetDouble()).Validate();
}

/// <summary>Chooses the donor's standard or alternate playlists; JSON admission requires a Boolean.</summary>
internal sealed record DaggerfallMusicTuning(bool AlternatePlaylists)
{
    internal static DaggerfallMusicTuning Read(JsonElement section) => new(section.GetProperty("alternatePlaylists").GetBoolean());
}

/// <summary>
/// The first-person weapon's look. A 3D style draws the art installed beside the product
/// (<see cref="Presentation.DaggerfallHeldArt"/>) for the weapons it covers and strikes it has motion for, and the
/// classic sprites otherwise.
/// </summary>
internal sealed record DaggerfallViewmodelTuning(DaggerfallViewmodelStyle Style)
{
    internal static DaggerfallViewmodelTuning Classic { get; } = new(DaggerfallViewmodelStyle.Classic);

    internal static DaggerfallViewmodelTuning Read(JsonElement section)
    {
        string style = section.GetProperty("style").GetString() ?? "";
        return style switch
        {
            "classic" => Classic,
            "held" => new(DaggerfallViewmodelStyle.Held),
            "heldRetro" => new(DaggerfallViewmodelStyle.HeldRetro),
            _ => throw new InvalidDataException($"viewmodel.style '{style}' is not one of classic, held, heldRetro."),
        };
    }
}
