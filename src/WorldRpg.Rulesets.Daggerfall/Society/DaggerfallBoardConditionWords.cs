using WorldRpg.Politics;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// What the towns say of each board condition, on notice boards and wherever a token's own words are shown. Each names
/// the symptom, never a secret move's true seat: only what is public in itself is named, as a war's two sides, a
/// persecuted guild or a treaty's partner. A monster surge keeps the bounty wording its creature family gives it.
/// </summary>
internal static class DaggerfallBoardConditionWords
{
    /// <summary>The headline and body of <paramref name="condition"/> on <paramref name="region"/>, or null to keep the kind's own wording.</summary>
    internal static (string Headline, string Body)? Of(Board board, Region region, RegionCondition condition)
    {
        string r = region.Name;
        (string? first, string? second) = condition.Detail?.Split(',') is [var a, var b] ? (SeatName(board, a), SeatName(board, b)) : (null, null);
        return condition.Kind switch
        {
            ConditionKind.Famine => ($"Famine in {r}", $"The harvest has failed across {r}. Bread is dear, and the poor go hungry."),
            ConditionKind.PriceSurge when condition.Detail == "grain" => ($"Grain Dear in {r}", $"Grain prices have leapt in the markets of {r}."),
            ConditionKind.PriceSurge => ($"{Capitalize(condition.Detail ?? "goods")} Dear in {r}",
                $"Merchants across {r} are asking far more for {condition.Detail ?? "their goods"} than they did, and the rest of their wares follow."),
            ConditionKind.Raiders => ($"Raiders on the Roads of {r}", $"Raiders strike the farms and wagons of {r}. Grain runs short, and travellers go armed."),
            ConditionKind.Plague => ($"Sickness in {r}", $"A sickness spreads through {r}. The temples are full, and the gates watch for the fevered."),
            ConditionKind.MonsterSurge => null,
            ConditionKind.WarFront => ($"War Along the Borders of {r}",
                $"The armies of {first ?? "two crowns"}{(second is null ? "" : $" and {second}")} contest {r}. Its gates and roads are under martial watch."),
            ConditionKind.Skirmish => ($"Skirmishes in {r}",
                $"The vassals of {first ?? "rival lords"}{(second is null ? "" : $" and {second}")} clash along the marches of {r}. Patrols question every traveller."),
            ConditionKind.Unrest => ($"Unrest in {r}", $"Crowds gather and tempers run high in {r}. The watch is out in force at the gates."),
            ConditionKind.Crackdown => ($"Watch Crackdown in {r}", $"The watch of {r} is sweeping the streets and checking papers at every gate. Those of poor standing are questioned or turned away."),
            ConditionKind.CrimeWave => ($"Crime Wave in {r}", $"Thieves and cutpurses work the streets of {r}. Shopkeepers have raised their prices to cover their losses."),
            ConditionKind.WitchHunt => ($"Witch Hunt in {r}", $"Witch-finders question anyone who deals in magic in {r}. Spellcraft costs dear, and is sold quietly."),
            ConditionKind.Persecution => ($"{SeatName(board, condition.Detail) ?? "A Guild"} Persecuted in {r}",
                $"{SeatName(board, condition.Detail) ?? "A guild"} has been driven from the halls of {r}, and its members are refused at the door."),
            ConditionKind.Recruitment => ($"Recruiters in {r}", $"Recruiters cry for swords in the squares of {r}. Hirelings and sellswords come cheap while the drums beat."),
            ConditionKind.Prosperity => ($"Good Times in {r}", $"Trade is brisk and purses are full in {r}. Merchants ask more, and get it."),
            ConditionKind.Festival => ($"Festival in {r}", $"{r} keeps festival. The streets are hung with garlands, and the shops sell cheap to the crowds."),
            _ => null,
        };
    }

    /// <summary>The words of <paramref name="region"/> cut off from food for <paramref name="weeks"/> weeks.</summary>
    internal static (string Headline, string Body) CutOff(Region region, int weeks) =>
        ($"Roads Cut: {region.Name} Goes Hungry",
            $"No grain has reached {region.Name} for {(weeks == 1 ? "a week" : $"{weeks} weeks")}. Stalls stand empty, and prices climb.");

    /// <summary>The words of a court condition on <paramref name="crown"/>, shown in its capital, <paramref name="capital"/>.</summary>
    internal static (string Headline, string Body) Court(Board board, Seat crown, Region capital, CourtCondition condition) => condition.Kind switch
    {
        CourtConditionKind.ParanoidCourt => ($"Suspicion at the Court of {crown.Name}",
            $"The court of {crown.Name} trusts no one. Petitioners are turned away, and the gates of {capital.Name} watch every stranger."),
        CourtConditionKind.HostageTreaty => ($"Hostages Given by {crown.Name}",
            $"{crown.Name} has sent hostages to {SeatName(board, condition.Source) ?? "a rival"} to seal a treaty. The court receives no new petitioners while they are held."),
        _ => ($"Murmurs at the Court of {crown.Name}",
            $"Fees and prices in {capital.Name} have risen, and no one at court will say where the coin goes."),
    };

    private static string? SeatName(Board board, string? seat) => seat is not null && board.HasSeat(seat) ? board.Seat(seat).Name : null;

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
