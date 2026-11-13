using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The holiday owner on its own, with no session: a kept holiday announces once, on its first observation, and
/// leaving it is silent.
/// </summary>
public sealed class DaggerfallHolidaysTests
{
    // New Life Festival is kept everywhere on the first day of the year; the second day keeps none in region 17.
    private static readonly DaggerfallCalendar NewLife = new(405, 0, 0, 12, 0, 0);
    private static readonly DaggerfallCalendar Ordinary = new(405, 0, 1, 12, 0, 0);
    private static readonly DaggerfallTextKey NewLifeText = new(DaggerfallTextKind.Resource, "8350");

    [Fact]
    public void A_kept_holiday_announces_its_line_once()
    {
        DaggerfallHolidays holidays = new();
        DaggerfallTextResolver text = Text((NewLifeText, "New Life Festival"));

        Assert.Null(holidays.Announcement);
        Assert.Equal("New Life Festival", holidays.Observe(NewLife, 17, DaggerfallSiteKind.TownCity, text));
        Assert.Equal(1, holidays.Announcement?.HolidayId);
        Assert.Null(holidays.Observe(NewLife with { Hour = 18 }, 17, DaggerfallSiteKind.TownCity, text));
        Assert.Equal(1, holidays.Announcement?.HolidayId);
    }

    [Fact]
    public void Leaving_a_holiday_is_silent_and_a_return_announces_again()
    {
        DaggerfallHolidays holidays = new();
        DaggerfallTextResolver text = Text((NewLifeText, "New Life Festival"));
        _ = holidays.Observe(NewLife, 17, DaggerfallSiteKind.TownCity, text);

        Assert.Null(holidays.Observe(Ordinary, 17, DaggerfallSiteKind.TownCity, text));
        Assert.Null(holidays.Announcement);
        Assert.Null(holidays.Observe(NewLife, 17, DaggerfallSiteKind.DungeonKeep, text));
        Assert.Equal("New Life Festival", holidays.Observe(NewLife, 17, DaggerfallSiteKind.TownVillage, text));
    }

    private static DaggerfallTextResolver Text(params (DaggerfallTextKey Key, string Text)[] lines) => new(new DaggerfallTextSet(
        lines.ToDictionary(line => line.Key, line => new DaggerfallTextValue(line.Key, "test", "en", 0, 0, line.Text.Length, 1,
            DaggerfallTextState.Read, string.Empty, [], [new(DaggerfallTextCode.Text, line.Text, null, null)])), []));
}
