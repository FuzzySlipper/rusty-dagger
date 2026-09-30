using System.Text;
using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallOpenInteractionsTests
{
    /// <summary>Every screen holds the world by default; a screen that should not is an explicit exception, and there are none.</summary>
    [Fact]
    public void Every_screen_holds_the_world() =>
        Assert.All(Enum.GetValues<DaggerfallInteractionScreen>(), screen => Assert.True(DaggerfallOpenInteractions.HoldsWorld(screen), $"{screen} lets the world run."));

    [Fact]
    public void Mode_request_follows_the_open_screens_and_death()
    {
        bool dungeonText = false, dialogue = false;
        DaggerfallOpenInteractions open = new(() => null, () => "loot", () => dungeonText, () => dialogue, () => false, () => false, () => false);

        Assert.Null(open.ModeRequest(ProductMode.Playing, playerDefeated: false));
        dialogue = true;
        Assert.Equal(ProductMode.Modal, open.ModeRequest(ProductMode.Playing, playerDefeated: false));
        Assert.Equal("Interaction open.", open.ModeMessage(ProductMode.Modal));
        dialogue = false;
        dungeonText = true;
        Assert.Null(open.ModeRequest(ProductMode.Modal, playerDefeated: false));
        Assert.Equal("Dungeon text open.", open.ModeMessage(ProductMode.Modal));
        dungeonText = false;
        Assert.Equal(ProductMode.Playing, open.ModeRequest(ProductMode.Modal, playerDefeated: false));
        // The product's own modes have no screen to follow; death outranks everything.
        Assert.Null(open.ModeRequest(ProductMode.Title, playerDefeated: false));
        Assert.Null(open.ModeRequest(ProductMode.Paused, playerDefeated: false));
        Assert.Equal(ProductMode.Dead, open.ModeRequest(ProductMode.Paused, playerDefeated: true));
    }

    [Fact]
    public void The_game_menu_holds_the_world_while_the_dom_reports_it_open_and_spends_a_panel_request()
    {
        DaggerfallOpenInteractions open = new(() => null, () => "loot", () => false, () => false, () => false, () => false, () => false);
        open.RequestPanel("inventory");

        open.SetMenuOpen(true);
        Assert.Equal(ProductMode.Modal, open.ModeRequest(ProductMode.Playing, playerDefeated: false));
        Assert.Equal("Menu open.", open.ModeMessage(ProductMode.Modal));
        // Opening the menu is the DOM acting on the pad's request, which the held world could not age away.
        Assert.Null(open.LatestPanelRequest);

        open.SetMenuOpen(false);
        Assert.Equal(ProductMode.Playing, open.ModeRequest(ProductMode.Modal, playerDefeated: false));
    }

    [Theory]
    [InlineData("""{"action":"menu","open":true}""", true)]
    [InlineData("""{"action":"menu","open":false}""", false)]
    public void Menu_action_carries_whether_the_menu_is_open(string json, bool expected)
    {
        DaggerfallPlayerUiAction action = Assert.IsType<DaggerfallPlayerUiAction>(DaggerfallUiAction.Parse(Encoding.UTF8.GetBytes(json)));
        Assert.Equal(DaggerfallUiActionKind.Menu, action.Kind);
        Assert.Equal(expected, action.Open);
    }

    [Theory]
    [InlineData("""{"action":"menu"}""")]
    [InlineData("""{"action":"menu","open":"yes"}""")]
    [InlineData("""{"action":"menu","open":true,"item":"x"}""")]
    public void Malformed_menu_action_is_not_recognized(string json) =>
        Assert.Null(DaggerfallUiAction.Parse(Encoding.UTF8.GetBytes(json)));

    /// <summary>The menu's own panels still act while the menu holds the world.</summary>
    [Theory]
    [InlineData("CharacterLevelAllocate")]
    [InlineData("CharacterLevelCommit")]
    [InlineData("TransportSelect")]
    [InlineData("TransportToggle")]
    [InlineData("TransportLeaveShip")]
    [InlineData("Rest")]
    [InlineData("WagonPut")]
    [InlineData("WagonTake")]
    [InlineData("InventoryUse")]
    [InlineData("SaveSlot")]
    public void Menu_panel_actions_are_admitted_while_the_world_is_held(string kind) =>
        Assert.True(DaggerfallUiAction.RuleFor(Enum.Parse<DaggerfallUiActionKind>(kind)).Admits(DaggerfallUiPhases.Modal));
}
