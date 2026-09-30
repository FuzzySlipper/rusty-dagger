using WorldRpg.Kit;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallOpenInteractionsTests
{
    [Theory]
    [InlineData("LootContainer", true)]
    [InlineData("DungeonText", true)]
    [InlineData("Dialogue", false)]
    [InlineData("CharacterCreation", false)]
    [InlineData("LevelUp", false)]
    [InlineData("Bank", false)]
    public void Only_loot_and_dungeon_text_hold_the_world(string screen, bool holds) =>
        Assert.Equal(holds, DaggerfallOpenInteractions.HoldsWorld(Enum.Parse<DaggerfallInteractionScreen>(screen)));

    [Fact]
    public void Mode_request_follows_the_world_holding_screens_and_death()
    {
        bool dungeonText = false, dialogue = true;
        DaggerfallOpenInteractions open = new(() => null, () => "loot", () => dungeonText, () => dialogue, () => false, () => false, () => false);

        // An open dialogue runs over ordinary play, so nothing is asked.
        Assert.Null(open.ModeRequest(ProductMode.Playing, playerDefeated: false));
        dungeonText = true;
        Assert.Equal(ProductMode.Modal, open.ModeRequest(ProductMode.Playing, playerDefeated: false));
        Assert.Null(open.ModeRequest(ProductMode.Modal, playerDefeated: false));
        Assert.Equal("Dungeon text open.", open.ModeMessage(ProductMode.Modal));
        dungeonText = false;
        Assert.Equal(ProductMode.Playing, open.ModeRequest(ProductMode.Modal, playerDefeated: false));
        // The product's own modes have no screen to follow; death outranks everything.
        Assert.Null(open.ModeRequest(ProductMode.Title, playerDefeated: false));
        Assert.Null(open.ModeRequest(ProductMode.Paused, playerDefeated: false));
        Assert.Equal(ProductMode.Dead, open.ModeRequest(ProductMode.Paused, playerDefeated: true));
    }
}
