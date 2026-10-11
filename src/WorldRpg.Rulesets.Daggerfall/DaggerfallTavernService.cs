using Rusty.Engine.Mechanics;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed record DaggerfallTavernOption(string Id, string Label, ulong Price, int Health);
internal sealed record DaggerfallTavernView(bool Hungry, IReadOnlyList<DaggerfallTavernOption> Menu);

/// <summary>Classic tavern purchases; the shared service owner debits gold and the calendar owns the four-hour interval.</summary>
internal sealed class DaggerfallTavernService(DaggerfallServiceTransactions services, StatsComponent player,
    Func<DaggerfallCalendar> calendar, long? lastMealMinute = null)
{
    internal const string ServiceName = "food-drink";
    internal long? LastMealMinute { get; private set; } = lastMealMinute is null or >= 0 ? lastMealMinute : throw new ArgumentOutOfRangeException(nameof(lastMealMinute));
    private static readonly (string Id, int Price)[] Menu = [("ale",1),("beer",1),("mead",2),("wine",3),
        ("bread",1),("broth",1),("cheese",2),("fowl",3),("gruel",2),("pie",2),("stew",3)];
    internal DaggerfallTavernView Read()
    {
        int holiday = calendar().GetHolidayId(0);
        return new(LastMealMinute is null || calendar().ToAbsoluteSeconds() / 60 - LastMealMinute >= 240,
            Menu.Select(item =>
            {
                int price = holiday == 37 ? Math.Max(1, item.Price >> 1) : item.Price;
                return new DaggerfallTavernOption(item.Id, char.ToUpperInvariant(item.Id[0]) + item.Id[1..],
                    holiday == 1 ? 0UL : (ulong)price, 2 * price);
            }).ToArray());
    }
    internal string Buy(DaggerfallServiceRequest request, string item, ulong expectedPrice)
    {
        var view = Read();
        if (!view.Hungry) return "You are not hungry.";
        var option = view.Menu.SingleOrDefault(value => value.Id == item);
        if (option is null || option.Price != expectedPrice) return "The tavern menu changed. Check the current price.";
        var quote = services.Quote(request, new(false), new(option.Price));
        if (quote.Quote is null) return "The purchase was refused. Check your gold and the tavern keeper.";
        var result = services.Commit(quote.Quote);
        if (!result.Accepted) return "The purchase was refused. Check your gold and the tavern keeper.";
        var health = player.GetTrack(TrackId.Parse("health"));
        health.SetCurrent(Math.Min(health.Maximum.Value, health.Current + option.Health));
        LastMealMinute = calendar().ToAbsoluteSeconds() / 60;
        return $"You enjoy the {option.Label.ToLowerInvariant()}.";
    }
}
