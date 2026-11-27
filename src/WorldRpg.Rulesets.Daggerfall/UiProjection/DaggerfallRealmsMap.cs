using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Travel;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>A board region on the Realms view: its classic region, the arms it shows and their colour, and whether a liege's.</summary>
internal sealed record DaggerfallRealmShade(int Region, string Arms, string Colour, bool Liege);

/// <summary>One line of the Realms view's key: a crown's arms, its name, its blazon and its colour.</summary>
internal sealed record DaggerfallRealmKey(string Arms, string Name, string Blazon, string Colour);

/// <summary>
/// The world map's Realms view (#11144, Patch: a toggled view): each board region's shape, shaded by the arms it shows
/// now, with a key of the crowns that hold regions. A region that answers to a crown takes the crown's colour; one that
/// answers to no one shows its own, faintly. The page toggles the view and draws it; this is read fresh each time.
/// </summary>
internal sealed record DaggerfallRealmsView(IReadOnlyList<DaggerfallRegionOutline> Outlines, IReadOnlyList<DaggerfallRealmShade> Regions,
    IReadOnlyList<DaggerfallRealmKey> Key);

/// <summary>Reads the Realms view from the live liege (<see cref="DaggerfallRealmArms"/>) over the board's regions.</summary>
internal sealed class DaggerfallRealmsMap(DaggerfallDefinitions definitions, DaggerfallRealmArms arms)
{
    private IReadOnlyList<DaggerfallRegionOutline>? _outlines;

    internal DaggerfallRealmsView? Read()
    {
        if (definitions.PoliticalCanon is not { } canon) return null;
        HashSet<int> boardRegions = [.. canon.Setup.Regions.Select(region => region.CatalogRegion)];
        _outlines ??= [.. DaggerfallRegionOutlines.Trace(definitions.Grids.Politic, DaggerfallTravelMapPixel.Width, DaggerfallTravelMapPixel.Height)
            .Where(outline => boardRegions.Contains(outline.Region))];
        DaggerfallHeraldry heraldry = arms.Heraldry;
        List<DaggerfallRealmShade> shades = [];
        foreach (var region in canon.Setup.Regions.OrderBy(region => region.CatalogRegion))
            if (arms.Of(region.CatalogRegion) is { } shown)
                shades.Add(new(region.CatalogRegion, shown.Id, DaggerfallHeraldry.Hex(heraldry.Colour(shown.Livery)), arms.LiegeSeatOf(region.CatalogRegion) is not null));
        DaggerfallRealmKey[] key = [.. canon.Setup.Seats
            .Select(seat => (seat, crown: arms.OfSeat(seat.Id)))
            .Where(pair => pair.crown is { } crown && shades.Any(shade => shade.Liege && shade.Arms == crown.Id))
            .Select(pair => new DaggerfallRealmKey(pair.crown!.Id, pair.seat.Name, pair.crown.Blazon, DaggerfallHeraldry.Hex(heraldry.Colour(pair.crown.Livery))))];
        return new(_outlines, shades, key);
    }
}
