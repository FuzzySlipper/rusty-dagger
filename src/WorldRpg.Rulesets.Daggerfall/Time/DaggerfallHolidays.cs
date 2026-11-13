using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;

namespace WorldRpg.Rulesets.Daggerfall.World;

/// <summary>
/// The holiday kept today where the player stands, and whether it has been announced: the donor shows a
/// holiday's text once, on entering an eligible location and after loading.
/// </summary>
/// <remarks>
/// The session asks on the first playing update, on every date change after it and when a synthetic time
/// increase (a rest, a journey, a service) ends, handing in the calendar and the site. The tracking starts
/// unannounced, so the first observation of a kept holiday is itself the entry. A dungeon, a graveyard, a
/// coven or the player's ship never announces, and leaving a holiday clears the tracking silently rather than
/// reporting the ordinary day. Nothing here is saved.
/// </remarks>
internal sealed class DaggerfallHolidays
{
    private int _lastHolidayId;

    /// <summary>
    /// The holiday announcement the calendar currently names for the site, or null when the site is no
    /// settlement or no holiday is kept there today.
    /// </summary>
    internal DaggerfallHolidayAnnouncement? Announcement { get; private set; }

    /// <summary>
    /// Observes the date at a site. Returns the holiday's line when the calendar names a different holiday there
    /// than it last did, and null otherwise, including when a holiday has just ended.
    /// </summary>
    internal string? Observe(DaggerfallCalendar date, int? region, DaggerfallSiteKind? siteKind, DaggerfallTextResolver text)
    {
        DaggerfallHolidayAnnouncement? announcement = DaggerfallHolidayAnnouncement.ForDate(date, region, siteKind);
        int holidayId = announcement?.HolidayId ?? 0;
        if (holidayId == _lastHolidayId) return null;

        _lastHolidayId = holidayId;
        Announcement = announcement;
        return announcement is null ? null : text.Resolve(announcement.TextKey, DaggerfallTextContext.Empty).Text;
    }
}
