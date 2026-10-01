using Rusty.Engine.Mechanics;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Guilds;
using WorldRpg.Rulesets.Daggerfall.Presentation;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed partial class DaggerfallSession
{
    private int _lodgingDays = 1;
    internal DaggerfallLodgingView? LodgingView
    {
        get
        {
            if (_activeProfileKey.Kind != DaggerfallWorldProfileKind.Interior
                || CurrentInteriorBuilding() is not { BuildingType: DaggerfallLodgingState.TavernBuildingType } placed) return null;
            DaggerfallSiteBuildingId id = new(placed.BlockX, placed.BlockY, placed.Building.Index);
            DaggerfallSiteBuildingSource source = _site.RequireBuildingSource(_activeProfileKey.Site, id);
            bool free = DaggerfallConcreteGuildCatalog.All.Any(guild =>
            {
                var member = State.GuildMembership.Read(guild.FactionId, checked((int)_time.Calendar.DayNumber));
                return DaggerfallConcreteGuildPolicy.EvaluateService(guild, DaggerfallConcreteGuildService.FreeTavernRooms,
                    new DaggerfallGuildServiceContext(member.IsMember, member.Rank, CurrentRegion: _activeProfileKey.Site.Region)).Eligible;
            });
            ulong price = DaggerfallLodgingState.Quote(_lodgingDays, _time.Calendar, source.Quality,
                Math.Clamp(State.Actors.Player.Stats.GetStat(StatId.Parse("mercantile")).ValueInt, 0, 100),
                Math.Clamp(State.Actors.Player.Stats.GetStat(StatId.Parse("personality")).ValueInt, 0, 100), free);
            long now = _time.Calendar.ToAbsoluteSeconds();
            long remaining = State.Lodging.RemainingSeconds(_activeProfileKey.Site, id, now);
            return new(DaggerfallLodgingState.Key(_activeProfileKey.Site, id), _site.RequireBuilding(_activeProfileKey.Site, id).Name,
                _lodgingDays, price, (remaining + 3599) / 3600,
                State.Lodging.CanRent(_activeProfileKey.Site, id, _lodgingDays, now));
        }
    }
    private void ChangeLodging(DaggerfallPlayerUiAction action)
    {
        if (LodgingView is not { } previous || previous.Key != action.Key)
        { Presentation.SetOutcome("This tavern is no longer available."); return; }
        _lodgingDays = action.Days!.Value;
        DaggerfallLodgingView quote = LodgingView!;
        if (action.Kind == DaggerfallUiActionKind.LodgingQuote) return;
        if (!quote.CanBook) { Presentation.SetOutcome("You may rent a room for up to 350 days in total."); return; }
        if (action.Amount != quote.Price) { Presentation.SetOutcome("The room price changed. Please check the new quote."); return; }
        DaggerfallInteriorBuilding placed = CurrentInteriorBuilding()!;
        bool booked = State.Lodging.Book(_activeProfileKey.Site, new(placed.BlockX, placed.BlockY, placed.Building.Index),
            quote.Days, _time.Calendar.ToAbsoluteSeconds(), quote.Price, State.Currency);
        Presentation.SetOutcome(booked ? $"Room booked for {quote.Days} more day(s)." : "You do not have enough gold to rent this room.");
    }
}
