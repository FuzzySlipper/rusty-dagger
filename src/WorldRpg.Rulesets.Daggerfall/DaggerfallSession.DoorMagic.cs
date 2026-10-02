using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed partial class DaggerfallSession
{
    /// <summary>Returns null only when ordinary activation should own this door.</summary>
    private DaggerfallActivationOutcome? ActivateDoorMagic(DaggerfallRdbDoorId id)
    {
        var ready = State.Effects.Active.Where(effect => effect.Context.Target.Value == DaggerfallActorIdentity.PlayerEntityId
                && effect.Definition.DoorMagic != DaggerfallDoorMagic.None)
            .OrderBy(effect => effect.Definition.DoorMagic).FirstOrDefault(); // Lock precedes Open, regardless of cast order.
        if (ready is null) return null;
        DaggerfallDoorView before = _doors.Read(id);
        int level = State.Progression.Level;
        DaggerfallDoorOperationResult result;
        bool applied;
        string message;
        if (ready.Definition.DoorMagic == DaggerfallDoorMagic.Lock)
        {
            result = _doors.Lock(id, DaggerfallDoorOperationSource.Spell, level);
            applied = result == DaggerfallDoorOperationResult.Started;
            if (result != DaggerfallDoorOperationResult.SpecialDoor
                && before.Motion == DaggerfallDoorMotion.Open)
                applied |= _doors.Close(id, DaggerfallDoorOperationSource.Spell) == DaggerfallDoorOperationResult.Started;
            message = result switch
            {
                DaggerfallDoorOperationResult.Started => "The door is locked by your spell.",
                DaggerfallDoorOperationResult.AlreadyLocked => "The door is already locked.",
                _ => "This door only responds to its mechanism.",
            };
        }
        else
        {
            bool skeletonKey = ready.Context.Item is { } item && State.ItemInstances.ContainsUnique(item.Value)
                && State.ItemInstances.RequireUnique(item.Value).Enchantment == DaggerfallMagicItemIds.SkeletonKey;
            result = _doors.OpenByMagic(id, level, skeletonKey);
            applied = result == DaggerfallDoorOperationResult.Started;
            message = result switch
            {
                DaggerfallDoorOperationResult.Started => "The spell opens the door.",
                DaggerfallDoorOperationResult.AlreadyOpen => "The door is already open.",
                DaggerfallDoorOperationResult.SpecialDoor => "This door only responds to its mechanism.",
                _ => "The lock is too strong for your Open spell.",
            };
        }
        State.Effects.Cancel(ready.Context.Instance);
        return new(applied, message);
    }
}
