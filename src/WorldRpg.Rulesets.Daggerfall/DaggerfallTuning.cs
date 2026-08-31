using WorldRpg.Kit.Controls;
using System.Text.Json;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed record DaggerfallTuning(
    PlayerControlTuning PlayerControl,
    SpatialTuning Spatial,
    FirstPersonCameraTuning Camera,
    DaggerfallMeleeTargetingTuning MeleeTargeting)
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
        new DaggerfallMeleeTargetingTuning(2.25d, .5d));

    internal DaggerfallTuning Validate() => this with
    {
        PlayerControl = PlayerControl.Validate(),
        Spatial = Spatial.Validate(),
        Camera = Camera.Validate(),
        MeleeTargeting = MeleeTargeting.Validate(),
    };

    internal static DaggerfallTuning Read(ReadOnlySpan<byte> payload)
    {
        using JsonDocument document = JsonDocument.Parse(payload.ToArray());
        JsonElement root = document.RootElement;
        JsonElement controls = root.GetProperty("playerControl");
        JsonElement spatial = root.GetProperty("spatial");
        JsonElement camera = root.GetProperty("camera");
        JsonElement meleeTargeting = root.GetProperty("meleeTargeting");
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
                meleeTargeting.GetProperty("minimumFacingCosine").GetDouble()))
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

internal readonly record struct PlayerInitialLook(float YawRadians, float PitchRadians)
{
    internal PlayerInitialLook Validate()
    {
        if (!float.IsFinite(YawRadians)) throw new ArgumentOutOfRangeException(nameof(YawRadians));
        if (!float.IsFinite(PitchRadians)) throw new ArgumentOutOfRangeException(nameof(PitchRadians));
        return this;
    }
}
