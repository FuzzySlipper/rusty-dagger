using System.Text.Json;

namespace WorldRpg.Politics;

/// <summary>
/// The whole board in play, as saved between Sundays: everything that changes as it plays, read back against the setup
/// and card set it was made from (<see cref="Board.Save"/>, <see cref="Board.Load"/>). Cards are named by id. A game
/// saved and resumed plays on exactly as one never stopped.
/// </summary>
public sealed record BoardSave(
    int Turn,
    ulong Random,
    string? Leader,
    IReadOnlyList<SeatSave> Seats,
    IReadOnlyList<RegionSave> Regions,
    IReadOnlyList<RelationSetup> Relations,
    IReadOnlyList<PendingMoveSave> Pending,
    IReadOnlyList<Agreement> Agreements,
    int DealsMade,
    IReadOnlyList<War> Wars,
    int WarsMade,
    IReadOnlyList<Truce> Truces,
    IReadOnlyList<Feud> Feuds,
    int FeudsMade,
    IReadOnlyList<string> WorldDraw,
    IReadOnlyList<string> WorldDiscard,
    IReadOnlyList<HelpSave> Helps,
    IReadOnlyList<WorldNudge> Queued,
    IReadOnlyList<OpenArc> Arcs)
{
    /// <summary>The save as JSON.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, PoliticsRecordJson.Default.BoardSave);

    /// <summary>A save read from JSON; a malformed one is refused, naming what is wrong (<paramref name="source"/> names where it came from).</summary>
    public static BoardSave FromJson(string json, string source)
    {
        try
        {
            return JsonSerializer.Deserialize(json, PoliticsRecordJson.Default.BoardSave) ?? throw new BoardSetupException(source, ["the save holds no board"]);
        }
        catch (JsonException exception)
        {
            throw new BoardSetupException(source, [exception.Message]);
        }
    }
}

/// <summary>A seat's changing state: its holder (and under a regency whom it plays as), plan, grudges, resources, court, reserves, boons and cards.</summary>
public sealed record SeatSave(
    string Id,
    string Holder,
    string? PlaysAs,
    Plan? Plan,
    IReadOnlyList<Grudge> Grudges,
    int Coin,
    int Favour,
    int Force,
    IReadOnlyList<CourtCondition> Court,
    int ContestedWeeks,
    IReadOnlyList<string> Boons,
    IReadOnlyList<string> DrawPile,
    IReadOnlyList<string> Hand,
    IReadOnlyList<string> Discard);

/// <summary>A region's changing state.</summary>
public sealed record RegionSave(
    string Id,
    string? Allegiance,
    int Control,
    int Prosperity,
    int Order,
    int ContestableFrom,
    IReadOnlyDictionary<string, int> Influence,
    IReadOnlyDictionary<Office, string> Offices,
    IReadOnlyList<RegionCondition> Conditions,
    RegionSupply Supply,
    int CutWeeks,
    int StrainWeeks);

/// <summary>A slow move under way, its card named by id.</summary>
public sealed record PendingMoveSave(string Id, string Seat, string Card, IReadOnlyList<string> Targets, string? Origin, int Played, int Arrives);

/// <summary>How many times <paramref name="Helper"/> has helped <paramref name="Helped"/> (<see cref="Memory"/>).</summary>
public sealed record HelpSave(string Helper, string Helped, int Count);
