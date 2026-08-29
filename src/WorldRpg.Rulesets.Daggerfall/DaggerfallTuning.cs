using WorldRpg.Kit.Controls;
using System.Text.Json;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed record DaggerfallTuning(PlayerControlTuning PlayerControl, SpatialTuning Spatial, PlayerInitialLook InitialPlayerLook)
{
    internal static DaggerfallTuning Defaults { get; } = new(
        new PlayerControlTuning(.0035f, -1.5533f, 1.5533f, .35f, InvertHorizontal: false, InvertVertical: false, WrapYaw: true),
        new SpatialTuning(.5, 32, .5, 32, 2),
        new PlayerInitialLook(MathF.PI, 0f));

    internal DaggerfallTuning Validate() => this with
    {
        PlayerControl = PlayerControl.Validate(),
        Spatial = Spatial.Validate(),
        InitialPlayerLook = InitialPlayerLook.Validate(),
    };

    internal static DaggerfallTuning Read(ReadOnlySpan<byte> payload)
    {
        using JsonDocument document = JsonDocument.Parse(payload.ToArray());
        JsonElement root = document.RootElement;
        JsonElement controls = root.GetProperty("playerControl");
        JsonElement spatial = root.GetProperty("spatial");
        JsonElement look = root.GetProperty("initialPlayerLook");
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
                spatial.GetProperty("navigationCellSize").GetDouble(),
                checked((uint)spatial.GetProperty("navigationChunkSize").GetInt32()),
                checked((uint)spatial.GetProperty("navigationMaximumStepCells").GetInt32())),
            new PlayerInitialLook(look.GetProperty("yawRadians").GetSingle(), look.GetProperty("pitchRadians").GetSingle()))
            .Validate();
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
