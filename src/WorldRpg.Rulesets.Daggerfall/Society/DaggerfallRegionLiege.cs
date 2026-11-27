using WorldRpg.Politics;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// The crown each classic region's province answers to: its liege. A region keeps its own province, court, people and
/// law; what the political board changes is the liege, so the faction tree's province links come from the live board's
/// allegiance and every other link from the catalog (wiki <c>rusty-dagger/region-liege</c>). It keeps nothing of its
/// own and saves nothing: the board save holds allegiance. Before a board is bound (or in a game without one), and for
/// a classic region the board has no region for, the catalog's own parent stands, so Betony answers to Daggerfall.
/// </summary>
internal sealed class DaggerfallRegionLiege
{
    private const int ProvinceFactionType = 7;

    private readonly DaggerfallFactionsSet _factions;
    private readonly DaggerfallBoardSeatSet _seats;
    private readonly Dictionary<int, DaggerfallFactionDefinition> _provinces;
    private Board? _board;
    private Dictionary<int, Region> _boardRegions = [];

    internal DaggerfallRegionLiege(DaggerfallFactionsSet factions, DaggerfallBoardSeatSet seats)
    {
        _factions = factions ?? throw new ArgumentNullException(nameof(factions));
        _seats = seats ?? throw new ArgumentNullException(nameof(seats));
        _provinces = factions.Factions.Values
            .Where(faction => faction.Type == ProvinceFactionType && faction.Region >= 0)
            .GroupBy(faction => faction.Region)
            .ToDictionary(group => group.Key, group => group.MinBy(faction => faction.Id)!);
    }

    /// <summary>Reads allegiance from <paramref name="board"/> from now on (<see cref="DaggerfallPoliticalBoard"/> binds its own).</summary>
    internal void Bind(Board board)
    {
        _board = board ?? throw new ArgumentNullException(nameof(board));
        _boardRegions = board.Regions.ToDictionary(region => region.CatalogRegion);
    }

    /// <summary>The province faction of classic region <paramref name="region"/>: its ruler, which the region keeps whoever its liege.</summary>
    internal DaggerfallFactionDefinition? ProvinceOf(int region) => _provinces.GetValueOrDefault(region);

    /// <summary>The province of the crown classic region <paramref name="region"/> answers to, or null where it answers to none.</summary>
    internal DaggerfallFactionDefinition? LiegeOf(int region) =>
        ProvinceOf(region) is { } province && ParentOf(province) is > 0 and var liege ? _factions.Factions.GetValueOrDefault(liege) : null;

    /// <summary>
    /// The province of the crown <paramref name="region"/> answers to, or its own province where it answers to none: whom
    /// the region's oaths and homage name.
    /// </summary>
    internal DaggerfallFactionDefinition? SovereignOf(int region) => LiegeOf(region) ?? ProvinceOf(region);

    /// <summary>
    /// The liege <paramref name="region"/> took on with a board move still being consolidated (a crown's annexation, siege
    /// or cession, held uncontested for <see cref="Card.ConsolidationWeeks"/> weeks), or null where none is new.
    /// </summary>
    internal DaggerfallFactionDefinition? NewLiegeOf(int region) =>
        _board is not null && _boardRegions.TryGetValue(region, out Region? held) && _board.Turn < held.ContestableFrom ? LiegeOf(region) : null;

    /// <summary>
    /// The faction <paramref name="faction"/> answers to: for a region's province, its liege on the board (0 for none),
    /// and for every other faction its catalog parent.
    /// </summary>
    internal int ParentOf(DaggerfallFactionDefinition faction)
    {
        ArgumentNullException.ThrowIfNull(faction);
        if (faction.Type != ProvinceFactionType || !_boardRegions.TryGetValue(faction.Region, out Region? held)
            || ProvinceOf(faction.Region)?.Id != faction.Id)
            return faction.Parent;
        if (held.Allegiance is not { } seat || !_seats.TryGetBySeatId(seat, out DaggerfallBoardSeatBinding? crown)) return 0;
        return crown!.OfficeFactionId == faction.Id ? faction.Parent : crown.OfficeFactionId;
    }

    /// <summary>The faction <paramref name="factionId"/> answers to (<see cref="ParentOf(DaggerfallFactionDefinition)"/>), or 0 for an unknown one.</summary>
    internal int ParentOf(int factionId) =>
        _factions.Factions.TryGetValue(factionId, out DaggerfallFactionDefinition? faction) ? ParentOf(faction) : 0;

    /// <summary>
    /// The factions that answer to <paramref name="faction"/>: its catalog children, less any province whose liege is now
    /// another crown, then the provinces the board gave it as vassals, in region order.
    /// </summary>
    internal IEnumerable<int> ChildrenOf(DaggerfallFactionDefinition faction)
    {
        ArgumentNullException.ThrowIfNull(faction);
        foreach (int child in faction.Children)
            if (!_factions.Factions.TryGetValue(child, out DaggerfallFactionDefinition? definition) || ParentOf(definition) == faction.Id)
                yield return child;
        if (_board is null) yield break;
        foreach ((int region, _) in _boardRegions.OrderBy(pair => pair.Key))
            if (ProvinceOf(region) is { } vassal && !faction.Children.Contains(vassal.Id) && ParentOf(vassal) == faction.Id)
                yield return vassal.Id;
    }

    /// <summary>
    /// <paramref name="factionId"/> and every faction above it, nearest first, through <see cref="ParentOf(DaggerfallFactionDefinition)"/>:
    /// a court of an annexed region climbs through its province to the liege. Ends at a faction the catalog lacks or one
    /// already passed.
    /// </summary>
    internal IEnumerable<DaggerfallFactionDefinition> Lineage(int factionId)
    {
        HashSet<int> seen = [];
        for (int current = factionId; current > 0 && seen.Add(current); )
        {
            if (!_factions.Factions.TryGetValue(current, out DaggerfallFactionDefinition? faction)) yield break;
            yield return faction;
            current = ParentOf(faction);
        }
    }
}
