using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>One room privilege at an exact placed tavern, measured by the shared calendar.</summary>
internal sealed record DaggerfallRoomBooking(int Region, int SiteIndex, int BlockX, int BlockY, int BuildingIndex, long ExpiresAt)
{
    internal DaggerfallSiteId Site => new(Region, SiteIndex);
    internal DaggerfallSiteBuildingId Building => new(BlockX, BlockY, BuildingIndex);
    internal string Key => $"{Site}/{Building}";
    internal void Validate()
    {
        if (Region is < 0 or >= 62 || SiteIndex < 0 || BlockX < 0 || BlockY < 0 || BuildingIndex < 0 || ExpiresAt <= 0)
            throw new ArgumentException("A room booking must name a placed building and a positive calendar expiry.");
    }
}

internal sealed record DaggerfallLodgingSave(DaggerfallRoomBooking[] Rooms)
{
    internal static DaggerfallLodgingSave Empty { get; } = new([]);
    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Rooms);
        HashSet<string> keys = new(StringComparer.Ordinal);
        foreach (DaggerfallRoomBooking room in Rooms)
        {
            ArgumentNullException.ThrowIfNull(room);
            room.Validate();
            if (!keys.Add(room.Key)) throw new ArgumentException($"Lodging repeats room '{room.Key}'.");
        }
    }
    internal void Validate(DaggerfallLocationSet locations)
    {
        Validate();
        foreach (DaggerfallRoomBooking room in Rooms)
        {
            DaggerfallSiteRecord? site = locations.Records.SingleOrDefault(value => value.Id == room.Site);
            if (site?.Exterior is not { } exterior || !exterior.Buildings.TryGetValue(room.Building, out var building)
                || building.Source.BuildingType != DaggerfallLodgingState.TavernBuildingType)
                throw new ArgumentException($"Saved room '{room.Key}' does not resolve to an admitted tavern building.");
        }
    }
}

/// <summary>Classic rental duration and pricing; no independent clock or currency balance.</summary>
internal sealed class DaggerfallLodgingState
{
    internal const int TavernBuildingType = 15;
    internal const int MaximumDays = 350;
    private readonly Dictionary<string, DaggerfallRoomBooking> _rooms = new(StringComparer.Ordinal);
    internal DaggerfallLodgingState(DaggerfallLodgingSave? restored = null)
    {
        if (restored is null) return;
        restored.Validate();
        foreach (DaggerfallRoomBooking room in restored.Rooms) _rooms.Add(room.Key, room);
    }
    internal static string Key(DaggerfallSiteId site, DaggerfallSiteBuildingId building) => $"{site}/{building}";
    internal long RemainingSeconds(DaggerfallSiteId site, DaggerfallSiteBuildingId building, long now) =>
        _rooms.TryGetValue(Key(site, building), out var room) ? Math.Max(0, room.ExpiresAt - now) : 0;
    internal bool CanRent(DaggerfallSiteId site, DaggerfallSiteBuildingId building, int days, long now) =>
        days is >= 1 and <= MaximumDays && days + RemainingSeconds(site, building, now) / DaggerfallCalendar.SecondsPerDay <= MaximumDays;
    internal bool Book(DaggerfallSiteId site, DaggerfallSiteBuildingId building, int days,
        long now, ulong price, DaggerfallCurrencyService currency)
    {
        if (!CanRent(site, building, days, now)) return false;
        long expiry = checked(now + RemainingSeconds(site, building, now) + (long)days * DaggerfallCalendar.SecondsPerDay);
        DaggerfallRoomBooking room = new(site.Region, site.Index, building.BlockX, building.BlockY, building.Index, expiry);
        room.Validate();
        if (price != 0 && !currency.TrySpendCarried(price)) return false;
        _rooms[room.Key] = room;
        return true;
    }
    internal DaggerfallLodgingSave Capture(long now)
    {
        foreach (string key in _rooms.Where(value => value.Value.ExpiresAt <= now).Select(value => value.Key).ToArray()) _rooms.Remove(key);
        return new([.. _rooms.Values.OrderBy(value => value.Key, StringComparer.Ordinal)]);
    }
    /// <summary>FormulaHelper.CalculateRoomCost: seven gold per day, one free Heart's Day when crossed.</summary>
    internal static int CalculateRoomCost(int days, int dayOfYear)
    {
        if (days is < 1 or > MaximumDays || dayOfYear is < 1 or > DaggerfallCalendar.DaysPerYear)
            throw new ArgumentOutOfRangeException(nameof(days));
        return 7 * (dayOfYear <= 46 && dayOfYear + days > 46 ? days - 1 : days);
    }
    internal static ulong Quote(int days, DaggerfallCalendar calendar, int quality, int mercantile, int personality, bool free)
    {
        int cost = CalculateRoomCost(days, calendar.DayOfYear);
        return free ? 0 : checked((ulong)DaggerfallRegionalEconomyPolicy.CalculateTradePrice(
            cost, quality, false, mercantile, personality, 0));
    }
}

internal sealed record DaggerfallLodgingView(string Key, string Name, int Days, ulong Price, long RemainingHours, bool CanBook);
