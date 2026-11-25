using System.Text.Json;
using System.Text.Json.Serialization;

namespace WorldRpg.Politics;

/// <summary>
/// A board's starting position: its canon seed, the seats at the table, the regions and the relations between
/// seats. The canon Iliac Bay start is <c>canon/iliac-bay.board.json</c> beside this library.
/// </summary>
public sealed record BoardSetup(
    ulong Seed,
    IReadOnlyList<SeatSetup> Seats,
    IReadOnlyList<RegionSetup> Regions,
    IReadOnlyList<RelationSetup> Relations)
{
    /// <summary>The canon start's file name in the build output.</summary>
    public const string CanonFile = "canon/iliac-bay.board.json";

    /// <summary>The canon Iliac Bay start, read from beside this library.</summary>
    public static BoardSetup Canon() => Load(Path.Combine(AppContext.BaseDirectory, CanonFile));

    /// <summary>Reads and validates a board file; a malformed or inconsistent one is refused with every problem named.</summary>
    public static BoardSetup Load(string path)
    {
        BoardSetup? setup;
        try
        {
            setup = JsonSerializer.Deserialize(File.ReadAllBytes(path), PoliticsJson.Default.BoardSetup);
        }
        catch (JsonException exception)
        {
            throw new BoardSetupException(path, [exception.Message]);
        }
        if (setup is null) throw new BoardSetupException(path, ["the file holds no board"]);
        IReadOnlyList<string> problems = setup.Problems();
        if (problems.Count > 0) throw new BoardSetupException(path, problems);
        return setup;
    }

    /// <summary>Everything wrong with this setup; an empty list means a board can be made from it.</summary>
    public IReadOnlyList<string> Problems()
    {
        List<string> problems = [];
        HashSet<string> seats = new(StringComparer.Ordinal);
        HashSet<string> regions = new(StringComparer.Ordinal);
        foreach (SeatSetup seat in Seats)
        {
            if (!seats.Add(seat.Id)) problems.Add($"seat '{seat.Id}' is listed twice");
            foreach ((string name, int value) in new[] { ("coin", seat.Coin), ("favour", seat.Favour), ("force", seat.Force) })
                if (value is < 0 or > Board.ResourceLimit) problems.Add($"seat '{seat.Id}' {name} {value} is outside 0..{Board.ResourceLimit}");
            foreach (SeatGrant grant in seat.Grants ?? [])
            {
                if (grant.Amount is < 1 or > Board.ResourceLimit) problems.Add($"seat '{seat.Id}' grant of {grant.Amount} {grant.Resource} is outside 1..{Board.ResourceLimit}");
                if (grant.EveryWeeks < 1) problems.Add($"seat '{seat.Id}' grant of {grant.Resource} every {grant.EveryWeeks} weeks is not a schedule");
            }
        }
        Dictionary<string, SeatKind> kinds = Seats.GroupBy(seat => seat.Id).ToDictionary(group => group.Key, group => group.First().Kind, StringComparer.Ordinal);
        foreach (RegionSetup region in Regions)
        {
            if (!regions.Add(region.Id)) problems.Add($"region '{region.Id}' is listed twice");
            foreach ((string name, int value) in new[] { ("control", region.Control), ("prosperity", region.Prosperity), ("order", region.Order) })
                if (value is < 0 or > Board.TrackLimit) problems.Add($"region '{region.Id}' {name} {value} is outside 0..{Board.TrackLimit}");
            if (region.Allegiance is not null && !seats.Contains(region.Allegiance))
                problems.Add($"region '{region.Id}' owes allegiance to unknown seat '{region.Allegiance}'");
            else if (region.Allegiance is not null && kinds[region.Allegiance] != SeatKind.Crown)
                problems.Add($"region '{region.Id}' owes allegiance to '{region.Allegiance}', which is not a crown");
            if (region.Capital && region.Allegiance is null) problems.Add($"capital '{region.Id}' owes allegiance to no seat");
            foreach ((string seat, int pieces) in region.Influence)
            {
                if (!seats.Contains(seat)) problems.Add($"region '{region.Id}' holds influence of unknown seat '{seat}'");
                if (pieces is < 0 or > Board.InfluenceLimit) problems.Add($"region '{region.Id}' influence of '{seat}' {pieces} is outside 0..{Board.InfluenceLimit}");
            }
        }
        foreach (RegionSetup region in Regions)
        {
            foreach (string border in region.Borders)
            {
                RegionSetup? other = Regions.FirstOrDefault(candidate => candidate.Id == border);
                if (other is null) problems.Add($"region '{region.Id}' borders unknown region '{border}'");
                else if (other == region) problems.Add($"region '{region.Id}' borders itself");
                else if (!other.Borders.Contains(region.Id)) problems.Add($"region '{region.Id}' borders '{border}', which does not list it back");
            }
        }
        foreach (SeatSetup seat in Seats)
        {
            if (seat.Kind == SeatKind.Power)
            {
                if (seat.Home is not null) problems.Add($"seat '{seat.Id}' is a power and holds no capital, but names home '{seat.Home}'");
                continue;
            }
            RegionSetup? home = Regions.FirstOrDefault(region => region.Id == seat.Home);
            if (seat.Home is null) problems.Add($"crown '{seat.Id}' has no home capital");
            else if (home is null) problems.Add($"seat '{seat.Id}' has unknown home region '{seat.Home}'");
            else if (!home.Capital || home.Allegiance != seat.Id) problems.Add($"seat '{seat.Id}' home '{seat.Home}' is not a capital it holds");
        }
        foreach (RegionSetup capital in Regions.Where(region => region.Capital))
            if (!Seats.Any(seat => seat.Home == capital.Id)) problems.Add($"capital '{capital.Id}' is no seat's home");
        HashSet<(string, string)> pairs = [];
        foreach (RelationSetup relation in Relations)
        {
            if (!seats.Contains(relation.A) || !seats.Contains(relation.B)) problems.Add($"relation {relation.A}/{relation.B} names an unknown seat");
            else if (relation.A == relation.B) problems.Add($"relation {relation.A}/{relation.B} relates a seat to itself");
            else if (!pairs.Add(Board.Pair(relation.A, relation.B))) problems.Add($"relation {relation.A}/{relation.B} is listed twice");
            if (relation.Value is < -Board.RelationLimit or > Board.RelationLimit)
                problems.Add($"relation {relation.A}/{relation.B} {relation.Value} is outside -{Board.RelationLimit}..{Board.RelationLimit}");
        }
        return problems;
    }
}

/// <summary>
/// A seat at the table: who plays, its kind, the cast individual holding it now (none where the head of the body is
/// not cast), the home capital a crown holds, its starting resources and the grants it is paid on a schedule.
/// </summary>
public sealed record SeatSetup(
    string Id,
    string Name,
    SeatKind Kind,
    string? Holder,
    string? Home,
    int Coin,
    int Favour,
    int Force,
    IReadOnlyList<SeatGrant>? Grants = null);

/// <summary>
/// A resource paid to a seat in upkeep every <paramref name="EveryWeeks"/> weeks, on top of its income: what makes a
/// seat's standing its own, such as the Blades' steady supply of agents.
/// </summary>
public sealed record SeatGrant(Resource Resource, int Amount, int EveryWeeks);

/// <summary>
/// A region on the board. <paramref name="CatalogRegion"/> is the region's index in the Daggerfall catalog, which the
/// session uses to place the board's conditions in the world. <paramref name="Borders"/> lists the regions it touches
/// by land or a short sea crossing; a border is listed on both sides.
/// </summary>
public sealed record RegionSetup(
    string Id,
    string Name,
    int CatalogRegion,
    bool Capital,
    string? Allegiance,
    int Control,
    int Prosperity,
    int Order,
    IReadOnlyDictionary<string, int> Influence,
    IReadOnlyList<string> Borders);

/// <summary>The standing between two seats, from -3 (at war) to +3 (pact); an unlisted pair starts at 0.</summary>
public sealed record RelationSetup(string A, string B, int Value);

/// <summary>A board file that cannot make a board, with every problem found in it.</summary>
public sealed class BoardSetupException(string path, IReadOnlyList<string> problems)
    : Exception($"{path} is not a valid board:{Environment.NewLine}  {string.Join(Environment.NewLine + "  ", problems)}")
{
    public IReadOnlyList<string> Problems { get; } = problems;
}
