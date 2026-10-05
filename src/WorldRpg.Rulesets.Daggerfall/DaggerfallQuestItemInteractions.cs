using System.Text.RegularExpressions;
using WorldRpg.Kit.Inventory;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed record DaggerfallQuestItemTransfer(string Item, string Recipient, DaggerfallCreatedItem? Prototype, long[] Recipients)
{
    internal DaggerfallQuestItemTransfer Copy() => this with { Recipients = [.. Recipients] };
}

internal static partial class DaggerfallQuestTaskCompiler
{
    private static readonly Regex ClickedItem = Header(@"^clicked\s+item\s+(?<item>[a-zA-Z0-9_.-]+)(?:\s+say\s+(?<message>[a-zA-Z0-9_.-]+))?$");
    private static readonly Regex TotingItem = Header(@"^toting\s+(?<item>[a-zA-Z0-9_.-]+)\s+and\s+(?<npc>[a-zA-Z0-9_.-]+)\s+clicked(?:\s+saying\s+(?<message>[a-zA-Z0-9_.-]+))?$");
    private static readonly Regex ItemUsed = Header(@"^(?<item>[a-zA-Z0-9_.-]+)\s+used(?:\s+saying\s+(?<message>\d+))?\s+do\s+(?<task>[a-zA-Z0-9_.-]+)$");
    private static readonly Regex GiveItem = Header(@"^give\s+item\s+(?<item>[a-zA-Z0-9_.-]+)\s+to\s+(?<target>[a-zA-Z0-9_.-]+)$");
    private static readonly Regex PayMoney = Header(@"^pay\s+(?<amount>\d+)\s+(?<kind>money|gold)\s+do\s+(?<paid>[a-zA-Z0-9_.-]+)\s+otherwise\s+do\s+(?<unpaid>[a-zA-Z0-9_.-]+)$");
    private static DaggerfallQuestTaskOperation? CompileItemInteraction(string line, int sourceLine)
    {
        var match = ClickedItem.Match(line);
        var kind = DaggerfallQuestTaskOperationKind.ClickedItem;
        if (!match.Success) { match = TotingItem.Match(line); kind = DaggerfallQuestTaskOperationKind.TotingItem; }
        if (!match.Success) { match = ItemUsed.Match(line); kind = DaggerfallQuestTaskOperationKind.ItemUsed; }
        if (match.Success)
        {
            string text = match.Groups["message"].Value;
            int? message = int.TryParse(text, out int number) ? number : null;
            string[] targets = [Canonical(match.Groups["item"].Value)];
            if (match.Groups["npc"].Success) targets = [targets[0], Canonical(match.Groups["npc"].Value)];
            if (match.Groups["task"].Success) targets = [targets[0], Canonical(match.Groups["task"].Value)];
            return new(kind, sourceLine, line, targets, [], message, MessageAlias: message is null && text.Length > 0 ? text : null);
        }
        if (GiveItem.Match(line) is { Success: true } give)
            return new(DaggerfallQuestTaskOperationKind.GiveItem, sourceLine, line, [Canonical(give.Groups["item"].Value), Canonical(give.Groups["target"].Value)], [], null);
        if (PayMoney.Match(line) is { Success: true } pay)
            return new(DaggerfallQuestTaskOperationKind.PayMoney, sourceLine, line, [Canonical(pay.Groups["paid"].Value), Canonical(pay.Groups["unpaid"].Value), pay.Groups["kind"].Value], [], null,
                Step: Step(pay.Groups["amount"].Value, sourceLine));
        return null;
    }
}

internal sealed partial class DaggerfallQuestInstances
{
    private Func<ulong, bool, bool>? _payQuestMoney;
    private Func<DaggerfallQuestRuntimeInstance, string, string, string, DaggerfallQuestItemTransfer?, DaggerfallQuestItemTransfer?>? _giveQuestItem;
    internal void BindItemInteractions(Func<ulong, bool, bool> pay, Func<DaggerfallQuestRuntimeInstance, string, string, string, DaggerfallQuestItemTransfer?, DaggerfallQuestItemTransfer?> give)
    { _payQuestMoney = pay; _giveQuestItem = give; }

    internal bool ItemClicked(DaggerfallItemInstanceMetadata metadata)
    {
        if (metadata.QuestId is not { } quest || metadata.QuestItemSymbol is not { } symbol || !_instances.TryGetValue(quest, out var instance)
            || instance.Lifecycle != DaggerfallQuestLifecycle.Active) return false;
        var item = instance.Resources.SingleOrDefault(resource => resource.Symbol == symbol && resource.SelectedItem is not null);
        if (item is null) return false;
        SetResource(quest, item with { HasPlayerClicked = true });
        return true;
    }

    internal DaggerfallInventoryUseResult? UseItem(DaggerfallItemInstanceMetadata metadata)
    {
        if (metadata.Owner != DaggerfallItemOwner.Player || metadata.QuestId is not { } quest || metadata.QuestItemSymbol is not { } symbol
            || !_instances.TryGetValue(quest, out var instance) || instance.Lifecycle != DaggerfallQuestLifecycle.Active) return null;
        var item = instance.Resources.SingleOrDefault(resource => resource.Symbol == symbol && resource.SelectedItem is not null);
        if (item is null) return null;
        var program = Program(instance.SourceFile);
        bool watched = program.Tasks.Where((_, index) => instance.Tasks[index].IsSet && !instance.Tasks[index].IsDropped)
            .Any(task => task.Operations.Select((operation, index) => (operation, index)).Any(value => value.operation.Kind == DaggerfallQuestTaskOperationKind.ItemUsed
                && value.operation.Targets[0] == symbol && !instance.Tasks[program.TaskIndexes[task.Symbol]].OperationCompleted[value.index]));
        if (watched)
        {
            SetResource(quest, item with { UseClicked = true });
        }
        if (ItemUsedMessage(instance, symbol) is not { } message) return watched ? new(true, "Quest item used.") : null;
        if (!Messages.Deliveries.Any(delivery => delivery.InstanceId == quest && delivery.MessageId == message && delivery.Delivery == DaggerfallQuestMessageDelivery.Letter))
            Messages.Letter(instance, message);
        return new(true, "Quest letter read.");
    }

    DaggerfallQuestClickResult IDaggerfallQuestTaskLifecycle.ItemClick(DaggerfallQuestRuntimeInstance instance, DaggerfallQuestTaskOperation operation)
    {
        var item = instance.Resources.SingleOrDefault(resource => resource.Symbol == operation.Targets[0] && resource.SelectedItem is not null);
        if (item is null) return new(false);
        DaggerfallQuestResourceState? npc = null;
        if (operation.Kind == DaggerfallQuestTaskOperationKind.TotingItem)
        {
            npc = instance.Resources.SingleOrDefault(resource => resource.Symbol == operation.Targets[1] && resource.SelectedPerson is not null);
            if (npc is null || !npc.HasPlayerClicked || npc.IsHidden || npc.IsNpcDestroyed || !Items.Have(instance, item.Symbol)) return new(false);
        }
        else if (!item.HasPlayerClicked) return new(false);
        int? message = ResolveInteractionMessage(instance, operation);
        if (npc is not null)
        {
            if (Items.Take(instance, item.Symbol) == DaggerfallQuestItemResult.Unavailable) return new(false);
            _pendingClickRearms.Add((instance.InstanceId, npc.Symbol));
        }
        return new(true, MessageId: message);
    }

    bool IDaggerfallQuestTaskLifecycle.ItemUsed(DaggerfallQuestRuntimeInstance instance, DaggerfallQuestTaskOperation operation)
    {
        var item = instance.Resources.SingleOrDefault(resource => resource.Symbol == operation.Targets[0] && resource.SelectedItem is not null);
        if (item?.UseClicked != true) return false;
        if (ResolveInteractionMessage(instance, operation) is { } message) Messages.Popup(instance, message);
        return true;
    }

    string? IDaggerfallQuestTaskLifecycle.PayMoney(DaggerfallQuestTaskOperation operation)
    {
        ulong amount = checked((ulong)operation.Step!.Value);
        if (amount == 0) return null;
        bool paid = (_payQuestMoney ?? throw new InvalidOperationException("No quest payment owner is composed."))(amount, operation.Targets[2] == "gold");
        return operation.Targets[paid ? 0 : 1];
    }

    bool IDaggerfallQuestTaskLifecycle.GiveItem(DaggerfallQuestRuntimeInstance instance, DaggerfallQuestTaskOperation operation, DaggerfallQuestTaskRuntimeState state, int index)
    {
        string key = state.Symbol + "/" + index;
        var transfer = (_giveQuestItem ?? throw new InvalidOperationException("No quest item transfer owner is composed."))
            (instance, operation.Targets[0], operation.Targets[1], key, state.OperationState[index].ItemTransfer);
        if (transfer is null) return false;
        state.OperationState[index] = state.OperationState[index] with { ItemTransfer = transfer };
        return true;
    }

    private void AdmitQueuedFoeItems(DaggerfallQuestRuntimeInstance instance)
    {
        foreach (var state in instance.Tasks)
            for (int index = 0; index < state.OperationState.Length; index++)
                if (state.OperationState[index].ItemTransfer is { Prototype: not null } queued)
                {
                    var updated = (_giveQuestItem ?? throw new InvalidOperationException("No quest item transfer owner is composed."))
                        (instance, queued.Item, queued.Recipient, state.Symbol + "/" + index, queued);
                    if (updated is not null) state.OperationState[index] = state.OperationState[index] with { ItemTransfer = updated };
                }
    }

    private int? ResolveInteractionMessage(DaggerfallQuestRuntimeInstance instance, DaggerfallQuestTaskOperation operation)
    {
        if (operation.MessageId is null or 0 && operation.MessageAlias is null) return null;
        if (!Messages.TryResolveMessage(instance, operation.MessageId, operation.MessageAlias, out int message, out var diagnostic)) throw new NotSupportedException(diagnostic);
        return message;
    }
}
