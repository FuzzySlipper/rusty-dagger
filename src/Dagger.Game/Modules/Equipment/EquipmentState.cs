using RustyDagger.Game.Daggerfall.Content;

namespace RustyDagger.Game.Modules.Equipment;

internal sealed class EquipmentState(WeaponDefinition rightHand)
{
    internal WeaponDefinition RightHand { get; } = rightHand;
}
