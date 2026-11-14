using WorldRpg.Kit.Inventory;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>What the loot panels show of quest items: a taken selection's item, and the reward pile opened on success.</summary>
internal sealed partial class DaggerfallSession
{
    /// <summary>The item a loot selection takes, so the quest that owns it hears it was taken.</summary>
    private DaggerfallItemInstanceMetadata? QuestLootMetadata(DaggerfallItemOwner owner, InventoryContainerSelection selection)
    {
        if (selection.UniqueEntityId is { } entity)
            return State.ItemInstances.RequireUnique(State.Inventory.GetDurableItemId(new(entity)).Value);
        return selection.Stack is { } stack ? State.ItemInstances.RequireStack(owner, stack) : null;
    }

    /// <summary>Opens a quest reward's pile and says so, or asks the player to come back for it.</summary>
    private bool PresentQuestReward(long? ground)
    {
        if (ground is { } id && !_lootUi.OpenGround(id))
        {
            Presentation.SetOutcome("Return to the reward location to collect your quest reward.");
            return false;
        }
        Presentation.SetOutcome("Quest completed.");
        return true;
    }
}
