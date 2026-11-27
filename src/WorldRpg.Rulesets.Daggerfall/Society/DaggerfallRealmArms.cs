using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// The arms a region shows (#11144): its liege's, read live from the board through <see cref="DaggerfallRegionLiege"/>,
/// or its own where it answers to no one. A crown's home region shows its crown's. Banners, the map's Realms view and the
/// livery of the guards who serve the liege all read this, so a change of liege on the board's Sunday reaches them all.
/// It keeps nothing.
/// </summary>
internal sealed class DaggerfallRealmArms(DaggerfallRegionLiege liege, DaggerfallHeraldry heraldry)
{
    /// <summary>The heraldry the arms come from.</summary>
    internal DaggerfallHeraldry Heraldry => heraldry;

    /// <summary>The arms classic region <paramref name="region"/> shows, or null for a region the board has no region for.</summary>
    internal DaggerfallArms? Of(int region) =>
        liege.LiegeSeatOf(region) is { } seat && heraldry.Seats.TryGetValue(seat, out DaggerfallArms? crown) ? crown : Own(region);

    /// <summary>Classic region <paramref name="region"/>'s own arms, whoever it answers to; null where the board has no region for it.</summary>
    internal DaggerfallArms? Own(int region) =>
        liege.BoardRegion(region) is { } held && heraldry.Regions.TryGetValue(held.Id, out DaggerfallArms? arms) ? arms : null;

    /// <summary>The board seat classic region <paramref name="region"/> answers to, or null where it answers to no one.</summary>
    internal string? LiegeSeatOf(int region) => liege.LiegeSeatOf(region);

    /// <summary>The arms of board seat <paramref name="seat"/>, or null for a seat that bears none.</summary>
    internal DaggerfallArms? OfSeat(string seat) => heraldry.Seats.GetValueOrDefault(seat);
}
