using WorldRpg.Kit.Controls;

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
