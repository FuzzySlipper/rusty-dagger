using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Targeting;
using Rusty.Engine;
using System.Numerics;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.Modules.Behavior;
using WorldRpg.Rulesets.Daggerfall.Modules.Loot;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Facts;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.Presentation;
using WorldRpg.Kit.Progression;
using WorldRpg.Kit.World;
using KitEquipmentSlotId = WorldRpg.Kit.Inventory.EquipmentSlotId;
using KitUniqueInventoryItem = WorldRpg.Kit.Inventory.UniqueInventoryItem;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Explicit current-state capture and restoration; native presentation and input are reconstructed by composition.</summary>
internal sealed class DaggerSessionPersistence
{
    private readonly DaggerfallState State;
    private readonly DaggerfallCorpseLootModule _corpseLoot;
    private readonly DaggerfallUniqueItemAllocator _uniqueItems;
    private readonly FirstPersonCameraSystem _camera;
    private readonly DaggerfallWorldTime _time;
    private readonly DaggerfallSiteContext _site;
    internal DaggerSessionPersistence(DaggerfallState state, DaggerfallCorpseLootModule corpses,
        DaggerfallUniqueItemAllocator uniqueItems, FirstPersonCameraSystem camera, DaggerfallWorldTime time, DaggerfallSiteContext site)
    {
        State = state; _corpseLoot = corpses; _uniqueItems = uniqueItems; _camera = camera; _time = time; _site = site;
    }
    internal RulesetSavePayload Capture(ulong? generation, ulong? step)
    {
        PlayerControlState control = State.PlayerControl;
        WorldPoint playerPosition = control.Position
            ?? throw new InvalidOperationException("Daggerfall cannot save without a player position.");
        DaggerfallPlayerSave player = new(
            playerPosition.X, playerPosition.Y, playerPosition.Z,
            control.YawRadians, control.PitchRadians,
            DaggerfallStatsSaveBoundary.Capture(State.Actors.Player.Stats, State.Actors.Player.Actor.Entity));
        DaggerfallActorSave[] actors = State.Actors.All
            .OrderBy(actor => actor.DurableId)
            .Select(actor => new DaggerfallActorSave(
                actor.DurableId,
                actor.Position.X, actor.Position.Y, actor.Position.Z, actor.HeadingYawRadians,
                DaggerfallStatsSaveBoundary.Capture(actor.Stats, actor.Actor.Entity)))
            .ToArray();
        DaggerfallInventorySave inventorySave = CaptureInventory(State.Inventory, State.Equipment);
        DaggerfallCorpseSave[] corpses = _corpseLoot.Corpses.Values.OrderBy(corpse => corpse.ActorId).Select(corpse =>
        {
            InventoryView? contents = corpse.IsRegistered ? State.Containers.Read(corpse.Owner) : null;
            return new DaggerfallCorpseSave(
                corpse.ActorId,
                corpse.OriginatingSequence,
                corpse.IsRegistered,
                corpse.IsInteractable,
                contents?.Stacks.OrderBy(stack => stack.Definition.Value, StringComparer.Ordinal)
                    .Select(stack => new DaggerfallStackSave(stack.Definition.Value, stack.Quantity)).ToArray() ?? [],
                contents?.UniqueItems.OrderBy(item => item.Entity.Value)
                    .Select(item => new DaggerfallUniqueSave(item.Definition.Value, State.Actors.Entities.IdentityOf(item.Entity).Value)).ToArray() ?? []);
        }).ToArray();
        DaggerfallActorInventorySave[] actorInventories = State.ActorInventories.OrderBy(entry => entry.Key)
            .Select(entry => new DaggerfallActorInventorySave(entry.Key, CaptureInventory(entry.Value, State.EquipmentFor(entry.Key)))).ToArray();
        return DaggerfallSavePayload.Encode(new DaggerfallSavePayload(
            player,
            actors,
            State.Progression.Experience,
            State.Progression.Level,
            inventorySave,
            corpses,
            _uniqueItems.CaptureState(),
            State.Kit.AttackExecution.CaptureCooldowns(generation, step)
                .Select(value => new DaggerfallCombatCooldownSave(value.AttackerId, value.RemainingSteps)).ToArray(),
            new DaggerfallCalendarSave(_time.Calendar.Year, _time.Calendar.Month, _time.Calendar.Day, _time.Calendar.Hour, _time.Calendar.Minute, _time.Calendar.Second, _time.RemainderSeconds),
            _site.Capture(),
            actorInventories));
    }

    internal void Restore(DaggerfallSavePayload saved)
    {
        // Current-state relationships were resolved before this fresh session was constructed.
        DaggerfallActorSave[] actors = saved.Actors.OrderBy(actor => actor.EntityId).ToArray();
        foreach (DaggerfallActorSave actor in actors)
        {
            if (!State.Actors.TryGet(actor.EntityId, out ActorState? current))
                throw new ArgumentException($"The saved Daggerfall actor '{actor.EntityId}' is not present in the selected content.", nameof(saved));
            current.ApplyPose(new ActorPose(new WorldPoint(actor.X, actor.Y, actor.Z), actor.HeadingRadians));
        }

        State.Progression.AdvanceTo(saved.Experience, saved.Level);

        ApplyInventory(saved.Inventory, State.Inventory, State.Equipment);
        ApplyActorInventories(saved.ActorInventories);

        WorldPoint position = new(saved.Player.X, saved.Player.Y, saved.Player.Z);
        State.PlayerControl.YawRadians = saved.Player.YawRadians;
        State.PlayerControl.PitchRadians = saved.Player.PitchRadians;
        State.PlayerControl.Restore(position, default);
        // Native continuation and held input start fresh; durable pose and corpse relationships are restored.
        _corpseLoot.Restore(saved.Corpses);
        State.Kit.AttackExecution.RestoreCooldowns(saved.CombatCooldowns.Select(value => new AttackCooldown(value.AttackerId, value.RemainingSteps)));
        _camera.Update(State.PlayerControl);
        // Enemy behavior, perception leases, held input, pending loot, facts,
        // presentation effects and a swing still waiting for its damage frame are
        // intentionally transient.  The next admitted step observes rebuilt actor
        // state without replaying them; a swing cut off by a save keeps the cooldown
        // it already charged but never lands.
    }

    private DaggerfallInventorySave CaptureInventory(MechanicsInventoryCoordinator inventoryOwner, MechanicsEquipmentCoordinator equipmentOwner)
    {
        InventoryView inventory = inventoryOwner.Read();
        EquipmentRead equipped = equipmentOwner.Read();
        return new(
            inventory.Stacks.OrderBy(stack => stack.Definition.Value, StringComparer.Ordinal)
                .Select(stack => new DaggerfallStackSave(stack.Definition.Value, stack.Quantity)).ToArray(),
            inventory.UniqueItems.OrderBy(item => item.Entity.Value)
                .Select(item => new DaggerfallUniqueSave(item.Definition.Value, State.Actors.Entities.IdentityOf(item.Entity).Value)).ToArray(),
            equipped.Assignments.OrderBy(assignment => assignment.Slot.Value, StringComparer.Ordinal)
                .Select(assignment => new DaggerfallEquipmentSave(assignment.Slot.Value, State.Actors.Entities.IdentityOf(new EntityId(assignment.Item.EntityId)).Value)).ToArray());
    }

    private static void ApplyInventory(DaggerfallInventorySave saved, MechanicsInventoryCoordinator inventory, MechanicsEquipmentCoordinator equipment)
    {
        foreach (DaggerfallStackSave stack in saved.Stacks)
        {
            inventory.Grant(new InventoryGrant(new InventoryItemId(stack.ItemId), stack.Quantity));
        }
        Dictionary<ulong, KitUniqueInventoryItem> unique = [];
        foreach (DaggerfallUniqueSave item in saved.UniqueItems)
        {
            unique.Add(item.EntityId, equipment.Materialize(new DurableIdentityReference(DurableIdentityKind.Item, item.EntityId), new InventoryItemId(item.ItemId)));
        }
        foreach (IGrouping<ulong, DaggerfallEquipmentSave> group in saved.Equipment.GroupBy(value => value.ItemEntityId))
        {
            equipment.Equip(unique[group.Key], group.Select(value => new KitEquipmentSlotId(value.SlotId)).ToArray());
        }
    }

    private void ApplyActorInventories(DaggerfallActorInventorySave[] saved)
    {
        foreach (DaggerfallActorInventorySave section in saved)
        {
            MechanicsInventoryCoordinator inventory = State.InventoryFor(section.EntityId)
                ?? throw new ArgumentException($"Saved inventory owner {section.EntityId} is missing.");
            ApplyInventory(section.Inventory, inventory, State.EquipmentFor(section.EntityId));
        }
    }
}
