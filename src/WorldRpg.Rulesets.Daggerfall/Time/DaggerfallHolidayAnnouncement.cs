using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall.World;

/// <summary>
/// The donor's holiday announcement for one date and site: the holiday the calendar names for the
/// site's region, and the text record that carries its words.
/// </summary>
/// <remarks>
/// The donor shows this on entering an exterior location while a holiday is kept there
/// (<c>PlayerEnterExit.ShowHolidayText</c> reads the current region's holiday and shows text
/// <c>8349 + holidayId</c> when it is non-zero). Dungeons and graveyards get flavour text instead,
/// and covens and the player's ship get no entry text at all, so those four groups stay silent
/// here too. The product has no location transitions yet, so the entry condition is the site's
/// kind, and the announcement also fires when the clock crosses midnight into a holiday while the
/// session stands at an eligible site, which the donor's entry-only check cannot observe. The text
/// resolves through the text set with its macros unexpanded; expansion is the substitution owner's
/// work, not this fact's.
/// </remarks>
/// <param name="HolidayId">The one-based holiday identity the calendar names.</param>
/// <param name="TextKey">The text key carrying the holiday's words.</param>
/// <param name="Region">The zero-based region index whose calendar names it.</param>
internal sealed record DaggerfallHolidayAnnouncement(int HolidayId, DaggerfallTextKey TextKey, int Region)
{
    /// <summary>The first text record carrying holiday words; the donor addresses holidays from here.</summary>
    public const int HolidaysStartRecord = 8349;

    /// <summary>
    /// Computes the announcement for a date and site, or null when there is none to make: no region,
    /// no eligible site, or no holiday kept there that day.
    /// </summary>
    internal static DaggerfallHolidayAnnouncement? ForDate(DaggerfallCalendar date, int? region, DaggerfallSiteKind? kind)
    {
        if (region is not { } celebrated || kind is not { } entered || !IsHolidayEligible(entered))
        {
            return null;
        }

        int holidayId = date.GetHolidayId(celebrated);
        if (holidayId == 0)
        {
            return null;
        }

        return new DaggerfallHolidayAnnouncement(
            holidayId,
            new DaggerfallTextKey(DaggerfallTextKind.Resource, (HolidaysStartRecord + holidayId).ToString(System.Globalization.CultureInfo.InvariantCulture)),
            celebrated);
    }

    /// <summary>
    /// Whether the donor primes holiday text on entering a site of this kind: every exterior
    /// location except covens and the player's ship, with dungeons and graveyards routed to
    /// flavour text instead.
    /// </summary>
    private static bool IsHolidayEligible(DaggerfallSiteKind kind) => kind is not
        DaggerfallSiteKind.DungeonLabyrinth
        and not DaggerfallSiteKind.DungeonKeep
        and not DaggerfallSiteKind.DungeonRuin
        and not DaggerfallSiteKind.Graveyard
        and not DaggerfallSiteKind.Coven
        and not DaggerfallSiteKind.HomeYourShips;
}
