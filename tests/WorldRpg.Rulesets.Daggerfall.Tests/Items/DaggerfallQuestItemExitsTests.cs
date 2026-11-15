using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallQuestItemExitsTests
{
    private static readonly DaggerfallItemInstanceMetadata Ordinary =
        new DaggerfallItemInstanceMetadata("apple", "none", 0, 0, 0, true, false, null, null, null, DaggerfallItemOwner.Player).Validate();
    private static readonly DaggerfallItemInstanceMetadata Quest = Ordinary with { QuestId = "quest-1", QuestItemSymbol = "gift" };

    [Fact]
    public void A_quest_item_is_refused_unless_its_quest_lets_it_go()
    {
        QuestItemDropsFake quests = new();
        DaggerfallQuestItemExits exits = new(new DaggerfallItemInstances(), quests);

        Assert.Equal(DaggerfallQuestItemExits.Refused, exits.Refusal(Quest));
        quests.Allow = true;
        Assert.Null(exits.Refusal(Quest));
    }

    [Fact]
    public void An_ordinary_or_permanent_item_always_leaves()
    {
        DaggerfallQuestItemExits exits = new(new DaggerfallItemInstances(), new QuestItemDropsFake());

        Assert.Null(exits.Refusal(Ordinary));
        Assert.Null(exits.Refusal(Quest with { QuestId = null, QuestItemSymbol = null }));
    }

    [Fact]
    public void Leaving_tells_the_quest_except_into_the_wagon_and_coming_back_tells_it_again()
    {
        QuestItemDropsFake quests = new();
        DaggerfallQuestItemExits exits = new(new DaggerfallItemInstances(), quests);

        exits.Moved(Quest, DaggerfallItemOwner.Player, DaggerfallItemOwner.Wagon(1));
        Assert.Empty(quests.LeftItems);
        exits.Moved(Quest, DaggerfallItemOwner.Player, DaggerfallItemOwner.Merchant(4));
        Assert.Single(quests.LeftItems);
        exits.Moved(Quest, DaggerfallItemOwner.Merchant(4), DaggerfallItemOwner.Player);
        Assert.Single(quests.ReturnedItems);
        exits.Moved(Ordinary, DaggerfallItemOwner.Player, DaggerfallItemOwner.Ground(9));
        Assert.Single(quests.LeftItems);
    }
}
