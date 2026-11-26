using System.Text.Json;
using WorldRpg.Politics;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>
/// The political board's canon start and cards, read from the base pack (<c>world/iliac-bay.board.json</c> and
/// <c>world/iliac-bay.cards.json</c>). Every playthrough starts the board from this table with its canon seed.
/// </summary>
internal sealed record DaggerfallPoliticalCanon(BoardSetup Setup, CardSet Cards);

internal static partial class DaggerfallBaseContent
{
    private const int LastClassicRegion = 61;

    /// <summary>The canon board, or null for a base pack that carries none (then no board runs).</summary>
    private static DaggerfallPoliticalCanon? ReadPoliticalCanon(JsonElement root, DaggerfallBoardSeatSet seats, DaggerfallContentDiagnostics diagnostics)
    {
        bool hasBoard = root.TryGetProperty("politicalBoard", out JsonElement boardSection);
        bool hasCards = root.TryGetProperty("politicalCards", out JsonElement cardsSection);
        if (!hasBoard && !hasCards) return null;
        if (!hasBoard || !hasCards)
        {
            diagnostics.Add("The political board needs both its 'politicalBoard' and 'politicalCards' sections.");
            return null;
        }

        BoardSetup setup;
        CardSet cards;
        try
        {
            setup = BoardSetup.Read(boardSection.GetRawText(), "politicalBoard");
            cards = CardSet.Read(cardsSection.GetRawText(), "politicalCards");
        }
        catch (BoardSetupException exception)
        {
            diagnostics.Add(exception.Message);
            return null;
        }

        foreach (string problem in cards.Problems(setup)) diagnostics.Add($"Political cards: {problem}");
        foreach (RegionSetup region in setup.Regions)
            if (region.CatalogRegion is < 0 or > LastClassicRegion)
                diagnostics.Add($"Political board region '{region.Id}' names catalog region {region.CatalogRegion}, which is no classic region.");
        foreach (SeatSetup seat in setup.Seats)
            if (!seats.TryGetBySeatId(seat.Id, out _))
                diagnostics.Add($"Political board seat '{seat.Id}' has no board-seat binding to an office faction.");
        foreach (DaggerfallBoardSeatBinding binding in seats.All)
            if (!setup.Seats.Any(seat => seat.Id == binding.SeatId))
                diagnostics.Add($"Board-seat binding '{binding.SeatId}' names no seat of the political board.");
        return new DaggerfallPoliticalCanon(setup, cards);
    }
}
