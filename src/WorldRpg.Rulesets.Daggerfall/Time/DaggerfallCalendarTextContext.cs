using System.Globalization;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using static WorldRpg.Rulesets.Daggerfall.Presentation.DaggerfallTextContexts;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>The calendar's values formatted for the donor's date and time text macros.</summary>
internal static class DaggerfallCalendarTextContext
{
    internal static DaggerfallTextCalendarContext For(DaggerfallDefinitions definitions, DaggerfallCalendar calendar)
    {
        int day = calendar.Day + 1;
        string suffix = DaggerfallCalendar.OrdinalSuffix(day);
        string dayName = calendar.DayName;
        string month = calendar.MonthName;
        string season = List(definitions, "seasonNames")[calendar.Season switch
            { DaggerfallSeason.Spring => 1, DaggerfallSeason.Summer => 2, DaggerfallSeason.Autumn => 0, _ => 3 }];
        return new(Date: string.Format(CultureInfo.InvariantCulture, Internal(definitions, "dateFormatString"), dayName, day, suffix, month),
            Time: calendar.DescribeTime(), DayNumber: Number(day), DayName: dayName,
            DayWithSuffix: Number(day) + suffix, MonthNumber: Number(calendar.Month + 1), MonthName: month,
            Year: Number(calendar.Year), Minute: Number(calendar.Minute), Hour: Number(calendar.Hour),
            Sign: List(definitions, "birthSignNames")[calendar.Month], Season: season);
    }
}
