using WorldRpg.Rulesets.Daggerfall.Guilds;

namespace WorldRpg.Rulesets.Daggerfall.Policies;

/// <summary>The seven documented temple blessing targets, plus Arkay's cure-only distinction.</summary>
internal enum DaggerfallTempleBlessingTarget
{
    None,
    Speed,
    Luck,
    Intelligence,
    Endurance,
    Personality,
    LegalReputation,
    Mercantile,
}

/// <summary>
/// Daggerfall temple policy shared by provider admission and the active-effect owner. The target
/// map is the documented temple table; magnitude and time are the owner-approved WorldRpg policy
/// recorded by #9147 because the donor implementation leaves both unfinished.
/// </summary>
internal static class DaggerfallTemplePolicy
{
    internal const int MinimumBlessingRank = 0;
    internal const int MaximumBlessingRank = DaggerfallSocialState.MaximumGuildRank;
    internal const int MaximumBlessingMinutes = 1440;
    internal const int CurePricePerAffliction = 250;

    /// <summary>FORM-03's rank-plus-two contribution, with no guessed reputation multiplier.</summary>
    internal static int CalculateTempleBlessing(int rank)
    {
        ValidateRank(rank);
        return checked(rank + 2);
    }

    /// <summary>FORM-03 overload used by a paid provider; payment controls time, not magnitude.</summary>
    internal static int CalculateTempleBlessing(ulong donationGold, int rank)
    {
        if (donationGold == 0) throw new ArgumentOutOfRangeException(nameof(donationGold));
        return CalculateTempleBlessing(rank);
    }

    internal static int BlessingDurationMinutes(ulong donationGold)
    {
        if (donationGold == 0) throw new ArgumentOutOfRangeException(nameof(donationGold));
        return checked((int)Math.Min((ulong)MaximumBlessingMinutes, donationGold));
    }

    internal static bool TryResolveTarget(int deityFactionId, out DaggerfallTempleBlessingTarget target) =>
        (target = deityFactionId switch
        {
            DaggerfallConcreteGuildCatalog.AkatoshFactionId => DaggerfallTempleBlessingTarget.Speed,
            DaggerfallConcreteGuildCatalog.ArkayFactionId => DaggerfallTempleBlessingTarget.None,
            DaggerfallConcreteGuildCatalog.DibellaFactionId => DaggerfallTempleBlessingTarget.Luck,
            DaggerfallConcreteGuildCatalog.JulianosFactionId => DaggerfallTempleBlessingTarget.Intelligence,
            DaggerfallConcreteGuildCatalog.KynarethFactionId => DaggerfallTempleBlessingTarget.Endurance,
            DaggerfallConcreteGuildCatalog.MaraFactionId => DaggerfallTempleBlessingTarget.Personality,
            DaggerfallConcreteGuildCatalog.StendarrFactionId => DaggerfallTempleBlessingTarget.LegalReputation,
            DaggerfallConcreteGuildCatalog.ZenitharFactionId => DaggerfallTempleBlessingTarget.Mercantile,
            _ => DaggerfallTempleBlessingTarget.None,
        }) != DaggerfallTempleBlessingTarget.None || deityFactionId == DaggerfallConcreteGuildCatalog.ArkayFactionId;

    internal static int ReducedCureCost(int baseCost, int deityFactionId, int rank, bool member)
    {
        if (baseCost < 0) throw new ArgumentOutOfRangeException(nameof(baseCost));
        ValidateRank(rank);
        if (deityFactionId != DaggerfallConcreteGuildCatalog.ArkayFactionId || !member) return baseCost;
        // Temple.ReducedCureCost uses the donor's fixed-point ten-percent-per-rank reduction.
        return checked((((10 - rank) << 8) / 10 * baseCost) >> 8);
    }

    internal static int MembershipFaction(int deityFactionId) =>
        DaggerfallConcreteGuildCatalog.ForDeity(deityFactionId).FactionId;

    private static void ValidateRank(int rank)
    {
        if (rank is < MinimumBlessingRank or > MaximumBlessingRank)
            throw new ArgumentOutOfRangeException(nameof(rank), rank, "A temple rank is outside the supported 0..9 range.");
    }
}

