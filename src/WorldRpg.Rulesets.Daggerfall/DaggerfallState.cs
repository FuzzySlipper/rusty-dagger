using WorldRpg.Rulesets.Daggerfall.Modules.Actors;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.Modules.Encounters;
using WorldRpg.Rulesets.Daggerfall.Modules.Equipment;
using WorldRpg.Rulesets.Daggerfall.Modules.Inventory;
using WorldRpg.Rulesets.Daggerfall.Modules.PlayerControl;
using WorldRpg.Rulesets.Daggerfall.Modules.Progression;

namespace WorldRpg.Rulesets.Daggerfall;

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
