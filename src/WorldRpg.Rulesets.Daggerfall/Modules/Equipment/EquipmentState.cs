using WorldRpg.Rulesets.Daggerfall.Modules.Combat;

namespace WorldRpg.Rulesets.Daggerfall.Modules.Equipment;

internal sealed class EquipmentState(WeaponDefinition rightHand)
{
    internal WeaponDefinition RightHand { get; } = rightHand;
}
