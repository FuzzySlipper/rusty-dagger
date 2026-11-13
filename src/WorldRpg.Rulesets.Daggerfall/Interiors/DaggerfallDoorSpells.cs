using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// A door the player activates while an Open or Lock spell waits on them: the spell opens or locks it instead of the
/// hand, at the player's level, and is spent. Door operation, so Interiors', over Magic's effects.
/// </summary>
internal sealed class DaggerfallDoorSpells(DaggerfallEffectLifecycle effects, DaggerfallItemInstances instances, Func<int> playerLevel)
{
    /// <summary>Returns null only when ordinary activation should own this door.</summary>
    internal DaggerfallActivationOutcome? Activate(DaggerfallDoorRuntime doors, DaggerfallRdbDoorId id)
    {
        var ready = effects.Active.Where(effect => effect.Context.Target.Value == DaggerfallActorIdentity.PlayerEntityId
                && effect.Definition.DoorMagic != DaggerfallDoorMagic.None
                && !(doors.ExteriorBuildingOf(id) is not null && effect.Definition.DoorMagic == DaggerfallDoorMagic.Lock))
            .OrderBy(effect => effect.Definition.DoorMagic).FirstOrDefault(); // Lock precedes Open, regardless of cast order.
        if (ready is null) return null;
        DaggerfallDoorView before = doors.Read(id);
        int level = playerLevel();
        DaggerfallDoorOperationResult result;
        bool applied;
        string message;
        if (ready.Definition.DoorMagic == DaggerfallDoorMagic.Lock)
        {
            result = doors.Lock(id, DaggerfallDoorOperationSource.Spell, level);
            applied = result == DaggerfallDoorOperationResult.Started;
            if (result != DaggerfallDoorOperationResult.SpecialDoor
                && before.Motion == DaggerfallDoorMotion.Open)
                applied |= doors.Close(id, DaggerfallDoorOperationSource.Spell) == DaggerfallDoorOperationResult.Started;
            message = result switch
            {
                DaggerfallDoorOperationResult.Started => "The door is locked by your spell.",
                DaggerfallDoorOperationResult.AlreadyLocked => "The door is already locked.",
                _ => "This door only responds to its mechanism.",
            };
        }
        else
        {
            bool skeletonKey = ready.Context.Item is { } item && instances.ContainsUnique(item.Value)
                && instances.RequireUnique(item.Value).Enchantment == DaggerfallMagicItemIds.SkeletonKey;
            result = doors.OpenByMagic(id, level, skeletonKey);
            applied = result == DaggerfallDoorOperationResult.Started;
            message = result switch
            {
                DaggerfallDoorOperationResult.Started => "The spell opens the door.",
                DaggerfallDoorOperationResult.AlreadyOpen => "The door is already open.",
                DaggerfallDoorOperationResult.SpecialDoor => "This door only responds to its mechanism.",
                _ => "The lock is too strong for your Open spell.",
            };
        }
        effects.Cancel(ready.Context.Instance);
        return new(applied, message);
    }
}
