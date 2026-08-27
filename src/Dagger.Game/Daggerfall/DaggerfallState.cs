using RustyDagger.Game.Modules.Actors;
using RustyDagger.Game.Modules.Combat;
using RustyDagger.Game.Modules.Encounters;
using RustyDagger.Game.Modules.Equipment;
using RustyDagger.Game.Modules.Inventory;
using RustyDagger.Game.Modules.PlayerControl;
using RustyDagger.Game.Modules.Progression;

namespace RustyDagger.Game.Daggerfall;

/// <summary>Composition and inspection aggregate; each mutable family remains owned by its module.</summary>
internal sealed class DaggerfallState(PlayerControlState playerControl, ActorsState actors, InventoryState inventory, EquipmentState equipment, CombatState combat, EncounterState encounters, ProgressionState progression)
{
    internal PlayerControlState PlayerControl { get; } = playerControl;
    internal ActorsState Actors { get; } = actors;
    internal InventoryState Inventory { get; } = inventory;
    internal EquipmentState Equipment { get; } = equipment;
    internal CombatState Combat { get; } = combat;
    internal EncounterState Encounters { get; } = encounters;
    internal ProgressionState Progression { get; } = progression;
    internal ulong UpdateSequence { get; private set; }
    internal void AdvanceSequence() => UpdateSequence++;
}
