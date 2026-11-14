using Rusty.Engine.Mechanics;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Guilds;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// Renting a room at the tavern the player stands in: its quote for the days the player chose (free to a guild
/// member whose hall grants free rooms) and the booking. The bookings are the lodging state's; the chosen days are
/// presentation and are not saved.
/// </summary>
/// <param name="building">The admitted interior building the player stands in.</param>
internal sealed class DaggerfallRoomRental(
    DaggerfallLodgingState lodging,
    DaggerfallSiteContext site,
    DaggerfallGuildMembershipPolicy guildMembership,
    StatsComponent player,
    DaggerfallCurrencyService currency,
    Func<DaggerfallCalendar> calendar,
    Func<DaggerfallWorldProfileKey> activeProfile,
    Func<DaggerfallInteriorBuilding?> building)
{
    private int _days = 1;

    /// <summary>The room at the tavern the player stands in, quoted for the chosen days; none outside a tavern.</summary>
    internal DaggerfallLodgingView? View
    {
        get
        {
            DaggerfallWorldProfileKey profile = activeProfile();
            if (profile.Kind != DaggerfallWorldProfileKind.Interior
                || building() is not { BuildingType: DaggerfallLodgingState.TavernBuildingType } placed) return null;
            DaggerfallSiteBuildingId id = new(placed.BlockX, placed.BlockY, placed.Building.Index);
            DaggerfallSiteBuildingSource source = site.RequireBuildingSource(profile.Site, id);
            DaggerfallCalendar now = calendar();
            bool free = DaggerfallConcreteGuildCatalog.All.Any(guild =>
            {
                var member = guildMembership.Read(guild.FactionId, checked((int)now.DayNumber));
                return DaggerfallConcreteGuildPolicy.EvaluateService(guild, DaggerfallConcreteGuildService.FreeTavernRooms,
                    new DaggerfallGuildServiceContext(member.IsMember, member.Rank, CurrentRegion: profile.Site.Region)).Eligible;
            });
            ulong price = DaggerfallLodgingState.Quote(_days, now, source.Quality,
                Math.Clamp(player.GetStat(StatId.Parse("mercantile")).ValueInt, 0, 100),
                Math.Clamp(player.GetStat(StatId.Parse("personality")).ValueInt, 0, 100), free);
            long seconds = now.ToAbsoluteSeconds();
            long remaining = lodging.RemainingSeconds(profile.Site, id, seconds);
            return new(DaggerfallLodgingState.Key(profile.Site, id), site.RequireBuilding(profile.Site, id).Name,
                _days, price, (remaining + 3599) / 3600,
                lodging.CanRent(profile.Site, id, _days, seconds));
        }
    }

    /// <summary>
    /// Chooses the days to rent the room with this key for and, when <paramref name="book"/>, books it at the quoted
    /// <paramref name="price"/>. Returns the player's line, or none for a quote alone.
    /// </summary>
    internal string? Choose(string? key, int days, bool book, ulong? price)
    {
        if (View is not { } previous || previous.Key != key) return "This tavern is no longer available.";
        _days = days;
        DaggerfallLodgingView quote = View!;
        if (!book) return null;
        if (!quote.CanBook) return "You may rent a room for up to 350 days in total.";
        if (price != quote.Price) return "The room price changed. Please check the new quote.";
        DaggerfallInteriorBuilding placed = building()!;
        bool booked = lodging.Book(activeProfile().Site, new(placed.BlockX, placed.BlockY, placed.Building.Index),
            quote.Days, calendar().ToAbsoluteSeconds(), quote.Price, currency);
        return booked ? $"Room booked for {quote.Days} more day(s)." : "You do not have enough gold to rent this room.";
    }
}
