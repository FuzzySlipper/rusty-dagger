namespace WorldRpg.Rulesets.Daggerfall.Crime;

/// <summary>The crime labels retained by the classic player crime enum.</summary>
internal enum DaggerfallCrimeKind
{
    AttemptedBreakingAndEntering = 1,
    Trespassing = 2,
    BreakingAndEntering = 3,
    Assault = 4,
    Murder = 5,
    HorseTheft = 6,
    Robbery = 7,
    Arson = 8,
    Pilfering = 9,
    Impersonating = 10,
    HighTreason = 11,
    Pickpocketing = 12,
    Theft = 13,
    Treason = 14,
    LoanDefault = 15,
}

internal enum DaggerfallCrimeAction
{
    Pickpocket,
    Shoplifting,
    Theft,
    Assault,
    Entry,
}

internal enum DaggerfallCrimeAttemptOutcome
{
    Failed,
    SucceededWithoutTakingProperty,
    PropertyTransferred,
    DamageAccepted,
    EntryAccepted,
}

internal enum DaggerfallCrimeStage
{
    Attempted,
    Completed,
}

internal enum DaggerfallCrimeTargetKind
{
    Unknown,
    Civilian,
    Guard,
    Other,
}

internal enum DaggerfallCrimeWitnessQuery
{
    NotQueried,
    CompletedWithoutWitnesses,
    CompletedWithWitnesses,
}

internal enum DaggerfallCrimeGuildRequirement
{
    Thieving,
    Murder,
}

internal enum DaggerfallCrimeGuildCredit
{
    None,
    Thieving,
    CivilianMurder,
    GuardMurder,
}

/// <summary>
/// Exact source formula inputs. The caller supplies the live skill and levels already admitted by
/// the Daggerfall character/target owners; this policy owns only the classic arithmetic.
/// </summary>
internal static class DaggerfallCrimePolicy
{
    private static readonly IReadOnlyDictionary<DaggerfallCrimeKind, int> ReputationLosses =
        new Dictionary<DaggerfallCrimeKind, int>
        {
            [DaggerfallCrimeKind.AttemptedBreakingAndEntering] = 10,
            [DaggerfallCrimeKind.Trespassing] = 5,
            [DaggerfallCrimeKind.BreakingAndEntering] = 10,
            [DaggerfallCrimeKind.Assault] = 8,
            [DaggerfallCrimeKind.Murder] = 20,
            [DaggerfallCrimeKind.HorseTheft] = 10,
            [DaggerfallCrimeKind.Robbery] = 2,
            [DaggerfallCrimeKind.Arson] = 1,
            [DaggerfallCrimeKind.Pilfering] = 2,
            [DaggerfallCrimeKind.Impersonating] = 2,
            [DaggerfallCrimeKind.HighTreason] = 75,
            [DaggerfallCrimeKind.Pickpocketing] = 2,
            [DaggerfallCrimeKind.Theft] = 8,
            [DaggerfallCrimeKind.Treason] = 36,
            [DaggerfallCrimeKind.LoanDefault] = 10,
        };

    /// <summary>Classic player-vs-townsperson pickpocket chance.</summary>
    internal static int CalculatePickpocketingChance(int livePickpocketSkill, int playerLevel) =>
        CalculatePickpocketingChance(livePickpocketSkill, playerLevel, targetLevel: null);

    /// <summary>
    /// Classic pickpocket chance. Enemy targets add five points per player level above the target;
    /// townsperson targets have no level adjustment. The donor clamps the result to 5 through 95.
    /// </summary>
    internal static int CalculatePickpocketingChance(int livePickpocketSkill, int playerLevel, int? targetLevel)
    {
        long chance = livePickpocketSkill;
        if (targetLevel is int level)
            chance += 5L * ((long)playerLevel - level);
        return ClampChance(chance);
    }

    /// <summary>Classic shoplifting detection chance for the donor's combined integer basket weight/count.</summary>
    internal static int CalculateShopliftingChance(int livePickpocketSkill, int shopQuality, int weightAndNumItems) =>
        ClampChance(100L - livePickpocketSkill + shopQuality + weightAndNumItems);

    /// <summary>
    /// Basket overload preserving DFUnity's caller conversion: cast basket weight to int first
    /// (truncation toward zero), then add the item count.
    /// </summary>
    internal static int CalculateShopliftingChance(
        int livePickpocketSkill,
        int shopQuality,
        double basketWeight,
        int basketItemCount)
    {
        if (!double.IsFinite(basketWeight) || basketWeight < 0d)
            throw new ArgumentOutOfRangeException(nameof(basketWeight));
        ArgumentOutOfRangeException.ThrowIfNegative(basketItemCount);
        if (basketWeight > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(basketWeight));
        int combined = checked((int)basketWeight + basketItemCount);
        return CalculateShopliftingChance(livePickpocketSkill, shopQuality, combined);
    }

    /// <summary>FALL.EXE/DFUnity regional legal reputation loss for the implemented crime labels.</summary>
    internal static int RegionalReputationLoss(DaggerfallCrimeKind crime) =>
        ReputationLosses.TryGetValue(crime, out int loss)
            ? loss
            : throw new ArgumentOutOfRangeException(nameof(crime), crime, "The donor has no legal-reputation loss for this crime label.");

    /// <summary>The donor also reduces the region's people-faction standing by half, using integer truncation.</summary>
    internal static int PeopleFactionReputationLoss(DaggerfallCrimeKind crime) => RegionalReputationLoss(crime) / 2;

    internal static int GuildCreditPoints(DaggerfallCrimeGuildCredit credit) => credit switch
    {
        DaggerfallCrimeGuildCredit.None => 0,
        DaggerfallCrimeGuildCredit.Thieving => 1,
        DaggerfallCrimeGuildCredit.CivilianMurder => 5,
        DaggerfallCrimeGuildCredit.GuardMurder => 1,
        _ => throw new ArgumentOutOfRangeException(nameof(credit)),
    };

    private static int ClampChance(long chance) => (int)Math.Clamp(chance, 5L, 95L);
}
