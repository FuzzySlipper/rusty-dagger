using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Transport;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// <see cref="DaggerfallExpeditionPanel"/> built without the session, over a transport and <see cref="GuildMakerFixture"/>'s
/// pack and wagon: the map's opening and entering, the ship's and the transport change's Session lists recorded as plain
/// inputs. The expedition itself is not reached here; its session tests drive it.
/// </summary>
public sealed class DaggerfallExpeditionPanelTests : IDisposable
{
    private readonly GuildMakerFixture _fixture = new();
    private readonly PresentationState _presentation = new("Ready", 5d);
    private readonly List<string> _lists = [];
    private readonly DaggerfallExpeditionPanel _panel;

    public DaggerfallExpeditionPanelTests()
    {
        DaggerfallInventoryPresentation inventoryUi = new(new DaggerfallEquipmentMoves(_fixture.Inventory, _fixture.Equipment, _fixture.Definitions),
            _fixture.Definitions, new Dictionary<string, string>());
        _panel = new(null!, null!, null!, null!, [], DaggerfallWorldNames.None, null, () => true,
            new DaggerfallTransportPolicy(DaggerfallTuning.Defaults.Transport), _fixture.Wagon, _fixture.Inventory, inventoryUi,
            () => _lists.Add("places"), () => DaggerfallTransportAccessContext.At(DaggerfallWorldProfileKind.Exterior, null, null, false),
            () => _lists.Add("open"), () => _lists.Add("enter"),
            () => { _lists.Add("board"); return DaggerfallTransportActionResult.Accepted(DaggerfallTransportMode.Foot, true, "Boarded."); },
            () => { _lists.Add("leave"); return DaggerfallTransportActionResult.Accepted(DaggerfallTransportMode.Foot, false, "Left the ship."); },
            result => _lists.Add($"change {result.Message}"), _presentation, null!, () => new(0, 0));
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
    public void The_map_opens_through_its_list_and_a_wagon_move_names_the_pack_it_saw()
    {
        _panel.Open();
        _panel.RequestPlaces();
        Assert.Equal(["open", "places"], _lists);

        _panel.ChangeWagon(new DaggerfallPlayerUiAction("wagon-put", "stale", Item: "stack:x", Amount: 1));
        Assert.Equal("Inventory changed. Choose the item again.", _presentation.LastOutcome);
    }

    [Fact]
    public void The_world_map_shows_only_while_the_page_reports_it_and_asks_for_its_places_as_it_opens()
    {
        Assert.False(_panel.WorldMapOpen);
        Assert.Null(_panel.ReadWorldMap());
        _panel.ChangeWorldMap(new DaggerfallPlayerUiAction("world-map", Open: true));
        Assert.True(_panel.WorldMapOpen);
        Assert.Equal(["places"], _lists);
        _panel.ChangeWorldMap(new DaggerfallPlayerUiAction("world-map", Open: true));
        Assert.Equal(["places"], _lists);
        _panel.ChangeWorldMap(new DaggerfallPlayerUiAction("world-map", Open: false));
        Assert.False(_panel.WorldMapOpen);
    }

    public void Dispose() => _fixture.Dispose();
}
