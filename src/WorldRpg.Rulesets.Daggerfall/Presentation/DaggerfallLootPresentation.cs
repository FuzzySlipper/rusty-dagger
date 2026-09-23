using System.Globalization;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Inventory;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Modules.Loot;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

internal sealed record LootPresentation(string Container, string Revision, string Title, InventoryItemPresentation[] Items, bool Empty, string Message);
internal sealed record PendingGroundLoot(long Id, InventoryContainerSelection Selection, ulong ExpectedWorldRevision, string Definition, ulong Quantity);

/// <summary>One explicitly opened corpse. Contents and transfers remain Engine inventory facts.</summary>
internal sealed class DaggerfallLootPresentation(DaggerfallCorpseLootModule loot, DaggerfallInventoryPresentation items, DaggerfallGroundContainers? ground = null)
{
    private long? _actor;
    private long? _ground;
    private ulong _opening;
    internal string Message { get; private set; } = "Aim at nearby loot and press F.";
    private string Token => _ground is long groundId ? $"ground:{groundId}:{_opening}" : $"{_actor}:{_opening}";

    internal LootPresentation? Read()
    {
        InventoryView? contents;
        DaggerfallItemOwner owner;
        string title;
        if (_ground is long groundId)
        {
            contents = ground?.Read(groundId);
            owner = DaggerfallItemOwner.Ground(groundId);
            title = "Dropped items — loot";
        }
        else if (_actor is long actor)
        {
            contents = loot.ReadContents(actor);
            owner = DaggerfallItemOwner.Corpse(actor);
            title = DaggerfallInventoryPresentation.Label(loot.ContainerName(actor)) + " — loot";
        }
        else return null;
        InventoryItemPresentation[] rows = contents is null ? [] : contents.Stacks
            .Select(stack => items.DescribeItem(DaggerfallInventoryPresentation.StackKey(stack.Id), stack.Definition.Value, stack.Quantity,
                owner: owner))
            .Concat(contents.UniqueItems.Select(item => items.DescribeItem(DaggerfallInventoryPresentation.UniqueKey(item.Entity.Value), item.Definition.Value, 1,
                owner: owner)))
            .OrderBy(item => item.Key, StringComparer.Ordinal).ToArray();
        return new(Token, $"{Token}:{contents?.StoreRevision ?? 0}:{items.MetadataRevision}", title,
            rows, rows.Length == 0, Message);
    }

    /// <summary>Opening never moves items. An empty search only retires its interaction marker.</summary>
    internal PendingCorpseLoot? Open(PlayerControlState player, LookReceipt look)
    {
        PendingCorpseLoot? prepared = loot.PrepareLoot(player, look);
        _actor = prepared?.ActorId;
        _ground = null;
        if (prepared is null) { Message = "No eligible loot within reach. Aim at a nearby corpse."; return null; }
        _opening++;
        Message = prepared.IsEmpty ? "Empty. This container remains open until Exit." : "Choose an item to take. Stack buttons take one unit.";
        return prepared.IsEmpty ? prepared : null;
    }

    /// <summary>Opens the corpse chosen by contextual activation without reselecting an overlap.</summary>
    internal PendingCorpseLoot? OpenResolved(long actorId)
    {
        PendingCorpseLoot? prepared = loot.PrepareResolvedLoot(actorId);
        _actor = prepared?.ActorId;
        _ground = null;
        if (prepared is null) { Message = "That loot is no longer available."; return null; }
        _opening++;
        Message = prepared.IsEmpty ? "Empty. This container remains open until Exit." : "Choose an item to take. Stack buttons take one unit.";
        return prepared.IsEmpty ? prepared : null;
    }

    internal void Close(string? container)
    {
        if (container == Token) { _actor = null; _ground = null; }
    }

    /// <summary>Closes an interaction when a relocation changes the player's reachable world context.</summary>
    internal void CloseAll()
    {
        _actor = null;
        _ground = null;
    }

    /// <summary>
    /// Clears an open container for one retiring actor. The mode machine observes the closed
    /// loot through the ordinary Read-null path and follows back to play, so no stale token
    /// can outlive the actor it names.
    /// </summary>
    internal void CloseActor(long actorId)
    {
        if (_actor == actorId) _actor = null;
    }

    internal bool OpenGround(long id)
    {
        if (ground?.Read(id) is null) { Message = "That dropped item pile is no longer available."; return false; }
        _actor = null;
        _ground = id;
        _opening++;
        Message = "Choose an item to take.";
        return true;
    }

    internal PendingGroundLoot? PrepareGroundTake(DaggerfallPlayerUiAction action)
    {
        if (_ground is not long id || ground is null) return null;
        LootPresentation? current = Read();
        if (current is null || action.Container != current.Container || action.Revision != current.Revision)
        { Message = "Loot changed. Choose the item again."; return null; }
        InventoryItemPresentation? item = current.Items.SingleOrDefault(item => item.Key == action.Item);
        if (item is null) { Message = "That item is no longer in this container."; return null; }
        InventoryView contents = ground.Read(id)!;
        ulong quantity = action.Amount ?? 1;
        InventoryContainerSelection selection;
        if (item.Key.StartsWith("unique:", StringComparison.Ordinal))
        {
            if (quantity != 1) { Message = "A unique item can only be taken once."; return null; }
            selection = new(new InventoryItemId(item.Definition), 1, UniqueEntityId: ulong.Parse(item.Key.AsSpan("unique:".Length), CultureInfo.InvariantCulture));
        }
        else if (item.Key.StartsWith("stack:", StringComparison.Ordinal))
        {
            InventoryStackId source = InventoryStackId.Parse(item.Key.Substring("stack:".Length));
            if (quantity == 0 || quantity > ulong.Parse(item.Quantity, CultureInfo.InvariantCulture)) { Message = "Choose a quantity still in that stack."; return null; }
            InventoryStackId destination = ground.ResolveTakeDestination(id, source, InventoryStackId.Parse($"daggerfall.ground.take.{id}.{_opening}.{source.Value}"));
            selection = new(new InventoryItemId(item.Definition), quantity, source, destination);
        }
        else return null;
        return new PendingGroundLoot(id, selection, contents.StoreRevision, item.Definition, quantity);
    }

    internal void CompleteGround(bool committed, string? detail = null) => Message = committed
        ? "Item taken."
        : $"Cannot take that item. {detail}";

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
        ulong quantity = action.Amount ?? 1;
        ulong? unique = item.Key.StartsWith("unique:", StringComparison.Ordinal)
            ? ulong.Parse(item.Key.AsSpan("unique:".Length), CultureInfo.InvariantCulture) : null;
        InventoryStackId? stack = item.Key.StartsWith("stack:", StringComparison.Ordinal)
            ? InventoryStackId.Parse(item.Key.Substring("stack:".Length)) : null;
        if (unique is not null && quantity != 1) { Message = "A unique item can only be taken once."; return null; }
        if (stack is not null && quantity > ulong.Parse(item.Quantity, CultureInfo.InvariantCulture))
        { Message = "Choose a quantity still in that stack."; return null; }
        InventoryStackId? destination = stack is null
            ? null
            : loot.ResolveTakeDestination(_actor!.Value, stack,
                InventoryStackId.Parse($"daggerfall.loot.take.{_actor}.{_opening}.{stack.Value}"));
        InventoryContainerSelection selection = new(new InventoryItemId(item.Definition), quantity, Stack: stack,
            DestinationStack: destination, UniqueEntityId: unique);
        return prepared with
        {
            Selection = selection,
            ExpectedWorldRevision = contents.StoreRevision,
            Facts = [new LootAwardedFact(prepared.ActorId, item.Definition, quantity, prepared.Corpse.OriginatingSequence)],
        };
    }

    internal void Complete(CorpseLootCommitResult result)
    {
        Message = result == CorpseLootCommitResult.Rejected
            ? "Cannot take that item right now. Contents have not been moved."
            : Read()?.Empty == true ? "Empty. This container remains open until Exit." : "Item taken.";
    }
}
