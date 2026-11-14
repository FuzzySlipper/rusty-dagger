using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Transport;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// <see cref="DaggerfallTravelPanel"/> built without the session, over a transport and <see cref="GuildMakerFixture"/>'s
/// pack and wagon: the journey's, the ship's and the transport change's Session lists recorded as plain inputs. Fast
/// travel's map is not reached here.
/// </summary>
public sealed class DaggerfallTravelPanelTests : IDisposable
{
    private readonly GuildMakerFixture _fixture = new();
    private readonly PresentationState _presentation = new("Ready", 5d);
    private readonly List<string> _lists = [];
    private readonly DaggerfallTravelPanel _panel;

    public DaggerfallTravelPanelTests()
    {
        DaggerfallInventoryPresentation inventoryUi = new(new DaggerfallEquipmentMoves(_fixture.Inventory, _fixture.Equipment, _fixture.Definitions),
            _fixture.Definitions, new Dictionary<string, string>());
        _panel = new(null!, new DaggerfallTransportPolicy(DaggerfallTuning.Defaults.Transport), _fixture.Wagon, _fixture.Inventory, inventoryUi,
            () => _lists.Add("places"), () => DaggerfallTransportAccessContext.At(DaggerfallWorldProfileKind.Exterior, null, null, false),
            (quote, cost) => _lists.Add($"journey {quote} {cost}"),
            () => { _lists.Add("board"); return DaggerfallTransportActionResult.Accepted(DaggerfallTransportMode.Foot, true, "Boarded."); },
            () => { _lists.Add("leave"); return DaggerfallTransportActionResult.Accepted(DaggerfallTransportMode.Foot, false, "Left the ship."); },
            result => _lists.Add($"change {result.Message}"), _presentation);
    }

    [Fact]
    public void A_transport_action_runs_its_list_then_the_change_and_says_the_result()
    {
        _panel.ChangeTransport(new DaggerfallPlayerUiAction("transport-board-ship"));
        Assert.Equal(["board", "change Boarded."], _lists);
        Assert.Equal("Boarded.", _presentation.LastOutcome);

        _lists.Clear();
        _panel.ChangeTransport(new DaggerfallPlayerUiAction("transport-select", Mode: "horse"));
        Assert.StartsWith("change ", Assert.Single(_lists), StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => _panel.ChangeTransport(new DaggerfallPlayerUiAction("transport-select", Mode: "boat")));
    }

    [Fact]
    public void A_journey_is_the_accepted_quote_and_a_wagon_move_names_the_pack_it_saw()
    {
        _panel.Accept(new DaggerfallPlayerUiAction("travel-accept", Key: "quote-1", Amount: 40));
        Assert.Equal(["journey quote-1 40"], _lists);

        _panel.ChangeWagon(new DaggerfallPlayerUiAction("wagon-put", "stale", Item: "stack:x", Amount: 1));
        Assert.Equal("Inventory changed. Choose the item again.", _presentation.LastOutcome);
    }

    public void Dispose() => _fixture.Dispose();
}
