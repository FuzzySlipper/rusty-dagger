namespace WorldRpg.Politics;

/// <summary>What is happening in a region. Each kind's weekly weight on the region is <see cref="Conditions.Weigh"/>.</summary>
public enum ConditionKind
{
    /// <summary>Prices of a kind of goods (<see cref="RegionCondition.Detail"/>) are up: the holder's coin income rises.</summary>
    PriceSurge,

    /// <summary>Food has run short: the region's prosperity pays no coin.</summary>
    Famine,

    /// <summary>Plague: order falls.</summary>
    Plague,

    /// <summary>A creature family (<see cref="RegionCondition.Detail"/>) is abroad: control falls.</summary>
    MonsterSurge,

    /// <summary>Crime is rising: order falls.</summary>
    CrimeWave,

    /// <summary>The watch is cracking down: order rises and prosperity falls.</summary>
    Crackdown,

    /// <summary>The people are restless: control falls.</summary>
    Unrest,

    /// <summary>A front between the seats in <see cref="RegionCondition.Detail"/>: prosperity and order fall.</summary>
    WarFront,

    /// <summary>The vassals of the seats in <see cref="RegionCondition.Detail"/> are skirmishing: order falls.</summary>
    Skirmish,

    /// <summary>Raiders on the roads: prosperity falls.</summary>
    Raiders,

    /// <summary>Recruiters are busy: the holder gains force.</summary>
    Recruitment,

    /// <summary>Good times: prosperity rises.</summary>
    Prosperity,

    /// <summary>A festival: order rises.</summary>
    Festival,

    /// <summary>The seat named in <see cref="RegionCondition.Detail"/>, such as a temple, is persecuted: its influence there falls.</summary>
    Persecution,

    /// <summary>Witches are hunted: order rises and prosperity falls.</summary>
    WitchHunt,
}

/// <summary>What troubles a crown's court after a plot against it. Its weight is <see cref="Conditions.Weigh"/>.</summary>
public enum CourtConditionKind
{
    /// <summary>The court trusts no one: the crown's favour income falls.</summary>
    ParanoidCourt,

    /// <summary>The crown has given hostages to the source seat: its relation with that seat cannot fall below 0.</summary>
    HostageTreaty,

    /// <summary>A corrupt advisor skims the treasury for the source seat: coin passes from the crown to it.</summary>
    CorruptAdvisor,
}

/// <summary>
/// A condition token on a region: its kind and the goods, creature family, temple or seats it concerns, its magnitude
/// (1–3), the seat that laid it or none for the world, and the turns it starts and ends on.
/// </summary>
public sealed record RegionCondition(ConditionKind Kind, string? Detail, int Magnitude, string? Source, int Start, int End);

/// <summary>A condition on a crown's court, laid by a seat's plot.</summary>
public sealed record CourtCondition(CourtConditionKind Kind, int Magnitude, string Source, int Start, int End);

/// <summary>What conditions do: the weekly weight each puts on its region or court during upkeep.</summary>
public static class Conditions
{
    public const int MaxMagnitude = 3;

    /// <summary>
    /// How often a condition's slow weight falls due, in weeks: tracks move by 1 on the weeks this divides, and a
    /// stronger condition moves them on more of them (magnitude 3 every week, 2 every second week, 1 every fourth).
    /// </summary>
    public static bool Due(int magnitude, int turn) => turn % (magnitude switch { >= 3 => 1, 2 => 2, _ => 4 }) == 0;

    /// <summary>
    /// The coin <paramref name="region"/>'s holder is paid from its prosperity this week: the region's prosperity, unless
    /// a famine has emptied the granaries, raised by any price surge's magnitude.
    /// </summary>
    public static int ProsperityIncome(Region region) =>
        (region.Conditions.Any(condition => condition.Kind == ConditionKind.Famine) ? 0 : region.Prosperity)
        + region.Conditions.Where(condition => condition.Kind == ConditionKind.PriceSurge).Sum(condition => condition.Magnitude);

    /// <summary>Applies each condition on <paramref name="region"/> that falls due this turn.</summary>
    public static void Weigh(Board board, Region region)
    {
        foreach (RegionCondition condition in region.Conditions.ToArray())
        {
            if (!Due(condition.Magnitude, board.Turn)) continue;
            switch (condition.Kind)
            {
                case ConditionKind.Plague or ConditionKind.CrimeWave or ConditionKind.Skirmish: board.AdjustOrder(region, -1); break;
                case ConditionKind.MonsterSurge or ConditionKind.Unrest: board.AdjustControl(region, -1); break;
                case ConditionKind.Crackdown or ConditionKind.WitchHunt:
                    board.AdjustOrder(region, 1);
                    board.AdjustProsperity(region, -1);
                    break;
                case ConditionKind.WarFront:
                    board.AdjustProsperity(region, -1);
                    board.AdjustOrder(region, -1);
                    break;
                case ConditionKind.Raiders: board.AdjustProsperity(region, -1); break;
                case ConditionKind.Recruitment:
                    if (region.Allegiance is not null) board.AdjustResource(board.Seat(region.Allegiance), Resource.Force, 1);
                    break;
                case ConditionKind.Prosperity: board.AdjustProsperity(region, 1); break;
                case ConditionKind.Festival: board.AdjustOrder(region, 1); break;
                case ConditionKind.Persecution: board.AdjustInfluence(region, board.Seat(condition.Detail!), -1); break;
            }
        }
    }

    /// <summary>Applies each condition on <paramref name="crown"/>'s court that falls due this turn.</summary>
    public static void Weigh(Board board, Seat crown)
    {
        foreach (CourtCondition condition in crown.Court.ToArray())
        {
            if (!Due(condition.Magnitude, board.Turn)) continue;
            switch (condition.Kind)
            {
                case CourtConditionKind.ParanoidCourt: board.AdjustResource(crown, Resource.Favour, -1); break;
                case CourtConditionKind.CorruptAdvisor:
                    if (crown.Coin == 0) break;
                    board.AdjustResource(crown, Resource.Coin, -1);
                    board.AdjustResource(board.Seat(condition.Source), Resource.Coin, 1);
                    break;
            }
        }
    }
}
