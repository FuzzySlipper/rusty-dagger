using WorldRpg.Kit.Presentation;
using WorldRpg.Kit.Progression;
using WorldRpg.Rulesets.Daggerfall.Banking;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// <see cref="DaggerfallEconomyPanels"/> built without the session, over a real bank counter (<see cref="GuildMakerFixture"/>'s
/// purse and services) and no conversation: the panels' refusals when no one serves the player. The owners these paths
/// never reach (merchant, rooms, training, holdings) are left out.
/// </summary>
public sealed class DaggerfallEconomyPanelsTests : IDisposable
{
    private readonly GuildMakerFixture _fixture = new();
    private readonly PresentationState _presentation = new("Ready", 5d);
    private readonly List<string> _panels = [];
    private readonly DaggerfallEconomyPanels _panelsOwner;

    public DaggerfallEconomyPanelsTests()
    {
        DaggerfallRegionalBankState bank = new(_fixture.Currency, _fixture.Inventory, _fixture.Instances);
        DaggerfallServiceProvider? opened = null;
        DaggerfallBanking banking = new(bank, _fixture.Currency, new DaggerfallLoanState(), _fixture.Services, _fixture.Social,
            _fixture.Definitions.Factions, new ProgressionState(), region => $"region {region}", () => false, () => _fixture.Calendar,
            () => opened, provider => opened = provider);
        _panelsOwner = new(banking, bank, null!, null!, new DaggerfallTavernService(null!, null!, () => _fixture.Calendar), null!, null!,
            _fixture.Inventory, _fixture.Wagon, null!, _fixture.Definitions, () => null, () => null,
            () => throw new InvalidOperationException("No transport is reached here."), _panels.Add,
            _ => throw new InvalidOperationException("No interior is entered here."), _presentation);
    }

    [Fact]
    public void With_no_one_serving_the_counters_say_so()
    {
        _panelsOwner.BuyTavernMeal(new DaggerfallPlayerUiAction("tavern-buy", "1", Key: "ale", Amount: 1));
        Assert.Equal("That tavern keeper is no longer available.", _presentation.LastOutcome);

        _panelsOwner.OpenCurrentBank("1");
        Assert.Equal("That bank service is no longer available.", _presentation.LastOutcome);
        Assert.Empty(_panels);
    }

    [Fact]
    public void Money_and_loans_need_an_open_bank()
    {
        _panelsOwner.ChangeCurrency(new DaggerfallPlayerUiAction("currency-deposit-gold", Amount: 5));
        Assert.Equal(DaggerfallBanking.Unavailable, _presentation.LastOutcome);
        _panelsOwner.ChangeLoan(new DaggerfallPlayerUiAction("bank-loan-issue", Amount: 100));
        Assert.Equal(DaggerfallBanking.Unavailable, _presentation.LastOutcome);
    }

    [Fact]
    public void Only_merchant_actions_are_trades()
    {
        Assert.Throws<ArgumentException>(() => _panelsOwner.ChangeMerchant(new DaggerfallPlayerUiAction("tavern-buy")));
    }

    public void Dispose() => _fixture.Dispose();
}
