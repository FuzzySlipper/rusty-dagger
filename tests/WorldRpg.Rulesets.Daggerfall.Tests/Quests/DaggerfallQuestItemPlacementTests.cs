using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Where a quest's items physically go, built without the session over <see cref="QuestItemStage"/>'s pack, item
/// instances, quest custody and ground piles; no unloaded site keeps anything and no corpse is on the ground.
/// </summary>
public sealed class DaggerfallQuestItemPlacementTests
{
    private static DaggerfallQuestItemPlacement Items(QuestItemStage stage) => new(stage.Quests, stage.QuestItems, stage.Instances, stage.Actors,
        stage.Player, stage.Ground, stage.UniqueItems, () => new Dictionary<DaggerfallWorldProfileKey, DaggerfallSiteRuntimeDelta>(),
        (_, _) => throw new InvalidOperationException("No unloaded site keeps anything in this stage."), _ => false,
        (_, _) => throw new InvalidOperationException("No corpse is on the ground in this stage."));

    [Fact]
    public void A_reward_waits_for_the_player_and_is_then_set_on_the_ground_under_them()
    {
        using QuestItemStage stage = new();
        DaggerfallQuestRuntimeInstance quest = stage.StartWithGift();
        DaggerfallQuestItemPlacement items = Items(stage);
        Assert.Null(items.OfferReward(quest, "gift"));

        stage.Player.MoveTo(new System.Numerics.Vector3(2f, 0f, 3f));
        long? pile = items.OfferReward(quest, "gift");

        Assert.NotNull(pile);
        Assert.True(stage.Ground.TryGet(pile.Value, out var container));
        Assert.Equal(new WorldPoint(2f, 0f, 3f), container.Position);
        Assert.Single(stage.Instances.UniqueItems, pair => pair.Value.Owner == DaggerfallItemOwner.Ground(pile.Value) && pair.Value.QuestItemSymbol == "gift");
    }

    [Fact]
    public void A_gift_for_a_foe_not_yet_placed_waits_in_its_queue()
    {
        using QuestItemStage stage = new();
        DaggerfallQuestRuntimeInstance quest = stage.StartWithGift();
        quest.Resources = [.. quest.Resources, new("enemy", DaggerfallQuestResourceBinding.Pending()) { SelectedFoe = new("nymph", 1, false) }];

        DaggerfallQuestItemTransfer? queued = Items(stage).Give(quest, "gift", "enemy", "give/1", null);

        Assert.NotNull(queued);
        Assert.Equal(("gift", "enemy"), (queued.Item, queued.Recipient));
        Assert.Empty(queued.Recipients);
        Assert.NotNull(queued.Prototype);
    }
}
