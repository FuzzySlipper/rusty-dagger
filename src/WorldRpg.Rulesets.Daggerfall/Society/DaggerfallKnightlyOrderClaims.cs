namespace WorldRpg.Rulesets.Daggerfall.Guilds;

/// <summary>
/// Current-schema durable entitlement state for one knightly order. Armor claims are keyed by the
/// rank at which the donor award was accepted; the house flag is independent because the donor
/// grants one local house only once at rank nine.
/// </summary>
internal sealed record DaggerfallKnightlyOrderClaimSave(
    int FactionId,
    int[] ClaimedArmorRanks,
    bool HouseClaimed)
{
    internal DaggerfallKnightlyOrderClaimSave Validate()
    {
        if (!DaggerfallConcreteGuildCatalog.TryGet(FactionId, out DaggerfallConcreteGuildDefinition? definition)
            || definition.Kind != DaggerfallConcreteGuildKind.KnightlyOrder)
            throw new ArgumentException($"Faction {FactionId} is not a retained knightly order.", nameof(FactionId));
        ArgumentNullException.ThrowIfNull(ClaimedArmorRanks);
        if (ClaimedArmorRanks.Any(rank => rank is < 0 or > DaggerfallSocialState.MaximumGuildRank)
            || ClaimedArmorRanks.Distinct().Count() != ClaimedArmorRanks.Length)
            throw new ArgumentException("Knightly armor claims must contain each Daggerfall rank at most once.", nameof(ClaimedArmorRanks));
        return this;
    }

    internal DaggerfallKnightlyOrderClaims ToProjection()
    {
        Validate();
        return new(new HashSet<int>(ClaimedArmorRanks), HouseClaimed);
    }

    internal static DaggerfallKnightlyOrderClaimSave FromProjection(
        int factionId,
        DaggerfallKnightlyOrderClaims claims)
    {
        ArgumentNullException.ThrowIfNull(claims);
        return new DaggerfallKnightlyOrderClaimSave(
            factionId, claims.ClaimedArmorRanks.Order().ToArray(), claims.HouseClaimed).Validate();
    }
}

/// <summary>Current-schema collection of the retained knightly-order claim records.</summary>
internal sealed record DaggerfallKnightlyOrderClaimStateSave(DaggerfallKnightlyOrderClaimSave[] Claims)
{
    internal static DaggerfallKnightlyOrderClaimStateSave Empty { get; } = new([]);

    internal DaggerfallKnightlyOrderClaimStateSave Validate()
    {
        ArgumentNullException.ThrowIfNull(Claims);
        HashSet<int> factions = [];
        foreach (DaggerfallKnightlyOrderClaimSave claim in Claims)
        {
            ArgumentNullException.ThrowIfNull(claim);
            claim.Validate();
            if (!factions.Add(claim.FactionId))
                throw new ArgumentException($"Knightly claim state repeats faction {claim.FactionId}.", nameof(Claims));
        }
        return this;
    }
}

/// <summary>
/// Mutable current state for knightly-order entitlements. It is deliberately separate from the
/// policy projection: only a successful concrete armor grant or house allocation may call a commit
/// method, while save capture reads the same state at the explicit product save boundary.
/// </summary>
internal sealed class DaggerfallKnightlyOrderClaimState
{
    private readonly Dictionary<int, HashSet<int>> _armorClaims = [];
    private readonly HashSet<int> _houseClaims = [];

    internal DaggerfallKnightlyOrderClaimState(DaggerfallKnightlyOrderClaimStateSave? restored = null)
    {
        if (restored is null) return;
        restored.Validate();
        foreach (DaggerfallKnightlyOrderClaimSave claim in restored.Claims)
        {
            _armorClaims.Add(claim.FactionId, new HashSet<int>(claim.ClaimedArmorRanks));
            if (claim.HouseClaimed) _houseClaims.Add(claim.FactionId);
        }
    }

    internal DaggerfallKnightlyOrderClaims Read(int factionId)
    {
        RequireOrder(factionId);
        return new(
            _armorClaims.TryGetValue(factionId, out HashSet<int>? armor) ? armor : new HashSet<int>(),
            _houseClaims.Contains(factionId));
    }

    /// <summary>Commits the rank claim after the item owner confirms an actual grant.</summary>
    internal bool TryCommitArmor(int factionId, int rank)
    {
        RequireOrder(factionId);
        ValidateRank(rank);
        HashSet<int> claims = _armorClaims.TryGetValue(factionId, out HashSet<int>? existing)
            ? existing
            : (_armorClaims[factionId] = []);
        return claims.Add(rank);
    }

    /// <summary>Commits the one house claim after the property owner confirms actual allocation.</summary>
    internal bool TryCommitHouse(int factionId)
    {
        RequireOrder(factionId);
        return _houseClaims.Add(factionId);
    }

    internal DaggerfallKnightlyOrderClaimStateSave Capture()
    {
        DaggerfallKnightlyOrderClaimSave[] claims = _armorClaims.Keys
            .Concat(_houseClaims)
            .Distinct()
            .Order()
            .Select(factionId => DaggerfallKnightlyOrderClaimSave.FromProjection(factionId, Read(factionId)))
            .Where(claim => claim.ClaimedArmorRanks.Length != 0 || claim.HouseClaimed)
            .ToArray();
        return new DaggerfallKnightlyOrderClaimStateSave(claims).Validate();
    }

    private static void ValidateRank(int rank)
    {
        if (rank is < 0 or > DaggerfallSocialState.MaximumGuildRank)
            throw new ArgumentOutOfRangeException(nameof(rank), rank, "A knightly claim names no Daggerfall rank.");
    }

    private static DaggerfallConcreteGuildDefinition RequireOrder(int factionId)
    {
        DaggerfallConcreteGuildDefinition definition = DaggerfallConcreteGuildCatalog.ForFaction(factionId);
        if (definition.Kind != DaggerfallConcreteGuildKind.KnightlyOrder)
            throw new ArgumentException($"Faction {factionId} is not a retained knightly order.", nameof(factionId));
        return definition;
    }
}
