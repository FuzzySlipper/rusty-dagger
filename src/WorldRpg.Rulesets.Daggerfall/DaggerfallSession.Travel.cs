using WorldRpg.Rulesets.Daggerfall.Travel;
using WorldRpg.Rulesets.Daggerfall.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed partial class DaggerfallSession
{
    private readonly DaggerfallTravelPolicy _travelPolicy;
    private DaggerfallTravelDestination[] _travelSearchResults = [];
    private bool _travelSearchInitialized;
    private DaggerfallSiteId? _travelSelectedDestination;
    private (bool Cautious, bool Inn, bool Ship) _travelSelectedOptions = (true, false, false);
    private string? _travelMessage;

    private void ChangeTravel(DaggerfallPlayerUiAction action)
    {
        if (action.Action == "travel-search")
        {
            string term = action.Text?.Trim() ?? string.Empty;
            _travelSearchResults = [.. _travelPolicy.SupportedDestinations()
                .Where(destination => destination.Name.Contains(term, StringComparison.OrdinalIgnoreCase))
                .Take(40)];
            _travelSearchInitialized = true;
            _travelMessage = _travelSearchResults.Length == 0 ? "No discovered destination matches that name." : null;
            return;
        }
        if (action.Region is not int region || action.Destination is not int index)
            throw new ArgumentException("Travel preview requires a selected destination.", nameof(action));
        _travelSelectedDestination = new DaggerfallSiteId(region, index);
        _travelSelectedOptions = (action.Cautious, action.Inn, action.Ship);
        _ = CurrentTravelQuote();
    }

    private DaggerfallTravelQuote? CurrentTravelQuote()
    {
        if (_travelSelectedDestination is not DaggerfallSiteId destination) return null;
        try
        {
            DaggerfallCurrencyTotals funds = State.Currency.Read();
            DaggerfallTravelOptions options = new(
                _travelSelectedOptions.Cautious,
                _travelSelectedOptions.Inn,
                _travelSelectedOptions.Ship,
                State.Transport.HasHorse(State.Inventory.Read()),
                State.Transport.HasCart(State.Inventory.Read()),
                State.Property?.OwnsShip ?? false,
                AvailableGold: checked(funds.Gold + funds.LettersOfCredit),
                AvailableGoldPieces: funds.Gold);
            DaggerfallTravelQuote quote = _travelPolicy.Quote(QuestTravelOrigin(), destination, options);
            _travelMessage = quote.CanAfford ? null : "You cannot afford the selected route and lodging options.";
            return quote;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OverflowException)
        {
            _travelMessage = exception.Message;
            return null;
        }
    }

    private DaggerfallTravelPresentation ReadTravelPresentation()
    {
        if (!_travelSearchInitialized)
        {
            _travelSearchResults = [.. _travelPolicy.SupportedDestinations().Take(40)];
            _travelSearchInitialized = true;
        }
        return new(_travelSearchResults, CurrentTravelQuote(), _travelMessage);
    }

    /// <summary>The current world-map pixel, including wilderness steps away from an exterior site.</summary>
    private DaggerfallTravelMapPixel QuestTravelOrigin()
    {
        if (_activeProfileKey.Kind == DaggerfallWorldProfileKind.Exterior)
        {
            DaggerfallExteriorCellId cell = CurrentExteriorCell();
            return new(cell.X, cell.Y);
        }
        DaggerfallSiteRecord site = _site.ActiveSite
            ?? throw new InvalidOperationException("Quest travel duration requires an active geographic site.");
        return new(site.MapPixelX, site.MapPixelY);
    }
}

internal sealed record DaggerfallTravelPresentation(
    IReadOnlyList<DaggerfallTravelDestination> Destinations,
    DaggerfallTravelQuote? Quote,
    string? Message);
