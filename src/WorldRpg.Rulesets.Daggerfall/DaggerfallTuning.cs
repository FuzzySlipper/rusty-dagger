using WorldRpg.Kit.Controls;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed record DaggerfallTuning(PlayerControlTuning PlayerControl, SpatialTuning Spatial)
{
    internal static DaggerfallTuning Defaults { get; } = new(PlayerControlTuning.Defaults, SpatialTuning.Defaults);
    internal DaggerfallTuning Validate() => this with { PlayerControl = PlayerControl.Validate(), Spatial = Spatial.Validate() };
}
