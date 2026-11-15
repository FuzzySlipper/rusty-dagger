namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>A quest side for item exits: it allows a drop when told to and records what it was told.</summary>
internal sealed class QuestItemDropsFake : IDaggerfallQuestItemDrops
{
    internal bool Allow { get; set; }
    internal List<DaggerfallItemInstanceMetadata> LeftItems { get; } = [];
    internal List<DaggerfallItemInstanceMetadata> ReturnedItems { get; } = [];

    public bool MayLeave(DaggerfallItemInstanceMetadata item) => Allow;
    public void Left(DaggerfallItemInstanceMetadata item) => LeftItems.Add(item);
    public void Returned(DaggerfallItemInstanceMetadata item) => ReturnedItems.Add(item);

    /// <summary>The exits rule over these instances, with no quest allowing anything.</summary>
    internal static DaggerfallQuestItemExits Exits(DaggerfallItemInstances instances) => new(instances, new QuestItemDropsFake());
}
