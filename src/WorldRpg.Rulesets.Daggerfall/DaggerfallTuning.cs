using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.Modules.PlayerControl;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed record DaggerfallTuning(PlayerControlTuning PlayerControl, SpatialTuning Spatial, CombatTuning Combat)
{
    internal static DaggerfallTuning Defaults { get; } = new(PlayerControlTuning.Defaults, SpatialTuning.Defaults, new CombatTuning(PlayerAttackCost: 5, PlayerAttackCooldownSeconds: .75f, PlayerMeleeReach: 2.25f));
    internal DaggerfallTuning Validate() => this with { PlayerControl = PlayerControl.Validate(), Spatial = Spatial.Validate(), Combat = Combat.Validate() };
}
