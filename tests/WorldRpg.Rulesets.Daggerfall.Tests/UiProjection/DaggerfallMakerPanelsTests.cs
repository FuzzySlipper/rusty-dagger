using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// <see cref="DaggerfallMakerPanels"/> built without the session over <see cref="GuildMakerFixture"/>'s item and potion
/// makers, with no one at the counter.
/// </summary>
public sealed class DaggerfallMakerPanelsTests : IDisposable
{
    private readonly GuildMakerFixture _fixture = new();
    private readonly PresentationState _presentation = new("Ready", 5d);

    [Fact]
    public void Without_a_maker_at_the_counter_the_panes_are_closed_and_the_actions_say_so()
    {
        DaggerfallServiceCounter counter = new(() => null, () => null, null!, () => default, new DaggerfallActivationPresentation());
        DaggerfallMakerPanels makers = new(_fixture.ItemMaker(), _fixture.PotionMaker(), _fixture.Definitions, counter, _presentation);
        Assert.Null(makers.ReadItemMaker());
        Assert.Null(makers.ReadPotionMaker());

        makers.ChangeItemMaker(new DaggerfallPlayerUiAction("itemmaker-draft", "1", Text: "{}"));
        Assert.Equal("This enchanter is unavailable to you.", _presentation.LastOutcome);
        makers.MakePotion(new DaggerfallPlayerUiAction("potion-mix", "1", Text: "[1]"));
        Assert.Equal("This potion maker is unavailable to you.", _presentation.LastOutcome);
    }

    [Fact]
    public void The_item_makers_wording_passes_its_own_sentences_through()
    {
        Assert.Equal("Enchantment preview updated.", DaggerfallMakerPanels.ItemMakerOutcomeText("Preview"));
        Assert.Equal("That item cannot hold another power.", DaggerfallMakerPanels.ItemMakerOutcomeText("That item cannot hold another power."));
    }

    public void Dispose() => _fixture.Dispose();
}
