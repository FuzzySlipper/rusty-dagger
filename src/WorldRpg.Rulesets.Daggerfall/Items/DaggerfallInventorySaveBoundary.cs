using Rusty.Engine.Mechanics;
using WorldRpg.Kit.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>Current inventory values shared by save capture and resource unloading.</summary>
internal static class DaggerfallInventorySaveBoundary
{
    internal static (DaggerfallStackSave[] Stacks, DaggerfallUniqueSave[] UniqueItems) CaptureContents(
        InventoryView contents, DaggerfallItemOwner owner, DaggerfallItemInstances instances, EntityDirectory entities) => (
        contents.Stacks.OrderBy(stack => stack.Id.Value, StringComparer.Ordinal)
            .Select(stack => new DaggerfallStackSave(stack.Id.Value, stack.Definition.Value, stack.Quantity,
                instances.RequireStack(owner, stack.Id).Capture())).ToArray(),
        contents.UniqueItems.OrderBy(item => item.Entity.Value)
            .Select(item =>
            {
                ulong identity = entities.IdentityOf(item.Entity).Value;
                return new DaggerfallUniqueSave(item.Definition.Value, identity, instances.RequireUnique(identity).Capture());
            }).ToArray());
}
