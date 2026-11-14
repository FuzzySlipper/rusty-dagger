using System.Globalization;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Facts;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using WorldRpg.Rulesets.Daggerfall.Modules.Loot;
using WorldRpg.Rulesets.Daggerfall.Presentation;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// The loot window's actions: what using a body or a pile opens, taking one row or every row, a quest reward's pile,
/// and the quest item a take hands its quest.
/// </summary>
/// <param name="questItemTaken">Quests: the quest that owns an item hears it was taken.</param>
/// <param name="stockContainer">Economy: stocks a property container as it opens; whether it is private, or null when it is gone.</param>
/// <param name="observePropertyTake">Economy: what is taken from a property container is theft of its goods.</param>
internal sealed class DaggerfallLootActions(DaggerfallLootPresentation loot, DaggerfallCorpseLootModule corpseLoot,
    DaggerfallGroundContainers ground, FactBuffer<IProductFact> facts, DaggerfallEncumbrancePolicy encumbrance,
    DaggerfallDefinitions definitions, DaggerfallItemInstances instances, MechanicsInventoryCoordinator inventory,
    PlayerControlState player, PlayerInputSystem input, Func<DaggerfallItemInstanceMetadata, bool> questItemTaken,
    Func<long, bool?> stockContainer, Action<DaggerfallGroundContainer, InventoryContainerTransferReceipt, string, ulong> observePropertyTake,
    PresentationState presentation)
{
    /// <summary>
    /// Takes every row of the shown container through the ordinary take, each whole and against the
    /// container's current revision, and stops at the first one refused (too heavy, out of reach), so
    /// what was taken stays taken and the refusal is reported. It reports once, not once per row.
    /// </summary>
    internal void TakeAll(DaggerfallPlayerUiAction action)
    {
        LootPresentation? shown = loot.Read();
        if (shown is null || shown.Container != action.Container || shown.Revision != action.Revision)
        {
            presentation.SetOutcome("Loot changed. Choose the item again.");
            return;
        }
        int taken = 0;
        foreach (string key in shown.Items.Select(item => item.Key).ToArray())
        {
            LootPresentation? current = loot.Read();
            if (current?.Items.SingleOrDefault(item => item.Key == key) is not { } row) continue;
            Take(new DaggerfallPlayerUiAction("loot-take", current.Revision, key, Container: current.Container,
                Amount: ulong.Parse(row.Quantity, CultureInfo.InvariantCulture)), report: false);
            if (loot.Read()?.Items.Any(item => item.Key == key) ?? false) break;
            taken++;
        }
        loot.CompleteAll(taken, loot.Read()?.Empty ?? true);
        presentation.SetOutcome(loot.Message);
    }

    /// <summary>
    /// A valid take applies at once: the transfer commits, its completed-change facts deliver at the
    /// update's boundary even while the modal holds the world, and the published presentation already
    /// reflects the result. A refused take (a stale revision, a closed container) states its reason too.
    /// </summary>
    internal void Take(DaggerfallPlayerUiAction action, bool report = true)
    {
        if (loot.PrepareGroundTake(action) is { } groundTake)
        {
            if (!encumbrance.CanCarry(definitions.RequireItem(new DaggerfallItemId(groundTake.Definition)), groundTake.Quantity,
                groundTake.Selection.UniqueEntityId is ulong entity ? instances.RequireUnique(inventory.GetDurableItemId(new(entity)).Value) : null))
            {
                loot.CompleteGround(false, "You cannot carry any more.");
                if (report) presentation.SetOutcome(loot.Message);
                return;
            }
            try
            {
                ground.TryGet(groundTake.Id, out var source);
                var questItem = QuestItem(DaggerfallItemOwner.Ground(groundTake.Id), groundTake.Selection);
                var transfer = ground.Take(groundTake.Id, groundTake.Selection, groundTake.ExpectedWorldRevision);
                if (questItem is not null) questItemTaken(questItem);
                observePropertyTake(source, transfer, groundTake.Definition, groundTake.Quantity);
                loot.CompleteGround(true);
            }
            catch (Exception rejection) when (rejection is InvalidOperationException or ArgumentException)
            {
                loot.CompleteGround(false, "That item cannot be taken.");
            }
            if (report) presentation.SetOutcome(loot.Message);
            return;
        }
        if (loot.PrepareTake(action, player, input.ResolveCurrentLook(player)) is { } take)
        {
            var questItem = take.Selection is { } selection ? QuestItem(DaggerfallItemOwner.Corpse(take.ActorId), selection) : null;
            var result = corpseLoot.TryCommitLoot(take, facts);
            if (result == CorpseLootCommitResult.Committed && questItem is not null) questItemTaken(questItem);
            loot.Complete(result);
        }
        if (report) presentation.SetOutcome(loot.Message);
    }

    /// <summary>What using a body does: opens its loot window, an empty one committed at once.</summary>
    internal DaggerfallActivationOutcome OpenCorpse(long actorId)
    {
        if (loot.OpenResolved(actorId) is { IsEmpty: true } empty)
            corpseLoot.TryCommitLoot(empty, facts);
        return loot.Read() is null
            ? new(false, loot.Message)
            : new(true, loot.Message);
    }

    /// <summary>What using a pile or a property container does: stocks it as it opens, then opens its loot window.</summary>
    internal DaggerfallActivationOutcome OpenGround(long id) =>
        stockContainer(id) is not bool privateProperty ? new(false, "That container is no longer available.")
        : loot.OpenGround(id, privateProperty) ? new(true, loot.Message) : new(false, loot.Message);

    /// <summary>Opens a quest reward's pile and says so, or asks the player to come back for it.</summary>
    internal bool PresentQuestReward(long? pile)
    {
        if (pile is { } id && !loot.OpenGround(id))
        {
            presentation.SetOutcome("Return to the reward location to collect your quest reward.");
            return false;
        }
        presentation.SetOutcome("Quest completed.");
        return true;
    }

    /// <summary>The item a loot selection takes, so the quest that owns it hears it was taken.</summary>
    private DaggerfallItemInstanceMetadata? QuestItem(DaggerfallItemOwner owner, InventoryContainerSelection selection)
    {
        if (selection.UniqueEntityId is { } entity)
            return instances.RequireUnique(inventory.GetDurableItemId(new(entity)).Value);
        return selection.Stack is { } stack ? instances.RequireStack(owner, stack) : null;
    }
}
