using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The donor's holiday announcement: what the calendar names for a site. The donor shows text
/// 8349 + holidayId on entering a city that keeps a holiday; the session announces on its first
/// observation of one and when the clock crosses midnight into one.
/// </summary>
public sealed class DaggerfallHolidayAnnouncementTests
{
    [Fact]
    public void Names_the_everywhere_holiday_with_its_text_record()
    {
        // New Life Festival is kept everywhere on the first day of the year.
        DaggerfallHolidayAnnouncement? announcement =
            DaggerfallHolidayAnnouncement.ForDate(new DaggerfallCalendar(405, 0, 0, 12, 0, 0), 17, DaggerfallSiteKind.TownCity);

        Assert.NotNull(announcement);
        Assert.Equal(1, announcement.HolidayId);
        Assert.Equal(new DaggerfallTextKey(DaggerfallTextKind.Resource, "8350"), announcement.TextKey);
        Assert.Equal(17, announcement.Region);
    }

    [Fact]
    public void Honors_the_everywhere_or_one_region_convention()
    {
        // The second holiday belongs to region index 24 alone.
        DaggerfallCalendar date = new(405, 0, 1, 12, 0, 0);
        Assert.Equal(2, DaggerfallHolidayAnnouncement.ForDate(date, 24, DaggerfallSiteKind.TownCity)?.HolidayId);
        Assert.Null(DaggerfallHolidayAnnouncement.ForDate(date, 17, DaggerfallSiteKind.TownCity));
    }

    [Fact]
    public void Stays_silent_only_where_the_donor_stays_silent()
    {
        DaggerfallCalendar holiday = new(405, 0, 0, 12, 0, 0);
        // Dungeons and graveyards get flavour text instead; covens and the ship get no entry text.
        Assert.Null(DaggerfallHolidayAnnouncement.ForDate(holiday, 17, DaggerfallSiteKind.DungeonLabyrinth));
        Assert.Null(DaggerfallHolidayAnnouncement.ForDate(holiday, 17, DaggerfallSiteKind.DungeonKeep));
        Assert.Null(DaggerfallHolidayAnnouncement.ForDate(holiday, 17, DaggerfallSiteKind.DungeonRuin));
        Assert.Null(DaggerfallHolidayAnnouncement.ForDate(holiday, 17, DaggerfallSiteKind.Graveyard));
        Assert.Null(DaggerfallHolidayAnnouncement.ForDate(holiday, 17, DaggerfallSiteKind.Coven));
        Assert.Null(DaggerfallHolidayAnnouncement.ForDate(holiday, 17, DaggerfallSiteKind.HomeYourShips));
        Assert.Null(DaggerfallHolidayAnnouncement.ForDate(holiday, 17, null));
        Assert.Null(DaggerfallHolidayAnnouncement.ForDate(holiday, null, DaggerfallSiteKind.TownCity));
        // Every other exterior location primes the announcement, not just the towns.
        Assert.NotNull(DaggerfallHolidayAnnouncement.ForDate(holiday, 17, DaggerfallSiteKind.TownCity));
        Assert.NotNull(DaggerfallHolidayAnnouncement.ForDate(holiday, 17, DaggerfallSiteKind.TownHamlet));
        Assert.NotNull(DaggerfallHolidayAnnouncement.ForDate(holiday, 17, DaggerfallSiteKind.TownVillage));
        Assert.NotNull(DaggerfallHolidayAnnouncement.ForDate(holiday, 17, DaggerfallSiteKind.HomeFarms));
        Assert.NotNull(DaggerfallHolidayAnnouncement.ForDate(holiday, 17, DaggerfallSiteKind.HomeWealthy));
        Assert.NotNull(DaggerfallHolidayAnnouncement.ForDate(holiday, 17, DaggerfallSiteKind.HomePoor));
        Assert.NotNull(DaggerfallHolidayAnnouncement.ForDate(holiday, 17, DaggerfallSiteKind.ReligionTemple));
        Assert.NotNull(DaggerfallHolidayAnnouncement.ForDate(holiday, 17, DaggerfallSiteKind.ReligionCult));
        Assert.NotNull(DaggerfallHolidayAnnouncement.ForDate(holiday, 17, DaggerfallSiteKind.Tavern));
        // An ordinary day names nothing anywhere.
        Assert.Null(DaggerfallHolidayAnnouncement.ForDate(new DaggerfallCalendar(405, 0, 5, 12, 0, 0), 17, DaggerfallSiteKind.TownCity));
    }
}
