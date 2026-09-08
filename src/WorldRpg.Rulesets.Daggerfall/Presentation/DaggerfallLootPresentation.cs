using System.Globalization;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Inventory;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Modules.Loot;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

internal sealed record LootPresentation(string Container, string Revision, string Title, InventoryItemPresentation[] Items, bool Empty, string Message);

/// <summary>One explicitly opened corpse. Contents and transfers remain Engine inventory facts.</summary>
internal sealed class DaggerfallLootPresentation(DaggerfallCorpseLootModule loot, DaggerfallInventoryPresentation items)
{
    private long? _actor;
    private ulong _opening;
    internal string Message { get; private set; } = "Aim at nearby loot and press F.";
    private string Token => $"{_actor}:{_opening}";

    internal LootPresentation? Read()
    {
        if (_actor is not long actor) return null;
        InventoryView? contents = loot.ReadContents(actor);
        InventoryItemPresentation[] rows = contents is null ? [] : contents.Stacks
            .Select(stack => items.DescribeItem(DaggerfallInventoryPresentation.StackKey(stack.Definition.Value), stack.Definition.Value, stack.Quantity))
            .Concat(contents.UniqueItems.Select(item => items.DescribeItem(DaggerfallInventoryPresentation.UniqueKey(item.Entity.Value), item.Definition.Value, 1)))
            .OrderBy(item => item.Key, StringComparer.Ordinal).ToArray();
        return new(Token, $"{Token}:{contents?.WorldRevision ?? 0}", DaggerfallInventoryPresentation.Label(loot.ContainerName(actor)) + " — loot",
            rows, rows.Length == 0, Message);
    }

    /// <summary>Opening never moves items. An empty search only retires its interaction marker.</summary>
    internal PendingCorpseLoot? Open(PlayerControlState player, LookReceipt look)
    {
        PendingCorpseLoot? prepared = loot.PrepareLoot(player, look);
        _actor = prepared?.Container.ActorId;
        if (prepared is null) { Message = "No eligible loot within reach. Aim at a nearby corpse."; return null; }
        _opening++;
        Message = prepared.IsEmpty ? "Empty. This container remains open until Exit." : "Choose an item to take. Stack buttons take one unit.";
        return prepared.IsEmpty ? prepared : null;
    }

    internal void Close(string? container)
    {
        if (container == Token) _actor = null;
    }

    internal PendingCorpseLoot? PrepareTake(DaggerfallPlayerUiAction action, PlayerControlState player, LookReceipt look)
    {
        LootPresentation? current = Read();
        if (current is null || action.Container != current.Container || action.Revision != current.Revision)
        { Message = "Loot changed. Choose the item again."; return null; }
        InventoryItemPresentation? item = current.Items.SingleOrDefault(item => item.Key == action.Item);
        if (item is null) { Message = "That item is no longer in this container."; return null; }
        PendingCorpseLoot? prepared = loot.PrepareLoot(player, look, _actor);
        if (prepared is null) { Message = "This loot is no longer visible within reach."; return null; }
        InventoryView contents = loot.ReadContents(_actor!.Value)!;
        ulong? unique = item.Key.StartsWith("unique:", StringComparison.Ordinal)
            ? ulong.Parse(item.Key.AsSpan("unique:".Length), CultureInfo.InvariantCulture) : null;
        // The retained loot UI takes one stack unit or one unique entity per click.
        InventoryContainerSelection selection = new(new InventoryItemId(item.Definition), 1, unique);
        return prepared with
        {
            Selection = selection,
            ExpectedWorldRevision = contents.WorldRevision,
            Facts = [new LootAwardedFact(prepared.Container.ActorId, item.Definition, 1, prepared.Container.OriginatingSequence)],
        };
    }

    internal void Complete(CorpseLootCommitResult result)
    {
        Message = result == CorpseLootCommitResult.Rejected
            ? "Cannot take that item right now. Contents have not been moved."
            : Read()?.Empty == true ? "Empty. This container remains open until Exit." : "Item taken.";
    }
}
