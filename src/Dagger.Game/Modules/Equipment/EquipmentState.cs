using RustyDagger.Game.Modules.Combat;

namespace RustyDagger.Game.Modules.Equipment;

internal sealed class EquipmentState(WeaponDefinition rightHand)
{
    internal WeaponDefinition RightHand { get; } = rightHand;
}
