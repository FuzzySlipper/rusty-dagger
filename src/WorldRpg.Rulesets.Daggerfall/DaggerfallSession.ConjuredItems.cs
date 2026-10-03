namespace WorldRpg.Rulesets.Daggerfall;

internal sealed partial class DaggerfallSession
{
    /// <summary>Expiry is item policy on the one admitted calendar and the canonical inventories.</summary>
    private void ExpireConjuredItems()
    {
        long minute = MinuteIndex(_time.Calendar);
        foreach (var entry in State.ItemInstances.UniqueItems.Where(value => value.Value.Conjuration?.ExpiresAtMinute <= minute).ToArray())
            DestroyUniqueItem(entry.Key);
        foreach (var entry in State.ItemInstances.StackItems.Where(value => value.Metadata.Conjuration?.ExpiresAtMinute <= minute).ToArray())
            ConsumeItemStack(entry.Owner, entry.Stack);
        State.HeldEnchantments.Refresh();
        // Inactive deltas stay coherent while suspended. The site's existing materialization
        // restores their effects and stat sources before this same live sweep releases them.
    }
}
